using System.Collections.Generic;
using Kukolony.Colonies;

namespace Kukolony.Resources
{
    /// <summary>
    ///     What a colony has to chop nearby.
    /// </summary>
    /// <remarks>
    ///     The sweep itself lives in <see cref="GroundSweep" />, which mining shares: the two
    ///     differ by one predicate, and the anchoring rule they both depend on took three rounds
    ///     of review to get right. What is left here is the predicate and the name.
    /// </remarks>
    internal static class ChoppingGround
    {
        private static readonly GroundSweep Sweep = new GroundSweep(
            () => { if (!Choppable.IsReady) Choppable.Rebuild(); },
            () => Choppable.IsReady,
            hash => Choppable.Of(hash) != ChopKind.None);

        /// <summary>How far from any of a Kolony's places chopping looks for work.</summary>
        internal static float SearchRadius => GroundSweep.SearchRadius;

        /// <summary>Dropped when a world unloads; a colony's identity does not survive one.</summary>
        internal static void Clear() => Sweep.Clear();

        /// <summary>
        ///     Drops the cache so a check can see a world it has just changed.
        /// </summary>
        /// <remarks>
        ///     A five-second cache is right for a settlement and wrong for a check that plants a
        ///     tree and immediately asks whether there is one.
        /// </remarks>
        internal static void ResetForTest() => Sweep.Clear();

        /// <summary>Everything loaded and choppable that this colony can see, as ZDOIDs.</summary>
        internal static List<ZDOID> Near(Colony colony) => Sweep.Near(colony);
    }
}
