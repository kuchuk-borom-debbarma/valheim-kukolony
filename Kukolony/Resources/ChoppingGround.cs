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

        /// <summary>
        ///     How far from any of a Kolony's places chopping looks for work.
        /// </summary>
        /// <remarks>
        ///     The single answer, asked by everything that needs it rather than each deriving
        ///     its own from the config. Three rounds of review found the scan, the work area
        ///     and the screen's reach row disagreeing about this number in three different
        ///     shapes; they disagreed because each computed it. Now there is one.
        /// </remarks>
        internal static float SearchRadius =>
            ModConfig.ResourceScanRadius != null ? ModConfig.ResourceScanRadius.Value : 96f;

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
            float bound = SearchRadius;

            // The hearth and every claimed flag anchor the search, each out to the same
            // distance: this is the one number that says how far chopping may look, and
            // everything that needs to know - the work area, the job screen's reach row -
            // asks for it rather than deriving its own.
            //
            // Three rounds of review went on this knob, capping it at a flag's radius and
            // then at what the keep-alive holds, and each cap broke the rule the job relies
            // on: the scan bounds, the work area narrows. The second one was also answering
            // a question this class never had. Discovery is bounded by what is *loaded* -
            // the scan walks live instances, as the remarks above say - so a tree in an
            // unheld zone is undiscoverable whatever radius is used, while a tree in a zone
            // the villager's own travelling halo holds open is discoverable and legitimately
            // work. Bounding by the kept circles refused the second kind for no gain.
            Anchors.Clear();
            Vector3 hearth = colony.transform.position;
            Anchors.Add(new Vector4(hearth.x, hearth.y, hearth.z, bound));

            IReadOnlyList<Vector4> flags = Colonies.KolonyReach.FlagAreas(colony);
            for (int i = 0; i < flags.Count; i++)
            {
                // Copied out immediately, and by index: that list is a shared scratch buffer
                // rebuilt on the next ask by anyone.
                Vector4 flag = flags[i];
                Anchors.Add(new Vector4(flag.x, flag.y, flag.z, bound));
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
