using System.Collections;
using System.Collections.Generic;
using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Colonies
{
    /// <summary>
    ///     Finds colonies, including ones nobody is standing near.
    ///
    ///     This is what makes a colony more than a local object: the member lists can be
    ///     read straight off ZDOs, so the keep-alive knows where a colony's chests and
    ///     workstations are without any of them being instantiated. That is what lets a
    ///     villager reach a container that is far away.
    /// </summary>
    internal static class ColonyRegistry
    {
        private static readonly List<ZDO> ColonyZdos = new List<ZDO>();

        internal static int KnownColonies => ColonyZdos.Count;

        /// <summary>
        ///     Positions of everything every known colony owns, members included.
        ///     Villagers are excluded - they report their own live position elsewhere,
        ///     and a stale ZDO position for a walking villager would hold the wrong zone.
        /// </summary>
        internal static void CollectMemberPositions(List<Vector3> into)
        {
            if (ZDOMan.instance == null)
            {
                return;
            }

            foreach (ZDO colonyZdo in ColonyZdos)
            {
                if (colonyZdo == null || !colonyZdo.IsValid())
                {
                    continue;
                }

                ColonyState state = new ColonyState(colonyZdo);

                AddPositions(state, ColonyMemberKind.Container, into);
                AddPositions(state, ColonyMemberKind.Station, into);
                AddPositions(state, ColonyMemberKind.Home, into);
            }
        }

        private static void AddPositions(ColonyState state, ColonyMemberKind kind, List<Vector3> into)
        {
            foreach (ZDOID member in state.GetMembers(kind))
            {
                ZDO zdo = ZDOMan.instance.GetZDO(member);
                if (zdo != null && zdo.IsValid())
                {
                    into.Add(zdo.GetPosition());
                }
            }
        }

        internal static void Clear() => ColonyZdos.Clear();

        /// <summary>
        ///     Sweeps the world for colony hearths without instantiating any. Iterative so
        ///     the cost is spread across frames.
        /// </summary>
        internal static IEnumerator Scan()
        {
            List<ZDO> found = new List<ZDO>();
            int index = 0;

            while (true)
            {
                if (ZDOMan.instance == null)
                {
                    yield break;
                }

                bool done;
                try
                {
                    done = ZDOMan.instance.GetAllZDOsWithPrefabIterative(
                        ColonyPrefab.PrefabName, found, ref index);
                }
                catch (System.Exception e)
                {
                    Log.Warning($"[colony] hearth scan aborted: {e.Message}");
                    yield break;
                }

                if (done)
                {
                    break;
                }

                yield return null;
            }

            ColonyZdos.Clear();
            ColonyZdos.AddRange(found);
        }
    }
}
