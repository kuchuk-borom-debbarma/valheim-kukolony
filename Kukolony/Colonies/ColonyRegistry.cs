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
        ///     The circles every known Kolony claims: each hearth with its radius, and each
        ///     claimed flag with its own. Read off ZDOs, so an unloaded outpost still holds
        ///     its ground open - which is the point of planting a flag there. The flags'
        ///     circles come from <see cref="KolonyReach" />, so what the keep-alive holds and
        ///     what reach answers are one rendering, not two that must stay identical.
        /// </summary>
        internal static void CollectAreas(List<Vector4> into)
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

                // The hearth anchors itself. It never used to: only villagers and registered
                // structures fed the halo, so a hearth with neither held nothing open and a
                // fresh Kolony's ground could unload out from under its first villager.
                Vector3 home = colonyZdo.GetPosition();
                into.Add(new Vector4(home.x, home.y, home.z, Colony.ConfiguredRadius));

                KolonyReach.CollectFlagAreas(new ColonyState(colonyZdo), into);
            }
        }

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

        internal static void Clear()
        {
            ColonyZdos.Clear();

            // A fresh world deserves a fresh sweep, not the tail of the last one's timer.
            _nextSweep = 0f;
        }

        /// <summary>Whether a sweep is in flight, for screens that want to say "looking".</summary>
        internal static bool Sweeping => _sweeping;

        private static bool _sweeping;
        private static float _nextSweep;

        /// <summary>How stale the registry may go on peers the keep-alive driver ignores.</summary>
        private const float SweepSeconds = 10f;

        /// <summary>
        ///     Rescans if the registry may be stale, throttled. Any consumer may poke this
        ///     from its own Update; the registry is static, so the caller lends the body the
        ///     coroutine runs on.
        /// </summary>
        /// <remarks>
        ///     This is what fills the list for everyone the keep-alive driver does not: a
        ///     joined client, and anyone with the feature off. Left to the driver alone, the
        ///     flag screen and the map pins read an empty registry forever on exactly those
        ///     peers. Where the driver already sweeps on its own timer this stands down, so
        ///     the server never runs the scan twice. A one-shot ("only when empty") gate is
        ///     deliberately not used - it froze the list at its first answer, so a hearth
        ///     built afterwards never appeared and a destroyed one never left.
        /// </remarks>
        internal static void EnsureFresh(MonoBehaviour host)
        {
            if (host == null || _sweeping || Time.time < _nextSweep) return;
            if (ZNet.instance == null || ZDOMan.instance == null) return;
            if (ModConfig.KeepAliveEnabled.Value && ZNet.instance.IsServer()) return;

            _sweeping = true;
            _nextSweep = Time.time + SweepSeconds;
            host.StartCoroutine(Sweep());
        }

        private static IEnumerator Sweep()
        {
            try
            {
                yield return Scan();
            }
            finally
            {
                _sweeping = false;
            }
        }

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
