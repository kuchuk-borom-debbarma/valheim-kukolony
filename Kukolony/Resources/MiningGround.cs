using System.Collections.Generic;
using Kukolony.Colonies;

namespace Kukolony.Resources
{
    /// <summary>
    ///     What a colony has to mine nearby.
    /// </summary>
    /// <remarks>
    ///     The sweep itself lives in <see cref="GroundSweep" />, which chopping shares. What is
    ///     here is the predicate and the name - and the reason mining needs its own cache rather
    ///     than filtering chopping's: the two look for different things, and a sweep that found
    ///     both would hand every chopping villager a list of rocks to skip past.
    /// </remarks>
    internal static class MiningGround
    {
        private static readonly GroundSweep Sweep = new GroundSweep(
            () => { if (!Mineable.IsReady) Mineable.Rebuild(); },
            () => Mineable.IsReady,
            hash => Mineable.Of(hash) != MineKind.None);

        /// <summary>How far from any of a Kolony's places mining looks for work.</summary>
        internal static float SearchRadius => GroundSweep.SearchRadius;

        /// <summary>Dropped when a world unloads; a colony's identity does not survive one.</summary>
        internal static void Clear() => Sweep.Clear();

        /// <summary>Drops the cache so a check can see a world it has just changed.</summary>
        internal static void ResetForTest() => Sweep.Clear();

        /// <summary>Everything loaded and minable that this colony can see, as ZDOIDs.</summary>
        internal static List<ZDOID> Near(Colony colony) => Sweep.Near(colony);
    }
}
