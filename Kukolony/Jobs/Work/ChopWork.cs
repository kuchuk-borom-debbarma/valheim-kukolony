using Kukolony.Colonies;
using Kukolony.Resources;

namespace Kukolony.Jobs.Work
{
    /// <summary>
    ///     Fells trees and cuts up the logs they leave.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The first job whose targets are not registered structures, and the first that
    ///         needs a tool: an axe in the villager's bag, put there by its outfit or by a
    ///         fetch-outfit job. Without one it refuses to start and says so, rather than
    ///         chopping by fiat.
    ///     </para>
    ///     <para>
    ///         <b>One cycle is one tree, or one log - not both.</b> Felling leaves a log where
    ///         the tree stood, and cutting that up is a separate pass on a separate object.
    ///         Logs are preferred when any are lying about, so a villager finishes what it
    ///         started before starting something new, and wood actually appears.
    ///     </para>
    ///     <para>
    ///         The wood ends up on the ground. Putting it away is hauling's job, which already
    ///         exists and already does it well - so this does not learn to carry.
    ///     </para>
    /// </remarks>
    internal sealed class ChopWork : ColonyWork
    {
        public override ColonyJobType Type => ColonyJobType.Chop;
        public override ToolRequirement RequiredTool => ToolRequirement.Axe;

        /// <summary>Trees, which nothing else has any reason to keep loaded.</summary>
        public override bool GathersFromTheWorld => true;

        /// <summary>
        ///     Only how far to range. There is no container at either end: trees are not
        ///     stored anywhere and the wood is left where it falls.
        /// </summary>
        public override JobSetting Reads => JobSetting.SearchRadius;

        public override WorkStep Next(WorkState state, WorkFacts facts) => WorkUntilGone.Next(state, facts);

        public override JobResult ChooseSource(WorkContext context, out string activity) =>
            ColonyJobEngine.ChooseResource(context, out activity);

        public override JobResult Collect(WorkContext context, out string activity) =>
            ColonyJobEngine.StrikeTarget(context, out activity);
    }
}
