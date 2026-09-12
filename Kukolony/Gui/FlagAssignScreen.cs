using System.Collections;
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
        private bool _scanning;
        private int _page;

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
            _instance._page = 0;
            _instance._root.SetActive(true);
            _instance.Block(true);
            _instance.Refresh();
            _instance.EnsureScanned();
        }

        /// <summary>
        ///     Sweeps for hearths when none are known yet.
        /// </summary>
        /// <remarks>
        ///     The registry is normally filled by the keep-alive driver's scan - which runs
        ///     only on the server, and only while the feature is enabled. A client on a
        ///     dedicated server, or anyone with keep-alive off, opened this screen to "No
        ///     Kolony exists yet" with hearths standing in the world. The screen is the
        ///     other thing that needs the list, so it sweeps for itself.
        /// </remarks>
        private void EnsureScanned()
        {
            if (_scanning || ColonyRegistry.KnownColonies > 0) return;

            _scanning = true;
            StartCoroutine(ScanThenRefresh());
        }

        private IEnumerator ScanThenRefresh()
        {
            yield return ColonyRegistry.Scan();
            _scanning = false;
            if (_root != null && _root.activeSelf && _flag != null) Refresh();
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

            // Children die at the end of the frame, so without this the new rows are laid
            // out alongside the old ones for a frame - the same flicker the Kolony screen's
            // rebuild already guards against.
            _content.transform.DetachChildren();

            Widgets.Title(_content.transform, "Kolony Flag");

            ZDOID owner = _flag.Owner;
            Widgets.Subtitle(_content.transform, owner.IsNone()
                ? "Claimed by nobody. Choose the Kolony this flag works for."
                : "Working for " + WorkFlag.OwnerName(owner));

            Column column = new Column(_content.transform, _page);

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
                Widgets.Label(none, _scanning
                    ? "Looking for Kolonies..."
                    : "No Kolony exists yet. Place a Kolony Hearth first.", Color.gray);
            }

            foreach (ZDO hearth in known)
            {
                if (hearth == null || !hearth.IsValid()) continue;
                if (!column.TryRow(out Row row)) continue;

                string name = new ColonyState(hearth).Name;
                if (string.IsNullOrEmpty(name)) name = Colony.UnnamedLabel;

                float away = Utils.DistanceXZ(hearth.GetPosition(), _flag.transform.position);
                Widgets.Caption(row, $"{name} <color=grey>({away:0} m away)</color>", 420f);

                ZDOID id = hearth.m_uid;
                bool current = owner == id;
                Widgets.Button(row, current ? "Working here" : "Assign", 180f, () =>
                {
                    if (current) return;

                    RegisterOutcome outcome = ColonyOperations.AssignFlag(id, _flag);
                    // The flag is the "what" and the Kolony is where it went - swapped,
                    // this reported "Registered Fort Kuku to Kolony Flag".
                    Report.Say(ColonyOperations.Explain(outcome,
                        StructureRegistry.DisplayName(_flag.gameObject), name));
                    Refresh();
                });
            }

            if (column.PageOverran)
            {
                // The list shrank under a page the player had turned to. Step back rather
                // than showing an empty screen that looks like every Kolony vanished.
                _page = column.Pages - 1;
                Refresh();
                return;
            }

            if (column.Pages > 1)
            {
                Row pager = new Row(_content.transform, Panel.PagerY);
                Widgets.Caption(pager, string.Empty, 260f);
                Widgets.Button(pager, "<", 60f, () =>
                {
                    _page = Mathf.Max(0, _page - 1);
                    Refresh();
                });
                Widgets.Caption(pager, $"{_page + 1} / {column.Pages}", 90f);
                Widgets.Button(pager, ">", 60f, () =>
                {
                    _page = Mathf.Min(column.Pages - 1, _page + 1);
                    Refresh();
                });
            }

            // The footer, not a column row: a row has to fit on the current page, and a
            // world with more Kolonies than fit one page silently never rendered Close,
            // leaving Escape as the only way out.
            Row footer = new Row(_content.transform, Panel.FooterY);
            Widgets.Caption(footer, string.Empty, 260f);
            Widgets.Button(footer, "Close", 160f, Close);
        }

        private void Block(bool value)
        {
            if (_blocked == value) return;

            _blocked = value;
            GUIManager.BlockInput(value);
        }

        private static List<Transform> Children(Transform parent)
        {
            List<Transform> children = new List<Transform>(parent.childCount);
            foreach (Transform child in parent) children.Add(child);
            return children;
        }
    }
}
