using System;
using System.Collections.Generic;
using Kukolony.Colonies;
using Kukolony.Villagers;
using UnityEngine;

namespace Kukolony.Jobs
{
    /// <summary>
    ///     Concrete, ownership-safe execution for the built-in colony job catalogue.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The engine is a synchronous fixed-tick state machine with two modes, both driven
    ///         entirely from persisted villager state so a reload resumes mid-job. With no
    ///         <c>StepTarget</c> set it is <em>selecting</em>: it picks a loose item or a
    ///         registered structure and records the choice. With a target set it is
    ///         <em>executing</em>: move first, then act according to <c>RuntimePhase</c>
    ///         (<c>pickup</c>, <c>acquiring</c>, <c>depositing</c>, or operate).
    ///     </para>
    ///     <para>
    ///         <c>QueueProgress</c> is multiplexed as a per-job sub-state, not a percentage:
    ///         <c>0</c> fresh; <c>1</c> the station wants input, so the next tick fetches from a
    ///         source before returning; <c>2</c> beehive honey is in the bag and needs
    ///         depositing; <c>100..140</c> beehive extraction fired and the engine is waiting
    ///         for the honey drop to spawn, incrementing as a bounded retry so a failed
    ///         extraction gives up instead of looping.
    ///     </para>
    ///     <para>
    ///         No executor writes raw internal station ZDO keys. Station changes go through
    ///         probe-verified vanilla RPCs; container changes claim ownership, mutate via
    ///         Inventory, and persist via Container.
    ///     </para>
    /// </remarks>
    internal static class ColonyJobEngine
    {
        // Acceptance hooks exercise the same ownership-safe primitives without waiting
        // for pathfinding. They are internal and are only called by the config-gated
        // in-game harness.
        internal static JobResult TestPickup(GameObject target, Inventory bag, VillagerState state, out string activity) =>
            PickupLoose(target, bag, state, out activity);
        internal static JobResult TestAcquire(GameObject target, Inventory bag, ColonyJobConfig job, VillagerState state, out string activity) =>
            Acquire(target, bag, job, state, out activity);
        internal static JobResult TestDeposit(GameObject target, Inventory bag, ColonyJobConfig job, VillagerState state, out string activity) =>
            Deposit(target, bag, job, state, out activity);
        internal static JobResult TestOperate(GameObject target, Inventory bag, ColonyJobConfig job, VillagerState state, out string activity) =>
            Operate(target, bag, job, state, out activity);
        internal static bool TestLimitReached(Colony colony, ColonyJobConfig job) => LimitReached(colony, job);
        /// <summary>Which station protocol a target resolves to, or empty for none.</summary>
        internal static string TestResolveProtocol(GameObject target, Colonies.StructureCapability declared)
        {
            Stations.IStationProtocol protocol = Stations.StationProtocols.Resolve(target, declared);
            return protocol == null ? string.Empty : protocol.GetType().Name;
        }

        /// <summary>
        ///     Advances one job by one tick and reports the player-visible activity string.
        ///     Returns <see cref="JobResult.Skipped"/> rather than blocking when the
        ///     destination stock limit is already satisfied, so a saturated job yields the
        ///     villager to the next queue entry instead of spinning.
        /// </summary>
        internal static JobResult Tick(Villager villager, MonsterAI ai, Container bag, Colony colony,
            ColonyJobConfig job, out string activity)
        {
            activity = ColonyJobCatalog.DisplayName(job.Type);
            VillagerState state = villager.State;
            Inventory inventory = bag != null ? bag.GetInventory() : null;
            if (inventory == null) return JobResult.Failed;

            if (!state.StepTarget.IsNone())
            {
                return TickTarget(villager, ai, inventory, colony, job, out activity);
            }

            if (LimitReached(colony, job))
            {
                activity = "skipping: stock limit reached";
                return JobResult.Skipped;
            }

            bool carrying = FirstMatching(inventory, job.ItemFilters) != null;
            switch (job.Type)
            {
                case ColonyJobType.HaulLoose:
                    return carrying
                        ? (!job.Destination.IsNone()
                            ? SelectExplicitContainer(villager, colony, job.Destination, "depositing", out activity)
                            : SelectStructure(villager, colony, job, StructureCapability.Container, "depositing", out activity))
                        : SelectLooseItem(villager, colony, job, "pickup", out activity);
                case ColonyJobType.Transfer:
                    return SelectExplicitContainer(villager, colony, carrying ? job.Destination : job.Source,
                        carrying ? "depositing" : "acquiring", out activity);
                case ColonyJobType.CollectBeehives:
                    if (state.QueueProgress == 2)
                        return !job.Destination.IsNone()
                            ? SelectExplicitContainer(villager, colony, job.Destination, "depositing", out activity)
                            : SelectStructure(villager, colony, job, StructureCapability.Container, "depositing", out activity);
                    if (state.QueueProgress >= 100)
                    {
                        JobResult loose = SelectLooseItem(villager, colony, job, "pickup-collect", out activity);
                        if (loose == JobResult.Skipped && state.QueueProgress < 140)
                        {
                            state.SetQueueProgress(state.QueueProgress + 1);
                            activity = "waiting for extracted honey";
                            return JobResult.Running;
                        }
                        return loose;
                    }
                    return SelectStructure(villager, colony, job, StructureCapability.BeeHive, "operating", out activity);
                case ColonyJobType.OperateCookingStations:
                case ColonyJobType.OperateFermenters:
                    if (carrying || state.QueueProgress > 0)
                    {
                        if (!carrying)
                            return SelectSource(villager, colony, job, out activity);
                    }
                    return SelectStructure(villager, colony, job,
                        ColonyJobCatalog.RequiredCapability(job.Type), "operating", out activity);
                default:
                    if (!carrying)
                    {
                        JobResult source = SelectSource(villager, colony, job, out activity);
                        if (source != JobResult.Skipped) return source;
                    }
                    return SelectStructure(villager, colony, job,
                        ColonyJobCatalog.RequiredCapability(job.Type), "operating", out activity);
            }
        }

        /// <summary>
        ///     Executing mode: resolve the recorded target, walk to it, then dispatch on
        ///     <c>RuntimePhase</c>. A target whose ZDO is gone fails the job, but one that is
        ///     merely unloaded keeps the job running — the zone may still stream in, and
        ///     discarding the target would lose committed progress.
        /// </summary>
        private static JobResult TickTarget(Villager villager, MonsterAI ai, Inventory bag,
            Colony colony, ColonyJobConfig job, out string activity)
        {
            VillagerState state = villager.State;
            ZDO zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(state.StepTarget) : null;
            if (zdo == null || !zdo.IsValid())
            {
                state.ResetRuntime();
                activity = "target missing";
                return JobResult.Failed;
            }

            GameObject target = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(state.StepTarget) : null;
            if (target == null)
            {
                activity = "waiting for target to load";
                return JobResult.Running;
            }

            MoveResult movement = VillagerMovement.MoveTowards(ai, target.transform.position,
                Mathf.Max(.5f, job.StopDistance));
            if (movement == MoveResult.Moving)
            {
                activity = "walking to " + TargetName(target);
                return JobResult.Running;
            }
            if (movement == MoveResult.PathFailed)
            {
                state.ResetRuntime();
                activity = "path failed";
                return JobResult.Failed;
            }

            switch (state.RuntimePhase)
            {
                case "pickup": return PickupLoose(target, bag, state, out activity);
                case "acquiring": return Acquire(target, bag, job, state, out activity);
                case "depositing": return Deposit(target, bag, job, state, out activity);
                default: return Operate(target, bag, job, state, out activity);
            }
        }

        private static JobResult SelectLooseItem(Villager villager, Colony colony,
            ColonyJobConfig job, string phase, out string activity)
        {
            ItemDrop closest = null;
            float best = float.MaxValue;
            foreach (ItemDrop drop in ItemDrop.s_instances)
            {
                if (drop == null || !drop.TryGetComponent(out ZNetView view) || !view.IsValid()) continue;
                if (!Matches(Utils.GetPrefabName(drop.gameObject), job.ItemFilters)) continue;
                float distance = Utils.DistanceXZ(drop.transform.position, colony.transform.position);
                if (distance > job.SearchRadius || distance >= best) continue;
                if (job.Reservations && TargetClaims.IsClaimedByOther(view.GetZDO().m_uid, villager)) continue;
                closest = drop; best = distance;
            }
            if (closest == null) { activity = "skipping: no loose item"; return JobResult.Skipped; }
            SetTarget(villager.State, closest.GetComponent<ZNetView>().GetZDO().m_uid, phase);
            activity = "found " + Utils.GetPrefabName(closest.gameObject);
            return JobResult.Running;
        }

        private static JobResult SelectSource(Villager villager, Colony colony,
            ColonyJobConfig job, out string activity)
        {
            if (!job.Source.IsNone())
                return SelectExplicitContainer(villager, colony, job.Source, "acquiring", out activity);
            foreach (StructureRecord record in colony.State.GetStructures())
            {
                if ((record.Capabilities & StructureCapability.Container) == 0 || !record.IsLiveIn(colony)) continue;
                bool selected = job.SelectedStructures.Contains(record.Id);
                if (job.Targets == TargetMode.Selected && !selected) continue;
                if (job.Targets == TargetMode.Ignore && selected) continue;
                GameObject instance = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(record.Id) : null;
                if (instance == null || !instance.TryGetComponent(out Container container) ||
                    FirstMatching(container.GetInventory(), job.ItemFilters) == null) continue;
                if (job.Reservations && TargetClaims.IsClaimedByOther(record.Id, villager)) continue;
                SetTarget(villager.State, record.Id, "acquiring");
                activity = "acquiring from " + record.Name;
                return JobResult.Running;
            }
            activity = "skipping: no source item";
            return JobResult.Skipped;
        }

        private static JobResult SelectExplicitContainer(Villager villager, Colony colony,
            ZDOID id, string phase, out string activity)
        {
            StructureRecord record = colony.State.GetStructures().Find(candidate => candidate.Id == id);
            if (record == null || !record.IsLiveIn(colony) ||
                (record.Capabilities & StructureCapability.Container) == 0)
            {
                activity = "skipping: container unavailable";
                return JobResult.Skipped;
            }
            SetTarget(villager.State, id, phase);
            activity = phase;
            return JobResult.Running;
        }

        private static JobResult SelectStructure(Villager villager, Colony colony, ColonyJobConfig job,
            StructureCapability capability, string phase, out string activity)
        {
            foreach (StructureRecord record in colony.State.GetStructures())
            {
                if ((record.Capabilities & capability) == 0 || !record.IsLiveIn(colony)) continue;
                bool selected = job.SelectedStructures.Contains(record.Id);
                if (job.Targets == TargetMode.Selected && !selected) continue;
                if (job.Targets == TargetMode.Ignore && selected) continue;
                if (job.Reservations && TargetClaims.IsClaimedByOther(record.Id, villager)) continue;
                SetTarget(villager.State, record.Id, phase);
                activity = phase + " " + record.Name;
                return JobResult.Running;
            }
            activity = "skipping: no eligible target";
            return JobResult.Skipped;
        }

        private static JobResult PickupLoose(GameObject target, Inventory bag,
            VillagerState state, out string activity)
        {
            if (!target.TryGetComponent(out ItemDrop drop) || !target.TryGetComponent(out ZNetView view))
            {
                state.ResetRuntime(); activity = "pickup target invalid"; return JobResult.Failed;
            }
            if (!view.IsOwner()) { drop.RequestOwn(); activity = "claiming loose item"; return JobResult.Running; }
            if (!bag.CanAddItem(drop.m_itemData)) { state.ResetRuntime(); activity = "bag full"; return JobResult.Failed; }
            bool collecting = state.RuntimePhase == "pickup-collect";
            bag.AddItem(drop.m_itemData);
            ZNetScene.instance.Destroy(target);
            state.ResetRuntime();
            if (collecting) state.SetQueueProgress(2);
            activity = "picked up item";
            return JobResult.Running;
        }

        private static JobResult Acquire(GameObject target, Inventory bag, ColonyJobConfig job,
            VillagerState state, out string activity)
        {
            if (!TryOwnedContainer(target, out Container container, out JobResult ownership))
            {
                activity = ownership == JobResult.Running ? "claiming source" : "source invalid";
                return ownership;
            }
            Inventory source = container.GetInventory();
            ItemDrop.ItemData item = FirstMatching(source, job.ItemFilters);
            if (item == null) { state.ResetRuntime(); activity = "source has no matching item"; return JobResult.Skipped; }
            if (!bag.CanAddItem(item, 1)) { state.ResetRuntime(); activity = "bag full"; return JobResult.Failed; }
            if (!MoveOne(bag, source, item))
            {
                state.ResetRuntime();
                activity = "source transfer rejected";
                return JobResult.Failed;
            }
            container.Save();
            state.ResetRuntime();
            activity = "acquired item";
            return JobResult.Running;
        }

        private static JobResult Deposit(GameObject target, Inventory bag, ColonyJobConfig job,
            VillagerState state, out string activity)
        {
            if (!TryOwnedContainer(target, out Container container, out JobResult ownership))
            {
                activity = ownership == JobResult.Running ? "claiming destination" : "destination invalid";
                return ownership;
            }
            Inventory destination = container.GetInventory();
            ItemDrop.ItemData item = FirstMatching(bag, job.ItemFilters);
            if (item == null) { state.ResetRuntime(); activity = "nothing to deposit"; return JobResult.Failed; }
            if (!destination.CanAddItem(item, 1)) { state.ResetRuntime(); activity = "destination full"; return JobResult.Failed; }
            destination.MoveItemToThis(bag, item);
            container.Save();
            state.ResetRuntime();
            activity = "deposited item";
            return JobResult.Completed;
        }

        /// <summary>
        ///     Performs one step of work against a station, delegating to the protocol that
        ///     matches the target. The job's type no longer picks the protocol: what the
        ///     station is does, which is why supporting a new one needs no change here.
        /// </summary>
        private static JobResult Operate(GameObject target, Inventory bag, ColonyJobConfig job,
            VillagerState state, out string activity)
        {
            if (target == null || !target.TryGetComponent(out ZNetView view) || !view.IsValid())
            {
                state.ResetRuntime(); activity = "station invalid"; return JobResult.Failed;
            }
            if (!view.IsOwner()) { view.ClaimOwnership(); activity = "claiming station"; return JobResult.Running; }

            Stations.IStationProtocol protocol =
                Stations.StationProtocols.Resolve(target, ColonyJobCatalog.RequiredCapability(job.Type));
            if (protocol == null) return JobOutcomes.Failed(state, "unsupported operation", out activity);

            return protocol.Operate(
                new Stations.StationContext(target, view, bag, FirstMatching(bag, job.ItemFilters), state),
                out activity);
        }

        /// <summary>
        ///     True when the destination already holds at least the configured stock limit.
        ///     The limit is a threshold to maintain, not an amount to move, so work resumes
        ///     on its own once stock drops below it.
        /// </summary>
        private static bool LimitReached(Colony colony, ColonyJobConfig job)
        {
            if (job.StockLimit <= 0 || job.Destination.IsNone()) return false;
            GameObject target = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(job.Destination) : null;
            if (target == null || !target.TryGetComponent(out Container container)) return false;
            int count = 0;
            foreach (ItemDrop.ItemData item in container.GetInventory().GetAllItems())
                if (Matches(Utils.GetPrefabName(item.m_dropPrefab), job.ItemFilters)) count += item.m_stack;
            return count >= job.StockLimit;
        }

        /// <summary>
        ///     Resolves a container the villager may safely write to, requesting ownership when
        ///     another peer holds it. Every container mutation goes through this gate.
        /// </summary>
        private static bool TryOwnedContainer(GameObject target, out Container container, out JobResult result)
        {
            container = null;
            if (target == null || !target.TryGetComponent(out container) ||
                !target.TryGetComponent(out ZNetView view) || !view.IsValid())
            { result = JobResult.Failed; return false; }
            if (!view.IsOwner()) { view.ClaimOwnership(); result = JobResult.Running; return false; }
            result = JobResult.Completed; return true;
        }

        private static ItemDrop.ItemData FirstMatching(Inventory inventory, List<string> filters)
        {
            if (inventory == null) return null;
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
                if (item != null && Matches(Utils.GetPrefabName(item.m_dropPrefab), filters)) return item;
            return null;
        }

        private static bool Matches(string prefab, List<string> filters) =>
            filters.Count == 0 || filters.Contains(prefab);

        private static bool Consume(Inventory inventory, ItemDrop.ItemData item) =>
            item != null && inventory.RemoveItem(item, 1);

        private static bool MoveOne(Inventory destination, Inventory source, ItemDrop.ItemData item)
        {
            // The amount overload requires a real grid coordinate in current Valheim;
            // (-1,-1) is rejected even after CanAddItem succeeds. Prefer an existing
            // compatible stack, then an empty slot, and let the vanilla helper perform
            // both inventories' bookkeeping.
            for (int y = 0; y < destination.GetHeight(); y++)
                for (int x = 0; x < destination.GetWidth(); x++)
                {
                    ItemDrop.ItemData existing = destination.GetItemAt(x, y);
                    if (existing != null && Utils.GetPrefabName(existing.m_dropPrefab) == Utils.GetPrefabName(item.m_dropPrefab) &&
                        destination.MoveItemToThis(source, item, 1, x, y)) return true;
                }
            for (int y = 0; y < destination.GetHeight(); y++)
                for (int x = 0; x < destination.GetWidth(); x++)
                    if (destination.GetItemAt(x, y) == null && destination.MoveItemToThis(source, item, 1, x, y)) return true;
            return false;
        }

        private static void SetTarget(VillagerState state, ZDOID id, string phase)
        {
            state.SetStepTarget(id);
            state.SetRuntimePhase(phase);
        }

        private static string TargetName(GameObject target) =>
            target != null ? Utils.GetPrefabName(target) : "target";

        /// <summary>Clears runtime state and yields without consuming a queue attempt.</summary>
        private static JobResult FinishSkipped(VillagerState state, string message, out string activity) =>
            JobOutcomes.Skipped(state, message, out activity);

        /// <summary>Clears runtime state and consumes a queue attempt, bounding retries.</summary>
        private static JobResult FinishFailed(VillagerState state, string message, out string activity) =>
            JobOutcomes.Failed(state, message, out activity);

        /// <summary>
        ///     Releases the station and records sub-state <c>1</c> so the next tick fetches the
        ///     missing input from a source before coming back. Stays <c>Running</c>: the job is
        ///     making progress, it just needs materials first.
        /// </summary>
        private static JobResult NeedInput(VillagerState state, string message, out string activity) =>
            JobOutcomes.NeedInput(state, message, out activity);
    }
}
