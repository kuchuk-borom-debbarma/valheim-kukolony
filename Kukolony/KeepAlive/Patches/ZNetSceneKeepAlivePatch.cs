using HarmonyLib;
using UnityEngine;

namespace Kukolony.KeepAlive.Patches
{
    /// <summary>
    ///     Makes colony zones count as active for ownership arbitration - but only for
    ///     our own session.
    ///
    ///     ZDOMan.ReleaseNearbyZDOS decides who owns what, and AI runs only on the owner,
    ///     so our zones must look active to us or a kept-alive colony loses its villagers
    ///     and stops. The catch is that ReleaseNearbyZDOS runs once per peer: widening the
    ///     answer for everyone made an absent peer look like it still held the zone, so
    ///     its ownership was never released and the colony froze permanently, with no
    ///     other peer able to claim it. Vanilla would at least have reset the owner to 0.
    ///
    ///     OutsideActiveArea is deliberately not patched: it is not a loading check. Its
    ///     callers are SpawnArea (already player-gated), a falling-support check, and
    ///     WearNTear.UpdateWear - which uses it as the shortcut that stops structures
    ///     decaying when nobody is around.
    /// </summary>
    internal static class ZNetSceneKeepAlivePatch
    {
        /// <summary>Whose active area is currently being evaluated, when inside ReleaseNearbyZDOS.</summary>
        private static bool _inRelease;
        private static long _releaseUid;

        private static bool WideningAllowed()
        {
            // Outside ownership arbitration there is no peer to confuse - widening is
            // about our own view of what is loaded.
            if (!_inRelease)
            {
                return true;
            }

            return _releaseUid == ZDOMan.GetSessionID();
        }

        [HarmonyPatch(typeof(ZDOMan), "ReleaseNearbyZDOS")]
        private static class ReleaseNearbyZdosScope
        {
            private static void Prefix(long uid)
            {
                _inRelease = true;
                _releaseUid = uid;
            }

            // Finalizer, so a throw cannot leave the scope flag stuck on.
            private static void Finalizer() => _inRelease = false;
        }

        /// <summary>
        ///     Answers "does the ZDO's current owner still have this zone active?".
        ///     Only true for us - another peer's kept zones are not ours to assert.
        /// </summary>
        [HarmonyPatch(typeof(ZDOMan), "IsInPeerActiveArea")]
        private static class IsInPeerActiveArea
        {
            private static void Postfix(Vector2i sector, long uid, ref bool __result)
            {
                if (!__result && uid == ZDOMan.GetSessionID() && KeepAliveZones.Contains(sector))
                {
                    __result = true;
                }
            }
        }

        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.InActiveArea),
            new[] { typeof(Vector2i), typeof(Vector2i) })]
        private static class InActiveArea
        {
            private static void Postfix(Vector2i zone, ref bool __result)
            {
                // Inside arbitration this overload is only reached via IsInPeerActiveArea,
                // which applies its own per-uid rule above.
                if (!__result && !_inRelease && KeepAliveZones.Contains(zone))
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
                if (!__result && WideningAllowed() && KeepAliveZones.Contains(zone))
                {
                    __result = true;
                }
            }
        }
    }
}
