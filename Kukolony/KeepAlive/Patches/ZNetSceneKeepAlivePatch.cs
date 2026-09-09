using HarmonyLib;
using UnityEngine;

namespace Kukolony.KeepAlive.Patches
{
    /// <summary>
    ///     Makes colony zones count as "active" everywhere the game asks.
    ///
    ///     This is not only about keeping objects alive. ZDOMan.ReleaseNearbyZDOS uses
    ///     these same checks when deciding who owns what, so without them a kept-alive
    ///     colony would keep losing ownership of its villagers and stop ticking - AI runs
    ///     only on the owner. See docs/multiplayer.md.
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

        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.OutsideActiveArea),
            new[] { typeof(Vector3), typeof(Vector3) })]
        private static class OutsideActiveArea
        {
            private static void Postfix(Vector3 point, ref bool __result)
            {
                if (__result && KeepAliveZones.Contains(ZoneSystem.GetZone(point)))
                {
                    __result = false;
                }
            }
        }

        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.OutsideActiveArea),
            new[] { typeof(Vector3), typeof(Vector2i), typeof(int) })]
        private static class OutsideActiveAreaWithZone
        {
            private static void Postfix(Vector3 point, ref bool __result)
            {
                if (__result && KeepAliveZones.Contains(ZoneSystem.GetZone(point)))
                {
                    __result = false;
                }
            }
        }
    }
}
