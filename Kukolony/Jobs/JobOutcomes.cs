using Kukolony.Villagers;

namespace Kukolony.Jobs
{
    /// <summary>
    ///     The four ways a tick of work can end, each doing its own cleanup.
    /// </summary>
    /// <remarks>
    ///     Written as named constructors rather than bare returns so that "does this consume a
    ///     repetition, and what does it release" is a property of the ending itself rather than
    ///     something every call site has to remember. The previous system had the same idea and
    ///     it was one of the few parts that never grew a bug.
    /// </remarks>
    internal static class JobOutcomes
    {
        /// <summary>
        ///     Progress was made. Nothing is released and nothing is consumed.
        /// </summary>
        /// <remarks>
        ///     Every <c>Running</c> must be progress towards something that ends. The previous
        ///     system hung a villager forever by returning Running from a wait whose target
        ///     could never arrive, and because Running consumes no repetition, nothing bounded
        ///     it. A wait that cannot make progress belongs in <see cref="Skipped" />.
        /// </remarks>
        internal static JobResult Running(string doing, out string activity)
        {
            activity = doing;
            return JobResult.Running;
        }

        /// <summary>
        ///     A repetition finished. Releases the trip and consumes one count.
        /// </summary>
        internal static JobResult Completed(VillagerState state, string doing, out string activity)
        {
            state.ResetJob();
            activity = doing;
            return JobResult.Completed;
        }

        /// <summary>
        ///     It could not be done. Releases the trip and consumes one count, which is what
        ///     bounds retries on work that will never succeed.
        /// </summary>
        internal static JobResult Failed(VillagerState state, string why, out string activity)
        {
            state.ResetJob();
            activity = why;
            return JobResult.Failed;
        }

        /// <summary>
        ///     Nothing useful to do right now. Releases the trip, consumes nothing, and yields.
        /// </summary>
        /// <remarks>
        ///     Distinct from <see cref="Failed" /> on purpose: no eligible item, nowhere to put
        ///     one, or a tired villager is not a job that failed, and charging it a repetition
        ///     would let an idle job exhaust its own count and stop being tried.
        /// </remarks>
        internal static JobResult Skipped(VillagerState state, string why, out string activity)
        {
            state.ResetJob();
            activity = why;
            return JobResult.Skipped;
        }
    }
}
