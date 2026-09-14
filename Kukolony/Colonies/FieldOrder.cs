using System.Collections.Generic;

namespace Kukolony.Colonies
{
    /// <summary>How much of something a field should be growing.</summary>
    /// <remarks>
    ///     Persisted as an int, so append rather than reorder. <see cref="Fill" /> is zero
    ///     because an unwritten field reads as zero and "as many as fit" is the safe thing to
    ///     land on: it is what marking out a patch of ground and naming a crop obviously means,
    ///     and it is bounded by the field's own edge rather than by a number nobody set.
    /// </remarks>
    internal enum SowMode
    {
        /// <summary>As many as the field has room for.</summary>
        Fill = 0,

        /// <summary>Keep this many growing, sowing again whenever one is taken.</summary>
        Keep = 1,

        /// <summary>Sow this many and then stop for good.</summary>
        Once = 2
    }

    /// <summary>
    ///     One thing a field has been told to grow.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Its own type rather than <see cref="StructureOrder" />.</b> That one's docstring
    ///         says it serves a crafting station and a processing one, and what it counts is what
    ///         the settlement <em>holds</em>. A field counts what is <em>in the ground</em>, which
    ///         is a third meaning - and one shape carrying three meanings is how two of them come
    ///         to disagree without anybody noticing which.
    ///     </para>
    ///     <para>
    ///         <b>By plant prefab, not by crop.</b> The picker offers crops and this records what
    ///         was chosen to grow them, because the two are not one-to-one: a carrot and a carrot
    ///         seed come from different saplings and cost each other as seed. An order that said
    ///         "carrot" could not tell which of those a player meant.
    ///     </para>
    /// </remarks>
    internal sealed class FieldOrder
    {
        /// <summary>The plant prefab to put in the ground.</summary>
        internal string Plant = string.Empty;

        /// <summary>How many, for the modes that count. Ignored by <see cref="SowMode.Fill" />.</summary>
        internal int Count;

        internal SowMode Mode = SowMode.Fill;

        /// <summary>How many have been sown against a <see cref="SowMode.Once" /> order.</summary>
        /// <remarks>
        ///     Counted rather than inferred from the ground, because that is the whole difference
        ///     between <em>Once</em> and <em>Keep</em>: a one-off that read the ground would start
        ///     again the moment somebody harvested, which is exactly what it was told not to do.
        /// </remarks>
        internal int Sown;

        internal bool IsValid => !string.IsNullOrEmpty(Plant);

        /// <summary>
        ///     Whether this order wants another one planted, given what is already growing.
        /// </summary>
        /// <param name="growing">How many of this plant stand in the field now.</param>
        /// <param name="room">Whether the field has anywhere left to put one.</param>
        internal bool WantsMore(int growing, bool room)
        {
            if (!IsValid || !room) return false;

            switch (Mode)
            {
                case SowMode.Keep:
                    return Count > 0 && growing < Count;

                case SowMode.Once:
                    return Count > 0 && Sown < Count;

                default:
                    // Fill. Bounded by the ground rather than by a number, which is what the
                    // room flag already says - so reaching here means there is somewhere to put
                    // one and nothing saying not to.
                    return true;
            }
        }

        /// <summary>What a player should read for this order.</summary>
        /// <remarks>
        ///     Explicit, with no fallback that invents a plausible phrase, for the reason
        ///     <c>StructureCapabilities.Describe</c> has none: a default branch returning
        ///     something readable once made a newly added kind display as an existing one.
        /// </remarks>
        internal string Describe(string label)
        {
            switch (Mode)
            {
                case SowMode.Keep: return $"{label} - keep {Count} growing";
                case SowMode.Once: return $"{label} - sow {Count} ({Sown} done)";
                case SowMode.Fill: return $"{label} - fill the field";
                default: return label;
            }
        }
    }

    /// <summary>The rules that read a whole list of orders at once.</summary>
    internal static class FieldOrders
    {
        /// <summary>
        ///     The first order that wants another plant, or null when the field is content.
        /// </summary>
        /// <remarks>
        ///     <b>In the order they are listed, and the order is the player's.</b> A field told
        ///     to grow carrots and then turnips fills with carrots first and moves on when they
        ///     are satisfied, which is the only reading under which the list on the screen means
        ///     anything. Taking whichever is furthest behind would make the order decorative.
        /// </remarks>
        internal static FieldOrder Next(IReadOnlyList<FieldOrder> orders,
            System.Func<string, int> growing, bool room)
        {
            if (orders == null || growing == null) return null;

            for (int i = 0; i < orders.Count; i++)
            {
                FieldOrder order = orders[i];
                if (order == null || !order.IsValid) continue;
                if (!order.WantsMore(growing(order.Plant), room)) continue;

                return order;
            }

            return null;
        }
    }
}
