using Kukolony.Core;
using Kukolony.Villagers;
using UnityEngine;

namespace Kukolony.Debug
{
    /// <summary>
    ///     Answers the one question the client build cannot: does a dedicated server
    ///     actually instantiate GameObjects and tick AI, or does it only relay ZDOs?
    ///
    ///     It matters because a dedicated server parks its reference position at
    ///     (1000000, 0, 1000000), roughly 1000km outside the playable world, so vanilla
    ///     creates nothing there. If our keep-alive cannot make it create anything either,
    ///     then server-owned idle colonies are impossible and the design needs owner
    ///     election instead - see docs/multiplayer.md.
    ///
    ///     Runs only on a dedicated server. Spawns its own villager, because a dedicated
    ///     server has no local player to spawn one for it.
    /// </summary>
    internal sealed class DedicatedServerProbe : MonoBehaviour
    {
        private const float ReportIntervalSeconds = 5f;
        private const float SpawnAfterSeconds = 10f;
        private const float QuitAfterSeconds = 70f;

        private float _elapsed;
        private float _nextReport;
        private bool _spawned;
        private Villager _subject;
        private string _firstActivity;
        private bool _sawActivityChange;

        private void Update()
        {
            if (ZNet.instance == null || !ZNet.instance.IsDedicated())
            {
                return;
            }

            if (ZNetScene.instance == null || ZoneSystem.instance == null || ZDOMan.instance == null)
            {
                return;
            }

            _elapsed += Time.deltaTime;

            if (!_spawned && _elapsed > SpawnAfterSeconds)
            {
                _spawned = true;
                SpawnSubject();
            }

            TrackSubject();

            if (_elapsed >= _nextReport)
            {
                _nextReport = _elapsed + ReportIntervalSeconds;
                Report();
            }

            if (_elapsed > QuitAfterSeconds)
            {
                Summarise();
                Application.Quit();
            }
        }

        /// <summary>
        ///     Spawns near world origin rather than near the reference position, since
        ///     the reference position is the parked one out at a million.
        /// </summary>
        private void SpawnSubject()
        {
            GameObject prefab = ZNetScene.instance.GetPrefab(VillagerPrefab.PrefabName);
            if (prefab == null)
            {
                Log.Error("[ServerProbe] villager prefab is not registered on the server");
                return;
            }

            Vector3 position = new Vector3(0f, 0f, 0f);
            position.y = ZoneSystem.instance.GetSolidHeight(position) + 1f;

            GameObject spawned = Object.Instantiate(prefab, position, Quaternion.identity);
            _subject = spawned != null ? spawned.GetComponent<Villager>() : null;

            Log.Info($"[ServerProbe] spawned villager at ({position.x:F0},{position.y:F0},{position.z:F0}) " +
                     $"-> {(_subject != null ? "component ok" : "NO COMPONENT")}");
        }

        /// <summary>
        ///     A changing activity is the proof that AI is actually ticking, rather than
        ///     the object merely existing.
        /// </summary>
        private void TrackSubject()
        {
            if (_subject == null)
            {
                return;
            }

            string activity = _subject.Activity;
            if (_firstActivity == null)
            {
                _firstActivity = activity;
            }
            else if (!_sawActivityChange && activity != _firstActivity)
            {
                _sawActivityChange = true;
                Log.Info($"[ServerProbe] villager AI is ticking: '{_firstActivity}' -> '{activity}'");
            }
        }

        private void Report()
        {
            Vector3 reference = ZNet.instance.GetReferencePosition();

            Log.Info(
                $"[ServerProbe] t={_elapsed:F0}s " +
                $"dedicated={ZNet.instance.IsDedicated()} server={ZNet.instance.IsServer()} " +
                $"refPos=({reference.x:F0},{reference.y:F0},{reference.z:F0}) " +
                $"zones={ZoneSystem.instance.m_zones.Count} " +
                $"activeAreaLoaded={ZoneSystem.instance.IsActiveAreaLoaded()} " +
                $"instances={ZNetScene.instance.NrOfInstances()} " +
                $"baseAI={BaseAI.Instances.Count} " +
                $"villagers={Villager.Instances.Count} " +
                $"keptZones={KeepAlive.KeepAliveZones.Count}");
        }

        private void Summarise()
        {
            bool villagerExists = _subject != null;
            bool hasZdo = villagerExists && _subject.State.IsValid;

            TestReport report = new TestReport("Dedicated server - can it simulate?");
            report.Check(ZNetScene.instance.NrOfInstances() > 0,
                "server instantiates GameObjects at all",
                $"{ZNetScene.instance.NrOfInstances()} instance(s)");
            report.Check(villagerExists, "spawned villager survives on the server");
            report.Check(hasZdo, "villager has a valid ZDO",
                villagerExists ? _subject.Diagnose() : "no villager");
            report.Check(BaseAI.Instances.Count > 0, "BaseAI instances exist to be ticked",
                $"{BaseAI.Instances.Count}");
            report.Check(_sawActivityChange, "villager AI actually ticked (activity changed)",
                _sawActivityChange ? "yes" : $"stuck on '{_firstActivity}'");
            report.Note($"reference position {ZNet.instance.GetReferencePosition()}");
            report.Note($"zones loaded {ZoneSystem.instance.m_zones.Count}, " +
                        $"kept zones {KeepAlive.KeepAliveZones.Count}");
            report.Print();
        }
    }
}
