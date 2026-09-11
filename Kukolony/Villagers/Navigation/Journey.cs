using UnityEngine;

namespace Kukolony.Villagers.Navigation
{
    /// <summary>
    ///     Making long journeys possible by preparing the ground for them.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Valheim's navmesh is a lazily built cache, and asking for a path is how you ask
    ///         for it to be built.</b> From the decompiled source: tiles are 32m squares built
    ///         from physics colliders; <c>GetPath</c> pokes the tiles at both ends of the query
    ///         itself; <c>UpdatePathfinding</c> runs every 0.1s and builds at most one tile per
    ///         call; tiles expire after <c>m_tileTimeout</c> (30s) without a poke.
    ///     </para>
    ///     <para>
    ///         <b>The destination is walked to directly, however far away it is.</b> That is not
    ///         a shortcut - it is what Valheim's own creatures do, and the reason they cross the
    ///         world. <c>BaseAI.MoveTo</c> follows a <em>partial</em> path (<c>FindPath</c> calls
    ///         <c>GetPath</c> with its defaults, so <c>requireFullPath</c> is false) and
    ///         recomputes at most once a second. A creature therefore walks to the edge of the
    ///         navmesh that exists, and by the time it arrives the next stretch has been built.
    ///         Streaming is already happening; it only needs ground to stream.
    ///     </para>
    ///     <para>
    ///         This replaced a version that chose its own intermediate hops, and the failure is
    ///         worth keeping because it looked so reasonable. The navmesh around a villager that
    ///         has not moved reaches about two metres, so the hops it picked were one to three
    ///         metres long - and handing <c>MoveTo</c> a target one metre away makes a character
    ///         decelerate to a stop rather than walk. Measured: three centimetres of progress
    ///         every three seconds, while planning flawlessly and reporting itself as
    ///         travelling. Choosing the waypoints was the entire mistake. The engine picks
    ///         better ones, continuously, for free.
    ///     </para>
    ///     <para>
    ///         So what is left here is only what the engine cannot do for itself: holding the
    ///         ground ahead loaded, and asking for paths into it early enough that the tiles are
    ///         built before the villager gets there. Ground behind looks after itself - tiles
    ///         time out and the keep-alive halo moves along with the villager.
    ///     </para>
    /// </remarks>
    internal sealed class Journey
    {
        /// <summary>
        ///     How far ahead to hold ground open, and the distance beyond which a walk counts as
        ///     travelling rather than stepping across the settlement.
        /// </summary>
        /// <remarks>
        ///     Comfortably inside both things that bound it: <c>PokeArea</c> marks a 3x3 block of
        ///     32m tiles around each end of a query, and the keep-alive halo holds a 3x3 block of
        ///     64m zones around each anchor.
        /// </remarks>
        internal const float HopLength = 45f;

        private const float PokeSeconds = 1f;

        /// <summary>How far to look for standable ground when a journey becomes observed.</summary>
        private const float ResumeSearch = 20f;

        private readonly MonsterAI _ai;

        private Vector3 _waypoint;
        private bool _travelling;
        private float _nextPoke;

        internal Journey(MonsterAI ai) => _ai = ai;

        /// <summary>Where the ground needs to exist, so the keep-alive set can hold it open.</summary>
        internal Vector3 Waypoint => _travelling ? _waypoint : _ai.transform.position;

        /// <summary>Whether this is a journey rather than a step across the settlement.</summary>
        internal bool Travelling => _travelling;

        /// <summary>Prepares the ground between here and there. Call before walking.</summary>
        internal void Prepare(Vector3 destination)
        {
            Vector3 here = _ai.transform.position;
            float remaining = Utils.DistanceXZ(here, destination);

            _travelling = remaining > HopLength;
            if (!_travelling) return;

            Vector3 bearing = destination - here;
            bearing.y = 0f;
            if (bearing.sqrMagnitude < .01f) return;

            // The keep-alive anchor: far enough ahead that its halo covers ground the villager
            // has not reached, near enough that it is on the way rather than over the horizon.
            _waypoint = here + bearing.normalized * Mathf.Min(HopLength, remaining);
            if (ZoneSystem.instance != null &&
                ZoneSystem.instance.GetSolidHeight(_waypoint, out float ground))
            {
                _waypoint.y = ground;
            }

            PokeAhead(here, destination);
        }

        internal void Forget() => _travelling = false;

        /// <summary>
        ///     Whether anyone could see this happen.
        /// </summary>
        /// <remarks>
        ///     The range is generous on purpose. Being wrong towards "somebody is watching"
        ///     costs a slower journey; being wrong the other way means a player sees a villager
        ///     glide.
        /// </remarks>
        internal bool Observed(Vector3 where)
        {
            float range = ModConfig.TravelObservedRange.Value;
            return range > 0f && Player.GetClosestPlayer(where, range) != null;
        }

        /// <summary>
        ///     Moves the villager towards its destination at walking pace, without walking.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>This is what makes arrival reliable rather than likely.</b> Walking depends
        ///         on a navmesh, a navmesh depends on loaded colliders, and ground that no player
        ///         has ever been near may never get either - so a villager sent across the world
        ///         on its own can be stuck forever through no fault of its own. Measured on
        ///         identical terrain, the same journey covered eleven metres in thirty seconds on
        ///         one run and two on the next. That is not something to build a settlement on.
        ///     </para>
        ///     <para>
        ///         So when nobody can see it, the villager advances by dead reckoning at its own
        ///         walking speed. This is not a cheat and not a teleport: it takes exactly as long
        ///         as walking would, it is bounded to a step at a time, and the villager anchors
        ///         its own keep-alive halo as it goes, so ground loads along the route behind and
        ///         ahead of it. The moment a player is close enough to see anything, it goes back
        ///         to walking - and it is put down on the navmesh first, so it never resumes
        ///         standing in a lake.
        ///     </para>
        /// </remarks>
        internal bool Advance(Character body, Vector3 destination, float deltaTime)
        {
            if (body == null) return false;

            Vector3 here = body.transform.position;
            Vector3 bearing = destination - here;
            bearing.y = 0f;

            float remaining = bearing.magnitude;
            float step = Reckoning.StepLength(remaining, body.m_walkSpeed, deltaTime);

            if (step <= 0f) return remaining <= 0f;

            Vector3 next = here + bearing.normalized * step;
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(next, out float ground))
            {
                next.y = ground;
            }
            else
            {
                // Ground that has not loaded cannot be measured. Keeping the current height is
                // safe because the villager holds its own zone open, so the terrain arrives
                // under it within moments and the next step lands properly.
                next.y = here.y;
            }

            Place(body, next);
            return remaining - step <= 0f;
        }

        /// <summary>
        ///     Puts a villager back on ground the pathfinder recognises, before it walks again.
        /// </summary>
        internal void Resume(Character body)
        {
            if (body == null || Pathfinding.instance == null) return;

            if (Pathfinding.instance.FindValidPoint(out Vector3 valid, body.transform.position,
                    ResumeSearch, _ai.m_pathAgentType))
            {
                Place(body, valid);
            }
        }

        private static void Place(Character body, Vector3 position)
        {
            // Both, and in this order. Moving only the transform lets the rigidbody drag the
            // character back next physics step; moving only the body leaves everything that
            // reads the transform a frame behind.
            if (body.m_body != null) body.m_body.position = position;
            body.transform.position = position;
        }

        /// <summary>
        ///     Asks for a path further along the route purely for the side effect.
        /// </summary>
        /// <remarks>
        ///     The answer is discarded. <c>GetPath</c> pokes the navmesh tiles at both ends
        ///     whatever it returns, so this is the game's own way of saying "build the ground
        ///     over there, I am coming" - and it costs one query a second rather than one a tick.
        /// </remarks>
        private void PokeAhead(Vector3 here, Vector3 destination)
        {
            if (Pathfinding.instance == null || Time.time < _nextPoke) return;
            _nextPoke = Time.time + PokeSeconds;

            Pathfinding.instance.GetPath(here, _waypoint, null, _ai.m_pathAgentType,
                requireFullPath: false, cleanup: false);
        }
    }
}
