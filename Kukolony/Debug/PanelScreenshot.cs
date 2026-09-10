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
    internal sealed class PanelScreenshot : MonoBehaviour
    {
        private const float WorldSettleSeconds = 8f;

        private bool _started;

        private void Update()
        {
            if (ModConfig.DebugScreenshotEnabled.Value || (ModConfig.BenchmarkMode.Value && ModConfig.BenchmarkStage.Value == "ui"))
            {
                Application.runInBackground = true;
            }
            if (_started || (!ModConfig.DebugScreenshotEnabled.Value && (!ModConfig.BenchmarkMode.Value || ModConfig.BenchmarkStage.Value != "ui")))
            {
                return;
            }

            if (Player.m_localPlayer == null || ZNetScene.instance == null || ZoneSystem.instance == null)
            {
                return;
            }

            if (!ZoneSystem.instance.IsActiveAreaLoaded())
            {
                return;
            }

            _started = true;
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            yield return new WaitForSecondsRealtime(WorldSettleSeconds);

            Vector3 origin = Player.m_localPlayer.transform.position;
            TestWorld.Purge(origin);
            yield return new WaitForSecondsRealtime(1f);

            Colony colony = BuildScenario(origin);
            if (colony == null)
            {
                Log.Error("[Screenshot] could not build a colony to photograph");
                Application.Quit();
                yield break;
            }

            Log.Info("[Screenshot] scenario built");

            // Stagger appearance setup: several human rigs instantiated in one frame can
            // monopolise Unity's main thread on macOS and make a screenshot run look hung.
            for (int i = 0; i < 6; i++)
            {
                Villager villager = Spawn<Villager>(VillagerPrefab.PrefabName,
                    origin + Vector3.back * (3f + i * 1.5f));
                if (villager != null && villager.TryGetComponent(out ZNetView view))
                {
                    colony.Register(ColonyMemberKind.Villager, view);
                    List<ColonyJobConfig> configured = colony.State.GetEffectiveJobs();
                    ColonyAssignments.SetQueue(view.GetZDO().m_uid,
                        new List<string> { configured[i % configured.Count].Id, configured[(i + 1) % configured.Count].Id });
                }
                yield return new WaitForSecondsRealtime(.75f);
            }

            // Let villagers tick so persisted identities and activities are visible.
            yield return new WaitForSecondsRealtime(3f);

            ColonyPanel panel = ColonyPanel.Instance;
            if (panel == null)
            {
                Log.Error("[Screenshot] colony panel was never built");
                Application.Quit();
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
            panel.ShowMemberDetailForTest(0);
            yield return new WaitForSecondsRealtime(1f);
            yield return Capture("colony-member-detail.png");
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
            Application.Quit();
        }

        private static Colony BuildScenario(Vector3 origin)
        {
            Colony colony = Spawn<Colony>(ColonyPrefab.PrefabName, origin + Vector3.forward * 4f);
            if (colony == null)
            {
                return null;
            }

            colony.EnsureNamed();

            string[] prefabs = { "piece_chest_wood", "fire_pit", "smelter", "charcoal_kiln",
                "piece_cookingstation", "fermenter", "piece_beehive" };
            for (int i = 0; i < prefabs.Length; i++)
            {
                GameObject structure = Spawn(prefabs[i], origin + Vector3.right * (5f + i * 3f));
                RegisterStructure(colony, structure, "Test " + prefabs[i]);
            }

            List<ColonyJobConfig> jobs = ColonyJobCatalog.CreateDefaults();
            jobs[0].ItemFilters.Add("Wood");
            jobs[0].Count = 3;
            jobs[0].StockLimit = 20;
            colony.State.SetJobs(jobs);
            ColonyOperations.SavePreset(colony, "Portable wood hauling", jobs[0], false);
            ColonyOperations.SavePreset(colony, "Local wood hauling", jobs[0], true);

            return colony;
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

                string directory = ModConfig.BenchmarkMode.Value ? ModConfig.BenchmarkOutputPath.Value : ModConfig.DebugScreenshotPath.Value;
                Directory.CreateDirectory(directory);

                string path = Path.Combine(directory, fileName);
                File.WriteAllBytes(path, png);
                Log.Info($"[Screenshot] wrote {path} ({png.Length} bytes, {shot.width}x{shot.height})");
            }
            catch (System.Exception e)
            {
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
