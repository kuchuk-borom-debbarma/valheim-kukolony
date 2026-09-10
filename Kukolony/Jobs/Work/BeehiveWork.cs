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
    internal sealed class BeehiveWork : IColonyWork
    {
        public ColonyJobType Type => ColonyJobType.CollectBeehives;
        public ToolRequirement RequiredTool => ToolRequirement.None;
        public StructureCapability TargetCapability => StructureCapability.Container;

        public WorkStep Next(WorkState state, WorkFacts facts) => TapThenGather.Next(state, facts);

        public JobResult ChooseSource(WorkContext context, out string activity) =>
            context.Phase == WorkState.Gathering
                ? ColonyJobEngine.ChooseLooseItem(context, out activity)
                : ColonyJobEngine.ChooseStation(context, StructureCapability.BeeHive, out activity);

        public JobResult Collect(WorkContext context, out string activity) =>
            context.Phase == WorkState.Retrieving
                ? ColonyJobEngine.PickUpTarget(context, out activity)
                : ColonyJobEngine.OperateTarget(context, StructureCapability.BeeHive, out activity);

        public JobResult ChooseTarget(WorkContext context, out string activity) =>
            ColonyJobEngine.ChooseContainer(context, TargetCapability, out activity);

        public JobResult Deliver(WorkContext context, out string activity) =>
            ColonyJobEngine.DepositCarried(context, out activity);
    }
}
