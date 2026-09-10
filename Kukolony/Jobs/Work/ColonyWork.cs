using Kukolony.Colonies;

namespace Kukolony.Jobs.Work
{
    /// <summary>
    ///     What every job does unless it says otherwise: fetch something and deliver it to a
    ///     container.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Almost every colony job is that shape, and the ones that are not differ in one or
    ///         two steps rather than in all of them. Stating the common answer once means a new
    ///         job is only the part that is actually new, and means a change to how delivery
    ///         works is a change in one place.
    ///     </para>
    ///     <para>
    ///         A job may still implement <see cref="IColonyWork"/> directly. This is a
    ///         convenience for the common shape, not a requirement.
    ///     </para>
    /// </remarks>
    internal abstract class ColonyWork : IColonyWork
    {
        public abstract ColonyJobType Type { get; }
        public abstract JobSetting Settings { get; }

        /// <summary>Containers, unless the job hands its load to something else.</summary>
        public virtual StructureCapability TargetCapability => StructureCapability.Container;

        /// <summary>Nothing, unless the work needs a tool to do it at all.</summary>
        public virtual ToolRequirement RequiredTool => ToolRequirement.None;

        /// <summary>
        ///     False: most work is done on containers and stations the colony registered, and
        ///     those are kept loaded already.
        /// </summary>
        public virtual bool GathersFromTheWorld => false;

        public virtual WorkStep Next(WorkState state, WorkFacts facts) => FetchAndDeliver.Next(state, facts);

        /// <summary>
        ///     The bag holds something this job still has work to do with. Usually that is
        ///     simply an item the job wants; work whose load is not consumed by delivering it
        ///     has to say so differently.
        /// </summary>
        public virtual bool Carrying(WorkSubject subject) =>
            ColonyJobEngine.HoldsWanted(subject.Bag, subject.Job.ItemFilters);

        /// <summary>
        ///     The load is used where the villager stands, so there is nothing to choose and
        ///     nowhere to walk. True for a job told to leave its load in a pile.
        /// </summary>
        public virtual bool DeliversInPlace(WorkSubject subject) =>
            subject.Job.DropOnGround && TargetCapability == StructureCapability.Container;

        public abstract JobResult ChooseSource(WorkContext context, out string activity);
        public abstract JobResult Collect(WorkContext context, out string activity);

        public virtual JobResult ChooseTarget(WorkContext context, out string activity) =>
            ColonyJobEngine.ChooseContainer(context, TargetCapability, out activity);

        public virtual JobResult Deliver(WorkContext context, out string activity) =>
            ColonyJobEngine.DepositCarried(context, out activity);
    }
}
