using System.Collections.Generic;
using Kukolony.Core;

namespace Kukolony.Jobs
{
    /// <summary>Which work a job does. Growing this list is how the mod grows.</summary>
    /// <remarks>Persisted as an int, so append rather than reorder.</remarks>
    internal enum JobKind
    {
        /// <summary>Put things where they belong.</summary>
        Haul = 0
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

            List<string> items = Items ?? new List<string>();
            package.Write(items.Count);
            foreach (string item in items) package.Write(item ?? string.Empty);
        }

        internal static JobDefinition Read(ZPackage package)
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

            int count = package.ReadInt();
            if (count < 0 || count > 256)
            {
                Log.Warning($"[job] '{job.Name}' claims {count} items - ignoring them.");
                return job;
            }

            for (int i = 0; i < count; i++) job.Items.Add(package.ReadString());
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
                default: return string.Empty;
            }
        }
    }
}
