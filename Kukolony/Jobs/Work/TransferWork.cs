using Kukolony.Colonies;

namespace Kukolony.Jobs.Work
{
    /// <summary>
    ///     Moves items from one container to another.
    /// </summary>
    /// <remarks>
    ///     The same journey as hauling, differing only in where the goods come from, so it
    ///     shares the sequence and replaces one step.
    /// </remarks>
    internal sealed class TransferWork : IColonyWork
    {
        public ColonyJobType Type => ColonyJobType.Transfer;
        public ToolRequirement RequiredTool => ToolRequirement.None;
        public StructureCapability TargetCapability => StructureCapability.Container;

        /// <summary>Both ends are containers, and nothing is searched for on the ground.</summary>
        public JobSetting Settings =>
            JobSetting.Source | JobSetting.Destination | JobSetting.DropOnGround;

        public WorkStep Next(WorkState state, WorkFacts facts) => FetchAndDeliver.Next(state, facts);

        public JobResult ChooseSource(WorkContext context, out string activity) =>
            ColonyJobEngine.ChooseStockedContainer(context, out activity);

        public JobResult Collect(WorkContext context, out string activity) =>
            ColonyJobEngine.TakeFromTarget(context, out activity);

        public JobResult ChooseTarget(WorkContext context, out string activity) =>
            ColonyJobEngine.ChooseContainer(context, TargetCapability, out activity);

        public JobResult Deliver(WorkContext context, out string activity) =>
            ColonyJobEngine.DepositCarried(context, out activity);
    }
}
