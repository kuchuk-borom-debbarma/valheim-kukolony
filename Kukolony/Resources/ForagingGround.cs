using System.Collections.Generic;
using Kukolony.Colonies;

namespace Kukolony.Resources
{
    /// <summary>
    ///     What a colony has to pick nearby.
    /// </summary>
    /// <remarks>
    ///     The sweep itself lives in <see cref="GroundSweep" />, which chopping and mining share:
    ///     the three differ by one predicate, and the anchoring rule they all depend on took
    ///     three rounds of review to get right. What is left here is the predicate and the name.
    /// </remarks>
    internal static class ForagingGround
    {
        private static readonly GroundSweep Sweep = new GroundSweep(
            () => { if (!Forageable.IsReady) Forageable.Rebuild(); },
            () => Forageable.IsReady,
            hash => Forageable.Of(hash) != ForageKind.None);

        /// <summary>How far from any of a Kolony's places foraging looks for work.</summary>
        internal static float SearchRadius => GroundSweep.SearchRadius;

        /// <summary>Dropped when a world unloads; a colony's identity does not survive one.</summary>
        internal static void Clear() => Sweep.Clear();

        /// <summary>
        ///     Drops the cache so a check can see a world it has just changed.
        /// </summary>
        internal static void ResetForTest() => Sweep.Clear();

        /// <summary>Everything loaded and pickable that this colony can see, as ZDOIDs.</summary>
        internal static List<ZDOID> Near(Colony colony) => Sweep.Near(colony);
    }
}
