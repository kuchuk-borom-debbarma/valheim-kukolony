using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Jobs.Steps
{
    /// <summary>
    ///     Moves the targeted ground item into the villager's bag.
    ///
    ///     Input:  <see cref="JobContext.Target" /> pointing at an ItemDrop.
    ///     Output: the item is in the bag and gone from the world.
    ///
    ///     Picking up needs ZDO ownership - ItemDrop.CanPickup is literally
    ///     `m_nview.IsOwner()`. Requesting it is asynchronous, so this step stays Running
    ///     until ownership arrives rather than failing on the first attempt.
    /// </summary>
    internal sealed class PickUpItemStep : IJobStep
    {
        /// <summary>
        ///     ItemDrop.RequestOwn backs off exponentially up to 30s, so a few seconds of
        ///     waiting is normal. Beyond this the owner is probably not coming.
        /// </summary>
        private const float OwnershipTimeoutSeconds = 10f;

        private float _waiting;

        public string Name => "pick_up_item";

        public string Describe(JobContext context) =>
            $"picking up {Readable.Item(context.ItemFilter)}";

        public StepStatus Tick(JobContext context)
        {
            GameObject target = context.ResolveTarget();
            if (target == null || !target.TryGetComponent(out ItemDrop drop))
            {
                _waiting = 0f;
                return StepStatus.Failed;
            }

            if (!drop.TryGetComponent(out ZNetView nview) || !nview.IsValid())
            {
                _waiting = 0f;
                return StepStatus.Failed;
            }

            if (!nview.IsOwner())
            {
                _waiting += context.DeltaTime;
                if (_waiting > OwnershipTimeoutSeconds)
                {
                    _waiting = 0f;
                    Log.Debug("[job] gave up waiting for item ownership");
                    return StepStatus.Failed;
                }

                // Vanilla's own retry path, including its backoff.
                drop.RequestOwn();
                return StepStatus.Running;
            }

            _waiting = 0f;

            Inventory bag = context.Bag.GetInventory();
            if (bag == null || !bag.CanAddItem(drop.m_itemData))
            {
                return StepStatus.Failed;
            }

            int stack = drop.m_itemData.m_stack;
            bag.AddItem(drop.m_itemData);

            // Destroy through ZNetScene so the ZDO goes with it, exactly as
            // Humanoid.Pickup does.
            ZNetScene.instance.Destroy(drop.gameObject);

            context.Target = ZDOID.None;
            Log.Debug($"[job] picked up {stack}x {context.ItemFilter}");
            return StepStatus.Succeeded;
        }
    }
}
