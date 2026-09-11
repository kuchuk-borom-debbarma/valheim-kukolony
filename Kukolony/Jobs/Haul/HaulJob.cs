using System.Collections.Generic;
using Kukolony.Colonies;
using Kukolony.Villagers;
using Kukolony.Villagers.Navigation;
using UnityEngine;

namespace Kukolony.Jobs.Haul
{
    /// <summary>
    ///     Everything one tick of hauling needs, gathered once.
    /// </summary>
    internal sealed class HaulContext
    {
        internal Villager Villager;
        internal Colony Colony;
        internal Container Bag;
        internal VillagerWalk Walk;
        internal VillagerAnimation Animation;
        internal JobDefinition Job;
        internal VillagerState State;
    }

    /// <summary>
    ///     The doing half of hauling. The deciding half is
    ///     <see cref="HaulTransitions" />, which has no Unity in it and is tested separately.
    /// </summary>
    internal static class HaulJob
    {
        /// <summary>How close counts as being at something.</summary>
        private const float Reach = 2f;

        internal static JobResult Tick(HaulContext context, out string activity)
        {
            VillagerState state = context.State;

            GameObject source = Resolve(state.Target, out bool sourceLost);
            GameObject destination = Resolve(state.Destination, out bool destinationLost);

            // Gone is different from not loaded. Something destroyed is worth giving up on;
            // something merely out of memory is worth waiting for - but not forever, which is
            // why the wait is a Skipped rather than an unbounded Running.
            if (sourceLost) state.ClearTarget();
            if (destinationLost) state.SetDestination(ZDOID.None);

            List<ItemDrop.ItemData> carried = Carrying.Cargo(context.Bag.GetInventory(), state.Cargo);

            // Recorded cargo the bag does not actually hold is a stale hint - the player emptied
            // the villager, or another peer did. Forgetting it here means the trip re-chooses
            // rather than walking a delivery it cannot make.
            if (carried.Count == 0 && !string.IsNullOrEmpty(state.Cargo)) state.SetCargo(string.Empty);

            HaulFacts facts = new HaulFacts(
                hasSource: !state.Target.IsNone(),
                hasDestination: !state.Destination.IsNone(),
                atSource: Within(context, source),
                atDestination: Within(context, destination),
                carrying: carried.Count > 0,
                bagFull: !context.Bag.GetInventory().HaveEmptySlot(),
                tired: false);

            HaulStep step = HaulTransitions.Next((HaulState)state.WorkState, facts);

            switch (step.Action)
            {
                case HaulAction.Yield:
                    return JobOutcomes.Skipped(state, "nothing to haul", out activity);

                case HaulAction.ChooseWork:
                    return Record(state, step, Choose(context, carried, out activity));

                case HaulAction.MoveToSource:
                    return Record(state, step, Walk(context, source, "fetching", out activity));

                case HaulAction.Collect:
                    return Record(state, step, Collect(context, source, out activity));

                case HaulAction.MoveToDestination:
                    return Record(state, step, Walk(context, destination, "carrying", out activity));

                case HaulAction.Deposit:
                    return Record(state, step, Deposit(context, destination, carried, out activity));

                case HaulAction.Complete:
                    return JobOutcomes.Completed(state, "done hauling", out activity);

                default:
                    // An action this engine does not handle is a programming error, not a
                    // world state. Say so rather than silently idling.
                    return JobOutcomes.Failed(state, "unhandled haul action", out activity);
            }
        }

        /// <summary>
        ///     Records the next state when a step made progress.
        /// </summary>
        /// <remarks>
        ///     A step reporting Completed means <em>that step</em> finished, which is not the
        ///     job being done — so it becomes Running, and only the transition table's own
        ///     Complete ends a trip. Getting this backwards ends the cycle wherever the last
        ///     action happened to sit, and everything after it never runs.
        /// </remarks>
        private static JobResult Record(VillagerState state, HaulStep step, JobResult result)
        {
            if (result != JobResult.Running && result != JobResult.Completed) return result;
            state.SetWorkState((int)step.Next);
            return JobResult.Running;
        }

        private static JobResult Choose(HaulContext context, List<ItemDrop.ItemData> carried, out string activity)
        {
            VillagerState state = context.State;

            // Carrying something already: it needs a home, not a new errand.
            if (carried.Count > 0)
            {
                // The first deliverable thing in the load decides where this trip goes.
                // Everything else is delivered on a later pass, which keeps one destination per
                // trip, and an oddment nothing claims does not hold the rest of the load hostage.
                if (!Selection.FirstDeliverable(context.Colony, carried,
                        context.Villager.transform.position, out ItemDrop.ItemData _, out StructureRecord home))
                {
                    // Nothing it holds has anywhere to go. Put one down each pass rather than
                    // carrying them about: the bag is the villager's working space, and a load
                    // of oddments in it is a villager that can no longer haul.
                    context.Animation.Reach();
                    Carrying.PutDown(context.Bag.GetInventory(), carried[0], context.Villager.transform.position);
                    return JobOutcomes.Skipped(context.State, "nowhere to put this, so I left it", out activity);
                }

                state.SetDestination(home.Id);
                activity = "carrying";
                return JobResult.Running;
            }

            if (!Selection.TryFindGroundWork(context.Colony, context.Job, context.Villager,
                    out ItemDrop item, out StructureRecord destination))
            {
                return JobOutcomes.Skipped(context.State, "nothing to haul", out activity);
            }

            if (!item.TryGetComponent(out ZNetView view) || !view.IsValid())
            {
                return JobOutcomes.Skipped(context.State, "nothing to haul", out activity);
            }

            // Taking the target is also taking the claim, so no other villager walks here.
            state.SetTarget(view.GetZDO().m_uid);
            state.SetDestination(destination.Id);
            activity = "fetching";
            return JobResult.Running;
        }

        private static JobResult Walk(HaulContext context, GameObject target, string doing, out string activity)
        {
            if (target == null)
            {
                // Not loaded yet. Its zone may still be streaming in, but waiting forever is
                // how the previous system hung a villager, so this yields instead.
                return JobOutcomes.Skipped(context.State, "waiting for the world", out activity);
            }

            switch (context.Walk.MoveTowards(target.transform.position, Reach))
            {
                case MoveResult.Moving:
                    activity = doing;
                    return JobResult.Running;

                case MoveResult.Arrived:
                    activity = doing;
                    return JobResult.Running;

                default:
                    return JobOutcomes.Failed(context.State, "cannot get there", out activity);
            }
        }

        private static JobResult Collect(HaulContext context, GameObject source, out string activity)
        {
            if (source == null || !source.TryGetComponent(out ItemDrop drop))
            {
                context.State.ClearTarget();
                activity = "it was gone";
                return JobResult.Running;
            }

            context.Animation.Reach();

            switch (Carrying.TakeFromGround(drop, context.Bag.GetInventory(), out string taken))
            {
                case TakeResult.Took:
                    // The claim goes with the item; the trip and its destination remain, so a
                    // sweep can pick up the next thing bound for the same chest.
                    context.State.AddCargo(taken);
                    context.State.ClearTarget();
                    activity = "picked it up";
                    return JobResult.Running;

                case TakeResult.Waiting:
                    activity = "reaching for it";
                    return JobResult.Running;

                case TakeResult.Full:
                    context.State.ClearTarget();
                    activity = "my bag is full";
                    return JobResult.Running;

                default:
                    context.State.ClearTarget();
                    activity = "it was gone";
                    return JobResult.Running;
            }
        }

        private static JobResult Deposit(HaulContext context, GameObject destination,
            List<ItemDrop.ItemData> carried, out string activity)
        {
            if (destination == null || !destination.TryGetComponent(out Container container))
            {
                context.State.SetDestination(ZDOID.None);
                activity = "that chest is gone";
                return JobResult.Running;
            }

            if (carried.Count == 0) return JobOutcomes.Completed(context.State, "done hauling", out activity);

            // What the trip is holding may not all belong here. Releasing the destination sends
            // it back to choosing rather than forcing flint into the wood shed, and is also how
            // a chest that filled up mid-trip is noticed.
            if (!Selection.FirstDeliverable(context.Colony, carried, context.Villager.transform.position,
                    out ItemDrop.ItemData load, out StructureRecord belongs))
            {
                context.State.SetDestination(ZDOID.None);
                activity = "nowhere to put what I am carrying";
                return JobResult.Running;
            }

            if (belongs.Id != context.State.Destination)
            {
                context.State.SetDestination(ZDOID.None);
                activity = "this goes somewhere else";
                return JobResult.Running;
            }

            context.Animation.Reach();

            switch (Carrying.Deposit(context.Bag.GetInventory(), load, container))
            {
                case TakeResult.Took:
                    activity = "putting it away";
                    return JobResult.Running;

                case TakeResult.Waiting:
                    activity = "opening the chest";
                    return JobResult.Running;

                case TakeResult.Full:
                    // Full is not a failure of the job: something else may take it, so the
                    // destination is released and the trip re-chooses.
                    context.State.SetDestination(ZDOID.None);
                    activity = "that chest is full";
                    return JobResult.Running;

                default:
                    context.State.SetDestination(ZDOID.None);
                    activity = "that chest is gone";
                    return JobResult.Running;
            }
        }

        private static bool Within(HaulContext context, GameObject thing) =>
            thing != null &&
            Utils.DistanceXZ(thing.transform.position, context.Villager.transform.position) <= Reach;

        /// <summary>
        ///     Finds what an id refers to, distinguishing destroyed from merely not loaded.
        /// </summary>
        private static GameObject Resolve(ZDOID id, out bool lost)
        {
            lost = false;
            if (id.IsNone()) return null;

            ZDO zdo = ZDOMan.instance?.GetZDO(id);
            if (zdo == null || !zdo.IsValid())
            {
                lost = true;
                return null;
            }

            return ZNetScene.instance?.FindInstance(id);
        }
    }
}
