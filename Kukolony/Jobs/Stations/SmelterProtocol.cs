using Kukolony.Colonies;
using UnityEngine;

namespace Kukolony.Jobs.Stations
{
    /// <summary>
    ///     Loads smelters, kilns and their relatives. One carried item can be either fuel or
    ///     ore, so the station's own fuel definition decides which, rather than the job.
    /// </summary>
    internal sealed class SmelterProtocol : IStationProtocol
    {
        public StructureCapability Capability => StructureCapability.Smelter;
        public bool Matches(GameObject target) => target.GetComponent<Smelter>() != null;

        public JobResult Operate(StationContext context, out string activity)
        {
            if (!context.Target.TryGetComponent(out Smelter smelter))
                return JobOutcomes.Failed(context.State, "smelter invalid", out activity);
            if (context.Item == null)
                return JobOutcomes.Skipped(context.State, "no input", out activity);

            bool isFuel = smelter.m_fuelItem != null &&
                          Utils.GetPrefabName(smelter.m_fuelItem.gameObject) == context.ItemName;
            if (isFuel)
            {
                if (smelter.m_maxFuel > 0 && smelter.GetFuel() >= smelter.m_maxFuel)
                    return JobOutcomes.Skipped(context.State, "smelter fuel full", out activity);
                if (!context.Consume())
                    return JobOutcomes.Failed(context.State, "fuel disappeared", out activity);
                context.View.InvokeRPC("RPC_AddFuel");
                return JobOutcomes.Completed(context.State, "operation submitted", out activity);
            }

            if (!smelter.IsItemAllowed(context.Item))
                return JobOutcomes.Skipped(context.State, "input not accepted", out activity);
            if (smelter.GetQueueSize() >= smelter.m_maxOre)
                return JobOutcomes.Skipped(context.State, "smelter input full", out activity);
            string ore = context.ItemName;
            if (!context.Consume())
                return JobOutcomes.Failed(context.State, "input disappeared", out activity);
            context.View.InvokeRPC("RPC_AddOre", ore, false);
            return JobOutcomes.Completed(context.State, "operation submitted", out activity);
        }
    }
}
