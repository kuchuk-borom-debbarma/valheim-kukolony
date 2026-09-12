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

        internal static int Count => Allowed.Count;

        internal static bool IsReady => Allowed.Count > 0;

        /// <summary>
        ///     Built once from ZNetScene by component, not by name - a name list would
        ///     silently miss modded chests and stations.
        /// </summary>
        internal static void Rebuild()
        {
            Allowed.Clear();

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

            Log.Info($"[KeepAlive] allowlist covers {Allowed.Count} prefab(s)");
        }

        internal static bool Contains(int prefabHash) => Allowed.Contains(prefabHash);

        /// <summary>
        ///     Dropped when a world unloads - prefab hashes are per-session once mods can
        ///     register their own, so carrying a list across worlds risks stale entries.
        /// </summary>
        internal static void Clear() => Allowed.Clear();

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

            // Loose items: the raw material of anything a colony picks up.
            if (prefab.GetComponent<ItemDrop>() != null)
            {
                return true;
            }

            // And the colony itself.
            // The flag is a Piece already, but name it anyway: a marker that got filtered
            // out of its own kept zone would be an outpost nobody can interact with.
            return prefab.GetComponent<Villagers.Villager>() != null
                   || prefab.GetComponent<Colonies.Colony>() != null
                   || prefab.GetComponent<Colonies.WorkFlag>() != null;
        }
    }
}
