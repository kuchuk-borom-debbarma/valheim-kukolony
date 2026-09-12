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

            foreach (ZDO colonyZdo in ValidColonies())
            {
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

            foreach (ZDO colonyZdo in ValidColonies())
            {
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

        /// <summary>
        ///     The known hearth ZDOs that still resolve. The one reading of "valid" - the
        ///     null-and-IsValid filter used to be hand-copied at five call sites, which is
        ///     five places to miss when what "valid" means changes.
        /// </summary>
        internal static IEnumerable<ZDO> ValidColonies()
        {
            foreach (ZDO zdo in ColonyZdos)
            {
                if (zdo != null && zdo.IsValid())
                {
                    yield return zdo;
                }
            }
        }

        /// <summary>Whether any known hearth still resolves.</summary>
        internal static bool AnyValid()
        {
            foreach (ZDO _ in ValidColonies())
            {
                return true;
            }

            return false;
        }

        internal static void Clear()
        {
            ColonyZdos.Clear();

            // Anything still sweeping is sweeping a world that no longer exists; the
            // generation bump makes it abort rather than repopulate this list with the
            // dead world's ZDOs - which ZDOMan recycles, so keeping them is holding
            // references that will soon report valid as unrelated objects.
            _generation++;
            _sweeping = false;

            // And a fresh world deserves a fresh sweep, not the tail of the last one's timer.
            _nextSweep = 0f;
        }

        /// <summary>Whether a sweep is in flight, for screens that want to say "looking".</summary>
        internal static bool Sweeping => _sweeping;

        /// <summary>Bumped when a sweep lands with a different list, for screens to watch.</summary>
        internal static int Revision { get; private set; }

        private static bool _sweeping;
        private static float _nextSweep;
        private static int _generation;

        /// <summary>How stale the registry may go between sweeps.</summary>
        private const float SweepSeconds = 10f;

        /// <summary>
        ///     Rescans if the registry may be stale, throttled. Any consumer may poke this
        ///     from its own Update; the registry is static, so the caller lends the body the
        ///     coroutine runs on.
        /// </summary>
        /// <remarks>
        ///     This is what fills the list for everyone the keep-alive driver does not: a
        ///     joined client, and anyone with the feature off. The throttle arms whenever
        ///     any sweep completes - the driver's scans go through <see cref="Scan" /> too -
        ///     so where the driver already keeps the list fresh this stands down by itself,
        ///     with no copy of the driver's own gating to drift out of agreement with it.
        ///     A one-shot ("only when empty") gate is deliberately not used - it froze the
        ///     list at its first answer, so a hearth built afterwards never appeared and a
        ///     destroyed one never left.
        /// </remarks>
        internal static void EnsureFresh(MonoBehaviour host)
        {
            if (host == null || _sweeping || Time.time < _nextSweep) return;
            if (ZNet.instance == null || ZDOMan.instance == null) return;

            // StartCoroutine on an inactive host fails without running a single line, so
            // the in-flight flag is set inside Scan - which runs synchronously up to its
            // first yield - never latched out here where a refused start would wedge it.
            host.StartCoroutine(Scan());
        }

        /// <summary>
        ///     Sweeps the world for colony hearths without instantiating any. Iterative so
        ///     the cost is spread across frames.
        /// </summary>
        internal static IEnumerator Scan()
        {
            _sweeping = true;
            int generation = _generation;

            try
            {
                List<ZDO> found = new List<ZDO>();
                int index = 0;

                while (true)
                {
                    // The world can go away - or be replaced - under a multi-frame sweep.
                    // The generation check is what stops a sweep started in one world from
                    // writing that world's ZDOs into the next one's registry.
                    if (ZDOMan.instance == null || generation != _generation)
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

                if (generation != _generation)
                {
                    yield break;
                }

                bool changed = Changed(found);
                ColonyZdos.Clear();
                ColonyZdos.AddRange(found);
                if (changed)
                {
                    Revision++;
                }
            }
            finally
            {
                _sweeping = false;
                _nextSweep = Time.time + SweepSeconds;
            }
        }

        private static bool Changed(List<ZDO> found)
        {
            if (found.Count != ColonyZdos.Count) return true;

            for (int i = 0; i < found.Count; i++)
            {
                if (!ReferenceEquals(found[i], ColonyZdos[i])) return true;
            }

            return false;
        }
    }
}
