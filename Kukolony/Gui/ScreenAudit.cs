using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Kukolony.Gui
{
    /// <summary>
    ///     Checks a built screen against the layout's own rules.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The predecessor's audit was a script that parsed the C# source for literal
    ///         coordinates. It caught real bugs, but it lived outside the repository, it could
    ///         only see numbers written as constants, and a layout computed by a system has
    ///         none. This walks what was actually built instead, so it audits the screen rather
    ///         than the source's appearance, and it runs inside the gate rather than beside it.
    ///     </para>
    ///     <para>
    ///         Overlap within a row is already impossible - <see cref="Row" /> allocates cells
    ///         in sequence - and so is overlap between rows, which sit at a fixed pitch. This
    ///         exists for what the layout cannot promise: that a widget used the system at all,
    ///         and that the strings handed to it fit the cells they were given.
    ///     </para>
    /// </remarks>
    internal static class ScreenAudit
    {
        /// <summary>One element, in panel coordinates.</summary>
        internal readonly struct Element
        {
            internal Element(string path, int depth, Rect rect, string text, float needed)
            {
                Path = path;
                Depth = depth;
                Rect = rect;
                Text = text;
                Needed = needed;
            }

            internal string Path { get; }

            /// <summary>
            ///     1 for something the layout placed; deeper for a control's own parts.
            /// </summary>
            internal int Depth { get; }

            internal Rect Rect { get; }

            /// <summary>The string this element draws, or empty when it draws none.</summary>
            internal string Text { get; }

            /// <summary>Width the string needs. Zero when there is no string.</summary>
            internal float Needed { get; }
        }

        internal sealed class Result
        {
            internal List<Element> Elements { get; } = new List<Element>();

            internal List<string> OutOfBounds { get; } = new List<string>();

            internal List<string> Overlaps { get; } = new List<string>();

            internal List<string> Clipped { get; } = new List<string>();

            internal bool Clean => OutOfBounds.Count == 0 && Overlaps.Count == 0 && Clipped.Count == 0;

            internal string Summary =>
                $"{Elements.Count} elements, {OutOfBounds.Count} out of bounds, " +
                $"{Overlaps.Count} overlapping, {Clipped.Count} clipped";

            internal string FirstFault
            {
                get
                {
                    if (OutOfBounds.Count > 0) return "out of bounds: " + OutOfBounds[0];
                    if (Overlaps.Count > 0) return "overlap: " + Overlaps[0];
                    if (Clipped.Count > 0) return "clipped: " + Clipped[0];
                    return string.Empty;
                }
            }
        }

        /// <summary>
        ///     Inspects everything drawn under <paramref name="root" />.
        /// </summary>
        internal static Result Inspect(Transform root)
        {
            Result result = new Result();
            if (root == null)
            {
                return result;
            }

            Collect(root, root, string.Empty, 1, result.Elements);
            CheckBounds(result);
            CheckOverlaps(result);
            CheckClipping(result);
            return result;
        }

        private static void Collect(Transform node, Transform root, string path, int depth, List<Element> into)
        {
            int index = 0;
            foreach (Transform child in node)
            {
                // Siblings routinely share a name, so the index goes in the path. Without it a
                // genuine fault reports "Button over Button" and names neither.
                string label = child.name + "[" + index++ + "]";
                string childPath = string.IsNullOrEmpty(path) ? label : path + "/" + label;

                // Only things that draw are measured. Containers exist to group, and a group
                // is allowed to span its children.
                Graphic graphic = child.GetComponent<Graphic>();
                Text text = child.GetComponent<Text>();
                bool draws = text != null || (graphic != null && child.GetComponent<Image>() != null);

                if (draws && child is RectTransform rect && child.gameObject.activeInHierarchy)
                {
                    into.Add(Measure(rect, root, childPath, depth, text));
                }

                Collect(child, root, childPath, depth + 1, into);
            }
        }

        private static Element Measure(RectTransform rect, Transform root, string path, int depth, Text text)
        {
            // Corners in world space, then back into the root's space, so nesting and anchors
            // are accounted for rather than assumed away.
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);

            Vector3 min = root.InverseTransformPoint(corners[0]);
            Vector3 max = root.InverseTransformPoint(corners[2]);

            Rect local = Rect.MinMaxRect(
                Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y),
                Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));

            string content = text != null ? text.text : string.Empty;
            float needed = text != null ? text.preferredWidth : 0f;
            return new Element(path, depth, local, content, needed);
        }

        private static void CheckBounds(Result result)
        {
            foreach (Element element in result.Elements)
            {
                if (element.Rect.xMin < Panel.ContentLeft - Tolerance ||
                    element.Rect.xMax > Panel.ContentRight + Tolerance)
                {
                    result.OutOfBounds.Add(
                        $"{element.Path} spans [{element.Rect.xMin:F0}, {element.Rect.xMax:F0}]");
                }
            }
        }

        private static void CheckOverlaps(Result result)
        {
            for (int i = 0; i < result.Elements.Count; i++)
            {
                for (int j = i + 1; j < result.Elements.Count; j++)
                {
                    Element a = result.Elements[i];
                    Element b = result.Elements[j];

                    // Only what the layout placed. How a control arranges its own parts is the
                    // control's business and is frequently deliberate overlap: a button's
                    // label sits on the button, and an input field stacks its placeholder and
                    // its text in the same space because only one of them is ever shown.
                    // Bounds and clipping still apply to every depth - those are faults
                    // wherever they happen.
                    if (a.Depth != 1 || b.Depth != 1)
                    {
                        continue;
                    }

                    // Shrunk before comparing, so elements that merely touch at an edge are
                    // not reported: a one-pixel abutment is a rounding artefact, not a bug.
                    if (!Shrink(a.Rect).Overlaps(Shrink(b.Rect)))
                    {
                        continue;
                    }

                    result.Overlaps.Add($"{a.Path} over {b.Path} near y={a.Rect.center.y:F0}");
                }
            }
        }

        private static void CheckClipping(Result result)
        {
            foreach (Element element in result.Elements)
            {
                if (element.Needed <= 0f || string.IsNullOrEmpty(element.Text))
                {
                    continue;
                }

                if (element.Needed > element.Rect.width + Tolerance)
                {
                    result.Clipped.Add(
                        $"{element.Path} needs {element.Needed:F0} in {element.Rect.width:F0} " +
                        $"for \"{Shorten(element.Text)}\"");
                }
            }
        }

        private static Rect Shrink(Rect rect) =>
            Rect.MinMaxRect(rect.xMin + Tolerance, rect.yMin + Tolerance,
                rect.xMax - Tolerance, rect.yMax - Tolerance);

        private static string Shorten(string text) =>
            text.Length <= 24 ? text : text.Substring(0, 24) + "...";

        private const float Tolerance = 1f;
    }
}
