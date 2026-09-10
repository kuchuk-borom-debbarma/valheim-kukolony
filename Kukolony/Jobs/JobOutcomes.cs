using Kukolony.Villagers;

namespace Kukolony.Jobs
{
    /// <summary>
    ///     The shared vocabulary for ending a step. These were private to the engine while it
    ///     was the only thing that ran work; station protocols and piece handlers need the
    ///     same four endings, and the distinction between them is a behaviour contract rather
    ///     than a convenience.
    /// </summary>
    internal static class JobOutcomes
    {
        /// <summary>
        ///     Nothing useful to do. Releases the target and consumes no queue attempt, so an
        ///     idle job yields to the next entry instead of starving the villager.
        /// </summary>
        internal static JobResult Skipped(VillagerState state, string message, out string activity)
        {
            state.ResetRuntime();
            // The prefix is player-visible in the roster's activity column, and reads as a
            // deliberate pause rather than a fault.
            activity = "skipping: " + message;
            return JobResult.Skipped;
        }

        /// <summary>
        ///     The step could not complete. Consumes a queue attempt, which is what bounds
        ///     retries on work that will never succeed.
        /// </summary>
        internal static JobResult Failed(VillagerState state, string message, out string activity)
        {
            state.ResetRuntime();
            activity = message;
            return JobResult.Failed;
        }

        /// <summary>
        ///     The station wants materials the villager is not carrying.
        /// </summary>
        /// <remarks>
        ///     This yields and begins the pipeline again, so the fetch pieces run and the
        ///     villager comes back carrying something. It must not report Running: a running
        ///     step is a step that made progress, and the walker advances past it - which
        ///     would step over the station itself and finish the cycle having done nothing.
        ///     No attempt is consumed, because wanting materials is not a failure.
        /// </remarks>
        internal static JobResult NeedInput(VillagerState state, string message, out string activity)
        {
            state.ResetJob();
            activity = "skipping: " + message;
            return JobResult.Skipped;
        }

        /// <summary>The step did its work. Releases the target and consumes a queue attempt.</summary>
        internal static JobResult Completed(VillagerState state, string message, out string activity)
        {
            state.ResetRuntime();
            activity = message;
            return JobResult.Completed;
        }
    }
}
