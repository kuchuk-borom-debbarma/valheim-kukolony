using Kukolony.Colonies;

namespace Kukolony.Jobs.Work
{
    /// <summary>
    ///     Collects items left on the ground and puts them in a container.
    /// </summary>
    internal sealed class HaulWork : ColonyWork
    {
        public override ColonyJobType Type => ColonyJobType.HaulLoose;

        /// <summary>No source: hauling looks on the ground, which is what the radius bounds.</summary>
        public override JobSetting Settings =>
            JobSetting.Destination | JobSetting.SearchRadius | JobSetting.DropOnGround;

        public override JobResult ChooseSource(WorkContext context, out string activity) =>
            ColonyJobEngine.ChooseLooseItem(context, out activity);

        public override JobResult Collect(WorkContext context, out string activity) =>
            ColonyJobEngine.PickUpTarget(context, out activity);
    }
}
