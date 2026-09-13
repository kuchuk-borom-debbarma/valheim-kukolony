using System;
using System.Collections.Generic;

namespace Kukolony.Colonies
{
    /// <summary>Whether an order is a standing one or a one-off.</summary>
    /// <remarks>
    ///     Zero is <see cref="Maintain" /> because an unwritten field reads as zero, and a
    ///     standing order is the safe thing to land on: it stops of its own accord once the
    ///     settlement has enough, where a one-off that forgot it was finished would keep going.
    /// </remarks>
    internal enum OrderMode
    {
        /// <summary>Keep the settlement holding this many, resuming whenever it falls short.</summary>
        Maintain = 0,

        /// <summary>Make this many once, then stop for good.</summary>
        Once = 1
    }

    /// <summary>
    ///     One thing a structure has been told to produce.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The same shape serves a crafting station and a processing one, which is why it is
    ///         a type rather than a pair of fields. On a forge the item is what to make; on a
    ///         kiln it is what the kiln produces, so "keep it fed until we have a hundred coal"
    ///         and "make fifty nails" are one sentence with two subjects.
    ///     </para>
    ///     <para>
    ///         <b>By prefab name.</b> Every count this is compared against - a bag, a chest,
    ///         <see cref="Stock" /> - is indexed by prefab name, while the game's own crafting
    ///         requirements are keyed by the localised shared name. Mixing the two is the bug
    ///         that once had a villager fetching wood it was standing on, so the spelling is
    ///         fixed here and converted in exactly one place.
    ///     </para>
    /// </remarks>
    internal sealed class StructureOrder
    {
        internal string Item = string.Empty;

        /// <summary>How many. Zero or less means the order says nothing and is ignored.</summary>
        internal int Count;

        internal OrderMode Mode = OrderMode.Maintain;

        /// <summary>
        ///     Whether a <see cref="OrderMode.Once" /> order has already been filled.
        /// </summary>
        /// <remarks>
        ///     A latch rather than a recomputation, because "have we made fifty" cannot be
        ///     answered by looking at the settlement: fifty arrows made and fifty arrows fired
        ///     leave no trace, and a one-off order would start again every time the stock was
        ///     spent - which is precisely the standing order the player did not ask for.
        ///     Cleared whenever the line is edited, so changing your mind restarts it.
        /// </remarks>
        internal bool Done;

        /// <summary>Whether this order still wants anything made.</summary>
        internal bool Wants => Count > 0 && !string.IsNullOrEmpty(Item) &&
                               !(Mode == OrderMode.Once && Done);

        /// <summary>Whether the settlement already holds everything this order asked for.</summary>
        internal bool SatisfiedBy(int held) => !Wants || held >= Count;
    }

    /// <summary>
    ///     What a list of orders still wants.
    /// </summary>
    /// <remarks>
    ///     Arithmetic rather than behaviour, and deliberately with no Unity in it, so every
    ///     case is checked in about a second rather than only inside a four-minute game run.
    ///     The stock count arrives as a function because where it comes from - the settlement's
    ///     containers - is exactly the part that needs a world.
    /// </remarks>
    internal static class Orders
    {
        /// <summary>
        ///     Whether a station with these orders still has work to do.
        /// </summary>
        /// <remarks>
        ///     <b>No orders means no limit</b>, which is the opposite of what a list normally
        ///     means here and is the right answer for both users. A kiln that has been given no
        ///     target should be kept fed, as it was before orders existed; and a crafting
        ///     station with nothing to make is filtered out long before this, because it has
        ///     nothing to make rather than no limit on making it.
        /// </remarks>
        internal static bool WantsMore(List<StructureOrder> orders, Func<string, int> held)
        {
            if (orders == null || orders.Count == 0) return true;

            foreach (StructureOrder order in orders)
            {
                if (order == null || !order.Wants) continue;
                if (!order.SatisfiedBy(held(order.Item))) return true;
            }

            // Every order is filled, or every one has been finished and latched. Either way
            // there is nothing here to do - as distinct from a station that was never given an
            // order at all, which returned true above because it has no limit rather than a
            // limit already reached.
            return false;
        }

        /// <summary>
        ///     The orders that still want something, in the list's own order.
        /// </summary>
        /// <remarks>
        ///     Ordered rather than sorted by need, because the list is the player's sentence and
        ///     reordering it silently would make the screen a poor description of what happens.
        /// </remarks>
        internal static List<StructureOrder> Outstanding(List<StructureOrder> orders, Func<string, int> held)
        {
            List<StructureOrder> wanting = new List<StructureOrder>();
            if (orders == null) return wanting;

            foreach (StructureOrder order in orders)
            {
                if (order != null && order.Wants && !order.SatisfiedBy(held(order.Item))) wanting.Add(order);
            }

            return wanting;
        }
    }
}
