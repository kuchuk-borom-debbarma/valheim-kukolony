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
                // Keyed by what it is, not by which one: a hundred anonymous logs of the same
                // kind is one problem reported a hundred times, and the count is the useful part.
                Chatter.Warn("[haul] unidentified " + drop.gameObject.name,
                    $"'{drop.gameObject.name}' has no prefab to identify it; leaving it");
                return TakeResult.Unavailable;
            }

            if (!bag.CanAddItem(item)) return TakeResult.Full;

            if (!bag.AddItem(item.Clone())) return TakeResult.Full;

            taken = Utils.GetPrefabName(item.m_dropPrefab);
            view.Destroy();
            return TakeResult.Took;
        }

        /// <summary>
        ///     Takes one kind of item out of a container and into a bag.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Ownership first, as everywhere else: writing a container's inventory requires
        ///         owning it, asking is an RPC, and reporting <see cref="TakeResult.Waiting" />
        ///         and trying again next tick is the pattern that works.
        ///     </para>
        ///     <para>
        ///         A stack larger than the space left is taken in part rather than refused. The
        ///         alternative - all or nothing - means a bag with four free slots ignores a
        ///         chest holding one enormous stack, which is the exact case where hauling was
        ///         wanted most.
        ///     </para>
        /// </remarks>
        /// <param name="most">
        ///     The most to take, or a negative number for as much as will fit. Tending asks for
        ///     exactly what a station is short of: a villager that emptied a chest of fifty wood
        ///     to put five in a kiln would spend the rest of the trip carrying forty-five back.
        /// </param>
        internal static TakeResult TakeFromContainer(Container from, ItemDrop.ItemData item, Inventory bag,
            out string taken, int most = -1)
        {
            taken = string.Empty;
            if (from == null || item == null || bag == null) return TakeResult.Unavailable;
            if (!from.TryGetComponent(out ZNetView view) || !view.IsValid()) return TakeResult.Unavailable;

            if (!view.IsOwner())
            {
                view.ClaimOwnership();
                return TakeResult.Waiting;
            }

            Inventory contents = from.GetInventory();
            if (contents == null) return TakeResult.Unavailable;

            int room = RoomFor(bag, item);
            if (room <= 0) return TakeResult.Full;

            int amount = Mathf.Min(item.m_stack, room);
            if (most >= 0) amount = Mathf.Min(amount, most);
            if (amount <= 0) return TakeResult.Full;

            if (!TryFindSlot(bag, item, out int x, out int y)) return TakeResult.Full;

            taken = NameOf(item);
            return bag.MoveItemToThis(contents, item, amount, x, y) ? TakeResult.Took : TakeResult.Full;
        }

        /// <summary>
        ///     How many of an item an inventory could still take.
        /// </summary>
        /// <remarks>
        ///     Counted rather than asked, because <c>CanAddItem</c> answers only yes or no for a
        ///     whole stack and the interesting case is the partial one. Room in existing stacks
        ///     of the same item, plus a full stack for every empty slot. Used by both halves of
        ///     the trip - taking and depositing - so a chest with room for part of a load is
        ///     treated the same way on the way in as on the way out.
        /// </remarks>
        private static int RoomFor(Inventory inventory, ItemDrop.ItemData item)
        {
            if (item.m_shared == null) return 0;

            int maximum = Mathf.Max(1, item.m_shared.m_maxStackSize);
            int room = inventory.GetEmptySlots() * maximum;

            string prefab = NameOf(item);
            foreach (ItemDrop.ItemData existing in inventory.GetAllItems())
            {
                if (NameOf(existing) != prefab || existing.m_quality != item.m_quality) continue;
                room += Mathf.Max(0, maximum - existing.m_stack);
            }

            return room;
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
        /// <param name="allowed">
        ///     How many more of this item the destination was told to take, or a negative
        ///     number for no limit.
        /// </param>
        internal static TakeResult Deposit(Inventory bag, ItemDrop.ItemData item, Container into,
            int allowed = -1)
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

            // Counted rather than asked. CanAddItem answers for the whole stack at once - it is
            // free stack space plus empty slots measured against item.m_stack - so a villager
            // carrying fifty wood to a chest with room for twenty was told no and put down
            // nothing at all. The taking half has always worked this out properly; this half
            // asked the yes-or-no question and believed it, and the job is specified the other
            // way: partial deposits are fine, and what will not fit stays in the bag.
            int room = RoomFor(destination, item);

            // Bounded by the cap as well as by the shelf space. The cap used to be enforced
            // only by the shuffle that carried an overshoot back out again - and now that a
            // chest keeps what it holds, nothing would ever bring it back down. A shed told
            // to keep ten, holding nine, would take a villager's fifty and sit at fifty-nine
            // for good, with its own screen still reading "at most 10".
            if (allowed >= 0) room = Mathf.Min(room, allowed);
            if (room <= 0) return TakeResult.Full;

            if (!TryFindSlot(destination, item, out int x, out int y)) return TakeResult.Full;

            int amount = Mathf.Min(item.m_stack, room);
            return destination.MoveItemToThis(bag, item, amount, x, y)
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
