using System.Collections.Generic;
using UnityEngine;

namespace Kukolony.Colonies
{
    /// <summary>
    ///     Whether a point is the Kolony's ground: within the hearth's radius, or within any
    ///     of its flags'.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This is the one predicate that turns a flag from a marker into an outpost.
    ///         Registration, the settlement index's four eligibility gates and the candidate
    ///         listing all ask "is this in reach", and they must all get the same answer — a
    ///         chest the register screen accepts but the hauling index cannot see would be a
    ///         chest that fills with nothing and says nothing.
    ///     </para>
    ///     <para>
    ///         Flag positions and radii are read off ZDOs, so an unloaded flag still extends
    ///         reach — its outpost keeps working with nobody there, which is the point of it.
    ///         Cached per Kolony against the structures revision, because reach is asked per
    ///         record inside index rebuilds and decoding the structure list per question would
    ///         make the rebuild quadratic.
    ///     </para>
    /// </remarks>
    internal static class KolonyReach
    {
        private sealed class Snapshot
        {
            internal int Revision;
            internal readonly List<Vector4> Areas = new List<Vector4>();
        }

        private static readonly Dictionary<ZDOID, Snapshot> Snapshots =
            new Dictionary<ZDOID, Snapshot>();

        /// <summary>Dropped when a world unloads; a Kolony's identity does not survive one.</summary>
        internal static void Clear() => Snapshots.Clear();

        internal static bool Covers(Colony colony, Vector3 point)
        {
            if (colony == null) return false;
            if (Utils.DistanceXZ(point, colony.transform.position) <= colony.EffectiveRadius)
            {
                return true;
            }

            foreach (Vector4 area in FlagAreas(colony))
            {
                if (Utils.DistanceXZ(point, new Vector3(area.x, area.y, area.z)) <= area.w)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        ///     The claimed flags' circles, as (x, y, z, radius), for anyone who needs the
        ///     shapes themselves — the keep-alive holds their zones open with this.
        /// </summary>
        internal static IReadOnlyList<Vector4> FlagAreas(Colony colony)
        {
            if (colony == null) return System.Array.Empty<Vector4>();

            ZDOID key = colony.Id;
            if (key.IsNone()) return System.Array.Empty<Vector4>();

            int revision = colony.State.StructuresRevision;
            if (Snapshots.TryGetValue(key, out Snapshot cached) && cached.Revision == revision)
            {
                return cached.Areas;
            }

            Snapshot fresh = new Snapshot { Revision = revision };
            foreach (StructureRecord record in colony.State.GetStructures())
            {
                if ((record.Capabilities & StructureCapability.WorkArea) == 0) continue;

                ZDO zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(record.Id) : null;
                if (zdo == null || !zdo.IsValid()) continue;

                Vector3 at = zdo.GetPosition();
                fresh.Areas.Add(new Vector4(at.x, at.y, at.z, WorkFlag.RadiusOf(zdo)));
            }

            Snapshots[key] = fresh;
            return fresh.Areas;
        }
    }
}
