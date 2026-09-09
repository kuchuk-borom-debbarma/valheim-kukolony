using HarmonyLib;
using UnityEngine;

namespace Kukolony.KeepAlive.Patches
{
    /// <summary>
    ///     Keeps the terrain of colony zones loaded.
    ///
    ///     Every patch here only ever widens what the game considers active, so with an
    ///     empty zone set the behaviour is exactly stock.
    /// </summary>
    internal static class ZoneSystemKeepAlivePatch
    {
        /// <summary>
        ///     Pokes our zones alongside the player's. Poking loads the zone root - the
        ///     terrain, and with it the collider the navmesh is built from - and resets
        ///     the zone's unload timer.
        /// </summary>
        [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.CreateLocalZones))]
        private static class CreateLocalZones
        {
            private static void Postfix(ZoneSystem __instance, ref bool __result)
            {
                if (__result || KeepAliveZones.IsEmpty)
                {
                    return;
                }

                foreach (Vector2i zone in KeepAliveZones.All)
                {
                    if (!__instance.PokeLocalZone(zone))
                    {
                        continue;
                    }

                    // Matching vanilla: it reports "something was loaded this pass" and
                    // then stops, so zone loading stays spread across frames.
                    __result = true;
                    break;
                }
            }
        }

        /// <summary>
        ///     ZNetScene refuses to create objects until the active area is loaded. Our
        ///     zones count as part of it, so a colony's objects are not created against
        ///     terrain that has not arrived yet.
        /// </summary>
        [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.IsActiveAreaLoaded))]
        private static class IsActiveAreaLoaded
        {
            private static void Postfix(ZoneSystem __instance, ref bool __result)
            {
                if (!__result || KeepAliveZones.IsEmpty)
                {
                    return;
                }

                foreach (Vector2i zone in KeepAliveZones.All)
                {
                    if (__instance.m_zones.ContainsKey(zone))
                    {
                        continue;
                    }

                    __result = false;
                    return;
                }
            }
        }
    }
}
