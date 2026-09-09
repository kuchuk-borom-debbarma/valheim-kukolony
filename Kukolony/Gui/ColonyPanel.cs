using System.Collections.Generic;
using Jotunn.Managers;
using Kukolony.Colonies;
using Kukolony.Core;
using Kukolony.Villagers;
using UnityEngine;
using UnityEngine.UI;

namespace Kukolony.Gui
{
    /// <summary>
    ///     Manage a colony: name it, spawn villagers, add buildings, and assign each
    ///     villager a home and a workstation.
    ///
    ///     A front end over ZDO fields the AI already reads, so nothing here changes
    ///     behaviour - it is the only way a player can set what previously needed code.
    ///     Villagers are addressed by ZDOID throughout, so ones that are nowhere near a
    ///     player still appear and can still be assigned.
    ///
    ///     Same three rules WorkPostPanel had to get right: claim ownership before
    ///     writing, pair every BlockInput, and rebuild on scene change.
    /// </summary>
    internal sealed class ColonyPanel : MonoBehaviour
    {
        private const int VillagerRows = 4;
        private const int PickerRows = 5;

        /// <summary>What the shared picker strip is currently offering.</summary>
        private enum PickerMode
        {
            Hidden,
            AddNearby,
            AssignHome,
            AssignStation
        }

        internal static ColonyPanel Instance { get; private set; }

        private GameObject _root;
        private InputField _nameField;
        private Text _counts;
        private Text _cost;
        private Text _hint;
        private readonly List<Text> _villagerLabels = new List<Text>();
        private readonly List<Button> _homeButtons = new List<Button>();
        private readonly List<Button> _workButtons = new List<Button>();
        private readonly List<Button> _pickerButtons = new List<Button>();

        private Colony _colony;
        private int _page;
        private PickerMode _mode = PickerMode.Hidden;
        private ColonyMemberKind _addingKind = ColonyMemberKind.Container;
        private ZDOID _subject = ZDOID.None;
        private bool _inputBlocked;

        internal bool IsOpen => _root != null && _root.activeSelf;

        internal static void Register() => GUIManager.OnCustomGUIAvailable += Rebuild;

        private static void Rebuild()
        {
            if (Instance != null)
            {
                Object.Destroy(Instance.gameObject);
            }

            GameObject holder = new GameObject("KukolonyColonyPanel");
            holder.transform.SetParent(GUIManager.CustomGUIFront.transform, worldPositionStays: false);
            Instance = holder.AddComponent<ColonyPanel>();
            Instance.Build();
        }

        internal void Open(Colony colony)
        {
            if (_root == null || colony == null)
            {
                return;
            }

            _colony = colony;
            _page = 0;
            _mode = PickerMode.Hidden;
            _subject = ZDOID.None;

            if (colony.TryGetComponent(out ZNetView nview) && nview.IsValid())
            {
                nview.ClaimOwnership();
            }

            colony.EnsureNamed();

            // A destroyed bed would otherwise linger in the picker. Prune on open, when
            // it is cheap and the player is about to look at the list.
            ColonyState state = colony.State;
            if (state.IsValid)
            {
                foreach (ColonyMemberKind kind in new[]
                         {
                             ColonyMemberKind.Villager, ColonyMemberKind.Container,
                             ColonyMemberKind.Station, ColonyMemberKind.Home
                         })
                {
                    state.PruneMissing(kind);
                }
            }

            RefreshAll();
            _root.SetActive(true);
            SetInputBlocked(true);
        }

        internal void Close()
        {
            if (_root != null)
            {
                _root.SetActive(false);
            }

            _colony = null;
            SetInputBlocked(false);
        }

        /// <summary>
        ///     BlockInput is refcounted, so an unpaired open leaves the player unable to
        ///     move. Tracking our own state means a double open or close cannot leak one.
        /// </summary>
        private void SetInputBlocked(bool blocked)
        {
            if (_inputBlocked == blocked)
            {
                return;
            }

            _inputBlocked = blocked;
            GUIManager.BlockInput(blocked);
        }

        private void OnDestroy()
        {
            SetInputBlocked(false);
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            if (IsOpen && Input.GetKeyDown(KeyCode.Escape))
            {
                Close();
            }
        }

        private void Build()
        {
            _root = GUIManager.Instance.CreateWoodpanel(
                transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 0f), 580f, 740f, draggable: true);
            _root.SetActive(false);

            Title("Colony", -26f);

            GameObject nameField = GUIManager.Instance.CreateInputField(_root.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -62f),
                InputField.ContentType.Standard, "colony name", 16, 380f, 30f);
            _nameField = nameField.GetComponent<InputField>();
            _nameField.onEndEdit.AddListener(ApplyName);

            _counts = Label(string.Empty, -96f, 15, Color.white);
            _cost = Label(string.Empty, -118f, 13, Color.grey);

            AddNearbyButton("+ storage", ColonyMemberKind.Container, -200f);
            AddNearbyButton("+ stations", ColonyMemberKind.Station, -100f);
            AddNearbyButton("+ homes", ColonyMemberKind.Home, 0f);

            GameObject spawn = GUIManager.Instance.CreateButton("+ villager", _root.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(100f, -150f), 130f, 28f);
            spawn.GetComponent<Button>().onClick.AddListener(SpawnVillager);

            BuildVillagerRows();
            BuildPicker();

            GameObject close = GUIManager.Instance.CreateButton("Close", _root.transform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 32f), 140f, 32f);
            close.GetComponent<Button>().onClick.AddListener(Close);
        }

        private void AddNearbyButton(string text, ColonyMemberKind kind, float x)
        {
            GameObject button = GUIManager.Instance.CreateButton(text, _root.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(x, -150f), 130f, 28f);
            button.GetComponent<Button>().onClick.AddListener(() => ShowAddNearby(kind));
        }

        private void BuildVillagerRows()
        {
            Title("Villagers", -196f);

            for (int i = 0; i < VillagerRows; i++)
            {
                float y = -226f - i * 34f;
                int row = i;

                _villagerLabels.Add(GUIManager.Instance.CreateText(string.Empty, _root.transform,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(190f, y),
                    GUIManager.Instance.AveriaSerifBold, 15, Color.white,
                    true, Color.black, 340f, 26f, false).GetComponent<Text>());

                GameObject home = GUIManager.Instance.CreateButton("home", _root.transform,
                    new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-146f, y), 84f, 26f);
                home.GetComponent<Button>().onClick.AddListener(() => BeginAssign(row, PickerMode.AssignHome));
                _homeButtons.Add(home.GetComponent<Button>());

                GameObject work = GUIManager.Instance.CreateButton("work", _root.transform,
                    new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-56f, y), 84f, 26f);
                work.GetComponent<Button>().onClick.AddListener(() => BeginAssign(row, PickerMode.AssignStation));
                _workButtons.Add(work.GetComponent<Button>());
            }

            GameObject prev = GUIManager.Instance.CreateButton("< prev", _root.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-80f, -374f), 100f, 26f);
            prev.GetComponent<Button>().onClick.AddListener(() => ChangePage(-1));

            GameObject next = GUIManager.Instance.CreateButton("next >", _root.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(80f, -374f), 100f, 26f);
            next.GetComponent<Button>().onClick.AddListener(() => ChangePage(1));
        }

        private void BuildPicker()
        {
            _hint = Label(string.Empty, -418f, 14, GUIManager.Instance.ValheimOrange);

            for (int i = 0; i < PickerRows; i++)
            {
                GameObject row = GUIManager.Instance.CreateButton(string.Empty, _root.transform,
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -448f - i * 30f), 500f, 28f);
                row.SetActive(false);
                _pickerButtons.Add(row.GetComponent<Button>());
            }
        }

        private void Title(string text, float y)
        {
            GUIManager.Instance.CreateText(text, _root.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y),
                GUIManager.Instance.AveriaSerifBold, 18, GUIManager.Instance.ValheimOrange,
                true, Color.black, 460f, 26f, false);
        }

        private Text Label(string text, float y, int size, Color colour)
        {
            return GUIManager.Instance.CreateText(text, _root.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y),
                GUIManager.Instance.AveriaSerifBold, size, colour,
                true, Color.black, 520f, 24f, false).GetComponent<Text>();
        }

        private List<ZDOID> CurrentVillagers() =>
            _colony != null && _colony.State.IsValid
                ? _colony.State.GetMembers(ColonyMemberKind.Villager)
                : new List<ZDOID>();

        private void ChangePage(int delta)
        {
            int pages = Mathf.Max(1, Mathf.CeilToInt(CurrentVillagers().Count / (float)VillagerRows));
            _page = Mathf.Clamp(_page + delta, 0, pages - 1);
            RefreshAll();
        }

        private void BeginAssign(int row, PickerMode mode)
        {
            List<ZDOID> villagers = CurrentVillagers();
            int index = _page * VillagerRows + row;
            if (index >= villagers.Count)
            {
                return;
            }

            _subject = villagers[index];
            _mode = mode;
            RefreshPicker();
        }

        private void ShowAddNearby(ColonyMemberKind kind)
        {
            _addingKind = kind;
            _mode = PickerMode.AddNearby;
            _subject = ZDOID.None;
            RefreshPicker();
        }

        private void ApplyName(string value)
        {
            if (_colony == null || string.IsNullOrEmpty(value))
            {
                return;
            }

            ColonyState state = _colony.State;
            if (state.IsValid)
            {
                state.SetName(value);
                RefreshAll();
            }
        }

        private void SpawnVillager()
        {
            if (_colony == null || ZNetScene.instance == null)
            {
                return;
            }

            GameObject prefab = ZNetScene.instance.GetPrefab(VillagerPrefab.PrefabName);
            if (prefab == null)
            {
                Log.Error("Cannot spawn - villager prefab is not registered.");
                return;
            }

            Vector3 position = _colony.transform.position + _colony.transform.forward * 2f + Vector3.up;
            GameObject spawned = Object.Instantiate(prefab, position, Quaternion.identity);

            if (spawned != null && spawned.TryGetComponent(out ZNetView view))
            {
                _colony.Register(ColonyMemberKind.Villager, view);
            }

            RefreshAll();
        }

        private void RefreshAll()
        {
            if (_colony == null)
            {
                return;
            }

            ColonyState state = _colony.State;
            if (!state.IsValid)
            {
                return;
            }

            if (_nameField != null && !_nameField.isFocused)
            {
                _nameField.text = state.Name;
            }

            _counts.text =
                $"Villagers {state.CountMembers(ColonyMemberKind.Villager)}    "
                + $"Storage {state.CountMembers(ColonyMemberKind.Container)}    "
                + $"Workstations {state.CountMembers(ColonyMemberKind.Station)}    "
                + $"Homes {state.CountMembers(ColonyMemberKind.Home)}";

            // The colony has no radius, so nothing stops a member being assigned a long
            // way off. Rather than forbid it, show what it costs.
            _cost.text = $"holding {KeepAlive.KeepAliveZones.Count} of "
                         + $"{ModConfig.KeepAliveMaxZones.Value} zones loaded";

            RefreshVillagerRows(state);
            RefreshPicker();
        }

        private void RefreshVillagerRows(ColonyState state)
        {
            List<ZDOID> villagers = state.GetMembers(ColonyMemberKind.Villager);

            for (int i = 0; i < VillagerRows; i++)
            {
                int index = _page * VillagerRows + i;
                bool used = index < villagers.Count;

                _villagerLabels[i].text = used
                    ? ColonyAssignments.DescribeVillager(state, villagers[index])
                    : string.Empty;

                _homeButtons[i].gameObject.SetActive(used);
                _workButtons[i].gameObject.SetActive(used);
            }
        }

        private void RefreshPicker()
        {
            if (_colony == null)
            {
                return;
            }

            ColonyState state = _colony.State;
            switch (_mode)
            {
                case PickerMode.AddNearby:
                    ShowNearbyCandidates();
                    return;
                case PickerMode.AssignHome:
                    ShowMemberChoices(state, ColonyMemberKind.Home);
                    return;
                case PickerMode.AssignStation:
                    ShowMemberChoices(state, ColonyMemberKind.Station);
                    return;
                default:
                    HidePicker();
                    return;
            }
        }

        private void HidePicker()
        {
            _hint.text = string.Empty;
            foreach (Button button in _pickerButtons)
            {
                button.gameObject.SetActive(false);
            }
        }

        private void ShowNearbyCandidates()
        {
            Vector3 around = Player.m_localPlayer != null
                ? Player.m_localPlayer.transform.position
                : _colony.transform.position;

            List<NearbyMembers.Candidate> candidates = NearbyMembers.Find(_addingKind, around);
            _hint.text = candidates.Count == 0
                ? $"No {NearbyMembers.KindLabel(_addingKind).ToLower()} near you"
                : $"Add {NearbyMembers.KindLabel(_addingKind).ToLower()} to the colony:";

            for (int i = 0; i < _pickerButtons.Count; i++)
            {
                Button button = _pickerButtons[i];
                if (i >= candidates.Count)
                {
                    button.gameObject.SetActive(false);
                    continue;
                }

                NearbyMembers.Candidate candidate = candidates[i];
                Bind(button, $"+ {NearbyMembers.Describe(candidate)}", () =>
                {
                    if (_colony.Register(_addingKind, candidate.View))
                    {
                        Log.Info($"Added {candidate.Label} to colony '{_colony.State.Name}'");
                    }

                    RefreshAll();
                });
            }
        }

        /// <summary>
        ///     Offers only what the colony already owns. Adding and assigning stay two
        ///     distinct steps, so the colony never grows as a side effect of assignment.
        /// </summary>
        private void ShowMemberChoices(ColonyState state, ColonyMemberKind kind)
        {
            if (_subject.IsNone())
            {
                HidePicker();
                return;
            }

            List<ZDOID> members = state.GetMembers(kind);
            string what = kind == ColonyMemberKind.Home ? "home" : "workstation";
            _hint.text = $"Choose a {what} for {ColonyAssignments.NameOf(_subject)}:";

            // First row always clears, so an assignment can be undone.
            int row = 0;
            Bind(_pickerButtons[row++], "- none -", () =>
            {
                if (kind == ColonyMemberKind.Home)
                {
                    ColonyAssignments.ClearHome(_subject);
                }
                else
                {
                    ColonyAssignments.ClearStation(_subject);
                }

                _mode = PickerMode.Hidden;
                RefreshAll();
            });

            for (int i = 0; i < members.Count && row < _pickerButtons.Count; i++, row++)
            {
                ZDOID member = members[i];
                Bind(_pickerButtons[row], ColonyAssignments.DescribeChoice(state, kind, member, i), () =>
                {
                    if (kind == ColonyMemberKind.Home)
                    {
                        ColonyAssignments.AssignHome(state, _subject, member);
                    }
                    else
                    {
                        ColonyAssignments.AssignStation(_subject, member);
                    }

                    _mode = PickerMode.Hidden;
                    RefreshAll();
                });
            }

            for (; row < _pickerButtons.Count; row++)
            {
                _pickerButtons[row].gameObject.SetActive(false);
            }
        }

        private static void Bind(Button button, string label, UnityEngine.Events.UnityAction action)
        {
            button.gameObject.SetActive(true);
            button.GetComponentInChildren<Text>().text = label;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }
    }
}
