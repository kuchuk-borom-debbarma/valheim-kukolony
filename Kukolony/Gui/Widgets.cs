using System;
using Jotunn.Managers;
using Kukolony.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Kukolony.Gui
{
    /// <summary>
    ///     One builder per kind of value a screen can show, so no screen invents its own
    ///     control for a flag or a number.
    /// </summary>
    /// <remarks>
    ///     Everything here places through <see cref="Row" />, which allocates cells in
    ///     sequence. No builder takes an x coordinate, and none should ever be given one.
    /// </remarks>
    internal static class Widgets
    {
        internal const float LabelWidth = 300f;
        internal const float ControlWidth = 150f;
        internal const float NudgeWidth = 44f;
        internal const int BodySize = 16;
        internal const int TitleSize = 24;

        /// <summary>A line of text spanning the rest of the row.</summary>
        internal static Text Label(Row row, string text, Color? colour = null)
        {
            if (!row.TryTakeRest(out float x, out float width))
            {
                return Overflowed(row, "label");
            }

            return Text(row.Parent, text, x, row.Y, BodySize, width, TextAnchor.MiddleLeft, colour);
        }

        /// <summary>A label taking a fixed share of the row, so controls after it line up.</summary>
        internal static Text Caption(Row row, string text, float width = LabelWidth, Color? colour = null)
        {
            if (!row.TryTake(width, out float x, out float granted))
            {
                return Overflowed(row, "caption");
            }

            return Text(row.Parent, text, x, row.Y, BodySize, granted, TextAnchor.MiddleLeft, colour);
        }

        internal static Button Button(Row row, string text, float width, Action onClick)
        {
            if (!row.TryTake(width, out float x, out float granted))
            {
                Overflowed(row, "button '" + text + "'");
                return null;
            }

            Button button = GUIManager.Instance
                .CreateButton(text, row.Parent, Centre, Centre, new Vector2(x, row.Y), granted, Panel.RowHeight)
                .GetComponent<Button>();
            button.onClick.AddListener(() => onClick());
            return button;
        }

        /// <summary>A flag, shown as what it currently is rather than as a box to decode.</summary>
        internal static void Flag(Row row, string label, bool value, Action<bool> onChange)
        {
            Caption(row, label);
            Button(row, value ? "Yes" : "No", ControlWidth, () => onChange(!value));
        }

        /// <summary>
        ///     A bounded number with a step either side.
        /// </summary>
        /// <remarks>
        ///     Clamped here rather than by the caller, so a setting cannot be nudged out of its
        ///     own range by a screen that forgot. <paramref name="format" /> carries the unit,
        ///     because a bare number leaves the player guessing at metres versus counts.
        /// </remarks>
        internal static void Number(Row row, string label, float value, float min, float max, float step,
            Func<float, string> format, Action<float> onChange)
        {
            Caption(row, label);
            Button(row, "-", NudgeWidth, () => onChange(Mathf.Clamp(value - step, min, max)));

            if (row.TryTake(ControlWidth - 2f * (NudgeWidth + Panel.CellGap), out float x, out float granted))
            {
                Text(row.Parent, format(value), x, row.Y, BodySize, granted, TextAnchor.MiddleCenter);
            }

            Button(row, "+", NudgeWidth, () => onChange(Mathf.Clamp(value + step, min, max)));
        }

        /// <summary>One of a few: a button that steps to the next option, wrapping.</summary>
        internal static void Cycle(Row row, string label, string[] options, int index, Action<int> onChange)
        {
            Caption(row, label);

            if (options == null || options.Length == 0)
            {
                // An empty option set is a caller bug. Saying so beats rendering a button that
                // does nothing when pressed.
                Label(row, "<color=red>no options</color>");
                return;
            }

            int safe = ((index % options.Length) + options.Length) % options.Length;
            Button(row, options[safe], ControlWidth, () => onChange((safe + 1) % options.Length));
        }

        /// <summary>
        ///     Free text, committed when editing ends rather than per keystroke.
        /// </summary>
        internal static InputField Text(Row row, string label, string value, Action<string> onCommit)
        {
            Caption(row, label);

            if (!row.TryTakeRest(out float x, out float granted))
            {
                Overflowed(row, "input '" + label + "'");
                return null;
            }

            InputField input = GUIManager.Instance.CreateInputField(row.Parent, Centre, Centre,
                    new Vector2(x, row.Y), InputField.ContentType.Standard, string.Empty,
                    BodySize, granted, Panel.RowHeight)
                .GetComponent<InputField>();
            input.text = value ?? string.Empty;
            input.onEndEdit.AddListener(text => onCommit(text));
            return input;
        }

        /// <summary>
        ///     One of many, or several of many: a row that opens a picker.
        /// </summary>
        /// <remarks>
        ///     The chosen value is shown on the row itself, so a screen full of these reads as
        ///     a summary rather than as a row of identical "Choose" buttons.
        /// </remarks>
        internal static void Choice(Row row, string label, string chosen, Action onOpen,
            float width = ControlWidth)
        {
            Caption(row, label);
            Button(row, string.IsNullOrEmpty(chosen) ? "Choose..." : chosen, width, onOpen);
        }

        /// <summary>
        ///     Tears a rebuilt panel's content down, immediately from the layout's point of
        ///     view.
        /// </summary>
        /// <remarks>
        ///     Destroy only takes effect at the end of the frame, so without the detach the
        ///     new rows are built alongside the old ones for a frame and the layout audit
        ///     sees both. Iterating the live children is safe precisely because the destroy
        ///     is deferred; the detach afterwards is what makes it immediate. One shared
        ///     teardown, because the flag screen copied the Kolony screen's and shipped
        ///     without the detach.
        /// </remarks>
        internal static void ClearChildren(Transform parent)
        {
            foreach (Transform child in parent)
            {
                UnityEngine.Object.Destroy(child.gameObject);
            }

            parent.DetachChildren();
        }

        /// <summary>
        ///     The page-turning row, when there is anything to turn. One builder, because
        ///     two hand-copied pagers had already drifted (clamping inline vs in a setter)
        ///     within a single commit of each other.
        /// </summary>
        internal static void Pager(Transform parent, int page, int pages, Action<int> turn)
        {
            if (pages <= 1) return;

            Row row = new Row(parent, Panel.PagerY);
            Caption(row, string.Empty, 260f);
            Button(row, "<", 60f, () => turn(Mathf.Max(0, page - 1)));
            Caption(row, $"{page + 1} / {pages}", 90f);
            Button(row, ">", 60f, () => turn(Mathf.Min(pages - 1, page + 1)));
        }

        /// <summary>
        ///     The footer row every panel ends with: Close, and Back where there is a stack
        ///     to go back through. One builder for the same reason as the pager - the two
        ///     hand-copied footers had already drifted their Close widths apart.
        /// </summary>
        internal static void Footer(Transform parent, Action onClose, Action onBack = null)
        {
            Row footer = new Row(parent, Panel.FooterY);
            Caption(footer, string.Empty, 260f);
            if (onBack != null) Button(footer, "Back", 140f, onBack);
            Button(footer, "Close", 140f, onClose);
        }

        internal static Text Title(Transform parent, string text) =>
            Text(parent, text, 0f, Panel.TitleY, TitleSize, Panel.ContentWidth, TextAnchor.MiddleCenter);

        internal static Text Subtitle(Transform parent, string text, Color? colour = null) =>
            Text(parent, text, 0f, Panel.SubtitleY, BodySize, Panel.ContentWidth, TextAnchor.MiddleCenter, colour);

        /// <summary>
        ///     The one text primitive. Everything above goes through it, so there is a single
        ///     place where a font, an outline or a size fitter could go wrong.
        /// </summary>
        /// <remarks>
        ///     The content size fitter is deliberately off. With it on, a Text resizes its own
        ///     rect to fit the string, which quietly undoes the cell it was allocated and makes
        ///     the layout audit meaningless - the rect would always fit, having been grown to.
        /// </remarks>
        private static Text Text(Transform parent, string text, float x, float y, int size, float width,
            TextAnchor anchor, Color? colour = null)
        {
            Text label = GUIManager.Instance.CreateText(text, parent, Centre, Centre, new Vector2(x, y),
                    GUIManager.Instance.AveriaSerifBold, size, colour ?? Color.white, true, Color.black,
                    width, Panel.RowHeight, false)
                .GetComponent<Text>();
            label.alignment = anchor;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            return label;
        }

        private static Text Overflowed(Row row, string what)
        {
            Log.Warning($"[screen] no room left in the row for {what}; it was not drawn");
            return null;
        }

        /// <summary>
        ///     Top-centre anchor. Rows count downward from the top of the panel, so a screen
        ///     that grows does not move the rows above the one that changed.
        /// </summary>
        private static Vector2 Centre => new Vector2(.5f, 1f);
    }
}
