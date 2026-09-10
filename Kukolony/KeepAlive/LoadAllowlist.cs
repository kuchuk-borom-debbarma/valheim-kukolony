using System.Collections.Generic;
using Kukolony.Core;
using UnityEngine;

namespace Kukolony.KeepAlive
{
    /// <summary>
    ///     Which prefabs are worth instantiating in a zone that is only loaded because a
    ///     villager is standing in it.
    ///
    ///     ChunkLoader-style mods load everything, paying for hundreds of trees and rocks
    ///     nobody is looking at. We load only what the colony interacts with or has to
    ///     path around, which is the bulk of the saving.
    ///
    ///     Piece is on the list deliberately: walking through a tree that was not loaded
    ///     is cosmetic, walking through your wall is not.
    /// </summary>
    internal static class LoadAllowlist
    {
        private static readonly HashSet<int> Allowed = new HashSet<int>();

        /// <summary>Whether the last build included trees. See <see cref="Rebuild(bool)"/>.</summary>
        private static bool _gathering;

        internal static int Count => Allowed.Count;

        internal static bool IsReady => Allowed.Count > 0;

        internal static bool IncludesResources => _gathering;

        /// <summary>
        ///     Built once from ZNetScene by component, not by name - a name list would
        ///     silently miss modded chests and stations.
        /// </summary>
        /// <param name="gathering">
        ///     Whether any colony is set to gather, which is the only reason to instantiate
        ///     trees. Off-screen a villager cannot chop what was never loaded: it would pick a
        ///     tree, walk to it and wait forever, while working perfectly every time anyone
        ///     came to look. So gathering colonies must load them.
        ///
        ///     They are not on the list unconditionally because trees are the most numerous
        ///     thing in the world by a wide margin, and loading every one in every kept zone is
        ///     exactly the cost this allowlist exists to avoid. A colony that only hauls and
        ///     smelts pays nothing.
        /// </param>
        internal static void Rebuild(bool gathering)
        {
            Allowed.Clear();
            _gathering = gathering;

            if (ZNetScene.instance == null)
            {
                return;
            }

            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                if (prefab != null && Matters(prefab))
                {
                    Allowed.Add(prefab.name.GetStableHashCode());
                }
            }

            Log.Info($"[KeepAlive] allowlist covers {Allowed.Count} prefab(s)"
                     + (gathering ? ", including trees for gathering" : string.Empty));
        }

        internal static bool Contains(int prefabHash) => Allowed.Contains(prefabHash);

        /// <summary>
        ///     Dropped when a world unloads - prefab hashes are per-session once mods can
        ///     register their own, so carrying a list across worlds risks stale entries.
        /// </summary>
        internal static void Clear()
        {
            Allowed.Clear();
            _gathering = false;
        }

        private static bool Matters(GameObject prefab)
        {
            // Anything the player built, so villagers path around walls rather than
            // through them - and so chests and stations exist to be worked.
            if (prefab.GetComponent<Piece>() != null)
            {
                return true;
            }

            if (prefab.GetComponent<Container>() != null
                || prefab.GetComponent<CraftingStation>() != null
                || prefab.GetComponent<Smelter>() != null
                || prefab.GetComponent<Fireplace>() != null)
            {
                return true;
            }

            // Terrain modifications. Heightmap.Regenerate applies them only from a live
            // TerrainComp, which has its own registry and no Piece component - so without
            // this a kept zone silently reverts to raw world-gen: a levelled base becomes
            // a hillside with the buildings still floating in it, and villagers path
            // against ground that is not there. It snaps back the moment a player arrives,
            // which makes it near-impossible to reproduce under observation.
            if (prefab.GetComponent<TerrainComp>() != null)
            {
                return true;
            }

            // Loose items are the raw material of every gathering job.
            if (prefab.GetComponent<ItemDrop>() != null)
            {
                return true;
            }

            // What a gathering colony works on. Only when one is, because this is by far the
            // most expensive entry on the list.
            if (_gathering && Resources.ResourceIndex.Of(prefab.name.GetStableHashCode())
                != Resources.ResourceKind.None)
            {
                return true;
            }

            // And the colony itself.
            return prefab.GetComponent<Villagers.Villager>() != null
                   || prefab.GetComponent<Colonies.Colony>() != null;
        }
    }
}
