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
    ///     Manage a colony: name it, spawn villagers, and assign storage, workstations and
    ///     homes to it.
    ///
    ///     A front end over the colony's ZDO, like WorkPostPanel is over a post's. Same
    ///     three rules that panel had to get right: claim ownership before writing, pair
    ///     every BlockInput, and rebuild on scene change because CustomGUIFront is
    ///     recreated per scene.
    /// </summary>
    internal sealed class ColonyPanel : MonoBehaviour
    {
        private const int MemberRows = 4;

        internal static ColonyPanel Instance { get; private set; }

        private GameObject _root;
        private InputField _nameField;
        private Text _summary;
        private Text _cost;
        private readonly Dictionary<ColonyMemberKind, Text> _counts = new Dictionary<ColonyMemberKind, Text>();
        private readonly List<Button> _candidateButtons = new List<Button>();

        private Colony _colony;
        private ColonyMemberKind _addingKind = ColonyMemberKind.Container;
        private bool _inputBlocked;

        internal bool IsOpen => _root != null && _root.activeSelf;

        internal static void Register()
        {
            GUIManager.OnCustomGUIAvailable += Rebuild;
        }

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

            // Membership writes touch the colony's ZDO, and a non-owner write is clobbered
            // on the next sync.
            if (colony.TryGetComponent(out ZNetView nview) && nview.IsValid())
            {
                nview.ClaimOwnership();
            }

            colony.EnsureNamed();
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
                new Vector2(0f, 0f), 540f, 560f, draggable: true);
            _root.SetActive(false);

            GUIManager.Instance.CreateText("Colony", _root.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -28f),
                GUIManager.Instance.AveriaSerifBold, 22, GUIManager.Instance.ValheimOrange,
                true, Color.black, 400f, 30f, false);

            GameObject nameField = GUIManager.Instance.CreateInputField(_root.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -66f),
                InputField.ContentType.Standard, "colony name", 16, 380f, 32f);
            _nameField = nameField.GetComponent<InputField>();
            _nameField.onEndEdit.AddListener(ApplyName);

            _summary = Label(string.Empty, -104f, 16, Color.white);
            _cost = Label(string.Empty, -128f, 14, Color.grey);

            BuildMemberRow(ColonyMemberKind.Villager, -166f);
            BuildMemberRow(ColonyMemberKind.Container, -206f);
            BuildMemberRow(ColonyMemberKind.Station, -246f);
            BuildMemberRow(ColonyMemberKind.Home, -286f);

            GameObject spawn = GUIManager.Instance.CreateButton("Spawn villager", _root.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -330f), 200f, 32f);
            spawn.GetComponent<Button>().onClick.AddListener(SpawnVillager);

            for (int i = 0; i < MemberRows; i++)
            {
                GameObject row = GUIManager.Instance.CreateButton(string.Empty, _root.transform,
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -376f - i * 30f), 460f, 28f);
                row.SetActive(false);
                _candidateButtons.Add(row.GetComponent<Button>());
            }

            GameObject close = GUIManager.Instance.CreateButton("Close", _root.transform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 34f), 140f, 34f);
            close.GetComponent<Button>().onClick.AddListener(Close);
        }

        private Text Label(string text, float y, int size, Color colour)
        {
            return GUIManager.Instance.CreateText(text, _root.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y),
                GUIManager.Instance.AveriaSerifBold, size, colour,
                true, Color.black, 480f, 24f, false).GetComponent<Text>();
        }

        private void BuildMemberRow(ColonyMemberKind kind, float y)
        {
            _counts[kind] = GUIManager.Instance.CreateText(string.Empty, _root.transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(170f, y),
                GUIManager.Instance.AveriaSerifBold, 16, Color.white,
                true, Color.black, 260f, 24f, false).GetComponent<Text>();

            if (kind == ColonyMemberKind.Villager)
            {
                return;
            }

            GameObject add = GUIManager.Instance.CreateButton("Add nearby", _root.transform,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-110f, y), 150f, 28f);
            add.GetComponent<Button>().onClick.AddListener(() => ShowCandidates(kind));
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
            if (_colony == null || Player.m_localPlayer == null || ZNetScene.instance == null)
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

        private void ShowCandidates(ColonyMemberKind kind)
        {
            _addingKind = kind;

            Vector3 around = Player.m_localPlayer != null
                ? Player.m_localPlayer.transform.position
                : _colony.transform.position;

            List<NearbyMembers.Candidate> candidates = NearbyMembers.Find(kind, around);

            for (int i = 0; i < _candidateButtons.Count; i++)
            {
                Button button = _candidateButtons[i];
                if (i >= candidates.Count)
                {
                    button.gameObject.SetActive(false);
                    continue;
                }

                NearbyMembers.Candidate candidate = candidates[i];
                button.gameObject.SetActive(true);
                button.GetComponentInChildren<Text>().text =
                    $"+ {NearbyMembers.Describe(candidate)}";
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => AddMember(candidate));
            }

            _summary.text = candidates.Count == 0
                ? $"No {NearbyMembers.KindLabel(kind).ToLower()} near you to add."
                : $"Adding {NearbyMembers.KindLabel(kind).ToLower()} - pick one:";
        }

        private void AddMember(NearbyMembers.Candidate candidate)
        {
            if (_colony == null || candidate.View == null)
            {
                return;
            }

            if (_colony.Register(_addingKind, candidate.View))
            {
                Log.Info($"Added {candidate.Label} to colony '{_colony.State.Name}'");
            }

            ShowCandidates(_addingKind);
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

            foreach (KeyValuePair<ColonyMemberKind, Text> entry in _counts)
            {
                entry.Value.text = $"{NearbyMembers.KindLabel(entry.Key)}: {state.CountMembers(entry.Key)}";
            }

            // The colony has no radius, so nothing stops a member being assigned a long
            // way off. Rather than forbid it, show what it costs - a scattered colony is
            // allowed, but the player can see the price.
            _cost.text = $"holding {KeepAlive.KeepAliveZones.Count} of "
                         + $"{ModConfig.KeepAliveMaxZones.Value} zones loaded";
        }
    }
}
