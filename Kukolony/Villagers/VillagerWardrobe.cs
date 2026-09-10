using System.Collections.Generic;
using UnityEngine;

namespace Kukolony.Villagers
{
    /// <summary>
    ///     Puts on what a villager's outfit asks for and its bag actually holds.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>The item never enters the creature's own inventory.</b> That is a safety
    ///         choice rather than a shortcut: the routine that equips a creature's best weapon
    ///         runs on load through a path this mod does not suppress, and would quietly strip
    ///         a tool placed there. The truth is the villager's persisted bag; the visible slot
    ///         is a mirror of it, and mirrors can be rebuilt.
    ///     </para>
    ///     <para>
    ///         Almost none of the work is ours. Every visible slot is ZDO-backed and resolves an
    ///         item from its prefab hash, so what a villager wears replicates to other players
    ///         and survives a reload without anything of ours running. All this does is decide,
    ///         on the owner, what those slots should say.
    ///     </para>
    /// </remarks>
    internal static class VillagerWardrobe
    {
        /// <summary>
        ///     Writes every visible slot from the outfit, wearing only what the bag holds and
        ///     baring the rest. Caller must own the ZDO.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>Only slots the outfit names are touched.</b> A villager already rolls
        ///         clothes when it is born, and an outfit that managed every slot would strip
        ///         those the moment it was assigned - so naming a slot is what hands it over,
        ///         and an outfit that names nothing changes nothing.
        ///     </para>
        ///     <para>
        ///         A named slot whose item the villager does not own is bared. That is the
        ///         visible signal that the outfit is not yet satisfied, and it is what stops a
        ///         villager showing an axe it no longer has.
        ///     </para>
        ///     <para>
        ///         Safe to call every tick and cheap to do so: each setter writes a ZDO field
        ///         that already holds the same value, which changes nothing. Making this a
        ///         mirror of the bag rather than a step some job must remember to run is what
        ///         keeps the two from drifting apart.
        ///     </para>
        /// </remarks>
        internal static void Wear(VisEquipment vis, Inventory bag, Outfit outfit)
        {
            if (vis == null || outfit == null) return;
            for (int slot = 0; slot < Outfit.SlotCount; slot++)
            {
                if (outfit.Ignores((OutfitSlot)slot)) continue;
                Set(vis, (OutfitSlot)slot, Preferred(bag, outfit.Choices[slot]));
            }
        }

        /// <summary>
        ///     What an equip job should go and fetch: for each slot the outfit manages and the
        ///     villager cannot yet fill, everything that would fill it.
        /// </summary>
        /// <remarks>
        ///     A slot already satisfied asks for nothing, even when a more preferred item in it
        ///     is missing - otherwise a villager wearing leather would spend forever walking to
        ///     containers looking for troll leather nobody has crafted.
        /// </remarks>
        internal static List<string> Missing(Inventory bag, Outfit outfit)
        {
            List<string> missing = new List<string>();
            if (outfit == null) return missing;
            for (int slot = 0; slot < Outfit.SlotCount; slot++)
            {
                List<string> choices = outfit.Choices[slot];
                if (choices.Count == 0 || Preferred(bag, choices) != null) continue;
                foreach (string item in choices)
                    if (!missing.Contains(item)) missing.Add(item);
            }
            return missing;
        }

        /// <summary>
        ///     The first item in this slot's preference order that the villager actually owns,
        ///     or null. Order is the whole point: put the better armour first and everybody
        ///     upgrades as soon as one exists.
        /// </summary>
        private static ItemDrop.ItemData Preferred(Inventory bag, List<string> choices)
        {
            foreach (string item in choices)
            {
                ItemDrop.ItemData found = Find(bag, item);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>The bag's copy of this item, or null when it has none.</summary>
        private static ItemDrop.ItemData Find(Inventory bag, string item)
        {
            if (bag == null || string.IsNullOrEmpty(item)) return null;
            foreach (ItemDrop.ItemData entry in bag.GetAllItems())
            {
                if (entry == null || entry.m_dropPrefab == null) continue;
                if (Utils.GetPrefabName(entry.m_dropPrefab) == item) return entry;
            }
            return null;
        }

        /// <summary>
        ///     Points a visible slot at an item, or bares it when there is none. Quality and
        ///     variant come from the villager's own copy rather than a guess: they choose which
        ///     model is shown, and an upgraded chestpiece does not look like a fresh one.
        /// </summary>
        private static void Set(VisEquipment vis, OutfitSlot slot, ItemDrop.ItemData item)
        {
            int hash = item == null || item.m_dropPrefab == null
                ? 0
                : Utils.GetPrefabName(item.m_dropPrefab).GetStableHashCode();
            int quality = item == null ? 1 : Mathf.Max(1, item.m_quality);
            int variant = item == null ? 0 : item.m_variant;
            switch (slot)
            {
                case OutfitSlot.Helmet: vis.SetHelmetItem(hash); break;
                case OutfitSlot.Chest: vis.SetChestItem(hash); break;
                case OutfitSlot.Legs: vis.SetLegItem(hash); break;
                case OutfitSlot.Shoulder: vis.SetShoulderItem(hash, quality, variant); break;
                case OutfitSlot.Utility: vis.SetUtilityItem(hash); break;
                case OutfitSlot.RightHand: vis.SetRightItem(hash, quality); break;
                case OutfitSlot.LeftHand: vis.SetLeftItem(hash, quality, variant); break;
            }
        }
    }
}
