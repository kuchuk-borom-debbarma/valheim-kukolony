using UnityEngine;
using UnityEngine.AI;

namespace Kukolony.Villagers.Navigation
{
    /// <summary>
    ///     Somewhere an agent of a given kind can actually stand.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>This exists because the game's own answer is broken.</b>
    ///         <c>Pathfinding.FindValidPoint</c> builds its query filter with
    ///         <c>agentTypeID = (int)settings.m_agentType</c> - the enum's ordinal, 0, 1, 2 -
    ///         where Unity assigns agent type IDs as hashes. The filter therefore matches no
    ///         agent type at all and <c>NavMesh.SamplePosition</c> answers no almost everywhere.
    ///         <c>Pathfinding.GetPath</c> gets this right in <c>SnapToNavMesh</c>, using
    ///         <c>settings.m_build.agentTypeID</c>, which is why pathing works while asking
    ///         "is there ground here" does not.
    ///     </para>
    ///     <para>
    ///         Measured rather than deduced: with the engine's version, a travelling villager
    ///         reported <c>hasRoute=False canStand=False</c> on every tick of a journey it was
    ///         walking perfectly well, and was carried for half of it - because both of those
    ///         questions went through that one call and both always answered no.
    ///     </para>
    /// </remarks>
    internal static class Standing
    {
        /// <summary>Whether an agent of this kind can stand within a radius, and where.</summary>
        internal static bool Near(Vector3 around, float radius, Pathfinding.AgentType agent,
            out Vector3 point)
        {
            point = around;
            if (Pathfinding.instance == null) return false;

            // Poked first, as the engine's own call does: the answer is about navmesh, and a
            // tile nobody has asked about has none.
            Pathfinding.instance.GetPath(around, around, null, agent, requireFullPath: false,
                cleanup: false);

            Pathfinding.AgentSettings settings = Pathfinding.instance.GetSettings(agent);
            NavMeshQueryFilter filter = new NavMeshQueryFilter
            {
                agentTypeID = settings.m_build.agentTypeID,
                areaMask = settings.m_areaMask
            };

            if (!NavMesh.SamplePosition(around, out NavMeshHit hit, radius, filter)) return false;

            point = hit.position;
            return true;
        }
    }
}
