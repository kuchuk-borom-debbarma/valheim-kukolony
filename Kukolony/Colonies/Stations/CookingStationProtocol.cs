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

        internal override StationWant WhatItWants(StructureSettings settings)
        {
            if (_station == null || settings == null) return StationWant.Nothing;

            // A cooking station burns nothing of its own - the ones that do carry a Fireplace as
            // well, which is a protocol this mod does not have yet and deliberately does not
            // guess at.
            int slots = Slots();
            int wanted = StationAppetite.InputWanted(Used(), slots, settings.KeepFull);
            if (wanted <= 0) return StationWant.Nothing;

            foreach (string input in settings.Input)
            {
                if (!string.IsNullOrEmpty(input) && Cooks(input)) return new StationWant(input, false, wanted);
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

        internal override bool TakeOutput()
        {
            if (_station == null || View == null || !View.IsValid()) return false;

            if (!View.IsOwner())
            {
                View.ClaimOwnership();
                return false;
            }

            int before = Used();

            // Probe-verified shape from docs/valheim-findings.md: a position for the food to pop
            // out at, and a count. Taken from the station itself rather than from the villager,
            // so a piece of meat never lands inside a wall the villager happens to be standing
            // against.
            View.InvokeRPC("RPC_RemoveDoneItem", View.transform.position + Vector3.up, 1);

            // Whether it actually came off is read back rather than assumed, for the same reason
            // feeding is: the call answers nothing at all.
            return Used() < before;
        }

        internal override FeedResult WouldTake(string prefab, bool asFuel)
        {
            if (_station == null) return FeedResult.Unavailable;
            if (asFuel) return FeedResult.Refused;
            if (!Cooks(prefab)) return FeedResult.Refused;

            return _station.GetFreeSlot() < 0 ? FeedResult.Full : FeedResult.Fed;
        }

        protected override double Progress(bool asFuel) => Used();

        protected override void Submit(string prefab, bool asFuel) =>
            View.InvokeRPC("RPC_AddItem", prefab, false);

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

        private bool Finished(int slot)
        {
            _station.GetSlot(slot, out string item, out float _, out CookingStation.Status status, out bool _);
            if (string.IsNullOrEmpty(item)) return false;

            return status == CookingStation.Status.Done || status == CookingStation.Status.Burnt;
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
