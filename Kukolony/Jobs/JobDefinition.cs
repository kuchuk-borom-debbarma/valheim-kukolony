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
        Chop = 1,

        /// <summary>Keep stations supplied, and take off what they have finished.</summary>
        Tend = 2,

        /// <summary>Make what the settlement's stations have been told to make.</summary>
        Craft = 3,

        /// <summary>Break rock for what is in it.</summary>
        Mine = 4
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

        /// <summary>
        ///     How many item or species names a job may carry.
        /// </summary>
        /// <remarks>
        ///     The same agreement <see cref="MaxAreas" /> keeps, for the same reason: both are
        ///     length-prefixed, and a count the reader refuses but the writer was willing to
        ///     produce loses the stream's position for every job after this one. Both pickers
        ///     are multi-select over a whole catalogue, so the limit is reachable by clicking.
        /// </remarks>
        internal const int MaxNames = 256;

        /// <summary>Whether it tidies containers as well as the ground.</summary>
        internal bool TidyContainers = true;

        /// <summary>Whether to fill the bag before delivering, or set out with the first load.</summary>
        internal bool FillBagFirst = true;

        /// <summary>
        ///     The places this job works, in the order it should try them, by durable token.
        ///     An empty list - or an empty token within it - means the whole settlement.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Tokens rather than addresses, for the reason every other reference here is:
        ///         loading renumbers every ZDOID, so an address alone points at whatever later
        ///         occupies that slot - and a job pointed at the wrong place is a villager
        ///         working somewhere nobody asked it to.
        ///     </para>
        ///     <para>
        ///         <b>Ordered, and tried in order.</b> A job works the first place that has
        ///         anything for it and only moves on when that place is done - so "the near
        ///         copse, then the far one" is a thing a player can say, and a woodcutter
        ///         clears one wood at a time instead of walking between two half-cut ones.
        ///         The alternative, pooling every area and taking whatever is nearest, cannot
        ///         express that and makes the order on screen a lie.
        ///     </para>
        /// </remarks>
        internal List<string> Areas = new List<string>();

        /// <summary>
        ///     How many areas a job may carry.
        /// </summary>
        /// <remarks>
        ///     Enforced when writing as well as when reading. The count is the first
        ///     length-prefixed field in the record, so a reader that refuses one the writer
        ///     was willing to produce does not merely lose that job - it loses its place in
        ///     the stream and every job after it decodes from the wrong offset.
        /// </remarks>
        internal const int MaxAreas = 64;

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

        /// <summary>
        ///     Which ores this job goes after. Empty means all of them.
        /// </summary>
        /// <remarks>
        ///     By what a deposit <em>yields</em> rather than by which rock it is, because "mine
        ///     tin" is the sentence a player has in mind and "mine the small rock that appears
        ///     in the Black Forest" is not. A deposit's drop table is public on its prefab, so
        ///     this is answerable with nothing loaded - and it covers a modded deposit that
        ///     drops copper without this mod ever hearing of it.
        /// </remarks>
        internal List<string> Ores = new List<string>();

        /// <summary>
        ///     Whether loose rock counts as work, as well as ore deposits.
        /// </summary>
        /// <remarks>
        ///     Off by default, and for the same reason <see cref="ChopUndergrowth" /> is: the
        ///     game offers no way to tell a boulder from a crate - DestructibleType is None,
        ///     Default, Tree and Character, with no Stone - so this admits a great deal of
        ///     scenery along with the stone somebody wanted. A settlement should not quietly
        ///     flatten its own surroundings because something had a Destructible on it.
        /// </remarks>
        internal bool MineBoulders;

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
            List<string> areas = Areas ?? new List<string>();
            if (areas.Count > MaxAreas)
            {
                Log.Warning($"[job] '{Name}' has {areas.Count} work areas - keeping the first {MaxAreas}.");
                areas = areas.GetRange(0, MaxAreas);
            }

            package.Write(areas.Count);
            foreach (string area in areas) package.Write(area ?? string.Empty);

            package.Write(WorkRadius);

            package.Write(ChopTrees);
            package.Write(ChopLogs);
            package.Write(ChopUndergrowth);
            package.Write(LeaveStanding);
            package.Write(StockItem ?? string.Empty);
            package.Write(StockTarget);

            List<string> items = Items ?? new List<string>();
            if (items.Count > MaxNames)
            {
                Log.Warning($"[job] '{Name}' handles {items.Count} items - keeping the first {MaxNames}.");
                items = items.GetRange(0, MaxNames);
            }

            package.Write(items.Count);
            foreach (string item in items) package.Write(item ?? string.Empty);

            List<string> species = Species ?? new List<string>();
            if (species.Count > MaxNames)
            {
                Log.Warning($"[job] '{Name}' names {species.Count} species - keeping the first {MaxNames}.");
                species = species.GetRange(0, MaxNames);
            }

            package.Write(species.Count);
            foreach (string name in species) package.Write(name ?? string.Empty);

            // Version 5 wrote four more fields here - which kinds of station, supply or
            // clear, fuel or material, and a list of named stations. They described stations
            // rather than work, so they moved to the stations themselves and version 6 stops
            // writing them. Read still consumes them from a version-5 blob, because they are
            // not the last thing in the stream: every job after this one is read from it.
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

                // Read in the order Write wrote them. An object initializer runs its assignments
                // top to bottom, so this is safe - but it is safe by a language guarantee rather
                // than by anything visible here, which is worth a line of warning to whoever adds
                // the next field.
                FillBagFirst = package.ReadBool()
            };

            // Version 3 and below kept a single place; version 4 keeps an ordered list. The
            // old field is read into the list rather than dropped, so a job somebody pointed
            // at their quarry last week still works the quarry.
            if (version >= 4)
            {
                int areas = package.ReadInt();
                if (areas < 0 || areas > MaxAreas)
                {
                    // Thrown, not skipped past, exactly as JobPreset does: every job after
                    // this one is read from the same stream, so a count this wrong means the
                    // position is already lost and carrying on decodes plausible nonsense.
                    // The writer caps at the same number, so reaching here means the blob is
                    // corrupt rather than merely old.
                    throw new System.IO.InvalidDataException(
                        $"job '{job.Name}' claims {areas} work areas");
                }

                for (int i = 0; i < areas; i++) job.Areas.Add(package.ReadString());
            }
            else
            {
                string single = package.ReadString();
                if (!string.IsNullOrEmpty(single)) job.Areas.Add(single);
            }

            job.WorkRadius = package.ReadSingle();

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
            if (count < 0 || count > MaxNames)
            {
                // Thrown rather than skipped, as the areas count is: the position in the
                // stream is already lost, and reading the next job from the middle of this
                // one decodes item names as job ids.
                throw new System.IO.InvalidDataException($"job '{job.Name}' claims {count} items");
            }

            for (int i = 0; i < count; i++) job.Items.Add(package.ReadString());

            if (version < 3) return job;

            int species = package.ReadInt();
            if (species < 0 || species > MaxNames)
            {
                throw new System.IO.InvalidDataException($"job '{job.Name}' claims {species} species");
            }

            for (int i = 0; i < species; i++) job.Species.Add(package.ReadString());

            // Version 5 alone carried the tending settings that have since moved onto the
            // stations. Earlier versions never wrote them and version 6 stopped, so this reads
            // them for exactly one layout - and reads them to *discard* them, because the bytes
            // must be consumed either way. Every job after this one decodes from the same
            // stream, and a reader that skipped four fields would decode the next job from the
            // middle of this one. The same reason retired capability bits are masked on read
            // rather than left out of the format.
            if (version != 5) return job;

            package.ReadInt();
            package.ReadInt();
            package.ReadInt();

            int stations = package.ReadInt();
            if (stations < 0 || stations > MaxAreas)
            {
                throw new System.IO.InvalidDataException($"job '{job.Name}' claims {stations} stations");
            }

            for (int i = 0; i < stations; i++) package.ReadString();

            // Said rather than dropped in silence. These settings had real effects - a job set
            // to clear only, or to two named kilns, becomes a job that supplies every station in
            // its work area - and the stations they moved to start at their permissive defaults.
            // A player who is told can go and set them; one who is not finds out by watching ore
            // disappear into a furnace they had excluded.
            if (job.Kind == JobKind.Tend)
            {
                Log.Warning($"[job] '{job.Name}' was set up before tending moved onto the stations. " +
                            "Its station settings were dropped - open each station to set them again.");
            }

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
                case JobKind.Tend: return "Tend";
                case JobKind.Craft: return "Craft";
                case JobKind.Mine: return "Mine";
                default: return string.Empty;
            }
        }
    }
}
