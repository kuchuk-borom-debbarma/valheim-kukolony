using System.Collections.Generic;
using Kukolony.Colonies;
using Kukolony.Colonies.Stations;
using UnityEngine;

namespace Kukolony.Jobs.Craft
{
    /// <summary>
    ///     Mending worn gear at a station that can mend it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Kept apart from the crafting path because it shares the legs and nothing else.
    ///         Both fetch something from a chest and carry it to a station; a craft then spends
    ///         what it carried and produces something new, while a repair spends <em>nothing</em>
    ///         and hands back the same item. Vanilla's repair costs no materials at all - it sets
    ///         durability to its maximum and stops - and folding that into the consumption path
    ///         would mean a branch through every line of it.
    ///     </para>
    ///     <para>
    ///         The gate is vanilla's, reproduced rather than called: <c>InventoryGui.CanRepair</c>
    ///         needs a <c>Player</c> for its cheat check and its current station. What it decides
    ///         is written out here with the original beside it, so a Valheim update that changes
    ///         the rule is visible rather than silently disagreed with.
    ///     </para>
    /// </remarks>
    internal static class Repairing
    {
        /// <summary>
        ///     The highest station level any recipe asks for.
        /// </summary>
        /// <remarks>
        ///     Vanilla compares with <c>Mathf.Min(station.GetLevel(), 4)</c>, so a bench with
        ///     five extensions is no better than one with three. Mirrored, or a villager would
        ///     refuse a repair the player can do standing at the same bench.
        /// </remarks>
        private const int HighestLevel = 4;

        /// <summary>Whether an item has lost durability and can get it back.</summary>
        /// <remarks>
        ///     <c>m_canBeReparied</c> is vanilla's spelling, typo and all. Worth naming here so
        ///     the next person to grep for "repaired" and find nothing knows why.
        /// </remarks>
        internal static bool Worn(ItemDrop.ItemData item) =>
            item?.m_shared != null && item.m_shared.m_useDurability &&
            item.m_shared.m_canBeReparied && item.m_durability < item.GetMaxDurability();

        /// <summary>
        ///     Whether this station is the right place to mend this item.
        /// </summary>
        /// <remarks>
        ///     Either the recipe's repair station or the one that makes it, matched by name -
        ///     which is what lets a modded forge repair what a vanilla forge would, and why the
        ///     comparison is on <c>m_name</c> rather than on the component or the prefab.
        /// </remarks>
        internal static bool CanMend(CraftStation station, ItemDrop.ItemData item)
        {
            if (station == null || !station.IsValid || !Worn(item)) return false;
            if (ObjectDB.instance == null) return false;

            Recipe recipe = ObjectDB.instance.GetRecipe(item);
            if (recipe == null) return false;
            if (recipe.m_craftingStation == null && recipe.m_repairStation == null) return false;

            string here = station.StationName;
            bool right = (recipe.m_repairStation != null && recipe.m_repairStation.m_name == here) ||
                         (recipe.m_craftingStation != null && recipe.m_craftingStation.m_name == here);

            if (!right) return false;

            return Mathf.Min(station.Level, HighestLevel) >= recipe.m_minStationLevel;
        }

        /// <summary>Whether anything in this bag can be mended here.</summary>
        internal static bool Anything(Inventory bag, CraftStation station) => First(bag, station) != null;

        /// <summary>The first thing in the bag this station can mend, or null.</summary>
        internal static ItemDrop.ItemData First(Inventory bag, CraftStation station)
        {
            if (bag == null) return null;

            foreach (ItemDrop.ItemData item in bag.GetAllItems())
            {
                if (CanMend(station, item)) return item;
            }

            return null;
        }

        /// <summary>
        ///     Mends one item, and says what it was.
        /// </summary>
        /// <remarks>
        ///     One per call, because a repair is worth watching happen and because a villager
        ///     that silently restored a chest's worth of gear in a frame would be indisponible
        ///     from a villager that did nothing. Nothing is consumed - that is vanilla's rule,
        ///     not a simplification.
        /// </remarks>
        internal static bool Mend(Inventory bag, CraftStation station, out string mended)
        {
            mended = string.Empty;

            ItemDrop.ItemData item = First(bag, station);
            if (item == null) return false;

            item.m_durability = item.GetMaxDurability();
            mended = item.m_shared.m_name;

            // Told, because the inventory is on a networked container and nothing else here
            // knows the item changed - the same reason every other write to a bag says so.
            bag.Changed();
            return true;
        }

        /// <summary>
        ///     A station that mends, and a chest with something for it, both inside a work area.
        /// </summary>
        /// <remarks>
        ///     Only looked for when there is nothing to make. Crafting is what a player asked
        ///     for by writing an order; repairing is what a station offers to do with its spare
        ///     time, and a settlement that mended axes while a standing order went unmade would
        ///     be answering a question nobody had asked.
        /// </remarks>
        internal static bool Find(Colony colony, List<WorkArea> areas, Villagers.Villager asker,
            out StructureRecord station, out StructureRecord chest, out string item)
        {
            station = null;
            chest = null;
            item = string.Empty;
            if (colony == null || asker == null) return false;

            foreach (WorkArea area in areas)
            {
                foreach (StructureRecord record in colony.State.GetStructures())
                {
                    if ((record.Capabilities & StructureCapability.Crafting) == 0) continue;
                    if (!record.Settings.Repairs || !record.WorkableIn(colony)) continue;
                    if (Unreachable.Refuses(asker.Id, record.Id)) continue;
                    if (TargetClaims.IsClaimedByOther(record.Id, asker)) continue;

                    ZDO zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(record.Id) : null;
                    if (zdo == null || !zdo.IsValid() || !area.Contains(zdo.GetPosition())) continue;

                    GameObject instance = ZNetScene.instance != null
                        ? ZNetScene.instance.FindInstance(record.Id)
                        : null;

                    // The station has to be loaded to be asked. Its level comes from extensions
                    // standing beside it, and whether it can mend a particular axe depends on
                    // that level - so this is one question the prefab cannot answer from home.
                    if (instance == null || !CraftProbe.TryFind(instance, out CraftStation found)) continue;

                    if (!TryFindWorn(colony, asker, found, area, out chest, out item)) continue;

                    station = record;
                    return true;
                }
            }

            return false;
        }

        /// <summary>A registered chest in this area holding something that station could mend.</summary>
        private static bool TryFindWorn(Colony colony, Villagers.Villager asker, CraftStation station,
            WorkArea area, out StructureRecord chest, out string item)
        {
            chest = null;
            item = string.Empty;

            foreach (StructureRecord record in colony.State.GetStructures())
            {
                if ((record.Capabilities & StructureCapability.Storage) == 0) continue;
                if (!record.Settings.MayTakeFrom || !record.WorkableIn(colony)) continue;
                if (Unreachable.Refuses(asker.Id, record.Id)) continue;

                ZDO zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(record.Id) : null;
                if (zdo == null || !zdo.IsValid() || !area.Contains(zdo.GetPosition())) continue;

                GameObject instance = ZNetScene.instance != null
                    ? ZNetScene.instance.FindInstance(record.Id)
                    : null;

                Container container = instance != null
                    ? instance.GetComponentInChildren<Container>(true)
                    : null;

                Inventory inventory = container != null ? container.GetInventory() : null;
                if (inventory == null) continue;

                foreach (ItemDrop.ItemData held in inventory.GetAllItems())
                {
                    if (!CanMend(station, held)) continue;

                    chest = record;
                    item = Carrying.NameOf(held);
                    if (!string.IsNullOrEmpty(item)) return true;
                }
            }

            return false;
        }
    }
}
