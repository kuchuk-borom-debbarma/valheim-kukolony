using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Kukolony.Colonies;
using Kukolony.Jobs;
using Kukolony.Gui;
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
            yield return CheckTidying(report, colony, origin);
            yield return CheckWorkAreas(report, colony, origin);
            yield return CheckResting(report, colony, origin);
            CheckSayingThingsOnce(report);
            yield return CheckMapPins(report, colony);
            yield return CheckDistantTravel(report, colony, origin);
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
            LastPassed = report.Print();
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
        ///     must report anything but "no colony". Without it this check would pass on a
        ///     villager that reported "no colony" permanently, which is the more likely defect
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
            report.Check(villager.Activity != "no colony",
                "control: a villager in a live colony does not report being colonyless",
                $"activity='{villager.Activity}'");

            ColonyMembership.SetColony(zdo, ZDOID.None);
            yield return new WaitForSecondsRealtime(.4f);
            report.Check(villager.Activity == "no colony",
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
            VillagerLifecycle.Remove(colony, who);
            yield return new WaitForSecondsRealtime(1.5f);

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
                    WorkArea = outpost.PersistentId, WorkRadius = 12f
                }
            });

            JobDefinition stored = colony.State.GetJobs().Find(j => j.Id == "outpost");
            report.Check(stored != null && stored.WorkArea == outpost.PersistentId &&
                         Mathf.Approximately(stored.WorkRadius, 12f),
                "a job's work area survives being written to the colony record",
                $"area='{(stored == null ? "none" : stored.WorkArea)}' radius={stored?.WorkRadius ?? -1f}");

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
            bool wasLoaded = true;
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

                // Unity's null is not C#'s: a destroyed MonoBehaviour compares equal to null but
                // still arrives here as a live reference, and touching its transform throws.
                bool loaded = walker != null && ZNetScene.instance.FindInstance(who) != null;
                if (wasLoaded && !loaded) unloads++;
                wasLoaded = loaded;

                if (loaded)
                {
                    if (walker.IsReckoning) wentUnseen = true;
                    else if (wentUnseen) cameIntoView = true;
                }

                // Read from the record when there is no body to read from. The record is what
                // the world keeps, so it is also the honest measure of how far the journey got.
                float now = Utils.DistanceXZ(
                    loaded ? walker.transform.position : living.GetPosition(), destination);
                if (now < closest) closest = now;
            }

            string story = $"{startedAt:0}m to {closest:0}m in {elapsed:0}s, walkedMostly={!wentUnseen}" +
                           (expectHandover ? $" cameIntoView={cameIntoView}" : string.Empty) +
                           $" unloadedTimes={unloads}" +
                           (destroyed ? " - ZDO GONE, TRULY DESTROYED" : string.Empty);
            Core.Log.Info($"[Benchmark] travel {what}: {story}");

            bool reached = closest <= ArrivedWithin;
            report.Check(reached, $"a villager sent {what} arrives", story);

            report.Check(!destroyed,
                $"control: it still exists after travelling {what}",
                destroyed ? "its record was deleted en route" : $"record intact, unloaded {unloads} time(s)");

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

            Release(doomed);
            yield return new WaitForSecondsRealtime(.4f);

            int reaped = StructureReaper.Sweep(colony);
            report.Check(reaped >= 1 && !colony.State.GetStructures().Any(r => r.Id == record.Id),
                "a destroyed structure's record is removed", $"reaped={reaped}");

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
        ///     Clears what felling a tree leaves behind. Each control below asserts that a
        ///     villager finds nothing to do, and a log dropped by the phase before it is
        ///     something to do - which is correct behaviour reported as a failure.
        /// </summary>
 
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

        private static GameObject Spawn(string prefabName, Vector3 position)
        {
            GameObject prefab = ZNetScene.instance.GetPrefab(prefabName);
            if (prefab == null) return null;
            position.y = ZoneSystem.instance.GetSolidHeight(position) + .2f;
            return UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity);
        }
    }
}
