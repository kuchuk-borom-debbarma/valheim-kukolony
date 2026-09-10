using Kukolony.Colonies;
using UnityEngine;

namespace Kukolony.Jobs.Stations
{
    /// <summary>
    ///     Extracts honey. Unlike the other stations this produces a world drop rather than
    ///     changing the station's contents, so it stays Running and records that a drop is
    ///     expected; the job is not done until that honey has been collected and stored.
    /// </summary>
    internal sealed class BeehiveProtocol : IStationProtocol
    {
        public StructureCapability Capability => StructureCapability.BeeHive;
        public bool Matches(GameObject target) => target.GetComponent<Beehive>() != null;

        public JobResult Operate(StationContext context, out string activity)
        {
            if (!context.Target.TryGetComponent(out Beehive hive) || hive.GetHoneyLevel() <= 0)
                return JobOutcomes.Skipped(context.State, "no honey ready", out activity);

            // Tapping is the whole of this step. Waiting for the honey to appear and
            // collecting it are pieces of their own, so no sub-state is left behind here.
            context.View.InvokeRPC("RPC_Extract");
            context.State.ClearTarget();
            context.State.SetQueueProgress(0);
            activity = "extracting honey";
            return JobResult.Running;
        }
    }
}
