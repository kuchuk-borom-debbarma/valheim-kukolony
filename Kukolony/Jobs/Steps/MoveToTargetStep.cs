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
        /// <summary>
        ///     How long to tolerate "no path" before giving up.
        ///
        ///     BaseAI.FindPath is throttled and returns false until it has actually run,
        ///     so the first tick after taking a target almost always reports failure even
        ///     when the target is perfectly reachable. Treating that as fatal made
        ///     villagers restart their cycle the instant they set off - which looked like
        ///     two villagers fighting over one item, and was not.
        /// </summary>
        private const float PathGraceSeconds = 3f;

        private readonly float _stopDistance;

        private float _failingFor;

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
                // Destroyed, or its zone unloaded. Either way the plan is stale, and
                // unlike a missing path this will not fix itself.
                _failingFor = 0f;
                return StepStatus.Failed;
            }

            switch (Villagers.VillagerMovement.MoveTowards(context.Ai, target.transform.position, _stopDistance))
            {
                case Villagers.MoveResult.Arrived:
                    _failingFor = 0f;
                    return StepStatus.Succeeded;

                case Villagers.MoveResult.PathFailed:
                    _failingFor += context.DeltaTime;
                    if (_failingFor < PathGraceSeconds)
                    {
                        // Probably just the pathfinder not having run yet. Keep asking.
                        return StepStatus.Running;
                    }

                    _failingFor = 0f;
                    return StepStatus.Failed;

                default:
                    _failingFor = 0f;
                    return StepStatus.Running;
            }
        }
    }
}
