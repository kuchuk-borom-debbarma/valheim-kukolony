using System;
using System.Collections.Generic;
using Kukolony.Jobs.Steps;

namespace Kukolony.Jobs
{
    /// <summary>
    ///     Turns a step type name from a definition file into a runnable step.
    ///
    ///     These names are **user-facing API**. They appear in every job file a player
    ///     writes, so renaming one silently breaks their content. The set is deliberately
    ///     small, and named for what a step does rather than how it does it.
    /// </summary>
    internal static class JobStepFactory
    {
        private static readonly Dictionary<string, Func<JobStepSpec, IJobStep>> Builders =
            new Dictionary<string, Func<JobStepSpec, IJobStep>>(StringComparer.OrdinalIgnoreCase)
            {
                { "find_ground_item", _ => new FindGroundItemStep() },
                { "move_to_target", spec => new MoveToTargetStep(spec.GetFloat("stopDistance", 2f)) },
                { "pick_up_item", _ => new PickUpItemStep() },
                { "resolve_destination", _ => new ResolveDestinationStep() },
                { "deposit_item", _ => new DepositItemStep() }
            };

        internal static IEnumerable<string> KnownTypes => Builders.Keys;

        /// <summary>Builds a step, or returns null if the type is not recognised.</summary>
        internal static IJobStep TryBuild(JobStepSpec spec)
        {
            if (spec == null || string.IsNullOrEmpty(spec.Type))
            {
                return null;
            }

            return Builders.TryGetValue(spec.Type, out Func<JobStepSpec, IJobStep> builder)
                ? builder(spec)
                : null;
        }
    }
}
