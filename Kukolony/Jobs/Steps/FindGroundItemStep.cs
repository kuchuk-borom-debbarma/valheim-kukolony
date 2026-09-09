using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Jobs.Steps
{
    /// <summary>
    ///     Finds the nearest matching item lying on the ground within the post's radius.
    ///
    ///     Input:  the post's item filter and radius.
    ///     Output: <see cref="JobContext.Target" /> set to the item.
    /// </summary>
    internal sealed class FindGroundItemStep : IJobStep
    {
        public string Name => "find_ground_item";

        public string Describe(JobContext context) =>
            $"looking for {Readable.Item(context.ItemFilter)}";

        public StepStatus Tick(JobContext context)
        {
            if (context.ItemFilters.Count == 0)
            {
                return StepStatus.Failed;
            }

            ItemDrop closest = null;
            float closestDistance = float.MaxValue;

            // ItemDrop keeps a static registry of every loose item. Iterating it is far
            // cheaper than a physics sweep, and it is exactly what the registry is for.
            foreach (ItemDrop drop in ItemDrop.s_instances)
            {
                if (drop == null || !drop.TryGetComponent(out ZNetView nview) || !nview.IsValid())
                {
                    continue;
                }

                string prefabName = Utils.GetPrefabName(drop.gameObject);
                if (!context.ItemFilters.Contains(prefabName))
                {
                    continue;
                }

                float distance = Utils.DistanceXZ(drop.transform.position, context.Anchor);
                if (distance > context.Radius || distance >= closestDistance)
                {
                    continue;
                }

                // Someone else is already walking to this one. Without the check every
                // villager converges on the nearest item and all but one wastes the trip.
                if (TargetClaims.IsClaimedByOther(nview.GetZDO().m_uid, context.Villager))
                {
                    continue;
                }

                closest = drop;
                closestDistance = distance;
            }

            if (closest == null)
            {
                return StepStatus.Failed;
            }

            context.Target = closest.GetComponent<ZNetView>().GetZDO().m_uid;
            context.Villager.State.SetActiveItem(Utils.GetPrefabName(closest.gameObject));
            Log.Debug($"[job] found {context.ItemFilter} at {closestDistance:F0}m");
            return StepStatus.Succeeded;
        }
    }
}
