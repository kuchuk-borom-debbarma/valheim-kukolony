using Kukolony.Colonies;

namespace Kukolony.Jobs.Work
{
    /// <summary>
    ///     Keeps a kind of station running: take its materials out of a container, carry them
    ///     over, and hand them in.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Fuelling a fire, loading a smelter, filling a cooking station and starting a
    ///         fermenter are one job with one setting changed - which structures count. They
    ///         differ in what the station does with what it is given, and the station itself
    ///         already decides that: the protocol is chosen by probing the target, so this
    ///         never learns what a smelter is.
    ///     </para>
    ///     <para>
    ///         So this is one class configured four ways rather than four near-identical files.
    ///     </para>
    /// </remarks>
    internal class StationWork : ColonyWork
    {
        internal StationWork(ColonyJobType type, StructureCapability capability)
        {
            Type = type;
            TargetCapability = capability;
        }

        public override ColonyJobType Type { get; }
        public override StructureCapability TargetCapability { get; }

        /// <summary>
        ///     No destination: the load goes into the station, which is chosen by capability
        ///     rather than by a container setting.
        /// </summary>
        public override JobSetting Settings => JobSetting.Source;

        public override JobResult ChooseSource(WorkContext context, out string activity) =>
            ColonyJobEngine.ChooseStockedContainer(context, out activity);

        public override JobResult Collect(WorkContext context, out string activity) =>
            ColonyJobEngine.TakeFromTarget(context, out activity);

        public override JobResult ChooseTarget(WorkContext context, out string activity) =>
            ColonyJobEngine.ChooseStation(context, TargetCapability, out activity);

        /// <summary>
        ///     Hands the carried item to the station. What that means is the station's business
        ///     - a smelter reads its own fuel definition to tell fuel from ore - so this does
        ///     not inspect the target.
        /// </summary>
        public override JobResult Deliver(WorkContext context, out string activity) =>
            ColonyJobEngine.OperateTarget(context, TargetCapability, out activity);
    }
}
