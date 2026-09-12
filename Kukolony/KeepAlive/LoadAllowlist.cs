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

            // What an axe can cut, because a chopping villager cannot find work in a zone
            // whose trees were filtered out of it - and would idle off-screen while working
            // perfectly every time anybody came to look, which is the hardest class of fault
            // this mod has to guard against.
            //
            // This is the one entry that costs something real: trees are the most numerous
            // thing in the world, so a kept zone now instantiates its forest as well as its
            // buildings. It is bounded by the zone cap rather than by the world, and the
            // alternative is a job that silently does not work, so it is the right trade -
            // but it is a trade, and the zone budget is where it will be felt.
            if (Resources.Choppable.Classify(prefab) != Resources.ChopKind.None)
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
