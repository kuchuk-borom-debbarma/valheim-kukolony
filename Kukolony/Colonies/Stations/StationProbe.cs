using UnityEngine;

namespace Kukolony.Colonies.Stations
{
    /// <summary>
    ///     The only definition of "a station" in this mod.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         One predicate, asked by every surface: what may be registered, what the nearby list
    ///         offers, what a job's picker shows, and what a villager walks to. Two surfaces with
    ///         two tests drift apart, and the symptom is a structure a player can register that no
    ///         job will ever touch - or the reverse. See docs/components.md.
    ///     </para>
    ///     <para>
    ///         <b>By component, never by name</b>, so a modded kiln or an oven from a content pack
    ///         works the day it is installed, with nothing here to change.
    ///     </para>
    ///     <para>
    ///         <b>Order matters, because objects carry several of these.</b> An oven is a cooking
    ///         station, a windmill is a smelter, a hearth is a fireplace, and a fuelled cooking
    ///         station is both - so the more specific protocol is asked first. Fireplace is not
    ///         probed at all: one that burns for ever or refuses refills still accepts fuel and
    ///         still reports a change, so feeding one destroys the fuel silently, and that needs
    ///         its own protocol rather than a place in this list.
    ///     </para>
    /// </remarks>
    internal static class StationProbe
    {
        /// <summary>Whether this object is a station, and how to operate it.</summary>
        internal static bool TryFind(GameObject candidate, out StationProtocol protocol)
        {
            protocol = null;
            if (candidate == null) return false;

            // A creature and a loose item are never structures, whatever they carry.
            if (candidate.GetComponent<Character>() != null) return false;
            if (candidate.GetComponent<ItemDrop>() != null) return false;

            if (!candidate.TryGetComponent(out ZNetView view) || !view.IsValid()) return false;

            // Most specific first. An oven is a cooking station and a fireplace; a fuelled
            // cooking station is both; a windmill is a smelter. Asking in this order is what
            // decides which of an object's several components speaks for it.
            if (Own(candidate, view, out CookingStation cooking))
            {
                protocol = new CookingStationProtocol(view, cooking);
                return true;
            }

            if (Own(candidate, view, out Fermenter fermenter))
            {
                protocol = new FermenterProtocol(view, fermenter);
                return true;
            }

            if (Own(candidate, view, out Smelter smelter))
            {
                protocol = new SmelterStation(view, smelter);
                return true;
            }

            // Last, and it has to be. An oven is a cooking station *and* a fireplace, and a
            // fuelled cooking station is both - so anything that also cooks or smelts has already
            // been claimed above by the component that actually does the work. What falls through
            // to here is a thing whose only trade is burning: a hearth, a fire pit, a brazier.
            //
            // Getting this order wrong would not fail loudly. Every oven in the world would
            // become a fire pit that takes wood and cooks nothing, and the settlement would look
            // busy the entire time.
            if (Own(candidate, view, out Fireplace fire))
            {
                protocol = new FireplaceProtocol(view, fire);
                return true;
            }

            return false;
        }

        /// <summary>Whether this object carries a station component at all, without building one.</summary>
        /// <remarks>
        ///     For registration, which asks the question of something it is not about to operate.
        ///     Kept beside <see cref="TryFind" /> so the two cannot come to disagree about what a
        ///     station is.
        /// </remarks>
        internal static bool Is(GameObject candidate) => TryFind(candidate, out StationProtocol _);

        /// <summary>
        ///     A component belonging to this networked object, wherever it sits on the hierarchy.
        /// </summary>
        /// <remarks>
        ///     Children count - Valheim routinely splits an object's parts across child
        ///     transforms - but only a child of the <em>same</em> ZNetView, or a longhouse
        ///     inherits the capabilities of everything standing inside it.
        /// </remarks>
        private static bool Own<T>(GameObject candidate, ZNetView view, out T found) where T : Component
        {
            found = null;
            foreach (T component in candidate.GetComponentsInChildren<T>(true))
            {
                if (component == null) continue;
                if (component.GetComponentInParent<ZNetView>() != view) continue;

                found = component;
                return true;
            }

            return false;
        }
    }
}
