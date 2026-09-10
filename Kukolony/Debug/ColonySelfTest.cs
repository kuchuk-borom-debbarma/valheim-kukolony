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
    /// <summary>Functional phases invoked exclusively by ColonyBenchmarkController.</summary>
    /// <remarks>
    ///     <para>
    ///         Passive by design: nothing here starts itself, purges a world, launches a
    ///         process, or terminates Valheim. The controller owns the lifecycle; this type
    ///         only asserts. Fixtures are spawned into, and registered with, the uniquely
    ///         named benchmark colony so reload cleanup owns them.
    ///     </para>
    ///     <para>
    ///         The proof is split across two processes. <see cref="RunFresh"/> builds the
    ///         colony, exercises every concrete executor against real stations and containers,
    ///         then <see cref="PreparePersistenceSnapshot"/> writes known values.
    ///         <see cref="RunReload"/> runs in a second launch and asserts those values
    ///         survived a real save, process exit, and load. Persistence claims are only
    ///         meaningful across that boundary, which is why a single run cannot pass alone.
    ///     </para>
    ///     <para>
    ///         Positive claims are paired with a disabled or failing control — see
    ///         <c>CheckPairedControls</c> — so a check cannot pass merely because the
    ///         mechanism never ran.
    ///     </para>
    /// </remarks>
    internal static class BenchmarkFunctionalScenario
    {
        internal const string PersistenceName = "Kukolony Benchmark Persistence V1";
        private static readonly int RunIdKey = "kukolony.benchmark.run".GetStableHashCode();
        private static readonly int PrimaryMemberKey =
            "kukolony.benchmark.primary-member.v2".GetStableHashCode();
        internal static bool LastPassed { get; private set; }

        /// <summary>
        ///     First-launch phase: build the colony and fixtures, assert registry eligibility,
        ///     pipeline validity, presets, queue semantics, executors, and station contracts,
        ///     then stamp the run so the reload phase can recognise its own colony.
        /// </summary>
        internal static IEnumerator RunFresh(string runId)
        {
            LastPassed = false;
            TestReport report = new TestReport("Colony acceptance run 1 - create and save");
            Vector3 origin = Player.m_localPlayer.transform.position;
            Colony colony = Spawn<Colony>(ColonyPrefab.PrefabName, origin + Vector3.forward * 4f);
            report.Check(colony != null, "colony prefab is registered");
            if (colony == null) { report.Print(); yield break; }
            colony.EnsureNamed();
            colony.State.SetName(PersistenceName);
            SetRunId(colony, runId);

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
            if (outside != null) ZNetScene.instance.Destroy(outside);
            report.Check(!StructureRegistry.TryCapabilities(
                ZNetScene.instance.GetPrefab(VillagerPrefab.PrefabName), out _),
                "NPC is excluded from structure registration");

            List<ColonyJobConfig> jobs = ColonyJobCatalog.CreateDefaults();
            if (!jobs[0].ItemFilters.Contains("Wood")) jobs[0].ItemFilters.Add("Wood");
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

            Core.Log.Info("[Benchmark] villager spawn begin");
            Villager villager = Spawn<Villager>(VillagerPrefab.PrefabName, origin + Vector3.back * 4f);
            // ZNetView.Awake creates the ZDO synchronously, so this normally exits on the
            // first check. Kept as a cheap guard against a prefab whose network component
            // is disabled or destroyed, which would otherwise fail further down as a
            // confusing null rather than here.
            int readinessChecks = 0;
            while (villager != null && readinessChecks++ < 40)
            {
                if (villager.TryGetComponent(out ZNetView readyView) && readyView.IsValid() && readyView.GetZDO() != null) break;
                yield return new WaitForSecondsRealtime(.25f);
            }
            Core.Log.Info("[Benchmark] villager spawn frame completed");
            ZNetView view = villager != null ? villager.GetComponent<ZNetView>() : null;
            report.Check(view != null &&
                         colony.Register(ColonyMemberKind.Villager, view), "villager joins colony");
            if (villager != null && view != null)
            {
                SetPrimaryMember(colony, view.GetZDO().m_uid);
                Core.Log.Info($"[Benchmark] primary villager id={view.GetZDO().m_uid} " +
                              $"prefab={view.GetZDO().GetPrefab()} persistent={view.GetZDO().Persistent}");
                report.Check(view.GetZDO().Persistent, "live villager ZDO is persistent");
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
            CheckPairedControls(report, colony, villager, origin, chestRecord.Id);
            yield return CheckLifecycle(report, colony, origin);
            yield return CheckHaulPipeline(report, colony);
            yield return CheckTransferPipeline(report, colony);
            CheckStationContracts(report);
            CheckStationProtocols(report, origin);
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
            LastPassed = report.Print();
        }

        /// <summary>
        ///     Second-launch phase: assert the snapshot written before exit survived the save,
        ///     including cross-ZDO references that a chunked save renormalises, then clean up
        ///     the run's fixtures.
        /// </summary>
        internal static void RunReload(Colony colony)
        {
            LastPassed = false;
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
            ZDOID primary = GetPrimaryMember(colony);
            ZDO zdo = ZDOMan.instance.GetZDO(primary);
            List<ZDO> loadedVillagers = FindVillagerZdos();
            Core.Log.Info($"[Benchmark] reloaded primary villager id={primary} " +
                          $"zdo={(zdo != null ? zdo.GetPrefab().ToString() : "missing")}; " +
                          $"loaded villager ZDOs={string.Join(",", loadedVillagers.Select(item => item.m_uid.ToString()).ToArray())}");
            report.Check(zdo != null, "real villager ZDO survived save and relaunch");
            if (zdo != null)
            {
                VillagerState villager = new VillagerState(zdo);
                report.Check(villager.GetQueue().Count == 2, "villager queue survived save and relaunch");
                report.Check(villager.QueuePosition == 1 && villager.QueueAttempt == 1,
                    "villager queue runtime survived save and relaunch");
                report.Check(villager.QueueProgress == 7 && villager.RuntimePhase == "acceptance-persisted" &&
                             !villager.StepTarget.IsNone(), "active target and runtime progress survived save and relaunch");
                report.Check(StoredBagCount(zdo, "Coal") == 3,
                    "villager bag contents survived save and relaunch");
                report.Check(villager.StepCursor == 3,
                    "villager pipeline cursor survived save and relaunch");
            }
            CheckStationContracts(report);
            LastPassed = report.Print();
        }

        /// <summary>
        ///     Writes known queue, position, attempt, progress, phase, and target values to the
        ///     primary villager immediately before the world save, so the reload phase has
        ///     specific values to verify rather than merely checking the villager still exists.
        /// </summary>
        internal static void PreparePersistenceSnapshot(Colony colony)
        {
            if (colony == null || ZDOMan.instance == null) return;
            ZDO zdo = ZDOMan.instance.GetZDO(GetPrimaryMember(colony));
            StructureRecord target = colony.State.GetStructures().FirstOrDefault(record => record.Name == "Renamed storage");
            if (zdo == null || target == null) return;
            VillagerState persisted = new VillagerState(zdo);
            persisted.SetQueue(new List<string> { "acceptance.persistence.a", "acceptance.persistence.b" });
            persisted.SetQueuePosition(1);
            persisted.SetQueueAttempt(1);
            persisted.SetQueueProgress(7);
            persisted.SetRuntimePhase("acceptance-persisted");
            persisted.SetStepTarget(target.Id);
            // Nothing reads the cursor for behaviour yet. Proving it round-trips a real save
            // now means the walker can rely on it later without a second in-game run.
            persisted.SetStepCursor(3);

            // Fill the bag here rather than earlier so no job tick can spend it before the
            // save; the queue above is deliberately unresolvable, so this villager idles.
            GameObject carried = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(zdo.m_uid) : null;
            if (carried != null && carried.TryGetComponent(out ZNetView carriedView) && carriedView.IsValid())
            {
                Container bag = VillagerInventory.Attach(carried, carriedView);
                Clear(bag.GetInventory());
                for (int i = 0; i < 3; i++) Add(bag.GetInventory(), "Coal");
                Core.Log.Info($"[Benchmark] primary villager bag before save: live=" +
                              $"{Count(bag.GetInventory(), "Coal")} stored={StoredBagCount(zdo, "Coal")}");
            }

            // ZDOMan serializes its sector index, not its ID dictionary. Verify the
            // production creature followed the same indexing contract as a vanilla
            // placed object before treating a successful Save call as evidence.
            var sectorsField = typeof(ZDOMan).GetField("m_objectsBySector",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var sectors = sectorsField?.GetValue(ZDOMan.instance) as List<ZDO>[];
            uint sector = zdo.GetSectorIndex().Sector;
            bool indexed = sectors != null && sector < sectors.Length &&
                           sectors[sector] != null && sectors[sector].Contains(zdo);
            Core.Log.Info($"[Benchmark] final villager snapshot id={zdo.m_uid} " +
                          $"persistent={zdo.Persistent} sector={sector} indexed={indexed}");
            if (!indexed)
            {
                ZDOMan.instance.AddToSector(zdo, zdo.GetSectorIndex());
                Core.Log.Warning("Repaired missing villager sector index before save");
            }
            ZDOMan.instance.SetDirtySector(zdo);
            Core.Log.Info("[Benchmark] final villager persistence snapshot written");
        }

        private static List<ZDO> FindVillagerZdos()
        {
            List<ZDO> result = new List<ZDO>();
            int index = 0;
            while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(
                       VillagerPrefab.PrefabName, result, ref index)) { }
            return result;
        }

        /// <summary>
        ///     True when this colony was created by the given run. Lets the reload launch tell
        ///     its own fixtures apart from any left by an earlier or interrupted run.
        /// </summary>
        internal static bool BelongsToRun(Colony colony, string runId)
        {
            if (colony == null || !colony.TryGetComponent(out ZNetView view) || !view.IsValid()) return false;
            return view.GetZDO().GetString(RunIdKey, string.Empty) == runId;
        }

        private static void SetRunId(Colony colony, string runId)
        {
            if (colony.TryGetComponent(out ZNetView view) && view.IsValid())
                view.GetZDO().Set(RunIdKey, runId ?? string.Empty);
        }

        private static void SetPrimaryMember(Colony colony, ZDOID member)
        {
            if (colony.TryGetComponent(out ZNetView view) && view.IsValid())
                view.GetZDO().Set(PrimaryMemberKey,
                    Core.PersistentZdoReference.Ensure(ZDOMan.instance.GetZDO(member)));
        }

        private static ZDOID GetPrimaryMember(Colony colony)
        {
            if (colony == null || !colony.TryGetComponent(out ZNetView view) || !view.IsValid()) return ZDOID.None;
            return Core.PersistentZdoReference.Resolve(
                view.GetZDO().GetString(PrimaryMemberKey, string.Empty));
        }

        /// <summary>
        ///     Covers the production spawn and remove path a player drives from the hearth.
        ///     Every positive claim is paired with a control, because both operations can look
        ///     like they worked while doing nothing: a spawn that never registers, or a remove
        ///     that destroys the villager and quietly eats what it carried.
        /// </summary>
        private static IEnumerator CheckLifecycle(TestReport report, Colony colony, Vector3 origin)
        {
            CheckResetSemantics(report, colony);

            int beforeSpawn = FindVillagerZdos().Count;
            report.Check(VillagerLifecycle.Spawn(null) == null && FindVillagerZdos().Count == beforeSpawn,
                "spawn refuses without a colony and creates no villager");

            Villager born = VillagerLifecycle.Spawn(colony);
            ZNetView bornView = born != null && born.TryGetComponent(out ZNetView found) ? found : null;
            report.Check(born != null && bornView != null && bornView.IsValid() && bornView.GetZDO() != null,
                "hearth spawn produces a network-valid villager in the same frame");
            if (bornView == null) yield break;

            ZDOID bornId = bornView.GetZDO().m_uid;
            report.Check(colony.State.GetMembers(ColonyMemberKind.Villager).Contains(bornId) &&
                         ColonyMembership.BelongsTo(bornView.GetZDO(), colony.Id),
                "hearth spawn registers membership in both directions");
            report.Check(ZoneSystem.instance != null &&
                         ZoneSystem.instance.GetSolidHeight(born.transform.position, out float ground) &&
                         born.transform.position.y >= ground - .5f,
                "hearth spawn places the villager on solid ground");
            yield return null;

            Container bag = VillagerInventory.Attach(born.gameObject, bornView);
            Add(bag.GetInventory(), "Wood");
            Add(bag.GetInventory(), "Wood");
            Vector3 where = born.transform.position;
            int woodBefore = LooseCount("Wood", where);
            yield return new WaitForSecondsRealtime(.2f);

            bool removed = VillagerLifecycle.Remove(colony, bornId);
            yield return new WaitForSecondsRealtime(.3f);
            report.Check(removed && !colony.State.GetMembers(ColonyMemberKind.Villager).Contains(bornId),
                "removing a villager drops it from colony membership");
            report.Check(ZDOMan.instance.GetZDO(bornId) == null &&
                         ZNetScene.instance.FindInstance(bornId) == null,
                "removing a villager destroys its ZDO and scene instance");
            report.Check(LooseCount("Wood", where) == woodBefore + 2,
                "removing a villager drops its carried items on the ground");

            // Control: an empty villager must destroy cleanly and drop nothing, proving the
            // drops above came from the bag rather than from removal itself.
            Villager emptied = VillagerLifecycle.Spawn(colony);
            if (emptied != null && emptied.TryGetComponent(out ZNetView emptyView) && emptyView.IsValid())
            {
                ZDOID emptyId = emptyView.GetZDO().m_uid;
                Vector3 emptyAt = emptied.transform.position;
                int anyBefore = LooseCount(string.Empty, emptyAt);
                yield return new WaitForSecondsRealtime(.2f);
                bool emptyRemoved = VillagerLifecycle.Remove(colony, emptyId);
                yield return new WaitForSecondsRealtime(.3f);
                report.Check(emptyRemoved && ZDOMan.instance.GetZDO(emptyId) == null &&
                             LooseCount(string.Empty, emptyAt) == anyBefore,
                    "removing an empty villager destroys it and creates no drops");
            }

            // Does a live bag actually reach the villager's ZDO? VillagerInventory exists to
            // stop a villager losing what it carries when its zone unloads, and that claim
            // rests on Container flushing to s_items. Uses Flint so it cannot disturb the
            // Coal counts the recovery check below depends on. Reported as a log rather than
            // an assertion: the verdict that matters is the cross-reload check in RunReload.
            Villager carrier = VillagerLifecycle.Spawn(colony);
            if (carrier != null && carrier.TryGetComponent(out ZNetView carrierView) && carrierView.IsValid())
            {
                ZDO carrierZdo = carrierView.GetZDO();
                Container carrierBag = VillagerInventory.Attach(carrier.gameObject, carrierView);
                Add(carrierBag.GetInventory(), "Flint");
                report.Check(Count(carrierBag.GetInventory(), "Flint") == 1,
                    "control: a live villager bag holds what was added to it");
                report.Check(StoredBagCount(carrierZdo, "Flint") == 1,
                    "a villager bag is persisted to its ZDO as soon as it changes");
                VillagerLifecycle.Remove(colony, carrierZdo.m_uid);
                yield return new WaitForSecondsRealtime(.2f);
            }

            // The stored-bag branch, exercised directly. It cannot be reached by faking an
            // unloaded villager: destroying the GameObject leaves a stale ZNetScene instance
            // entry that vanilla's OnZDODestroyed dereferences without a guard, which a real
            // unload never produces. The ZDO record is seeded here rather than through
            // Container.Save so this proves the decode-drop-clear logic itself, independent
            // of when the live container chooses to flush.
            Villager stored = VillagerLifecycle.Spawn(colony);
            if (stored != null && stored.TryGetComponent(out ZNetView storedView) && storedView.IsValid())
            {
                ZDO storedZdo = storedView.GetZDO();
                Container storedBag = VillagerInventory.Attach(stored.gameObject, storedView);
                Add(storedBag.GetInventory(), "Coal");

                Vector3 hearth = colony.transform.position;
                int coalBefore = LooseCount("Coal", hearth);
                report.Check(StoredBagCount(storedZdo, "Coal") == 1,
                    "control: a stored bag record is present before recovery");

                VillagerLifecycle.TestRecoverStoredBag(storedZdo, hearth);
                // Checked before yielding: this villager is still loaded, so its live bag
                // would re-persist the item on the next change. In the real unloaded case
                // there is no live container to write it back.
                report.Check(StoredBagCount(storedZdo, string.Empty) == 0,
                    "recovered bag is cleared so it cannot be claimed twice");
                yield return new WaitForSecondsRealtime(.3f);
                report.Check(LooseCount("Coal", hearth) == coalBefore + 1,
                    "an out-of-range villager's bag is recovered from its ZDO at the hearth");

                VillagerLifecycle.Remove(colony, storedZdo.m_uid);
                yield return new WaitForSecondsRealtime(.2f);
            }

            // Control: an unregistered villager is not ours to destroy.
            Villager stranger = Spawn<Villager>(VillagerPrefab.PrefabName, origin + Vector3.left * 14f);
            if (stranger != null && stranger.TryGetComponent(out ZNetView strangerView) && strangerView.IsValid())
            {
                ZDOID strangerId = strangerView.GetZDO().m_uid;
                report.Check(!VillagerLifecycle.Remove(colony, strangerId) &&
                             ZDOMan.instance.GetZDO(strangerId) != null,
                    "remove refuses a non-member and leaves it alive");
                ZNetScene.instance.Destroy(stranger.gameObject);
            }
        }

        /// <summary>
        ///     Drives a whole haul through ColonyJobEngine.Tick, which is the only method that
        ///     sequences select, move and act together.
        /// </summary>
        /// <remarks>
        ///     Every other executor check calls a leaf directly to avoid waiting on
        ///     pathfinding, so the composition between them has never been exercised in game.
        ///     Now that a job's steps come from its piece list rather than its type, that gap
        ///     is exactly where a regression would hide. Fixtures are placed within a generous
        ///     stop distance so movement completes on the first tick and no pathfinding is
        ///     involved; the sequencing is the subject, not the walking.
        /// </remarks>
        private static IEnumerator CheckHaulPipeline(TestReport report, Colony colony)
        {
            Villager worker = VillagerLifecycle.Spawn(colony);
            if (worker == null || !worker.TryGetComponent(out ZNetView view) || !view.IsValid() ||
                !worker.TryGetComponent(out MonsterAI ai))
            {
                report.Check(false, "haul pipeline runs end to end through the engine tick", "no worker");
                yield break;
            }

            Vector3 at = worker.transform.position;
            GameObject chest = Spawn("piece_chest_wood", at + Vector3.right * 3f);
            Register(colony, chest, "Pipeline destination");
            GameObject dropped = Spawn("Flint", at + Vector3.left * 3f);
            Container bag = VillagerInventory.Attach(worker.gameObject, view);
            Clear(bag.GetInventory());
            yield return new WaitForSecondsRealtime(.3f);

            ColonyJobConfig job = Job(ColonyJobType.HaulLoose, "Flint");
            job.Pieces.AddRange(JobPipeline.For(ColonyJobType.HaulLoose));
            job.Destination = chest.GetComponent<ZNetView>().GetZDO().m_uid;
            job.StopDistance = 12f;

            Inventory destination = chest.GetComponent<Container>().GetInventory();
            int before = Count(destination, "Flint");
            JobResult result = JobResult.Running;
            var actions = new List<string>();
            for (int tick = 0; tick < 80 && result != JobResult.Completed; tick++)
            {
                result = ColonyJobEngine.Tick(worker, ai, bag, colony, job, out string activity);
                if (actions.Count == 0 || actions[actions.Count - 1] != activity) actions.Add(activity);
                if (result == JobResult.Failed) break;
                yield return null;
            }
            yield return new WaitForSecondsRealtime(.2f);

            int after = Count(destination, "Flint");
            // Delivered amount is the drop's own stack size, not necessarily one.
            report.Check(result == JobResult.Completed && after > before && worker.State.StepCursor == 0,
                "haul pipeline runs end to end through the engine tick",
                $"result={result} stored={after} was={before} cursor={worker.State.StepCursor} " +
                $"steps={string.Join(" | ", actions.ToArray())}");

            // Control: the same job with no pieces cannot run. Without it this would pass
            // just as well if the walker were ignoring the piece list and hauling anyway.
            ColonyJobConfig empty = Job(ColonyJobType.HaulLoose, "Flint");
            empty.Destination = job.Destination;
            JobResult emptyResult = ColonyJobEngine.Tick(worker, ai, bag, colony, empty, out _);
            report.Check(emptyResult == JobResult.Skipped,
                "control: a job with no pipeline refuses to run", $"result={emptyResult}");

            if (dropped != null) ZNetScene.instance.Destroy(dropped);
            VillagerLifecycle.Remove(colony, view.GetZDO().m_uid);
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>
        ///     Drives a whole container-to-container transfer through the engine tick.
        /// </summary>
        /// <remarks>
        ///     Transfer is the job that shows why settings will need to live on pieces rather
        ///     than on the job: both the take and the put read the same filter list, so it can
        ///     only ever move one kind of item to somewhere else. Parity is the bar here - the
        ///     piece-driven path must do exactly what the type-driven one did.
        /// </remarks>
        private static IEnumerator CheckTransferPipeline(TestReport report, Colony colony)
        {
            Villager worker = VillagerLifecycle.Spawn(colony);
            if (worker == null || !worker.TryGetComponent(out ZNetView view) || !view.IsValid() ||
                !worker.TryGetComponent(out MonsterAI ai))
            {
                report.Check(false, "transfer pipeline runs end to end through the engine tick", "no worker");
                yield break;
            }

            Vector3 at = worker.transform.position;
            GameObject from = Spawn("piece_chest_wood", at + Vector3.forward * 3f);
            GameObject into = Spawn("piece_chest_wood", at + Vector3.back * 3f);
            Register(colony, from, "Pipeline source");
            Register(colony, into, "Pipeline sink");
            Inventory source = from.GetComponent<Container>().GetInventory();
            Inventory sink = into.GetComponent<Container>().GetInventory();
            Clear(source);
            Clear(sink);
            Add(source, "Flint");
            Container bag = VillagerInventory.Attach(worker.gameObject, view);
            Clear(bag.GetInventory());
            yield return new WaitForSecondsRealtime(.3f);

            ColonyJobConfig job = Job(ColonyJobType.Transfer, "Flint");
            job.Pieces.AddRange(JobPipeline.For(ColonyJobType.Transfer));
            job.Source = from.GetComponent<ZNetView>().GetZDO().m_uid;
            job.Destination = into.GetComponent<ZNetView>().GetZDO().m_uid;
            job.StopDistance = 12f;

            JobResult result = JobResult.Running;
            var steps = new List<string>();
            for (int tick = 0; tick < 80 && result != JobResult.Completed; tick++)
            {
                result = ColonyJobEngine.Tick(worker, ai, bag, colony, job, out string activity);
                if (steps.Count == 0 || steps[steps.Count - 1] != activity) steps.Add(activity);
                if (result == JobResult.Failed) break;
                yield return null;
            }
            yield return new WaitForSecondsRealtime(.2f);

            report.Check(result == JobResult.Completed && Count(sink, "Flint") == 1 &&
                         Count(source, "Flint") == 0 && worker.State.StepCursor == 0,
                "transfer pipeline moves an item between containers through the engine tick",
                $"result={result} moved={Count(sink, "Flint")} left={Count(source, "Flint")} " +
                $"cursor={worker.State.StepCursor} steps={string.Join(" | ", steps.ToArray())}");

            // Control: with nothing to take, the job must yield and move nothing. Without it
            // the check above would pass just as well if the destination were being filled by
            // something other than this pipeline.
            Clear(source);
            int settled = Count(sink, "Flint");
            // An explicitly chosen source is targeted whether or not it holds anything, so
            // the refusal appears a few ticks later when the take finds nothing. Run until
            // the pipeline comes to rest rather than judging it on its first tick.
            JobResult idle = JobResult.Running;
            for (int tick = 0; tick < 40 && idle == JobResult.Running; tick++)
            {
                idle = ColonyJobEngine.Tick(worker, ai, bag, colony, job, out _);
                yield return null;
            }
            report.Check(idle == JobResult.Skipped && Count(sink, "Flint") == settled,
                "control: transfer with an empty source comes to rest and moves nothing",
                $"result={idle} sink={Count(sink, "Flint")} was={settled}");

            VillagerLifecycle.Remove(colony, view.GetZDO().m_uid);
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>
        ///     The two resets differ in exactly one way, and the whole piece cursor depends on
        ///     it: releasing a target keeps a villager's place in its pipeline, abandoning the
        ///     job loses it. Asserted together so neither can quietly become the other.
        /// </summary>
        private static void CheckResetSemantics(TestReport report, Colony colony)
        {
            Villager subject = VillagerLifecycle.Spawn(colony);
            if (subject == null || !subject.TryGetComponent(out ZNetView view) || !view.IsValid()) return;
            VillagerState state = subject.State;

            state.SetStepCursor(4);
            state.ClearTarget();
            bool keptOnClear = state.StepCursor == 4;

            state.ResetJob();
            bool droppedOnReset = state.StepCursor == 0;

            report.Check(keptOnClear && droppedOnReset,
                "releasing a target keeps the pipeline cursor and abandoning the job clears it",
                $"afterClear={(keptOnClear ? 4 : -1)} afterReset={state.StepCursor}");
            VillagerLifecycle.Remove(colony, view.GetZDO().m_uid);
        }

        /// <summary>
        ///     Items of a prefab held in a villager's persisted bag, decoded straight from the
        ///     ZDO the bag writes through. Reads the stored record rather than a live Container
        ///     so it answers the same question before and after a reload.
        /// </summary>
        private static int StoredBagCount(ZDO zdo, string prefabName)
        {
            Inventory decoded = VillagerInventory.Stored(zdo);
            if (prefabName.Length == 0) return decoded.GetAllItems().Count;
            return Count(decoded, prefabName);
        }

        /// <summary>
        ///     Loose item drops of a prefab near a point; an empty name counts every drop.
        ///     Used to prove removal spilled a bag rather than destroying it.
        /// </summary>
        private static int LooseCount(string prefabName, Vector3 near)
        {
            int count = 0;
            foreach (ItemDrop drop in ItemDrop.s_instances)
            {
                if (drop == null || Vector3.Distance(drop.transform.position, near) > 4f) continue;
                if (prefabName.Length != 0 && Utils.GetPrefabName(drop.gameObject) != prefabName) continue;
                count += drop.m_itemData != null ? drop.m_itemData.m_stack : 1;
            }
            return count;
        }

        /// <summary>
        ///     Asserts every station prefab resolves to its own protocol. Protocol choice moved
        ///     from the job's type to what the target actually is, and several vanilla prefabs
        ///     carry more than one station component, so a wrong probe order would silently
        ///     operate the wrong half of a station.
        /// </summary>
        private static void CheckStationProtocols(TestReport report, Vector3 origin)
        {
            var expected = new[]
            {
                ("fire_pit", StructureCapability.Fireplace, "FireplaceProtocol"),
                ("smelter", StructureCapability.Smelter, "SmelterProtocol"),
                ("charcoal_kiln", StructureCapability.Smelter, "SmelterProtocol"),
                ("piece_cookingstation", StructureCapability.CookingStation, "CookingStationProtocol"),
                ("fermenter", StructureCapability.Fermenter, "FermenterProtocol"),
                ("piece_beehive", StructureCapability.BeeHive, "BeehiveProtocol")
            };
            bool allResolved = true;
            string detail = string.Empty;
            foreach ((string prefab, StructureCapability capability, string protocol) in expected)
            {
                GameObject probe = Spawn(prefab, origin + Vector3.forward * 24f);
                string actual = ColonyJobEngine.TestResolveProtocol(probe, capability);
                if (actual != protocol) { allResolved = false; detail += $"{prefab}={actual} "; }
                if (probe != null) ZNetScene.instance.Destroy(probe);
            }

            // Control: a container is not a station. Without this the claim above would pass
            // just as well if the resolver returned the first protocol for anything.
            GameObject chest = Spawn("piece_chest_wood", origin + Vector3.forward * 27f);
            bool chestRefused = ColonyJobEngine.TestResolveProtocol(chest, StructureCapability.None).Length == 0;
            if (chest != null) ZNetScene.instance.Destroy(chest);

            report.Check(allResolved && chestRefused,
                "every station resolves its own protocol and a container resolves none", detail);
        }

        /// <summary>
        ///     Asserts each station prefab still exposes the vanilla RPCs the executors invoke,
        ///     so a game update that renames or removes one fails here with a clear message
        ///     rather than as silent no-op work in the field.
        /// </summary>
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
            Register(colony, bagObject, "Benchmark inventory");
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

            yield return CheckStation(report, colony, state, bag, origin, "fire_pit",
                Job(ColonyJobType.FuelFireplaces, "Wood"), "Wood", "fuel fireplaces");
            yield return CheckStation(report, colony, state, bag, origin, "smelter",
                Job(ColonyJobType.OperateSmelters, "CopperOre"), "CopperOre", "operate smelters");
            yield return CheckStation(report, colony, state, bag, origin, "charcoal_kiln",
                Job(ColonyJobType.OperateSmelters, "Wood"), "Wood", "operate charcoal kilns");

            GameObject cooking = Spawn("piece_cookingstation", origin + Vector3.right * 12f);
            Register(colony, cooking, "Benchmark cooking station");
            CookingStation cookingComponent = cooking != null ? cooking.GetComponent<CookingStation>() : null;
            string cookable = FirstAllowed(cookingComponent, "RawMeat", "DeerMeat", "NeckTail", "FishRaw");
            yield return CheckStation(report, state, bag, origin, cooking,
                Job(ColonyJobType.OperateCookingStations, cookable), cookable, "operate cooking stations");

            GameObject fermenter = Spawn("fermenter", origin + Vector3.right * 15f);
            Register(colony, fermenter, "Benchmark fermenter");
            Fermenter fermenterComponent = fermenter != null ? fermenter.GetComponent<Fermenter>() : null;
            string fermentable = FirstAllowed(fermenterComponent, "BarleyWineBase", "MeadBaseHealthMinor",
                "MeadBaseStaminaMinor", "MeadBasePoisonResist");
            yield return CheckStation(report, state, bag, origin, fermenter,
                Job(ColonyJobType.OperateFermenters, fermentable), fermentable, "operate fermenters");

            Clear(bag);
            GameObject hiveObject = Spawn("piece_beehive", origin + Vector3.right * 18f);
            Register(colony, hiveObject, "Benchmark beehive");
            Beehive hive = hiveObject != null ? hiveObject.GetComponent<Beehive>() : null;
            if (hive != null) hive.m_secPerUnit = .01f;
            yield return new WaitForSecondsRealtime(1f);
            if (hive != null) hive.UpdateBees();
            ColonyJobConfig hiveJob = Job(ColonyJobType.CollectBeehives, "Honey");
            ZNetView destinationView = destinationObject.GetComponent<ZNetView>();
            hiveJob.Destination = destinationView.GetZDO().m_uid;
            int honeyBefore = Count(destinationObject.GetComponent<Container>().GetInventory(), "Honey");
            JobResult hiveResult = ColonyJobEngine.TestOperate(hiveObject, bag, hiveJob, state, out _);

            // Extraction spawns honey as a world drop, and how many frames that takes is not
            // ours to control. A fixed wait made this assertion flaky; retry to a deadline
            // instead, the same way the transfer check waits out an ownership handshake.
            ItemDrop honey = null;
            for (int attempt = 0; attempt < 20 && honey == null; attempt++)
            {
                yield return new WaitForSecondsRealtime(.1f);
                honey = ItemDrop.s_instances.FirstOrDefault(drop => drop != null &&
                    Utils.GetPrefabName(drop.gameObject) == "Honey");
            }
            bool honeyDropped = honey != null;
            if (honeyDropped)
            {
                state.SetRuntimePhase("pickup-collect");
                for (int attempt = 0; attempt < 10 && Count(bag, "Honey") == 0; attempt++)
                {
                    ColonyJobEngine.TestPickup(honey.gameObject, bag, state, out _);
                    yield return new WaitForSecondsRealtime(.1f);
                }
            }
            JobResult honeyDeposit = Count(bag, "Honey") > 0
                ? ColonyJobEngine.TestDeposit(destinationObject, bag, hiveJob, state, out _)
                : JobResult.Failed;
            report.Check(hive != null && hiveResult == JobResult.Running && honeyDeposit == JobResult.Completed &&
                         Count(destinationObject.GetComponent<Container>().GetInventory(), "Honey") == honeyBefore + 1,
                "collect beehives extracts, picks up and stores honey",
                $"result={hiveResult} dropped={honeyDropped} deposit={honeyDeposit} " +
                $"stored={Count(destinationObject.GetComponent<Container>().GetInventory(), "Honey")} was={honeyBefore}");

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

        private static IEnumerator CheckStation(TestReport report, Colony colony, VillagerState state, Inventory bag,
            Vector3 origin, string prefab, ColonyJobConfig job, string item, string label)
        {
            GameObject target = Spawn(prefab, origin + Vector3.right * UnityEngine.Random.Range(8f, 20f));
            Register(colony, target, "Benchmark " + prefab);
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

        /// <summary>
        ///     Negative controls. Each positive claim about claims, keep-alive, limits, or
        ///     compatibility is paired with a case that must fail, so a check cannot pass
        ///     merely because the mechanism never ran.
        /// </summary>
        private static void CheckPairedControls(TestReport report, Colony colony, Villager villager,
            Vector3 origin, ZDOID target)
        {
            bool claims = ModConfig.ClaimsEnabled.Value;
            bool keepAlive = ModConfig.KeepAliveEnabled.Value;
            Villager other = Spawn<Villager>(VillagerPrefab.PrefabName, origin + Vector3.back * 10f);
            if (other != null && other.TryGetComponent(out ZNetView otherView))
                colony.Register(ColonyMemberKind.Villager, otherView);
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

        /// <summary>
        ///     Verifies a prefab carries component <typeparamref name="T"/> and that every named
        ///     RPC or method still exists on it.
        /// </summary>
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
            return UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity);
        }
    }
}
