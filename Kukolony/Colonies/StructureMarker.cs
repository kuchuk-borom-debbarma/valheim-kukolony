using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Colonies
{
    /// <summary>
    ///     Registers whatever the player is looking at, with a key.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Registering used to be a sweep of everything nearby and nothing else, which is
    ///         fine for a base already built and useless for saying "that one". Pointing at a
    ///         thing is how a player says which thing they mean, so that is what this does:
    ///         look at a chest, press a key, it belongs to the colony; press again and it does
    ///         not.
    ///     </para>
    ///     <para>
    ///         It reports what happened either way. Silence is the failure mode this exists to
    ///         avoid - a player who cannot tell "not registerable" from "you missed" learns
    ///         nothing from pressing the key again.
    ///     </para>
    /// </remarks>
    internal sealed class StructureMarker : MonoBehaviour
    {
        /// <summary>How far the player can reach to mark something, in metres.</summary>
        private const float Reach = 12f;

        internal static void Register(GameObject host) => host.AddComponent<StructureMarker>();

        private void Update()
        {
            if (!Input.GetKeyDown(ModConfig.MarkStructureHotkey.Value)) return;
            if (Chat.instance != null && Chat.instance.HasFocus()) return;
            if (Console.IsVisible()) return;

            Player player = Player.m_localPlayer;
            if (player == null) return;
            Toggle(player);
        }

        private static void Toggle(Player player)
        {
            GameObject target = LookingAt(player);
            if (target == null)
            {
                Say("Nothing in reach to mark.");
                return;
            }

            StructureRecord record = StructureRegistry.Describe(target);
            if (record == null)
            {
                Say($"{StructureRegistry.DisplayName(target)} is not something a colony can use.");
                return;
            }

            // Nearest colony rather than "the" colony: a player with two bases should be able
            // to stand in one of them and mark, without first saying which.
            Colony colony = Nearest(target.transform.position);
            if (colony == null)
            {
                Say("No colony hearth within range of that.");
                return;
            }

            if (colony.State.GetStructures().Exists(existing => existing.Id == record.Id))
            {
                colony.RemoveStructure(record.Id);
                Say($"Removed {record.Name} from {colony.State.Name}.");
                return;
            }

            Say(colony.RegisterStructure(record)
                ? $"Registered {record.Name} to {colony.State.Name}."
                : $"Could not register {record.Name}.");
        }

        /// <summary>
        ///     The networked object under the crosshair. Uses the same ray the game uses for
        ///     interaction, so what the player is looking at means the same thing here as it
        ///     does everywhere else.
        /// </summary>
        private static GameObject LookingAt(Player player)
        {
            if (GameCamera.instance == null) return null;
            Transform eye = GameCamera.instance.transform;
            if (!Physics.Raycast(eye.position, eye.forward, out RaycastHit hit, Reach + 5f,
                    LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "item", "vehicle")))
                return null;
            // Colliders usually hang off a child of the networked object, so walk up rather
            // than demanding the hit be the root.
            ZNetView view = hit.collider.GetComponentInParent<ZNetView>();
            return view != null && view.IsValid() ? view.gameObject : null;
        }

        /// <summary>The colony whose radius contains this point, nearest first, or null.</summary>
        private static Colony Nearest(Vector3 point)
        {
            Colony best = null;
            float closest = float.MaxValue;
            foreach (Colony colony in Colony.Instances)
            {
                if (colony == null) continue;
                float distance = Utils.DistanceXZ(colony.transform.position, point);
                if (distance > colony.EffectiveRadius || distance >= closest) continue;
                best = colony;
                closest = distance;
            }
            return best;
        }

        private static void Say(string message)
        {
            Log.Info("[marker] " + message);
            if (MessageHud.instance != null)
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, message);
        }
    }
}
