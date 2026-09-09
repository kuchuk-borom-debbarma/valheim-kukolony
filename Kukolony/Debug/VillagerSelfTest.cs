using System.Linq;
using Kukolony.Core;
using Kukolony.Villagers;
using UnityEngine;

namespace Kukolony.Debug
{
    /// <summary>
    ///     Runs the villager acceptance test in-game and writes a pass/fail block to the
    ///     log, so verification does not depend on a human playing and describing what
    ///     they saw.
    ///
    ///     Two runs cover the whole milestone:
    ///       * first run  - no villager exists, so spawn one, then displace it and watch
    ///                      it walk home;
    ///       * second run - a villager exists from last time, so its name and home must
    ///                      have survived the reload.
    ///
    ///     The run ends by saving and quitting, which is what makes the second run a
    ///     genuine persistence test rather than a repeat of the first.
    /// </summary>
    internal sealed class VillagerSelfTest : MonoBehaviour
    {
        /// <summary>
        ///     How long to let the world stream in before deciding anything. The player
        ///     exists well before nearby zones finish loading, and a villager can be
        ///     instantiated and then destroyed again as zones settle - so deciding which
        ///     kind of run this is any earlier reads the world mid-load.
        /// </summary>
        private const float WorldSettleSeconds = 8f;

        private const float SettleSeconds = 3f;
        private const float DisplaceDistance = 40f;
        private const float WalkHomeTimeout = 90f;

        private enum Phase
        {
            WaitingForWorld,
            Settling,
            CheckingIdentity,
            WalkingHome,
            Done
        }

        private Phase _phase = Phase.WaitingForWorld;
        private TestReport _report;
        private Villager _subject;
        private float _timer;
        private float _startDistance;
        private bool _isReloadRun;

        private void Update()
        {
            // The probe takes over the run when enabled; two things quitting the game
            // at once would truncate whichever log block came second.
            if (!ModConfig.AutoTestEnabled.Value || ModConfig.DebugProbeEnabled.Value
                || ModConfig.HaulTestEnabled.Value || _phase == Phase.Done)
            {
                return;
            }

            switch (_phase)
            {
                case Phase.WaitingForWorld:
                    WaitForWorld();
                    break;
                case Phase.Settling:
                    Settle();
                    break;
                case Phase.CheckingIdentity:
                    CheckIdentity();
                    break;
                case Phase.WalkingHome:
                    WatchWalkHome();
                    break;
            }
        }

        private void WaitForWorld()
        {
            if (Player.m_localPlayer == null || ZNetScene.instance == null || ZoneSystem.instance == null)
            {
                return;
            }

            // Wait for the area around the player to be genuinely loaded, then settle.
            if (!ZoneSystem.instance.IsActiveAreaLoaded())
            {
                return;
            }

            _timer += Time.deltaTime;
            if (_timer < WorldSettleSeconds)
            {
                return;
            }

            _subject = FindVillager();
            _isReloadRun = _subject != null;

            _report = new TestReport(_isReloadRun
                ? "Run 2 - villager loaded from save"
                : "Run 1 - villager spawned fresh");

            if (!_isReloadRun && !SpawnSubject())
            {
                Finish();
                return;
            }

            _timer = 0f;
            _phase = Phase.Settling;
        }

        /// <summary>Let the villager tick a few times so it can tame and name itself.</summary>
        private void Settle()
        {
            _timer += Time.deltaTime;

            // Re-resolve rather than trusting the stored reference. A villager whose zone
            // unloads is destroyed, and a destroyed Unity object compares equal to null.
            if (_subject == null)
            {
                _subject = FindVillager();
            }

            if (_timer < SettleSeconds)
            {
                return;
            }

            _phase = Phase.CheckingIdentity;
        }

        private void CheckIdentity()
        {
            if (_subject == null)
            {
                _subject = FindVillager();
            }

            if (_subject == null)
            {
                _report.Check(false, "villager present in world",
                    _isReloadRun
                        ? "one existed at world load but its zone unloaded before the check"
                        : "spawn did not survive");
                Finish();
                return;
            }

            _report.Check(true, "villager present in world");

            VillagerState state = _subject.State;
            _report.Check(state.IsValid, "villager has a valid ZDO", _subject.Diagnose());
            if (!state.IsValid)
            {
                _report.Note("components: " + string.Join(", ",
                    _subject.GetComponents<Component>()
                        .Where(c => c != null)
                        .Select(c => c.GetType().Name)
                        .ToArray()));
                Finish();
                return;
            }

            _report.Check(state.HasName, "villager has a name", state.Name);
            _report.Check(state.HasHome, "villager has a home",
                $"({state.Home.x:F1}, {state.Home.y:F1}, {state.Home.z:F1})");

            bool tamed = _subject.TryGetComponent(out Character character) && character.IsTamed();
            _report.Check(tamed, "villager is tamed (friendly to the player)");

            CheckPlayerModel();
            CheckBag();

            if (_isReloadRun)
            {
                // The whole point of run 2: these came off disk, not from a fresh spawn.
                _report.Note("name and home above were restored from the save file");
                Finish();
                return;
            }

            Displace();
        }

        /// <summary>
        ///     The villager must be a converted Player clone: a player body rig, but no
        ///     Player component. Leaving Player attached would register it in s_players
        ///     and distort spawners, boss checks and targeting.
        /// </summary>
        private void CheckPlayerModel()
        {
            _report.Check(!_subject.TryGetComponent(out Player _),
                "villager is NOT a Player (would corrupt spawn and targeting logic)");
            _report.Check(_subject.TryGetComponent(out Humanoid _), "villager has a Humanoid");
            _report.Check(_subject.TryGetComponent(out MonsterAI _), "villager has a MonsterAI");

            if (!_subject.TryGetComponent(out VisEquipment vis))
            {
                _report.Check(false, "villager has VisEquipment");
                return;
            }

            _report.Check(vis.m_models != null && vis.m_models.Length > 0,
                "villager has a swappable body model", $"models={vis.m_models?.Length ?? 0}");

            ZDO zdo = _subject.State.IsValid ? _subject.GetComponent<ZNetView>().GetZDO() : null;
            if (zdo == null)
            {
                return;
            }

            // VisEquipment stores equipment slots as the prefab name's stable hash, not
            // the name - GetString here returns empty and reads as "not dressed".
            int chest = zdo.GetInt(ZDOVars.s_chestItem, 0);
            int legs = zdo.GetInt(ZDOVars.s_legItem, 0);
            _report.Check(chest != 0 && legs != 0,
                "villager is dressed", $"chestHash={chest}, legsHash={legs}");

            // Report the ZDO hashes, not VisEquipment's live fields - those are synced
            // from the ZDO over several frames and read as empty if sampled too early.
            // Comparing these across runs is what proves the appearance is stable.
            _report.Note($"appearance hashes: model={zdo.GetInt(ZDOVars.s_modelIndex, -1)} " +
                         $"chest={chest} legs={legs} " +
                         $"hair={zdo.GetInt(ZDOVars.s_hairItem, 0)} " +
                         $"beard={zdo.GetInt(ZDOVars.s_beardItem, 0)} " +
                         $"skin={zdo.GetVec3(ZDOVars.s_skinColor, Vector3.zero)}");
        }

        /// <summary>
        ///     The bag must persist like a chest. On the first run we put something in it;
        ///     on the second we check it is still there, which is the only real proof.
        /// </summary>
        private void CheckBag()
        {
            Transform holder = _subject.transform.Find("KukolonyBag");
            if (holder == null || !holder.TryGetComponent(out Container bag))
            {
                _report.Check(false, "villager has a persistent bag");
                return;
            }

            _report.Check(true, "villager has a persistent bag");

            Inventory inventory = bag.GetInventory();
            if (inventory == null)
            {
                _report.Check(false, "bag inventory is initialised (Container found its ZDO)");
                return;
            }

            _report.Check(true, "bag inventory is initialised (Container found its ZDO)");

            if (_isReloadRun)
            {
                int wood = inventory.CountItems("$item_wood");
                _report.Check(wood > 0, "bag contents survived the reload", $"{wood} wood");
                return;
            }

            GameObject woodPrefab = ZNetScene.instance.GetPrefab("Wood");
            if (woodPrefab == null)
            {
                _report.Note("no Wood prefab - skipping bag write");
                return;
            }

            bool added = inventory.AddItem(woodPrefab, 5);
            _report.Check(added, "put 5 wood in the bag (checked again after reload)");
        }

        /// <summary>
        ///     Teleports the villager away from home so the go-home behaviour has
        ///     something to do. Moving it directly is the only way to test this without a
        ///     human luring it.
        /// </summary>
        private void Displace()
        {
            Vector3 home = _subject.State.Home;
            Vector3 displaced = home + new Vector3(DisplaceDistance, 0f, 0f);
            displaced.y = ZoneSystem.instance.GetSolidHeight(displaced) + 0.5f;

            _subject.transform.position = displaced;
            _startDistance = Utils.DistanceXZ(home, _subject.transform.position);

            _report.Note($"displaced villager to {_startDistance:F0}m from home");

            _timer = 0f;
            _phase = Phase.WalkingHome;
        }

        private void WatchWalkHome()
        {
            _timer += Time.deltaTime;

            if (_subject == null)
            {
                _report.Check(false, "villager survived displacement");
                Finish();
                return;
            }

            float distance = Utils.DistanceXZ(_subject.State.Home, _subject.transform.position);

            if (distance <= ModConfig.GoHomeRadius.Value)
            {
                _report.Check(true, "villager walked home",
                    $"{_startDistance:F0}m -> {distance:F0}m in {_timer:F0}s");
                Finish();
                return;
            }

            if (_timer > WalkHomeTimeout)
            {
                bool madeProgress = distance < _startDistance - 5f;
                _report.Check(false, "villager reached home within timeout",
                    $"{_startDistance:F0}m -> {distance:F0}m after {_timer:F0}s");
                _report.Check(madeProgress, "villager at least moved toward home");
                Finish();
            }
        }

        private bool SpawnSubject()
        {
            GameObject prefab = ZNetScene.instance.GetPrefab(VillagerPrefab.PrefabName);
            if (prefab == null)
            {
                _report.Check(false, "villager prefab is registered");
                return false;
            }

            _report.Check(true, "villager prefab is registered");

            Vector3 position = Player.m_localPlayer.transform.position
                               + Player.m_localPlayer.transform.forward * 4f
                               + Vector3.up;

            GameObject spawned = Object.Instantiate(prefab, position, Quaternion.identity);
            _subject = spawned != null ? spawned.GetComponent<Villager>() : null;
            if (spawned != null)
            {
                _report.Note($"spawned '{spawned.name}' active={spawned.activeInHierarchy} " +
                             $"parent='{(spawned.transform.parent != null ? spawned.transform.parent.name : "<none>")}'");
            }

            _report.Check(_subject != null, "spawned villager carries the Villager component");
            return _subject != null;
        }

        private static Villager FindVillager() =>
            Object.FindObjectsByType<Villager>(FindObjectsSortMode.None).FirstOrDefault();

        /// <summary>
        ///     Prints the result, then saves and quits. Saving is what makes the next run
        ///     a real reload test; quitting means an automated run terminates on its own.
        /// </summary>
        private void Finish()
        {
            _phase = Phase.Done;
            _report.Print();

            if (!ModConfig.AutoTestQuitWhenDone.Value)
            {
                Log.Info("[SelfTest] finished; leaving the game running.");
                return;
            }

            Log.Info("[SelfTest] saving and quitting.");
            if (Game.instance != null)
            {
                Game.instance.Logout(save: true, changeToStartScene: false);
            }

            Application.Quit();
        }
    }
}
