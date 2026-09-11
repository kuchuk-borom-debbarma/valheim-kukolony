using UnityEngine;

namespace Kukolony.Villagers.Navigation
{
    /// <summary>
    ///     Turning "go to that thing" into a point the pathfinder will accept.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b><c>BaseAI.FindPath</c> requires a complete path and gives up entirely without
    ///         one.</b> That is the root of villagers appearing to walk at obstacles and stop:
    ///         they are not walking at anything. Measured at the moment of failure, with a
    ///         villager stuck five metres from a chest:
    ///     </para>
    ///     <code>
    ///     agent=Humanoid  fullPath=False  partialPath=True  waypoints=0
    ///     </code>
    ///     <para>
    ///         The agent type is right and the navmesh can route almost all the way there - but
    ///         the destination itself is the centre of a solid object, which is not a point
    ///         anything can stand on. No full path exists, so <c>FindPath</c> returns false,
    ///         <c>MoveTo</c> takes its "stopped" branch with an empty waypoint list, and the
    ///         villager never takes a step. It is not bad pathfinding; it is a destination the
    ///         pathfinder was right to refuse.
    ///     </para>
    ///     <para>
    ///         So the destination is snapped to the navmesh before anyone is asked to walk to
    ///         it. <c>Pathfinding.FindValidPoint</c> is the engine's own answer to "somewhere
    ///         near here that an agent of this kind can be", which beats offsetting by a guessed
    ///         distance - a guess has no idea whether the spot it picked is a wall, a drop, or
    ///         the inside of the next chest along.
    ///     </para>
    /// </remarks>
    internal static class Approach
    {
        /// <summary>
        ///     Close enough to something that can be stood on.
        /// </summary>
        /// <remarks>
        ///     Not as tight as "standing on it", because an item lying against a rock or a tree
        ///     has the same coarse-navmesh problem a chest does - measured at 3.4m with no full
        ///     path available. Picking up needs ownership of the item's ZDO rather than an
        ///     outstretched arm, so the looser tolerance costs nothing real.
        /// </remarks>
        internal const float ToLooseItem = 3.5f;

        /// <summary>
        ///     Close enough to something solid.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Five metres, which is further than it looks like it should need and is
        ///         measured rather than chosen. A villager sent to a chest walks its full path,
        ///         consumes every waypoint, and comes to rest <b>4.1m</b> from the chest with a
        ///         valid complete path behind it - that is simply as near as the navmesh goes.
        ///         Valheim's path tiles are coarse and a placed piece blocks several metres
        ///         around itself, so demanding three and a half produced a villager standing as
        ///         close as it was ever going to get, reporting that it could not get there,
        ///         forever.
        ///     </para>
        ///     <para>
        ///         Nothing is lost by the extra distance: taking from and putting into a
        ///         container needs ownership of its ZDO, not an outstretched arm, and the reach
        ///         gesture is cosmetic. Being a little further away than necessary costs
        ///         nothing; being a little too close costs the whole job.
        ///     </para>
        /// </remarks>
        internal const float ToStructure = 5f;

        /// <summary>
        ///     How far around a refused destination to look for one that works.
        /// </summary>
        /// <remarks>
        ///     Deliberately smaller than <see cref="ToStructure" />. The search radius and the
        ///     arrival tolerance compound: a substitute point found five metres from the chest
        ///     is a villager that arrives exactly where it was sent and is still too far away to
        ///     use anything. Keeping the substitute nearer than the tolerance means reaching it
        ///     is reaching the chest.
        /// </remarks>
        private const float SearchRadius = 3f;

        /// <summary>How close a villager needs to get to this before it counts as being there.</summary>
        /// <remarks>
        ///     Asked of what the object <em>is</em> rather than of its colliders. A dropped item
        ///     has a collider too - it has to, or it would fall through the world - so testing
        ///     for one would stand the villager off from every item it was sent to pick up.
        ///
        ///     One function, used both to walk and to decide it has arrived. Two numbers that
        ///     disagree produce a villager that stops walking and never registers arriving.
        /// </remarks>
        internal static float DistanceTo(GameObject target) =>
            target != null && target.GetComponent<ItemDrop>() != null ? ToLooseItem : ToStructure;

        /// <summary>
        ///     Somewhere the villager can actually stand in order to reach <paramref name="desired" />.
        /// </summary>
        /// <remarks>
        ///     Falls back to the desired point itself when the navmesh offers nothing. That is
        ///     not a silent failure: walking at an unreachable point is what stall detection is
        ///     for, and pretending the destination does not exist would lose work that becomes
        ///     reachable a moment later when a door opens or a zone finishes loading.
        /// </remarks>
        internal static Vector3 Standing(MonsterAI ai, Vector3 desired)
        {
            if (ai == null || Pathfinding.instance == null) return desired;

            Pathfinding.AgentType agent = ai.m_pathAgentType;
            Vector3 from = ai.transform.position;

            if (Pathfinding.instance.HavePath(from, desired, agent)) return desired;

            if (Pathfinding.instance.FindValidPoint(out Vector3 valid, desired, SearchRadius, agent) &&
                Pathfinding.instance.HavePath(from, valid, agent))
            {
                return valid;
            }

            return desired;
        }
    }
}
