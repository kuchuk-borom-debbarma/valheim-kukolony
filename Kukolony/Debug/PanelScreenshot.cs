using System.Collections;
using System.Collections.Generic;
using System.IO;
using Kukolony.Colonies;
using Kukolony.Core;
using Kukolony.Gui;
using Kukolony.Jobs;
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

            // Deliberately not cleared. The in-world captures happen in the functional phase,
            // which runs first, so resetting the list here dropped three photographs out of the
            // manifest and a failure among them out of the verdict - the run reported every
            // picture it took while quietly not counting the ones taken earliest. One benchmark
            // runs per process, so there is nothing stale to clear.
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

            // The detail screen brings its own subject rather than borrowing one, for the
            // same reason the register-nearby capture spawns its candidates: a photograph must
            // frame what it claims to show. Naming an existing fixture made this capture depend
            // on that chest outliving every check in the run, and the reaper legitimately
            // removes a structure something else destroyed - so the picture went missing and
            // failed the phase for a reason that had nothing to do with it. Choosing "whatever
            // is registered" instead was no better: it landed on the same kiln the settings
            // capture uses, and the storage settings stopped being photographed at all.
            GameObject portrait = Spawn("piece_chest_wood", colony.transform.position + new Vector3(0f, 0f, 5f));
            yield return new WaitForSecondsRealtime(.3f);
            if (portrait != null) ColonyOperations.Register(colony, portrait);

            StructureRecord subject = colony.State.GetStructures()
                .FindLast(r => (r.Capabilities & StructureCapability.Storage) != 0 &&
                               r.StatusIn(colony) == StructureStatus.Ready);

            // Given something to hold and a limit on it, because the cap row only exists for an
            // item the chest actually names - a chest set to "anything" has nothing to cap, and
            // photographing that would be evidence the rows do not render.
            if (subject != null)
            {
                ColonyOperations.EditSettings(colony, subject.Id, s =>
                {
                    s.Accepts = new List<string> { "Wood" };
                    s.TakeUnclaimed = true;
                    s.SetCap("Wood", 200);
                });
            }

            if (subject != null)
            {
                screen.Push(new StructureDetailScreen(subject.PersistentId, subject.Id));
                yield return new WaitForSecondsRealtime(.4f);
                yield return Capture("colony-screen-structure.png");
            }
            else
            {
                Log.Error("[Screenshot] the chest spawned for the detail capture did not register");
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

            // A structure with settings worth looking at: the kiln is the one that shows every
            // processing row, and it is chosen by name rather than by position in the list.
            StructureRecord station = colony.State.GetStructures()
                .Find(r => (r.Capabilities & StructureCapability.Processing) != 0);
            if (station != null)
            {
                screen.Root(new ColonyHomeScreen());
                screen.Push(new StructureListScreen());
                screen.Push(new StructureDetailScreen(station.PersistentId, station.Id));
                yield return new WaitForSecondsRealtime(.4f);
                yield return Capture("colony-screen-settings.png");
            }
            else
            {
                Log.Error("[Screenshot] no processing structure to photograph settings on");
                _captureFailed = true;
            }

            // Jobs, with one pointed at a work area so the screen shows a configured job
            // rather than an empty one. A screenshot must frame its subject.
            if (subject != null)
            {
                List<JobDefinition> jobs = colony.State.GetJobs();
                jobs.Add(new JobDefinition
                {
                    Id = "screenshot-haul", Name = "Haul to the shed", Kind = JobKind.Haul,
                    Repeat = 4, Items = new List<string> { "Wood" },
                    Areas = new List<string> { subject.PersistentId }, WorkRadius = 24f
                });
                colony.State.SetJobs(jobs);
            }

            screen.Root(new ColonyHomeScreen());
            screen.Push(new JobListScreen());
            yield return new WaitForSecondsRealtime(.4f);
            yield return Capture("colony-screen-jobs.png");

            screen.Push(new JobDetailScreen("screenshot-haul"));
            yield return new WaitForSecondsRealtime(.4f);
            yield return Capture("colony-screen-job.png");

            // A preset with something in it, because an empty one shows none of what the screen
            // is for - a photograph must frame its subject.
            colony.State.SetPresets(new List<JobPreset>
            {
                new JobPreset
                {
                    Id = "screenshot-preset", Name = "Hauler",
                    Jobs = new List<string> { "screenshot-haul" }
                }
            });

            screen.Root(new ColonyHomeScreen());
            screen.Push(new JobListScreen());
            screen.Push(new PresetListScreen());
            screen.Push(new PresetDetailScreen("screenshot-preset"));
            yield return new WaitForSecondsRealtime(.4f);
            yield return Capture("colony-screen-preset.png");

            screen.Root(new ColonyHomeScreen());
            screen.Push(new VillagerListScreen());
            yield return new WaitForSecondsRealtime(.4f);
            yield return Capture("colony-screen-villagers.png");

            List<ZDOID> people = colony.State.GetMembers(ColonyMemberKind.Villager);
            if (people.Count > 0)
            {
                screen.Push(new VillagerDetailScreen(people[0]));
                yield return new WaitForSecondsRealtime(.4f);
                yield return Capture("colony-screen-villager.png");
            }
            else
            {
                Log.Error("[Screenshot] no villager to photograph a detail screen for");
                _captureFailed = true;
            }

            screen.Root(new ColonyHomeScreen());
            screen.Push(new RegisterNearbyScreen());
            yield return new WaitForSecondsRealtime(.4f);
            yield return Capture("colony-screen-register-nearby.png");

            Discard(candidate);
            Discard(secondCandidate);
            if (portrait != null)
            {
                colony.RemoveStructure(subject != null ? subject.Id : ZDOID.None);
                Discard(portrait);
            }
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

            // Say which figure is which, and what it is wearing.
            //
            // The shot contains the player as well as the villager on purpose, and nothing in
            // it says so - which is how I spent several runs diagnosing the player's bare chest
            // as a villager bug, and wrote "villagers are undressed" into two documents. The
            // villager stands to the RIGHT of frame; the centre figure is the player. The log
            // line below is what the image has to be read against.
            if (subject.TryGetComponent(out ZNetView subjectView) && subjectView.IsValid())
            {
                ZDO zdo = subjectView.GetZDO();
                Log.Info($"[Screenshot] colony-villager.png: the villager is the RIGHT figure, " +
                         $"name='{subject.DisplayName()}' model={zdo.GetInt(ZDOVars.s_modelIndex, 0)} " +
                         $"chest={zdo.GetInt(ZDOVars.s_chestItem, 0)} legs={zdo.GetInt(ZDOVars.s_legItem, 0)} " +
                         $"helmet={zdo.GetInt(ZDOVars.s_helmetItem, 0)} cape={zdo.GetInt(ZDOVars.s_shoulderItem, 0)} " +
                         $"hair={zdo.GetInt(ZDOVars.s_hairItem, 0)}; the centre figure is the player");
            }

            yield return Capture("colony-villager.png");
        }

        /// <summary>
        ///     Photographs the settlement at work, from where the player is standing.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The checks can prove a chest gained five wood. They cannot show a villager
        ///         walking to it, and "it works" and "it looks like it works" are not the same
        ///         claim - a villager that reaches its chest by sliding there backwards passes
        ///         every assertion in the suite.
        ///     </para>
        ///     <para>
        ///         The player is turned to face the subject rather than moved to it. The camera
        ///         follows the player every frame and overwrites anything set on it directly, so
        ///         aiming means aiming the player; and moving the player is what decides which
        ///         zones stay loaded, which is the one thing a hauling test must not disturb.
        ///     </para>
        /// </remarks>
        internal static IEnumerator PhotographAtWork(string fileName, Vector3 subject, string note,
            Vector3? alsoShow = null)
        {
            // The frame's own honesty, recorded here so no caller can forget it: a photograph
            // with no second point is a lone close-up of the subject, and a caption that reads
            // as though a counterpart is in frame has already misled a review of this suite
            // three commits running. One suffix in the one place every capture goes through.
            if (!alsoShow.HasValue)
            {
                note += " (framed alone - no second subject resolved)";
            }

            // Checked here, because this is called from the functional phase rather than the UI
            // one and so sits outside that phase's gate. Without it, turning screenshots off
            // still detached the camera and froze the sky at noon in the middle of a hauling
            // measurement.
            if (!ModConfig.BenchmarkScreenshots.Value) yield break;

            GameCamera rig = GameCamera.instance;
            if (rig == null)
            {
                Log.Warning($"[Screenshot] no camera to photograph {fileName} with");
                yield break;
            }

            Transform lens = rig.transform;
            bool wasEnabled = rig.enabled;
            Vector3 wasAt = lens.position;
            Quaternion wasFacing = lens.rotation;

            // The camera is flown to the subject rather than the player being turned towards it.
            // Turning the player worked and was the wrong tool: where the player stands decides
            // which zones stay loaded and which villagers keep being simulated, and a hauling
            // test must not have its world moved underneath it to take a photograph. Detaching
            // the camera moves nothing in the world at all.
            //
            // Disabling the component is the whole trick. GameCamera rewrites its own transform
            // every LateUpdate from the player's position, so a transform set on it is gone
            // before anything renders.
            // Photographed in daylight. The benchmark runs at whatever hour the world happens
            // to be at, and the first in-world captures came back as a dark smear in which a
            // villager could not be told from a rock - a picture nobody can read is the same as
            // no picture. Restored afterwards, because recovery from rest is counted in the
            // world's own hours and a benchmark that silently froze noon would be measuring
            // something else.
            EnvMan sky = EnvMan.instance;
            bool wasDebugTime = sky != null && sky.m_debugTimeOfDay;
            float wasTime = sky == null ? 0f : sky.m_debugTime;
            if (sky != null)
            {
                sky.m_debugTimeOfDay = true;
                sky.m_debugTime = .5f;
            }

            rig.enabled = false;
            try
            {
                // Both the villager and whatever it is walking to, when the caller knows. A
                // villager on its own answers "does it look like a person"; a villager and the
                // chest it is heading for answers "is it doing the right thing", which is the
                // question actually being asked of a hauling run.
                Vector3 other = alsoShow ?? subject;
                Vector3 middle = (subject + other) * .5f;
                float spread = Vector3.Distance(subject, other);
                float back = Mathf.Clamp(spread * .9f + 5f, 6f, 22f);

                // Set off to one side rather than straight behind, so the hearth dome and the
                // chests are not between the camera and the subject - the first shots framed a
                // villager neatly and put a stone dome in front of it.
                Vector3 along = other - subject;
                along.y = 0f;
                Vector3 aside = Vector3.Cross(
                    along.sqrMagnitude > .01f ? along.normalized : Vector3.forward, Vector3.up);

                // Flattened and re-normalised first: the cross product is only unit length when
                // the two points are level, so on a slope the camera quietly crept in, and two
                // points stacked vertically collapsed it to nothing and left LookRotation with a
                // forward pointing straight at its own up.
                aside = aside.sqrMagnitude > .01f ? aside.normalized : Vector3.right;

                Vector3 eye = middle + aside * back + Vector3.up * (back * .45f);
                if (ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(eye, out float ground))
                {
                    eye.y = Mathf.Max(eye.y, ground + 2f);
                }

                lens.position = eye;
                lens.rotation = Quaternion.LookRotation((middle + Vector3.up * 1f) - eye);

                Log.Info($"[Screenshot] {fileName}: {note}; " +
                         $"subject at {subject}, camera {Vector3.Distance(subject, eye):0.0}m away");

                // Two frames: one for the move to take effect, one to be rendered.
                yield return null;
                yield return null;
                yield return Capture(fileName);
            }
            finally
            {
                lens.position = wasAt;
                lens.rotation = wasFacing;
                rig.enabled = wasEnabled;

                if (sky != null)
                {
                    sky.m_debugTimeOfDay = wasDebugTime;
                    sky.m_debugTime = wasTime;
                }
            }
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
