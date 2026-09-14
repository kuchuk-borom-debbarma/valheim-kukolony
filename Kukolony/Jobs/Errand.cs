using System;
using Kukolony.Colonies;
using Kukolony.Core;
using Kukolony.Gui;
using Kukolony.Villagers;
using Kukolony.Villagers.Navigation;
using UnityEngine;

namespace Kukolony.Jobs
{
    /// <summary>
    ///     Going and getting something the work needs.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>A rule rather than a job.</b> Fetching is not work somebody queues - it is what
    ///         anybody does before starting, and it belongs to every job that needs a thing in
    ///         hand. Written as a job it would need a queue entry per thing, and a villager would
    ///         report "no pickaxe" while walking to fetch a pickaxe, which reads as broken.
    ///     </para>
    ///     <para>
    ///         <b>Run before the state machine, not inside it.</b> Every table that needs
    ///         something already yields when it has none, and that answer stays right when the
    ///         settlement has none either. This is asked first and answers <c>null</c> when there
    ///         is nothing to fetch, so the table goes on to say what it always said.
    ///     </para>
    ///     <para>
    ///         <b>And it keeps no state.</b> The chest is found again on every tick rather than
    ///         remembered, because the alternative is a third set of target fields living beside
    ///         the job's own and going stale in ways only this errand would know about. The search
    ///         is over the settlement's cached snapshot, and the walk re-aims at a point that does
    ///         not move - so re-deciding costs a lookup and buys correctness for free when
    ///         somebody else takes the axe first.
    ///     </para>
    ///     <para>
    ///         <b>What is being fetched is a predicate, not a kind.</b> This started as the tool
    ///         errand and everything in it was general except one call. Sowing needs the same walk
    ///         for a seed, and a second copy would be two places to fix the day the settlement
    ///         learns to lock a chest.
    ///     </para>
    /// </remarks>
    internal static class Errand
    {
        /// <summary>
        ///     Fetches the first thing a chest yields to this test, or answers null when the
        ///     settlement has none.
        /// </summary>
        /// <param name="wanted">
        ///     Picks the thing out of an inventory, or answers null. Asked of the chest and of the
        ///     bag by the caller, which is what keeps the two from disagreeing: anything this
        ///     fetches must be something the job will then find.
        /// </param>
        /// <param name="what">What to call it while walking - "an axe", "carrot seeds".</param>
        /// <returns>
        ///     Null when there is nothing to fetch, which is not a failure - it is the state the
        ///     job's own "no axe" answer already describes.
        /// </returns>
        internal static JobResult? Fetch(Villager villager, Colony colony, Container bag,
            VillagerWalk walk, VillagerState state, float deltaTime,
            Func<Inventory, ItemDrop.ItemData> wanted, string what, out string activity)
        {
            activity = string.Empty;
            if (villager == null || colony == null || bag == null || walk == null || wanted == null)
            {
                return null;
            }

            Vector3 here = villager.transform.position;
            StructureRecord holding = Where(colony, villager, wanted, here);
            if (holding == null) return null;

            GameObject chest = ZNetScene.instance != null
                ? ZNetScene.instance.FindInstance(holding.Id)
                : null;

            // Registered, holding one, and not loaded. Nothing to walk to yet, and the job's own
            // answer is the honest one until its zone comes in.
            if (chest == null) return null;

            if (Utils.DistanceXZ(chest.transform.position, here) > Approach.ToStructure)
            {
                switch (walk.MoveTowards(chest.transform.position, Approach.ToStructure,
                            deltaTime: deltaTime))
                {
                    case MoveResult.PathFailed:
                        // Briefly refused, as every other walk here refuses: a chest that cannot
                        // be reached now is often reachable once a door opens.
                        Unreachable.Refuse(villager.Id, holding.Id, Unreachable.BlockedForSeconds);
                        return JobOutcomes.Skipped(state, $"cannot get to {what}", out activity);

                    default:
                        return JobOutcomes.Running($"fetching {what}", out activity);
                }
            }

            walk.Forget();

            Container container = chest.GetComponentInChildren<Container>(true);
            ItemDrop.ItemData thing = container != null ? wanted(container.GetInventory()) : null;

            if (thing == null)
            {
                // Taken by somebody else between choosing and arriving. Not a failure; the next
                // tick looks again and finds the next chest, or finds none and the job says so.
                return JobOutcomes.Running($"looking for {what}", out activity);
            }

            switch (Carrying.TakeFromContainer(container, thing, bag.GetInventory(), out string taken))
            {
                case TakeResult.Took:
                    VillagerInventory.Persist(bag, Record(villager));
                    Report.Say($"{state.Name} took {ItemCatalogue.Label(taken)} from {holding.Name}.");
                    return JobOutcomes.Running($"took {what}", out activity);

                case TakeResult.Waiting:
                    return JobOutcomes.Running("opening the chest", out activity);

                case TakeResult.Full:
                    // A bag with no room is a bag that needs emptying, which is hauling's
                    // business rather than this errand's.
                    return JobOutcomes.Skipped(state, $"no room for {what}", out activity);

                default:
                    return JobOutcomes.Running($"looking for {what}", out activity);
            }
        }

        /// <summary>
        ///     The nearest chest the settlement will let this villager take from.
        /// </summary>
        /// <remarks>
        ///     Through <see cref="SettlementIndex.WhatMayBeTidied" />, which is already the list
        ///     of containers the settlement may take from - so a chest switched to "villagers may
        ///     not use what is here" keeps its contents, which is exactly what a player means by
        ///     putting their own axe in it.
        /// </remarks>
        private static StructureRecord Where(Colony colony, Villager villager,
            Func<Inventory, ItemDrop.ItemData> wanted, Vector3 here)
        {
            StructureRecord nearest = null;
            float closest = float.MaxValue;

            foreach (StructureRecord record in SettlementIndex.WhatMayBeTidied(colony))
            {
                if (Unreachable.Refuses(villager.Id, record.Id)) continue;

                Inventory inside = StructureInventory.Live(record.Id);
                if (inside == null) continue;
                if (wanted(inside) == null) continue;

                ZDO zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(record.Id) : null;
                if (zdo == null || !zdo.IsValid()) continue;

                float distance = Utils.DistanceXZ(zdo.GetPosition(), here);
                if (distance >= closest) continue;

                closest = distance;
                nearest = record;
            }

            return nearest;
        }

        private static ZNetView Record(Villager villager) =>
            villager != null && villager.TryGetComponent(out ZNetView view) && view.IsValid()
                ? view
                : null;
    }
}
