using System.Collections.Generic;
using Kukolony.Colonies;
using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Resources
{
    /// <summary>
    ///     What a colony has to work on nearby, refreshed on a timer rather than looked up
    ///     again for every villager.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Trees have no registry of their own - the game keeps instance lists for items and
    ///         creatures, not for scenery - so finding one means looking through everything
    ///         loaded. That is affordable once every few seconds for a colony; it would not be
    ///         affordable once per villager per tick, which is why this exists at all.
    ///     </para>
    ///     <para>
    ///         <b>It only sees what is loaded.</b> Off-screen that depends on the keep-alive
    ///         allowlist including trees, which it does. Without that a villager away from any
    ///         player would find nothing to chop and quietly idle - and would work perfectly
    ///         every time anyone came to watch.
    ///     </para>
    /// </remarks>
    internal static class ColonyResources
    {
        /// <summary>
        ///     Long enough that the scan is cheap, short enough that a tree felled by hand
        ///     stops being offered before a villager has walked all the way to it.
        /// </summary>
        private const float RefreshSeconds = 5f;

        private sealed class Cache
        {
            internal float Refreshed;
            internal readonly List<ZDOID> Found = new List<ZDOID>();
        }

        private static readonly Dictionary<ZDOID, Cache> Caches = new Dictionary<ZDOID, Cache>();

        /// <summary>Dropped when a world unloads; a colony's identity does not survive it.</summary>
        internal static void Clear() => Caches.Clear();

        /// <summary>
        ///     Loaded resources of this kind within range of the colony, nearest first is not
        ///     promised - the caller picks, because only it knows what it can reach and what
        ///     somebody else has already claimed.
        /// </summary>
        internal static List<ZDOID> Near(Colony colony, ResourceKind kind, float radius)
        {
            List<ZDOID> matching = new List<ZDOID>();
            if (colony == null || kind == ResourceKind.None) return matching;

            foreach (ZDOID id in Snapshot(colony))
            {
                ZDO zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(id) : null;
                if (zdo == null || !zdo.IsValid()) continue;
                if (ResourceIndex.Of(zdo.GetPrefab()) != kind) continue;
                if (Utils.DistanceXZ(zdo.GetPosition(), colony.transform.position) > radius) continue;
                matching.Add(id);
            }
            return matching;
        }

        private static List<ZDOID> Snapshot(Colony colony)
        {
            ZDOID key = colony.Id;
            if (!Caches.TryGetValue(key, out Cache cache))
            {
                cache = new Cache();
                Caches[key] = cache;
            }
            if (Time.time - cache.Refreshed < RefreshSeconds && cache.Found.Count > 0) return cache.Found;

            cache.Refreshed = Time.time;
            cache.Found.Clear();
            if (ZNetScene.instance == null || !ResourceIndex.IsReady) return cache.Found;

            // The widest possible net, bounded once here rather than per villager. Callers
            // narrow it by kind and by their own radius, which are cheap by comparison.
            float bound = ModConfig.ResourceScanRadius.Value;
            foreach (ZNetView view in ZNetScene.instance.m_instances.Values)
            {
                if (view == null || !view.IsValid()) continue;
                ZDO zdo = view.GetZDO();
                if (ResourceIndex.Of(zdo.GetPrefab()) == ResourceKind.None) continue;
                if (Utils.DistanceXZ(zdo.GetPosition(), colony.transform.position) > bound) continue;
                cache.Found.Add(zdo.m_uid);
            }
            return cache.Found;
        }
    }
}
