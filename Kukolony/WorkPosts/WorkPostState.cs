using System.Collections.Generic;
using UnityEngine;

namespace Kukolony.WorkPosts
{
    /// <summary>
    ///     A work post's configuration, stored on its ZDO.
    ///
    ///     This is the customisation surface: what job to run, what item to work with,
    ///     where to put the results, how far to range. Everything a player can change
    ///     about a post lives here, so the job engine reads configuration from one place
    ///     regardless of whether a GUI, an interaction or a config file set it.
    /// </summary>
    internal readonly struct WorkPostState
    {
        private static readonly int JobKey = "kukolony.job".GetStableHashCode();
        private static readonly int ItemKey = "kukolony.job.item".GetStableHashCode();
        // A ZDOID occupies two ZDO slots (user id + object id), so its cached key is a
        // hash pair rather than a single hash.
        private static readonly KeyValuePair<int, int> TargetKey = ZDO.GetHashZDOID("kukolony.job.target");
        private static readonly int RadiusKey = "kukolony.job.radius".GetStableHashCode();

        private readonly ZDO _zdo;

        internal WorkPostState(ZDO zdo)
        {
            _zdo = zdo;
        }

        internal bool IsValid => _zdo != null;

        /// <summary>Job id, matching a definition in the job library. Empty means idle.</summary>
        internal string JobId => _zdo?.GetString(JobKey, string.Empty) ?? string.Empty;

        /// <summary>Prefab name of the item this post works with.</summary>
        internal string ItemFilter => _zdo?.GetString(ItemKey, string.Empty) ?? string.Empty;

        /// <summary>Bound destination container. None means "find the nearest match".</summary>
        internal ZDOID Destination => _zdo?.GetZDOID(TargetKey) ?? ZDOID.None;

        internal float Radius => _zdo?.GetFloat(RadiusKey, 0f) ?? 0f;

        internal bool HasJob => !string.IsNullOrEmpty(JobId);

        internal void SetJob(string jobId) => _zdo.Set(JobKey, jobId);

        internal void SetItemFilter(string prefabName) => _zdo.Set(ItemKey, prefabName);

        internal void SetDestination(ZDOID container) => _zdo.Set(TargetKey, container);

        internal void SetRadius(float radius) => _zdo.Set(RadiusKey, radius);
    }
}
