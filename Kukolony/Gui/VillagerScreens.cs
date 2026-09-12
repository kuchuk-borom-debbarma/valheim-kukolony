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

                Widgets.Caption(row, VillagerRoster.Name(member), 220f);
                Widgets.Caption(row, Doing(member), 170f, Color.gray);
                Widgets.Caption(row, bed == null ? "no bed" : bed.Name, 170f,
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

            StructureRecord bed = SettlementIndex.BedOf(colony, _villager);
            if (column.TryRow(out Row bedRow))
            {
                Widgets.Choice(bedRow, "Sleeps in", bed == null ? string.Empty : bed.Name,
                    () => host.Push(new PickerScreen("Which bed", filter => FreeBeds(colony, bed, filter),
                        bed == null ? null : new[] { bed.PersistentId }, false, chosen => AssignBed(colony, chosen))));
            }

            // The queue is a multi-select whose order is kept, because a queue IS an order: a
            // villager works the first job its repeats allow, then the next. Storing it on the
            // villager rather than the colony means reassigning one person does not rewrite the
            // settlement's record and invalidate every cached answer built from it.
            List<string> queue = new VillagerState(zdo).GetQueue();
            if (column.TryRow(out Row jobs))
            {
                Widgets.Choice(jobs, "Works at", DescribeQueue(colony, queue),
                    () => host.Push(new PickerScreen("Which jobs, in order",
                        filter => JobOptions(colony, filter), queue, true,
                        chosen =>
                        {
                            zdo.SetOwner(ZDOMan.GetSessionID());
                            new VillagerState(zdo).SetQueue(chosen);
                            Report.Say(chosen.Count == 0
                                ? "Given nothing to do."
                                : $"Assigned {chosen.Count} job(s).");
                            host.Refresh();
                        })), 260f);
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
        private static string DescribeQueue(Colony colony, List<string> queue)
        {
            if (queue.Count == 0) return "nothing";

            JobDefinition first = colony.State.GetJobs().Find(j => j.Id == queue[0]);
            string lead = first != null ? first.Name : "a job that is gone";
            return queue.Count == 1 ? lead : $"{lead} +{queue.Count - 1}";
        }

        private static List<PickerScreen.Option> JobOptions(Colony colony, string filter)
        {
            List<PickerScreen.Option> options = new List<PickerScreen.Option>();
            foreach (JobDefinition job in colony.State.GetJobs())
            {
                if (!string.IsNullOrEmpty(filter) &&
                    job.Name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                options.Add(new PickerScreen.Option(job.Id,
                    $"{job.Name} - {JobListScreen.Where(colony, job)}"));
            }

            return options;
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
