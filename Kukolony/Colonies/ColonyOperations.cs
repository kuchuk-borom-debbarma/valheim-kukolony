using System;
using System.Collections.Generic;
using System.Linq;

namespace Kukolony.Colonies
{
    /// <summary>Ordering offered by the Structures tab and the target picker.</summary>
    internal enum StructureSort { Name, Type, Capability, Status }

    /// <summary>
    ///     Colony-level operations shared by the panel and the benchmark scenarios, kept out
    ///     of the UI so both drive the same code paths.
    /// </summary>
    internal static class ColonyOperations
    {
        /// <summary>
        ///     Search, capability filter, and sort over a colony's structure records. Matches
        ///     display name or prefab, so a renamed structure is still findable by what it is.
        ///     <c>Status</c> sorts live records first; ineligible ones remain listed rather
        ///     than hidden, because the player decides whether to remove them.
        /// </summary>
        internal static List<StructureRecord> FilterStructures(Colony colony, string search,
            StructureCapability capability, StructureSort sort)
        {
            string query = (search ?? string.Empty).Trim();
            IEnumerable<StructureRecord> records = colony.State.GetStructures()
                .Where(record => capability == StructureCapability.None ||
                                 (record.Capabilities & capability) != 0)
                .Where(record => query.Length == 0 ||
                                 (record.Name ?? string.Empty).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 (record.Prefab ?? string.Empty).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
            switch (sort)
            {
                case StructureSort.Type: records = records.OrderBy(r => r.Prefab).ThenBy(r => r.Name); break;
                case StructureSort.Capability: records = records.OrderBy(r => (int)r.Capabilities).ThenBy(r => r.Name); break;
                case StructureSort.Status: records = records.OrderByDescending(r => r.IsLiveIn(colony)).ThenBy(r => r.Name); break;
                default: records = records.OrderBy(r => r.Name); break;
            }
            return records.ToList();
        }

        /// <summary>Renames a registered structure. The record keeps its identity and targets.</summary>
        internal static bool RenameStructure(Colony colony, ZDOID id, string name)
        {
            if (colony == null || string.IsNullOrWhiteSpace(name)) return false;
            List<StructureRecord> records = colony.State.GetStructures();
            StructureRecord record = records.Find(r => r.Id == id);
            if (record == null) return false;
            record.Name = name.Trim();
            colony.State.SetStructures(records);
            return true;
        }

        /// <summary>
        ///     Registers every eligible structure currently inside the colony radius and
        ///     returns how many were added. Already-registered candidates are rejected by
        ///     <see cref="Colony.RegisterStructure"/>, so this is safe to repeat.
        /// </summary>
        internal static int RegisterDiscovered(Colony colony)
        {
            int added = 0;
            foreach (StructureRecord candidate in StructureRegistry.FindRegisterable(colony))
                if (colony.RegisterStructure(candidate)) added++;
            return added;
        }

    }
}
