using Kukolony.Colonies;
using Kukolony.Villagers;
using Kukolony.Villagers.Navigation;
using UnityEngine;

namespace Kukolony.Jobs
{
    /// <summary>
    ///     Going and getting the tool the work needs.
    /// </summary>
    /// <remarks>
    ///     The walk, the chest and the taking all live in <see cref="Errand" />, which sowing
    ///     shares: the two differ by one question - what counts as the thing - and everything
    ///     else was general the day this was written. What is left here is that question, and the
    ///     rule that it must be the same one the job will ask of the bag afterwards.
    /// </remarks>
    internal static class ToolErrand
    {
        /// <summary>
        ///     Fetches a tool of this kind, or answers null when there is nothing to fetch.
        /// </summary>
        /// <returns>
        ///     Null when the settlement has no tool this villager may take - which is not a
        ///     failure, and is the state the job's own "no axe" answer already describes.
        /// </returns>
        internal static JobResult? Run(Villager villager, Colony colony, Container bag,
            VillagerWalk walk, VillagerState state, float deltaTime, ToolKind kind,
            out string activity)
        {
            string what = kind == ToolKind.Axe ? "an axe" : "a pickaxe";

            // The same predicate the job uses to find the tool in the bag afterwards. Two
            // different questions here would be a villager fetching an axe for ever and never
            // seeing the one it is carrying.
            return Errand.Fetch(villager, colony, bag, walk, state, deltaTime,
                inside => VillagerTool.Best(inside, kind), what, out activity);
        }
    }
}
