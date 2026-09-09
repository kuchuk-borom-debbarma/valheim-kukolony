using System.Collections.Generic;
using Kukolony.Colonies;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace Kukolony.Gui
{
    /// <summary>Small searchable shortcut to any loaded colony; direct hearth use remains available.</summary>
    internal sealed class ColonyPicker : MonoBehaviour
    {
        internal static ColonyPicker Instance { get; private set; }
        private GameObject _root;
        private InputField _search;
        private readonly List<Button> _buttons = new List<Button>();

        internal static void Register() => GUIManager.OnCustomGUIAvailable += Build;

        private static void Build()
        {
            GameObject holder = new GameObject("KukolonyColonyPicker");
            holder.transform.SetParent(GUIManager.CustomGUIFront.transform, false);
            Instance = holder.AddComponent<ColonyPicker>();
            Instance.Create();
        }

        private void Create()
        {
            _root = GUIManager.Instance.CreateWoodpanel(transform, new Vector2(.5f,.5f), new Vector2(.5f,.5f), Vector2.zero, 420, 300, true);
            GUIManager.Instance.CreateText("Colonies", _root.transform, new Vector2(.5f,1), new Vector2(.5f,1), new Vector2(0,-30), GUIManager.Instance.AveriaSerifBold, 24, Color.white, true, Color.black, 300, 30, false);
            _search = GUIManager.Instance.CreateInputField(_root.transform, new Vector2(.5f,1), new Vector2(.5f,1), new Vector2(0,-68), InputField.ContentType.Standard, "search", 24, 300, 28).GetComponent<InputField>();
            _search.onValueChanged.AddListener(_ => Refresh());
            _root.SetActive(false);
        }

        private void Update()
        {
            if (Input.GetKeyDown(ModConfig.ColonyPickerHotkey.Value) && (Chat.instance == null || !Chat.instance.HasFocus()) && !Console.IsVisible())
            {
                _root.SetActive(!_root.activeSelf); if (_root.activeSelf) Refresh();
            }
            if (_root != null && _root.activeSelf && Input.GetKeyDown(KeyCode.Escape)) _root.SetActive(false);
        }

        private void Refresh()
        {
            foreach (Button button in _buttons) if (button != null) Destroy(button.gameObject);
            _buttons.Clear(); string query = _search != null ? _search.text.ToLowerInvariant() : string.Empty; int row = 0;
            foreach (Colony colony in Colony.Instances)
            {
                if (colony == null || !colony.State.Name.ToLowerInvariant().Contains(query)) continue;
                Colony selected = colony;
                Button button = GUIManager.Instance.CreateButton(colony.State.Name, _root.transform, new Vector2(.5f,1), new Vector2(.5f,1), new Vector2(0,-108-row*32), 320, 28).GetComponent<Button>();
                button.onClick.AddListener(() => { ColonyPanel.Instance?.Open(selected); _root.SetActive(false); }); _buttons.Add(button); row++;
            }
        }

        internal void ShowForTest()
        {
            if (_root == null) return;
            _root.SetActive(true);
            Refresh();
        }

        internal void HideForTest()
        {
            if (_root != null) _root.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
