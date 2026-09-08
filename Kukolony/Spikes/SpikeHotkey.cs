using UnityEngine;

namespace Kukolony.Spikes
{
    /// <summary>
    ///     Spike B support - diagnostic only, delete with the spike.
    ///
    ///     Spawns the spike target creature on a hotkey so the spike does not depend on
    ///     the vanilla dev console, which needs the -console launch argument and whose
    ///     default F5 binding collides with macOS function keys.
    ///
    ///     Ctrl+Shift+K spawns one in front of the player.
    /// </summary>
    internal class SpikeHotkey : MonoBehaviour
    {
        private const string TargetPrefab = "Dverger";

        private bool _reportedPrefabLookup;

        private void Update()
        {
            if (Kukolony.SpikeBrainEnabled == null || !Kukolony.SpikeBrainEnabled.Value)
            {
                return;
            }

            Player player = Player.m_localPlayer;
            if (player == null || ZNetScene.instance == null)
            {
                return;
            }

            // Answer the "is the prefab name even right?" question as soon as a world is
            // loaded, without needing anyone to press anything.
            if (!_reportedPrefabLookup)
            {
                _reportedPrefabLookup = true;
                GameObject found = ZNetScene.instance.GetPrefab(TargetPrefab);
                if (found == null)
                {
                    Jotunn.Logger.LogError(
                        $"[SpikeB] prefab '{TargetPrefab}' does NOT exist - the spike target name is wrong.");
                }
                else
                {
                    Jotunn.Logger.LogInfo(
                        $"[SpikeB] prefab '{TargetPrefab}' resolved ok. Ctrl+Shift+K spawns one.");
                }
            }

            // Don't fire while typing into chat or the console.
            if (Chat.instance != null && Chat.instance.HasFocus())
            {
                return;
            }

            if (Console.IsVisible())
            {
                return;
            }

            if (!Input.GetKey(KeyCode.LeftControl) || !Input.GetKey(KeyCode.LeftShift))
            {
                return;
            }

            if (!Input.GetKeyDown(KeyCode.K))
            {
                return;
            }

            Spawn(player);
        }

        private static void Spawn(Player player)
        {
            GameObject prefab = ZNetScene.instance.GetPrefab(TargetPrefab);
            if (prefab == null)
            {
                Jotunn.Logger.LogError($"[SpikeB] cannot spawn - prefab '{TargetPrefab}' not found");
                return;
            }

            // A few metres in front of the player, lifted clear of the ground.
            Vector3 pos = player.transform.position
                          + player.transform.forward * 4f
                          + Vector3.up * 1f;

            GameObject spawned = Object.Instantiate(prefab, pos, Quaternion.identity);
            Jotunn.Logger.LogInfo(
                $"[SpikeB] spawned '{TargetPrefab}' at ({pos.x:F1},{pos.y:F1},{pos.z:F1}) " +
                $"instance={(spawned != null ? spawned.GetInstanceID() : 0)}");
        }
    }
}
