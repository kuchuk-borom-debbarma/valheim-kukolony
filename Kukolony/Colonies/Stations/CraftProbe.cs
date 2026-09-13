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
    }
}
