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
        ///     Forces the next ask to rescan.
        /// </summary>
        /// <remarks>
        ///     For checks that plant trees and then ask about them. The refresh interval is
        ///     tuned for a running game, where five seconds of staleness is invisible; in a
        ///     check it is the difference between measuring the rule and measuring the cache.
        /// </remarks>
        internal static void ResetForTest() => Caches.Clear();

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
            if (ZNetScene.instance == null) return cache.Found;

            // Built here, at the point of use, rather than by whichever system happens to
            // start first. The keep-alive driver looked like the natural home until you
            // notice it stands down entirely when its feature is switched off or the peer is
            // a client - which would have left the classifier empty and chopping quietly
            // finding nothing, with the config toggle for an unrelated feature as the cause.
            if (!Choppable.IsReady) Choppable.Rebuild();
            if (!Choppable.IsReady) return cache.Found;

            // The widest net, bounded once here rather than once per villager.
            //
            // Bounded from every place the Kolony has, not only its hearth. Measuring from
            // the hearth alone made the ceiling a ceiling on the whole feature: a flag
            // planted at a wood further out than the config radius could be claimed, pointed
            // at by a job, and shown on the map, and every villager sent to it reported
            // "nothing to chop" forever - the scan had already discarded the trees before the
            // work area was consulted. An outpost is a place the Kolony works, so it anchors
            // the search the same way the hearth does.
            float bound = ModConfig.ResourceScanRadius.Value;

            // The hearth and every claimed flag both anchor the search, each out to the
            // config distance - but never past the ground the keep-alive actually holds
            // open for it.
            //
            // That second bound is the one that took three attempts. The config alone
            // inverted nothing, but it let the scan offer trees in zones nothing keeps
            // loaded: a flag with a small radius holds only its own circle plus a ring, so
            // work beyond that exists while a player happens to be out there and vanishes
            // when they walk home. A job that finds work only when watched is the exact
            // fault off-screen simulation is for. Capping at the flag's bare radius instead
            // was worse - it inverted the rule the job relies on, that the scan bounds and
            // the work area narrows. So: the config is the ceiling, and what is kept loaded
            // is the floor under it.
            Anchors.Clear();
            Vector3 hearth = colony.transform.position;
            Anchors.Add(new Vector4(hearth.x, hearth.y, hearth.z,
                Mathf.Min(bound, Kept(Colonies.Colony.ConfiguredRadius))));

            IReadOnlyList<Vector4> flags = Colonies.KolonyReach.FlagAreas(colony);
            for (int i = 0; i < flags.Count; i++)
            {
                // Copied out immediately, and by index: that list is a shared scratch buffer
                // rebuilt on the next ask by anyone.
                Vector4 flag = flags[i];
                Anchors.Add(new Vector4(flag.x, flag.y, flag.z, Mathf.Min(bound, Kept(flag.w))));
            }

            foreach (ZNetView view in ZNetScene.instance.m_instances.Values)
            {
                if (view == null || !view.IsValid()) continue;

                ZDO zdo = view.GetZDO();
                if (Choppable.Of(zdo.GetPrefab()) == ChopKind.None) continue;
                if (!WithinAnyAnchor(zdo.GetPosition())) continue;

                cache.Found.Add(zdo.m_uid);
            }

            return cache.Found;
        }

        /// <summary>
        ///     How far from a circle's centre the keep-alive actually holds zones open.
        /// </summary>
        /// <remarks>
        ///     The same arithmetic <c>KeepAliveZones</c> applies to a circle anchor: its own
        ///     radius plus one ring of neighbouring zones, so the edge of an outpost is
        ///     walkable ground rather than a cliff into nothing. Written here rather than
        ///     shared because the two are asking different questions of the same number - one
        ///     decides which zones to hold, this decides how far it is honest to look - and a
        ///     single helper would invite changing both by editing one.
        /// </remarks>
        private static float Kept(float radius) => radius + 64f;

        /// <summary>
        ///     Where this colony works and how far, reused across refreshes so the scan does
        ///     not allocate a list per colony per five seconds.
        /// </summary>
        private static readonly List<Vector4> Anchors = new List<Vector4>();

        private static bool WithinAnyAnchor(Vector3 at)
        {
            for (int i = 0; i < Anchors.Count; i++)
            {
                Vector4 anchor = Anchors[i];
                if (Utils.DistanceXZ(at, new Vector3(anchor.x, anchor.y, anchor.z)) <= anchor.w)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
