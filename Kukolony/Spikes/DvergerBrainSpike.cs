using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Kukolony.Spikes
{
    /// <summary>
    ///     Spike B - diagnostic only, delete once docs/spike-results.md records the outcome.
    ///
    ///     Tests three assumptions the design rests on:
    ///       1. A Harmony prefix on MonsterAI.UpdateAI can suppress vanilla creature behaviour.
    ///       2. The publicized build lets us call the protected BaseAI.MoveTo at runtime
    ///          (i.e. SecurityPermission(SkipVerification) actually works on Unity 6 / arm64).
    ///       3. MoveTo drives real pathfinding when called from our code rather than from
    ///          the vanilla update loop.
    ///
    ///     Evidence goes to BepInEx/LogOutput.log, because the spike is verified by reading
    ///     the log rather than by watching the screen.
    /// </summary>
    [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.UpdateAI))]
    internal static class DvergerBrainSpike
    {
        private const string TargetPrefab = "Dverger";
        private const float LogInterval = 1f;
        private const float StopDistance = 3f;

        private static readonly int TargetPrefabHash = TargetPrefab.GetStableHashCode();

        // Per-instance log throttle and first-sight marker, keyed by Unity instance id.
        private static readonly Dictionary<int, float> NextLogTime = new Dictionary<int, float>();
        private static readonly HashSet<int> Announced = new HashSet<int>();

        // Every distinct non-target prefab we see, logged once. If the target name is
        // wrong, this is what tells us the right one instead of the spike silently
        // doing nothing.
        private static readonly HashSet<int> SeenPrefabs = new HashSet<int>();

        // Latched so a broken assumption is reported once, not 20x per second.
        private static bool _moveToFailureLogged;

        private static bool Prefix(MonsterAI __instance, float dt)
        {
            if (Kukolony.SpikeBrainEnabled == null || !Kukolony.SpikeBrainEnabled.Value)
            {
                return true;
            }

            ZNetView nview = __instance.m_nview;
            if (nview == null || !nview.IsValid())
            {
                return true;
            }

            int prefabHash = nview.GetZDO().GetPrefab();
            if (prefabHash != TargetPrefabHash)
            {
                if (SeenPrefabs.Add(prefabHash))
                {
                    GameObject prefab = ZNetScene.instance != null
                        ? ZNetScene.instance.GetPrefab(prefabHash)
                        : null;
                    string name = prefab != null ? prefab.name : "<unknown>";
                    Jotunn.Logger.LogInfo($"[SpikeB] saw non-target MonsterAI prefab '{name}' (hash={prefabHash})");
                }

                return true; // not a Dverger - leave vanilla alone
            }

            // BaseAI.UpdateAI normally does these checks before anything else. Returning
            // false from this prefix skips it entirely, so we have to do them ourselves
            // or we would be testing the wrong thing.
            if (!nview.IsOwner())
            {
                return false;
            }

            int id = __instance.GetInstanceID();

            if (Announced.Add(id))
            {
                Jotunn.Logger.LogInfo(
                    $"[SpikeB] prefix ACTIVE on {TargetPrefab} id={id} " +
                    $"pos={Fmt(__instance.transform.position)} - vanilla AI suppressed");
            }

            Tick(__instance, dt, id);
            return false; // suppress vanilla monster logic
        }

        private static void Tick(MonsterAI ai, float dt, int id)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                return;
            }

            Vector3 target = player.transform.position;
            Vector3 here = ai.transform.position;
            float distance = Vector3.Distance(here, target);

            bool arrived;
            bool havePath;
            try
            {
                // The actual assumption under test: a protected BaseAI method, reached
                // through the publicized reference assembly.
                havePath = ai.HavePath(target);
                arrived = ai.MoveTo(dt, target, StopDistance, false);
            }
            catch (Exception e)
            {
                if (!_moveToFailureLogged)
                {
                    _moveToFailureLogged = true;
                    Jotunn.Logger.LogError(
                        $"[SpikeB] FAILED calling BaseAI.MoveTo/HavePath: {e.GetType().Name}: {e.Message}");
                    Jotunn.Logger.LogError($"[SpikeB] {e}");
                }
                return;
            }

            if (!NextLogTime.TryGetValue(id, out float next) || Time.time >= next)
            {
                NextLogTime[id] = Time.time + LogInterval;
                Jotunn.Logger.LogInfo(
                    $"[SpikeB] id={id} dist={distance:F2} havePath={havePath} arrived={arrived} " +
                    $"self={Fmt(here)} target={Fmt(target)}");
            }
        }

        private static string Fmt(Vector3 v) => $"({v.x:F1},{v.y:F1},{v.z:F1})";
    }
}
