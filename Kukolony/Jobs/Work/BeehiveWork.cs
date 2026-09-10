using Kukolony.Colonies;

namespace Kukolony.Jobs.Work
{
    /// <summary>
    ///     Taps beehives and stores the honey.
    /// </summary>
    /// <remarks>
    ///     The only job whose work produces a world drop rather than changing what a station
    ///     holds, so one cycle is two journeys: out to the hive, then out to whatever fell from
    ///     it. Both are collecting, and <see cref="WorkContext.Phase"/> is what tells them
    ///     apart - see <see cref="TapThenGather"/> for why they are separate states.
    /// </remarks>
    internal sealed class BeehiveWork : ColonyWork
    {
        public override ColonyJobType Type => ColonyJobType.CollectBeehives;

        /// <summary>
        ///     No source: the hive is chosen by capability, and what it produces is found on
        ///     the ground, which is what the radius bounds.
        /// </summary>
        public override JobSetting Settings =>
            JobSetting.Destination | JobSetting.SearchRadius | JobSetting.DropOnGround;

        public override WorkStep Next(WorkState state, WorkFacts facts) => TapThenGather.Next(state, facts);

        public override JobResult ChooseSource(WorkContext context, out string activity) =>
            context.Phase == WorkState.Gathering
                ? ColonyJobEngine.ChooseLooseItem(context, out activity)
                : ColonyJobEngine.ChooseStation(context, StructureCapability.BeeHive, out activity);

        public override JobResult Collect(WorkContext context, out string activity) =>
            context.Phase == WorkState.Retrieving
                ? ColonyJobEngine.PickUpTarget(context, out activity)
                : ColonyJobEngine.OperateTarget(context, StructureCapability.BeeHive, out activity);
    }
}
