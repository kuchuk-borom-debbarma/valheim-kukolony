using UnityEngine;

namespace Kukolony.Villagers
{
    /// <summary>Every slot a villager can be dressed in, in the order a screen shows them.</summary>
    /// <remarks>
    ///     Persisted by index, so append rather than reorder.
    /// </remarks>
    internal enum WearSlot
    {
        Helmet = 0,
        Chest = 1,
        Legs = 2,
        Shoulder = 3,
        Utility = 4,
        RightHand = 5,
        LeftHand = 6
    }

    /// <summary>
    ///     Puts on what a villager's bag actually holds.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>The item never enters the creature's own inventory.</b> That is a safety choice
    ///         rather than a shortcut: the routine that equips a creature's best weapon runs on
    ///         load through a path this mod does not suppress, and would quietly strip anything
    ///         placed there. The truth is the villager's persisted bag; the visible slot mirrors
    ///         it, and a mirror can be rebuilt.
    ///     </para>
    ///     <para>
    ///         Almost none of the work is ours. Every visible slot is ZDO-backed and resolves an
    ///         item from its prefab hash, so what a villager wears replicates to other players
    ///         and survives a reload with nothing of ours running. All this does is decide, on
    ///         the owner, what those slots should say.
    ///     </para>
    /// </remarks>
    internal static class VillagerWardrobe
    {
        internal const int SlotCount = 7;

        /// <summary>
        ///     Writes a slot from an item the villager owns, or bares it when the item is null.
        /// </summary>
        /// <remarks>
        ///     Quality and variant come from the villager's own copy rather than a constant:
        ///     they choose which model is drawn, so an upgraded chestpiece should not look like
        ///     a fresh one. Two of these setters take arguments the decompiled reference does
        ///     not show, which is why the shapes are read from the shipped assembly.
        /// </remarks>
        internal static void Set(VisEquipment vis, WearSlot slot, ItemDrop.ItemData item)
        {
            if (vis == null) return;

            int hash = item == null || item.m_dropPrefab == null
                ? 0
                : Utils.GetPrefabName(item.m_dropPrefab).GetStableHashCode();
            int quality = item == null ? 1 : Mathf.Max(1, item.m_quality);
            int variant = item == null ? 0 : item.m_variant;

            switch (slot)
            {
                case WearSlot.Helmet: vis.SetHelmetItem(hash); break;
                case WearSlot.Chest: vis.SetChestItem(hash); break;
                case WearSlot.Legs: vis.SetLegItem(hash); break;
                case WearSlot.Shoulder: vis.SetShoulderItem(hash, quality, variant); break;
                case WearSlot.Utility: vis.SetUtilityItem(hash); break;
                case WearSlot.RightHand: vis.SetRightItem(hash, quality); break;
                case WearSlot.LeftHand: vis.SetLeftItem(hash, quality, variant); break;
            }
        }

        /// <summary>What is currently shown in a slot, as a prefab hash. Zero means bare.</summary>
        internal static int Worn(ZDO zdo, WearSlot slot)
        {
            if (zdo == null) return 0;
            switch (slot)
            {
                case WearSlot.Helmet: return zdo.GetInt(ZDOVars.s_helmetItem, 0);
                case WearSlot.Chest: return zdo.GetInt(ZDOVars.s_chestItem, 0);
                case WearSlot.Legs: return zdo.GetInt(ZDOVars.s_legItem, 0);
                case WearSlot.Shoulder: return zdo.GetInt(ZDOVars.s_shoulderItem, 0);
                case WearSlot.Utility: return zdo.GetInt(ZDOVars.s_utilityItem, 0);
                case WearSlot.RightHand: return zdo.GetInt(ZDOVars.s_rightItem, 0);
                case WearSlot.LeftHand: return zdo.GetInt(ZDOVars.s_leftItem, 0);
                default: return 0;
            }
        }

        /// <summary>
        ///     What a worn hash is called.
        /// </summary>
        /// <remarks>
        ///     Resolved from the game's item table rather than from the villager's bag. A
        ///     villager's rolled clothes are written straight to the visible slots and were
        ///     never bag items, so asking the bag what it is wearing answered "something they
        ///     no longer have" for every villager who had simply been born dressed.
        /// </remarks>
        internal static string NameOf(int hash)
        {
            if (hash == 0) return "nothing";
            if (ObjectDB.instance == null) return "something";

            foreach (GameObject prefab in ObjectDB.instance.m_items)
            {
                if (prefab == null || prefab.name.GetStableHashCode() != hash) continue;
                if (!prefab.TryGetComponent(out ItemDrop drop) || drop.m_itemData?.m_shared == null)
                    return prefab.name;

                return Localization.instance != null
                    ? Localization.instance.Localize(drop.m_itemData.m_shared.m_name)
                    : prefab.name;
            }

            return "something unknown";
        }

        /// <summary>
        ///     What a screen should offer for a slot.
        /// </summary>
        /// <remarks>
        ///     Hands return <see cref="ItemDrop.ItemData.ItemType.None" /> meaning "no filter",
        ///     because tools and weapons are filed under several types and a list that filtered
        ///     by one would quietly omit the axe.
        /// </remarks>
        internal static ItemDrop.ItemData.ItemType Accepts(WearSlot slot)
        {
            switch (slot)
            {
                case WearSlot.Helmet: return ItemDrop.ItemData.ItemType.Helmet;
                case WearSlot.Chest: return ItemDrop.ItemData.ItemType.Chest;
                case WearSlot.Legs: return ItemDrop.ItemData.ItemType.Legs;
                case WearSlot.Shoulder: return ItemDrop.ItemData.ItemType.Shoulder;
                case WearSlot.Utility: return ItemDrop.ItemData.ItemType.Utility;
                default: return ItemDrop.ItemData.ItemType.None;
            }
        }

        internal static string Label(WearSlot slot)
        {
            switch (slot)
            {
                case WearSlot.Helmet: return "Head";
                case WearSlot.Chest: return "Chest";
                case WearSlot.Legs: return "Legs";
                case WearSlot.Shoulder: return "Cape";
                case WearSlot.Utility: return "Trinket";
                case WearSlot.RightHand: return "Right hand";
                case WearSlot.LeftHand: return "Left hand";
                default: return string.Empty;
            }
        }

        /// <summary>Whether an item can go in a slot at all.</summary>
        internal static bool Fits(ItemDrop.ItemData item, WearSlot slot)
        {
            if (item?.m_shared == null) return false;

            ItemDrop.ItemData.ItemType accepts = Accepts(slot);
            if (accepts != ItemDrop.ItemData.ItemType.None) return item.m_shared.m_itemType == accepts;

            // Hands: anything the player could hold.
            switch (item.m_shared.m_itemType)
            {
                case ItemDrop.ItemData.ItemType.OneHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
                case ItemDrop.ItemData.ItemType.Bow:
                case ItemDrop.ItemData.ItemType.Shield:
                case ItemDrop.ItemData.ItemType.Tool:
                case ItemDrop.ItemData.ItemType.Torch:
                    return true;
                default:
                    return false;
            }
        }
    }
}
