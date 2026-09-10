using System.Collections;
using System.Collections.Generic;
using System.IO;
using Kukolony.Colonies;
using Kukolony.Core;
using Kukolony.Gui;
using Kukolony.Villagers;
using Kukolony.Jobs;
using UnityEngine;

namespace Kukolony.Debug
{
    /// <summary>
    ///     Builds a populated colony, opens the panel, and screenshots it.
    ///
    ///     The harness can prove every rule behind a button, but not whether the panel
    ///     reads well - which is the one thing that actually needs looking at. macOS
    ///     screen capture is blocked without Screen Recording permission, so the game
    ///     captures itself instead: no OS permission is involved, and the result is
    ///     exactly what a player sees.
    /// </summary>
    /// <summary>UI evidence phase invoked exclusively by ColonyBenchmarkController.</summary>
    internal static class BenchmarkUiScenario
    {
        internal static bool LastPassed { get; private set; }
        private static bool _captureFailed;
        private static readonly List<string> Captures = new List<string>();
        internal static IEnumerator Run(Colony colony)
        {
            LastPassed = false;
            _captureFailed = false;
            Captures.Clear();
            Vector3 origin = Player.m_localPlayer.transform.position;
            yield return new WaitForSecondsRealtime(1f);

            if (colony == null)
            {
                Log.Error("[Screenshot] could not build a colony to photograph");
                yield break;
            }

            string[] prefabs = { "piece_chest_wood", "fire_pit", "smelter", "charcoal_kiln",
                "piece_cookingstation", "fermenter", "piece_beehive" };
            // Readable, phase-unique names: the functional phase already registers
            // "Benchmark <prefab>" fixtures, and raw prefab names truncate in list rows,
            // which made two distinct structures render identically in UI evidence.
            string[] names = { "Showcase storage", "Showcase fire pit", "Showcase smelter",
                "Showcase kiln", "Showcase cooking station", "Showcase fermenter",
                "Showcase beehive" };
            List<GameObject> structures = new List<GameObject>();
            for (int i = 0; i < prefabs.Length; i++)
            {
                GameObject structure = Spawn(prefabs[i], origin + Vector3.right * (5f + i * 3f));
                if (structure != null) structures.Add(structure);
                RegisterStructure(colony, structure, names[i]);
            }
            Log.Info("[Screenshot] scenario built");

            // Pagination and selection exercise persisted member records independently
            // from rendering. Reuse existing ZNet-backed fixtures so this UI scenario
            // creates no extra actors; the functional phase already exercises two real
            // production NPCs concurrently.
            List<ColonyJobConfig> configured = colony.State.GetEffectiveJobs();
            ZDOID detailSubject = ZDOID.None;
            for (int i = 0; i < 6; i++)
            {
                GameObject fixture = i < structures.Count ? structures[i] : null;
                if (fixture != null && fixture.TryGetComponent(out ZNetView view) && view.IsValid())
                {
                    colony.Register(ColonyMemberKind.Villager, view);
                    VillagerState state = new VillagerState(view.GetZDO());
                    state.SetName("Showcase villager " + (i + 1));
                    state.SetRuntimePhase(i % 2 == 0 ? "Finding target" : "Waiting for work");
                    ColonyAssignments.SetQueue(view.GetZDO().m_uid,
                        new List<string> { configured[i % configured.Count].Id, configured[(i + 1) % configured.Count].Id });
                    if (detailSubject == ZDOID.None) detailSubject = view.GetZDO().m_uid;
                }
            }
            Log.Info("[Screenshot] lightweight member records ready");
            yield return null;

            ColonyPanel panel = ColonyPanel.Instance;
            if (panel == null)
            {
                Log.Error("[Screenshot] colony panel was never built");
                yield break;
            }

            Log.Info("[Screenshot] opening panel");

            panel.Open(colony);
            yield return new WaitForSecondsRealtime(1.5f);

            Log.Info("[Screenshot] capturing colony panel");
            yield return Capture("colony-panel.png");

            panel.ShowTabForTest("Structures");
            yield return new WaitForSecondsRealtime(.5f);
            yield return Capture("colony-structures.png");
            panel.ShowTabForTest("Members");
            yield return new WaitForSecondsRealtime(1f);
            yield return Capture("colony-members.png");
            panel.ShowPageForTest(1);
            yield return new WaitForSecondsRealtime(1f);
            yield return Capture("colony-members-page-2.png");
            if (detailSubject != ZDOID.None) panel.ShowMemberDetailForTest(detailSubject);
            else panel.ShowMemberDetailForTest(0);
            yield return new WaitForSecondsRealtime(1f);
            yield return Capture("colony-member-detail.png");
            panel.ShowRemoveConfirmForTest();
            yield return new WaitForSecondsRealtime(1f);
            yield return Capture("colony-member-remove-confirm.png");
            panel.ShowRegisterPickerForTest();
            yield return new WaitForSecondsRealtime(1f);
            yield return Capture("colony-register-picker.png");
            panel.ShowOutfitsForTest();
            yield return new WaitForSecondsRealtime(1f);
            yield return Capture("colony-outfits.png");
            panel.ShowOutfitCardForTest();
            yield return new WaitForSecondsRealtime(1f);
            yield return Capture("colony-outfit-card.png");
            panel.ShowTabForTest("Jobs");
            yield return new WaitForSecondsRealtime(.5f);
            yield return Capture("colony-jobs.png");
            panel.ShowJobForTest(0);
            yield return new WaitForSecondsRealtime(1f);
            yield return Capture("colony-job-config.png");
            panel.ShowTargetPickerForTest();
            yield return new WaitForSecondsRealtime(1f);
            yield return Capture("colony-structure-picker.png");
            panel.ShowPresetsForTest();
            yield return new WaitForSecondsRealtime(1f);
            yield return Capture("colony-preset-application.png");

            panel.Close();
            ColonyPicker.Instance?.ShowForTest();
            yield return new WaitForSecondsRealtime(.5f);
            yield return Capture("colony-picker.png");
            ColonyPicker.Instance?.HideForTest();
            yield return new WaitForSecondsRealtime(0.5f);

            Log.Info("[Screenshot] done");
            File.WriteAllText(Path.Combine(ModConfig.BenchmarkOutputPath.Value, "screenshots.manifest.json"),
                "{\"screenshots\":[" + string.Join(",", Captures) + "]}");
            LastPassed = !_captureFailed;
        }

        private static void RegisterStructure(Colony colony, GameObject structure, string name)
        {
            if (structure == null || !structure.TryGetComponent(out ZNetView view) || !view.IsValid()) return;
            if (!StructureRegistry.TryCapabilities(structure, out StructureCapability capabilities)) return;
            colony.RegisterStructure(new StructureRecord
            {
                Id = view.GetZDO().m_uid,
                Name = name,
                Prefab = Utils.GetPrefabName(structure),
                Capabilities = capabilities
            });
        }


        private static T Spawn<T>(string prefabName, Vector3 position) where T : Component
        {
            GameObject prefab = ZNetScene.instance.GetPrefab(prefabName);
            if (prefab == null)
            {
                Log.Error($"[Screenshot] prefab '{prefabName}' not found");
                return null;
            }

            position.y = ZoneSystem.instance.GetSolidHeight(position) + 0.2f;
            GameObject spawned = Object.Instantiate(prefab, position, Quaternion.identity);
            return spawned != null ? spawned.GetComponent<T>() : null;
        }

        private static GameObject Spawn(string prefabName, Vector3 position)
        {
            GameObject prefab = ZNetScene.instance.GetPrefab(prefabName);
            if (prefab == null)
            {
                Log.Warning($"[Screenshot] optional prefab '{prefabName}' not found");
                return null;
            }
            position.y = ZoneSystem.instance.GetSolidHeight(position) + .2f;
            return Object.Instantiate(prefab, position, Quaternion.identity);
        }

        /// <summary>
        ///     Grabs the frame as a texture and writes the PNG ourselves, rather than
        ///     ScreenCapture.CaptureScreenshot, whose output path is relative to whatever
        ///     the working directory happens to be. Must run after WaitForEndOfFrame or
        ///     the frame is not finished being drawn.
        /// </summary>
        private static IEnumerator Capture(string fileName)
        {
            yield return new WaitForEndOfFrame();

            Texture2D shot = null;
            try
            {
                shot = ScreenCapture.CaptureScreenshotAsTexture();
                byte[] png = shot.EncodeToPNG();

                string directory = ModConfig.BenchmarkOutputPath.Value;
                Directory.CreateDirectory(directory);

                string path = Path.Combine(directory, fileName);
                File.WriteAllBytes(path, png);
                Captures.Add("{\"file\":\"" + fileName + "\",\"width\":" + shot.width +
                    ",\"height\":" + shot.height + ",\"capturedUtc\":\"" + System.DateTime.UtcNow.ToString("O") + "\"}");
                Log.Info($"[Screenshot] wrote {path} ({png.Length} bytes, {shot.width}x{shot.height})");
            }
            catch (System.Exception e)
            {
                _captureFailed = true;
                Log.Error($"[Screenshot] capture failed: {e.Message}");
            }
            finally
            {
                if (shot != null)
                {
                    Object.Destroy(shot);
                }
            }
        }
    }
}
