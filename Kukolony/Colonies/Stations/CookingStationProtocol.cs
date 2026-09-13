using UnityEngine;

namespace Kukolony.Colonies.Stations
{
    /// <summary>
    ///     Cooking stations and ovens - everything built on <see cref="CookingStation" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Clearing is the half that matters.</b> A station whose slots are full cannot
    ///         accept anything at all, so taking finished food off is the only move that makes
    ///         progress - and food left on it burns. A supply-only villager will sit in front of
    ///         a full oven for ever, which is why the job's default does both.
    ///     </para>
    ///     <para>
    ///         <b>Burnt food comes off too.</b> It occupies a slot exactly as cooked food does,
    ///         and a station that only ever cleared the edible would fill up with cinders and
    ///         stop. Whether it was worth cooking is not this job's business.
    ///     </para>
    /// </remarks>
    internal sealed class CookingStationProtocol : StationProtocol
    {
        private readonly CookingStation _station;

        internal CookingStationProtocol(ZNetView view, CookingStation station) : base(view)
        {
            _station = station;
        }

        internal override StationKind Kind => StationKind.Cooking;

        internal override StationWant WhatItWants(StructureSettings settings, string carrying)
        {
            if (_station == null || settings == null) return StationWant.Nothing;

            bool free = string.IsNullOrEmpty(carrying);

            // Some of them burn. A stone oven keeps a fire going under the food, and asking it
            // for fuel before asking it for meat is the same order a smelter uses and for the
            // same reason: fuel is what keeps the work it already has moving.
            string fuel = Burns();
            if ((free || carrying == fuel) && fuel.Length > 0 && settings.Fuel.Contains(fuel))
            {
                int logs = Fuelling();
                if (logs > 0) return new StationWant(fuel, true, logs);
            }

            int slots = Slots();
            int wanted = StationAppetite.InputWanted(Used(), slots, settings.KeepFull);
            if (wanted <= 0) return StationWant.Nothing;

            foreach (string input in settings.Input)
            {
                if (string.IsNullOrEmpty(input)) continue;
                if (!free && carrying != input) continue;
                if (Cooks(input)) return new StationWant(input, false, wanted);
            }

            return StationWant.Nothing;
        }

        internal override bool HasOutput()
        {
            if (_station == null) return false;

            for (int slot = 0; slot < Slots(); slot++)
            {
                if (Finished(slot)) return true;
            }

            return false;
        }

        /// <summary>
        ///     Whether the station would give this slot up, asked the way it asks itself.
        /// </summary>
        /// <remarks>
        ///     <c>RPC_RemoveDoneItem</c> walks the slots and takes the first whose
        ///     <c>IsItemDone(name)</c> is true - a test on the item's <em>name</em>, which is
        ///     true for cooked food and for the overcooked item alike. Asking a different
        ///     question here and the handler that one would let this report output the station
        ///     then declines to give up: nothing comes off, the villager re-chooses, and picks
        ///     the same station again for ever.
        /// </remarks>
        private bool Finished(int slot)
        {
            _station.GetSlot(slot, out string item, out float _, out CookingStation.Status _, out bool _);
            return !string.IsNullOrEmpty(item) && _station.IsItemDone(item);
        }

        internal override bool TakeOutput()
        {
            if (_station == null || View == null || !View.IsValid()) return false;

            if (!View.IsOwner())
            {
                View.ClaimOwnership();
                return false;
            }

            int before = Used();

            // RPC_RemoveDoneItem(userPoint, amount): it finds the first finished slot, spawns
            // that many copies at the point given, and clears the slot. Vanilla passes the
            // player's position because that is where a player wants it; this passes the
            // station's own, so a piece of meat never lands inside a wall the villager happens
            // to be standing against. One, because a villager has no cooking skill to earn the
            // bonus yield the player's path can roll for.
            View.InvokeRPC("RPC_RemoveDoneItem", View.transform.position + Vector3.up, 1);

            // Whether it actually came off is read back rather than assumed, for the same reason
            // feeding is: the call answers nothing at all.
            return Used() < before;
        }

        internal override FeedResult WouldTake(string prefab, bool asFuel)
        {
            if (_station == null) return FeedResult.Unavailable;
            if (asFuel)
            {
                if (Burns() != prefab) return FeedResult.Refused;
                return Fuelling() > 0 ? FeedResult.Fed : FeedResult.Full;
            }

            if (!Cooks(prefab)) return FeedResult.Refused;

            return _station.GetFreeSlot() < 0 ? FeedResult.Full : FeedResult.Fed;
        }

        protected override double Progress(bool asFuel) => asFuel ? _station.GetFuel() : Used();

        protected override void Submit(string prefab, bool asFuel)
        {
            if (asFuel) View.InvokeRPC("RPC_AddFuel");
            else View.InvokeRPC("RPC_AddItem", prefab, false);
        }

        /// <summary>What this station burns, or empty if it burns nothing.</summary>
        private string Burns() =>
            _station.m_useFuel && _station.m_fuelItem != null
                ? _station.m_fuelItem.gameObject.name
                : string.Empty;

        /// <summary>
        ///     How much fuel it is short of.
        /// </summary>
        /// <remarks>
        ///     The same rule a smelter follows, in the terms an oven has: one that does not burn
        ///     while empty has nothing to burn for until there is food on it, so it is not lit -
        ///     and one that burns regardless is kept going, because that is what it does whether
        ///     anybody feeds it or not.
        /// </remarks>
        private int Fuelling()
        {
            if (!_station.m_useFuel || _station.m_maxFuel <= 0) return 0;
            if (!_station.m_useFueldWhileEmpty && Used() <= 0) return 0;

            double shortfall = System.Math.Floor(_station.m_maxFuel - _station.GetFuel());
            return shortfall <= 0d ? 0 : (int)shortfall;
        }

        private int Slots() => _station.m_slots != null ? _station.m_slots.Length : 0;

        /// <summary>How many slots are occupied, finished or not.</summary>
        private int Used()
        {
            int used = 0;
            for (int slot = 0; slot < Slots(); slot++)
            {
                _station.GetSlot(slot, out string item, out float _, out CookingStation.Status _, out bool _);
                if (!string.IsNullOrEmpty(item)) used++;
            }

            return used;
        }

        /// <summary>Whether this station has a conversion for an item, by name.</summary>
        /// <remarks>
        ///     Asked of the station's own conversion list rather than of a name we wrote down,
        ///     so a modded oven answers for its own recipes.
        /// </remarks>
        private bool Cooks(string prefab)
        {
            if (_station.m_conversion == null) return false;

            foreach (CookingStation.ItemConversion conversion in _station.m_conversion)
            {
                if (conversion == null || conversion.m_from == null) continue;
                if (conversion.m_from.gameObject.name == prefab) return true;
            }

            return false;
        }
    }
}
