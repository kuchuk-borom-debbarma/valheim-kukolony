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

            foreach (JobDefinition job in jobs)
            {
                if (!column.TryRow(out Row row)) continue;

                string id = job.Id;
                Widgets.Caption(row, job.Name, 220f);
                Widgets.Caption(row, JobDefinition.Describe(job.Kind), 150f, Color.gray);
                Widgets.Caption(row, Where(colony, job), 190f, Color.gray);
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

        /// <summary>A short answer to "where does this happen", for the list.</summary>
        /// <remarks>
        ///     Several places are summarised by the first and a count, rather than listed:
        ///     the row is one line beside a label, and the order is what the first one says.
        /// </remarks>
        internal static string Where(Colony colony, JobDefinition job)
        {
            List<string> tokens = job.Areas ?? new List<string>();
            if (tokens.Count == 0) return WholeKolony;

            // The registry is decoded once for the whole row. GetStructures base64-decodes a
            // package and allocates every record in it, and asking per token - and once more
            // for the name - meant a screen listing forty jobs decoded the settlement's
            // structures a hundred and sixty times, on every refresh, which is once per click
            // in a multi-select picker.
            List<StructureRecord> records = colony.State.GetStructures();

            // The first two are named, not counted. "The whole Kolony +1" was accurate and
            // read as "and the copse as well" rather than "the copse after this one" - and
            // the order is the whole point of the list.
            string first = PlaceName(records, tokens[0]);
            if (tokens.Count == 1) return first;

            string summary = tokens.Count == 2
                ? $"{first} then {PlaceName(records, tokens[1])}"
                : $"{first} then {PlaceName(records, tokens[1])} +{tokens.Count - 2}";

            // Counted over the whole list rather than the ones named. An area further down
            // that has been destroyed hid behind the count, so the row went on promising
            // places the job no longer works - the same silence the single-area message
            // exists to break.
            int gone = 0;
            foreach (string token in tokens)
            {
                if (!TryPlaceName(records, token, out string _)) gone++;
            }

            // A named one saying so already covers itself.
            return gone > 0 ? $"{summary} ({gone} gone)" : summary;
        }

        /// <summary>What one place token is called.</summary>
        private static string PlaceName(List<StructureRecord> records, string token) =>
            TryPlaceName(records, token, out string name) ? name : GonePlace;

        /// <summary>
        ///     The area was destroyed or unregistered. Said plainly, because the job still
        ///     runs - it falls back to the settlement - and a player should know why it
        ///     suddenly ranges further than they told it to.
        /// </summary>
        private const string GonePlace = "a place that is gone";

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
