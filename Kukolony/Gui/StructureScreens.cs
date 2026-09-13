using System.Collections.Generic;
using Kukolony.Colonies;
using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Gui
{
    /// <summary>
    ///     Everything the colony has registered, and the way into each one.
    /// </summary>
    internal sealed class StructureListScreen : ScreenView
    {
        internal override string Title => "Structures";

        internal override bool StillValid(ColonyScreen host) => host.Colony != null;

        internal override void Build(ColonyScreen host, Column column)
        {
            Colony colony = host.Colony;
            List<StructureRecord> records = colony.State.GetStructures();

            if (records.Count == 0 && column.TryRow(out Row empty))
            {
                Widgets.Label(empty, "Nothing registered yet.", Color.gray);
            }

            foreach (StructureRecord record in records)
            {
                if (!column.TryRow(out Row row))
                {
                    continue;
                }

                StructureStatus status = record.StatusIn(colony);
                Widgets.Caption(row, record.Name, 260f);
                Widgets.Caption(row, StructureCapabilities.Describe(record.Capabilities), 190f);
                Widgets.Caption(row, StructureStatusText.Label(status), 120f,
                    StructureStatusText.Colour(status));

                StructureRecord chosen = record;
                Widgets.Button(row, "Open", 110f, () => host.Push(new StructureDetailScreen(chosen.PersistentId, chosen.Id)));
            }

            if (column.TryRow(out Row add))
            {
                Widgets.Button(add, "Register something nearby", 300f,
                    () => host.Push(new RegisterNearbyScreen()));
            }
        }
    }

    /// <summary>
    ///     One structure: what the colony understands it to be, its name, and removal.
    /// </summary>
    /// <remarks>
    ///     Keyed on the durable token rather than the runtime address, because the address is
    ///     only valid for as long as the object stays loaded and this screen outlives that.
    ///     The address is kept as a fallback for records old enough to have no token.
    /// </remarks>
    internal sealed class StructureDetailScreen : ScreenView
    {
        private readonly string _token;
        private readonly ZDOID _fallback;

        internal StructureDetailScreen(string token, ZDOID fallback)
        {
            _token = token ?? string.Empty;
            _fallback = fallback;
        }

        internal override string Title => "Structure";

        internal override bool StillValid(ColonyScreen host) => host.Colony != null;

        internal override void Build(ColonyScreen host, Column column)
        {
            Colony colony = host.Colony;
            StructureRecord record = Find(colony);

            if (record == null)
            {
                if (column.TryRow(out Row gone))
                {
                    Widgets.Label(gone, "This structure is no longer registered.", Color.gray);
                }

                return;
            }

            if (column.TryRow(out Row name))
            {
                Widgets.Text(name, "Name", record.Name, value =>
                {
                    string trimmed = (value ?? string.Empty).Trim();
                    if (trimmed.Length == 0)
                    {
                        Report.Say("A structure needs a name.");
                        host.Refresh();
                        return;
                    }

                    ColonyOperations.RenameStructure(colony, record.Id, trimmed);
                    Report.Say($"Renamed to {trimmed}.");
                    host.Refresh();
                });
            }

            if (column.TryRow(out Row kind))
            {
                Widgets.Caption(kind, "Registered as");
                Widgets.Label(kind, StructureCapabilities.Describe(record.Capabilities));
            }

            StructureSettings settings = record.Settings;
            if ((record.Capabilities & StructureCapability.Storage) != 0)
            {
                BuildStorage(host, column, colony, record, settings);
            }

            if ((record.Capabilities & StructureCapability.Processing) != 0)
            {
                BuildProcessing(host, column, colony, record, settings);
            }

            if ((record.Capabilities & StructureCapability.Rest) != 0)
            {
                BuildRest(host, column, colony, record, settings);
            }

            StructureStatus status = record.StatusIn(colony);
            if (column.TryRow(out Row state))
            {
                Widgets.Caption(state, "Status");
                Widgets.Label(state, StructureStatusText.Label(status), StructureStatusText.Colour(status));
            }

            if (column.TryRow(out Row kindOf))
            {
                Widgets.Caption(kindOf, "What it is");
                Widgets.Label(kindOf, record.Prefab, Color.gray);
            }

            // A structure that cannot be found here may simply not have been replicated to this
            // peer. Removing on that evidence is the loss the whole "never infer destruction
            // from absence" rule exists to prevent - routed through a person rather than code,
            // which makes it no less permanent.
            bool guessing = status == StructureStatus.NotFound &&
                            ZNet.instance != null && !ZNet.instance.IsServer();
            if (guessing && column.TryRow(out Row warn))
            {
                Widgets.Label(warn, "Not loaded here - it may still exist.", Color.gray);
            }

            if (column.TryRow(out Row remove))
            {
                string label = guessing ? "Remove anyway" : "Remove";
                Widgets.Button(remove, label, 220f, () =>
                {
                    colony.RemoveStructure(record.Id);
                    Report.Say($"Removed {record.Name} from {colony.State.Name}.");
                    host.Pop();
                });
            }
        }

        /// <summary>
        ///     What belongs here, and whether the settlement may take from it.
        /// </summary>
        /// <remarks>
        ///     An empty list means <em>anything</em>, and says so. That is what an overflow
        ///     chest is, so the empty state is a real setting rather than one nobody has filled
        ///     in yet - and a row reading "nothing" would describe a chest the settlement can
        ///     never use.
        /// </remarks>
        private static void BuildStorage(ColonyScreen host, Column column, Colony colony,
            StructureRecord record, StructureSettings settings)
        {
            if (column.TryRow(out Row holds))
            {
                Widgets.Choice(holds, "Holds",
                    settings.Accepts.Count == 0 ? "anything" : Summarise(settings.Accepts),
                    () => host.Push(new PickerScreen("What belongs here", SearchItems,
                        settings.Accepts, true, chosen =>
                        {
                            ColonyOperations.EditSettings(colony, record.Id, s => s.Accepts = chosen);
                            host.Refresh();
                        })));
            }

            if (column.TryRow(out Row take))
            {
                Widgets.Flag(take, "May take from", settings.MayTakeFrom, value =>
                {
                    ColonyOperations.EditSettings(colony, record.Id, s => s.MayTakeFrom = value);
                    host.Refresh();
                });
            }

            if (column.TryRow(out Row dump))
            {
                Widgets.Flag(dump, "Take unclaimed items", settings.TakeUnclaimed, value =>
                {
                    ColonyOperations.EditSettings(colony, record.Id, s => s.TakeUnclaimed = value);
                    host.Refresh();
                });
            }

            // A cap row per item this chest was told to hold - and per item that still has a
            // cap, whether or not it is still named.
            //
            // A cap is stored beside the list rather than in it, so taking an item out of the
            // list used to leave its cap enforced by the index with no row anywhere able to
            // show or clear it: a chest emptied of its list became an overflow chest that
            // silently refused wood past ten. Pruning the cap on write was tried and is
            // worse - the picker commits on every toggle, so retyping a list destroyed the
            // caps of items about to be put straight back, and clearing the last item wiped
            // every cap in one tap. Showing the orphan is what lets a person decide.
            foreach (string item in CappedOrAccepted(settings))
            {
                if (!column.TryRow(out Row cap)) continue;

                string named = item;
                int amount = settings.CapFor(named);
                Widgets.Number(cap, "At most " + ItemCatalogue.Label(named), amount < 0 ? 0 : amount,
                    0f, 9999f, 10f,
                    value => value <= 0f ? "no limit" : ((int)value).ToString(),
                    value =>
                    {
                        ColonyOperations.EditSettings(colony, record.Id,
                            s => s.SetCap(named, value <= 0f ? -1 : (int)value));

                        // After the edit returns, not inside it. EditSettings mutates the
                        // decoded record and only writes it back on the way out, while the
                        // rebuild decodes fresh from the ZDO - so refreshing from within the
                        // mutator drew the value as it was before the change. The row then
                        // showed the old number, the nudge closure carried the old number,
                        // and the cap could never move more than one step from where it
                        // started.
                        host.Refresh();
                    });
            }
        }

        /// <summary>
        ///     What to keep a station fed with, offered from what it actually accepts.
        /// </summary>
        /// <remarks>
        ///     Both lists come from the prefab, so this works for a station that is nowhere
        ///     near the player. A station with no fuel item shows no fuel row at all rather
        ///     than an empty one: a charcoal kiln burns nothing, and offering to configure its
        ///     fuel would be offering a setting the structure ignores.
        /// </remarks>
        /// <summary>
        ///     Everything this container needs a cap row for: what it accepts, and anything
        ///     that still carries a cap from when it did.
        /// </summary>
        private static List<string> CappedOrAccepted(StructureSettings settings)
        {
            List<string> named = new List<string>(settings.Accepts);
            foreach (KeyValuePair<string, int> cap in settings.Caps)
            {
                if (!named.Contains(cap.Key)) named.Add(cap.Key);
            }

            return named;
        }

        private static void BuildProcessing(ColonyScreen host, Column column, Colony colony,
            StructureRecord record, StructureSettings settings)
        {
            string fuel = ProcessingOptions.Fuel(record.Prefab);
            if (fuel.Length > 0 && column.TryRow(out Row fuelRow))
            {
                bool keepFuelled = settings.Fuel.Contains(fuel);
                Widgets.Flag(fuelRow, "Keep fuelled with " + ItemCatalogue.Label(fuel), keepFuelled, value =>
                {
                    ColonyOperations.EditSettings(colony, record.Id, s =>
                        s.Fuel = value ? new List<string> { fuel } : new List<string>());
                    host.Refresh();
                });
            }

            List<string> inputs = ProcessingOptions.Inputs(record.Prefab);
            if (inputs.Count > 0 && column.TryRow(out Row inputRow))
            {
                Widgets.Choice(inputRow, "Feed it",
                    settings.Input.Count == 0 ? "nothing" : Summarise(settings.Input),
                    () => host.Push(new PickerScreen("What to feed it",
                        filter => Options(inputs, filter), settings.Input, true, chosen =>
                        {
                            ColonyOperations.EditSettings(colony, record.Id, s => s.Input = chosen);
                            host.Refresh();
                        })));
            }

            // Only where "how full" can mean anything. A fermenter takes one batch and is then
            // busy for days, so the row rendered "50% (0)" - a number that told the player to
            // keep it empty, for a setting its protocol does not read. A job must not offer a
            // setting it ignores, and neither must a structure.
            int capacity = ProcessingOptions.MaxInput(record.Prefab);
            if (capacity > 1 && column.TryRow(out Row full))
            {
                // A fraction, shown as the count it works out to, because "half full" is the
                // durable intent and "5 ore" is what the player can picture.
                int max = capacity;
                Widgets.Number(full, "Keep it", settings.KeepFull, 0f, 1f, .25f,
                    v => max > 0 ? $"{v * 100f:F0}% ({Mathf.RoundToInt(v * max)})" : $"{v * 100f:F0}%",
                    value =>
                    {
                        ColonyOperations.EditSettings(colony, record.Id, s => s.KeepFull = value);
                        host.Refresh();
                    });
            }
        }

        /// <summary>
        ///     Who sleeps here. One villager, and assigning a bed that is taken moves them.
        /// </summary>
        private static void BuildRest(ColonyScreen host, Column column, Colony colony,
            StructureRecord record, StructureSettings settings)
        {
            if (!column.TryRow(out Row row)) return;

            Widgets.Choice(row, "Sleeps here", Colonies.VillagerRoster.Name(settings.Sleeper),
                () => host.Push(new PickerScreen("Who sleeps here",
                    filter => Colonies.VillagerRoster.Options(colony, filter),
                    settings.HasSleeper ? new[] { settings.Sleeper.ToString() } : null,
                    false, chosen =>
                    {
                        Colonies.VillagerRoster.Assign(colony, record, chosen);
                        host.Refresh();
                    })));
        }

        private static string Summarise(List<string> items) =>
            items.Count == 1 ? ItemCatalogue.Label(items[0]) : $"{items.Count} kinds";

        private static List<PickerScreen.Option> SearchItems(string filter)
        {
            List<PickerScreen.Option> options = new List<PickerScreen.Option>();
            foreach (ItemCatalogue.Entry entry in ItemCatalogue.Search(filter, 40))
                options.Add(new PickerScreen.Option(entry.PrefabName, entry.DisplayName));
            return options;
        }

        /// <summary>
        ///     A fixed set of choices, filtered. Unlike the item catalogue an empty filter
        ///     shows everything, because "everything this kiln takes" is a short list and
        ///     hiding it until the player types would be hiding the answer.
        /// </summary>
        private static List<PickerScreen.Option> Options(List<string> prefabs, string filter)
        {
            List<PickerScreen.Option> options = new List<PickerScreen.Option>();
            foreach (string prefab in prefabs)
            {
                string label = ItemCatalogue.Label(prefab);
                if (filter.Length > 0 &&
                    label.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0 &&
                    prefab.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                options.Add(new PickerScreen.Option(prefab, label));
            }

            return options;
        }

        private StructureRecord Find(Colony colony)
        {
            List<StructureRecord> records = colony.State.GetStructures();
            if (_token.Length > 0)
            {
                StructureRecord byToken = records.Find(r => r.PersistentId == _token);
                if (byToken != null) return byToken;
            }

            return _fallback.IsNone() ? null : records.Find(r => r.Id == _fallback);
        }
    }

    /// <summary>
    ///     Everything nearby that could be registered, and what each would become.
    /// </summary>
    internal sealed class RegisterNearbyScreen : ScreenView
    {
        private string _filter = string.Empty;

        internal override string Title => "Register nearby";

        internal override string Subtitle => "Anything the Kolony understands, within reach";

        internal override bool StillValid(ColonyScreen host) => host.Colony != null;

        internal override void Build(ColonyScreen host, Column column)
        {
            Colony colony = host.Colony;

            if (column.TryRow(out Row search))
            {
                Widgets.Text(search, "Search", _filter, value =>
                {
                    _filter = value ?? string.Empty;
                    host.Refresh();
                });
            }

            List<StructureRecord> candidates = StructureRegistry.FindRegisterable(colony);
            List<ZDOID> registered = new List<ZDOID>();
            foreach (StructureRecord held in colony.State.GetStructures()) registered.Add(held.Id);

            int offered = 0;
            foreach (StructureRecord candidate in candidates)
            {
                if (registered.Contains(candidate.Id)) continue;
                if (_filter.Length > 0 &&
                    candidate.Name.IndexOf(_filter, System.StringComparison.OrdinalIgnoreCase) < 0 &&
                    candidate.Prefab.IndexOf(_filter, System.StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                offered++;
                if (!column.TryRow(out Row row)) continue;

                Widgets.Caption(row, candidate.Name, 300f);
                Widgets.Caption(row, StructureCapabilities.Describe(candidate.Capabilities), 200f);

                StructureRecord chosen = candidate;
                Widgets.Button(row, "Register", 150f, () =>
                {
                    GameObject instance = ZNetScene.instance != null
                        ? ZNetScene.instance.FindInstance(chosen.Id)
                        : null;
                    Report.Say(ColonyOperations.Explain(
                        ColonyOperations.Register(colony, instance), chosen.Name, colony.State.Name));
                    host.Refresh();
                });
            }

            if (offered == 0 && column.TryRow(out Row none))
            {
                Widgets.Label(none, _filter.Length > 0
                    ? "Nothing nearby matches."
                    : "Nothing nearby left to register.", Color.gray);
            }
        }
    }

    /// <summary>
    ///     What a status reads as. Explicit, with no branch that invents a plausible word for a
    ///     case nobody handled.
    /// </summary>
    internal static class StructureStatusText
    {
        internal static string Label(StructureStatus status)
        {
            switch (status)
            {
                case StructureStatus.Ready: return "ready";
                case StructureStatus.OutOfReach: return "out of reach";
                case StructureStatus.NotFound: return "not found";
                default: return string.Empty;
            }
        }

        internal static Color Colour(StructureStatus status)
        {
            switch (status)
            {
                case StructureStatus.Ready: return Color.green;
                case StructureStatus.NotFound: return Color.red;
                default: return Color.gray;
            }
        }
    }
}
