using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Kukolony.KeepAlive.Patches
{
    /// <summary>
    ///     Brings colony objects into the world, and keeps them there.
    ///
    ///     ZNetScene.RemoveObjects destroys every instance that is *not* in the lists it
    ///     is handed, so appending to those lists is simultaneously what creates our
    ///     objects and what stops them being destroyed.
    ///
    ///     The append is scoped to ZNetScene.CreateDestroyObjects, because
    ///     ZDOMan.FindSectorObjects has three other callers and appending for them causes
    ///     real damage:
    ///
    ///       * ZNetScene.IsAreaReady - returns false if any ZDO in the list has no
    ///         instance. Game.FindSpawnPoint and player teleport gate on it, so appending
    ///         every colony ZDO in the world turned "is this area ready" into "is every
    ///         colony fully instantiated" and could hang world join or a portal.
    ///       * ZDOMan.ReleaseNearbyZDOS - ownership arbitration.
    ///       * ZDOMan.CreateSyncList - what gets pushed to each peer.
    /// </summary>
    internal static partial class ZDOManKeepAlivePatch
    {
        /// <summary>Reused so a 30Hz patch does not allocate.</summary>
        private static readonly List<ZDO> Buffer = new List<ZDO>();

        /// <summary>
        ///     Zones we held but did not append, because the player's own area was believed to
        ///     cover them.
        /// </summary>
        /// <remarks>
        ///     Recorded so it can be asked about. A villager destroyed while its zone was both
        ///     held and skipped means the belief is wrong and there is a ring of ground that
        ///     neither vanilla nor this covers - which is not something that can be worked out
        ///     by reading, because the reference decompile is a different build from the one
        ///     installed.
        /// </remarks>
        private static readonly HashSet<Vector2s> Appended = new HashSet<Vector2s>();

        /// <summary>
        ///     Our own visited-sector set, deliberately not the game's.
        /// </summary>
        /// <remarks>
        ///     <c>ZDOMan.FindObjects</c> requires a set of sectors already visited and skips any
        ///     that are in it. Handing it <c>m_visitedSectorIndices</c> - the game's own, which
        ///     vanilla has just finished filling in during the call we are a postfix to - means
        ///     every sector vanilla looked at is one we cannot enumerate. For a zone vanilla
        ///     visited but chose to put nothing in the near list, our append therefore finds
        ///     nothing to add and says nothing about it, and the objects there are destroyed.
        ///
        ///     Cleared per pass, so we still do not append the same zone twice ourselves.
        /// </remarks>
        private static readonly HashSet<ZoneSystem.SectorIndex> Visited =
            new HashSet<ZoneSystem.SectorIndex>();

        /// <summary>Whether anything was actually appended from this zone in the last pass.</summary>
        internal static bool AppendedFrom(Vector2s zone) => Appended.Contains(zone);

        /// <summary>
        ///     True only while ZNetScene.CreateDestroyObjects is on the stack. Harmony
        ///     gives a postfix no caller context, so the caller marks itself.
        /// </summary>
        private static bool _inCreateDestroy;

        /// <summary>
        ///     Marks the one call path where appending our zones is correct.
        /// </summary>
        [HarmonyPatch(typeof(ZNetScene), "CreateDestroyObjects")]
        private static class CreateDestroyObjectsScope
        {
            private static void Prefix()
            {
                _inCreateDestroy = true;
                Appended.Clear();
                Visited.Clear();
            }

            // Finalizer rather than Postfix: it runs even if the original throws, so an
            // exception cannot leave the flag stuck on and quietly widen every other
            // FindSectorObjects caller.
            private static void Finalizer() => _inCreateDestroy = false;
        }

        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.FindSectorObjects))]
        private static class FindSectorObjects
        {
            private static void Postfix(ZDOMan __instance, Vector2s sector, SimulationDistance simulationDistance,
                List<ZDO> sectorObjects)
            {
                if (!_inCreateDestroy || KeepAliveZones.IsEmpty)
                {
                    return;
                }

                foreach (Vector2s zone in KeepAliveZones.All)
                {
                    // Every held zone is appended, including ones the player's own area looks
                    // like it already covers.
                    //
                    // This used to skip those, on the reasoning that vanilla loads them in full
                    // and appending again would duplicate every ZDO. The reasoning was wrong and
                    // the symptom was specific: a villager walking away from the settlement was
                    // destroyed at about a hundred metres, every time, while reporting that its
                    // zone was held. Instrumenting the decision said zoneHeld=True skipped=True
                    // at the moment it died - so there is a ring where this declined to append
                    // and vanilla did not in fact cover it.
                    //
                    // The exact boundary is not something to work out by reading, because the
                    // reference decompile is a different build from the one installed - its
                    // FindSectorObjects takes (Vector2i, int area) where the live one takes
                    // (Vector2s, SimulationDistance). Appending unconditionally is correct
                    // whatever the boundary is: a duplicate costs a wasted lookup in a list that
                    // is already being walked, and being wrong the other way costs a villager.

                    Append(__instance, zone, sectorObjects);
                }
            }

            /// <summary>
            ///     Appends only what the colony needs. A zone loaded purely because a
            ///     villager is standing in it does not need its trees, rocks or wildlife
            ///     instantiated - that is the bulk of the objects and none of the value.
            /// </summary>
            private static void Append(ZDOMan zdoMan, Vector2s zone, List<ZDO> destination)
            {
                Buffer.Clear();
                zdoMan.FindObjects(zone, Buffer, Visited);
                if (Buffer.Count == 0) return;

                bool filter = ModConfig.KeepAliveFilterObjects.Value && LoadAllowlist.IsReady;
                foreach (ZDO zdo in Buffer)
                {
                    if (zdo == null) continue;
                    if (filter && !LoadAllowlist.Contains(zdo.GetPrefab())) continue;

                    destination.Add(zdo);
                    Appended.Add(zone);
                }
            }
        }

        // FindDistantObjects is deliberately not patched. Chunk-loader mods suppress it
        // for forced zones because they append the whole zone to the near list, so the
        // distant pass would add the same ZDOs twice. We filter, and distant scenery is
        // not on the allowlist, so there is nothing to duplicate - suppressing it only
        // deleted the big-tree LOD around every colony, leaving a hole in the landscape
        // when viewed from a few hundred metres. Duplicates would be harmless anyway:
        // both create paths skip ZDOs already marked Created.
    }
}
