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

        internal void Write(ZPackage package)
        {
            package.Write(Item ?? string.Empty);
            package.Write(Count);
            package.Write((int)Mode);
            package.Write(Done);
        }

        internal static StructureOrder Read(ZPackage package)
        {
            StructureOrder order = new StructureOrder
            {
                Item = package.ReadString(),
                Count = package.ReadInt()
            };

            // Explicit rather than a cast, for the reason every other enum here is read this
            // way: a blob written by a later build can carry a mode this one has never heard
            // of, and casting an unknown number into an enum produces a value no switch
            // handles and no branch rejects.
            int mode = package.ReadInt();
            order.Mode = mode == (int)OrderMode.Once ? OrderMode.Once : OrderMode.Maintain;
            order.Done = package.ReadBool();
            return order;
        }
    }
}
