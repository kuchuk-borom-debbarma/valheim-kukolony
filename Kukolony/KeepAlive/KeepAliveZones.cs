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

        /// <summary>How many anchors the cap refused on the last rebuild, for checks.</summary>
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

            int haloSize = (rings * 2 + 1) * (rings * 2 + 1);

            foreach (Vector3 position in villagerPositions)
            {
                // All-or-nothing per villager. Applying the cap mid-halo could leave a
                // villager holding a couple of neighbour zones but not the one it is
                // standing in - paying the loading cost while not being kept alive at all.
                if (Zones.Count + haloSize > cap)
                {
                    capped = true;
                    dropped++;
                    continue;
                }

                Vector2s centre = ZoneSystem.GetZone(position);

                for (int y = -rings; y <= rings; y++)
                {
                    for (int x = -rings; x <= rings; x++)
                    {
                        Zones.Add(new Vector2s((short)(centre.x + x), (short)(centre.y + y)));
                    }
                }
            }

            // Circles: every zone the circle touches, plus one ring of neighbours so the
            // edge of an outpost is walkable ground rather than a cliff into nothing.
            //
            // Filled nearest the anchor first, and truncated rather than refused. The
            // all-or-nothing rule that is right for a villager's halo is wrong here: the
            // config allows radii whose footprint alone exceeds the cap, and a circle
            // refused wholesale meant the largest outposts - the ones a flag exists for -
            // were exactly the ones holding nothing open, while reach went on reporting
            // their ground Ready. A truncated circle keeps the flag and the ground closest
            // to it, which degrades instead of vanishing.
            List<Vector2s> circle = new List<Vector2s>();
            foreach (Vector4 area in areas)
            {
                Vector3 at = new Vector3(area.x, area.y, area.z);
                float reach = area.w + 64f;

                Vector2s low = ZoneSystem.GetZone(at - new Vector3(reach, 0f, reach));
                Vector2s high = ZoneSystem.GetZone(at + new Vector3(reach, 0f, reach));

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

                        circle.Add(new Vector2s(x, y));
                    }
                }

                circle.Sort((a, b) =>
                    SquaredZoneDistance(a, at).CompareTo(SquaredZoneDistance(b, at)));

                foreach (Vector2s zone in circle)
                {
                    if (Zones.Contains(zone)) continue;
                    if (Zones.Count >= cap)
                    {
                        // Sorted nearest-first, so everything not yet added is the far edge.
                        capped = true;
                        dropped++;
                        break;
                    }

                    Zones.Add(zone);
                }
            }

            foreach (Vector3 position in structurePositions)
            {
                if (Zones.Count + haloSize > cap)
                {
                    capped = true;
                    dropped++;
                    continue;
                }

                Vector2s centre = ZoneSystem.GetZone(position);

                for (int y = -rings; y <= rings; y++)
                {
                    for (int x = -rings; x <= rings; x++)
                    {
                        Zones.Add(new Vector2s((short)(centre.x + x), (short)(centre.y + y)));
                    }
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

        /// <summary>How far a zone's centre sits from an anchor, for nearest-first filling.</summary>
        private static float SquaredZoneDistance(Vector2s zone, Vector3 anchor)
        {
            float dx = anchor.x - zone.x * 64f;
            float dz = anchor.z - zone.y * 64f;
            return dx * dx + dz * dz;
        }
    }
}
