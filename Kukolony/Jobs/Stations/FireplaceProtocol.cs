using Kukolony.Colonies;
using UnityEngine;

namespace Kukolony.Jobs.Stations
{
    /// <summary>Feeds fuel to fireplaces, hearths, and both kinds of torch.</summary>
    internal sealed class FireplaceProtocol : IStationProtocol
    {
        public StructureCapability Capability => StructureCapability.Fireplace;
        public bool Matches(GameObject target) => target.GetComponent<Fireplace>() != null;

        public JobResult Operate(StationContext context, out string activity)
        {
            if (!context.Target.TryGetComponent(out Fireplace fire))
                return JobOutcomes.Failed(context.State, "fireplace invalid", out activity);

            // A fireplace that burns forever, or refuses refills, still accepts AddFuel and
            // still reports a change - so without these guards a villager feeds resin into
            // it indefinitely and the fuel is simply destroyed.
            if (fire.m_infiniteFuel) return JobOutcomes.Skipped(context.State, "fireplace never needs fuel", out activity);
            if (!fire.m_canRefill) return JobOutcomes.Skipped(context.State, "fireplace cannot be refilled", out activity);

            if (context.Item == null || fire.m_fuelItem == null ||
                context.ItemName != Utils.GetPrefabName(fire.m_fuelItem.gameObject))
                return JobOutcomes.Skipped(context.State, "no compatible fuel", out activity);

            // AddFuel owns the max-fuel guard and submits the verified AddFuelAmount RPC.
            // CanUseItems cannot be used here: it checks the local Player inventory rather
            // than the villager bag. A revision that did not move means the station was full.
            uint revision = context.View.GetZDO().DataRevision;
            fire.AddFuel(1f);
            if (context.View.GetZDO().DataRevision == revision)
                return JobOutcomes.Skipped(context.State, "fireplace full", out activity);
            if (!context.Consume())
                return JobOutcomes.Failed(context.State, "fuel disappeared", out activity);
            return JobOutcomes.Completed(context.State, "operation submitted", out activity);
        }
    }
}
