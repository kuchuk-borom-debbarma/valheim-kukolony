using System.Collections.Generic;
using Kukolony.Core;
using Kukolony.Gui;
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

        /// <summary>
        ///     The AI tick's own interval, for anything that moves by rate.
        /// </summary>
        /// <remarks>
        ///     Neither available clock is this interval: the render frame varies and the
        ///     physics step is two fifths of it. Left to default, a rescued villager covers
        ///     ground at frame rate rather than the AI rate - which matters more now that a
        ///     trip is given up on after a number of seconds, because a haul crossing would
        ///     be abandoned over water a chop crossing gets across.
        /// </remarks>
        internal float DeltaTime;
    }

    /// <summary>
    ///     The doing half of hauling. The deciding half is
    ///     <see cref="HaulTransitions" />, which has no Unity in it and is tested separately.
    /// </summary>
    internal static class HaulJob
    {
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
                tired: false,
                fillBagFirst: context.Job != null && context.Job.FillBagFirst);

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
                // Unless the job wants a full load, in which case another item bound for the
                // same chest is part of this trip rather than a new errand. Bound for the same
                // chest specifically: picking up whatever is nearest would quietly turn one
                // destination per trip into several, and that rule is what makes a claim have
                // an obvious owner.
                if (context.Job != null && context.Job.FillBagFirst &&
                    !state.Destination.IsNone() &&
                    context.Bag.GetInventory().HaveEmptySlot())
                {
                    StructureRecord bound = SettlementIndex.Find(context.Colony, state.Destination);
                    if (bound != null && Selection.TryFindGroundWork(context.Colony, context.Job,
                            context.Villager, out ItemDrop more, out StructureRecord _, bound) &&
                        more.TryGetComponent(out ZNetView reaching) && reaching.IsValid())
                    {
                        state.SetTarget(reaching.GetZDO().m_uid);
                        context.Walk.Forget();
                        activity = "fetching";
                        return JobResult.Running;
                    }
                }

                // The first deliverable thing in the load decides where this trip goes.
                // Everything else is delivered on a later pass, which keeps one destination per
                // trip, and an oddment nothing claims does not hold the rest of the load hostage.
                if (!Selection.FirstDeliverable(context.Colony, carried,
                        context.Villager.transform.position, context.Villager.Id,
                        out ItemDrop.ItemData _, out StructureRecord home))
                {
                    // Nothing it holds has anywhere to go. Put one down each pass rather than
                    // carrying them about: the bag is the villager's working space, and a load
                    // of oddments in it is a villager that can no longer haul.
                    //
                    // Told to the player, because this is a settlement problem rather than a
                    // villager one - somebody needs to build a chest that wants this, or mark one
                    // as the dump - and keyed by the item so two kinds of oddment are two
                    // complaints rather than one confusing tally.
                    string stranded = Carrying.NameOf(carried[0]);
                    Chatter.Say("nowhere to put " + stranded,
                        $"Nowhere to put {ItemCatalogue.Label(stranded)}; it was left on the ground.");

                    context.Animation.Reach();
                    Carrying.PutDown(context.Bag.GetInventory(), carried[0], context.Villager.transform.position);
                    return JobOutcomes.Skipped(context.State, "nowhere to put this, so I left it", out activity);
                }

                state.SetDestination(home.Id);
                context.Walk.Forget();
                activity = "carrying";
                return JobResult.Running;
            }

            // Ground first, decided per villager at the moment it picks up work rather than
            // across the whole settlement. A settlement with a permanent trickle of dropped
            // items would otherwise never reorganise itself at all, because there would always
            // be something on the floor somewhere.
            if (Selection.TryFindGroundWork(context.Colony, context.Job, context.Villager,
                    out ItemDrop item, out StructureRecord destination))
            {
                if (!item.TryGetComponent(out ZNetView view) || !view.IsValid())
                {
                    return JobOutcomes.Skipped(context.State, "nothing to haul", out activity);
                }

                // Taking the target is also taking the claim, so no other villager walks here.
                state.SetTarget(view.GetZDO().m_uid);
                state.SetDestination(destination.Id);
                context.Walk.Forget();
                                activity = "fetching";
                return JobResult.Running;
            }

            if (Selection.TryFindContainerWork(context.Colony, context.Job, context.Villager,
                    out GameObject chest, out StructureRecord shouldBe))
            {
                // The whole chest is claimed, not the stack inside it: one villager tidies one
                // container. Two villagers picking through the same chest is how a settlement
                // double-handles a stack, and the claim already works per object.
                if (!chest.TryGetComponent(out ZNetView holding) || !holding.IsValid())
                {
                    return JobOutcomes.Skipped(context.State, "nothing to haul", out activity);
                }

                state.SetTarget(holding.GetZDO().m_uid);
                state.SetDestination(shouldBe.Id);
                context.Walk.Forget();
                activity = "tidying up";
                return JobResult.Running;
            }

            return JobOutcomes.Skipped(context.State, "nothing to haul", out activity);
        }

        private static JobResult Walk(HaulContext context, GameObject target, string doing, out string activity)
        {
            if (target == null)
            {
                // Not loaded yet. Its zone may still be streaming in, but waiting forever is
                // how the previous system hung a villager, so this yields instead.
                //
                // Said out loud, coalesced, because two villagers standing still for thirty
                // seconds is indistinguishable from two villagers walking badly unless
                // something reports which of them this was. The ZDO's own view of where it is
                // matters: an object whose record is fine but which nothing instantiated is a
                // different fault from one whose zone genuinely has not arrived.
                ZDOID stalled = context.State.Target.IsNone()
                    ? context.State.Destination
                    : context.State.Target;
                ZDO record = stalled.IsNone() ? null : ZDOMan.instance?.GetZDO(stalled);
                if (record != null)
                {
                    Vector3 where = record.GetPosition();
                    var itsZone = ZoneSystem.GetZone(where);
                    var ourZone = ZoneSystem.GetZone(context.Villager.transform.position);
                    string prefab = ZNetScene.instance != null
                        ? ZNetScene.instance.GetPrefab(record.GetPrefab())?.name ?? "unknown"
                        : "?";

                    Chatter.Warn("[haul] unresolved target",
                        $"waiting for {stalled} ({prefab}): valid={record.IsValid()} " +
                        $"owner={record.GetOwner()} ours={record.IsOwner()} " +
                        $"at={where} itsZone={itsZone} ourZone={ourZone} " +
                        $"zoneLoaded={(ZoneSystem.instance != null && ZoneSystem.instance.IsZoneLoaded(where))} " +
                        $"distance={Utils.DistanceXZ(where, context.Villager.transform.position):0.0}m");
                }

                return JobOutcomes.Skipped(context.State, "waiting for the world", out activity);
            }

            // How close it can actually get depends on what it is walking to: a chest stops
            // the villager a good metre short of its own centre, and demanding the centre is
            // demanding a position inside the chest.
            switch (context.Walk.MoveTowards(target.transform.position, Approach.DistanceTo(target),
                deltaTime: context.DeltaTime))
            {
                case MoveResult.Moving:
                    // Bounded first. Walking is the one step that can go on reporting progress
                    // for ever, and the queue ignores Running entirely - so a villager that
                    // cannot reach a chest holds its job open and the rest of its queue never
                    // runs.
                    JobResult? stuck = JobOutcomes.GiveUpIfStuck(context.Villager, context.State,
                        context.Walk.TripStalledFor, Walked(target),
                        () => Named(context, target), out string gaveUp);
                    if (stuck.HasValue)
                    {
                        // Refused for a while, or Choose re-derives the same answer from the
                        // same world on the very next tick and the villager walks the same
                        // unreachable route for ever, a repetition at a time.
                        //
                        // The leg actually being walked, not the trip's recorded target. This
                        // method walks to a source and to a destination, and on the delivery
                        // leg the recorded target is either nothing at all - so the refusal
                        // was a no-op and the villager set straight off again - or, when
                        // tidying, the source chest, so failing to reach a destination
                        // refused a perfectly reachable chest somewhere else entirely.
                        Unreachable.Refuse(context.Villager.Id, Walked(target));
                        activity = gaveUp;
                        return stuck.Value;
                    }

                    // Still going, so the claim on what it is going to is still live. Without
                    // this the reservation ages against the length of the walk rather than
                    // against being stuck, and any errand longer than the timeout loses its
                    // claim halfway - which is two villagers converging on one log, arrived at
                    // by both of them behaving correctly.
                    context.State.TouchClaim();
                    activity = doing;
                    return JobResult.Running;

                case MoveResult.Arrived:
                    context.State.TouchClaim();
                    activity = doing;
                    return JobResult.Running;

                default:
                    // Refused as well as failed, but briefly. Without any refusal the trip is
                    // released, Choose re-derives the same answer from the same world, and the
                    // same failure lands on the next tick - forty failures a minute until the
                    // villager tires itself out and goes to bed. With the full refusal it is
                    // worse the other way: inside a settlement this verdict is reached after
                    // four seconds of no progress, so somebody standing in a doorway would
                    // blacklist a perfectly good chest for five minutes, and the villager
                    // would then report there is nowhere to put what it is carrying and drop
                    // it on the floor. Long enough to break the loop, short enough to forgive
                    // a doorway.
                    Unreachable.Refuse(context.Villager.Id, Walked(target),
                        Unreachable.BlockedForSeconds);

                    // Says how far short it stopped and what it was asked for. "Cannot get
                    // there" is the same sentence whether the target is unreachable, the stop
                    // distance is smaller than the thing being walked to, or the villager never
                    // moved at all - and those are three different bugs.
                    float gap = Utils.DistanceXZ(target.transform.position,
                        context.Villager.transform.position);
                    return JobOutcomes.Failed(context.State,
                        $"cannot get there (stopped {gap:0.0}m away, needed {Approach.DistanceTo(target):0.0}m, " +
                        $"{context.Villager.Explain(target.transform.position)})", out activity);
            }
        }

        /// <summary>
        ///     What to call the thing being walked to, preferring the player's own name.
        /// </summary>
        /// <remarks>
        ///     A structure the player renamed "Ore stash" should be reported as that rather
        ///     than as "Chest". Computed only once the bound has tripped, so looking it up
        ///     costs nothing on an ordinary walking tick.
        /// </remarks>
        private static string Named(HaulContext context, GameObject target)
        {
            StructureRecord record = SettlementIndex.Find(context.Colony, Walked(target));
            return record != null ? record.Name : StructureRegistry.DisplayName(target);
        }

        /// <summary>The id of the thing actually being walked to, or none.</summary>
        private static ZDOID Walked(GameObject target) =>
            target != null && target.TryGetComponent(out ZNetView view) && view.IsValid()
                ? view.GetZDO().m_uid
                : ZDOID.None;

        private static JobResult Collect(HaulContext context, GameObject source, out string activity)
        {
            // A source is either something lying on the ground or a container being tidied. The
            // state machine does not care which - it is about where the villager is, not what it
            // came for - so the only place the difference exists is here.
            if (source != null && source.GetComponentInChildren<Container>(true) != null)
            {
                return CollectFromContainer(context, source, out activity);
            }

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

        /// <summary>
        ///     Takes out of a container whatever belongs at this trip's destination.
        /// </summary>
        /// <remarks>
        ///     What to take is worked out here, at the chest, rather than remembered from when
        ///     the trip was chosen. The contents can change while the villager walks - another
        ///     villager, the player, a station drawing fuel - and a remembered item is a promise
        ///     the world never agreed to keep. Deciding on arrival also batches for free:
        ///     everything in this chest bound for that one leaves in the same visit.
        /// </remarks>
        private static JobResult CollectFromContainer(HaulContext context, GameObject source, out string activity)
        {
            Container container = source.GetComponentInChildren<Container>(true);
            StructureRecord from = SettlementIndex.Find(context.Colony, context.State.Target);
            StructureRecord to = SettlementIndex.Find(context.Colony, context.State.Destination);

            if (container == null || from == null || to == null)
            {
                context.State.ClearTarget();
                activity = "that chest is gone";
                return JobResult.Running;
            }

            ItemDrop.ItemData wanted = Selection.WhatToTakeFrom(context.Colony, context.Job, from, to,
                container.GetInventory());
            if (wanted == null)
            {
                // Nothing left here bound for where this trip is going. Before letting go of a
                // chest it is standing at and has already claimed, leave it in order - moving
                // the wrong things out is only half of organising.
                Tidying.Organise(container);

                // Releasing the chest ends the sweep and frees it for another villager; what is
                // already carried still gets delivered, because the trip keeps its destination.
                context.State.ClearTarget();
                activity = "that is sorted out";
                return JobResult.Running;
            }

            context.Animation.Reach();

            switch (Carrying.TakeFromContainer(container, wanted, context.Bag.GetInventory(), out string lifted))
            {
                case TakeResult.Took:
                    context.State.AddCargo(lifted);
                    activity = "tidying up";
                    return JobResult.Running;

                case TakeResult.Waiting:
                    activity = "opening the chest";
                    return JobResult.Running;

                case TakeResult.Full:
                    context.State.ClearTarget();
                    activity = "my bag is full";
                    return JobResult.Running;

                default:
                    context.State.ClearTarget();
                    activity = "that chest is gone";
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

            if (carried.Count == 0)
            {
                // The load is delivered and the villager is standing at the chest it filled.
                // Packing it now is free, and a chest that was just added to is exactly the one
                // most likely to have gained a split stack.
                Tidying.Organise(container);
                return JobOutcomes.Completed(context.State, "done hauling", out activity);
            }

            // What the trip is holding may not all belong here. Releasing the destination sends
            // it back to choosing rather than forcing flint into the wood shed, and is also how
            // a chest that filled up mid-trip is noticed.
            if (!Selection.FirstDeliverable(context.Colony, carried, context.Villager.transform.position,
                    context.Villager.Id, out ItemDrop.ItemData load, out StructureRecord belongs))
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

            // What this chest has room for under its own cap, so a delivery cannot overshoot
            // a limit nothing will later correct.
            int allowed = SettlementIndex.RoomUnderCap(belongs, Carrying.NameOf(load));

            switch (Carrying.Deposit(context.Bag.GetInventory(), load, container, allowed))
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

        /// <summary>
        ///     Whether the villager is close enough to work on this.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The same measure the walking used, from the same place. When arriving and
        ///         having arrived are two different numbers, a villager walks as far as it can,
        ///         is told it is not there yet, and tries again forever - which is precisely what
        ///         happened: <see cref="Arrival" /> would accept a villager stopped seven metres
        ///         from a chest while this still demanded five, so the trip never advanced and
        ///         the wood was carried about indefinitely.
        ///     </para>
        ///     <para>
        ///         Tight while the villager is still closing, so it walks right up to things in
        ///         the ordinary case, and as generous as <see cref="Arrival" /> once it has
        ///         stopped getting closer - because at that point this is as near as it goes.
        ///     </para>
        /// </remarks>
        private static bool Within(HaulContext context, GameObject thing)
        {
            if (thing == null) return false;

            float reach = context.Walk.StalledFor >= Arrival.SettledSeconds
                ? Arrival.WorkingReach
                : Approach.DistanceTo(thing);

            return Utils.DistanceXZ(thing.transform.position, context.Villager.transform.position) <= reach;
        }

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
