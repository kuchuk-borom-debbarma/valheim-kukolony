using System.Collections.Generic;

namespace Kukolony.Jobs
{
    /// <summary>
    ///     An ordered cycle of steps. Reaching the end wraps back to the start, so a job
    ///     is a loop rather than a one-shot task - a villager assigned to haul keeps
    ///     hauling.
    /// </summary>
    internal sealed class Job
    {
        internal Job(string id, IReadOnlyList<IJobStep> steps)
        {
            Id = id;
            Steps = steps;
        }

        internal string Id { get; }

        internal IReadOnlyList<IJobStep> Steps { get; }

        internal int StepCount => Steps.Count;
    }
}
