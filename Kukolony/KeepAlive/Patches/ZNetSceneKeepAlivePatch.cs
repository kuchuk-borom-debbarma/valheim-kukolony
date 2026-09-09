using HarmonyLib;
using UnityEngine;

namespace Kukolony.KeepAlive.Patches
{
    /// <summary>
    ///     Makes colony zones count as active for ownership arbitration.
    ///
    ///     ZDOMan.ReleaseNearbyZDOS uses InActiveArea when deciding who owns what, and AI
    ///     only runs on the owner - without this a kept-alive colony would keep losing
    ///     ownership of its villagers and quietly stop.
    ///
    ///     Note what is deliberately NOT patched here: OutsideActiveArea. It is not used
    ///     for loading at all. Its callers are SpawnArea (already gated on a player being
    ///     in range), a falling-support check, and WearNTear.UpdateWear - which uses it as
    ///     the shortcut that stops structures decaying when nobody is around. Patching it
    ///     made colony buildings weather and collapse during long absences.
    /// </summary>
    internal static class ZNetSceneKeepAlivePatch
    {
        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.InActiveArea),
            new[] { typeof(Vector2i), typeof(Vector2i) })]
        private static class InActiveArea
        {
            private static void Postfix(Vector2i zone, ref bool __result)
            {
                if (!__result && KeepAliveZones.Contains(zone))
                {
                    __result = true;
                }
            }
        }

        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.InActiveArea),
            new[] { typeof(Vector2i), typeof(Vector2i), typeof(int) })]
        private static class InActiveAreaWithArea
        {
            private static void Postfix(Vector2i zone, ref bool __result)
            {
                if (!__result && KeepAliveZones.Contains(zone))
                {
                    __result = true;
                }
            }
        }
    }
}
