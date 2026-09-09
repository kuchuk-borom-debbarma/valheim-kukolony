using System.Collections.Generic;
using Kukolony.Core;
using UnityEngine;

namespace Kukolony.KeepAlive
{
    /// <summary>
    ///     The zones held open because a villager is in or near them.
    ///
    ///     The villager is the loader. Rather than a fixed radius around a work post, the
    ///     loaded region follows the villagers, so it costs what the colony actually
    ///     occupies rather than what it might.
    /// </summary>
    internal static class KeepAliveZones
    {
        private static readonly HashSet<Vector2i> Zones = new HashSet<Vector2i>();

        internal static int Count => Zones.Count;

        internal static bool IsEmpty => Zones.Count == 0;

        internal static bool Contains(Vector2i zone) => Zones.Count != 0 && Zones.Contains(zone);

        internal static IEnumerable<Vector2i> All => Zones;

        /// <summary>
        ///     Rebuilds from every villager position we know about - loaded ones from the
        ///     registry, unloaded ones from their ZDOs.
        ///
        ///     Each villager contributes its own zone plus a halo of neighbours, because a
        ///     villager cannot path into unloaded ground: the navmesh is built from
        ///     colliders that are actually present, so it needs somewhere to walk into.
        /// </summary>
        internal static void Rebuild(IEnumerable<Vector3> villagerPositions)
        {
            Zones.Clear();

            if (!ModConfig.KeepAliveEnabled.Value)
            {
                return;
            }

            int rings = ModConfig.KeepAliveHaloRings.Value;
            int cap = ModConfig.KeepAliveMaxZones.Value;
            bool capped = false;

            foreach (Vector3 position in villagerPositions)
            {
                Vector2i centre = ZoneSystem.GetZone(position);

                for (int y = -rings; y <= rings; y++)
                {
                    for (int x = -rings; x <= rings; x++)
                    {
                        if (Zones.Count >= cap)
                        {
                            capped = true;
                            break;
                        }

                        Zones.Add(new Vector2i(centre.x + x, centre.y + y));
                    }
                }
            }

            if (capped)
            {
                // Never truncate silently - a colony that stops working for an invisible
                // reason is the worst possible failure here.
                Log.Warning($"[KeepAlive] zone cap of {cap} reached; some villagers will not be kept loaded.");
            }
        }
    }
}
