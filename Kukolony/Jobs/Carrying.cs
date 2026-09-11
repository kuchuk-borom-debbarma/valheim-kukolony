using System.Collections.Generic;
using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Jobs
{
    /// <summary>How a take ended.</summary>
    internal enum TakeResult
    {
        /// <summary>Something moved into the bag.</summary>
        Took,

        /// <summary>Not ours yet. Ownership was requested; try again next tick.</summary>
        Waiting,

        /// <summary>The bag has no room for it.</summary>
        Full,

        /// <summary>Gone, or never takeable.</summary>
        Unavailable
    }

    /// <summary>
    ///     Moving items between the world, a villager's bag, and containers.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Never <c>ItemDrop.Pickup</c>.</b> It puts the item into
    ///         <c>Humanoid.m_inventory</c>, which is never written to disk for anything but a
    ///         player, so a villager would carry wood that vanished the moment its zone
    ///         unloaded. Worse, when the item is not already owned it starts a repeating
    ///         coroutine that casts the requester to <c>Player</c> — a null reference for a
    ///         villager. The take is done by hand instead.
    ///     </para>
    ///     <para>
    ///         <b>Claiming is asynchronous, so a take may need a second tick.</b> Removing a
    ///         ground item requires owning it; asking is an RPC. Reporting
    ///         <see cref="TakeResult.Waiting" /> and trying again is the pattern that works for
    ///         items and containers alike.
    ///     </para>
    /// </remarks>
    internal static class Carrying
    {
        /// <summary>
        ///     Takes a loose item off the ground into a bag, reporting what was taken.
        /// </summary>
        /// <remarks>
        ///     <paramref name="taken" /> is an output rather than something the caller reads
        ///     back off the drop, because a fully-taken drop is destroyed by the time this
        ///     returns and touching a destroyed component throws. It is also the only place
        ///     that knows the repaired prefab name for an item that reached the ground without
        ///     one.
        /// </remarks>
        internal static TakeResult TakeFromGround(ItemDrop drop, Inventory bag, out string taken)
        {
            taken = string.Empty;
            if (drop == null || bag == null) return TakeResult.Unavailable;
            if (drop.m_itemData?.m_dropPrefab == null && drop.m_itemData?.m_shared == null)
                return TakeResult.Unavailable;

            if (!drop.TryGetComponent(out ZNetView view) || !view.IsValid()) return TakeResult.Unavailable;

            // CanPickup is the ownership test, and it also enforces a half-second blackout
            // after an item is spawned - so a villager cannot instantly re-take what it just
            // put down, which is what stops a drop-and-grab loop.
            if (!drop.CanPickup())
            {
                view.ClaimOwnership();
                return TakeResult.Waiting;
            }

            drop.Load();
            ItemDrop.ItemData item = drop.m_itemData;

            // An item needs to know which prefab it came from, or it has no identity once it
            // is in a bag - and the settlement asks "where does a Wood go" by prefab name. The
            // field is set when an item passes through an inventory, so anything that reached
            // the ground another way arrives without it and would become unhaulable the moment
            // it was picked up.
            if (item.m_dropPrefab == null && ObjectDB.instance != null)
            {
                item.m_dropPrefab = ObjectDB.instance.GetItemPrefab(Utils.GetPrefabName(drop.gameObject));
            }

            if (item.m_dropPrefab == null)
            {
                Log.Warning($"[haul] '{drop.gameObject.name}' has no prefab to identify it; leaving it");
                return TakeResult.Unavailable;
            }

            if (!bag.CanAddItem(item)) return TakeResult.Full;

            if (!bag.AddItem(item.Clone())) return TakeResult.Full;

            taken = Utils.GetPrefabName(item.m_dropPrefab);
            view.Destroy();
            return TakeResult.Took;
        }

        /// <summary>
        ///     Puts an item back on the ground.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         For the item nothing in the settlement claims. Left in the bag it would ride
        ///         around forever, and a villager whose bag slowly fills with oddments stops
        ///         being able to haul at all - so the settlement's answer of "leave it where it
        ///         is and say so" has to include putting down what was already picked up.
        ///     </para>
        ///     <para>
        ///         Spawned from the prefab and given the item data by hand, the same way the
        ///         game's own drop does. <c>ItemDrop.DropItem</c> is not used: it throws on item
        ///         data with no <c>m_dropPrefab</c>, and a throw inside a job kills the coroutine
        ///         driving it rather than failing anything visible.
        ///     </para>
        ///     <para>
        ///         Safe from being picked straight back up, because an item with no home is
        ///         never chosen as work in the first place.
        ///     </para>
        /// </remarks>
        internal static bool PutDown(Inventory bag, ItemDrop.ItemData item, Vector3 where)
        {
            if (bag == null || item?.m_dropPrefab == null) return false;

            GameObject spawned = Object.Instantiate(item.m_dropPrefab, where, Quaternion.identity);
            if (spawned == null) return false;

            if (!spawned.TryGetComponent(out ItemDrop drop) ||
                !spawned.TryGetComponent(out ZNetView view) || !view.IsValid())
            {
                Object.Destroy(spawned);
                return false;
            }

            ItemDrop.ItemData copy = item.Clone();
            copy.m_dropPrefab = item.m_dropPrefab;
            drop.m_itemData = copy;
            drop.Save();

            bag.RemoveItem(item);
            return true;
        }

        /// <summary>
        ///     Moves one item from a bag into a container.
        /// </summary>
        /// <remarks>
        ///     The slot is found here rather than left to the game.
        ///     <c>Inventory.MoveItemToThis</c>'s amount overload rejects <c>(-1, -1)</c> even
        ///     after <c>CanAddItem</c> has said yes — a measured API defect, and a silent one,
        ///     so the working pattern is to prefer an existing compatible stack and fall back to
        ///     an empty slot.
        /// </remarks>
        internal static TakeResult Deposit(Inventory bag, ItemDrop.ItemData item, Container into)
        {
            if (bag == null || item == null || into == null) return TakeResult.Unavailable;
            if (!into.TryGetComponent(out ZNetView view) || !view.IsValid()) return TakeResult.Unavailable;

            // Writing a container's inventory requires owning it; the container saves itself
            // once we do, through its own change hook.
            if (!view.IsOwner())
            {
                view.ClaimOwnership();
                return TakeResult.Waiting;
            }

            Inventory destination = into.GetInventory();
            if (destination == null) return TakeResult.Unavailable;
            if (!destination.CanAddItem(item)) return TakeResult.Full;

            if (!TryFindSlot(destination, item, out int x, out int y)) return TakeResult.Full;

            return destination.MoveItemToThis(bag, item, item.m_stack, x, y)
                ? TakeResult.Took
                : TakeResult.Full;
        }

        /// <summary>
        ///     The hauled goods in a bag: the items on the trip's manifest, and nothing else.
        /// </summary>
        /// <remarks>
        ///     Deliberately not "everything in the bag". A villager's clothing lives in the same
        ///     inventory as what it is hauling, so a job that took the whole bag would try to
        ///     file the villager's own trousers in a chest. An empty cargo name means this trip
        ///     is carrying nothing, whatever else the bag holds.
        /// </remarks>
        internal static List<ItemDrop.ItemData> Cargo(Inventory bag, string manifest)
        {
            List<ItemDrop.ItemData> found = new List<ItemDrop.ItemData>();
            if (bag == null || string.IsNullOrEmpty(manifest)) return found;

            string[] listed = manifest.Split(',');
            foreach (ItemDrop.ItemData item in bag.GetAllItems())
            {
                if (item?.m_dropPrefab == null) continue;

                string name = Utils.GetPrefabName(item.m_dropPrefab);
                foreach (string wanted in listed)
                {
                    if (wanted != name) continue;
                    found.Add(item);
                    break;
                }
            }

            return found;
        }

        /// <summary>The prefab an item came from, or empty if it has no identity.</summary>
        internal static string NameOf(ItemDrop.ItemData item) =>
            item?.m_dropPrefab == null ? string.Empty : Utils.GetPrefabName(item.m_dropPrefab);

        /// <summary>
        ///     A real grid coordinate for an item: an existing stack of the same thing with room,
        ///     else the first empty slot.
        /// </summary>
        private static bool TryFindSlot(Inventory destination, ItemDrop.ItemData item, out int x, out int y)
        {
            x = 0;
            y = 0;
            if (item.m_shared == null) return false;

            int width = destination.GetWidth();
            int height = destination.GetHeight();

            // Prefer topping up a stack, so a chest does not fill with part-empty slots.
            foreach (ItemDrop.ItemData existing in destination.GetAllItems())
            {
                if (existing?.m_shared == null) continue;
                if (existing.m_shared.m_name != item.m_shared.m_name) continue;
                if (existing.m_quality != item.m_quality) continue;
                if (existing.m_stack >= existing.m_shared.m_maxStackSize) continue;

                x = existing.m_gridPos.x;
                y = existing.m_gridPos.y;
                return true;
            }

            for (int row = 0; row < height; row++)
            {
                for (int column = 0; column < width; column++)
                {
                    if (destination.GetItemAt(column, row) != null) continue;
                    x = column;
                    y = row;
                    return true;
                }
            }

            return false;
        }
    }
}
