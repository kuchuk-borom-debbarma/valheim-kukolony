using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace Kukolony.Gui
{
    /// <summary>
    ///     A screen built wrongly on purpose, so the layout audit can be shown to work.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         An audit that inspects nothing passes every run and proves nothing, which is the
    ///         failure that had to be dug out of this project's own suite once already. The
    ///         only way to know it has teeth is to hand it something broken and watch it
    ///         object, so this commits all three faults it is supposed to catch: an element
    ///         outside the content column, two elements on top of each other, and a string far
    ///         too long for the cell holding it.
    ///     </para>
    ///     <para>
    ///         It places by hand, deliberately bypassing <see cref="Row" />. That is the point:
    ///         the layout system makes these faults impossible, so the only way to produce one
    ///         is to do what the layout system exists to stop. It is never reachable from the
    ///         colony screen and is only ever pushed by the benchmark.
    ///     </para>
    /// </remarks>
    internal sealed class BrokenScreen : ScreenView
    {
        internal override string Title => "Deliberately broken";

        internal override string Subtitle => "Fixture for the layout audit";

        internal override void Build(ColonyScreen host, Column column)
        {
            if (!column.TryRow(out Row anchor))
            {
                return;
            }

            Transform parent = anchor.Parent;

            // Outside the content column: far enough right to render on the frame.
            Place(parent, "outside the column", 600f, Panel.FirstRowY, 200f);

            // Two elements at the same coordinates.
            Place(parent, "underneath", 0f, Panel.FirstRowY - Panel.Pitch, 200f);
            Place(parent, "on top", 0f, Panel.FirstRowY - Panel.Pitch, 200f);

            // A string with nowhere near enough room for it.
            Place(parent, "a sentence far longer than the narrow cell it has been given here",
                -200f, Panel.FirstRowY - 2f * Panel.Pitch, 60f);
        }

        private static void Place(Transform parent, string text, float x, float y, float width)
        {
            Text label = GUIManager.Instance.CreateText(text, parent, new Vector2(.5f, 1f),
                    new Vector2(.5f, 1f), new Vector2(x, y), GUIManager.Instance.AveriaSerifBold,
                    Widgets.BodySize, Color.white, true, Color.black, width, Panel.RowHeight, false)
                .GetComponent<Text>();
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
        }
    }
}
