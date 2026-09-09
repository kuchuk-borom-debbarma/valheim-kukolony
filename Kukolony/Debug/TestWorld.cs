using System.Collections.Generic;
using Kukolony.Colonies;
using Kukolony.Core;
using Kukolony.Villagers;
using Kukolony.WorkPosts;
using UnityEngine;

namespace Kukolony.Debug
{
    /// <summary>
    ///     Resets the test world between runs.
    ///
    ///     Generating a fresh world per run costs five minutes of terrain generation and
    ///     leaves a trail of throwaway saves. Reusing one world is far quicker, but only
    ///     works if each run starts from a known state - otherwise villagers, posts and
    ///     wood accumulate and every run tests something slightly different.
    ///
    ///     Deliberately blunt: it destroys every villager, work post, container and loose
    ///     item near the player. That would be reckless in a real world, which is why it
    ///     only ever runs from the test harness in a world the harness created.
    /// </summary>
    internal static class TestWorld
    {
        /// <summary>
        ///     Has to reach past the far destination chest the haul test places at 140m.
        ///     At 80m it did not, so chests piled up at the destination run after run.
        /// </summary>
        private const float PurgeRadius = 220f;

        /// <summary>Rounds of the ZDO sweep before giving up, so a bug cannot hang a run.</summary>
        private const int MaxScanRounds = 500;

        internal static void Purge(Vector3 around)
        {
            int villagers = DestroyAll(CollectVillagers());
            int posts = DestroyAll(CollectPosts());
            int props = DestroyAll(CollectProps(around));

            // The lists above only see what is loaded. Anything the last run left in a
            // zone that is not loaded right now survives a purge - and then our own
            // keep-alive loads its zone and resurrects it mid-test. That is not a
            // hypothetical: a screenshot run left five villagers behind, they came back
            // during the next haul run, and their halos ate the zone budget.
            int stale = DestroyZdosWideWorld(VillagerPrefab.PrefabName)
                        + DestroyZdosWideWorld(WorkPostPrefab.PrefabName)
                        + DestroyZdosWideWorld(ColonyPrefab.PrefabName);

            ColonyRegistry.Clear();

            Log.Info($"[TestWorld] purged {villagers} villager(s), {posts} post(s), "
                     + $"{props} prop(s), {stale} unloaded ZDO(s)");
        }

        /// <summary>
        ///     Destroys every ZDO of a prefab anywhere in the world, loaded or not.
        ///     Ownership first, because only the owner may destroy a ZDO.
        /// </summary>
        private static int DestroyZdosWideWorld(string prefabName)
        {
            if (ZDOMan.instance == null)
            {
                return 0;
            }

            List<ZDO> found = new List<ZDO>();
            int index = 0;
            for (int round = 0; round < MaxScanRounds; round++)
            {
                if (ZDOMan.instance.GetAllZDOsWithPrefabIterative(prefabName, found, ref index))
                {
                    break;
                }
            }

            int destroyed = 0;
            foreach (ZDO zdo in found)
            {
                if (zdo == null || !zdo.IsValid())
                {
                    continue;
                }

                zdo.SetOwner(ZDOMan.GetSessionID());
                ZDOMan.instance.DestroyZDO(zdo);
                destroyed++;
            }

            return destroyed;
        }

        private static List<GameObject> CollectVillagers()
        {
            List<GameObject> found = new List<GameObject>();
            foreach (Villager villager in Villager.Instances)
            {
                if (villager != null)
                {
                    found.Add(villager.gameObject);
                }
            }

            return found;
        }

        private static List<GameObject> CollectPosts()
        {
            List<GameObject> found = new List<GameObject>();
            foreach (WorkPost post in WorkPost.Instances)
            {
                if (post != null)
                {
                    found.Add(post.gameObject);
                }
            }

            return found;
        }

        /// <summary>Chests and loose items left behind by a previous run.</summary>
        private static List<GameObject> CollectProps(Vector3 around)
        {
            List<GameObject> found = new List<GameObject>();

            List<Piece> pieces = new List<Piece>();
            Piece.GetAllPiecesInRadius(around, PurgeRadius, pieces);
            foreach (Piece piece in pieces)
            {
                if (piece != null && piece.GetComponent<Container>() != null)
                {
                    found.Add(piece.gameObject);
                }
            }

            foreach (ItemDrop drop in ItemDrop.s_instances)
            {
                if (drop != null && Utils.DistanceXZ(drop.transform.position, around) <= PurgeRadius)
                {
                    found.Add(drop.gameObject);
                }
            }

            return found;
        }

        /// <summary>
        ///     Destroys through ZNetScene so the ZDO goes too. Only the owner may destroy
        ///     a ZDO, and the harness owns everything it spawned, so this is reliable here.
        /// </summary>
        private static int DestroyAll(List<GameObject> objects)
        {
            int destroyed = 0;
            foreach (GameObject target in objects)
            {
                if (target == null)
                {
                    continue;
                }

                ZNetScene.instance.Destroy(target);
                destroyed++;
            }

            return destroyed;
        }
    }
}
