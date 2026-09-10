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
            report.Check(colony.State.GetJobs().Count == ColonyJobCatalog.All.Length,
                "a starter job for every kind of work persists",
                $"saved={colony.State.GetJobs().Count} kinds={ColonyJobCatalog.All.Length}");

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
            yield return CheckDestinationChoice(report, colony);
            yield return CheckDeclaredSettings(report, colony);
            yield return CheckOutfit(report, colony);
            CheckRegisterableContainers(report, colony);
            yield return CheckChopping(report, colony);
            yield return CheckChopReservation(report, colony);
            yield return CheckStationJob(report, colony);
            yield return CheckHiveJob(report, colony);
            CheckStationContracts(report);
            CheckStationProtocols(report, origin);
            // Counting the catalogue against itself would prove nothing, so these ask what a
            // complete catalogue actually means: every kind of work is implemented, has a
            // starter job, and has a name of its own. A new job type that someone forgets to
            // finish fails here rather than turning up in the panel under somebody else's name,
            // which is what a plausible-looking fallback did the first time this was added.
            report.Check(ColonyJobCatalog.All.All(type => Jobs.Work.WorkRegistry.For(type) != null),
                "every job type is work the colony knows how to do");
            report.Check(ColonyJobCatalog.CreateDefaults().Count == ColonyJobCatalog.All.Length,
                "every job type has a starter job");
            report.Check(ColonyJobCatalog.All.All(type => ColonyJobCatalog.DisplayName(type).Length > 0) &&
                         ColonyJobCatalog.All.Select(ColonyJobCatalog.DisplayName).Distinct().Count()
                         == ColonyJobCatalog.All.Length,
                "every job type has a name of its own");
            report.Check(ColonyJobCatalog.All.All(type => ColonyJobCatalog.Describe(type).Length > 0) &&
                         ColonyJobCatalog.All.Select(ColonyJobCatalog.Describe).Distinct().Count()
                         == ColonyJobCatalog.All.Length,
                "every job type says what it does");

            // Bounds the window in which the snapshot's target can go missing: this is the
            // last moment the acceptance run controls, and PreparePersistenceSnapshot logs the
            // same thing at the first moment it does.
            StructureRecord storage = colony.State.GetStructures()
                .FirstOrDefault(record => record.Name == "Renamed storage");
            Core.Log.Info($"[Benchmark] acceptance end: structures={colony.State.GetStructures().Count} " +
                          $"storage={(storage == null ? "missing" : storage.Id.ToString())} " +
                          $"live={(storage != null && ZDOMan.instance.GetZDO(storage.Id) != null)}");

            // The runtime snapshot the reload phase verifies is written by
            // PreparePersistenceSnapshot, from the controller, immediately before the world is
            // saved. There used to be a second copy of it here too; the two disagreed about
            // what the villager's target should be, the later one won, and a failure in the
            // reload phase named whichever object this one had chosen. One writer only.

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
            report.Check(state.GetJobs().Count == ColonyJobCatalog.All.Length,
                "job configurations survived save and relaunch",
                $"loaded={state.GetJobs().Count} kinds={ColonyJobCatalog.All.Length}");
            report.Check(state.GetJobs().All(job => Jobs.Work.WorkRegistry.For(job.Type) != null),
                "job types survived save and relaunch");
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
                // Without saying which field moved, a failure here is a four-minute guess.
                // It was, repeatedly.
                report.Check(villager.QueueProgress == 7 && villager.RuntimePhase == "acceptance-persisted" &&
                             !villager.StepTarget.IsNone(),
                    "active target and runtime progress survived save and relaunch",
                    $"progress={villager.QueueProgress} phase='{villager.RuntimePhase}' " +
                    $"stored[{villager.DescribeTarget()}]");
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
            Core.Log.Info($"[Benchmark] snapshot start: structures={colony.State.GetStructures().Count} " +
                          $"storage={target.Id} live={(ZDOMan.instance.GetZDO(target.Id) != null)}");
            VillagerState persisted = new VillagerState(zdo);
            persisted.SetQueue(new List<string> { "acceptance.persistence.a", "acceptance.persistence.b" });
            persisted.SetQueuePosition(1);
            persisted.SetQueueAttempt(1);
            persisted.SetQueueProgress(7);
            persisted.SetRuntimePhase("acceptance-persisted");

            // The reference points at the colony, not at a chest.
            //
            // A reference survives a reload only if it carries a token, and only the owner of
            // the thing referenced can mint one. Owning it requires it to be resident, and by
            // the time this runs it may not be: the phase before this one moves the view
            // about, and ZDOs outside the loaded region are released. Measured across the
            // window, the chest was resident at the end of the acceptance run and gone by the
            // start of this one - so the reference was written as a bare runtime address,
            // which the load renumbers, and the reload phase reported the field as lost.
            //
            // The colony is resident by construction here, which makes this assert the save
            // rather than which zones happened to be loaded. It is still a genuine cross-ZDO
            // reference, which is the thing being tested.
            ZDOID anchor = colony.TryGetComponent(out ZNetView colonyView) && colonyView.IsValid()
                ? colonyView.GetZDO().m_uid
                : target.Id;
            persisted.SetStepTarget(anchor);
            Core.Log.Info($"[Benchmark] snapshot target {persisted.DescribeTarget()}");
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
                report.Check(false, "the haul job runs end to end through the engine tick", "no worker");
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
                "the haul job runs end to end through the engine tick",
                $"result={result} stored={after} was={before} cursor={worker.State.StepCursor} " +
                $"steps={string.Join(" | ", actions.ToArray())}");

            // Control: the same job asking for something that is not lying about must come
            // to rest and carry nothing. Without it the check above would pass just as well
            // if hauling ignored its filter and fetched whatever it found first.
            Clear(bag.GetInventory());
            ColonyJobConfig absent = Job(ColonyJobType.HaulLoose, "Ruby");
            absent.Destination = job.Destination;
            absent.StopDistance = 12f;
            JobResult absentResult = ColonyJobEngine.Tick(worker, ai, bag, colony, absent, out _);
            report.Check(absentResult == JobResult.Skipped && Count(bag.GetInventory(), "Flint") == 0,
                "control: a haul job whose item is not on the ground carries nothing",
                $"result={absentResult} bag={Count(bag.GetInventory(), "Flint")}");

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
                report.Check(false, "the transfer job runs end to end through the engine tick", "no worker");
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
                "the transfer job moves an item between containers through the engine tick",
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

        /// <summary>Runs a job through the engine tick until it completes or gives up.</summary>
        private static IEnumerator Run(Villager worker, MonsterAI ai, Container bag, Colony colony,
            ColonyJobConfig job)
        {
            JobResult result = JobResult.Running;
            for (int tick = 0; tick < 80 && result != JobResult.Completed; tick++)
            {
                result = ColonyJobEngine.Tick(worker, ai, bag, colony, job, out _);
                if (result == JobResult.Failed || result == JobResult.Skipped) break;
                yield return null;
            }
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>
        ///     Covers the two settings that decide where a load ends up: a pile on the ground,
        ///     or a container that can actually take it.
        /// </summary>
        /// <remarks>
        ///     Choosing a container is where a haul most easily wastes a journey. Ordinary
        ///     selection would pick the first eligible one and only discover it is full on
        ///     arrival, so the check puts a full chest and an empty one in front of a villager
        ///     and asserts which one the item reaches. The control fills both: with nowhere to
        ///     put anything it must come to rest still carrying, rather than pick one anyway.
        /// </remarks>
        private static IEnumerator CheckDestinationChoice(TestReport report, Colony colony)
        {
            Villager worker = VillagerLifecycle.Spawn(colony);
            if (worker == null || !worker.TryGetComponent(out ZNetView view) || !view.IsValid() ||
                !worker.TryGetComponent(out MonsterAI ai))
            {
                report.Check(false, "a job puts its load down or in a container with room", "no worker");
                yield break;
            }
            Container bag = VillagerInventory.Attach(worker.gameObject, view);
            Vector3 at = worker.transform.position;

            // A pile: carry something and ask for it on the ground rather than in a chest.
            Clear(bag.GetInventory());
            Add(bag.GetInventory(), "Flint");
            ColonyJobConfig pile = Job(ColonyJobType.HaulLoose, "Flint");
            pile.DropOnGround = true;
            pile.StopDistance = 12f;
            yield return Run(worker, ai, bag, colony, pile);
            bool droppedIt = Count(bag.GetInventory(), "Flint") == 0 && LooseCount("Flint", at) > 0;

            // Room: one full chest, one with space.
            GameObject full = Spawn("piece_chest_wood", at + Vector3.right * 3f);
            GameObject roomy = Spawn("piece_chest_wood", at + Vector3.left * 3f);
            StructureRecord fullRecord = Register(colony, full, "Full chest");
            StructureRecord roomyRecord = Register(colony, roomy, "Roomy chest");
            Inventory fullBox = full.GetComponent<Container>().GetInventory();
            Inventory roomyBox = roomy.GetComponent<Container>().GetInventory();
            Clear(fullBox); Clear(roomyBox);
            Fill(fullBox, "Wood");
            Clear(bag.GetInventory());
            Add(bag.GetInventory(), "Flint");

            ColonyJobConfig choose = Job(ColonyJobType.HaulLoose, "Flint");
            // Scope the choice to this check's two chests. The colony already holds containers
            // registered by earlier checks, and one of those has room - choosing it would be
            // correct behaviour against a polluted fixture rather than a fault.
            choose.Targets = TargetMode.Selected;
            if (fullRecord != null) choose.SelectedStructures.Add(fullRecord.Id);
            if (roomyRecord != null) choose.SelectedStructures.Add(roomyRecord.Id);
            choose.StopDistance = 12f;
            yield return new WaitForSecondsRealtime(.3f);
            var chooseSteps = new List<string>();
            JobResult chooseResult = JobResult.Running;
            for (int tick = 0; tick < 80 && chooseResult != JobResult.Completed; tick++)
            {
                chooseResult = ColonyJobEngine.Tick(worker, ai, bag, colony, choose, out string step);
                if (chooseSteps.Count == 0 || chooseSteps[chooseSteps.Count - 1] != step) chooseSteps.Add(step);
                if (chooseResult == JobResult.Failed || chooseResult == JobResult.Skipped) break;
                yield return null;
            }
            yield return new WaitForSecondsRealtime(.2f);
            int reachedRoomy = Count(roomyBox, "Flint");
            int reachedFull = Count(fullBox, "Flint");
            bool avoidedFull = reachedRoomy == 1 && reachedFull == 0;

            // Control: with every container full it must refuse rather than choose one.
            Clear(roomyBox);
            Fill(roomyBox, "Wood");
            Clear(bag.GetInventory());
            Add(bag.GetInventory(), "Flint");
            yield return new WaitForSecondsRealtime(.2f);
            JobResult stuck = JobResult.Running;
            for (int tick = 0; tick < 40 && stuck == JobResult.Running; tick++)
            {
                stuck = ColonyJobEngine.Tick(worker, ai, bag, colony, choose, out _);
                yield return null;
            }
            bool refused = stuck == JobResult.Skipped && Count(bag.GetInventory(), "Flint") == 1;

            report.Check(droppedIt && avoidedFull && refused,
                "a job puts its load down or in a container with room, and refuses a full one",
                $"dropped={droppedIt} roomy={reachedRoomy} full={reachedFull} " +
                $"choose={chooseResult} steps={string.Join(" | ", chooseSteps.ToArray())} " +
                $"whenNoRoom={stuck} stillCarrying={Count(bag.GetInventory(), "Flint")}");

            VillagerLifecycle.Remove(colony, view.GetZDO().m_uid);
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>
        ///     Proves a job ignores the settings it declares it does not read.
        /// </summary>
        /// <remarks>
        ///     The panel hides a setting a job does not use, which is only honest if the job
        ///     really ignores it. So a haul job is given a source container holding exactly what
        ///     it wants, and must take from the ground anyway and leave the container alone.
        ///     The control is a transfer job with the same source, which must empty it - without
        ///     that, this would pass just as well if the source were unreachable or empty.
        /// </remarks>
        private static IEnumerator CheckDeclaredSettings(TestReport report, Colony colony)
        {
            Villager worker = VillagerLifecycle.Spawn(colony);
            if (worker == null || !worker.TryGetComponent(out ZNetView view) || !view.IsValid() ||
                !worker.TryGetComponent(out MonsterAI ai))
            {
                report.Check(false, "a job ignores the settings it does not declare", "no worker");
                yield break;
            }

            Vector3 at = worker.transform.position;
            GameObject from = Spawn("piece_chest_wood", at + Vector3.forward * 3f);
            GameObject into = Spawn("piece_chest_wood", at + Vector3.back * 3f);
            Register(colony, from, "Ignored source");
            Register(colony, into, "Declared sink");
            Inventory source = from.GetComponent<Container>().GetInventory();
            Inventory sink = into.GetComponent<Container>().GetInventory();
            Container bag = VillagerInventory.Attach(worker.gameObject, view);
            ZDOID fromId = from.GetComponent<ZNetView>().GetZDO().m_uid;
            ZDOID intoId = into.GetComponent<ZNetView>().GetZDO().m_uid;

            Clear(source); Clear(sink); Clear(bag.GetInventory());
            Add(source, "Flint");
            GameObject dropped = Spawn("Flint", at + Vector3.right * 3f);
            yield return new WaitForSecondsRealtime(.3f);

            ColonyJobConfig haul = Job(ColonyJobType.HaulLoose, "Flint");
            haul.Source = fromId;
            haul.Destination = intoId;
            haul.StopDistance = 12f;
            yield return Run(worker, ai, bag, colony, haul);
            // Captured now: the control below empties this container, so reading it at report
            // time would print the same thing whether the check passed or failed.
            int leftInSource = Count(source, "Flint");
            bool tookFromGround = Count(sink, "Flint") > 0 && leftInSource == 1;

            // Control: the same source, read by work that declares it.
            Clear(sink); Clear(bag.GetInventory());
            ColonyJobConfig transfer = Job(ColonyJobType.Transfer, "Flint");
            transfer.Source = fromId;
            transfer.Destination = intoId;
            transfer.StopDistance = 12f;
            yield return new WaitForSecondsRealtime(.2f);
            yield return Run(worker, ai, bag, colony, transfer);
            bool tookFromSource = Count(source, "Flint") == 0 && Count(sink, "Flint") > 0;

            report.Check(tookFromGround && tookFromSource,
                "a job ignores a setting it does not declare, and reads one it does",
                $"haulLeftSource={leftInSource} transferEmptiedIt={tookFromSource}");

            if (dropped != null) ZNetScene.instance.Destroy(dropped);
            VillagerLifecycle.Remove(colony, view.GetZDO().m_uid);
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>
        ///     Proves a villager fetches what its outfit asks for and ends up wearing it.
        /// </summary>
        /// <remarks>
        ///     Two claims, and the second is the one that can quietly be false. Fetching is
        ///     ordinary work and shows up in the bag. Wearing goes through the game's own
        ///     visible-equipment path, which resolves an item from a prefab hash and only
        ///     renders it if that hash means something - so the check waits for the model to
        ///     actually show the piece rather than trusting that the write happened.
        ///
        ///     The control is a villager whose containers hold nothing its outfit names. It
        ///     must stay bare, which is what distinguishes this from a villager that would have
        ///     ended up dressed regardless.
        /// </remarks>
        private static IEnumerator CheckOutfit(TestReport report, Colony colony)
        {
            const string Piece = "ArmorLeatherChest";
            List<Outfit> outfits = colony.State.GetEffectiveOutfits();
            Outfit uniform = new Outfit { Name = "Benchmark uniform" };
            uniform[OutfitSlot.Chest] = Piece;
            outfits.RemoveAll(entry => entry.Name == uniform.Name);
            outfits.Add(uniform);
            colony.State.SetOutfits(outfits);

            Villager dressed = VillagerLifecycle.Spawn(colony);
            Villager bare = VillagerLifecycle.Spawn(colony);
            if (dressed == null || bare == null ||
                !dressed.TryGetComponent(out ZNetView dressedView) ||
                !bare.TryGetComponent(out ZNetView bareView) ||
                !dressed.TryGetComponent(out MonsterAI ai) ||
                !dressed.TryGetComponent(out VisEquipment vis))
            {
                report.Check(false, "a villager fetches its outfit and wears it", "no worker");
                yield break;
            }

            Vector3 at = dressed.transform.position;
            GameObject wardrobe = Spawn("piece_chest_wood", at + Vector3.right * 3f);
            Register(colony, wardrobe, "Benchmark wardrobe");
            Inventory store = wardrobe.GetComponent<Container>().GetInventory();
            Container bag = VillagerInventory.Attach(dressed.gameObject, dressedView);
            Clear(store);
            Clear(bag.GetInventory());
            bool staged = Add(store, Piece);
            dressed.State.SetOutfitName(uniform.Name);
            bare.State.SetOutfitName(uniform.Name);
            yield return new WaitForSecondsRealtime(.3f);

            ColonyJobConfig equip = Job(ColonyJobType.Equip, string.Empty);
            equip.Source = wardrobe.GetComponent<ZNetView>().GetZDO().m_uid;
            equip.StopDistance = 12f;
            JobResult result = JobResult.Running;
            var steps = new List<string>();
            for (int tick = 0; tick < 80 && result != JobResult.Completed; tick++)
            {
                result = ColonyJobEngine.Tick(dressed, ai, bag, colony, equip, out string activity);
                if (steps.Count == 0 || steps[steps.Count - 1] != activity) steps.Add(activity);
                if (result == JobResult.Failed || result == JobResult.Skipped) break;
                yield return null;
            }

            // Wearing is the villager's own business, done on its owned tick, and the model
            // only shows the piece once the game has resolved and instantiated it.
            int expected = Piece.GetStableHashCode();
            for (int attempt = 0; attempt < 40 && vis.m_currentChestItemHash != expected; attempt++)
                yield return new WaitForSecondsRealtime(.1f);
            bool wearing = result == JobResult.Completed && Count(bag.GetInventory(), Piece) == 1 &&
                           vis.m_currentChestItemHash == expected;

            // Control: same outfit, nothing to fetch.
            Clear(store);
            Container bareBag = VillagerInventory.Attach(bare.gameObject, bareView);
            Clear(bareBag.GetInventory());
            VisEquipment bareVis = bare.GetComponent<VisEquipment>();
            bare.TryGetComponent(out MonsterAI bareAi);
            JobResult idle = JobResult.Running;
            for (int tick = 0; tick < 40 && idle == JobResult.Running; tick++)
            {
                idle = ColonyJobEngine.Tick(bare, bareAi, bareBag, colony, equip, out _);
                yield return null;
            }
            yield return new WaitForSecondsRealtime(.5f);
            bool stayedBare = idle == JobResult.Skipped &&
                              Count(bareBag.GetInventory(), Piece) == 0 &&
                              bareVis != null && bareVis.m_currentChestItemHash != expected;

            report.Check(staged && wearing && stayedBare,
                "a villager fetches the outfit it lacks and wears it, and one with none stays bare",
                $"staged={staged} result={result} held={Count(bag.GetInventory(), Piece)} " +
                $"shown={vis.m_currentChestItemHash == expected} control={idle} " +
                $"controlShown={(bareVis != null && bareVis.m_currentChestItemHash == expected)} " +
                $"steps={string.Join(" | ", steps.ToArray())}");

            VillagerLifecycle.Remove(colony, dressedView.GetZDO().m_uid);
            VillagerLifecycle.Remove(colony, bareView.GetZDO().m_uid);
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>
        ///     A container that is not a build piece can still be registered.
        /// </summary>
        /// <remarks>
        ///     Discovery used to walk Valheim's piece registry, which holds only what the
        ///     hammer builds. A cart is a perfectly good container a player would expect a
        ///     colony to draw from, and it was invisible - the villager reported "no source
        ///     item" while the axe sat in the cart in front of them. The control is a loose
        ///     item drop, which must stay unregisterable: the point is to widen what counts as
        ///     a container, not to let anything at all become one.
        /// </remarks>
        private static void CheckRegisterableContainers(TestReport report, Colony colony)
        {
            Vector3 at = colony.transform.position + Vector3.right * 6f;
            GameObject cart = Spawn("Cart", at);
            GameObject loose = Spawn("Wood", at + Vector3.forward * 2f);
            if (cart == null || loose == null)
            {
                report.Check(false, "a container that is not a build piece can be registered",
                    $"fixtures missing: cart={(cart != null)} loose={(loose != null)}");
                return;
            }

            StructureRecord cartRecord = StructureRegistry.Describe(cart);
            StructureRecord looseRecord = StructureRegistry.Describe(loose);
            bool cartCounts = cartRecord != null &&
                              (cartRecord.Capabilities & StructureCapability.Container) != 0;
            bool sweepFindsIt = StructureRegistry.FindRegisterable(colony)
                .Exists(record => cartRecord != null && record.Id == cartRecord.Id);

            report.Check(cartCounts && sweepFindsIt && looseRecord == null,
                "a container that is not a build piece can be registered, and a loose item cannot",
                $"cart={(cartRecord == null ? "rejected: " + StructureRegistry.Explain(cart) : cartRecord.Capabilities.ToString())} " +
                $"inSweep={sweepFindsIt} loose={(looseRecord == null ? "rejected" : "accepted")}");

            Release(cart);
            Release(loose);
        }

        /// <summary>
        ///     Chopping, and the three ways it can silently do nothing.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         A blow that lands and a blow the game quietly discarded look identical from
        ///         the outside, so every claim here is measured as health before against health
        ///         after. That is readable because the call is synchronous once the object is
        ///         ours - which is itself the first thing that can go wrong, since a tree the
        ///         world generated has no owner and every peer declines to damage it.
        ///     </para>
        ///     <para>
        ///         Three controls, because each is a way for the job to look busy and achieve
        ///         nothing: a tree too hard for the axe, a tree outside the radius that must
        ///         never be chosen, and a second villager that must not take a tree the first
        ///         has claimed.
        ///     </para>
        /// </remarks>
        private static IEnumerator CheckChopping(TestReport report, Colony colony)
        {
            Villager worker = VillagerLifecycle.Spawn(colony);
            if (worker == null || !worker.TryGetComponent(out ZNetView view) || !view.IsValid() ||
                !worker.TryGetComponent(out MonsterAI ai))
            {
                report.Check(false, "a villager fells a tree", "no worker");
                yield break;
            }

            // A clearing of its own, well away from the run's chests and stations. Trees
            // fall, logs roll, and both do damage where they land: felling one beside the
            // fixtures destroyed the chest another check's assertion pointed at, which
            // surfaced two phases later as a persistence failure.
            Vector3 at = colony.transform.position + Vector3.forward * 40f;
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(at, out float ground))
                at.y = ground + .2f;
            worker.transform.position = at;
            Container bag = VillagerInventory.Attach(worker.gameObject, view);
            Clear(bag.GetInventory());
            bool armed = Add(bag.GetInventory(), "AxeStone");

            // Named by the index rather than by this file. Prefab names are asset data and
            // guessing one produced a fixture that silently did not exist, which read as the
            // feature being broken.
            Resources.ResourceIndex.Rebuild();
            // Everything the job could reach, so the only trees in range are this check's.
            ClearTrees(colony.transform.position, 60f);
            ClearTrees(at, 40f);
            string soft = Resources.ResourceIndex.SampleTree(0, 0);
            string hard = Resources.ResourceIndex.SampleTree(2, int.MaxValue);
            GameObject beech = soft.Length == 0 ? null : Spawn(soft, at + Vector3.right * 4f);
            GameObject oak = hard.Length == 0 ? null : Spawn(hard, at + Vector3.left * 4f);
            GameObject distant = soft.Length == 0 ? null : Spawn(soft, colony.transform.position + Vector3.forward * 90f);
            bool fixtures = beech != null && oak != null && distant != null;
            Resources.ColonyResources.Clear();
            yield return new WaitForSecondsRealtime(.5f);
            if (!fixtures)
            {
                report.Check(false, "a villager fells a tree, refuses one its axe cannot cut, and ignores one out of range",
                    $"fixtures missing: soft='{soft}' hard='{hard}'");
                VillagerLifecycle.Remove(colony, view.GetZDO().m_uid);
                yield break;
            }

            ColonyJobConfig chop = Job(ColonyJobType.Chop, string.Empty);
            chop.StopDistance = 12f;
            // Wide enough to reach the clearing, narrow enough to exclude the far tree the
            // range control depends on. Distance is measured from the hearth, not the villager.
            chop.SearchRadius = 55f;

            // The oak is the tier control and must not be chopped, so it is out of scope
            // while the beech is being felled and put back for its own check below.
            float oakBefore = Health(oak);
            float distantBefore = Health(distant);
            float beechBefore = Health(beech);
            ZNetScene.instance.Destroy(oak);
            yield return new WaitForSecondsRealtime(.2f);

            JobResult result = JobResult.Running;
            var steps = new List<string>();
            for (int tick = 0; tick < 120 && result != JobResult.Completed; tick++)
            {
                result = ColonyJobEngine.Tick(worker, ai, bag, colony, chop, out string activity);
                if (steps.Count == 0 || steps[steps.Count - 1] != activity) steps.Add(activity);
                if (result == JobResult.Failed || result == JobResult.Skipped) break;
                yield return null;
            }
            yield return new WaitForSecondsRealtime(.3f);
            float beechAfter = Health(beech);
            bool chopped = beechAfter < beechBefore;

            // The documented surprise: felling produces a log, not wood. A colony that never
            // cut its logs up would look busy and fill no chests, so this is asserted rather
            // than left as folklore - but only for species that have a log to leave, and the
            // trunk is looked for over a wide area because it falls and slides.
            bool expectsALog = Resources.ResourceIndex.LeavesALog(soft);
            bool leftALog = beech == null && (!expectsALog || Nearby<TreeLog>(at, 60f) > 0);

            // Control: too hard for a stone axe. It must say so and stop, not swing forever.
            ClearFelled(at, 80f);
            ClearFelled(colony.transform.position, 80f);
            GameObject hardOak = Spawn(hard, at + Vector3.left * 4f);
            Resources.ColonyResources.Clear();
            worker.State.ResetJob();
            yield return new WaitForSecondsRealtime(.3f);
            float hardBefore = Health(hardOak);
            JobResult tooHard = JobResult.Running;
            for (int tick = 0; tick < 60 && tooHard == JobResult.Running; tick++)
            {
                tooHard = ColonyJobEngine.Tick(worker, ai, bag, colony, chop, out _);
                yield return null;
            }
            yield return new WaitForSecondsRealtime(.2f);
            // Captured now: this control's tree is destroyed before the next one runs, so
            // reading it at report time would print zero whether it was chopped or not.
            float hardAfter = Health(hardOak);
            bool refusedOak = tooHard == JobResult.Skipped && hardAfter >= hardBefore;

            // Control: out of range. Shrink the radius so only the distant tree is left, and
            // it must be ignored rather than walked to.
            Release(hardOak);
            ClearFelled(at, 80f);
            ClearFelled(colony.transform.position, 80f);
            Resources.ColonyResources.Clear();
            worker.State.ResetJob();
            // The far tree is inside the scan but outside the job, which is the claim. A
            // radius small enough to exclude everything would pass whether it existed or not.
            chop.SearchRadius = 55f;
            yield return new WaitForSecondsRealtime(.3f);
            JobResult outOfRange = JobResult.Running;
            for (int tick = 0; tick < 40 && outOfRange == JobResult.Running; tick++)
            {
                outOfRange = ColonyJobEngine.Tick(worker, ai, bag, colony, chop, out _);
                yield return null;
            }
            bool ignoredDistant = outOfRange == JobResult.Skipped && Health(distant) >= distantBefore;

            report.Check(armed && fixtures && chopped && leftALog && refusedOak && ignoredDistant,
                "a villager fells a tree, refuses one its axe cannot cut, and ignores one out of range",
                $"armed={armed} soft={soft} hard={hard} health={beechBefore}->{beechAfter} " +
                $"log={leftALog} expectsLog={expectsALog} logs={Nearby<TreeLog>(at, 60f)} " +
                $"tooHard={tooHard} oak={hardBefore}->{hardAfter} " +
                $"outOfRange={outOfRange} steps={string.Join(" | ", steps.ToArray())}");

            if (distant != null) ZNetScene.instance.Destroy(distant);
            ClearFelled(at, 60f);
            VillagerLifecycle.Remove(colony, view.GetZDO().m_uid);
            yield return new WaitForSecondsRealtime(.2f);
            Resources.ColonyResources.Clear();
        }

        /// <summary>
        ///     Two villagers, one tree: the second must find something else to do.
        /// </summary>
        /// <remarks>
        ///     Without this a colony looks like it is working twice as fast and is not: both
        ///     villagers walk to the same trunk, one of them lands every blow and the other
        ///     stands there. The control turns reservations off and the second villager does
        ///     take the tree, which is what proves the refusal came from the claim rather than
        ///     from the tree being unfindable.
        /// </remarks>
        private static IEnumerator CheckChopReservation(TestReport report, Colony colony)
        {
            Villager first = VillagerLifecycle.Spawn(colony);
            Villager second = VillagerLifecycle.Spawn(colony);
            if (first == null || second == null ||
                !first.TryGetComponent(out ZNetView firstView) || !second.TryGetComponent(out ZNetView secondView) ||
                !first.TryGetComponent(out MonsterAI firstAi) || !second.TryGetComponent(out MonsterAI secondAi))
            {
                report.Check(false, "two villagers do not chop the same tree", "no workers");
                yield break;
            }

            Vector3 at = first.transform.position;
            Container firstBag = VillagerInventory.Attach(first.gameObject, firstView);
            Container secondBag = VillagerInventory.Attach(second.gameObject, secondView);
            Clear(firstBag.GetInventory());
            Clear(secondBag.GetInventory());
            Add(firstBag.GetInventory(), "AxeStone");
            Add(secondBag.GetInventory(), "AxeStone");

            // Its own clearing too, for the same reason: nothing here fells the tree, but a
            // fixture beside the colony's own is a hazard waiting for the next change.
            at = colony.transform.position + Vector3.forward * 40f;
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(at, out float clearing))
                at.y = clearing + .2f;
            first.transform.position = at;
            second.transform.position = at;
            ClearTrees(colony.transform.position, 60f);
            ClearTrees(at, 40f);
            string species = Resources.ResourceIndex.SampleTree(0, 0);
            GameObject only = species.Length == 0 ? null : Spawn(species, at + Vector3.right * 5f);
            if (only == null)
            {
                report.Check(false, "two villagers do not chop the same tree", "no tree a stone axe can fell");
                VillagerLifecycle.Remove(colony, firstView.GetZDO().m_uid);
                VillagerLifecycle.Remove(colony, secondView.GetZDO().m_uid);
                yield break;
            }
            Resources.ColonyResources.Clear();
            first.State.ResetJob();
            second.State.ResetJob();
            yield return new WaitForSecondsRealtime(.4f);

            ColonyJobConfig chop = Job(ColonyJobType.Chop, string.Empty);
            chop.StopDistance = 12f;
            chop.SearchRadius = 55f;

            // One tick is enough for the first villager to claim: choosing is the first thing
            // the cycle does, and the claim is simply its recorded target.
            ColonyJobEngine.Tick(first, firstAi, firstBag, colony, chop, out _);
            bool claimed = !first.State.StepTarget.IsNone();

            JobResult shutOut = ColonyJobEngine.Tick(second, secondAi, secondBag, colony, chop, out string blocked);
            // The invariant is that they do not work on the same thing, not that the second
            // finds nothing - a world with a spare tree in it would fail the stricter claim
            // while behaving perfectly.
            bool refused = shutOut != JobResult.Failed &&
                           second.State.StepTarget != first.State.StepTarget;

            // Control: the same tree, the same second villager, claims turned off.
            second.State.ResetJob();
            chop.Reservations = false;
            yield return new WaitForSecondsRealtime(.2f);
            ColonyJobEngine.Tick(second, secondAi, secondBag, colony, chop, out _);
            bool sharedIt = second.State.StepTarget == first.State.StepTarget &&
                            !second.State.StepTarget.IsNone();

            report.Check(claimed && refused && sharedIt,
                "two villagers do not chop the same tree unless reservations are off",
                $"claimed={claimed} refused={shutOut} why={blocked} shared={sharedIt}");

            if (only != null) ZNetScene.instance.Destroy(only);
            VillagerLifecycle.Remove(colony, firstView.GetZDO().m_uid);
            VillagerLifecycle.Remove(colony, secondView.GetZDO().m_uid);
            yield return new WaitForSecondsRealtime(.2f);
            Resources.ColonyResources.Clear();
        }

        /// <summary>
        ///     A destructible's replicated health, or zero when it is gone.
        /// </summary>
        /// <remarks>
        ///     Read from the ZDO, because that is what damage writes - but an undamaged object
        ///     has never written it, so the fallback has to be the prefab's own full health.
        ///     Defaulting to zero instead made a felled tree and an untouched one report the
        ///     same number, which read as the blow never landing.
        /// </remarks>
        private static float Health(GameObject target)
        {
            if (target == null || !target.TryGetComponent(out ZNetView view) || !view.IsValid()) return 0f;
            float full = target.TryGetComponent(out TreeBase tree) ? tree.m_health
                : target.TryGetComponent(out TreeLog log) ? log.m_health : 0f;
            return view.GetZDO().GetFloat(ZDOVars.s_health, full);
        }

        /// <summary>
        ///     Fells nothing and clears everything: every tree and log within range, so a
        ///     chopping check knows exactly what its villagers can see.
        /// </summary>
        /// <remarks>
        ///     The world is full of trees, which is the whole point of the feature and the
        ///     ruin of any assertion that names one. Without this a villager walks off to a
        ///     Beech the world generated while the check waits for it to touch the tree the
        ///     check planted. Deforesting the benchmark world is free: it exists for this.
        /// </remarks>
        private static void ClearTrees(Vector3 origin, float radius)
        {
            foreach (TreeBase tree in FindAll<TreeBase>())
                if (tree != null && Vector3.Distance(tree.transform.position, origin) <= radius)
                    Release(tree.gameObject);
            foreach (TreeLog log in FindAll<TreeLog>())
                if (log != null && Vector3.Distance(log.transform.position, origin) <= radius)
                    Release(log.gameObject);
        }

        /// <summary>
        ///     Destroys a networked object, taking ownership first - destroying one we do not
        ///     own is a silent no-op, which would leave the trees standing and the check
        ///     failing for a reason nothing reports.
        /// </summary>
        private static void Release(GameObject target)
        {
            if (target == null) return;
            if (target.TryGetComponent(out ZNetView view) && view.IsValid()) view.ClaimOwnership();
            ZNetScene.instance.Destroy(target);
        }

        /// <summary>
        ///     Clears what felling a tree leaves behind. Each control below asserts that a
        ///     villager finds nothing to do, and a log dropped by the phase before it is
        ///     something to do - which is correct behaviour reported as a failure.
        /// </summary>
        private static void ClearFelled(Vector3 origin, float radius)
        {
            foreach (TreeLog log in FindAll<TreeLog>())
                if (log != null && Vector3.Distance(log.transform.position, origin) <= radius)
                    Release(log.gameObject);
            foreach (ItemDrop drop in FindAll<ItemDrop>())
                if (drop != null && Vector3.Distance(drop.transform.position, origin) <= radius)
                    Release(drop.gameObject);
        }

        private static int Nearby<T>(Vector3 origin, float radius) where T : Component
        {
            int count = 0;
            foreach (T found in FindAll<T>())
                if (found != null && Vector3.Distance(found.transform.position, origin) <= radius) count++;
            return count;
        }

        /// <summary>
        ///     Every loaded component of a type. Ordering is not asked for, which is both
        ///     faster and the only form of this call the engine still offers without a warning.
        /// </summary>
        private static T[] FindAll<T>() where T : Component =>
            UnityEngine.Object.FindObjectsByType<T>(FindObjectsSortMode.None);

        /// <summary>
        ///     Drives a whole station job through the engine tick: fetch the fuel, carry it
        ///     over, hand it in.
        /// </summary>
        /// <remarks>
        ///     The station protocols are already covered one call at a time. What this covers
        ///     is the order they are called in, which is the part that changed when stations
        ///     stopped being a piece list and became a job that sequences itself. The job is
        ///     scoped to the fireplace this check placed, because the colony hearth is itself
        ///     a fireplace and would otherwise be a legitimate answer.
        /// </remarks>
        private static IEnumerator CheckStationJob(TestReport report, Colony colony)
        {
            Villager worker = VillagerLifecycle.Spawn(colony);
            if (worker == null || !worker.TryGetComponent(out ZNetView view) || !view.IsValid() ||
                !worker.TryGetComponent(out MonsterAI ai))
            {
                report.Check(false, "the fuel job runs end to end through the engine tick", "no worker");
                yield break;
            }

            Vector3 at = worker.transform.position;
            GameObject chest = Spawn("piece_chest_wood", at + Vector3.right * 3f);
            GameObject fire = Spawn("fire_pit", at + Vector3.left * 3f);
            Register(colony, chest, "Fuel source");
            StructureRecord fireRecord = Register(colony, fire, "Fuel destination");
            Inventory store = chest.GetComponent<Container>().GetInventory();
            Container bag = VillagerInventory.Attach(worker.gameObject, view);
            Clear(store);
            Clear(bag.GetInventory());
            Add(store, "Wood");
            yield return new WaitForSecondsRealtime(.3f);

            ColonyJobConfig job = Job(ColonyJobType.FuelFireplaces, "Wood");
            job.Source = chest.GetComponent<ZNetView>().GetZDO().m_uid;
            job.Targets = TargetMode.Selected;
            if (fireRecord != null) job.SelectedStructures.Add(fireRecord.Id);
            job.StopDistance = 12f;

            int before = StationMetric(fire, job);
            JobResult result = JobResult.Running;
            var steps = new List<string>();
            for (int tick = 0; tick < 80 && result != JobResult.Completed; tick++)
            {
                result = ColonyJobEngine.Tick(worker, ai, bag, colony, job, out string activity);
                if (steps.Count == 0 || steps[steps.Count - 1] != activity) steps.Add(activity);
                if (result == JobResult.Failed || result == JobResult.Skipped) break;
                yield return null;
            }
            yield return new WaitForSecondsRealtime(.2f);
            int after = StationMetric(fire, job);
            bool fuelled = result == JobResult.Completed && after != before && Count(store, "Wood") == 0;

            // Control: nothing to fetch, so the job must come to rest without touching the
            // fire. Without it the check above would pass just as well if the villager were
            // feeding the fire out of thin air.
            Clear(store);
            Clear(bag.GetInventory());
            int settled = StationMetric(fire, job);
            JobResult idle = JobResult.Running;
            for (int tick = 0; tick < 40 && idle == JobResult.Running; tick++)
            {
                idle = ColonyJobEngine.Tick(worker, ai, bag, colony, job, out _);
                yield return null;
            }
            bool refused = idle == JobResult.Skipped && StationMetric(fire, job) == settled;

            report.Check(fuelled && refused,
                "the fuel job fetches fuel and feeds a fireplace through the engine tick",
                $"result={result} station={before}->{after} left={Count(store, "Wood")} " +
                $"whenEmpty={idle} steps={string.Join(" | ", steps.ToArray())}");

            VillagerLifecycle.Remove(colony, view.GetZDO().m_uid);
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>
        ///     Drives a whole beehive job through the engine tick: tap, wait, pick up, store.
        /// </summary>
        /// <remarks>
        ///     This is the only job whose cycle collects twice - once at the hive and once off
        ///     the ground - and telling those apart is the thing most likely to go wrong. A
        ///     villager that tapped the hive a second time instead of picking up the honey
        ///     would still look busy, so the check is that honey reaches the chest.
        /// </remarks>
        private static IEnumerator CheckHiveJob(TestReport report, Colony colony)
        {
            Villager worker = VillagerLifecycle.Spawn(colony);
            if (worker == null || !worker.TryGetComponent(out ZNetView view) || !view.IsValid() ||
                !worker.TryGetComponent(out MonsterAI ai))
            {
                report.Check(false, "the beehive job runs end to end through the engine tick", "no worker");
                yield break;
            }

            Vector3 at = worker.transform.position;
            GameObject chest = Spawn("piece_chest_wood", at + Vector3.right * 3f);
            GameObject hiveObject = Spawn("piece_beehive", at + Vector3.left * 3f);
            Register(colony, chest, "Honey destination");
            StructureRecord hiveRecord = Register(colony, hiveObject, "Honey source");
            Inventory store = chest.GetComponent<Container>().GetInventory();
            Container bag = VillagerInventory.Attach(worker.gameObject, view);
            Clear(store);
            Clear(bag.GetInventory());

            // Hives fill up on their own schedule, which is far longer than a benchmark. Run
            // the clock forward rather than waiting it out.
            Beehive hive = hiveObject != null ? hiveObject.GetComponent<Beehive>() : null;
            if (hive != null) hive.m_secPerUnit = .01f;
            yield return new WaitForSecondsRealtime(1f);
            if (hive != null) hive.UpdateBees();

            ColonyJobConfig job = Job(ColonyJobType.CollectBeehives, "Honey");
            job.Destination = chest.GetComponent<ZNetView>().GetZDO().m_uid;
            job.Targets = TargetMode.Selected;
            if (hiveRecord != null) job.SelectedStructures.Add(hiveRecord.Id);
            job.StopDistance = 12f;
            job.SearchRadius = 64f;

            int before = Count(store, "Honey");
            JobResult result = JobResult.Running;
            var steps = new List<string>();
            for (int tick = 0; tick < 120 && result != JobResult.Completed; tick++)
            {
                result = ColonyJobEngine.Tick(worker, ai, bag, colony, job, out string activity);
                if (steps.Count == 0 || steps[steps.Count - 1] != activity) steps.Add(activity);
                if (result == JobResult.Failed || result == JobResult.Skipped) break;
                // The honey is a world drop, so the wait needs real time to pass rather than
                // frames to elapse.
                yield return new WaitForSecondsRealtime(.05f);
            }
            yield return new WaitForSecondsRealtime(.2f);
            int stored = Count(store, "Honey");
            bool collected = result == JobResult.Completed && stored > before;

            // Control: an emptied hive has nothing to give, so the job must come to rest
            // without storing anything.
            int settled = Count(store, "Honey");
            JobResult idle = JobResult.Running;
            for (int tick = 0; tick < 60 && idle == JobResult.Running; tick++)
            {
                idle = ColonyJobEngine.Tick(worker, ai, bag, colony, job, out _);
                yield return new WaitForSecondsRealtime(.05f);
            }
            bool refused = idle == JobResult.Skipped && Count(store, "Honey") == settled;

            report.Check(collected && refused,
                "the beehive job taps, collects and stores honey through the engine tick",
                $"result={result} stored={stored} was={before} whenEmpty={idle} " +
                $"after={Count(store, "Honey")} steps={string.Join(" | ", steps.ToArray())}");

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
            // How much honey a hive has accumulated by the time it is tapped is its own
            // business, and a deposit moves the whole stack, so assert that honey arrived
            // rather than that exactly one unit did.
            report.Check(hive != null && hiveResult == JobResult.Running && honeyDeposit == JobResult.Completed &&
                         Count(destinationObject.GetComponent<Container>().GetInventory(), "Honey") > honeyBefore,
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
            // A station that wants materials must yield, not report progress. Reporting
            // progress makes the walker advance past the station and finish a cycle having
            // operated nothing.
            Clear(bag);
            report.Check(ColonyJobEngine.TestOperate(cooking, bag, Job(ColonyJobType.OperateCookingStations, "RawMeat"), state, out _) == JobResult.Skipped,
                "a station needing input yields instead of reporting progress");

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
