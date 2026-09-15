using Kukolony.Colonies;
using Kukolony.Villagers;
using Kukolony.Villagers.Navigation;
using UnityEngine;

namespace Kukolony.Jobs
{
    /// <summary>
    ///     Going and getting something to eat.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The walk, the chest and the taking all live in <see cref="Errand" />, which the
    ///         tool and seed errands share. What is left here is the one question that differs -
    ///         what counts as food - and it must be the same question the eating asks of the bag
    ///         afterwards, or a villager fetches forever and never sees what it is carrying.
    ///     </para>
    ///     <para>
    ///         <b>The chests it will open are the ones the settlement may take from</b>, because
    ///         that is what <see cref="Errand" /> searches. A chest switched to "villagers may not
    ///         use what is here" keeps its food and still accepts deliveries - which is what a
    ///         player means when they set that flag on their own larder.
    ///     </para>
    /// </remarks>
    internal static class FoodErrand
    {
        /// <summary>
        ///     Fetches the most filling food the settlement will part with, or answers null when
        ///     there is none.
        /// </summary>
        /// <returns>
        ///     Null when there is nothing to fetch, which is not a failure - it is the state the
        ///     eating's own "nothing to eat" answer already describes.
        /// </returns>
        internal static JobResult? Run(Villager villager, Colony colony, Container bag,
            VillagerWalk walk, VillagerState state, float deltaTime, out string activity) =>
            Errand.Fetch(villager, colony, bag, walk, state, deltaTime,
                inside => Best(inside), "something to eat", out activity);

        /// <summary>
        ///     The most filling thing in here, or null if none of it is food.
        /// </summary>
        /// <remarks>
        ///     <b>Most filling, not first found.</b> This is the whole reason cooking is worth
        ///     doing: a cooked meal carries several times the burn time of what it was made from,
        ///     so a settlement that cooks feeds itself on far fewer trips. Taking the first edible
        ///     thing found would have made a kitchen decorative.
        /// </remarks>
        internal static ItemDrop.ItemData Best(Inventory inventory)
        {
            if (inventory == null) return null;

            ItemDrop.ItemData best = null;
            float most = 0f;

            foreach (ItemDrop.ItemData held in inventory.GetAllItems())
            {
                float worth = Worth(held);
                if (worth <= most) continue;

                most = worth;
                best = held;
            }

            return best;
        }

        /// <summary>
        ///     How many seconds of fullness this item is worth, or zero if it is not food.
        /// </summary>
        /// <remarks>
        ///     <b>Straight from the asset, never from a list of names.</b> Every food in the game
        ///     carries its own burn time, so this mod neither ships a table that would go stale
        ///     nor has to have heard of a modded food for villagers to eat it. Both numbers are
        ///     required: <c>m_food</c> alone admits things that fill nothing, and a burn time
        ///     alone admits mead, which is a potion rather than a meal.
        /// </remarks>
        internal static float Worth(ItemDrop.ItemData item)
        {
            ItemDrop.ItemData.SharedData shared = item?.m_shared;
            if (shared == null) return 0f;
            if (shared.m_food <= 0f || shared.m_foodBurnTime <= 0f) return 0f;

            return shared.m_foodBurnTime;
        }
    }
}
