using System.Collections.Generic;
using Kukolony.Colonies;
using UnityEngine;

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
        public abstract JobSetting Reads { get; }

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

        /// <summary>
        ///     Everything this job can be configured with, in the order it should be shown.
        /// </summary>
        /// <remarks>
        ///     The common settings are stated once here and narrowed by <see cref="Settings"/>,
        ///     so a job that does not read a source never offers one. A job with genuinely
        ///     different configuration overrides this and appends its own.
        /// </remarks>
        public virtual List<Settings.JobSettingSpec> Describe()
        {
            List<Settings.JobSettingSpec> specs = new List<Settings.JobSettingSpec>
            {
                new Settings.JobSettingSpec
                {
                    Label = "Items", Kind = Settings.SettingKind.Items,
                    Help = "What this job works with. Nothing chosen means anything.",
                    GetList = job => job.ItemFilters
                },
                new Settings.JobSettingSpec
                {
                    Label = "Repeats", Kind = Settings.SettingKind.Number,
                    Help = "How many times to run before the next job in the queue.",
                    Minimum = 1f, Maximum = 20f, Step = 1f,
                    GetNumber = job => job.Count,
                    SetNumber = (job, value) => job.Count = Mathf.RoundToInt(value)
                },
                new Settings.JobSettingSpec
                {
                    Label = "Stop at stock", Kind = Settings.SettingKind.Number,
                    Help = "Pause once the destination holds this many. Zero never pauses.",
                    Minimum = 0f, Maximum = 500f, Step = 5f,
                    GetNumber = job => job.StockLimit,
                    SetNumber = (job, value) => job.StockLimit = Mathf.RoundToInt(value)
                },
                new Settings.JobSettingSpec
                {
                    Label = "Which targets", Kind = Settings.SettingKind.Choice,
                    Help = "Every eligible structure, only the chosen, or all but those.",
                    Options = _ => new List<Settings.Option>
                    {
                        new Settings.Option(nameof(TargetMode.All), "All of them"),
                        new Settings.Option(nameof(TargetMode.Selected), "Only the chosen"),
                        new Settings.Option(nameof(TargetMode.Ignore), "All but the chosen")
                    },
                    GetChoice = job => job.Targets.ToString(),
                    SetChoice = (job, value) =>
                    {
                        if (System.Enum.TryParse(value, out TargetMode mode)) job.Targets = mode;
                    }
                },
                new Settings.JobSettingSpec
                {
                    Label = "Chosen structures", Kind = Settings.SettingKind.Structures,
                    Help = "Which ones the choice above refers to.",
                    Capability = TargetCapability,
                    GetReferences = job => job.SelectedStructures,
                    // Meaningless while every structure counts, and offering it there invites
                    // a player to pick some and watch it change nothing.
                    Visible = job => job.Targets != TargetMode.All
                },
                new Settings.JobSettingSpec
                {
                    Label = "Reserve targets", Kind = Settings.SettingKind.Toggle,
                    Help = "Stop two villagers working on the same thing.",
                    GetFlag = job => job.Reservations,
                    SetFlag = (job, value) => job.Reservations = value
                },
                new Settings.JobSettingSpec
                {
                    Label = "Stand within", Kind = Settings.SettingKind.Number,
                    Help = "How close a villager gets before it acts.",
                    Minimum = .5f, Maximum = 8f, Step = .5f,
                    Format = value => value.ToString("0.#") + "m",
                    GetNumber = job => job.StopDistance,
                    SetNumber = (job, value) => job.StopDistance = value
                }
            };

            if ((Reads & JobSetting.SearchRadius) != 0)
                specs.Add(new Settings.JobSettingSpec
                {
                    Label = "Search within", Kind = Settings.SettingKind.Number,
                    Help = "How far from the hearth to look.",
                    Minimum = 4f, Maximum = 128f, Step = 4f,
                    Format = value => Mathf.RoundToInt(value) + "m",
                    GetNumber = job => job.SearchRadius,
                    SetNumber = (job, value) => job.SearchRadius = value
                });

            if ((Reads & JobSetting.Source) != 0)
                specs.Add(new Settings.JobSettingSpec
                {
                    Label = "Take from", Kind = Settings.SettingKind.StructureRef,
                    Help = "A particular container, or leave it to pick one.",
                    Capability = StructureCapability.Container,
                    GetReference = job => job.Source,
                    SetReference = (job, value) => job.Source = value
                });

            if ((Reads & JobSetting.DropOnGround) != 0)
                specs.Add(new Settings.JobSettingSpec
                {
                    Label = "Leave it on the ground", Kind = Settings.SettingKind.Toggle,
                    Help = "Gather to a pile instead of putting it away.",
                    GetFlag = job => job.DropOnGround,
                    SetFlag = (job, value) => job.DropOnGround = value
                });

            if ((Reads & JobSetting.Destination) != 0)
                specs.Add(new Settings.JobSettingSpec
                {
                    Label = "Put it in", Kind = Settings.SettingKind.StructureRef,
                    Help = "A particular container, or let it pick one with room.",
                    Capability = StructureCapability.Container,
                    GetReference = job => job.Destination,
                    SetReference = (job, value) => job.Destination = value,
                    // There is no destination when the load goes on the ground.
                    Visible = job => !job.DropOnGround
                });

            return specs;
        }
    }
}
