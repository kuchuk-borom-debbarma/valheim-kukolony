using System.Collections.Generic;
using Kukolony.Colonies;

namespace Kukolony.Resources
{
    /// <summary>
    ///     What a colony has standing that wants mending.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The sweep itself lives in <see cref="GroundSweep" />, which chopping, mining and
    ///         foraging share. This is the first caller to use its second predicate: the others
    ///         ask about a <em>kind</em> of thing, which a prefab hash settles, while a wall is
    ///         worth walking to or not depending on what has happened to that particular wall.
    ///     </para>
    ///     <para>
    ///         <b>The widest net in the mod, and the cheapest test.</b> A settled base is
    ///         thousands of pieces where a forest is dozens of trees, so the per-candidate cost
    ///         matters here more than anywhere else - and it is one float read off a record that
    ///         is already in hand.
    ///     </para>
    ///     <para>
    ///         <b>The threshold is a static rather than a parameter</b>, which is not how this
    ///         would be written if the cache were per job. It is per <em>colony</em>: one list
    ///         serves every villager, so the sweep cannot hold one villager's setting. Set before
    ///         each sweep by the job, which is the only caller, and the default is what a job that
    ///         never set it would have used.
    ///     </para>
    /// </remarks>
    internal static class RepairGround
    {
        /// <summary>
        ///     How worn something must be before the sweep bothers to return it.
        /// </summary>
        /// <remarks>
        ///     Deliberately the loosest any job is likely to ask for rather than the tightest: the
        ///     sweep is shared, so it must return a superset of what any one job wants and let the
        ///     job narrow it. A sweep tuned to one villager's threshold would hide work from a
        ///     second villager whose threshold was looser.
        /// </remarks>
        internal static float Below = 1f;

        private static readonly GroundSweep Sweep = new GroundSweep(
            () => { if (!Repairable.IsReady) Repairable.Rebuild(); },
            () => Repairable.IsReady,
            hash => Repairable.Of(hash) != null,
            zdo => Repairable.Worn(zdo, Below));

        /// <summary>How far from any of a Kolony's places mending looks for work.</summary>
        internal static float SearchRadius => GroundSweep.SearchRadius;

        /// <summary>Dropped when a world unloads; a colony's identity does not survive one.</summary>
        internal static void Clear() => Sweep.Clear();

        /// <summary>Drops the cache so a check can see a world it has just changed.</summary>
        internal static void ResetForTest() => Sweep.Clear();

        /// <summary>Everything loaded and worn that this colony can see, as ZDOIDs.</summary>
        internal static List<ZDOID> Near(Colony colony) => Sweep.Near(colony);
    }
}
