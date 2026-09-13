using System.Collections.Generic;
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

        /// <summary>
        ///     How often the route ahead is re-sampled for water, and how far apart the
        ///     samples sit. Terrain height comes from the world generator - arithmetic, not
        ///     colliders - so the answer does not depend on anything being loaded.
        /// </summary>
        private const float ProbeSeconds = 1f;

        private const float ProbeStep = 12f;

        /// <summary>
        ///     Fallback for how far under the surface ground must sit before it counts as
        ///     water, used only when the villager's own swim depth is unset. A ford a
        ///     villager can wade through on foot is not a crossing, and counting any depth
        ///     at all sent villagers into a deliberate crossing over ankle-deep shallows.
        ///     The live threshold comes from <c>Character.m_swimDepth</c> - the game's own
        ///     number for where this body stops walking - so a prefab or game update that
        ///     changes it cannot silently diverge from a constant restating it here.
        /// </summary>
        private const float WadeDepth = 1.5f;

        /// <summary>How far around the projected next stretch to look for walkable ground.</summary>
        private const float LegSearch = 15f;

        /// <summary>How much closer a snapped leg must be before it counts as the way forward.</summary>
        private const float MinimumLegGain = 10f;

        /// <summary>
        ///     How far off the straight bearing a leg may be tried, in order.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Straight ahead first, then a little either side, then a lot. A villager walks
        ///         the line when the line is walkable and goes round when it is not, which is the
        ///         whole of what "avoiding things" means at this scale - the local avoidance
        ///         inside a leg is the navmesh's own.
        ///     </para>
        ///     <para>
        ///         Both signs at each angle, nearer first: a detour of twenty-five degrees is
        ///         preferred to one of fifty whichever side it lies, and preferring one side
        ///         would make a villager circle an obstacle always the same way, which is how
        ///         one ends up walking into the same corner from two directions.
        ///     </para>
        /// </remarks>
        private static readonly float[] Fan = { 0f, 25f, -25f, 50f, -50f, 75f, -75f, 110f, -110f };

        /// <summary>How long a chosen leg is kept before the fan is walked again.</summary>
        /// <remarks>
        ///     Choosing asks the pathfinder up to nine questions, which is not a thing to do
        ///     twenty times a second per villager. A leg is a second or two of walking, and it
        ///     is re-chosen when it is reached or when it stops being reachable - this is only
        ///     the backstop for ground that changes underneath it.
        /// </remarks>
        private const float LegSeconds = 2f;

        /// <summary>Near enough to the leg to want the next one.</summary>
        private const float LegReached = 6f;

        /// <summary>The shortest leg worth walking to.</summary>
        private const float ShortestLeg = 8f;

        /// <summary>
        ///     How long a villager may push at a leg without closing on it before that leg is
        ///     treated as a wall.
        /// </summary>
        /// <remarks>
        ///     Three seconds, against the rescue ladder's forty-five. They are answers to
        ///     different questions: the ladder asks "is this villager stranded", which wants
        ///     patience, and this asks "is this way blocked", which wants none - a villager
        ///     walking into a hillside is not going to start climbing it on the fortieth second.
        /// </remarks>
        private const float LegStuckSeconds = 3f;

        /// <summary>Closing this much counts as getting somewhere.</summary>
        private const float LegProgress = 1.5f;

        /// <summary>How long a direction that failed is left alone.</summary>
        private const float BlockedSeconds = 25f;

        /// <summary>How wide an arc a failed direction closes off.</summary>
        /// <remarks>
        ///     Forty degrees either side, which is wide enough that the next candidate is a
        ///     genuinely different way round rather than the same hillside half a step over,
        ///     and narrow enough that a villager blocked in one direction has not talked itself
        ///     out of most of the compass.
        /// </remarks>
        private const float BlockedArc = 40f;

        /// <summary>How many failed directions are remembered at once.</summary>
        private const int BlockedKept = 3;

        /// <summary>
        ///     How near standable ground must be to count as a stride away - close enough
        ///     that putting a villager there reads as stepping onto it.
        /// </summary>
        /// <remarks>
        ///     Named rather than taken as "the first ring of the rescue ladder", because the
        ///     two are different questions that happen to share a number. The ladder's rings
        ///     are about where a rescue may place somebody; this is about what a watching
        ///     player will accept. Retuning the ladder - widening its first ring for rough
        ///     ground, or prepending a finer one - would otherwise silently move the
        ///     watched-water rule with it, and at 20m that rule is the visible teleport it
        ///     was written to stop.
        /// </remarks>
        private const float StrideSearch = 5f;

        /// <summary>
        ///     How far to look for standable ground, widening until something is found.
        /// </summary>
        /// <remarks>
        ///     Nearest first, so a villager is put down as close as possible to where it actually
        ///     is rather than flung to the far side of whatever it was standing on.
        /// </remarks>
        private static readonly float[] ResumeSearches = { StrideSearch, 20f, 60f };

        private readonly MonsterAI _ai;

        /// <summary>How far a destination must move to count as somewhere else.</summary>
        /// <remarks>
        ///     Small, because the question is "is this the same errand", and a job re-reading the
        ///     world nudges a target by centimetres rather than metres. Anything larger lets one
        ///     journey's latch follow a villager onto the next.
        /// </remarks>
        private const float DestinationMoved = 5f;

        private Vector3 _destination = new Vector3(float.MaxValue, 0f, float.MaxValue);
        private Vector3 _waypoint;

        /// <summary>Whether the current waypoint was chosen by asking, rather than by bearing.</summary>
        private bool _legChosen;

        private float _legUntil;

        /// <summary>
        ///     How long a villager that has just lost its route still counts as having one.
        /// </summary>
        /// <remarks>
        ///     The fan finding nothing is usually the corridor not having finished building
        ///     rather than the villager being walled in - and the probing that just failed is
        ///     itself what pokes those tiles, so the answer is often different a second later.
        ///     Carrying somebody the moment the first question comes back "no" is how a journey
        ///     over open meadow ends up part-glided.
        /// </remarks>
        private const float LookingSeconds = 8f;

        private float _lookingUntil;
        private bool _travelling;
        private float _nextPoke;
        private float _nextProbe;
        private bool _waterAhead;

        internal Journey(MonsterAI ai) => _ai = ai;

        /// <summary>Where the ground needs to exist, so the keep-alive set can hold it open.</summary>
        internal Vector3 Waypoint => _travelling ? _waypoint : _ai.transform.position;

        /// <summary>
        ///     Whether the current leg is one the pathfinder said could be walked.
        /// </summary>
        /// <remarks>
        ///     False means the fan found nothing reachable - walled in, or the corridor has not
        ///     finished building - which is the only circumstance in which carrying a villager
        ///     is the lesser evil.
        /// </remarks>
        internal bool HasRoute => _travelling && (_legChosen || Time.time < _lookingUntil);

        /// <summary>Whether this is a journey rather than a step across the settlement.</summary>
        internal bool Travelling => _travelling;

        /// <summary>
        ///     Whether the next stretch of this journey is under water.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Sampled from the world generator rather than from colliders, because the
        ///         question is about ground nobody has loaded and arithmetic is the only thing
        ///         that can answer for it. A handful of points between here and the waypoint,
        ///         once a second - a probe, not a survey.
        ///     </para>
        ///     <para>
        ///         This is what turns an ocean from a forty-five-second stall at the shoreline
        ///         into a decision. Water cannot become walkable by waiting, which is the whole
        ///         difference between it and every other reason a walk stops making progress.
        ///     </para>
        /// </remarks>
        internal bool WaterAhead(Vector3 from)
        {
            if (!_travelling) return false;
            if (Time.time < _nextProbe) return _waterAhead;

            _nextProbe = Time.time + ProbeSeconds;
            _waterAhead = false;

            if (WorldGenerator.instance == null || ZoneSystem.instance == null) return false;

            float water = ZoneSystem.instance.m_waterLevel;
            float wade = SwimDepth();
            Vector3 bearing = _waypoint - from;
            bearing.y = 0f;
            float span = bearing.magnitude;
            if (span < ProbeStep) return false;

            Vector3 step = bearing / span;
            for (float along = ProbeStep; along <= span; along += ProbeStep)
            {
                Vector3 at = from + step * along;
                if (WorldGenerator.instance.GetHeight(at.x, at.z) < water - wade)
                {
                    _waterAhead = true;
                    break;
                }
            }

            return _waterAhead;
        }

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

            // A latch belongs to one journey. Being sent somewhere else is a new journey, however
            // near it is - and without noticing that, a villager which once had a far target went
            // on walking towards a leg forty-five metres ahead after its target became a chest
            // ten metres away, which is what wandering off in a random direction looks like from
            // the outside.
            if (Utils.DistanceXZ(_destination, destination) > DestinationMoved)
            {
                _destination = destination;
                _travelling = false;
                ForgetWater();
            }

            if (!_travelling && remaining > HopLength)
            {
                _travelling = true;

                // The grace starts with the journey, not with the first leg found. The corridor
                // ahead is at its least built in the moment a villager sets off, so the first
                // fan is the one most likely to come back empty - and treating that first "no"
                // as "walled in" is how a journey over open meadow began with a glide.
                _lookingUntil = Time.time + LookingSeconds;
            }
            if (_travelling && remaining <= arriveWithin) _travelling = false;
            if (!_travelling) return;

            Vector3 bearing = destination - here;
            bearing.y = 0f;
            if (bearing.sqrMagnitude < .01f) return;

            // The leg that was chosen stands until it is reached, stops being walkable, or ages
            // out. Re-asking every tick would be nine path queries twenty times a second per
            // villager, and would also make the route flicker between two equally good ways
            // round the same rock.
            // Kept until it is reached or it ages out, and deliberately not re-verified in
            // between: asking whether the leg is still reachable is itself a path query, and one
            // of those per tick per villager is twenty a second for an answer that changes on
            // the scale of seconds. Two seconds of walking is the resolution this needs.
            bool reached = Utils.DistanceXZ(here, _waypoint) <= LegReached;

            // Whether the leg is being walked or merely leaned on. A body pressed against a
            // slope it cannot climb reports every sign of walking - the legs move, the
            // pathfinder is happy, the leg is reachable on the mesh - and closes no distance at
            // all, because Unity's idea of a walkable incline and what a Valheim body can climb
            // are two different numbers.
            bool blocked = _legChosen && !reached && Pushing(here);

            if (_legChosen && !reached && !blocked && Time.time < _legUntil)
            {
                PokeAhead(here, destination);
                return;
            }

            // That way is a wall. Refusing the direction rather than the point matters: the
            // point a step to its left is the same hillside, and a villager that re-chose it
            // would lean on the hill again three seconds later, for ever.
            if (blocked) Block(here, _waypoint);

            if (!TryChooseLeg(here, destination, remaining, bearing.normalized, out Vector3 leg))
            {
                // Nothing reachable in any direction. The straight-line waypoint is kept so the
                // keep-alive still holds ground open ahead - the corridor being unbuilt is the
                // usual reason, and the tiles the probing just poked are what fixes it a moment
                // later. The rescue ladder is what catches a villager genuinely walled in.
                _waypoint = Ahead(here, bearing.normalized, remaining);
                _legChosen = false;
                PokeAhead(here, destination);
                return;
            }

            _waypoint = leg;
            _legChosen = true;
            _legUntil = Time.time + LegSeconds;
            _lookingUntil = Time.time + LegSeconds + LookingSeconds;
            _legClosest = Utils.DistanceXZ(here, leg);
            _legPushingSince = Time.time;

            PokeAhead(here, destination);
        }

        /// <summary>
        ///     The furthest leg along the fan that this villager can actually walk to.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>Reachable, not merely walkable-looking.</b> The leg used to be the point
        ///         forty-five metres along the straight bearing, snapped to the nearest navmesh.
        ///         That asks whether there is ground there and never whether the villager can
        ///         get to it - so a leg on the far side of a rock face, a ravine or a lake was
        ///         accepted, the villager walked into the obstacle, stalled, and the rescue
        ///         ladder carried it the rest of the way in a straight line. Going round things
        ///         is not a thing a bearing can express.
        ///     </para>
        ///     <para>
        ///         Every candidate is asked of the pathfinder with a <em>full</em> path required,
        ///         because the point of the question is "can I walk there", and a partial path is
        ///         the answer "no, but I can walk towards it" - which is what got a villager
        ///         stuck against the obstacle in the first place.
        ///     </para>
        ///     <para>
        ///         Probing is not free of side effects and that is half of why it works: every
        ///         query pokes a three-by-three block of navmesh tiles around the point it asks
        ///         about, so the fan builds the corridor ahead as it looks down it.
        ///     </para>
        /// </remarks>
        private bool TryChooseLeg(Vector3 here, Vector3 destination, float remaining,
            Vector3 bearing, out Vector3 leg)
        {
            leg = here;
            if (Pathfinding.instance == null) return false;

            // The long hop first, then shorter ones. A leg the villager cannot reach is usually
            // reaching too far - past the built navmesh, or across the neck of a bay - and three
            // metres of walking that is genuinely walkable beats forty-five metres of sliding.
            // Each shorter attempt also pokes tiles nearer to hand, which is the ground most
            // likely to finish building first.
            foreach (float share in Hops)
            {
                if (TryFan(here, destination, remaining * share, bearing, out leg)) return true;
            }

            return false;
        }

        /// <summary>How much of a hop to try, longest first.</summary>
        private static readonly float[] Hops = { 1f, .5f, .25f };

        private bool TryFan(Vector3 here, Vector3 destination, float reach, Vector3 bearing,
            out Vector3 leg)
        {
            leg = here;

            // Below this there is no point: the leg would be inside the villager's own arrival
            // tolerance and choosing it would be choosing to stand still.
            if (reach < ShortestLeg) return false;

            float remaining = Utils.DistanceXZ(here, destination);
            float gain = Mathf.Min(MinimumLegGain, reach * .5f);

            foreach (float angle in Fan)
            {
                Vector3 aimed = Quaternion.Euler(0f, angle, 0f) * bearing;
                if (Refused(aimed)) continue;

                Vector3 candidate = Ahead(here, aimed, reach);

                // No FindValidPoint here any more: it is the engine's broken sampler, and
                // GetPath snaps both of its own ends correctly - so asking for the path is both
                // the snap and the answer, in one question instead of two.

                // How far along the navmesh this actually gets, which is the leg. Asking for a
                // *full* path was too strict by half: over ground that is still building, most
                // candidates have no complete route yet, so every one of them was refused and
                // the villager was declared walled in and carried - measured at half the journey
                // spent being carried over open meadow.
                //
                // A partial path is not a failure here, it is the answer to the question this
                // job actually asks: how far can I walk towards that. Its last corner is the
                // edge of the built world in that direction, and walking to it is what brings
                // the next stretch into range - which is the whole mechanism.
                if (!Pathfinding.instance.GetPath(here, candidate, _corners, _ai.m_pathAgentType,
                        requireFullPath: false, cleanup: false))
                {
                    continue;
                }

                if (_corners.Count == 0) continue;

                Vector3 reached = _corners[_corners.Count - 1];

                // Still progress. The nearest navmesh point can be to the side of the route or
                // behind it, and a partial path can stop almost where it started - a villager
                // sent sideways walks perfectly well while getting no closer, which reads
                // exactly like being stuck.
                if (Utils.DistanceXZ(reached, destination) >= remaining - gain) continue;

                leg = reached;
                return true;
            }

            return false;
        }

        /// <summary>
        ///     Whether the villager is leaning on its leg rather than walking it.
        /// </summary>
        /// <remarks>
        ///     Measured as distance to the leg, not distance travelled: a villager grinding
        ///     along the foot of a slope is moving, sometimes briskly, and getting no nearer to
        ///     where it was going. The clock resets on every real gain, so a slow climb that is
        ///     working is never mistaken for a wall.
        /// </remarks>
        private bool Pushing(Vector3 here)
        {
            float distance = Utils.DistanceXZ(here, _waypoint);

            if (distance < _legClosest - LegProgress)
            {
                _legClosest = distance;
                _legPushingSince = Time.time;
                return false;
            }

            return Time.time - _legPushingSince > LegStuckSeconds;
        }

        /// <summary>Refuses the direction a leg was in, for a while.</summary>
        private void Block(Vector3 here, Vector3 leg)
        {
            Vector3 bearing = leg - here;
            bearing.y = 0f;
            if (bearing.sqrMagnitude < .01f) return;

            bearing = bearing.normalized;

            // Oldest out. A villager in a dead end can refuse its way out of every direction it
            // has, and then it has nothing to try - three is enough to walk round a hill and
            // few enough that the compass reopens behind it.
            if (_blocked.Count >= BlockedKept) _blocked.RemoveAt(0);

            _blocked.Add(new Vector4(bearing.x, bearing.z, 0f, Time.time + BlockedSeconds));
        }

        /// <summary>Whether this direction is one that has just failed.</summary>
        private bool Refused(Vector3 bearing)
        {
            float now = Time.time;

            for (int i = _blocked.Count - 1; i >= 0; i--)
            {
                Vector4 wall = _blocked[i];
                if (now > wall.w)
                {
                    _blocked.RemoveAt(i);
                    continue;
                }

                Vector3 was = new Vector3(wall.x, 0f, wall.y);
                if (Vector3.Angle(was, bearing) <= BlockedArc) return true;
            }

            return false;
        }

        /// <summary>A point one hop along a bearing, put on the ground.</summary>
        private static Vector3 Ahead(Vector3 here, Vector3 bearing, float remaining)
        {
            Vector3 point = here + bearing * Mathf.Min(HopLength, remaining);
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(point, out float ground))
            {
                point.y = ground;
            }

            return point;
        }

        /// <summary>The corners of the last path asked for, reused so choosing allocates nothing.</summary>
        private readonly List<Vector3> _corners = new List<Vector3>();

        /// <summary>Directions that turned out to be walls, and when they stop being refused.</summary>
        private readonly List<Vector4> _blocked = new List<Vector4>();

        private float _legClosest = float.MaxValue;

        private float _legPushingSince;

        internal void Forget()
        {
            _travelling = false;
            _legChosen = false;
            _blocked.Clear();
            _legClosest = float.MaxValue;

            // The probe caches for a second, so a water flag latched at the end of one
            // errand survived into the next one started within it - and a villager on dry
            // land opened its new journey with a phantom crossing.
            ForgetWater();
        }

        private void ForgetWater()
        {
            _waterAhead = false;
            _nextProbe = 0f;
        }

        /// <summary>Where this body stops walking and starts swimming.</summary>
        private float SwimDepth()
        {
            Character body = _ai != null ? _ai.m_character : null;
            return body != null && body.m_swimDepth > 0f ? body.m_swimDepth : WadeDepth;
        }

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
        ///     Whether there is anywhere within reach this villager could stand and walk
        ///     from, and whether that somewhere is within a stride.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Asked before deciding, and answered without moving anything, so the
        ///         decision and the action agree about the world. The alternative - deciding
        ///         to put a villager back on its feet and only then discovering there is
        ///         nowhere to put it - is what left one standing still for five minutes,
        ///         allowed to do neither.
        ///     </para>
        ///     <para>
        ///         Both answers come from one search, because the decision needs both every
        ///         tick while rescuing and the narrow question is the wide one's first ring -
        ///         asking them separately put two identical pathfinder queries on the same
        ///         tick. <paramref name="near" /> is the one decision where the ladder's
        ///         sixty-metre generosity is wrong: a watched water crossing must land only
        ///         where landing reads as stepping ashore, not as snapping to a shore across
        ///         the bay.
        ///     </para>
        /// </remarks>
        internal bool CanStand(Character body, out bool near)
        {
            near = false;
            if (body == null) return false;

            foreach (float radius in ResumeSearches)
            {
                if (!HasStanding(body, radius)) continue;

                // Measured against the stride, not against which ring happened to answer,
                // so the ladder's rings and the watched-landing rule can be retuned apart.
                near = radius <= StrideSearch;
                return true;
            }

            return false;
        }

        private bool TryFindStanding(Character body, out Vector3 point)
        {
            point = Vector3.zero;
            if (body == null || Pathfinding.instance == null) return false;

            foreach (float radius in ResumeSearches)
            {
                if (Standing.Near(body.transform.position, radius, _ai.m_pathAgentType, out point))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether an agent of this kind could stand within a given radius.</summary>
        private bool HasStanding(Character body, float radius) =>
            Standing.Near(body.transform.position, radius, _ai.m_pathAgentType, out _);

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
