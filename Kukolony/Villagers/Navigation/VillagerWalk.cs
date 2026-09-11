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

        /// <summary>How much closer counts as having got somewhere.</summary>
        private const float ProgressStep = .25f;

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
        private const float RescueAfterSeconds = 15f;

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
        private float _lastProgress;
        private bool _reckoning;
        private float _reckonUntil;
        private int _rescues;
        private int _bursts;

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

        /// <summary>The closest it has managed to get to the current target.</summary>
        internal float Closest => _closest;

        /// <param name="deltaTime">
        ///     The AI tick's own step. Defaulted to the frame time for callers that do not have
        ///     it, but passing the real one matters: the AI is driven at a fixed 0.05s while a
        ///     frame is nearer 0.02, so a villager covering ground unseen moved at forty per
        ///     cent of its own walking speed - correct-looking, steady, and wrong.
        /// </param>
        internal MoveResult MoveTowards(Vector3 target, float stopDistance, bool run = false,
            float deltaTime = 0f)
        {
            _journey.Prepare(target, stopDistance);

            // A rescue already under way. Bursts are bounded so that walking is always tried
            // again: the first version simply reckoned while it was stalled, and since nothing
            // updated the stall clock during reckoning it never stopped being stalled - so a
            // villager that escaped one bad patch glided the rest of the way home and reported
            // that it had never come back into view.
            if (_reckoning)
            {
                if (Time.time < _reckonUntil && _journey.Travelling)
                {
                    VillagerMovement.Stop(_ai);
                    return _journey.Advance(_ai.m_character, target,
                        deltaTime > 0f ? deltaTime : Time.deltaTime)
                        ? MoveResult.Arrived
                        : MoveResult.Moving;
                }

                // Only hand back to the ground when there is ground to hand back to. Otherwise
                // keep covering distance: a villager set down where no path can even begin
                // reports no path forever, and would stand there until something removed it.
                if (!EndReckoning()) _reckonUntil = Time.time + Rescue.BurstSeconds(_bursts);
                return MoveResult.Moving;
            }

            if (_journey.Travelling && StalledFor > RescueAfterSeconds)
            {
                // In view, and the gentle option has not been exhausted: put it back on the
                // navmesh where it stands. A villager that cannot walk is usually standing
                // somewhere the navmesh does not reach, and this is a correction of a metre or
                // two rather than something worth hiding from.
                if (_journey.Observed(_ai.transform.position) && _rescues < RescuesBeforeGliding)
                {
                    // If there is nowhere to stand, putting it back on the navmesh is not a
                    // rescue and pretending otherwise costs fifteen seconds per attempt.
                    if (_journey.Resume(_ai.m_character))
                    {
                        _rescues++;
                        Forget();
                        return MoveResult.Moving;
                    }

                    BeginReckoning();
                    return MoveResult.Moving;
                }

                BeginReckoning();
                return MoveResult.Moving;
            }

            Retarget(target);

            float distance = Utils.DistanceXZ(target, _ai.transform.position);
            if (distance < _closest - ProgressStep)
            {
                // Real progress, not the first reading of a new journey. Forgetting a route sets
                // the closest-yet to infinity, so the tick after it every distance looks like an
                // improvement - and the rescue that did the forgetting immediately cleared its
                // own counter, leaving the ladder unable to climb past its second rung.
                bool measured = _closest < float.MaxValue;

                _closest = distance;
                _lastProgress = Time.time;

                if (measured)
                {
                    // Walking is working again, so the ground it was struggling with is behind
                    // it. Both ladders start from the bottom next time.
                    _rescues = 0;
                    _bursts = 0;
                }
            }

            MoveResult stepped = VillagerMovement.MoveTowards(_ai, _standing,
                VillagerMovement.MinimumStopDistance, run);

            // Inside the grace, a stop means the throttled pathfinder has not considered this
            // target yet. There is nothing to conclude from it either way.
            if (stepped != MoveResult.Moving && Time.time < _graceUntil) return MoveResult.Moving;

            float patience = _journey.Travelling ? StallSeconds : NearbyStallSeconds;
            switch (Arrival.Judge(stepped != MoveResult.Moving, distance, stopDistance,
                        StalledFor, patience))
            {
                case Approaching.Arrived:
                    return MoveResult.Arrived;

                case Approaching.GaveUp:
                    return MoveResult.PathFailed;

                default:
                    return MoveResult.Moving;
            }
        }

        private void BeginReckoning()
        {
            _reckoning = true;
            _bursts++;
            _reckonUntil = Time.time + Rescue.BurstSeconds(_bursts);
        }

        /// <summary>Hands the villager back to the ground: physics on, feet on the navmesh.</summary>
        /// <returns>False when there was nowhere to stand, so the rescue must continue.</returns>
        private bool EndReckoning()
        {
            if (!_journey.Resume(_ai.m_character)) return false;

            _reckoning = false;
            Forget();
            return true;
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
