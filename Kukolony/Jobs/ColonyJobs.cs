using System;
using System.Collections.Generic;
using Kukolony.Colonies;

namespace Kukolony.Jobs
{
    internal enum ColonyJobType
    {
        HaulLoose,
        Transfer,
        FuelFireplaces,
        OperateSmelters,
        OperateCookingStations,
        OperateFermenters,
        CollectBeehives
    }

    internal enum TargetMode { All, Selected, Ignore }
    internal enum JobResult { Running, Completed, Failed, Skipped }

    internal sealed class ColonyJobConfig
    {
        internal string Id = Guid.NewGuid().ToString("N");
        internal ColonyJobType Type;
        internal string Name = string.Empty;
        internal TargetMode Targets = TargetMode.All;
        internal readonly List<ZDOID> SelectedStructures = new List<ZDOID>();
        internal readonly List<string> ItemFilters = new List<string>();
        internal ZDOID Source = ZDOID.None;
        internal ZDOID Destination = ZDOID.None;
        internal int StockLimit;
        internal int Count = 1;
        internal bool Reservations = true;
        internal float SearchRadius = 32f;
        internal float StopDistance = 2f;

        internal ColonyJobConfig Clone(bool includeTargets)
        {
            ColonyJobConfig copy = new ColonyJobConfig
            {
                Id = Id,
                Type = Type,
                Name = Name,
                Targets = includeTargets ? Targets : TargetMode.All,
                Source = includeTargets ? Source : ZDOID.None,
                Destination = includeTargets ? Destination : ZDOID.None,
                StockLimit = StockLimit,
                Count = Count,
                Reservations = Reservations,
                SearchRadius = SearchRadius,
                StopDistance = StopDistance
            };
            copy.ItemFilters.AddRange(ItemFilters);
            if (includeTargets) copy.SelectedStructures.AddRange(SelectedStructures);
            return copy;
        }
    }

    internal sealed class JobPreset
    {
        internal string Name = string.Empty;
        internal bool ColonyLocal;
        internal ColonyJobConfig Settings = new ColonyJobConfig();
    }

    internal static class ColonyJobCatalog
    {
        internal static readonly ColonyJobType[] All =
            (ColonyJobType[])Enum.GetValues(typeof(ColonyJobType));

        internal static List<ColonyJobConfig> CreateDefaults()
        {
            List<ColonyJobConfig> jobs = new List<ColonyJobConfig>();
            foreach (ColonyJobType type in All)
            {
                ColonyJobConfig job = new ColonyJobConfig
                {
                    Id = "default." + type.ToString().ToLowerInvariant(),
                    Type = type,
                    Name = DisplayName(type),
                    Count = 1
                };
                switch (type)
                {
                    case ColonyJobType.HaulLoose:
                    case ColonyJobType.Transfer:
                        job.ItemFilters.Add("Wood");
                        break;
                    case ColonyJobType.FuelFireplaces:
                        job.ItemFilters.Add("Wood");
                        break;
                    case ColonyJobType.OperateSmelters:
                        job.ItemFilters.Add("Coal");
                        job.ItemFilters.Add("CopperOre");
                        break;
                    case ColonyJobType.OperateCookingStations:
                        job.ItemFilters.Add("RawMeat");
                        break;
                    case ColonyJobType.OperateFermenters:
                        job.ItemFilters.Add("BarleyWineBase");
                        break;
                    case ColonyJobType.CollectBeehives:
                        job.ItemFilters.Add("Honey");
                        break;
                }
                jobs.Add(job);
            }
            return jobs;
        }

        internal static string DisplayName(ColonyJobType type)
        {
            switch (type)
            {
                case ColonyJobType.HaulLoose: return "Haul loose items";
                case ColonyJobType.Transfer: return "Transfer containers";
                case ColonyJobType.FuelFireplaces: return "Fuel fireplaces";
                case ColonyJobType.OperateSmelters: return "Operate smelters and kilns";
                case ColonyJobType.OperateCookingStations: return "Operate cooking stations";
                case ColonyJobType.OperateFermenters: return "Operate fermenters";
                default: return "Collect beehives";
            }
        }

        internal static StructureCapability RequiredCapability(ColonyJobType type)
        {
            switch (type)
            {
                case ColonyJobType.FuelFireplaces: return StructureCapability.Fireplace;
                case ColonyJobType.OperateSmelters: return StructureCapability.Smelter;
                case ColonyJobType.OperateCookingStations: return StructureCapability.CookingStation;
                case ColonyJobType.OperateFermenters: return StructureCapability.Fermenter;
                case ColonyJobType.CollectBeehives: return StructureCapability.BeeHive;
                default: return StructureCapability.Container;
            }
        }
    }
}
