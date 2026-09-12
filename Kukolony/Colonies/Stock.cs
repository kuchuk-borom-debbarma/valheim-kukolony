namespace Kukolony.Colonies
{
    /// <summary>
    ///     How much of something the settlement is holding.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The question a gathering job needs and a hauling job never did. Hauling stops
    ///         when nothing is misplaced, which is visible and self-limiting; a forest has no
    ///         such point, so a woodcutter needs to be told what "enough" is and then be able
    ///         to ask whether it has it.
    ///     </para>
    ///     <para>
    ///         <b>An unreadable container counts as nothing, and that is the safe direction.</b>
    ///         Contents are a loaded-only question - a chest's ZDO reports an empty item string
    ///         even when it demonstrably holds two wood, measured five ways - so a settlement
    ///         whose woodshed is off-screen under-counts. Under-counting means the job keeps
    ///         working when it might have stopped, which costs some wood nobody needed;
    ///         over-counting would mean a job that stops gathering because it could not see,
    ///         and a settlement that quietly starves is the worse of the two.
    ///     </para>
    /// </remarks>
    internal static class Stock
    {
        /// <summary>
        ///     How many of an item prefab the settlement's registered storage holds.
        /// </summary>
        /// <remarks>
        ///     Counted over storage the settlement can actually reach, so a chest that has
        ///     fallen out of reach stops propping up a stopping rule - the same containers the
        ///     index would offer as destinations are the ones that count as holdings.
        /// </remarks>
        internal static int Held(Colony colony, string itemPrefab)
        {
            if (colony == null || string.IsNullOrEmpty(itemPrefab)) return 0;

            int total = 0;
            foreach (StructureRecord record in colony.State.GetStructures())
            {
                if ((record.Capabilities & StructureCapability.Storage) == 0) continue;
                if (record.StatusIn(colony) != StructureStatus.Ready) continue;

                int held = StructureInventory.Count(record.Id, itemPrefab);
                if (held > 0) total += held;
            }

            return total;
        }
    }
}
