using System.Collections.Generic;
using Kukolony.Colonies;
using UnityEngine;

namespace Kukolony.Resources
{
    /// <summary>
    ///     What a colony has to chop nearby, refreshed on a timer rather than looked up again
    ///     for every villager.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Trees have no registry.</b> The game keeps instance lists for items and for
    ///         creatures, not for scenery, so finding one means looking through everything
    ///         loaded. That is affordable once every few seconds <em>for a colony</em>. It
    ///         would not be affordable once per villager per tick, which is the only reason
    ///         this class exists.
    ///     </para>
    ///     <para>
    ///         The mod this one replaces died partly of exactly that: an overlap sphere of five
    ///         hundred metres, per villager, per second, with no layer mask. Survivable with
    ///         one worker and not with a settlement.
    ///     </para>
    ///     <para>
    ///         <b>It only sees what is loaded.</b> Off screen that depends on the keep-alive
    ///         allowlist including trees. Without it a villager away from any player finds
    ///         nothing to chop and idles — and works perfectly every time somebody comes to
    ///         watch, which is the hardest kind of fault to notice.
    ///     </para>
    /// </remarks>
    internal static class ChoppingGround
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

        /// <summary>Dropped when a world unloads; a colony's identity does not survive one.</summary>
        internal static void Clear() => Caches.Clear();

        /// <summary>
        ///     Everything loaded and choppable that this colony can see, as ZDOIDs.
        /// </summary>
        /// <remarks>
        ///     Deliberately unsorted and unfiltered beyond the outer bound. The caller decides
        ///     what it wants, how far it will walk and what somebody else has already claimed,
        ///     because only it knows those things — and re-sorting a shared list per villager
        ///     would put the cost back where this class took it away from.
        /// </remarks>
        internal static List<ZDOID> Near(Colony colony)
        {
            if (colony == null) return new List<ZDOID>();

            ZDOID key = colony.Id;
            if (key.IsNone()) return new List<ZDOID>();

            if (!Caches.TryGetValue(key, out Cache cache))
            {
                cache = new Cache();
                Caches[key] = cache;
            }

            if (Time.time - cache.Refreshed < RefreshSeconds) return cache.Found;

            cache.Refreshed = Time.time;
            cache.Found.Clear();
            if (ZNetScene.instance == null || !Choppable.IsReady) return cache.Found;

            // The widest net, bounded once here rather than once per villager. The config is
            // the outer ceiling and a job's own work area narrows it further; neither can
            // reach past this.
            float bound = ModConfig.ResourceScanRadius.Value;
            Vector3 hearth = colony.transform.position;

            foreach (ZNetView view in ZNetScene.instance.m_instances.Values)
            {
                if (view == null || !view.IsValid()) continue;

                ZDO zdo = view.GetZDO();
                if (Choppable.Of(zdo.GetPrefab()) == ChopKind.None) continue;
                if (Utils.DistanceXZ(zdo.GetPosition(), hearth) > bound) continue;

                cache.Found.Add(zdo.m_uid);
            }

            return cache.Found;
        }
    }
}
