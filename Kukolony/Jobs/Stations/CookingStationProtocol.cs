using Kukolony.Colonies;
using UnityEngine;

namespace Kukolony.Jobs.Stations
{
    /// <summary>Loads cooking stations and clears them once everything on them is done.</summary>
    internal sealed class CookingStationProtocol : IStationProtocol
    {
        public StructureCapability Capability => StructureCapability.CookingStation;
        public bool Matches(GameObject target) => target.GetComponent<CookingStation>() != null;

        public JobResult Operate(StationContext context, out string activity)
        {
            if (!context.Target.TryGetComponent(out CookingStation cooking))
                return JobOutcomes.Failed(context.State, "cooking station invalid", out activity);

            // Clearing comes first: a full station of cooked food cannot accept anything, so
            // taking it off is the only move that makes progress.
            if (!cooking.IsEmpty() && cooking.IsEverythingCooked())
            {
                context.View.InvokeRPC("RPC_RemoveDoneItem", context.Target.transform.position, 1);
                return JobOutcomes.Completed(context.State, "operation submitted", out activity);
            }
            if (context.Item != null && cooking.IsItemAllowed(context.Item) && !cooking.IsStationFull())
            {
                string food = context.ItemName;
                if (!context.Consume())
                    return JobOutcomes.Failed(context.State, "food disappeared", out activity);
                context.View.InvokeRPC("RPC_AddItem", food, false);
                return JobOutcomes.Completed(context.State, "operation submitted", out activity);
            }
            if (context.Item == null && !cooking.IsStationFull())
                return JobOutcomes.NeedInput(context.State, "cooking station needs food", out activity);
            return JobOutcomes.Skipped(context.State, "cooking station has no available action", out activity);
        }
    }
}
