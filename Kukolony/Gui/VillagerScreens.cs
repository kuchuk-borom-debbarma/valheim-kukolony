using System.Collections.Generic;
using Kukolony.Colonies;
using Kukolony.Core;
using Kukolony.Jobs;
using Kukolony.Villagers;
using UnityEngine;

namespace Kukolony.Gui
{
    /// <summary>
    ///     Everyone who lives here.
    /// </summary>
    internal sealed class VillagerListScreen : ScreenView
    {
        internal override string Title => "Villagers";

        internal override bool StillValid(ColonyScreen host) => host.Colony != null;

        internal override void Build(ColonyScreen host, Column column)
        {
            Colony colony = host.Colony;

            // Decoded once, not once per row: GetMembers unpacks a string every call, so a
            // list that asked per row would cost more the more villagers a settlement had -
            // which is the one thing a settlement with no population cap cannot afford.
            List<ZDOID> members = colony.State.GetMembers(ColonyMemberKind.Villager);

            if (members.Count == 0 && column.TryRow(out Row empty))
            {
                Widgets.Label(empty, "Nobody lives here yet.", Color.gray);
            }

            foreach (ZDOID member in members)
            {
                if (!column.TryRow(out Row row)) continue;

                ZDO zdo = ZDOMan.instance?.GetZDO(member);
                VillagerState state = new VillagerState(zdo);
                StructureRecord bed = SettlementIndex.BedOf(colony, member);

                // 220 + 260 + 150 + 110 and three gaps is 764 of the 800 there is. What a
                // villager is doing gets the wide cell because it is the column that changes,
                // and all three are held to their cells: names are whatever a player typed and
                // an activity like "putting Copper ore back" already overran the narrow one it
                // used to have, drawing itself across the bed beside it.
                Widgets.Caption(row, JobListScreen.Fit(VillagerRoster.Name(member), 22), 220f);
                Widgets.Caption(row, JobListScreen.Fit(Doing(member), 26), 260f, Color.gray);
                Widgets.Caption(row, bed == null ? "no bed" : JobListScreen.Fit(bed.Name, 15), 150f,
                    bed == null ? Color.gray : Color.white);

                ZDOID chosen = member;
                Widgets.Button(row, "Open", 110f, () => host.Push(new VillagerDetailScreen(chosen)));
            }

            if (column.TryRow(out Row add))
            {
                Widgets.Button(add, "New villager", 220f, () =>
                {
                    Villager born = VillagerLifecycle.Spawn(colony);
                    Report.Say(born != null
                        ? $"A new villager joined {colony.State.Name}."
                        : "That villager could not be placed.");
                    host.Refresh();
                });
            }
        }

        /// <summary>
        ///     What a villager is doing, when anyone can see it.
        /// </summary>
        /// <remarks>
        ///     Activity lives on the component, not the ZDO, so a villager nobody is near has
        ///     none to report. Saying "not loaded" is honest; inventing "idle" would claim
        ///     knowledge of something nothing is simulating.
        /// </remarks>
        internal static string Doing(ZDOID villager)
        {
            foreach (Villager live in Villager.Instances)
            {
                if (live == null || !live.TryGetComponent(out ZNetView view) || !view.IsValid()) continue;
                if (view.GetZDO().m_uid == villager) return live.Activity;
            }

            return "not loaded";
        }
    }

    /// <summary>
    ///     One villager: who they are, what they have, and what can be done about it.
    /// </summary>
    internal sealed class VillagerDetailScreen : ScreenView
    {
        private readonly ZDOID _villager;

        internal VillagerDetailScreen(ZDOID villager) => _villager = villager;

        internal override string Title => "Villager";

        internal override bool StillValid(ColonyScreen host) => host.Colony != null;

        internal override void Build(ColonyScreen host, Column column)
        {
            Colony colony = host.Colony;
            ZDO zdo = ZDOMan.instance?.GetZDO(_villager);

            if (zdo == null)
            {
                if (column.TryRow(out Row gone))
                {
                    Widgets.Label(gone, "This villager is no longer here.", Color.gray);
                }

                return;
            }

            VillagerState state = new VillagerState(zdo);

            if (column.TryRow(out Row name))
            {
                Widgets.Text(name, "Name", state.Name, value =>
                {
                    string trimmed = (value ?? string.Empty).Trim();
                    if (trimmed.Length == 0)
                    {
                        Report.Say("A villager needs a name.");
                        host.Refresh();
                        return;
                    }

                    // The name lives on the villager's ZDO, so the villager must be ours to
                    // write - a non-owner's rename lands locally and is lost on the next sync.
                    zdo.SetOwner(ZDOMan.GetSessionID());
                    new VillagerState(zdo).SetName(trimmed);
                    Report.Say($"Renamed to {trimmed}.");
                    host.Refresh();
                });
            }

            if (column.TryRow(out Row doing))
            {
                Widgets.Caption(doing, "Doing");
                Widgets.Label(doing, VillagerListScreen.Doing(_villager), Color.gray);
            }

            if (column.TryRow(out Row energy))
            {
                Widgets.Caption(energy, "Energy");
                Widgets.Label(energy, Villager.EnergyText(new VillagerState(zdo)));
            }

            StructureRecord bed = SettlementIndex.BedOf(colony, _villager);
            if (column.TryRow(out Row bedRow))
            {
                Widgets.Choice(bedRow, "Sleeps in", bed == null ? string.Empty : bed.Name,
                    () => host.Push(new PickerScreen("Which bed", filter => FreeBeds(colony, bed, filter),
                        bed == null ? null : new[] { bed.PersistentId }, false, chosen => AssignBed(colony, chosen))));
            }

            // One row, one question: what does this villager do. A single job, or a preset -
            // which is a named list of jobs in an order somebody already worked out - and never
            // a mixture of the two. Building an order by hand is what the preset screen is for,
            // and having built one there it should not also be buildable here by a different
            // route, because two ways to say the same thing is two things to keep in step and
            // reads as two settings that might disagree.
            List<string> queue = new VillagerState(zdo).GetQueue();
            if (column.TryRow(out Row jobs))
            {
                Widgets.Choice(jobs, "Work", Doing(colony, queue),
                    () => host.Push(new PickerScreen("What should they do",
                        filter => WorkOptions(colony, filter), new List<string> { Chosen(colony, queue) },
                        false, chosen => Assign(host, colony, zdo, chosen))), 260f);
            }

            BuildEquipment(host, column, zdo);
            BuildCarried(host, column, colony, zdo);

            if (column.TryRow(out Row remove))
            {
                Widgets.Button(remove, "Remove villager", 220f, () =>
                {
                    string who = VillagerRoster.Name(_villager);
                    VillagerLifecycle.Remove(colony, _villager);
                    Report.Say($"{who} left {colony.State.Name}, leaving behind what they carried.");
                    host.Pop();
                });
            }
        }

        /// <summary>
        ///     What this villager does, in as few words as say it.
        /// </summary>
        /// <remarks>
        ///     The preset's name when the queue is one word for word, and the queue itself
        ///     otherwise. Naming the preset is shorter and is what a player called it, and
        ///     falling back to the queue is what keeps the row honest the moment somebody edits
        ///     away from the template.
        /// </remarks>
        private static string Doing(Colony colony, List<string> queue)
        {
            // Held to the cell, because a preset's name is whatever a player typed and these
            // labels overflow rather than clip. DescribeQueue bounds its own.
            string preset = MatchingPreset(colony, queue);
            return preset.Length > 0
                ? JobListScreen.Fit(preset, QueueBudget)
                : DescribeQueue(colony, queue);
        }

        /// <summary>The id of the preset this queue is, word for word, or empty.</summary>
        private static string MatchingPresetId(Colony colony, List<string> queue) =>
            Matching(colony, queue, false);

        private static string Matching(Colony colony, List<string> queue, bool named)
        {
            if (queue.Count == 0) return string.Empty;

            foreach (JobPreset preset in colony.State.GetPresets())
            {
                if (preset.Jobs.Count != queue.Count) continue;

                bool same = true;
                for (int i = 0; i < queue.Count && same; i++)
                {
                    same = preset.Jobs[i] == queue[i];
                }

                if (same) return named ? preset.Name : preset.Id;
            }

            return string.Empty;
        }

        /// <summary>
        ///     The preset this villager's queue matches, if one does.
        /// </summary>
        /// <remarks>
        ///     Shown rather than remembered. A villager is not "on" a preset - it was given
        ///     one, and may have been edited since - so storing which one would be a claim the
        ///     world is free to falsify. Comparing the queue says what is true now, and says
        ///     nothing when somebody has departed from the template, which is the honest
        ///     answer rather than a stale name.
        /// </remarks>
        private static string MatchingPreset(Colony colony, List<string> queue)
        {
            if (queue.Count == 0) return string.Empty;

            foreach (JobPreset preset in colony.State.GetPresets())
            {
                if (preset.Jobs.Count != queue.Count) continue;

                bool same = true;
                for (int i = 0; i < queue.Count && same; i++)
                {
                    same = preset.Jobs[i] == queue[i];
                }

                if (same) return preset.Name;
            }

            return string.Empty;
        }

        private static List<PickerScreen.Option> PresetOptions(Colony colony, string filter)
        {
            List<PickerScreen.Option> options = new List<PickerScreen.Option>();
            foreach (JobPreset preset in colony.State.GetPresets())
            {
                if (!string.IsNullOrEmpty(filter) &&
                    preset.Name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                // What it does as well as what it is called, because a list of names alone
                // makes a player open every one of them to find the one they meant.
                options.Add(new PickerScreen.Option(preset.Id,
                    $"{preset.Name} - {PresetListScreen.Describe(colony, preset)}"));
            }

            return options;
        }

        /// <summary>Gives this villager a preset's queue, in the preset's own order.</summary>
        /// <summary>
        ///     Everything a villager can be given: nothing, a preset, or one job.
        /// </summary>
        /// <remarks>
        ///     Presets first, because a preset is the answer whenever somebody has already
        ///     decided what a role looks like, and a bare job is the exception. Prefixed ids
        ///     rather than two lists, so one press means one thing and the villager's orders
        ///     cannot end up half a preset and half something else.
        /// </remarks>
        private static List<PickerScreen.Option> WorkOptions(Colony colony, string filter)
        {
            List<PickerScreen.Option> options = new List<PickerScreen.Option>
            {
                new PickerScreen.Option(string.Empty, "nothing")
            };

            foreach (JobPreset preset in colony.State.GetPresets())
            {
                if (!Matches(preset.Name, filter)) continue;

                options.Add(new PickerScreen.Option(PresetPrefix + preset.Id,
                    $"{JobListScreen.Fit(preset.Name, JobListScreen.NameBudget)} " +
                    $"- {preset.Jobs.Count} job(s)"));
            }

            List<StructureRecord> records = null;
            foreach (JobDefinition job in colony.State.GetJobs())
            {
                if (!Matches(job.Name, filter)) continue;

                records = records ?? colony.State.GetStructures();
                options.Add(new PickerScreen.Option(JobPrefix + job.Id,
                    $"{JobListScreen.Fit(job.Name, JobListScreen.NameBudget)} " +
                    $"- {JobListScreen.Where(records, job, JobListScreen.PickerBudget)}"));
            }

            return options;
        }

        private static bool Matches(string name, string filter) =>
            string.IsNullOrEmpty(filter) ||
            name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Which row of the picker this villager's orders are, so it opens on it.</summary>
        private static string Chosen(Colony colony, List<string> queue)
        {
            if (queue.Count == 0) return string.Empty;

            string preset = MatchingPresetId(colony, queue);
            if (preset.Length > 0) return PresetPrefix + preset;

            // A queue of one is that job; a longer one that matches no preset is left over from
            // an older build, and matches no row rather than pretending to be one of them.
            return queue.Count == 1 ? JobPrefix + queue[0] : string.Empty;
        }

        private const string PresetPrefix = "preset:";

        private const string JobPrefix = "job:";

        /// <summary>
        ///     Gives a villager its orders, whole.
        /// </summary>
        /// <remarks>
        ///     Whatever was there before is replaced rather than added to - picking a job after
        ///     a preset means that job and nothing else, which is what "one row, one question"
        ///     has to mean if the row is to be believed.
        /// </remarks>
        private void Assign(ColonyScreen host, Colony colony, ZDO zdo, List<string> chosen)
        {
            string id = chosen.Count == 0 ? string.Empty : chosen[0];

            if (id.StartsWith(PresetPrefix, System.StringComparison.Ordinal))
            {
                ApplyPreset(host, colony, new List<string> { id.Substring(PresetPrefix.Length) });
                return;
            }

            zdo.SetOwner(ZDOMan.GetSessionID());

            if (id.StartsWith(JobPrefix, System.StringComparison.Ordinal))
            {
                string job = id.Substring(JobPrefix.Length);
                new VillagerState(zdo).SetQueue(new List<string> { job });

                JobDefinition definition = colony.State.GetJobs().Find(j => j.Id == job);
                Report.Say(definition == null
                    ? "That job is gone."
                    : $"{VillagerRoster.Name(_villager)} now works {definition.Name}.");

                host.Refresh();
                return;
            }

            new VillagerState(zdo).SetQueue(new List<string>());
            Report.Say($"{VillagerRoster.Name(_villager)} has nothing to do.");
            host.Refresh();
        }

        private void ApplyPreset(ColonyScreen host, Colony colony, List<string> chosen)
        {
            if (chosen.Count == 0) return;

            JobPreset preset = colony.State.GetPresets().Find(p => p.Id == chosen[0]);
            if (preset == null)
            {
                Report.Say("That preset is gone.");
                host.Refresh();
                return;
            }

            // The same call the preset screen makes when it hands work to everybody, so one
            // villager and a whole settlement cannot come to disagree about what giving a
            // preset means.
            int count = Assignment.Apply(new List<ZDOID> { _villager }, preset.Jobs);

            Report.Say(count == 0
                ? $"Could not give {preset.Name} to {VillagerRoster.Name(_villager)}."
                : $"{VillagerRoster.Name(_villager)} now works {preset.Name}.");
            host.Refresh();
        }

        /// <summary>
        ///     What the villager is wearing, and what else it owns that would fit.
        /// </summary>
        /// <remarks>
        ///     A slot offers only what the villager's bag holds, because the visible slot is a
        ///     mirror of the bag rather than a wardrobe of its own. Putting on something it does
        ///     not own would show an item that vanishes the moment anything rebuilt the mirror.
        /// </remarks>
        private void BuildEquipment(ColonyScreen host, Column column, ZDO zdo)
        {
            if (column.TryRow(out Row heading))
            {
                Widgets.Label(heading, "Wearing", Color.gray);
            }

            for (int i = 0; i < VillagerWardrobe.SlotCount; i++)
            {
                WearSlot slot = (WearSlot)i;
                if (!column.TryRow(out Row row)) continue;

                int worn = VillagerWardrobe.Worn(zdo, slot);
                WearSlot chosenSlot = slot;
                Widgets.Choice(row, VillagerWardrobe.Label(slot), VillagerWardrobe.NameOf(worn),
                    () => host.Push(new PickerScreen($"What to wear on {VillagerWardrobe.Label(chosenSlot).ToLowerInvariant()}",
                        filter => Fitting(zdo, chosenSlot, filter), null, false,
                        chosen => Wear(host, zdo, chosenSlot, chosen))), 330f);
            }
        }

        private void BuildCarried(ColonyScreen host, Column column, Colony colony, ZDO zdo)
        {
            Inventory bag = VillagerInventory.Stored(zdo);
            List<ItemDrop.ItemData> items = bag.GetAllItems();

            if (column.TryRow(out Row heading))
            {
                Widgets.Label(heading, items.Count == 0
                    ? "Carrying nothing"
                    : $"Carrying ({items.Count})", Color.gray);
            }

            foreach (ItemDrop.ItemData item in items)
            {
                if (!column.TryRow(out Row row)) continue;
                Widgets.Caption(row, item.m_shared?.m_name is string token && token.Length > 0
                    ? Localization.instance.Localize(token) : "?", 320f);
                Widgets.Label(row, item.m_stack.ToString(), Color.gray);
            }

            if (column.TryRow(out Row give))
            {
                Widgets.Button(give, "Give an item", 220f,
                    () => host.Push(new PickerScreen("What to give", FromPlayer, null, false,
                        chosen => Give(host, zdo, chosen))));
            }
        }

        /// <summary>Items in the player's own inventory, which is how gear reaches a villager.</summary>
        private static List<PickerScreen.Option> FromPlayer(string filter)
        {
            List<PickerScreen.Option> options = new List<PickerScreen.Option>();
            Player player = Player.m_localPlayer;
            if (player == null) return options;

            foreach (ItemDrop.ItemData item in player.GetInventory().GetAllItems())
            {
                if (item?.m_dropPrefab == null) continue;
                string prefab = Utils.GetPrefabName(item.m_dropPrefab);
                string label = Localization.instance.Localize(item.m_shared.m_name);
                if (filter.Length > 0 &&
                    label.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                options.Add(new PickerScreen.Option(prefab, $"{label} x{item.m_stack}"));
            }

            return options;
        }

        private void Give(ColonyScreen host, ZDO zdo, List<string> chosen)
        {
            if (chosen == null || chosen.Count == 0 || Player.m_localPlayer == null) return;

            GameObject instance = ZNetScene.instance?.FindInstance(_villager);
            if (instance == null || !instance.TryGetComponent(out ZNetView view) || !view.IsValid())
            {
                Report.Say("That villager is not loaded here, so nothing can be handed over.");
                return;
            }

            Inventory mine = Player.m_localPlayer.GetInventory();
            ItemDrop.ItemData item = null;
            foreach (ItemDrop.ItemData candidate in mine.GetAllItems())
                if (candidate?.m_dropPrefab != null &&
                    Utils.GetPrefabName(candidate.m_dropPrefab) == chosen[0]) { item = candidate; break; }

            if (item == null)
            {
                Report.Say("You no longer have that.");
                host.Pop();
                return;
            }

            view.ClaimOwnership();
            Container bag = VillagerInventory.Attach(instance, view);
            if (!bag.GetInventory().AddItem(item))
            {
                Report.Say("Their bag is full.");
                return;
            }

            mine.RemoveItem(item);
            Report.Say($"Gave {Localization.instance.Localize(item.m_shared.m_name)} to " +
                       $"{VillagerRoster.Name(_villager)}.");
            host.Pop();
        }

        private void Wear(ColonyScreen host, ZDO zdo, WearSlot slot, List<string> chosen)
        {
            GameObject instance = ZNetScene.instance?.FindInstance(_villager);
            if (instance == null || !instance.TryGetComponent(out ZNetView view) || !view.IsValid() ||
                !instance.TryGetComponent(out VisEquipment vis))
            {
                Report.Say("That villager is not loaded here, so their clothes cannot be changed.");
                return;
            }

            view.ClaimOwnership();
            string prefab = chosen != null && chosen.Count > 0 ? chosen[0] : string.Empty;

            ItemDrop.ItemData item = null;
            if (prefab.Length > 0)
            {
                foreach (ItemDrop.ItemData candidate in VillagerInventory.Stored(zdo).GetAllItems())
                    if (candidate?.m_dropPrefab != null &&
                        Utils.GetPrefabName(candidate.m_dropPrefab) == prefab) { item = candidate; break; }
            }

            VillagerWardrobe.Set(vis, slot, item);
            Report.Say(item == null
                ? $"{VillagerRoster.Name(_villager)}'s {VillagerWardrobe.Label(slot).ToLowerInvariant()} is bare."
                : $"{VillagerRoster.Name(_villager)} is wearing " +
                  $"{Localization.instance.Localize(item.m_shared.m_name)}.");
            host.Pop();
        }

        /// <summary>What the villager owns that fits this slot, plus the option of nothing.</summary>
        private static List<PickerScreen.Option> Fitting(ZDO zdo, WearSlot slot, string filter)
        {
            List<PickerScreen.Option> options = new List<PickerScreen.Option>
            {
                new PickerScreen.Option(string.Empty, "(nothing)")
            };

            foreach (ItemDrop.ItemData item in VillagerInventory.Stored(zdo).GetAllItems())
            {
                if (item?.m_dropPrefab == null || !VillagerWardrobe.Fits(item, slot)) continue;
                string label = Localization.instance.Localize(item.m_shared.m_name);
                if (filter.Length > 0 &&
                    label.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                options.Add(new PickerScreen.Option(Utils.GetPrefabName(item.m_dropPrefab), label));
            }

            return options;
        }


        private static List<PickerScreen.Option> FreeBeds(Colony colony, StructureRecord current, string filter)
        {
            List<PickerScreen.Option> options = new List<PickerScreen.Option>
            {
                new PickerScreen.Option(string.Empty, "(no bed)")
            };

            List<StructureRecord> beds = SettlementIndex.FreeBeds(colony);
            if (current != null) beds.Add(current);

            foreach (StructureRecord bed in beds)
            {
                if (filter.Length > 0 &&
                    bed.Name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                options.Add(new PickerScreen.Option(bed.PersistentId, bed.Name));
            }

            return options;
        }

        /// <summary>What a villager's queue reads as on one line.</summary>
        /// <remarks>
        ///     Names the first and counts the rest. A queue of five reads as "Haul +4" rather
        ///     than as a list that would not fit and would be truncated somewhere arbitrary.
        /// </remarks>
        private const int QueueBudget = 24;

        private static string DescribeQueue(Colony colony, List<string> queue)
        {
            if (queue.Count == 0) return "nothing";

            JobDefinition first = colony.State.GetJobs().Find(j => j.Id == queue[0]);
            string lead = first != null ? first.Name : "a job that is gone";

            // Held to the button it is drawn on, the same way the job row's own summary is.
            // A job name is refused only when empty, so "Haul everything to the shed by the
            // docks" drew four hundred pixels of text across a two-hundred-and-sixty pixel
            // control - and the count, which the name cannot be read to imply, went with it.
            string tail = queue.Count == 1 ? string.Empty : $" +{queue.Count - 1}";
            return JobListScreen.Fit(lead, Mathf.Max(1, QueueBudget - tail.Length)) + tail;
        }


        private void AssignBed(Colony colony, List<string> chosen)
        {
            string token = chosen != null && chosen.Count > 0 ? chosen[0] : string.Empty;

            // Clearing means taking this villager out of whatever bed they are in.
            StructureRecord current = SettlementIndex.BedOf(colony, _villager);
            if (token.Length == 0)
            {
                if (current != null) VillagerRoster.Assign(colony, current, null);
                return;
            }

            StructureRecord bed = colony.State.GetStructures().Find(r => r.PersistentId == token);
            if (bed != null) VillagerRoster.Assign(colony, bed, new List<string> { _villager.ToString() });
        }
    }
}
