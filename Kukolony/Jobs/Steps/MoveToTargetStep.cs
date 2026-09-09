using UnityEngine;

namespace Kukolony.Jobs.Steps
{
    /// <summary>
    ///     Walks to whatever the previous step targeted.
    ///
    ///     Input:  <see cref="JobContext.Target" />.
    ///     Output: none - the villager is simply standing next to it.
    ///
    ///     This step appears twice in the haul job, once to reach an item and once to
    ///     reach a container. It works for both because it knows nothing about either.
    /// </summary>
    internal sealed class MoveToTargetStep : IJobStep
    {
        private readonly float _stopDistance;

        internal MoveToTargetStep(float stopDistance)
        {
            _stopDistance = stopDistance;
        }

        public string Name => "move_to_target";

        public StepStatus Tick(JobContext context)
        {
            GameObject target = context.ResolveTarget();
            if (target == null)
            {
                // Destroyed, or its zone unloaded. Either way the plan is stale.
                return StepStatus.Failed;
            }

            switch (Villagers.VillagerMovement.MoveTowards(context.Ai, target.transform.position, _stopDistance))
            {
                case Villagers.MoveResult.Arrived:
                    return StepStatus.Succeeded;

                case Villagers.MoveResult.PathFailed:
                    return StepStatus.Failed;

                default:
                    return StepStatus.Running;
            }
        }
    }
}
