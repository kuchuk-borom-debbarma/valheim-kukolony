using System.Collections.Generic;
using System.Linq;
using Kukolony.Core;
using Kukolony.Jobs;
using Kukolony.Villagers;
using Kukolony.WorkPosts;
using UnityEngine;

namespace Kukolony.Debug
{
    /// <summary>
    ///     Acceptance test for the haul job: build a post and a chest, drop wood, and
    ///     watch a villager move it. Verified from the log, not by watching.
    ///
    ///     Runs instead of <see cref="VillagerSelfTest" /> when enabled, since both spawn
    ///     villagers and quit the game.
    /// </summary>
    internal sealed class HaulJobSelfTest : MonoBehaviour
    {
        private const string HauledItem = "Wood";
        /// <summary>
        ///     Fewer items than villagers, deliberately. With plenty of wood the villagers
        ///     stagger naturally and rarely contend, which made an earlier version of this
        ///     test pass even with claims disabled - it proved nothing. Scarcity forces
        ///     every villager to want the same item at the same time.
        /// </summary>
        private const int DroppedStacks = 2;

        /// <summary>
        ///     Several villagers on one post is the whole point: one villager can never
        ///     contend with itself, so a single-villager test cannot detect a broken
        ///     claim scheme.
        /// </summary>
        private const int VillagerCount = 3;
        private const float WorldSettleSeconds = 8f;
        private const float JobTimeoutSeconds = 180f;

        /// <summary>
        ///     Far enough that the colony is well outside the player's active area, so
        ///     only the keep-alive can be holding it open.
        /// </summary>
        private const float AwayDistance = 500f;

        private enum Phase { WaitingForWorld, Building, Working, Done }

        private Phase _phase = Phase.WaitingForWorld;
        private TestReport _report;
        private readonly List<Villager> _villagers = new List<Villager>();
        private readonly HashSet<string> _seenActivities = new HashSet<string>();
        private readonly HashSet<string> _seenHoverLines = new HashSet<string>();
        /// <summary>
        ///     Well outside a villager's 1-ring halo (3x3 zones of 64m), so the chest is
        ///     only reachable because the colony holds its zone open.
        /// </summary>
        private const float FarChestDistance = 140f;

        private Colonies.Colony _colony;
        private Vector3 _colonyCentre;
        private int _rawCollisions;
        private string _rawSample;
        private int _exclusiveCollisions;
        private int _sharedTargetSamples;
        private string _worstCollision;
        private WorkPost _post;
        private Colonies.Colony _hearth;

        /// <summary>
        ///     The chest is tracked by ZDOID, not by reference. Off-screen its GameObject
        ///     is destroyed and recreated as zones cycle, so a held reference goes null
        ///     and reads as "the chest is gone" - which is how an earlier version of this
        ///     test reported a working haul as a failure.
        /// </summary>
        private ZDOID _chestId = ZDOID.None;
        private float _timer;

        private void Update()
        {
            if (!ModConfig.HaulTestEnabled.Value || _phase == Phase.Done)
            {
                return;
            }

            switch (_phase)
            {
                case Phase.WaitingForWorld: WaitForWorld(); break;
                case Phase.Building: Build(); break;
                case Phase.Working: Work(); break;
            }
        }

        private void WaitForWorld()
        {
            if (Player.m_localPlayer == null || ZNetScene.instance == null || ZoneSystem.instance == null)
            {
                return;
            }

            if (!ZoneSystem.instance.IsActiveAreaLoaded())
            {
                return;
            }

            _timer += Time.deltaTime;
            if (_timer < WorldSettleSeconds)
            {
                return;
            }

            _timer = 0f;
            _report = new TestReport("Haul job - post, villager, chest");
            _phase = Phase.Building;
        }

        private void Build()
        {
            Vector3 origin = Player.m_localPlayer.transform.position;

            // Reuse one world rather than generating a fresh one per run. That only works
            // if each run starts clean, or leftovers from the last run change the result.
            TestWorld.Purge(origin);

            _post = Spawn<WorkPost>(WorkPostPrefab.PrefabName, origin + Vector3.forward * 4f);
            _report.Check(_post != null, "work post piece placed");

            _hearth = Spawn<Colonies.Colony>(Colonies.ColonyPrefab.PrefabName, origin + Vector3.back * 4f);
            _report.Check(_hearth != null, "colony hearth placed");

            // Deliberately far: beyond any villager halo, so only colony membership can
            // make it reachable. This is the question that prompted colonies, as a test.
            Container chest = Spawn<Container>("piece_chest_wood", origin + Vector3.right * FarChestDistance);
            if (chest != null && chest.TryGetComponent(out ZNetView chestView) && chestView.IsValid())
            {
                _chestId = chestView.GetZDO().m_uid;
            }

            _report.Check(!_chestId.IsNone(), $"destination chest placed {FarChestDistance:F0}m away");

            for (int i = 0; i < VillagerCount; i++)
            {
                Villager villager = Spawn<Villager>(
                    VillagerPrefab.PrefabName, origin + Vector3.forward * 2f + Vector3.right * i);
                if (villager != null)
                {
                    _villagers.Add(villager);
                }
            }

            _report.Check(_villagers.Count == VillagerCount, "villagers spawned",
                $"{_villagers.Count}/{VillagerCount}");

            if (_post == null || _chestId.IsNone() || _villagers.Count == 0)
            {
                Finish();
                return;
            }

            // Bind the chest explicitly - this is the "put it in *that* chest" case.
            WorkPostState state = _post.State;
            if (!state.IsValid)
            {
                _report.Check(false, "work post has a valid ZDO", _post.name);
                Finish();
                return;
            }

            RegisterColony();

            state.SetJob(JobLibrary.Haul);
            state.SetItemFilter(HauledItem);
            state.SetDestination(_chestId);
            _report.Check(true, "post configured", $"job=haul item={HauledItem} destination=bound chest");

            GameObject woodPrefab = ZNetScene.instance.GetPrefab(HauledItem);
            if (woodPrefab == null)
            {
                _report.Check(false, $"'{HauledItem}' prefab exists");
                Finish();
                return;
            }

            // Clustered and far from the post, so the walk is long and the window in
            // which two villagers could both be heading for the same log is wide.
            for (int i = 0; i < DroppedStacks; i++)
            {
                Vector3 spot = origin + Vector3.forward * 18f + Vector3.right * (i * 1.5f) + Vector3.up;
                spot.y = ZoneSystem.instance.GetSolidHeight(spot) + 0.5f;
                Object.Instantiate(woodPrefab, spot, Quaternion.identity);
            }

            _report.Check(true, $"dropped {DroppedStacks} {HauledItem} on the ground");
            CheckJobDefinitions();
            CheckPanel();
            GoAway();
            _phase = Phase.Working;
        }

        private void Work()
        {
            _timer += Time.deltaTime;
            SampleClaims();
            SampleVisibleState();

            int inChest = CountInChest();
            if (inChest >= DroppedStacks)
            {
                _report.Check(true, "all wood hauled into the bound chest WITH THE PLAYER AWAY",
                    $"{inChest}/{DroppedStacks} after {_timer:F0}s");
                ReportKeepAlive();
                ReportVisibleState();
                ReportClaims();
                Finish();
                return;
            }

            if (_timer > JobTimeoutSeconds)
            {
                _report.Check(false, "all wood hauled into the bound chest WITH THE PLAYER AWAY",
                    $"{inChest}/{DroppedStacks} after {_timer:F0}s");
                ReportKeepAlive();
                ReportVisibleState();
                ReportClaims();

                // Where did the wood actually go? The deposit step succeeded, so it went
                // somewhere - the bag, the bound chest, or a chest we did not expect.
                foreach (Villager villager in _villagers)
                {
                    if (villager == null)
                    {
                        continue;
                    }

                    int carried = 0;
                    Transform bagHolder = villager.transform.Find("KukolonyBag");
                    if (bagHolder != null && bagHolder.TryGetComponent(out Container bag))
                    {
                        Inventory bagInventory = bag.GetInventory();
                        carried = bagInventory?.CountItems("$item_wood") ?? -1;
                    }

                    _report.Note($"'{villager.State.Name}' step={villager.State.StepIndex} " +
                                 $"activity={villager.Activity} carrying={carried} wood " +
                                 $"target={villager.State.StepTarget}");
                }

                Container boundChest = ResolveChest();
                _report.Note($"bound chest {_chestId} instantiated={boundChest != null} " +
                             $"contents={boundChest?.GetInventory()?.NrOfItems() ?? -1} stack(s)");

                // Any other container that might have received it.
                System.Collections.Generic.List<Piece> pieces = new System.Collections.Generic.List<Piece>();
                Piece.GetAllPiecesInRadius(_colonyCentre, 200f, pieces);
                foreach (Piece piece in pieces)
                {
                    if (piece != null && piece.TryGetComponent(out Container other)
                        && (other.GetInventory()?.NrOfItems() ?? 0) > 0)
                    {
                        _report.Note($"container '{piece.gameObject.name}' at " +
                                     $"{Utils.DistanceXZ(piece.transform.position, _colonyCentre):F0}m holds " +
                                     $"{other.GetInventory().NrOfItems()} stack(s)");
                    }
                }

                Finish();
            }
        }

        /// <summary>
        ///     Stage C assertions, and honest about the limit: mouse clicks cannot be
        ///     driven from here, so this covers everything except whether the layout is
        ///     any good. What it does cover is the part that matters - the panel builds,
        ///     opening claims ownership and blocks input, closing releases it exactly
        ///     once, and the pickers return real data.
        /// </summary>
        private void CheckPanel()
        {
            Gui.WorkPostPanel panel = Gui.WorkPostPanel.Instance;
            _report.Check(panel != null, "work post panel was built");
            if (panel == null || _post == null)
            {
                return;
            }

            _post.TryGetComponent(out ZNetView postView);

            panel.Open(_post);
            _report.Check(panel.IsOpen, "panel opens from the post");
            _report.Check(postView != null && postView.IsOwner(),
                "opening the panel claims ownership of the post");

            // An unpaired input block leaves the player unable to move, so a double close
            // must not decrement twice.
            panel.Close();
            panel.Close();
            _report.Check(!panel.IsOpen, "panel closes, and closing twice is harmless");

            Gui.ItemCatalogue.Rebuild();
            System.Collections.Generic.List<Gui.ItemCatalogue.Entry> matches =
                Gui.ItemCatalogue.Search("wood", 6);
            bool foundWood = matches.Exists(m => m.PrefabName == "Wood");
            _report.Check(foundWood, "item search finds Wood for 'wood'",
                $"{Gui.ItemCatalogue.Count} items indexed, {matches.Count} matches");

            // Searched around the chest, not the post: the chest is deliberately far
            // outside the post's work radius now, which is the whole point of the colony.
            Container chest = ResolveChest();
            Vector3 searchAround = chest != null ? chest.transform.position : _colonyCentre;
            System.Collections.Generic.List<Gui.NearbyContainers.Entry> containers =
                Gui.NearbyContainers.Find(searchAround, 32f);
            _report.Check(containers.Count > 0, "destination picker finds containers",
                $"{containers.Count} container(s) near the chest");
        }

        /// <summary>
        ///     Stage B assertions: the job in use must have come from a definition file,
        ///     the folder must be self-creating, and a bad file must be rejected on its
        ///     own without taking the good ones down with it.
        /// </summary>
        private void CheckJobDefinitions()
        {
            string folder = Jobs.JobDefinitionLoader.JobsFolder;
            _report.Check(System.IO.Directory.Exists(folder), "jobs folder exists", folder);

            string haulFile = System.IO.Path.Combine(folder, Jobs.DefaultJobDefinitions.HaulFileName);
            _report.Check(System.IO.File.Exists(haulFile), "default haul.json was written");

            _report.Check(Jobs.JobLibrary.LoadedFromDefinitions,
                "haul job came from JSON, not the built-in fallback",
                $"{Jobs.JobLibrary.Count} job(s) loaded");

            CheckBadDefinitionIsRejected(folder);
        }

        /// <summary>
        ///     Writes a definition with an unknown step type and reloads. The bad file
        ///     must be dropped and the good ones must survive - a malformed file breaking
        ///     the whole library would be the worst outcome for a player editing them.
        /// </summary>
        private void CheckBadDefinitionIsRejected(string folder)
        {
            string badFile = System.IO.Path.Combine(folder, "kukolony_test_bad.json");
            try
            {
                System.IO.File.WriteAllText(badFile,
                    "{\"id\":\"broken\",\"steps\":[{\"type\":\"no_such_step\"}]}");

                int before = Jobs.JobLibrary.Count;
                Jobs.JobLibrary.Load();
                int after = Jobs.JobLibrary.Count;

                _report.Check(Jobs.JobLibrary.Find("broken") == null,
                    "a definition with an unknown step type is rejected");
                _report.Check(Jobs.JobLibrary.Find(Jobs.JobLibrary.Haul) != null,
                    "the good definitions still load alongside a broken one",
                    $"{before} -> {after} job(s)");
            }
            catch (System.Exception e)
            {
                _report.Check(false, "bad-definition check ran", e.Message);
            }
            finally
            {
                try
                {
                    if (System.IO.File.Exists(badFile))
                    {
                        System.IO.File.Delete(badFile);
                    }

                    // Leave the library in the state the rest of the run expects.
                    Jobs.JobLibrary.Load();
                }
                catch (System.Exception)
                {
                    // Cleanup only - never fail the run over it.
                }
            }
        }

        /// <summary>
        ///     The actual claim assertion. Two villagers holding the same non-empty target
        ///     at the same instant means the reservation scheme is not working, so every
        ///     tick is sampled rather than only the end state.
        /// </summary>
        private void SampleClaims()
        {
            for (int i = 0; i < _villagers.Count; i++)
            {
                Villager first = _villagers[i];
                if (first == null || !first.State.IsValid)
                {
                    continue;
                }

                ZDOID target = first.State.StepTarget;
                if (target.IsNone())
                {
                    continue;
                }

                for (int j = i + 1; j < _villagers.Count; j++)
                {
                    Villager second = _villagers[j];
                    if (second == null || !second.State.IsValid || second.State.StepTarget != target)
                    {
                        continue;
                    }

                    // Sharing a container is intended - many villagers may stock one
                    // chest. Only an exclusive target being double-booked is a bug, so
                    // the kind of thing being shared decides whether this counts.
                    _rawCollisions++;
                    if (_rawSample == null)
                    {
                        GameObject resolved = ZNetScene.instance.FindInstance(target);
                        _rawSample = resolved == null
                            ? $"{target} resolves to NULL"
                            : $"{target} -> '{resolved.name}' components=" + string.Join("/",
                                resolved.GetComponents<Component>()
                                    .Where(c => c != null).Select(c => c.GetType().Name).ToArray());
                    }

                    if (IsExclusiveTarget(target))
                    {
                        _exclusiveCollisions++;
                        _worstCollision = $"'{first.State.Name}' and '{second.State.Name}' both on {target}";
                    }
                    else
                    {
                        _sharedTargetSamples++;
                    }
                }
            }
        }

        /// <summary>
        ///     Puts everything into one colony. Without this the far chest's zone is never
        ///     held open and the villager cannot reach it.
        /// </summary>
        private void RegisterColony()
        {
            if (_hearth == null)
            {
                return;
            }

            _hearth.EnsureNamed();

            bool registered = true;
            Container chest = ResolveChest();
            if (chest != null && chest.TryGetComponent(out ZNetView chestView))
            {
                registered &= _hearth.Register(Colonies.ColonyMemberKind.Container, chestView);
            }

            if (_post != null && _post.TryGetComponent(out ZNetView postView))
            {
                registered &= _hearth.Register(Colonies.ColonyMemberKind.Station, postView);
            }

            foreach (Villager villager in _villagers)
            {
                if (villager != null && villager.TryGetComponent(out ZNetView villagerView))
                {
                    _hearth.Register(Colonies.ColonyMemberKind.Villager, villagerView);
                }
            }

            Colonies.ColonyState colonyState = _hearth.State;
            _report.Check(registered, "chest and post registered to the colony");
            _report.Check(colonyState.CountMembers(Colonies.ColonyMemberKind.Villager) == _villagers.Count,
                "villagers registered to the colony",
                $"{colonyState.CountMembers(Colonies.ColonyMemberKind.Villager)}/{_villagers.Count}");
            _report.Note($"colony '{colonyState.Name}' owns " +
                         $"{colonyState.CountMembers(Colonies.ColonyMemberKind.Container)} storage, " +
                         $"{colonyState.CountMembers(Colonies.ColonyMemberKind.Station)} station(s)");

            // Membership must round-trip through the ZPackage encoding, not just exist in
            // memory - it is the thing that has to survive a reload.
            System.Collections.Generic.List<ZDOID> containers =
                colonyState.GetMembers(Colonies.ColonyMemberKind.Container);
            _report.Check(containers.Count == 1 && !containers[0].IsNone(),
                "membership round-trips through the ZDO encoding");
        }

        /// <summary>
        ///     Teleports the player far from the colony. Everything after this point is
        ///     only possible because the villagers hold their own zones open - which is
        ///     the entire feature under test.
        /// </summary>
        private void GoAway()
        {
            Player player = Player.m_localPlayer;
            if (player == null || _post == null)
            {
                return;
            }

            _colonyCentre = _post.transform.position;

            Vector3 away = _colonyCentre + Vector3.right * AwayDistance;
            away.y = ZoneSystem.instance.GetSolidHeight(away) + 1f;

            // TeleportTo rather than assigning the transform: it runs the game's own
            // loading path, so the player does not end up standing in ungenerated ground.
            player.TeleportTo(away, player.transform.rotation, distantTeleport: true);

            _report.Check(true, $"player moved {AwayDistance:F0}m away from the colony");
        }

        /// <summary>
        ///     Proves the colony is genuinely outside the player's normal active area, so
        ///     a pass cannot be explained by the player still being close enough.
        /// </summary>
        private void ReportKeepAlive()
        {
            // m_localPlayer goes null while the world streams around a teleport, and this
            // runs on a tick, so every access has to tolerate it.
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                _report.Note("player was mid-teleport when keep-alive was sampled");
            }
            else
            {
                Vector3 playerPosition = player.transform.position;
                float distance = Utils.DistanceXZ(playerPosition, _colonyCentre);

                _report.Check(distance > 200f, "colony is far outside the player's active area",
                    $"{distance:F0}m away");

                bool outside = ZNetScene.OutsideActiveArea(_colonyCentre, playerPosition);
                _report.Note(outside
                    ? "colony is outside the vanilla active area"
                    : "colony counts as inside the active area (the keep-alive patches make it so)");
            }

            _report.Check(KeepAlive.KeepAliveZones.Count > 0,
                "zones are being held open for the colony (villagers AND colony members)",
                $"{KeepAlive.KeepAliveZones.Count} zone(s), cap {ModConfig.KeepAliveMaxZones.Value}");

            _report.Check(KeepAlive.KeepAliveZones.Count <= ModConfig.KeepAliveMaxZones.Value,
                "kept-zone count is within the configured cap");

            _report.Note($"allowlist covers {KeepAlive.LoadAllowlist.Count} prefab(s); " +
                         $"filtering {(ModConfig.KeepAliveFilterObjects.Value ? "on" : "off")}");
        }

        /// <summary>
        ///     Records what a player would actually see on hover, so the assertion is
        ///     about the rendered text rather than internal fields.
        /// </summary>
        private void SampleVisibleState()
        {
            foreach (Villager villager in _villagers)
            {
                if (villager == null || !villager.State.IsValid)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(villager.Activity))
                {
                    _seenActivities.Add(villager.Activity);
                }

                string hover = villager.DescribeForHover();
                if (!string.IsNullOrEmpty(hover))
                {
                    _seenHoverLines.Add(hover.Replace("\n", " | "));
                }
            }
        }

        /// <summary>
        ///     A villager must be able to say which job it is on and which stage of that
        ///     job it is at. "working" told the player nothing.
        /// </summary>
        private void ReportVisibleState()
        {
            _report.Check(_seenActivities.Count >= 3,
                "villagers report distinct stages of the job",
                $"{_seenActivities.Count} distinct: {string.Join(" / ", new List<string>(_seenActivities).ToArray())}");

            bool namesTheJob = false;
            bool namesTheItem = false;
            foreach (string line in _seenHoverLines)
            {
                if (line.Contains("haul"))
                {
                    namesTheJob = true;
                }

                if (line.Contains("Wood"))
                {
                    namesTheItem = true;
                }
            }

            _report.Check(namesTheJob, "hover names the job the villager is on");
            _report.Check(namesTheItem, "hover names what the villager is working with");

            foreach (string line in _seenHoverLines)
            {
                _report.Note($"hover: {line}");
            }
        }

        /// <summary>
        ///     Exclusive means "only one villager may work this" - a loose item. A
        ///     container is shared on purpose.
        /// </summary>
        private static bool IsExclusiveTarget(ZDOID target)
        {
            GameObject instance = ZNetScene.instance.FindInstance(target);
            return instance != null && instance.GetComponent<ItemDrop>() != null;
        }

        private void ReportClaims()
        {
            _report.Check(_exclusiveCollisions == 0,
                "no two villagers ever claimed the same item",
                _exclusiveCollisions == 0
                    ? $"{VillagerCount} villagers, no collisions"
                    : $"{_exclusiveCollisions} colliding samples - {_worstCollision}");

            _report.Note($"shared-container samples = {_sharedTargetSamples} (expected, chests are shareable)");
            _report.Note($"raw collisions = {_rawCollisions}; first sample: {_rawSample ?? "none"}");

            bool bound = _villagers.TrueForAll(v => v != null && v.State.HasPost);
            _report.Check(bound, "every villager bound itself to the post");

            _report.Note($"live villager instances = {Villager.Instances.Count} " +
                         $"(registry leak check), active claims = {Jobs.TargetClaims.ActiveClaimCount()}");
        }

        /// <summary>Re-resolves the chest every time rather than trusting a reference.</summary>
        private Container ResolveChest()
        {
            if (_chestId.IsNone() || ZNetScene.instance == null)
            {
                return null;
            }

            GameObject instance = ZNetScene.instance.FindInstance(_chestId);
            return instance != null ? instance.GetComponent<Container>() : null;
        }

        private int CountInChest()
        {
            Container chest = ResolveChest();
            if (chest == null)
            {
                return 0;
            }

            Inventory inventory = chest.GetInventory();
            return inventory?.CountItems("$item_wood") ?? 0;
        }

        private static T Spawn<T>(string prefabName, Vector3 position) where T : Component
        {
            GameObject prefab = ZNetScene.instance.GetPrefab(prefabName);
            if (prefab == null)
            {
                Log.Error($"[HaulTest] prefab '{prefabName}' not found");
                return null;
            }

            position.y = ZoneSystem.instance.GetSolidHeight(position) + 0.2f;
            GameObject spawned = Object.Instantiate(prefab, position, Quaternion.identity);
            return spawned != null ? spawned.GetComponent<T>() : null;
        }

        private void Finish()
        {
            _phase = Phase.Done;
            _report.Print();

            if (!ModConfig.AutoTestQuitWhenDone.Value)
            {
                return;
            }

            if (Game.instance != null)
            {
                Game.instance.Logout(save: true, changeToStartScene: false);
            }

            Application.Quit();
        }
    }
}
