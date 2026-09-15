using Kukolony.Colonies;
using Kukolony.Core;
using Kukolony.Gui;
using Kukolony.Jobs;
using Kukolony.Villagers.Navigation;
using UnityEngine;

namespace Kukolony.Villagers
{
    /// <summary>
    ///     Getting hungry, going to eat, and starving when there is nothing.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Asked before the queue, beside resting</b>, and for the same reason resting is:
    ///         a hungry villager has nothing useful to offer any job, and asking one for work it
    ///         is about to abandon burns a repetition to discover that. Eating comes first of the
    ///         two, because being tired costs a settlement time and being hungry costs it people.
    ///     </para>
    ///     <para>
    ///         <b>Villagers cannot use the game's own eating.</b> <c>Humanoid.ConsumeItem</c> is
    ///         eleven bytes that defer to <c>CanConsumeItem</c>; the real work - the three food
    ///         slots, <c>EatFood</c>, <c>UpdateFood</c> - lives on <c>Player</c> and nowhere else.
    ///         So satiety is modelled here. What is <em>not</em> modelled here is how filling any
    ///         particular food is: that is read off the asset, so the balance stays the game's.
    ///     </para>
    ///     <para>
    ///         <b>And hunger only advances while a villager is loaded and ticking</b>, because
    ///         this is the only thing that advances it. A villager in an unloaded zone is not
    ///         starving quietly; it is not doing anything. That is deliberate - a settlement that
    ///         works unattended must not be able to kill everybody for the crime of the player
    ///         walking away, or of the keep-alive reaching its zone cap. The step is capped as
    ///         well, so even a villager that does tick after a long gap is charged a minute for
    ///         it rather than six hours. See <see cref="Hunger.MaxStepSeconds" />.
    ///     </para>
    /// </remarks>
    internal static class Eating
    {
        /// <summary>
        ///     Satiety right now, with the time since the last step charged against it.
        /// </summary>
        /// <remarks>
        ///     Writes only when there is a step worth writing. This is asked on the work tick,
        ///     which runs at the AI's rate, and a replicated ZDO write per villager per frame is
        ///     the one shape a settlement with no population cap cannot carry.
        /// </remarks>
        internal static float Charge(VillagerState state)
        {
            float stored = state.Fed;

            // Never stamped: a villager born before this existed, or one on its first tick.
            // Stamping it now rather than charging it for every second since the world began is
            // what stops an existing save from opening on a funeral.
            if (state.FedAt <= 0d)
            {
                state.SetFed(stored);
                return stored;
            }

            double since = ZNet.instance == null ? 0d : ZNet.instance.GetTimeSeconds() - state.FedAt;
            float step = Hunger.Step(since);
            if (step <= 0f) return Hunger.Left(stored, 0f, Floor);

            float left = Hunger.Left(stored, step, Floor);
            state.SetFed(left);
            return left;
        }

        /// <summary>
        ///     Eats if it needs to, and dies if it has gone too long without.
        /// </summary>
        /// <returns>True when the villager is dealing with hunger and must not be given work.</returns>
        internal static bool Tick(Villager villager, Colony colony, VillagerState state,
            Container bag, VillagerWalk walk, float deltaTime, out string doing)
        {
            doing = string.Empty;
            if (villager == null || colony == null) return false;

            float left = Charge(state);

            if (!Hunger.Wants(left, HungryBelow))
            {
                Chatter.Forget(Key(villager));
                return false;
            }

            // Something edible already in the bag, which includes food it happens to be hauling.
            // A hungry person eating one of the twenty steaks they are carrying is the right
            // answer, and the alternative - starving to death on top of a full load - is not.
            ItemDrop.ItemData meal = bag != null ? FoodErrand.Best(bag.GetInventory()) : null;
            if (meal != null)
            {
                doing = Eat(villager, state, bag, meal, left);
                return true;
            }

            JobResult? errand = FoodErrand.Run(villager, colony, bag, walk, state, deltaTime,
                out string fetching);
            if (errand != null)
            {
                doing = fetching;
                return true;
            }

            // Hungry, with nothing in the bag and nothing in the settlement. While there is still
            // something left in it, it goes back to work - a villager that downed tools the
            // moment it fancied a meal would never build the kitchen.
            if (!Hunger.IsStarving(left)) return false;

            doing = Starve(villager, colony, state, left);
            return true;
        }

        /// <summary>Eats one of something, and says so.</summary>
        private static string Eat(Villager villager, VillagerState state, Container bag,
            ItemDrop.ItemData meal, float left)
        {
            float worth = FoodErrand.Worth(meal);

            // The prefab name, through the same helper the errand reports with. m_shared.m_name
            // is the localisation token, which the catalogue does not index - so naming the meal
            // that way reads as a blank in the very message that proves this worked.
            string what = Carrying.NameOf(meal);

            // Removed by instance rather than by name. Inventory's name-matching overload matches
            // on the localised name and silently skips what it cannot find, which in this mod has
            // already been the difference between spending a thing and appearing to.
            bag.GetInventory().RemoveOneItem(meal);
            VillagerInventory.Persist(bag, Record(villager));

            state.SetFed(Hunger.Ate(left, worth, Cap));
            Chatter.Forget(Key(villager));

            Report.Say($"{state.Name} ate {ItemCatalogue.Label(what)}.");
            return "eating";
        }

        /// <summary>
        ///     Stops working, says so, and eventually dies of it.
        /// </summary>
        /// <remarks>
        ///     <b>The saying comes long before the dying</b>, and through <see cref="Chatter" />
        ///     so it is said once rather than twenty times a second. This mod runs off-screen for
        ///     hours, and the realistic failure is coming back to a dead settlement caused by a
        ///     chest nobody filled - so a death that was not announced well in advance is a bug
        ///     in this method even when the arithmetic above it is perfect.
        /// </remarks>
        private static string Starve(Villager villager, Colony colony, VillagerState state,
            float left)
        {
            Chatter.Say(Key(villager), $"{state.Name} has nothing to eat and has stopped working.");

            float grace = Grace;
            if (!Hunger.Starved(left, grace))
            {
                return $"starving - {IdleWatch.Spell(Hunger.StarvingFor(left))}";
            }

            if (!Kills) return "starving";

            Report.Say($"{state.Name} has starved to death.");
            Chatter.Forget(Key(villager));
            VillagerLifecycle.Remove(colony, villager.Id);

            return "starved";
        }

        /// <summary>One complaint per villager, cleared the moment it eats.</summary>
        private static string Key(Villager villager) => $"[hunger] {villager.Id}";

        private static ZNetView Record(Villager villager) =>
            villager != null && villager.TryGetComponent(out ZNetView view) && view.IsValid()
                ? view
                : null;

        /// <summary>
        ///     How far into hunger a villager may fall, as a negative number.
        /// </summary>
        /// <remarks>
        ///     The grace, negated. Floored there so that a famine survived with the killing
        ///     switched off leaves everybody one meal from recovery rather than owing hours of
        ///     food that no reachable chest could ever repay.
        /// </remarks>
        private static float Floor => -Grace;

        private static float Grace =>
            ModConfig.StarvingGraceSeconds != null ? ModConfig.StarvingGraceSeconds.Value : 1800f;

        private static float Cap =>
            ModConfig.FedCapSeconds != null ? ModConfig.FedCapSeconds.Value : 1800f;

        /// <summary>
        ///     The satiety at which a villager stops to eat, never at or above what it can hold.
        /// </summary>
        /// <remarks>
        ///     <b>The clamp is not tidiness, it is a livelock.</b> A villager full to the cap that
        ///     still counts as hungry eats, gains nothing, and eats again - working through a
        ///     larder in seconds and never doing anything else. Two independent config numbers
        ///     can be set that way by anybody who has not thought about it, so the pairing is
        ///     enforced here rather than trusted to the person editing the file.
        /// </remarks>
        private static float HungryBelow
        {
            get
            {
                float wanted = ModConfig.HungryBelowSeconds != null
                    ? ModConfig.HungryBelowSeconds.Value
                    : 600f;
                float cap = Cap;

                return wanted < cap ? wanted : cap;
            }
        }

        private static bool Kills => ModConfig.StarvingKills != null && ModConfig.StarvingKills.Value;
    }
}
