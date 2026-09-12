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
        /// <summary>
        ///     The circles every known Kolony claims: each hearth with its radius, and each
        ///     claimed flag with its own. Read off ZDOs, so an unloaded outpost still holds
        ///     its ground open - which is the point of planting a flag there.
        /// </summary>
        internal static void CollectAreas(List<Vector4> into)
        {
            if (ZDOMan.instance == null)
            {
                return;
            }

            float hearthRadius = ModConfig.ColonyRadius != null ? ModConfig.ColonyRadius.Value : 48f;

            foreach (ZDO colonyZdo in ColonyZdos)
            {
                if (colonyZdo == null || !colonyZdo.IsValid())
                {
                    continue;
                }

                // The hearth anchors itself. It never used to: only villagers and registered
                // structures fed the halo, so a hearth with neither held nothing open and a
                // fresh Kolony's ground could unload out from under its first villager.
                Vector3 home = colonyZdo.GetPosition();
                into.Add(new Vector4(home.x, home.y, home.z, hearthRadius));

                ColonyState state = new ColonyState(colonyZdo);
                foreach (StructureRecord structure in state.GetStructures())
                {
                    if ((structure.Capabilities & StructureCapability.WorkArea) == 0) continue;

                    ZDO zdo = ZDOMan.instance.GetZDO(structure.Id);
                    if (zdo == null || !zdo.IsValid()) continue;

                    Vector3 at = zdo.GetPosition();
                    into.Add(new Vector4(at.x, at.y, at.z, WorkFlag.RadiusOf(zdo)));
                }
            }
        }

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

                foreach (StructureRecord structure in state.GetStructures())
                {
                    ZDO zdo = ZDOMan.instance.GetZDO(structure.Id);
                    if (zdo != null && zdo.IsValid())
                    {
                        into.Add(zdo.GetPosition());
                    }
                }
            }
        }

        internal static IReadOnlyList<ZDO> GetKnownColonies() => ColonyZdos;
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
