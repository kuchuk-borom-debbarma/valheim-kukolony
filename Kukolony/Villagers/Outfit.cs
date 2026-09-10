using System.Collections.Generic;

namespace Kukolony.Villagers
{
    /// <summary>Where a worn item goes on a villager.</summary>
    /// <remarks>
    ///     These are the slots the game itself renders on a player model, which is what a
    ///     villager is built from. Persisted by index, so append rather than reorder.
    /// </remarks>
    internal enum OutfitSlot
    {
        Helmet = 0,
        Chest = 1,
        Legs = 2,
        Shoulder = 3,
        Utility = 4,
        RightHand = 5,
        LeftHand = 6
    }

    /// <summary>
    ///     What a villager should be wearing and holding: one item name per slot.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A preference, not a wardrobe. The outfit says what is wanted; whether the
    ///         villager has any of it is a separate question, answered by its bag. Nothing here
    ///         moves an item, and an outfit naming something the colony does not own is not an
    ///         error - the villager simply goes without until one turns up.
    ///     </para>
    ///     <para>
    ///         Owned by a colony and shared by name, the way job presets are, so dressing a
    ///         dozen villagers alike is one edit rather than a dozen.
    ///     </para>
    /// </remarks>
    internal sealed class Outfit
    {
        internal const int SlotCount = 7;

        internal string Name = string.Empty;

        /// <summary>Prefab name per slot, indexed by <see cref="OutfitSlot"/>. Empty means bare.</summary>
        internal readonly string[] Items = new string[SlotCount];

        internal string this[OutfitSlot slot]
        {
            get => Items[(int)slot] ?? string.Empty;
            set => Items[(int)slot] = value ?? string.Empty;
        }

        /// <summary>Every item this outfit names, without the empty slots.</summary>
        internal List<string> Wanted()
        {
            List<string> wanted = new List<string>();
            foreach (string item in Items)
                if (!string.IsNullOrEmpty(item) && !wanted.Contains(item)) wanted.Add(item);
            return wanted;
        }

        internal Outfit Clone()
        {
            Outfit copy = new Outfit { Name = Name };
            for (int i = 0; i < SlotCount; i++) copy.Items[i] = Items[i];
            return copy;
        }

        /// <summary>
        ///     The outfit a new colony starts with, which names nothing.
        /// </summary>
        /// <remarks>
        ///     Empty on purpose. Villagers already roll their own clothes, so a starter outfit
        ///     that named anything would change how every existing colony looks the moment this
        ///     shipped. Naming a slot is how a player takes it over.
        /// </remarks>
        internal static Outfit Everyday() => new Outfit { Name = "Everyday" };
    }
}
