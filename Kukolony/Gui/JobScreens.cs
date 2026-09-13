using System.Collections.Generic;
using Kukolony.Colonies;
using Kukolony.Core;
using Kukolony.Jobs;
using UnityEngine;

namespace Kukolony.Gui
{
    /// <summary>
    ///     The jobs a settlement knows how to do, and what each of them is for.
    /// </summary>
    /// <remarks>
    ///     Jobs belong to the colony rather than to a villager: they are named, shared, and
    ///     changed only when a player edits them. A villager carries the queue - which jobs it
    ///     works and in what order - because that changes per villager and must not rewrite the
    ///     colony's record every time somebody is reassigned.
    /// </remarks>
    internal sealed class JobListScreen : ScreenView
    {
        /// <summary>
        ///     The one rendering of "this job works everywhere". The picker offers it and the
        ///     job row reads it back, and the two being separate literals meant an edit to one
        ///     made the selection look like it had not taken.
        /// </summary>
        internal const string WholeKolony = "the whole Kolony";

        internal override string Title => "Jobs";

        internal override bool StillValid(ColonyScreen host) => host.Colony != null;

        internal override void Build(ColonyScreen host, Column column)
        {
            Colony colony = host.Colony;
            List<JobDefinition> jobs = colony.State.GetJobs();

            if (jobs.Count == 0 && column.TryRow(out Row empty))
            {
                Widgets.Label(empty, "This Kolony has no jobs yet.", Color.gray);
            }

            // Decoded once for the whole list rather than once a row, and not at all for a
            // settlement with no jobs - GetStructures base64-decodes a package and resolves
            // a durable reference per record, which is not a thing to pay to draw nothing.
            List<StructureRecord> records = jobs.Count > 0
                ? colony.State.GetStructures()
                : new List<StructureRecord>();

            foreach (JobDefinition job in jobs)
            {
                if (!column.TryRow(out Row row)) continue;

                string id = job.Id;

                // Held to the cell like everything else on this row. A job name is refused
                // only when empty, so "Haul everything to the shed by the docks" was free to
                // run across the two columns beside it.
                Widgets.Caption(row, Fit(job.Name, 22), 220f);
                Widgets.Caption(row, JobDefinition.Describe(job.Kind), 150f, Color.gray);
                Widgets.Caption(row, Where(records, job, RowBudget), 190f, Color.gray);
                Widgets.Button(row, "Open", 110f, () => host.Push(new JobDetailScreen(id)));
            }

            // Presets live next to jobs because that is where a player is when they realise
            // they are about to configure the same thing twenty times.
            if (column.TryRow(out Row presets))
            {
                Widgets.Caption(presets, "Work presets");
                Widgets.Caption(presets, colony.State.GetPresets().Count.ToString(), 80f);
                Widgets.Button(presets, "Manage", 160f, () => host.Push(new PresetListScreen()));
            }

            if (!column.TryRow(out Row adding)) return;

            // One button per kind, because the kind cannot be changed afterwards - a job's
            // settings only mean anything for the work it does, and letting a configured haul
            // job become a chop job would silently reinterpret every one of them. Naming them
            // here is also the only way a new kind becomes reachable at all: a single "New
            // job" button that hardcoded Haul is exactly how the previous kind stayed
            // unreachable after being added to the enum.
            Widgets.Caption(adding, "Add a job", 190f);
            Add(host, adding, JobKind.Haul);
            Add(host, adding, JobKind.Chop);
        }

        private static void Add(ColonyScreen host, Row row, JobKind kind)
        {
            string label = JobDefinition.Describe(kind);
            if (string.IsNullOrEmpty(label)) return;

            Widgets.Button(row, label, 170f, () =>
            {
                Colony colony = host.Colony;
                if (colony == null) return;

                List<JobDefinition> next = colony.State.GetJobs();
                JobDefinition fresh = new JobDefinition
                {
                    Id = System.Guid.NewGuid().ToString("N"),
                    Name = label,
                    Kind = kind,
                    Repeat = 4
                };

                next.Add(fresh);
                colony.State.SetJobs(next);
                Report.Say($"Added '{fresh.Name}'.");
                host.Refresh();
            });
        }

        /// <summary>
        ///     How much room a job row's "where" cell has, in characters.
        /// </summary>
        /// <remarks>
        ///     The cell is 190 px and the labels overflow rather than clip - they are drawn
        ///     over whatever is next in the row - so the string has to be held to a length
        ///     that fits. Measured against the longest text the old single-place row was ever
        ///     asked to draw, which fitted.
        /// </remarks>
        private const int RowBudget = 19;

        /// <summary>How much room the job screen's own choice button has.</summary>
        private const int ChoiceBudget = 24;

        /// <summary>
        ///     The villager job picker's share of its cell, which it shares with a job name.
        /// </summary>
        internal const int PickerBudget = 18;

        /// <summary>What a name is cut to when it shares a cell with something else.</summary>
        internal const int NameBudget = 18;

        /// <summary>A short answer to "where does this happen", for a screen showing one job.</summary>
        /// <remarks>
        ///     Decodes the structure registry itself. Callers listing several jobs must hoist
        ///     that out and use the overload - GetStructures base64-decodes a package and
        ///     allocates every record in it, so a forty-job screen decoded it forty times per
        ///     refresh, and a picker refreshes on every click.
        /// </remarks>
        internal static string Where(Colony colony, JobDefinition job, int budget = ChoiceBudget) =>
            Where(colony.State.GetStructures(), job, budget);

        /// <summary>
        ///     The same answer, against a registry the caller has already decoded.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>One name, then counts.</b> Naming the first two places and joining them
        ///         with "then" was tried, to make the ordering visible in the summary itself,
        ///         and it cost four review rounds: "the whole Kolony" is sixteen characters
        ///         and " then " is six, so in a 190 px cell the second name never got a
        ///         character and every job starting at the settlement drew the same truncated
        ///         string - while the two-name arithmetic overran its budget whenever a
        ///         warning was present. The ordering is stated where there is room for it:
        ///         the picker lists the places in order and says so.
        ///     </para>
        ///     <para>
        ///         <b>Only the first place's name is ever cut.</b> The counts behind it are
        ///         short by construction - at most fourteen characters for the largest list
        ///         a job may hold - so they survive every budget here, which is what trimming
        ///         from the right had been quietly deleting. The missing-place marker sits in
        ///         the name's position and is trimmed with it, but it is six characters
        ///         against a floor of four, so it degrades to "(go…" rather than vanishing.
        ///     </para>
        /// </remarks>
        internal static string Where(List<StructureRecord> records, JobDefinition job, int budget)
        {
            budget = Mathf.Max(budget, 8);

            List<string> tokens = job.Areas ?? new List<string>();
            if (tokens.Count == 0) return Fit(WholeKolony, budget);

            // Counted over the places behind the "+N", because the first one says so itself.
            int gone = 0;
            for (int i = 1; i < tokens.Count; i++)
            {
                if (!TryPlaceName(records, tokens[i], out string _)) gone++;
            }

            int hidden = tokens.Count - 1;
            string tail = hidden > 0 ? $" +{hidden}" : string.Empty;
            if (gone > 0) tail += $" ({gone} gone)";

            // The named place, or the marker. The marker is short on purpose: the sentence it
            // replaced was twenty characters, which is the whole of the narrowest cell, so
            // the one row that most needed to say "this is broken" was the one that could not
            // fit the words.
            string first = TryPlaceName(records, tokens[0], out string name) ? name : GonePlace;

            // What is left after the parts that must survive. At least one character, so a
            // budget swallowed whole by counts still shows that a place was named.
            return Fit(first, Mathf.Max(1, budget - tail.Length)) + tail;
        }

        /// <summary>
        ///     Holds a line to a cell it is drawn in.
        /// </summary>
        /// <remarks>
        ///     Characters rather than pixels, which is a proxy - but the alternative is
        ///     measuring text from a static with no font to hand, and a proxy that keeps the
        ///     string near the length that already fitted beats a label drawn across the
        ///     button beside it.
        /// </remarks>
        internal static string Fit(string text, int budget)
        {
            if (text == null || text.Length <= budget) return text;

            // A budget too small to hold anything still has to produce something, and an
            // ellipsis alone is a truthful "there was more here" - failing open and returning
            // the whole string is the one answer that cannot be right, because the caller
            // asked precisely because it has no room.
            return budget <= 1 ? "\u2026" : text.Substring(0, budget - 1) + "\u2026";
        }

        /// <summary>
        ///     The area was destroyed or unregistered.
        /// </summary>
        /// <remarks>
        ///     Six characters, not a sentence. The job still runs - it falls back to the
        ///     settlement - and a player should know why it suddenly ranges further than they
        ///     told it to, which means this has to reach them in a cell nineteen characters
        ///     wide. "A place that is gone" did not, and was cut to "a place that is go".
        /// </remarks>
        private const string GonePlace = "(gone)";

        private static bool TryPlaceName(List<StructureRecord> records, string token, out string name)
        {
            name = WholeKolony;
            if (string.IsNullOrEmpty(token)) return true;

            foreach (StructureRecord record in records)
            {
                if (record.PersistentId != token) continue;

                name = record.Name;
                return true;
            }

            return false;
        }
    }

    /// <summary>One job: what it does, what it handles, and where.</summary>
    internal sealed class JobDetailScreen : ScreenView
    {
        private readonly string _id;

        internal JobDetailScreen(string id) => _id = id;

        internal override string Title => "Job";

        internal override bool StillValid(ColonyScreen host) =>
            host.Colony != null && Find(host.Colony) != null;

        internal override void Build(ColonyScreen host, Column column)
        {
            Colony colony = host.Colony;
            JobDefinition job = Find(colony);
            if (job == null) return;

            if (column.TryRow(out Row name))
            {
                // Refused rather than stored, as the structure rename beside it does. A job
                // has no unnamed fallback, so an empty name draws a blank row and leaves the
                // job identifiable only by its kind and its position in the list.
                Widgets.Text(name, "Name", job.Name, value =>
                {
                    string trimmed = (value ?? string.Empty).Trim();
                    if (trimmed.Length == 0)
                    {
                        Report.Say("A job needs a name.");
                        host.Refresh();
                        return;
                    }

                    Edit(host, j => j.Name = trimmed);
                    host.Refresh();
                });
            }

            if (column.TryRow(out Row kind))
            {
                Widgets.Caption(kind, "What it does");
                Widgets.Label(kind, JobDefinition.Describe(job.Kind));
            }

            if (column.TryRow(out Row repeat))
            {
                Widgets.Number(repeat, "Times before the next job", job.Repeat, 1f, 50f, 1f,
                    value => ((int)value).ToString(),
                    value => { Edit(host, j => j.Repeat = (int)value); host.Refresh(); });
            }

            // Each kind shows only the settings it reads. A job must not offer a setting it
            // ignores: FillBagFirst was persisted, shown here as a toggle, and read by nothing
            // for long enough that the trap got written down and then walked into anyway - so
            // the gate is here, at the one place a setting becomes visible.
            if (job.Kind == JobKind.Haul) BuildHaul(host, column, job);
            if (job.Kind == JobKind.Chop) BuildChop(host, column, job);

            // Where it works. The Kolony itself and its work-area flags, and nothing else:
            // any registered thing can still serve as a centre, but offering every chest and
            // kiln in the settlement made a list nobody could find an outpost in. The empty
            // choice is the settlement itself - a real answer rather than an unset one, so it
            // is offered first and named.
            //
            // Several may be chosen, and the order is kept: a job works the first place with
            // anything to do and moves on when it is done.
            if (column.TryRow(out Row where))
            {
                // One job on this screen, so the registry decode Where does for itself is
                // not worth hoisting. It is not the only one: the reach row below asks each
                // named place how far it reaches, and each of those decodes the registry
                // again. Fine for one screen at the rate a person presses buttons, and worth
                // knowing before anything here is put in a loop.
                Widgets.Choice(where, "Where it works", JobListScreen.Where(colony, job),
                    () => host.Push(new PickerScreen("Where it works",
                        filter => Places(colony, job, filter),
                        // An unpointed job works the settlement, so that is what the picker
                        // opens showing as chosen - the alternative shows nothing selected
                        // for a job that is plainly working somewhere.
                        job.Areas.Count == 0 ? new List<string> { string.Empty } : job.Areas, true,
                        chosen =>
                        {
                            Edit(host, j => j.Areas = chosen);
                            host.Refresh();
                        })), 260f);
            }

            // Asked of the first place the job names, never of the whole list. The reach is
            // one number applied to every named place, and the settlement's own reach is set
            // on the hearth - so a job working "the Kolony, then the north copse" must state
            // the copse's reach here, not the Kolony's, which is what reading the first entry
            // of the list would have given.
            JobDefinition place = NamedPlace(job);

            if (place != null && column.TryRow(out Row radius))
            {
                // Asked of the job itself, so the row states the radius actually in force
                // rather than re-deriving one. For chopping that is already held to the
                // search radius, which is why the bound below can never contradict it.
                float shown = Effective(colony, place);

                // The bound comes from the place, never from the current value.
                //
                // Taking it from what is displayed makes the row a one-way ratchet: on a
                // two-hundred-metre flag a single tap down writes 196, and the bound then
                // follows that number, so the last four metres can never be nudged back -
                // the job works a smaller area than the flag it is pointed at, permanently
                // and with no way to say otherwise. Deriving it from the place instead keeps
                // the ceiling still while the value moves under it. The other way round -
                // a bound below what is shown - collapses the job to the bound on the first
                // tap, which is the failure this replaced.
                // Both terms, because they guard opposite failures and each was tried alone.
                // The place-derived term stops the ratchet: a bound that follows the current
                // value can only ever shrink, so one tap down on a wide flag loses the rest
                // for good. The shown term stops the collapse: a bound under what is
                // displayed makes the first tap jump the value *down* to it, which is what
                // happens the moment a flag is shrunk or the work area re-pointed after a
                // reach was set against the old one.
                // Over every named place, not just the one shown. The reach is one number
                // applied to all of them, so a bound taken from the first meant a second,
                // wider flag could never be given more than the default ceiling from this
                // screen - and which flag you had picked first decided it.
                float most = job.Kind == JobKind.Chop ? Resources.ChoppingGround.SearchRadius : 128f;
                float bound = Mathf.Max(shown, Mathf.Max(most, WidestNamedPlace(colony, job)));

                Widgets.Number(radius, "How far it reaches", shown, 8f, bound, 4f,
                    value => $"{value:F0} m",
                    value =>
                    {
                        Edit(host, j => j.WorkRadius = value);

                        // Redrawn, because the row reads the radius in force and an edit that
                        // leaves it showing the old number reads as a control that did not
                        // take.
                        host.Refresh();
                    });
            }

            if (column.TryRow(out Row remove))
            {
                Widgets.Button(remove, "Delete this job", 220f, () =>
                {
                    List<JobDefinition> jobs = colony.State.GetJobs();
                    jobs.RemoveAll(j => j.Id == _id);
                    colony.State.SetJobs(jobs);

                    // Villagers keep their queues: QueueRunner already bypasses a job that is
                    // no longer defined, so a deleted job does not strand anybody. Rewriting
                    // every villager to remove one entry would be a lot of ZDO writes to
                    // achieve what the runner does for free.
                    Report.Say("Job deleted. Villagers assigned to it will skip it.");
                    host.Pop();
                });
            }
        }

        private void BuildHaul(ColonyScreen host, Column column, JobDefinition job)
        {
            if (column.TryRow(out Row items))
            {
                Widgets.Choice(items, "Which items",
                    job.Items.Count == 0 ? "anything" : Summarise(job.Items),
                    () => host.Push(new PickerScreen("Which items", SearchItems, job.Items, true,
                        chosen => { Edit(host, j => j.Items = chosen); host.Refresh(); })));
            }

            if (column.TryRow(out Row tidy))
            {
                Widgets.Flag(tidy, "Tidy containers too", job.TidyContainers,
                    value => { Edit(host, j => j.TidyContainers = value); host.Refresh(); });
            }

            if (column.TryRow(out Row load))
            {
                Widgets.Flag(load, "Fill the bag before delivering", job.FillBagFirst,
                    value => { Edit(host, j => j.FillBagFirst = value); host.Refresh(); });
            }
        }

        /// <summary>
        ///     What a chopping job takes, which of it to leave, and when to stop.
        /// </summary>
        /// <remarks>
        ///     Two stopping rules, deliberately, because they answer different questions.
        ///     "Leave standing" is about the forest - do not strip this place. "Stop when we
        ///     have" is about the settlement - we do not need more. A player wants one, the
        ///     other, or both, and a full woodshed beside a bare hillside is the failure the
        ///     first one prevents.
        /// </remarks>
        private void BuildChop(ColonyScreen host, Column column, JobDefinition job)
        {
            if (column.TryRow(out Row what))
            {
                Widgets.Caption(what, "What to chop", 220f);
                Toggle(host, what, "Trees", job.ChopTrees, (j, on) => j.ChopTrees = on);
                Toggle(host, what, "Logs", job.ChopLogs, (j, on) => j.ChopLogs = on);
                Toggle(host, what, "Undergrowth", job.ChopUndergrowth,
                    (j, on) => j.ChopUndergrowth = on, 190f);
            }

            if (column.TryRow(out Row species))
            {
                Widgets.Choice(species, "Which trees",
                    job.Species.Count == 0 ? "any of them" : SummariseSpecies(job.Species),
                    () => host.Push(new PickerScreen("Which trees", SearchTrees, job.Species, true,
                        chosen => { Edit(host, j => j.Species = chosen); host.Refresh(); })));
            }

            if (column.TryRow(out Row leave))
            {
                Widgets.Number(leave, "Leave standing", job.LeaveStanding, 0f, 50f, 1f,
                    value => value <= 0f ? "none" : $"{value:F0} trees",
                    value => { Edit(host, j => j.LeaveStanding = (int)value); host.Refresh(); });
            }

            if (column.TryRow(out Row stock))
            {
                Widgets.Choice(stock, "Stop when we have",
                    string.IsNullOrEmpty(job.StockItem) ? "never stop" : ItemCatalogue.Label(job.StockItem),
                    () => host.Push(new PickerScreen("Stop when we have", SearchItems,
                        new List<string> { job.StockItem }, false,
                        chosen =>
                        {
                            Edit(host, j =>
                            {
                                j.StockItem = chosen.Count == 0 ? string.Empty : chosen[0];

                                // A target of zero means never stop, so choosing an item and
                                // being left at zero would read as a setting that does
                                // nothing. Giving it a number is what makes the choice take.
                                if (!string.IsNullOrEmpty(j.StockItem) && j.StockTarget <= 0)
                                {
                                    j.StockTarget = 50;
                                }
                            });
                            host.Refresh();
                        })));
            }

            // Only once there is something to count. A threshold with no item is a number
            // that cannot mean anything, and showing it invites setting it and expecting it
            // to work.
            if (!string.IsNullOrEmpty(job.StockItem) && column.TryRow(out Row target))
            {
                Widgets.Number(target, "How much is enough", job.StockTarget, 0f, 999f, 10f,
                    value => value <= 0f ? "never stop" : $"{value:F0}",
                    value => { Edit(host, j => j.StockTarget = (int)value); host.Refresh(); });
            }
        }

        /// <summary>
        ///     One of several on-or-off choices sharing a row.
        /// </summary>
        /// <remarks>
        ///     Narrower than <see cref="Widgets.Flag" />, which takes a full-width label and a
        ///     control - three of those would be three rows for one question.
        /// </remarks>
        private void Toggle(ColonyScreen host, Row row, string label, bool value,
            System.Action<JobDefinition, bool> set, float width = 150f)
        {
            Widgets.Button(row, $"{label}: {(value ? "yes" : "no")}", width, () =>
            {
                Edit(host, j => set(j, !value));
                host.Refresh();
            });
        }

        /// <summary>The radius this job works, whatever decided it.</summary>
        private static float Effective(Colony colony, JobDefinition job) =>
            job.Kind == JobKind.Chop
                ? Jobs.Chop.ChopJob.Area(colony, job).Radius
                : WorkArea.For(colony, job).Radius;

        /// <summary>
        ///     The widest reach any of this job's named places has of its own.
        /// </summary>
        /// <remarks>
        ///     Asked with no reach of its own, so it answers what the places reach rather than
        ///     what the job has already been set to - a bound that followed the current value
        ///     could only ever shrink, and one tap down on a wide flag would lose the rest of
        ///     it for good. Copies are used rather than clearing and restoring the real
        ///     record, because a screen that mutated the job to read from it would write that
        ///     mutation to the colony if anything threw between.
        /// </remarks>
        private static float WidestNamedPlace(Colony colony, JobDefinition job)
        {
            float widest = 0f;

            foreach (string token in job.Areas)
            {
                if (string.IsNullOrEmpty(token)) continue;

                widest = Mathf.Max(widest, Effective(colony, new JobDefinition
                {
                    Kind = job.Kind,
                    Areas = new List<string> { token }
                }));
            }

            return widest;
        }

        /// <summary>
        ///     This job reduced to the first place it names, or null if it names none.
        /// </summary>
        /// <remarks>
        ///     The reach row's subject. A job that only works the settlement has no reach of
        ///     its own to set - that is the hearth's - and showing the row there would be a
        ///     control that changes nothing, which is the shape of setting this codebase has
        ///     already been burned by.
        /// </remarks>
        private static JobDefinition NamedPlace(JobDefinition job)
        {
            foreach (string token in job.Areas)
            {
                if (string.IsNullOrEmpty(token)) continue;

                return new JobDefinition
                {
                    Kind = job.Kind,
                    WorkRadius = job.WorkRadius,
                    Areas = new List<string> { token }
                };
            }

            return null;
        }

        private JobDefinition Find(Colony colony) => colony.State.GetJobs().Find(j => j.Id == _id);

        /// <summary>
        ///     Reads the list, changes one job, writes it back.
        /// </summary>
        /// <remarks>
        ///     The whole list every time, because that is how the record is stored - one blob per
        ///     colony. Cheap at the rate a person presses buttons, and it keeps the colony record
        ///     the single source of truth rather than caching an edited copy somewhere.
        /// </remarks>
        private void Edit(ColonyScreen host, System.Action<JobDefinition> change)
        {
            Colony colony = host.Colony;
            if (colony == null) return;

            List<JobDefinition> jobs = colony.State.GetJobs();
            JobDefinition job = jobs.Find(j => j.Id == _id);
            if (job == null) return;

            change(job);
            colony.State.SetJobs(jobs);
        }

        /// <summary>
        ///     The settlement, then its work-area flags.
        /// </summary>
        /// <remarks>
        ///     Not every registered structure. A work area can still be centred on anything
        ///     registered - resolution is by token and knows nothing of this list - but a
        ///     player choosing where a job works is choosing between the Kolony and the places
        ///     they planted flags for, and burying those among every chest, kiln and bed made
        ///     the choice unusable in a settlement of any size.
        ///
        ///     A place the job already names is listed whatever it is, so a job pointed at a
        ///     chest by an older build can still be seen and unpicked rather than being stuck
        ///     with a setting no screen offers a way to change.
        /// </remarks>
        private static List<PickerScreen.Option> Places(Colony colony, JobDefinition job, string filter)
        {
            List<PickerScreen.Option> options = new List<PickerScreen.Option>
            {
                new PickerScreen.Option(string.Empty, JobListScreen.WholeKolony)
            };

            foreach (StructureRecord record in colony.State.GetStructures())
            {
                bool flag = (record.Capabilities & StructureCapability.WorkArea) != 0;
                if (!flag && !job.Areas.Contains(record.PersistentId)) continue;

                if (!string.IsNullOrEmpty(filter) &&
                    record.Name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                options.Add(new PickerScreen.Option(record.PersistentId, record.Name));
            }

            return options;
        }

        private static List<PickerScreen.Option> SearchItems(string filter)
        {
            List<PickerScreen.Option> options = new List<PickerScreen.Option>();
            foreach (ItemCatalogue.Entry entry in ItemCatalogue.Search(filter, 40))
                options.Add(new PickerScreen.Option(entry.PrefabName, entry.DisplayName));
            return options;
        }

        /// <summary>
        ///     The trees this world contains, asked of the classifier rather than named here.
        /// </summary>
        /// <remarks>
        ///     Shown by prefab name, unlike items, which have a localised label. A tree has no
        ///     item name to look up - and a player excluding a species is picking from what
        ///     their own world actually grows, modded trees included.
        /// </remarks>
        private static List<PickerScreen.Option> SearchTrees(string filter)
        {
            List<string> names = new List<string>();
            Resources.Choppable.TreeNames(names);

            List<PickerScreen.Option> options = new List<PickerScreen.Option>();
            foreach (string name in names)
            {
                if (!string.IsNullOrEmpty(filter) &&
                    name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                options.Add(new PickerScreen.Option(name, name));
            }

            return options;
        }

        private static string Summarise(List<string> items) =>
            items.Count == 1 ? ItemCatalogue.Label(items[0]) : $"{items.Count} kinds";

        private static string SummariseSpecies(List<string> species) =>
            species.Count == 1 ? species[0] : $"{species.Count} kinds";
    }
}
