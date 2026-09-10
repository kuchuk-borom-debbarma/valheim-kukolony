using Kukolony.Colonies;

namespace Kukolony.Jobs.Work
{
    /// <summary>
    ///     Collects items left on the ground and puts them in a container.
    /// </summary>
    internal sealed class HaulWork : IColonyWork
    {
        public ColonyJobType Type => ColonyJobType.HaulLoose;
        public ToolRequirement RequiredTool => ToolRequirement.None;
        public StructureCapability TargetCapability => StructureCapability.Container;

        public WorkStep Next(WorkState state, WorkFacts facts) => FetchAndDeliver.Next(state, facts);

        public JobResult ChooseSource(WorkContext context, out string activity) =>
            ColonyJobEngine.ChooseLooseItem(context, out activity);

        public JobResult Collect(WorkContext context, out string activity) =>
            ColonyJobEngine.PickUpTarget(context, out activity);

        public JobResult ChooseTarget(WorkContext context, out string activity) =>
            ColonyJobEngine.ChooseContainer(context, TargetCapability, out activity);

        public JobResult Deliver(WorkContext context, out string activity) =>
            ColonyJobEngine.DepositCarried(context, out activity);
    }
}
