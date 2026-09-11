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

        private readonly MonsterAI _ai;
        private readonly Journey _journey;

        private Vector3 _target;
        private Vector3 _standing;
        private bool _hasTarget;
        private float _graceUntil;
        private float _closest;
        private float _lastProgress;
        private bool _wasReckoning;

        internal VillagerWalk(MonsterAI ai)
        {
            _ai = ai;
            _journey = new Journey(ai);
        }

        /// <summary>Where this villager currently intends to be, for the keep-alive set.</summary>
        internal Vector3 Waypoint => _journey.Waypoint;

        internal bool Travelling => _journey.Travelling;

        /// <summary>Whether it is covering ground unseen rather than walking it.</summary>
        internal bool Reckoning => _wasReckoning;

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
            // Anywhere further than a single hop is walked in hops, because Valheim will not
            // answer a path across ground it has not built a navmesh for - and it only builds
            // one where something has asked to walk. Near targets take the same route through
            // this with the destination unchanged, so there is one movement call in the mod
            // and it does not matter whether the caller knows the distance.
            _journey.Prepare(target, stopDistance);

            // Walking is the way a villager travels. Reckoning is what rescues it when walking
            // has stopped working, and nothing else does.
            //
            // Measured, same terrain and same destination on different runs: walking covered 83m
            // at a metre a second on one, and two metres in five minutes on the next. So it is
            // right often enough to be the default and wrong often enough that something has to
            // catch it - which is the whole argument for making this a rescue rather than the
            // primary mechanism. Reckoning ignores terrain; a villager should only be excused
            // from the ground when the ground has failed it.
            //
            // Gated on having made no progress rather than on distance. The earlier version
            // asked "is this a long journey and is nobody watching", which deadlocked: a
            // villager has to walk clear of the settlement before nobody is watching, so one
            // that stalled on its way out was never far enough away to be rescued.
            // Stalled in view: put it back on the navmesh where it stands, and let it try again.
            //
            // A villager that cannot walk is usually standing somewhere the navmesh does not
            // cover, and FindValidPoint answers "the nearest place an agent of this kind can be"
            // - a correction of a metre or two, which is not worth hiding from. Reckoning is not
            // used here: a player watching would see it glide, and the whole reason reckoning is
            // allowed at all is that nobody can see it.
            if (_journey.Travelling && StalledFor > RescueAfterSeconds &&
                _journey.Observed(_ai.transform.position))
            {
                _journey.Resume(_ai.m_character);
                Forget();
                return MoveResult.Moving;
            }

            if (_journey.Travelling && StalledFor > RescueAfterSeconds &&
                !_journey.Observed(_ai.transform.position))
            {
                _wasReckoning = true;
                VillagerMovement.Stop(_ai);
                return _journey.Advance(_ai.m_character, target,
                    deltaTime > 0f ? deltaTime : Time.deltaTime)
                    ? MoveResult.Arrived
                    : MoveResult.Moving;
            }

            // Coming back into view: put it down somewhere it can walk from before it tries.
            if (_wasReckoning)
            {
                _wasReckoning = false;
                _journey.Resume(_ai.m_character);
                Forget();
            }

            Retarget(target);

            float distance = Utils.DistanceXZ(target, _ai.transform.position);
            if (distance < _closest - ProgressStep)
            {
                _closest = distance;
                _lastProgress = Time.time;
            }

            // Walk as close to the navmesh point as it can get, and judge arrival generously
            // against the thing actually wanted. Passing the caller's tolerance to both would
            // compound them - stopping short of a point that is already short of the chest -
            // and the villager would arrive precisely where it was sent, still out of reach.
            MoveResult result = VillagerMovement.MoveTowards(_ai, _standing,
                VillagerMovement.MinimumStopDistance, run);
            if (result == MoveResult.Moving) return MoveResult.Moving;

            if (distance <= stopDistance) return MoveResult.Arrived;

            // Stopped, but not there. That is the ordinary state of affairs rather than a
            // failure: BaseAI follows a *partial* path and reports "stopped" every time it
            // reaches the end of the navmesh that has been built so far, which on any walk
            // across unvisited ground happens over and over. The tiles ahead then build - the
            // asking is what builds them - and the next call carries on.
            //
            // So a walk fails when it stops getting closer, not when it stops walking. The
            // grace still covers the first seconds, when the throttled pathfinder is answering
            // about a target it has not considered yet and there is no progress to measure.
            if (Time.time < _graceUntil) return MoveResult.Moving;

            float patience = _journey.Travelling ? StallSeconds : NearbyStallSeconds;
            return StalledFor < patience ? MoveResult.Moving : MoveResult.PathFailed;
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
