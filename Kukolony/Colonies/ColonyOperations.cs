using System;
using System.Collections.Generic;
using System.Linq;
using Kukolony.Jobs;

namespace Kukolony.Colonies
{
    internal enum StructureSort { Name, Type, Capability, Status }

    internal static class ColonyOperations
    {
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

        internal static int RegisterDiscovered(Colony colony)
        {
            int added = 0;
            foreach (StructureRecord candidate in StructureRegistry.FindRegisterable(colony))
                if (colony.RegisterStructure(candidate)) added++;
            return added;
        }

        internal static void SavePreset(Colony colony, string name, ColonyJobConfig job, bool local)
        {
            List<JobPreset> presets = colony.State.GetPresets();
            presets.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            presets.Add(new JobPreset { Name = name.Trim(), ColonyLocal = local, Settings = job.Clone(local) });
            colony.State.SetPresets(presets);
        }

        internal static ColonyJobConfig ApplyPreset(JobPreset preset)
        {
            ColonyJobConfig job = preset.Settings.Clone(preset.ColonyLocal);
            job.Id = Guid.NewGuid().ToString("N");
            return job;
        }
    }
}
