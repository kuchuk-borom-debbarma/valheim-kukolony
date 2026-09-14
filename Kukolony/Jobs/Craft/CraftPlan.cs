using System;
using System.Collections.Generic;

namespace Kukolony.Jobs.Craft
{
    /// <summary>One thing a recipe needs, and how much of it.</summary>
    internal struct CraftNeed
    {
        internal CraftNeed(string item, int amount)
        {
            Item = item;
            Amount = amount;
        }

        /// <summary>By prefab name, which is the spelling every count here is in.</summary>
        internal string Item;

        internal int Amount;
    }

    /// <summary>
    ///     How much to make, and whether the materials are there to make it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Arithmetic with no Unity in it, so every case is checked in about a second rather
    ///         than only inside a four-minute game run. What it decides is easy to get subtly
    ///         wrong and hard to see wrong: a villager that makes one too many, or that fetches
    ///         materials for a craft it cannot complete, looks exactly like one that is working.
    ///     </para>
    ///     <para>
    ///         The counts arrive as functions because where they come from - a bag, a chest, the
    ///         settlement - is exactly the part that needs a world.
    ///     </para>
    /// </remarks>
    internal static class CraftPlan
    {
        /// <summary>
        ///     How many of an order are still worth making.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Bounded by three separate things, and each bound is a rule somebody would
        ///         otherwise have to remember: the order's own target, the materials that exist,
        ///         and how many the villager can carry away. A craft that cannot be carried is a
        ///         craft that lands on the floor.
        ///     </para>
        ///     <para>
        ///         <b>Zero is a real answer</b> and means "not now" rather than "never" - the
        ///         station may want something else, and the settlement may be short for an hour.
        ///     </para>
        /// </remarks>
        /// <param name="wanted">How many the order still wants - target less what is held.</param>
        /// <param name="perCraft">How many the recipe yields in one go.</param>
        /// <param name="needs">What one craft consumes.</param>
        /// <param name="available">How much of an item can be got hold of.</param>
        /// <param name="room">How many of the product there is room to carry.</param>
        internal static int HowMany(int wanted, int perCraft, List<CraftNeed> needs,
            Func<string, int> available, int room)
        {
            if (wanted <= 0 || perCraft <= 0 || room <= 0) return 0;

            // Rounded up, because a recipe yielding two is not a reason to stop one short of a
            // target of five - the settlement asked for five and will get six, which is the
            // answer a player expects from "keep five".
            int crafts = (wanted + perCraft - 1) / perCraft;

            // What the bag can carry away caps it too, and this one rounds *down*: a craft
            // whose product will not fit is a craft that spills.
            crafts = Math.Min(crafts, room / perCraft);

            if (crafts <= 0) return 0;
            if (needs == null) return crafts;

            foreach (CraftNeed need in needs)
            {
                if (need.Amount <= 0) continue;

                // The scarcest requirement decides, which is what makes this one loop rather
                // than a check per material followed by a separate count.
                int affordable = available(need.Item) / need.Amount;
                if (affordable < crafts) crafts = affordable;
                if (crafts <= 0) return 0;
            }

            return crafts;
        }

        /// <summary>
        ///     Whether everything one craft needs is to hand.
        /// </summary>
        /// <remarks>
        ///     Asked of the bag before a villager walks to a station, and asked again before it
        ///     lifts a hammer. A recipe half-fetched is the state that reads as working and is
        ///     not: the villager arrives, cannot craft, and goes back for the rest for ever.
        /// </remarks>
        internal static bool Enough(List<CraftNeed> needs, Func<string, int> held)
        {
            if (needs == null) return true;

            foreach (CraftNeed need in needs)
            {
                if (need.Amount > 0 && held(need.Item) < need.Amount) return false;
            }

            return true;
        }

        /// <summary>
        ///     The first thing a craft is short of, or empty when it is short of nothing.
        /// </summary>
        /// <remarks>
        ///     One name rather than a list, because it becomes a sentence a villager says - "no
        ///     iron in any chest" - and a villager reciting a shopping list is not an
        ///     improvement on one naming the thing it went looking for first.
        /// </remarks>
        internal static string Missing(List<CraftNeed> needs, Func<string, int> held)
        {
            if (needs == null) return string.Empty;

            foreach (CraftNeed need in needs)
            {
                if (need.Amount > 0 && held(need.Item) < need.Amount) return need.Item;
            }

            return string.Empty;
        }
    }
}
