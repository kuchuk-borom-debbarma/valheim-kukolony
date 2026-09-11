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

        /// <summary>How far around the projected next stretch to look for walkable ground.</summary>
        private const float LegSearch = 15f;

        /// <summary>How much closer a snapped leg must be before it counts as the way forward.</summary>
        private const float MinimumLegGain = 10f;

        /// <summary>
        ///     How far to look for standable ground, widening until something is found.
        /// </summary>
        /// <remarks>
        ///     Nearest first, so a villager is put down as close as possible to where it actually
        ///     is rather than flung to the far side of whatever it was standing on.
        /// </remarks>
        private static readonly float[] ResumeSearches = { 5f, 20f, 60f };

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
        /// <param name="arriveWithin">
        ///     How close counts as arrived. A journey ends here or when somebody can see it -
        ///     never at a distance threshold.
        /// </param>
        /// <remarks>
        ///     <b>Travelling latches.</b> It begins when the destination is further off than the
        ///     settlement is wide and ends only on arrival; it does not switch off again partway
        ///     because the remaining distance dropped below the same number that started it.
        ///     That version oscillated: at exactly 45m out the villager announced it had finished
        ///     travelling, handed over to walking, failed to walk across ground with no navmesh,
        ///     and was immediately far enough away to start travelling again - back and forth,
        ///     stuck at 45m, at full speed, reporting nothing wrong.
        /// </remarks>
        internal void Prepare(Vector3 destination, float arriveWithin)
        {
            Vector3 here = _ai.transform.position;
            float remaining = Utils.DistanceXZ(here, destination);

            if (!_travelling && remaining > HopLength) _travelling = true;
            if (_travelling && remaining <= arriveWithin) _travelling = false;
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

            // Snapped to somewhere an agent of this kind can actually be. A straight line
            // projected forty-five metres ahead lands in a lake or against a cliff often enough
            // to matter, and a leg the villager cannot walk to is no better than a destination it
            // cannot walk to - it produces the same unanswerable path query and the same journey
            // spent entirely on rescues. Wide, because the point of the leg is to be roughly
            // ahead rather than exactly there.
            if (Pathfinding.instance != null &&
                Pathfinding.instance.FindValidPoint(out Vector3 walkable, _waypoint, LegSearch,
                    _ai.m_pathAgentType) &&
                Utils.DistanceXZ(walkable, destination) < remaining - MinimumLegGain)
            {
                // Only if it is still progress. FindValidPoint answers "the nearest place an
                // agent can be", which can be to the side of the route or behind it - and a
                // villager sent sideways walks perfectly well while getting no closer to where
                // it was going, which reads exactly like being stuck.
                _waypoint = walkable;
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
            // The same pace it would have travelled at, which is a jog: covering ground unseen
            // must take as long as doing it properly, or it stops being a simulation of the
            // journey and becomes a way of skipping it.
            float step = Reckoning.StepLength(remaining, Pace(body), deltaTime);

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

            Place(body, next, reckoning: true);
            return remaining - step <= 0f;
        }

        /// <summary>
        ///     Puts a villager back on ground the pathfinder recognises, before it walks again.
        /// </summary>
        /// <summary>
        ///     Puts a villager back on ground the pathfinder recognises, before it walks again.
        /// </summary>
        /// <returns>
        ///     Whether there was anywhere to put it. False means the villager is standing where
        ///     no agent of its kind can be, and handing it back to walking would strand it.
        /// </returns>
        /// <remarks>
        ///     <b>This is the difference between a rescue and a trap.</b> Covering ground unseen
        ///     ignores terrain, so it can set a villager down on water, inside a rock, or on a
        ///     ledge with no route off it. <c>GetPath</c> begins by snapping the <em>start</em> of
        ///     the route onto the navmesh and fails outright if it cannot - which is why a
        ///     stranded villager reports no path at all rather than a bad one, and why it reports
        ///     it forever. Widening the search is cheap; the caller keeps covering ground until
        ///     there is somewhere to land.
        /// </remarks>
        internal bool Resume(Character body)
        {
            if (TryFindStanding(body, out Vector3 valid))
            {
                Place(body, valid, reckoning: false);
                return true;
            }

            // Physics comes back even when there was nowhere better to stand. A villager left
            // kinematic cannot be moved by anything - not the character controller, not gravity -
            // so failing to find a landing spot must never also leave it frozen.
            if (body != null && body.m_body != null) body.m_body.isKinematic = false;
            return false;
        }

        /// <summary>
        ///     Whether there is anywhere within reach this villager could stand and walk from.
        /// </summary>
        /// <remarks>
        ///     Asked before deciding, and answered without moving anything, so the decision and
        ///     the action agree about the world. The alternative - deciding to put a villager
        ///     back on its feet and only then discovering there is nowhere to put it - is what
        ///     left one standing still for five minutes, allowed to do neither.
        /// </remarks>
        internal bool CanStand(Character body) => TryFindStanding(body, out _);

        private bool TryFindStanding(Character body, out Vector3 point)
        {
            point = Vector3.zero;
            if (body == null || Pathfinding.instance == null) return false;

            foreach (float radius in ResumeSearches)
            {
                if (Pathfinding.instance.FindValidPoint(out point, body.transform.position,
                        radius, _ai.m_pathAgentType))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>How fast this villager covers ground on a journey.</summary>
        /// <remarks>
        ///     Run speed, because a travelling villager runs. A creature with no run speed
        ///     configured falls back to walking rather than standing still, which is the failure
        ///     a zero here would cause.
        /// </remarks>
        private static float Pace(Character body) =>
            body.m_runSpeed > 0f ? body.m_runSpeed : body.m_walkSpeed;

        private static void Place(Character body, Vector3 position, bool reckoning)
        {
            // Physics off while covering ground unseen, and on again the moment it walks.
            //
            // Setting a position every tick and letting the character controller resolve it is
            // a fight the controller wins: at the first slope the villager was pushed back
            // exactly as far as it was moved, and sat at 0.0m/s for five minutes insisting it
            // was travelling. Nobody can see this happen - that is the precondition for
            // reckoning at all - so there is nothing to be gained by colliding with scenery,
            // and everything to be lost.
            if (body.m_body != null && body.m_body.isKinematic != reckoning)
            {
                body.m_body.isKinematic = reckoning;
            }

            // Both, and in this order. Moving only the transform lets the rigidbody drag the
            // character back next physics step; moving only the body leaves everything that
            // reads the transform a frame behind.
            if (body.m_body != null) body.m_body.position = position;
            body.transform.position = position;

            // And tell the ZDO, which is the part that is easy to forget and fatal to omit.
            // Valheim decides what exists by ZDO *sector*, and a sector only changes when
            // ZDO.SetPosition is called - moving a transform by hand never touches it. So a
            // villager covering ground unseen had a body three hundred metres from where the
            // world believed it to be, the keep-alive held zones around a position nothing
            // agreed with, and ZNetScene destroyed it mid-journey for being in no sector any
            // list mentioned. It walked 317 metres and then stopped existing.
            if (body.m_nview != null && body.m_nview.IsValid() && body.m_nview.IsOwner())
            {
                body.m_nview.GetZDO().SetPosition(position);
            }
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
