using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Kukolony.Colonies;
using Kukolony.Gui;
using Kukolony.Jobs;
using Kukolony.Villagers;
using UnityEngine;

namespace Kukolony.Debug
{
    internal sealed class ColonySelfTest : MonoBehaviour
    {
        private const string PersistenceName = "Kukolony Acceptance Colony V13";
        private bool _started;

        private void Update()
        {
            if (ModConfig.AutoTestEnabled.Value || ModConfig.BenchmarkMode.Value)
            {
                Application.runInBackground = true;
            }
            bool enabled = ModConfig.AutoTestEnabled.Value || (ModConfig.BenchmarkMode.Value && ModConfig.BenchmarkStage.Value != "ui");
            if (_started || !enabled || ModConfig.DebugProbeEnabled.Value ||
                ModConfig.DebugScreenshotEnabled.Value || Player.m_localPlayer == null ||
                ZoneSystem.instance == null || !ZoneSystem.instance.IsActiveAreaLoaded()) return;
            _started = true;
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            yield return new WaitForSecondsRealtime(ModConfig.BenchmarkMode.Value ? 10f : 8f);
            Colony existing = Colony.Instances.FirstOrDefault(c => c != null && c.State.Name == PersistenceName);
            if (existing != null) RunReload(existing);
            else yield return RunFresh();
            yield return new WaitForSecondsRealtime(.1f);
            if (ModConfig.AutoTestQuitWhenDone.Value && Game.instance != null)
            {
                Game.instance.Logout(true, false);
                // Game.Logout starts the profile/world save batch asynchronously.
                // Give Steam and Valheim enough time to flush it before the runner
                // starts the next process; the shell runner also waits for exit.
                yield return new WaitForSecondsRealtime(8f);
                Application.Quit();
            }
        }

        private static IEnumerator RunFresh()
        {
            TestReport report = new TestReport("Colony acceptance run 1 - create and save");
            Vector3 origin = Player.m_localPlayer.transform.position;
            TestWorld.Purge(origin);
            Colony colony = Spawn<Colony>(ColonyPrefab.PrefabName, origin + Vector3.forward * 4f);
            report.Check(colony != null, "colony prefab is registered");
            if (colony == null) { report.Print(); yield break; }
            colony.EnsureNamed();
            colony.State.SetName(PersistenceName);

            GameObject chest = Spawn("piece_chest_wood", origin + Vector3.right * 7f);
            StructureRecord chestRecord = Register(colony, chest, "Main storage");
            report.Check(chestRecord != null, "placed ZNet structure registers inside live radius");
            report.Check(chestRecord != null && (chestRecord.Capabilities & StructureCapability.Container) != 0,
                "container capability is cached");
            report.Check(ColonyOperations.RenameStructure(colony, chestRecord.Id, "Renamed storage") &&
                         colony.State.GetStructures()[0].Name == "Renamed storage", "structure naming persists");
            report.Check(ColonyOperations.FilterStructures(colony, "renamed", StructureCapability.Container,
                StructureSort.Name).Count == 1, "structure search and capability filtering");
            report.Check(ColonyOperations.FilterStructures(colony, string.Empty, StructureCapability.None,
                StructureSort.Status).Count == 1, "structure status sorting retains live records");

            Vector3 originalChestPosition = chest.transform.position;
            chest.transform.position = origin + Vector3.right * (colony.EffectiveRadius + 8f);
            chest.GetComponent<ZNetView>().GetZDO().SetPosition(chest.transform.position);
            yield return new WaitForSecondsRealtime(.2f);
            report.Check(colony.State.GetStructures().Count == 1 && !chestRecord.IsLiveIn(colony),
                "registered out-of-radius structure stays visible but becomes ineligible");
            chest.transform.position = originalChestPosition;
            chest.GetComponent<ZNetView>().GetZDO().SetPosition(originalChestPosition);
            yield return new WaitForSecondsRealtime(.2f);

            GameObject deleted = Spawn("piece_chest_wood", origin + Vector3.right * 10f);
            StructureRecord deletedRecord = Register(colony, deleted, "Deleted storage");
            if (deleted != null) ZNetScene.instance.Destroy(deleted);
            yield return new WaitForSecondsRealtime(.2f);
            report.Check(deletedRecord != null && colony.State.GetStructures().Any(r => r.Id == deletedRecord.Id) &&
                         !deletedRecord.IsLiveIn(colony), "deleted structure stays visible but is ineligible");

            GameObject outside = Spawn("piece_chest_wood", origin + Vector3.right * (colony.EffectiveRadius + 12f));
            StructureRecord outsideRecord = MakeRecord(outside, "Outside");
            report.Check(outsideRecord != null && !colony.RegisterStructure(outsideRecord),
                "out-of-radius structure is rejected");
            report.Check(!StructureRegistry.TryCapabilities(
                ZNetScene.instance.GetPrefab(VillagerPrefab.PrefabName), out _),
                "NPC is excluded from structure registration");

            List<ColonyJobConfig> jobs = ColonyJobCatalog.CreateDefaults();
            jobs[0].ItemFilters.Add("Wood");
            jobs[0].Destination = chestRecord.Id;
            jobs[0].SelectedStructures.Add(chestRecord.Id);
            jobs[0].Targets = TargetMode.Selected;
            jobs[0].Count = 2;
            jobs[0].StockLimit = 10;
            colony.State.SetJobs(jobs);
            report.Check(colony.State.GetJobs().Count == 7, "all seven concrete job configurations persist");
            report.Check(jobs.All(job => JobPipeline.IsValid(job, out _) && job.Pieces.Count >= 3),
                "starter jobs are valid typed piece pipelines");
            ColonyJobConfig invalidPipeline = new ColonyJobConfig { Name = "invalid" };
            invalidPipeline.Pieces.Add(new JobPiece { Kind = JobPieceKind.Start });
            invalidPipeline.Pieces.Add(new JobPiece { Kind = JobPieceKind.PutItem });
            invalidPipeline.Pieces.Add(new JobPiece { Kind = JobPieceKind.End });
            report.Check(!JobPipeline.IsValid(invalidPipeline, out _),
                "pipeline validation blocks missing compatible customisation");

            ColonyOperations.SavePreset(colony, "portable", jobs[0], false);
            ColonyOperations.SavePreset(colony, "local", jobs[0], true);
            List<JobPreset> presets = colony.State.GetPresets();
            JobPreset portable = presets.Find(p => p.Name == "portable");
            JobPreset local = presets.Find(p => p.Name == "local");
            report.Check(portable != null && portable.Settings.Destination.IsNone() &&
                         portable.Settings.SelectedStructures.Count == 0, "portable preset strips exact targets");
            report.Check(local != null && local.Settings.Destination == chestRecord.Id &&
                         local.Settings.SelectedStructures.Count == 1, "local preset retains exact targets");

            Villager villager = Spawn<Villager>(VillagerPrefab.PrefabName, origin + Vector3.back * 4f);
            // A full Valheim character rig is initialized over subsequent frames.
            // Yield before reading its ZDO/inventory so the unattended fixture never
            // contends with that initialization on the same main-thread frame.
            yield return new WaitForSecondsRealtime(1f);
            ZNetView view = villager != null ? villager.GetComponent<ZNetView>() : null;
            report.Check(view != null &&
                         colony.Register(ColonyMemberKind.Villager, view), "villager joins colony");
            if (villager != null && view != null)
            {
                VillagerState state = villager.State;
                ColonyAssignments.SetQueue(view.GetZDO().m_uid,
                    new List<string> { jobs[0].Id, jobs[1].Id });
                state = villager.State;
                report.Check(state.GetQueue().Count == 2, "villager queue is stored before save");
                QueueRunner.Apply(state, jobs, JobResult.Completed);
                report.Check(state.QueuePosition == 0 && state.QueueAttempt == 1,
                    "completed consumes one configured count");
                QueueRunner.Apply(state, jobs, JobResult.Failed);
                report.Check(state.QueuePosition == 1 && state.QueueAttempt == 0,
                    "failed consumes count and exhaustion advances");
                QueueRunner.Apply(state, jobs, JobResult.Skipped);
                report.Check(state.QueuePosition == 0 && state.QueueAttempt == 0,
                    "skipped consumes no count and yields; final entry loops");
                state.SetQueue(new List<string>());
            }

            if (villager != null && view != null)
                yield return CheckConcreteExecutors(report, colony, villager, origin);
            CheckPairedControls(report, villager, origin, chestRecord.Id);
            CheckStationContracts(report);
            report.Check(Enum.GetValues(typeof(ColonyJobType)).Length == 7, "concrete job catalog is complete");

            // Write a non-trivial runtime snapshot last, immediately before the world is
            // saved, so run 2 proves every per-villager field came from disk.
            if (villager != null && view != null)
            {
                VillagerState persisted = villager.State;
                ColonyAssignments.SetQueue(view.GetZDO().m_uid,
                    new List<string> { "acceptance.persistence.a", "acceptance.persistence.b" });
                persisted = villager.State;
                persisted.SetQueuePosition(1);
                persisted.SetQueueAttempt(1);
                persisted.SetQueueProgress(7);
                persisted.SetRuntimePhase("acceptance-persisted");
                persisted.SetStepTarget(chestRecord.Id);
            }
            report.Print();
        }

        private static void RunReload(Colony colony)
        {
            TestReport report = new TestReport("Colony acceptance run 2 - reload");
            ColonyState state = colony.State;
            report.Check(state.Name == PersistenceName, "colony name survived save and relaunch");
            report.Check(state.GetStructures().Any(r => r.Name == "Renamed storage"),
                "registered structure and name survived save and relaunch");
            report.Check(state.GetJobs().Count == 7, "job configurations survived save and relaunch");
            report.Check(state.GetJobs().All(job => JobPipeline.IsValid(job, out _) && job.Pieces.Count >= 3),
                "typed pipeline definitions survived save and relaunch");
            report.Check(state.GetPresets().Count == 2, "portable and local presets survived save and relaunch");
            List<ZDOID> members = state.GetMembers(ColonyMemberKind.Villager);
            report.Check(members.Count > 0, "member list survived save and relaunch");
            if (members.Count > 0)
            {
                ZDO zdo = ZDOMan.instance.GetZDO(members[0]);
                VillagerState villager = new VillagerState(zdo);
                report.Check(villager.GetQueue().Count == 2, "villager queue survived save and relaunch");
                report.Check(villager.QueuePosition == 1 && villager.QueueAttempt == 1,
                    "villager queue runtime survived save and relaunch");
                report.Check(villager.QueueProgress == 7 && villager.RuntimePhase == "acceptance-persisted" &&
                             !villager.StepTarget.IsNone(), "active target and runtime progress survived save and relaunch");
            }
            CheckStationContracts(report);
            report.Print();
            TestWorld.Purge(Player.m_localPlayer.transform.position);
        }

        private static void CheckStationContracts(TestReport report)
        {
            CheckContract<Fireplace>(report, "fire_pit", "RPC_AddFuelAmount");
            CheckContract<Smelter>(report, "smelter", "RPC_AddOre", "RPC_AddFuel");
            CheckContract<Smelter>(report, "charcoal_kiln", "RPC_AddOre");
            CheckContract<CookingStation>(report, "piece_cookingstation", "RPC_AddItem", "RPC_RemoveDoneItem");
            CheckContract<Fermenter>(report, "fermenter", "RPC_AddItem", "RPC_Tap");
            CheckContract<Beehive>(report, "piece_beehive", "RPC_Extract");
        }

        private static IEnumerator CheckConcreteExecutors(TestReport report, Colony colony,
            Villager villager, Vector3 origin)
        {
            ZNetView villagerView = villager.GetComponent<ZNetView>();
            // Use an ordinary ZNet-backed container as the deterministic test
            // inventory. Attaching a new Container to a fully animated NPC is an
            // engine-hostile mutation and can stall the macOS player rig.
            GameObject bagObject = Spawn("piece_chest_wood", origin + Vector3.back * 7f);
            Container bagComponent = bagObject != null ? bagObject.GetComponent<Container>() : null;
            Inventory bag = bagComponent != null ? bagComponent.GetInventory() : null;
            if (bag == null)
            {
                report.Check(false, "executor test inventory is available");
                yield break;
            }
            VillagerState state = villager.State;

            GameObject sourceObject = Spawn("piece_chest_wood", origin + Vector3.left * 8f);
            GameObject destinationObject = Spawn("piece_chest_wood", origin + Vector3.left * 11f);
            Register(colony, sourceObject, "Executor source");
            Register(colony, destinationObject, "Executor destination");

            Clear(bag);
            GameObject woodDrop = Spawn("Wood", origin + Vector3.left * 6f);
            int before = Count(destinationObject.GetComponent<Container>().GetInventory(), "Wood");
            JobResult pickup = ColonyJobEngine.TestPickup(woodDrop, bag, state, out _);
            ColonyJobConfig haul = Job(ColonyJobType.HaulLoose, "Wood");
            JobResult haulDeposit = ColonyJobEngine.TestDeposit(destinationObject, bag, haul, state, out _);
            report.Check(pickup == JobResult.Running && haulDeposit == JobResult.Completed &&
                         Count(destinationObject.GetComponent<Container>().GetInventory(), "Wood") == before + 1,
                "haul loose items executes pickup and ownership-safe container deposit");

            Clear(bag);
            Inventory source = sourceObject.GetComponent<Container>().GetInventory();
            Add(source, "Wood");
            JobResult acquired = JobResult.Running;
            string acquireActivity = string.Empty;
            for (int attempt = 0; attempt < 10 && Count(bag, "Wood") == 0; attempt++)
            {
                acquired = ColonyJobEngine.TestAcquire(sourceObject, bag, haul, state, out acquireActivity);
                yield return new WaitForSecondsRealtime(.2f);
            }
            JobResult transferred = JobResult.Running;
            for (int attempt = 0; attempt < 10 && transferred == JobResult.Running; attempt++)
            {
                transferred = ColonyJobEngine.TestDeposit(destinationObject, bag, haul, state, out _);
                yield return new WaitForSecondsRealtime(.2f);
            }
            report.Check(acquired == JobResult.Running && transferred == JobResult.Completed,
                "transfer executes ownership-safe source and destination writes",
                $"acquire={acquireActivity} bag={Count(bag, "Wood")} source={Count(source, "Wood")} slots={bag.GetEmptySlots()} size={bag.GetWidth()}x{bag.GetHeight()}");

            yield return CheckStation(report, state, bag, origin, "fire_pit",
                Job(ColonyJobType.FuelFireplaces, "Wood"), "Wood", "fuel fireplaces");
            yield return CheckStation(report, state, bag, origin, "smelter",
                Job(ColonyJobType.OperateSmelters, "CopperOre"), "CopperOre", "operate smelters");
            yield return CheckStation(report, state, bag, origin, "charcoal_kiln",
                Job(ColonyJobType.OperateSmelters, "Wood"), "Wood", "operate charcoal kilns");

            GameObject cooking = Spawn("piece_cookingstation", origin + Vector3.right * 12f);
            CookingStation cookingComponent = cooking != null ? cooking.GetComponent<CookingStation>() : null;
            string cookable = FirstAllowed(cookingComponent, "RawMeat", "DeerMeat", "NeckTail", "FishRaw");
            yield return CheckStation(report, state, bag, origin, cooking,
                Job(ColonyJobType.OperateCookingStations, cookable), cookable, "operate cooking stations");

            GameObject fermenter = Spawn("fermenter", origin + Vector3.right * 15f);
            Fermenter fermenterComponent = fermenter != null ? fermenter.GetComponent<Fermenter>() : null;
            string fermentable = FirstAllowed(fermenterComponent, "BarleyWineBase", "MeadBaseHealthMinor",
                "MeadBaseStaminaMinor", "MeadBasePoisonResist");
            yield return CheckStation(report, state, bag, origin, fermenter,
                Job(ColonyJobType.OperateFermenters, fermentable), fermentable, "operate fermenters");

            Clear(bag);
            GameObject hiveObject = Spawn("piece_beehive", origin + Vector3.right * 18f);
            Beehive hive = hiveObject != null ? hiveObject.GetComponent<Beehive>() : null;
            if (hive != null) hive.m_secPerUnit = .01f;
            yield return new WaitForSecondsRealtime(1f);
            if (hive != null) hive.UpdateBees();
            ColonyJobConfig hiveJob = Job(ColonyJobType.CollectBeehives, "Honey");
            ZNetView destinationView = destinationObject.GetComponent<ZNetView>();
            hiveJob.Destination = destinationView.GetZDO().m_uid;
            int honeyBefore = Count(destinationObject.GetComponent<Container>().GetInventory(), "Honey");
            JobResult hiveResult = ColonyJobEngine.TestOperate(hiveObject, bag, hiveJob, state, out _);
            yield return new WaitForSecondsRealtime(.5f);
            ItemDrop honey = ItemDrop.s_instances.FirstOrDefault(drop => drop != null &&
                Utils.GetPrefabName(drop.gameObject) == "Honey");
            if (honey != null)
            {
                state.SetRuntimePhase("pickup-collect");
                ColonyJobEngine.TestPickup(honey.gameObject, bag, state, out _);
                yield return new WaitForSecondsRealtime(.2f);
                if (Count(bag, "Honey") == 0)
                    ColonyJobEngine.TestPickup(honey.gameObject, bag, state, out _);
            }
            JobResult honeyDeposit = Count(bag, "Honey") > 0
                ? ColonyJobEngine.TestDeposit(destinationObject, bag, hiveJob, state, out _)
                : JobResult.Failed;
            report.Check(hive != null && hiveResult == JobResult.Running && honeyDeposit == JobResult.Completed &&
                         Count(destinationObject.GetComponent<Container>().GetInventory(), "Honey") == honeyBefore + 1,
                "collect beehives extracts, picks up and stores honey",
                $"ready={(hive != null ? hive.GetHoneyLevel() : -1)} result={hiveResult} drop={(honey != null)} bag={Count(bag, "Honey")} deposit={honeyDeposit}");

            Clear(bag);
            Fill(destinationObject.GetComponent<Container>().GetInventory(), "Wood");
            Add(bag, "Wood");
            report.Check(ColonyJobEngine.TestDeposit(destinationObject, bag, haul, state, out _) == JobResult.Failed,
                "full destination fails without losing carried item");
            haul.Destination = destinationObject.GetComponent<ZNetView>().GetZDO().m_uid;
            haul.StockLimit = 1;
            report.Check(ColonyJobEngine.TestLimitReached(colony, haul),
                "target stock limit produces a skip condition until stock falls below threshold");
            report.Check(ColonyJobEngine.TestOperate(null, bag, Job(ColonyJobType.FuelFireplaces, "Wood"), state, out _) == JobResult.Failed,
                "deleted or invalid station target fails safely");
        }

        private static IEnumerator CheckStation(TestReport report, VillagerState state, Inventory bag,
            Vector3 origin, string prefab, ColonyJobConfig job, string item, string label)
        {
            GameObject target = Spawn(prefab, origin + Vector3.right * UnityEngine.Random.Range(8f, 20f));
            yield return CheckStation(report, state, bag, origin, target, job, item, label);
        }

        private static IEnumerator CheckStation(TestReport report, VillagerState state, Inventory bag,
            Vector3 origin, GameObject target, ColonyJobConfig job, string item, string label)
        {
            Clear(bag);
            bool staged = Add(bag, item);
            int before = bag.GetAllItems().Sum(entry => entry.m_stack);
            int stationBefore = StationMetric(target, job);
            JobResult result = staged ? ColonyJobEngine.TestOperate(target, bag, job, state, out _) : JobResult.Failed;
            yield return new WaitForSecondsRealtime(.2f);
            int after = bag.GetAllItems().Sum(entry => entry.m_stack);
            int stationAfter = StationMetric(target, job);
            report.Check(target != null && staged && result == JobResult.Completed && after == before - 1 &&
                         stationAfter != stationBefore,
                label + " consumes one compatible input and submits vanilla RPC");
        }

        private static int StationMetric(GameObject target, ColonyJobConfig job)
        {
            if (target == null) return int.MinValue;
            if (target.TryGetComponent(out Smelter smelter))
            {
                string item = job.ItemFilters.FirstOrDefault() ?? string.Empty;
                return smelter.m_fuelItem != null && Utils.GetPrefabName(smelter.m_fuelItem.gameObject) == item
                    ? Mathf.RoundToInt(smelter.GetFuel() * 100f) : smelter.GetQueueSize();
            }
            if (target.TryGetComponent(out CookingStation cooking)) return cooking.GetFreeSlot();
            if (target.TryGetComponent(out Fermenter fermenter)) return fermenter.GetContent();
            if (target.TryGetComponent(out Fireplace fire)) return (int)fire.m_nview.GetZDO().DataRevision;
            return 0;
        }

        private static void CheckPairedControls(TestReport report, Villager villager, Vector3 origin, ZDOID target)
        {
            bool claims = ModConfig.ClaimsEnabled.Value;
            bool keepAlive = ModConfig.KeepAliveEnabled.Value;
            Villager other = Spawn<Villager>(VillagerPrefab.PrefabName, origin + Vector3.back * 10f);
            if (villager != null) villager.State.SetStepTarget(target);
            ModConfig.ClaimsEnabled.Value = true;
            bool claimed = other != null && TargetClaims.IsClaimedByOther(target, other);
            ModConfig.ClaimsEnabled.Value = false;
            bool unclaimedControl = other != null && !TargetClaims.IsClaimedByOther(target, other);
            report.Check(claimed && unclaimedControl, "claims pass with enabled path and fail-open control");

            ModConfig.KeepAliveEnabled.Value = true;
            KeepAlive.KeepAliveZones.Rebuild(new[] { origin });
            int enabledCount = KeepAlive.KeepAliveZones.Count;
            ModConfig.KeepAliveEnabled.Value = false;
            KeepAlive.KeepAliveZones.Rebuild(new[] { origin });
            report.Check(enabledCount > 0 && KeepAlive.KeepAliveZones.Count == 0,
                "keep-alive has passing enabled path and empty disabled control");
            ModConfig.ClaimsEnabled.Value = claims;
            ModConfig.KeepAliveEnabled.Value = keepAlive;
            KeepAlive.KeepAliveZones.Clear();
        }

        private static ColonyJobConfig Job(ColonyJobType type, string item)
        {
            ColonyJobConfig job = new ColonyJobConfig { Type = type, Name = ColonyJobCatalog.DisplayName(type) };
            if (!string.IsNullOrEmpty(item)) job.ItemFilters.Add(item);
            return job;
        }

        private static string FirstAllowed(CookingStation station, params string[] candidates)
        {
            foreach (string candidate in candidates)
            {
                GameObject prefab = ObjectDB.instance?.GetItemPrefab(candidate);
                if (station != null && prefab != null && station.IsItemAllowed(candidate)) return candidate;
            }
            return string.Empty;
        }

        private static string FirstAllowed(Fermenter station, params string[] candidates)
        {
            foreach (string candidate in candidates)
            {
                GameObject prefab = ObjectDB.instance?.GetItemPrefab(candidate);
                if (station != null && prefab != null && station.IsItemAllowed(candidate.GetStableHashCode())) return candidate;
            }
            return string.Empty;
        }

        private static bool Add(Inventory inventory, string prefabName)
        {
            GameObject prefab = ObjectDB.instance?.GetItemPrefab(prefabName);
            return inventory != null && prefab != null && inventory.AddItem(prefab, 1);
        }

        private static int Count(Inventory inventory, string prefabName) => inventory.GetAllItems()
            .Where(item => Utils.GetPrefabName(item.m_dropPrefab) == prefabName).Sum(item => item.m_stack);

        private static void Clear(Inventory inventory)
        {
            foreach (ItemDrop.ItemData item in inventory.GetAllItems().ToList()) inventory.RemoveItem(item);
        }

        private static void Fill(Inventory inventory, string prefabName)
        {
            Clear(inventory);
            GameObject prefab = ObjectDB.instance?.GetItemPrefab(prefabName);
            ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (drop == null) return;
            while (inventory.GetEmptySlots() > 0) inventory.AddItem(prefab, drop.m_itemData.m_shared.m_maxStackSize);
        }

        private static void CheckContract<T>(TestReport report, string prefabName, params string[] methods)
            where T : Component
        {
            GameObject prefab = ZNetScene.instance.GetPrefab(prefabName);
            T component = prefab != null ? prefab.GetComponent<T>() : null;
            bool valid = component != null;
            if (valid)
            {
                Type type = component.GetType();
                valid = methods.All(name => type.GetMethod(name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null);
            }
            report.Check(valid, prefabName + " exposes verified " + typeof(T).Name + " RPC contract");
        }

        private static StructureRecord Register(Colony colony, GameObject target, string name)
        {
            StructureRecord record = MakeRecord(target, name);
            return record != null && colony.RegisterStructure(record) ? record : null;
        }

        private static StructureRecord MakeRecord(GameObject target, string name)
        {
            if (target == null || !target.TryGetComponent(out ZNetView view) || !view.IsValid() ||
                !StructureRegistry.TryCapabilities(target, out StructureCapability capabilities)) return null;
            return new StructureRecord { Id=view.GetZDO().m_uid, Name=name,
                Prefab=Utils.GetPrefabName(target), Capabilities=capabilities };
        }

        private static T Spawn<T>(string prefabName, Vector3 position) where T : Component =>
            Spawn(prefabName, position)?.GetComponent<T>();

        private static GameObject Spawn(string prefabName, Vector3 position)
        {
            GameObject prefab = ZNetScene.instance.GetPrefab(prefabName);
            if (prefab == null) return null;
            position.y = ZoneSystem.instance.GetSolidHeight(position) + .2f;
            return Instantiate(prefab, position, Quaternion.identity);
        }
    }
}
