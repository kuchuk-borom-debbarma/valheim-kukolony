using System.Collections.Generic;
using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Resources
{
    /// <summary>What a world object is worth doing to.</summary>
    internal enum ResourceKind
    {
        None = 0,
        /// <summary>A standing tree. Felling it leaves a log, not items.</summary>
        Tree = 1,
        /// <summary>A felled trunk. Cutting it up is what actually produces wood.</summary>
        Log = 2
    }

    /// <summary>
    ///     Which prefabs are worth working on, by prefab hash.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Built once from ZNetScene by component, never by name. A name list would miss
    ///         every modded tree and every vanilla one nobody thought to write down, and there
    ///         are dozens of vanilla ones.
    ///     </para>
    ///     <para>
    ///         The point of indexing by hash is that finding work then costs an integer compare
    ///         per candidate rather than a GetComponent. That is what makes it affordable to
    ///         look through everything the game has loaded.
    ///     </para>
    /// </remarks>
    internal static class ResourceIndex
    {
        private static readonly Dictionary<int, ResourceKind> Kinds = new Dictionary<int, ResourceKind>();

        /// <summary>
        ///     The prefabs behind those hashes, kept so the benchmark can pick a real tree
        ///     instead of naming one. Prefab names are asset data: this mod does not ship the
        ///     assets, cannot see them from the managed assembly, and has already been wrong
        ///     about one. Asking the index is the only way to be sure.
        /// </summary>
        private static readonly List<GameObject> Prefabs = new List<GameObject>();

        internal static bool IsReady => Kinds.Count > 0;

        internal static ResourceKind Of(int prefabHash) =>
            Kinds.TryGetValue(prefabHash, out ResourceKind kind) ? kind : ResourceKind.None;

        internal static void Rebuild()
        {
            Kinds.Clear();
            Prefabs.Clear();
            if (ZNetScene.instance == null) return;

            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                if (prefab == null) continue;
                ResourceKind kind = Classify(prefab);
                if (kind == ResourceKind.None) continue;
                Kinds[prefab.name.GetStableHashCode()] = kind;
                Prefabs.Add(prefab);
            }

            Log.Info($"[resources] index covers {Kinds.Count} prefab(s)");
        }

        /// <summary>
        ///     Dropped when a world unloads. Prefab hashes are per-session once mods can
        ///     register their own, so carrying a list across worlds risks stale entries.
        /// </summary>
        internal static void Clear()
        {
            Kinds.Clear();
            Prefabs.Clear();
        }

        /// <summary>
        ///     Name of an indexed tree whose minimum tool tier falls in this range, or empty
        ///     when the game has none. Lets the benchmark ask for "a tree a stone axe can fell"
        ///     and "a tree it cannot" without naming either.
        /// </summary>
        internal static string SampleTree(int lowestTier, int highestTier)
        {
            string best = string.Empty;
            float smallest = float.MaxValue;
            foreach (GameObject prefab in Prefabs)
            {
                if (prefab == null || !prefab.TryGetComponent(out TreeBase tree)) continue;
                if (tree.m_minToolTier < lowestTier || tree.m_minToolTier > highestTier) continue;
                // Smallest of the eligible ones. A big trunk falls, slides, and ends up a
                // long way from where it stood, which makes anything measured by distance
                // from the stump unreliable.
                if (tree.m_health >= smallest) continue;
                smallest = tree.m_health;
                best = prefab.name;
            }
            return best;
        }

        /// <summary>Whether felling this tree leaves a log to cut up, or drops its wood where it stood.</summary>
        internal static bool LeavesALog(string prefabName)
        {
            foreach (GameObject prefab in Prefabs)
                if (prefab != null && prefab.name == prefabName)
                    return prefab.TryGetComponent(out TreeBase tree) && tree.m_logPrefab != null;
            return false;
        }

        private static ResourceKind Classify(GameObject prefab)
        {
            // Order matters: a felled trunk is its own object with its own component, and
            // asking about the log first keeps the two from being confused.
            if (prefab.GetComponent<TreeLog>() != null) return ResourceKind.Log;
            if (prefab.GetComponent<TreeBase>() != null) return ResourceKind.Tree;
            return ResourceKind.None;
        }
    }
}
