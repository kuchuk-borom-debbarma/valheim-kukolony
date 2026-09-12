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
            _stamp = 0L;

            // And a fresh world deserves a fresh sweep, not the tail of the last one's timer.
            _nextSweep = 0f;
        }

        /// <summary>
        ///     Whether a sweep is in flight, for screens that want to say "looking".
        /// </summary>
        /// <remarks>
        ///     Answered from a heartbeat rather than a bare flag, because a coroutine can
        ///     stop existing without unwinding: Unity abandons a host's iterators when the
        ///     host is destroyed, and <c>finally</c> never runs. The flag screen rebuilds
        ///     itself - destroying the very object it hosted its sweep on - every time
        ///     Jotunn raises its GUI-available event, so this is a thing that happens, not
        ///     a thing that could. A flag alone would then read "sweeping" with nothing
        ///     walking, for good: the screen says it is looking forever, and the guard in
        ///     <see cref="Scan" /> refuses every future sweep, freezing the registry.
        /// </remarks>
        internal static bool Sweeping =>
            _sweeping && Time.time - _heartbeat <= SweepGrace;

        /// <summary>
        ///     Bumped when a sweep lands with anything a screen would draw differently, for
        ///     screens to watch. Names are part of that, not just which hearths exist - a
        ///     list keyed on identity alone left an open screen showing a Kolony's old name
        ///     after somebody renamed it.
        /// </summary>
        internal static int Revision { get; private set; }

        private static bool _sweeping;
        private static float _nextSweep;
        private static float _heartbeat;
        private static int _generation;
        private static long _stamp;

        /// <summary>How stale the registry may go between sweeps.</summary>
        private const float SweepSeconds = 10f;

        /// <summary>
        ///     How long a sweep may go without a heartbeat before it is presumed dead. A
        ///     living sweep touches it every frame, so this only has to outlast a frame -
        ///     generously, because being early here means two sweeps at once, which is
        ///     merely wasteful, while being late means the registry stays frozen.
        /// </summary>
        private const float SweepGrace = 2f;

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
            // The GameObject, not the component: StartCoroutine fails on an inactive
            // object but runs perfectly well for a disabled component on an active one, so
            // isActiveAndEnabled would refuse sweeps that could have happened.
            if (host == null || !host.gameObject.activeInHierarchy) return;
            if (Sweeping || Time.time < _nextSweep) return;
            if (ZNet.instance == null || ZDOMan.instance == null) return;

            // Armed before starting, not only on completion. StartCoroutine on an inactive
            // host logs an error and runs not one line of the iterator, so a caller that
            // pokes this every frame turned into an error per frame once that happened -
            // the throttle has to bound the attempt, not just the success. Scan's own
            // finally re-arms it on real completion, so nothing changes when it works.
            //
            // The in-flight flag stays inside Scan, which runs synchronously to its first
            // yield: latched out here, a refused start would wedge it true forever.
            _nextSweep = Time.time + SweepSeconds;
            host.StartCoroutine(Scan());
        }

        /// <summary>
        ///     Sweeps the world for colony hearths without instantiating any. Iterative so
        ///     the cost is spread across frames.
        /// </summary>
        internal static IEnumerator Scan()
        {
            // One sweep at a time. The driver scans on its own timer and any consumer may
            // poke EnsureFresh, so two sweeps could overlap - and the first to finish
            // cleared the in-flight flag and armed the throttle while the second was still
            // walking, which reported "not sweeping" mid-sweep and did the work twice.
            // Deferring only to a sweep that is actually alive, never to a bare flag: an
            // abandoned coroutine cannot clear it, and a guard that trusted it would make
            // one destroyed GUI host freeze the registry for the whole session.
            // Checked before the try, so this early exit cannot run the cleanup below.
            if (Sweeping) yield break;

            _sweeping = true;
            _heartbeat = Time.time;
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

                    // Still walking. Said every frame, so a sweep that stops saying it is
                    // presumed dead and another may take over.
                    _heartbeat = Time.time;
                    yield return null;
                }

                if (generation != _generation)
                {
                    yield break;
                }

                long stamp = Stamp(found);
                ColonyZdos.Clear();
                ColonyZdos.AddRange(found);

                if (stamp != _stamp)
                {
                    _stamp = stamp;
                    Revision++;
                }
            }
            finally
            {
                // Only if this sweep still owns the flag. A sweep unwinding after its world
                // was left has already been disowned by Clear - clearing the flag here would
                // clear a *new* world's sweep, and arming the throttle here would make the
                // new world wait out a dead world's timer before its first sweep.
                if (generation == _generation)
                {
                    _sweeping = false;
                    _nextSweep = Time.time + SweepSeconds;
                }
            }
        }

        /// <summary>
        ///     A fingerprint of what a screen would draw from the known Kolonies: which
        ///     they are, and the state each carries.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>Order-insensitive</b>, because the sweep's order is not news. Comparing
        ///         positionally made any permutation of the same hearths read as a change,
        ///         and a change destroy-and-rebuilds the open screen - a button destroyed
        ///         between pointer-down and pointer-up never fires, so a spurious redraw
        ///         swallows the player's click.
        ///     </para>
        ///     <para>
        ///         <b>State, not just identity</b>, because the screen draws names and which
        ///         Kolony a flag already serves, and both are written into an existing
        ///         hearth's ZDO. Keyed on identity alone, a rename left a client's open
        ///         screen showing the old name for as long as it stayed open - and fed that
        ///         stale name into the report it printed on assigning. <c>DataRevision</c> is
        ///         the ZDO's own counter for "something on me was written", which is exactly
        ///         the question, and costs no allocation to ask.
        ///     </para>
        /// </remarks>
        private static long Stamp(List<ZDO> colonies)
        {
            long stamp = 0L;
            foreach (ZDO zdo in colonies)
            {
                if (zdo == null || !zdo.IsValid()) continue;

                // Mixed per hearth, then summed - so the combination does not depend on the
                // order, while two hearths swapping states still reads as a change.
                long id = zdo.m_uid.GetHashCode();
                long mixed = (id * 31L) ^ ((long)zdo.DataRevision * 1000003L);
                stamp += mixed;
                stamp += 1L << 32; // counts the hearths, in the same number
            }

            return stamp;
        }
    }
}
