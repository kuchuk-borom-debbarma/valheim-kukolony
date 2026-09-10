using Kukolony.Colonies;

namespace Kukolony.Jobs.Work
{
    /// <summary>
    ///     Moves items from one container to another.
    /// </summary>
    /// <remarks>
    ///     The same journey as hauling, differing only in where the goods come from, so it
    ///     replaces one step and inherits the rest.
    /// </remarks>
    internal sealed class TransferWork : ColonyWork
    {
        public override ColonyJobType Type => ColonyJobType.Transfer;

        /// <summary>Both ends are containers, and nothing is searched for on the ground.</summary>
        public override JobSetting Reads =>
            JobSetting.Source | JobSetting.Destination | JobSetting.DropOnGround;

        public override JobResult ChooseSource(WorkContext context, out string activity) =>
            ColonyJobEngine.ChooseStockedContainer(context, out activity);

        public override JobResult Collect(WorkContext context, out string activity) =>
            ColonyJobEngine.TakeFromTarget(context, out activity);
    }
}
