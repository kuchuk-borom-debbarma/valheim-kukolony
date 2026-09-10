using Kukolony.Colonies;
using UnityEngine;

namespace Kukolony.Jobs.Work
{
    /// <summary>
    ///     One job a villager can be given: what it is, what it needs before it can start, and
    ///     how it decides what to do next.
    /// </summary>
    /// <remarks>
    ///     A job owns its own sequence rather than being assembled from parts. Adding work is a
    ///     new file and a registry entry; no existing job changes shape, and no dispatch grows
    ///     another case.
    ///
    ///     The decision is deliberately separated from the doing. <see cref="Next"/> is pure and
    ///     testable in the Unity-free project; performing the chosen action is the engine's job
    ///     and touches the world.
    /// </remarks>
    internal interface IColonyWork
    {
        ColonyJobType Type { get; }

        /// <summary>
        ///     Damage a carried tool must be able to do before this job will run, or none when
        ///     the job needs no tool. Chopping wants an axe; hauling wants nothing.
        /// </summary>
        ToolRequirement RequiredTool { get; }

        /// <summary>Structures this job can deliver to or operate.</summary>
        StructureCapability TargetCapability { get; }

        /// <summary>
        ///     The settings this job actually reads, among those not every job does. The panel
        ///     shows only these, so a job cannot offer a control it ignores.
        /// </summary>
        JobSetting Reads { get; }

        /// <summary>What to do next, given where the villager got to and what it can see.</summary>
        WorkStep Next(WorkState state, WorkFacts facts);

        /// <summary>
        ///     Whether the villager is holding something this job still has work to do with.
        ///     Asked of the job rather than assumed, because "carrying" is not always "the bag
        ///     holds a wanted item": a villager that has fetched its boots is carrying them
        ///     until it puts them on, and still holds them afterwards.
        /// </summary>
        bool Carrying(WorkSubject subject);

        /// <summary>
        ///     Whether the load is used where the villager stands, so there is nothing to
        ///     choose and nowhere to walk to.
        /// </summary>
        bool DeliversInPlace(WorkSubject subject);

        /// <summary>
        ///     Whether this job works on the world itself - trees, ore - rather than on things
        ///     the colony has registered.
        /// </summary>
        /// <remarks>
        ///     Asked before any villager runs, because it decides whether zones kept open for a
        ///     colony bother instantiating scenery. A job that gathers and cannot see what it
        ///     gathers idles silently off-screen and works perfectly under observation, which is
        ///     the hardest kind of fault to find.
        /// </remarks>
        bool GathersFromTheWorld { get; }

        /// <summary>
        ///     Everything this job can be configured with, described rather than drawn. The
        ///     panel renders controls from this, so a job cannot offer a setting it ignores and
        ///     cannot hide one it reads.
        /// </summary>
        System.Collections.Generic.List<Settings.JobSettingSpec> Describe();

        /// <summary>
        ///     Chooses what to work on. Separated from delivery because a job that hauls looks
        ///     for an item on the ground while one that transfers looks in a container.
        /// </summary>
        JobResult ChooseSource(WorkContext context, out string activity);
        JobResult Collect(WorkContext context, out string activity);

        /// <summary>Chooses where the result goes, and puts it there.</summary>
        JobResult ChooseTarget(WorkContext context, out string activity);
        JobResult Deliver(WorkContext context, out string activity);
    }

    /// <summary>
    ///     A villager and the job it is doing, without anything about where it has got to.
    ///     Questions asked before the next step is chosen take this; questions asked while
    ///     performing that step take the fuller <see cref="WorkContext"/>.
    /// </summary>
    internal readonly struct WorkSubject
    {
        internal WorkSubject(Villagers.Villager villager, Inventory bag, Colony colony, ColonyJobConfig job)
        {
            Villager = villager;
            Bag = bag;
            Colony = colony;
            Job = job;
        }

        internal Villagers.Villager Villager { get; }
        internal Inventory Bag { get; }
        internal Colony Colony { get; }
        internal ColonyJobConfig Job { get; }
    }

    /// <summary>The kind of tool a job needs, expressed as the damage it must be able to do.</summary>
    internal enum ToolRequirement
    {
        None = 0,
        Axe = 1,
        Pickaxe = 2
    }

    /// <summary>
    ///     Everything a job needs to act, and nothing more. Handing over the engine would let a
    ///     job reach into scheduling; this keeps it to one villager, one colony, one config.
    /// </summary>
    internal readonly struct WorkContext
    {
        internal WorkContext(Villagers.Villager villager, MonsterAI ai, Inventory bag,
            Colony colony, ColonyJobConfig job, GameObject target, WorkState phase)
        {
            Phase = phase;
            Villager = villager;
            Ai = ai;
            Bag = bag;
            Colony = colony;
            Job = job;
            Target = target;
        }

        internal Villagers.Villager Villager { get; }
        internal MonsterAI Ai { get; }
        internal Inventory Bag { get; }
        internal Colony Colony { get; }
        internal ColonyJobConfig Job { get; }

        /// <summary>The chosen thing, or null when it has not loaded yet.</summary>
        internal GameObject Target { get; }

        /// <summary>
        ///     Where the villager was in its cycle when this action was chosen. A job whose
        ///     cycle does the same kind of thing twice - tapping a hive and then picking up
        ///     what fell out are both collecting - reads this to tell them apart. Passed in
        ///     rather than read back off the villager, so an executor that resets state
        ///     mid-action cannot change the answer underneath the job.
        /// </summary>
        internal WorkState Phase { get; }

        internal Villagers.VillagerState State => Villager.State;
    }
}
