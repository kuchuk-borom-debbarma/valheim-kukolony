using System;
using System.Collections.Generic;
using Kukolony.Colonies;
using Kukolony.Party;
using UnityEngine;

namespace Kukolony.Resources
{
    /// <summary>
    ///     What a colony has to work on nearby, refreshed on a timer rather than looked up again
    ///     for every villager.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Scenery has no registry.</b> The game keeps instance lists for items and for
    ///         creatures, not for trees or rocks, so finding one means looking through everything
    ///         loaded. That is affordable once every few seconds <em>for a colony</em>. It would
    ///         not be affordable once per villager per tick, which is the only reason this class
    ///         exists.
    ///     </para>
    ///     <para>
    ///         The mod this one replaces died partly of exactly that: an overlap sphere of five
    ///         hundred metres, per villager, per second, with no layer mask. Survivable with one
    ///         worker and not with a settlement.
    ///     </para>
    ///     <para>
    ///         <b>It only sees what is loaded.</b> Off screen that depends on the keep-alive
    ///         allowlist including whatever is being swept for. Without it a villager away from
    ///         any player finds nothing and idles - and works perfectly every time somebody comes
    ///         to watch, which is the hardest kind of fault to notice.
    ///     </para>
    ///     <para>
    ///         <b>One sweep per resource, sharing the machinery.</b> Chopping had all of this to
    ///         itself; mining needs the same thing with a different test, and the difference
    ///         between the two is one predicate. Copying the rest would have been two places to
    ///         fix the next time the anchoring rule changes - and that rule has already taken
    ///         three rounds of review to get right.
    ///     </para>
    /// </remarks>
    internal sealed class GroundSweep
    {
        /// <summary>
        ///     Long enough that the scan is cheap, short enough that something taken by hand
        ///     stops being offered before a villager has walked all the way to it.
        /// </summary>
        private const float RefreshSeconds = 5f;

        /// <summary>
        ///     How far from any of a Kolony's places a sweep looks for work.
        /// </summary>
        /// <remarks>
        ///     The single answer, asked by everything that needs it rather than each deriving its
        ///     own from the config. Three rounds of review found the scan, the work area and the
        ///     screen's reach row disagreeing about this number in three different shapes; they
        ///     disagreed because each computed it. Now there is one.
        /// </remarks>
        internal static float SearchRadius =>
            ModConfig.ResourceScanRadius != null ? ModConfig.ResourceScanRadius.Value : 96f;

        /// <summary>
        ///     Where this colony works and how far, reused across refreshes so the scan does not
        ///     allocate a list per colony per five seconds.
        /// </summary>
        /// <remarks>
        ///     Static and shared between sweeps on purpose: it is scratch, filled and read inside
        ///     one call, and two sweeps never run inside each other.
        /// </remarks>
        private static readonly List<Vector4> Anchors = new List<Vector4>();

        private readonly Dictionary<ZDOID, Cache> _caches = new Dictionary<ZDOID, Cache>();
        private readonly Action _ensureReady;
        private readonly Func<bool> _ready;
        private readonly Func<int, bool> _wanted;
        private readonly Func<ZDO, bool> _worth;

        /// <param name="ensureReady">
        ///     Builds this sweep's classifier if it has not been built. Called at the point of
        ///     use rather than by whichever system happens to start first: the keep-alive driver
        ///     looked like the natural home until you notice it stands down entirely when its
        ///     feature is switched off or the peer is a client - which would have left the
        ///     classifier empty and the job quietly finding nothing, with the config toggle for
        ///     an unrelated feature as the cause.
        /// </param>
        /// <param name="wanted">Whether a prefab hash is something this sweep is looking for.</param>
        /// <param name="ready">Whether the classifier could be built at all.</param>
        /// <param name="worth">
        ///     Whether one particular instance is worth returning, asked of its record.
        /// </param>
        /// <remarks>
        ///     <para>
        ///         <paramref name="worth" /> is optional and chopping, mining and foraging all
        ///         pass nothing. They ask a question about a <em>kind</em> of thing - is this a
        ///         tree - which a prefab hash answers with an integer compare. Repair asks about
        ///         a particular one: is <em>this</em> wall damaged, which is per-instance state.
        ///     </para>
        ///     <para>
        ///         Given the record rather than the object, deliberately. Health lives on the ZDO,
        ///         so the question is a float read and needs nothing instantiated - which is what
        ///         makes it affordable to ask of every piece in a base rather than of a handful
        ///         of trees.
        ///     </para>
        /// </remarks>
        internal GroundSweep(Action ensureReady, Func<bool> ready, Func<int, bool> wanted,
            Func<ZDO, bool> worth = null)
        {
            _ensureReady = ensureReady;
            _ready = ready;
            _wanted = wanted;
            _worth = worth;
        }

        /// <summary>Dropped when a world unloads; a colony's identity does not survive one.</summary>
        internal void Clear() => _caches.Clear();

        /// <summary>
        ///     Everything loaded and wanted that this colony can see, as ZDOIDs.
        /// </summary>
        /// <remarks>
        ///     Deliberately unsorted and unfiltered beyond the outer bound. The caller decides
        ///     what it wants, how far it will walk and what somebody else has already claimed,
        ///     because only it knows those things - and re-sorting a shared list per villager
        ///     would put the cost back where this class took it away from.
        /// </remarks>
        internal List<ZDOID> Near(Colony colony)
        {
            if (colony == null) return new List<ZDOID>();

            ZDOID key = colony.Id;
            if (key.IsNone()) return new List<ZDOID>();

            if (!_caches.TryGetValue(key, out Cache cache))
            {
                cache = new Cache();
                _caches[key] = cache;
            }

            if (Time.time - cache.Refreshed < RefreshSeconds) return cache.Found;

            cache.Refreshed = Time.time;
            cache.Found.Clear();
            if (ZNetScene.instance == null) return cache.Found;

            _ensureReady();

            // And bail out if it could not be built. Without this the sweep walks every loaded
            // instance in the world asking a predicate that can only answer no - the one case
            // where the whole walk is guaranteed pointless.
            if (!_ready()) return cache.Found;

            // The widest net, bounded once here rather than once per villager.
            //
            // Bounded from every place the Kolony has, not only its hearth. Measuring from the
            // hearth alone made the ceiling a ceiling on the whole feature: a flag planted at a
            // wood further out than the config radius could be claimed, pointed at by a job, and
            // shown on the map, and every villager sent to it reported "nothing to do" forever -
            // the scan had already discarded the work before the work area was consulted. An
            // outpost is a place the Kolony works, so it anchors the search as the hearth does.
            //
            // Three rounds of review went on this knob, capping it at a flag's radius and then at
            // what the keep-alive holds, and each cap broke the rule the jobs rely on: the scan
            // bounds, the work area narrows. The second was also answering a question this class
            // never had. Discovery is bounded by what is *loaded* - the scan walks live instances
            // - so something in an unheld zone is undiscoverable whatever radius is used, while
            // something in a zone a villager's own travelling halo holds open is discoverable and
            // legitimately work. Bounding by the kept circles refused the second kind for no gain.
            float bound = SearchRadius;

            Anchors.Clear();
            Vector3 hearth = colony.transform.position;
            Anchors.Add(new Vector4(hearth.x, hearth.y, hearth.z, bound));

            IReadOnlyList<Vector4> flags = KolonyReach.FlagAreas(colony);
            for (int i = 0; i < flags.Count; i++)
            {
                // Copied out immediately, and by index: the list belongs to the colony and is
                // rebuilt on its own schedule, so holding a reference across the loop below
                // would be reading something that can change underneath it.
                Vector4 flag = flags[i];
                Anchors.Add(new Vector4(flag.x, flag.y, flag.z, bound));
            }

            // And every player this colony's villagers are following, because a party is a place
            // the Kolony works that happens to be walking around. Without this a villager taken
            // three hundred metres from home finds nothing: its job would have a work area
            // centred on its player and no candidates inside it, because discovery is bounded
            // here and here alone - which is the same shape as the bug a flag planted beyond the
            // scan radius produced, work claimed and shown on the map that no villager could see.
            //
            // Added to the colony's own sweep rather than given one per villager. The walk below
            // is over every loaded instance and is the expensive part; one more anchor costs one
            // more distance check per candidate, while a sweep per party villager would cost the
            // whole walk again.
            AddPartyAnchors(colony, bound);

            foreach (ZNetView view in ZNetScene.instance.m_instances.Values)
            {
                if (view == null || !view.IsValid()) continue;

                ZDO zdo = view.GetZDO();
                if (!_wanted(zdo.GetPrefab())) continue;
                if (!WithinAnyAnchor(zdo.GetPosition())) continue;

                // The cheap questions first, in the order they get cheaper to answer: a prefab
                // hash is an integer compare, a position is arithmetic, and only what survives
                // both is asked the per-instance question.
                if (_worth != null && !_worth(zdo)) continue;

                cache.Found.Add(zdo.m_uid);
            }

            return cache.Found;
        }

        /// <summary>
        ///     An anchor at each player being followed by one of this colony's villagers.
        /// </summary>
        /// <remarks>
        ///     Deduplicated, because a player with five villagers in their party is one place, and
        ///     five identical circles would be four wasted distance checks per candidate for the
        ///     whole sweep.
        /// </remarks>
        private static void AddPartyAnchors(Colony colony, float bound)
        {
            ZDOID id = colony.Id;

            foreach (Villagers.Villager villager in Villagers.Villager.Instances)
            {
                if (villager == null) continue;
                if (!villager.TryGetComponent(out ZNetView view) || !view.IsValid()) continue;

                ZDO zdo = view.GetZDO();
                if (ColonyMembership.GetColony(zdo) != id) continue;

                Villagers.VillagerState state = new Villagers.VillagerState(zdo);
                if (!state.IsValid || !state.InAParty) continue;

                Player leader = PartyMembership.LeaderOf(state);
                if (leader == null) continue;

                Vector3 at = leader.transform.position;
                if (AlreadyAnchored(at)) continue;

                Anchors.Add(new Vector4(at.x, at.y, at.z, bound));
            }
        }

        private static bool AlreadyAnchored(Vector3 at)
        {
            for (int i = 0; i < Anchors.Count; i++)
            {
                Vector4 anchor = Anchors[i];
                if (Utils.DistanceXZ(at, new Vector3(anchor.x, anchor.y, anchor.z)) < .5f) return true;
            }

            return false;
        }

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

        private sealed class Cache
        {
            internal float Refreshed;
            internal readonly List<ZDOID> Found = new List<ZDOID>();
        }
    }
}
