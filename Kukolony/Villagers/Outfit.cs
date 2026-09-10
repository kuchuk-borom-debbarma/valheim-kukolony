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

        /// <summary>
        ///     Acceptable items per slot, in order of preference. An empty list leaves the slot
        ///     alone.
        /// </summary>
        /// <remarks>
        ///     A list rather than one name, because "leather or troll leather, whichever we
        ///     have" is what a colony actually wants, and a single choice is just a list of
        ///     one. Order is the preference: the villager wears the first it owns, so putting
        ///     the better armour first upgrades everybody as soon as one is crafted.
        /// </remarks>
        internal readonly List<string>[] Choices = NewChoices();

        internal List<string> this[OutfitSlot slot] => Choices[(int)slot];

        private static List<string>[] NewChoices()
        {
            List<string>[] slots = new List<string>[SlotCount];
            for (int i = 0; i < SlotCount; i++) slots[i] = new List<string>();
            return slots;
        }

        /// <summary>Every item this outfit names, without duplicates.</summary>
        internal List<string> Wanted()
        {
            List<string> wanted = new List<string>();
            foreach (List<string> slot in Choices)
                foreach (string item in slot)
                    if (!string.IsNullOrEmpty(item) && !wanted.Contains(item)) wanted.Add(item);
            return wanted;
        }

        /// <summary>True when this slot is left to whatever the villager was born wearing.</summary>
        internal bool Ignores(OutfitSlot slot) => Choices[(int)slot].Count == 0;

        internal Outfit Clone()
        {
            Outfit copy = new Outfit { Name = Name };
            for (int i = 0; i < SlotCount; i++) copy.Choices[i].AddRange(Choices[i]);
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

        /// <summary>Which kind of item belongs in a slot, so a chest row offers chestpieces.</summary>
        internal static ItemDrop.ItemData.ItemType Accepts(OutfitSlot slot)
        {
            switch (slot)
            {
                case OutfitSlot.Helmet: return ItemDrop.ItemData.ItemType.Helmet;
                case OutfitSlot.Chest: return ItemDrop.ItemData.ItemType.Chest;
                case OutfitSlot.Legs: return ItemDrop.ItemData.ItemType.Legs;
                case OutfitSlot.Shoulder: return ItemDrop.ItemData.ItemType.Shoulder;
                case OutfitSlot.Utility: return ItemDrop.ItemData.ItemType.Utility;
                // Hands take tools and weapons, which the game files under several types, so
                // those rows offer everything rather than a list that quietly omits the axe.
                default: return ItemDrop.ItemData.ItemType.None;
            }
        }
    }
}
