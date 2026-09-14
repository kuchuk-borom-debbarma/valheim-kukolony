using UnityEngine;

namespace Kukolony.Colonies.Stations
{
    /// <summary>
    ///     The only definition of "crafting happens here" in this mod.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         One predicate, asked by every surface - what may be registered, what the nearby
    ///         list offers, what the order screen configures, what a villager walks to - for the
    ///         reason <see cref="StationProbe" /> gives: two tests drift apart, and the symptom is
    ///         a station a player can register that no villager will ever use. See
    ///         docs/components.md.
    ///     </para>
    ///     <para>
    ///         <b>Simpler than processing, and that is the whole finding.</b> Tending needed three
    ///         protocols because <c>Smelter</c>, <c>CookingStation</c> and <c>Fermenter</c> share
    ///         nothing but <c>Interactable</c>, which doors and beds carry too. Crafting needs one
    ///         component: every crafting station in the game carries <c>CraftingStation</c> -
    ///         workbench, forge, stonecutter, artisan table, galdr table, black forge, and the
    ///         cauldron, which is why food recipes arrive here without being asked for.
    ///     </para>
    ///     <para>
    ///         Because it is a component test and not a list of names, a station from a content
    ///         pack or another mod is registerable the day it is installed.
    ///     </para>
    /// </remarks>
    internal static class CraftProbe
    {
        /// <summary>Whether this object is a crafting station, and how to work at it.</summary>
        internal static bool TryFind(GameObject candidate, out CraftStation station)
        {
            station = null;
            if (candidate == null) return false;

            // A creature and a loose item are never structures, whatever they carry.
            if (candidate.GetComponent<Character>() != null) return false;
            if (candidate.GetComponent<ItemDrop>() != null) return false;

            if (!candidate.TryGetComponent(out ZNetView view) || !view.IsValid()) return false;

            // Children count, because Valheim splits an object's parts across child transforms -
            // but only a child of the same ZNetView, or a longhouse would inherit the
            // capabilities of every station standing inside it.
            foreach (CraftingStation found in candidate.GetComponentsInChildren<CraftingStation>(true))
            {
                if (found == null) continue;
                if (found.GetComponentInParent<ZNetView>() != view) continue;

                station = new CraftStation(view, found);
                return true;
            }

            return false;
        }

        /// <summary>Whether this object is a crafting station at all, without building an adapter.</summary>
        internal static bool Is(GameObject candidate) => TryFind(candidate, out CraftStation _);

        /// <summary>
        ///     The station name a prefab carries, or empty.
        /// </summary>
        /// <remarks>
        ///     The prefab question rather than the instance one, and it has to be separate:
        ///     <see cref="TryFind" /> requires a valid ZNetView and a prefab has none, so it
        ///     would refuse every prefab ever handed to it. Here rather than in the catalogue
        ///     that needs it, so the component test lives beside the other one and the two
        ///     cannot quietly come to disagree about what a crafting station is.
        /// </remarks>
        internal static string NameOfPrefab(GameObject prefab) =>
            Of(prefab) is CraftingStation station ? station.m_name : string.Empty;

        /// <summary>
        ///     Whether this kind of station also offers the recipes that need no station.
        /// </summary>
        /// <remarks>
        ///     Vanilla's own rule: <c>Player.RequiredCraftingStation</c> accepts a recipe naming
        ///     no station unless the station being used says otherwise. It is what makes a stone
        ///     axe appear at a workbench.
        /// </remarks>
        internal static bool ShowsBasic(GameObject prefab) =>
            Of(prefab) is CraftingStation station && station.m_showBasicRecipies;

        private static CraftingStation Of(GameObject prefab) =>
            prefab != null ? prefab.GetComponentInChildren<CraftingStation>(true) : null;
    }
}
