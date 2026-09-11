using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Kukolony.Colonies;
using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Debug
{
    /// <summary>
    /// Owns the complete benchmark lifecycle. The shell may launch the game and observe
    /// artifacts, but only this component advances in-world phases or exits Valheim.
    /// </summary>
    internal sealed class ColonyBenchmarkController : MonoBehaviour
    {
        private bool _started;
        private string _output;
        private string _runId;
        private bool _phaseFailed;

        private void Update()
        {
            if (_started || !ModConfig.BenchmarkMode.Value || ModConfig.DebugProbeEnabled.Value ||
                Player.m_localPlayer == null ||
                ZoneSystem.instance == null || !ZoneSystem.instance.IsActiveAreaLoaded()) return;
            _started = true;
            Application.runInBackground = true;
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            _runId = string.IsNullOrWhiteSpace(ModConfig.BenchmarkRunId.Value)
                ? DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") : ModConfig.BenchmarkRunId.Value.Trim();
            _output = Path.GetFullPath(ModConfig.BenchmarkOutputPath.Value);
            Directory.CreateDirectory(_output);
            Write("heartbeat.txt", "settling " + DateTime.UtcNow.ToString("O"));
            yield return new WaitForSecondsRealtime(ModConfig.BenchmarkSettleSeconds.Value);

            bool passed = false;
            string stage = "create";
            bool reloadExpected = PreviousCreatePassed();
            Colony existing = Colony.Instances.FirstOrDefault(c => c != null &&
                c.State.Name == BenchmarkFunctionalScenario.PersistenceName &&
                BenchmarkFunctionalScenario.BelongsToRun(c, _runId));
            if (reloadExpected && existing == null)
            {
                stage = "reload";
                Fail(new InvalidOperationException(
                    "Reload was requested by the preceding create run, but its colony was not loaded from disk."));
            }
            else if (existing != null)
            {
                stage = "reload";
                passed = RunPhase("reload-verification", () => BenchmarkFunctionalScenario.RunReload(existing));
                passed &= BenchmarkFunctionalScenario.LastPassed;
                if (passed)
                {
                    Cleanup(existing);
                    // DestroyZDO is routed even on the local server. Give that message a
                    // frame to remove fixtures before preparing the cleanup save.
                    yield return new WaitForSecondsRealtime(.5f);
                }
            }
            else
            {
                // An interrupted older run saves its fixtures before it ever writes a
                // terminal marker, and every run whose create phase failed leaves a colony,
                // its villagers and its chests behind for good. They accumulate, and a
                // leftover chest with room in it has already been picked by a job under test.
                int purged = PurgeStaleFixtures();
                if (purged > 0) Log.Info($"[Benchmark] purged {purged} fixture(s) left by earlier runs");
                yield return null;
                yield return Guard("functional", BenchmarkFunctionalScenario.RunFresh(_runId));
                passed = !_phaseFailed && BenchmarkFunctionalScenario.LastPassed;
                if (passed && ModConfig.BenchmarkScreenshots.Value)
                {
                    Colony created = Colony.Instances.FirstOrDefault(c => c != null &&
                        c.State.Name == BenchmarkFunctionalScenario.PersistenceName &&
                        BenchmarkFunctionalScenario.BelongsToRun(c, _runId));
                    yield return Guard("ui", BenchmarkUiScenario.Run(created));
                    passed &= !_phaseFailed && BenchmarkUiScenario.LastPassed;
                }
                if (passed)
                {
                    Colony created = Colony.Instances.FirstOrDefault(c => c != null &&
                        c.State.Name == BenchmarkFunctionalScenario.PersistenceName &&
                        BenchmarkFunctionalScenario.BelongsToRun(c, _runId));
                    BenchmarkFunctionalScenario.PreparePersistenceSnapshot(created);
                }
            }

            if (ModConfig.BenchmarkAutoExit.Value)
            {
                yield return SaveAndLogout();
                passed &= !_phaseFailed;
            }

            string terminal = $"BENCHMARK TERMINAL {stage} {(passed ? "PASS" : "FAIL")} run={_runId}";
            Write("benchmark-" + stage + ".log", TestReport.LastText + Environment.NewLine + terminal + Environment.NewLine);
            Write("benchmark-report.json", "{\"runId\":\"" + Escape(_runId) + "\",\"stage\":\"" + stage +
                "\",\"passed\":" + (passed ? "true" : "false") + ",\"finishedUtc\":\"" + DateTime.UtcNow.ToString("O") + "\"}");
            Log.Info(terminal);
            Write("heartbeat.txt", (ModConfig.BenchmarkAutoExit.Value ? "exiting " : "complete ") +
                DateTime.UtcNow.ToString("O"));
            if (ModConfig.BenchmarkAutoExit.Value) Application.Quit();
        }

        private IEnumerator Guard(string name, IEnumerator phase)
        {
            Stopwatch timer = Stopwatch.StartNew();
            Log.Info("[Benchmark] PHASE START " + name);
            while (true)
            {
                if (timer.Elapsed.TotalSeconds > ModConfig.BenchmarkPhaseTimeoutSeconds.Value)
                { Fail(new TimeoutException("Benchmark phase timed out: " + name)); yield break; }
                bool next;
                object current = null;
                try { next = phase.MoveNext(); if (next) current = phase.Current; }
                catch (Exception e) { Fail(new InvalidOperationException("Benchmark phase failed: " + name, e)); yield break; }
                Write("heartbeat.txt", name + " " + timer.ElapsedMilliseconds + "ms " + DateTime.UtcNow.ToString("O"));
                if (!next) break;
                yield return current;
            }
            Log.Info($"[Benchmark] PHASE END {name} {timer.ElapsedMilliseconds}ms");
        }

        private bool RunPhase(string name, Action phase)
        {
            Stopwatch timer = Stopwatch.StartNew(); Log.Info("[Benchmark] PHASE START " + name);
            try { phase(); Log.Info($"[Benchmark] PHASE END {name} {timer.ElapsedMilliseconds}ms"); return true; }
            catch (Exception e) { Fail(new InvalidOperationException("Benchmark phase failed: " + name, e)); return false; }
        }

        private void Fail(Exception error) { _phaseFailed = true; Write("failure.txt", error.ToString()); Log.Error("[Benchmark] " + error); }

        /// <summary>
        ///     Removes every benchmark fixture left in the world, working from ZDOs rather
        ///     than loaded instances so a colony whose zone is not loaded is still found.
        ///     Returns how many were destroyed.
        /// </summary>
        /// <remarks>
        ///     Scoped to the dedicated benchmark world by name. Someone can turn benchmark
        ///     mode on in their own save to watch it run, and deleting their villagers would
        ///     be unforgivable; there is nothing to clean up there anyway, because a run that
        ///     completes cleans up after itself.
        /// </remarks>
        private static int PurgeStaleFixtures()
        {
            if (ZNet.instance == null || ZDOMan.instance == null) return 0;
            if (ZNet.instance.GetWorldName() != ModConfig.BenchmarkWorld.Value) return 0;

            int destroyed = 0;
            // Every hearth, not only the run's own. Checks that need a colony they can
            // destroy build a throwaway one with an ordinary name, and a check that failed
            // before reaching its cleanup leaves it standing - which a name-matched purge
            // would then skip forever.
            foreach (ZDO hearth in FindAll(ColonyPrefab.PrefabName))
            {
                ColonyState state = new ColonyState(hearth);
                if (state.IsValid)
                    foreach (StructureRecord record in state.GetStructures()) { Destroy(record.Id); destroyed++; }
                Destroy(hearth.m_uid);
                destroyed++;
            }

            // Villagers are destroyed by prefab rather than by membership: a phase that ended
            // early leaves ones that were never registered, and those are exactly the ones
            // that pile up in the world.
            foreach (ZDO villager in FindAll(Villagers.VillagerPrefab.PrefabName)) { Destroy(villager.m_uid); destroyed++; }

            // And everything else a run puts in the world. Cleaning only what a colony had
            // registered left every chest, kiln and dropped log a scenario ever spawned
            // standing where it fell - after enough runs the benchmark world is a junkyard,
            // and a check that finds a leftover chest with room in it passes for the wrong
            // reason. Destroying by prefab is safe here only because this is gated to the
            // dedicated benchmark world.
            foreach (string prefab in Fixtures)
                foreach (ZDO fixture in FindAll(prefab)) { Destroy(fixture.m_uid); destroyed++; }
            return destroyed;
        }

        /// <summary>
        ///     Every prefab a scenario spawns. Anything a run creates belongs here, or it
        ///     accumulates: this list is the difference between a clean world and a junkyard.
        /// </summary>
        private static readonly string[] Fixtures =
        {
            // Structures
            "piece_chest_wood", "fire_pit", "smelter", "charcoal_kiln",
            "bed", "piece_bed", "bed_wood",
            "piece_cookingstation", "fermenter", "piece_beehive", "Cart",
            // Items left lying about by pickup, drop and felling checks
            "Wood", "Flint", "Coal", "CopperOre", "RawMeat", "Honey",
            "AxeStone", "ArmorLeatherChest",
            // Felled trunks. The trees themselves are world scenery and are deliberately
            // not on this list - they share prefabs with everything the world generated.
            "beech_log", "beech_log_half", "birch_log", "birch_log_half",
            "fir_log", "fir_log_half", "oak_log", "oak_log_half",
            "AshlandsTree1_log", "AshlandsTree1_log_half"
        };

        /// <summary>Every ZDO of a prefab currently known to this peer.</summary>
        private static List<ZDO> FindAll(string prefabName)
        {
            List<ZDO> found = new List<ZDO>();
            int index = 0;
            while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(prefabName, found, ref index)) { }
            return found;
        }

        private static void Cleanup(Colony colony)
        {
            foreach (ZDOID id in colony.State.GetMembers(ColonyMemberKind.Villager)
                .Concat(colony.State.GetStructures().Select(record => record.Id)).ToList()) Destroy(id);
            if (colony.TryGetComponent(out ZNetView view) && view.IsValid()) Destroy(view.GetZDO().m_uid);
        }

        private static void Destroy(ZDOID id)
        {
            ZDO zdo = ZDOMan.instance?.GetZDO(id); if (zdo == null || !zdo.IsValid()) return;
            zdo.SetOwner(ZDOMan.GetSessionID()); ZDOMan.instance.DestroyZDO(zdo);
        }

        private bool PreviousCreatePassed()
        {
            string path = Path.Combine(_output, "benchmark-create.log");
            if (!File.Exists(path)) return false;
            try
            {
                return File.ReadAllText(path).Contains("BENCHMARK TERMINAL create PASS run=" + _runId);
            }
            catch (Exception error)
            {
                Fail(new IOException("Could not read the create-run marker.", error));
                return false;
            }
        }

        private IEnumerator SaveAndLogout()
        {
            Write("heartbeat.txt", "saving " + DateTime.UtcNow.ToString("O"));

            // Last thing before the save, because anything between the two can undo it.
            foreach (Colony colony in Colony.Instances)
            {
                if (colony != null) BenchmarkFunctionalScenario.PrepareStructuresForSave(colony);
            }

            if (Game.instance == null)
            {
                Fail(new InvalidOperationException("Cannot persist benchmark fixtures because Game.instance is unavailable."));
                yield break;
            }

            // Save only the world, synchronously. Game.Logout(save:true) saves the cloud
            // character first; if Steam already owns a profile-storage batch it throws
            // before ZNet ever sees the world. This public ZNet contract takes three
            // explicit choices: synchronous, no peer profiles, no delayed frame.
            if (ZNet.instance == null)
            {
                Fail(new InvalidOperationException("Cannot persist benchmark fixtures because ZNet is unavailable."));
                yield break;
            }
            try
            {
                if (ZNet.instance.GetWorld() == null)
                    throw new InvalidOperationException("Benchmark world metadata is unavailable.");
                ZNet.instance.Save(true, false, false);
                Log.Info("[Benchmark] synchronous world save completed");
            }
            catch (Exception error)
            {
                Fail(new InvalidOperationException("Valheim's synchronous world save failed.", error));
                yield break;
            }

            // Public Logout rewrites its save argument through EnoughDiskSpaceAvailable,
            // so even Logout(false, ...) may save the profile. Invoke the game's private
            // teardown boundary with saveWorld=false instead. It marks Game as shutting
            // down, closes ZNetScene/ZNet, and makes OnApplicationQuit an idempotent no-op.
            try
            {
                MethodInfo shutdown = typeof(Game).GetMethod("Shutdown",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (shutdown == null) throw new MissingMethodException(typeof(Game).FullName, "Shutdown");
                shutdown.Invoke(Game.instance, new object[] { false });
            }
            catch (Exception error)
            {
                Fail(new InvalidOperationException("Valheim's no-save teardown failed.", error));
                yield break;
            }

            Write("heartbeat.txt", "world-saved " + DateTime.UtcNow.ToString("O"));
            yield return new WaitForSecondsRealtime(2f);
        }

        private void Write(string name, string text) { File.WriteAllText(Path.Combine(_output, name), text); }
        private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
