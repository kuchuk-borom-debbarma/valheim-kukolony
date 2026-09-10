using System.Collections.Generic;

namespace Kukolony.Jobs.Work
{
    /// <summary>
    ///     Every job the colony knows how to do, by type.
    /// </summary>
    /// <remarks>
    ///     The lookup is a dictionary rather than a switch so that supporting new work is
    ///     additive. A type with no entry falls back to the older execution path while it is
    ///     being migrated, which is what lets jobs move one at a time.
    /// </remarks>
    internal static class WorkRegistry
    {
        private static readonly Dictionary<ColonyJobType, IColonyWork> Jobs =
            Build(new IColonyWork[]
            {
                new HaulWork(),
                new TransferWork(),
                // The four station jobs differ only in which structures they serve. Adding a
                // fifth kind of station is a line here, not a class.
                new StationWork(ColonyJobType.FuelFireplaces, Colonies.StructureCapability.Fireplace),
                new StationWork(ColonyJobType.OperateSmelters, Colonies.StructureCapability.Smelter),
                new StationWork(ColonyJobType.OperateCookingStations, Colonies.StructureCapability.CookingStation),
                new StationWork(ColonyJobType.OperateFermenters, Colonies.StructureCapability.Fermenter),
                new BeehiveWork()
            });

        /// <summary>The job for this type, or null while it still uses the older path.</summary>
        internal static IColonyWork For(ColonyJobType type) =>
            Jobs.TryGetValue(type, out IColonyWork work) ? work : null;

        private static Dictionary<ColonyJobType, IColonyWork> Build(IColonyWork[] jobs)
        {
            Dictionary<ColonyJobType, IColonyWork> map = new Dictionary<ColonyJobType, IColonyWork>();
            foreach (IColonyWork job in jobs) map[job.Type] = job;
            return map;
        }
    }
}
