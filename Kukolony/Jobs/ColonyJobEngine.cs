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
            Acquire(target, bag, job.ItemFilters, state, out activity);
        internal static JobResult TestDeposit(GameObject target, Inventory bag, ColonyJobConfig job, VillagerState state, out string activity) =>
            Deposit(target, bag, job.ItemFilters, state, out activity);
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

            if (WalksPieces(job)) return TickWalker(villager, ai, inventory, colony, job, out activity);

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
                            : SelectStructure(villager, colony, job, null, StructureCapability.Container, "depositing", out activity))
                        : SelectLooseItem(villager, colony, job, null, "pickup", out activity);
                case ColonyJobType.Transfer:
                    return SelectExplicitContainer(villager, colony, carrying ? job.Destination : job.Source,
                        carrying ? "depositing" : "acquiring", out activity);
                case ColonyJobType.CollectBeehives:
                    if (state.QueueProgress == 2)
                        return !job.Destination.IsNone()
                            ? SelectExplicitContainer(villager, colony, job.Destination, "depositing", out activity)
                            : SelectStructure(villager, colony, job, null, StructureCapability.Container, "depositing", out activity);
                    if (state.QueueProgress >= 100)
                    {
                        JobResult loose = SelectLooseItem(villager, colony, job, null, "pickup-collect", out activity);
                        if (loose == JobResult.Skipped && state.QueueProgress < 140)
                        {
                            state.SetQueueProgress(state.QueueProgress + 1);
                            activity = "waiting for extracted honey";
                            return JobResult.Running;
                        }
                        return loose;
                    }
                    return SelectStructure(villager, colony, job, null, StructureCapability.BeeHive, "operating", out activity);
                case ColonyJobType.OperateCookingStations:
                case ColonyJobType.OperateFermenters:
                    if (carrying || state.QueueProgress > 0)
                    {
                        if (!carrying)
                            return SelectSource(villager, colony, job, null, out activity);
                    }
                    return SelectStructure(villager, colony, job, null,
                        ColonyJobCatalog.RequiredCapability(job.Type), "operating", out activity);
                default:
                    if (!carrying)
                    {
                        JobResult source = SelectSource(villager, colony, job, null, out activity);
                        if (source != JobResult.Skipped) return source;
                    }
                    return SelectStructure(villager, colony, job, null,
                        ColonyJobCatalog.RequiredCapability(job.Type), "operating", out activity);
            }
        }

        /// <summary>
        ///     Executing mode: resolve the recorded target, walk to it, then dispatch on
        ///     <c>RuntimePhase</c>. A target whose ZDO is gone fails the job, but one that is
        ///     merely unloaded keeps the job running — the zone may still stream in, and
        ///     discarding the target would lose committed progress.
        /// </summary>
        /// <summary>
        ///     Job types whose execution has moved to the piece walker. They are migrated one
        ///     at a time so each replacement is proven in-game before the executor it replaces
        ///     is removed.
        /// </summary>
        private static bool WalksPieces(ColonyJobConfig job) =>
            job.Type != ColonyJobType.CollectBeehives;

        /// <summary>
        ///     Runs a job by walking its pieces rather than by switching on its type.
        /// </summary>
        /// <remarks>
        ///     The decision of which step to take is made by <see cref="JobWalker"/>, which is
        ///     pure and tested without a world. This method only performs the chosen step,
        ///     reusing the same selectors and executors the type-driven path uses, and records
        ///     where the villager got to.
        /// </remarks>
        private static JobResult TickWalker(Villager villager, MonsterAI ai, Inventory bag,
            Colony colony, ColonyJobConfig job, out string activity)
        {
            VillagerState state = villager.State;
            List<JobPieceKind> kinds = job.Pieces.ConvertAll(piece => piece.Kind);
            GameObject target = ResolveTarget(state, out bool targetLost);
            if (targetLost)
            {
                state.ResetJob();
                activity = "target missing";
                return JobResult.Failed;
            }

            JobStep step = JobWalker.Next(kinds, state.StepCursor, new JobFacts(
                hasTarget: !state.StepTarget.IsNone(),
                arrivedAtTarget: false,
                // Carrying is a question about the job, not one step: any item any piece
                // wants counts, or the fetch pieces would look unsatisfied forever.
                carrying: FirstMatching(bag, PieceSettings.AllFilters(job)) != null,
                stockLimitReached: LimitReached(colony, job)));

            int next = step.Cursor + 1;
            switch (step.Action)
            {
                case StepAction.StopAtLimit:
                    return JobOutcomes.Skipped(state, "stock limit reached", out activity);

                case StepAction.CompleteCycle:
                    return JobOutcomes.Completed(state, "job cycle complete", out activity);

                // The world stopped matching the pipeline, usually a target taken by someone
                // else. Begin again rather than fail: the work itself is still valid.
                case StepAction.Restart:
                    return JobOutcomes.Skipped(state, "restarting", out activity);

                case StepAction.Invalid:
                    return JobOutcomes.Skipped(state, "pipeline cannot run", out activity);

                case StepAction.FindLooseItem:
                    return Advance(state, next, SelectLooseItem(villager, colony, job, PieceAt(job, step.Cursor), "pickup", out activity));

                case StepAction.SelectSource:
                    return Advance(state, next, SelectSource(villager, colony, job, PieceAt(job, step.Cursor), out activity));

                case StepAction.SelectTarget:
                    return Advance(state, next, SelectDestination(villager, colony, job, step.Cursor, out activity));

                case StepAction.Move:
                    return Walk(villager, ai, state, PieceSettings.StopDistance(job, PieceAt(job, step.Cursor)),
                        target, next, out activity);
            }

            // Everything below acts on the target, so a target that has not loaded yet is a
            // wait rather than a failure: its zone may still be streaming in.
            if (target == null)
            {
                activity = "waiting for target to load";
                return JobResult.Running;
            }
            switch (step.Action)
            {
                case StepAction.PickUp: return Advance(state, next, PickupLoose(target, bag, state, out activity));
                case StepAction.TakeItem:
                    return Advance(state, next, Acquire(target, bag, StepFilters(job, step.Cursor), state, out activity));
                case StepAction.PutItem:
                    return Advance(state, next, Deposit(target, bag, StepFilters(job, step.Cursor), state, out activity));
                case StepAction.OperateStation: return Advance(state, next, Operate(target, bag, job, state, out activity));
                default: return JobOutcomes.Skipped(state, "pipeline cannot run", out activity);
            }
        }

        /// <summary>
        ///     Records the next piece when a step made progress.
        /// </summary>
        /// <remarks>
        ///     A leaf executor reports Completed to mean its own step finished, which is not
        ///     the same as the job being done. Reported upwards unchanged it would end the
        ///     cycle wherever the last executor happened to sit, so anything after that piece
        ///     could never run - a deposit would end a pipeline and leave End unreached. The
        ///     step becomes Running and only the End piece completes the cycle, which also
        ///     makes one cycle cost exactly one queue attempt.
        ///
        ///     The cursor is written after the call because the executors clear the whole
        ///     cycle on their way out.
        /// </remarks>
        private static JobResult Advance(VillagerState state, int next, JobResult result)
        {
            if (result != JobResult.Running && result != JobResult.Completed) return result;
            state.SetStepCursor(next);
            return JobResult.Running;
        }

        /// <summary>Items the piece at this cursor works with, falling back to the job's.</summary>
        private static List<string> StepFilters(ColonyJobConfig job, int cursor) =>
            PieceSettings.Filters(job, PieceAt(job, cursor));

        /// <summary>The piece at this cursor, or null when the job has none there.</summary>
        private static JobPiece PieceAt(ColonyJobConfig job, int cursor) =>
            cursor >= 0 && cursor < job.Pieces.Count ? job.Pieces[cursor] : null;

        /// <summary>Walks to the chosen target, advancing only once the villager arrives.</summary>
        private static JobResult Walk(Villager villager, MonsterAI ai, VillagerState state,
            float stopDistance, GameObject target, int next, out string activity)
        {
            if (target == null)
            {
                activity = "waiting for target to load";
                return JobResult.Running;
            }
            MoveResult movement = VillagerMovement.MoveTowards(ai, target.transform.position,
                Mathf.Max(.5f, stopDistance));
            if (movement == MoveResult.Moving)
            {
                activity = "walking to " + TargetName(target);
                return JobResult.Running;
            }
            if (movement == MoveResult.PathFailed)
            {
                state.ResetJob();
                activity = "path failed";
                return JobResult.Failed;
            }
            state.SetStepCursor(next);
            activity = "arrived at " + TargetName(target);
            return JobResult.Running;
        }

        /// <summary>
        ///     Chooses where to put things. An explicit destination wins; otherwise the piece's
        ///     own declared capability picks among the colony's registered structures. This is
        ///     the first time a piece's capability decides anything at runtime.
        /// </summary>
        private static JobResult SelectDestination(Villager villager, Colony colony, ColonyJobConfig job,
            int cursor, out string activity)
        {
            JobPiece piece = cursor >= 0 && cursor < job.Pieces.Count ? job.Pieces[cursor] : null;
            ZDOID destination = PieceSettings.Container(job, piece, job.Destination);
            if (!destination.IsNone())
                return SelectExplicitContainer(villager, colony, destination, "depositing", out activity);
            StructureCapability capability = piece != null ? piece.Capability : StructureCapability.None;
            if (capability == StructureCapability.None) capability = StructureCapability.Container;
            return SelectStructure(villager, colony, job, piece, capability, "depositing", out activity);
        }

        /// <summary>
        ///     The villager's current target, and whether it is gone for good. A target whose
        ///     ZDO no longer exists is lost; one that is merely unloaded returns null and is
        ///     worth waiting for.
        /// </summary>
        private static GameObject ResolveTarget(VillagerState state, out bool lost)
        {
            lost = false;
            if (state.StepTarget.IsNone()) return null;
            ZDO zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(state.StepTarget) : null;
            if (zdo == null || !zdo.IsValid()) { lost = true; return null; }
            return ZNetScene.instance != null ? ZNetScene.instance.FindInstance(state.StepTarget) : null;
        }

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
                case "acquiring": return Acquire(target, bag, job.ItemFilters, state, out activity);
                case "depositing": return Deposit(target, bag, job.ItemFilters, state, out activity);
                default: return Operate(target, bag, job, state, out activity);
            }
        }

        private static JobResult SelectLooseItem(Villager villager, Colony colony,
            ColonyJobConfig job, JobPiece piece, string phase, out string activity)
        {
            List<string> filters = PieceSettings.Filters(job, piece);
            float radius = PieceSettings.SearchRadius(job, piece);
            bool reserve = PieceSettings.Reservations(job, piece);
            ItemDrop closest = null;
            float best = float.MaxValue;
            foreach (ItemDrop drop in ItemDrop.s_instances)
            {
                if (drop == null || !drop.TryGetComponent(out ZNetView view) || !view.IsValid()) continue;
                if (!Matches(Utils.GetPrefabName(drop.gameObject), filters)) continue;
                float distance = Utils.DistanceXZ(drop.transform.position, colony.transform.position);
                if (distance > radius || distance >= best) continue;
                if (reserve && TargetClaims.IsClaimedByOther(view.GetZDO().m_uid, villager)) continue;
                closest = drop; best = distance;
            }
            if (closest == null) { activity = "skipping: no loose item"; return JobResult.Skipped; }
            SetTarget(villager.State, closest.GetComponent<ZNetView>().GetZDO().m_uid, phase);
            activity = "found " + Utils.GetPrefabName(closest.gameObject);
            return JobResult.Running;
        }

        private static JobResult SelectSource(Villager villager, Colony colony,
            ColonyJobConfig job, JobPiece piece, out string activity)
        {
            ZDOID explicitSource = PieceSettings.Container(job, piece, job.Source);
            if (!explicitSource.IsNone())
                return SelectExplicitContainer(villager, colony, explicitSource, "acquiring", out activity);
            List<string> filters = PieceSettings.Filters(job, piece);
            List<ZDOID> scope = PieceSettings.Structures(job, piece);
            TargetMode mode = PieceSettings.Targets(job, piece);
            bool reserve = PieceSettings.Reservations(job, piece);
            foreach (StructureRecord record in colony.State.GetStructures())
            {
                if ((record.Capabilities & StructureCapability.Container) == 0 || !record.IsLiveIn(colony)) continue;
                bool selected = scope.Contains(record.Id);
                if (mode == TargetMode.Selected && !selected) continue;
                if (mode == TargetMode.Ignore && selected) continue;
                GameObject instance = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(record.Id) : null;
                if (instance == null || !instance.TryGetComponent(out Container container) ||
                    FirstMatching(container.GetInventory(), filters) == null) continue;
                if (reserve && TargetClaims.IsClaimedByOther(record.Id, villager)) continue;
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

        private static JobResult SelectStructure(Villager villager, Colony colony, ColonyJobConfig job, JobPiece piece,
            StructureCapability capability, string phase, out string activity)
        {
            List<ZDOID> scope = PieceSettings.Structures(job, piece);
            TargetMode mode = PieceSettings.Targets(job, piece);
            bool reserve = PieceSettings.Reservations(job, piece);
            foreach (StructureRecord record in colony.State.GetStructures())
            {
                if ((record.Capabilities & capability) == 0 || !record.IsLiveIn(colony)) continue;
                bool selected = scope.Contains(record.Id);
                if (mode == TargetMode.Selected && !selected) continue;
                if (mode == TargetMode.Ignore && selected) continue;
                if (reserve && TargetClaims.IsClaimedByOther(record.Id, villager)) continue;
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

        private static JobResult Acquire(GameObject target, Inventory bag, List<string> filters,
            VillagerState state, out string activity)
        {
            if (!TryOwnedContainer(target, out Container container, out JobResult ownership))
            {
                activity = ownership == JobResult.Running ? "claiming source" : "source invalid";
                return ownership;
            }
            Inventory source = container.GetInventory();
            ItemDrop.ItemData item = FirstMatching(source, filters);
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

        private static JobResult Deposit(GameObject target, Inventory bag, List<string> filters,
            VillagerState state, out string activity)
        {
            if (!TryOwnedContainer(target, out Container container, out JobResult ownership))
            {
                activity = ownership == JobResult.Running ? "claiming destination" : "destination invalid";
                return ownership;
            }
            Inventory destination = container.GetInventory();
            ItemDrop.ItemData item = FirstMatching(bag, filters);
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
