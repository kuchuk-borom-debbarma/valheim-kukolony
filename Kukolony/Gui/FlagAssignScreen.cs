using System.Collections.Generic;
using Jotunn.Managers;
using Kukolony.Colonies;
using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Gui
{
    /// <summary>
    ///     The screen a flag opens: which Kolony it serves, and how far it reaches.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Deliberately its own small panel rather than a view inside
    ///         <see cref="ColonyScreen" />. That screen requires a loaded Kolony to open at
    ///         all and closes itself the moment its Kolony is gone — reasonable for managing
    ///         one, and exactly wrong here, because the whole point of a flag is being planted
    ///         three hundred metres from any hearth, where no Kolony is loaded to host a
    ///         screen. The flag is loaded (the player is standing at it), so the flag hosts
    ///         its own.
    ///     </para>
    ///     <para>
    ///         Kolonies are listed from <see cref="ColonyRegistry" />, which reads hearth ZDOs
    ///         without instantiating anything — a Kolony does not need to be loaded to be
    ///         chosen, only to exist.
    ///     </para>
    /// </remarks>
    internal sealed class FlagAssignScreen : MonoBehaviour
    {
        private static FlagAssignScreen _instance;

        private GameObject _root;
        private GameObject _content;
        private WorkFlag _flag;
        private bool _blocked;

        internal static void Register() => GUIManager.OnCustomGUIAvailable += Rebuild;

        private static void Rebuild()
        {
            if (_instance != null)
            {
                Destroy(_instance.gameObject);
            }

            GameObject holder = new GameObject("KukolonyFlagScreen");
            holder.transform.SetParent(GUIManager.CustomGUIFront.transform, false);
            _instance = holder.AddComponent<FlagAssignScreen>();
            _instance.Build();
        }

        internal static void Open(WorkFlag flag)
        {
            if (_instance == null || flag == null)
            {
                Report.Say("The flag screen is not ready yet.");
                return;
            }

            _instance._flag = flag;
            _instance._root.SetActive(true);
            _instance.Block(true);
            _instance.Refresh();
        }

        private void Build()
        {
            _root = GUIManager.Instance.CreateWoodpanel(transform, new Vector2(.5f, .5f),
                new Vector2(.5f, .5f), Vector2.zero, Panel.Width, Panel.Height, true);
            _root.SetActive(false);

            _content = new GameObject("Content", typeof(RectTransform));
            _content.transform.SetParent(_root.transform, false);
            RectTransform rect = (RectTransform)_content.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private void Update()
        {
            if (_root == null || !_root.activeSelf) return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Close();
                return;
            }

            // The flag being destroyed under the open screen - broken by an enemy, or
            // hammered away by the player - leaves every row describing a thing that is
            // gone. Same rule as the Kolony screen: close, and say why.
            if (_flag == null)
            {
                Report.Say("The flag is gone.");
                Close();
            }
        }

        private void OnDestroy() => Block(false);

        private void Close()
        {
            if (_root != null) _root.SetActive(false);
            _flag = null;
            Block(false);
        }

        /// <summary>Destroy-and-rebuild, never diffing, as every screen here is.</summary>
        private void Refresh()
        {
            foreach (Transform child in Children(_content.transform))
            {
                Destroy(child.gameObject);
            }

            Widgets.Title(_content.transform, "Kolony Flag");

            ZDOID owner = _flag.Owner;
            Widgets.Subtitle(_content.transform, owner.IsNone()
                ? "Claimed by nobody. Choose the Kolony this flag works for."
                : "Working for " + OwnerName(owner));

            Column column = new Column(_content.transform, 0);

            if (column.TryRow(out Row radius))
            {
                Widgets.Number(radius, "How far it reaches", _flag.Radius, 8f, 256f, 8f,
                    value => $"{value:0} m",
                    value =>
                    {
                        _flag.SetRadius(value);
                        Refresh();
                    });
            }

            IReadOnlyList<ZDO> known = ColonyRegistry.GetKnownColonies();
            if (known.Count == 0 && column.TryRow(out Row none))
            {
                Widgets.Label(none, "No Kolony exists yet. Place a Kolony Hearth first.", Color.gray);
            }

            foreach (ZDO hearth in known)
            {
                if (hearth == null || !hearth.IsValid()) continue;
                if (!column.TryRow(out Row row)) continue;

                string name = new ColonyState(hearth).Name;
                if (string.IsNullOrEmpty(name)) name = "an unnamed Kolony";

                float away = Utils.DistanceXZ(hearth.GetPosition(), _flag.transform.position);
                Widgets.Caption(row, $"{name} <color=grey>({away:0} m away)</color>", 420f);

                ZDOID id = hearth.m_uid;
                bool current = owner == id;
                Widgets.Button(row, current ? "Working here" : "Assign", 180f, () =>
                {
                    if (current) return;

                    RegisterOutcome outcome = ColonyOperations.AssignFlag(id, _flag);
                    Report.Say(ColonyOperations.Explain(outcome, name,
                        StructureRegistry.DisplayName(_flag.gameObject)));
                    Refresh();
                });
            }

            if (column.TryRow(out Row close))
            {
                Widgets.Button(close, "Close", 160f, Close);
            }
        }

        private void Block(bool value)
        {
            if (_blocked == value) return;

            _blocked = value;
            GUIManager.BlockInput(value);
        }

        private static string OwnerName(ZDOID owner)
        {
            ZDO zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(owner) : null;
            if (zdo == null || !zdo.IsValid()) return "a Kolony that is gone";

            string name = new ColonyState(zdo).Name;
            return string.IsNullOrEmpty(name) ? "an unnamed Kolony" : name;
        }

        private static List<Transform> Children(Transform parent)
        {
            List<Transform> children = new List<Transform>(parent.childCount);
            foreach (Transform child in parent) children.Add(child);
            return children;
        }
    }
}
