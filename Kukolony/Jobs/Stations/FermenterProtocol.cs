using Kukolony.Colonies;
using UnityEngine;

namespace Kukolony.Jobs.Stations
{
    /// <summary>Fills fermenters and taps them when their contents are ready.</summary>
    internal sealed class FermenterProtocol : IStationProtocol
    {
        public StructureCapability Capability => StructureCapability.Fermenter;
        public bool Matches(GameObject target) => target.GetComponent<Fermenter>() != null;

        public JobResult Operate(StationContext context, out string activity)
        {
            if (!context.Target.TryGetComponent(out Fermenter fermenter))
                return JobOutcomes.Failed(context.State, "fermenter invalid", out activity);

            if (fermenter.GetStatus() == Fermenter.Status.Ready)
            {
                context.View.InvokeRPC("RPC_Tap");
                return JobOutcomes.Completed(context.State, "operation submitted", out activity);
            }
            if (fermenter.GetStatus() != Fermenter.Status.Empty)
                return JobOutcomes.Skipped(context.State, "fermenter is busy", out activity);
            if (context.Item == null)
                return JobOutcomes.NeedInput(context.State, "fermenter needs input", out activity);
            if (!fermenter.IsItemAllowed(context.Item))
                return JobOutcomes.Skipped(context.State, "input not accepted", out activity);

            // The fermenter RPC takes a prefab hash rather than a name; read it before
            // consuming, because the item is gone by the time the call is made.
            int hash = context.ItemName.GetStableHashCode();
            if (!context.Consume())
                return JobOutcomes.Failed(context.State, "fermentable disappeared", out activity);
            context.View.InvokeRPC("RPC_AddItem", hash, false);
            return JobOutcomes.Completed(context.State, "operation submitted", out activity);
        }
    }
}
