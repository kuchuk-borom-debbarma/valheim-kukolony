using UnityEngine;

namespace Kukolony.Villagers.Navigation
{
    /// <summary>
    ///     Walking towards something, with the patience and the memory that a bare move call
    ///     cannot have.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <see cref="VillagerMovement" /> is the stateless wrapper around the game's move
    ///         call, and it is right about a single tick. It cannot be right about a journey,
    ///         because two of the things a journey needs are memory: whether a path failure is
    ///         real, and whether the villager is actually getting anywhere.
    ///     </para>
    ///     <para>
    ///         <b>The grace exists because the game's own pathfinder is throttled.</b>
    ///         <c>BaseAI.FindPath</c> answers from a cache for a second at a time, so the first
    ///         tick after taking any new target reports failure whether or not a path exists.
    ///         The previous job system had a three second grace, lost it in a rewrite, and
    ///         villagers went back to abandoning work the instant they set off - a bug that read
    ///         as claim contention and took a measured run to pin down.
    ///     </para>
    ///     <para>
    ///         Progress is tracked here rather than in the job because it is the same question
    ///         for every job, and because "stuck" is not a moment but a history.
    ///     </para>
    /// </remarks>
    internal sealed class VillagerWalk
    {
        /// <summary>How long a path failure is forgiven after taking a new target.</summary>
        private const float GraceSeconds = 3f;

        /// <summary>
        ///     How much closer counts as having got somewhere.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Metres, not centimetres, and that distinction is the whole of it. This was a
        ///         quarter of a metre, which a villager grinding against a rock at a tenth of a
        ///         metre per second clears every couple of seconds - so it never looked stalled,
        ///         never got rescued, and inched one metre in five minutes while every rescue in
        ///         the ladder stood by waiting for it to stop making progress.
        ///     </para>
        ///     <para>
        ///         Three metres against the fifteen seconds of patience below is a floor of about
        ///         a fifth of a villager's walking speed. Anything slower than that is not walking
        ///         badly, it is failing to walk.
        ///     </para>
        /// </remarks>
        private const float ProgressStep = 3f;

        /// <summary>
        ///     How long without getting any closer before a walk is called off.
        /// </summary>
        /// <remarks>
        ///     Generous, because the thing it must not mistake for being stuck is a villager
        ///     waiting at the edge of the built world for the next navmesh tiles - measured at up
        ///     to fifteen seconds for ground nobody had asked about before.
        /// </remarks>
        private const float StallSeconds = 20f;

        /// <summary>
        ///     The same, for a walk that stays inside the settlement.
        /// </summary>
        /// <remarks>
        ///     Patience is only a virtue where the navmesh might still be building. Within a
        ///     settlement the ground has been walked already, so a villager that is not getting
        ///     closer is genuinely obstructed and the useful thing is to give up quickly and
        ///     pick different work. Applying the long patience everywhere made a villager spend
        ///     twenty seconds failing to reach a chest six metres away, which is most of the
        ///     time a hauling trip is given.
        /// </remarks>
        private const float NearbyStallSeconds = 4f;

        /// <summary>
        ///     How long a journey must be getting nowhere before the ground is given up on.
        /// </summary>
        /// <remarks>
        ///     Long enough that the navmesh has had its chance - tiles are built one per cycle
        ///     and a path across new ground measured up to fifteen seconds to appear - and short
        ///     enough that a villager does not spend a minute of a player's evening standing in
        ///     a bush. Progress resets it, so a journey that is merely slow is never rescued.
        /// </remarks>
        /// <remarks>
        ///     <para>
        ///         Forty-five seconds, and the number is a measurement rather than a preference.
        ///         The first thing a villager does on a new route is wait for the navmesh to be
        ///         built for ground nobody has walked, and asking the pathfinder while it waits
        ///         says exactly how long that takes:
        ///     </para>
        ///     <code>
        ///     stalled  5s: waypoints=0   fullPath=False  -> PathFailed
        ///     stalled 15s: waypoints=0   fullPath=False  -> PathFailed
        ///     stalled 20s: waypoints=1   fullPath=False  -> Moving
        ///     stalled 25s: waypoints=20  fullPath=True   -> Moving
        ///     </code>
        ///     <para>
        ///         Twenty-five seconds for a forty-four metre stretch, which is what one tile per
        ///         cycle with a five second minimum age comes to. Nothing was wrong; the villager
        ///         was waiting for the world. Rescuing at fifteen or thirty seconds meant every
        ///         journey gave up on walking a moment before walking became possible, and the
        ///         villager covered the whole distance without touching the ground.
        ///     </para>
        /// </remarks>
        private const float RescueAfterSeconds = 45f;

        /// <summary>
        ///     How far the next stretch must move before it is worth re-snapping to the navmesh.
        /// </summary>
        /// <remarks>
        ///     The waypoint slides forward as the villager walks, so without a threshold this
        ///     would ask the navmesh where to stand on every tick - and that question is a real
        ///     query, not a field read.
        /// </remarks>
        private const float LegChange = 5f;

        /// <summary>How far a destination must move to count as a different errand.</summary>
        /// <remarks>
        ///     Generous, because a job nudges its target about as it re-reads the world and none
        ///     of that is a new journey. Only being sent somewhere genuinely else should forgive
        ///     a villager the trouble it had getting here.
        /// </remarks>
        private const float NewErrandDistance = 20f;

        /// <summary>How many polite rescues to try before resorting to one a player might see.</summary>
        private const int RescuesBeforeGliding = 2;

        /// <summary>
        ///     How long one rescue lasts before walking is tried again.
        /// </summary>


        private readonly MonsterAI _ai;
        private readonly Journey _journey;

        private Vector3 _target;
        private Vector3 _standing;
        private bool _hasTarget;
        private float _graceUntil;
        private float _closest;
        private float _nearest;
        private float _lastProgress;

        /// <summary>
        ///     The trip's own clock, which belongs to the job rather than to the ladder.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Separate because three things were reading one field and wanting different
        ///         answers from it. The rescue ladder asks "has walking stopped working" and
        ///         wants its clock cleared every time it tries something; the locomotor asks
        ///         "is this a journey"; the job asks "should this trip be given up on". Three
        ///         rounds of review went on that field, each fixing one reader and breaking
        ///         another - and the last of them left the abandon bound measuring time since
        ///         the last rescue rung rather than time the trip had been failing, then
        ///         handing that inherited total to the next target and condemning it on sight.
        ///     </para>
        ///     <para>
        ///         So the trip keeps its own. It starts when a job takes a target, it is reset
        ///         by nothing else - not by a rescue rung, not by forgetting a route - and any
        ///         way of getting closer counts, gliding included, because for this question
        ///         covering ground unseen is exactly as good as walking.
        ///     </para>
        /// </remarks>
        private float _tripProgress;

        private float _tripClosest;
        private bool _onTrip;
        private bool _reckoning;
        private float _reckonUntil;
        private int _rescues;
        private int _bursts;
        private Vector3 _errand = new Vector3(float.MaxValue, 0f, float.MaxValue);
        private Vector3 _leg = new Vector3(float.MaxValue, 0f, float.MaxValue);
        private Vector3 _legStanding;
        private float _nextComplaint;

        internal VillagerWalk(MonsterAI ai)
        {
            _ai = ai;
            _journey = new Journey(ai);
        }

        /// <summary>Where this villager currently intends to be, for the keep-alive set.</summary>
        internal Vector3 Waypoint => _journey.Waypoint;

        internal bool Travelling => _journey.Travelling;

        /// <summary>Whether it is covering ground rather than walking it.</summary>
        internal bool Reckoning => _reckoning;

        /// <summary>How long the villager has been trying without getting closer.</summary>
        internal float StalledFor => _hasTarget ? Mathf.Max(0f, Time.time - _lastProgress) : 0f;

        /// <summary>
        ///     How long this trip has gone without getting any closer, by any means.
        /// </summary>
        /// <remarks>
        ///     What a job should read before giving up. <see cref="StalledFor" /> is the
        ///     ladder's and is cleared every time the ladder tries something, so a job reading
        ///     it is told about the last rung rather than about the trip.
        /// </remarks>
        internal float TripStalledFor => _onTrip ? Mathf.Max(0f, Time.time - _tripProgress) : 0f;

        /// <summary>
        ///     Starts the trip clock. Every job must call this when it takes a new target.
        /// </summary>
        /// <remarks>
        ///     Told rather than inferred. A target change was being guessed at from distance
        ///     comparisons inside the move, which the rescue branches return before ever
        ///     reaching - so a villager that gave up on one thing carried its whole stalled
        ///     total onto the next and gave up on that immediately, and on the one after, at
        ///     tick rate. The job knows when it chose something; nothing else reliably does.
        /// </remarks>
        internal void BeginTrip()
        {
            _onTrip = true;
            _tripProgress = Time.time;
            _tripClosest = float.MaxValue;
        }

        /// <summary>Ends the trip clock, so an idle villager is not accruing a stall.</summary>
        internal void EndTrip() => _onTrip = false;

        /// <summary>The closest it has managed to get to the current target.</summary>
        internal float Closest => _nearest;

        /// <param name="deltaTime">
        ///     The AI tick's own step. Defaulted to the frame time for callers that do not have
        ///     it, but passing the real one matters: the AI is driven at a fixed 0.05s while a
        ///     frame is nearer 0.02, so a villager covering ground unseen moved at forty per
        ///     cent of its own walking speed - correct-looking, steady, and wrong.
        /// </param>
        internal MoveResult MoveTowards(Vector3 target, float stopDistance, bool run = false,
            float deltaTime = 0f)
        {
            // A new errand starts the rescue ladder from the bottom. Without this a villager
            // that needed help getting somewhere arrives with its polite rescues already spent,
            // and covers the whole way back by gliding - which is how one crossed the last
            // ninety metres home in plain sight without touching the ground.
            if (Utils.DistanceXZ(_errand, target) > NewErrandDistance)
            {
                _errand = target;
                _rescues = 0;
                _bursts = 0;

                // And end any rescue in progress. Resetting only the counters left a villager
                // that arrived mid-rescue still covering ground, so it began its next errand
                // gliding and never touched the ground again - a hundred and fifty metres home
                // in twenty-one seconds. Every errand starts on foot.
                // The journey itself starts again too. Travelling latches on and is only
                // cleared by arriving, so without this a villager that once had a far target
                // kept walking towards a leg forty-five metres ahead long after its target
                // became a chest ten metres away - which looks exactly like a villager
                // wandering off in a random direction, because it is one.
                _journey.Forget();

                if (_reckoning)
                {
                    _reckoning = false;
                    _journey.Resume(_ai.m_character);
                    Forget();
                }
            }

            _journey.Prepare(target, stopDistance);

            // A rescue already under way. Bursts are bounded so that walking is always tried
            // again: the first version simply reckoned while it was stalled, and since nothing
            // updated the stall clock during reckoning it never stopped being stalled - so a
            // villager that escaped one bad patch glided the rest of the way home and reported
            // that it had never come back into view.
            // The decision itself is a pure function with every combination checked, because
            // getting it wrong is not obvious from reading it: four different versions of this
            // looked right while leaving a villager walking into a rock, gliding home in plain
            // sight, or doing neither for five minutes.
            // Being stuck qualifies a villager for help wherever it is standing. The rescue
            // ladder used to require a journey longer than the settlement is wide, which meant a
            // villager stuck twenty-six metres from a log it could see got nothing at all: no
            // rescue, and after the patience above ran out, a failure every few seconds until it
            // had tired itself out and gone to bed without delivering anything. Distance decides
            // whether the ground ahead needs holding open, not whether somebody deserves helping.
            // ...but you cannot be stuck where you were going. Without the second half, a
            // villager that had arrived stopped making progress, was therefore "stuck", and kept
            // itself eligible for a rescue that never ended - so it finished its journey sliding
            // rather than standing, which is the one thing covering ground unseen must not do.
            bool stuck = StalledFor > RescueAfterSeconds &&
                         Utils.DistanceXZ(target, _ai.transform.position) > stopDistance;
            bool onJourney = _journey.Travelling || stuck;

            // One search answers both: whether there is anywhere to stand at all, and
            // whether it is close enough that being put there reads as stepping ashore.
            bool nearLand = false;
            bool canStand = !onJourney || _journey.CanStand(_ai.m_character, out nearLand);

            TravelFacts facts = new TravelFacts(
                rescuing: _reckoning,
                travelling: onJourney,
                burstSpent: Time.time >= _reckonUntil,
                observed: _journey.Observed(_ai.transform.position),
                stalled: stuck,
                politeRescuesLeft: _rescues < RescuesBeforeGliding,
                canStand: canStand,
                waterAhead: _journey.WaterAhead(_ai.transform.position),
                nearLand: nearLand);

            switch (Locomotor.Decide(facts))
            {
                case Locomotion.CoverGround:
                    if (facts.BurstSpent) _reckonUntil = Time.time + Rescue.BurstSeconds(_bursts);

                    VillagerMovement.Stop(_ai);

                    // Covering ground counts as progress on a journey, and only on a journey.
                    //
                    // On a real journey the glide is how the villager travels - a water
                    // crossing covers ground continuously by design - so a clock that called
                    // that "getting nowhere" would abandon a crossing that was working.
                    //
                    // On a settlement-scale errand the glide is the rescue, and noting it as
                    // progress is a trap: `stuck` is recomputed from this clock every tick and
                    // is the only thing making `travelling` true for a short errand, so
                    // clearing it three metres into a glide tells Decide the journey is over,
                    // which lands on BackOnFoot, which forgets the route and resets the clock
                    // again. The villager creeps three metres per forty-five seconds and the
                    // abandon bound below it can never be reached - the exact hang this whole
                    // change exists to end, rebuilt out of its own fix.
                    //
                    // The ladders are deliberately not reset either way. Gliding is the
                    // rescue, and a rescue that cleared its own counters could rescue for ever.
                    float glided = Utils.DistanceXZ(target, _ai.transform.position);

                    // The trip's clock counts this however short the errand. Covering ground
                    // is getting closer, and the job's question is only ever whether the trip
                    // is getting anywhere.
                    NoteTripProgress(glided);

                    // The ladder's clock counts it only on a real journey. On a settlement
                    // errand the glide *is* the rescue, and this clock is the only thing
                    // telling the locomotor a rescue is under way - clearing it three metres
                    // in says the journey is over, which lands on back-on-foot, which forgets
                    // the route and starts the whole thing again three metres further on.
                    if (_journey.Travelling) NoteProgress(glided, climbDown: false);

                    return _journey.Advance(_ai.m_character, target,
                        deltaTime > 0f ? deltaTime : Time.deltaTime)
                        ? MoveResult.Arrived
                        : MoveResult.Moving;

                case Locomotion.BackOnFoot:
                    _reckoning = false;
                    _journey.Resume(_ai.m_character);
                    Forget();
                    return MoveResult.Moving;

                case Locomotion.PutBackOnNavmesh:
                    _rescues++;
                    _journey.Resume(_ai.m_character);
                    Forget();
                    return MoveResult.Moving;

                case Locomotion.BeginRescue:
                    BeginReckoning();
                    return MoveResult.Moving;
            }

            Retarget(target);

            float distance = Utils.DistanceXZ(target, _ai.transform.position);
            NoteTripProgress(distance);

            // Tracked separately from progress: the nearest it has been is useful for reporting
            // and costs nothing, while what resets the clock has to be a real advance.
            if (distance < _nearest) _nearest = distance;

            if (distance < _closest - ProgressStep)
            {
                // Real progress, not the first reading of a new journey. Forgetting a route sets
                // the closest-yet to infinity, so the tick after it every distance looks like an
                // improvement - and the rescue that did the forgetting immediately cleared its
                // own counter, leaving the ladder unable to climb past its second rung.
                bool measured = _closest < float.MaxValue;

                NoteProgress(distance, climbDown: measured);
            }

            // Walk towards the next stretch of the route, not the far end of it.
            //
            // GetPath snaps BOTH ends of a query onto the navmesh and fails outright if either
            // will not snap. A destination a hundred and sixty metres away in terrain nobody has
            // loaded has no navmesh to snap to, so asking for a path to it returns nothing at
            // all - not a partial path, nothing. That is why walking appeared to fail "near the
            // colony": the ground under the villager was fine, and the question was unanswerable
            // because of where it ended. The same villager walks to a chest six metres away
            // without complaint.
            //
            // The waypoint is forty-five metres along the bearing and inside the halo this
            // journey holds open, so it snaps, and the path that comes back is a real one.
            Vector3 leg = _journey.Travelling ? _journey.Waypoint : target;
            if (Utils.DistanceXZ(_leg, leg) > LegChange)
            {
                _leg = leg;
                _legStanding = Approach.Standing(_ai, leg);
            }

            // Villagers jog when crossing country and walk when working. Measured: walking a
            // hundred and sixty metres takes most of five minutes once the route bends round a
            // hill, because a villager closes about a metre of straight-line distance for every
            // three it actually walks. A settlement whose workers amble between outposts reads
            // as broken even when it is not.
            MoveResult stepped = VillagerMovement.MoveTowards(_ai, _legStanding,
                VillagerMovement.MinimumStopDistance, run || _journey.Travelling);

            // Says what the pathfinder thinks of the stretch it was asked to walk, while it is
            // still failing to walk it. A journey that never starts looks identical to one that
            // is merely slow, and the difference is in here.
            if (_journey.Travelling && StalledFor > 5f && Time.time > _nextComplaint)
            {
                _nextComplaint = Time.time + 5f;
                Core.Log.Info($"[leg] {Utils.DistanceXZ(_ai.transform.position, _legStanding):0}m to the " +
                              $"next stretch, stalled {StalledFor:0}s, stepped {stepped}, " +
                              VillagerMovement.Explain(_ai, _legStanding));
            }

            // Inside the grace, a stop means the throttled pathfinder has not considered this
            // target yet. There is nothing to conclude from it either way.
            if (stepped != MoveResult.Moving && Time.time < _graceUntil) return MoveResult.Moving;

            float patience = onJourney ? StallSeconds : NearbyStallSeconds;
            switch (Arrival.Judge(stepped != MoveResult.Moving, distance, stopDistance,
                        StalledFor, patience))
            {
                case Approaching.Arrived:
                    return MoveResult.Arrived;

                case Approaching.GaveUp:
                    // Before giving up, ask whether there is a route at all. No route usually
                    // means the navmesh has not been built for this ground yet, which takes about
                    // twenty-five seconds and is not the villager's fault; a route that exists
                    // while it fails to follow one is genuinely stuck and should fail quickly so
                    // the job can pick something else.
                    //
                    // Distance used to decide this, and short errands got four seconds - which is
                    // nowhere near long enough for new ground. A villager hauling thirty metres to
                    // an outpost failed forty times in a minute, tired itself out, and went to bed
                    // without delivering anything. Every one of those failures was the world still
                    // loading.
                    if (StalledFor < RescueAfterSeconds &&
                        !VillagerMovement.HasCompletePath(_ai, _legStanding))
                    {
                        return MoveResult.Moving;
                    }

                    return MoveResult.PathFailed;

                default:
                    return MoveResult.Moving;
            }
        }

        /// <summary>
        ///     Records that the villager got closer, and optionally that walking is working.
        /// </summary>
        /// <remarks>
        ///     <paramref name="climbDown" /> is what puts the rescue ladders back at the
        ///     bottom, and belongs only to walking: a glide that reset them would be a rescue
        ///     granting itself another rescue, for ever.
        /// </remarks>
        private void NoteProgress(float distance, bool climbDown)
        {
            if (distance >= _closest - ProgressStep) return;

            _closest = distance;
            _lastProgress = Time.time;

            if (!climbDown) return;

            // Walking is working again, so the ground it was struggling with is behind it.
            // Both ladders start from the bottom next time.
            _rescues = 0;
            _bursts = 0;
        }

        /// <summary>Records that the trip got closer, whatever moved the villager.</summary>
        private void NoteTripProgress(float distance)
        {
            if (!_onTrip || distance >= _tripClosest - ProgressStep) return;

            _tripClosest = distance;
            _tripProgress = Time.time;
        }

        private void BeginReckoning()
        {
            _reckoning = true;
            _bursts++;
            _reckonUntil = Time.time + Rescue.BurstSeconds(_bursts);
        }

        internal void Stop()
        {
            VillagerMovement.Stop(_ai);
            Forget();
        }

        /// <summary>Drops the route as well, for a villager whose errand has been cancelled.</summary>
        internal void Abandon()
        {
            _journey.Forget();
            Forget();
        }

        /// <summary>Drops what was learned about the current journey.</summary>
        internal void Forget()
        {
            _hasTarget = false;
            _closest = float.MaxValue;
            _nearest = float.MaxValue;
        }

        /// <summary>The point being walked to, which is not always the thing being walked at.</summary>
        internal Vector3 StandingAt => _standing;

        /// <summary>
        ///     Starts the clock again when the destination genuinely changes.
        /// </summary>
        /// <remarks>
        ///     Compared with a tolerance rather than exactly: a target that is itself moving - a
        ///     cart, a dropped item settling - would otherwise reset the grace every tick and
        ///     the villager could never be found stuck.
        /// </remarks>
        private void Retarget(Vector3 target)
        {
            if (_hasTarget && Utils.DistanceXZ(_target, target) < 1f) return;

            _target = target;
            _hasTarget = true;
            _nearest = float.MaxValue;

            // Resolved once per journey, not per tick. HavePath is a real query against the
            // navmesh, and a villager has no reason to ask it twenty times a second about a
            // destination that has not moved.
            _standing = Approach.Standing(_ai, target);
            _graceUntil = Time.time + GraceSeconds;
            _closest = float.MaxValue;
            _lastProgress = Time.time;
        }
    }
}
