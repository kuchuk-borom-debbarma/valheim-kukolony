using System.Collections.Generic;
using Jotunn.Managers;
using Kukolony.Colonies;
using Kukolony.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Kukolony.Gui
{
    /// <summary>
    ///     One screen a player sees at a time, and the stack of where they came from.
    /// </summary>
    internal abstract class ScreenView
    {
        internal abstract string Title { get; }

        internal virtual string Subtitle => null;

        /// <summary>
        ///     Adds every row this screen has. Paging is the column's business, so a screen
        ///     never counts rows and never checks whether one will fit.
        /// </summary>
        internal abstract void Build(ColonyScreen host, Column column);

        /// <summary>
        ///     Whether this screen still has a subject. A screen whose colony was destroyed
        ///     reports false and is dropped rather than drawing a stale reference.
        /// </summary>
        internal virtual bool StillValid(ColonyScreen host) => true;
    }

    /// <summary>
    ///     The single surface through which the settlement is managed.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Lives under Jotunn's <c>CustomGUIFront</c> and is rebuilt wholesale whenever
    ///         Jotunn signals that custom GUI is available, because anything cached across that
    ///         rebuild is a reference to a destroyed object.
    ///     </para>
    ///     <para>
    ///         The hotkey deliberately does <em>not</em> live here. Its predecessor read the
    ///         key in this component's own Update, so no key worked until the GUI event had
    ///         fired at least once - the screen could not be opened until after something else
    ///         had caused it to exist.
    ///     </para>
    /// </remarks>
    internal sealed class ColonyScreen : MonoBehaviour
    {
        private readonly List<Frame> _stack = new List<Frame>();

        private GameObject _root;
        private GameObject _content;
        private Text _message;
        private bool _blocked;

        internal static ColonyScreen Instance { get; private set; }

        internal bool IsOpen => _root != null && _root.activeSelf;

        /// <summary>The colony being managed. Null once it has been destroyed.</summary>
        internal Colony Colony { get; private set; }

        /// <summary>
        ///     What the player was looking at when the screen opened.
        /// </summary>
        /// <remarks>
        ///     Captured once, at open, rather than continuously: the player is looking at the
        ///     panel from the moment it appears, so a live raycast would answer "nothing" by
        ///     the time anything asked. Registration reads this at milestone 3.
        /// </remarks>
        internal GameObject LookedAt { get; private set; }

        internal static void Register() => GUIManager.OnCustomGUIAvailable += Rebuild;

        private static void Rebuild()
        {
            if (Instance != null)
            {
                Object.Destroy(Instance.gameObject);
            }

            GameObject holder = new GameObject("KukolonyColonyScreen");
            holder.transform.SetParent(GUIManager.CustomGUIFront.transform, false);
            Instance = holder.AddComponent<ColonyScreen>();
            Instance.Build();
        }

        /// <summary>
        ///     Opens on the nearest colony, or reports why it cannot.
        /// </summary>
        internal static void Toggle()
        {
            if (Instance == null)
            {
                // The GUI event has not fired yet, which is a real state on the main menu.
                Report.Say("The Kolony screen is not ready yet.");
                return;
            }

            if (Instance.IsOpen)
            {
                Instance.Close();
                return;
            }

            Player player = Player.m_localPlayer;
            if (player == null)
            {
                return;
            }

            Colony nearest = Nearest(player.transform.position);
            if (nearest == null)
            {
                // Worded for what is actually tested: the nearest LOADED Kolony, at any
                // range - opening from across the map is deliberate, since the screen works
                // off a loaded instance wherever the player stands. "Nearby" promised a
                // proximity check that has never existed.
                Report.Say("No Kolony is loaded to manage. Place a Kolony Hearth first.");
                return;
            }

            Instance.Open(nearest, PlayerLook.Target(player));
        }

        /// <summary>
        ///     Opens the settings of whatever registered thing the player is looking at.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The screen is organised the way a settlement is - colony, then a list, then a
        ///         structure, then one of its capabilities - which is right for finding something
        ///         you cannot see and wrong for the thing under your nose. A player standing in
        ///         front of a kiln knows exactly which row they want and has to walk down to it.
        ///     </para>
        ///     <para>
        ///         The colony is taken from the structure rather than from the player, because
        ///         the two can differ: a chest registered to an outpost is nearer to the outpost
        ///         than to whichever hearth happens to be closest to the player standing at it.
        ///         Opening the wrong colony's copy of a list would be worse than not opening.
        ///     </para>
        /// </remarks>
        internal static void OpenWhatIsLookedAt()
        {
            if (Instance == null)
            {
                Report.Say("The Kolony screen is not ready yet.");
                return;
            }

            Player player = Player.m_localPlayer;
            if (player == null) return;

            GameObject looking = PlayerLook.Target(player);
            if (looking == null || !looking.TryGetComponent(out ZNetView view) || !view.IsValid())
            {
                Report.Say("Look at something the Kolony has registered.");
                return;
            }

            ZDO zdo = view.GetZDO();
            Colony owner = Colony.FindFor(zdo);

            StructureRecord record = owner != null
                ? owner.State.GetStructures().Find(r => r.Id == zdo.m_uid)
                : null;

            if (record == null)
            {
                // Named, because "not registered" and "registered somewhere else" are different
                // answers and a player standing in front of their own chest deserves the one
                // that says what to do about it.
                Report.Say(owner == null
                    ? $"{StructureRegistry.DisplayName(looking)} is not registered to a Kolony."
                    : $"{StructureRegistry.DisplayName(looking)} belongs to {owner.State.Name} " +
                      "but has no record there.");
                return;
            }

            if (Instance.IsOpen) Instance.Close();

            Instance.Open(owner, looking);
            Instance.Push(new StructureDetailScreen(record.PersistentId, record.Id));

            // Straight to the one thing it is, when it is only one thing. A chest has a single
            // capability and the row between the player and its settings is a row that exists
            // for the pieces that carry several.
            List<StructureCapability> aspects = StructureDetailScreen.Aspects(record.Capabilities);
            if (aspects.Count == 1)
            {
                Instance.Push(new StructureAspectScreen(record.PersistentId, record.Id, aspects[0]));
            }
        }

        private static Colony Nearest(Vector3 position)
        {
            Colony best = null;
            float bestDistance = float.MaxValue;

            foreach (Colony colony in Colony.Instances)
            {
                if (colony == null)
                {
                    continue;
                }

                float distance = Utils.DistanceXZ(colony.transform.position, position);
                if (distance >= bestDistance)
                {
                    continue;
                }

                best = colony;
                bestDistance = distance;
            }

            return best;
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

        internal void Open(Colony colony, GameObject lookedAt)
        {
            if (_root == null || colony == null)
            {
                return;
            }

            Colony = colony;
            LookedAt = lookedAt;
            _stack.Clear();
            _stack.Add(new Frame(new ColonyHomeScreen()));

            _root.SetActive(true);
            Block(true);
            Report.Listener = Say;
            Refresh();

            Log.Info($"[screen] opened on '{colony.State.Name}'" +
                     (lookedAt != null ? $", looking at {StructureRegistry.DisplayName(lookedAt)}" : ", looking at nothing"));
        }

        internal void Close()
        {
            if (_root != null)
            {
                _root.SetActive(false);
            }

            Colony = null;
            LookedAt = null;
            _stack.Clear();
            Report.Listener = null;
            Block(false);
        }

        /// <summary>Opens a sub-screen, remembering where it came from.</summary>
        internal void Push(ScreenView screen)
        {
            if (screen == null)
            {
                return;
            }

            _stack.Add(new Frame(screen));
            Refresh();
        }

        /// <summary>Returns to the screen this one was opened from.</summary>
        internal void Pop()
        {
            if (_stack.Count <= 1)
            {
                Close();
                return;
            }

            _stack.RemoveAt(_stack.Count - 1);
            Refresh();
        }

        /// <summary>
        ///     Replaces the whole stack. Switching top-level screens clears sub-screens rather
        ///     than stranding the player inside one whose parent is no longer on show.
        /// </summary>
        internal void Root(ScreenView screen)
        {
            _stack.Clear();
            _stack.Add(new Frame(screen));
            Refresh();
        }

        /// <summary>
        ///     The subtree every screen is built into. The layout audit inspects this, so it
        ///     sees exactly what the player does and nothing of the panel frame around it.
        /// </summary>
        internal Transform Content => _content == null ? null : _content.transform;

        /// <summary>
        ///     How many pages and rows the last build produced. The values the pager itself
        ///     uses, so a check that reads them is reading what the player sees rather than
        ///     recomputing the answer and agreeing with itself.
        /// </summary>
        internal int LastPages { get; private set; } = 1;

        internal int LastRows { get; private set; }

        /// <summary>Turns to a page, as the pager buttons do.</summary>
        internal void ShowPageForTest(int page)
        {
            Page = page;
            Refresh();
        }

        internal int Depth => _stack.Count;

        internal ScreenView Current => _stack.Count == 0 ? null : _stack[_stack.Count - 1].Screen;

        internal int Page
        {
            get => _stack.Count == 0 ? 0 : _stack[_stack.Count - 1].Page;
            private set
            {
                if (_stack.Count > 0)
                {
                    _stack[_stack.Count - 1].Page = Mathf.Max(0, value);
                }
            }
        }

        internal void Say(string message)
        {
            if (_message != null)
            {
                _message.text = message ?? string.Empty;
            }
        }

        /// <summary>
        ///     Rebuilds the visible screen from scratch.
        /// </summary>
        /// <remarks>
        ///     Destroy-and-rebuild rather than diffing. A colony changes underneath this from
        ///     several directions - other players, villagers, the world - and a diff that is
        ///     wrong shows stale state convincingly. Rebuilding a dozen rows is cheap and is
        ///     always honest about what is true now.
        /// </remarks>
        internal void Refresh()
        {
            if (!IsOpen || _content == null)
            {
                return;
            }

            ScreenView screen = Current;
            if (screen == null)
            {
                Close();
                return;
            }

            Widgets.ClearChildren(_content.transform);

            Widgets.Title(_content.transform, screen.Title);
            string subtitle = screen.Subtitle;
            if (!string.IsNullOrEmpty(subtitle))
            {
                Widgets.Subtitle(_content.transform, subtitle, Color.gray);
            }

            Column column = new Column(_content.transform, Page);
            screen.Build(this, column);
            LastPages = column.Pages;
            LastRows = column.Total;

            if (column.PageOverran)
            {
                // The content shrank under a page the player had turned to. Step back rather
                // than showing an empty screen that looks like the list was emptied.
                Page = column.Pages - 1;
                Refresh();
                return;
            }

            BuildFooter(column);
            _message = Widgets.Label(new Row(_content.transform, Panel.MessageY), string.Empty,
                GUIManager.Instance.ValheimOrange);
        }

        private void BuildFooter(Column column)
        {
            Widgets.Pager(_content.transform, Page, column.Pages, page =>
            {
                Page = page;
                Refresh();
            });

            Widgets.Footer(_content.transform, Close, _stack.Count > 1 ? Pop : (System.Action)null);
        }

        private void Update()
        {
            if (!IsOpen)
            {
                return;
            }

            // The subject can be destroyed by anyone at any time, including another player.
            // Closing is the honest response: every screen below this one describes a colony
            // that no longer exists.
            if (Colony == null || Current == null || !Current.StillValid(this))
            {
                Report.Listener = null;
                Report.Say("The Kolony is gone.");
                Close();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Pop();
            }
        }

        private void OnDestroy()
        {
            // Not merely tidiness: leaving input blocked with no panel on screen leaves the
            // player unable to move and with nothing to close.
            Block(false);
            Report.Listener = null;
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        ///     Releases the mouse for the panel and gives it back afterwards.
        /// </summary>
        /// <remarks>
        ///     Valheim keeps the cursor captured for looking around, so a panel drawn without
        ///     this is visible and completely unusable. The predecessor's colony picker shipped
        ///     without it and was exactly that. Idempotent, because the release is paired with
        ///     both Close and OnDestroy and double-releasing is its own bug.
        /// </remarks>
        private void Block(bool value)
        {
            if (_blocked == value)
            {
                return;
            }

            _blocked = value;
            GUIManager.BlockInput(value);
        }

        /// <summary>One entry on the back stack: a screen, and where the player had paged to.</summary>
        private sealed class Frame
        {
            internal Frame(ScreenView screen) => Screen = screen;

            internal ScreenView Screen { get; }

            internal int Page { get; set; }
        }
    }

    /// <summary>
    ///     Reads the screen's hotkey.
    /// </summary>
    /// <remarks>
    ///     A separate component on the plugin object rather than on the screen itself, so the
    ///     key works from the moment the mod loads instead of only after Jotunn has built the
    ///     GUI once.
    /// </remarks>
    internal sealed class ColonyScreenHotkey : MonoBehaviour
    {
        internal static void Register(GameObject host) => host.AddComponent<ColonyScreenHotkey>();

        private void Update()
        {
            bool colony = Input.GetKeyDown(ModConfig.ColonyScreenHotkey.Value);
            bool structure = Input.GetKeyDown(ModConfig.StructureScreenHotkey.Value);

            if (!colony && !structure) return;

            // A letter key is a letter first. This guarded chat and the console, which are the
            // two places this mod does not put a text box - so naming a chest "Coal" closed the
            // screen on the first keystroke.
            if (Typing.Now()) return;

            // The structure key wins where they are set to the same thing, because it is the
            // more specific answer: somebody looking at a registered kiln and pressing one key
            // meant the kiln. With nothing registered in front of them it says so, and the
            // Kolony key is still there.
            if (structure) ColonyScreen.OpenWhatIsLookedAt();
            else ColonyScreen.Toggle();
        }
    }
}
