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

        /// <summary>
        ///     The layers the game itself uses to decide what you are pointing at.
        /// </summary>
        /// <remarks>
        ///     <c>character</c> and <c>character_net</c> matter more than they look. Without
        ///     them a creature is not hit at all, so pointing at a boar answers "nothing in
        ///     reach" instead of "a boar is not something a colony can use" - a refusal that
        ///     explains nothing, for the case a player is most likely to try.
        /// </remarks>
        private static readonly int Layers = LayerMask.GetMask(
            "Default", "static_solid", "Default_small", "piece", "piece_nonsolid",
            "item", "vehicle", "character", "character_net", "terrain");

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
            RaycastHit[] hits = Physics.RaycastAll(eye.position, eye.forward, Reach + 5f, Layers);
            if (hits.Length == 0)
            {
                return null;
            }

            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null)
                {
                    continue;
                }

                // Skip the player's own body. In third person the camera sits behind the
                // player, so the first thing the ray meets is the player - who would otherwise
                // become the thing being pointed at the moment creatures became hittable.
                if (hit.collider.attachedRigidbody != null &&
                    hit.collider.attachedRigidbody.gameObject == player.gameObject)
                {
                    continue;
                }

                // Colliders usually hang off a child of the networked object, so walk up rather
                // than demanding the hit be the root.
                ZNetView view = hit.collider.GetComponentInParent<ZNetView>();
                if (view == null || !view.IsValid())
                {
                    continue;
                }

                if (view.gameObject == player.gameObject)
                {
                    continue;
                }

                return view.gameObject;
            }

            return null;
        }
    }
}
