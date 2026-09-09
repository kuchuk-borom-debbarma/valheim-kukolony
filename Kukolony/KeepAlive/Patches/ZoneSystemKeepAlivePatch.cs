using HarmonyLib;
using UnityEngine;

namespace Kukolony.KeepAlive.Patches
{
    /// <summary>
    ///     Keeps the terrain of colony zones loaded.
    ///
    ///     IsActiveAreaLoaded is deliberately NOT patched. Its only vanilla use is
    ///     ZNetScene.CreateObjectsSorted, which returns immediately when it is false - so
    ///     forcing it false while any colony zone was still loading stopped object
    ///     creation everywhere, including around the player. It is not needed either:
    ///     CreateObjectsSorted already checks IsZoneReadyForType per object, so a colony
    ///     object cannot be created before its own zone is ready.
    /// </summary>
    internal static class ZoneSystemKeepAlivePatch
    {
        [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.CreateLocalZones))]
        private static class CreateLocalZones
        {
            private static void Postfix(ZoneSystem __instance, ref bool __result)
            {
                if (KeepAliveZones.IsEmpty)
                {
                    return;
                }

                // Vanilla spawns at most one zone per pass to spread the cost, and
                // __result reports whether that already happened.
                bool spawnedThisPass = __result;

                foreach (Vector2i zone in KeepAliveZones.All)
                {
                    bool alreadyLoaded = __instance.m_zones.ContainsKey(zone);

                    // Poking a loaded zone only resets its unload timer, so it must happen
                    // every pass regardless. Skipping it - as an early return on __result
                    // used to - let kept zones age past m_zoneTTL during any sustained
                    // vanilla zone loading, and get destroyed along with the terrain
                    // collider the villager needs to path on.
                    if (alreadyLoaded)
                    {
                        __instance.PokeLocalZone(zone);
                        continue;
                    }

                    if (spawnedThisPass)
                    {
                        continue;
                    }

                    if (__instance.PokeLocalZone(zone))
                    {
                        spawnedThisPass = true;
                        __result = true;
                    }
                }
            }
        }
    }
}
