using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Jobs
{
    /// <summary>
    ///     Taking things out of a bag and proving they went.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>The removal cannot be trusted to fail.</b> <c>Inventory.RemoveItem</c> returns
    ///         <c>void</c>, and it silently skips any item whose <c>m_worldLevel</c> is below the
    ///         world's - so on an NG+ world a villager spends nothing, produces everything, and no
    ///         call anywhere reports an error. Crafting found this and counts either side of every
    ///         removal; sowing is the second thing in this mod that has to spend, and one rule
    ///         written twice is two rules waiting to disagree about free carrots.
    ///     </para>
    ///     <para>
    ///         <b>Two spellings, and only one of them works.</b> Everything this mod counts is
    ///         indexed by <em>prefab</em> name, while <c>RemoveItem</c> matches on the localised
    ///         <em>shared</em> name. Mixing them is the bug that once had a villager fetching wood
    ///         it was standing on, so the conversion happens here and nowhere else.
    ///     </para>
    /// </remarks>
    internal static class Spending
    {
        /// <summary>
        ///     How much of an item is held, under the same rule that removing it will use.
        /// </summary>
        /// <remarks>
        ///     The world-level test is the point. Counting without it says the materials are
        ///     there, the removal takes nothing, and the work happens for free.
        /// </remarks>
        internal static int Held(Inventory inventory, string prefab)
        {
            if (inventory == null || string.IsNullOrEmpty(prefab)) return 0;

            int total = 0;
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
            {
                if (Carrying.NameOf(item) != prefab) continue;
                if (item.m_worldLevel < Game.m_worldLevel) continue;

                total += item.m_stack;
            }

            return total;
        }

        /// <summary>
        ///     How much is held under the name <c>RemoveItem</c> will match on.
        /// </summary>
        /// <remarks>
        ///     The shared name, not the prefab name, because that is what the removal compares -
        ///     and the removal is what this is used to verify. Two prefabs can share one shared
        ///     name, so counting by prefab either side of a removal that matched by shared name
        ///     would report a spend that did not happen, and one that did as a failure.
        /// </remarks>
        internal static int HeldByName(Inventory inventory, string sharedName)
        {
            if (inventory == null || string.IsNullOrEmpty(sharedName)) return 0;

            int total = 0;
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
            {
                if (item?.m_shared == null || item.m_shared.m_name != sharedName) continue;
                if (item.m_worldLevel < Game.m_worldLevel) continue;

                total += item.m_stack;
            }

            return total;
        }

        /// <summary>
        ///     The name <c>RemoveItem</c> will match, for an item named by its prefab.
        /// </summary>
        /// <remarks>
        ///     Taken from the bag rather than from the item database, because the thing being
        ///     spent is in the bag: an item's shared name is on the instance, and looking it up
        ///     elsewhere is an extra way for the two to disagree. Empty when nothing of that
        ///     prefab is held, which is a real answer and the caller's cue to stop.
        /// </remarks>
        internal static string SharedNameOf(Inventory inventory, string prefab)
        {
            if (inventory == null || string.IsNullOrEmpty(prefab)) return string.Empty;

            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
            {
                if (item?.m_shared == null) continue;
                if (Carrying.NameOf(item) != prefab) continue;

                return item.m_shared.m_name;
            }

            return string.Empty;
        }

        /// <summary>
        ///     Spends this many of a prefab, and says whether they actually went.
        /// </summary>
        /// <remarks>
        ///     <b>Counted either side.</b> A caller that trusted the call would carry on having
        ///     spent nothing, which is the shape of every free-crafting bug this rule exists to
        ///     prevent - and a settlement quietly getting something for nothing is far harder to
        ///     notice than one that refuses.
        /// </remarks>
        internal static bool Spend(Inventory inventory, string prefab, int amount, out string why)
        {
            why = string.Empty;
            if (inventory == null || string.IsNullOrEmpty(prefab) || amount <= 0)
            {
                why = "nothing to spend";
                return false;
            }

            if (Held(inventory, prefab) < amount)
            {
                why = "short of " + prefab;
                return false;
            }

            string named = SharedNameOf(inventory, prefab);
            if (string.IsNullOrEmpty(named))
            {
                why = "short of " + prefab;
                return false;
            }

            int before = HeldByName(inventory, named);
            inventory.RemoveItem(named, amount);
            int after = HeldByName(inventory, named);

            if (before - after >= amount) return true;

            // Said loudly, because the alternative is a settlement quietly working for free.
            Log.Warning($"[spend] could not spend {amount} {named} ({before} to {after}).");
            why = "could not spend " + prefab;
            return false;
        }
    }
}
