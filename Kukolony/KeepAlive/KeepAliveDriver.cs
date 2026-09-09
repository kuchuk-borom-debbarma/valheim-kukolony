using System.Collections;
using System.Collections.Generic;
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

        private readonly List<ZDO> _villagerZdos = new List<ZDO>();
        private readonly List<Vector3> _positions = new List<Vector3>();

        private float _refreshTimer;
        private float _scanTimer;
        private bool _scanning;

        private void Update()
        {
            if (!ModConfig.KeepAliveEnabled.Value || ZNetScene.instance == null || ZDOMan.instance == null)
            {
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
                StartCoroutine(ScanForVillagerZdos());
            }

            _refreshTimer += Time.deltaTime;
            if (_refreshTimer < RefreshIntervalSeconds)
            {
                return;
            }

            _refreshTimer = 0f;
            Refresh();
        }

        private void Refresh()
        {
            _positions.Clear();

            // Loaded villagers report their live position, which is what makes the kept
            // region follow them as they walk.
            foreach (Villager villager in Villager.Instances)
            {
                if (villager != null)
                {
                    _positions.Add(villager.transform.position);
                }
            }

            // Unloaded ones only exist as ZDOs. Their last known position is enough to
            // force the zone that will bring them back.
            foreach (ZDO zdo in _villagerZdos)
            {
                if (zdo != null && zdo.IsValid())
                {
                    _positions.Add(zdo.GetPosition());
                }
            }

            KeepAliveZones.Rebuild(_positions);
        }

        /// <summary>
        ///     Walks every villager ZDO in the world without instantiating anything.
        ///     Iterative so the cost is spread over frames rather than spiking.
        /// </summary>
        private IEnumerator ScanForVillagerZdos()
        {
            _scanning = true;

            List<ZDO> found = new List<ZDO>();
            int index = 0;

            while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(VillagerPrefab.PrefabName, found, ref index))
            {
                yield return null;
            }

            _villagerZdos.Clear();
            _villagerZdos.AddRange(found);
            _scanning = false;
        }
    }
}
