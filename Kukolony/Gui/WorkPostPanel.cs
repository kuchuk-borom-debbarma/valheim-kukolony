using System.Collections.Generic;
using Jotunn.Managers;
using Kukolony.Core;
using Kukolony.Jobs;
using Kukolony.WorkPosts;
using UnityEngine;
using UnityEngine.UI;

namespace Kukolony.Gui
{
    /// <summary>
    ///     The panel for configuring a work post: which job, which item, which container.
    ///
    ///     It is a front end over the post's ZDO keys and nothing more. The job engine
    ///     already reads its configuration from there, so the panel adds no parallel path
    ///     and a post remains fully configurable without it.
    /// </summary>
    internal sealed class WorkPostPanel : MonoBehaviour
    {
        private const int ItemResultRows = 6;
        private const int ContainerRows = 5;

        internal static WorkPostPanel Instance { get; private set; }

        private GameObject _root;
        private InputField _itemFilter;
        private InputField _presetName;
        private Text _summary;
        private readonly List<Button> _itemButtons = new List<Button>();
        private readonly List<Button> _containerButtons = new List<Button>();
        private List<NearbyContainers.Entry> _containers = new List<NearbyContainers.Entry>();

        private WorkPost _post;
        private bool _inputBlocked;

        internal bool IsOpen => _root != null && _root.activeSelf;

        internal static void Register()
        {
            GUIManager.OnCustomGUIAvailable += Rebuild;
        }

        /// <summary>
        ///     CustomGUIFront is destroyed and rebuilt on every scene change, so the panel
        ///     is rebuilt with it. Caching one across a world reload leaves a dead
        ///     reference and a panel that never opens again.
        /// </summary>
        private static void Rebuild()
        {
            if (Instance != null)
            {
                Object.Destroy(Instance.gameObject);
            }

            GameObject holder = new GameObject("KukolonyWorkPostPanel");
            holder.transform.SetParent(GUIManager.CustomGUIFront.transform, worldPositionStays: false);
            Instance = holder.AddComponent<WorkPostPanel>();
            Instance.Build();
        }

        internal void Open(WorkPost post)
        {
            if (_root == null || post == null)
            {
                return;
            }

            _post = post;

            // The panel writes the post's ZDO, and a non-owner write lands locally and is
            // clobbered on the next sync. Same rule that shaped the claim system.
            if (post.TryGetComponent(out ZNetView nview) && nview.IsValid())
            {
                nview.ClaimOwnership();
            }

            // ObjectDB is only populated in a world, so the catalogue is built on first
            // open rather than at plugin load.
            if (ItemCatalogue.Count == 0)
            {
                ItemCatalogue.Rebuild();
            }

            RefreshContainers();
            RefreshSummary();

            _root.SetActive(true);
            SetInputBlocked(true);
        }

        internal void Close()
        {
            if (_root != null)
            {
                _root.SetActive(false);
            }

            _post = null;
            SetInputBlocked(false);
        }

        /// <summary>
        ///     BlockInput is refcounted, so an unpaired open leaves the player unable to
        ///     move. Tracking our own state means a double open or a panel destroyed while
        ///     showing cannot leak a block.
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
            if (!IsOpen)
            {
                return;
            }

            // Closing on Escape rather than only via the button, so the input block can
            // always be escaped.
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Close();
            }
        }

        private void Build()
        {
            _root = GUIManager.Instance.CreateWoodpanel(
                transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 0f), 520f, 580f, draggable: true);
            _root.SetActive(false);

            GUIManager.Instance.CreateText("Work Post", _root.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -28f),
                GUIManager.Instance.AveriaSerifBold, 22, GUIManager.Instance.ValheimOrange,
                true, Color.black, 400f, 30f, false);

            _summary = GUIManager.Instance.CreateText(string.Empty, _root.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -60f),
                GUIManager.Instance.AveriaSerifBold, 16, Color.white,
                true, Color.black, 460f, 44f, false).GetComponent<Text>();

            GameObject preset = GUIManager.Instance.CreateInputField(_root.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-80f, -104f),
                InputField.ContentType.Standard, "preset name", 16, 270f, 30f);
            _presetName = preset.GetComponent<InputField>();
            GameObject save = GUIManager.Instance.CreateButton("Save preset", _root.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(154f, -104f), 120f, 30f);
            save.GetComponent<Button>().onClick.AddListener(SavePreset);
            GameObject load = GUIManager.Instance.CreateButton("Load", _root.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(234f, -104f), 60f, 30f);
            load.GetComponent<Button>().onClick.AddListener(LoadPreset);

            BuildItemPicker();
            BuildContainerPicker();

            GameObject close = GUIManager.Instance.CreateButton("Close", _root.transform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 36f), 140f, 36f);
            close.GetComponent<Button>().onClick.AddListener(Close);
        }

        private void BuildItemPicker()
        {
            GUIManager.Instance.CreateText("Item to work with", _root.transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(150f, -150f),
                GUIManager.Instance.AveriaSerifBold, 16, GUIManager.Instance.ValheimOrange,
                true, Color.black, 300f, 24f, false);

            GameObject field = GUIManager.Instance.CreateInputField(_root.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -180f),
                InputField.ContentType.Standard, "search items...", 16, 440f, 32f);
            _itemFilter = field.GetComponent<InputField>();
            _itemFilter.onValueChanged.AddListener(_ => RefreshItemResults());

            for (int i = 0; i < ItemResultRows; i++)
            {
                GameObject row = GUIManager.Instance.CreateButton(string.Empty, _root.transform,
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -216f - i * 30f), 440f, 28f);
                row.SetActive(false);
                _itemButtons.Add(row.GetComponent<Button>());
            }
        }

        private void BuildContainerPicker()
        {
            GUIManager.Instance.CreateText("Destination", _root.transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(130f, -406f),
                GUIManager.Instance.AveriaSerifBold, 16, GUIManager.Instance.ValheimOrange,
                true, Color.black, 300f, 24f, false);

            for (int i = 0; i < ContainerRows; i++)
            {
                GameObject row = GUIManager.Instance.CreateButton(string.Empty, _root.transform,
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -436f - i * 30f), 440f, 28f);
                row.SetActive(false);
                _containerButtons.Add(row.GetComponent<Button>());
            }
        }

        private void RefreshItemResults()
        {
            List<ItemCatalogue.Entry> matches =
                ItemCatalogue.Search(_itemFilter != null ? _itemFilter.text : string.Empty, ItemResultRows);

            for (int i = 0; i < _itemButtons.Count; i++)
            {
                Button button = _itemButtons[i];
                if (i >= matches.Count)
                {
                    button.gameObject.SetActive(false);
                    continue;
                }

                ItemCatalogue.Entry entry = matches[i];
                button.gameObject.SetActive(true);
                bool selected = _post != null && _post.State.ItemFilters.Contains(entry.PrefabName);
                button.GetComponentInChildren<Text>().text = $"{(selected ? "✓ " : string.Empty)}{entry.DisplayName}  ({entry.PrefabName})";
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => ApplyItem(entry.PrefabName));
            }
        }

        private void RefreshContainers()
        {
            _containers = _post != null
                ? NearbyContainers.Find(_post.transform.position, _post.EffectiveRadius)
                : new List<NearbyContainers.Entry>();

            for (int i = 0; i < _containerButtons.Count; i++)
            {
                Button button = _containerButtons[i];

                // First row is always "auto", so a bound chest can be un-bound.
                if (i == 0)
                {
                    button.gameObject.SetActive(true);
                    button.GetComponentInChildren<Text>().text = "Auto - nearest holding the item";
                    button.onClick.RemoveAllListeners();
                    button.onClick.AddListener(() => ApplyDestination(ZDOID.None));
                    continue;
                }

                int index = i - 1;
                if (index >= _containers.Count)
                {
                    button.gameObject.SetActive(false);
                    continue;
                }

                NearbyContainers.Entry entry = _containers[index];
                button.gameObject.SetActive(true);
                bool selected = _post != null && entry.Container != null
                    && entry.Container.TryGetComponent(out ZNetView selectedView)
                    && _post.State.Destinations.Contains(selectedView.GetZDO().m_uid);
                button.GetComponentInChildren<Text>().text = (selected ? "✓ " : string.Empty) + entry.Describe();
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() =>
                {
                    if (entry.Container != null
                        && entry.Container.TryGetComponent(out ZNetView nview) && nview.IsValid())
                    {
                        ApplyDestination(nview.GetZDO().m_uid);
                    }
                });
            }
        }

        private void ApplyItem(string prefabName)
        {
            if (_post == null)
            {
                return;
            }

            WorkPostState state = _post.State;
            if (!state.IsValid)
            {
                return;
            }

            List<string> items = state.ItemFilters;
            if (items.Contains(prefabName)) items.Remove(prefabName); else items.Add(prefabName);
            state.SetItemFilters(items);
            Log.Info($"Work post item selection now has {items.Count} item(s)");
            RefreshItemResults();
            RefreshSummary();
        }

        private void SavePreset()
        {
            if (_post == null || _presetName == null) return;
            WorkPostState state = _post.State;
            if (state.IsValid && JobPresetStore.Save(_presetName.text, state.ItemFilters, state.Destinations))
            {
                Log.Info($"Saved job preset '{_presetName.text}'");
            }
        }

        private void LoadPreset()
        {
            if (_post == null || _presetName == null) return;
            if (!JobPresetStore.TryLoad(_presetName.text, out List<string> items, out List<ZDOID> destinations))
            {
                Log.Warning($"No usable job preset named '{_presetName.text}'");
                return;
            }
            WorkPostState state = _post.State;
            if (!state.IsValid) return;
            state.SetItemFilters(items);
            state.SetDestinations(destinations);
            RefreshItemResults();
            RefreshContainers();
            RefreshSummary();
        }

        private void ApplyDestination(ZDOID container)
        {
            if (_post == null)
            {
                return;
            }

            WorkPostState state = _post.State;
            if (!state.IsValid)
            {
                return;
            }

            List<ZDOID> destinations = state.Destinations;
            if (container.IsNone()) destinations.Clear();
            else if (destinations.Contains(container)) destinations.Remove(container);
            else destinations.Add(container);
            state.SetDestinations(destinations);
            Log.Info(destinations.Count == 0 ? "Work post destination set to auto"
                : $"Work post has {destinations.Count} destination(s)");
            RefreshContainers();
            RefreshSummary();
        }

        private void RefreshSummary()
        {
            if (_summary == null || _post == null)
            {
                return;
            }

            WorkPostState state = _post.State;
            string job = state.HasJob ? state.JobId : "none";
            string item = state.ItemFilters.Count == 0 ? "not set" : string.Join(", ", state.ItemFilters.ToArray());
            string destination = state.Destinations.Count == 0 ? "auto" : $"{state.Destinations.Count} selected";

            _summary.text = $"Job: {job}    Item: {item}    Destination: {destination}";
        }
    }
}
