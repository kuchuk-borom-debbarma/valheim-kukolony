using UnityEngine;

namespace Kukolony.Gui
{
    /// <summary>
    ///     Panel geometry. The single source of every number the screen is built from.
    /// </summary>
    /// <remarks>
    ///     The predecessor spread these constants across nine hundred lines as literals at call
    ///     sites, which is how controls ended up overlapping each other and rendering past the
    ///     panel onto the world behind it. Nothing outside this file may invent a coordinate.
    /// </remarks>
    internal static class Panel
    {
        internal const float Width = 860f;
        internal const float Height = 680f;

        /// <summary>
        ///     Left edge of the content column. Elements are centre-pivoted, so an element
        ///     spans <c>x ± width/2</c> and the panel's own half-width is 430 - the 30 units of
        ///     margin are what stop copy rendering on the frame itself.
        /// </summary>
        internal const float ContentLeft = -400f;

        internal const float ContentWidth = 800f;
        internal const float ContentRight = ContentLeft + ContentWidth;

        internal const float TitleY = -30f;
        internal const float SubtitleY = -58f;

        /// <summary>First row sits below the title block.</summary>
        internal const float FirstRowY = -96f;

        internal const float RowHeight = 30f;

        /// <summary>
        ///     Distance between row centres. Larger than <see cref="RowHeight" /> so adjacent
        ///     rows have visible air between them rather than merely not intersecting.
        /// </summary>
        internal const float Pitch = 42f;

        /// <summary>Last row centre that still clears the footer.</summary>
        internal const float LastRowY = -540f;

        internal const float MessageY = -580f;
        internal const float PagerY = -614f;
        internal const float FooterY = -648f;

        /// <summary>
        ///     How many rows fit in one page, derived from the geometry rather than chosen.
        ///     A hand-picked row count and a hand-picked pitch disagree the moment either
        ///     changes, and the symptom is a final row drawn over the pager.
        /// </summary>
        internal static int RowsPerPage => Mathf.Max(1, Mathf.FloorToInt((FirstRowY - LastRowY) / Pitch) + 1);

        /// <summary>Gap between two controls sharing a row.</summary>
        internal const float CellGap = 8f;
    }

    /// <summary>
    ///     One row of the screen, which hands out horizontal cells left to right.
    /// </summary>
    /// <remarks>
    ///     Cells are allocated sequentially from a cursor, so two controls in the same row
    ///     <em>cannot</em> overlap - not "are checked for overlap", cannot. That is the whole
    ///     point of routing every control through here. The layout audit still runs, because
    ///     this guarantees nothing about a string being too long for the cell it was given.
    /// </remarks>
    internal sealed class Row
    {
        private float _cursor = Panel.ContentLeft;

        internal Row(Transform parent, float y)
        {
            Parent = parent;
            Y = y;
        }

        internal Transform Parent { get; }

        internal float Y { get; }

        internal float Remaining => Panel.ContentRight - _cursor;

        /// <summary>
        ///     Takes a cell of the requested width and returns its centre.
        /// </summary>
        /// <remarks>
        ///     Returns false rather than overflowing when the row is full. The caller draws
        ///     nothing and says so - a control silently squeezed to nothing looks identical to
        ///     one that was never meant to be there.
        /// </remarks>
        internal bool TryTake(float width, out float centreX, out float granted)
        {
            granted = Mathf.Min(width, Remaining);
            centreX = _cursor + granted / 2f;
            if (granted <= 1f)
            {
                granted = 0f;
                return false;
            }

            _cursor += granted + Panel.CellGap;
            return true;
        }

        /// <summary>Takes everything left in the row, for a trailing label.</summary>
        internal bool TryTakeRest(out float centreX, out float granted) =>
            TryTake(Remaining, out centreX, out granted);
    }

    /// <summary>
    ///     A column of rows at a fixed pitch, which owns paging.
    /// </summary>
    /// <remarks>
    ///     A screen adds every row it has unconditionally and never counts anything: the column
    ///     decides which rows fall inside the current page window and reports how many pages
    ///     there turned out to be. A screen that paged itself would have to know its own length
    ///     before building, which is exactly the bookkeeping that went wrong before.
    /// </remarks>
    internal sealed class Column
    {
        private readonly Transform _parent;
        private readonly int _page;
        private readonly int _perPage;
        private int _index;

        internal Column(Transform parent, int page, int perPage)
        {
            _parent = parent;
            _page = Mathf.Max(0, page);
            _perPage = Mathf.Max(1, perPage);
        }

        internal Column(Transform parent, int page)
            : this(parent, page, Panel.RowsPerPage)
        {
        }

        /// <summary>How many rows were offered, across every page.</summary>
        internal int Total => _index;

        internal int Pages => Mathf.Max(1, Mathf.CeilToInt(Total / (float)_perPage));

        /// <summary>Whether the caller asked for a page past the end.</summary>
        internal bool PageOverran => _page > 0 && _page >= Pages;

        /// <summary>
        ///     Offers the next row, which exists only if it falls on the current page.
        /// </summary>
        internal bool TryRow(out Row row)
        {
            int position = _index - _page * _perPage;
            _index++;

            if (position < 0 || position >= _perPage)
            {
                row = null;
                return false;
            }

            row = new Row(_parent, Panel.FirstRowY - position * Panel.Pitch);
            return true;
        }
    }
}
