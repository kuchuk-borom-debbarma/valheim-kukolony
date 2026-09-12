using System;
using System.Collections.Generic;
using Jotunn.Managers;
using Kukolony.Colonies;
using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Gui
{
    /// <summary>
    ///     What a colony is, and what it has.
    /// </summary>
    internal sealed class ColonyHomeScreen : ScreenView
    {
        internal override string Title => "Colony";

        internal override string Subtitle =>
            ColonyScreen.Instance != null && ColonyScreen.Instance.LookedAt != null
                ? "Looking at " + StructureRegistry.DisplayName(ColonyScreen.Instance.LookedAt)
                : "Looking at nothing";

        internal override bool StillValid(ColonyScreen host) => host.Colony != null;

        internal override void Build(ColonyScreen host, Column column)
        {
            Colony colony = host.Colony;
            ColonyState state = colony.State;

            if (column.TryRow(out Row name))
            {
                Widgets.Text(name, "Name", state.Name, value =>
                {
                    string trimmed = (value ?? string.Empty).Trim();
                    if (string.IsNullOrEmpty(trimmed))
                    {
                        Report.Say("A colony needs a name.");
                        host.Refresh();
                        return;
                    }

                    // Writing a colony's own record, so the colony must be ours to write.
                    if (colony.TryGetComponent(out ZNetView view) && view.IsValid())
                    {
                        view.ClaimOwnership();
                    }

                    colony.State.SetName(trimmed);
                    Report.Say($"Renamed to {trimmed}.");
                    host.Refresh();
                });
            }

            if (column.TryRow(out Row people))
            {
                Widgets.Caption(people, "Villagers");
                Widgets.Caption(people, state.CountMembers(ColonyMemberKind.Villager).ToString(), 80f);
                Widgets.Button(people, "Manage", 160f, () => host.Push(new VillagerListScreen()));
            }

            if (column.TryRow(out Row reach))
            {
                Widgets.Caption(reach, "Reach");
                Widgets.Label(reach, $"{colony.EffectiveRadius:F0} m");
            }

            List<StructureRecord> structures = state.GetStructures();
            if (column.TryRow(out Row structuresRow))
            {
                Widgets.Caption(structuresRow, "Structures");
                Widgets.Caption(structuresRow, structures.Count.ToString(), 80f);
                Widgets.Button(structuresRow, "Manage", 160f, () => host.Push(new StructureListScreen()));
            }

            if (column.TryRow(out Row jobsRow))
            {
                Widgets.Caption(jobsRow, "Jobs");
                Widgets.Caption(jobsRow, colony.State.GetJobs().Count.ToString(), 80f);
                Widgets.Button(jobsRow, "Manage", 160f, () => host.Push(new JobListScreen()));
            }

            // Registering what the player was looking at when the screen opened. Offered only
            // when there is something to offer: a row reading "Register nothing" would be
            // worse than the subtitle already saying they were looking at nothing.
            GameObject looked = host.LookedAt;
            if (looked != null && column.TryRow(out Row registerRow))
            {
                string what = StructureRegistry.DisplayName(looked);
                Widgets.Caption(registerRow, what, 300f);
                Widgets.Button(registerRow, "Register", 160f, () =>
                {
                    Report.Say(ColonyOperations.Explain(
                        ColonyOperations.Register(colony, looked), what, state.Name));
                    host.Refresh();
                });
            }

            if (column.TryRow(out Row nearbyRow))
            {
                Widgets.Button(nearbyRow, "Register something nearby", 300f,
                    () => host.Push(new RegisterNearbyScreen()));
            }

            if (column.TryRow(out Row switchRow))
            {
                Widgets.Button(switchRow, "Switch colony", 200f, () => host.Push(new ColonyListScreen()));
                Widgets.Button(switchRow, "Widget gallery", 200f, () => host.Push(new GalleryScreen()));
            }
        }
    }

    /// <summary>
    ///     Every colony currently loaded, so a player with two bases can manage either.
    /// </summary>
    internal sealed class ColonyListScreen : ScreenView
    {
        internal override string Title => "Switch colony";

        internal override void Build(ColonyScreen host, Column column)
        {
            foreach (Colony colony in Colony.Instances)
            {
                if (colony == null || !column.TryRow(out Row row))
                {
                    continue;
                }

                ColonyState state = colony.State;
                string label = string.IsNullOrEmpty(state.Name) ? "unnamed" : state.Name;
                bool current = colony == host.Colony;

                Widgets.Caption(row, label, 360f);
                Widgets.Caption(row, $"{state.CountMembers(ColonyMemberKind.Villager)} villagers", 180f);

                if (current)
                {
                    Widgets.Label(row, "current", GUIManager.Instance.ValheimOrange);
                    continue;
                }

                Colony chosen = colony;
                Widgets.Button(row, "Manage", 140f, () =>
                {
                    host.Open(chosen, host.LookedAt);
                    Report.Say($"Now managing {label}.");
                });
            }
        }
    }

    /// <summary>
    ///     One of many, or several of many, with search and paging.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         One picker serves both, because the only difference is whether choosing closes
    ///         it. Two pickers would drift, and the drift would be a multi-select that cannot
    ///         search.
    ///     </para>
    ///     <para>
    ///         Multi-select preserves the order things were chosen in. That is not decoration:
    ///         a fuel list is a preference order, and re-sorting it alphabetically on save
    ///         would silently change what a structure burns first.
    ///     </para>
    /// </remarks>
    internal sealed class PickerScreen : ScreenView
    {
        private readonly Func<string, List<Option>> _search;
        private readonly List<string> _chosen;
        private readonly bool _multiple;
        private readonly Action<List<string>> _commit;
        private string _filter = string.Empty;

        internal PickerScreen(string title, Func<string, List<Option>> search, IEnumerable<string> chosen,
            bool multiple, Action<List<string>> commit)
        {
            Title = title;
            _search = search;
            _chosen = chosen == null ? new List<string>() : new List<string>(chosen);
            _multiple = multiple;
            _commit = commit;
        }

        internal override string Title { get; }

        /// <summary>
        ///     Types into the search box, as the player would. Used by the screenshot phase,
        ///     which would otherwise photograph the empty state: an unfiltered catalogue
        ///     deliberately returns nothing, because a wall of every item in the game is not a
        ///     useful default.
        /// </summary>
        internal void SearchForTest(string filter) => _filter = filter ?? string.Empty;

        internal override string Subtitle => _multiple
            ? $"{_chosen.Count} chosen - order is kept"
            : null;

        internal override void Build(ColonyScreen host, Column column)
        {
            if (column.TryRow(out Row searchRow))
            {
                Widgets.Text(searchRow, "Search", _filter, value =>
                {
                    _filter = value ?? string.Empty;
                    host.Refresh();
                });
            }

            List<Option> results = _search(_filter) ?? new List<Option>();
            if (results.Count == 0 && column.TryRow(out Row empty))
            {
                Widgets.Label(empty, string.IsNullOrEmpty(_filter)
                    ? "Type to search."
                    : "Nothing matches.", Color.gray);
            }

            foreach (Option option in results)
            {
                if (!column.TryRow(out Row row))
                {
                    continue;
                }

                bool picked = _chosen.Contains(option.Id);
                Widgets.Caption(row, option.Label, 420f);

                Option captured = option;
                Widgets.Button(row, picked ? "Chosen" : "Choose", 150f, () =>
                {
                    if (_multiple)
                    {
                        if (picked)
                        {
                            _chosen.Remove(captured.Id);
                        }
                        else
                        {
                            _chosen.Add(captured.Id);
                        }

                        _commit(new List<string>(_chosen));
                        host.Refresh();
                        return;
                    }

                    _commit(new List<string> { captured.Id });
                    host.Pop();
                });
            }
        }

        /// <summary>One row's worth of choice: what is stored, and what is read.</summary>
        internal readonly struct Option
        {
            internal Option(string id, string label)
            {
                Id = id;
                Label = label;
            }

            internal string Id { get; }

            internal string Label { get; }
        }
    }

    /// <summary>
    ///     One of every widget kind, with values that belong to nobody.
    /// </summary>
    /// <remarks>
    ///     A widget with no caller has never been seen to render, and the settings these would
    ///     configure do not exist until milestones 3 and 4. This is what keeps the toolkit
    ///     honest in the meantime, and it is what the layout audit and the screenshot point at.
    ///     It is reachable from the colony screen rather than hidden behind a debug flag,
    ///     because a gallery nobody can open is a gallery nobody checks.
    /// </remarks>
    internal sealed class GalleryScreen : ScreenView
    {
        private static readonly string[] Modes = { "Nearest", "Furthest", "Anywhere" };

        private bool _flag = true;
        private float _number = 12f;
        private int _mode;
        private string _text = "Ashfell";
        private string _one = string.Empty;
        private List<string> _several = new List<string>();

        internal override string Title => "Widget gallery";

        internal override string Subtitle => "Every control the settlement can offer";

        internal override void Build(ColonyScreen host, Column column)
        {
            if (column.TryRow(out Row flag))
            {
                Widgets.Flag(flag, "A flag", _flag, value =>
                {
                    _flag = value;
                    host.Refresh();
                });
            }

            if (column.TryRow(out Row number))
            {
                Widgets.Number(number, "A number", _number, 0f, 64f, 4f, v => $"{v:F0} m", value =>
                {
                    _number = value;
                    host.Refresh();
                });
            }

            if (column.TryRow(out Row cycle))
            {
                Widgets.Cycle(cycle, "One of a few", Modes, _mode, value =>
                {
                    _mode = value;
                    host.Refresh();
                });
            }

            if (column.TryRow(out Row text))
            {
                Widgets.Text(text, "Free text", _text, value =>
                {
                    _text = value;
                    host.Refresh();
                });
            }

            if (column.TryRow(out Row one))
            {
                Widgets.Choice(one, "One of many", ItemLabel(_one), () => host.Push(new PickerScreen(
                    "Choose an item", SearchItems, new[] { _one }, false, chosen =>
                    {
                        _one = chosen.Count > 0 ? chosen[0] : string.Empty;
                        host.Refresh();
                    })));
            }

            if (column.TryRow(out Row several))
            {
                Widgets.Choice(several, "Several of many",
                    _several.Count == 0 ? string.Empty : $"{_several.Count} chosen",
                    () => host.Push(new PickerScreen("Choose items", SearchItems, _several, true, chosen =>
                    {
                        _several = chosen;
                        host.Refresh();
                    })));
            }

            if (column.TryRow(out Row action))
            {
                Widgets.Button(action, "Say something", 200f,
                    () => Report.Say("That is what a message looks like."));
            }

            // A list long enough to page, so paging is exercised by something a person can see
            // rather than only by a check.
            if (column.TryRow(out Row listHeading))
            {
                Widgets.Label(listHeading, "A list long enough to page", Color.gray);
            }

            for (int i = 1; i <= 24; i++)
            {
                if (!column.TryRow(out Row row))
                {
                    continue;
                }

                Widgets.Caption(row, $"Row {i}", 360f);
                Widgets.Label(row, i % 2 == 0 ? "even" : "odd", Color.gray);
            }
        }

        private static List<PickerScreen.Option> SearchItems(string filter)
        {
            List<PickerScreen.Option> options = new List<PickerScreen.Option>();
            foreach (ItemCatalogue.Entry entry in ItemCatalogue.Search(filter, 40))
            {
                options.Add(new PickerScreen.Option(entry.PrefabName, entry.DisplayName));
            }

            return options;
        }

        private static string ItemLabel(string prefab) =>
            string.IsNullOrEmpty(prefab) ? string.Empty : ItemCatalogue.Label(prefab);
    }
}
