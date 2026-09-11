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

        internal override string Subtitle => "Anything the colony understands, within reach";

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
