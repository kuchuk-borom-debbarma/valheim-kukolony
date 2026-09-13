using UnityEngine;

namespace Kukolony.Colonies.Stations
{
    /// <summary>
    ///     Fermenters - everything built on <see cref="Fermenter" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         One batch at a time, and the only one of the three whose add takes a <b>hash</b>
    ///         rather than a name. That difference is the reason this mod has protocols at all
    ///         instead of one station class: the game shares no interface here, and a job that
    ///         guessed a uniform contract would call a method that quietly does nothing.
    ///     </para>
    ///     <para>
    ///         Tapping produces a world drop rather than changing what the fermenter holds, so
    ///         hauling files what falls - the same division a smelter's output already uses.
    ///     </para>
    /// </remarks>
    internal sealed class FermenterProtocol : StationProtocol
    {
        private readonly Fermenter _fermenter;

        internal FermenterProtocol(ZNetView view, Fermenter fermenter) : base(view)
        {
            _fermenter = fermenter;
        }

        internal override StationKind Kind => StationKind.Fermenter;

        internal override StationWant WhatItWants(StructureSettings settings, string carrying)
        {
            if (_fermenter == null || settings == null) return StationWant.Nothing;

            // A fermenter takes one batch and is then busy for days. "How full to keep it" has
            // no meaning here - it is empty or it is not.
            if (_fermenter.GetStatus() != Fermenter.Status.Empty) return StationWant.Nothing;

            foreach (string input in settings.Input)
            {
                if (string.IsNullOrEmpty(input)) continue;
                if (!string.IsNullOrEmpty(carrying) && carrying != input) continue;
                if (Ferments(input)) return new StationWant(input, false, 1);
            }

            return StationWant.Nothing;
        }

        internal override bool HasOutput() =>
            _fermenter != null && _fermenter.GetStatus() == Fermenter.Status.Ready;

        internal override bool TakeOutput()
        {
            if (_fermenter == null || View == null || !View.IsValid()) return false;

            if (!View.IsOwner())
            {
                View.ClaimOwnership();
                return false;
            }

            if (_fermenter.GetStatus() != Fermenter.Status.Ready) return false;

            View.InvokeRPC("RPC_Tap");
            return _fermenter.GetStatus() != Fermenter.Status.Ready;
        }

        internal override FeedResult WouldTake(string prefab, bool asFuel)
        {
            if (_fermenter == null) return FeedResult.Unavailable;
            if (asFuel) return FeedResult.Refused;
            if (!Ferments(prefab)) return FeedResult.Refused;

            return _fermenter.GetStatus() == Fermenter.Status.Empty ? FeedResult.Fed : FeedResult.Full;
        }

        /// <summary>Empty or not, which is the whole of a fermenter's state for this purpose.</summary>
        protected override double Progress(bool asFuel) =>
            _fermenter.GetStatus() == Fermenter.Status.Empty ? 0d : 1d;

        protected override void Submit(string prefab, bool asFuel) =>
            // By hash, unlike the other two. The hash is taken from the name the caller captured
            // rather than from the item, because the base class has already spent it - reading
            // it off the ItemData here would read a thing that is gone.
            View.InvokeRPC("RPC_AddItem", prefab.GetStableHashCode(), false);

        private bool Ferments(string prefab)
        {
            if (_fermenter.m_conversion == null) return false;

            foreach (Fermenter.ItemConversion conversion in _fermenter.m_conversion)
            {
                if (conversion == null || conversion.m_from == null) continue;
                if (conversion.m_from.gameObject.name == prefab) return true;
            }

            return false;
        }
    }
}
