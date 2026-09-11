using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Colonies
{
    /// <summary>
    ///     What a registered container holds, when that can be known at all.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Measured: a container's contents are not readable from its ZDO.</b> A chest
    ///         that demonstrably held two wood, owned by this peer, reported an empty
    ///         <c>s_items</c> string - after the change, after an explicit <c>Save()</c>, read
    ///         through a fresh ZDO reference and through ZDOMan, using the game's own key
    ///         constant. Five runs, each ruling out one explanation.
    ///     </para>
    ///     <para>
    ///         That generalises the villager-bag finding rather than being a quirk of it: the
    ///         bag was written to its own key because <c>Container</c> never flushed, and it
    ///         turns out plain chests behave the same way here.
    ///     </para>
    ///     <para>
    ///         So capacity is knowable only while the structure is loaded, and the settlement
    ///         index must treat an unloaded container as <em>unknown</em> rather than as empty
    ///         or as full. Unknown means still a candidate: sending a villager that finds it
    ///         full costs one walk, while refusing to answer costs the settlement a destination
    ///         it really had.
    ///     </para>
    /// </remarks>
    internal static class StructureInventory
    {


        /// <summary>
        ///     What a container holds right now, or null when that cannot be known.
        /// </summary>
        /// <remarks>
        ///     Null means <em>unknown</em>, and is what an unloaded container returns. It is
        ///     deliberately not an empty inventory: empty reads as "plenty of room", which
        ///     would send every villager to the one chest nobody can see.
        /// </remarks>
        internal static Inventory Live(ZDOID id)
        {
            GameObject instance = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(id) : null;
            if (instance == null) return null;

            Container container = instance.GetComponentInChildren<Container>(true);
            return container != null ? container.GetInventory() : null;
        }

        /// <summary>
        ///     Whether this container has room for an item, read from its ZDO.
        /// </summary>
        /// <remarks>
        ///     Answers true when the contents cannot be read at all. A structure whose capacity
        ///     is unknown is still a candidate: sending a villager that then finds it full
        ///     costs a walk, and refusing to answer costs the settlement a destination it
        ///     actually had.
        /// </remarks>
        /// <summary>
        ///     How many of an item a container holds, counted by prefab.
        /// </summary>
        /// <remarks>
        ///     By prefab rather than by <c>Inventory.CountItems</c>, which matches on the
        ///     shared display name - two different prefabs can share one, and a settlement that
        ///     confuses them fetches the wrong thing.
        /// </remarks>
        internal static int Count(ZDOID id, string itemPrefab)
        {
            Inventory inventory = Live(id);
            if (inventory == null) return 0;

            int total = 0;
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
            {
                if (item?.m_dropPrefab != null && Utils.GetPrefabName(item.m_dropPrefab) == itemPrefab)
                    total += item.m_stack;
            }

            return total;
        }

        internal static bool HasRoomFor(ZDOID id, string itemPrefab)
        {
            Inventory inventory = Live(id);
            if (inventory == null) return true;

            GameObject item = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(itemPrefab) : null;
            if (item == null) return inventory.HaveEmptySlot();

            return inventory.CanAddItem(item);
        }

    }
}
