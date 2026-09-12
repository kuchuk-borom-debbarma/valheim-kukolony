using System.Collections.Generic;
using UnityEngine;

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

            // Keyed by colony and then by item, in two levels rather than one composed
            // string: this is asked every tick by every gathering villager, and building a
            // key per ask would put a string allocation on the hot path the cache exists to
            // keep cheap.
            ZDOID id = colony.Id;
            if (!Counts.TryGetValue(id, out Dictionary<string, Counted> byItem))
            {
                byItem = new Dictionary<string, Counted>();
                Counts[id] = byItem;
            }

            if (byItem.TryGetValue(itemPrefab, out Counted cached) &&
                Time.time - cached.At < FreshnessSeconds)
            {
                return cached.Total;
            }

            int total = 0;
            foreach (StructureRecord record in colony.State.GetStructures())
            {
                if ((record.Capabilities & StructureCapability.Storage) == 0) continue;
                if (record.StatusIn(colony) != StructureStatus.Ready) continue;

                int held = StructureInventory.Count(record.Id, itemPrefab);
                if (held > 0) total += held;
            }

            byItem[itemPrefab] = new Counted { At = Time.time, Total = total };
            return total;
        }

        /// <summary>
        ///     How long an answer stands before it is recounted.
        /// </summary>
        /// <remarks>
        ///     Counting walks every registered container and opens each one, so asking per
        ///     villager per tick is the cost the chopping scan cache exists to avoid. Short
        ///     enough that a stopping rule reacts in the same breath a player would notice,
        ///     long enough that a settlement of choppers counts once rather than each.
        /// </remarks>
        private const float FreshnessSeconds = 1f;

        private sealed class Counted
        {
            internal float At;
            internal int Total;
        }

        private static readonly Dictionary<ZDOID, Dictionary<string, Counted>> Counts =
            new Dictionary<ZDOID, Dictionary<string, Counted>>();

        /// <summary>Dropped when a world unloads; these colonies do not survive one.</summary>
        internal static void Clear() => Counts.Clear();

        /// <summary>
        ///     Forces the next ask to recount.
        /// </summary>
        /// <remarks>
        ///     For checks that change what a settlement holds and then ask about it in the
        ///     same frame. The freshness window is invisible in a running game and is the
        ///     difference between measuring the rule and measuring the cache in a check.
        /// </remarks>
        internal static void ResetForTest() => Counts.Clear();
    }
}
