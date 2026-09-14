using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Kukolony.Gui
{
    /// <summary>
    ///     Whether the player is writing something, wherever they happen to be writing it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every hotkey this mod reads has to ask, because a letter key is a letter first.
    ///         The screen's own key is <c>C</c>, so renaming a chest to "Coal" closed the screen
    ///         on the first keystroke - and the guard that existed covered chat and the console,
    ///         which are the two places this mod does <em>not</em> put a text box.
    ///     </para>
    ///     <para>
    ///         Shared rather than repeated, because a hotkey added later will be written by
    ///         copying one that exists, and the one it copies should already be right.
    ///     </para>
    /// </remarks>
    internal static class Typing
    {
        /// <summary>Whether a keystroke belongs to a text field rather than to a hotkey.</summary>
        internal static bool Now()
        {
            // The game's own three, in the order they are cheapest to ask.
            if (Console.IsVisible()) return true;
            if (Chat.instance != null && Chat.instance.HasFocus()) return true;

            // Signs, portals, and anything else that asks the player for a line of text.
            if (TextInput.IsVisible()) return true;

            // And ours. Jotunn builds plain InputFields, and Unity tells us which one has the
            // caret - so this needs no bookkeeping of our own and covers every field on every
            // screen, including ones nobody has written yet.
            GameObject focused = EventSystem.current != null
                ? EventSystem.current.currentSelectedGameObject
                : null;

            return focused != null && focused.activeInHierarchy &&
                   focused.GetComponent<InputField>() != null;
        }
    }
}
