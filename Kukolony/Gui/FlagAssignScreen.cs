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
        private bool _awaitingSweep;
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

            // The sweep is asked for before the first Refresh, or the multi-frame scan
            // runs while the screen shows the definitive "No Kolony exists yet" instead
            // of saying it is looking.
            ColonyRegistry.EnsureFresh(_instance);
            _instance._awaitingSweep = ColonyRegistry.Sweeping;

            _instance._flag = flag;
            _instance._page = 0;
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

            // The sweep the open kicked off has landed; show what it found.
            if (_awaitingSweep && !ColonyRegistry.Sweeping)
            {
                _awaitingSweep = false;
                Refresh();
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
            Widgets.ClearChildren(_content.transform);

            Widgets.Title(_content.transform, "Kolony Flag");

            ZDOID owner = _flag.Owner;
            Widgets.Subtitle(_content.transform, owner.IsNone()
                ? "Claimed by nobody. Choose the Kolony this flag works for."
                : "Working for " + WorkFlag.OwnerName(owner));

            Column column = new Column(_content.transform, _page);

            if (column.TryRow(out Row radius))
            {
                Widgets.Number(radius, "How far it reaches", _flag.Radius,
                    WorkFlag.MinRadius, WorkFlag.MaxRadius, 8f,
                    value => $"{value:0} m",
                    value =>
                    {
                        _flag.SetRadius(value);
                        Refresh();
                    });
            }

            // Counted over the hearths that still resolve, not the raw list: a destroyed
            // hearth's stale entry used to suppress this row while the per-row filter
            // below rendered nothing, which read as an unexplained empty screen.
            IReadOnlyList<ZDO> known = ColonyRegistry.GetKnownColonies();
            bool anyValid = false;
            foreach (ZDO hearth in known)
            {
                if (hearth != null && hearth.IsValid())
                {
                    anyValid = true;
                    break;
                }
            }

            if (!anyValid && column.TryRow(out Row none))
            {
                Widgets.Label(none, EmptyListExplanation(), Color.gray);
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

            Widgets.Pager(_content.transform, _page, column.Pages, page =>
            {
                _page = page;
                Refresh();
            });

            // The footer, not a column row: a row has to fit on the current page, and a
            // world with more Kolonies than fit one page silently never rendered Close,
            // leaving Escape as the only way out.
            Row footer = new Row(_content.transform, Panel.FooterY);
            Widgets.Caption(footer, string.Empty, 260f);
            Widgets.Button(footer, "Close", 140f, Close);
        }

        private void Block(bool value)
        {
            if (_blocked == value) return;

            _blocked = value;
            GUIManager.BlockInput(value);
        }

        /// <summary>
        ///     What an empty Kolony list means, told honestly per peer.
        /// </summary>
        /// <remarks>
        ///     A joined client only ever sees ZDOs the server replicated near some player,
        ///     so a hearth nobody has stood near this session simply is not here to find -
        ///     the sweep cannot fix that, and claiming "no Kolony exists" beside a standing
        ///     hearth is the screen lying about the world. The host's answer stays
        ///     definitive, because the host sees everything.
        /// </remarks>
        private static string EmptyListExplanation()
        {
            if (ColonyRegistry.Sweeping) return "Looking for Kolonies...";

            return ZNet.instance != null && !ZNet.instance.IsServer()
                ? "No Kolony known here yet. A far-off hearth appears once someone has been near it - or ask the host."
                : "No Kolony exists yet. Place a Kolony Hearth first.";
        }

    }
}
