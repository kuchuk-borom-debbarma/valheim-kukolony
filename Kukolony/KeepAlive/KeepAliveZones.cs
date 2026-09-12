using System;
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

        /// <summary>
        ///     Scratch for the circle phase, reused across rebuilds the way
        ///     <see cref="Zones" /> itself is: Rebuild runs every second for the whole
        ///     session, and allocating a list per hearth and flag per second is steady GC
        ///     churn in a loop whose point is running quietly.
        /// </summary>
        private static readonly List<List<Vector2s>> CircleBuffers = new List<List<Vector2s>>();

        private static readonly List<Vector3> CircleAnchors = new List<Vector3>();

        /// <summary>
        ///     The anchor the nearest-first sort is measuring against, and the one comparison
        ///     that reads it.
        /// </summary>
        /// <remarks>
        ///     A lambda capturing the loop's anchor allocates a closure and a delegate per
        ///     circle per rebuild, and Rebuild runs every second for the whole session - the
        ///     very allocation this buffer reuse exists to remove.
        /// </remarks>
        private static Vector3 _sortAnchor;

        private static readonly Comparison<Vector2s> NearestFirst = (a, b) =>
            SquaredZoneDistance(a, _sortAnchor).CompareTo(SquaredZoneDistance(b, _sortAnchor));

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
        internal static void Clear()
        {
            Zones.Clear();

            // The scratch too, so nothing is carried across a world boundary. Harmless as
            // it stands - zone coordinates are values, not ZDO references - but "cleared"
            // should mean cleared, and the next reader of these buffers need not wonder.
            foreach (List<Vector2s> buffer in CircleBuffers) buffer.Clear();
            CircleAnchors.Clear();

            // And the reporting, which is the part that actually leaked: latched at a
            // dead world's state, entering a capped world from a capped world logged no
            // warning at all - the transition never happened - so the "never truncate
            // silently" rule was broken by the very latch that exists to keep it. The
            // other way round it announced being back under a cap belonging to a world
            // that no longer exists. DroppedAnchors likewise answered for the dead world
            // until the next rebuild, and the self-test reads it.
            _reportedCapped = false;
            _dropped = 0;
        }

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
            // One anchor per circle, so the anchor list's length *is* the circle count -
            // a separate counter alongside it would be one `continue` away from letting a
            // demolished outpost's stale buffer be read as a live circle.
            CircleAnchors.Clear();
            int wanted = 0;
            foreach (Vector4 area in areas)
            {
                Vector3 at = new Vector3(area.x, area.y, area.z);
                float reach = area.w + 64f;

                Vector2s low = ZoneSystem.GetZone(at - new Vector3(reach, 0f, reach));
                Vector2s high = ZoneSystem.GetZone(at + new Vector3(reach, 0f, reach));

                CircleAnchors.Add(at);
                int index = CircleAnchors.Count - 1;
                if (index == CircleBuffers.Count) CircleBuffers.Add(new List<Vector2s>());
                List<Vector2s> circle = CircleBuffers[index];
                circle.Clear();
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
            }

            int circleCount = CircleAnchors.Count;

            if (Zones.Count + wanted <= cap)
            {
                // Everything fits, so there is no ordering to argue about and nothing to
                // sort. `wanted` may double-count a zone two circles share, which can only
                // send fitting work down the fair path below - never admit overflowing work
                // up here.
                for (int i = 0; i < circleCount; i++)
                {
                    foreach (Vector2s zone in CircleBuffers[i]) Zones.Add(zone);
                }
            }
            else
            {
                for (int i = 0; i < circleCount; i++)
                {
                    _sortAnchor = CircleAnchors[i];
                    CircleBuffers[i].Sort(NearestFirst);
                }

                for (int turn = 0; Zones.Count < cap; turn++)
                {
                    bool any = false;
                    for (int i = 0; i < circleCount; i++)
                    {
                        List<Vector2s> circle = CircleBuffers[i];
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
                for (int i = 0; i < circleCount; i++)
                {
                    foreach (Vector2s zone in CircleBuffers[i])
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

            // If even the halo's nominal size fits, it fits whatever overlaps - skip the
            // counting pass and just add. The count below only matters near the cap.
            int haloSize = (rings * 2 + 1) * (rings * 2 + 1);
            if (Zones.Count + haloSize <= cap)
            {
                Halo(centre, rings, add: true);
                return true;
            }

            // One geometry, walked twice: counting what is missing and adding it must
            // agree exactly about which zones the halo is, and the method's one documented
            // past bug was precisely a count that disagreed with its add.
            int missing = Halo(centre, rings, add: false);
            if (missing == 0) return true;
            if (Zones.Count + missing > cap) return false;

            Halo(centre, rings, add: true);
            return true;
        }

        /// <summary>
        ///     Walks a point anchor's halo, either adding its zones or counting the ones not
        ///     already held.
        /// </summary>
        /// <returns>How many of the halo's zones were missing before the walk.</returns>
        private static int Halo(Vector2s centre, int rings, bool add)
        {
            int missing = 0;
            for (int y = -rings; y <= rings; y++)
            {
                for (int x = -rings; x <= rings; x++)
                {
                    Vector2s zone = new Vector2s((short)(centre.x + x), (short)(centre.y + y));
                    if (add) { if (Zones.Add(zone)) missing++; }
                    else if (!Zones.Contains(zone)) missing++;
                }
            }

            return missing;
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
