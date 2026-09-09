using System.Collections.Generic;
using System.Linq;
using System.Text;
using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Debug
{
    /// <summary>
    ///     One-shot diagnostic that answers questions the decompiled assembly cannot:
    ///     what prefabs actually exist and what components they carry. Prefab contents
    ///     are asset data, not code, so this is the only way to know for certain.
    ///
    ///     Run it once, read the log, delete the guesswork. Not part of the mod.
    /// </summary>
    internal sealed class PrefabProbe : MonoBehaviour
    {
        private bool _done;

        private void Update()
        {
            if (_done || !ModConfig.DebugProbeEnabled.Value)
            {
                return;
            }

            if (ZNetScene.instance == null || ObjectDB.instance == null || Player.m_localPlayer == null)
            {
                return;
            }

            _done = true;
            Probe();

            if (ModConfig.AutoTestQuitWhenDone.Value)
            {
                Log.Info("[Probe] done - quitting.");
                Application.Quit();
            }
        }

        private static void Probe()
        {
            List<GameObject> prefabs = ZNetScene.instance.m_prefabs;
            Log.Info($"[Probe] ZNetScene holds {prefabs.Count} prefabs");

            ReportPlayerPrefab(prefabs);
            ReportVisEquipmentRigs(prefabs);
            ReportRandomisedHumanoids(prefabs);
            ReportComponents(prefabs, "Dverger");
            ReportSmallPieces(prefabs);
        }

        /// <summary>Can we clone the player body rig at all?</summary>
        private static void ReportPlayerPrefab(List<GameObject> prefabs)
        {
            GameObject player = prefabs.FirstOrDefault(p => p != null && p.name == "Player");
            if (player == null)
            {
                Log.Warning("[Probe] no 'Player' prefab in ZNetScene - cloning the player rig is not possible this way");
                return;
            }

            Log.Info("[Probe] 'Player' prefab IS present in ZNetScene");
            LogComponents("Player", player);

            if (player.TryGetComponent(out VisEquipment vis))
            {
                Log.Info($"[Probe] Player VisEquipment: models={vis.m_models.Length} " +
                         $"isPlayer={vis.m_isPlayer} bodyModel={(vis.m_bodyModel != null ? "set" : "NULL")}");
            }
        }

        /// <summary>Which prefabs carry a swappable body model - i.e. a player-like rig.</summary>
        private static void ReportVisEquipmentRigs(List<GameObject> prefabs)
        {
            List<string> rigs = new List<string>();
            foreach (GameObject prefab in prefabs)
            {
                if (prefab == null || !prefab.TryGetComponent(out VisEquipment vis))
                {
                    continue;
                }

                if (vis.m_models != null && vis.m_models.Length > 0)
                {
                    rigs.Add($"{prefab.name}(models={vis.m_models.Length},isPlayer={vis.m_isPlayer})");
                }
            }

            Log.Info($"[Probe] prefabs with a swappable body model ({rigs.Count}): {Join(rigs)}");
        }

        /// <summary>Vanilla humanoids that already randomise their own gear.</summary>
        private static void ReportRandomisedHumanoids(List<GameObject> prefabs)
        {
            List<string> found = new List<string>();
            foreach (GameObject prefab in prefabs)
            {
                if (prefab == null || !prefab.TryGetComponent(out Humanoid humanoid))
                {
                    continue;
                }

                int sets = humanoid.m_randomSets?.Length ?? 0;
                int armor = humanoid.m_randomArmor?.Length ?? 0;
                int weapon = humanoid.m_randomWeapon?.Length ?? 0;
                if (sets + armor + weapon > 0)
                {
                    found.Add($"{prefab.name}(sets={sets},armor={armor},weapon={weapon})");
                }
            }

            Log.Info($"[Probe] humanoids with randomised gear ({found.Count}): {Join(found)}");
        }

        /// <summary>
        ///     Placeable pieces we could clone as a work post. Filtered to buildable ones
        ///     with an icon, because CustomPiece.IsValid demands an icon and cloning is
        ///     the only way to get one without Unity.
        /// </summary>
        private static void ReportSmallPieces(List<GameObject> prefabs)
        {
            List<string> candidates = new List<string>();
            foreach (GameObject prefab in prefabs)
            {
                if (prefab == null || !prefab.TryGetComponent(out Piece piece))
                {
                    continue;
                }

                if (piece.m_icon == null || !piece.m_enabled)
                {
                    continue;
                }

                bool hasContainer = prefab.GetComponent<Container>() != null;
                bool hasCraftingStation = prefab.GetComponent<CraftingStation>() != null;
                candidates.Add($"{prefab.name}{(hasContainer ? "[chest]" : string.Empty)}" +
                               $"{(hasCraftingStation ? "[station]" : string.Empty)}");
            }

            Log.Info($"[Probe] buildable pieces with icons ({candidates.Count}): {Join(candidates)}");
        }

        private static void ReportComponents(List<GameObject> prefabs, string prefabName)
        {
            GameObject prefab = prefabs.FirstOrDefault(p => p != null && p.name == prefabName);
            if (prefab == null)
            {
                Log.Warning($"[Probe] prefab '{prefabName}' not found");
                return;
            }

            LogComponents(prefabName, prefab);
        }

        private static void LogComponents(string label, GameObject prefab)
        {
            IEnumerable<string> names = prefab.GetComponents<Component>()
                .Where(c => c != null)
                .Select(c => c.GetType().Name);
            Log.Info($"[Probe] {label} components: {Join(names)}");
        }

        private static string Join(IEnumerable<string> values)
        {
            StringBuilder builder = new StringBuilder();
            foreach (string value in values)
            {
                if (builder.Length > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(value);
            }

            return builder.Length == 0 ? "(none)" : builder.ToString();
        }
    }
}
