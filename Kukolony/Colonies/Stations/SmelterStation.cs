using UnityEngine;

namespace Kukolony.Colonies.Stations
{
    /// <summary>
    ///     Furnaces, charcoal kilns, blast furnaces, windmills, spinning wheels - everything built
    ///     on <see cref="Smelter" />.
    /// </summary>
    /// <remarks>
    ///     The simplest of the three, and the only one with nothing to collect: a smelter spawns
    ///     what it made on the ground at its own output point, where hauling already picks things
    ///     up. Tending never learns to carry a product, which is the same division chopping uses.
    /// </remarks>
    internal sealed class SmelterStation : StationProtocol
    {
        private readonly Smelter _smelter;

        internal SmelterStation(ZNetView view, Smelter smelter) : base(view)
        {
            _smelter = smelter;
        }

        internal override StationKind Kind => StationKind.Smelter;

        internal override StationWant WhatItWants(StructureSettings settings, string carrying)
        {
            if (_smelter == null || settings == null) return StationWant.Nothing;

            bool free = string.IsNullOrEmpty(carrying);

            // Fuel before material, and it is safe by construction rather than by policy:
            // FuelWanted answers zero whenever nothing is queued, so a fuel-first villager
            // cannot stoke an idle station - and when it is running, fuel is what keeps it so.
            string fuel = _smelter.m_fuelItem != null ? _smelter.m_fuelItem.gameObject.name : string.Empty;
            if ((free || carrying == fuel) && fuel.Length > 0 && settings.Fuel.Contains(fuel))
            {
                int wanted = StationAppetite.FuelWanted(_smelter.GetQueueSize(), _smelter.m_fuelPerProduct,
                    _smelter.m_maxFuel, _smelter.GetFuel());
                if (wanted > 0) return new StationWant(fuel, true, wanted);
            }

            int room = StationAppetite.InputWanted(_smelter.GetQueueSize(), _smelter.m_maxOre,
                settings.KeepFull);
            if (room <= 0) return StationWant.Nothing;

            foreach (string input in settings.Input)
            {
                if (string.IsNullOrEmpty(input)) continue;
                if (!free && carrying != input) continue;

                // Asked of the station rather than assumed from the record: the settings were
                // chosen from the prefab's conversion list, and a record outlives the asset data
                // it was made from.
                if (_smelter.IsItemAllowed(input)) return new StationWant(input, false, room);
            }

            return StationWant.Nothing;
        }

        /// <summary>Nothing to take off: the product is spawned on the ground.</summary>
        internal override bool HasOutput() => false;

        internal override bool TakeOutput() => false;

        internal override FeedResult WouldTake(string prefab, bool asFuel)
        {
            if (_smelter == null) return FeedResult.Unavailable;

            if (asFuel)
            {
                if (_smelter.m_fuelItem == null) return FeedResult.Refused;

                // The station's own guard, matched rather than guessed: vanilla refuses a log
                // once the fuel is within one of the maximum.
                return _smelter.GetFuel() > _smelter.m_maxFuel - 1 ? FeedResult.Full : FeedResult.Fed;
            }

            if (!_smelter.IsItemAllowed(prefab)) return FeedResult.Refused;
            return _smelter.GetQueueSize() >= _smelter.m_maxOre ? FeedResult.Full : FeedResult.Fed;
        }

        protected override double Progress(bool asFuel) =>
            asFuel ? _smelter.GetFuel() : _smelter.GetQueueSize();

        protected override void Submit(string prefab, bool asFuel)
        {
            // Probe-verified argument shapes, recorded in docs/valheim-findings.md. The names
            // carry the RPC_ prefix - the method names in the decompiled source do not, and
            // calling those does nothing at all.
            if (asFuel) View.InvokeRPC("RPC_AddFuel");
            else View.InvokeRPC("RPC_AddOre", prefab, false);
        }
    }
}
