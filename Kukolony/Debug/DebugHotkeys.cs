using Kukolony.Core;
using Kukolony.Villagers;
using UnityEngine;

namespace Kukolony.Debug
{
    /// <summary>
    ///     Development aid: spawns a villager in front of the player.
    ///
    ///     Deliberately not a console command - Valheim's console needs the -console
    ///     launch argument, and its F5 binding collides with macOS function keys.
    ///     Replaced by proper in-game recruitment once that exists.
    /// </summary>
    internal sealed class DebugHotkeys : MonoBehaviour
    {
        private const KeyCode SpawnKey = KeyCode.K;
        private const float SpawnDistance = 4f;

        private void Update()
        {
            if (!ModConfig.DebugSpawnEnabled.Value)
            {
                return;
            }

            Player player = Player.m_localPlayer;
            if (player == null || ZNetScene.instance == null)
            {
                return;
            }

            if (!IsChordPressed())
            {
                return;
            }

            Spawn(player);
        }

        private static bool IsChordPressed()
        {
            // Don't fire while the player is typing, wherever they are typing.
            if (Gui.Typing.Now())
            {
                return false;
            }

            return Input.GetKey(KeyCode.LeftControl)
                   && Input.GetKey(KeyCode.LeftShift)
                   && Input.GetKeyDown(SpawnKey);
        }

        private static void Spawn(Player player)
        {
            GameObject prefab = ZNetScene.instance.GetPrefab(VillagerPrefab.PrefabName);
            if (prefab == null)
            {
                Log.Error($"Cannot spawn - prefab '{VillagerPrefab.PrefabName}' is not registered.");
                return;
            }

            Vector3 position = player.transform.position
                               + player.transform.forward * SpawnDistance
                               + Vector3.up;

            // Instantiate directly rather than ZNetScene.SpawnObject, which broadcasts an
            // RPC to everyone. ZNetView.Awake creates the ZDO and we become its owner.
            Object.Instantiate(prefab, position, Quaternion.identity);

            Log.Info($"Spawned villager at ({position.x:F1}, {position.y:F1}, {position.z:F1})");
        }
    }
}
