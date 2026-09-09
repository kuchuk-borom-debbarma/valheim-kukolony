using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Kukolony.KeepAlive.Patches
{
    /// <summary>
    ///     Brings colony objects into the world, and keeps them there.
    ///
    ///     This is the load-bearing patch. ZNetScene.RemoveObjects destroys every instance
    ///     that is *not* in the lists it is handed, so appending to those lists is
    ///     simultaneously what creates our objects and what stops them being destroyed.
    /// </summary>
    internal static class ZDOManKeepAlivePatch
    {
        /// <summary>Reused so a per-frame patch does not allocate.</summary>
        private static readonly List<ZDO> Buffer = new List<ZDO>();

        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.FindSectorObjects))]
        private static class FindSectorObjects
        {
            private static void Postfix(ZDOMan __instance, Vector2i sector, int area, List<ZDO> sectorObjects)
            {
                if (KeepAliveZones.IsEmpty)
                {
                    return;
                }

                foreach (Vector2i zone in KeepAliveZones.All)
                {
                    // Already covered by the player's own active area - vanilla will load
                    // it in full, and adding it again would duplicate every ZDO.
                    if (zone.x >= sector.x - area && zone.x <= sector.x + area
                        && zone.y >= sector.y - area && zone.y <= sector.y + area)
                    {
                        continue;
                    }

                    Append(__instance, zone, sectorObjects);
                }
            }

            /// <summary>
            ///     Appends only what the colony needs. A zone loaded purely because a
            ///     villager is standing in it does not need its trees, rocks or wildlife
            ///     instantiated - that is the bulk of the objects and none of the value.
            /// </summary>
            private static void Append(ZDOMan zdoMan, Vector2i zone, List<ZDO> destination)
            {
                if (!ModConfig.KeepAliveFilterObjects.Value || !LoadAllowlist.IsReady)
                {
                    zdoMan.FindObjects(zone, destination);
                    return;
                }

                Buffer.Clear();
                zdoMan.FindObjects(zone, Buffer);

                foreach (ZDO zdo in Buffer)
                {
                    if (zdo != null && LoadAllowlist.Contains(zdo.GetPrefab()))
                    {
                        destination.Add(zdo);
                    }
                }
            }
        }

        /// <summary>
        ///     Distant objects are the low-detail versions the game shows across a valley.
        ///     Our zones are being loaded in full, so letting this also add them would put
        ///     the same ZDO in both lists.
        /// </summary>
        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.FindDistantObjects))]
        private static class FindDistantObjects
        {
            private static bool Prefix(Vector2i sector) => !KeepAliveZones.Contains(sector);
        }
    }
}
