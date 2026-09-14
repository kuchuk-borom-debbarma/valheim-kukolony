using UnityEngine;

namespace Kukolony.Colonies.Stations
{
    /// <summary>
    ///     Hearths, fire pits and braziers - everything built on <see cref="Fireplace" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>This was deliberately left out, and the reason was good.</b> The registry's own
    ///         notes said a fireplace "that burns for ever or refuses refills still accepts fuel
    ///         and still reports a change, so feeding one destroys the fuel silently". Both
    ///         objections turn out to be answerable from the prefab - <c>m_infiniteFuel</c> and
    ///         <c>m_canRefill</c> are public bools - so they are refused before a villager picks
    ///         up a log rather than discovered after it has burnt one. The third half, that the
    ///         call reports nothing useful, is what the base class's before-and-after reading
    ///         already handles for every station here.
    ///     </para>
    ///     <para>
    ///         <b>The simplest protocol of the four.</b> Fuel goes in and nothing comes out: a
    ///         fire is not a conversion, it is a thing that must not go out. So there is no
    ///         output, no conversion list, and the only question is how full to keep it.
    ///     </para>
    ///     <para>
    ///         <b>And a fire that was switched off stays off.</b> Being out of fuel and being
    ///         turned off are different states on the record, and a settlement that relit every
    ///         hearth somebody had deliberately put out would be a mod arguing with its player.
    ///     </para>
    /// </remarks>
    internal sealed class FireplaceProtocol : StationProtocol
    {
        private readonly Fireplace _fire;

        internal FireplaceProtocol(ZNetView view, Fireplace fire) : base(view)
        {
            _fire = fire;
        }

        internal override StationKind Kind => StationKind.Fire;

        internal override StationWant WhatItWants(StructureSettings settings, string carrying)
        {
            if (_fire == null || settings == null) return StationWant.Nothing;

            string fuel = Burns();
            if (fuel.Length == 0) return StationWant.Nothing;

            // The player has to have said so. Registering a hearth is not the same as asking for
            // it to be kept lit, and a settlement that fed every fire it knew about would burn a
            // forest without being told to.
            if (!settings.Fuel.Contains(fuel)) return StationWant.Nothing;

            // A villager already holding something asks the narrower question, for the reason the
            // base class records: an answer that moves under a loaded villager is a load carried
            // back and forth for ever.
            if (!string.IsNullOrEmpty(carrying) && carrying != fuel) return StationWant.Nothing;

            if (SwitchedOff()) return StationWant.Nothing;

            int shortfall = Shortfall(settings.KeepFull);
            return shortfall > 0 ? new StationWant(fuel, true, shortfall) : StationWant.Nothing;
        }

        /// <summary>A fire produces nothing. There is never anything to take off one.</summary>
        internal override bool HasOutput() => false;

        internal override bool TakeOutput() => false;

        internal override FeedResult WouldTake(string prefab, bool asFuel)
        {
            if (_fire == null || View == null || !View.IsValid()) return FeedResult.Unavailable;

            // Everything a fire takes is fuel. Material has no meaning here, and saying so is
            // better than quietly treating a haunch of meat as firewood.
            if (!asFuel || Burns() != prefab) return FeedResult.Refused;

            if (SwitchedOff()) return FeedResult.Refused;

            return Shortfall(1f) > 0 ? FeedResult.Fed : FeedResult.Full;
        }

        /// <summary>
        ///     How much fuel is on it, read from the record.
        /// </summary>
        /// <remarks>
        ///     From the ZDO rather than from a field, which is what makes the base class's
        ///     before-and-after reading mean anything: the fuel level is the thing the game
        ///     writes when a log goes in, and a fire burns it down on its own clock meanwhile.
        /// </remarks>
        protected override double Progress(bool asFuel) => Fuel();

        protected override void Submit(string prefab, bool asFuel)
        {
            // The same call a cooking station takes, and the same name. It clamps against
            // m_maxFuel itself and plays the fire's own "fuel added" effect, so unlike some of
            // the game's other doors this one both refuses to overfill and looks right.
            View.InvokeRPC("RPC_AddFuel");
        }

        /// <summary>What this fire burns, or empty when it cannot be fed at all.</summary>
        /// <remarks>
        ///     The two documented objections, refused here rather than discovered by burning a
        ///     log into one. A fire that never runs down does not want fuel, and one that refuses
        ///     refills will take the call and do nothing with it.
        /// </remarks>
        private string Burns()
        {
            if (_fire.m_infiniteFuel || !_fire.m_canRefill) return string.Empty;

            return _fire.m_fuelItem != null ? _fire.m_fuelItem.gameObject.name : string.Empty;
        }

        /// <summary>How many logs short of the level this fire is meant to be kept at.</summary>
        private int Shortfall(float keepFull)
        {
            if (_fire.m_maxFuel <= 0f) return 0;

            float wanted = _fire.m_maxFuel * Mathf.Clamp01(keepFull <= 0f ? 1f : keepFull);
            double shortfall = System.Math.Floor(wanted - Fuel());
            return shortfall <= 0d ? 0 : (int)shortfall;
        }

        private double Fuel()
        {
            ZDO zdo = View != null && View.IsValid() ? View.GetZDO() : null;
            return zdo == null ? 0d : zdo.GetFloat(ZDOVars.s_fuel, 0f);
        }

        /// <summary>
        ///     Whether somebody has deliberately put this fire out.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>Asked of what the fire is doing, not of how it records it.</b> The first
        ///         version compared the state key against zero, which a probe showed is not how
        ///         this works at all: a freshly placed fire has <em>no state written</em> - the
        ///         key reads as whatever default you pass - and one that has been switched off
        ///         reads <c>2</c>. So zero means nothing, and the rule would have silently
        ///         inverted into "feed the fires somebody deliberately put out".
        ///     </para>
        ///     <para>
        ///         Fuel with no flame is the observable answer and needs no encoding: a fire
        ///         holding wood and not burning has been put out by somebody, and one holding
        ///         none has simply run down - which is the thing this job exists to put right.
        ///         Measured on a candle in game: lit read <c>burning=True fuel=3</c>, and the same
        ///         candle after toggling read <c>burning=False fuel=3</c>.
        ///     </para>
        ///     <para>
        ///         A rained-on fire reads the same way and is also left alone, which is right for
        ///         a different reason: it already has the fuel it needs and will light again when
        ///         it dries.
        ///     </para>
        /// </remarks>
        private bool SwitchedOff() => _fire.m_canTurnOff && !_fire.IsBurning() && Fuel() > 0d;
    }
}
