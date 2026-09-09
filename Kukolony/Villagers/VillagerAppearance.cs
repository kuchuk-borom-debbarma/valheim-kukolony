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
        // Vanilla hair and beard prefab names. "" means bald / clean-shaven, which the
        // game accepts and which keeps the population varied.
        private static readonly string[] Hair =
        {
            "Hair1", "Hair2", "Hair3", "Hair4", "Hair5", "Hair6",
            "Hair7", "Hair8", "Hair9", "Hair10", "Hair11", "Hair12", "HairNone"
        };

        private static readonly string[] Beards =
        {
            "Beard1", "Beard2", "Beard3", "Beard4", "Beard5",
            "Beard6", "Beard7", "Beard8", "Beard9", "Beard10", "BeardNone"
        };

        // Simple working clothes. Deliberately low tier - a villager should not turn up
        // in wolf armour on day one.
        private static readonly string[] Chests =
        {
            "ArmorRagsChest", "ArmorLeatherChest", "ArmorTrollLeatherChest"
        };

        private static readonly string[] Legs =
        {
            "ArmorRagsLegs", "ArmorLeatherLegs", "ArmorTrollLeatherLegs"
        };

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
            vis.SetHairItem(Pick(Hair).GetStableHashCode());
            vis.SetBeardItem(vis.GetModelIndex() == 0 ? Pick(Beards).GetStableHashCode() : 0);

            vis.SetChestItem(Pick(Chests).GetStableHashCode());
            vis.SetLegItem(Pick(Legs).GetStableHashCode());

            Log.Debug($"Rolled villager appearance: model={vis.GetModelIndex()}");
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
