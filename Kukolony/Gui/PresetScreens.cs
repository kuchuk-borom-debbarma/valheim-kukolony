using System.Collections.Generic;
using Kukolony.Colonies;
using Kukolony.Core;
using Kukolony.Jobs;
using UnityEngine;

namespace Kukolony.Gui
{
    /// <summary>
    ///     Named sets of orders, and handing them out.
    /// </summary>
    /// <remarks>
    ///     A job says what work is; a preset says who does what. Without one, a settlement of a
    ///     hundred is assigned a hundred times by hand.
    /// </remarks>
    internal sealed class PresetListScreen : ScreenView
    {
        internal override string Title => "Work presets";

        internal override bool StillValid(ColonyScreen host) => host.Colony != null;

        internal override void Build(ColonyScreen host, Column column)
        {
            Colony colony = host.Colony;
            List<JobPreset> presets = colony.State.GetPresets();

            if (presets.Count == 0 && column.TryRow(out Row empty))
            {
                Widgets.Label(empty, "No presets yet. One preset is one kind of worker.", Color.gray);
            }

            foreach (JobPreset preset in presets)
            {
                if (!column.TryRow(out Row row)) continue;

                string id = preset.Id;
                Widgets.Caption(row, preset.Name, 240f);
                Widgets.Caption(row, Describe(colony, preset), 280f, Color.gray);
                Widgets.Button(row, "Open", 110f, () => host.Push(new PresetDetailScreen(id)));
            }

            if (!column.TryRow(out Row adding)) return;

            Widgets.Button(adding, "New preset", 200f, () =>
            {
                List<JobPreset> next = colony.State.GetPresets();
                JobPreset fresh = new JobPreset
                {
                    Id = System.Guid.NewGuid().ToString("N"),
                    Name = "Worker"
                };

                next.Add(fresh);
                colony.State.SetPresets(next);
                Report.Say($"Added the '{fresh.Name}' preset.");
                host.Refresh();
            });
        }

        /// <summary>What a preset contains, on one line.</summary>
        internal static string Describe(Colony colony, JobPreset preset)
        {
            if (preset.Jobs.Count == 0) return "no jobs yet";

            List<JobDefinition> jobs = colony.State.GetJobs();
            JobDefinition first = jobs.Find(j => j.Id == preset.Jobs[0]);
            string lead = first != null ? first.Name : "a job that is gone";

            return preset.Jobs.Count == 1 ? lead : $"{lead} +{preset.Jobs.Count - 1}";
        }
    }

    /// <summary>One preset: what it contains, and who to give it to.</summary>
    internal sealed class PresetDetailScreen : ScreenView
    {
        /// <summary>
        ///     How much room a job name has on these rows, in characters.
        /// </summary>
        /// <remarks>
        ///     The cell is 300 px and the labels overflow rather than clip, at the same ten
        ///     pixels a character the job screens are calibrated to.
        /// </remarks>
        private const int PresetNameBudget = 30;

        private readonly string _id;

        internal PresetDetailScreen(string id) => _id = id;

        internal override string Title => "Preset";

        internal override bool StillValid(ColonyScreen host) =>
            host.Colony != null && Find(host.Colony) != null;

        internal override void Build(ColonyScreen host, Column column)
        {
            Colony colony = host.Colony;
            JobPreset preset = Find(colony);
            if (preset == null) return;

            if (column.TryRow(out Row name))
            {
                Widgets.Text(name, "Name", preset.Name, value => Edit(host, p => p.Name = value.Trim()));
            }

            BuildOrder(host, column, colony, preset);

            List<ZDOID> everyone = Assignment.Everyone(colony);

            if (column.TryRow(out Row all))
            {
                Widgets.Caption(all, "Give it to everyone");
                Widgets.Button(all, $"All {everyone.Count}", 160f, () =>
                {
                    int count = Assignment.Apply(everyone, preset.Jobs);
                    Report.Say(Assigned(count, preset));
                    host.Refresh();
                });
            }

            // ...or to some of them. The same multi-select the rest of the screens use, so a
            // player picks villagers the way they pick anything else.
            if (column.TryRow(out Row some))
            {
                Widgets.Caption(some, "Give it to some");
                Widgets.Button(some, "Choose...", 160f,
                    () => host.Push(new PickerScreen("Which villagers",
                        filter => VillagerOptions(colony, filter), null, true,
                        chosen =>
                        {
                            int count = Assignment.Apply(Ids(chosen), preset.Jobs);
                            Report.Say(Assigned(count, preset));
                            host.Refresh();
                        })));
            }

            if (column.TryRow(out Row remove))
            {
                Widgets.Button(remove, "Delete this preset", 220f, () =>
                {
                    List<JobPreset> presets = colony.State.GetPresets();
                    presets.RemoveAll(p => p.Id == _id);
                    colony.State.SetPresets(presets);

                    // Villagers keep the orders they were given. A preset is a way of handing
                    // work out, not a thing villagers belong to - deleting the template must not
                    // quietly stop a settlement working.
                    Report.Say("Preset deleted. Villagers keep the jobs they were given.");
                    host.Pop();
                });
            }
        }

        private static string Assigned(int count, JobPreset preset) =>
            count == 0
                ? "Nobody to assign."
                : $"Gave '{preset.Name}' to {count} villager{(count == 1 ? string.Empty : "s")}.";

        private JobPreset Find(Colony colony) => colony.State.GetPresets().Find(p => p.Id == _id);

        private void Edit(ColonyScreen host, System.Action<JobPreset> change)
        {
            Colony colony = host.Colony;
            if (colony == null) return;

            List<JobPreset> presets = colony.State.GetPresets();
            JobPreset preset = presets.Find(p => p.Id == _id);
            if (preset == null) return;

            change(preset);
            colony.State.SetPresets(presets);
        }

        /// <summary>
        ///     The preset's jobs as the ordered list they are, and the way to change that order.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         A preset is a queue and a queue is an order, so the order has to be visible
        ///         and editable. It was a multi-select picker, which is the wrong shape twice
        ///         over: the order it returned was its own rather than the player's, and
        ///         nothing on screen showed what that order had turned out to be. A player
        ///         setting "chop, then haul" had no way to tell they had been given "haul,
        ///         then chop".
        ///     </para>
        ///     <para>
        ///         So the two halves are shown as what they are: the jobs this preset does,
        ///         numbered, each removable; and the jobs it does not, each addable. Adding
        ///         puts one at the end, which is the only rule needed to build any order -
        ///         add them in the sequence you want them done.
        ///     </para>
        /// </remarks>
        private void BuildOrder(ColonyScreen host, Column column, Colony colony, JobPreset preset)
        {
            List<JobDefinition> defined = colony.State.GetJobs();

            if (column.TryRow(out Row heading))
            {
                Widgets.Label(heading, preset.Jobs.Count == 0
                    ? "Does no jobs yet - add one below"
                    : $"Does these {preset.Jobs.Count} jobs, in this order:", Color.gray);
            }

            for (int i = 0; i < preset.Jobs.Count; i++)
            {
                if (!column.TryRow(out Row row)) continue;

                string id = preset.Jobs[i];
                JobDefinition job = defined.Find(j => j.Id == id);

                // A job the colony no longer defines still shows, named for what it is, because
                // a preset half full of deleted work should look wrong rather than quietly
                // shorten itself into something nobody asked for.
                // Held to the cell: a numbered row spends four characters before the name
                // starts, and a job name has no length limit of its own.
                string label = job == null ? "a job that is gone" : job.Name;
                label = JobListScreen.Fit(label, PresetNameBudget - 4);
                Widgets.Caption(row, $"{i + 1}.  {label}", 300f,
                    job == null ? Color.gray : Color.white);
                Widgets.Caption(row, job == null ? string.Empty : JobDefinition.Describe(job.Kind),
                    140f, Color.gray);

                int at = i;
                Widgets.Button(row, "Remove", 140f, () =>
                {
                    Edit(host, p =>
                    {
                        if (at >= 0 && at < p.Jobs.Count) p.Jobs.RemoveAt(at);
                    });
                    host.Refresh();
                });
            }

            // What is left to add. A job already in the list is not offered again: a preset is
            // an order to work through, and the same job twice in it is a rotation nobody has
            // asked for and a queue position that is ambiguous to read.
            foreach (JobDefinition job in defined)
            {
                if (preset.Jobs.Contains(job.Id)) continue;
                if (!column.TryRow(out Row row)) continue;

                Widgets.Caption(row, JobListScreen.Fit(job.Name, PresetNameBudget), 300f, Color.gray);
                Widgets.Caption(row, JobDefinition.Describe(job.Kind), 140f, Color.gray);

                string id = job.Id;
                Widgets.Button(row, "Add", 140f, () =>
                {
                    // Appended, never inserted. "Add them in the order you want them done" is
                    // one rule a player can hold in their head, and it can build any order.
                    Edit(host, p => p.Jobs.Add(id));
                    host.Refresh();
                });
            }
        }

        /// <summary>
        ///     Everyone in the settlement, loaded or not.
        /// </summary>
        /// <remarks>
        ///     Identified by ZDOID written as a string, because the picker deals in strings and a
        ///     villager's address is the only thing that names it uniquely. Parsed back on the way
        ///     out rather than kept in a side table that could fall out of step with the list.
        /// </remarks>
        private static List<PickerScreen.Option> VillagerOptions(Colony colony, string filter)
        {
            List<PickerScreen.Option> options = new List<PickerScreen.Option>();
            foreach (ZDOID id in Assignment.Everyone(colony))
            {
                string name = VillagerRoster.Name(id);
                if (!Matches(name, filter)) continue;

                options.Add(new PickerScreen.Option(id.ToString(),
                    $"{name} - {VillagerListScreen.Doing(id)}"));
            }

            return options;
        }

        private static List<ZDOID> Ids(List<string> chosen)
        {
            List<ZDOID> ids = new List<ZDOID>();
            foreach (string text in chosen)
            {
                if (TryParse(text, out ZDOID id)) ids.Add(id);
            }

            return ids;
        }

        /// <summary>
        ///     Reads a ZDOID back from the text the picker carried.
        /// </summary>
        /// <remarks>
        ///     ZDOID.ToString is "userID:id", and there is no parser in the game for it. Written
        ///     here rather than assumed, and returning false on anything unexpected rather than
        ///     producing a plausible wrong address - which would assign work to whatever object
        ///     happened to hold that id.
        /// </remarks>
        private static bool TryParse(string text, out ZDOID id)
        {
            id = ZDOID.None;
            if (string.IsNullOrEmpty(text)) return false;

            int split = text.IndexOf(':');
            if (split <= 0 || split >= text.Length - 1) return false;

            if (!long.TryParse(text.Substring(0, split), out long user)) return false;
            if (!uint.TryParse(text.Substring(split + 1), out uint local)) return false;

            id = new ZDOID(user, local);
            return true;
        }

        private static bool Matches(string text, string filter) =>
            string.IsNullOrEmpty(filter) ||
            (text != null && text.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
