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
    ///     Photographs what the mod puts in the world.
    ///
    ///     The harness can prove every rule behind a feature, but not whether the result
    ///     looks right - which is the one thing that actually needs looking at. macOS screen
    ///     capture is blocked without Screen Recording permission, so the game captures
    ///     itself instead: no OS permission is involved, and the result is exactly what a
    ///     player sees.
    ///
    ///     Photographing a villager is how a rig that glowed like a ghost was finally
    ///     noticed - every capture before that had framed the interface, so nothing had ever
    ///     photographed one. The screen captures below exist for what the layout audit cannot
    ///     judge: the audit proves nothing overlaps and nothing is clipped, and says nothing
    ///     about whether the result reads as a settlement's control panel.
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
            yield return new WaitForSecondsRealtime(1f);

            if (colony == null)
            {
                Log.Error("[Screenshot] no colony to photograph");
                yield break;
            }

            yield return CaptureVillager(colony);
            yield return CaptureScreens(colony);

            System.IO.File.WriteAllText(
                System.IO.Path.Combine(ModConfig.BenchmarkOutputPath.Value, "screenshots.manifest.json"),
                "[" + string.Join(",", Captures.ToArray()) + "]");
            LastPassed = !_captureFailed && Captures.Count > 0;
        }

        /// <summary>
        ///     Photographs each screen, driven through the same calls the buttons make.
        /// </summary>
        /// <remarks>
        ///     Subjects are chosen exactly rather than by index. Index-based selection depends
        ///     on list ordering, and photographed the wrong subject before now.
        /// </remarks>
        private static IEnumerator CaptureScreens(Colony colony)
        {
            ColonyScreen screen = ColonyScreen.Instance;
            if (screen == null)
            {
                Log.Error("[Screenshot] the colony screen does not exist");
                _captureFailed = true;
                yield break;
            }

            screen.Open(colony, null);
            yield return new WaitForSecondsRealtime(.4f);
            yield return Capture("colony-screen-home.png");

            screen.Push(new GalleryScreen());
            yield return new WaitForSecondsRealtime(.4f);
            yield return Capture("colony-screen-gallery.png");

            screen.ShowPageForTest(1);
            yield return new WaitForSecondsRealtime(.4f);
            yield return Capture("colony-screen-paged.png");

            screen.ShowPageForTest(0);
            PickerScreen picker = new PickerScreen("Choose an item", Items, null, false, _ => { });
            picker.SearchForTest("wood");
            screen.Push(picker);
            yield return new WaitForSecondsRealtime(.4f);
            yield return Capture("colony-screen-picker.png");

            screen.Root(new ColonyHomeScreen());
            screen.Push(new StructureListScreen());
            yield return new WaitForSecondsRealtime(.4f);
            yield return Capture("colony-screen-structures.png");

            // The detail screen is photographed on a named subject chosen exactly, not on
            // whichever record happens to sort first - index-based selection photographed the
            // wrong subject once already.
            StructureRecord subject = colony.State.GetStructures()
                .Find(r => r.Name == "Renamed storage");
            if (subject != null)
            {
                screen.Push(new StructureDetailScreen(subject.PersistentId, subject.Id));
                yield return new WaitForSecondsRealtime(.4f);
                yield return Capture("colony-screen-structure.png");
            }
            else
            {
                Log.Error("[Screenshot] no named structure to photograph");
                _captureFailed = true;
            }

            // Something for the list to offer. Everything nearby is registered by the time
            // this runs, so without a fresh candidate this photographs "nothing left to
            // register" - true, and evidence of nothing. A screenshot must frame its subject.
            GameObject candidate = Spawn("piece_chest_wood",
                colony.transform.position + new Vector3(4f, 0f, 4f));
            GameObject secondCandidate = Spawn("piece_chest_wood",
                colony.transform.position + new Vector3(-4f, 0f, 4f));
            yield return new WaitForSecondsRealtime(.3f);

            screen.Root(new ColonyHomeScreen());
            screen.Push(new RegisterNearbyScreen());
            yield return new WaitForSecondsRealtime(.4f);
            yield return Capture("colony-screen-register-nearby.png");

            Discard(candidate);
            Discard(secondCandidate);
            yield return null;

            screen.Close();
            yield return null;
        }

        /// <summary>
        ///     Removes a fixture this phase spawned. Ownership first - destroying something we
        ///     do not own is a silent no-op, and the leftover would be registerable next run.
        /// </summary>
        private static void Discard(GameObject target)
        {
            if (target == null) return;
            if (target.TryGetComponent(out ZNetView view) && view.IsValid()) view.ClaimOwnership();
            ZNetScene.instance.Destroy(target);
        }

        private static List<PickerScreen.Option> Items(string filter)
        {
            List<PickerScreen.Option> options = new List<PickerScreen.Option>();
            foreach (ItemCatalogue.Entry entry in ItemCatalogue.Search(filter, 40))
            {
                options.Add(new PickerScreen.Option(entry.PrefabName, entry.DisplayName));
            }

            return options;
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
        /// <summary>
        ///     Photographs a villager with no panel in the way.
        /// </summary>
        /// <remarks>
        ///     Every other capture here frames the interface, which is why a villager glowing
        ///     like the ghost it was cloned from survived run after run: nothing ever looked at
        ///     one. Appearance is exactly the kind of thing only a picture can check.
        /// </remarks>
        private static IEnumerator CaptureVillager(Colony colony)
        {
            Villager subject = null;
            foreach (Villager candidate in Villager.Instances)
                if (candidate != null) { subject = candidate; break; }
            if (subject == null || Player.m_localPlayer == null)
            {
                Log.Warning("[Screenshot] no villager to photograph");
                yield break;
            }

            // Placed relative to the camera, not the player. The game's camera follows the
            // player every frame and overwrites anything set on it, and positioning by the
            // player's own transform put the subject somewhere off frame twice - the pictures
            // came back showing the player's back and no villager at all.
            //
            // Standing it beside the player is deliberate: the two share a body rig, so having
            // both in shot is what makes "does this look like a person" answerable at a glance.
            if (GameCamera.instance == null) yield break;
            Transform eye = GameCamera.instance.transform;
            Vector3 spot = eye.position + eye.forward * 6f + eye.right * 1.5f;
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(spot, out float ground))
                spot.y = ground;
            subject.transform.position = spot;
            subject.transform.rotation = Quaternion.LookRotation(eye.position - spot);
            yield return new WaitForSecondsRealtime(1f);
            yield return Capture("colony-villager.png");
        }

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
