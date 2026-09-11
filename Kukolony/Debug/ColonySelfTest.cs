using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Kukolony.Colonies;
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

            yield return CheckLifecycle(report, colony, origin);
            CheckRegisterableContainers(report, colony);
            yield return CheckDurableReference(report, colony, origin);
            yield return CheckZdoLifetime(report, colony);
            yield return CheckOrphanedVillager(report, colony);
            yield return CheckDestroyedColonyLeavesStructures(report, colony, origin);
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
            StructureRecord storage = state.GetStructures().FirstOrDefault(r => r.Name == "Renamed storage");
            report.Check(storage != null, "registered structure and name survived save and relaunch");
            // The name alone would pass even if the reference to the object rotted, because
            // it is a plain string in the record. A chunked save renumbers raw runtime ids,
            // so the stored token is what has to carry the identity across. Assert the record
            // still points at a live object, and report both halves - a missing token and an
            // unloaded chest fail identically here and are fixed differently.
            if (storage != null)
            {
                ZDO storageZdo = ZDOMan.instance.GetZDO(storage.Id);
                report.Check(storageZdo != null && storageZdo.IsValid(),
                    "registered structure reference still resolves after save and relaunch",
                    $"id={storage.Id} token={(string.IsNullOrEmpty(storage.PersistentId) ? "none" : storage.PersistentId)}");
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
            StructureRecord target = colony.State.GetStructures().FirstOrDefault(record => record.Name == "Renamed storage");
            if (zdo == null || target == null) return;
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
