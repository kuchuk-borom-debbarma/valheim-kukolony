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
    ///     Valheim 1.0 replaced the zone-pair overloads with a point-and-centre pair, and
    ///     that centre is the peer being arbitrated for. So "is this us?" is now answerable
    ///     from the arguments, and the flag that used to track it - set in a prefix on
    ///     ReleaseNearbyZDOS, with all the ways a flag can get stuck - is gone.
    ///
    ///     OutsideActiveArea is deliberately not patched: it is not a loading check. Its
    ///     callers are SpawnArea (already player-gated), a falling-support check, and
    ///     WearNTear.UpdateWear - which uses it as the shortcut that stops structures
    ///     decaying when nobody is around.
    /// </summary>
    internal static class ZNetSceneKeepAlivePatch
    {
        /// <summary>
        ///     Whether the area being asked about is our own.
        ///
        ///     Everything hangs on this. Our kept zones are ours alone: claiming a remote
        ///     peer still has a zone active stops its ownership ever being released, and
        ///     the colony freezes with nobody able to take it. Answering only for
        ///     ourselves is both the safe direction and the true one.
        /// </summary>
        private static bool IsOurArea(Vector2s centreZone)
        {
            if (ZNet.instance == null)
            {
                return false;
            }

            return ZoneSystem.GetZone(ZNet.instance.GetReferencePosition()) == centreZone;
        }

        /// <summary>
        ///     The one widening point. Both public overloads funnel through this one, and
        ///     IsInPeerActiveArea calls it with the owner's reference position - so a
        ///     remote owner is filtered by IsOurArea without needing its own patch.
        /// </summary>
        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.InActiveArea),
            new[] { typeof(Vector3), typeof(Vector2s) })]
        private static class InActiveArea
        {
            private static void Postfix(Vector3 point, Vector2s centerZone, ref bool __result)
            {
                if (__result || !IsOurArea(centerZone))
                {
                    return;
                }

                if (KeepAliveZones.Contains(ZoneSystem.GetZone(point)))
                {
                    __result = true;
                }
            }
        }
    }
}
