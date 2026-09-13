using UnityEngine;

namespace Kukolony.Colonies.Stations
{
    /// <summary>What kind of station this is, for a setting that says which kinds to work.</summary>
    /// <remarks>Persisted as an int in a job's settings, so append rather than reorder.</remarks>
    internal enum StationKind
    {
        Smelter = 0,
        Cooking = 1,
        Fermenter = 2
    }

    /// <summary>What a station is short of, and whether it burns it or converts it.</summary>
    internal readonly struct StationWant
    {
        internal StationWant(string item, bool asFuel, int amount)
        {
            Item = item ?? string.Empty;
            AsFuel = asFuel;
            Amount = amount;
        }

        internal string Item { get; }

        internal bool AsFuel { get; }

        internal int Amount { get; }

        internal bool Any => Amount > 0 && Item.Length > 0;

        internal static StationWant Nothing => new StationWant(string.Empty, false, 0);
    }

    /// <summary>What came of trying to put something into a station.</summary>
    internal enum FeedResult
    {
        /// <summary>It went in, and the station's own numbers moved to prove it.</summary>
        Fed,

        /// <summary>Ownership is being taken. Ask again next tick.</summary>
        Waiting,

        /// <summary>No room. Nothing was spent.</summary>
        Full,

        /// <summary>The station will not take this. Nothing was spent.</summary>
        Refused,

        /// <summary>The station accepted the call and nothing changed. One item was spent.</summary>
        Swallowed,

        /// <summary>The station cannot be read - destroyed, or not loaded.</summary>
        Unavailable
    }

    /// <summary>
    ///     How to operate one kind of station.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>This exists because the game provides nothing like it.</b> A smelter, an oven and
    ///         a fermenter are unrelated classes with unrelated contracts - ore by name, fermenter
    ///         items by hash, fuel with no arguments at all - and they share only the interfaces a
    ///         door shares. So "a station" is this mod's own predicate: an object carrying a
    ///         component we have a probe-verified protocol for. See docs/components.md.
    ///     </para>
    ///     <para>
    ///         An abstract class rather than concrete code in the job, because three
    ///         implementations exist the day it is written - which is the bar this codebase sets
    ///         for abstracting, and the reason the predecessor's twelve guessed-at classes are in
    ///         the postmortem rather than in the repo.
    ///     </para>
    ///     <para>
    ///         <b>Giving is shared and the pieces are not.</b> Ownership, the capacity check, the
    ///         order of consuming and calling, and the proof that the call landed are identical
    ///         for every station and each one is a way to lose material silently - so they live
    ///         here once, and a protocol supplies only what it alone knows.
    ///     </para>
    /// </remarks>
    internal abstract class StationProtocol
    {
        protected StationProtocol(ZNetView view)
        {
            View = view;
        }

        internal ZNetView View { get; }

        internal abstract StationKind Kind { get; }

        /// <summary>
        ///     What this station is short of right now, given what the player configured.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Loaded-only by nature: capacity is asset data but how full it is is not, and a
        ///         prefab fallback would report every unreadable station as empty and feed it for
        ///         ever. An unreadable station is not an answer - the same rule taking from a
        ///         chest already follows.
        ///     </para>
        ///     <para>
        ///         <b><paramref name="carrying" /> is what stops the errand changing under the
        ///         villager.</b> Asked freely, a station answers with what it most wants, and
        ///         that answer moves: a cold furnace wants ore, and the instant one ore is in it
        ///         wants coal instead. A villager holding four more ore would then be told its
        ///         load is unwanted and walk it back to the chest - one ore in and four out,
        ///         every trip, for ever. So a villager that is already carrying something asks a
        ///         narrower question: <em>do you still want this?</em>
        ///     </para>
        /// </remarks>
        internal abstract StationWant WhatItWants(StructureSettings settings, string carrying);

        /// <summary>Whether something finished is sitting on it, blocking anything else going in.</summary>
        internal abstract bool HasOutput();

        /// <summary>
        ///     Takes the finished thing off, which makes it a world drop for hauling to file.
        /// </summary>
        /// <returns>True when the station's own state moved.</returns>
        internal abstract bool TakeOutput();

        /// <summary>Whether this station would take this item at all, right now.</summary>
        internal abstract FeedResult WouldTake(string prefab, bool asFuel);

        /// <summary>
        ///     A number that must go <em>up</em> when a load lands.
        /// </summary>
        /// <remarks>
        ///     Up, never merely different: a smelter burns its fuel down on its own one-second
        ///     tick, so a villager that fed it at the wrong moment would read a change that was
        ///     really a burn and call it success. Strictly greater is the only comparison that
        ///     cannot be fooled by the station working normally.
        /// </remarks>
        protected abstract double Progress(bool asFuel);

        /// <summary>Makes the call. The base class has already spent the item.</summary>
        protected abstract void Submit(string prefab, bool asFuel);

        /// <summary>
        ///     Puts one item in, and proves it went.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>The item is consumed before the call, never after.</b> A removal that fails
        ///         after the call has already handed the station a free item, and the settlement
        ///         pays for work it did not do.
        ///     </para>
        ///     <para>
        ///         Ownership is claimed even though the RPC would route to the owner by itself,
        ///         and the reason is the verification rather than the call: a routed RPC to
        ///         somebody else's station is a network round trip, so the reading taken
        ///         immediately afterwards would report every remote station as swallowing. Owning
        ///         it makes the before-and-after a fact about this tick.
        ///     </para>
        /// </remarks>
        internal FeedResult Give(Inventory bag, ItemDrop.ItemData item, bool asFuel)
        {
            if (View == null || !View.IsValid() || bag == null || item == null) return FeedResult.Unavailable;

            string prefab = Jobs.Carrying.NameOf(item);
            if (prefab.Length == 0) return FeedResult.Refused;

            // Asked before anything is spent, so a refusal costs nothing.
            FeedResult allowed = WouldTake(prefab, asFuel);
            if (allowed != FeedResult.Fed) return allowed;

            if (!View.IsOwner())
            {
                View.ClaimOwnership();
                return FeedResult.Waiting;
            }

            double before = Progress(asFuel);

            // One unit, and gone before the call. Inventory.RemoveItem answers nothing, so the
            // bag is re-read afterwards rather than trusted - a spend that did not happen is how
            // a station gets handed a free item, and it would look exactly like success.
            int held = bag.CountItems(item.m_shared.m_name);
            bag.RemoveItem(item.m_shared.m_name, 1);
            if (bag.CountItems(item.m_shared.m_name) >= held) return FeedResult.Unavailable;

            Submit(prefab, asFuel);

            return Progress(asFuel) > before ? FeedResult.Fed : FeedResult.Swallowed;
        }
    }
}
