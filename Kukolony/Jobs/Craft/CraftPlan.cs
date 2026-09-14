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
