using System;
using System.Collections.Generic;
using Kukolony.Colonies;

namespace Kukolony.Jobs
{
    internal enum ColonyJobType { HaulLoose, Transfer, FuelFireplaces, OperateSmelters, OperateCookingStations, OperateFermenters, CollectBeehives }
    internal enum TargetMode { All, Selected, Ignore }
    internal enum JobResult { Running, Completed, Failed, Skipped }

    /// <summary>Typed colony-owned configuration. It deliberately has no player-authored step graph.</summary>
    internal sealed class ColonyJobConfig
    {
        internal string Id = Guid.NewGuid().ToString("N");
        internal ColonyJobType Type;
        internal string Name;
        internal TargetMode Targets = TargetMode.All;
        internal List<ZDOID> SelectedStructures = new List<ZDOID>();
        internal List<string> ItemFilters = new List<string>();
        internal ZDOID Source = ZDOID.None;
        internal ZDOID Destination = ZDOID.None;
        internal int StockLimit;
        internal int Count = 1;
    }

    internal static class ColonyJobCatalog
    {
        internal static readonly ColonyJobType[] All = (ColonyJobType[])Enum.GetValues(typeof(ColonyJobType));
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
        internal static StructureCapability RequiredCapability(ColonyJobType type) => type == ColonyJobType.FuelFireplaces ? StructureCapability.Fireplace :
            type == ColonyJobType.OperateSmelters ? StructureCapability.Smelter : type == ColonyJobType.OperateCookingStations ? StructureCapability.CookingStation :
            type == ColonyJobType.OperateFermenters ? StructureCapability.Fermenter : type == ColonyJobType.CollectBeehives ? StructureCapability.BeeHive : StructureCapability.Container;
    }
}
