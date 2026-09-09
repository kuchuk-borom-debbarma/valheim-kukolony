using System.Collections.Generic;
using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Jobs.Steps
{
    /// <summary>
    ///     Empties the villager's bag into the targeted container.
    ///
    ///     Input:  <see cref="JobContext.Target" /> pointing at a Container.
    ///     Output: the bag is empty and the container holds the goods.
    ///
    ///     Container writes only persist for the ZDO owner - Container.OnContainerChanged
    ///     checks IsOwner before saving - so ownership is claimed first. Without that the
    ///     transfer appears to work and is silently lost on the next sync.
    /// </summary>
    internal sealed class DepositItemStep : IJobStep
    {
        public string Name => "deposit_item";

        public string Describe(JobContext context) =>
            $"storing {Readable.Item(context.ItemFilter)}";

        public StepStatus Tick(JobContext context)
        {
            GameObject target = context.ResolveTarget();
            if (target == null || !target.TryGetComponent(out Container container))
            {
                return StepStatus.Failed;
            }

            if (!target.TryGetComponent(out ZNetView nview) || !nview.IsValid())
            {
                return StepStatus.Failed;
            }

            if (!nview.IsOwner())
            {
                nview.ClaimOwnership();
                return StepStatus.Running;
            }

            Inventory bag = context.Bag.GetInventory();
            Inventory destination = container.GetInventory();
            if (bag == null || destination == null)
            {
                return StepStatus.Failed;
            }

            // Copy the list: moving items mutates the source collection.
            List<ItemDrop.ItemData> carried = new List<ItemDrop.ItemData>(bag.GetAllItems());
            if (carried.Count == 0)
            {
                context.Target = ZDOID.None;
                return StepStatus.Succeeded;
            }

            int moved = 0;
            foreach (ItemDrop.ItemData item in carried)
            {
                if (!destination.CanAddItem(item))
                {
                    // Full. What is left stays in the bag and goes in next cycle.
                    break;
                }

                destination.MoveItemToThis(bag, item);
                moved++;
            }

            if (moved == 0)
            {
                return StepStatus.Failed;
            }

            context.Target = ZDOID.None;
            Log.Debug($"[job] deposited {moved} stack(s)");
            return StepStatus.Succeeded;
        }
    }
}
