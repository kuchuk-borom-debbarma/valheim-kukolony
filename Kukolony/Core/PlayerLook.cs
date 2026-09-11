using UnityEngine;

namespace Kukolony.Core
{
    /// <summary>
    ///     What the player is pointing at.
    /// </summary>
    /// <remarks>
    ///     Pointing is how a player says "that one" without holding a tool, so both the marker
    ///     hotkey and the screen's context capture ask this. One raycast, one layer mask: two
    ///     copies would drift, and the drift would show up as one route registering something
    ///     the other refused.
    /// </remarks>
    internal static class PlayerLook
    {
        /// <summary>How far the player can reach, in metres.</summary>
        internal const float Reach = 12f;

        private static readonly int Layers = LayerMask.GetMask(
            "Default", "static_solid", "Default_small", "piece", "item", "vehicle");

        /// <summary>
        ///     The networked object under the crosshair, or null. Looking at nothing is an
        ///     ordinary answer, not an error.
        /// </summary>
        internal static GameObject Target(Player player)
        {
            if (player == null || GameCamera.instance == null)
            {
                return null;
            }

            Transform eye = GameCamera.instance.transform;
            if (!Physics.Raycast(eye.position, eye.forward, out RaycastHit hit, Reach + 5f, Layers))
            {
                return null;
            }

            // Colliders usually hang off a child of the networked object, so walk up rather
            // than demanding the hit be the root.
            ZNetView view = hit.collider.GetComponentInParent<ZNetView>();
            return view != null && view.IsValid() ? view.gameObject : null;
        }
    }
}
