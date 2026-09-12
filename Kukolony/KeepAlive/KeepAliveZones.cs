using System.Collections.Generic;
using Kukolony.Core;
using UnityEngine;

namespace Kukolony.KeepAlive
{
    /// <summary>
    ///     The zones held open because a villager is in or near them.
    ///
    ///     The loaded region follows villagers and registered structures, so it costs what
    ///     the colony actually occupies rather than a speculative world-wide area.
    /// </summary>
    internal static class KeepAliveZones
    {
        private static readonly HashSet<Vector2s> Zones = new HashSet<Vector2s>();

        /// <summary>Latched so the cap is reported on change rather than every second.</summary>
        private static bool _reportedCapped;

        internal static int Count => Zones.Count;

        internal static bool IsEmpty => Zones.Count == 0;

        internal static bool Contains(Vector2s zone) => Zones.Count != 0 && Zones.Contains(zone);

        internal static IEnumerable<Vector2s> All => Zones;

        /// <summary>
        ///     Stops holding anything open. Must be called on every path that skips
        ///     Rebuild, or the last computed set stays live - which meant disabling the
        ///     feature did not restore vanilla behaviour, and one world's zones leaked
        ///     into the next.
        /// </summary>
        internal static void Clear() => Zones.Clear();

        /// <summary>
        ///     How many anchors the cap refused or truncated on the last rebuild, for checks.
        ///     An anchor whose zones were all already held by something else costs nothing
        ///     and is never counted - only an anchor that needed ground it could not get.
        /// </summary>
        internal static int DroppedAnchors => _dropped;

        private static int _dropped;

        /// <summary>
        ///     Rebuilds from every villager position we know about - loaded ones from the
        ///     registry, unloaded ones from their ZDOs.
        ///
        ///     Each villager contributes its own zone plus a halo of neighbours, because a
        ///     villager cannot path into unloaded ground: the navmesh is built from
        ///     colliders that are actually present, so it needs somewhere to walk into.
        /// </summary>
        /// <summary>
        ///     Rebuilds the kept set from point anchors and circle anchors, in that order.
        /// </summary>
        /// <remarks>
        ///     The order is a priority, because the cap consumes anchors until it binds and
        ///     silently drops the rest: villagers first (a dropped villager stops existing),
        ///     then the circles - hearths and flags, whose ground is the Kolony itself - then
        ///     bare structure positions, which are almost always inside a circle already and
        ///     so cost nothing when they are.
        /// </remarks>
        internal static void Rebuild(IEnumerable<Vector3> villagerPositions,
            IEnumerable<Vector4> areas, IEnumerable<Vector3> structurePositions)
        {
            Zones.Clear();

            if (!ModConfig.KeepAliveEnabled.Value)
            {
                return;
            }

            int rings = ModConfig.KeepAliveHaloRings.Value;
            int cap = ModConfig.KeepAliveMaxZones.Value;
            bool capped = false;
            int dropped = 0;

            foreach (Vector3 position in villagerPositions)
            {
                // All-or-nothing per villager. Applying the cap mid-halo could leave a
                // villager holding a couple of neighbour zones but not the one it is
                // standing in - paying the loading cost while not being kept alive at all.
                if (!TryHoldHalo(position, rings, cap))
                {
                    capped = true;
                    dropped++;
                }
            }

            // Circles: every zone the circle touches, plus one ring of neighbours so the
            // edge of an outpost is walkable ground rather than a cliff into nothing.
            //
            // When they all fit they are simply added. When the cap binds they are grown
            // together, each nearest its anchor first, one zone per circle per turn: every
            // hearth and flag keeps its centre and what drops is every circle's far edge.
            // Filling one circle to the cap before starting the next handed whole colonies
            // to enumeration order - the first big outpost packed the set solid and the
            // second Kolony's hearth held nothing open at all, silently, while reach went
            // on reporting its ground Ready.
            List<List<Vector2s>> circles = new List<List<Vector2s>>();
            List<Vector3> anchors = new List<Vector3>();
            int wanted = 0;
            foreach (Vector4 area in areas)
            {
                Vector3 at = new Vector3(area.x, area.y, area.z);
                float reach = area.w + 64f;

                Vector2s low = ZoneSystem.GetZone(at - new Vector3(reach, 0f, reach));
                Vector2s high = ZoneSystem.GetZone(at + new Vector3(reach, 0f, reach));

                List<Vector2s> circle = new List<Vector2s>();
                for (short y = low.y; y <= high.y; y++)
                {
                    for (short x = low.x; x <= high.x; x++)
                    {
                        // The corners of the bounding box can lie well outside the circle;
                        // clamping the centre onto the zone's square is the cheap exact test.
                        Vector3 zoneCentre = new Vector3(x * 64f, at.y, y * 64f);
                        float dx = Mathf.Max(Mathf.Abs(at.x - zoneCentre.x) - 32f, 0f);
                        float dz = Mathf.Max(Mathf.Abs(at.z - zoneCentre.z) - 32f, 0f);
                        if (dx * dx + dz * dz > reach * reach) continue;

                        Vector2s zone = new Vector2s(x, y);
                        if (!Zones.Contains(zone)) wanted++;
                        circle.Add(zone);
                    }
                }

                circles.Add(circle);
                anchors.Add(at);
            }

            if (Zones.Count + wanted <= cap)
            {
                // Everything fits, so there is no ordering to argue about and nothing to
                // sort. `wanted` may double-count a zone two circles share, which can only
                // send fitting work down the fair path below - never admit overflowing work
                // up here.
                foreach (List<Vector2s> circle in circles)
                {
                    foreach (Vector2s zone in circle) Zones.Add(zone);
                }
            }
            else
            {
                for (int i = 0; i < circles.Count; i++)
                {
                    Vector3 at = anchors[i];
                    circles[i].Sort((a, b) =>
                        SquaredZoneDistance(a, at).CompareTo(SquaredZoneDistance(b, at)));
                }

                for (int turn = 0; Zones.Count < cap; turn++)
                {
                    bool any = false;
                    foreach (List<Vector2s> circle in circles)
                    {
                        if (turn >= circle.Count) continue;

                        any = true;
                        Vector2s zone = circle[turn];
                        if (Zones.Contains(zone) || Zones.Count >= cap) continue;

                        Zones.Add(zone);
                    }

                    if (!any) break;
                }

                // What each circle could not get, counted honestly: a circle is truncated
                // only if it needed ground it does not hold.
                foreach (List<Vector2s> circle in circles)
                {
                    foreach (Vector2s zone in circle)
                    {
                        if (Zones.Contains(zone)) continue;

                        capped = true;
                        dropped++;
                        break;
                    }
                }
            }

            foreach (Vector3 position in structurePositions)
            {
                if (!TryHoldHalo(position, rings, cap))
                {
                    capped = true;
                    dropped++;
                }
            }

            _dropped = dropped;

            // Never truncate silently - a colony that stops working for an invisible
            // reason is the worst failure here. But Rebuild runs every second, so warning
            // unconditionally buried the rest of the log; report only on the transition.
            if (capped != _reportedCapped)
            {
                _reportedCapped = capped;
                if (capped)
                {
                    Log.Warning($"[KeepAlive] zone cap of {cap} reached - {_dropped} anchor(s) " +
                                "dropped or truncated. Raise KeepAliveMaxZones if this is a real outpost.");
                }
                else
                {
                    Log.Info("[KeepAlive] back under the zone cap.");
                }
            }
        }

        /// <summary>
        ///     Holds a point anchor's halo open, whole or not at all. False means the cap
        ///     refused it.
        /// </summary>
        /// <remarks>
        ///     The cost is counted against what is already held, not against the halo's
        ///     nominal size: a structure standing inside an already-kept circle needs no new
        ///     zones and must not be reported dropped for standing at a cap it never
        ///     consumed - which is exactly what a truncated outpost circle made every chest
        ///     inside it look like.
        /// </remarks>
        private static bool TryHoldHalo(Vector3 position, int rings, int cap)
        {
            Vector2s centre = ZoneSystem.GetZone(position);

            int missing = 0;
            for (int y = -rings; y <= rings; y++)
            {
                for (int x = -rings; x <= rings; x++)
                {
                    if (!Zones.Contains(new Vector2s((short)(centre.x + x), (short)(centre.y + y))))
                    {
                        missing++;
                    }
                }
            }

            if (missing == 0) return true;
            if (Zones.Count + missing > cap) return false;

            for (int y = -rings; y <= rings; y++)
            {
                for (int x = -rings; x <= rings; x++)
                {
                    Zones.Add(new Vector2s((short)(centre.x + x), (short)(centre.y + y)));
                }
            }

            return true;
        }

        /// <summary>How far a zone's centre sits from an anchor, for nearest-first filling.</summary>
        private static float SquaredZoneDistance(Vector2s zone, Vector3 anchor)
        {
            float dx = anchor.x - zone.x * 64f;
            float dz = anchor.z - zone.y * 64f;
            return dx * dx + dz * dz;
        }
    }
}
