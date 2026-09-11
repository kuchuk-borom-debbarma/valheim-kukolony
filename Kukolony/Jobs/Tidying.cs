using System.Collections.Generic;
using UnityEngine;

namespace Kukolony.Jobs
{
    /// <summary>
    ///     Leaving a container better than you found it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Moving the wrong things out is only half of organising. A chest a villager has
    ///         worked should also read tidily: split stacks merged, and contents in an order
    ///         that stays the same from one visit to the next.
    ///     </para>
    ///     <para>
    ///         Costs nothing to walk to, because it happens at a chest the villager is already
    ///         standing at and has already claimed. It is skipped entirely when there is
    ///         nothing to gain - rewriting a container makes it save itself and tells every
    ///         watcher it changed, so a settlement that rewrote every chest it looked at would
    ///         pay for tidiness twenty times a second.
    ///     </para>
    /// </remarks>
    internal static class Tidying
    {
        /// <summary>
        ///     Packs split stacks together and puts the contents in a stable order.
        /// </summary>
        /// <returns>Whether anything actually changed.</returns>
        internal static bool Organise(Container container)
        {
            Inventory inventory = container == null ? null : container.GetInventory();
            if (inventory == null) return false;

            // Writing a container needs owning it, as everywhere else. A non-owner's write
            // lands locally and is clobbered on the next sync, which would look like a chest
            // that tidies itself and then untidies itself.
            if (!container.TryGetComponent(out ZNetView view) || !view.IsValid() || !view.IsOwner())
            {
                return false;
            }

            bool changed = Pack(inventory);
            changed |= Arrange(inventory);

            if (changed) inventory.Changed();
            return changed;
        }

        /// <summary>Merges part-used stacks of the same item.</summary>
        private static bool Pack(Inventory inventory)
        {
            bool changed = false;

            // Grouped first, because GetAllItems returns the backing list and removing from it
            // while walking it throws.
            foreach (KeyValuePair<string, List<ItemDrop.ItemData>> group in GroupByKind(inventory))
            {
                List<ItemDrop.ItemData> items = group.Value;
                if (items.Count < 2) continue;

                int maximum = Mathf.Max(1, items[0].m_shared.m_maxStackSize);

                List<int> sizes = new List<int>();
                foreach (ItemDrop.ItemData item in items) sizes.Add(item.m_stack);

                List<int> packed = Stacking.Pack(sizes, maximum);
                if (packed.Count == 0) continue;

                for (int i = 0; i < items.Count; i++)
                {
                    if (i < packed.Count) items[i].m_stack = packed[i];
                    else inventory.RemoveItem(items[i]);
                }

                changed = true;
            }

            return changed;
        }

        /// <summary>
        ///     Lays the contents out in a stable order.
        /// </summary>
        /// <remarks>
        ///     By prefab name, which is arbitrary but never changes. Display names would read
        ///     better and would also reorder themselves when the game's language changed, so a
        ///     chest would look freshly shuffled to a player who switched language.
        /// </remarks>
        private static bool Arrange(Inventory inventory)
        {
            List<ItemDrop.ItemData> items = new List<ItemDrop.ItemData>(inventory.GetAllItems());
            items.Sort((a, b) => string.CompareOrdinal(Carrying.NameOf(a), Carrying.NameOf(b)));

            int width = Mathf.Max(1, inventory.GetWidth());
            bool changed = false;

            for (int i = 0; i < items.Count; i++)
            {
                Vector2i slot = new Vector2i(i % width, i / width);
                if (items[i].m_gridPos == slot) continue;

                items[i].m_gridPos = slot;
                changed = true;
            }

            return changed;
        }

        private static Dictionary<string, List<ItemDrop.ItemData>> GroupByKind(Inventory inventory)
        {
            Dictionary<string, List<ItemDrop.ItemData>> groups =
                new Dictionary<string, List<ItemDrop.ItemData>>();

            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
            {
                if (item?.m_shared == null) continue;

                // Quality is part of the identity: a stack of worn armour and a stack of new
                // armour are not the same thing, and merging them would upgrade or destroy one.
                string key = Carrying.NameOf(item) + "#" + item.m_quality;
                if (key.Length == 1) continue;

                if (!groups.TryGetValue(key, out List<ItemDrop.ItemData> items))
                {
                    items = new List<ItemDrop.ItemData>();
                    groups[key] = items;
                }

                items.Add(item);
            }

            return groups;
        }
    }
}
