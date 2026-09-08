using UnityEngine;

namespace Kukolony.Villagers
{
    /// <summary>Outcome of a movement step.</summary>
    internal enum MoveResult
    {
        /// <summary>Still walking. Call again next tick.</summary>
        Moving,

        /// <summary>Standing at the destination.</summary>
        Arrived,

        /// <summary>The pathfinder gave up. The villager is NOT at the destination.</summary>
        PathFailed
    }

    /// <summary>
    ///     The only place in the mod that calls <see cref="BaseAI.MoveTo" />.
    ///
    ///     This exists because MoveTo's return value is a trap: it returns true for
    ///     "stopped", not "arrived". Two of its four true-returning branches are
    ///     pathfinding failures:
    ///
    ///         if (DistanceXZ(point, transform.position) &lt; max(dist, num)) return true;  // arrived
    ///         if (!FindPath(point))  { StopMoving(); return true; }   // NO PATH
    ///         if (m_path.Count == 0) { StopMoving(); return true; }   // EMPTY PATH
    ///
    ///     Taking that at face value means a villager that cannot reach a container
    ///     reports its move step complete and the job carries on as if it were standing
    ///     there. Verified in game - see docs/spike-results.md.
    ///
    ///     Wrapping it once means no call site can get this wrong.
    /// </summary>
    internal static class VillagerMovement
    {
        internal static MoveResult MoveTowards(MonsterAI ai, Vector3 target, float stopDistance, bool run = false)
        {
            // MoveTo is protected on BaseAI; reachable because we build against
            // publicized assemblies. See docs/modding-basics.md.
            if (!ai.MoveTo(Time.fixedDeltaTime, target, stopDistance, run))
            {
                return MoveResult.Moving;
            }

            // It stopped. Whether that means success is ours to determine.
            return Utils.DistanceXZ(target, ai.transform.position) < stopDistance
                ? MoveResult.Arrived
                : MoveResult.PathFailed;
        }

        internal static void Stop(MonsterAI ai) => ai.StopMoving();
    }
}
