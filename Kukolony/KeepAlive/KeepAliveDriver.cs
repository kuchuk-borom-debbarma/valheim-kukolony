using System;
using System.Collections;
using System.Collections.Generic;
using Kukolony.Colonies;
using Kukolony.Core;
using Kukolony.Villagers;
using UnityEngine;

namespace Kukolony.KeepAlive
{
    /// <summary>
    ///     Keeps <see cref="KeepAliveZones" /> up to date.
    ///
    ///     Two sources, because a villager far from any player is not instantiated and so
    ///     cannot report its own position:
    ///
    ///       * loaded villagers - the component registry, read every refresh;
    ///       * unloaded villagers - their ZDOs, scanned on a timer.
    ///
    ///     The second is what bootstraps the whole thing. Find the ZDO, force its zone,
    ///     the villager instantiates, and from then on it holds its own zone open because
    ///     ZoneSystem never unloads a zone containing a live instance.
    /// </summary>
    internal sealed class KeepAliveDriver : MonoBehaviour
    {
        /// <summary>Cheap enough to run often; the expensive part is the ZDO scan.</summary>
        private const float RefreshIntervalSeconds = 1f;

        private static readonly int VillagerPrefabHash = VillagerPrefab.PrefabName.GetStableHashCode();

        private readonly List<ZDO> _villagerZdos = new List<ZDO>();
        private readonly List<Vector3> _positions = new List<Vector3>();
        private readonly List<Vector4> _areas = new List<Vector4>();
        private readonly List<Vector3> _structures = new List<Vector3>();

        private float _refreshTimer;
        private float _scanTimer;
        private bool _scanning;
        private bool _wasInWorld;

        private void Update()
        {
            if (!InWorld())
            {
                // Leaving a world invalidates everything we were holding.
                if (_wasInWorld)
                {
                    Shutdown();
                }

                return;
            }

            _wasInWorld = true;

            if (!ShouldDrive())
            {
                // Zones are only ever cleared inside Rebuild, so an early return that
                // skipped it used to leave the last computed set live indefinitely -
                // toggling the config off did not restore vanilla behaviour, and a
                // previous world's zones survived into the next one.
                KeepAliveZones.Clear();
                return;
            }

            if (!LoadAllowlist.IsReady)
            {
                LoadAllowlist.Rebuild();
            }

            _scanTimer += Time.deltaTime;
            if (!_scanning && _scanTimer >= ModConfig.KeepAliveScanSeconds.Value)
            {
                _scanTimer = 0f;
                StartCoroutine(ScanWorld());
            }

            _refreshTimer += Time.deltaTime;
            if (_refreshTimer < RefreshIntervalSeconds)
            {
                return;
            }

            _refreshTimer = 0f;
            Refresh();
        }

        private static bool InWorld() =>
            ZNetScene.instance != null && ZDOMan.instance != null && ZNet.instance != null;

        /// <summary>
        ///     Only the server simulates idle colonies: ZDOMan.ReleaseNearbyZDOS is
        ///     server-side, and AI runs only on the ZDO owner. A client forcing zones
        ///     would load every colony in the world for objects it does not own and cannot
        ///     tick - and would take on all of these patches' side effects for nothing.
        ///
        ///     In single-player and host-and-play the player is the server, so this is
        ///     true for everyone who is not a joining client.
        /// </summary>
        private static bool ShouldDrive() =>
            ModConfig.KeepAliveEnabled.Value && ZNet.instance.IsServer();

        /// <summary>
        ///     Drops everything held across a world boundary.
        ///
        ///     ZDOs matter here: ZDOMan.ShutDown releases them to a pool that hands the
        ///     same objects back out for unrelated ZDOs, so keeping references across
        ///     sessions is a use-after-free. A recycled ZDO reports IsValid again with a
        ///     different prefab and position, and would hold zones open at random places.
        /// </summary>
        private void Shutdown()
        {
            _wasInWorld = false;
            _scanning = false;
            _scanTimer = 0f;
            _refreshTimer = 0f;
            _villagerZdos.Clear();
            ColonyRegistry.Clear();
            KolonyReach.Clear();
            KeepAliveZones.Clear();
            LoadAllowlist.Clear();

            // Everything chopping remembers is per-world: prefab hashes are per-session once
            // mods can register their own, a colony's identity does not survive a world, and
            // "my axe cannot cut that tree" is about a tree that no longer exists.
            Colonies.Stock.Clear();
            Resources.Choppable.Clear();
            Resources.ChoppingGround.Clear();
            Jobs.Chop.ChopJob.Clear();
            Jobs.Unreachable.Clear();
            StopAllCoroutines();
        }

        private void Refresh()
        {
            _positions.Clear();

            // Loaded villagers report their live position, which is what makes the kept
            // region follow them as they walk.
            foreach (Villager villager in Villager.Instances)
            {
                if (villager == null) continue;

                _positions.Add(villager.transform.position);

                // Where it is going, as well as where it is. A villager crossing open country
                // needs the ground ahead to exist before it can be asked for a path into it -
                // the navmesh is built from colliders that are actually present - so a journey
                // loads its own corridor by intending to walk there.
                if (villager.IsTravelling)
                {
                    _positions.Add(villager.Waypoint);
                }
            }

            // Unloaded ones only exist as ZDOs. Their last known position is enough to
            // force the zone that will bring them back.
            foreach (ZDO zdo in _villagerZdos)
            {
                // A recycled ZDO reports valid again as an unrelated object - ZDOMan pools
                // and reuses them - so confirm it is still a villager before holding a
                // halo open around wherever it now is.
                if (zdo != null && zdo.IsValid() && zdo.GetPrefab() == VillagerPrefabHash)
                {
                    _positions.Add(zdo.GetPosition());
                }
            }

            // The Kolonies' own ground: each hearth with its radius, each claimed flag
            // with its own. Circles rather than points, so a real outpost keeps all of its
            // zones rather than the one its flagpole stands in.
            _areas.Clear();
            ColonyRegistry.CollectAreas(_areas);

            // Every registered structure, last. Almost all of them stand inside a circle
            // already and cost nothing more; the ones that do not are the lowest priority
            // when the cap binds, because losing a chest's zone loses a delivery, while
            // losing a villager's loses the villager.
            _structures.Clear();
            ColonyRegistry.CollectMemberPositions(_structures);

            KeepAliveZones.Rebuild(_positions, _areas, _structures);
        }

        /// <summary>
        ///     Walks every villager ZDO in the world without instantiating anything.
        ///     Iterative so the cost is spread over frames rather than spiking.
        /// </summary>
        private IEnumerator ScanWorld()
        {
            _scanning = true;

            try
            {
                yield return ColonyRegistry.Scan();

                List<ZDO> found = new List<ZDO>();
                int index = 0;

                while (true)
                {
                    // The scan yields across frames, so the world can go away underneath
                    // it. Without this check it throws, the coroutine dies, and the latch
                    // below never clears - leaving the scan dead for the rest of the
                    // process and off-screen colonies unable to bootstrap ever again.
                    if (!InWorld())
                    {
                        yield break;
                    }

                    bool done;
                    try
                    {
                        done = ZDOMan.instance.GetAllZDOsWithPrefabIterative(
                            VillagerPrefab.PrefabName, found, ref index);
                    }
                    catch (Exception e)
                    {
                        Log.Warning($"[KeepAlive] villager scan aborted: {e.Message}");
                        yield break;
                    }

                    if (done)
                    {
                        break;
                    }

                    yield return null;
                }

                _villagerZdos.Clear();
                _villagerZdos.AddRange(found);
            }
            finally
            {
                _scanning = false;
            }
        }
    }
}
