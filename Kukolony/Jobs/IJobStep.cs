namespace Kukolony.Jobs
{
    /// <summary>
    ///     One fragment of work: find a thing, walk to it, pick it up.
    ///
    ///     A step knows nothing about what runs before or after it. It reads its inputs
    ///     from the context, writes its outputs back, and reports whether it is done.
    ///     That ignorance is what lets the same step appear twice in one job and be
    ///     reused unchanged by the next job.
    /// </summary>
    internal interface IJobStep
    {
        /// <summary>Short machine name, used in logs, diagnostics and job files.</summary>
        string Name { get; }

        /// <summary>
        ///     What this step is doing right now, in words a player would use - "walking
        ///     to Wood", not "move_to_target". Shown on hover.
        ///
        ///     Lives on the step rather than in a lookup table because only the step knows
        ///     what it is currently acting on. Must tolerate being called on a client that
        ///     does not own the villager, since hover text is rendered by whoever is
        ///     looking at it.
        /// </summary>
        string Describe(JobContext context);

        StepStatus Tick(JobContext context);
    }
}
