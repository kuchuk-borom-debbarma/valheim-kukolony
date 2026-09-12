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
        internal override string Title => "Jobs";

        internal override bool StillValid(ColonyScreen host) => host.Colony != null;

        internal override void Build(ColonyScreen host, Column column)
        {
            Colony colony = host.Colony;
            List<JobDefinition> jobs = colony.State.GetJobs();

            if (jobs.Count == 0 && column.TryRow(out Row empty))
            {
                Widgets.Label(empty, "This settlement has no jobs yet.", Color.gray);
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

            if (!column.TryRow(out Row adding)) return;

            Widgets.Button(adding, "New job", 200f, () =>
            {
                List<JobDefinition> next = colony.State.GetJobs();
                JobDefinition fresh = new JobDefinition
                {
                    Id = System.Guid.NewGuid().ToString("N"),
                    Name = "Haul",
                    Kind = JobKind.Haul,
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
            if (string.IsNullOrEmpty(job.WorkArea)) return "the whole settlement";

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
                float shown = job.WorkRadius > 0f ? job.WorkRadius : WorkArea.DefaultRadius;
                Widgets.Number(radius, "How far it reaches", shown, 8f, 128f, 4f,
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
                new PickerScreen.Option(string.Empty, "the whole settlement")
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

        private static string Summarise(List<string> items) =>
            items.Count == 1 ? ItemCatalogue.Label(items[0]) : $"{items.Count} kinds";
    }
}
