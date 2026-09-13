using UnityEngine;

namespace Kukolony.Colonies.Stations
{
    /// <summary>
    ///     A crafting station, and everything a villager needs to know to work at one.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Unlike processing, crafting needs no protocol. There is nothing to hand a station
    ///         and no call to make: the game has no component that crafts and no server-side path
    ///         for it. <c>InventoryGui.DoCrafting</c> is the only implementation and it is welded
    ///         to the local player - skills, DLC checks, the upgrade dialog, <c>Player.m_localPlayer</c>
    ///         throughout. So the craft itself is ours, and this type is only the part the station
    ///         can answer: may it be used, what can be made here, and where to stand.
    ///     </para>
    ///     <para>
    ///         <b>Why the checks are copied rather than called.</b> <c>CraftingStation.CheckUsable</c>
    ///         asks exactly these two questions, but it takes a <c>Player</c> and dereferences it
    ///         for <c>NoCostCheat()</c> and <c>Message</c>. A villager has neither, so the rules
    ///         are reproduced here - and being reproduced, they are written out with the vanilla
    ///         source beside them so a Valheim update that changes one is visible.
    ///     </para>
    /// </remarks>
    internal sealed class CraftStation
    {
        /// <summary>
        ///     How much of the sky a roof must block. Vanilla's number, from CheckUsable.
        /// </summary>
        private const float EnoughCover = 0.7f;

        private readonly ZNetView _view;

        internal CraftStation(ZNetView view, CraftingStation station)
        {
            _view = view;
            Station = station;
        }

        internal CraftingStation Station { get; }

        /// <summary>
        ///     The name recipes match on - "$piece_workbench", "$piece_forge".
        /// </summary>
        /// <remarks>
        ///     A recipe names the station it needs by this string rather than by prefab, which is
        ///     what lets a modded station that calls itself a forge run forge recipes, and what
        ///     lets us answer "what can be made here" from the prefab with nothing loaded.
        /// </remarks>
        internal string StationName => Station != null ? Station.m_name : string.Empty;

        /// <summary>1, plus one for each attached extension.</summary>
        /// <remarks>
        ///     Instance data, not prefab data: the adze beside a workbench is a separate object
        ///     that may or may not be loaded. Which is why the order screen can list what a
        ///     station could make from home, but only the villager standing there can say
        ///     whether the level is high enough.
        /// </remarks>
        internal int Level => Station != null ? Station.GetLevel() : 0;

        internal float UseDistance => Station != null ? Station.m_useDistance : 2f;

        /// <summary>Which crafting animation the rig should play here.</summary>
        internal int UseAnimation => Station != null ? Station.m_useAnimation : 0;

        internal Vector3 Position => Station != null ? Station.transform.position : Vector3.zero;

        internal bool IsValid => Station != null && _view != null && _view.IsValid();

        /// <summary>
        ///     Whether work can happen here right now, and if not, what to say about it.
        /// </summary>
        /// <remarks>
        ///     Both conditions are about the station's surroundings rather than the station, so
        ///     both can change without anything being written: a roof burns down, a fire goes
        ///     out. Asked each time rather than cached for that reason.
        /// </remarks>
        internal bool Usable(out string why)
        {
            why = string.Empty;
            if (!IsValid)
            {
                why = "it is not there";
                return false;
            }

            if (Station.m_craftRequireRoof)
            {
                // The station's own check point where it has one - a forge's is above the
                // anvil, not above its origin - and its origin where it does not, which a
                // modded station may well not.
                Vector3 point = Station.m_roofCheckPoint != null
                    ? Station.m_roofCheckPoint.position
                    : Station.transform.position;

                Cover.GetCoverForPoint(point, out float cover, out bool underRoof);
                if (!underRoof)
                {
                    why = "it needs a roof";
                    return false;
                }

                if (cover < EnoughCover)
                {
                    why = "it is too exposed";
                    return false;
                }
            }

            if (Station.m_craftRequireFire && !HasFire())
            {
                why = "it needs a fire";
                return false;
            }

            return true;
        }

        /// <summary>Runs the station's own in-use visual, as a player standing at it would.</summary>
        internal void PokeInUse()
        {
            if (IsValid) Station.PokeInUse();
        }

        /// <summary>
        ///     Whether a fire is burning close enough.
        /// </summary>
        /// <remarks>
        ///     Computed rather than read off <c>m_haveFire</c>, which holds the same answer. The
        ///     field is refreshed by <c>InvokeRepeating("CheckFire", 1f, 1f)</c>, so it is up to
        ///     a second stale and it is only started when the station was built with
        ///     <c>m_craftRequireFire</c> already set - both fine for a hover text and neither
        ///     worth relying on for a decision a villager acts on. This is the same call that
        ///     field is filled from.
        /// </remarks>
        private bool HasFire() =>
            EffectArea.IsPointPlus025InsideBurningArea(Station.transform.position);
    }
}
