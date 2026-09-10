using System.Collections.Generic;
using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Villagers
{
    /// <summary>
    ///     Gives each villager a distinct face and outfit.
    ///
    ///     Almost none of this is our work. VisEquipment already writes model index, skin
    ///     colour, hair colour and every equipment slot straight to the ZDO, so an
    ///     appearance chosen once persists and replicates by itself. All we do is choose,
    ///     once, on the owner. See docs/npc-design.md.
    /// </summary>
    internal static class VillagerAppearance
    {
        /// <summary>
        ///     Hair and beards, read from the game rather than listed here.
        /// </summary>
        /// <remarks>
        ///     The game files these as customisation items, exactly as the player's own
        ///     appearance screen does, so a villager can be given any style a player could
        ///     choose - including any a another mod adds. The previous hardcoded list of a
        ///     dozen names would silently miss all of them and go stale besides.
        /// </remarks>
        private static readonly List<string> Hair = new List<string>();
        private static readonly List<string> Beards = new List<string>();

        /// <summary>
        ///     How often a villager wears something in a slot that not everyone bothers with.
        ///     A village where everybody has a helmet and a cape reads as a uniform; one where
        ///     nobody does reads as a barracks of the underdressed.
        /// </summary>
        private const float HelmetChance = .35f;
        private const float CapeChance = .3f;
        private const float TrinketChance = .15f;

        private static readonly Dictionary<ItemDrop.ItemData.ItemType, List<string>> Wardrobe =
            new Dictionary<ItemDrop.ItemData.ItemType, List<string>>();

        /// <summary>
        ///     Rolls an appearance and writes it to the ZDO. Caller must own the ZDO.
        /// </summary>
        internal static void Randomise(VisEquipment vis)
        {
            // Model 0 and 1 are the two player body types.
            vis.SetModel(Random.Range(0, 2));

            vis.SetSkinColor(RandomSkinTone());
            vis.SetHairColor(RandomHairTone());

            // 1.0 made these take the stable hash rather than the prefab name. The ZDO
            // always stored a hash - the API just says so now. 0 means "nothing worn",
            // which is what an empty name used to mean.
            EnsureStyles();
            vis.SetHairItem(Hair.Count == 0 ? 0 : Pick(Hair.ToArray()).GetStableHashCode());
            // Beards belong to the first body type only, as they do for players.
            vis.SetBeardItem(vis.GetModelIndex() == 0 && Beards.Count > 0
                ? Pick(Beards.ToArray()).GetStableHashCode() : 0);

            // Everyone is dressed; the rest is chance, so a village looks like people who
            // turned up rather than a set with one costume.
            vis.SetChestItem(PickWorn(ItemDrop.ItemData.ItemType.Chest));
            vis.SetLegItem(PickWorn(ItemDrop.ItemData.ItemType.Legs));
            vis.SetHelmetItem(Random.value < HelmetChance
                ? PickWorn(ItemDrop.ItemData.ItemType.Helmet) : 0);
            vis.SetShoulderItem(Random.value < CapeChance
                ? PickWorn(ItemDrop.ItemData.ItemType.Shoulder) : 0, 1, 0);
            vis.SetUtilityItem(Random.value < TrinketChance
                ? PickWorn(ItemDrop.ItemData.ItemType.Utility) : 0);

            Log.Debug($"Rolled villager appearance: model={vis.GetModelIndex()}");
        }

        /// <summary>
        ///     Everything the player can make, by prefab name.
        /// </summary>
        /// <remarks>
        ///     "Everything wearable" turned out to include armour worn by monsters - villagers
        ///     came back in golem plate and fenring boots, which render with the creature
        ///     shader and are not clothes a person owns. Having a recipe is the game's own
        ///     answer to "could a player have this", and it needs no list of names.
        ///
        ///     It does exclude the few wearables that are found rather than crafted. That is
        ///     the right side to err on: a villager missing a rare cape is not noticeable, and
        ///     a villager in golem armour is.
        /// </remarks>
        private static readonly HashSet<string> Craftable = new HashSet<string>();

        private static void EnsureCraftable()
        {
            if (Craftable.Count > 0 || ObjectDB.instance == null || ObjectDB.instance.m_recipes == null) return;
            foreach (Recipe recipe in ObjectDB.instance.m_recipes)
            {
                if (recipe == null || recipe.m_item == null) continue;
                Craftable.Add(recipe.m_item.gameObject.name);
            }
            Log.Info($"[villager] {Craftable.Count} craftable item(s) known");
        }

        /// <summary>
        ///     Collects every hairstyle and beard the game knows about, once.
        /// </summary>
        private static void EnsureStyles()
        {
            if (Hair.Count > 0 || Beards.Count > 0 || ObjectDB.instance == null) return;
            foreach (GameObject prefab in ObjectDB.instance.m_items)
            {
                if (prefab == null || !prefab.TryGetComponent(out ItemDrop drop)) continue;
                ItemDrop.ItemData.SharedData shared = drop.m_itemData?.m_shared;
                if (shared == null || shared.m_itemType != ItemDrop.ItemData.ItemType.Customization) continue;
                if (prefab.name.StartsWith("Beard", System.StringComparison.OrdinalIgnoreCase)) Beards.Add(prefab.name);
                else if (prefab.name.StartsWith("Hair", System.StringComparison.OrdinalIgnoreCase)) Hair.Add(prefab.name);
            }
            Log.Info($"[villager] {Hair.Count} hairstyle(s) and {Beards.Count} beard(s) to choose from");
        }

        /// <summary>
        ///     A random everyday garment for a slot, as a prefab hash, or zero for none.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Everything the player can wear is fair game, so villagers vary as widely as
        ///         players do. It costs nothing to allow: a visible slot is a picture, not
        ///         protection, so a villager in wolf armour is dressed rather than armoured.
        ///     </para>
        ///     <para>
        ///         Read from what the game actually has rather than from a list written here.
        ///         A hand-written list of prefab names is a list of guesses that ages badly -
        ///         this project has already shipped one naming trees that do not exist - and
        ///         it would miss every garment another mod adds.
        ///     </para>
        /// </remarks>
        private static int PickWorn(ItemDrop.ItemData.ItemType slot)
        {
            List<string> options = Wearable(slot);
            return options.Count == 0 ? 0 : Pick(options.ToArray()).GetStableHashCode();
        }

        private static List<string> Wearable(ItemDrop.ItemData.ItemType slot)
        {
            if (Wardrobe.TryGetValue(slot, out List<string> cached)) return cached;

            List<string> found = new List<string>();
            if (ObjectDB.instance != null)
            {
                EnsureCraftable();
                foreach (GameObject prefab in ObjectDB.instance.m_items)
                {
                    if (prefab == null || !prefab.TryGetComponent(out ItemDrop drop)) continue;
                    ItemDrop.ItemData.SharedData shared = drop.m_itemData?.m_shared;
                    if (shared == null || shared.m_itemType != slot) continue;
                    if (!Craftable.Contains(prefab.name)) continue;
                    found.Add(prefab.name);
                }
            }
            Wardrobe[slot] = found;
            Log.Info($"[villager] {found.Count} wearable {slot} item(s) to choose from");
            return found;
        }

        /// <summary>
        ///     Skin tone as a brightness multiplier. Valheim stores skin as a colour
        ///     vector; keeping the channels equal varies lightness without tinting.
        /// </summary>
        private static Vector3 RandomSkinTone()
        {
            float tone = Random.Range(0.6f, 1.0f);
            return new Vector3(tone, tone, tone);
        }

        private static Vector3 RandomHairTone()
        {
            float tone = Random.Range(0.1f, 1.0f);
            return new Vector3(tone, tone, tone);
        }

        private static string Pick(string[] options) => options[Random.Range(0, options.Length)];
    }
}
