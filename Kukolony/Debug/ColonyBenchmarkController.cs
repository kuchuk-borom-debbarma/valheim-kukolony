using System;
using System.Collections;
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
                // An interrupted older run may have saved its fixtures before it wrote a
                // terminal marker. They are benchmark-owned, but not evidence for this
                // run, so remove them without touching any other world object.
                foreach (Colony stale in Colony.Instances.Where(c => c != null &&
                    c.State.Name == BenchmarkFunctionalScenario.PersistenceName).ToList()) Cleanup(stale);
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
