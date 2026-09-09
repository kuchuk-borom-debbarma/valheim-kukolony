using System.Collections;
using System.Collections.Generic;
using System.IO;
using Kukolony.Colonies;
using Kukolony.Core;
using Kukolony.Gui;
using Kukolony.Villagers;
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
            if (_started || !ModConfig.DebugScreenshotEnabled.Value)
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
            yield return new WaitForSeconds(WorldSettleSeconds);

            Vector3 origin = Player.m_localPlayer.transform.position;
            TestWorld.Purge(origin);
            yield return new WaitForSeconds(1f);

            Colony colony = BuildScenario(origin);
            if (colony == null)
            {
                Log.Error("[Screenshot] could not build a colony to photograph");
                Application.Quit();
                yield break;
            }

            // Let the villagers tick once so they have names and assignments to show.
            yield return new WaitForSeconds(3f);
            AssignEveryone(colony);

            ColonyPanel panel = ColonyPanel.Instance;
            if (panel == null)
            {
                Log.Error("[Screenshot] colony panel was never built");
                Application.Quit();
                yield break;
            }

            panel.Open(colony);
            yield return new WaitForSeconds(1.5f);

            yield return Capture("colony-panel.png");

            // And the picker mid-assignment, which is the busiest the panel ever gets.
            panel.ShowHomePickerForTest(0);
            yield return new WaitForSeconds(1f);
            yield return Capture("colony-panel-assigning.png");

            panel.Close();
            yield return new WaitForSeconds(0.5f);

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

            Register(colony, ColonyMemberKind.Station,
                Spawn<WorkPosts.WorkPost>(WorkPosts.WorkPostPrefab.PrefabName, origin + Vector3.right * 5f));
            Register(colony, ColonyMemberKind.Container,
                Spawn<Container>("piece_chest_wood", origin + Vector3.right * 8f));

            for (int i = 0; i < 3; i++)
            {
                Register(colony, ColonyMemberKind.Home,
                    Spawn<Bed>("bed", origin + Vector3.left * (5f + i * 3f)));
            }

            for (int i = 0; i < 5; i++)
            {
                Register(colony, ColonyMemberKind.Villager,
                    Spawn<Villager>(VillagerPrefab.PrefabName, origin + Vector3.back * (3f + i)));
            }

            return colony;
        }

        /// <summary>Gives the rows something to show rather than a column of dashes.</summary>
        private static void AssignEveryone(Colony colony)
        {
            ColonyState state = colony.State;
            List<ZDOID> villagers = state.GetMembers(ColonyMemberKind.Villager);
            List<ZDOID> homes = state.GetMembers(ColonyMemberKind.Home);
            List<ZDOID> stations = state.GetMembers(ColonyMemberKind.Station);

            for (int i = 0; i < villagers.Count; i++)
            {
                if (i < homes.Count)
                {
                    ColonyAssignments.AssignHome(state, villagers[i], homes[i]);
                }

                if (stations.Count > 0 && i % 2 == 0)
                {
                    ColonyAssignments.AssignStation(villagers[i], stations[0]);
                }
            }
        }

        private static void Register<T>(Colony colony, ColonyMemberKind kind, T component)
            where T : Component
        {
            if (component != null && component.TryGetComponent(out ZNetView view) && view.IsValid())
            {
                colony.Register(kind, view);
            }
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

                string directory = ModConfig.DebugScreenshotPath.Value;
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
