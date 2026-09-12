using System.Collections.Generic;
using Kukolony.Core;

namespace Kukolony.Jobs
{
    /// <summary>Which work a job does. Growing this list is how the mod grows.</summary>
    /// <remarks>Persisted as an int, so append rather than reorder.</remarks>
    internal enum JobKind
    {
        /// <summary>Put things where they belong.</summary>
        Haul = 0,

        /// <summary>Cut down what an axe can cut down.</summary>
        Chop = 1
    }

    /// <summary>
    ///     A named piece of work a colony offers, which villagers can be queued onto.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Owned by the colony rather than by a villager, so a settlement of a hundred is
    ///         not a hundred configurations. A villager's queue holds ids, and the definition
    ///         they point at is shared — editing it changes the work for everyone doing it,
    ///         which is the point.
    ///     </para>
    ///     <para>
    ///         A definition is also a preset: naming one and pointing several villagers at it is
    ///         what presets are, so there is no second concept and no second store.
    ///     </para>
    /// </remarks>
    internal sealed class JobDefinition
    {
        /// <summary>Stable identity, so renaming a job does not orphan every queue.</summary>
        internal string Id = string.Empty;

        internal string Name = string.Empty;

        internal JobKind Kind = JobKind.Haul;

        /// <summary>How many times a villager runs this before the queue advances.</summary>
        internal int Repeat = 1;

        /// <summary>Items this job handles. Empty means everything.</summary>
        internal List<string> Items = new List<string>();

        /// <summary>Whether it tidies containers as well as the ground.</summary>
        internal bool TidyContainers = true;

        /// <summary>Whether to fill the bag before delivering, or set out with the first load.</summary>
        internal bool FillBagFirst = true;

        /// <summary>
        ///     The registered structure this job works around, by durable token. Empty means the
        ///     whole settlement.
        /// </summary>
        /// <remarks>
        ///     A token rather than an address, for the reason every other reference here is:
        ///     loading renumbers every ZDOID, so an address alone points at whatever later
        ///     occupies that slot - and a job pointed at the wrong place is a villager working
        ///     somewhere nobody asked it to.
        /// </remarks>
        internal string WorkArea = string.Empty;

        /// <summary>How far that reaches. Zero means the default.</summary>
        internal float WorkRadius;

        /// <summary>
        ///     Which choppable things this job takes: standing trees, fallen logs, undergrowth.
        /// </summary>
        /// <remarks>
        ///     Trees and logs by default; stumps and bushes are opt-in. Classification admits
        ///     anything an axe demonstrably bites, and that is a wide net to point a villager
        ///     at without being asked - a settlement should not quietly flatten the scenery
        ///     because something had a Destructible on it.
        /// </remarks>
        internal bool ChopTrees = true;

        internal bool ChopLogs = true;

        internal bool ChopUndergrowth;

        /// <summary>
        ///     Tree prefabs this job will fell. Empty means all of them.
        /// </summary>
        /// <remarks>
        ///     The same shape as <see cref="Items" />, including that empty means everything.
        ///     This is how a player says "leave the birches" - an answer the mod has no
        ///     business choosing for them.
        /// </remarks>
        internal List<string> Species = new List<string>();

        /// <summary>
        ///     How many standing trees to leave in the work area. Zero means take them all.
        /// </summary>
        /// <remarks>
        ///     The rule against clear-cutting, and it is free: the scan already counts what it
        ///     found, so below the threshold there is simply no work. It answers a different
        ///     question from <see cref="StockTarget" /> and neither substitutes for the other -
        ///     a full woodshed beside a bare hillside is the failure this one prevents.
        /// </remarks>
        internal int LeaveStanding;

        /// <summary>What the settlement is gathering, for the purpose of knowing when to stop.</summary>
        internal string StockItem = string.Empty;

        /// <summary>
        ///     How much of it is enough. Zero means never stop.
        /// </summary>
        /// <remarks>
        ///     The terminus this job otherwise lacks. Hauling stops when nothing is misplaced,
        ///     which is visible and self-limiting; a forest has no such point, and a woodcutter
        ///     without a stopping rule strips the map while looking correct the whole time.
        /// </remarks>
        internal int StockTarget;

        internal void Write(ZPackage package)
        {
            package.Write(Id ?? string.Empty);
            package.Write(Name ?? string.Empty);
            package.Write((int)Kind);
            package.Write(Repeat);
            package.Write(TidyContainers);
            package.Write(FillBagFirst);
            package.Write(WorkArea ?? string.Empty);
            package.Write(WorkRadius);

            package.Write(ChopTrees);
            package.Write(ChopLogs);
            package.Write(ChopUndergrowth);
            package.Write(LeaveStanding);
            package.Write(StockItem ?? string.Empty);
            package.Write(StockTarget);

            List<string> items = Items ?? new List<string>();
            package.Write(items.Count);
            foreach (string item in items) package.Write(item ?? string.Empty);

            List<string> species = Species ?? new List<string>();
            package.Write(species.Count);
            foreach (string name in species) package.Write(name ?? string.Empty);
        }

        /// <summary>
        ///     Reads a record written by <see cref="Write" />, or by the build before it.
        /// </summary>
        /// <remarks>
        ///     <paramref name="version" /> is the colony's job-blob version rather than one of
        ///     this record's own. Version 2 stops after the item list and knows nothing of the
        ///     chopping settings, which simply keep their defaults.
        /// </remarks>
        internal static JobDefinition Read(ZPackage package, int version)
        {
            JobDefinition job = new JobDefinition
            {
                Id = package.ReadString(),
                Name = package.ReadString(),
                Kind = (JobKind)package.ReadInt(),
                Repeat = package.ReadInt(),
                TidyContainers = package.ReadBool(),
                FillBagFirst = package.ReadBool(),

                // Read in the order Write wrote them. An object initializer runs its assignments
                // top to bottom, so this is safe - but it is safe by a language guarantee rather
                // than by anything visible here, which is worth a line of warning to whoever adds
                // the next field.
                WorkArea = package.ReadString(),
                WorkRadius = package.ReadSingle()
            };

            if (version >= 3)
            {
                job.ChopTrees = package.ReadBool();
                job.ChopLogs = package.ReadBool();
                job.ChopUndergrowth = package.ReadBool();
                job.LeaveStanding = package.ReadInt();
                job.StockItem = package.ReadString();
                job.StockTarget = package.ReadInt();
            }

            int count = package.ReadInt();
            if (count < 0 || count > 256)
            {
                Log.Warning($"[job] '{job.Name}' claims {count} items - ignoring them.");
                return job;
            }

            for (int i = 0; i < count; i++) job.Items.Add(package.ReadString());

            if (version < 3) return job;

            int species = package.ReadInt();
            if (species < 0 || species > 256)
            {
                Log.Warning($"[job] '{job.Name}' claims {species} species - ignoring them.");
                return job;
            }

            for (int i = 0; i < species; i++) job.Species.Add(package.ReadString());
            return job;
        }

        /// <summary>
        ///     What this kind of work is called, for a player.
        /// </summary>
        /// <remarks>
        ///     Explicit, with no fallback that invents a plausible name. The previous catalogue
        ///     had one, and a newly added job reached the screen labelled "Collect beehives" —
        ///     convincing, wrong, and invisible to a check that counted job types rather than
        ///     asking whether each was named. Unnamed work returns empty and fails loudly.
        /// </remarks>
        internal static string Describe(JobKind kind)
        {
            switch (kind)
            {
                case JobKind.Haul: return "Haul";
                case JobKind.Chop: return "Chop";
                default: return string.Empty;
            }
        }
    }
}
