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
        internal static string Where(Colony colony, JobDefinition job)
        {
            if (string.IsNullOrEmpty(job.WorkArea)) return WholeKolony;

            foreach (StructureRecord record in colony.State.GetStructures())
            {
                if (record.PersistentId == job.WorkArea) return record.Name;
            }

            // The area was destroyed or unregistered. Said plainly, because the job still runs -
            // it falls back to the settlement - and a player should know why it suddenly ranges
            // further than they told it to.
            return "a place that is gone";
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
                Widgets.Text(name, "Name", job.Name, value => Edit(host, j => j.Name = value.Trim()));
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
                    value => Edit(host, j => j.Repeat = (int)value));
            }

            // Each kind shows only the settings it reads. A job must not offer a setting it
            // ignores: FillBagFirst was persisted, shown here as a toggle, and read by nothing
            // for long enough that the trap got written down and then walked into anyway - so
            // the gate is here, at the one place a setting becomes visible.
            if (job.Kind == JobKind.Haul) BuildHaul(host, column, job);
            if (job.Kind == JobKind.Chop) BuildChop(host, column, job);

            // Where it works. Anything registered can be the centre of a work area, and the
            // empty choice is the settlement itself - which is a real answer rather than an
            // unset one, so it is offered first and named.
            if (column.TryRow(out Row where))
            {
                Widgets.Choice(where, "Where it works", JobListScreen.Where(colony, job),
                    () => host.Push(new PickerScreen("Where it works", filter => Places(colony, filter),
                        new List<string> { job.WorkArea }, false,
                        chosen =>
                        {
                            Edit(host, j => j.WorkArea = chosen.Count == 0 ? string.Empty : chosen[0]);
                            host.Refresh();
                        })), 260f);
            }

            if (!string.IsNullOrEmpty(job.WorkArea) && column.TryRow(out Row radius))
            {
                // The number actually in force, not a constant that happens to be the
                // fallback for some work areas. Pointed at a flag, an unset reach means the
                // flag's own radius - so a job on a two-hundred-metre flag used to read
                // "48 m" on this row while working the whole outpost, and no slider position
                // could have stated the truth.
                float shown = WorkArea.For(colony, job).Radius;

                // Capped for chopping at the ceiling the scan will honour, because a reach
                // set past it is a promise this screen cannot keep: the search never looks
                // that far, so the job would report nothing to chop for trees this row said
                // were in range.
                float most = job.Kind == JobKind.Chop
                    ? Mathf.Min(128f, ModConfig.ResourceScanRadius.Value)
                    : 128f;

                Widgets.Number(radius, "How far it reaches", Mathf.Min(shown, most), 8f, most, 4f,
                    value => $"{value:F0} m",
                    value => Edit(host, j => j.WorkRadius = value));
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
                    value => Edit(host, j => j.TidyContainers = value));
            }

            if (column.TryRow(out Row load))
            {
                Widgets.Flag(load, "Fill the bag before delivering", job.FillBagFirst,
                    value => Edit(host, j => j.FillBagFirst = value));
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
                    value => Edit(host, j => j.LeaveStanding = (int)value));
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
                    value => Edit(host, j => j.StockTarget = (int)value));
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

        /// <summary>The settlement, then everything registered to it.</summary>
        private static List<PickerScreen.Option> Places(Colony colony, string filter)
        {
            List<PickerScreen.Option> options = new List<PickerScreen.Option>
            {
                new PickerScreen.Option(string.Empty, JobListScreen.WholeKolony)
            };

            foreach (StructureRecord record in colony.State.GetStructures())
            {
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
