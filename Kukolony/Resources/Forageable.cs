using System.Collections.Generic;
using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Resources
{
    /// <summary>What a pair of hands can pick, and whether it comes back.</summary>
    /// <remarks>
    ///     Persisted in job settings as a bit per kind, so append rather than reorder.
    /// </remarks>
    internal enum ForageKind
    {
        None = 0,

        /// <summary>
        ///     A berry bush, a mushroom, a thistle - anything that grows back on its own.
        /// </summary>
        /// <remarks>
        ///     Told apart by <c>m_respawnTimeMinutes</c> being above zero, which is asset data
        ///     on the prefab rather than a name this mod would have to know. A modded bush that
        ///     regrows is a bush that regrows without anybody adding it to a list.
        /// </remarks>
        Regrows = 1,

        /// <summary>
        ///     Picked once and gone: a grown crop, an obsidian outcrop, a surtling core.
        /// </summary>
        /// <remarks>
        ///     Still ordinary work - a farm is exactly this, and harvesting one is the thing
        ///     most people will want a forager for. But it is the half that can strip a place
        ///     permanently, so the job can be told to leave it alone.
        /// </remarks>
        Once = 2
    }

    /// <summary>
    ///     Which prefabs are worth walking up to and picking, and what they yield.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>One component and no ambiguity.</b> Chopping and mining both end in a guess -
    ///         a <c>Destructible</c> an axe bites might be a stump or a wagon, and the game has
    ///         no Stone type at all - so both have an opt-in tail for the scenery they cannot
    ///         tell apart. <c>Pickable</c> has no such tail: it exists for exactly one purpose,
    ///         which is a person walking up and taking the thing. So there is nothing here to
    ///         switch on, and the kinds below are about what happens afterwards rather than
    ///         about what something is.
    ///     </para>
    ///     <para>
    ///         <b>An unripe crop is not a <c>Pickable</c> at all.</b> A planted seed is a
    ///         <c>Plant</c>, which grows and replaces itself with a different prefab that
    ///         carries <c>Pickable</c>. So "only harvest what is ready" needs no rule and no
    ///         timer - the classifier simply never sees the unripe one, and wild berries and
    ///         farmed carrots come out of one predicate.
    ///     </para>
    ///     <para>
    ///         Indexed by hash so finding work costs an integer compare per candidate rather
    ///         than a <c>GetComponent</c>, which is what makes it affordable to look through
    ///         everything the game has loaded - and what mining learned the hard way when
    ///         asking the scene per candidate per tick dropped the frame rate.
    ///     </para>
    /// </remarks>
    internal static class Forageable
    {
        private static readonly Dictionary<int, ForageKind> Kinds = new Dictionary<int, ForageKind>();

        /// <summary>What each indexed prefab gives up when picked, by item prefab name.</summary>
        private static readonly Dictionary<int, List<string>> Drops = new Dictionary<int, List<string>>();

        /// <summary>
        ///     Which prefabs start life already picked.
        /// </summary>
        /// <remarks>
        ///     <c>Pickable.Awake</c> reads its own picked flag as
        ///     <c>zdo.GetBool(s_picked, m_defaultPicked)</c>, so a record with nothing written
        ///     means <em>this prefab's</em> default and not <c>false</c>. Indexed because the
        ///     job asks it of every candidate while choosing, and because getting it wrong sends
        ///     villagers to bushes that were never there to pick.
        /// </remarks>
        private static readonly Dictionary<int, bool> StartPicked = new Dictionary<int, bool>();

        /// <summary>
        ///     The prefabs behind those hashes, kept so a check can pick a real bush rather than
        ///     name one. Prefab names are asset data this mod cannot see from the managed
        ///     assembly, and it has been wrong about one before.
        /// </summary>
        private static readonly List<GameObject> Prefabs = new List<GameObject>();

        internal static bool IsReady => Kinds.Count > 0;

        internal static ForageKind Of(int prefabHash) =>
            Kinds.TryGetValue(prefabHash, out ForageKind kind) ? kind : ForageKind.None;

        /// <summary>What this prefab gives up when it is picked, by item prefab name.</summary>
        internal static List<string> Yields(int prefabHash) =>
            Drops.TryGetValue(prefabHash, out List<string> found) ? found : new List<string>();

        /// <summary>Whether this prefab yields any of these, or whether nothing was asked for.</summary>
        /// <remarks>
        ///     An empty list means everything, which is the reading every other allow-list in
        ///     this mod has and for the same reason: "I did not narrow it" is not "I wanted
        ///     none".
        /// </remarks>
        internal static bool DropsAny(int prefabHash, List<string> wanted)
        {
            if (wanted == null || wanted.Count == 0) return true;

            foreach (string dropped in Yields(prefabHash))
            {
                if (wanted.Contains(dropped)) return true;
            }

            return false;
        }

        /// <summary>
        ///     Whether a record with nothing written should be read as already picked.
        /// </summary>
        /// <remarks>
        ///     The default the component itself would use. Asked by the job so that "is there
        ///     anything on this bush" can be answered from the ZDO alone - no instance, no
        ///     components - which is what makes it affordable inside the loop that chooses.
        /// </remarks>
        internal static bool StartsPicked(int prefabHash) =>
            StartPicked.TryGetValue(prefabHash, out bool picked) && picked;

        internal static void Rebuild()
        {
            Kinds.Clear();
            Drops.Clear();
            StartPicked.Clear();
            Prefabs.Clear();
            if (ZNetScene.instance == null) return;

            int regrowing = 0, once = 0;
            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                if (prefab == null) continue;

                ForageKind kind = Classify(prefab);
                if (kind == ForageKind.None) continue;

                int hash = prefab.name.GetStableHashCode();
                Kinds[hash] = kind;
                Drops[hash] = Yield(prefab);
                StartPicked[hash] = prefab.TryGetComponent(out Pickable pickable) && pickable.m_defaultPicked;
                Prefabs.Add(prefab);

                if (kind == ForageKind.Regrows) regrowing++;
                else once++;
            }

            Log.Info($"[forage] {regrowing} thing(s) that grow back and {once} that do not, indexed");
        }

        /// <summary>
        ///     Dropped when a world unloads. Prefab hashes are per-session once mods can register
        ///     their own, so carrying entries across worlds risks pointing at whatever later
        ///     takes the same hash.
        /// </summary>
        internal static void Clear()
        {
            Kinds.Clear();
            Drops.Clear();
            StartPicked.Clear();
            Prefabs.Clear();
        }

        /// <summary>
        ///     Everything anything in this world can be picked for, for a player choosing what to
        ///     gather.
        /// </summary>
        /// <remarks>
        ///     Read from the world rather than listed, which is what lets the picker offer a
        ///     modded berry without this mod hearing of it - and what stops it offering barley
        ///     on a world where nobody has been to the plains.
        /// </remarks>
        internal static void Harvest(List<string> into)
        {
            if (into == null) return;
            if (!IsReady) Rebuild();

            foreach (GameObject prefab in Prefabs)
            {
                if (prefab == null) continue;

                foreach (string dropped in Yields(prefab.name.GetStableHashCode()))
                {
                    if (!into.Contains(dropped)) into.Add(dropped);
                }
            }

            into.Sort(System.StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        ///     A real prefab name for something of this kind, or empty when this world has none.
        /// </summary>
        /// <remarks>
        ///     So a check can assert what the regrowing switch does without naming a bush on one
        ///     side and a crop on the other - the same service <see cref="Choppable.SampleTree" />
        ///     and <see cref="Mineable.SampleDeposit" /> do, and for the same reason: this mod
        ///     does not ship the assets and cannot check its own spelling.
        /// </remarks>
        internal static string Sample(ForageKind kind)
        {
            if (!IsReady) Rebuild();

            foreach (GameObject prefab in Prefabs)
            {
                if (prefab == null) continue;
                if (Of(prefab.name.GetStableHashCode()) != kind) continue;

                // Skip anything that starts picked: a check that spawns one gets an empty bush
                // and then proves nothing, which is the failure mode a sample exists to avoid.
                if (prefab.TryGetComponent(out Pickable pickable) && pickable.m_defaultPicked) continue;

                return prefab.name;
            }

            return string.Empty;
        }

        /// <summary>
        ///     What a pair of hands would make of this prefab, asked straight rather than through
        ///     the hash index.
        /// </summary>
        /// <remarks>
        ///     Shared with the keep-alive allowlist, which is built from prefabs in its own pass
        ///     and cannot wait for this index to be ready. Two component checks that had to agree
        ///     about what a bush is, written twice, would be two answers to the one question that
        ///     decides whether an off-screen villager can find work at all.
        /// </remarks>
        internal static ForageKind Classify(GameObject prefab)
        {
            if (prefab == null) return ForageKind.None;
            if (!prefab.TryGetComponent(out Pickable pickable)) return ForageKind.None;

            // Nothing to give. A Pickable with no item and no extra table is scenery wearing
            // the component - picking it would be a walk for nothing, every time.
            if (pickable.m_itemPrefab == null &&
                (pickable.m_extraDrops?.m_drops == null || pickable.m_extraDrops.m_drops.Count == 0))
            {
                return ForageKind.None;
            }

            return pickable.m_respawnTimeMinutes > 0f ? ForageKind.Regrows : ForageKind.Once;
        }

        private static List<string> Yield(GameObject prefab)
        {
            List<string> dropped = new List<string>();
            if (!prefab.TryGetComponent(out Pickable pickable)) return dropped;

            if (pickable.m_itemPrefab != null) dropped.Add(pickable.m_itemPrefab.name);
            DropNames.Add(dropped, pickable.m_extraDrops);
            return dropped;
        }
    }
}
