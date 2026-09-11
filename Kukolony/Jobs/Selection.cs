using System.Collections.Generic;
using Kukolony.Colonies;
using Kukolony.Villagers;
using UnityEngine;

namespace Kukolony.Jobs
{
    /// <summary>
    ///     Choosing what to work on, and where the result goes.
    /// </summary>
    /// <remarks>
    ///     Kept apart from the job that uses it. The previous engine put selection, movement,
    ///     inventory and station adapters in one class that every job called back into
    ///     statically; it reached seven hundred lines and the interface stayed clean while the
    ///     thing behind it did not.
    /// </remarks>
    internal static class Selection
    {
        /// <summary>
        ///     The nearest loose item worth hauling, with somewhere for it to go.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Distance is measured from the villager, but candidates are bounded by the
        ///         colony's reach rather than the villager's whim, so a settlement works its own
        ///         ground instead of wandering after whatever happens to be closest.
        ///     </para>
        ///     <para>
        ///         A destination is required <em>before</em> the villager sets off. Choosing an
        ///         item and discovering on arrival that nothing wants it fails the job after a
        ///         walk, which is a worse answer than choosing something else.
        ///     </para>
        /// </remarks>
        internal static bool TryFindGroundWork(Colony colony, JobDefinition job, Villager asker,
            out ItemDrop item, out StructureRecord destination)
        {
            item = null;
            destination = null;
            if (colony == null || asker == null) return false;

            Vector3 centre = colony.transform.position;
            Vector3 from = asker.transform.position;
            float reach = colony.EffectiveRadius;
            float best = float.MaxValue;

            // The game keeps its own registry of loose items, so this never walks every loaded
            // object. It holds only what is loaded, which is the right meaning of "nearby".
            foreach (ItemDrop drop in ItemDrop.s_instances)
            {
                if (drop == null || drop.m_itemData?.m_dropPrefab == null) continue;
                if (Utils.DistanceXZ(drop.transform.position, centre) > reach) continue;

                if (!drop.TryGetComponent(out ZNetView view) || !view.IsValid()) continue;

                string prefab = Utils.GetPrefabName(drop.m_itemData.m_dropPrefab);
                if (!Wanted(job, prefab)) continue;

                ZDOID id = view.GetZDO().m_uid;
                if (TargetClaims.IsClaimedByOther(id, asker)) continue;

                float distance = Utils.DistanceXZ(drop.transform.position, from);
                if (distance >= best) continue;

                List<StructureRecord> homes = SettlementIndex.WhereDoesItGo(colony, prefab, from);
                if (homes.Count == 0) continue;

                best = distance;
                item = drop;
                destination = homes[0];
            }

            return item != null;
        }

        /// <summary>
        ///     Where a carried item should go, or null if nothing will take it.
        /// </summary>
        internal static StructureRecord WhereFor(Colony colony, string itemPrefab, Vector3 from)
        {
            List<StructureRecord> homes = SettlementIndex.WhereDoesItGo(colony, itemPrefab, from);
            return homes.Count == 0 ? null : homes[0];
        }

        /// <summary>Whether a job handles this item. An empty list means everything.</summary>
        private static bool Wanted(JobDefinition job, string prefab) =>
            job?.Items == null || job.Items.Count == 0 || job.Items.Contains(prefab);
    }
}
