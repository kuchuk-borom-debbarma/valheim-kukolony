using System.Collections.Generic;
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
        /// <summary>Generous enough to catch anything a previous run scattered around.</summary>
        private const float PurgeRadius = 80f;

        internal static void Purge(Vector3 around)
        {
            int villagers = DestroyAll(CollectVillagers());
            int posts = DestroyAll(CollectPosts());
            int props = DestroyAll(CollectProps(around));

            Log.Info($"[TestWorld] purged {villagers} villager(s), {posts} post(s), {props} prop(s)");
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
