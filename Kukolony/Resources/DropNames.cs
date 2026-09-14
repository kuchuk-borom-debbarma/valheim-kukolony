using System.Collections.Generic;

namespace Kukolony.Resources
{
    /// <summary>
    ///     What a drop table holds, by prefab name.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>The table's own list rather than a roll of it.</b> <c>GetDropList</c> picks at
    ///         random and would answer differently every time it was asked, which is no use for a
    ///         question a player is answering once on a screen - "which rocks give tin" has to be
    ///         the same answer twice running.
    ///     </para>
    ///     <para>
    ///         Shared because mining and foraging both ask it, and because the two would have
    ///         had to agree: a drop table read one way for deposits and another for bushes is
    ///         two answers to the one question that decides what a job's picker offers.
    ///     </para>
    /// </remarks>
    internal static class DropNames
    {
        internal static void Add(List<string> into, DropTable table)
        {
            if (into == null || table?.m_drops == null) return;

            foreach (DropTable.DropData drop in table.m_drops)
            {
                if (drop.m_item == null) continue;

                string name = drop.m_item.name;
                if (name.Length > 0 && !into.Contains(name)) into.Add(name);
            }
        }
    }
}
