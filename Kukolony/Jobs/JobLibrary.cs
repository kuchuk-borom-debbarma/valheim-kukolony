using System.Collections.Generic;
using Kukolony.Jobs.Steps;

namespace Kukolony.Jobs
{
    /// <summary>
    ///     The jobs a work post can be set to.
    ///
    ///     Composed in code for now. Stage B replaces the body of <see cref="BuildHaul" />
    ///     with definitions read from JSON, without changing anything that consumes this -
    ///     which is the point of keeping lookup behind a method.
    /// </summary>
    internal static class JobLibrary
    {
        internal const string Haul = "haul";

        private static readonly Dictionary<string, Job> Jobs = new Dictionary<string, Job>
        {
            { Haul, BuildHaul() }
        };

        internal static IEnumerable<string> Ids => Jobs.Keys;

        internal static Job Find(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            return Jobs.TryGetValue(id, out Job job) ? job : null;
        }

        /// <summary>
        ///     Gather a named item from the ground and stock it in a container.
        ///
        ///     move_to_target appears twice, unchanged, once for the item and once for the
        ///     container. That reuse is the whole reason steps take their target from the
        ///     context rather than knowing what they are walking to.
        /// </summary>
        private static Job BuildHaul()
        {
            return new Job(Haul, new IJobStep[]
            {
                new FindGroundItemStep(),
                new MoveToTargetStep(stopDistance: 2f),
                new PickUpItemStep(),
                new ResolveDestinationStep(),
                new MoveToTargetStep(stopDistance: 2f),
                new DepositItemStep()
            });
        }
    }
}
