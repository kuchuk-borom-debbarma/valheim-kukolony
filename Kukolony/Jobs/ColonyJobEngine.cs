using System;
using System.Collections.Generic;
using Kukolony.Colonies;
using Kukolony.Villagers;
using UnityEngine;

namespace Kukolony.Jobs
{
    /// <summary>Concrete, ownership-safe execution for the built-in colony job catalogue.</summary>
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

        private static JobResult Operate(GameObject target, Inventory bag, ColonyJobConfig job,
            VillagerState state, out string activity)
        {
            if (target == null || !target.TryGetComponent(out ZNetView view) || !view.IsValid())
            {
                state.ResetRuntime(); activity = "station invalid"; return JobResult.Failed;
            }
            if (!view.IsOwner()) { view.ClaimOwnership(); activity = "claiming station"; return JobResult.Running; }
            ItemDrop.ItemData item = FirstMatching(bag, job.ItemFilters);
            switch (job.Type)
            {
                case ColonyJobType.FuelFireplaces:
                    if (!target.TryGetComponent(out Fireplace fire))
                        return FinishFailed(state, "fireplace invalid", out activity);
                    if (item == null || fire.m_fuelItem == null ||
                        Utils.GetPrefabName(item.m_dropPrefab) != Utils.GetPrefabName(fire.m_fuelItem.gameObject))
                        return FinishSkipped(state, "no compatible fuel", out activity);
                    // AddFuel owns the max-fuel guard and submits the verified
                    // AddFuelAmount RPC. CanUseItems cannot be used here because it checks
                    // the local Player inventory rather than the villager bag.
                    uint fuelRevision = view.GetZDO().DataRevision;
                    fire.AddFuel(1f);
                    if (view.GetZDO().DataRevision == fuelRevision)
                        return FinishSkipped(state, "fireplace full", out activity);
                    if (!Consume(bag, item)) return FinishFailed(state, "fuel disappeared", out activity);
                    break;
                case ColonyJobType.OperateSmelters:
                    if (!target.TryGetComponent(out Smelter smelter)) return FinishFailed(state, "smelter invalid", out activity);
                    if (item == null) return FinishSkipped(state, "no input", out activity);
                    string smelterItem = Utils.GetPrefabName(item.m_dropPrefab);
                    if (smelter.m_fuelItem != null && Utils.GetPrefabName(smelter.m_fuelItem.gameObject) == smelterItem)
                    {
                        if (smelter.m_maxFuel > 0 && smelter.GetFuel() >= smelter.m_maxFuel)
                            return FinishSkipped(state, "smelter fuel full", out activity);
                        if (!Consume(bag, item)) return FinishFailed(state, "fuel disappeared", out activity);
                        view.InvokeRPC("RPC_AddFuel");
                    }
                    else
                    {
                        if (!smelter.IsItemAllowed(item))
                            return FinishSkipped(state, "input not accepted", out activity);
                        if (smelter.GetQueueSize() >= smelter.m_maxOre)
                            return FinishSkipped(state, "smelter input full", out activity);
                        if (!Consume(bag, item)) return FinishFailed(state, "input disappeared", out activity);
                        view.InvokeRPC("RPC_AddOre", smelterItem, false);
                    }
                    break;
                case ColonyJobType.OperateCookingStations:
                    if (!target.TryGetComponent(out CookingStation cooking)) return FinishFailed(state, "cooking station invalid", out activity);
                    if (!cooking.IsEmpty() && cooking.IsEverythingCooked())
                    {
                        view.InvokeRPC("RPC_RemoveDoneItem", target.transform.position, 1);
                    }
                    else if (item != null && cooking.IsItemAllowed(item) && !cooking.IsStationFull())
                    {
                        string cookingItem = Utils.GetPrefabName(item.m_dropPrefab);
                        if (!Consume(bag, item)) return FinishFailed(state, "food disappeared", out activity);
                        view.InvokeRPC("RPC_AddItem", cookingItem, false);
                    }
                    else if (item == null && !cooking.IsStationFull())
                        return NeedInput(state, "cooking station needs food", out activity);
                    else
                        return FinishSkipped(state, "cooking station has no available action", out activity);
                    break;
                case ColonyJobType.OperateFermenters:
                    if (!target.TryGetComponent(out Fermenter fermenter)) return FinishFailed(state, "fermenter invalid", out activity);
                    if (fermenter.GetStatus() == Fermenter.Status.Ready)
                        view.InvokeRPC("RPC_Tap");
                    else if (fermenter.GetStatus() == Fermenter.Status.Empty && item != null && fermenter.IsItemAllowed(item))
                    {
                        int hash = Utils.GetPrefabName(item.m_dropPrefab).GetStableHashCode();
                        if (!Consume(bag, item)) return FinishFailed(state, "fermentable disappeared", out activity);
                        view.InvokeRPC("RPC_AddItem", hash, false);
                    }
                    else if (fermenter.GetStatus() == Fermenter.Status.Empty && item == null)
                        return NeedInput(state, "fermenter needs input", out activity);
                    else
                        return FinishSkipped(state, "fermenter is busy", out activity);
                    break;
                case ColonyJobType.CollectBeehives:
                    if (!target.TryGetComponent(out Beehive hive) || hive.GetHoneyLevel() <= 0)
                        return FinishSkipped(state, "no honey ready", out activity);
                    view.InvokeRPC("RPC_Extract");
                    state.ResetRuntime();
                    state.SetQueueProgress(100);
                    activity = "extracting honey";
                    return JobResult.Running;
                default:
                    return FinishFailed(state, "unsupported operation", out activity);
            }
            state.ResetRuntime();
            activity = "operation submitted";
            return JobResult.Completed;
        }

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

        private static JobResult FinishSkipped(VillagerState state, string message, out string activity)
        { state.ResetRuntime(); activity = "skipping: " + message; return JobResult.Skipped; }

        private static JobResult FinishFailed(VillagerState state, string message, out string activity)
        { state.ResetRuntime(); activity = message; return JobResult.Failed; }

        private static JobResult NeedInput(VillagerState state, string message, out string activity)
        {
            state.SetStepTarget(ZDOID.None);
            state.SetRuntimePhase(string.Empty);
            state.SetQueueProgress(1);
            activity = message;
            return JobResult.Running;
        }
    }
}
