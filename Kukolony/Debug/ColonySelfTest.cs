using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Kukolony.Colonies;
using Kukolony.Jobs;
using Kukolony.Jobs.Chop;
using Kukolony.Jobs.Forage;
using Kukolony.Jobs.Mine;
using Kukolony.Gui;
using Kukolony.KeepAlive;
using Kukolony.Resources;
using Kukolony.Resources.Foraging;
using Kukolony.Resources.Mining;
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
        private const string PersistedVillagerName = "Kukolony Persisted Villager V1";
        private const string PersistedStructureName = "Kukolony Persisted Storage V1";
        private const string PersistedHaulerName = "Kukolony Interrupted Hauler V1";
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
            report.Check(chestRecord != null && (chestRecord.Capabilities & StructureCapability.Storage) != 0,
                "container capability is cached");
            report.Check(ColonyOperations.RenameStructure(colony, chestRecord.Id, "Renamed storage") &&
                         colony.State.GetStructures()[0].Name == "Renamed storage", "structure naming persists");
            report.Check(ColonyOperations.FilterStructures(colony, "renamed", StructureCapability.Storage,
                StructureSort.Name).Count == 1, "structure search and capability filtering");
            report.Check(ColonyOperations.FilterStructures(colony, string.Empty, StructureCapability.None,
                StructureSort.Status).Count == 1, "structure status sorting retains live records");

            // Its own chest, not the one the reload phase depends on.
            //
            // This used to teleport "Renamed storage" out of radius and back, and that chest is
            // also the subject of the save-and-relaunch check. Moving a placed piece by its
            // transform leaves it without WearNTear support, and the support check fires some
            // seconds later and breaks it - so the structure was destroyed during the
            // screenshot phase and the failure surfaced a phase later as "registration does not
            // persist". Intermittent, because it is a race between that check and the end of
            // the run; adding logging to find it was enough to make it stop happening.
            //
            // The lesson was already written down after a falling tree destroyed a benchmark
            // chest: keep destructive fixtures away from other checks' subjects.
            GameObject roamer = Spawn("piece_chest_wood", origin + Vector3.right * 5f);
            yield return null;
            StructureRecord roamerRecord = Register(colony, roamer, "Roaming storage");
            if (roamerRecord != null)
            {
                roamer.transform.position = origin + Vector3.right * (colony.EffectiveRadius + 8f);
                roamer.GetComponent<ZNetView>().GetZDO().SetPosition(roamer.transform.position);
                yield return new WaitForSecondsRealtime(.2f);
                report.Check(colony.State.GetStructures().Exists(r => r.Id == roamerRecord.Id) &&
                             !roamerRecord.IsLiveIn(colony),
                    "registered out-of-radius structure stays visible but becomes ineligible");
                colony.RemoveStructure(roamerRecord.Id);
                Release(roamer);
            }
            else
            {
                report.Check(false, "out-of-radius check could register its own chest");
            }

            yield return new WaitForSecondsRealtime(.2f);

            // A record that can never be found, and is not known to be dead: the state the
            // whole "never infer destruction from absence" rule protects. It has to survive
            // everything, so it is created here and asserted on much later, after the reaper
            // has run over it - see CheckReaper.
            PlantUnfindable(colony, "Unfindable storage");

            GameObject outside = Spawn("piece_chest_wood", origin + Vector3.right * (colony.EffectiveRadius + 12f));
            StructureRecord outsideRecord = MakeRecord(outside, "Outside");
            report.Check(outsideRecord != null && !colony.RegisterStructure(outsideRecord),
                "out-of-radius structure is rejected");
            if (outside != null) ZNetScene.instance.Destroy(outside);
            report.Check(!StructureRegistry.TryCapabilities(
                ZNetScene.instance.GetPrefab(VillagerPrefab.PrefabName), out _),
                "NPC is excluded from structure registration");

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
            }

            Trace(colony, "before any check");
            yield return CheckLifecycle(report, colony, origin);
            Trace(colony, "CheckLifecycle");
            CheckRegisterableContainers(report, colony);
            Trace(colony, "CheckRegisterableContainers");
            yield return CheckDurableReference(report, colony, origin);
            Trace(colony, "CheckDurableReference");
            yield return CheckZdoLifetime(report, colony);
            Trace(colony, "CheckZdoLifetime");
            yield return CheckOrphanedVillager(report, colony);
            Trace(colony, "CheckOrphanedVillager");
            yield return CheckDestroyedColonyLeavesStructures(report, colony, origin);
            Trace(colony, "CheckDestroyedColonyLeavesStructures");
            yield return CheckRegistration(report, colony, origin);
            Trace(colony, "CheckRegistration");
            yield return CheckReaper(report, colony, origin);
            Trace(colony, "CheckReaper");
            yield return CheckStoredContents(report, colony, origin);
            Trace(colony, "CheckStoredContents");
            yield return CheckOutpostStaysLoaded(report, colony);
            Trace(colony, "CheckOutpostStaysLoaded");
            yield return CheckSettingsAndIndex(report, colony, origin);
            yield return CheckAppearance(report, colony);
            yield return CheckVillagerLiving(report, colony, origin);
            yield return CheckJobQueue(report, colony);
            yield return CheckHauling(report, colony, origin);
            yield return ClearTheGround(colony, "CheckHauling");
            yield return CheckHaulingThoroughly(report, colony, origin);
            yield return ClearTheGround(colony, "CheckHaulingThoroughly");
            yield return CheckHaulingGoesWrong(report, colony, origin);
            yield return ClearTheGround(colony, "CheckHaulingGoesWrong");
            yield return CheckTwoVillagersOneItem(report, colony, origin);
            yield return ClearTheGround(colony, "CheckTwoVillagersOneItem");
            yield return CheckFillingTheBagFirst(report, colony, origin);
            yield return ClearTheGround(colony, "CheckFillingTheBagFirst");
            yield return CheckAChestMayBeLeftAlone(report, colony, origin);
            yield return ClearTheGround(colony, "CheckAChestMayBeLeftAlone");
            yield return CheckAPartlyFullChestTakesWhatFits(report, colony, origin);
            yield return ClearTheGround(colony, "CheckAPartlyFullChestTakesWhatFits");
            yield return CheckTheItemVanishingMidWalk(report, colony, origin);
            yield return ClearTheGround(colony, "CheckTheItemVanishingMidWalk");
            yield return CheckABagThatFillsMidTrip(report, colony, origin);
            yield return ClearTheGround(colony, "CheckABagThatFillsMidTrip");
            yield return CheckBeingOrphanedMidHaul(report, colony, origin);
            yield return ClearTheGround(colony, "CheckBeingOrphanedMidHaul");
            yield return CheckTidying(report, colony, origin);
            yield return ClearTheGround(colony, "CheckTidying");
            yield return CheckTidyingKeepsUnidentifiedItems(report, colony, origin);
            yield return ClearTheGround(colony, "CheckTidyingKeepsUnidentifiedItems");
            yield return CheckWorkAreas(report, colony, origin);
            yield return CheckResting(report, colony, origin);
            CheckSayingThingsOnce(report);
            CheckStructureRecordsSurviveAVersion(report);
            CheckJobsSurviveAVersion(report);
            yield return CheckMapPins(report, colony);
            yield return CheckWalkingUpToAVillager(report, colony);
            yield return ClearTheGround(colony, "CheckWalkingUpToAVillager");
            yield return CheckBulkAssignment(report, colony);
            yield return ClearTheGround(colony, "CheckBulkAssignment");
            yield return CheckWorkFlags(report, colony, origin);
            yield return CheckDistantTravel(report, colony, origin);
            yield return CheckFlagToFlagTravel(report, colony, origin);

            // Chopping. The index first, because an empty one makes every check below pass by
            // finding nothing to contradict.
            // The queue's own rules, before the jobs that depend on them: a job that cannot
            // end is a queue that cannot advance, and every check after this one assumes it
            // does. Shared with the focused slice rather than copied - a focused run that
            // drifted from this one would be worse than no focused run.
            yield return CheckAStuckTripEndsAndTheQueueMovesOn(report, colony);
            yield return CheckAPresetKeepsItsOrder(report, colony);

            yield return CheckChoppingIndex(report);
            yield return CheckTreesAreKeptLoaded(report);
            yield return CheckTheAxeIsPutAway(report, colony);
            yield return CheckACappedChestKeepsWhatItHas(report, colony, origin);
            yield return CheckUnclaimedDamageDoesNothing(report, origin);
            yield return CheckGivingUpOnAnUncuttableTree(report, origin);
            yield return CheckChopSettings(report, origin);
            yield return CheckChopStoppingRules(report, colony, origin);
            yield return CheckChoppingFellsATree(report, colony, origin);
            yield return Mining(report, colony, origin);
            yield return Foraging(report, colony, origin);
            Trace(colony, "CheckSettingsAndIndex");
            yield return ScreenChecks.Run(report, colony, origin);
            ReportVillagerMaterials();

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
        ///     One slice of the acceptance run, with only the setup its own checks need.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The full run builds a settlement, exercises registration, hauling, tidying,
        ///         resting, work areas, travel and the screens, then saves and relaunches to
        ///         verify persistence. That is the right shape for proving a release and the
        ///         wrong shape for iterating on one feature: most of an hour of real time, of
        ///         which the part under test is two minutes.
        ///     </para>
        ///     <para>
        ///         So a focus runs its own checks against a bare colony and nothing else. It
        ///         deliberately shares the checks themselves rather than copying them - a
        ///         focused run that drifted from the full one would be worse than no focused
        ///         run, because it would pass while the real one failed.
        ///     </para>
        /// </remarks>
        internal static IEnumerator RunFocused(string runId, string focus)
        {
            LastPassed = false;
            string wanted = (focus ?? string.Empty).Trim().ToLowerInvariant();
            TestReport report = new TestReport($"Colony acceptance run - {wanted} only");

            Vector3 origin = Player.m_localPlayer.transform.position;
            Colony colony = Spawn<Colony>(ColonyPrefab.PrefabName, origin + Vector3.forward * 4f);
            report.Check(colony != null, "colony prefab is registered");
            if (colony == null) { LastPassed = report.Print(); yield break; }

            colony.EnsureNamed();
            colony.State.SetName(PersistenceName);
            SetRunId(colony, runId);
            yield return new WaitForSecondsRealtime(.3f);

            switch (wanted)
            {
                case "chop":
                    yield return CheckChoppingIndex(report);
                    yield return CheckTreesAreKeptLoaded(report);
                    yield return CheckTheAxeIsPutAway(report, colony);
                    yield return CheckACappedChestKeepsWhatItHas(report, colony, origin);
                    yield return CheckUnclaimedDamageDoesNothing(report, origin);
                    yield return CheckGivingUpOnAnUncuttableTree(report, origin);
                    yield return CheckChopSettings(report, origin);
                    yield return CheckChopStoppingRules(report, colony, origin);
                    yield return CheckChoppingFellsATree(report, colony, origin);
                    break;

                case "swing":
                    yield return CheckTheAxeSwingIsSeen(report, colony, origin);
                    break;

                case "tend":
                    CheckStructureRecordsSurviveAVersion(report);
                    yield return CheckTheStationContract(report, origin);
                    yield return CheckAnIdleSmelterIsNotStoked(report, colony, origin);
                    yield return CheckAKilnIsKeptHalfFullAndStops(report, colony, origin);
                    yield return CheckEnoughStopsTheTending(report, colony, origin);
                    break;

                case "craft":
                    CheckStructureRecordsSurviveAVersion(report);
                    CheckJobsSurviveAVersion(report);
                    yield return CheckAStationRefusesWhatItCannotDo(report, colony, origin);
                    yield return CheckACraftedOrderIsMadeFiledAndStops(report, colony, origin);
                    break;

                case "mine":
                    CheckJobsSurviveAVersion(report);
                    yield return Mining(report, colony, origin);
                    break;

                case "forage":
                    CheckJobsSurviveAVersion(report);
                    yield return Foraging(report, colony, origin);
                    break;

                case "queue":
                    yield return CheckAStuckTripEndsAndTheQueueMovesOn(report, colony);
                    yield return CheckAPresetKeepsItsOrder(report, colony);
                    break;

                case "travel":
                    yield return CheckWorkFlags(report, colony, origin);
                    yield return CheckDistantTravel(report, colony, origin);
                    yield return CheckFlagToFlagTravel(report, colony, origin);
                    break;

                default:
                    // Named but unknown. Failing beats running everything under a name that
                    // says otherwise, or running nothing and reporting a pass.
                    report.Check(false, $"'{wanted}' is not a slice this run knows",
                        "known: chop, travel, queue, tend, craft, mine, forage, swing");
                    break;
            }

            // The fixtures go with it. A focused run does not save, so anything left standing
            // is left in the player's world rather than in a throwaway one.
            Cleanup(colony);
            LastPassed = report.Print();
        }

        /// <summary>Takes a focused run's colony and everything it registered back out again.</summary>
        private static void Cleanup(Colony colony)
        {
            if (colony == null) return;

            foreach (ZDOID member in colony.State.GetMembers(ColonyMemberKind.Villager))
            {
                VillagerLifecycle.Remove(colony, member);
            }

            foreach (StructureRecord record in colony.State.GetStructures())
            {
                colony.RemoveStructure(record.Id);
            }

            if (colony.TryGetComponent(out ZNetView view) && view.IsValid())
            {
                view.ClaimOwnership();
                ZNetScene.instance.Destroy(colony.gameObject);
            }
        }

        /// <summary>
        ///     Second-launch phase: assert the snapshot written before exit survived the save,
        ///     including cross-ZDO references that a chunked save renormalises, then clean up
        ///     the run's fixtures.
        /// </summary>
        internal static IEnumerator RunReload(Colony colony)
        {
            LastPassed = false;
            TestReport report = new TestReport("Colony acceptance run 2 - reload");
            ColonyState state = colony.State;
            report.Check(state.Name == PersistenceName, "colony name survived save and relaunch");
            StructureRecord storage = state.GetStructures().FirstOrDefault(r => r.Name == PersistedStructureName);
            report.Check(storage != null, "registered structure and name survived save and relaunch");
            // The name alone would pass even if the reference to the object rotted, because
            // it is a plain string in the record. A chunked save renumbers raw runtime ids,
            // so the stored token is what has to carry the identity across. Assert the record
            // still points at a live object, and report both halves - a missing token and an
            // unloaded chest fail identically here and are fixed differently.
            if (storage != null)
            {
                ZDO storageZdo = ZDOMan.instance.GetZDO(storage.Id);

                // Say what was actually found. An id in session form here means the token did
                // not resolve and the stale address was used, which is a different fault from
                // the object having been destroyed - and they print identically otherwise.
                int carrying = 0;
                foreach (ZDOID candidate in ZDOExtraData.GetAllZDOIDsWithHash(
                             ZDOExtraData.Type.String, "kukolony.persistent-id.v1".GetStableHashCode()))
                {
                    ZDO carrier = ZDOMan.instance.GetZDO(candidate);
                    if (carrier != null &&
                        Core.PersistentZdoReference.Get(carrier) == storage.PersistentId) carrying++;
                }

                report.Check(storageZdo != null && storageZdo.IsValid(),
                    "registered structure reference still resolves after save and relaunch",
                    $"id={storage.Id} token={(string.IsNullOrEmpty(storage.PersistentId) ? "none" : storage.PersistentId)} " +
                    $"zdosCarryingThatToken={carrying} structuresInColony={state.GetStructures().Count}");
            }
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
                report.Check(villager.Name == PersistedVillagerName,
                    "villager name survived save and relaunch", $"name='{villager.Name}'");
                report.Check(villager.HasHome, "villager home survived save and relaunch",
                    $"home={villager.Home}");
                report.Check(villager.HasAppearance,
                    "villager appearance flag survived save and relaunch");
                report.Check(StoredBagCount(zdo, "Coal") == 3,
                    "villager bag contents survived save and relaunch");
            }

            yield return CheckAnInterruptedHaulFinishes(report, colony, storage);

            LastPassed = report.Print();
        }

        /// <summary>
        ///     A villager that was carrying a load when the world was saved delivers it.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         This is the one claim the reload phase could not make before, because it is
        ///         about behaviour rather than stored values and the phase was a plain method
        ///         that returned before the villager could take a step. Everything else here
        ///         asks whether a field came back; this asks whether the work did.
        ///     </para>
        ///     <para>
        ///         The recorded state is checked first and separately. A villager that resumed by
        ///         forgetting everything and starting over would also end up delivering the wood,
        ///         and would be indistinguishable from one that resumed properly if delivery were
        ///         the only thing asserted.
        ///     </para>
        /// </remarks>
        private static IEnumerator CheckAnInterruptedHaulFinishes(TestReport report, Colony colony,
            StructureRecord destination)
        {
            ZDO hauler = null;
            foreach (ZDOID member in colony.State.GetMembers(ColonyMemberKind.Villager))
            {
                ZDO candidate = ZDOMan.instance.GetZDO(member);
                if (candidate == null) continue;
                if (new VillagerState(candidate).Name == PersistedHaulerName) hauler = candidate;
            }

            report.Check(hauler != null,
                "the villager that was carrying a load when the world was saved is still here",
                $"found={(hauler != null)}");
            if (hauler == null || destination == null) yield break;

            VillagerState state = new VillagerState(hauler);
            report.Check(state.GetQueue().Contains("resume") && state.Cargo == "Wood",
                "its orders and what it was carrying came back with it",
                $"queue=[{string.Join(",", state.GetQueue())}] cargo='{state.Cargo}' " +
                $"workState={state.WorkState} inBag={StoredBagCount(hauler, "Wood")}");

            report.Check(StoredBagCount(hauler, "Wood") == 5,
                "control: the load itself survived, so there is something left to deliver",
                $"inBag={StoredBagCount(hauler, "Wood")}");

            // What the chest holds already is not what this villager delivered. Counting the
            // total rather than the difference made this pass while the load it was supposed to
            // be carrying had been delivered before the save.
            int already = StructureInventory.Count(destination.Id, "Wood");

            GameObject woken = ZNetScene.instance == null
                ? null
                : ZNetScene.instance.FindInstance(hauler.m_uid);
            if (woken != null && woken.TryGetComponent(out ZNetView wokenView) && wokenView.IsValid())
            {
                SendRested(wokenView);
            }
            else
            {
                state.SetEnergy(Energy.Full);
            }

            int delivered = 0;
            for (int attempt = 0; attempt < 120 && delivered < 5; attempt++)
            {
                yield return new WaitForSecondsRealtime(.5f);
                delivered = StructureInventory.Count(destination.Id, "Wood") - already;
            }

            GameObject loaded = ZNetScene.instance == null
                ? null
                : ZNetScene.instance.FindInstance(hauler.m_uid);
            Villager villager = loaded == null ? null : loaded.GetComponent<Villager>();

            report.Check(delivered >= 5,
                "and it finishes the delivery it was interrupted in the middle of",
                $"delivered={delivered} alreadyThere={already} doing='{villager?.Activity}'");
        }

        /// <summary>
        ///     Writes known name, home and bag contents to the primary villager immediately
        ///     before the world save, so the reload phase has specific values to verify rather
        ///     than merely checking the villager still exists.
        /// </summary>
        internal static void PreparePersistenceSnapshot(Colony colony)
        {
            if (colony == null || ZDOMan.instance == null) return;
            ZDO zdo = ZDOMan.instance.GetZDO(GetPrimaryMember(colony));
            if (zdo == null)
            {
                Core.Log.Error("[Benchmark] no primary villager to snapshot - the reload phase will fail");
                return;
            }

            // The subject is registered here, immediately before the save, rather than reused
            // from a check four minutes earlier.
            //
            // It used to be the chest from the first structure check, which meant the reload
            // phase was really asking "did that chest survive the entire suite" - and when
            // something destroyed it mid-run, three unrelated reload checks failed at once
            // with no hint why, because this method returned silently when it could not find
            // it. What the reload phase means to ask is whether a registered structure
            // survives a save, so it now gets a structure of its own with nothing between its
            // registration and the save.
            StructureRecord target = RegisterPersistenceSubject(colony);
            if (target == null)
            {
                Core.Log.Error("[Benchmark] could not register a persistence subject - " +
                               "the reload phase will fail, and this is why");
                return;
            }
            Core.Log.Info($"[Benchmark] snapshot start: structures={colony.State.GetStructures().Count} " +
                          $"storage={target.Id} live={(ZDOMan.instance.GetZDO(target.Id) != null)}");
            VillagerState persisted = new VillagerState(zdo);
            persisted.SetName(PersistedVillagerName);

            // Idled here rather than assumed to be idle. This villager now survives the sweep
            // between checks, so it is on the roster when work is handed out to everybody in
            // bulk - and it kept that queue, into the save, for a later build to resume. The
            // remark below says there is no work for it to do; this is what makes that true.
            persisted.SetQueue(new List<string>());

            // Fill the bag here rather than earlier so nothing can spend it before the save.
            // There is no work for a villager to do yet, so it simply idles until the save.
            GameObject carried = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(zdo.m_uid) : null;
            if (carried != null && carried.TryGetComponent(out ZNetView carriedView) && carriedView.IsValid())
            {
                Container bag = VillagerInventory.Attach(carried, carriedView);
                Clear(bag.GetInventory());
                for (int i = 0; i < 3; i++) Add(bag.GetInventory(), "Coal");
                Core.Log.Info($"[Benchmark] primary villager bag before save: live=" +
                              $"{Count(bag.GetInventory(), "Coal")} stored={StoredBagCount(zdo, "Coal")}");
            }

            LeaveAHaulerMidDelivery(colony, target);

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
        /// <summary>
        ///     Destroying a colony must not take the world with it: a registered chest is the
        ///     player's chest, not the colony's property.
        /// </summary>
        /// <remarks>
        ///     Uses its own throwaway hearth far from the benchmark's, because the claim can
        ///     only be tested by destroying one, and every check after this needs the real one
        ///     alive. Distance matters as well as timing - registration is bounded by radius,
        ///     so a second hearth placed near the first would start claiming its structures.
        ///
        ///     The control is the same chest one step earlier, which proves the chest was
        ///     genuinely there to begin with and that the check would notice its loss.
        /// </remarks>
        private static IEnumerator CheckDestroyedColonyLeavesStructures(
            TestReport report, Colony colony, Vector3 origin)
        {
            // Far enough that the real colony cannot claim the chest, close enough that the
            // zone is loaded. The first attempt used a round 120m and the hearth was placed
            // into unloaded terrain, where the ground query answers zero and the piece lands
            // under the world and is destroyed - which read as the feature deleting it.
            Vector3 where = origin + new Vector3(colony.EffectiveRadius + 20f, 0f, 0f);
            if (ZoneSystem.instance == null || !ZoneSystem.instance.GetSolidHeight(where, out float _))
            {
                report.Check(false, "destroyed-colony check found loaded ground to build on",
                    $"at {where}");
                yield break;
            }

            Colony temporary = Spawn<Colony>(ColonyPrefab.PrefabName, where);
            Container chest = Spawn<Container>("piece_chest_wood", where + new Vector3(2f, 0f, 0f));
            yield return new WaitForSecondsRealtime(.3f);

            // Re-checked after the wait, not just after the spawn: a piece that fails to
            // survive placement disappears during it, and Unity's destroyed objects only
            // compare equal to null - reading one is the exception this check first hit.
            if (temporary == null || chest == null ||
                !chest.TryGetComponent(out ZNetView chestView) || !chestView.IsValid() ||
                !temporary.TryGetComponent(out ZNetView temporaryView) || !temporaryView.IsValid())
            {
                report.Check(false, "destroyed-colony check kept its own hearth and chest alive",
                    $"hearth={(temporary != null)} chest={(chest != null)}");
                yield break;
            }

            ZDOID chestId = chestView.GetZDO().m_uid;
            temporary.RegisterStructure(StructureRegistry.Describe(chest.gameObject));
            report.Check(temporary.State.GetStructures().Any(r => r.Id == chestId),
                "control: the throwaway colony registered its chest before being destroyed");

            temporaryView.ClaimOwnership();
            ZNetScene.instance.Destroy(temporary.gameObject);
            yield return new WaitForSecondsRealtime(.4f);

            ZDO chestZdo = ZDOMan.instance.GetZDO(chestId);
            report.Check(chestZdo != null && chestZdo.IsValid() &&
                         ZNetScene.instance.FindInstance(chestId) != null,
                "a registered structure outlives the colony that registered it",
                $"zdo={(chestZdo != null)} instance={(ZNetScene.instance.FindInstance(chestId) != null)}");

            Release(chest.gameObject);
            yield return null;
        }

        /// <summary>
        ///     A villager whose colony it cannot see stops and says so, rather than walking to
        ///     where the hearth used to be.
        /// </summary>
        /// <remarks>
        ///     The control is the same villager one step earlier: a member of a live colony
        ///     must report anything but the no-Kolony sentinel. Without it this check would pass on a
        ///     villager that reported it permanently, which is the more likely defect
        ///     - the reason to be careful here is that idling and being orphaned look identical
        ///     from outside, since both stand still.
        ///
        ///     The membership pointer is cleared directly rather than by destroying a colony,
        ///     because the benchmark has exactly one hearth and the checks after this one need
        ///     it. What the villager reacts to is "I cannot see my colony", and an unset
        ///     pointer and a destroyed hearth reach that through the same path.
        /// </remarks>
        private static IEnumerator CheckOrphanedVillager(TestReport report, Colony colony)
        {
            Villager villager = VillagerLifecycle.Spawn(colony);
            if (villager == null || !villager.TryGetComponent(out ZNetView view) || !view.IsValid())
            {
                report.Check(false, "orphan check could spawn a villager");
                yield break;
            }

            ZDO zdo = view.GetZDO();
            ZDOID id = zdo.m_uid;
            yield return new WaitForSecondsRealtime(.4f);
            report.Check(villager.Activity != Villager.NoKolonyActivity,
                "control: a villager in a live colony does not report being colonyless",
                $"activity='{villager.Activity}'");

            ColonyMembership.SetColony(zdo, ZDOID.None);
            yield return new WaitForSecondsRealtime(.4f);
            report.Check(villager.Activity == Villager.NoKolonyActivity,
                "a villager that cannot see its colony reports it",
                $"activity='{villager.Activity}'");

            // Orphaned is a state, not a death sentence: the villager keeps existing and keeps
            // its identity, so a hearth rebuilt next to it still has someone to take back.
            report.Check(ZDOMan.instance.GetZDO(id) != null &&
                         ZNetScene.instance.FindInstance(id) != null,
                "an orphaned villager is not destroyed");
            report.Check(new VillagerState(zdo).HasName,
                "an orphaned villager keeps its name");

            VillagerLifecycle.Remove(colony, id);
            yield return new WaitForSecondsRealtime(.2f);
        }

        private static IEnumerator CheckLifecycle(TestReport report, Colony colony, Vector3 origin)
        {

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
                              (cartRecord.Capabilities & StructureCapability.Storage) != 0;
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
        ///     Can a destroyed object be told apart from one that is merely not in memory?
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         This decides whether a colony may ever delete a registration on its own. If
        ///         the two are indistinguishable then walking away from an outpost and deleting
        ///         its configuration are the same act, which would be unforgivable.
        ///     </para>
        ///     <para>
        ///         The game keeps a list of ZDOs it knows to be dead, so destruction can be
        ///         positive evidence rather than an inference from absence. What this measures
        ///         is whether that list actually distinguishes the three cases: alive,
        ///         destroyed, and never-heard-of - the last standing in for "not in memory",
        ///         since to this peer they are the same situation.
        ///     </para>
        /// </remarks>
        /// <summary>
        ///     A villager picks something off the ground and puts it where it belongs.
        /// </summary>
        /// <remarks>
        ///     The first end-to-end proof that the settlement does work: the decision table,
        ///     selection, claiming, walking, the manual take and the deposit, all together
        ///     against a real chest and a real dropped item.
        /// </remarks>
        private static IEnumerator CheckHauling(TestReport report, Colony colony, Vector3 origin)
        {
            // Cleared first. Villagers wear clothes, removing one drops its bag, and earlier
            // checks remove several - so the ground is littered with garments that this check
            // then plants one more of and asserts nothing happens to. A villager hauling a
            // spare shirt off the floor is behaving correctly and would fail the control.
            SweepLooseItems(colony);

            GameObject chest = Spawn("piece_chest_wood", origin + new Vector3(6f, 0f, 6f));
            yield return null;
            StructureRecord store = Register(colony, chest, "Wood shed");
            if (store == null)
            {
                report.Check(false, "haul check could register a chest");
                yield break;
            }

            ColonyOperations.EditSettings(colony, store.Id, s => s.Accepts = new List<string> { "Wood" });

            // Dropped between the villager and the chest, well inside the colony's reach.
            Vector3 where = origin + new Vector3(3f, 0f, 3f);
            if (ZoneSystem.instance.GetSolidHeight(where, out float ground)) where.y = ground + .5f;
            // Instantiated rather than dropped through ItemDrop.DropItem: a prefab's item
            // data has no m_dropPrefab until it has been in an inventory, and DropItem
            // instantiates exactly that - so the call throws, which killed the coroutine and
            // stalled the whole phase rather than failing a check.
            GameObject prefab = ObjectDB.instance.GetItemPrefab("Wood");
            ItemDrop dropped = null;
            if (prefab != null)
            {
                GameObject spawned = UnityEngine.Object.Instantiate(prefab, where, Quaternion.identity);
                if (spawned.TryGetComponent(out dropped)) dropped.SetStack(5);
            }

            report.Check(dropped != null, "control: there is wood on the ground to haul",
                $"dropped={(dropped == null ? "none" : "yes")}");
            if (dropped == null) yield break;

            List<JobDefinition> jobs = new List<JobDefinition>
            {
                new JobDefinition { Id = "haul", Name = "Haul", Kind = JobKind.Haul, Repeat = 8 }
            };
            colony.State.SetJobs(jobs);

            Villager hauler = VillagerLifecycle.Spawn(colony);
            yield return null;
            if (hauler == null || !hauler.TryGetComponent(out ZNetView view) || !view.IsValid())
            {
                report.Check(false, "haul check could spawn a villager");
                yield break;
            }

            ZDOID who = view.GetZDO().m_uid;
            SendRested(view);
            new VillagerState(view.GetZDO()).SetQueue(new List<string> { "haul" });

            // Plant a garment rather than relying on what this villager happened to be born
            // wearing - clothing is rolled, so asserting on it would pass or fail by luck.
            // A bag item that is not this trip's cargo must never be delivered: clothing lives
            // in the same bag as the goods, and a hauler that shipped "the bag" would file its
            // own shirt in the chest. That shipped once.
            // Attach is idempotent and returns the bag already hanging off the villager. Asked
            // for by name rather than with GetComponent, which finds nothing: the container
            // lives on a child object, and a null bag here made this control pass by default.
            Container haulerBag = VillagerInventory.Attach(hauler.gameObject, view);
            GameObject garment = ObjectDB.instance.GetItemPrefab("ArmorLeatherChest");
            if (haulerBag != null && garment != null && garment.TryGetComponent(out ItemDrop garmentDrop))
            {
                // Item data taken straight off a prefab has no m_dropPrefab - the field is
                // filled in when an item passes through an inventory - so a clone of it has no
                // identity and nothing can name it. The same gap made picked-up ground items
                // unhaulable.
                ItemDrop.ItemData planted = garmentDrop.m_itemData.Clone();
                planted.m_dropPrefab = garment;
                haulerBag.GetInventory().AddItem(planted);
                VillagerInventory.Persist(haulerBag, view);
            }

            report.Check(haulerBag != null && Carrying.Cargo(haulerBag.GetInventory(), "ArmorLeatherChest").Count > 0,
                "haul check could plant a garment on the villager to test against");

            // Long enough to walk a few metres, pick up and deliver. Reported as what actually
            // happened rather than just pass or fail, because "it did not finish" and "it never
            // started" are different problems.
            int inChest = 0;
            string doing = string.Empty;
            for (int attempt = 0; attempt < 60 && inChest == 0; attempt++)
            {
                yield return new WaitForSecondsRealtime(.5f);
                inChest = StructureInventory.Count(store.Id, "Wood");
                doing = hauler.Activity;
            }

            // Say what it is holding and what the settlement thinks of it. "Nowhere to put
            // this" is the same sentence whether the villager grabbed the wrong thing, the
            // chest stopped being an answer, or the item lost its identity - and those are
            // three different bugs.
            System.Text.StringBuilder holding = new System.Text.StringBuilder();
            Inventory carried = VillagerInventory.Stored(view.GetZDO());
            foreach (ItemDrop.ItemData held in carried.GetAllItems())
            {
                string name = held?.m_dropPrefab == null ? "<no prefab>" : Utils.GetPrefabName(held.m_dropPrefab);
                holding.Append(name).Append('x').Append(held?.m_stack ?? 0).Append(' ');
            }

            report.Check(inChest > 0,
                "a villager hauls loose wood into the chest that asked for it",
                $"inChest={inChest} doing='{doing}' " +
                $"energy={Resting.Now(new VillagerState(view.GetZDO())):0} holding='{holding}' " +
                $"shedStatus={store.StatusIn(colony)} " +
                $"woodGoesTo={SettlementIndex.WhereDoesItGo(colony, "Wood", origin).Count} place(s)");

            report.Check(ZDOMan.instance.GetZDO(view.GetZDO().m_uid) != null,
                "control: the villager survived the job rather than being destroyed by it");

            int keptOnPerson = 0;
            if (haulerBag != null)
            {
                keptOnPerson = Carrying.Cargo(haulerBag.GetInventory(), "ArmorLeatherChest").Count;
            }

            report.Check(keptOnPerson > 0,
                "control: hauling delivers its cargo and not the villager's own belongings",
                $"garment still on the villager={keptOnPerson} " +
                $"garmentsInChest={StructureInventory.Count(store.Id, "ArmorLeatherChest")}");

            VillagerLifecycle.Remove(colony, who);
            colony.State.SetJobs(new List<JobDefinition>());
            if (chest != null) { colony.RemoveStructure(store.Id); Release(chest); }
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>
        ///     How far a villager can be asked to walk before the pathfinder stops answering.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Work areas are meant to be placed away from the colony, so "can a villager
        ///         get there" stops being rhetorical. Two things bound it and neither is
        ///         obvious: a villager cannot path into unloaded ground, and Valheim's own path
        ///         search has limits of its own.
        ///     </para>
        ///     <para>
        ///         This measures rather than asserts. Most of what it reports is a finding, not
        ///         a requirement - the only thing checked is that near travel works, which is
        ///         the control that says the measurement apparatus itself is sound.
        ///     </para>
        /// </remarks>
        /// <summary>
        ///     One preset, given to everybody at once, including villagers nobody has loaded.
        /// </summary>
        /// <remarks>
        ///     The unloaded half is the point. A settlement worth assigning in bulk is spread
        ///     over enough ground that some of it is always out of memory, and orders that only
        ///     reached whoever happened to be standing nearby would be worse than none - a player
        ///     would have no way to tell which half took.
        /// </remarks>
        private static IEnumerator CheckBulkAssignment(TestReport report, Colony colony)
        {
            colony.State.SetJobs(new List<JobDefinition>
            {
                new JobDefinition { Id = "bulk-a", Name = "First", Kind = JobKind.Haul, Repeat = 2 },
                new JobDefinition { Id = "bulk-b", Name = "Second", Kind = JobKind.Haul, Repeat = 3 }
            });

            JobPreset preset = new JobPreset
            {
                Id = "bulk-preset",
                Name = "Hauler",
                Jobs = new List<string> { "bulk-a", "bulk-b" }
            };

            colony.State.SetPresets(new List<JobPreset> { preset });

            JobPreset reread = colony.State.GetPresets().Find(p => p.Id == "bulk-preset");
            report.Check(reread != null && reread.Name == "Hauler" && reread.Jobs.Count == 2 &&
                         reread.Jobs[0] == "bulk-a" && reread.Jobs[1] == "bulk-b",
                "a preset survives being written to the colony record, in order",
                reread == null ? "no record" : $"'{reread.Name}' with {reread.Jobs.Count} job(s)");

            List<ZDOID> made = new List<ZDOID>();
            for (int i = 0; i < 3; i++)
            {
                Villager villager = VillagerLifecycle.Spawn(colony);
                yield return null;
                if (villager != null && villager.TryGetComponent(out ZNetView spawned) && spawned.IsValid())
                {
                    made.Add(spawned.GetZDO().m_uid);
                }
            }

            report.Check(made.Count == 3, "control: three villagers to assign work to",
                $"spawned={made.Count}");
            if (made.Count != 3) yield break;

            // One of them is unloaded on purpose: its ZDO stays, its instance goes. That is the
            // state most of a real settlement is in most of the time.
            //
            // Unloaded the way the game unloads things - drop it from the scene's instance table
            // and destroy the object - NOT with ZNetScene.Destroy, which destroys the ZDO as
            // well. Using that deleted the villager outright, and the check correctly reported
            // that two of three had been assigned: the code had behaved properly and the fixture
            // had not.
            GameObject instance = ZNetScene.instance.FindInstance(made[2]);
            if (instance != null && instance.TryGetComponent(out ZNetView loaded) && loaded.IsValid())
            {
                ZNetScene.instance.m_instances.Remove(loaded.GetZDO());
                UnityEngine.Object.Destroy(instance);
            }

            yield return new WaitForSecondsRealtime(.5f);

            report.Check(ZNetScene.instance.FindInstance(made[2]) == null,
                "control: one of them really is unloaded",
                $"loaded={(ZNetScene.instance.FindInstance(made[2]) != null)}");

            int assigned = Assignment.Apply(Assignment.Everyone(colony), preset.Jobs);
            report.Check(assigned >= 3,
                "one preset assigns every villager in the settlement at once",
                $"assigned={assigned} of {Assignment.Everyone(colony).Count}");

            int carried = 0;
            foreach (ZDOID id in made)
            {
                ZDO zdo = ZDOMan.instance.GetZDO(id);
                if (zdo == null) continue;

                List<string> queue = new VillagerState(zdo).GetQueue();
                if (queue.Count == 2 && queue[0] == "bulk-a" && queue[1] == "bulk-b") carried++;
            }

            report.Check(carried == 3,
                "control: the orders reached the unloaded villager as well as the loaded ones",
                $"{carried} of 3 carry the queue in order");

            // And a reassignment starts the new orders from the beginning rather than resuming
            // at whatever position the old queue had reached.
            ZDO first = ZDOMan.instance.GetZDO(made[0]);
            new VillagerState(first).SetQueuePosition(1);
            Assignment.Apply(new List<ZDOID> { made[0] }, preset.Jobs);

            report.Check(new VillagerState(ZDOMan.instance.GetZDO(made[0])).QueuePosition == 0,
                "control: new orders start at the beginning, not where the old ones left off",
                $"position={new VillagerState(ZDOMan.instance.GetZDO(made[0])).QueuePosition}");

            foreach (ZDOID id in made) VillagerLifecycle.Remove(colony, id);
            colony.State.SetPresets(new List<JobPreset>());
            colony.State.SetJobs(new List<JobDefinition>());
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>
        ///     Every villager appears on the map, and none of it is written to the save.
        /// </summary>
        /// <remarks>
        ///     The save check is the one that would bite silently. A saved pin goes into the
        ///     player's own map profile, so getting it wrong adds one pin per villager per
        ///     session to their file for ever - invisible while playing, and permanent.
        /// </remarks>
        private static IEnumerator CheckMapPins(TestReport report, Colony colony)
        {
            if (Minimap.instance == null)
            {
                report.Note("map pin check skipped: no minimap in this session");
                yield break;
            }

            Villager walker = VillagerLifecycle.Spawn(colony);
            yield return new WaitForSecondsRealtime(1.5f);
            if (walker == null || !walker.TryGetComponent(out ZNetView view) || !view.IsValid())
            {
                report.Check(false, "map pin check could spawn a villager");
                yield break;
            }

            ZDOID who = view.GetZDO().m_uid;
            string name = VillagerRoster.Name(who);

            Minimap.PinData pin = FindPin(name);
            report.Check(pin != null, "a villager is shown on the map",
                $"looking for a pin named like '{name}'");

            // The settlement itself, which is the thing a map is most obviously for. Pinned from
            // the registry rather than from loaded instances, so one across the map still shows.
            Minimap.PinData home = FindPin(colony.State.Name);
            report.Check(home != null, "a settlement is shown on the map",
                $"looking for a pin named like '{colony.State.Name}'");

            report.Check(home == null || !home.m_save,
                "control: settlement pins are not written into the player's saved map either",
                $"save={(home == null ? "no pin" : home.m_save.ToString())}");

            report.Check(home == null || pin == null || home.m_type != pin.m_type,
                "control: a settlement and a villager do not look the same on the map",
                $"settlement={home?.m_type} villager={pin?.m_type}");

            // A job pointed at a structure puts that structure on the map, named after the job
            // that sends people there.
            GameObject post = Spawn("piece_chest_wood", colony.transform.position + new Vector3(9f, 0f, 0f));
            yield return new WaitForSecondsRealtime(.3f);
            StructureRecord marked = post == null ? null : Register(colony, post, "Quarry");

            if (marked != null)
            {
                colony.State.SetJobs(new List<JobDefinition>
                {
                    new JobDefinition
                    {
                        Id = "pin-job", Name = "Quarry haul", Kind = JobKind.Haul,
                        Areas = new List<string> { marked.PersistentId }
                    }
                });

                yield return new WaitForSecondsRealtime(2f);

                Minimap.PinData area = FindPin("Quarry");
                report.Check(area != null, "a work area a job is pointed at is shown on the map",
                    $"looking for a pin named like 'Quarry'");

                report.Check(area == null || area.m_name.Contains("Quarry haul"),
                    "control: the work area says which job sends people there",
                    $"label='{area?.m_name}'");

                // Only the ones a job names. Every registered chest on the map would be noise.
                colony.State.SetJobs(new List<JobDefinition>());
                yield return new WaitForSecondsRealtime(2f);

                report.Check(FindPin("Quarry") == null,
                    "control: a structure no job works at is not a work area, and is not pinned",
                    $"stillPinned={(FindPin("Quarry") != null)}");

                colony.RemoveStructure(marked.Id);
                Release(post);
            }

            report.Check(pin == null || !pin.m_save,
                "control: villager pins are never written into the player's saved map",
                $"save={(pin == null ? "no pin" : pin.m_save.ToString())}");

            if (pin != null)
            {
                report.Check(Utils.DistanceXZ(pin.m_pos, walker.transform.position) < 8f,
                    "control: the pin is where the villager actually is",
                    $"{Utils.DistanceXZ(pin.m_pos, walker.transform.position):0.0}m apart");
            }

            // And it goes away with them, rather than pointing at somebody who is gone.
            //
            // Waited for rather than slept through. The map redraws on its own once-a-second
            // timer, so a fixed pause only ever has whatever margin is left over, and this one
            // had half a second - enough to pass for weeks and then report a pin that lived a
            // tick too long as a broken feature. Polling fails only if the pin really stays.
            VillagerLifecycle.Remove(colony, who);

            for (int attempt = 0; attempt < 30 && FindPin(name) != null; attempt++)
            {
                yield return new WaitForSecondsRealtime(.25f);
            }

            report.Check(FindPin(name) == null,
                "a villager's pin goes when the villager does",
                $"stillPinned={(FindPin(name) != null)}");
        }

        private static Minimap.PinData FindPin(string name)
        {
            if (Minimap.instance == null || Minimap.instance.m_pins == null) return null;

            foreach (Minimap.PinData pin in Minimap.instance.m_pins)
            {
                if (pin != null && pin.m_name != null && pin.m_name.StartsWith(name)) return pin;
            }

            return null;
        }

        /// <summary>
        ///     A structure record written by an older build still decodes, with its new fields
        ///     at their defaults.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The bytes are built by hand because there is no other way to produce them -
        ///         the writer only writes the current layout - and because this is the branch
        ///         that fails silently. A misread does not throw; it yields a chest whose name
        ///         is a fragment of a token and whose settings are plausible nonsense.
        ///     </para>
        ///     <para>
        ///         It is worth a check of its own because the cost of getting it wrong is not a
        ///         broken feature. The reader used to accept exactly one version and discard
        ///         anything else, so adding a field would have deleted every registration in
        ///         every colony - each chest, kiln and bed with its name and configuration -
        ///         and shown a settlement that had simply never been set up.
        ///     </para>
        /// </remarks>
        private static void CheckStructureRecordsSurviveAVersion(TestReport report)
        {
            // Exactly the bytes version 3 wrote: accepts, may-take-from, take-unclaimed, caps,
            // fuel, input, keep-full, sleeper, sleeper token. Nothing after.
            ZPackage old = new ZPackage();
            old.Write(1);
            old.Write("Wood");
            old.Write(true);
            old.Write(false);
            old.Write(1);
            old.Write("Stone");
            old.Write(40);
            old.Write(1);
            old.Write("Coal");
            old.Write(1);
            old.Write("CopperOre");
            old.Write(0.75f);
            old.Write(ZDOID.None);
            old.Write(string.Empty);

            // A second record after it, so under- or over-consumption shows up. Reading one
            // blob asserts what the fields decode to and nothing at all about where the reader
            // finished - and finishing in the wrong place is the failure that takes a whole
            // settlement with it.
            old.Write(1);
            old.Write("Coal");
            old.Write(false);
            old.Write(true);
            old.Write(0);
            old.Write(0);
            old.Write(0);
            old.Write(0.25f);
            old.Write(ZDOID.None);
            old.Write(string.Empty);

            ZPackage stream = new ZPackage(old.GetArray());
            StructureSettings read = StructureSettings.Read(stream, 3);
            StructureSettings second = StructureSettings.Read(stream, 3);

            report.Check(second.Accepts.Count == 1 && second.Accepts[0] == "Coal" &&
                         !second.MayTakeFrom && second.TakeUnclaimed &&
                         Mathf.Approximately(second.KeepFull, 0.25f),
                "and so does the record written after it, which is where a lost byte would show",
                $"accepts={second.Accepts.Count} mayTake={second.MayTakeFrom} " +
                $"unclaimed={second.TakeUnclaimed} keepFull={second.KeepFull:0.00}");

            bool kept = read.Accepts.Count == 1 && read.Accepts[0] == "Wood" && read.MayTakeFrom &&
                        !read.TakeUnclaimed && read.CapFor("Stone") == 40 &&
                        read.Fuel.Count == 1 && read.Fuel[0] == "Coal" &&
                        read.Input.Count == 1 && read.Input[0] == "CopperOre" &&
                        Mathf.Approximately(read.KeepFull, 0.75f);
            report.Check(kept,
                "a structure registered before orders existed keeps everything it was told",
                $"accepts={read.Accepts.Count} cap={read.CapFor("Stone")} fuel={read.Fuel.Count} " +
                $"input={read.Input.Count} keepFull={read.KeepFull:0.00}");

            // The half that matters most: a structure from before the switch existed is *in*
            // service. Defaulting the other way would have silently stopped every settlement.
            report.Check(read.InService && !read.Repairs && read.Orders.Count == 0 &&
                         read.Work == StationWork.Both &&
                         read.Carries == StationCargo.Both,
                "and arrives in service, with no orders and nothing repaired",
                $"inService={read.InService} repairs={read.Repairs} orders={read.Orders.Count} " +
                $"work={read.Work} carries={read.Carries}");

            // The control: today's layout round-trips, so the check above is reading an old
            // blob rather than passing because the reader ignores its input.
            StructureSettings written = new StructureSettings { InService = false, Repairs = true,
                Work = StationWork.Collect,
                Carries = StationCargo.Fuel };
            written.Orders.Add(new StructureOrder { Item = "ArrowWood", Count = 50,
                Mode = OrderMode.Once, Done = true });

            ZPackage now = new ZPackage();
            written.Write(now);
            StructureSettings back = StructureSettings.Read(new ZPackage(now.GetArray()),
                ColonyState.StructureFormat);

            bool round = !back.InService && back.Repairs &&
                         back.Work == StationWork.Collect &&
                         back.Carries == StationCargo.Fuel &&
                         back.Orders.Count == 1 && back.Orders[0].Item == "ArrowWood" &&
                         back.Orders[0].Count == 50 && back.Orders[0].Mode == OrderMode.Once &&
                         back.Orders[0].Done;
            report.Check(round,
                "control: today's settings write and read back unchanged, orders and all",
                $"inService={back.InService} repairs={back.Repairs} work={back.Work} " +
                $"carries={back.Carries} orders={back.Orders.Count}");
        }

        /// <summary>
        ///     A version-5 job blob still decodes, and so does the job written after it.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Version 5 wrote four fields this build no longer writes - which station kinds,
        ///         supply or clear, fuel or material, and a list of named stations - because they
        ///         described stations and now live on the stations. Dropping them from the writer
        ///         is easy; the part that can go wrong is the reader, because those bytes are not
        ///         at the end of anything. Every job in a colony is written into one stream, so a
        ///         reader that failed to consume them would decode the next job from four fields
        ///         into this one and produce a job full of plausible nonsense rather than an
        ///         error.
        ///     </para>
        ///     <para>
        ///         Hence two jobs and an assertion about the <em>second</em> one. A check that
        ///         read a single record would pass whether or not the bytes were consumed.
        ///     </para>
        /// </remarks>
        private static void CheckJobsSurviveAVersion(TestReport report)
        {
            ZPackage stream = new ZPackage();

            // Written as bytes rather than as objects. The fields a JobDefinition would carry
            // are not all written by WriteSix, so building one here would state numbers that
            // nothing under test ever sees - the values asserted below are WriteSix's own.
            //
            // A version-5 record is version *six's* layout plus the four retired fields.
            //
            // It used to be built from today's writer plus that tail, which was true exactly
            // until the writer gained a tail of its own: mining's two fields then landed
            // between the two halves, the retired ints were read off a stream five bytes out,
            // and the station count came back as 512 - over the limit, so Read threw. Nothing
            // catches that, so it took the whole run with it, and every check after this one
            // silently never ran. A fixture built from the live writer is a fixture that
            // changes when the writer does.
            WriteSix(stream, "older", "Older tend", JobKind.Tend, 4);
            WriteRetired(stream);
            WriteSix(stream, "after", "The one after it", JobKind.Chop, 9);
            WriteRetired(stream);

            ZPackage reading = new ZPackage(stream.GetArray());
            JobDefinition readFirst = JobDefinition.Read(reading, 5);
            JobDefinition readSecond = JobDefinition.Read(reading, 5);

            report.Check(readFirst.Id == "older" && readFirst.Repeat == 4 &&
                         readFirst.Kind == JobKind.Tend,
                "a job written before tending moved onto the stations still decodes",
                $"id='{readFirst.Id}' repeat={readFirst.Repeat} kind={readFirst.Kind}");

            report.Check(readSecond.Id == "after" && readSecond.Repeat == 9 &&
                         readSecond.Kind == JobKind.Chop && readSecond.LeaveStanding == 4 &&
                         readSecond.StockItem == "Wood" && readSecond.StockTarget == 60,
                "and so does the job written after it, which is where a lost byte would show",
                $"id='{readSecond.Id}' repeat={readSecond.Repeat} leave={readSecond.LeaveStanding} " +
                $"stock='{readSecond.StockItem}'x{readSecond.StockTarget}");

            // Version 6, which mining appended to. Built by hand for the same reason version 3
            // is: the writer only writes today's layout, and this is the branch that fails
            // silently - a reader that ran past the end of a version-6 record would decode the
            // next job from inside this one and produce plausible nonsense rather than an error.
            ZPackage six = new ZPackage();
            WriteSix(six, "sixth", "Older chop", JobKind.Chop, 5);
            WriteSix(six, "seventh", "The one after it", JobKind.Haul, 11);

            ZPackage older = new ZPackage(six.GetArray());
            JobDefinition beforeMining = JobDefinition.Read(older, 6);
            JobDefinition afterIt = JobDefinition.Read(older, 6);

            report.Check(beforeMining.Id == "sixth" && beforeMining.Repeat == 5 &&
                         !beforeMining.MineBoulders && beforeMining.Ores.Count == 0,
                "a job written before mining decodes, with the mining settings at their defaults",
                $"id='{beforeMining.Id}' repeat={beforeMining.Repeat} " +
                $"boulders={beforeMining.MineBoulders} ores={beforeMining.Ores.Count}");

            report.Check(afterIt.Id == "seventh" && afterIt.Repeat == 11 &&
                         afterIt.Kind == JobKind.Haul,
                "and so does the one after it",
                $"id='{afterIt.Id}' repeat={afterIt.Repeat} kind={afterIt.Kind}");

            // Version 7, which foraging appended to. Built by hand for the reason version 6 is:
            // the writer only writes today's layout, and a reader that ran past the end of a
            // version-7 record would decode the next job from inside this one and produce
            // plausible nonsense rather than an error.
            ZPackage seven = new ZPackage();
            WriteSeven(seven, "eighth", "Older mine", JobKind.Mine, 6);
            WriteSeven(seven, "ninth", "The one after it", JobKind.Haul, 13);

            ZPackage sevens = new ZPackage(seven.GetArray());
            JobDefinition beforeForaging = JobDefinition.Read(sevens, 7);
            JobDefinition afterThat = JobDefinition.Read(sevens, 7);

            report.Check(beforeForaging.Id == "eighth" && beforeForaging.Repeat == 6 &&
                         beforeForaging.MineBoulders && beforeForaging.Ores.Count == 1 &&
                         !beforeForaging.ForageRegrowingOnly && beforeForaging.Harvest.Count == 0,
                "a job written before foraging decodes, with the foraging settings at their defaults",
                $"id='{beforeForaging.Id}' ores={beforeForaging.Ores.Count} " +
                $"regrowing={beforeForaging.ForageRegrowingOnly} harvest={beforeForaging.Harvest.Count}");

            report.Check(afterThat.Id == "ninth" && afterThat.Repeat == 13 &&
                         afterThat.Kind == JobKind.Haul,
                "and so does the one after it, which is where a lost byte would show",
                $"id='{afterThat.Id}' repeat={afterThat.Repeat} kind={afterThat.Kind}");

            // Today's layout, written and read back with the mining and foraging fields actually
            // set. The version checks above all decode blobs this build cannot write, so a Read
            // that consumed either tail in the wrong order would have passed every one of them.
            ZPackage now = new ZPackage();
            JobDefinition mining = new JobDefinition
            {
                Id = "pit", Name = "Mine", Kind = JobKind.Mine, Repeat = 3,
                MineBoulders = true, Ores = new List<string> { "TinOre", "CopperOre" }
            };
            mining.Write(now);

            JobDefinition foraging = new JobDefinition
            {
                Id = "patch", Name = "Forage", Kind = JobKind.Forage, Repeat = 7,
                ForageRegrowingOnly = true,
                Harvest = new List<string> { "Raspberry", "Mushroom" }
            };
            foraging.Write(now);

            JobDefinition alongside = new JobDefinition
                { Id = "beside", Name = "Haul", Kind = JobKind.Haul, Repeat = 2 };
            alongside.Write(now);

            ZPackage today = new ZPackage(now.GetArray());
            JobDefinition readMine = JobDefinition.Read(today, ColonyState.JobFormat);
            JobDefinition readForage = JobDefinition.Read(today, ColonyState.JobFormat);
            JobDefinition readBeside = JobDefinition.Read(today, ColonyState.JobFormat);

            // The migration notice belongs to version 5 alone. It escaped that branch once
            // already, when the mining fields were appended below it, and then fired for every
            // job this build writes - per villager, per tick. Asserted in both directions,
            // because a notice that never fires and one that always fires look identical from
            // any single blob.
            Core.Chatter.Forget(JobDefinition.MigrationNotice("old-tend"));

            ZPackage saying = new ZPackage();
            WriteSix(saying, "old-tend", "Older tend", JobKind.Tend, 1);
            WriteRetired(saying);
            JobDefinition.Read(new ZPackage(saying.GetArray()), 5);

            report.Check(Core.Chatter.Said(JobDefinition.MigrationNotice("old-tend")),
                "a version-5 tending job says its station settings were dropped");

            Core.Chatter.Forget(JobDefinition.MigrationNotice("now"));

            ZPackage quiet = new ZPackage();
            new JobDefinition { Id = "now", Name = "Tend", Kind = JobKind.Tend, Repeat = 1 }.Write(quiet);
            JobDefinition.Read(new ZPackage(quiet.GetArray()), ColonyState.JobFormat);

            report.Check(!Core.Chatter.Said(JobDefinition.MigrationNotice("now")),
                "control: a tending job written today says nothing of the sort");

            report.Check(readMine.Kind == JobKind.Mine && readMine.MineBoulders &&
                         readMine.Ores.Count == 2 && readMine.Ores[0] == "TinOre" &&
                         readMine.Ores[1] == "CopperOre" && readBeside.Id == "beside" &&
                         readBeside.Repeat == 2,
                "a mining job written today reads back with its ores, and so does the job after it",
                $"boulders={readMine.MineBoulders} ores={readMine.Ores.Count} " +
                $"next='{readBeside.Id}'x{readBeside.Repeat}");

            report.Check(readForage.Kind == JobKind.Forage && readForage.Repeat == 7 &&
                         readForage.ForageRegrowingOnly && readForage.Harvest.Count == 2 &&
                         readForage.Harvest[0] == "Raspberry" && readForage.Harvest[1] == "Mushroom",
                "and a foraging job reads back with what it gathers, between two jobs that are not one",
                $"kind={readForage.Kind} regrowing={readForage.ForageRegrowingOnly} " +
                $"harvest={readForage.Harvest.Count} next='{readBeside.Id}'x{readBeside.Repeat}");
        }

        /// <summary>One job in the layout version 7 wrote - version six's, plus the mining tail.</summary>
        /// <remarks>
        ///     Built on <see cref="WriteSix" /> rather than on the live writer, for the reason
        ///     that one records: a fixture built from today's writer is a fixture that changes
        ///     when the writer does, and the last time that happened the record after it decoded
        ///     from five bytes out and threw, which took the whole run with it in silence.
        /// </remarks>
        private static void WriteSeven(ZPackage package, string id, string name, JobKind kind, int repeat)
        {
            WriteSix(package, id, name, kind, repeat);

            package.Write(true);

            package.Write(1);
            package.Write("TinOre");
        }

        /// <summary>One job in the layout version 6 wrote - today's, without the mining tail.</summary>
        private static void WriteSix(ZPackage package, string id, string name, JobKind kind, int repeat)
        {
            package.Write(id);
            package.Write(name);
            package.Write((int)kind);
            package.Write(repeat);
            package.Write(true);
            package.Write(false);

            package.Write(1);
            package.Write("some-area-token");

            package.Write(21f);

            package.Write(true);
            package.Write(true);
            package.Write(false);
            package.Write(4);
            package.Write("Wood");
            package.Write(60);

            package.Write(1);
            package.Write("Stone");

            package.Write(1);
            package.Write("Birch");
        }

        /// <summary>The four fields version 5 wrote and this build does not.</summary>
        private static void WriteRetired(ZPackage package)
        {
            package.Write(3);
            package.Write(1);
            package.Write(2);
            package.Write(1);
            package.Write("some-station-token");
        }

        /// <summary>
        ///     A problem that keeps being true is reported once, with a count.
        /// </summary>
        /// <remarks>
        ///     Driven directly rather than by staging a settlement with nowhere to put things,
        ///     because what is under test is the collapsing and not the hauling. Fifty calls is
        ///     roughly two and a half seconds of one villager deciding, which is the rate this
        ///     exists to survive.
        /// </remarks>
        private static void CheckSayingThingsOnce(TestReport report)
        {
            System.Action<string> previous = Core.Report.Listener;
            int said = 0;

            try
            {
                Core.Report.Listener = _ => said++;
                Core.Chatter.Clear();

                for (int i = 0; i < 50; i++) Core.Chatter.Say("same", "the settlement has nowhere to put Wood");

                report.Check(said == 1,
                    "a problem that keeps being true is reported once rather than fifty times",
                    $"said={said}");

                // Control: collapsing must be per problem, or two different faults would hide
                // each other and a settlement would report only whichever happened first.
                said = 0;
                Core.Chatter.Say("different", "the settlement has nowhere to put Stone");

                report.Check(said == 1,
                    "control: a different problem is still reported",
                    $"said={said}");

                // Control: once the situation changes, the next occurrence is news again rather
                // than being counted into a tally that started minutes ago.
                said = 0;
                Core.Chatter.Forget("same");
                Core.Chatter.Say("same", "the settlement has nowhere to put Wood");

                report.Check(said == 1,
                    "control: a problem that went away and came back is reported again",
                    $"said={said}");
            }
            finally
            {
                Core.Report.Listener = previous;
                Core.Chatter.Clear();
            }
        }

        /// <summary>
        ///     A tired villager stops, goes to bed, sleeps, and gets up again.
        /// </summary>
        /// <remarks>
        ///     Energy is driven directly rather than by making a villager work until it tires,
        ///     which would take an in-game night of real time. What is under test is the
        ///     behaviour around the thresholds - stopping, walking to the right place, lying on
        ///     it, recovering, and getting up - not the arithmetic, which is proven at a table.
        /// </remarks>
        private static IEnumerator CheckResting(TestReport report, Colony colony, Vector3 origin)
        {
            GameObject bedPiece = SpawnFirst(origin + new Vector3(-6f, 0f, 4f), "bed", "piece_bed02");
            yield return new WaitForSecondsRealtime(.3f);

            StructureRecord bed = bedPiece == null ? null : Register(colony, bedPiece, "A bed");
            report.Check(bed != null, "rest check could place and register a bed",
                $"placed={(bedPiece != null)}");
            if (bed == null) yield break;

            Villager sleeper = VillagerLifecycle.Spawn(colony);
            yield return null;
            if (sleeper == null || !sleeper.TryGetComponent(out ZNetView view) || !view.IsValid())
            {
                report.Check(false, "rest check could spawn a villager");
                yield break;
            }

            ZDOID who = view.GetZDO().m_uid;
            ColonyOperations.EditSettings(colony, bed.Id, s =>
            {
                s.Sleeper = who;
                s.SleeperToken = Core.PersistentZdoReference.Ensure(view.GetZDO());
            });

            VillagerState state = new VillagerState(view.GetZDO());
            report.Check(state.StoredEnergy > ModConfig.TiredBelow.Value,
                "control: a new villager is rested rather than born exhausted",
                $"energy={state.StoredEnergy:0}");

            // Exhaust it and let it decide for itself what to do about that.
            state.SetEnergy(1f);
            state.SetResting(false);

            string doing = string.Empty;
            bool wentToBed = false;
            for (int attempt = 0; attempt < 60 && !wentToBed; attempt++)
            {
                yield return new WaitForSecondsRealtime(.5f);
                doing = sleeper.Activity;
                wentToBed = doing == "sleeping";
            }

            report.Check(wentToBed, "a tired villager takes itself to bed and sleeps",
                $"doing='{doing}' energy={Resting.Now(new VillagerState(view.GetZDO())):0}");

            report.Check(Utils.DistanceXZ(sleeper.transform.position, bedPiece.transform.position) < 3f,
                "control: it is asleep on its bed rather than standing somewhere near it",
                $"{Utils.DistanceXZ(sleeper.transform.position, bedPiece.transform.position):0.0}m from the bed");

            // The rig decides whether this can look right; say which, because a villager resting
            // upright is a cosmetic shortfall and not a broken feature.
            report.Note(sleeper.CanSleep
                ? "the rig has a sleep animation and is using it"
                : "the rig has no sleep animation; villagers rest upright");

            // Recovering, and doing so from the bed's rate rather than the ground's.
            VillagerState asleep = new VillagerState(view.GetZDO());
            float before = Resting.Now(asleep);
            yield return new WaitForSecondsRealtime(4f);
            float after = Resting.Now(new VillagerState(view.GetZDO()));

            report.Check(after > before, "a sleeping villager recovers energy",
                $"{before:0.0} -> {after:0.0} in 4s");

            // And gets up when rested, rather than sleeping forever.
            new VillagerState(view.GetZDO()).SetEnergy(ModConfig.RestedAbove.Value + 5f);
            bool woke = false;
            for (int attempt = 0; attempt < 30 && !woke; attempt++)
            {
                yield return new WaitForSecondsRealtime(.5f);
                woke = sleeper.Activity != "sleeping";
            }

            report.Check(woke, "a rested villager gets up again",
                $"doing='{sleeper.Activity}'");

            report.Check(!new VillagerState(view.GetZDO()).Resting,
                "control: and stops being recorded as resting, so it is given work again",
                $"resting={new VillagerState(view.GetZDO()).Resting}");

            VillagerLifecycle.Remove(colony, who);
            colony.RemoveStructure(bed.Id);
            Release(bedPiece);
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>
        ///     A job pointed at an outpost works there and leaves the rest of the settlement alone.
        /// </summary>
        /// <remarks>
        ///     Both halves matter and the second is the one that can be got wrong silently: it is
        ///     easy to write a work area that a villager respects by accident because it never
        ///     looked that far anyway. So the item it must ignore is put well inside the
        ///     settlement, where it would certainly be picked up if the area were not doing
        ///     anything.
        /// </remarks>
        private static IEnumerator CheckWorkAreas(TestReport report, Colony colony, Vector3 origin)
        {
            SweepLooseItems(colony);
            SettlementIndex.ResetForTest();

            // The outpost: a chest at the edge of the settlement, used as the centre of a small
            // area. Anything already registered can be one - that is the point of building work
            // areas out of structures rather than as a new thing to place.
            GameObject post = Spawn("piece_chest_wood", origin + new Vector3(30f, 0f, 0f));
            GameObject shed = Spawn("piece_chest_wood", origin + new Vector3(6f, 0f, 6f));
            yield return new WaitForSecondsRealtime(.3f);

            StructureRecord outpost = Register(colony, post, "Outpost");
            StructureRecord store = Register(colony, shed, "Wood shed");
            if (outpost == null || store == null)
            {
                report.Check(false, "work area check could register an outpost and a shed");
                yield break;
            }

            ColonyOperations.EditSettings(colony, store.Id, s => s.Accepts = new List<string> { "Wood" });
            ColonyOperations.EditSettings(colony, outpost.Id, s => s.Accepts = new List<string> { "Coal" });

            colony.State.SetJobs(new List<JobDefinition>
            {
                new JobDefinition
                {
                    Id = "outpost", Name = "Outpost haul", Kind = JobKind.Haul, Repeat = 20,
                    Areas = new List<string> { outpost.PersistentId }, WorkRadius = 12f
                }
            });

            JobDefinition stored = colony.State.GetJobs().Find(j => j.Id == "outpost");
            report.Check(stored != null && stored.Areas.Count == 1 &&
                         stored.Areas[0] == outpost.PersistentId &&
                         Mathf.Approximately(stored.WorkRadius, 12f),
                "a job's work area survives being written to the colony record",
                $"areas='{(stored == null ? "none" : string.Join(",", stored.Areas))}' " +
                $"radius={stored?.WorkRadius ?? -1f}");

            // Several places, in the order they were given. The order is the feature - a job
            // works the first place with anything to do - so a codec that kept the set and
            // lost the sequence would pass "the areas survived" and still work the wrong wood
            // first.
            List<JobDefinition> ordered = colony.State.GetJobs();
            JobDefinition editing = ordered.Find(j => j.Id == "outpost");
            if (editing == null)
            {
                report.Check(false, "work area check could re-read the job it just wrote");
                yield break;
            }

            editing.Areas = new List<string> { store.PersistentId, outpost.PersistentId };
            colony.State.SetJobs(ordered);

            JobDefinition twice = colony.State.GetJobs().Find(j => j.Id == "outpost");
            report.Check(twice != null && twice.Areas.Count == 2 &&
                         twice.Areas[0] == store.PersistentId &&
                         twice.Areas[1] == outpost.PersistentId,
                "a job keeps several work areas, in the order they were given",
                $"areas='{(twice == null ? "none" : string.Join(",", twice.Areas))}'");

            // And the one-answer form reads the first of them, which is what the reach row
            // and the map pins are told.
            report.Check(twice != null && WorkArea.For(colony, twice).Name == store.Name,
                "the first of a job's work areas is the one a single answer names",
                $"first='{(twice == null ? "none" : WorkArea.For(colony, twice).Name)}'");

            // A record written by the build that kept one work area, decoded against the
            // layout that keeps a list. This is the one branch that can fail silently rather
            // than loudly: a misread here does not throw, it produces a job full of
            // plausible nonsense, so it is worth building the old bytes by hand.
            ZPackage legacy = new ZPackage();
            legacy.Write("legacy");
            legacy.Write("Old haul");
            legacy.Write((int)JobKind.Haul);
            legacy.Write(7);
            legacy.Write(true);
            legacy.Write(false);
            legacy.Write(outpost.PersistentId);
            legacy.Write(19f);
            legacy.Write(true);
            legacy.Write(true);
            legacy.Write(false);
            legacy.Write(3);
            legacy.Write("Wood");
            legacy.Write(40);
            legacy.Write(1);
            legacy.Write("Stone");
            legacy.Write(1);
            legacy.Write("Birch");

            JobDefinition old = JobDefinition.Read(new ZPackage(legacy.GetArray()), 3);
            bool decoded = old.Areas.Count == 1 && old.Areas[0] == outpost.PersistentId &&
                           Mathf.Approximately(old.WorkRadius, 19f) && old.Repeat == 7 &&
                           old.LeaveStanding == 3 && old.StockItem == "Wood" && old.StockTarget == 40 &&
                           old.Items.Count == 1 && old.Items[0] == "Stone" &&
                           old.Species.Count == 1 && old.Species[0] == "Birch";
            report.Check(decoded,
                "a job written before work areas became a list still decodes, every field in place",
                $"areas={old.Areas.Count} radius={old.WorkRadius} repeat={old.Repeat} " +
                $"leave={old.LeaveStanding} stock='{old.StockItem}'x{old.StockTarget} " +
                $"items={old.Items.Count} species={old.Species.Count}");

            // Put back to the single outpost the behavioural half of this check relies on.
            ordered = colony.State.GetJobs();
            JobDefinition restoring = ordered.Find(j => j.Id == "outpost");
            if (restoring == null)
            {
                report.Check(false, "work area check could restore the job it had been editing");
                yield break;
            }

            restoring.Areas = new List<string> { outpost.PersistentId };
            colony.State.SetJobs(ordered);

            // One log inside the area, one well outside it but comfortably inside the settlement.
            ItemDrop inside = DropItem("Wood", origin + new Vector3(27f, 0f, 3f), 3);
            ItemDrop outside = DropItem("Wood", origin + new Vector3(4f, 0f, -4f), 3);
            report.Check(inside != null && outside != null,
                "control: there is wood both inside and outside the work area",
                $"inside={(inside != null)} outside={(outside != null)}");
            if (inside == null || outside == null) yield break;

            ZDOID outsideId = outside.GetComponent<ZNetView>().GetZDO().m_uid;

            Villager hand = VillagerLifecycle.Spawn(colony);
            yield return null;
            if (hand == null || !hand.TryGetComponent(out ZNetView view) || !view.IsValid())
            {
                report.Check(false, "work area check could spawn a villager");
                yield break;
            }

            ZDOID who = view.GetZDO().m_uid;
            SendRested(view);
            new VillagerState(view.GetZDO()).SetQueue(new List<string> { "outpost" });

            int delivered = 0;
            List<string> story = new List<string>();
            // Long enough for the navmesh to be built for ground nobody has walked, which is
            // most of half a minute, and then for the villager to walk thirty metres and back.
            for (int attempt = 0; attempt < 240 && delivered == 0; attempt++)
            {
                yield return new WaitForSecondsRealtime(.5f);
                delivered = StructureInventory.Count(store.Id, "Wood");

                // What it actually did, not just where it ended up. "Delivered nothing" is the
                // same sentence whether it never found the work, never reached it, or reached it
                // and could not pick it up.
                if (story.Count == 0 || story[story.Count - 1] != hand.Activity) story.Add(hand.Activity);
            }

            report.Check(delivered > 0,
                "a villager assigned to an outpost works there",
                $"delivered={delivered} energy={Resting.Now(new VillagerState(view.GetZDO())):0} " +
                $"did='{string.Join(" > ", story.ToArray())}'");

            // The half that proves the area is doing something: the log by the hearth, which any
            // unbounded hauler would have taken first because it was nearer.
            bool leftAlone = ZNetScene.instance.FindInstance(outsideId) != null;
            report.Check(leftAlone,
                "control: it leaves alone wood outside its work area, however near the hearth",
                $"stillThere={leftAlone}");

            VillagerLifecycle.Remove(colony, who);
            colony.State.SetJobs(new List<JobDefinition>());
            colony.RemoveStructure(outpost.Id);
            colony.RemoveStructure(store.Id);
            Release(post);
            Release(shed);
            SweepLooseItems(colony);
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>
        ///     Sends a villager to work rested, for checks that are not about energy.
        /// </summary>
        /// <remarks>
        ///     Fixture hygiene rather than hiding anything: a check controls the variables it is
        ///     not testing. Energy is real and a villager that fails at something fifty times
        ///     genuinely should tire - which is exactly what happened to the work-area check, and
        ///     it failed reporting "resting", a true statement about a villager that had given up
        ///     for entirely correct reasons. The energy each check ends with is reported either
        ///     way, so exhaustion is never the silent explanation for a failure.
        /// </remarks>
        private static void SendRested(ZNetView view)
        {
            if (view == null || !view.IsValid()) return;

            VillagerState state = new VillagerState(view.GetZDO());
            state.SetResting(false);
            state.SetRestRate(0f);
            state.SetEnergy(Energy.Full);
        }

        /// <summary>Puts an item on the ground with an identity, the way a real drop has one.</summary>
        private static ItemDrop DropItem(string prefabName, Vector3 where, int stack)
        {
            if (ZoneSystem.instance.GetSolidHeight(where, out float ground)) where.y = ground + .5f;

            GameObject prefab = ObjectDB.instance.GetItemPrefab(prefabName);
            if (prefab == null) return null;

            GameObject spawned = UnityEngine.Object.Instantiate(prefab, where, Quaternion.identity);
            if (!spawned.TryGetComponent(out ItemDrop drop)) return null;

            drop.SetStack(stack);
            return drop;
        }

        private static IEnumerator CheckDistantTravel(TestReport report, Colony colony, Vector3 origin)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                report.Check(false, "travel check needs a player to walk to");
                yield break;
            }

            if (!TryFindLand(origin, TravelDistance, out Vector3 outpost))
            {
                report.Check(false, "travel check could find solid ground to walk to",
                    $"nothing but water within reach of {TravelDistance:0}m");
                yield break;
            }

            Villager walker = VillagerLifecycle.Spawn(colony);
            yield return new WaitForSecondsRealtime(.5f);
            if (walker == null || !walker.TryGetComponent(out ZNetView view) || !view.IsValid())
            {
                report.Check(false, "travel check could spawn a villager");
                yield break;
            }

            ZDOID who = view.GetZDO().m_uid;

            // Out to the far point, then back to the player. Nobody moves but the villager.
            //
            // The obvious way to stage this was to teleport the player to the far end and have
            // the villager walk to them - and a distant teleport hangs the game outright, three
            // minutes of no log output and no exception. Sending the villager out and back gets
            // both halves without it: the outward leg is entirely unobserved, and the return leg
            // crosses into view on its own as it approaches the player standing at the hearth.
            bool outward = false;
            yield return Journey(report, walker, who, outpost, "out to open country beyond the settlement",
                expectHandover: false, arrived: result => outward = result);

            if (!outward)
            {
                VillagerLifecycle.Remove(colony, who);
                yield break;
            }

            yield return Journey(report, walker, who, player.transform.position, "home again",
                expectHandover: true, arrived: _ => { });

            if (walker != null) VillagerLifecycle.Remove(colony, who);
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>
        ///     One leg of a journey, watched to its end.
        /// </summary>
        /// <remarks>
        ///     <paramref name="expectHandover" /> is the interesting half. A villager returning to
        ///     the settlement crosses out of dead reckoning and into walking as it comes within
        ///     sight of the player, and that transition is the one most likely to look wrong -
        ///     the villager is put down on the navmesh at that moment, and if that goes badly it
        ///     resumes inside a rock or on top of water.
        /// </remarks>
        private static IEnumerator Journey(TestReport report, Villager walker, ZDOID who,
            Vector3 destination, string what, bool expectHandover, System.Action<bool> arrived)
        {
            float startedAt = Utils.DistanceXZ(walker.transform.position, destination);
            walker.SendOnErrand(destination);

            bool wentUnseen = false;
            bool cameIntoView = false;
            bool destroyed = false;
            int unloads = 0;
            int unowned = 0;
            bool wasLoaded = true;
            Vector3 sampledAt = walker != null ? walker.transform.position : Vector3.zero;
            int strode = 0;
            int slid = 0;
            int carried = 0;
            bool photograph = true;
            float closest = startedAt;
            float elapsed = 0f;

            while (elapsed < TravelSeconds && closest > ArrivedWithin)
            {
                yield return new WaitForSecondsRealtime(.5f);
                elapsed += .5f;

                // Gone and not-loaded are different, and the difference decides whether this is
                // a bug or a delay. An unloaded villager still has its ZDO and comes back when
                // something holds its zone again; a destroyed one does not. Never infer
                // destruction from absence - measuring the instance alone reported "DESTROYED
                // EN ROUTE" for what may only have been a villager between zone loads.
                ZDO living = ZDOMan.instance.GetZDO(who);
                if (living == null)
                {
                    destroyed = true;
                    break;
                }

                // Sampled as it goes, because the state that matters is the one it travels
                // in: ownership is handed back and forth as a villager crosses in and out of
                // anybody's active area.
                if (!living.HasOwner()) unowned++;

                // Unity's null is not C#'s: a destroyed MonoBehaviour compares equal to null but
                // still arrives here as a live reference, and touching its transform throws.
                bool loaded = walker != null && ZNetScene.instance.FindInstance(who) != null;
                if (wasLoaded && !loaded) unloads++;
                wasLoaded = loaded;

                if (loaded)
                {
                    if (walker.IsReckoning)
                    {
                        wentUnseen = true;
                        carried++;
                    }
                    else if (wentUnseen) cameIntoView = true;

                    // Whether it is walking, asked of the rig rather than of a flag. "Not
                    // reckoning" says the rescue ladder is not carrying it; it says nothing
                    // about whether the legs are moving, and a villager sliding across the
                    // ground in an idle pose satisfies every flag this check had. The clip
                    // playing is the evidence - and it is the same evidence the axe swing
                    // needed, for the same reason.
                    Vector3 at = walker.transform.position;
                    if (Utils.DistanceXZ(at, sampledAt) > SlidingStep)
                    {
                        if (Walking(walker)) strode++;
                        else slid++;

                        if (photograph && slid + strode >= 4)
                        {
                            photograph = false;
                            yield return BenchmarkUiScenario.PhotographAtWork("travel-underway.png",
                                at, $"a villager {what}, {Utils.DistanceXZ(at, destination):0}m to go");
                        }
                    }

                    sampledAt = at;
                }

                // Read from the record when there is no body to read from. The record is what
                // the world keeps, so it is also the honest measure of how far the journey got.
                float now = Utils.DistanceXZ(
                    loaded ? walker.transform.position : living.GetPosition(), destination);
                if (now < closest) closest = now;
            }

            string story = $"{startedAt:0}m to {closest:0}m in {elapsed:0}s, walkedMostly={!wentUnseen}" +
                           $" strode={strode} slid={slid} carriedSamples={carried}" +
                           (expectHandover ? $" cameIntoView={cameIntoView}" : string.Empty) +
                           $" unloadedTimes={unloads}" +
                           (destroyed ? " - ZDO GONE, TRULY DESTROYED" : string.Empty);
            Core.Log.Info($"[Benchmark] travel {what}: {story}");

            bool reached = closest <= ArrivedWithin;
            report.Check(reached, $"a villager sent {what} arrives", story);

            // Covering ground on its legs, not merely covering ground. Measured over the samples
            // where it actually moved: a villager that slid would show ground covered with no
            // walk playing, which is exactly what every other flag here would have called a
            // clean journey.
            report.Check(strode > 0 && slid <= strode / 8,
                $"a villager sent {what} walks there rather than sliding",
                $"strode={strode} slid={slid} of {strode + slid} moving samples");

            report.Check(!destroyed,
                $"control: it still exists after travelling {what}",
                destroyed ? "its record was deleted en route" : $"record intact, unloaded {unloads} time(s)");

            // Owned the whole way, because an unowned villager is not merely unsimulated - the
            // game stops drawing it. Character.CustomFixedUpdate passes zdo.HasOwner() straight
            // to SetVisible, which parks the LOD reference point a million metres from every
            // camera, so the symptom a player meets first is a villager that has vanished while
            // its map pin stays exactly where it should be. Worth its own check: every other
            // measure here passed happily while a villager stood invisible thinking nothing.
            report.Check(unowned == 0,
                $"a villager sent {what} stays owned, so it stays simulated and drawn",
                unowned == 0 ? "owned at every sample" : $"unowned at {unowned} sample(s)");

            // Give it a moment to finish. The loop above stops as soon as the villager is
            // near enough for this check's purposes, which is not the same instant the journey
            // itself considers the trip over - and sampling in that gap asked whether a villager
            // was on its feet while it was still in the act of landing. Three separate failures
            // in this suite have been a check and the code disagreeing about "arrived".
            float landing = 0f;
            while (landing < 5f && walker != null && walker.IsReckoning)
            {
                yield return new WaitForSecondsRealtime(.25f);
                landing += .25f;
            }

            if (expectHandover)
            {
                // The property worth holding is not that a handover happened - a villager that
                // walked the whole way never needed one - but that it finished on its feet.
                // Arriving mid-glide means the last thing a player sees is a villager sliding
                // into the settlement, which is the one thing covering ground unseen must never
                // do.
                report.Check(!walker.IsReckoning,
                    "a villager arrives on its feet rather than sliding in",
                    story + $" reckoningAtArrival={walker.IsReckoning}");
            }

            arrived(reached);
        }

        /// <summary>
        ///     Flag to flag at three hundred metres, with water staged in the middle.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Every other travel measurement in this suite is a hundred and sixty metres
        ///         over ground a villager can walk. This is the case the flag exists for and
        ///         the one the water-crossing code was written for, and neither has been
        ///         measured: an outpost is only an outpost if villagers can reliably get to it,
        ///         and "reliably" across water is exactly where walking stops being an option.
        ///     </para>
        ///     <para>
        ///         The water is <em>found</em> rather than built. Digging a channel would mean
        ///         terrain edits that outlive the check, and a staged pond is not what the
        ///         probe reads anyway - it samples the world generator, so the crossing has to
        ///         be real world water to be the thing under test.
        ///     </para>
        ///     <para>
        ///         Both ends carry a claimed flag, because the ground at an outpost has to stay
        ///         loaded for a villager to arrive into it at all. That makes this a check of
        ///         the flag and the crossing together, which is how they will be used.
        ///     </para>
        /// </remarks>
        private static IEnumerator CheckFlagToFlagTravel(TestReport report, Colony colony, Vector3 origin)
        {
            if (!TryFindLandAcrossWater(origin, FarTravelDistance, out Vector3 across, out float wet))
            {
                // Not a failure of the mod. Said plainly rather than silently passing, because
                // a check that quietly did not run is worse than one that says it could not.
                report.Check(true,
                    "flag-to-flag check: this world has no water crossing within reach, so it did not run",
                    $"no bearing at {FarTravelDistance:0}m crosses water onto land");
                yield break;
            }

            GameObject nearFlag = Spawn(WorkFlagPrefab.PrefabName, origin + new Vector3(6f, 0f, 0f));
            GameObject farFlag = Spawn(WorkFlagPrefab.PrefabName, across);
            yield return new WaitForSecondsRealtime(.4f);

            WorkFlag near = nearFlag != null ? nearFlag.GetComponent<WorkFlag>() : null;
            WorkFlag far = farFlag != null ? farFlag.GetComponent<WorkFlag>() : null;
            if (near == null || far == null)
            {
                report.Check(false, "flag-to-flag check could place two flags");
                Release(nearFlag);
                Release(farFlag);
                yield break;
            }

            RegisterOutcome tookNear = ColonyOperations.AssignFlag(colony.Id, near);
            RegisterOutcome tookFar = ColonyOperations.AssignFlag(colony.Id, far);
            bool claimed = tookNear == RegisterOutcome.Registered && tookFar == RegisterOutcome.Registered;
            report.Check(claimed,
                "both ends of the crossing are claimed outposts",
                $"near={tookNear} far={tookFar} gap={wet:0}m of water");

            if (!claimed)
            {
                // Stop here. Without the flags there is no corridor held open, so the journey
                // below would spend ten minutes of real time failing for a reason already
                // reported - and report it a second time as though it were a travel fault.
                Cleanup(colony, near, far, nearFlag, farFlag);
                yield break;
            }

            Villager walker = VillagerLifecycle.Spawn(colony);
            yield return new WaitForSecondsRealtime(.5f);
            if (walker == null || !walker.TryGetComponent(out ZNetView view) || !view.IsValid())
            {
                report.Check(false, "flag-to-flag check could spawn a villager");
                Cleanup(colony, near, far, nearFlag, farFlag);
                yield break;
            }

            ZDOID who = view.GetZDO().m_uid;

            // Watched for a dropped anchor across the whole run, not sampled at the end. The
            // cap consumes anchors until it binds and the run is the only time the budget is
            // under real pressure - two flags, a villager and its waypoint all at once.
            int droppedDuring = 0;
            bool arrived = false;

            yield return FarJourney(report, walker, who, across,
                $"across {wet:0}m of water to a flag {FarTravelDistance:0}m out",
                dropped => droppedDuring = Mathf.Max(droppedDuring, dropped),
                result => arrived = result);

            report.Check(droppedDuring == 0,
                "control: no keep-alive anchor was silently dropped during the crossing",
                $"worstDropped={droppedDuring}");

            if (arrived)
            {
                report.Check(walker != null && !walker.IsReckoning,
                    "it comes back onto its feet at the far shore rather than arriving mid-glide",
                    $"reckoning={walker != null && walker.IsReckoning}");

                report.Check(walker != null && walker.TryGetComponent(out Character body) &&
                             body.m_body != null && !body.m_body.isKinematic,
                    "control: physics is restored after the crossing",
                    "a villager left kinematic cannot be moved by anything");
            }

            Cleanup(colony, near, far, nearFlag, farFlag);
            if (walker != null) VillagerLifecycle.Remove(colony, who);
            yield return new WaitForSecondsRealtime(.2f);
        }

        private static void Cleanup(Colony colony, WorkFlag near, WorkFlag far,
            GameObject nearFlag, GameObject farFlag)
        {
            if (near != null) colony.RemoveStructure(near.Id);
            if (far != null) colony.RemoveStructure(far.Id);
            Release(nearFlag);
            Release(farFlag);
        }

        /// <summary>
        ///     One long leg, watched to its end, reporting the worst zone-budget moment.
        /// </summary>
        /// <remarks>
        ///     A separate walk from <see cref="Journey" /> rather than a parameter on it: this
        ///     one runs to a different time bound, watches the anchor budget, and is not
        ///     staged around a player standing at the far end. Threading all of that through
        ///     the existing one would make the shorter check harder to read for the sake of
        ///     sharing a loop.
        /// </remarks>
        private static IEnumerator FarJourney(TestReport report, Villager walker, ZDOID who,
            Vector3 destination, string what, System.Action<int> dropped, System.Action<bool> arrived)
        {
            float startedAt = Utils.DistanceXZ(walker.transform.position, destination);
            walker.SendOnErrand(destination);

            bool crossed = false;
            bool destroyed = false;
            int unloads = 0;
            bool wasLoaded = true;
            float closest = startedAt;
            float elapsed = 0f;

            while (elapsed < FarTravelSeconds && closest > ArrivedWithin)
            {
                yield return new WaitForSecondsRealtime(.5f);
                elapsed += .5f;

                dropped(KeepAlive.KeepAliveZones.DroppedAnchors);

                ZDO living = ZDOMan.instance.GetZDO(who);
                if (living == null)
                {
                    destroyed = true;
                    break;
                }

                bool loaded = walker != null && ZNetScene.instance.FindInstance(who) != null;
                if (wasLoaded && !loaded) unloads++;
                wasLoaded = loaded;

                if (loaded && walker.IsReckoning) crossed = true;

                float now = Utils.DistanceXZ(
                    loaded ? walker.transform.position : living.GetPosition(), destination);
                if (now < closest) closest = now;
            }

            string story = $"{startedAt:0}m to {closest:0}m in {elapsed:0}s, crossed={crossed}, " +
                           $"unloadedTimes={unloads}" +
                           (destroyed ? " - ZDO GONE, TRULY DESTROYED" : string.Empty);
            Core.Log.Info($"[Benchmark] far travel {what}: {story}");

            bool reached = closest <= ArrivedWithin;
            report.Check(reached, $"a villager sent {what} arrives", story);

            report.Check(unloads == 0,
                "control: it was never unloaded en route - the flags held its corridor open",
                story);

            report.Check(!destroyed, "control: it still exists after the crossing", story);

            // Let the landing finish before anyone asks whether it landed. Three separate
            // failures in this suite have been a check and the code disagreeing about
            // "arrived".
            float landing = 0f;
            while (landing < 5f && walker != null && walker.IsReckoning)
            {
                yield return new WaitForSecondsRealtime(.25f);
                landing += .25f;
            }

            arrived(reached);
        }

        /// <summary>
        ///     Somewhere solid roughly this far off whose straight line from here crosses open
        ///     water, and how much water that is.
        /// </summary>
        /// <remarks>
        ///     The straight line is what matters rather than the walking route, because the
        ///     straight line is what the villager's own probe samples. Sixteen bearings, the
        ///     same as <see cref="TryFindLand" /> - and the widest crossing wins, because a
        ///     puddle a villager can wade is not the case under test.
        /// </remarks>
        private static bool TryFindLandAcrossWater(Vector3 from, float distance,
            out Vector3 found, out float water)
        {
            found = from;
            water = 0f;
            if (WorldGenerator.instance == null || ZoneSystem.instance == null) return false;

            float level = ZoneSystem.instance.m_waterLevel;

            for (int step = 0; step < 16; step++)
            {
                float radians = step * Mathf.PI * 2f / 16f;
                Vector3 bearing = new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians));
                Vector3 candidate = from + bearing * distance;

                // The far end has to be standable ground, or this measures a villager
                // sensibly refusing to walk into the sea.
                if (WorldGenerator.instance.GetBiome(candidate.x, candidate.z) == Heightmap.Biome.Ocean) continue;
                float height = WorldGenerator.instance.GetHeight(candidate.x, candidate.z);
                if (height < level + 5f) continue;

                // How much of the line between is under water, sampled every eight metres.
                float wet = 0f;
                for (float along = 8f; along < distance; along += 8f)
                {
                    Vector3 at = from + bearing * along;
                    if (WorldGenerator.instance.GetHeight(at.x, at.z) < level) wet += 8f;
                }

                if (wet < MinimumCrossing || wet <= water) continue;

                candidate.y = height;
                found = candidate;
                water = wet;
            }

            return water >= MinimumCrossing;
        }

        /// <summary>How far off the far flag goes, in metres.</summary>
        private const float FarTravelDistance = 300f;

        /// <summary>
        ///     How long to allow for three hundred metres including a swim.
        /// </summary>
        /// <remarks>
        ///     Scaled from the shorter leg's allowance rather than guessed: roughly twice the
        ///     distance, and dead reckoning covers ground at running pace rather than faster,
        ///     so the crossing is not a shortcut in time either.
        /// </remarks>
        private const float FarTravelSeconds = 600f;

        /// <summary>
        ///     How much water makes a crossing worth calling one.
        /// </summary>
        /// <remarks>
        ///     Wide enough that walking around it is not the obvious route and that the probe's
        ///     twelve-metre sampling cannot step over it.
        /// </remarks>
        private const float MinimumCrossing = 40f;

        /// <summary>How far off to send the villager, in metres.</summary>
        private const float TravelDistance = 160f;

        /// <summary>
        ///     How long to allow for it, at a villager's walking pace with room to spare.
        /// </summary>
        /// <remarks>
        ///     Deliberately generous. A hundred and sixty metres at about a metre and a half a
        ///     second is under two minutes of walking, and the rest is slack for zone loading,
        ///     rough ground and the handover. A check that fails because it was in a hurry
        ///     teaches nothing, and this one is watching real time pass.
        /// </remarks>
        private const float TravelSeconds = 300f;

        /// <summary>Near enough to the player to count as having arrived.</summary>
        private const float ArrivedWithin = 8f;

        /// <summary>
        ///     Somewhere solid, roughly this far from a point, without loading anything.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <c>WorldGenerator</c> answers height and biome for anywhere in the world from
        ///         the seed alone, which is what makes this possible: the destination has to be
        ///         chosen <em>before</em> the player goes there, and asking the loaded world about
        ///         ground that is not loaded returns nothing.
        ///     </para>
        ///     <para>
        ///         Sixteen bearings rather than one, because the first fixed direction this check
        ///         used pointed into a lake and a villager that will not walk into water is
        ///         behaving correctly - which is not what it meant to measure. Water level is 30,
        ///         so a few metres of margin keeps the destination off the shoreline as well as
        ///         out of the sea.
        ///     </para>
        /// </remarks>
        private static bool TryFindLand(Vector3 from, float distance, out Vector3 found)
        {
            found = from;
            if (WorldGenerator.instance == null) return false;

            for (int step = 0; step < 16; step++)
            {
                float radians = step * Mathf.PI * 2f / 16f;
                Vector3 candidate = from + new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians)) * distance;

                if (WorldGenerator.instance.GetBiome(candidate.x, candidate.z) == Heightmap.Biome.Ocean) continue;

                float height = WorldGenerator.instance.GetHeight(candidate.x, candidate.z);
                if (height < ZoneSystem.instance.m_waterLevel + 5f) continue;

                candidate.y = height;
                found = candidate;
                return true;
            }

            return false;
        }

        /// <summary>
        ///     Losing the settlement while carrying its goods.
        /// </summary>
        /// <remarks>
        ///     The existing orphan check takes an idle villager, which is the easy half. A
        ///     villager orphaned mid-trip is holding the settlement's goods and a job that has
        ///     a reference to a colony it can no longer see, and the two failures worth ruling
        ///     out are losing the load and carrying on regardless. Rejoining is checked too,
        ///     because an orphan that can never work again is a villager the player has to
        ///     replace rather than rescue.
        /// </remarks>
        private static IEnumerator CheckBeingOrphanedMidHaul(TestReport report, Colony colony, Vector3 origin)
        {
            SweepLooseItems(colony);
            SettlementIndex.ResetForTest();

            GameObject chest = Spawn("piece_chest_wood", origin + new Vector3(5f, 0f, 7f));
            yield return new WaitForSecondsRealtime(.3f);
            StructureRecord store = Register(colony, chest, "Orphan store");
            if (store == null)
            {
                report.Check(false, "orphaned-mid-haul check could register a chest");
                yield break;
            }

            ColonyOperations.EditSettings(colony, store.Id, s => s.Accepts = new List<string> { "Wood" });
            colony.State.SetJobs(new List<JobDefinition>
            {
                new JobDefinition { Id = "lost", Name = "Lost", Kind = JobKind.Haul, Repeat = 30 }
            });

            Villager stray = VillagerLifecycle.Spawn(colony);
            yield return null;
            if (stray == null || !stray.TryGetComponent(out ZNetView view) || !view.IsValid())
            {
                report.Check(false, "orphaned-mid-haul check could spawn a villager");
                yield break;
            }

            ZDO zdo = view.GetZDO();
            ZDOID who = zdo.m_uid;
            ZDOID home = ColonyMembership.GetColony(zdo);
            VillagerState state = new VillagerState(zdo);
            SendRested(view);

            // Put it mid-trip rather than waiting for it to get there on its own. Watching for
            // a natural pickup means racing the delivery that follows it - the window where the
            // bag holds something is a few seconds wide, and a sampler that misses it reports a
            // villager that never worked. What is being tested is the state, not how it was
            // reached.
            Container bag = VillagerInventory.Attach(stray.gameObject, view);
            Clear(bag.GetInventory());
            for (int i = 0; i < 4; i++) Add(bag.GetInventory(), "Wood");

            int carrying = Count(bag.GetInventory(), "Wood");
            state.SetCargo("Wood");
            state.SetDestination(store.Id);
            state.SetWorkState((int)Jobs.Haul.HaulState.Delivering);
            state.SetQueue(new List<string> { "lost" });

            report.Check(carrying == 4,
                "control: the villager is carrying the settlement's goods before it loses it",
                $"carrying={carrying}");

            ColonyMembership.SetColony(zdo, ZDOID.None);
            yield return new WaitForSecondsRealtime(1.5f);

            report.Check(stray != null && stray.Activity == Villager.NoKolonyActivity,
                "a villager orphaned mid-trip stops rather than finishing a delivery to nobody",
                $"doing='{stray?.Activity}'");

            Container held = stray == null ? null : stray.GetComponentInChildren<Container>(true);
            int kept = held == null ? -1 : Count(held.GetInventory(), "Wood");
            report.Check(kept == carrying,
                "and it is still holding what it had picked up, rather than dropping it",
                $"kept={kept} of {carrying}");

            report.Check(StructureInventory.Count(store.Id, "Wood") == 0,
                "control: nothing reached the chest while it had no Kolony to work for",
                $"inChest={StructureInventory.Count(store.Id, "Wood")}");

            // Taken back in. An orphan that can never work again is one to replace, not rescue.
            ColonyMembership.SetColony(zdo, home);
            SettlementIndex.ResetForTest();

            int delivered = 0;
            for (int sample = 0; sample < 120 && delivered == 0; sample++)
            {
                yield return new WaitForSecondsRealtime(.5f);
                delivered = StructureInventory.Count(store.Id, "Wood");
            }

            report.Check(delivered > 0,
                "and it delivers once the settlement is its own again",
                $"delivered={delivered} doing='{stray?.Activity}'");

            VillagerLifecycle.Remove(colony, who);
            colony.RemoveStructure(store.Id);
            Release(chest);
            colony.State.SetJobs(new List<JobDefinition>());
            SweepLooseItems(colony);
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>
        ///     A bag that fills partway through a sweep ends the sweep and delivers.
        /// </summary>
        /// <remarks>
        ///     The decision table has always had the branch - a full bag stops collecting even
        ///     with more to take - and nothing exercised it in a game. It is also the case where
        ///     a villager's own belongings and its cargo are most likely to be confused, because
        ///     they share one inventory and the thing that is full is the inventory: a villager
        ///     packed with its own things must still haul, and must still come back with its own
        ///     things.
        /// </remarks>
        private static IEnumerator CheckABagThatFillsMidTrip(TestReport report, Colony colony, Vector3 origin)
        {
            SweepLooseItems(colony);
            SettlementIndex.ResetForTest();

            GameObject chest = Spawn("piece_chest_wood", origin + new Vector3(5f, 0f, 7f));
            yield return new WaitForSecondsRealtime(.3f);
            StructureRecord store = Register(colony, chest, "Full-bag store");
            if (store == null)
            {
                report.Check(false, "full-bag check could register a chest");
                yield break;
            }

            ColonyOperations.EditSettings(colony, store.Id, s => s.Accepts = new List<string> { "Wood" });
            colony.State.SetJobs(new List<JobDefinition>
            {
                new JobDefinition { Id = "cram", Name = "Cram", Kind = JobKind.Haul, Repeat = 30 }
            });

            // Several separate drops, so the sweep has more to take than it can hold.
            for (int i = 0; i < 4; i++) DropItem("Wood", origin + new Vector3(2f + i, 0f, 4f), 1);

            Villager hauler = VillagerLifecycle.Spawn(colony);
            yield return null;
            if (hauler == null || !hauler.TryGetComponent(out ZNetView view) || !view.IsValid())
            {
                report.Check(false, "full-bag check could spawn a villager");
                yield break;
            }

            ZDOID who = view.GetZDO().m_uid;
            SendRested(view);

            // Its own belongings in every slot but one. Placed by grid position rather than
            // added, because adding stacks them into a single slot and what has to be scarce
            // here is slots, not items.
            Container bag = VillagerInventory.Attach(hauler.gameObject, view);
            Inventory carried = bag.GetInventory();
            Clear(carried);

            int width = Mathf.Max(1, carried.GetWidth());
            int height = Mathf.Max(1, carried.GetHeight());
            for (int slot = 0; slot < width * height - 1; slot++)
            {
                Split(carried, "Coal", 1, slot % width, slot / width);
            }

            int belongings = Count(carried, "Coal");
            report.Check(carried.GetEmptySlots() == 1 && belongings > 0,
                "control: the villager has one free slot and belongings of its own",
                $"free={carried.GetEmptySlots()} coal={belongings}");

            new VillagerState(view.GetZDO()).SetQueue(new List<string> { "cram" });

            int delivered = 0;
            for (int sample = 0; sample < 120 && delivered == 0; sample++)
            {
                yield return new WaitForSecondsRealtime(.5f);
                delivered = StructureInventory.Count(store.Id, "Wood");
            }

            report.Check(delivered > 0,
                "a villager whose bag fills mid-sweep delivers rather than stalling",
                $"delivered={delivered} doing='{hauler?.Activity}'");

            Container after = hauler == null ? null : hauler.GetComponentInChildren<Container>(true);
            int kept = after == null ? -1 : Count(after.GetInventory(), "Coal");

            report.Check(kept == belongings,
                "control: and it comes back with its own things, which were never cargo",
                $"kept={kept} of {belongings}, inChest={StructureInventory.Count(store.Id, "Coal")}");

            VillagerLifecycle.Remove(colony, who);
            colony.RemoveStructure(store.Id);
            Release(chest);
            colony.State.SetJobs(new List<JobDefinition>());
            SweepLooseItems(colony);
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>
        ///     The thing being fetched is taken away while the villager is walking to it.
        /// </summary>
        /// <remarks>
        ///     The player picking up a log a villager is halfway to is an ordinary afternoon in
        ///     a settlement, and the failure it causes is the quiet kind: a villager holding a
        ///     target that no longer exists, walking to where it used to be, or standing still
        ///     holding a claim nobody can take off it. The claim being released matters as much
        ///     as the villager recovering, because a held claim outlives the villager's interest
        ///     in it and stops everyone else.
        /// </remarks>
        private static IEnumerator CheckTheItemVanishingMidWalk(TestReport report, Colony colony,
            Vector3 origin)
        {
            SweepLooseItems(colony);
            SettlementIndex.ResetForTest();

            GameObject chest = Spawn("piece_chest_wood", origin + new Vector3(5f, 0f, 7f));
            yield return new WaitForSecondsRealtime(.3f);
            StructureRecord store = Register(colony, chest, "Vanishing store");
            if (store == null)
            {
                report.Check(false, "vanishing-item check could register a chest");
                yield break;
            }

            ColonyOperations.EditSettings(colony, store.Id, s => s.Accepts = new List<string> { "Wood" });
            colony.State.SetJobs(new List<JobDefinition>
            {
                new JobDefinition { Id = "chase", Name = "Chase", Kind = JobKind.Haul, Repeat = 30 }
            });

            // Far enough out that there is a walk to interrupt.
            ItemDrop bait = DropItem("Wood", origin + new Vector3(-4f, 0f, 14f), 4);
            if (bait == null || !bait.TryGetComponent(out ZNetView baitView) || !baitView.IsValid())
            {
                report.Check(false, "vanishing-item check could drop an item");
                yield break;
            }

            ZDOID baitId = baitView.GetZDO().m_uid;

            Villager chaser = VillagerLifecycle.Spawn(colony);
            yield return null;
            if (chaser == null || !chaser.TryGetComponent(out ZNetView view) || !view.IsValid())
            {
                report.Check(false, "vanishing-item check could spawn a villager");
                yield break;
            }

            ZDOID who = view.GetZDO().m_uid;
            SendRested(view);
            new VillagerState(view.GetZDO()).SetQueue(new List<string> { "chase" });

            // Wait until it has actually committed to the item, so this interrupts a walk
            // rather than racing the decision to start one.
            VillagerState chaserState = new VillagerState(view.GetZDO());
            bool committed = false;
            for (int sample = 0; sample < 60 && !committed; sample++)
            {
                yield return new WaitForSecondsRealtime(.25f);
                committed = chaserState.Target == baitId;
            }

            report.Check(committed,
                "control: the villager set off for the item before it was taken away",
                $"committed={committed} doing='{chaser?.Activity}'");
            if (!committed) yield break;

            // Snatched, the way a player picking it up would.
            baitView.Destroy();
            yield return new WaitForSecondsRealtime(.5f);

            bool released = false;
            for (int sample = 0; sample < 40 && !released; sample++)
            {
                yield return new WaitForSecondsRealtime(.25f);
                released = chaserState.Target != baitId;
            }

            report.Check(released,
                "a villager lets go of something that was taken away while it walked to it",
                $"target={chaserState.Target} doing='{chaser?.Activity}'");

            report.Check(ZNetScene.instance.FindInstance(who) != null,
                "control: and it is still alive to do something else",
                $"loaded={ZNetScene.instance.FindInstance(who) != null}");

            // And it gets on with the next thing rather than standing where the item was.
            DropItem("Wood", origin + new Vector3(3f, 0f, 5f), 3);
            int delivered = 0;
            for (int sample = 0; sample < 100 && delivered == 0; sample++)
            {
                yield return new WaitForSecondsRealtime(.5f);
                delivered = StructureInventory.Count(store.Id, "Wood");
            }

            report.Check(delivered > 0,
                "and takes new work rather than waiting for what is gone",
                $"delivered={delivered} doing='{chaser?.Activity}'");

            VillagerLifecycle.Remove(colony, who);
            colony.RemoveStructure(store.Id);
            Release(chest);
            colony.State.SetJobs(new List<JobDefinition>());
            SweepLooseItems(colony);
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>
        ///     A chest with room for part of a load takes that part, rather than refusing it all.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         "Partial deposits are fine; what will not fit stays in the bag" is how the job
        ///         is specified, and it is the taking half that was written that way - a stack
        ///         larger than the space left is taken in part rather than refused, because all
        ///         or nothing means a bag with four free slots ignores a chest holding one
        ///         enormous stack.
        ///     </para>
        ///     <para>
        ///         The delivering half asks <c>CanAddItem</c>, which answers for the whole stack
        ///         at once. So this stages the case directly: a chest with room for two more wood
        ///         and a villager carrying five.
        ///     </para>
        /// </remarks>
        private static IEnumerator CheckAPartlyFullChestTakesWhatFits(TestReport report, Colony colony,
            Vector3 origin)
        {
            SweepLooseItems(colony);
            SettlementIndex.ResetForTest();

            GameObject chest = Spawn("piece_chest_wood", origin + new Vector3(5f, 0f, 7f));
            yield return new WaitForSecondsRealtime(.3f);
            StructureRecord store = Register(colony, chest, "Nearly full store");
            Container container = chest == null ? null : chest.GetComponentInChildren<Container>(true);
            if (store == null || container == null)
            {
                report.Check(false, "partial-deposit check could register a chest");
                yield break;
            }

            ColonyOperations.EditSettings(colony, store.Id, s => s.Accepts = new List<string> { "Wood" });

            // Every slot taken, and the last one a wood stack two short of full: room for
            // exactly two more wood and nowhere else for them to go.
            Inventory inventory = container.GetInventory();
            Fill(inventory, "Stone");
            int width = Mathf.Max(1, inventory.GetWidth());
            int height = Mathf.Max(1, inventory.GetHeight());
            ItemDrop.ItemData last = inventory.GetItemAt(width - 1, height - 1);
            if (last != null) inventory.RemoveItem(last);

            GameObject woodPrefab = ObjectDB.instance?.GetItemPrefab("Wood");
            int maximum = woodPrefab != null && woodPrefab.TryGetComponent(out ItemDrop woodDrop)
                ? woodDrop.m_itemData.m_shared.m_maxStackSize
                : 50;
            Split(inventory, "Wood", maximum - 2, width - 1, height - 1);

            int roomFor = maximum - CountIn(container, "Wood");
            report.Check(roomFor == 2 && inventory.GetEmptySlots() == 0,
                "control: the chest has room for exactly two more wood and no empty slot",
                $"room={roomFor} emptySlots={inventory.GetEmptySlots()}");

            colony.State.SetJobs(new List<JobDefinition>
            {
                new JobDefinition { Id = "squeeze", Name = "Squeeze", Kind = JobKind.Haul, Repeat = 30 }
            });

            // Five on the ground, which is more than will fit.
            DropItem("Wood", origin + new Vector3(3f, 0f, 5f), 5);

            Villager hauler = VillagerLifecycle.Spawn(colony);
            yield return null;
            if (hauler == null || !hauler.TryGetComponent(out ZNetView view) || !view.IsValid())
            {
                report.Check(false, "partial-deposit check could spawn a villager");
                yield break;
            }

            ZDOID who = view.GetZDO().m_uid;
            SendRested(view);
            new VillagerState(view.GetZDO()).SetQueue(new List<string> { "squeeze" });

            int before = CountIn(container, "Wood");
            int added = 0;
            for (int sample = 0; sample < 100 && added < 2; sample++)
            {
                yield return new WaitForSecondsRealtime(.5f);
                added = CountIn(container, "Wood") - before;
            }

            report.Check(added == 2,
                "a chest with room for part of a load takes that part rather than refusing it all",
                $"added={added} doing='{hauler?.Activity}'");

            VillagerLifecycle.Remove(colony, who);
            colony.RemoveStructure(store.Id);
            Release(chest);
            colony.State.SetJobs(new List<JobDefinition>());
            SweepLooseItems(colony);
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>
        ///     A chest marked "may not be taken from" is never a source, however wrong its contents.
        /// </summary>
        /// <remarks>
        ///     This is the setting that keeps a player's own chest a player's own chest. The
        ///     index filters on it, which is easy to check by reading; whether the tidying half
        ///     of the job ever reaches the index with the right question is not, and that is the
        ///     half that would empty somebody's armoury into the overflow.
        /// </remarks>
        private static IEnumerator CheckAChestMayBeLeftAlone(TestReport report, Colony colony, Vector3 origin)
        {
            SweepLooseItems(colony);
            SettlementIndex.ResetForTest();

            GameObject privateChest = Spawn("piece_chest_wood", origin + new Vector3(5f, 0f, 7f));
            GameObject shed = Spawn("piece_chest_wood", origin + new Vector3(8f, 0f, 7f));
            yield return new WaitForSecondsRealtime(.3f);

            StructureRecord locked = Register(colony, privateChest, "Private chest");
            StructureRecord store = Register(colony, shed, "Wood store");
            if (locked == null || store == null)
            {
                report.Check(false, "left-alone check could register two chests");
                yield break;
            }

            // The private chest takes anything, so the wood in it genuinely is misplaced and a
            // villager genuinely would move it. Only the flag stops it.
            ColonyOperations.EditSettings(colony, locked.Id, s =>
            {
                s.Accepts = new List<string>();
                s.MayTakeFrom = false;
            });
            ColonyOperations.EditSettings(colony, store.Id, s => s.Accepts = new List<string> { "Wood" });

            Container keep = privateChest.GetComponentInChildren<Container>(true);
            if (keep == null || PutIn(keep, "Wood", 10) != 10)
            {
                report.Check(false, "left-alone check could stock the private chest");
                yield break;
            }

            // Read back from the colony rather than reusing what Register handed over: a record
            // is a value, so the copy taken before the settings were edited still says what the
            // chest accepted then. Both chests read as overflow and the control failed while the
            // feature underneath it worked.
            StructureRecord lockedNow = SettlementIndex.Find(colony, locked.Id);
            StructureRecord storeNow = SettlementIndex.Find(colony, store.Id);
            int privateScore = lockedNow == null ? -99 : SettlementIndex.ScoreOf(lockedNow, "Wood");
            int storeScore = storeNow == null ? -99 : SettlementIndex.ScoreOf(storeNow, "Wood");

            report.Check(storeScore > privateScore,
                "control: the wood really is misplaced, so only the flag can stop the move",
                $"private={privateScore} store={storeScore}");

            colony.State.SetJobs(new List<JobDefinition>
            {
                new JobDefinition
                {
                    Id = "sort", Name = "Sort", Kind = JobKind.Haul, Repeat = 30, TidyContainers = true
                }
            });

            Villager keeper = VillagerLifecycle.Spawn(colony);
            yield return null;
            if (keeper == null || !keeper.TryGetComponent(out ZNetView view) || !view.IsValid())
            {
                report.Check(false, "left-alone check could spawn a villager");
                yield break;
            }

            ZDOID who = view.GetZDO().m_uid;
            SendRested(view);
            new VillagerState(view.GetZDO()).SetQueue(new List<string> { "sort" });

            for (int sample = 0; sample < 60; sample++) yield return new WaitForSecondsRealtime(.5f);

            int leftAlone = CountIn(keep, "Wood");
            report.Check(leftAlone == 10,
                "a chest marked not to be taken from is left alone, however wrong its contents",
                $"stillThere={leftAlone} doing='{keeper?.Activity}'");

            // Control: the same chest, the same wood, the same villager - flag off.
            ColonyOperations.EditSettings(colony, locked.Id, s => s.MayTakeFrom = true);
            SettlementIndex.ResetForTest();

            Container into = shed.GetComponentInChildren<Container>(true);
            int moved = 0;
            for (int sample = 0; sample < 120 && moved == 0; sample++)
            {
                yield return new WaitForSecondsRealtime(.5f);
                moved = into == null ? 0 : CountIn(into, "Wood");
            }

            report.Check(moved > 0,
                "control: with the flag cleared the same wood moves, so the flag is what stopped it",
                $"moved={moved} doing='{keeper?.Activity}'");

            VillagerLifecycle.Remove(colony, who);
            colony.RemoveStructure(locked.Id);
            colony.RemoveStructure(store.Id);
            Release(privateChest);
            Release(shed);
            colony.State.SetJobs(new List<JobDefinition>());
            SweepLooseItems(colony);
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>
        ///     "Fill the bag before delivering", which the job screen has always offered.
        /// </summary>
        /// <remarks>
        ///     Measured as the most the villager ever held at once, with the setting on and off.
        ///     The deterministic table proves the branch is reachable; only a run proves the
        ///     engine answers it by finding another item rather than falling straight through
        ///     to delivering, which is what it did for as long as nothing read the setting.
        /// </remarks>
        private static IEnumerator CheckFillingTheBagFirst(TestReport report, Colony colony, Vector3 origin)
        {
            SweepLooseItems(colony);
            SettlementIndex.ResetForTest();

            GameObject chest = Spawn("piece_chest_wood", origin + new Vector3(5f, 0f, 7f));
            yield return new WaitForSecondsRealtime(.3f);
            StructureRecord store = Register(colony, chest, "Load store");
            if (store == null)
            {
                report.Check(false, "full-load check could register a chest");
                yield break;
            }

            ColonyOperations.EditSettings(colony, store.Id, s => s.Accepts = new List<string> { "Wood" });

            int loaded = -1, loadedDelivered = 0, eager = -1, eagerDelivered = 0;
            yield return CarryAsMuchAsItCan(colony, origin, store, true,
                (most, moved) => { loaded = most; loadedDelivered = moved; });
            yield return CarryAsMuchAsItCan(colony, origin, store, false,
                (most, moved) => { eager = most; eagerDelivered = moved; });

            report.Check(loaded > 1 && loadedDelivered > 0,
                "a villager told to fill its bag first carries more than one thing at a time",
                $"mostHeld={loaded} delivered={loadedDelivered}");

            // Delivery is asserted as well as the load, because "never held more than one" is
            // also what a villager that did no work at all looks like - and the first version
            // of this control read exactly that way. Held is sampled rather than counted, so
            // the claim is that it never carried a load, not that it carried precisely one.
            report.Check(eagerDelivered > 0 && eager <= 1,
                "control: told to set out eagerly, the same villager never builds up a load",
                $"mostHeld={eager} delivered={eagerDelivered}");

            colony.RemoveStructure(store.Id);
            Release(chest);
            colony.State.SetJobs(new List<JobDefinition>());
            SweepLooseItems(colony);
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>Runs one load and reports the most wood the villager held at once.</summary>
        private static IEnumerator CarryAsMuchAsItCan(Colony colony, Vector3 origin, StructureRecord store,
            bool fillFirst, Action<int, int> onMeasured)
        {
            int before = StructureInventory.Count(store.Id, "Wood");

            colony.State.SetJobs(new List<JobDefinition>
            {
                new JobDefinition
                {
                    Id = "load", Name = "Load haul", Kind = JobKind.Haul,
                    Repeat = 30, FillBagFirst = fillFirst
                }
            });

            // Separate drops rather than one stack: a single stack would be picked up whole and
            // would say nothing about whether the villager went back for more.
            for (int i = 0; i < 4; i++)
            {
                DropItem("Wood", origin + new Vector3(-4f - i, 0f, 12f), 1);
            }

            Villager hauler = VillagerLifecycle.Spawn(colony);
            yield return null;
            if (hauler == null || !hauler.TryGetComponent(out ZNetView view) || !view.IsValid())
            {
                onMeasured(-1, 0);
                yield break;
            }

            ZDOID who = view.GetZDO().m_uid;
            SendRested(view);
            new VillagerState(view.GetZDO()).SetQueue(new List<string> { "load" });

            int most = 0;
            var doing = new HashSet<string>();
            for (int sample = 0; sample < 160; sample++)
            {
                doing.Add(hauler == null ? "?" : hauler.Activity ?? "?");
                yield return new WaitForSecondsRealtime(.25f);
                if (hauler == null || ZNetScene.instance.FindInstance(who) == null) break;

                Container bag = hauler.GetComponentInChildren<Container>(true);
                Inventory inventory = bag == null ? null : bag.GetInventory();
                if (inventory == null) continue;

                int wood = 0;
                foreach (ItemDrop.ItemData held in inventory.GetAllItems())
                {
                    if (Carrying.NameOf(held) == "Wood") wood += held.m_stack;
                }

                if (wood > most) most = wood;
            }

            int delivered = Mathf.Max(0, StructureInventory.Count(store.Id, "Wood") - before);
            Core.Log.Info($"[load] fillFirst={fillFirst} mostHeld={most} delivered={delivered} " +
                          $"activities=[{string.Join(", ", doing)}]");

            VillagerLifecycle.Remove(colony, who);
            SweepLooseItems(colony);
            yield return new WaitForSecondsRealtime(.2f);
            onMeasured(most, delivered);
        }

        /// <summary>
        ///     Two villagers, one log: the claim, measured end to end and with the switch off.
        /// </summary>
        /// <remarks>
        ///     The unit checks elsewhere prove the claim registry answers correctly. They cannot
        ///     prove the job <em>asks</em> it, which is the part that actually stops two
        ///     villagers walking to one stack. So this counts collisions — moments where two
        ///     villagers hold the same target at once — through a real haul, and then runs the
        ///     same haul with <c>ClaimsEnabled</c> off. An assertion that has never failed
        ///     proves nothing; the control is what makes the zero mean something.
        /// </remarks>
        private static IEnumerator CheckTwoVillagersOneItem(TestReport report, Colony colony, Vector3 origin)
        {
            SweepLooseItems(colony);
            SettlementIndex.ResetForTest();

            GameObject chest = Spawn("piece_chest_wood", origin + new Vector3(5f, 0f, 7f));
            yield return new WaitForSecondsRealtime(.3f);
            StructureRecord store = Register(colony, chest, "Contested store");
            if (store == null)
            {
                report.Check(false, "contention check could register a chest");
                yield break;
            }

            ColonyOperations.EditSettings(colony, store.Id, s => s.Accepts = new List<string> { "Wood" });
            colony.State.SetJobs(new List<JobDefinition>
            {
                new JobDefinition { Id = "race", Name = "Race haul", Kind = JobKind.Haul, Repeat = 30 }
            });

            bool wasEnabled = ModConfig.ClaimsEnabled.Value;
            int withClaims = -1, withoutClaims = -1;
            bool standing = true;

            yield return RaceForOneItem(colony, origin, store, true,
                (r, ok) => { withClaims = r; standing &= ok; });
            yield return RaceForOneItem(colony, origin, store, false,
                (r, ok) => { withoutClaims = r; standing &= ok; });

            // A round whose chest quietly stopped being a destination reports no collisions for
            // the same reason an empty settlement does. Said out loud, because the control
            // round did exactly that and read as a pass of the thing it was controlling for.
            report.Check(standing,
                "control: both rounds had a working chest and the wood reached it",
                $"standing={standing}");

            ModConfig.ClaimsEnabled.Value = wasEnabled;

            report.Check(withClaims == 0,
                "two villagers never reach for the same item at once",
                $"collisions={withClaims}");

            report.Check(withoutClaims > 0,
                "control: with claims switched off they do collide, so the zero above is real",
                $"collisions={withoutClaims}");

            colony.RemoveStructure(store.Id);
            Release(chest);
            colony.State.SetJobs(new List<JobDefinition>());
            SweepLooseItems(colony);
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>
        ///     Runs one two-villager race and reports how often they held the same target.
        /// </summary>
        private static IEnumerator RaceForOneItem(Colony colony, Vector3 origin, StructureRecord store,
            bool claims, Action<int, bool> onCounted)
        {
            ModConfig.ClaimsEnabled.Value = claims;
            TargetClaims.Invalidate();
            int stocked = StructureInventory.Count(store.Id, "Wood");

            // One stack, far enough out that both have to walk for it - a race decided before
            // anyone takes a step measures nothing.
            DropItem("Wood", origin + new Vector3(0f, 0f, 18f), 5);

            var racers = new List<ZDOID>();
            for (int i = 0; i < 2; i++)
            {
                Villager racer = VillagerLifecycle.Spawn(colony);
                yield return null;
                if (racer == null || !racer.TryGetComponent(out ZNetView view) || !view.IsValid()) continue;

                SendRested(view);
                new VillagerState(view.GetZDO()).SetQueue(new List<string> { "race" });
                racers.Add(view.GetZDO().m_uid);
            }

            // Why the villagers did or did not find the wood, asked of the same functions the
            // job asks. A control round that quietly found no work reads as "no collisions".
            int loose = 0;
            foreach (ItemDrop drop in ItemDrop.s_instances)
            {
                if (drop == null || drop.m_itemData?.m_dropPrefab == null) continue;
                if (Utils.GetPrefabName(drop.m_itemData.m_dropPrefab) == "Wood") loose++;
            }

            StructureRecord home = Selection.WhereFor(colony, "Wood", origin);
            bool usable = home != null && home.Id == store.Id;

            Core.Log.Info($"[race] claims={claims} looseWood={loose} racers={racers.Count} " +
                          $"home={(home == null ? "none" : home.Name)} usable={usable} " +
                          $"registered={Registered(colony, store.Id)}");

            int collisions = 0;
            int busy = 0;
            var shared = new HashSet<string>();
            var doing = new HashSet<string>();
            var last = new Dictionary<ZDOID, Vector3>();
            var walked = new Dictionary<ZDOID, float>();
            var closest = new Dictionary<ZDOID, float>();

            for (int sample = 0; sample < 120; sample++)
            {
                yield return new WaitForSecondsRealtime(.25f);
                collisions += SharedTargets(shared);

                foreach (Villager watched in Villager.Instances)
                {
                    if (watched == null || !watched.State.IsValid) continue;
                    if (!watched.State.Target.IsNone()) busy++;
                    doing.Add(watched.Activity ?? "?");

                    // Ground covered and nearest approach, which is what tells "stood still"
                    // apart from "walked the whole time and never arrived" - two different
                    // bugs that produce the same word in an activity log.
                    ZDOID id = watched.Id;
                    if (id.IsNone()) continue;
                    Vector3 now = watched.transform.position;
                    if (last.TryGetValue(id, out Vector3 was))
                    {
                        walked.TryGetValue(id, out float sofar);
                        walked[id] = sofar + Utils.DistanceXZ(was, now);
                    }

                    last[id] = now;

                    GameObject target = ZNetScene.instance == null
                        ? null
                        : ZNetScene.instance.FindInstance(watched.State.Target);
                    if (target == null) continue;

                    float gap = Utils.DistanceXZ(now, target.transform.position);
                    if (!closest.TryGetValue(id, out float best) || gap < best) closest[id] = gap;
                }
            }

            var trace = new List<string>();
            foreach (KeyValuePair<ZDOID, float> entry in walked)
            {
                if (entry.Value <= 0f) continue;

                closest.TryGetValue(entry.Key, out float near);
                trace.Add($"walked={entry.Value:0}m closest={near:0}m");
            }

            Core.Log.Info($"[race] claims={claims} collisions={collisions} busySamples={busy} " +
                     $"villagers={racers.Count} sharedTargets=[{string.Join(", ", shared)}] " +
                     $"activities=[{string.Join(", ", doing)}] {string.Join(" | ", trace)}");

            // Whether anybody actually got there. Two villagers that both fail to reach the
            // wood also collide zero times, and a run where exactly that happened read as a
            // clean pass of the claim.
            int arrived = StructureInventory.Count(store.Id, "Wood") - stocked;

            foreach (ZDOID racer in racers) VillagerLifecycle.Remove(colony, racer);
            SweepLooseItems(colony);
            yield return new WaitForSecondsRealtime(.2f);
            onCounted(collisions, usable && arrived > 0);
        }

        /// <summary>
        ///     Walking up to somebody and pressing use opens their screen.
        /// </summary>
        /// <remarks>
        ///     A settlement is a place full of people, and the way you deal with a person is to
        ///     go and talk to them. Finding their row in a list opened from a hearth works and
        ///     stops being reasonable the moment you can see the villager you mean.
        /// </remarks>
        private static IEnumerator CheckWalkingUpToAVillager(TestReport report, Colony colony)
        {
            Villager subject = VillagerLifecycle.Spawn(colony);
            yield return null;
            if (subject == null || !subject.TryGetComponent(out ZNetView view) || !view.IsValid())
            {
                report.Check(false, "hover check could spawn a villager");
                yield break;
            }

            ZDOID who = view.GetZDO().m_uid;
            yield return new WaitForSecondsRealtime(.4f);

            // Asked of DescribeForHover, which is what the Character patch returns to the
            // game. An earlier version of this asserted a GetHoverText on the villager itself
            // and passed while the text never reached a player: Character already implements
            // Hoverable and wins the lookup, so the method under test was one nothing called.
            string hover = subject.DescribeForHover() ?? string.Empty;
            report.Check(hover.Contains(subject.DisplayName()) && hover.Contains(subject.Activity),
                "looking at a villager says who they are and what they are doing",
                $"hover='{hover.Replace("\n", " | ")}'");

            report.Check(hover.Contains("$KEY_Use") || hover.ToLowerInvariant().Contains("manage"),
                "and offers the key that opens their screen",
                $"hover='{hover.Replace("\n", " | ")}'");

            ColonyScreen screen = ColonyScreen.Instance;
            if (screen == null)
            {
                report.Check(false, "hover check had a colony screen to open");
                yield break;
            }

            if (screen.IsOpen) screen.Close();
            yield return null;

            report.Check(!screen.IsOpen, "control: the screen is shut before anyone is spoken to",
                $"open={screen.IsOpen}");

            bool took = subject.Interact(null, false, false);
            yield return null;

            report.Check(took && screen.IsOpen && screen.Current is VillagerDetailScreen,
                "using a villager opens the colony screen on that villager",
                $"handled={took} open={screen.IsOpen} top={screen.Current?.GetType().Name}");

            // Held keys repeat. A screen that reopens twenty times a second cannot be used.
            bool whileHeld = subject.Interact(null, true, false);
            report.Check(!whileHeld,
                "control: holding the key does not reopen it over and over",
                $"heldHandled={whileHeld}");

            screen.Close();
            VillagerLifecycle.Remove(colony, who);
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>Whether a record is still on the colony's books.</summary>
        private static bool Registered(Colony colony, ZDOID structure)
        {
            foreach (StructureRecord record in colony.State.GetStructures())
            {
                if (record.Id == structure) return true;
            }

            return false;
        }

        /// <summary>
        ///     How many villagers are working on something another villager is also working on.
        /// </summary>
        /// <remarks>
        ///     Counted off <see cref="Villager.Instances" /> rather than through
        ///     <see cref="TargetClaims" />: that index is keyed by target, so it collapses
        ///     exactly the duplicates this is trying to find and would report zero always.
        /// </remarks>
        private static int SharedTargets(HashSet<string> saw = null)
        {
            var seen = new Dictionary<ZDOID, int>();
            foreach (Villager villager in Villager.Instances)
            {
                if (villager == null || !villager.State.IsValid) continue;

                ZDOID target = villager.State.Target;
                if (target.IsNone()) continue;

                seen.TryGetValue(target, out int count);
                seen[target] = count + 1;
            }

            int shared = 0;
            foreach (KeyValuePair<ZDOID, int> entry in seen)
            {
                if (entry.Value <= 1) continue;

                shared += entry.Value - 1;
                if (saw == null) continue;

                GameObject what = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(entry.Key) : null;
                saw.Add(what == null ? "gone" : Utils.GetPrefabName(what));
            }

            return shared;
        }

        /// <summary>The chests the hauling checks place, and nobody else's.</summary>
        private static readonly HashSet<string> HaulFixtures = new HashSet<string>
        {
            // Every name the hauling and tidying checks register, including the ones belonging
            // to checks this sweep merely follows. The first version listed only the chests I
            // had just added, so a "Stone store" or a "Wood shed" abandoned by an early return
            // survived to poison the next check - which is the exact failure this exists to
            // stop, left in place for half the fixtures.
            "The only chest", "Contested store", "Load store", "Private chest", "Wood store",
            "Stone store", "Nearly full store", "Vanishing store", "Full-bag store",
            "Orphan store", "Overflow", "Wood shed"
        };

        /// <summary>
        ///     Puts the settlement back the way the next check expects to find it.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Every check tidies up after itself, and that is not enough: a check that gives
        ///         up partway - a fixture that would not place, a villager that never picked
        ///         anything up - leaves through an early return and never reaches its own
        ///         cleanup. One chest left registered then poisons everything after it, and the
        ///         failures land on checks that have nothing to do with the fault. That is
        ///         exactly what happened: an abandoned "Orphan store" still holding fourteen
        ///         wood made the tidying check report that wood no longer migrates to the chest
        ///         that asked for it, because the wood had gone somewhere perfectly reasonable
        ///         that the check knew nothing about.
        ///     </para>
        ///     <para>
        ///         So the sequence clears the ground between checks rather than trusting each
        ///         one to. It deliberately spares the persistence fixtures, which exist to
        ///         survive to the save, and says what it removed - a check leaking a fixture is
        ///         worth knowing about even when the run goes green.
        ///     </para>
        /// </remarks>
        private static IEnumerator ClearTheGround(Colony colony, string after)
        {
            if (colony == null) yield break;

            // By name, and only the ones the haul checks place. Clearing everything not
            // needed for persistence looked tidier and was wrong: several checks leave a
            // fixture on purpose for a later one to find - a renamed chest, a bed, a kiln, a
            // structure that is deliberately unfindable - and sweeping those away failed the
            // screens that go looking for them. A fixture nobody registered is not leaked, it
            // is somebody else's.
            var leaked = new List<string>();
            foreach (StructureRecord record in colony.State.GetStructures())
            {
                if (!HaulFixtures.Contains(record.Name)) continue;

                GameObject instance = ZNetScene.instance?.FindInstance(record.Id);
                leaked.Add(record.Name);
                colony.RemoveStructure(record.Id);
                if (instance != null) Release(instance);
            }

            // Villagers too. Several checks return early after spawning one, and a villager
            // left behind is counted by the next check's claim and collision measurements.
            //
            // The run's own primary villager is spared, and sparing it by name would not have
            // worked: it is not renamed until the persistence snapshot, which runs after every
            // phase, so a sweep keyed on the persisted names would have destroyed it on the
            // very first call. What depended on it - the screen audit, two photographs and the
            // whole reload stage - would then have failed somewhere else entirely.
            ZDOID primary = GetPrimaryMember(colony);
            foreach (ZDOID member in colony.State.GetMembers(ColonyMemberKind.Villager))
            {
                if (member == primary) continue;

                ZDO record = ZDOMan.instance?.GetZDO(member);
                string name = record == null ? string.Empty : new VillagerState(record).Name;
                if (name == PersistedVillagerName || name == PersistedHaulerName) continue;

                VillagerLifecycle.Remove(colony, member);
            }

            // Sparing the primary means it now outlives every check, including the one that
            // hands work out to everybody. It must go back to idle: the persistence snapshot
            // states plainly that it has nothing to do until the save, and a queue left on it
            // would be carried into the save to be resumed by a later build.
            ZDO primaryRecord = primary.IsNone() ? null : ZDOMan.instance?.GetZDO(primary);
            if (primaryRecord != null)
            {
                // SetQueue resets position, attempt and the job itself, so that is all of it.
                new VillagerState(primaryRecord).SetQueue(new List<string>());
            }

            colony.State.SetJobs(new List<JobDefinition>());
            SweepLooseItems(colony);
            SettlementIndex.ResetForTest();
            TargetClaims.Invalidate();

            if (leaked.Count > 0)
            {
                Core.Log.Info($"[Benchmark] cleared after {after}: {string.Join(", ", leaked)}");
            }

            yield return new WaitForSecondsRealtime(.3f);
        }

        /// <summary>
        ///     Hauling when the world does not cooperate.
        /// </summary>
        /// <remarks>
        ///     Everything here is a thing that happens in a real settlement and nowhere in a
        ///     tidy test: a chest that fills, a chest destroyed while somebody is walking to it,
        ///     two villagers reaching for one log, and things lying outside the settlement
        ///     entirely. A job that only works when nothing changes is not a job.
        /// </remarks>
        private static IEnumerator CheckHaulingGoesWrong(TestReport report, Colony colony, Vector3 origin)
        {
            SweepLooseItems(colony);
            SettlementIndex.ResetForTest();

            GameObject chest = Spawn("piece_chest_wood", origin + new Vector3(5f, 0f, 7f));
            yield return new WaitForSecondsRealtime(.3f);
            StructureRecord store = Register(colony, chest, "The only chest");
            if (store == null)
            {
                report.Check(false, "adversarial haul check could register a chest");
                yield break;
            }

            ColonyOperations.EditSettings(colony, store.Id, s => s.Accepts = new List<string> { "Wood" });

            colony.State.SetJobs(new List<JobDefinition>
            {
                new JobDefinition { Id = "rough", Name = "Rough haul", Kind = JobKind.Haul, Repeat = 30 }
            });

            // 1. Something lying outside the settlement is not the settlement's business.
            Vector3 beyond = origin + new Vector3(colony.EffectiveRadius + 25f, 0f, 0f);
            ItemDrop far = DropItem("Wood", beyond, 3);
            ZDOID farId = far == null ? ZDOID.None : far.GetComponent<ZNetView>().GetZDO().m_uid;

            // 2. A full chest cannot be the answer to anything.
            if (chest.TryGetComponent(out Container full)) Fill(full.GetInventory(), "Stone");

            ItemDrop nearby = DropItem("Wood", origin + new Vector3(3f, 0f, 3f), 3);
            report.Check(nearby != null && far != null,
                "control: wood inside the settlement and wood beyond it",
                $"near={(nearby != null)} far={(far != null)}");
            if (nearby == null || far == null) yield break;

            Villager hand = VillagerLifecycle.Spawn(colony);
            yield return null;
            if (hand == null || !hand.TryGetComponent(out ZNetView view) || !view.IsValid())
            {
                report.Check(false, "adversarial haul check could spawn a villager");
                yield break;
            }

            ZDOID who = view.GetZDO().m_uid;
            SendRested(view);
            new VillagerState(view.GetZDO()).SetQueue(new List<string> { "rough" });

            // With the only chest full, it must not spin: give it time to prove it settles.
            float strayed = 0f;
            for (int attempt = 0; attempt < 40; attempt++)
            {
                yield return new WaitForSecondsRealtime(.5f);
                if (hand == null || ZNetScene.instance.FindInstance(who) == null) break;

                float away = Utils.DistanceXZ(hand.transform.position, colony.transform.position);
                if (away > strayed) strayed = away;
            }

            report.Check(ZNetScene.instance.FindInstance(who) != null,
                "a villager with nowhere to put anything is still alive and still here",
                $"doing='{hand?.Activity}'");

            report.Check(strayed <= colony.EffectiveRadius,
                "control: and it did not go looking beyond the settlement for somewhere to put it",
                $"strayed {strayed:0}m");

            // 3. Now make room, and it should get on with it.
            if (chest.TryGetComponent(out Container emptied)) emptied.GetInventory().RemoveAll();
            SettlementIndex.ResetForTest();

            int delivered = 0;
            for (int attempt = 0; attempt < 80 && delivered == 0; attempt++)
            {
                yield return new WaitForSecondsRealtime(.5f);
                delivered = StructureInventory.Count(store.Id, "Wood");
            }

            report.Check(delivered > 0,
                "a villager that had nowhere to put things starts again once there is room",
                $"delivered={delivered} doing='{hand?.Activity}'");

            // Only worth asserting now: while the chest was full this villager was hauling
            // nothing at all, so "it left the far wood alone" said nothing about reach. It
            // has now provably hauled, and still did not go outside for the rest.
            report.Check(delivered > 0 && ZNetScene.instance.FindInstance(farId) != null,
                "wood outside the settlement is left where it lies, by a villager that is hauling",
                $"stillThere={ZNetScene.instance.FindInstance(farId) != null} delivered={delivered}");

            // 4. Destroy the chest out from under it and make sure it recovers rather than
            //    spinning on a destination that no longer exists.
            colony.RemoveStructure(store.Id);
            Release(chest);
            DropItem("Wood", origin + new Vector3(-3f, 0f, 3f), 3);
            yield return new WaitForSecondsRealtime(6f);

            report.Check(ZNetScene.instance.FindInstance(who) != null,
                "a villager survives its destination being destroyed under it",
                $"doing='{hand?.Activity}'");

            VillagerLifecycle.Remove(colony, who);
            colony.State.SetJobs(new List<JobDefinition>());
            SweepLooseItems(colony);
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>
        ///     A full haul: two kinds of thing, two chests, and a villager that stays put.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The wandering check is the reason this exists. A villager walking off in a
        ///         direction unrelated to its work is the loudest possible symptom and the
        ///         easiest to miss in a check that only asks whether the wood arrived - it can
        ///         arrive eventually while the villager spends a minute walking to the horizon
        ///         and back.
        ///     </para>
        ///     <para>
        ///         So how far it strays is measured every half second and asserted against the
        ///         settlement's own radius. Work is bounded by that radius, so anything beyond it
        ///         is by definition not work.
        ///     </para>
        /// </remarks>
        private static IEnumerator CheckHaulingThoroughly(TestReport report, Colony colony, Vector3 origin)
        {
            SweepLooseItems(colony);
            SettlementIndex.ResetForTest();

            GameObject woodChest = Spawn("piece_chest_wood", origin + new Vector3(5f, 0f, 7f));
            GameObject stoneChest = Spawn("piece_chest_wood", origin + new Vector3(8f, 0f, 7f));
            yield return new WaitForSecondsRealtime(.3f);

            StructureRecord woodStore = Register(colony, woodChest, "Wood store");
            StructureRecord stoneStore = Register(colony, stoneChest, "Stone store");
            if (woodStore == null || stoneStore == null)
            {
                report.Check(false, "haul check could register two chests");
                yield break;
            }

            ColonyOperations.EditSettings(colony, woodStore.Id, s => s.Accepts = new List<string> { "Wood" });
            ColonyOperations.EditSettings(colony, stoneStore.Id, s => s.Accepts = new List<string> { "Stone" });

            ItemDrop wood = DropItem("Wood", origin + new Vector3(3f, 0f, 3f), 5);
            ItemDrop stone = DropItem("Stone", origin + new Vector3(-3f, 0f, 3f), 5);
            report.Check(wood != null && stone != null,
                "control: there is wood and stone on the ground to sort",
                $"wood={(wood != null)} stone={(stone != null)}");
            if (wood == null || stone == null) yield break;

            colony.State.SetJobs(new List<JobDefinition>
            {
                new JobDefinition { Id = "sort", Name = "Sort", Kind = JobKind.Haul, Repeat = 20 }
            });

            Villager hand = VillagerLifecycle.Spawn(colony);
            yield return null;
            if (hand == null || !hand.TryGetComponent(out ZNetView view) || !view.IsValid())
            {
                report.Check(false, "haul check could spawn a villager");
                yield break;
            }

            ZDOID who = view.GetZDO().m_uid;
            SendRested(view);
            new VillagerState(view.GetZDO()).SetQueue(new List<string> { "sort" });

            float strayed = 0f;
            int inWood = 0;
            int inStone = 0;
            List<string> story = new List<string>();

            // Photographed as it goes, not only totted up afterwards. A chest that gained five
            // wood proves the arithmetic; it does not show a villager walking to it, and a
            // villager that arrives by sliding there backwards passes every assertion here.
            bool caughtFetching = false;
            bool caughtCarrying = false;

            for (int attempt = 0; attempt < 160 && (inWood < 5 || inStone < 5); attempt++)
            {
                yield return new WaitForSecondsRealtime(.5f);

                if (hand == null || ZNetScene.instance?.FindInstance(who) == null) break;

                float away = Utils.DistanceXZ(hand.transform.position, colony.transform.position);
                if (away > strayed) strayed = away;

                inWood = StructureInventory.Count(woodStore.Id, "Wood");
                inStone = StructureInventory.Count(stoneStore.Id, "Stone");

                if (story.Count == 0 || story[story.Count - 1] != hand.Activity) story.Add(hand.Activity);

                // Two independent ifs rather than a chain: a villager can be worth
                // photographing walking out and walking back within the same sample, and an
                // else-if meant the first sample was spent on one of them and the other could
                // then be missed entirely.
                if (!caughtFetching && hand.Activity == "fetching")
                {
                    caughtFetching = true;
                    GameObject fetching = ZNetScene.instance?.FindInstance(new VillagerState(view.GetZDO()).Target);

                    // The chests are the fallback frame, not a null: a target that stopped
                    // resolving mid-walk collapses a null second point onto the subject and
                    // produces the minimum-distance lone close-up the end-of-run fallbacks
                    // were written to avoid - and the latch above means no later shot
                    // replaces this one. The caption stops asserting a journey the frame
                    // cannot show, which was this block's own lesson applied one call late.
                    yield return BenchmarkUiScenario.PhotographAtWork("haul-fetching.png",
                        hand.transform.position,
                        fetching != null
                            ? $"'{hand.DisplayName()}' on its way to something to pick up"
                            : $"'{hand.DisplayName()}' set out fetching, but its target no longer resolves",
                        fetching != null ? fetching.transform.position : woodChest.transform.position);
                }

                // Asked again, because the capture above yields several frames and the villager
                // can be unloaded inside them. A destroyed MonoBehaviour still hands back its
                // stale ZDO rather than throwing, so the cargo test passes and the two lines
                // below - its position and its name - are what would throw, killing the
                // coroutine and taking the whole run with it.
                if (hand == null || ZNetScene.instance?.FindInstance(who) == null) break;

                VillagerState sampled = new VillagerState(view.GetZDO());
                if (!caughtCarrying && sampled.Cargo.Length > 0 &&
                    Carrying.Cargo(hand.GetComponentInChildren<Container>(true)?.GetInventory(),
                        sampled.Cargo).Count > 0)
                {
                    // The bag is asked as well as the manifest, because the manifest outlives
                    // the bag by design - ResetJob keeps it, and the half-second idle pause
                    // after a Skipped is exactly one sampler period wide. Triggering on the
                    // manifest alone photographed an empty-handed villager captioned as
                    // loaded, and the latch meant no honest moment ever replaced it.
                    caughtCarrying = true;

                    // "Bound" and "arrived" are different claims and the old caption made the
                    // stronger one: this shot fires the moment cargo appears, at the pickup
                    // pile, so "at the chest that asked for it" was wrong on nearly every
                    // normal run. What the frame proves is where the trip is going, so that
                    // is all the caption says. An unresolved destination is only called
                    // cleared when the trip itself says so - merely-not-instantiated looks
                    // identical from FindInstance, and the job side already separates the two.
                    GameObject toward = ZNetScene.instance?.FindInstance(sampled.Destination);
                    Vector3? frame = toward != null ? toward.transform.position : (Vector3?)null;
                    string doing = frame.HasValue
                        ? $"'{hand.DisplayName()}' is '{hand.Activity}', bound for the chest that asked for it"
                        : sampled.Destination.IsNone()
                            ? $"'{hand.DisplayName()}' is '{hand.Activity}' with a load and nowhere bound"
                            : $"'{hand.DisplayName()}' is '{hand.Activity}', bound somewhere not loaded here";

                    yield return BenchmarkUiScenario.PhotographAtWork("haul-delivering.png",
                        hand.transform.position, doing,
                        frame ?? stoneChest.transform.position);
                }
            }

            // Whatever the sampler did not catch is written anyway.
            //
            // The captures above are keyed on states that make a good picture, and a state that
            // makes a good picture is exactly the kind that can pass between two samples - more
            // so now that villagers move at a player's pace. The shell requires all three files,
            // so a missed one ends the run with "missing screenshot" long after the hauling it
            // was photographing, which is a failure message pointing nowhere near its cause.
            // Catching the moment is worth trying for and must not be worth failing for.
            // Asked fresh for each one. These run back to back and each costs several frames,
            // so a villager still on its feet has moved on by the third - a position and an
            // activity read once would aim the last photograph where the subject used to be and
            // caption it with what it used to be doing.
            bool Loaded() => hand != null && ZNetScene.instance?.FindInstance(who) != null;
            Vector3 Subject() => Loaded() ? hand.transform.position : woodChest.transform.position;
            string Caption(string why) => Loaded()
                ? $"{why}'{hand.DisplayName()}', doing '{hand.Activity}'; " +
                  $"wood chest holds {inWood}, stone chest holds {inStone}"
                : $"{why}the villager is no longer loaded; " +
                  $"wood chest holds {inWood}, stone chest holds {inStone}";

            // All three framed against the stone chest, because with nobody to photograph the
            // subject falls back to the wood chest - so naming the wood chest as the second
            // point makes both points the same, and the framing collapses to a single chest
            // seen from one side at the minimum distance. These pictures exist for exactly the
            // case that produces that frame.
            Vector3 opposite = stoneChest.transform.position;

            if (!caughtFetching)
            {
                yield return BenchmarkUiScenario.PhotographAtWork("haul-fetching.png", Subject(),
                    Caption("no setting-out moment was sampled; "), opposite);
            }

            if (!caughtCarrying)
            {
                yield return BenchmarkUiScenario.PhotographAtWork("haul-delivering.png", Subject(),
                    Caption("no carrying moment was sampled; "), opposite);
            }

            yield return BenchmarkUiScenario.PhotographAtWork("haul-settled.png", Subject(),
                Caption("after the hauling: "), opposite);

            // Read once more, fresh. The last loop sample is stale by however long the
            // photographs took - several frames each - and a delivery landing inside that
            // window scored as a failure while the cross-contamination check below, which
            // does read fresh, printed the contradicting count in the same report.
            inWood = StructureInventory.Count(woodStore.Id, "Wood");
            inStone = StructureInventory.Count(stoneStore.Id, "Stone");

            string did = string.Join(" > ", story.ToArray());

            report.Check(inWood >= 5, "a villager hauls loose wood to the chest that asked for wood",
                $"inWoodStore={inWood} did='{did}'");

            report.Check(inStone >= 5, "and loose stone to the chest that asked for stone",
                $"inStoneStore={inStone}");

            // Specificity: neither ends up in the other's chest, which is what a settlement that
            // sorts means as opposed to one that merely tidies up.
            report.Check(StructureInventory.Count(woodStore.Id, "Stone") <= 0 &&
                         StructureInventory.Count(stoneStore.Id, "Wood") <= 0,
                "control: neither ends up in the other chest",
                $"stoneInWoodStore={StructureInventory.Count(woodStore.Id, "Stone")} " +
                $"woodInStoneStore={StructureInventory.Count(stoneStore.Id, "Wood")}");

            // The wandering check. Everything it was asked to do was within a few metres of the
            // hearth, so anything beyond the settlement's own reach is a villager going somewhere
            // nobody sent it.
            report.Check(strayed <= colony.EffectiveRadius,
                "a working villager stays within the settlement it works for",
                $"strayed {strayed:0}m from a hearth with {colony.EffectiveRadius:0}m of reach");

            report.Check(ZNetScene.instance?.FindInstance(who) != null,
                "control: it survived the job rather than being destroyed by it",
                $"loaded={ZNetScene.instance?.FindInstance(who) != null}");

            // The vanilla despawn flags, which walk a creature away from the nearest player and
            // then destroy it. Neither check asks whether the creature is tamed, so being
            // somebody's villager is no protection - and the symptoms are exactly "it wandered
            // off for no reason" and "it faded away in front of me".
            MonsterAI brain = hand == null ? null : hand.GetComponent<MonsterAI>();
            report.Check(brain != null && !brain.DespawnInDay() && !brain.IsEventCreature(),
                "a villager is never one of the creatures the game despawns by itself",
                brain == null
                    ? "no MonsterAI"
                    : $"despawnInDay={brain.DespawnInDay()} eventCreature={brain.IsEventCreature()}");

            VillagerLifecycle.Remove(colony, who);
            colony.State.SetJobs(new List<JobDefinition>());
            colony.RemoveStructure(woodStore.Id);
            colony.RemoveStructure(stoneStore.Id);
            Release(woodChest);
            Release(stoneChest);
            SweepLooseItems(colony);
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>
        ///     Organising: wood in the wrong chest moves to the right one, and then everything
        ///     stops.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>The settlement coming to rest is the claim worth proving.</b> The shuffle
        ///         loop - A says move this to B, B says move it back - is invisible in a short
        ///         test, because a settlement in permanent motion looks exactly like a settlement
        ///         being busy. So this measures stillness, and pairs it with a control that puts
        ///         real work back and watches the same villager move again: stillness from a
        ///         broken villager and stillness from a sorted settlement are the same picture.
        ///     </para>
        ///     <para>
        ///         The ground is swept first. An overflow chest claims everything by definition,
        ///         so a stray item left by an earlier check is a home away from home and the
        ///         settlement would never be still - which would fail this for a reason that has
        ///         nothing to do with tidying.
        ///     </para>
        /// </remarks>
        /// <summary>
        ///     Packing a chest must never merge two things that cannot say what they are.
        /// </summary>
        /// <remarks>
        ///     An item with no drop prefab answers the empty string when asked its name, so
        ///     every one of them grouped under the same key and was packed into a single stack
        ///     with the surplus deleted. Items reach that state in ordinary play - the field is
        ///     set when an item passes through an inventory, so anything that arrived another
        ///     way arrives without it - and the loss is silent, which is the worst kind.
        /// </remarks>
        private static IEnumerator CheckTidyingKeepsUnidentifiedItems(TestReport report, Colony colony,
            Vector3 origin)
        {
            GameObject chest = Spawn("piece_chest_wood", origin + new Vector3(-7f, 0f, 5f));
            yield return new WaitForSecondsRealtime(.3f);

            Container container = chest == null ? null : chest.GetComponentInChildren<Container>(true);
            if (container == null)
            {
                report.Check(false, "unidentified-item check could place a chest");
                yield break;
            }

            Inventory inventory = container.GetInventory();
            inventory.RemoveAll();

            // Two genuinely different items, both stripped of the field that names them.
            foreach (string prefabName in new[] { "Wood", "Stone" })
            {
                GameObject prefab = ObjectDB.instance?.GetItemPrefab(prefabName);
                if (prefab == null || !prefab.TryGetComponent(out ItemDrop drop)) continue;

                ItemDrop.ItemData nameless = drop.m_itemData.Clone();
                nameless.m_dropPrefab = null;
                nameless.m_stack = 3;
                inventory.m_inventory.Add(nameless);
            }

            int before = inventory.m_inventory.Count;
            report.Check(before == 2,
                "control: two unidentified items are in the chest to begin with",
                $"items={before}");

            Tidying.Organise(container);
            int after = inventory.m_inventory.Count;

            report.Check(after == before,
                "packing a chest never merges two things that cannot say what they are",
                $"before={before} after={after}");

            inventory.RemoveAll();
            Release(chest);
            yield return new WaitForSecondsRealtime(.2f);
        }

        private static IEnumerator CheckTidying(TestReport report, Colony colony, Vector3 origin)
        {
            SettlementIndex.ResetForTest();
            int swept = SweepLooseItems(colony);

            // Placed where a villager has already been proven able to walk - the hauling check
            // works this side of the colony. The first attempt put them a few metres the other
            // way, behind the standing stones that ring the hearth, and the villager reported
            // "cannot get there" from thirteen metres out while closing a tenth of a metre per
            // try. A fixture the subject cannot reach fails the feature, not the fixture, and
            // that is the third time placement has cost a run.
            GameObject overflowChest = Spawn("piece_chest_wood", origin + new Vector3(5f, 0f, 7f));
            GameObject woodChest = Spawn("piece_chest_wood", origin + new Vector3(8f, 0f, 7f));
            yield return new WaitForSecondsRealtime(.3f);

            StructureRecord overflow = Register(colony, overflowChest, "Overflow");
            StructureRecord shed = Register(colony, woodChest, "Wood shed");
            if (overflow == null || shed == null)
            {
                report.Check(false, "tidy check could register two chests");
                yield break;
            }

            ColonyOperations.EditSettings(colony, overflow.Id, s => s.Accepts = new List<string>());
            ColonyOperations.EditSettings(colony, shed.Id, s => s.Accepts = new List<string> { "Wood" });

            Container misplaced = overflowChest.GetComponentInChildren<Container>(true);
            report.Check(misplaced != null && PutIn(misplaced, "Wood", 10) == 10,
                "control: there is wood in the wrong chest to begin with",
                $"inOverflow={(misplaced == null ? -1 : CountIn(misplaced, "Wood"))} sweptFromGround={swept}");
            if (misplaced == null) yield break;

            colony.State.SetJobs(new List<JobDefinition>
            {
                new JobDefinition { Id = "tidy", Name = "Tidy", Kind = JobKind.Haul, Repeat = 20 }
            });

            Villager keeper = VillagerLifecycle.Spawn(colony);
            yield return null;
            if (keeper == null || !keeper.TryGetComponent(out ZNetView view) || !view.IsValid())
            {
                report.Check(false, "tidy check could spawn a villager");
                yield break;
            }

            ZDOID who = view.GetZDO().m_uid;
            SendRested(view);
            new VillagerState(view.GetZDO()).SetQueue(new List<string> { "tidy" });

            Container into = woodChest.GetComponentInChildren<Container>(true);
            int moved = 0;

            // The sequence of things it did, not just what it was doing when time ran out.
            // "Still fetching after forty seconds" is the same sentence whether it walked a
            // long way once or bounced between two decisions two hundred times, and those are
            // different bugs.
            List<string> story = new List<string>();
            for (int attempt = 0; attempt < 40 && moved < 10; attempt++)
            {
                yield return new WaitForSecondsRealtime(.5f);
                moved = into == null ? 0 : CountIn(into, "Wood");
                if (story.Count == 0 || story[story.Count - 1] != keeper.Activity) story.Add(keeper.Activity);
            }

            report.Check(moved >= 10,
                "wood in an overflow chest migrates to the chest that names it",
                $"inWoodShed={moved} leftInOverflow={CountIn(misplaced, "Wood")} " +
                $"loose={LooseItemsNear(colony)} elsewhere='{WhereTheWoodWent(colony)}' " +
                $"steps={story.Count} did='{string.Join(" > ", story.ToArray())}'");

            report.Check(CountIn(misplaced, "Wood") == 0,
                "control: the overflow chest it came from is emptied of it, not merely copied from",
                $"leftInOverflow={CountIn(misplaced, "Wood")}");

            // Wait for the villager to actually run out of work before measuring stillness.
            // Measuring after a fixed delay asked whether the settlement was still, while it was
            // still legitimately delivering what it had already picked up - so the check failed
            // on a settlement that was working correctly, which is the worst kind of flake.
            float settling = 0f;
            while (settling < 20f && keeper.Activity != "nothing to haul")
            {
                yield return new WaitForSecondsRealtime(.5f);
                settling += .5f;
            }

            report.Check(keeper.Activity == "nothing to haul",
                "control: the villager ran out of work before stillness was measured",
                $"doing '{keeper.Activity}' after {settling:0}s");

            // Everything is where it belongs, so nothing should come BACK out of the chest that
            // names it and nothing should reappear in the one that does not.
            //
            // Asserted as "no wood moves anywhere" before, which is a different and wrong claim:
            // a villager that finds a stray log on the floor and files it correctly is doing its
            // job, and the shed gaining two wood during the window failed a check about
            // ping-ponging for a settlement that was behaving perfectly. What ping-ponging looks
            // like is wood leaving the right chest, or arriving back in the wrong one.
            int shedBefore = CountIn(into, "Wood");
            yield return new WaitForSecondsRealtime(3f);
            int shedAfter = CountIn(into, "Wood");
            int strayAfter = CountIn(misplaced, "Wood");

            report.Check(shedAfter >= shedBefore && strayAfter == 0,
                "a sorted settlement never moves wood back out of the chest that asked for it",
                $"shed {shedBefore} -> {shedAfter}, back in the overflow chest={strayAfter}, " +
                $"doing='{keeper.Activity}'");

            // The control that makes the line above mean anything: a villager that has stopped
            // because everything is sorted must still start again when something is not.
            PutIn(misplaced, "Wood", 5);
            int movedAgain = 0;
            for (int attempt = 0; attempt < 20 && movedAgain < 5; attempt++)
            {
                yield return new WaitForSecondsRealtime(.5f);
                movedAgain = CountIn(into, "Wood") - moved;
            }

            report.Check(movedAgain >= 5,
                "control: the same villager moves again the moment something is misplaced",
                $"movedAgain={movedAgain} doing='{keeper.Activity}'");

            // Two chests that both name wood are equal, so there is no reason to move between
            // them - the case that would otherwise shuffle forever.
            ColonyOperations.EditSettings(colony, overflow.Id, s => s.Accepts = new List<string> { "Wood" });
            SettlementIndex.ResetForTest();
            int inShed = CountIn(into, "Wood");
            PutIn(misplaced, "Wood", 5);
            yield return new WaitForSecondsRealtime(3f);

            report.Check(CountIn(into, "Wood") == inShed && CountIn(misplaced, "Wood") == 5,
                "wood is not moved between two chests that both name wood",
                $"shed={CountIn(into, "Wood")} was {inShed}, other={CountIn(misplaced, "Wood")}");

            // A chest the player keeps is never a source, however badly its contents score.
            ColonyOperations.EditSettings(colony, overflow.Id, s =>
            {
                s.Accepts = new List<string> { "Coal" };
                s.MayTakeFrom = false;
            });
            SettlementIndex.ResetForTest();
            yield return new WaitForSecondsRealtime(3f);

            report.Check(CountIn(misplaced, "Wood") == 5,
                "a chest marked not to be taken from is never a source, even holding the wrong thing",
                $"stillThere={CountIn(misplaced, "Wood")}");

            // Packing: split stacks in a chest a villager works are merged. Applied directly
            // rather than waited for, because what is under test is the operation on a real
            // Valheim inventory - the decision itself is proven at a table, and staging a
            // villager into exactly the right moment would test the scheduler instead.
            Inventory shedInventory = into.GetInventory();
            shedInventory.RemoveAll();
            // Placed in two slots by hand. AddItem stacks automatically, so the obvious way to
            // stage a split stack quietly produces a single tidy one and the check passes
            // against a fixture that was never untidy.
            Split(shedInventory, "Wood", 30, 0, 0);
            Split(shedInventory, "Wood", 20, 1, 0);
            int slotsBefore = shedInventory.GetAllItems().Count;

            bool organised = Tidying.Organise(into);
            int slotsAfter = shedInventory.GetAllItems().Count;

            report.Check(organised && slotsAfter < slotsBefore && CountIn(into, "Wood") == 50,
                "a chest a villager works has its split stacks packed together",
                $"slots {slotsBefore} -> {slotsAfter}, wood={CountIn(into, "Wood")}");

            report.Check(!Tidying.Organise(into),
                "control: an already tidy chest is not rewritten, so watching one costs nothing",
                $"slots={shedInventory.GetAllItems().Count}");

            VillagerLifecycle.Remove(colony, who);
            colony.State.SetJobs(new List<JobDefinition>());
            colony.RemoveStructure(overflow.Id);
            colony.RemoveStructure(shed.Id);
            Release(overflowChest);
            Release(woodChest);
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>
        ///     Every registered container and what wood is in it.
        /// </summary>
        /// <remarks>
        ///     "It did not arrive" and "it arrived somewhere else" look identical from the
        ///     destination, and only one of them is a bug in hauling - the other is a fixture
        ///     that left a chest registered.
        /// </remarks>
        private static string WhereTheWoodWent(Colony colony)
        {
            System.Text.StringBuilder found = new System.Text.StringBuilder();
            foreach (StructureRecord record in colony.State.GetStructures())
            {
                if ((record.Capabilities & StructureCapability.Storage) == 0) continue;

                int held = StructureInventory.Count(record.Id, "Wood");
                if (held <= 0) continue;

                found.Append(record.Name).Append('=').Append(held).Append(' ');
            }

            return found.Length == 0 ? "nowhere" : found.ToString();
        }

        /// <summary>How many loose items lie in the colony's reach right now.</summary>
        private static int LooseItemsNear(Colony colony)
        {
            int count = 0;
            foreach (ItemDrop drop in ItemDrop.s_instances)
            {
                if (drop == null) continue;
                if (Utils.DistanceXZ(drop.transform.position, colony.transform.position) <= colony.EffectiveRadius)
                    count++;
            }

            return count;
        }

        /// <summary>Destroys loose items in the colony's reach, so a fixture starts from nothing.</summary>
        private static int SweepLooseItems(Colony colony)
        {
            List<ItemDrop> doomed = new List<ItemDrop>();
            foreach (ItemDrop drop in ItemDrop.s_instances)
            {
                if (drop == null) continue;
                if (Utils.DistanceXZ(drop.transform.position, colony.transform.position) > colony.EffectiveRadius)
                    continue;
                doomed.Add(drop);
            }

            foreach (ItemDrop drop in doomed)
            {
                if (drop.TryGetComponent(out ZNetView view) && view.IsValid()) view.Destroy();
            }

            return doomed.Count;
        }

        /// <summary>Places a stack in one grid slot, bypassing automatic stacking.</summary>
        private static void Split(Inventory inventory, string prefabName, int count, int x, int y)
        {
            GameObject prefab = ObjectDB.instance?.GetItemPrefab(prefabName);
            if (inventory == null || prefab == null || !prefab.TryGetComponent(out ItemDrop drop)) return;

            ItemDrop.ItemData stack = drop.m_itemData.Clone();
            stack.m_dropPrefab = prefab;
            stack.m_stack = count;
            stack.m_gridPos = new Vector2i(x, y);
            inventory.m_inventory.Add(stack);
            inventory.Changed();
        }

        /// <summary>Puts a number of an item into a container, returning how many went in.</summary>
        private static int PutIn(Container container, string prefabName, int count)
        {
            Inventory inventory = container?.GetInventory();
            GameObject prefab = ObjectDB.instance?.GetItemPrefab(prefabName);
            if (inventory == null || prefab == null || !prefab.TryGetComponent(out ItemDrop drop)) return 0;

            int before = CountIn(container, prefabName);

            // Built by hand rather than through Inventory.AddItem(prefab, count), so the item
            // carries the prefab it came from. Without it the settlement cannot name what it is
            // looking at, and a chest full of anonymous wood reads as a chest full of nothing.
            ItemDrop.ItemData stack = drop.m_itemData.Clone();
            stack.m_dropPrefab = prefab;
            stack.m_stack = count;
            inventory.AddItem(stack);
            return CountIn(container, prefabName) - before;
        }

        private static int CountIn(Container container, string prefabName)
        {
            Inventory inventory = container?.GetInventory();
            if (inventory == null) return 0;

            int total = 0;
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
            {
                if (item?.m_dropPrefab != null && Utils.GetPrefabName(item.m_dropPrefab) == prefabName)
                    total += item.m_stack;
            }

            return total;
        }

        /// <summary>
        ///     The queue: order, repeats, yielding, missing jobs, and claims.
        /// </summary>
        /// <remarks>
        ///     Outcomes are applied directly rather than by running real work, because what is
        ///     under test is the scheduling - that Skipped costs nothing and yields, that Failed
        ///     costs a repetition so impossible work runs out, and that a deleted job does not
        ///     strand the villager. Staging those through real hauling would prove them slowly
        ///     and prove them together.
        /// </remarks>
        private static IEnumerator CheckJobQueue(TestReport report, Colony colony)
        {
            Villager villager = VillagerLifecycle.Spawn(colony);
            yield return null;
            if (villager == null || !villager.TryGetComponent(out ZNetView view) || !view.IsValid())
            {
                report.Check(false, "queue check could spawn a villager");
                yield break;
            }

            ZDOID who = view.GetZDO().m_uid;
            VillagerState state = new VillagerState(view.GetZDO());

            List<JobDefinition> jobs = new List<JobDefinition>
            {
                new JobDefinition { Id = "a", Name = "First", Kind = JobKind.Haul, Repeat = 2 },
                new JobDefinition { Id = "b", Name = "Second", Kind = JobKind.Haul, Repeat = 1 }
            };
            colony.State.SetJobs(jobs);

            report.Check(colony.State.GetJobs().Count == 2,
                "a colony's jobs survive being written and read back",
                $"jobs={colony.State.GetJobs().Count}");

            state.SetQueue(new List<string> { "a", "b" });
            report.Check(QueueRunner.Current(state, jobs)?.Id == "a",
                "control: a villager starts at the front of its queue");

            // Failed consumes a repetition, so work that cannot succeed runs out.
            QueueRunner.Apply(state, jobs, JobResult.Failed);
            report.Check(QueueRunner.Current(state, jobs)?.Id == "a" && state.QueueAttempt == 1,
                "a failure consumes one repetition and stays on the entry",
                $"attempt={state.QueueAttempt}");

            QueueRunner.Apply(state, jobs, JobResult.Failed);
            report.Check(QueueRunner.Current(state, jobs)?.Id == "b",
                "exhausting the repetitions advances to the next entry",
                $"current={QueueRunner.Current(state, jobs)?.Id}");

            // Skipped costs nothing: an idle job must not burn its own count and drop out.
            state.SetQueue(new List<string> { "a", "b" });
            QueueRunner.Apply(state, jobs, JobResult.Skipped);
            report.Check(QueueRunner.Current(state, jobs)?.Id == "b" && state.QueueAttempt == 0,
                "a skip yields to the next entry and consumes nothing",
                $"current={QueueRunner.Current(state, jobs)?.Id} attempt={state.QueueAttempt}");

            // The ring: after the last entry comes the first.
            QueueRunner.Apply(state, jobs, JobResult.Skipped);
            report.Check(QueueRunner.Current(state, jobs)?.Id == "a",
                "the queue is a ring");

            // Running changes nothing at all.
            int before = state.QueueAttempt;
            QueueRunner.Apply(state, jobs, JobResult.Running);
            report.Check(state.QueueAttempt == before && QueueRunner.Current(state, jobs)?.Id == "a",
                "control: progress consumes nothing and keeps the entry");

            // A deleted job is bypassed rather than stalling the villager.
            state.SetQueue(new List<string> { "gone", "b" });
            report.Check(QueueRunner.Current(state, jobs)?.Id == "b",
                "a queue entry whose job was deleted is bypassed, not stalled",
                $"current={QueueRunner.Current(state, jobs)?.Id}");

            state.SetQueue(new List<string> { "gone", "alsogone" });
            report.Check(QueueRunner.Current(state, jobs) == null,
                "control: a queue of nothing but missing jobs terminates rather than spinning");

            // Claims.
            state.SetQueue(new List<string> { "a" });
            Villager other = VillagerLifecycle.Spawn(colony);
            yield return null;
            if (other != null && other.TryGetComponent(out ZNetView otherView) && otherView.IsValid())
            {
                ZDOID contested = otherView.GetZDO().m_uid;
                TargetClaims.Invalidate();
                report.Check(!TargetClaims.IsClaimedByOther(contested, other),
                    "control: nothing is claimed before anyone takes it");

                state.SetTarget(contested);
                TargetClaims.Invalidate();
                report.Check(TargetClaims.IsClaimedByOther(contested, other),
                    "a villager's target is a claim other villagers can see");
                report.Check(!TargetClaims.IsClaimedByOther(contested, villager),
                    "control: a villager is not blocked by its own claim");

                state.ResetJob();
                TargetClaims.Invalidate();
                report.Check(!TargetClaims.IsClaimedByOther(contested, other),
                    "ending the job releases the claim with nothing to remember");

                VillagerLifecycle.Remove(colony, contested);
            }

            VillagerLifecycle.Remove(colony, who);
            colony.State.SetJobs(new List<JobDefinition>());
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>
        ///     A villager's bed is its home, and its clothes come off its own bag.
        /// </summary>
        private static IEnumerator CheckVillagerLiving(TestReport report, Colony colony, Vector3 origin = default(Vector3))
        {
            Villager villager = VillagerLifecycle.Spawn(colony);
            yield return new WaitForSecondsRealtime(.4f);
            if (villager == null || !villager.TryGetComponent(out ZNetView view) || !view.IsValid())
            {
                report.Check(false, "living check could spawn a villager");
                yield break;
            }

            ZDOID who = view.GetZDO().m_uid;
            ZDO zdo = view.GetZDO();

            // Home, before any bed.
            Vector3 spawnHome = villager.ResolveHomeForTest();
            report.Check(spawnHome != Vector3.zero,
                "control: a villager without a bed calls its spawn point home",
                $"home={spawnHome}");

            GameObject bedObject = SpawnFirst(origin + new Vector3(4f, 0f, -8f), "bed", "piece_bed", "bed_wood");
            yield return null;
            StructureRecord bed = Register(colony, bedObject, "A bed of one's own");
            if (bed != null)
            {
                VillagerRoster.Assign(colony, bed, new List<string> { who.ToString() });
                yield return null;

                ZDO bedZdo = ZDOMan.instance.GetZDO(bed.Id);
                Vector3 bedHome = villager.ResolveHomeForTest();
                report.Check(bedZdo != null &&
                             Utils.DistanceXZ(bedHome, bedZdo.GetPosition()) < 1f,
                    "a villager given a bed calls the bed home",
                    $"home={bedHome} bed={(bedZdo == null ? "none" : bedZdo.GetPosition().ToString())}");
            }
            else
            {
                report.Check(false, "living check could register a bed");
            }

            // Clothes come off the bag, and only off the bag.
            Container bag = VillagerInventory.Attach(villager.gameObject, view);
            Add(bag.GetInventory(), "ArmorLeatherChest");
            yield return null;

            ItemDrop.ItemData worn = null;
            foreach (ItemDrop.ItemData item in VillagerInventory.Stored(zdo).GetAllItems())
                if (item?.m_dropPrefab != null &&
                    Utils.GetPrefabName(item.m_dropPrefab) == "ArmorLeatherChest") worn = item;

            report.Check(worn != null, "control: the villager owns the chestpiece before wearing it");

            if (worn != null && villager.TryGetComponent(out VisEquipment vis))
            {
                VillagerWardrobe.Set(vis, WearSlot.Chest, worn);
                yield return null;
                report.Check(VillagerWardrobe.Worn(zdo, WearSlot.Chest) ==
                             "ArmorLeatherChest".GetStableHashCode(),
                    "a villager wears what it is given, and the ZDO says so",
                    $"worn={VillagerWardrobe.Worn(zdo, WearSlot.Chest)}");

                VillagerWardrobe.Set(vis, WearSlot.Chest, null);
                yield return null;
                report.Check(VillagerWardrobe.Worn(zdo, WearSlot.Chest) == 0,
                    "control: a slot can be bared again",
                    $"worn={VillagerWardrobe.Worn(zdo, WearSlot.Chest)}");
            }

            // The item must never be in the creature's own inventory: the game strips that on
            // load, which would silently undress a villager - and later, disarm one.
            Humanoid humanoid = villager.GetComponent<Humanoid>();
            report.Check(humanoid == null || humanoid.GetInventory() == null ||
                         humanoid.GetInventory().NrOfItems() == 0,
                "nothing is put in the creature's own inventory, which the game strips on load",
                $"items={(humanoid?.GetInventory()?.NrOfItems() ?? -1)}");

            VillagerLifecycle.Remove(colony, who);
            if (bedObject != null) Release(bedObject);
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>
        ///     Villagers are dressed, and they do not all look the same.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The roadmap's "done when" is five villagers who each look different, and
        ///         until now nothing asserted any of it - the only appearance check was that a
        ///         boolean flag survived a reload. A photograph then showed a villager wearing
        ///         nothing but a loincloth while the code that dresses them had already run, and
        ///         the missing assertion is exactly why that survived four milestones.
        ///     </para>
        ///     <para>
        ///         Asserted on the ZDO values rather than on the live component: that is what
        ///         persists and replicates, and the component's fields are documented to trail
        ///         the ZDO by several frames while models attach.
        ///     </para>
        /// </remarks>
        private static IEnumerator CheckAppearance(TestReport report, Colony colony)
        {
            const int wanted = 5;
            List<ZDOID> born = new List<ZDOID>();
            for (int i = 0; i < wanted; i++)
            {
                Villager villager = VillagerLifecycle.Spawn(colony);
                if (villager != null && villager.TryGetComponent(out ZNetView view) && view.IsValid())
                    born.Add(view.GetZDO().m_uid);
            }

            // Appearance is rolled on the first owned tick, not at spawn.
            yield return new WaitForSecondsRealtime(1f);

            report.Check(born.Count == wanted, "control: five villagers were spawned to compare",
                $"spawned={born.Count}");

            List<string> looks = new List<string>();
            int dressed = 0;
            int uncraftable = 0;
            foreach (ZDOID id in born)
            {
                ZDO zdo = ZDOMan.instance.GetZDO(id);
                if (zdo == null) continue;

                int chest = zdo.GetInt(ZDOVars.s_chestItem, 0);
                int legs = zdo.GetInt(ZDOVars.s_legItem, 0);
                if (chest != 0 && legs != 0) dressed++;
                if (!VillagerAppearance.IsCraftableHash(chest) || !VillagerAppearance.IsCraftableHash(legs))
                    uncraftable++;

                looks.Add($"{zdo.GetInt(ZDOVars.s_modelIndex, 0)}/{chest}/{legs}/" +
                          $"{zdo.GetInt(ZDOVars.s_hairItem, 0)}/{zdo.GetInt(ZDOVars.s_beardItem, 0)}");
            }

            report.Check(dressed == born.Count,
                "every villager is wearing a chest and legs",
                $"dressed={dressed} of {born.Count}; looks={string.Join(" ", looks.ToArray())}");

            report.Check(uncraftable == 0,
                "control: nothing worn is an item a player could not craft",
                $"uncraftable={uncraftable}");

            // Across the set, not per villager: five identical rolls would pass any check that
            // only asked whether each one had something on.
            HashSet<string> distinct = new HashSet<string>(looks);
            report.Check(distinct.Count > 1,
                "villagers do not all look the same",
                $"distinct={distinct.Count} of {looks.Count}");

            // Control: the same villager read twice is the same villager, so "different" above
            // means different people rather than a value that changes on every read.
            if (born.Count > 0)
            {
                ZDO first = ZDOMan.instance.GetZDO(born[0]);
                string again = first == null ? string.Empty :
                    $"{first.GetInt(ZDOVars.s_modelIndex, 0)}/{first.GetInt(ZDOVars.s_chestItem, 0)}/" +
                    $"{first.GetInt(ZDOVars.s_legItem, 0)}/{first.GetInt(ZDOVars.s_hairItem, 0)}/" +
                    $"{first.GetInt(ZDOVars.s_beardItem, 0)}";
                report.Check(again == looks[0],
                    "control: re-reading one villager gives the same appearance",
                    $"first='{looks[0]}' again='{again}'");
            }

            foreach (ZDOID id in born) VillagerLifecycle.Remove(colony, id);
            yield return new WaitForSecondsRealtime(.3f);
        }

        /// <summary>
        ///     Settings stick to the record, and the index answers from them.
        /// </summary>
        /// <remarks>
        ///     The index is the piece jobs will lean on, so the checks are about its answers
        ///     being <em>usable</em> rather than merely non-empty: a chest that claims the wrong
        ///     item, a chest that claims the right one but is full, and a station nobody
        ///     configured all have to be absent from the answer for different reasons.
        /// </remarks>
        private static IEnumerator CheckSettingsAndIndex(TestReport report, Colony colony, Vector3 origin)
        {
            SettlementIndex.ResetForTest();
            Vector3 at = origin + new Vector3(-10f, 0f, 6f);

            GameObject woodChest = Spawn("piece_chest_wood", at);
            GameObject coalChest = Spawn("piece_chest_wood", at + new Vector3(3f, 0f, 0f));
            yield return new WaitForSecondsRealtime(.3f);
            StructureRecord wood = Register(colony, woodChest, "Wood store");
            StructureRecord coal = Register(colony, coalChest, "Coal store");
            if (wood == null || coal == null)
            {
                report.Check(false, "settings check could register two chests");
                yield break;
            }

            ColonyOperations.EditSettings(colony, wood.Id, s => s.Accepts = new List<string> { "Wood" });
            ColonyOperations.EditSettings(colony, coal.Id, s => s.Accepts = new List<string> { "Coal" });

            StructureRecord stored = colony.State.GetStructures().Find(r => r.Id == wood.Id);
            report.Check(stored != null && stored.Settings.Accepts.Count == 1 &&
                         stored.Settings.Accepts[0] == "Wood",
                "a structure's settings are kept on its record",
                $"accepts={(stored == null ? "none" : string.Join(",", stored.Settings.Accepts.ToArray()))}");

            List<StructureRecord> forWood = SettlementIndex.WhereDoesItGo(colony, "Wood", at);
            report.Check(forWood.Exists(r => r.Id == wood.Id) && !forWood.Exists(r => r.Id == coal.Id),
                "the settlement says where an item goes, and does not offer a chest that refuses it",
                $"answers={forWood.Count}");

            // Control, scoped to this check's own chests. Asserting the whole colony has
            // nowhere to put Flint depends on what every earlier check happened to leave
            // registered - and an earlier one leaves a chest claiming nothing, which by design
            // claims everything. A control an unrelated check can break is not a control.
            List<StructureRecord> forFlint = SettlementIndex.WhereDoesItGo(colony, "Flint", at);
            report.Check(!forFlint.Exists(r => r.Id == wood.Id) && !forFlint.Exists(r => r.Id == coal.Id),
                "control: a chest that named its item does not accept a different one",
                $"answers={forFlint.Count}");

            ColonyOperations.EditSettings(colony, coal.Id, s => s.Accepts = new List<string>());
            forFlint = SettlementIndex.WhereDoesItGo(colony, "Flint", at);
            report.Check(forFlint.Exists(r => r.Id == coal.Id),
                "a chest that claims nothing claims anything, which is what an overflow chest is",
                $"answers={forFlint.Count}");

            // Capacity is part of the question. Filling the overflow chest must remove it from
            // the answer, or a villager walks to a chest with no room in it.
            if (coalChest.TryGetComponent(out Container coalContainer))
            {
                Fill(coalContainer.GetInventory(), "Wood");
                yield return null;
                List<StructureRecord> afterFull = SettlementIndex.WhereDoesItGo(colony, "Flint", at);
                report.Check(!afterFull.Exists(r => r.Id == coal.Id),
                    "a full chest is not an answer, because arriving to find it full wastes the walk",
                    $"answers={afterFull.Count}");
            }

            // The index rebuilds on a revision change, not per query - the whole reason it
            // exists is that a hundred villagers must not each walk the settlement.
            SettlementIndex.WhereDoesItGo(colony, "Wood", at);
            int before = SettlementIndex.Rebuilds;
            for (int i = 0; i < 5; i++) SettlementIndex.WhereDoesItGo(colony, "Wood", at);
            report.Check(SettlementIndex.Rebuilds == before,
                "control: repeated questions do not rebuild the index",
                $"rebuilds={SettlementIndex.Rebuilds - before}");

            ColonyOperations.EditSettings(colony, wood.Id, s => s.MayTakeFrom = false);
            // The rebuild is lazy, so it happens on the next question rather than on the edit.
            // Reading the counter without asking one measured nothing.
            SettlementIndex.WhereDoesItGo(colony, "Wood", at);
            report.Check(SettlementIndex.Rebuilds == before + 1,
                "changing a setting rebuilds the index, once",
                $"rebuilds={SettlementIndex.Rebuilds - before}");

            yield return CheckPlacement(report, colony, at, wood, coal, coalChest);

            Release(woodChest);
            Release(coalChest);
            yield return null;
            yield return CheckBeds(report, colony, origin);
        }

        /// <summary>
        ///     Specificity, caps and the dump flag: that a setting reaches the score, and the
        ///     score reaches the answer.
        /// </summary>
        /// <remarks>
        ///     The scoring rule itself is proven at a table in the deterministic suite, where
        ///     every branch is reachable in a second. What cannot be proven there is the wiring -
        ///     that a cap typed into a screen ends up on the record, is read against what the
        ///     container actually holds, and removes that container from the settlement's
        ///     answer. This checks the wiring and nothing else.
        ///
        ///     <para>
        ///         <c>coal</c> arrives here claiming nothing, which is what an overflow chest
        ///         is, and <c>wood</c> arrives naming Wood. That is the exact pair specificity
        ///         is about.
        ///     </para>
        /// </remarks>
        private static IEnumerator CheckPlacement(TestReport report, Colony colony, Vector3 at,
            StructureRecord wood, StructureRecord coal, GameObject coalChest)
        {
            // The overflow chest was filled with wood by the check above; emptying it puts both
            // chests back on equal terms, so what follows measures the score and not the space.
            if (coalChest.TryGetComponent(out Container overflow)) overflow.GetInventory().RemoveAll();
            yield return null;

            List<StructureRecord> homes = SettlementIndex.WhereDoesItGo(colony, "Wood", at);
            report.Check(homes.Count > 0 && homes[0].Id == wood.Id,
                "the chest that names an item beats one that merely takes anything",
                $"first={(homes.Count == 0 ? "none" : homes[0].Name)} of {homes.Count}");

            report.Check(homes.Exists(r => r.Id == coal.Id),
                "control: the overflow chest is still an answer, just a worse one",
                $"answers={homes.Count}");

            // A cap of one against a chest holding one: at its cap, not over it. The boundary
            // is where an off-by-one would live.
            ColonyOperations.EditSettings(colony, wood.Id, s => s.SetCap("Wood", 1));
            GameObject woodChest = ZNetScene.instance.FindInstance(wood.Id);
            if (woodChest != null && woodChest.TryGetComponent(out Container into))
            {
                GameObject prefab = ObjectDB.instance.GetItemPrefab("Wood");
                if (prefab != null && prefab.TryGetComponent(out ItemDrop item))
                {
                    ItemDrop.ItemData one = item.m_itemData.Clone();
                    one.m_dropPrefab = prefab;
                    one.m_stack = 1;
                    into.GetInventory().AddItem(one);
                }
            }

            yield return null;
            SettlementIndex.ResetForTest();
            List<StructureRecord> capped = SettlementIndex.WhereDoesItGo(colony, "Wood", at);
            report.Check(!capped.Exists(r => r.Id == wood.Id),
                "a chest at its cap stops attracting more of that item",
                $"answers={capped.Count}");

            report.Check(capped.Exists(r => r.Id == coal.Id),
                "control: the cap silences one chest, not the settlement",
                $"answers={capped.Count}");

            ColonyOperations.EditSettings(colony, wood.Id, s => s.SetCap("Wood", 50));
            SettlementIndex.ResetForTest();
            List<StructureRecord> raised = SettlementIndex.WhereDoesItGo(colony, "Wood", at);
            report.Check(raised.Exists(r => r.Id == wood.Id),
                "control: below its cap the same chest accepts again",
                $"answers={raised.Count}");

            // The dump: a chest that names something else entirely, taking what nothing claims.
            ColonyOperations.EditSettings(colony, coal.Id, s =>
            {
                s.Accepts = new List<string> { "Coal" };
                s.TakeUnclaimed = false;
            });
            SettlementIndex.ResetForTest();
            List<StructureRecord> unclaimed = SettlementIndex.WhereDoesItGo(colony, "Flint", at);
            report.Check(!unclaimed.Exists(r => r.Id == coal.Id),
                "control: without the dump flag a chest takes only what it named",
                $"answers={unclaimed.Count}");

            ColonyOperations.EditSettings(colony, coal.Id, s => s.TakeUnclaimed = true);
            SettlementIndex.ResetForTest();
            unclaimed = SettlementIndex.WhereDoesItGo(colony, "Flint", at);
            report.Check(unclaimed.Exists(r => r.Id == coal.Id),
                "an item nothing claims goes to the settlement's dump",
                $"answers={unclaimed.Count}");

            // Settings must survive the blob, or the screen edits something the settlement
            // never reads. The format version was bumped for exactly these two fields.
            StructureRecord reread = colony.State.GetStructures().Find(r => r.Id == coal.Id);
            report.Check(reread != null && reread.Settings.TakeUnclaimed,
                "the dump flag survives being written to the colony record",
                $"read={(reread == null ? "no record" : reread.Settings.TakeUnclaimed.ToString())}");

            StructureRecord rereadWood = colony.State.GetStructures().Find(r => r.Id == wood.Id);
            report.Check(rereadWood != null && rereadWood.Settings.CapFor("Wood") == 50,
                "a cap survives being written to the colony record",
                $"cap={(rereadWood == null ? -1 : rereadWood.Settings.CapFor("Wood"))}");

            report.Check(rereadWood != null && rereadWood.Settings.CapFor("Stone") < 0,
                "control: an item with no cap reads as having none rather than as zero",
                $"cap={(rereadWood == null ? -1 : rereadWood.Settings.CapFor("Stone"))}");

            ColonyOperations.EditSettings(colony, wood.Id, s => s.SetCap("Wood", -1));
            ColonyOperations.EditSettings(colony, coal.Id, s => s.TakeUnclaimed = false);
            yield return null;
        }

        /// <summary>
        ///     One villager sleeps in one bed, and moving them says which bed they left.
        /// </summary>
        private static IEnumerator CheckBeds(TestReport report, Colony colony, Vector3 origin)
        {
            GameObject first = SpawnFirst(origin + new Vector3(-6f, 0f, -6f), "bed", "piece_bed", "bed_wood");
            GameObject second = SpawnFirst(origin + new Vector3(-9f, 0f, -6f), "bed", "piece_bed", "bed_wood");
            yield return new WaitForSecondsRealtime(.3f);
            StructureRecord bedA = Register(colony, first, "First bed");
            StructureRecord bedB = Register(colony, second, "Second bed");

            Villager villager = VillagerLifecycle.Spawn(colony);
            yield return null;
            if (bedA == null || bedB == null || villager == null ||
                !villager.TryGetComponent(out ZNetView villagerView) || !villagerView.IsValid())
            {
                report.Check(false, "bed check could register two beds and a villager");
                yield break;
            }

            ZDOID who = villagerView.GetZDO().m_uid;
            report.Check(SettlementIndex.FreeBeds(colony).Count >= 2,
                "control: both beds are free before anyone is assigned",
                $"free={SettlementIndex.FreeBeds(colony).Count}");

            VillagerRoster.Assign(colony, bedA, new List<string> { who.ToString() });
            StructureRecord assigned = SettlementIndex.BedOf(colony, who);
            report.Check(assigned != null && assigned.Id == bedA.Id,
                "a villager can be given a bed, and the settlement knows which",
                $"bed={(assigned == null ? "none" : assigned.Name)}");

            VillagerRoster.Assign(colony, bedB, new List<string> { who.ToString() });
            StructureRecord moved = SettlementIndex.BedOf(colony, who);
            List<StructureRecord> free = SettlementIndex.FreeBeds(colony);
            report.Check(moved != null && moved.Id == bedB.Id && free.Exists(r => r.Id == bedA.Id),
                "assigning a bed to someone who has one moves them, freeing the old bed",
                $"bed={(moved == null ? "none" : moved.Name)} freeNow={free.Count} said='{Core.Report.Last}'");
            report.Check(Core.Report.Last.Contains("First bed"),
                "moving a villager says which bed they left",
                $"said='{Core.Report.Last}'");

            VillagerLifecycle.Remove(colony, who);
            if (first != null) Release(first);
            if (second != null) Release(second);
            yield return null;
        }

        /// <summary>
        ///     A registered structure at an outpost nobody is near stays loaded.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         This is what decides whether "unknown capacity" is the common case or a rare
        ///         fallback. Registration is bounded by the colony radius, so a structure is
        ///         always near <em>its own</em> hearth - the interesting case is a second
        ///         settlement far from the player, which is precisely what the keep-alive
        ///         exists for.
        ///     </para>
        ///     <para>
        ///         The control is the unregistered chest in CheckUnloadedStaysKnown, sitting at
        ///         the same distance and observed to unload. Same place, same prefab, same
        ///         distance from the player: the only difference is being registered, so that
        ///         is what the difference in outcome can be attributed to.
        ///     </para>
        /// </remarks>
        private static IEnumerator CheckOutpostStaysLoaded(TestReport report, Colony colony)
        {
            if (!ModConfig.KeepAliveEnabled.Value)
            {
                report.Note("outpost check skipped: keep-alive is off");
                yield break;
            }

            // Built on loaded ground and then moved, not built where it is going: unloaded
            // terrain answers zero to a ground query, so a piece placed at 900m lands under
            // the world and is destroyed. That cost a run here and had already cost one in
            // milestone 1 - the same mistake, the second time.
            Vector3 near = colony.transform.position + new Vector3(0f, 0f, 40f);
            GameObject hearth = Spawn(ColonyPrefab.PrefabName, near);
            GameObject chest = Spawn("piece_chest_wood", near + new Vector3(3f, 0f, 0f));
            yield return new WaitForSecondsRealtime(.4f);

            Colony outpost = hearth != null ? hearth.GetComponent<Colony>() : null;
            if (outpost == null || chest == null || !chest.TryGetComponent(out ZNetView view) || !view.IsValid())
            {
                report.Check(false, "outpost check could place a distant colony and chest");
                yield break;
            }

            outpost.EnsureNamed();
            ZDOID chestId = view.GetZDO().m_uid;
            RegisterOutcome outcome = ColonyOperations.Register(outpost, chest);
            report.Check(outcome == RegisterOutcome.Registered,
                "control: the outpost's chest was registered to it", $"outcome={outcome}");

            // Now move both far away, together, so the chest stays inside its own colony's
            // radius while both leave the player's active area.
            Vector3 far = colony.transform.position + Vector3.right * 900f;
            Relocate(hearth, far);
            Relocate(chest, far + new Vector3(3f, 0f, 0f));

            // Long enough for the colony scan and at least one keep-alive pass to notice.
            yield return new WaitForSecondsRealtime(ModConfig.KeepAliveScanSeconds.Value * 3f + 2f);

            bool stillLoaded = ZNetScene.instance.FindInstance(chestId) != null;
            Inventory readable = StructureInventory.Live(chestId);
            report.Check(stillLoaded,
                "a registered structure 900m from the player is kept loaded by its colony",
                $"loaded={stillLoaded} capacityReadable={readable != null}");

            if (chest != null) Release(chest);
            if (outpost != null && outpost.TryGetComponent(out ZNetView outpostView) && outpostView.IsValid())
            {
                outpostView.ClaimOwnership();
                ZNetScene.instance.Destroy(outpost.gameObject);
            }

            yield return null;
        }

        /// <summary>
        ///     Moves an object and its ZDO together, so the game agrees about where it is.
        /// </summary>
        private static void Relocate(GameObject target, Vector3 position)
        {
            if (target == null || !target.TryGetComponent(out ZNetView view) || !view.IsValid()) return;
            view.ClaimOwnership();
            target.transform.position = position;
            view.GetZDO().SetPosition(position);
        }

        /// <summary>
        ///     What a registered container's capacity can and cannot tell the settlement.
        /// </summary>
        /// <remarks>
        ///     This began as a check that contents are readable from the ZDO, which would have
        ///     let the index answer capacity for structures nobody is standing near. It is not:
        ///     a chest holding two wood, owned by this peer, reported an empty record after the
        ///     change, after an explicit Save, through a fresh ZDO reference, through ZDOMan,
        ///     and using the game's own key constant. What survives is the fact itself, so the
        ///     next person to assume it does not spend another five runs finding out.
        /// </remarks>
        private static IEnumerator CheckStoredContents(TestReport report, Colony colony, Vector3 origin)
        {
            GameObject chest = Spawn("piece_chest_wood", origin + Vector3.forward * 12f);
            yield return null;
            if (chest == null || !chest.TryGetComponent(out ZNetView view) || !view.IsValid() ||
                !chest.TryGetComponent(out Container container))
            {
                report.Check(false, "capacity check could place a chest");
                yield break;
            }

            view.ClaimOwnership();
            ZDOID id = view.GetZDO().m_uid;

            bool added = Add(container.GetInventory(), "Wood") &&
                         Add(container.GetInventory(), "Wood") &&
                         Add(container.GetInventory(), "Coal");
            report.Check(added && Count(container.GetInventory(), "Wood") == 2,
                "control: the chest actually holds what was put in it",
                $"added={added} owner={view.IsOwner()}");

            yield return new WaitForSecondsRealtime(1.6f);

            // The finding, asserted so it cannot quietly start being true without anyone
            // noticing - if a game update makes containers flush, this fails and the index can
            // be made smarter on purpose rather than by accident.
            int record = view.GetZDO().GetString(ZDOVars.s_items, string.Empty).Length;
            report.Check(record == 0,
                "a container's contents are NOT on its ZDO, so capacity is a loaded-only question",
                $"recordChars={record} live={Count(container.GetInventory(), "Wood")}");

            Inventory live = StructureInventory.Live(id);
            report.Check(live != null && Count(live, "Wood") == 2,
                "a loaded container's contents are readable",
                $"wood={(live == null ? -1 : Count(live, "Wood"))}");

            report.Check(StructureInventory.HasRoomFor(id, "Coal"),
                "control: a loaded chest with space reports room");

            Fill(container.GetInventory(), "Wood");
            yield return null;
            report.Check(!StructureInventory.HasRoomFor(id, "Coal"),
                "a full chest reports no room, so it is not an answer",
                $"emptySlots={StructureInventory.Live(id)?.GetEmptySlots()}");

            // Unknown, not empty. An unloaded container must stay a candidate rather than
            // looking like a chest with infinite room or one with none.
            ZDOID unheard = new ZDOID(4242424242u, 987654321u);
            report.Check(StructureInventory.Live(unheard) == null &&
                         StructureInventory.HasRoomFor(unheard, "Wood"),
                "an unreadable container is unknown rather than full, so it stays a candidate");

            Release(chest);
            yield return null;
        }

        /// <summary>
        ///     A record goes when its structure is known destroyed, and only then.
        /// </summary>
        /// <remarks>
        ///     The control is the whole safety argument, and matters more than the feature. A
        ///     reaper that removes records for things it merely cannot find would delete an
        ///     outpost's configuration the moment nobody stood near it - silently, permanently,
        ///     and discovered a long way from here.
        /// </remarks>
        private static IEnumerator CheckReaper(TestReport report, Colony colony, Vector3 origin)
        {
            StructureReaper.ResetForTest();

            GameObject doomed = Spawn("piece_chest_wood", origin + Vector3.right * 10f);
            yield return null;
            StructureRecord record = Register(colony, doomed, "Doomed storage");
            report.Check(record != null, "control: the reaper's subject was registered before dying");
            if (record == null) yield break;

            // One sweep while it lives, so the reaper has seen where it is. That is what a
            // record loaded from disk needs: its stored address is from the previous session
            // and is not the id the game will list as dead.
            StructureReaper.Sweep(colony);
            report.Check(colony.State.GetStructures().Any(r => r.Id == record.Id),
                "control: a living structure survives a sweep");

            ZDOID doomedId = doomed.GetComponent<ZNetView>().GetZDO().m_uid;
            Release(doomed);

            // Swept repeatedly rather than once after a fixed pause. A destroy has to travel
            // through ZDOMan before the death is recorded, and a single sweep four tenths of a
            // second later was reading the answer before it existed - which failed as
            // "reaped=0", indistinguishable from a reaper that does not work.
            int reaped = 0;
            float waited = 0f;
            while (waited < 5f && reaped == 0)
            {
                yield return new WaitForSecondsRealtime(.25f);
                waited += .25f;
                reaped = StructureReaper.Sweep(colony);
            }

            bool gone = ZDOMan.instance.GetZDO(doomedId) == null;
            bool listed = ZDOMan.instance.m_deadZDOs != null &&
                          ZDOMan.instance.m_deadZDOs.ContainsKey(doomedId);

            report.Check(reaped >= 1 && !colony.State.GetStructures().Any(r => r.Id == record.Id),
                "a destroyed structure's record is removed",
                $"reaped={reaped} after {waited:0.0}s, zdoGone={gone}, listedAsDead={listed}");

            // The control. This record's object cannot be resolved and is not in the dead
            // list - exactly what an unloaded outpost looks like to a peer that cannot see it.
            bool survived = colony.State.GetStructures().Any(r => r.Name == "Unfindable storage");
            report.Check(survived,
                "control: a record that cannot be found, but is not known dead, survives the reaper");
        }

        /// <summary>
        ///     Adds a record pointing at an id the world never issued.
        /// </summary>
        /// <remarks>
        ///     Unresolvable and not dead-listed, which is precisely how a structure in an
        ///     unloaded zone looks from a peer that has not been told about it. Built by hand
        ///     rather than through registration because no real object can be put into this
        ///     state on purpose - that is the point of it.
        /// </remarks>
        private static void PlantUnfindable(Colony colony, string name)
        {
            List<StructureRecord> records = colony.State.GetStructures();
            records.Add(new StructureRecord
            {
                Id = new ZDOID(4242424242u, 987654321u),
                PersistentId = "benchmark-unfindable-v1",
                Name = name,
                Prefab = "piece_chest_wood",
                Capabilities = StructureCapability.Storage
            });
            colony.State.SetStructures(records);
        }

        /// <summary>
        ///     Registering: what a colony accepts, what it refuses, and what it says.
        /// </summary>
        /// <remarks>
        ///     Every claim here is paired, because "it registered" and "it refused" are both
        ///     easy to produce by accident - a path that refuses everything passes half of
        ///     these, and a path that accepts everything passes the other half.
        /// </remarks>
        private static IEnumerator CheckRegistration(TestReport report, Colony colony, Vector3 origin)
        {
            // A bed is the one capability with no prior coverage at all, and naming a prefab
            // that does not exist gives a silent null that reads as a feature failure - which
            // has happened here before. So the candidates are tried and the answer reported.
            GameObject bed = SpawnFirst(origin + Vector3.forward * 4f, "bed", "piece_bed", "bed_wood");
            GameObject kiln = SpawnFirst(origin + Vector3.forward * 8f, "charcoal_kiln", "smelter");
            yield return new WaitForSecondsRealtime(.3f);

            report.Check(bed != null && kiln != null,
                "control: the registration check found a bed and a processing station to use",
                $"bed={(bed == null ? "none" : Utils.GetPrefabName(bed))} " +
                $"kiln={(kiln == null ? "none" : Utils.GetPrefabName(kiln))}");

            if (bed != null)
            {
                RegisterOutcome outcome = ColonyOperations.Register(colony, bed);
                StructureRecord record = Recorded(colony, bed);
                report.Check(outcome == RegisterOutcome.Registered && record != null &&
                             (record.Capabilities & StructureCapability.Rest) != 0,
                    "a bed registers as Rest",
                    $"outcome={outcome} capabilities={(record == null ? "none" : StructureCapabilities.Describe(record.Capabilities))}");

                report.Check(ColonyOperations.Register(colony, bed) == RegisterOutcome.AlreadyHere,
                    "registering the same structure twice is refused as already registered");
            }

            if (kiln != null)
            {
                RegisterOutcome outcome = ColonyOperations.Register(colony, kiln);
                StructureRecord record = Recorded(colony, kiln);
                report.Check(outcome == RegisterOutcome.Registered && record != null &&
                             (record.Capabilities & StructureCapability.Processing) != 0,
                    "a charcoal kiln registers as Processing",
                    $"outcome={outcome} capabilities={(record == null ? "none" : StructureCapabilities.Describe(record.Capabilities))}");
            }

            // Control: a creature is refused, and refused for being a creature rather than for
            // being out of reach or unreadable. Standing right beside the hearth so distance
            // cannot be what rejects it.
            Villager creature = VillagerLifecycle.Spawn(colony);
            yield return null;
            if (creature != null)
            {
                int before = colony.State.GetStructures().Count;
                RegisterOutcome outcome = ColonyOperations.Register(colony, creature.gameObject);
                report.Check(outcome == RegisterOutcome.NotUsable &&
                             colony.State.GetStructures().Count == before,
                    "control: a creature is refused and nothing is registered",
                    $"outcome={outcome} explain='{StructureRegistry.Explain(creature.gameObject)}'");

                if (creature.TryGetComponent(out ZNetView creatureView) && creatureView.IsValid())
                    VillagerLifecycle.Remove(colony, creatureView.GetZDO().m_uid);
            }

            // Out of reach, with the reason.
            GameObject distant = Spawn("piece_chest_wood",
                origin + new Vector3(colony.EffectiveRadius + 14f, 0f, 0f));
            yield return null;
            if (distant != null)
            {
                RegisterOutcome outcome = ColonyOperations.Register(colony, distant);
                report.Check(outcome == RegisterOutcome.OutOfReach,
                    "a structure outside the colony's reach is refused for being out of reach",
                    $"outcome={outcome}");
                Release(distant);
            }

            yield return CheckMoveBetweenColonies(report, colony, origin);
        }

        /// <summary>
        ///     A structure belongs to one colony at a time, and changing which says so.
        /// </summary>
        private static IEnumerator CheckMoveBetweenColonies(TestReport report, Colony colony, Vector3 origin)
        {
            Vector3 where = origin + new Vector3(0f, 0f, -30f);
            if (ZoneSystem.instance == null || !ZoneSystem.instance.GetSolidHeight(where, out float _))
            {
                report.Check(false, "move check found ground for its second hearth");
                yield break;
            }

            GameObject spawned = Spawn(ColonyPrefab.PrefabName, where);
            GameObject chest = Spawn("piece_chest_wood", where + new Vector3(3f, 0f, 0f));
            yield return new WaitForSecondsRealtime(.3f);

            Colony second = spawned != null ? spawned.GetComponent<Colony>() : null;
            if (second == null || chest == null)
            {
                report.Check(false, "move check kept its second hearth and chest alive");
                yield break;
            }

            second.EnsureNamed();
            RegisterOutcome first = ColonyOperations.Register(second, chest);
            bool inSecond = Recorded(second, chest) != null;
            report.Check(first == RegisterOutcome.Registered && inSecond,
                "control: the chest belonged to the second colony before the move",
                $"outcome={first}");

            RegisterOutcome moved = ColonyOperations.Register(colony, chest);
            report.Check(moved == RegisterOutcome.Moved,
                "registering a structure another colony holds moves it, and says so",
                $"outcome={moved}");
            report.Check(Recorded(colony, chest) != null && Recorded(second, chest) == null,
                "a moved structure is in exactly one colony's list",
                $"inNew={Recorded(colony, chest) != null} inOld={Recorded(second, chest) != null}");

            if (chest.TryGetComponent(out ZNetView chestView) && chestView.IsValid())
                colony.RemoveStructure(chestView.GetZDO().m_uid);
            Release(chest);
            if (second.TryGetComponent(out ZNetView secondView) && secondView.IsValid())
            {
                secondView.ClaimOwnership();
                ZNetScene.instance.Destroy(second.gameObject);
            }

            yield return null;
        }

        /// <summary>
        ///     Makes sure every registered structure will actually be written by the save.
        /// </summary>
        /// <remarks>
        ///     <c>ZDOMan.Save</c> walks its sector index rather than its id dictionary, so a ZDO
        ///     missing from that index is silently not written - which presents as "registration
        ///     stopped persisting" rather than as a fixture problem. The villager has had this
        ///     repair since it was first found.
        ///
        ///     Called immediately before the save rather than at snapshot time. Doing it earlier
        ///     fixed it once and then stopped working: there is a grace period between the two,
        ///     and zones unloading during it can drop a ZDO back out of the index. Repairing at
        ///     the last possible moment leaves nothing in between.
        /// </remarks>
        internal static int PrepareStructuresForSave(Colony colony)
        {
            if (colony == null || ZDOMan.instance == null) return 0;

            var sectorsField = typeof(ZDOMan).GetField("m_objectsBySector",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var sectors = sectorsField?.GetValue(ZDOMan.instance) as List<ZDO>[];

            int repaired = 0;
            int total = 0;
            foreach (StructureRecord record in colony.State.GetStructures())
            {
                ZDO zdo = ZDOMan.instance.GetZDO(record.Id);
                if (zdo == null || !zdo.IsValid()) continue;
                total++;

                uint sector = zdo.GetSectorIndex().Sector;
                bool indexed = sectors != null && sector < sectors.Length &&
                               sectors[sector] != null && sectors[sector].Contains(zdo);
                if (!indexed)
                {
                    ZDOMan.instance.AddToSector(zdo, zdo.GetSectorIndex());
                    repaired++;
                }

                ZDOMan.instance.SetDirtySector(zdo);
            }

            // Written to the run directory, not just the log: the game log is per-launch and
            // the reload overwrites it, so evidence about what happened before the save was
            // being destroyed by the very relaunch it was needed to explain.
            // Appended, and named. Every loaded colony passes through here, and writing the
            // file instead of appending meant reading whichever happened to be last - the same
            // "one writer per piece of state" mistake that made a persistence failure name the
            // wrong object once before.
            List<string> missing = new List<string>();
            foreach (StructureRecord record in colony.State.GetStructures())
            {
                ZDO check = ZDOMan.instance.GetZDO(record.Id);
                if (check == null || !check.IsValid())
                    missing.Add($"{record.Name}(dead={KnownDead(record.Id)})");
            }

            string note = $"colony '{colony.State.Name}': {total} resolvable of " +
                          $"{colony.State.GetStructures().Count}, {repaired} repaired, " +
                          $"names: {string.Join("|", colony.State.GetStructures().ConvertAll(r => r.Name).ToArray())}, " +
                          $"missing: {(missing.Count == 0 ? "none" : string.Join("|", missing.ToArray()))}";
            Core.Log.Info("[Benchmark] " + note);
            try
            {
                System.IO.File.AppendAllText(System.IO.Path.Combine(
                    ModConfig.BenchmarkOutputPath.Value, "before-save.txt"), note + System.Environment.NewLine);
            }
            catch (System.Exception e) { Core.Log.Warning("could not write before-save note: " + e.Message); }

            return repaired;
        }

        private static string _lostAfter = string.Empty;

        /// <summary>
        ///     Notes the first stage after which the benchmark's own storage record stops
        ///     resolving.
        /// </summary>
        /// <remarks>
        ///     Bisecting by instrumentation rather than by reading: a chest registered early was
        ///     found destroyed by the time the world saved, and which of a dozen checks killed
        ///     it is not answerable by inspection. Written to the run directory, because the
        ///     game log does not survive the relaunch that needs explaining.
        /// </remarks>
        internal static void Trace(Colony colony, string stage)
        {
            if (_lostAfter.Length > 0 || colony == null || ZDOMan.instance == null) return;

            StructureRecord record = colony.State.GetStructures().Find(r => r.Name == "Renamed storage");
            if (record == null)
            {
                _lostAfter = stage + " (record itself gone)";
            }
            else if (ZDOMan.instance.GetZDO(record.Id) == null)
            {
                _lostAfter = $"{stage} (dead={KnownDead(record.Id)})";
            }
            else
            {
                return;
            }

            Core.Log.Error("[Benchmark] 'Renamed storage' stopped resolving after " + _lostAfter);
            try
            {
                System.IO.File.AppendAllText(System.IO.Path.Combine(
                    ModConfig.BenchmarkOutputPath.Value, "lost-at.txt"),
                    "stopped resolving after " + _lostAfter + System.Environment.NewLine);
            }
            catch (System.Exception) { }
        }

        /// <summary>
        ///     Places and registers the structure the reload phase will look for.
        /// </summary>
        /// <remarks>
        ///     Its own chest, created last. Sharing a subject with an earlier check made a
        ///     persistence failure depend on everything that ran in between - which is the
        ///     "keep destructive fixtures away from other checks' subjects" lesson, arrived at
        ///     a second time by a different route.
        /// </remarks>
        /// <summary>
        ///     Leaves a villager holding a load it has not delivered, moments before the save.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The whole design rests on <em>facts outrank the recorded state</em>: a villager
        ///         that reloads holding something goes straight to delivering it, which is what
        ///         makes reloads, ownership transfers and stolen targets repair themselves with no
        ///         migration. Nothing was testing it. The reload phase proved that names, homes
        ///         and bags survive a save, and said nothing about whether work does.
        ///     </para>
        ///     <para>
        ///         A villager of its own rather than the primary one, whose bag is asserted to
        ///         hold exactly three coal on the other side: a hauler that delivered or put down
        ///         part of its load would fail that check from underneath, and the two faults
        ///         would be indistinguishable.
        ///     </para>
        /// </remarks>
        private static void LeaveAHaulerMidDelivery(Colony colony, StructureRecord destination)
        {
            if (colony == null || destination == null) return;

            ColonyOperations.EditSettings(colony, destination.Id,
                s => s.Accepts = new List<string> { "Wood" });

            colony.State.SetJobs(new List<JobDefinition>
            {
                new JobDefinition { Id = "resume", Name = "Resume haul", Kind = JobKind.Haul, Repeat = 30 }
            });

            Villager hauler = VillagerLifecycle.Spawn(colony);
            if (hauler == null || !hauler.TryGetComponent(out ZNetView view) || !view.IsValid())
            {
                Core.Log.Error("[Benchmark] could not leave a hauler mid-delivery; the reload phase will say so");
                return;
            }

            VillagerState state = new VillagerState(view.GetZDO());
            state.SetName(PersistedHaulerName);
            state.SetQueue(new List<string> { "resume" });

            Container bag = VillagerInventory.Attach(hauler.gameObject, view);
            Clear(bag.GetInventory());
            for (int i = 0; i < 5; i++) Add(bag.GetInventory(), "Wood");

            // Interrupted at the point that matters: carrying, with a destination chosen and
            // the walk not finished.
            state.SetCargo("Wood");
            state.SetDestination(destination.Id);
            state.SetWorkState((int)Jobs.Haul.HaulState.Delivering);

            // And held there. The save does not happen the instant this returns, and the first
            // attempt gave the villager long enough to walk over and finish - so the reload
            // phase found an empty bag, a cleared cargo and a work state back at Choosing, and
            // reported a villager that had resumed nothing because there was nothing left to
            // resume. A tired villager yields without touching its trip, which is exactly the
            // hold this needs; the reload phase wakes it and then watches.
            state.SetResting(false);
            state.SetRestRate(0f);
            state.SetEnergy(0f);

            Core.Log.Info($"[Benchmark] hauler left mid-delivery: carrying={Count(bag.GetInventory(), "Wood")} " +
                          $"destination={destination.Id}");
        }

        private static StructureRecord RegisterPersistenceSubject(Colony colony)
        {
            GameObject chest = Spawn("piece_chest_wood", colony.transform.position + new Vector3(0f, 0f, -6f));
            if (chest == null || !chest.TryGetComponent(out ZNetView view) || !view.IsValid()) return null;

            RegisterOutcome outcome = ColonyOperations.Register(colony, chest);
            if (outcome != RegisterOutcome.Registered && outcome != RegisterOutcome.Moved)
            {
                Core.Log.Error($"[Benchmark] persistence subject was not registered: {outcome}");
                return null;
            }

            ZDOID id = view.GetZDO().m_uid;
            ColonyOperations.RenameStructure(colony, id, PersistedStructureName);
            return colony.State.GetStructures().Find(r => r.Id == id);
        }

        /// <summary>The colony's record for an object, or null.</summary>
        private static StructureRecord Recorded(Colony colony, GameObject target)
        {
            if (colony == null || target == null ||
                !target.TryGetComponent(out ZNetView view) || !view.IsValid()) return null;
            ZDOID id = view.GetZDO().m_uid;
            return colony.State.GetStructures().Find(r => r.Id == id);
        }

        /// <summary>
        ///     Spawns the first prefab name that exists, and says which. Guessing a prefab name
        ///     that does not exist produced a silent null once, which read as the feature being
        ///     broken rather than the fixture being absent.
        /// </summary>
        private static GameObject SpawnFirst(Vector3 position, params string[] names)
        {
            foreach (string name in names)
            {
                if (ZNetScene.instance == null || ZNetScene.instance.GetPrefab(name) == null) continue;
                return Spawn(name, position);
            }

            Core.Log.Warning("[Benchmark] none of these prefabs exist: " + string.Join(",", names));
            return null;
        }

        /// <summary>
        ///     A durable reference must follow its token, not the raw address stored beside it.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Loading renumbers every ZDO to (1, N) in file order - measured across runs,
        ///         one villager returned as 1:2605 and 1:2607 for the same object. Records
        ///         persist the id they last resolved to, so after one reload and any write the
        ///         blob holds a dense address that the next load hands to a different object.
        ///         Resolving by that address finds something live and valid and wrong.
        ///     </para>
        ///     <para>
        ///         Checked here rather than across relaunches because the corruption needs a
        ///         second reload after a write to appear, and the harness runs two launches. A
        ///         token deliberately paired with another live object's address is the same
        ///         situation without the wait.
        ///     </para>
        /// </remarks>
        private static IEnumerator CheckDurableReference(TestReport report, Colony colony, Vector3 origin)
        {
            GameObject chest = Spawn("piece_chest_wood", origin + Vector3.left * 6f);
            yield return null;
            if (chest == null || !chest.TryGetComponent(out ZNetView view) || !view.IsValid() ||
                !colony.TryGetComponent(out ZNetView colonyView) || !colonyView.IsValid())
            {
                report.Check(false, "durable-reference check could place its fixture");
                yield break;
            }

            view.ClaimOwnership();
            string token = Core.PersistentZdoReference.Ensure(view.GetZDO());
            ZDOID chestId = view.GetZDO().m_uid;
            ZDOID otherId = colonyView.GetZDO().m_uid;

            report.Check(!string.IsNullOrEmpty(token) && chestId != otherId,
                "control: the fixture has a token and two distinct live objects to confuse",
                $"token={(string.IsNullOrEmpty(token) ? "none" : "present")} chest={chestId} other={otherId}");

            ZDOID wrongFallback = Core.PersistentZdoReference.Resolve(token, otherId);
            report.Check(wrongFallback == chestId,
                "a token outranks a stale address that resolves to the wrong object",
                $"resolved={wrongFallback} wanted={chestId} decoy={otherId}");

            ZDOID matching = Core.PersistentZdoReference.Resolve(token, chestId);
            report.Check(matching == chestId,
                "control: an address that agrees with its token is still trusted",
                $"resolved={matching}");

            ZDOID noToken = Core.PersistentZdoReference.Resolve(string.Empty, otherId);
            report.Check(noToken == otherId,
                "control: with no token the raw address is all there is, and is used",
                $"resolved={noToken}");

            Release(chest);
            yield return null;
        }

        private static IEnumerator CheckZdoLifetime(TestReport report, Colony colony)
        {
            GameObject chest = Spawn("piece_chest_wood", colony.transform.position + Vector3.right * 8f);
            if (chest == null || !chest.TryGetComponent(out ZNetView view) || !view.IsValid())
            {
                report.Check(false, "a destroyed object can be told from one that is not loaded", "no fixture");
                yield break;
            }

            ZDOID id = view.GetZDO().m_uid;
            bool aliveResolves = ZDOMan.instance.GetZDO(id) != null;
            bool aliveNotDead = !KnownDead(id);

            // A ZDOID nothing ever issued. To this peer an object it has never been told
            // about and one whose zone is not loaded are the same situation, so this is the
            // stand-in for "unloaded" that needs no teleporting to produce.
            ZDOID unheard = new ZDOID(4242424242u, 987654321u);
            bool unheardResolves = ZDOMan.instance.GetZDO(unheard) != null;
            bool unheardDead = KnownDead(unheard);

            Release(chest);
            yield return null;
            yield return new WaitForSecondsRealtime(.5f);
            bool destroyedResolves = ZDOMan.instance.GetZDO(id) != null;
            bool destroyedListed = KnownDead(id);

            report.Check(aliveResolves && aliveNotDead && !unheardResolves && !unheardDead &&
                         destroyedListed,
                "a destroyed object can be told from one that is merely not in memory",
                $"alive[resolves={aliveResolves} dead={!aliveNotDead}] " +
                $"destroyed[resolves={destroyedResolves} dead={destroyedListed}] " +
                $"unheard[resolves={unheardResolves} dead={unheardDead}]");

            ReportVillagerMaterials();
            yield return CheckUnloadedStaysKnown(report, colony);
        }

        /// <summary>
        ///     Does a structure whose zone unloads stay *known*, or does it vanish like a
        ///     destroyed one?
        /// </summary>
        /// <remarks>
        ///     The check above used a ZDOID nothing ever issued as a stand-in for "not in
        ///     memory". That is a proxy, and a proxy is not evidence. This does the real
        ///     thing: put a chest far enough away that the game stops keeping it instantiated,
        ///     and watch what each lookup says while it happens.
        ///
        ///     The answer decides how much machinery cleanup needs. If an unloaded object
        ///     still resolves, then a lookup returning nothing already means destroyed and no
        ///     dead-list bookkeeping is required at all.
        /// </remarks>
        private static IEnumerator CheckUnloadedStaysKnown(TestReport report, Colony colony)
        {
            // Far enough to be outside any active zone, and nowhere near anything the colony
            // keeps alive - an unregistered chest holds no zone open by itself.
            Vector3 far = colony.transform.position + Vector3.right * 900f;
            GameObject remote = Spawn("piece_chest_wood", far);
            if (remote == null || !remote.TryGetComponent(out ZNetView view) || !view.IsValid())
            {
                report.Check(false, "an unloaded structure stays known", "no fixture");
                yield break;
            }

            ZDOID id = view.GetZDO().m_uid;
            bool everUnloaded = false;
            bool knownWhileUnloaded = false;
            bool deadWhileUnloaded = false;

            for (int attempt = 0; attempt < 60 && !everUnloaded; attempt++)
            {
                yield return new WaitForSecondsRealtime(.5f);
                if (ZNetScene.instance.FindInstance(id) != null) continue;
                everUnloaded = true;
                knownWhileUnloaded = ZDOMan.instance.GetZDO(id) != null;
                deadWhileUnloaded = KnownDead(id);
            }

            // Never observing the unload is not a pass and not a failure - it is a measurement
            // that did not happen, and reporting it as either would be a lie.
            report.Check(!everUnloaded || (knownWhileUnloaded && !deadWhileUnloaded),
                everUnloaded
                    ? "an unloaded structure stays known, and is not listed as dead"
                    : "an unloaded structure stays known (INCONCLUSIVE - it never unloaded)",
                $"unloaded={everUnloaded} stillKnown={knownWhileUnloaded} listedDead={deadWhileUnloaded}");

            Release(remote);
        }

        /// <summary>
        ///     Reports what a live villager is actually drawn with.
        /// </summary>
        /// <remarks>
        ///     Villagers render gold while the player renders as skin. The rig they are cloned
        ///     from is a glowing spectral thing, so the likely cause is an emissive material -
        ///     but the last two asset-shaped problems here were both diagnosed wrongly by
        ///     reasoning about them, so this reports the shaders, colours and emission actually
        ///     in use rather than assuming.
        /// </remarks>
        private static void ReportVillagerMaterials()
        {
            Villager subject = null;
            foreach (Villager candidate in Villager.Instances)
                if (candidate != null) { subject = candidate; break; }
            if (subject == null) { Core.Log.Info("[villager] no instance to inspect"); return; }

            foreach (SkinnedMeshRenderer renderer in subject.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer == null) continue;
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null) continue;
                    string emission = material.HasProperty("_EmissionColor")
                        ? material.GetColor("_EmissionColor").ToString() : "none";
                    string skin = material.HasProperty("_SkinColor")
                        ? material.GetColor("_SkinColor").ToString() : "none";
                    string hue = material.HasProperty("_Color")
                        ? material.GetColor("_Color").ToString() : "none";
                    Core.Log.Info($"[villager] {renderer.name} mat={material.name} " +
                                  $"shader={(material.shader != null ? material.shader.name : "null")} " +
                                  $"color={hue} skin={skin} emission={emission} " +
                                  $"keywords={string.Join("|", material.shaderKeywords)}");
                }
            }
        }

        /// <summary>Whether the game has recorded this object as destroyed.</summary>
        private static bool KnownDead(ZDOID id) =>
            ZDOMan.instance != null && ZDOMan.instance.m_deadZDOs != null &&
            ZDOMan.instance.m_deadZDOs.ContainsKey(id);

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
        ///     Deposits survive in a zone kept open for a villager, so mining works off-screen.
        /// </summary>
        /// <remarks>
        ///     The failure this guards is invisible by construction: a mine outpost whose rock
        ///     was filtered out of its own kept zone finds nothing, idles, and works perfectly
        ///     every time somebody comes to look.
        /// </remarks>
        private static IEnumerator CheckRockIsKeptLoaded(TestReport report)
        {
            if (!LoadAllowlist.IsReady) LoadAllowlist.Rebuild();
            if (!Mineable.IsReady) Mineable.Rebuild();

            string deposit = Mineable.SampleDeposit(0, 99);
            if (string.IsNullOrEmpty(deposit) || ZNetScene.instance == null)
            {
                report.Check(false, "kept-rock check could name a deposit");
                yield break;
            }

            report.Check(LoadAllowlist.Contains(deposit.GetStableHashCode()),
                "deposits are loaded in a zone kept open for a villager, so mining works off-screen",
                $"deposit={deposit}");

            report.Check(!LoadAllowlist.Contains("not_a_real_prefab".GetStableHashCode()),
                "control: the allowlist still excludes what a colony has no use for");

            yield break;
        }

        /// <summary>
        ///     A pickaxe leaves the hand when the villager stops mining.
        /// </summary>
        /// <remarks>
        ///     The slot is ZDO-backed and nothing else writes it, so a tool left in a hand stays
        ///     there for the rest of the world. Mining made this reachable a second way: before
        ///     the hand learned what a pickaxe is, one would have read as the player's own
        ///     choice and been left alone for ever.
        /// </remarks>
        private static IEnumerator CheckThePickaxeIsPutAway(TestReport report, Colony colony)
        {
            Villager villager = VillagerLifecycle.Spawn(colony);
            yield return null;

            if (villager == null || !villager.TryGetComponent(out ZNetView view) || !view.IsValid())
            {
                report.Check(false, "control: the pickaxe check could spawn a villager");
                yield break;
            }

            VisEquipment dressed = villager.GetComponentInChildren<VisEquipment>(true);
            ItemDrop.ItemData pick = AnyPickaxe();

            if (dressed == null || pick == null)
            {
                report.Check(false, "control: the pickaxe check could find a villager to dress and a pickaxe",
                    $"dressed={(dressed != null)} pickaxe={(pick != null)}");
                VillagerLifecycle.Remove(colony, view.GetZDO().m_uid);
                yield break;
            }

            VillagerWardrobe.Set(dressed, WearSlot.RightHand, pick);

            // Read with no wait, as the axe check does and for the reason it records: an idle
            // villager's own tick bares the hand every frame, so anything yielded here takes
            // the pickaxe back before the control can see it - and the assertion below would
            // then pass because nothing was ever there. This check was written with a yield and
            // the control caught it on the first run, which is what a control is for.
            report.Check(VillagerWardrobe.Worn(view.GetZDO(), WearSlot.RightHand) != 0,
                "control: the villager really is holding a pickaxe before this is asked",
                $"worn={VillagerWardrobe.Worn(view.GetZDO(), WearSlot.RightHand)}");

            VillagerTool.PutAway(dressed, view.GetZDO());

            report.Check(VillagerWardrobe.Worn(view.GetZDO(), WearSlot.RightHand) == 0,
                "a pickaxe is taken out of the hand when the villager is not mining",
                $"worn={VillagerWardrobe.Worn(view.GetZDO(), WearSlot.RightHand)}");

            VillagerLifecycle.Remove(colony, view.GetZDO().m_uid);
            yield return null;
        }

        /// <summary>
        ///     A real pickaxe, ready to be worn.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Cloned with its prefab attached. Item data taken straight off a prefab has no
        ///         <c>m_dropPrefab</c> - the field is filled in when an item passes through an
        ///         inventory - and the wardrobe hashes exactly that, so handing it the bare
        ///         prefab data writes hash zero, the hand stays empty, and the check that
        ///         follows passes because nothing was ever there. This suite has recorded that
        ///         trap once already, a few hundred lines down.
        ///     </para>
        ///     <para>
        ///         And filtered by skill, not by damage. Pickaxe damage alone is not a pickaxe -
        ///         the lesson <see cref="FindAxe" /> carries, where the first chop-damaging item
        ///         in the database turned out to be a creature's attack and every villager the
        ///         suite armed was carrying one.
        ///     </para>
        /// </remarks>
        private static ItemDrop.ItemData AnyPickaxe() => FindPickaxe(0, 99);

        /// <summary>A real pickaxe within a tier range, ready to be carried or worn.</summary>
        private static ItemDrop.ItemData FindPickaxe(int lowestTier, int highestTier)
        {
            if (ObjectDB.instance?.m_items == null) return null;

            foreach (GameObject prefab in ObjectDB.instance.m_items)
            {
                if (prefab == null || !prefab.TryGetComponent(out ItemDrop drop)) continue;

                ItemDrop.ItemData.SharedData shared = drop.m_itemData?.m_shared;
                if (shared == null || shared.m_damages.m_pickaxe <= 0f) continue;
                if (shared.m_skillType != Skills.SkillType.Pickaxes) continue;
                if (shared.m_toolTier < lowestTier || shared.m_toolTier > highestTier) continue;

                ItemDrop.ItemData worn = drop.m_itemData.Clone();
                worn.m_dropPrefab = prefab;
                return worn;
            }

            return null;
        }

        /// <summary>How long a villager is given to break some of a deposit.</summary>
        private const float MineSeconds = 120f;

        /// <summary>How long a villager is given to find something and pick it.</summary>
        /// <remarks>
        ///     Shorter than mining's, because one reach finishes a bush where a vein takes forty
        ///     blows. What this has to cover is the walk, not the work.
        /// </remarks>
        private const float ForageSeconds = 90f;

        /// <summary>
        ///     A villager chooses a deposit, walks to it, and takes it apart.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>The one check in which the mining job actually runs.</b> Everything else
        ///         here strikes the rock through the protocol or asks a predicate directly, which
        ///         means the engine - choosing, claiming, walking, the arrival latch, the blow
        ///         cadence, the hand - was never executed by anything. <c>MineJob.Tick</c> could
        ///         have failed on its first line and every other check would have passed.
        ///     </para>
        ///     <para>
        ///         <b>Three rocks, and two of them are controls.</b> One the villager should
        ///         take; one nearer but too hard for its pickaxe, which catches a tier gate that
        ///         stopped working; one outside the work area, which proves both that deposits do
        ///         not come apart on their own and that the area bounds the job.
        ///     </para>
        ///     <para>
        ///         The subject is disowned before the villager is released, because that is the
        ///         state every world-generated rock is in - and damage routed to nobody is
        ///         absorbed silently, which is the failure chopping calls the worst in the job.
        ///     </para>
        /// </remarks>
        /// <summary>
        ///     Everything mining, in the order a failure is most useful in.
        /// </summary>
        /// <remarks>
        ///     Shared by the slice and by the acceptance run rather than listed in both. The
        ///     chop block is listed twice and has stayed in step by luck; the newer jobs were
        ///     slice-only, which meant the command that gates a release asserted nothing about
        ///     them at all.
        ///
        ///     Index first, because every check below it is meaningless if the classifier is
        ///     empty - and an empty classifier makes them all pass by finding nothing to
        ///     contradict.
        /// </remarks>
        private static IEnumerator Mining(TestReport report, Colony colony, Vector3 origin)
        {
            yield return CheckMiningIndex(report);
            yield return CheckRockIsKeptLoaded(report);
            yield return CheckThePickaxeIsPutAway(report, colony);
            yield return CheckAVillagerFetchesItsOwnTool(report, colony, origin);
            yield return CheckMineSettings(report);
            yield return CheckMineStoppingRules(report, colony, origin);
            yield return CheckAVeinIsMinedPartByPart(report, colony, origin);
            yield return CheckMiningBreaksADeposit(report, colony, origin);
        }

        /// <summary>
        ///     A villager with an empty bag goes and gets a pickaxe out of a chest.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>The errand is a rule, not a job</b>, so nothing queues it and nothing on
        ///         any screen shows it running. That makes it exactly the kind of behaviour that
        ///         can rot unnoticed: both tool-holding jobs already yield politely when there is
        ///         no tool, so an errand that silently stopped working would look like a
        ///         settlement with no pickaxes rather than like a bug.
        ///     </para>
        ///     <para>
        ///         <b>Written as an if-and-only-if</b>, in that order. The chest is first marked
        ///         as one villagers may not take from and the bag must stay empty - which is the
        ///         arm that fails if the errand simply raids every container it can see, and it
        ///         is the arm a player cares about, because a chest switched off is where
        ///         somebody keeps their own gear. Only then is the setting flipped, with the same
        ///         chest and the same villager, so the second arm cannot pass by accident.
        ///     </para>
        ///     <para>
        ///         No deposit is placed. The errand runs before the state machine is consulted,
        ///         so a rock would only add a way for this to fail for an unrelated reason.
        ///     </para>
        /// </remarks>
        private static IEnumerator CheckAVillagerFetchesItsOwnTool(TestReport report, Colony colony,
            Vector3 origin)
        {
            SweepLooseItems(colony);
            SettlementIndex.ResetForTest();

            // Beside the Kolony, not out at the mining site. Registration is bounded by the
            // hearth's reach and the claimed flags' circles, so a chest eighty metres out comes
            // back OutOfReach - which is what this check's own control caught on its first run
            // in game, and is the reason the control is a control rather than an assumption.
            Vector3 site = origin + new Vector3(-11f, 0f, -4f);

            // Far enough that the walk is a walk. At arm's length the villager would already be
            // standing at the chest and the errand would never have to path anywhere.
            GameObject chest = Spawn("piece_chest_wood", site + new Vector3(8f, 0f, 0f));
            yield return new WaitForSecondsRealtime(.3f);

            StructureRecord shed = Register(colony, chest, "Tool shed");
            Container store = chest != null ? chest.GetComponentInChildren<Container>(true) : null;
            ItemDrop.ItemData spare = AnyPickaxe();

            if (shed == null || store == null || spare == null)
            {
                report.Check(false, "control: the tool errand check could stock a registered chest",
                    $"registered={(shed != null)} container={(store != null)} pickaxe={(spare != null)} " +
                    $"fromHearth={(chest == null ? -1f : Utils.DistanceXZ(chest.transform.position, colony.transform.position)):0}m");
                Release(chest);
                yield break;
            }

            Clear(store.GetInventory());
            store.GetInventory().AddItem(spare);

            // Off first. This is the arm that matters most, and putting it second would let a
            // villager that had already fetched one pass it holding the earlier pickaxe.
            ColonyOperations.EditSettings(colony, shed.Id, settings => settings.MayTakeFrom = false);
            SettlementIndex.ResetForTest();

            colony.State.SetJobs(new List<JobDefinition>
            {
                new JobDefinition { Id = "errand", Name = "Mine", Kind = JobKind.Mine, Repeat = 30 }
            });

            Villager miner = VillagerLifecycle.Spawn(colony);
            yield return new WaitForSecondsRealtime(.4f);

            if (miner == null || !miner.TryGetComponent(out ZNetView view) || !view.IsValid())
            {
                report.Check(false, "control: the tool errand check could spawn a villager");
                colony.RemoveStructure(shed.Id);
                Release(chest);
                colony.State.SetJobs(new List<JobDefinition>());
                yield break;
            }

            ZDOID who = view.GetZDO().m_uid;
            SendRested(view);

            Container bag = VillagerInventory.Attach(miner.gameObject, view);
            Clear(bag.GetInventory());
            VillagerInventory.Persist(bag, view);

            new VillagerState(view.GetZDO()).SetQueue(new List<string> { "errand" });
            miner.transform.position = site;

            report.Check(VillagerTool.Best(bag.GetInventory(), ToolKind.Pickaxe) == null,
                "control: the villager starts with nothing in its bag");

            // Long enough to walk four metres several times over, so "it did not go" is the
            // reading rather than "it had not got there yet".
            for (float waited = 0f; waited < 10f; waited += .5f)
            {
                yield return new WaitForSecondsRealtime(.5f);
            }

            bool raided = miner != null && VillagerTool.Best(bag.GetInventory(), ToolKind.Pickaxe) != null;

            report.Check(!raided,
                "a chest marked as one villagers may not take from keeps its tools",
                $"tookIt={raided} doing='{(miner != null ? miner.Activity : "gone")}' " +
                $"inChest={Count(store.GetInventory(), spare.m_dropPrefab != null ? spare.m_dropPrefab.name : string.Empty)}");

            ColonyOperations.EditSettings(colony, shed.Id, settings => settings.MayTakeFrom = true);
            SettlementIndex.ResetForTest();

            bool fetched = false;
            float elapsed = 0f;

            while (elapsed < 45f && !fetched)
            {
                yield return new WaitForSecondsRealtime(.5f);
                elapsed += .5f;

                if (miner == null || !view.IsValid()) break;
                fetched = VillagerTool.Best(bag.GetInventory(), ToolKind.Pickaxe) != null;
            }

            report.Check(fetched,
                "and once it may, a villager with no pickaxe walks to the chest and takes one",
                $"after={elapsed:0}s doing='{(miner != null ? miner.Activity : "gone")}' " +
                $"leftInChest={(store != null ? store.GetInventory().NrOfItems() : -1)}");

            VillagerLifecycle.Remove(colony, who);
            colony.RemoveStructure(shed.Id);
            Release(chest);
            colony.State.SetJobs(new List<JobDefinition>());
            SweepLooseItems(colony);
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>
        ///     Everything foraging, in the order a failure is most useful in.
        /// </summary>
        /// <remarks>
        ///     Shared by the slice and by the acceptance run rather than listed in both, as
        ///     mining's block is. Index first, because every check below it is meaningless if the
        ///     classifier is empty - and an empty classifier makes them all pass by finding
        ///     nothing to contradict.
        /// </remarks>
        private static IEnumerator Foraging(TestReport report, Colony colony, Vector3 origin)
        {
            yield return CheckForagingIndex(report);
            yield return CheckWhatGrowsIsKeptLoaded(report);
            yield return CheckForageSettings(report);
            yield return CheckForageStoppingRules(report, colony, origin);
            yield return CheckForagingPicksABush(report, colony, origin);
        }

        /// <summary>
        ///     The foraging classifier knows what this world holds, and can name it.
        /// </summary>
        /// <remarks>
        ///     Asked of the index rather than written down, for the reason every list here is:
        ///     the mod does not ship the assets and has been wrong about a prefab name before.
        ///     The harvest list is the interesting half - it is read from the pickables
        ///     themselves, so a world where it came back empty would mean the job's picker
        ///     offers nothing and every check below passes by finding nothing.
        /// </remarks>
        private static IEnumerator CheckForagingIndex(TestReport report)
        {
            if (!Forageable.IsReady) Forageable.Rebuild();

            report.Check(Forageable.IsReady, "the foraging classifier found prefabs to classify");

            string regrows = Forageable.Sample(ForageKind.Regrows);
            string once = Forageable.Sample(ForageKind.Once);

            report.Check(!string.IsNullOrEmpty(regrows),
                "it can name something that grows back, so checks need not guess at prefab names",
                $"sample={regrows}");

            // The pair a "leave what does not grow back" check depends on. A world with only one
            // kind cannot stage that check and should say so rather than pass.
            report.Check(!string.IsNullOrEmpty(once),
                "control: and it can tell that from something picked once and gone",
                $"regrows={regrows} once={once}");

            List<string> gathered = new List<string>();
            Forageable.Harvest(gathered);
            report.Check(gathered.Count > 0,
                "the harvest picker has real things to offer, read off the pickables themselves",
                $"items={gathered.Count}");

            // What a bush yields, asked of the one that was named. An index that classified
            // everything and knew what nothing dropped would satisfy every line above.
            List<string> fromOne = Forageable.Yields(regrows.GetStableHashCode());
            report.Check(fromOne.Count > 0,
                "control: and it knows what a named bush gives up, which is what the filter reads",
                $"{regrows} -> {string.Join(",", fromOne.ToArray())}");

            yield break;
        }

        private static IEnumerator CheckWhatGrowsIsKeptLoaded(TestReport report)
        {
            if (!LoadAllowlist.IsReady) LoadAllowlist.Rebuild();
            if (!Forageable.IsReady) Forageable.Rebuild();

            string bush = Forageable.Sample(ForageKind.Regrows);
            if (string.IsNullOrEmpty(bush) || ZNetScene.instance == null)
            {
                report.Check(false, "kept-bush check could name something pickable");
                yield break;
            }

            report.Check(LoadAllowlist.Contains(bush.GetStableHashCode()),
                "what grows is loaded in a zone kept open for a villager, so foraging works off-screen",
                $"bush={bush}");

            report.Check(!LoadAllowlist.Contains("not_a_real_prefab".GetStableHashCode()),
                "control: the allowlist still excludes what a colony has no use for");

            yield break;
        }

        /// <summary>
        ///     The two settings a foraging job has, asked of the job rather than reimplemented.
        /// </summary>
        /// <remarks>
        ///     Through <see cref="ForageJob.WouldTake" />, which is the predicate the engine
        ///     itself uses. A check that wrote the rule out again would pass while the job
        ///     quietly ignored the setting, which is the failure this codebase has already paid
        ///     for once.
        /// </remarks>
        private static IEnumerator CheckForageSettings(TestReport report)
        {
            if (!Forageable.IsReady) Forageable.Rebuild();

            string regrows = Forageable.Sample(ForageKind.Regrows);
            string once = Forageable.Sample(ForageKind.Once);

            if (string.IsNullOrEmpty(regrows))
            {
                report.Check(false, "control: the settings check could name something pickable");
                yield break;
            }

            int hash = regrows.GetStableHashCode();
            List<string> gives = Forageable.Yields(hash);

            JobDefinition any = new JobDefinition { Id = "any", Kind = JobKind.Forage };
            report.Check(ForageJob.WouldTake(any, hash),
                "control: a foraging job with nothing named gathers anything",
                $"bush={regrows} gives={string.Join(",", gives.ToArray())}");

            // Drawn from the world rather than invented: something no bush of this kind gives.
            List<string> everything = new List<string>();
            Forageable.Harvest(everything);
            string elsewhere = everything.Find(item => !gives.Contains(item));

            if (string.IsNullOrEmpty(elsewhere))
            {
                report.Check(true, "harvest filter: this world offers one thing, so the narrowing did not run",
                    $"items={everything.Count}");
            }
            else
            {
                JobDefinition narrowed = new JobDefinition
                {
                    Id = "narrow", Kind = JobKind.Forage,
                    Harvest = new List<string> { elsewhere }
                };

                report.Check(!ForageJob.WouldTake(narrowed, hash),
                    "a job set to one thing leaves alone what does not give it",
                    $"asked={elsewhere} bush={regrows}");

                if (gives.Count > 0)
                {
                    JobDefinition raised = new JobDefinition
                    {
                        Id = "raised", Kind = JobKind.Forage,
                        Harvest = new List<string> { gives[0] }
                    };

                    report.Check(ForageJob.WouldTake(raised, hash),
                        "control: and takes it once what it gives is asked for",
                        $"asked={gives[0]} bush={regrows}");
                }
            }

            // The conservation switch, both ways round. Stated as an if-and-only-if because a
            // switch that refused everything and one that refused nothing look identical from
            // either half alone.
            if (string.IsNullOrEmpty(once))
            {
                report.Check(true, "regrowing filter: this world has nothing that is picked once");
            }
            else
            {
                JobDefinition careful = new JobDefinition
                    { Id = "careful", Kind = JobKind.Forage, ForageRegrowingOnly = true };

                report.Check(!ForageJob.WouldTake(careful, once.GetStableHashCode()),
                    "told to leave what does not grow back, a job leaves it",
                    $"once={once}");

                report.Check(ForageJob.WouldTake(careful, hash),
                    "control: and still takes what does grow back",
                    $"regrows={regrows}");

                JobDefinition ordinary = new JobDefinition { Id = "ordinary", Kind = JobKind.Forage };
                report.Check(ForageJob.WouldTake(ordinary, once.GetStableHashCode()),
                    "control: and without the switch it takes that too, so a farm is harvested",
                    $"once={once}");
            }

            yield break;
        }

        private static IEnumerator CheckForageStoppingRules(TestReport report, Colony colony,
            Vector3 origin)
        {
            if (!Forageable.IsReady) Forageable.Rebuild();

            List<string> gathered = new List<string>();
            Forageable.Harvest(gathered);

            if (gathered.Count == 0)
            {
                report.Check(false, "control: the stopping check could name something this world grows");
                yield break;
            }

            string item = gathered[0];
            GameObject chest = Spawn("piece_chest_wood", origin + new Vector3(-6f, 0f, 12f));
            yield return new WaitForSecondsRealtime(.4f);

            StructureRecord store = Register(colony, chest, "Larder");
            Container box = chest != null ? chest.GetComponentInChildren<Container>(true) : null;

            if (store == null || box == null)
            {
                report.Check(false, "control: the stopping check could register a chest");
                Release(chest);
                yield break;
            }

            JobDefinition job = new JobDefinition
                { Id = "patch", Kind = JobKind.Forage, StockItem = item, StockTarget = 10 };

            Stock.ResetForTest();
            report.Check(!ForageJob.WouldStop(colony, job),
                "control: an empty settlement has not got enough of anything",
                $"item={item} held={Stock.Held(colony, item)}");

            int put = PutIn(box, item, 12);
            Stock.ResetForTest();

            report.Check(put > 0 && Stock.Held(colony, item) >= 12,
                "control: the settlement can say how much of it is holding",
                $"put={put} held={Stock.Held(colony, item)}");

            report.Check(ForageJob.WouldStop(colony, job),
                "at its target a foraging job stops asking for more",
                $"held={Stock.Held(colony, item)} target=10");

            job.StockTarget = 500;
            report.Check(!ForageJob.WouldStop(colony, job),
                "control: below it the same job carries on", "target=500");

            job.StockTarget = 0;
            report.Check(!ForageJob.WouldStop(colony, job),
                "control: a target of zero means never stop");

            colony.RemoveStructure(store.Id);
            Release(chest);
            Stock.ResetForTest();
            SweepLooseItems(colony);
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>
        ///     A villager finds a bush, picks it, and stops - and the one outside its work area
        ///     keeps its berries.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The behaviour unique to this job is in the ending. Everywhere else finishing
        ///         means the thing is gone; here the bush is still standing and the only
        ///         difference is a flag on its record. So the two assertions that matter are that
        ///         it went bare, and that the villager then <em>stopped</em> - a job that could
        ///         not tell a picked bush from a full one would stand in front of it reaching for
        ///         ever, and every other assertion here would still pass.
        ///     </para>
        ///     <para>
        ///         The subject is disowned before the villager is released, because that is the
        ///         state every world-generated bush is in - and an RPC routed to nobody is
        ///         absorbed silently, which is the failure the mining and chopping jobs both
        ///         found the hard way.
        ///     </para>
        /// </remarks>
        private static IEnumerator CheckForagingPicksABush(TestReport report, Colony colony,
            Vector3 origin)
        {
            Vector3 site = ForagingSite(origin);
            SweepDrops(site, 40f);
            ForageJob.Clear();
            ForagingGround.ResetForTest();
            if (!Forageable.IsReady) Forageable.Rebuild();

            string named = Forageable.Sample(ForageKind.Regrows);
            if (string.IsNullOrEmpty(named)) named = Forageable.Sample(ForageKind.Once);

            if (string.IsNullOrEmpty(named))
            {
                report.Check(false, "control: the foraging check could name something to pick");
                yield break;
            }

            GameObject flag = Spawn(WorkFlagPrefab.PrefabName, site);
            yield return new WaitForSecondsRealtime(.3f);

            WorkFlag planted = flag != null ? flag.GetComponent<WorkFlag>() : null;
            if (planted == null || ColonyOperations.AssignFlag(colony.Id, planted) != RegisterOutcome.Registered)
            {
                report.Check(false, "control: the foraging check could plant and claim a flag");
                Release(flag);
                yield break;
            }

            StructureRecord flagRecord = colony.State.GetStructures().Find(r => r.Id == planted.Id);

            GameObject subject = Spawn(named, site + new Vector3(8f, 0f, 0f));
            GameObject outside = Spawn(named, site + new Vector3(26f, 0f, 0f));
            yield return new WaitForSecondsRealtime(.5f);

            if (subject == null || outside == null ||
                !Harvest.TryFind(subject, out Pickable bush) ||
                !Harvest.TryFind(outside, out Pickable spare))
            {
                report.Check(false, "control: the foraging check could place what it picks",
                    $"named={named} subject={(subject != null)} outside={(outside != null)}");
                Cleanup(colony, planted, null, flag, null);
                Release(subject);
                Release(outside);
                yield break;
            }

            report.Check(Harvest.Ripe(bush) && Harvest.Ripe(spare),
                "control: both have something on them before anybody touches them",
                $"subject={Harvest.Ripe(bush)} outside={Harvest.Ripe(spare)}");

            colony.State.SetJobs(new List<JobDefinition>
            {
                new JobDefinition
                {
                    Id = "pick", Name = "Forage", Kind = JobKind.Forage, Repeat = 30,
                    Areas = new List<string> { flagRecord?.PersistentId ?? string.Empty },
                    WorkRadius = 14f
                }
            });

            Villager picker = VillagerLifecycle.Spawn(colony);
            yield return new WaitForSecondsRealtime(.4f);

            if (picker == null || !picker.TryGetComponent(out ZNetView view) || !view.IsValid())
            {
                report.Check(false, "control: the foraging check could spawn a villager");
                Cleanup(colony, planted, null, flag, null);
                Release(subject);
                Release(outside);
                colony.State.SetJobs(new List<JobDefinition>());
                yield break;
            }

            ZDOID who = view.GetZDO().m_uid;
            SendRested(view);

            new VillagerState(view.GetZDO()).SetQueue(new List<string> { "pick" });
            picker.transform.position = site + new Vector3(2f, 0f, 2f);

            // Unowned, as every bush the world generated is.
            if (subject.TryGetComponent(out ZNetView subjectView) && subjectView.IsValid())
            {
                subjectView.GetZDO().SetOwner(0L);
            }

            ForagingGround.ResetForTest();

            bool everHeldTheSubject = false;
            bool bare = false;
            float elapsed = 0f;
            float wentBareAt = 0f;

            while (elapsed < ForageSeconds && (!bare || elapsed < wentBareAt + 6f))
            {
                yield return new WaitForSecondsRealtime(.2f);
                elapsed += .2f;

                if (picker == null || !view.IsValid()) break;

                VillagerState state = new VillagerState(view.GetZDO());
                if (subjectView != null && subjectView.IsValid() && state.Target == subjectView.GetZDO().m_uid)
                {
                    everHeldTheSubject = true;
                }

                bool ripe = subject != null && Harvest.TryFind(subject, out Pickable live) && Harvest.Ripe(live);
                if (!ripe && !bare)
                {
                    bare = true;
                    wentBareAt = elapsed;
                }
            }

            report.Check(everHeldTheSubject,
                "a villager chose something to pick itself - nothing here pointed it at one",
                $"held={everHeldTheSubject} after {elapsed:0}s doing='{(picker != null ? picker.Activity : "gone")}'");

            report.Check(bare,
                "and it went bare under it, with this check picking nothing",
                $"bare={bare} in {elapsed:0}s doing='{(picker != null ? picker.Activity : "gone")}'");

            // The ending this job has and no other does. A bush that is picked is still a bush,
            // so a job that could not tell would go on reaching at it - and every assertion
            // above would still pass while it did.
            if (bare && picker != null && view.IsValid())
            {
                ZDOID stillHeld = new VillagerState(view.GetZDO()).Target;
                ZDOID subjectId = subjectView != null && subjectView.IsValid()
                    ? subjectView.GetZDO().m_uid
                    : ZDOID.None;

                report.Check(stillHeld.IsNone() || stillHeld != subjectId,
                    "and it let the bush go once there was nothing on it, rather than reaching for ever",
                    $"held={stillHeld} subject={subjectId} doing='{picker.Activity}' " +
                    $"{elapsed - wentBareAt:0}s after it went bare");
            }

            bool outsideStillRipe = outside != null && Harvest.TryFind(outside, out Pickable still) &&
                                    Harvest.Ripe(still);

            report.Check(outsideStillRipe,
                "control: the one outside the work area still has everything on it",
                $"ripe={outsideStillRipe}");

            VillagerLifecycle.Remove(colony, who);
            Cleanup(colony, planted, null, flag, null);
            Release(subject);
            Release(outside);
            SweepDrops(site, 40f);
            colony.State.SetJobs(new List<JobDefinition>());
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>Somewhere to forage, away from the chopping and mining sites.</summary>
        private static Vector3 ForagingSite(Vector3 origin)
        {
            Vector3 site = ChoppingSite(origin) + new Vector3(-80f, 0f, 0f);
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(site, out float ground))
            {
                site.y = ground;
            }

            return site;
        }

        private static IEnumerator CheckMiningBreaksADeposit(TestReport report, Colony colony,
            Vector3 origin)
        {
            Vector3 site = MiningSite(origin);
            SweepDrops(site, 40f);
            MineJob.Clear();
            MiningGround.ResetForTest();
            if (!Mineable.IsReady) Mineable.Rebuild();

            string soft = Mineable.SampleDeposit(0, 0);
            if (string.IsNullOrEmpty(soft)) soft = Mineable.SampleDeposit(0, 99);

            if (string.IsNullOrEmpty(soft))
            {
                report.Check(false, "control: the mining check could name a deposit to break");
                yield break;
            }

            GameObject flag = Spawn(WorkFlagPrefab.PrefabName, site);
            yield return new WaitForSecondsRealtime(.3f);

            WorkFlag planted = flag != null ? flag.GetComponent<WorkFlag>() : null;
            if (planted == null || ColonyOperations.AssignFlag(colony.Id, planted) != RegisterOutcome.Registered)
            {
                report.Check(false, "control: the mining check could plant and claim a flag");
                Release(flag);
                yield break;
            }

            StructureRecord flagRecord = colony.State.GetStructures().Find(r => r.Id == planted.Id);

            GameObject subject = Spawn(soft, site + new Vector3(8f, 0f, 0f));
            GameObject outside = Spawn(soft, site + new Vector3(26f, 0f, 0f));

            // Nearer than the subject on purpose: without a tier gate, nearest-wins takes it.
            string hard = Mineable.SampleDeposit(2, 99);
            GameObject tough = string.IsNullOrEmpty(hard)
                ? null
                : Spawn(hard, site + new Vector3(4f, 0f, 4f));

            yield return new WaitForSecondsRealtime(.5f);

            if (subject == null || outside == null ||
                !MineProbe.TryFind(subject, out MineProtocol rock) ||
                !MineProbe.TryFind(outside, out MineProtocol spare))
            {
                report.Check(false, "control: the mining check could place its deposits",
                    $"deposit={soft} subject={(subject != null)} outside={(outside != null)}");
                Cleanup(colony, planted, null, flag, null);
                Release(subject);
                Release(outside);
                Release(tough);
                yield break;
            }

            List<MineArea> parts = new List<MineArea>();
            rock.Areas(parts);
            int whole = parts.Count;

            List<MineArea> untouched = new List<MineArea>();
            spare.Areas(untouched);
            int away = untouched.Count;

            report.Check(whole > 0, "control: there is a deposit with parts to work",
                $"deposit={soft} parts={whole} protocol={rock.GetType().Name}");

            colony.State.SetJobs(new List<JobDefinition>
            {
                new JobDefinition
                {
                    Id = "mine", Name = "Mine", Kind = JobKind.Mine, Repeat = 30,
                    Areas = new List<string> { flagRecord?.PersistentId ?? string.Empty },
                    WorkRadius = 14f
                }
            });

            Villager miner = VillagerLifecycle.Spawn(colony);
            yield return new WaitForSecondsRealtime(.4f);

            if (miner == null || !miner.TryGetComponent(out ZNetView view) || !view.IsValid())
            {
                report.Check(false, "control: the mining check could spawn a villager");
                Cleanup(colony, planted, null, flag, null);
                Release(subject);
                Release(outside);
                Release(tough);
                colony.State.SetJobs(new List<JobDefinition>());
                yield break;
            }

            ZDOID who = view.GetZDO().m_uid;
            SendRested(view);

            Container bag = VillagerInventory.Attach(miner.gameObject, view);

            // Tier zero only. That is what makes the nearer deposit unbreakable, and so what
            // makes the tier assertion below mean anything.
            bool armed = GivePickaxe(bag, view, 0, 0);
            report.Check(armed, "control: the villager has a plain pickaxe, without which this does nothing");

            new VillagerState(view.GetZDO()).SetQueue(new List<string> { "mine" });
            miner.transform.position = site + new Vector3(2f, 0f, 2f);

            // Unowned, as every rock the world generated is.
            if (subject.TryGetComponent(out ZNetView subjectView) && subjectView.IsValid())
            {
                subjectView.GetZDO().SetOwner(0L);
            }

            MiningGround.ResetForTest();

            ZDOID toughId = tough != null && tough.TryGetComponent(out ZNetView toughView) && toughView.IsValid()
                ? toughView.GetZDO().m_uid
                : ZDOID.None;

            bool everHeldTheHardOne = false;
            bool everHeldTheSubject = false;
            bool everHeldThePickaxe = false;
            bool photographed = false;
            double claimAtFirst = 0d;
            double claimAtLast = 0d;
            float workedFor = 0f;
            int left = whole;
            float elapsed = 0f;

            // Long enough to outlive a claim, because "the claim is refreshed while mining" is
            // the assertion that needs it and thirty seconds is the life of one.
            float atLeast = ModConfig.ClaimTtlSeconds != null ? ModConfig.ClaimTtlSeconds.Value + 8f : 38f;

            while (elapsed < MineSeconds && (left > 0 || elapsed < atLeast))
            {
                yield return new WaitForSecondsRealtime(.2f);
                elapsed += .2f;

                if (miner == null || !view.IsValid()) break;

                VillagerState state = new VillagerState(view.GetZDO());
                if (!toughId.IsNone() && state.Target == toughId) everHeldTheHardOne = true;

                if (subjectView != null && subjectView.IsValid() && state.Target == subjectView.GetZDO().m_uid)
                {
                    everHeldTheSubject = true;

                    if (claimAtFirst <= 0d) claimAtFirst = state.ClaimedSince;
                    claimAtLast = state.ClaimedSince;
                    workedFor = elapsed;
                }

                if (VillagerWardrobe.Worn(view.GetZDO(), WearSlot.RightHand) != 0) everHeldThePickaxe = true;

                parts.Clear();
                if (subject != null && MineProbe.TryFind(subject, out MineProtocol live)) live.Areas(parts);
                left = parts.Count;

                if (left < whole && !photographed)
                {
                    photographed = true;
                    yield return BenchmarkUiScenario.PhotographAtWork("mine-at-work.png",
                        miner.transform.position, "a villager mining, with the deposit part-broken",
                        subject.transform.position);
                }
            }

            report.Check(everHeldTheSubject,
                "a villager chose a deposit itself - nothing here pointed it at one",
                $"held={everHeldTheSubject} after {elapsed:0}s energy={new VillagerState(view.GetZDO()).StoredEnergy:0}");

            report.Check(left < whole,
                "and the deposit came apart under it, with this check striking no blow",
                $"parts {whole} -> {left} in {elapsed:0}s did='{(miner != null ? miner.Activity : "gone")}'");

            untouched.Clear();
            if (outside != null && MineProbe.TryFind(outside, out MineProtocol still)) still.Areas(untouched);

            report.Check(untouched.Count == away,
                "control: the deposit outside the work area still has every part",
                $"outside {away} -> {untouched.Count}");

            report.Check(everHeldThePickaxe,
                "control: it had the pickaxe in its hand while it worked");

            if (!toughId.IsNone())
            {
                report.Check(!everHeldTheHardOne,
                    "a plain pickaxe never went for the rock it could not break, though it was nearer",
                    $"hard={hard} tier={Mineable.TierOf(ZNetScene.instance.GetPrefab(hard))}");
            }
            else
            {
                report.Check(true, "tier check: this world has no rock a plain pickaxe cannot break");
            }

            report.Check(workedFor >= atLeast - 2f,
                "control: it worked that deposit for longer than a claim lives",
                $"worked={workedFor:0}s ttl={(ModConfig.ClaimTtlSeconds != null ? ModConfig.ClaimTtlSeconds.Value : 30f):0}s");

            report.Check(claimAtLast > claimAtFirst,
                "and its claim was refreshed while it worked, so nobody can take a half-mined vein",
                $"claimed {claimAtFirst} -> {claimAtLast}");

            VillagerLifecycle.Remove(colony, who);
            Cleanup(colony, planted, null, flag, null);
            Release(subject);
            Release(outside);
            Release(tough);
            SweepDrops(site, 40f);
            colony.State.SetJobs(new List<JobDefinition>());
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>
        ///     What a mining job takes and what it leaves, asked of the job's own predicate.
        /// </summary>
        /// <remarks>
        ///     Through <c>WouldTake</c> rather than by reimplementing the rule, for the reason
        ///     chopping records: a check that writes the rule out again goes on passing while the
        ///     job quietly ignores the setting. Nothing is spawned for the loose-rock half -
        ///     what the classifier calls a boulder may be a crate or a barrel, and a fixture
        ///     whose content misleads the reader costs more than it proves.
        /// </remarks>
        private static IEnumerator CheckMineSettings(TestReport report)
        {
            if (!Mineable.IsReady) Mineable.Rebuild();

            string deposit = Mineable.SampleDeposit(0, 99);
            if (string.IsNullOrEmpty(deposit))
            {
                report.Check(false, "control: the settings check could name a deposit");
                yield break;
            }

            int hash = deposit.GetStableHashCode();
            List<string> drops = Mineable.Yields(hash);

            JobDefinition any = new JobDefinition { Id = "any", Kind = JobKind.Mine };
            report.Check(MineJob.WouldTake(any, hash),
                "control: a mining job with no ore named takes any deposit",
                $"deposit={deposit} drops={string.Join(",", drops.ToArray())}");

            // Drawn from the world rather than invented: an ore this deposit does not drop.
            List<string> ores = new List<string>();
            Mineable.Ores(ores);

            string elsewhere = ores.Find(ore => !drops.Contains(ore));
            if (string.IsNullOrEmpty(elsewhere))
            {
                report.Check(true, "ore filter: this world has one ore, so the narrowing did not run",
                    $"ores={ores.Count}");
            }
            else
            {
                JobDefinition other = new JobDefinition
                    { Id = "other", Kind = JobKind.Mine, Ores = new List<string> { elsewhere } };

                report.Check(!MineJob.WouldTake(other, hash),
                    "a job told to mine one ore leaves a deposit that does not drop it",
                    $"wanted={elsewhere} drops={string.Join(",", drops.ToArray())}");
            }

            if (drops.Count > 0)
            {
                JobDefinition mine = new JobDefinition
                    { Id = "mine", Kind = JobKind.Mine, Ores = new List<string> { drops[0] } };

                report.Check(MineJob.WouldTake(mine, hash),
                    "control: and takes the deposit that does drop it", $"wanted={drops[0]}");
            }

            string boulder = Mineable.SampleBoulder();
            if (string.IsNullOrEmpty(boulder))
            {
                report.Check(true, "loose rock: this world has none classified, so that did not run");
            }
            else
            {
                int loose = boulder.GetStableHashCode();

                report.Check(!MineJob.WouldTake(any, loose),
                    "a mining job leaves loose rock alone - it is opt-in", $"boulder={boulder}");

                JobDefinition breaking = new JobDefinition
                    { Id = "rock", Kind = JobKind.Mine, MineBoulders = true };

                report.Check(MineJob.WouldTake(breaking, loose),
                    "control: a job told to break loose rock takes it");

                // The asymmetry nobody would think to write down: an ore list narrows deposits
                // and says nothing about loose rock, which has a switch of its own.
                JobDefinition both = new JobDefinition
                {
                    Id = "both", Kind = JobKind.Mine, MineBoulders = true,
                    Ores = new List<string> { elsewhere ?? "nothing-drops-this" }
                };

                report.Check(MineJob.WouldTake(both, loose),
                    "an ore list narrows deposits only, so it does not exclude loose rock",
                    $"boulder={boulder}");
            }

            // The tier gate, asked where the job asks it: before anybody walks anywhere.
            string hard = Mineable.SampleDeposit(2, 99);
            if (string.IsNullOrEmpty(hard))
            {
                report.Check(true, "tier check: this world has no rock a plain pickaxe cannot break");
            }
            else
            {
                int tough = hard.GetStableHashCode();
                int needs = Mineable.TierOf(ZNetScene.instance.GetPrefab(hard));

                report.Check(!MineJob.WouldBreak(tough, 0),
                    "a pickaxe too weak for a deposit is refused before anybody walks to it",
                    $"deposit={hard} needs={needs} pick=0");

                report.Check(MineJob.WouldBreak(tough, needs),
                    "control: and the same deposit is taken with a pickaxe that can break it",
                    $"needs={needs}");
            }

            yield break;
        }

        /// <summary>
        ///     A mining job stops when the settlement has what it asked for.
        /// </summary>
        /// <remarks>
        ///     The terminus matters more here than anywhere else: a forest grows back and a vein
        ///     does not, so a rule that never fires strips a region permanently while every
        ///     individual decision is correct.
        /// </remarks>
        private static IEnumerator CheckMineStoppingRules(TestReport report, Colony colony, Vector3 origin)
        {
            if (!Mineable.IsReady) Mineable.Rebuild();

            List<string> ores = new List<string>();
            Mineable.Ores(ores);

            if (ores.Count == 0)
            {
                report.Check(false, "control: the stopping check could name an ore this world drops");
                yield break;
            }

            string ore = ores[0];
            GameObject chest = Spawn("piece_chest_wood", origin + new Vector3(-3f, 0f, 9f));
            yield return new WaitForSecondsRealtime(.4f);

            StructureRecord store = Register(colony, chest, "Ore store");
            Container box = chest != null ? chest.GetComponentInChildren<Container>(true) : null;

            if (store == null || box == null)
            {
                report.Check(false, "control: the stopping check could register a chest");
                Release(chest);
                yield break;
            }

            JobDefinition job = new JobDefinition
                { Id = "pit", Kind = JobKind.Mine, StockItem = ore, StockTarget = 10 };

            Stock.ResetForTest();
            report.Check(!MineJob.WouldStop(colony, job),
                "control: an empty settlement has not got enough of anything",
                $"ore={ore} held={Stock.Held(colony, ore)}");

            int put = PutIn(box, ore, 12);
            Stock.ResetForTest();

            report.Check(put > 0 && Stock.Held(colony, ore) >= 12,
                "control: the settlement can say how much ore it is holding",
                $"put={put} held={Stock.Held(colony, ore)}");

            report.Check(MineJob.WouldStop(colony, job),
                "at its target a mining job stops asking for more",
                $"held={Stock.Held(colony, ore)} target=10");

            job.StockTarget = 500;
            report.Check(!MineJob.WouldStop(colony, job),
                "control: below it the same job carries on", $"target=500");

            job.StockTarget = 0;
            report.Check(!MineJob.WouldStop(colony, job),
                "control: a target of zero means never stop");

            colony.RemoveStructure(store.Id);
            Release(chest);
            Stock.ResetForTest();
            SweepLooseItems(colony);
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>
        ///     The mining classifier knows what this world holds, and can name it.
        /// </summary>
        /// <remarks>
        ///     Asked of the index rather than written down, for the reason every list here is:
        ///     the mod does not ship the assets and has been wrong about a prefab name before.
        ///     The ore list is the interesting half - it is read from drop tables, so a world
        ///     where it came back empty would mean the job's picker offers nothing and the
        ///     failure would show up as "nothing to mine" with no explanation.
        /// </remarks>
        private static IEnumerator CheckMiningIndex(TestReport report)
        {
            if (!Mineable.IsReady) Mineable.Rebuild();

            report.Check(Mineable.IsReady, "the mining classifier found prefabs to classify");

            string anyDeposit = Mineable.SampleDeposit(0, 99);
            report.Check(!string.IsNullOrEmpty(anyDeposit),
                "it can name a real deposit, so checks need not guess at prefab names",
                $"sample={anyDeposit}");

            // The tiers a tool-tier check depends on. A world with only one tier of rock cannot
            // stage that check and should say so rather than pass.
            string soft = Mineable.SampleDeposit(0, 0);
            string hard = Mineable.SampleDeposit(2, 99);
            report.Check(!string.IsNullOrEmpty(soft) && !string.IsNullOrEmpty(hard),
                "control: it can tell rock any pickaxe breaks from rock that needs a good one",
                $"soft={soft} hard={hard}");

            List<string> ores = new List<string>();
            Mineable.Ores(ores);
            report.Check(ores.Count > 0,
                "the ore picker has real ores to offer, read from drop tables",
                $"ores={ores.Count}: {string.Join(",", ores.ToArray())}");

            yield break;
        }

        /// <summary>
        ///     A deposit is mined part by part, and what is left survives being unloaded.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The behaviour that is unique to this job. A tree is destroyed or it is not; a
        ///         deposit is destroyed one part at a time and spends its whole working life
        ///         partly mined - so the thing that can go wrong, and go wrong silently, is a
        ///         villager that cannot tell which parts are left.
        ///     </para>
        ///     <para>
        ///         Asserted on the parts rather than on the ore, because ore lands on the ground
        ///         and hauling is a separate job: counting drops would make this a check of two
        ///         things, and it would pass or fail for reasons that are not mining's.
        ///     </para>
        /// </remarks>
        private static IEnumerator CheckAVeinIsMinedPartByPart(TestReport report, Colony colony,
            Vector3 origin)
        {
            SweepLooseItems(colony);
            MiningGround.ResetForTest();

            if (!Mineable.IsReady) Mineable.Rebuild();

            // Whatever this world calls a deposit a plain pickaxe can break. Naming one would
            // be a fixture that keeps passing after the game renames it.
            string named = Mineable.SampleDeposit(0, 0);
            if (string.IsNullOrEmpty(named)) named = Mineable.SampleDeposit(0, 99);

            GameObject deposit = string.IsNullOrEmpty(named)
                ? null
                : Spawn(named, origin + new Vector3(12f, 0f, 12f));

            yield return new WaitForSecondsRealtime(.5f);

            if (deposit == null || !MineProbe.TryFind(deposit, out MineProtocol rock))
            {
                report.Check(false, "control: the mining check could place a deposit",
                    $"named='{named}' placed={(deposit != null)}");
                Release(deposit);
                yield break;
            }

            List<MineArea> parts = new List<MineArea>();
            rock.Areas(parts);
            int whole = parts.Count;

            report.Check(whole > 0, "a deposit offers parts to work", $"parts={whole}");

            // Struck directly rather than through a villager: what is under test is that the
            // protocol reads the rock correctly, and a villager walking to it would make this a
            // check of pathing as well.
            ZNetView view = deposit.GetComponent<ZNetView>();
            if (view != null) view.ClaimOwnership();

            // One blow, and then the count. "Takes parts off one at a time" was asserted as
            // `left < whole`, which passes just as well for a protocol that removes every part
            // at once - and that is the failure worth catching, because it is what aiming at
            // the wrong thing looks like.
            //
            // Not asserted as *exactly* one, though. A deposit kills its own unsupported parts
            // when their footing goes, several at a time, so a blow at the base legitimately
            // drops more than it struck - and a check that demanded one would fail on the
            // game working correctly.
            HitData single = new HitData { m_toolTier = 100, m_point = parts[0].At };
            single.m_damage.m_pickaxe = 5000f;

            BlowResult first = rock.Strike(parts[0], single, out string said);
            yield return null;

            parts.Clear();
            rock.Areas(parts);
            int afterOne = parts.Count;

            report.Check(first == BlowResult.Struck || first == BlowResult.Felled,
                "control: a blow with an overwhelming pickaxe lands",
                $"blow={first} said='{said}'");

            report.Check(afterOne < whole && afterOne > 0,
                "and it takes the deposit apart rather than all at once",
                $"parts {whole} -> {afterOne}");

            // What the record says, which is the claim this check exists for. Read before the
            // rest of the blows so the comparison afterwards means something.
            float recorded = rock.Remaining();

            // Deliberately short of finishing it. These assertions used to sit behind an
            // `if (left > 0)` while the loop above mined the rock to nothing, so on any deposit
            // small enough to exhaust they were skipped in silence.
            for (int blow = 0; blow < 3; blow++)
            {
                parts.Clear();
                rock.Areas(parts);
                if (parts.Count <= 1) break;

                HitData hit = new HitData { m_toolTier = 100, m_point = parts[0].At };
                hit.m_damage.m_pickaxe = 5000f;

                rock.Strike(parts[0], hit, out string _);
                yield return null;
            }

            parts.Clear();
            rock.Areas(parts);
            int left = parts.Count;

            report.Check(left > 0 && left < whole,
                "control: the deposit is left part-mined, which is what the next claim is about",
                $"parts {whole} -> {left}");

            // The half that only matters for mining: what is left lives on the record rather
            // than in memory, so a deposit half-mined is half-mined to anything that reads it.
            //
            // Asked through the protocol, which reads the record in whichever shape its
            // component uses - a base64 package, a float per part, or a single float. The
            // previous version of this compared two live protocols over the same object, which
            // both answered from the same collider hierarchy and so could only ever agree.
            float now = rock.Remaining();

            report.Check(now < recorded && now > 0f,
                "a part-mined deposit records what is left of it, and it is not nothing",
                $"recorded {recorded:0} -> {now:0}");

            // And the record outlives the reading. Guarded, because the deposit may have been
            // destroyed by the blows above - reading a destroyed object's ZDO is how a check
            // takes the whole run down with it.
            bool standing = view != null && view.IsValid();
            report.Check(standing && ZDOMan.instance.GetZDO(view.GetZDO().m_uid) != null,
                "control: and the deposit is still there to be read",
                $"standing={standing}");

            Release(deposit);
            SweepLooseItems(colony);
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>
        ///     What the classifier knows, before anything is asked to act on it.
        /// </summary>
        /// <remarks>
        ///     Cheap, and it fails first when it fails: every chopping check below is
        ///     meaningless if the index is empty, and an empty index makes them all pass by
        ///     finding nothing to contradict.
        /// </remarks>
        private static IEnumerator CheckChoppingIndex(TestReport report)
        {
            if (!Choppable.IsReady) Choppable.Rebuild();

            report.Check(Choppable.IsReady, "the chopping classifier found prefabs to classify");

            string anyTree = Choppable.SampleTree(0, 99);
            report.Check(!string.IsNullOrEmpty(anyTree),
                "it can name a real tree, so checks need not guess at prefab names",
                $"sample={anyTree}");

            // The tiers the give-up test depends on. If a world has only one tier of tree,
            // that check cannot be staged and should say so rather than pass.
            string soft = Choppable.SampleTree(0, 0);
            string hard = Choppable.SampleTree(2, 99);
            report.Check(!string.IsNullOrEmpty(soft) && !string.IsNullOrEmpty(hard),
                "control: it can tell a tree any axe can fell from one that needs a good axe",
                $"soft={soft} hard={hard}");

            List<string> names = new List<string>();
            Choppable.TreeNames(names);
            report.Check(names.Count > 0,
                "the species picker has real species to offer",
                $"species={names.Count}");

            yield break;
        }

        /// <summary>
        ///     The worst trap in the job, proven by its absence.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Damage is routed to whoever owns the object, and a world-generated tree has
        ///         no owner - so every peer decides the blow is somebody else's business and
        ///         drops it. The villager swings, the health does not move, and nothing
        ///         anywhere reports a problem.
        ///     </para>
        ///     <para>
        ///         This check needs the loudest control in the suite precisely because the trap
        ///         reports nothing when it bites: asserting that an unclaimed blow does
        ///         <em>nothing</em> is the only way to prove that claiming is what makes the
        ///         claimed one work.
        ///     </para>
        /// </remarks>
        private static IEnumerator CheckUnclaimedDamageDoesNothing(TestReport report, Vector3 origin)
        {
            Vector3 site = ChoppingSite(origin);
            SweepFelling(site, 24f);

            string species = Choppable.SampleTree(0, 0);
            if (string.IsNullOrEmpty(species))
            {
                report.Check(false, "unclaimed-damage check could find a tree to plant");
                yield break;
            }

            GameObject axe = FindAxe(0, 99);
            GameObject tree = Spawn(species, site);
            yield return new WaitForSecondsRealtime(.4f);

            if (tree == null || axe == null || !tree.TryGetComponent(out ZNetView view) || !view.IsValid())
            {
                report.Check(false, "unclaimed-damage check could plant a tree and find an axe",
                    $"tree={(tree == null ? "none" : species)} axe={(axe == null ? "none" : "yes")}");
                Release(tree);
                yield break;
            }

            ItemDrop.ItemData tool = axe.GetComponent<ItemDrop>().m_itemData;

            // Given away deliberately, so the blow below is made by a peer that does not own
            // the target - which is the state every world-generated tree starts in.
            view.GetZDO().SetOwner(0L);
            yield return new WaitForSecondsRealtime(.2f);

            long mine = ZDOMan.GetSessionID();
            Core.Log.Info($"[chop-check] disowned: owner={view.GetZDO().GetOwner()} session={mine} " +
                          $"isOwner={view.IsOwner()} at={view.transform.position} " +
                          $"playerAt={(Player.m_localPlayer == null ? Vector3.zero : Player.m_localPlayer.transform.position)}");

            float before = view.GetZDO().GetFloat(ZDOVars.s_health, tree.GetComponent<TreeBase>().m_health);
            BlowResult unclaimed = Felling.Strike(tree, tool, site + Vector3.back * 2f, out string said);
            yield return new WaitForSecondsRealtime(.2f);
            float afterUnclaimed = view.GetZDO().GetFloat(ZDOVars.s_health, before);

            report.Check(unclaimed == BlowResult.Claiming,
                "a blow at a tree nobody owns takes ownership instead of swinging",
                $"result={unclaimed} said='{said}'");

            report.Check(Mathf.Approximately(afterUnclaimed, before),
                "control: and it does not damage the tree - an unowned blow achieves nothing",
                $"health {before:0.0} -> {afterUnclaimed:0.0}");

            // Now it is ours, because the call above claimed it. The next blow must land.
            //
            // Reported either way, because "the claim did not stick" and "the blow did not
            // land" are different faults with the same symptom, and the whole design rests on
            // ownership being takeable. If ownership is being taken away again between the
            // two blows, that is the thing to know, and by whom.
            Core.Log.Info($"[chop-check] after claiming: owner={view.GetZDO().GetOwner()} " +
                          $"session={mine} isOwner={view.IsOwner()}");

            // Waited for rather than assumed, and re-claimed if it is taken away again.
            //
            // A fixed pause here made this check flaky: it failed one run and passed the
            // next on identical code. Ownership of a ZDO nobody is standing near is not
            // ours to keep - the game hands it about on its own schedule - so a window
            // between claiming and swinging sometimes contained that. Which is worth
            // knowing rather than papering over: the job already survives it, because a
            // blow at something it no longer owns re-claims and lands on the following
            // tick. The check needs to measure the blow, not the timing of that schedule.
            float owning = 0f;
            while (owning < 3f && view.IsValid() && !view.IsOwner())
            {
                view.ClaimOwnership();
                yield return new WaitForSecondsRealtime(.1f);
                owning += .1f;
            }

            report.Check(view.IsValid() && view.IsOwner(),
                "ownership of a tree nobody owns can actually be taken",
                $"owner={(view.IsValid() ? view.GetZDO().GetOwner() : 0L)} session={mine} " +
                $"took={owning:0.0}s");

            Core.Log.Info($"[chop-check] before second blow: owner={view.GetZDO().GetOwner()} " +
                          $"session={mine} isOwner={view.IsOwner()} waited={owning:0.0}s");
            BlowResult claimed = Felling.Strike(tree, tool, site + Vector3.back * 2f, out string then);
            yield return new WaitForSecondsRealtime(.2f);

            float afterClaimed = tree == null || !view.IsValid()
                ? 0f
                : view.GetZDO().GetFloat(ZDOVars.s_health, before);

            report.Check(claimed == BlowResult.Struck || claimed == BlowResult.Felled,
                "once owned, the same blow lands",
                $"result={claimed} said='{then}' health {before:0.0} -> {afterClaimed:0.0} " +
                $"owner={(view.IsValid() ? view.GetZDO().GetOwner() : 0L)} session={mine}");

            report.Check(afterClaimed < before,
                "control: the health actually moved, which is the only honest proof a blow landed",
                $"health {before:0.0} -> {afterClaimed:0.0}");

            // Health defaulting: an untouched tree and a damaged one must not read the same.
            // Defaulting the read to zero would make them identical, which is how a felled
            // tree and a fresh one became indistinguishable.
            GameObject fresh = Spawn(species, site + new Vector3(8f, 0f, 0f));
            yield return new WaitForSecondsRealtime(.4f);
            if (fresh != null && fresh.TryGetComponent(out ZNetView freshView) && freshView.IsValid())
            {
                float untouched = freshView.GetZDO()
                    .GetFloat(ZDOVars.s_health, fresh.GetComponent<TreeBase>().m_health);
                report.Check(untouched > afterClaimed,
                    "an untouched tree and a damaged one report different health",
                    $"untouched={untouched:0.0} damaged={afterClaimed:0.0}");
            }

            Release(fresh);
            Release(tree);
            SweepFelling(site, 24f);
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>
        ///     The loop, end to end: a standing tree becomes a log, and the log becomes wood.
        /// </summary>
        /// <remarks>
        ///     Sited well away from the settlement, because a falling tree has already
        ///     destroyed a benchmark chest in this repo and surfaced two phases later as a
        ///     persistence failure.
        /// </remarks>
        private static IEnumerator CheckChoppingFellsATree(TestReport report, Colony colony, Vector3 origin)
        {
            Vector3 site = ChoppingSite(origin);
            SweepFelling(site, 30f);

            string species = Choppable.SampleTree(0, 0);
            if (string.IsNullOrEmpty(species))
            {
                report.Check(false, "chop check could find a tree a basic axe can fell");
                yield break;
            }

            // A flag makes the site the Kolony's, which is also what lets a job be pointed at
            // it - the forest is not inside the hearth's radius and is not meant to be.
            GameObject flag = Spawn(WorkFlagPrefab.PrefabName, site);
            yield return new WaitForSecondsRealtime(.3f);
            WorkFlag planted = flag != null ? flag.GetComponent<WorkFlag>() : null;
            if (planted == null || ColonyOperations.AssignFlag(colony.Id, planted) != RegisterOutcome.Registered)
            {
                report.Check(false, "chop check could plant and claim a flag at the wood");
                Release(flag);
                yield break;
            }

            StructureRecord flagRecord = colony.State.GetStructures().Find(r => r.Id == planted.Id);
            GameObject tree = Spawn(species, site + new Vector3(6f, 0f, 0f));
            yield return new WaitForSecondsRealtime(.4f);

            // The scan caches for five seconds; the tree was planted a moment ago.
            ChoppingGround.ResetForTest();

            report.Check(tree != null, "control: there is a tree to fell", $"species={species}");
            if (tree == null)
            {
                Cleanup(colony, planted, null, flag, null);
                yield break;
            }

            colony.State.SetJobs(new List<JobDefinition>
            {
                new JobDefinition
                {
                    Id = "chop", Name = "Chop", Kind = JobKind.Chop, Repeat = 30,
                    Areas = new List<string> { flagRecord?.PersistentId ?? string.Empty },
                    WorkRadius = 32f
                }
            });

            Villager chopper = VillagerLifecycle.Spawn(colony);
            yield return new WaitForSecondsRealtime(.4f);
            if (chopper == null || !chopper.TryGetComponent(out ZNetView view) || !view.IsValid())
            {
                report.Check(false, "chop check could spawn a villager");
                Cleanup(colony, planted, null, flag, null);
                yield break;
            }

            ZDOID who = view.GetZDO().m_uid;
            SendRested(view);
            Container bag = VillagerInventory.Attach(chopper.gameObject, view);
            bool armed = GiveAxe(bag, view, 0, 99);
            report.Check(armed, "control: the villager has an axe, without which this job does nothing");

            new VillagerState(view.GetZDO()).SetQueue(new List<string> { "chop" });
            chopper.transform.position = site + new Vector3(3f, 0f, 0f);

            // What the job promises is that choppable things stop existing. Not that wood
            // appears: a tree leaves a log, a log leaves broken logs, and only those leave
            // wood - a chain whose shape is Valheim's rather than this job's, and whose end
            // belongs to hauling. Asserting on wood measured somebody else's work and failed
            // for it. Chopping is what is under test, so chopping is what is counted.
            bool sawALog = false;
            bool photographed = false;
            int standing = Nearby<TreeBase>(site, 30f);
            int choppable = standing + Nearby<TreeLog>(site, 30f);
            int started = choppable;
            float elapsed = 0f;

            while (elapsed < ChopSeconds && choppable > 0)
            {
                yield return new WaitForSecondsRealtime(.5f);
                elapsed += .5f;

                if (Nearby<TreeLog>(site, 30f) > 0) sawALog = true;

                // One frame of it actually happening, taken the first time there is a log on
                // the ground - which is the moment the loop is midway and both halves of it
                // are visible at once.
                if (sawALog && !photographed)
                {
                    photographed = true;
                    yield return BenchmarkUiScenario.PhotographAtWork("chop-at-work.png",
                        chopper.transform.position, "a villager chopping, with a felled log",
                        site + new Vector3(6f, 0f, 0f));
                }

                standing = Nearby<TreeBase>(site, 30f);
                choppable = standing + Nearby<TreeLog>(site, 30f);
            }

            report.Check(started > 0, "control: there was something to chop",
                $"started={started}");

            report.Check(standing == 0,
                "the tree came down",
                $"standing={standing} after {elapsed:0}s");

            report.Check(sawALog,
                "control: felling it left a log - a tree does not turn straight into wood",
                $"sawALog={sawALog}");

            report.Check(choppable == 0,
                "and the villager worked the log down too, until nothing choppable was left",
                $"remaining={choppable} after {elapsed:0}s");

            yield return BenchmarkUiScenario.PhotographAtWork("chop-cleared.png",
                chopper != null ? chopper.transform.position : site,
                $"the ground after chopping - {started} choppable things, {choppable} left",
                site + new Vector3(6f, 0f, 0f));

            VillagerLifecycle.Remove(colony, who);
            Cleanup(colony, planted, null, flag, null);

            // Put the job list back, as every other check here does. Left behind, a Repeat=30
            // chop job whose work area points at the flag just unregistered is what the
            // screen checks and the persistence reload then run against.
            colony.State.SetJobs(new List<JobDefinition>());
            SweepFelling(site, 30f);
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>
        ///     A tree this axe cannot bite is released and said once, not swung at forever.
        /// </summary>
        private static IEnumerator CheckGivingUpOnAnUncuttableTree(TestReport report, Vector3 origin)
        {
            Vector3 site = ChoppingSite(origin) + new Vector3(0f, 0f, 40f);
            SweepFelling(site, 20f);

            string tough = Choppable.SampleTree(2, 99);
            GameObject weak = FindAxe(0, 0);
            if (string.IsNullOrEmpty(tough) || weak == null)
            {
                report.Check(true,
                    "give-up check: this world has no tree/axe pair that cannot cut, so it did not run",
                    $"tough={tough} weakAxe={(weak == null ? "none" : "yes")}");
                yield break;
            }

            GameObject tree = Spawn(tough, site);
            yield return new WaitForSecondsRealtime(.4f);
            if (tree == null || !tree.TryGetComponent(out ZNetView view) || !view.IsValid())
            {
                report.Check(false, "give-up check could plant a tough tree");
                Release(tree);
                yield break;
            }

            view.ClaimOwnership();
            yield return new WaitForSecondsRealtime(.3f);

            ItemDrop.ItemData blunt = weak.GetComponent<ItemDrop>().m_itemData;
            BlowResult result = Felling.Strike(tree, blunt, site + Vector3.back * 2f, out string said);

            report.Check(result == BlowResult.TooHard,
                "a blunt axe on a hard tree reports that it cannot cut it, rather than nothing",
                $"result={result} said='{said}'");

            // Control: the same tree yields to a better axe, so the refusal is about the tool
            // and not about the tree being unhittable.
            GameObject good = FindAxe(2, 99);
            if (good != null)
            {
                float before = view.GetZDO()
                    .GetFloat(ZDOVars.s_health, tree.GetComponent<TreeBase>().m_health);
                BlowResult better = Felling.Strike(tree, good.GetComponent<ItemDrop>().m_itemData,
                    site + Vector3.back * 2f, out string _);
                yield return new WaitForSecondsRealtime(.2f);
                float after = !view.IsValid() ? 0f : view.GetZDO().GetFloat(ZDOVars.s_health, before);

                report.Check(better == BlowResult.Struck || better == BlowResult.Felled,
                    "control: the same tree yields to a better axe",
                    $"result={better} health {before:0.0} -> {after:0.0}");
            }

            Release(tree);
            SweepFelling(site, 20f);
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>
        ///     Each chopping setting changes behaviour, with the control that proves it.
        /// </summary>
        /// <remarks>
        ///     A job must not offer a setting it ignores. These are asked of the decision
        ///     directly rather than by watching a villager for minutes per setting: whether a
        ///     thing is eligible is the whole of what these settings do, and a check that
        ///     waits for a felling is measuring the walk as well.
        /// </remarks>
        private static IEnumerator CheckChopSettings(TestReport report, Vector3 origin)
        {
            Vector3 site = ChoppingSite(origin) + new Vector3(40f, 0f, 0f);
            SweepFelling(site, 20f);

            string species = Choppable.SampleTree(0, 0);
            if (string.IsNullOrEmpty(species))
            {
                report.Check(false, "chop-settings check could find a tree to plant");
                yield break;
            }

            GameObject tree = Spawn(species, site);
            yield return new WaitForSecondsRealtime(.4f);
            if (tree == null || !tree.TryGetComponent(out ZNetView view) || !view.IsValid())
            {
                report.Check(false, "chop-settings check could plant a tree");
                Release(tree);
                yield break;
            }

            ZDO zdo = view.GetZDO();

            JobDefinition fells = new JobDefinition { Kind = JobKind.Chop, ChopTrees = true };
            JobDefinition logsOnly = new JobDefinition
            {
                Kind = JobKind.Chop, ChopTrees = false, ChopLogs = true
            };

            report.Check(ChopJob.WouldTake(fells, zdo),
                "control: a felling job takes a standing tree");

            report.Check(!ChopJob.WouldTake(logsOnly, zdo),
                "a logs-only job leaves standing trees alone");

            JobDefinition wrongSpecies = new JobDefinition
            {
                Kind = JobKind.Chop, ChopTrees = true,
                Species = new List<string> { species + "_not_a_real_species" }
            };
            JobDefinition rightSpecies = new JobDefinition
            {
                Kind = JobKind.Chop, ChopTrees = true, Species = new List<string> { species }
            };

            report.Check(!ChopJob.WouldTake(wrongSpecies, zdo),
                "a species allow-list leaves species it does not name standing",
                $"planted={species}");

            report.Check(ChopJob.WouldTake(rightSpecies, zdo),
                "control: and takes the one it does name");

            report.Check(ChopJob.WouldTake(fells, zdo),
                "control: an empty species list still means every species");

            // Undergrowth, which is opt-in: a settlement should not quietly flatten scenery
            // because something had a Destructible on it.
            GameObject bush = SpawnFirst(site + new Vector3(6f, 0f, 0f),
                "Bush01", "Bush02_en", "shrub_2", "stubbe");
            yield return new WaitForSecondsRealtime(.4f);

            if (bush != null && bush.TryGetComponent(out ZNetView bushView) && bushView.IsValid() &&
                Choppable.Of(bushView.GetZDO().GetPrefab()) == ChopKind.Undergrowth)
            {
                JobDefinition clears = new JobDefinition
                {
                    Kind = JobKind.Chop, ChopTrees = false, ChopLogs = false, ChopUndergrowth = true
                };

                report.Check(ChopJob.WouldTake(clears, bushView.GetZDO()),
                    "a clearing job takes undergrowth");
                report.Check(!ChopJob.WouldTake(fells, bushView.GetZDO()),
                    "control: a felling job leaves undergrowth alone - it is opt-in");
            }

            Release(bush);
            Release(tree);
            SweepFelling(site, 20f);
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>
        ///     The two stopping rules, which are the settings most able to look like they work.
        /// </summary>
        /// <remarks>
        ///     Both are answered by asking the engine the same question a villager's tick asks,
        ///     rather than by watching a villager for minutes. "Leave standing" is about the
        ///     forest and "stop when we have" is about the settlement, and neither substitutes
        ///     for the other - so each gets its own control.
        /// </remarks>
        private static IEnumerator CheckChopStoppingRules(TestReport report, Colony colony, Vector3 origin)
        {
            Vector3 site = ChoppingSite(origin) + new Vector3(0f, 0f, -40f);
            SweepFelling(site, 24f);

            // Stop-when-we-have, measured against what the settlement actually holds.
            GameObject chest = Spawn("piece_chest_wood", origin + new Vector3(4f, 0f, -6f));
            yield return new WaitForSecondsRealtime(.3f);
            StructureRecord store = Register(colony, chest, "Stop-rule shed");
            if (store == null)
            {
                report.Check(false, "stopping-rule check could register a chest");
                Release(chest);
                yield break;
            }

            // Reset before the control as well as after the change. An earlier check that
            // asked the same question within the freshness window would otherwise have this
            // one measuring the cache rather than the settlement.
            Stock.ResetForTest();
            report.Check(Stock.Held(colony, "Wood") == 0,
                "control: an empty settlement holds none of it",
                $"held={Stock.Held(colony, "Wood")}");

            GameObject woodPrefab = ObjectDB.instance?.GetItemPrefab("Wood");
            Container box = chest.GetComponentInChildren<Container>(true);
            if (woodPrefab != null && box != null && woodPrefab.TryGetComponent(out ItemDrop woodDrop))
            {
                ItemDrop.ItemData stack = woodDrop.m_itemData.Clone();
                stack.m_dropPrefab = woodPrefab;
                stack.m_stack = 12;
                box.GetInventory().AddItem(stack);
            }

            // The count is cached for a moment, and the read above already cached zero for
            // this settlement in this same frame. Without this the rule below is measured
            // against a stale nothing and fails for a reason that is not the rule.
            Stock.ResetForTest();

            int held = Stock.Held(colony, "Wood");
            report.Check(held >= 12,
                "the settlement can say how much of a thing it is holding",
                $"held={held}");

            // The rule itself: above the line there is nothing worth cutting, below it there
            // is. Asked of the job's own facts rather than of a villager, because what is
            // under test is the threshold and not the walk.
            report.Check(ChopJob.WouldStop(colony,
                    new JobDefinition { Kind = JobKind.Chop, StockItem = "Wood", StockTarget = 10 }),
                "at the threshold the job stops asking for more",
                $"held={held} target=10");

            report.Check(!ChopJob.WouldStop(colony,
                    new JobDefinition { Kind = JobKind.Chop, StockItem = "Wood", StockTarget = 500 }),
                "control: below it the same job carries on",
                $"held={held} target=500");

            report.Check(!ChopJob.WouldStop(colony,
                    new JobDefinition { Kind = JobKind.Chop, StockItem = "Wood", StockTarget = 0 }),
                "control: a target of zero means never stop",
                $"held={held} target=0");

            colony.RemoveStructure(store.Id);
            Release(chest);

            // Leave-standing, which is about the place rather than the store. Planted as a
            // small stand so the threshold has something to count.
            //
            // A claimed flag goes in first, because the scan is bounded per anchor and this
            // site is beyond the config radius from the hearth. Without it the count is
            // always zero, the positive assertion passes vacuously, and the control that
            // says "a lower threshold is still work" fails for a reason that has nothing to
            // do with the rule.
            GameObject standFlag = Spawn(WorkFlagPrefab.PrefabName, site);
            yield return new WaitForSecondsRealtime(.3f);
            WorkFlag standAnchor = standFlag != null ? standFlag.GetComponent<WorkFlag>() : null;
            bool anchored = standAnchor != null &&
                            ColonyOperations.AssignFlag(colony.Id, standAnchor) == RegisterOutcome.Registered;
            report.Check(anchored,
                "control: the stand is inside the Kolony's reach, so the scan can see it");

            string species = Choppable.SampleTree(0, 0);
            List<GameObject> stand = new List<GameObject>();
            if (!string.IsNullOrEmpty(species))
            {
                for (int i = 0; i < 3; i++)
                {
                    stand.Add(Spawn(species, site + new Vector3(8f + i * 5f, 0f, 0f)));
                }
            }

            yield return new WaitForSecondsRealtime(.5f);

            // The scan caches for five seconds, which is right for a game and wrong for a
            // check that just planted its subject.
            ChoppingGround.ResetForTest();

            int planted = Nearby<TreeBase>(site, 24f);
            report.Check(planted >= 3, "control: there is a stand of trees to thin",
                $"standing={planted}");

            if (planted >= 3)
            {
                report.Check(ChopJob.WouldSpare(colony,
                        new JobDefinition { Kind = JobKind.Chop, LeaveStanding = planted }, site, 24f),
                    "a job told to leave this many standing takes no more trees",
                    $"standing={planted} leave={planted}");

                report.Check(!ChopJob.WouldSpare(colony,
                        new JobDefinition { Kind = JobKind.Chop, LeaveStanding = 1 }, site, 24f),
                    "control: with a lower threshold the same stand is still work",
                    $"standing={planted} leave=1");

                report.Check(!ChopJob.WouldSpare(colony,
                        new JobDefinition { Kind = JobKind.Chop, LeaveStanding = 0 }, site, 24f),
                    "control: zero means take them all");
            }

            foreach (GameObject tree in stand) Release(tree);
            if (standAnchor != null) colony.RemoveStructure(standAnchor.Id);
            Release(standFlag);
            SweepFelling(site, 24f);
            ChoppingGround.ResetForTest();
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>
        ///     A trip that cannot be finished ends, and the queue moves on.
        /// </summary>
        /// <remarks>
        ///     The failure a player actually meets: a villager reporting "off to chop" on a
        ///     hillside it cannot climb, for ever. Walking is the one step that can keep
        ///     saying "still going", and the queue ignores Running entirely - so the job never
        ///     finishes and the second entry of a preset never runs. One villager on a slope
        ///     quietly stops being a settlement.
        /// </remarks>
        private static IEnumerator CheckAStuckTripEndsAndTheQueueMovesOn(TestReport report,
            Colony colony)
        {
            report.Check(JobOutcomes.AbandonAfterSeconds > 45f,
                "control: the limit is past the rescue ladder, so it is not a second rescue",
                $"limit={JobOutcomes.AbandonAfterSeconds:0}s vs ladder at 45s");

            Villager villager = VillagerLifecycle.Spawn(colony);
            yield return new WaitForSecondsRealtime(.4f);
            if (villager == null || !villager.TryGetComponent(out ZNetView view) || !view.IsValid())
            {
                report.Check(false, "stuck-trip check could spawn a villager");
                yield break;
            }

            ZDOID who = view.GetZDO().m_uid;
            VillagerState state = new VillagerState(view.GetZDO());

            // The case the whole fix exists for, asked of a real villager with a real target -
            // the negative cases alone would all pass with the bound removed entirely, which
            // is a check that proves nothing.
            state.SetTarget(who);
            JobResult? gaveUp = JobOutcomes.GiveUpIfStuck(villager, state,
                JobOutcomes.AbandonAfterSeconds + 1f, who, () => "that", out string _);

            report.Check(gaveUp == JobResult.Failed,
                "a trip that has stopped making progress is given up on",
                $"result={gaveUp}");

            report.Check(state.Target.IsNone(),
                "control: the trip really is released, not merely reported");

            report.Check(!JobOutcomes.GiveUpIfStuck(villager, state, 0f, who, () => "that",
                    out string _).HasValue,
                "control: a trip that is making progress is left alone");

            report.Check(!JobOutcomes.GiveUpIfStuck(villager, state,
                    JobOutcomes.AbandonAfterSeconds - 1f, who, () => "that", out string _).HasValue,
                "control: and is left alone right up to the limit",
                $"limit={JobOutcomes.AbandonAfterSeconds:0}s");

            // And the clock the bound reads, which nothing had exercised. A check that feeds
            // the bound a number of its own proves the comparison and nothing about whether
            // the villager's own trip clock starts, advances or rebaselines - which is where
            // every fault in this area has actually been, including one round where the
            // clock was not wired up at all and every check still passed.
            //
            // Stilled first. A villager with a queue walks to its work and one away from home
            // walks home, and either would be handing the walk a different leg between these
            // calls - which rebaselines the very clock being measured.
            state.SetQueue(new List<string>());
            state.SetHome(villager.transform.position);
            yield return new WaitForSecondsRealtime(.2f);

            Vector3 spot = villager.transform.position + new Vector3(.5f, 0f, 0f);
            villager.WalkForTest(spot);
            report.Check(villager.TripStalledFor <= 0f,
                "control: a leg just begun has nothing on its clock",
                $"stalled={villager.TripStalledFor:0.00}s");

            // Short enough to count. Only gaps under the walking gap accrue, so a wait longer
            // than that is a villager that stopped walking and nothing is added at all - which
            // is how this check first asserted its way into failing on a clock that worked.
            yield return new WaitForSecondsRealtime(.3f);
            villager.WalkForTest(spot);

            float accrued = villager.TripStalledFor;
            report.Check(accrued > 0f,
                "a leg that keeps walking without getting closer accrues a stall",
                $"stalled={accrued:0.00}s");

            // A different place is a different trip, which is what every unannounced leg
            // relies on - a delivery after a collection, a walk back to a trunk that rolled.
            villager.WalkForTest(villager.transform.position + new Vector3(0f, 0f, 40f));
            report.Check(villager.TripStalledFor <= 0f,
                "walking somewhere else begins a new trip",
                $"stalled={villager.TripStalledFor:0.00}s");

            // And the second signal, for two targets too close together for the first to see.
            villager.WalkForTest(spot);
            yield return new WaitForSecondsRealtime(.3f);
            villager.WalkForTest(spot);
            report.Check(villager.TripStalledFor > 0f,
                "control: and the clock is running again on the new one",
                $"stalled={villager.TripStalledFor:0.00}s");

            villager.NewLegForTest();
            report.Check(villager.TripStalledFor <= 0f,
                "a job that chooses something new says so, for targets a stride apart",
                $"stalled={villager.TripStalledFor:0.00}s");

            // Stopping on purpose ends the trip. Resting drives the walk every tick and stops
            // once it arrives, so without this a villager accrues a stall for a whole night's
            // sleep and wakes up abandoning what it was doing before bed.
            villager.WalkForTest(spot);
            yield return new WaitForSecondsRealtime(.3f);
            villager.WalkForTest(spot);
            report.Check(villager.TripStalledFor > 0f,
                "control: the clock is running before it is stopped",
                $"stalled={villager.TripStalledFor:0.00}s");

            villager.StopForTest();
            report.Check(villager.TripStalledFor <= 0f,
                "stopping on purpose ends the trip, so sleeping accrues nothing",
                $"stalled={villager.TripStalledFor:0.00}s");

            // And the queue half this check is named for, which nothing was asserting.
            List<JobDefinition> two = new List<JobDefinition>
            {
                new JobDefinition { Id = "first", Name = "First", Kind = JobKind.Chop, Repeat = 1 },
                new JobDefinition { Id = "second", Name = "Second", Kind = JobKind.Haul, Repeat = 1 }
            };

            state.SetQueue(new List<string> { "first", "second" });
            state.SetQueuePosition(0);
            state.SetQueueAttempt(0);

            QueueRunner.Apply(state, two, JobResult.Running);
            report.Check(QueueRunner.Current(state, two)?.Id == "first",
                "control: a job still running keeps the villager where it is",
                $"current={QueueRunner.Current(state, two)?.Id}");

            QueueRunner.Apply(state, two, JobResult.Failed);
            report.Check(QueueRunner.Current(state, two)?.Id == "second",
                "a trip that gave up hands the villager to the next job",
                $"current={QueueRunner.Current(state, two)?.Id}");

            // And the half that makes giving up worth anything: the villager does not turn
            // round and choose the same thing again. Without this the bound only unblocks the
            // queue, and the next lap walks the same impossible route.
            Unreachable.Clear();
            report.Check(!Unreachable.Refuses(who, who),
                "control: nothing is refused before anything has been given up on");

            Unreachable.Refuse(who, who);
            report.Check(Unreachable.Refuses(who, who),
                "what a villager gave up reaching is refused to it afterwards");

            report.Check(!Unreachable.Refuses(colony.Id, who),
                "control: and only to it - another villager stands somewhere else");

            Unreachable.Forget(who);
            report.Check(!Unreachable.Refuses(who, who),
                "control: and it is dropped with the villager, not kept for a world");

            VillagerLifecycle.Remove(colony, who);
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>
        ///     A preset's order is the order its villagers work in.
        /// </summary>
        private static IEnumerator CheckAPresetKeepsItsOrder(TestReport report, Colony colony)
        {
            colony.State.SetJobs(new List<JobDefinition>
            {
                new JobDefinition { Id = "chop", Name = "Chop", Kind = JobKind.Chop, Repeat = 1 },
                new JobDefinition { Id = "haul", Name = "Haul", Kind = JobKind.Haul, Repeat = 1 }
            });

            // Built the way the screen builds one: appended, in the order chosen.
            List<JobPreset> presets = colony.State.GetPresets();
            presets.RemoveAll(p => p.Id == "order");
            JobPreset preset = new JobPreset { Id = "order", Name = "Ordered" };
            preset.Jobs.Add("chop");
            preset.Jobs.Add("haul");
            presets.Add(preset);
            colony.State.SetPresets(presets);

            JobPreset stored = colony.State.GetPresets().Find(p => p.Id == "order");
            bool kept = stored != null && stored.Jobs.Count == 2 &&
                        stored.Jobs[0] == "chop" && stored.Jobs[1] == "haul";
            report.Check(kept, "a preset keeps the order it was given",
                stored == null ? "missing" : string.Join(",", stored.Jobs.ToArray()));

            if (!kept)
            {
                // Stopped here rather than read on. Everything below dereferences this, and a
                // coroutine that throws never prints its report nor cleans up after itself.
                colony.State.SetJobs(new List<JobDefinition>());
                yield break;
            }

            Villager villager = VillagerLifecycle.Spawn(colony);
            yield return new WaitForSecondsRealtime(.4f);
            if (villager == null || !villager.TryGetComponent(out ZNetView view) || !view.IsValid())
            {
                report.Check(false, "preset-order check could spawn a villager");
                colony.State.SetJobs(new List<JobDefinition>());
                yield break;
            }

            ZDOID who = view.GetZDO().m_uid;
            Assignment.Apply(new List<ZDOID> { who }, stored.Jobs);

            VillagerState state = new VillagerState(view.GetZDO());
            List<string> queue = state.GetQueue();
            report.Check(queue.Count == 2 && queue[0] == "chop" && queue[1] == "haul",
                "and a villager given that preset works it in that order",
                string.Join(",", queue.ToArray()));

            List<JobDefinition> defined = colony.State.GetJobs();
            report.Check(QueueRunner.Current(state, defined)?.Id == "chop",
                "control: the first job really is first");

            QueueRunner.Apply(state, defined, JobResult.Completed);
            report.Check(QueueRunner.Current(state, defined)?.Id == "haul",
                "control: and finishing it hands over to the second");

            // Removing the first leaves the second, which is what the screen's Remove does.
            preset.Jobs.RemoveAt(0);
            presets = colony.State.GetPresets();
            presets.RemoveAll(p => p.Id == "order");
            presets.Add(preset);
            colony.State.SetPresets(presets);

            JobPreset trimmed = colony.State.GetPresets().Find(p => p.Id == "order");
            report.Check(trimmed != null && trimmed.Jobs.Count == 1 && trimmed.Jobs[0] == "haul",
                "removing one leaves the rest in order",
                trimmed == null ? "missing" : string.Join(",", trimmed.Jobs.ToArray()));

            VillagerLifecycle.Remove(colony, who);
            presets = colony.State.GetPresets();
            presets.RemoveAll(p => p.Id == "order");
            colony.State.SetPresets(presets);
            colony.State.SetJobs(new List<JobDefinition>());
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>
        ///     A full chest keeps what it has.
        /// </summary>
        /// <remarks>
        ///     The shuffle loop, reached by the one route the scoring could not see. A cap
        ///     refuses arrivals; scored against the item already inside, it read as a reason
        ///     to carry that item out - and the chest, now under its cap, immediately scored
        ///     best for it again. Nothing covered this because no check had ever set a cap,
        ///     which is exactly how a rule the design calls impossible went unimplemented.
        /// </remarks>
        private static IEnumerator CheckACappedChestKeepsWhatItHas(TestReport report, Colony colony,
            Vector3 origin)
        {
            SweepLooseItems(colony);
            SettlementIndex.ResetForTest();

            GameObject shed = Spawn("piece_chest_wood", origin + new Vector3(-7f, 0f, 5f));
            GameObject overflow = Spawn("piece_chest_wood", origin + new Vector3(-7f, 0f, 9f));
            yield return new WaitForSecondsRealtime(.3f);

            StructureRecord shedRecord = Register(colony, shed, "Capped shed");
            StructureRecord overflowRecord = Register(colony, overflow, "Overflow");
            if (shedRecord == null || overflowRecord == null)
            {
                report.Check(false, "capped-chest check could register its chests");
                Release(shed);
                Release(overflow);
                yield break;
            }

            // Named and capped; the other takes anything, which is what an overflow chest is.
            ColonyOperations.EditSettings(colony, shedRecord.Id, s =>
            {
                s.Accepts = new List<string> { "Wood" };
                s.SetCap("Wood", 10);
            });
            ColonyOperations.EditSettings(colony, overflowRecord.Id, s => s.Accepts = new List<string>());

            // Filled through the shared helper, which reports what it actually stored. Built
            // by hand and unchecked, a fixture that quietly failed would leave the chest under
            // its cap and the failure would read as a fault in the scoring.
            Container box = shed.GetComponentInChildren<Container>(true);
            int stocked = PutIn(box, "Wood", 10);
            report.Check(stocked == 10, "control: the shed is filled to its cap",
                $"stored={stocked}");

            SettlementIndex.ResetForTest();
            yield return new WaitForSecondsRealtime(.2f);

            // Re-read after the edits. A record is a value decoded fresh on every ask, and
            // EditSettings mutates its own decoded copy - so the ones captured at
            // registration still describe a chest that accepts anything and caps nothing.
            // This suite records that trap elsewhere and this check walked into it: every
            // score below was being taken against a chest with no cap at all.
            StructureRecord cappedNow = SettlementIndex.Find(colony, shedRecord.Id);
            StructureRecord overflowNow = SettlementIndex.Find(colony, overflowRecord.Id);
            if (cappedNow == null || overflowNow == null)
            {
                report.Check(false, "capped-chest check could re-read its chests after editing");
                Release(shed);
                Release(overflow);
                yield break;
            }

            int atCap = SettlementIndex.ScoreOf(cappedNow, "Wood", holding: true);
            int asDestination = SettlementIndex.ScoreOf(cappedNow, "Wood");
            int elsewhere = SettlementIndex.ScoreOf(overflowNow, "Wood");

            report.Check(!Placement.MayMove(atCap, elsewhere),
                "a chest at its cap keeps the wood it is already holding",
                $"here={atCap} overflow={elsewhere}");

            report.Check(asDestination == Placement.Refused,
                "control: and still refuses more of it, which is what the cap is for",
                $"asDestination={asDestination}");

            // A control that can actually fail: the same chest below its cap is offered
            // again. Asserting nothing is sent to a Refused chest only restates the line
            // above, because MayMove cannot accept a Refused destination by definition.
            ColonyOperations.EditSettings(colony, shedRecord.Id, s => s.SetCap("Wood", 50));
            SettlementIndex.ResetForTest();
            StructureRecord roomy = SettlementIndex.Find(colony, shedRecord.Id);
            int withRoom = roomy == null ? -99 : SettlementIndex.ScoreOf(roomy, "Wood");

            report.Check(withRoom == Placement.Named,
                "control: raise the cap and the same chest is the best home again",
                $"withRoom={withRoom}");

            // And the cap bounds what arrives, which is the only thing enforcing it now that
            // a chest keeps what it holds.
            ColonyOperations.EditSettings(colony, shedRecord.Id, s => s.SetCap("Wood", 10));
            SettlementIndex.ResetForTest();
            StructureRecord bounded = SettlementIndex.Find(colony, shedRecord.Id);
            report.Check(bounded != null && SettlementIndex.RoomUnderCap(bounded, "Wood") == 0,
                "a chest at its cap has no room under it for more",
                $"room={(bounded == null ? -99 : SettlementIndex.RoomUnderCap(bounded, "Wood"))}");

            ColonyOperations.EditSettings(colony, shedRecord.Id, s => s.SetCap("Wood", 25));
            SettlementIndex.ResetForTest();
            StructureRecord partial = SettlementIndex.Find(colony, shedRecord.Id);
            report.Check(partial != null && SettlementIndex.RoomUnderCap(partial, "Wood") == 15,
                "control: below its cap it has exactly the difference",
                $"room={(partial == null ? -99 : SettlementIndex.RoomUnderCap(partial, "Wood"))}");

            ColonyOperations.EditSettings(colony, shedRecord.Id, s => s.SetCap("Wood", 10));
            SettlementIndex.ResetForTest();

            // And the loop itself, through the job's own chooser rather than the scores.
            colony.State.SetJobs(new List<JobDefinition>
            {
                new JobDefinition { Id = "tidy", Name = "Tidy", Kind = JobKind.Haul, Repeat = 4,
                    TidyContainers = true }
            });

            Villager tidier = VillagerLifecycle.Spawn(colony);
            ZDOID who = tidier != null ? tidier.Id : ZDOID.None;
            yield return new WaitForSecondsRealtime(.3f);

            bool wouldMove = tidier != null && Selection.TryFindContainerWork(colony,
                colony.State.GetJobs()[0], tidier, out GameObject from, out StructureRecord _) &&
                from == shed;

            report.Check(!wouldMove,
                "and no tidying trip is invented to empty it",
                $"wouldMove={wouldMove}");

            if (!who.IsNone()) VillagerLifecycle.Remove(colony, who);
            colony.State.SetJobs(new List<JobDefinition>());
            colony.RemoveStructure(shedRecord.Id);
            colony.RemoveStructure(overflowRecord.Id);
            Release(shed);
            Release(overflow);
            SettlementIndex.ResetForTest();
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>
        ///     The axe is held while chopping and put away afterwards - and the player's own
        ///     choice of gear is not.
        /// </summary>
        /// <remarks>
        ///     Written because two rounds of fixes to this behaviour both shipped with a hole
        ///     in it, and nothing anywhere asserted on the right hand. The slot persists in
        ///     the save, so getting it wrong is not a cosmetic slip: it is a villager holding
        ///     an axe for the rest of a world, or a player's torch disappearing every time
        ///     they equip it.
        /// </remarks>
        private static IEnumerator CheckTheAxeIsPutAway(TestReport report, Colony colony)
        {
            Villager villager = VillagerLifecycle.Spawn(colony);

            // Taken now, while the villager is certainly valid - Spawn cannot have registered
            // it otherwise. Read later, on a failure path, it is None exactly when it is
            // needed, and the cleanup that depends on it becomes unreachable.
            ZDOID spawned = villager != null ? villager.Id : ZDOID.None;
            yield return new WaitForSecondsRealtime(.4f);

            ZNetView view = villager != null && villager.TryGetComponent(out ZNetView found) &&
                            found.IsValid()
                ? found
                : null;
            VisEquipment vis = villager != null && villager.TryGetComponent(out VisEquipment worn)
                ? worn
                : null;

            if (view == null || vis == null)
            {
                report.Check(false, "axe check could spawn a villager with equipment");

                // Removed even on this path, and destroyed outright when there is no valid
                // record to remove it by - which is the case that most often lands here. A
                // villager left behind is counted by the next check's claim and collision
                // measurements.
                // Destroying the object is not removing the villager: Spawn only returns once
                // the member has been written to the colony's persisted state, and it is
                // Unregister that takes it out again. Skipping that leaves a phantom id in the
                // roster for the rest of the world - unnamed in every picker, counted by
                // assignment, and pinned on the map. The id is the one captured at spawn,
                // because by here the record may be exactly what has gone invalid.
                if (!spawned.IsNone())
                {
                    VillagerLifecycle.Remove(colony, spawned);
                    colony.Unregister(ColonyMemberKind.Villager, spawned);
                }

                if (villager != null) Release(villager.gameObject);
                yield break;
            }

            ZDOID who = view.GetZDO().m_uid;
            ZDO zdo = view.GetZDO();

            GameObject axe = FindAxe(0, 99);
            if (axe == null)
            {
                report.Check(false, "axe check could find an axe in this world");
                VillagerLifecycle.Remove(colony, who);
                yield break;
            }

            // Put one in its hand the way the job does.
            //
            // Cloned with its drop prefab restored, because item data taken straight off an
            // ObjectDB prefab has no m_dropPrefab - the field is filled in when an item
            // passes through an inventory - and the wardrobe hashes exactly that field. Handed
            // the bare prefab data it writes hash zero, so the hand stays empty, the control
            // below fails, and the assertion after it passes because nothing was ever there.
            // That is this suite's own recorded trap, and this check walked into it.
            ItemDrop.ItemData held = axe.GetComponent<ItemDrop>().m_itemData.Clone();
            held.m_dropPrefab = axe;
            VillagerWardrobe.Set(vis, WearSlot.RightHand, held);

            // Read with no wait. An idle villager's own tick calls PutAxeAway every frame, so
            // anything yielded here takes the axe back before the control can see it - and
            // the check would then prove the behaviour by never observing it.
            report.Check(VillagerWardrobe.Worn(zdo, WearSlot.RightHand) != 0,
                "control: the villager is visibly holding the axe",
                $"worn={VillagerWardrobe.Worn(zdo, WearSlot.RightHand)}");

            VillagerTool.PutAway(vis, zdo);

            report.Check(VillagerWardrobe.Worn(zdo, WearSlot.RightHand) == 0,
                "an axe is put away when the villager is no longer chopping");

            // The other half, and the one a player notices: gear they chose stays. Asked of
            // a torch or a shield - anything that does not chop - because the rule is about
            // what the tool is, not about who wrote the slot.
            GameObject keepsake = FindNonAxeHandItem();
            if (keepsake == null)
            {
                report.Check(true,
                    "axe check: this world has no non-axe hand item, so the control did not run");
            }
            else
            {
                ItemDrop.ItemData kept = keepsake.GetComponent<ItemDrop>().m_itemData.Clone();
                kept.m_dropPrefab = keepsake;
                VillagerWardrobe.Set(vis, WearSlot.RightHand, kept);

                int before = VillagerWardrobe.Worn(zdo, WearSlot.RightHand);
                report.Check(before != 0, "control: the villager is visibly holding it",
                    $"item={keepsake.name} worn={before}");

                VillagerTool.PutAway(vis, zdo);

                report.Check(VillagerWardrobe.Worn(zdo, WearSlot.RightHand) == before,
                    "control: gear the player chose is left alone - only an axe is taken back",
                    $"item={keepsake.name}");

                VillagerWardrobe.Set(vis, WearSlot.RightHand, null);
            }

            // And the third answer, which is the one this round added and the one the control
            // above cannot distinguish: a hash that resolves to nothing must be left alone
            // *and* said out loud. Without this an Identify that had regressed to calling
            // everything unknown would still pass both checks above.
            // The key the warning is actually said under. It moved to VillagerTool when the
            // hand did, and renamed with it - a check watching the old key answers false for
            // ever, which is indistinguishable from the branch being silent.
            const string unknownKey = "[villager] unknown held item";
            Core.Chatter.Forget(unknownKey);
            vis.SetRightItem(0, 1);
            zdo.Set(ZDOVars.s_rightItem, "kukolony_not_a_real_item".GetStableHashCode());

            int mystery = VillagerWardrobe.Worn(zdo, WearSlot.RightHand);
            VillagerTool.PutAway(vis, zdo);

            report.Check(VillagerWardrobe.Worn(zdo, WearSlot.RightHand) == mystery,
                "an item it cannot identify is left in place rather than taken",
                $"worn={mystery}");

            report.Check(Core.Chatter.Said(unknownKey),
                "control: and it says so, rather than swallowing it");

            zdo.Set(ZDOVars.s_rightItem, 0);
            VillagerLifecycle.Remove(colony, who);
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>
        ///     Something a villager can hold that is not an axe, for the control above.
        /// </summary>
        private static GameObject FindNonAxeHandItem()
        {
            if (ObjectDB.instance == null) return null;

            foreach (GameObject candidate in ObjectDB.instance.m_items)
            {
                if (candidate == null || !candidate.TryGetComponent(out ItemDrop drop)) continue;

                ItemDrop.ItemData.SharedData shared = drop.m_itemData?.m_shared;
                if (shared == null) continue;

                // Neither kind of tool. The hand now recognises pickaxes as well as axes, and
                // Valheim's pickaxes are one-handed weapons - which fit this slot - so a helper
                // that only rejected chop damage could hand the control a pickaxe and watch it
                // be stripped, correctly, while the check called it a failure.
                if (shared.m_damages.m_chop > 0f || shared.m_damages.m_pickaxe > 0f) continue;
                if (!VillagerWardrobe.Fits(drop.m_itemData, WearSlot.RightHand)) continue;

                return candidate;
            }

            return null;
        }

        /// <summary>
        ///     Trees survive in a zone kept open for a villager.
        /// </summary>
        /// <remarks>
        ///     The off-screen case, asked of the allowlist rather than by walking away: a
        ///     chopping villager in a kept zone whose trees were filtered out finds nothing to
        ///     do, idles, and works perfectly every time somebody comes to look. Watching for
        ///     that requires nobody watching, which a check cannot arrange - so the mechanism
        ///     it depends on is asserted instead, and the full behaviour is left to the
        ///     off-screen run.
        /// </remarks>
        private static IEnumerator CheckTreesAreKeptLoaded(TestReport report)
        {
            if (!LoadAllowlist.IsReady) LoadAllowlist.Rebuild();
            if (!Choppable.IsReady) Choppable.Rebuild();

            string species = Choppable.SampleTree(0, 99);
            if (string.IsNullOrEmpty(species) || ZNetScene.instance == null)
            {
                report.Check(false, "kept-trees check could name a tree");
                yield break;
            }

            report.Check(LoadAllowlist.Contains(species.GetStableHashCode()),
                "trees are loaded in a zone kept open for a villager, so chopping works off-screen",
                $"species={species}");

            // Control: the allowlist is a filter and not a pass-through. Something the colony
            // has no interest in must still be excluded, or this proves nothing.
            report.Check(!LoadAllowlist.Contains("not_a_real_prefab".GetStableHashCode()),
                "control: the allowlist still excludes what a colony has no use for");

            yield break;
        }

        /// <summary>Where destructive chopping fixtures go, clear of anything registered.</summary>
        /// <remarks>
        ///     Far enough that a falling trunk cannot reach the settlement. A tree felled in
        ///     this repo has already destroyed a benchmark chest, and it surfaced two phases
        ///     later as a persistence failure rather than as a falling tree.
        /// </remarks>
        /// <summary>
        ///     Where mining fixtures go: beside ground three other checks already use, on an
        ///     offset none of them does.
        /// </summary>
        private static Vector3 MiningSite(Vector3 origin)
        {
            Vector3 site = ChoppingSite(origin) + new Vector3(-40f, 0f, 0f);
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(site, out float ground))
            {
                site.y = ground;
            }

            return site;
        }

        /// <summary>Clears loose items from a patch, so a count of what fell means this run's.</summary>
        private static void SweepDrops(Vector3 site, float radius)
        {
            foreach (ItemDrop drop in new List<ItemDrop>(ItemDrop.s_instances))
            {
                if (drop != null && Vector3.Distance(drop.transform.position, site) <= radius)
                    Release(drop.gameObject);
            }
        }

        /// <summary>Puts a pickaxe of a given tier range into a villager's bag.</summary>
        /// <remarks>
        ///     Cloned with its prefab attached, as <see cref="GiveAxe" /> is: item data taken
        ///     straight off a prefab has no <c>m_dropPrefab</c>, and everything that names an
        ///     item reads exactly that.
        /// </remarks>
        private static bool GivePickaxe(Container bag, ZNetView view, int lowestTier, int highestTier)
        {
            ItemDrop.ItemData pick = FindPickaxe(lowestTier, highestTier);
            if (bag == null || pick == null) return false;

            bag.GetInventory().AddItem(pick);
            VillagerInventory.Persist(bag, view);
            return true;
        }

        private static Vector3 ChoppingSite(Vector3 origin)
        {
            Vector3 site = origin + new Vector3(90f, 0f, -90f);
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(site, out float ground))
            {
                site.y = ground;
            }

            return site;
        }

        /// <summary>
        ///     How long to allow for felling one tree and cutting up its log.
        /// </summary>
        /// <remarks>
        ///     Generous: the villager has to walk to it, and a tree is many blows. A check that
        ///     fails because it was in a hurry teaches nothing.
        /// </remarks>
        private const float ChopSeconds = 180f;

        /// <summary>
        ///     Clears what felling a tree leaves behind: the trunk, its sub-logs, the stump and
        ///     the wood.
        /// </summary>
        /// <remarks>
        ///     Each chopping control asserts that a villager finds nothing to do, and a log
        ///     left by the phase before it is something to do - which is correct behaviour
        ///     reported as a failure. Run before a control, never after a positive check,
        ///     where it would destroy the evidence.
        /// </remarks>
        private static void SweepFelling(Vector3 site, float radius)
        {
            foreach (TreeLog log in FindAll<TreeLog>())
            {
                if (log != null && Vector3.Distance(log.transform.position, site) <= radius)
                    Release(log.gameObject);
            }

            foreach (TreeBase tree in FindAll<TreeBase>())
            {
                if (tree != null && Vector3.Distance(tree.transform.position, site) <= radius)
                    Release(tree.gameObject);
            }

            foreach (ItemDrop drop in new List<ItemDrop>(ItemDrop.s_instances))
            {
                if (drop != null && Vector3.Distance(drop.transform.position, site) <= radius)
                    Release(drop.gameObject);
            }
        }

        /// <summary>
        ///     An axe from the game's own item list whose tool tier falls in a range.
        /// </summary>
        /// <remarks>
        ///     Found by capability rather than named, for the reason every other list here is
        ///     asked rather than written: the mod does not ship the assets and has already been
        ///     wrong about a prefab name. "The worst axe in the game" and "a better one" are
        ///     the two things these checks actually need, and both are questions about tiers.
        /// </remarks>
        private static GameObject FindAxe(int lowestTier, int highestTier)
        {
            if (ObjectDB.instance == null) return null;

            GameObject best = null;
            foreach (GameObject candidate in ObjectDB.instance.m_items)
            {
                if (candidate == null || !candidate.TryGetComponent(out ItemDrop drop)) continue;

                ItemDrop.ItemData.SharedData shared = drop.m_itemData?.m_shared;
                if (shared == null || shared.m_damages.m_chop <= 0f) continue;

                // Chop damage alone is not an axe. The first item in the database that can chop
                // is Abomination_attack1 - a creature's attack, and every villager this suite
                // ever armed was carrying one: it fells trees perfectly well and answers for
                // none of the things an axe answers for, so the chop checks passed while the
                // villager chopped with an invisible gesture. The skill is what says "a thing a
                // person swings", and it is asset data rather than a name.
                if (shared.m_skillType != Skills.SkillType.Axes) continue;
                if (shared.m_toolTier < lowestTier || shared.m_toolTier > highestTier) continue;

                best = candidate;
                break;
            }

            return best;
        }

        /// <summary>Puts an axe of a given tier range into a villager's bag.</summary>
        private static bool GiveAxe(Container bag, ZNetView view, int lowestTier, int highestTier)
        {
            GameObject axe = FindAxe(lowestTier, highestTier);
            if (bag == null || axe == null || !axe.TryGetComponent(out ItemDrop drop)) return false;

            // Item data taken straight off a prefab has no m_dropPrefab - the field is filled
            // in when an item passes through an inventory - so a clone of it has no identity
            // and nothing can name it.
            ItemDrop.ItemData planted = drop.m_itemData.Clone();
            planted.m_dropPrefab = axe;
            bag.GetInventory().AddItem(planted);
            VillagerInventory.Persist(bag, view);
            return true;
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
        ///     Negative controls. Each positive claim about claims, keep-alive, limits, or
        ///     compatibility is paired with a case that must fail, so a check cannot pass
        ///     merely because the mechanism never ran.
        /// </summary>
 
 
 
 
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

        /// <summary>
        ///     Registers a fixture the way a player does, then names it.
        /// </summary>
        /// <remarks>
        ///     Deliberately not a hand-built record. Constructing one directly skips the claim
        ///     and the durable token, so the benchmark would exercise a path no player can take
        ///     and would prove persistence for records that are not the ones the game creates.
        /// </remarks>
        /// <summary>
        ///     That the station contracts this mod calls are the ones the game registered.
        /// </summary>
        /// <remarks>
        ///     The signature failure of tending is a call that silently does nothing: an RPC
        ///     invoked with the wrong argument list throws inside the <em>owner's</em> handler,
        ///     which on a remote peer is an exception nobody here ever sees. The repo's own
        ///     decompiled reference and its findings document disagree about whether AddOre takes
        ///     two arguments or three, so the live station is asked rather than either of them.
        /// </remarks>
        /// <summary>
        ///     That "we have enough" actually stops a villager fetching.
        /// </summary>
        /// <remarks>
        ///     In two rounds against one fixture, because a villager that stops for the right
        ///     reason and one that never started look identical from outside. The second round
        ///     raises the same job's target past what the settlement holds, and the same
        ///     villager must go back to work.
        /// </remarks>
        private static IEnumerator CheckEnoughStopsTheTending(TestReport report, Colony colony,
            Vector3 origin)
        {
            SweepLooseItems(colony);
            SettlementIndex.ResetForTest();

            GameObject kiln = SpawnFirst(origin + new Vector3(-8f, 0f, -8f), "charcoal_kiln", "smelter");
            GameObject chest = Spawn("piece_chest_wood", origin + new Vector3(-5f, 0f, -5f));
            yield return new WaitForSecondsRealtime(.4f);

            StructureRecord station = Register(colony, kiln, "Enough kiln");
            StructureRecord store = Register(colony, chest, "Enough store");
            Smelter smelter = kiln != null ? kiln.GetComponentInChildren<Smelter>(true) : null;
            if (station == null || store == null || smelter == null)
            {
                report.Check(false, "control: the enough check could place and register its fixtures");
                yield break;
            }

            // What this station makes, asked of the station. The stopping rule counts the
            // product, and naming it here rather than reading it would be a fixture that keeps
            // working when the game changes what a kiln is for.
            string material = string.Empty;
            string product = string.Empty;
            foreach (Smelter.ItemConversion conversion in smelter.m_conversion)
            {
                if (conversion == null || conversion.m_from == null || conversion.m_to == null) continue;

                material = conversion.m_from.gameObject.name;
                product = conversion.m_to.gameObject.name;
                break;
            }

            Container box = chest.GetComponentInChildren<Container>(true);
            int wood = material.Length > 0 ? PutIn(box, material, 40) : 0;
            int coal = product.Length > 0 ? PutIn(box, product, 20) : 0;

            if (wood <= 0 || coal <= 0)
            {
                report.Check(false, "control: the enough check could stock what a kiln eats and makes",
                    $"material='{material}'x{wood} product='{product}'x{coal}");
                yield break;
            }

            ColonyOperations.EditSettings(colony, station.Id, s =>
            {
                s.Input = new List<string> { material };
                s.KeepFull = 1f;
                s.Accepts = new List<string> { material, product };
            });

            // Ten, against the twenty already in the chest. Nothing to do from the first tick.
            //
            // The order is on the station now, not on the job. That is the whole of the change:
            // a settlement with two kilns can want a hundred coal from one and nothing from the
            // other, which it could not say while the number lived on the work.
            ColonyOperations.EditSettings(colony, station.Id, s =>
                s.Orders.Add(new StructureOrder { Item = product, Count = 10 }));

            colony.State.SetJobs(new List<JobDefinition>
            {
                new JobDefinition { Id = "enough", Name = "Tend", Kind = JobKind.Tend, Repeat = 30 }
            });

            Villager hand = VillagerLifecycle.Spawn(colony);
            yield return null;
            if (hand == null || !hand.TryGetComponent(out ZNetView who) || !who.IsValid())
            {
                report.Check(false, "control: the enough check could spawn a villager");
                yield break;
            }

            SendRested(who);
            new VillagerState(who.GetZDO()).SetQueue(new List<string> { "enough" });

            report.Check(Stock.Held(colony, product) >= 10,
                "control: the settlement really does hold more than the job asked for",
                $"{product}={Stock.Held(colony, product)} target=10");

            int before = CountIn(box, material);
            for (int attempt = 0; attempt < 40; attempt++) yield return new WaitForSecondsRealtime(.5f);

            report.Check(CountIn(box, material) == before && smelter.GetQueueSize() == 0,
                "a job that has enough stops fetching for its station",
                $"{material} {before}->{CountIn(box, material)} queue={smelter.GetQueueSize()} " +
                $"did='{hand.Activity}'");

            // The control. Same fixture, same villager - only the line moves.
            ColonyOperations.EditSettings(colony, station.Id, s =>
            {
                StructureOrder order = s.Orders.Find(o => o.Item == product);
                if (order != null) order.Count = 999;
            });

            for (int attempt = 0; attempt < 60 && smelter.GetQueueSize() == 0; attempt++)
            {
                yield return new WaitForSecondsRealtime(.5f);
            }

            report.Check(smelter.GetQueueSize() > 0,
                "control: raising the line puts the same villager back to work",
                $"queue={smelter.GetQueueSize()} {material}={CountIn(box, material)} did='{hand.Activity}'");

            VillagerLifecycle.Remove(colony, who.GetZDO().m_uid);
            colony.State.SetJobs(new List<JobDefinition>());
            colony.RemoveStructure(station.Id);
            colony.RemoveStructure(store.Id);
            Release(kiln);
            Release(chest);
            SweepLooseItems(colony);
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>
        ///     A station missing what it needs refuses, and the same station with it accepts.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The two halves have to be the same station, because a refusal on its own
        ///         proves nothing: a villager that never found the bench at all, or a fixture
        ///         that failed to register, would produce exactly the same silence. Only the
        ///         second half says the refusal was a decision.
        ///     </para>
        ///     <para>
        ///         <b>The requirement is set on the instance rather than staged.</b> Building a
        ///         roof and lighting a fire in a fixture would be testing Valheim's cover and
        ///         heat systems, which work; what is under test is whether <em>our</em> copy of
        ///         CheckUsable reads them - it has to be a copy, because vanilla's takes a
        ///         Player and dereferences it. So the fixture flips the station's own flag,
        ///         which is the input that rule actually reads.
        ///     </para>
        /// </remarks>
        private static IEnumerator CheckAStationRefusesWhatItCannotDo(TestReport report, Colony colony,
            Vector3 origin)
        {
            SweepLooseItems(colony);
            SettlementIndex.ResetForTest();

            GameObject bench = SpawnFirst(origin + new Vector3(6f, 0f, -10f), "piece_workbench");
            yield return new WaitForSecondsRealtime(.4f);

            CraftingStation station = bench != null ? bench.GetComponentInChildren<CraftingStation>(true) : null;
            if (station == null || !Colonies.Stations.CraftProbe.TryFind(bench, out Colonies.Stations.CraftStation adapter))
            {
                report.Check(false, "control: the refusal check could place a crafting station",
                    $"bench={(bench != null)}");
                Release(bench);
                yield break;
            }

            // Out in the open with no fire, which is the state being asserted about.
            station.m_craftRequireRoof = false;
            station.m_craftRequireFire = true;
            yield return new WaitForSecondsRealtime(1.2f);

            bool refused = !adapter.Usable(out string why);
            report.Check(refused && why.Contains("fire"),
                "a station that needs a fire and has none refuses, and says which",
                $"usable={!refused} said='{why}'");

            station.m_craftRequireFire = false;
            bool accepted = adapter.Usable(out string nowWhy);
            report.Check(accepted, "control: the same station accepts once it no longer needs one",
                $"usable={accepted} said='{nowWhy}'");

            // Roof, the other half of the same rule, on the same station.
            station.m_craftRequireRoof = true;
            bool roofless = !adapter.Usable(out string roofWhy);
            report.Check(roofless && roofWhy.Contains("roof"),
                "and a station standing in the open under a roof rule refuses too",
                $"usable={!roofless} said='{roofWhy}'");

            station.m_craftRequireRoof = false;
            Release(bench);
            SweepLooseItems(colony);
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>
        ///     The whole loop: a villager makes what a station was told to make, a hauler files
        ///     it, the order fills, and the making stops.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Written as one check rather than four because the parts do not work
        ///         separately. What a crafter makes stays in its own bag, and a bag is not
        ///         registered storage - so the order counts nothing until a hauler has carried
        ///         it to a chest. A "does it craft" check with no hauler would pass while the
        ///         settlement quietly made things for ever.
        ///     </para>
        ///     <para>
        ///         <b>The materials are counted.</b> That is the guard against the trap this job
        ///         was written around: Inventory.RemoveItem returns void and silently skips
        ///         anything below the world's level, so on an NG+ world a villager would consume
        ///         nothing and produce everything, and every other assertion here would pass.
        ///     </para>
        ///     <para>
        ///         The recipe is read from the catalogue rather than named. A fixture that
        ///         hardcoded "make nails" would keep passing after Valheim moved nails to a
        ///         different bench, which is the same reason nothing else here names a prefab.
        ///     </para>
        /// </remarks>
        private static IEnumerator CheckACraftedOrderIsMadeFiledAndStops(TestReport report, Colony colony,
            Vector3 origin)
        {
            SweepLooseItems(colony);
            SettlementIndex.ResetForTest();

            GameObject bench = SpawnFirst(origin + new Vector3(7f, 0f, 7f), "piece_workbench");
            GameObject chest = Spawn("piece_chest_wood", origin + new Vector3(4f, 0f, 7f));
            yield return new WaitForSecondsRealtime(.4f);

            CraftingStation component = bench != null ? bench.GetComponentInChildren<CraftingStation>(true) : null;
            if (component == null)
            {
                report.Check(false, "control: the crafting check could place a station");
                Release(bench);
                Release(chest);
                yield break;
            }

            // Standing in a field, which is where a benchmark runs. The roof and fire rules have
            // their own check above; here they would only be a way to fail for a reason that is
            // not what is being measured.
            component.m_craftRequireRoof = false;
            component.m_craftRequireFire = false;

            StructureRecord station = Register(colony, bench, "Craft bench");
            StructureRecord store = Register(colony, chest, "Craft store");
            Container box = chest != null ? chest.GetComponentInChildren<Container>(true) : null;

            if (station == null || store == null || box == null)
            {
                report.Check(false, "control: the crafting check could register its fixtures",
                    $"station={(station != null)} store={(store != null)} box={(box != null)}");
                Release(bench);
                Release(chest);
                yield break;
            }

            Recipe recipe = null;
            CraftOption making = null;
            foreach (CraftOption option in CraftCatalogue.For(component.m_name, component.m_showBasicRecipies))
            {
                Recipe candidate = CraftCatalogue.RecipeFor(option.Item);
                if (candidate?.m_resources == null || option.MinLevel > 1) continue;
                if (candidate.m_resources.Length == 0 || candidate.m_resources.Length > 2) continue;

                recipe = candidate;
                making = option;
                break;
            }

            if (recipe == null)
            {
                report.Check(false, "control: this bench knows how to make something simple",
                    $"station='{component.m_name}' options={CraftCatalogue.For(component.m_name, component.m_showBasicRecipies).Count}");
                Release(bench);
                Release(chest);
                yield break;
            }

            // Six times what one craft needs, so running out is never the reason it stops.
            List<string> materials = new List<string>();
            List<int> each = new List<int>();
            bool stocked = true;

            foreach (Piece.Requirement requirement in recipe.m_resources)
            {
                if (requirement?.m_resItem == null) continue;

                int amount = requirement.GetAmount(1);
                if (amount <= 0) continue;

                string prefab = requirement.m_resItem.gameObject.name;
                materials.Add(prefab);
                each.Add(amount);
                if (PutIn(box, prefab, amount * 6) <= 0) stocked = false;
            }

            if (!stocked || materials.Count == 0)
            {
                report.Check(false, "control: the crafting check could stock what the recipe needs",
                    $"making='{making.Item}' materials={materials.Count}");
                Release(bench);
                Release(chest);
                yield break;
            }

            string product = making.Item;
            int target = Mathf.Max(2, making.Amount * 2);

            ColonyOperations.EditSettings(colony, store.Id,
                s => s.Accepts = new List<string>(materials) { product });

            ColonyOperations.EditSettings(colony, station.Id,
                s => s.Orders.Add(new StructureOrder { Item = product, Count = target }));

            colony.State.SetJobs(new List<JobDefinition>
            {
                new JobDefinition { Id = "make", Name = "Craft", Kind = JobKind.Craft, Repeat = 60 },
                new JobDefinition { Id = "fetch", Name = "Haul", Kind = JobKind.Haul, Repeat = 60 }
            });

            Villager maker = VillagerLifecycle.Spawn(colony);
            Villager mover = VillagerLifecycle.Spawn(colony);
            yield return null;

            if (maker == null || mover == null ||
                !maker.TryGetComponent(out ZNetView makerView) || !makerView.IsValid() ||
                !mover.TryGetComponent(out ZNetView moverView) || !moverView.IsValid())
            {
                report.Check(false, "control: the crafting check could spawn a maker and a hauler");
                yield break;
            }

            SendRested(makerView);
            SendRested(moverView);
            new VillagerState(makerView.GetZDO()).SetQueue(new List<string> { "make" });
            new VillagerState(moverView.GetZDO()).SetQueue(new List<string> { "fetch" });

            // Switched off first. A station out of service must be invisible, and asserting that
            // before anything works is what stops it passing because nothing happened yet.
            ColonyOperations.EditSettings(colony, station.Id, s => s.InService = false);

            List<int> before = new List<int>();
            foreach (string material in materials) before.Add(CountIn(box, material));

            for (int attempt = 0; attempt < 24; attempt++) yield return new WaitForSecondsRealtime(.5f);

            bool untouched = true;
            for (int i = 0; i < materials.Count; i++) untouched &= CountIn(box, materials[i]) == before[i];

            report.Check(untouched && CountIn(box, product) == 0,
                "a station out of service is not worked",
                $"product={CountIn(box, product)} did='{maker.Activity}'");

            ColonyOperations.EditSettings(colony, station.Id, s => s.InService = true);

            for (int attempt = 0; attempt < 120 && CountIn(box, product) < target; attempt++)
            {
                yield return new WaitForSecondsRealtime(.5f);
            }

            int filed = CountIn(box, product);
            report.Check(filed >= target,
                "control: back in service, the order is made and a hauler files it",
                $"{product}={filed} target={target} maker='{maker.Activity}' hauler='{mover.Activity}'");

            // The materials really went. Everything above passes just as well for a settlement
            // crafting out of thin air.
            bool spent = true;
            string ledger = string.Empty;
            for (int i = 0; i < materials.Count; i++)
            {
                int now = CountIn(box, materials[i]) + CountIn(maker.Bag, materials[i]);
                ledger += $"{materials[i]} {before[i]}->{now} ";
                spent &= now < before[i];
            }

            report.Check(spent && filed > 0, "and the materials it used are gone", ledger);

            // Made enough, so it stops. Watched past the point rather than asserted at it: a
            // job that never stopped would be indistinguishable at the moment it arrived.
            int settled = CountIn(box, product);
            for (int attempt = 0; attempt < 24; attempt++) yield return new WaitForSecondsRealtime(.5f);

            int after = CountIn(box, product);
            report.Check(after <= settled + making.Amount,
                "and having made enough, it stops",
                $"{product} {settled}->{after} did='{maker.Activity}'");

            VillagerLifecycle.Remove(colony, makerView.GetZDO().m_uid);
            VillagerLifecycle.Remove(colony, moverView.GetZDO().m_uid);
            colony.State.SetJobs(new List<JobDefinition>());
            colony.RemoveStructure(station.Id);
            colony.RemoveStructure(store.Id);
            Release(bench);
            Release(chest);
            SweepLooseItems(colony);
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>
        ///     That an axe swing is actually seen, rather than merely asked for.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>Asserting that we called the setter proves nothing.</b> Every part of this
        ///         can be right - the rig has the trigger, the trigger fires, the RPC lands - and
        ///         the villager still chops invisibly, because an animator only leaves its
        ///         current state if some transition out of it accepts the parameters as they
        ///         stand. So this watches the animator itself: what state each layer is in before
        ///         the swing, and whether any of them moved.
        ///     </para>
        ///     <para>
        ///         The control is the same window with no swing in it. Without that, a villager
        ///         that happened to be drifting between idle states would pass this for ever.
        ///     </para>
        /// </remarks>
        private static IEnumerator CheckTheAxeSwingIsSeen(TestReport report, Colony colony, Vector3 origin)
        {
            Villager villager = VillagerLifecycle.Spawn(colony);
            yield return new WaitForSecondsRealtime(.5f);

            if (villager == null || !villager.TryGetComponent(out ZNetView view) || !view.IsValid())
            {
                report.Check(false, "control: the swing check could spawn a villager");
                yield break;
            }

            ZDOID who = view.GetZDO().m_uid;
            Animator animator = villager.GetComponentInChildren<Animator>();
            VillagerAnimation rig = villager.AnimationForTest;

            if (animator == null || rig == null)
            {
                report.Check(false, "control: the villager has an animator to watch",
                    $"animator={(animator != null)} rig={(rig != null)}");
                VillagerLifecycle.Remove(colony, who);
                yield break;
            }

            report.Check(rig.CanSwing, "the rig has an axe swing to play",
                $"swing='{rig.SwingName}' layers={animator.layerCount}");

            Container bag = villager.GetComponentInChildren<Container>(true);
            bool armed = bag != null && GiveAxe(bag, view, 0, 99);
            report.Check(armed, "control: the villager was given an axe to swing");
            if (!armed)
            {
                VillagerLifecycle.Remove(colony, who);
                yield break;
            }

            ItemDrop.ItemData axe = Wielded(bag, FindAxe(0, 99));

            // What standing still looks like, sampled rather than assumed - every state the rig
            // visits while doing nothing. Comparing against a single instant instead let a
            // villager drifting between two idle states pass this check while swinging nothing:
            // any change counted, including a change straight back.
            // By the name of the clip actually playing, not by a state hash. A hash tells you
            // something changed; it does not tell you into what, and this rig's layer 0 drifts
            // between several resting states on its own - which passed an earlier version of
            // this check while the photograph showed a villager standing perfectly still.
            HashSet<string> quiet = new HashSet<string>();
            for (int frame = 0; frame < 90; frame++)
            {
                yield return null;
                Collect(animator, quiet);
            }

            List<string> resting = new List<string>(quiet);
            string idle = string.Join(", ", resting.ToArray());

            // Held and swung in the same frame, with nothing yielded between. A villager whose
            // current job is not chopping puts its axe away every tick - rig included - so a
            // hold asserted a moment earlier is tidied away before the swing can use it. That
            // is why the production swing reasserts the hold itself rather than trusting one
            // set earlier, and this has to do the same or it would be testing something the
            // game never does.
            rig.Hold(axe);
            bool held = rig.Holding != 0;
            rig.Swing();

            report.Check(held,
                "the rig is told it is holding something in the same breath as the swing",
                $"statei={rig.Holding} axe='{(axe == null ? "none" : Carrying.NameOf(axe))}' " +
                $"layers={Weights(animator)}");

            // Whatever it plays over the next few seconds, named. Not "something changed":
            // this rig settles out of Standing Up into IdleTweaked on its own, and a check
            // that accepted any new clip called that a swing while the photograph showed a
            // villager standing still.
            HashSet<string> afterSwing = new HashSet<string>();
            for (int frame = 0; frame < 90; frame++)
            {
                yield return null;

                // Held again on every frame, because a chopping villager does: the job takes up
                // its axe each time it resolves one, so the rig stays told. This villager has no
                // job, and its own tick puts the axe away twenty times a second - so without
                // this the state is cleared out from under the swing a frame after it fires.
                rig.Hold(axe);
                Collect(animator, afterSwing);

                if (Swung(afterSwing))
                {
                    yield return BenchmarkUiScenario.PhotographAtWork("swing-axe.png",
                        villager.transform.position, "a villager mid axe-swing");
                    break;
                }
            }

            // The same request again, put straight to the animator rather than through the
            // synced wrapper. This is a diagnosis and not a fix: if the clip plays here and not
            // above, the controller is fine and the replication is not; if it plays in neither,
            // the trigger has no transition to take and no amount of firing it will help.
            HashSet<string> direct = new HashSet<string>();
            animator.SetTrigger(rig.SwingName);
            for (int frame = 0; frame < 60; frame++)
            {
                yield return null;
                rig.Hold(axe);
                Collect(animator, direct);
                if (Swung(direct)) break;
            }

            report.Check(quiet.Count > 0,
                "control: standing still was sampled, so there is something to compare against",
                $"clips while idle={quiet.Count} ({idle})");

            report.Check(Swung(afterSwing),
                "swinging an axe plays a swing",
                $"after the swing it played: {Join(afterSwing)}. idle was: {idle}. " +
                $"statei={rig.Holding} trigger='{rig.SwingName}' layers={Weights(animator)}");

            report.Check(!Swung(direct) || Swung(afterSwing),
                "diagnosis: if the rig can swing at all, the synced path is what asks it to",
                $"straight at the animator it played: {Join(direct)}");

            // Which of the rig's attack triggers is actually wired to a transition. Having a
            // parameter and having a state machine that listens to it are different things, and
            // this rig carries the player's entire parameter list - a hundred and fifty names,
            // most of which belong to weapons it will never hold. Asked rather than assumed,
            // once, so the answer comes from the rig.
            System.Text.StringBuilder wired = new System.Text.StringBuilder();
            foreach (string candidate in new[]
                     {
                         "swing_axe", "swing_axe0", "swing_axe1", "swing_axe2", "axe_secondary",
                         "swing_pickaxe", "swing_hammer", "unarmed_attack0", "interact"
                     })
            {
                if (!rig.Has(candidate, AnimatorControllerParameterType.Trigger)) continue;

                HashSet<string> played = new HashSet<string>();
                animator.SetTrigger(candidate);
                for (int frame = 0; frame < 45; frame++)
                {
                    yield return null;
                    rig.Hold(axe);
                    Collect(animator, played);
                }

                played.ExceptWith(quiet);
                wired.Append(candidate).Append("->")
                    .Append(played.Count == 0 ? "nothing" : Join(played)).Append("  ");
            }

            report.Check(Swung(afterSwing),
                "diagnosis: what each of this rig's attack triggers plays, for when this breaks again",
                wired.ToString());

            yield return CheckWatchingGivesTheCameraBack(report, villager);

            VillagerLifecycle.Remove(colony, who);
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>
        ///     That watching a villager borrows the camera and hands it back.
        /// </summary>
        /// <remarks>
        ///     The borrowing is the easy half. Handing it back is the half that strands a player
        ///     looking at somebody else's shoulder with no way out, so it is asserted as its own
        ///     claim: the game's camera is off while watching, on again afterwards, and the view
        ///     returns to where it was.
        /// </remarks>
        private static IEnumerator CheckWatchingGivesTheCameraBack(TestReport report, Villager villager)
        {
            if (GameCamera.instance == null)
            {
                report.Check(false, "control: there is a camera to borrow");
                yield break;
            }

            Transform lens = GameCamera.instance.transform;
            Vector3 before = lens.position;

            bool started = WatchCamera.Watch(villager.Id, "the watched villager");
            yield return null;
            yield return null;

            float away = Utils.DistanceXZ(lens.position, villager.transform.position);
            report.Check(started && !GameCamera.instance.enabled && away <= 40f,
                "watching a villager points the camera at them and takes the game's own off",
                $"started={started} gameCamera={GameCamera.instance.enabled} away={away:0.#}m");

            WatchCamera.StopWatching();
            yield return null;

            report.Check(GameCamera.instance.enabled && !WatchCamera.Watching,
                "and gives it back, rather than stranding a player behind somebody's shoulder",
                $"gameCamera={GameCamera.instance.enabled} watching={WatchCamera.Watching} " +
                $"returned={Utils.DistanceXZ(before, lens.position):0.#}m");

            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>Whether any of these clips is an attack rather than a way of standing.</summary>
        /// <remarks>
        ///     By name, because the names are the only thing here a person can check against the
        ///     photograph beside them. Valheim's own clips for this are "Axe swing" and its
        ///     numbered variants; matching loosely is right for a rig that may carry a modded
        ///     controller, and a false match would be visible in the report next to the frame.
        /// </remarks>
        private static bool Swung(HashSet<string> clips)
        {
            foreach (string clip in clips)
            {
                string name = clip.ToLowerInvariant();
                if (name.Contains("swing") || name.Contains("attack") || name.Contains("chop")) return true;
            }

            return false;
        }

        private static string Join(HashSet<string> clips) =>
            clips.Count == 0 ? "nothing" : string.Join(", ", new List<string>(clips).ToArray());

        /// <summary>How far a villager must move between samples for the step to count.</summary>
        /// <remarks>
        ///     Above the jitter of standing on uneven ground, below anything that could be
        ///     called covering distance.
        /// </remarks>
        private const float SlidingStep = 1f;

        /// <summary>
        ///     Whether this villager's legs are actually going.
        /// </summary>
        /// <remarks>
        ///     Asked of the rig, because every other way of asking is a flag that a sliding
        ///     villager satisfies: it is not reckoning, it is not stalled, it is getting closer.
        ///     A body covering ground in an idle pose is the one failure those cannot see, and
        ///     the clip playing is what tells them apart. Matched loosely by name - this rig's
        ///     locomotion clips are "Jog New", "Jog backward" and their walking relatives - so a
        ///     modded controller answers for itself.
        /// </remarks>
        private static bool Walking(Villager villager)
        {
            Animator animator = villager != null ? villager.GetComponentInChildren<Animator>() : null;
            if (animator == null) return false;

            HashSet<string> playing = new HashSet<string>();
            Collect(animator, playing);

            foreach (string clip in playing)
            {
                string name = clip.ToLowerInvariant();
                if (name.Contains("jog") || name.Contains("walk") || name.Contains("run")) return true;
            }

            return false;
        }

        /// <summary>Adds the name of every clip playing right now to a set.</summary>
        private static void Collect(Animator animator, HashSet<string> into)
        {
            for (int layer = 0; layer < animator.layerCount; layer++)
            {
                foreach (AnimatorClipInfo playing in animator.GetCurrentAnimatorClipInfo(layer))
                {
                    if (playing.clip != null) into.Add(playing.clip.name);
                }
            }
        }

        /// <summary>
        ///     Each layer's weight, because a clip on a layer weighted zero plays invisibly.
        /// </summary>
        private static string Weights(Animator animator)
        {
            System.Text.StringBuilder weights = new System.Text.StringBuilder();
            for (int layer = 0; layer < animator.layerCount; layer++)
            {
                weights.Append(layer).Append('=').Append(animator.GetLayerWeight(layer)).Append(' ');
            }

            return weights.ToString();
        }

        /// <summary>The axe this check put in the bag, found by the prefab it was made from.</summary>
        private static ItemDrop.ItemData Wielded(Container bag, GameObject axe)
        {
            Inventory inventory = bag != null ? bag.GetInventory() : null;
            if (inventory == null || axe == null) return null;

            string wanted = Utils.GetPrefabName(axe);
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
            {
                if (item != null && Carrying.NameOf(item) == wanted) return item;
            }

            return null;
        }

        private static IEnumerator CheckTheStationContract(TestReport report, Vector3 origin)
        {
            GameObject chest = Spawn("piece_chest_wood", origin + new Vector3(6f, 0f, -6f));
            yield return new WaitForSecondsRealtime(.3f);

            Container bag = chest != null ? chest.GetComponentInChildren<Container>(true) : null;
            report.Check(bag != null, "control: the contract check has somewhere to take items from");
            if (bag == null) yield break;

            yield return Contract(report, bag, origin + new Vector3(9f, 0f, -6f), "smelter");
            yield return Contract(report, bag, origin + new Vector3(12f, 0f, -6f), "piece_cookingstation");
            yield return Contract(report, bag, origin + new Vector3(15f, 0f, -6f), "fermenter");

            Release(chest);
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>
        ///     One station, fed for real, with its own numbers read back.
        /// </summary>
        /// <remarks>
        ///     The argument shapes are the thing at issue and a name check cannot reach them:
        ///     <c>ZNetView.m_functions</c> is keyed by name hash alone, so a handler registered
        ///     for a string and invoked with an int passes every test that only asks whether the
        ///     name is there. The three stations disagree about this in a way nothing else in
        ///     the game does - ore by name, fermenter items by hash - and the repo's own
        ///     decompiled reference contradicts the shipped assembly about which. So this puts a
        ///     real item in and asks the station whether it arrived.
        /// </remarks>
        private static IEnumerator Contract(TestReport report, Container bag, Vector3 where, string prefabName)
        {
            GameObject placed = SpawnFirst(where, prefabName);
            yield return new WaitForSecondsRealtime(.4f);

            if (placed == null)
            {
                report.Check(false, $"control: '{prefabName}' could be placed for the contract check");
                yield break;
            }

            if (!Colonies.Stations.StationProbe.TryFind(placed, out Colonies.Stations.StationProtocol protocol))
            {
                report.Check(false, $"'{prefabName}' is recognised as a station by its component",
                    $"components={StructureRegistry.Explain(placed)}");
                Release(placed);
                yield break;
            }

            if (placed.TryGetComponent(out ZNetView view) && view.IsValid()) view.ClaimOwnership();
            yield return new WaitForSecondsRealtime(.4f);

            List<string> inputs = ProcessingOptions.Inputs(Utils.GetPrefabName(placed));
            string material = inputs.Count > 0 ? inputs[0] : string.Empty;
            int seeded = material.Length > 0 ? PutIn(bag, material, 5) : 0;

            if (seeded <= 0)
            {
                report.Check(false, $"control: '{prefabName}' converts something this run could carry",
                    $"inputs={inputs.Count} material='{material}'");
                Release(placed);
                yield break;
            }

            ItemDrop.ItemData item = bag.GetInventory().GetItem(material, isPrefabName: true);
            Colonies.Stations.FeedResult result = item == null
                ? Colonies.Stations.FeedResult.Unavailable
                : protocol.Give(bag.GetInventory(), item, false);

            // Waiting means ownership had not landed yet, which is a legitimate answer on the
            // first ask rather than a verdict. One retry, then it counts.
            if (result == Colonies.Stations.FeedResult.Waiting)
            {
                yield return new WaitForSecondsRealtime(.6f);
                item = bag.GetInventory().GetItem(material, isPrefabName: true);
                if (item != null) result = protocol.Give(bag.GetInventory(), item, false);
            }

            report.Check(result == Colonies.Stations.FeedResult.Fed,
                $"a {prefabName} takes what this mod hands it, and its own numbers say so",
                $"result={result} material='{material}' kind={protocol.Kind}");

            // The other half of the same claim: what it was handed actually left the bag. A
            // station that reported success while the item stayed put would be worse than one
            // that refused.
            report.Check(CountIn(bag, material) < seeded,
                $"control: feeding a {prefabName} spends exactly what it was given",
                $"held={CountIn(bag, material)} seeded={seeded}");

            Release(placed);
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>
        ///     The claim this whole job is arranged around: a station with nothing to do is not
        ///     fed.
        /// </summary>
        /// <remarks>
        ///     In two rounds against one fixture, because the first round alone passes for a
        ///     villager that never found the station at all. Round two queues ore into the same
        ///     smelter and runs the same villager for the same time; the fuel must move then.
        /// </remarks>
        private static IEnumerator CheckAnIdleSmelterIsNotStoked(TestReport report, Colony colony,
            Vector3 origin)
        {
            SweepLooseItems(colony);
            SettlementIndex.ResetForTest();

            GameObject furnace = SpawnFirst(origin + new Vector3(8f, 0f, 4f), "smelter");
            GameObject chest = Spawn("piece_chest_wood", origin + new Vector3(5f, 0f, 7f));
            yield return new WaitForSecondsRealtime(.4f);

            StructureRecord station = Register(colony, furnace, "Furnace");
            StructureRecord store = Register(colony, chest, "Coal store");
            if (station == null || store == null || furnace == null)
            {
                report.Check(false, "control: the idle-smelter check could place and register its fixtures");
                yield break;
            }

            Smelter smelter = furnace.GetComponentInChildren<Smelter>(true);
            string fuel = ProcessingOptions.Fuel(station.Prefab);
            if (smelter == null || fuel.Length == 0)
            {
                report.Check(false, "control: the fixture is a station that burns something",
                    $"smelter={(smelter != null)} fuel='{fuel}'");
                yield break;
            }

            // Both on the station now. What it is fed and what a villager may carry to it are
            // facts about this smelter, which is the whole point of the move.
            ColonyOperations.EditSettings(colony, station.Id, s =>
            {
                s.Fuel = new List<string> { fuel };
                s.Carries = StationCargo.Fuel;
            });
            Container box = chest.GetComponentInChildren<Container>(true);
            int seeded = PutIn(box, fuel, 40);

            colony.State.SetJobs(new List<JobDefinition>
            {
                new JobDefinition
                {
                    Id = "tend", Name = "Tend", Kind = JobKind.Tend, Repeat = 30
                }
            });

            Villager hand = VillagerLifecycle.Spawn(colony);
            yield return null;
            if (hand == null || !hand.TryGetComponent(out ZNetView who) || !who.IsValid() || seeded <= 0)
            {
                report.Check(false, "control: the idle-smelter check could spawn a villager with fuel to carry",
                    $"villager={(hand != null)} seeded={seeded}");
                yield break;
            }

            SendRested(who);
            new VillagerState(who.GetZDO()).SetQueue(new List<string> { "tend" });

            // The fixtures answer before anything is timed. A control that found nothing to do
            // is not a control.
            bool offered = SettlementIndex.WhatWantsFeeding(colony, hand.transform.position)
                .Exists(r => r.Id == station.Id);
            bool stocked = SettlementIndex.WhereIsItKept(colony, fuel, hand.transform.position)
                .Exists(r => r.Id == store.Id);
            report.Check(offered && stocked,
                "control: the settlement offers this station and knows where its fuel is",
                $"offered={offered} stocked={stocked}");

            int coalBefore = CountIn(box, fuel);
            float fuelBefore = smelter.GetFuel();

            // Where it was standing when it ran out of things to do. A villager with nothing to
            // do should be standing still, and for a long time it was not: saying "nothing to
            // haul" does not stop a Valheim character, which keeps the direction it was last
            // given until something takes it back - so one walked off in a straight line and
            // was found swimming out to sea. This is the half minute in which that shows.
            Vector3 stood = hand.transform.position;

            for (int attempt = 0; attempt < 60; attempt++) yield return new WaitForSecondsRealtime(.5f);

            float drifted = Utils.DistanceXZ(stood, hand.transform.position);
            report.Check(drifted <= 4f,
                "a villager with nothing to do stays where it is",
                $"drifted={drifted:0.#}m while saying '{hand.Activity}'");

            int coalIdle = CountIn(box, fuel);
            float fuelIdle = smelter.GetFuel();
            report.Check(coalIdle == coalBefore && fuelIdle <= fuelBefore,
                "a smelter with nothing to smelt is not stoked",
                $"coal {coalBefore}->{coalIdle} fuel {fuelBefore:0.#}->{fuelIdle:0.#} did='{hand.Activity}'");

            // The control. The same station, the same villager, the same half minute - and now
            // there is something to burn for.
            List<string> inputs = ProcessingOptions.Inputs(station.Prefab);
            string ore = inputs.Count > 0 ? inputs[0] : string.Empty;
            if (ore.Length == 0)
            {
                report.Check(false, "control: the fixture smelter converts something");
                yield break;
            }

            if (furnace.TryGetComponent(out ZNetView furnaceView) && furnaceView.IsValid())
            {
                furnaceView.ClaimOwnership();
                yield return new WaitForSecondsRealtime(.3f);
                for (int i = 0; i < 3; i++) furnaceView.InvokeRPC("RPC_AddOre", ore, false);
            }

            yield return new WaitForSecondsRealtime(1f);
            int queued = smelter.GetQueueSize();

            for (int attempt = 0; attempt < 60; attempt++) yield return new WaitForSecondsRealtime(.5f);

            int coalWorking = CountIn(box, fuel);
            report.Check(queued > 0 && coalWorking < coalIdle,
                "control: the same villager does stoke the same smelter once it has ore",
                $"queued={queued} coal {coalIdle}->{coalWorking} fuel={smelter.GetFuel():0.#} " +
                $"did='{hand.Activity}'");

            yield return BenchmarkUiScenario.PhotographAtWork("tend-stoking.png", furnace.transform.position,
                "a villager keeping a working smelter fuelled", hand.transform.position);

            VillagerLifecycle.Remove(colony, who.GetZDO().m_uid);
            colony.State.SetJobs(new List<JobDefinition>());
            colony.RemoveStructure(station.Id);
            colony.RemoveStructure(store.Id);
            Release(furnace);
            Release(chest);
            SweepLooseItems(colony);
            yield return new WaitForSecondsRealtime(.2f);
        }

        /// <summary>
        ///     The spec's "done when": a kiln is filled to the line and then left alone.
        /// </summary>
        /// <remarks>
        ///     "It stops" is the claim, and the only way to test a stop is to keep watching after
        ///     it should have happened - so the villager runs on for a further quarter minute and
        ///     the queue is asserted not to have moved past the target.
        /// </remarks>
        private static IEnumerator CheckAKilnIsKeptHalfFullAndStops(TestReport report, Colony colony,
            Vector3 origin)
        {
            SweepLooseItems(colony);
            SettlementIndex.ResetForTest();

            GameObject kiln = SpawnFirst(origin + new Vector3(-8f, 0f, 4f), "charcoal_kiln", "smelter");
            GameObject chest = Spawn("piece_chest_wood", origin + new Vector3(-5f, 0f, 7f));
            yield return new WaitForSecondsRealtime(.4f);

            StructureRecord station = Register(colony, kiln, "Kiln");
            StructureRecord store = Register(colony, chest, "Wood store");
            if (station == null || store == null || kiln == null)
            {
                report.Check(false, "control: the kiln check could place and register its fixtures");
                yield break;
            }

            Smelter smelter = kiln.GetComponentInChildren<Smelter>(true);
            List<string> inputs = ProcessingOptions.Inputs(station.Prefab);
            string material = inputs.Count > 0 ? inputs[0] : string.Empty;
            if (smelter == null || material.Length == 0)
            {
                report.Check(false, "control: the fixture kiln converts something",
                    $"smelter={(smelter != null)} material='{material}'");
                yield break;
            }

            int target = StationAppetite.TargetQueue(smelter.m_maxOre, .5f);
            ColonyOperations.EditSettings(colony, station.Id, s =>
            {
                s.Input = new List<string> { material };
                s.KeepFull = .5f;
            });

            Container box = chest.GetComponentInChildren<Container>(true);
            int seeded = PutIn(box, material, smelter.m_maxOre * 2);

            colony.State.SetJobs(new List<JobDefinition>
            {
                new JobDefinition { Id = "tend", Name = "Tend", Kind = JobKind.Tend, Repeat = 30 }
            });

            Villager hand = VillagerLifecycle.Spawn(colony);
            yield return null;
            if (hand == null || !hand.TryGetComponent(out ZNetView who) || !who.IsValid() ||
                seeded <= 0 || target <= 0)
            {
                report.Check(false, "control: the kiln check could spawn a villager with material to carry",
                    $"villager={(hand != null)} seeded={seeded} target={target}");
                yield break;
            }

            SendRested(who);
            new VillagerState(who.GetZDO()).SetQueue(new List<string> { "tend" });

            int before = CountIn(box, material);
            int highest = 0;
            List<string> story = new List<string>();

            for (int attempt = 0; attempt < 240 && smelter.GetQueueSize() < target; attempt++)
            {
                yield return new WaitForSecondsRealtime(.5f);
                if (smelter.GetQueueSize() > highest) highest = smelter.GetQueueSize();
                if (story.Count == 0 || story[story.Count - 1] != hand.Activity) story.Add(hand.Activity);
            }

            report.Check(smelter.GetQueueSize() >= target,
                "a villager fills a kiln to the level its screen promises",
                $"queue={smelter.GetQueueSize()}/{target} did='{string.Join(" > ", story.ToArray())}'");

            yield return BenchmarkUiScenario.PhotographAtWork("tend-loading.png", kiln.transform.position,
                "a villager loading a charcoal kiln", hand.transform.position);

            // The half that proves it stops. Kept watching, because a job that never stopped
            // would pass every assertion above.
            for (int attempt = 0; attempt < 30; attempt++)
            {
                yield return new WaitForSecondsRealtime(.5f);
                if (smelter.GetQueueSize() > highest) highest = smelter.GetQueueSize();
            }

            report.Check(highest <= target,
                "and stops there rather than filling it to the top",
                $"highest={highest} target={target} capacity={smelter.m_maxOre}");

            // Not "exactly the target", which was the first shape of this check and was simply
            // wrong: a working kiln *consumes* its queue while it is being watched, so topping
            // it back up is the job doing what "keep it half full" asks rather than the job
            // over-fetching. What is worth asserting is that it filled the thing and that the
            // chest paid for what the kiln holds - the ceiling is the check above, which
            // watched the queue and saw it never pass the target.
            int taken = before - CountIn(box, material);
            int held = smelter.GetQueueSize();

            // Three things, each of which can fail on its own: it filled the kiln to the line;
            // it is not over-filled now; and nothing vanished on the way, because a kiln cannot
            // hold more than the chest gave up. What it cannot assert is equality - a working
            // kiln burns its queue while it is watched, and topping it back up is the job doing
            // what "keep it half full" asks rather than the job over-fetching.
            report.Check(taken >= target && held <= target && taken >= held,
                "what left the chest is accounted for by what the kiln holds and has burned",
                $"taken={taken} holding={held} burned={taken - held} target={target}");

            VillagerLifecycle.Remove(colony, who.GetZDO().m_uid);
            colony.State.SetJobs(new List<JobDefinition>());
            colony.RemoveStructure(station.Id);
            colony.RemoveStructure(store.Id);
            Release(kiln);
            Release(chest);
            SweepLooseItems(colony);
            yield return new WaitForSecondsRealtime(.2f);
        }

        private static StructureRecord Register(Colony colony, GameObject target, string name)
        {
            RegisterOutcome outcome = ColonyOperations.Register(colony, target);
            if (outcome != RegisterOutcome.Registered && outcome != RegisterOutcome.Moved)
            {
                Core.Log.Warning($"[Benchmark] fixture '{name}' was not registered: {outcome}");
                return null;
            }

            if (!target.TryGetComponent(out ZNetView view) || !view.IsValid()) return null;
            ColonyOperations.RenameStructure(colony, view.GetZDO().m_uid, name);
            return colony.State.GetStructures().Find(r => r.Id == view.GetZDO().m_uid);
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

        /// <summary>
        ///     The flag: claimed from afar, extending reach, and holding its ground open.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Placed well beyond the hearth's radius on purpose - a flag inside reach
        ///         would prove nothing, since ordinary registration already covers that
        ///         ground. Every positive claim here has the control the roadmap demands:
        ///         the same chest is measured before the flag exists, beside it, and after
        ///         the flag is gone.
        ///     </para>
        ///     <para>
        ///         Claiming goes through <see cref="ColonyOperations.AssignFlag" />, the
        ///         ZDO-based route, because that is the one a real outpost uses - the hearth
        ///         three hundred metres away is not loaded when a player stands at the flag.
        ///         The loaded-screen routes call the same registration underneath, which is
        ///         the standing rule that no route can accept what another refuses.
        ///     </para>
        /// </remarks>
        private static IEnumerator CheckWorkFlags(TestReport report, Colony colony, Vector3 origin)
        {
            // Beyond the colony's reach, which is all this check is about - and no further.
            // Ninety metres past the radius put the fixtures a hundred and thirty-eight metres
            // out, where there is no loaded ground for a piece to stand on: both objects
            // destroyed themselves in Awake and arrived here as null references, which reads
            // exactly like a prefab that does not exist. Twenty-five metres past the edge is
            // just as out of reach and is somewhere things can be built.
            Vector3 farOut = origin + new Vector3(colony.EffectiveRadius + 25f, 0f, 0f);

            // Control first: without any flag, ground out there is nobody's.
            GameObject strayChest = Spawn("piece_chest_wood", farOut + new Vector3(4f, 0f, 0f));
            yield return new WaitForSecondsRealtime(.3f);

            StructureRecord strayRecord = StructureRegistry.Describe(strayChest);
            RegisterOutcome strayOutcome = strayChest == null
                ? RegisterOutcome.NotUsable
                : ColonyOperations.Register(colony, strayChest);

            report.Check(strayChest != null && strayRecord != null &&
                         strayOutcome == RegisterOutcome.OutOfReach,
                "control: without a flag, a distant chest is refused as out of reach",
                $"chest={(strayChest != null)} record={(strayRecord != null)} outcome={strayOutcome} " +
                $"at {farOut.x:0},{farOut.z:0} which is {Utils.DistanceXZ(farOut, origin):0}m out, " +
                $"radius={colony.EffectiveRadius:0}");

            GameObject flag = Spawn(WorkFlagPrefab.PrefabName, farOut);
            yield return new WaitForSecondsRealtime(.3f);

            WorkFlag planted = flag != null ? flag.GetComponentInChildren<WorkFlag>(true) : null;
            if (planted == null)
            {
                report.Check(false, "flag check could place a flag",
                    $"prefab={(ZNetScene.instance.GetPrefab(WorkFlagPrefab.PrefabName) != null)} " +
                    $"instance={(flag != null)} " +
                    $"components={(flag == null ? "none" : StructureRegistry.Explain(flag))}");
                Release(strayChest);
                yield break;
            }

            report.Check(planted.Owner.IsNone(),
                "control: a freshly planted flag belongs to nobody");

            RegisterOutcome claimed = ColonyOperations.AssignFlag(colony.Id, planted);
            report.Check(claimed == RegisterOutcome.Registered,
                "a flag claims to a Kolony from beyond its reach",
                $"outcome={claimed}");

            report.Check(colony.State.GetStructures().Exists(r =>
                    r.Id == planted.Id && (r.Capabilities & StructureCapability.WorkArea) != 0),
                "and lands in the structure list as a work area");

            // The whole point: ground inside the flag's radius is the Kolony's now.
            SettlementIndex.ResetForTest();
            RegisterOutcome chestOutcome = ColonyOperations.Register(colony, strayChest);
            report.Check(chestOutcome == RegisterOutcome.Registered,
                "a chest beside the flag registers, three hundred-odd metres from home",
                $"outcome={chestOutcome}");

            StructureRecord chestRecord = colony.State.GetStructures()
                .Find(r => r.Id == strayChest.GetComponent<ZNetView>().GetZDO().m_uid);
            report.Check(chestRecord != null && chestRecord.StatusIn(colony) == StructureStatus.Ready,
                "and it is Ready - the hauling index can see it",
                $"status={chestRecord?.StatusIn(colony)}");

            // The keep-alive holds the flag's ground. Asked of the zone set directly: the
            // question is "would this survive nobody being here", which watching it while
            // being here cannot answer.
            // Waited for, not sampled. The registry is a sweep that runs as a coroutine, so
            // asking the instant after planting a flag asks a list that is still being built -
            // which reads as "the keep-alive does not know about this flag" when the truth is
            // "not yet". The driver asks for the sweep itself now; this waits for that answer.
            var areas = new List<Vector4>();
            for (int attempt = 0; attempt < 40 && areas.Count == 0; attempt++)
            {
                yield return new WaitForSecondsRealtime(.5f);
                areas.Clear();
                ColonyRegistry.CollectAreas(areas);
            }
            bool flagArea = areas.Exists(a =>
                Utils.DistanceXZ(new Vector3(a.x, 0f, a.z), farOut) < 2f && a.w >= 8f);
            report.Check(flagArea,
                "the keep-alive knows the flag's circle",
                $"areas={areas.Count}");

            report.Check(KeepAlive.KeepAliveZones.DroppedAnchors == 0,
                "control: nothing was silently dropped at the zone cap",
                $"dropped={KeepAlive.KeepAliveZones.DroppedAnchors}");

            // Reassigning is a move, not a duplicate - and the flag being gone takes the
            // ground with it: the chest survives as a record but drops to out of reach,
            // because falling out of reach never deregisters.
            colony.RemoveStructure(planted.Id);
            Release(flag);
            SettlementIndex.ResetForTest();
            yield return new WaitForSecondsRealtime(.3f);

            report.Check(chestRecord != null && chestRecord.StatusIn(colony) == StructureStatus.OutOfReach,
                "with the flag gone the chest is out of reach, not forgotten",
                $"status={chestRecord?.StatusIn(colony)}");

            report.Check(colony.State.GetStructures().Exists(r => r.Id == chestRecord.Id),
                "control: it is still on the books for when the flag comes back");

            colony.RemoveStructure(chestRecord.Id);
            Release(strayChest);
            yield return new WaitForSecondsRealtime(.2f);
        }

        private static GameObject Spawn(string prefabName, Vector3 position)
        {
            GameObject prefab = ZNetScene.instance.GetPrefab(prefabName);
            if (prefab == null) return null;
            position.y = ZoneSystem.instance.GetSolidHeight(position) + .2f;
            return UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity);
        }
    }
}
