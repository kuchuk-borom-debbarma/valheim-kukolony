using System.Collections.Generic;

namespace Kukolony.Jobs
{
    /// <summary>Pure pipeline ordering rules shared by the mod and deterministic tests.</summary>
    internal static class PipelineShapeRules
    {
        internal static bool IsValid(IList<JobPieceKind> pieces, out string message)
        {
            if (pieces == null || pieces.Count < 2 || pieces[0] != JobPieceKind.Start ||
                pieces[pieces.Count - 1] != JobPieceKind.End)
            { message = "A job must start with Start and end with End."; return false; }
            for (int i = 1; i < pieces.Count; i++)
                if ((pieces[i] == JobPieceKind.PickUp && !HasBefore(pieces, i, JobPieceKind.FindLooseItem)) ||
                    (pieces[i] == JobPieceKind.TakeItem && !HasBefore(pieces, i, JobPieceKind.SelectSource)) ||
                    (pieces[i] == JobPieceKind.PutItem && !HasBefore(pieces, i, JobPieceKind.SelectTarget)))
                { message = "This piece is missing compatible customisation from an earlier selection piece."; return false; }
            message = string.Empty; return true;
        }

        private static bool HasBefore(IList<JobPieceKind> pieces, int index, JobPieceKind kind)
        { for (int i = 0; i < index; i++) if (pieces[i] == kind) return true; return false; }
    }
}
