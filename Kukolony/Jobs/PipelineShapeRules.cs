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

            // Each piece declares what it needs and what it leaves behind, so ordering is one
            // rule rather than a special case per pair. Adding a piece kind means filling in
            // its entry in PieceCustomisation, not editing this method.
            JobCustomisation available = JobCustomisation.None;
            for (int i = 0; i < pieces.Count; i++)
            {
                JobCustomisation required = PieceCustomisation.Requires(pieces[i]);
                if ((available & required) != required)
                {
                    message = "This piece is missing compatible customisation from an earlier selection piece.";
                    return false;
                }
                available |= PieceCustomisation.Provides(pieces[i]);
            }
            message = string.Empty; return true;
        }
    }
}
