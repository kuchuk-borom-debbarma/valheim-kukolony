using System.Collections.Generic;
using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Resources
{
    /// <summary>What an axe can usefully be swung at.</summary>
    /// <remarks>Persisted in job settings as a bit per kind, so append rather than reorder.</remarks>
    internal enum ChopKind
    {
        None = 0,

        /// <summary>A standing tree. Felling it leaves a log, not wood.</summary>
        Tree = 1,

        /// <summary>A felled trunk, or one of the halves a big trunk breaks into.</summary>
        Log = 2,

        /// <summary>Stumps and bushes — whatever an axe bites that is neither of the above.</summary>
        Undergrowth = 3
    }

    /// <summary>
    ///     Which prefabs are worth swinging at, by prefab hash.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Built from components, never from names.</b> A name list would miss every
    ///         modded tree and most of the vanilla ones, and this mod does not ship the assets
    ///         so it cannot check its own spelling. The index is the only honest source of a
    ///         real prefab name, which is also why the benchmark asks it for a sample instead
    ///         of naming a species.
    ///     </para>
    ///     <para>
    ///         Indexed by hash so that finding work costs an integer compare per candidate
    ///         rather than a <c>GetComponent</c>. That is what makes it affordable to look
    ///         through everything the game has loaded.
    ///     </para>
    ///     <para>
    ///         <b>The game will not say what a thing needs.</b> <c>IDestructible</c> is
    ///         <c>Damage</c> and <c>GetDestructibleType</c>, and the latter is used in exactly
    ///         one place in the whole assembly — to pick which skill to credit for a hit. Tool
    ///         suitability is decided privately, inside the concrete class, after the RPC, and
    ///         <c>Damage</c> returns <c>void</c>. So this asks the only question it can answer
    ///         cheaply and in advance: <em>is chop damage capable of hurting this at all?</em>
    ///     </para>
    /// </remarks>
    internal static class Choppable
    {
        private static readonly Dictionary<int, ChopKind> Kinds = new Dictionary<int, ChopKind>();

        /// <summary>
        ///     The prefabs behind those hashes, kept so a check can pick a real tree rather
        ///     than name one. Prefab names are asset data: this mod does not ship the assets,
        ///     cannot see them from the managed assembly, and has been wrong about one before.
        /// </summary>
        private static readonly List<GameObject> Prefabs = new List<GameObject>();

        internal static bool IsReady => Kinds.Count > 0;

        internal static ChopKind Of(int prefabHash) =>
            Kinds.TryGetValue(prefabHash, out ChopKind kind) ? kind : ChopKind.None;

        internal static void Rebuild()
        {
            Kinds.Clear();
            Prefabs.Clear();
            if (ZNetScene.instance == null) return;

            int trees = 0, logs = 0, undergrowth = 0;
            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                if (prefab == null) continue;

                ChopKind kind = Classify(prefab);
                if (kind == ChopKind.None) continue;

                Kinds[prefab.name.GetStableHashCode()] = kind;
                Prefabs.Add(prefab);

                if (kind == ChopKind.Tree) trees++;
                else if (kind == ChopKind.Log) logs++;
                else undergrowth++;
            }

            Log.Info($"[chop] {trees} tree(s), {logs} log(s) and {undergrowth} other choppable thing(s) indexed");
        }

        /// <summary>
        ///     Dropped when a world unloads. Prefab hashes are per-session once mods can
        ///     register their own, so carrying entries across worlds risks pointing at
        ///     whatever later takes the same hash.
        /// </summary>
        internal static void Clear()
        {
            Kinds.Clear();
            Prefabs.Clear();
        }

        /// <summary>
        ///     A real prefab name for a tree whose minimum tool tier falls in this range, or
        ///     empty when the game has none.
        /// </summary>
        /// <remarks>
        ///     Lets a check ask for "a tree a stone axe can fell" and "a tree it cannot"
        ///     without naming either, which is the only way to write that check honestly.
        ///     The <em>smallest</em> eligible tree wins: a big trunk falls, slides, and comes
        ///     to rest a long way from where it stood, which makes anything measured by
        ///     distance from the stump unreliable.
        /// </remarks>
        internal static string SampleTree(int lowestTier, int highestTier)
        {
            string best = string.Empty;
            float smallest = float.MaxValue;

            foreach (GameObject prefab in Prefabs)
            {
                if (prefab == null || !prefab.TryGetComponent(out TreeBase tree)) continue;
                if (tree.m_minToolTier < lowestTier || tree.m_minToolTier > highestTier) continue;
                if (tree.m_health >= smallest) continue;

                smallest = tree.m_health;
                best = prefab.name;
            }

            return best;
        }

        /// <summary>Whether felling this tree leaves a log to cut up, or drops what it has where it stood.</summary>
        internal static bool LeavesALog(string prefabName)
        {
            foreach (GameObject prefab in Prefabs)
            {
                if (prefab != null && prefab.name == prefabName)
                {
                    return prefab.TryGetComponent(out TreeBase tree) && tree.m_logPrefab != null;
                }
            }

            return false;
        }

        private static ChopKind Classify(GameObject prefab)
        {
            // Order matters. A felled trunk is its own object with its own component, and
            // asking about the log first keeps the two from being confused.
            if (prefab.GetComponent<TreeLog>() != null) return ChopKind.Log;
            if (prefab.GetComponent<TreeBase>() != null) return ChopKind.Tree;

            // Everything else has to earn its place by being demonstrably vulnerable to an
            // axe. Admitting every Destructible in the game would hand villagers a licence to
            // demolish scenery nobody asked them to touch, and "it had a Destructible on it"
            // is not a reason to knock something down.
            if (prefab.TryGetComponent(out Destructible destructible) && Bites(destructible.m_damages.m_chop))
            {
                return ChopKind.Undergrowth;
            }

            return ChopKind.None;
        }

        /// <summary>Whether chop damage does anything at all to something with these resistances.</summary>
        private static bool Bites(HitData.DamageModifier modifier) =>
            modifier != HitData.DamageModifier.Immune && modifier != HitData.DamageModifier.Ignore;
    }
}
