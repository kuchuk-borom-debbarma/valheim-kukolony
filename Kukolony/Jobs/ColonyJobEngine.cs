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
            Operate(target, bag, job, ColonyJobCatalog.RequiredCapability(job.Type), state, out activity);
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
            activity = string.IsNullOrEmpty(job.Name) ? "working" : job.Name;
            Inventory inventory = bag != null ? bag.GetInventory() : null;
            if (inventory == null) return JobResult.Failed;

            Work.IColonyWork work = Work.WorkRegistry.For(job.Type);
            if (work == null) return JobOutcomes.Skipped(villager.State, "unknown job", out activity);
            return TickWork(work, villager, ai, inventory, colony, job, out activity);
        }


        /// <summary>Ticks a villager will wait for work to produce something before giving up.</summary>
        private const int WaitTicks = 40;

        /// <summary>Nearest matching loose item in range, ignoring claims. Read-only.</summary>
        private static ItemDrop FindLoose(Colony colony, List<string> filters, float radius)
        {
            foreach (ItemDrop drop in ItemDrop.s_instances)
            {
                if (drop == null || !drop.TryGetComponent(out ZNetView view) || !view.IsValid()) continue;
                if (!Matches(Utils.GetPrefabName(drop.gameObject), filters)) continue;
                if (Utils.DistanceXZ(drop.transform.position, colony.transform.position) > radius) continue;
                return drop;
            }
            return null;
        }

        // ---- Work adapters -------------------------------------------------------------
        // A job says what it wants done; these give it the existing executors unchanged, so
        // moving a job onto the new path changes how it is sequenced and not what it does.

        /// <summary>Whether the bag holds any of these items. Empty means anything counts.</summary>
        internal static bool HoldsWanted(Inventory bag, List<string> items) =>
            FirstMatching(bag, items) != null;

        internal static JobResult ChooseLooseItem(Work.WorkContext c, out string activity) =>
            SelectLooseItem(c.Villager, c.Colony, c.Job, "pickup", out activity);

        internal static JobResult ChooseStockedContainer(Work.WorkContext c, out string activity) =>
            ChooseStockedContainer(c, c.Job.ItemFilters, out activity);

        /// <summary>
        ///     The same, for work whose wanted items are not the job's own list. Equipping asks
        ///     for what the villager's outfit is missing, which differs villager by villager.
        /// </summary>
        internal static JobResult ChooseStockedContainer(Work.WorkContext c, List<string> items,
            out string activity) =>
            SelectSource(c.Villager, c.Colony, c.Job, items, out activity);

        /// <summary>
        ///     Chooses where the load goes. Among the colony's containers it will only pick one
        ///     that can take what is carried: choosing a full chest and finding out on arrival
        ///     fails the job after a walk, which is a worse answer than choosing another.
        /// </summary>
        internal static JobResult ChooseContainer(Work.WorkContext c, StructureCapability capability,
            out string activity) =>
            !c.Job.Destination.IsNone()
                ? SelectExplicitContainer(c.Villager, c.Colony, c.Job.Destination, "depositing", out activity)
                : SelectStructure(c.Villager, c.Colony, c.Job, capability, "depositing", out activity,
                    FirstMatching(c.Bag, c.Job.ItemFilters));

        internal static JobResult PickUpTarget(Work.WorkContext c, out string activity) =>
            PickupLoose(c.Target, c.Bag, c.State, out activity);

        internal static JobResult TakeFromTarget(Work.WorkContext c, out string activity) =>
            Acquire(c.Target, c.Bag, c.Job.ItemFilters, c.State, out activity);

        internal static JobResult TakeFromTarget(Work.WorkContext c, List<string> items,
            out string activity) =>
            Acquire(c.Target, c.Bag, items, c.State, out activity);

        /// <summary>
        ///     Stores the carried item, or puts it down where the villager stands when the job
        ///     asks for a pile rather than a container.
        /// </summary>
        internal static JobResult DepositCarried(Work.WorkContext c, out string activity) =>
            c.Job.DropOnGround
                ? DropCarried(c.Villager, c.Bag, c.Job.ItemFilters, out activity)
                : Deposit(c.Target, c.Bag, c.Job.ItemFilters, c.State, out activity);

        /// <summary>
        ///     Puts down what is carried, where the villager stands. Lets a job gather to a
        ///     pile rather than requiring a container for every outcome.
        /// </summary>
        private static JobResult DropCarried(Villager villager, Inventory bag, List<string> filters,
            out string activity)
        {
            ItemDrop.ItemData carried = FirstMatching(bag, filters);
            if (carried == null) return JobOutcomes.Skipped(villager.State, "nothing to put down", out activity);
            if (carried.m_dropPrefab == null)
                return JobOutcomes.Failed(villager.State, "carried item cannot be dropped", out activity);

            Vector3 at = villager.transform.position + Vector3.up * .5f + UnityEngine.Random.insideUnitSphere * .3f;
            ItemDrop.DropItem(carried, 0, at, Quaternion.Euler(0f, UnityEngine.Random.Range(0, 360), 0f));
            bag.RemoveItem(carried);
            villager.State.ClearTarget();
            activity = "put down " + Utils.GetPrefabName(carried.m_dropPrefab);
            return JobResult.Completed;
        }

        /// <summary>
        ///     Picks something to chop: a felled log if any are lying about, otherwise a
        ///     standing tree. Logs first so a villager finishes what it started - a colony that
        ///     kept felling and never cut up would produce no wood at all while looking busy.
        /// </summary>
        internal static JobResult ChooseResource(Work.WorkContext c, out string activity)
        {
            if (SelectNearest(c, Resources.ResourceKind.Log, out activity)) return JobResult.Running;
            if (SelectNearest(c, Resources.ResourceKind.Tree, out activity)) return JobResult.Running;
            return JobOutcomes.Skipped(c.State, "nothing to chop nearby", out activity);
        }

        /// <summary>
        ///     Nearest unclaimed resource of a kind, within the job's radius. Distance is
        ///     measured from the hearth rather than the villager so that the colony works
        ///     outward from itself instead of wandering off after whatever is closest.
        /// </summary>
        private static bool SelectNearest(Work.WorkContext c, Resources.ResourceKind kind, out string activity)
        {
            ZDOID best = ZDOID.None;
            float closest = float.MaxValue;
            foreach (ZDOID id in Resources.ColonyResources.Near(c.Colony, kind, c.Job.SearchRadius))
            {
                ZDO zdo = ZDOMan.instance.GetZDO(id);
                if (zdo == null || !zdo.IsValid()) continue;
                float distance = Utils.DistanceXZ(zdo.GetPosition(), c.Colony.transform.position);
                if (distance >= closest) continue;
                if (c.Job.Reservations && TargetClaims.IsClaimedByOther(id, c.Villager)) continue;
                best = id;
                closest = distance;
            }
            if (best.IsNone()) { activity = string.Empty; return false; }
            SetTarget(c.State, best, "chopping");
            activity = kind == Resources.ResourceKind.Log ? "found a log" : "found a tree";
            return true;
        }

        /// <summary>
        ///     Lands one blow. The target coming down is what ends the cycle, so this releases
        ///     it rather than leaving a reference to something the scene has destroyed - which
        ///     the next tick would read as a fault instead of as a felled tree.
        /// </summary>
        internal static JobResult StrikeTarget(Work.WorkContext c, out string activity)
        {
            ItemDrop.ItemData axe = FirstTool(c.Bag, Work.ToolRequirement.Axe);
            Resources.BlowResult blow = Resources.Felling.Strike(c.Target, axe, out activity);
            switch (blow)
            {
                case Resources.BlowResult.Struck:
                case Resources.BlowResult.Claiming:
                    return JobResult.Running;
                case Resources.BlowResult.Felled:
                    c.State.ClearTarget();
                    return JobResult.Running;
                default:
                    // Releasing the target matters as much here: without it the villager would
                    // stand in front of the same unchoppable tree for the rest of the session.
                    return JobOutcomes.Skipped(c.State, activity, out activity);
            }
        }

        internal static JobResult ChooseStation(Work.WorkContext c, StructureCapability capability,
            out string activity) =>
            SelectStructure(c.Villager, c.Colony, c.Job, capability, "operating", out activity);

        internal static JobResult OperateTarget(Work.WorkContext c, StructureCapability capability,
            out string activity) =>
            Operate(c.Target, c.Bag, c.Job, capability, c.State, out activity);

        /// <summary>
        ///     Stands by until the work already started puts something on the ground, giving
        ///     up after a bounded wait so a job that will never produce anything cannot hold a
        ///     villager forever. Reports whether the wait is over, because only then may the
        ///     villager move on to collecting.
        /// </summary>
        internal static JobResult AwaitProduce(Work.WorkContext c, out bool appeared, out string activity)
        {
            appeared = FindLoose(c.Colony, c.Job.ItemFilters, c.Job.SearchRadius) != null;
            if (appeared)
            {
                c.State.SetQueueProgress(0);
                activity = "collecting what appeared";
                return JobResult.Running;
            }
            int waited = c.State.QueueProgress + 1;
            if (waited > WaitTicks) return JobOutcomes.Skipped(c.State, "nothing appeared to collect", out activity);
            c.State.SetQueueProgress(waited);
            activity = "waiting for the work to finish";
            return JobResult.Running;
        }

        /// <summary>
        ///     Runs a job that owns its own sequence. The job decides what to do; this performs
        ///     it and records where the villager got to.
        /// </summary>
        private static JobResult TickWork(Work.IColonyWork work, Villager villager, MonsterAI ai,
            Inventory bag, Colony colony, ColonyJobConfig job, out string activity)
        {
            VillagerState state = villager.State;
            GameObject target = ResolveTarget(state, out bool targetLost);
            if (targetLost)
            {
                state.ResetJob();
                activity = "target missing";
                return JobResult.Failed;
            }

            bool arrived = false;
            if (target != null)
            {
                // Arrival is measured rather than remembered: a villager pushed away from its
                // target between ticks has not arrived, whatever it recorded last time.
                arrived = Utils.DistanceXZ(villager.transform.position, target.transform.position)
                          <= Mathf.Max(.5f, job.StopDistance);
            }

            Work.WorkSubject subject = new Work.WorkSubject(villager, bag, colony, job);
            bool inPlace = work.DeliversInPlace(subject);
            Work.WorkStep step = work.Next(state.Work, new Work.WorkFacts(
                hasTarget: !state.StepTarget.IsNone(),
                arrived: arrived,
                carrying: work.Carrying(subject),
                stockLimitReached: LimitReached(colony, job),
                hasTool: HasRequiredTool(bag, work.RequiredTool),
                deliversInPlace: inPlace));

            // The phase is where the decision landed, not where the villager set out from:
            // transitions skip through states whose outcome already holds, and a job that
            // collects twice in a cycle would otherwise be told the wrong half.
            Work.WorkContext context = new Work.WorkContext(villager, ai, bag, colony, job, target,
                step.From);
            switch (step.Action)
            {
                case Work.WorkAction.Yield:
                    return JobOutcomes.Skipped(state, RequirementMessage(work, bag), out activity);

                case Work.WorkAction.Complete:
                    return JobOutcomes.Completed(state, "job cycle complete", out activity);

                case Work.WorkAction.ChooseSource:
                    return Record(state, step, work.ChooseSource(context, out activity));

                case Work.WorkAction.ChooseTarget:
                    return Record(state, step, work.ChooseTarget(context, out activity));

                case Work.WorkAction.Move:
                    return Record(state, step, Walk(villager, ai, state,
                        job.StopDistance, target, out _, out activity));

                // Standing by holds the villager in the waiting state; only something having
                // appeared moves it on.
                case Work.WorkAction.Wait:
                    return Record(state, step, AwaitProduce(context, out bool appeared, out activity),
                        appeared);
            }

            // The rest act on the target, so one that has not loaded is worth waiting for
            // rather than failing: its zone may still be streaming in. Putting the load down
            // where the villager stands is the exception - there is no target to wait for,
            // and waiting for one would leave the villager holding it forever.
            if (target == null && !(inPlace && step.Action == Work.WorkAction.Deliver))
            {
                activity = "waiting for target to load";
                return JobResult.Running;
            }
            switch (step.Action)
            {
                case Work.WorkAction.Collect: return Record(state, step, work.Collect(context, out activity));
                case Work.WorkAction.Deliver: return Record(state, step, work.Deliver(context, out activity));
                default: return JobOutcomes.Skipped(state, "job cannot run", out activity);
            }
        }

        /// <summary>
        ///     Records the next state when a step made progress. A leaf reports Completed to
        ///     mean its own step finished, which is not the job being done, so it becomes
        ///     Running and only the job's own completion ends a cycle.
        /// </summary>
        private static JobResult Record(VillagerState state, Work.WorkStep step, JobResult result,
            bool advance = true)
        {
            if (result != JobResult.Running && result != JobResult.Completed) return result;
            if (advance) state.SetWork(step.Next);
            return JobResult.Running;
        }

        /// <summary>True when the bag holds a tool that can do the work this job needs.</summary>
        private static bool HasRequiredTool(Inventory bag, Work.ToolRequirement required) =>
            required == Work.ToolRequirement.None || FirstTool(bag, required) != null;

        /// <summary>
        ///     The best tool in the bag for this kind of work, or null. Best means highest
        ///     tier: a villager carrying a stone axe and a bronze one should use the bronze.
        /// </summary>
        internal static ItemDrop.ItemData FirstTool(Inventory bag, Work.ToolRequirement required)
        {
            if (required == Work.ToolRequirement.None || bag == null) return null;
            ItemDrop.ItemData best = null;
            foreach (ItemDrop.ItemData item in bag.GetAllItems())
            {
                if (item == null || item.m_shared == null) continue;
                // Classified by what the tool can do, not by its category: axes and pickaxes
                // are weapons in the game's own taxonomy, and only hammers and hoes are tools.
                HitData.DamageTypes damage = item.GetDamage();
                bool fits = required == Work.ToolRequirement.Axe ? damage.m_chop > 0f : damage.m_pickaxe > 0f;
                if (!fits) continue;
                if (best == null || item.m_shared.m_toolTier > best.m_shared.m_toolTier) best = item;
            }
            return best;
        }

        /// <summary>Says why a job yielded, so a missing tool is visible rather than mysterious.</summary>
        private static string RequirementMessage(Work.IColonyWork work, Inventory bag) =>
            HasRequiredTool(bag, work.RequiredTool)
                ? "nothing to do"
                : "needs " + (work.RequiredTool == Work.ToolRequirement.Axe ? "an axe" : "a pickaxe");

        /// <summary>
        ///     Walking itself, with nowhere to record progress. Jobs that own their sequencing
        ///     keep that record in their own terms rather than in a pipeline cursor.
        /// </summary>
        private static JobResult Walk(Villager villager, MonsterAI ai, VillagerState state,
            float stopDistance, GameObject target, out bool arrived, out string activity)
        {
            arrived = false;
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
            arrived = true;
            activity = "arrived at " + TargetName(target);
            return JobResult.Running;
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

        private static JobResult SelectLooseItem(Villager villager, Colony colony,
            ColonyJobConfig job, string phase, out string activity)
        {
            List<string> filters = job.ItemFilters;
            float radius = job.SearchRadius;
            bool reserve = job.Reservations;
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
            ColonyJobConfig job, List<string> filters, out string activity)
        {
            if (!job.Source.IsNone())
                return SelectExplicitContainer(villager, colony, job.Source, "acquiring", out activity);
            List<ZDOID> scope = job.SelectedStructures;
            TargetMode mode = job.Targets;
            bool reserve = job.Reservations;
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

        /// <param name="mustFit">
        ///     When given, only a container with room for this item is eligible. Null means
        ///     capacity is not this selection's concern - a smelter is not a container.
        /// </param>
        private static JobResult SelectStructure(Villager villager, Colony colony, ColonyJobConfig job,
            StructureCapability capability, string phase, out string activity,
            ItemDrop.ItemData mustFit = null)
        {
            List<ZDOID> scope = job.SelectedStructures;
            TargetMode mode = job.Targets;
            bool reserve = job.Reservations;
            foreach (StructureRecord record in colony.State.GetStructures())
            {
                if ((record.Capabilities & capability) == 0 || !record.IsLiveIn(colony)) continue;
                bool selected = scope.Contains(record.Id);
                if (mode == TargetMode.Selected && !selected) continue;
                if (mode == TargetMode.Ignore && selected) continue;
                if (mustFit != null && !HasRoomFor(record, mustFit)) continue;
                if (reserve && TargetClaims.IsClaimedByOther(record.Id, villager)) continue;
                SetTarget(villager.State, record.Id, phase);
                activity = phase + " " + record.Name;
                return JobResult.Running;
            }
            activity = mustFit != null ? "skipping: no container with room" : "skipping: no eligible target";
            return JobResult.Skipped;
        }

        /// <summary>
        ///     Whether a registered container can take one of this item. A structure that is
        ///     not loaded cannot be asked, and is treated as unusable rather than assumed
        ///     roomy: walking to it is the cost this check exists to avoid.
        /// </summary>
        private static bool HasRoomFor(StructureRecord record, ItemDrop.ItemData item)
        {
            GameObject instance = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(record.Id) : null;
            return instance != null && instance.TryGetComponent(out Container container) &&
                   container.GetInventory().CanAddItem(item, 1);
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
            StructureCapability declared, VillagerState state, out string activity)
        {
            if (target == null || !target.TryGetComponent(out ZNetView view) || !view.IsValid())
            {
                state.ResetRuntime(); activity = "station invalid"; return JobResult.Failed;
            }
            if (!view.IsOwner()) { view.ClaimOwnership(); activity = "claiming station"; return JobResult.Running; }

            Stations.IStationProtocol protocol = Stations.StationProtocols.Resolve(target, declared);
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
