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
    ///         What is cached per Kolony against the structures revision is the expensive
    ///         part: decoding the structure list down to the flags' ids, because reach is
    ///         asked per record inside index rebuilds and decoding per question would make
    ///         the rebuild quadratic. Positions and radii are read fresh on every ask — a
    ///         radius change writes only the flag's own ZDO and bumps no revision, and a
    ///         circle frozen at snapshot time kept answering with the reach the flag used to
    ///         have.
    ///     </para>
    /// </remarks>
    internal static class KolonyReach
    {
        private sealed class Snapshot
        {
            internal int Revision;
            internal readonly List<ZDOID> Flags = new List<ZDOID>();
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
            if (!Snapshots.TryGetValue(key, out Snapshot cached) || cached.Revision != revision)
            {
                cached = new Snapshot { Revision = revision };
                foreach (StructureRecord record in colony.State.GetStructures())
                {
                    if ((record.Capabilities & StructureCapability.WorkArea) == 0) continue;
                    cached.Flags.Add(record.Id);
                }

                Snapshots[key] = cached;
            }

            // Resolved on every ask, never frozen into the snapshot: a flag whose ZDO was
            // momentarily unresolved is retried rather than staying a hole in reach until
            // some unrelated structure edit, and an edited radius answers immediately.
            cached.Areas.Clear();
            foreach (ZDOID id in cached.Flags)
            {
                if (TryCircle(id, out Vector4 circle)) cached.Areas.Add(circle);
            }

            return cached.Areas;
        }

        /// <summary>
        ///     Appends every claimed flag's circle from a Kolony's records, loaded or not.
        ///     The keep-alive walks hearth ZDOs directly, so it cannot go through
        ///     <see cref="FlagAreas" /> — but it must render the same circles, or ground
        ///     that registers would be ground that unloads.
        /// </summary>
        internal static void CollectFlagAreas(ColonyState state, List<Vector4> into)
        {
            foreach (StructureRecord record in state.GetStructures())
            {
                if ((record.Capabilities & StructureCapability.WorkArea) == 0) continue;
                if (TryCircle(record.Id, out Vector4 circle)) into.Add(circle);
            }
        }

        /// <summary>The circle a flag's ZDO claims, when the ZDO resolves.</summary>
        private static bool TryCircle(ZDOID id, out Vector4 circle)
        {
            circle = default;
            ZDO zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(id) : null;
            if (zdo == null || !zdo.IsValid()) return false;

            Vector3 at = zdo.GetPosition();
            circle = new Vector4(at.x, at.y, at.z, WorkFlag.RadiusOf(zdo));
            return true;
        }
    }
}
