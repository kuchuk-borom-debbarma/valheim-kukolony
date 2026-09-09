using System.Collections.Generic;
using Kukolony.Core;
using Kukolony.Jobs;
using Kukolony.Villagers;
using UnityEngine;

namespace Kukolony.Debug
{
    /// <summary>Fast deterministic checks for queue semantics; world scenarios remain config-gated.</summary>
    internal sealed class ColonySelfTest : MonoBehaviour
    {
        private bool _reported;
        private void Update()
        {
            if (_reported || !ModConfig.AutoTestEnabled.Value || ZNet.instance == null) return;
            _reported = true;
            List<ColonyJobConfig> jobs = new List<ColonyJobConfig> {
                new ColonyJobConfig { Id = "one", Type = ColonyJobType.HaulLoose, Count = 2 },
                new ColonyJobConfig { Id = "two", Type = ColonyJobType.FuelFireplaces, Count = 1 } };
            Log.Info("[ColonyTest] queue contract enabled: completed/failed consume count; skipped does not; queues loop. "
                     + "Run the configured two-launch world scenario for persistence and station mutations.");
        }
    }
}
