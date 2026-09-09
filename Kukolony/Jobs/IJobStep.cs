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
        /// <summary>Short name, used in logs and diagnostics.</summary>
        string Name { get; }

        StepStatus Tick(JobContext context);
    }
}
