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
        ///     How long a trip may make no progress at all before it is given up on.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Comfortably past the rescue ladder, which is given forty-five seconds before
        ///         it starts and several rungs after that. This is not a second rescue - it is
        ///         the bound that says the rescues did not work.
        ///     </para>
        ///     <para>
        ///         Without it, walking is an unbounded Running. The ladder resets its own
        ///         counters on any scrap of progress, so a villager inching at a hillside
        ///         climbs a rung, gains half a metre, and starts again for ever - reporting
        ///         "off to chop" the whole time and never ending the trip. And because the
        ///         queue ignores Running entirely, the job never finishes and the next entry
        ///         in a preset never runs. One villager standing on a slope quietly stops
        ///         being a settlement.
        ///     </para>
        /// </remarks>
        internal const float AbandonAfterSeconds = 120f;

        /// <summary>
        ///     Gives up on a trip that has stopped making progress, if it has.
        /// </summary>
        /// <remarks>
        ///     Returns null while there is still reason to wait, so a caller can go on with
        ///     whatever it was doing. Every job that walks needs this, because every job that
        ///     walks can be asked to reach somewhere it cannot - and the rule this enforces is
        ///     stated on <see cref="Running" />: a wait that cannot make progress is not one.
        /// </remarks>
        internal static JobResult? GiveUpIfStuck(VillagerState state, float stalledFor,
            string what, out string activity)
        {
            activity = null;
            if (stalledFor < AbandonAfterSeconds) return null;

            // Said out loud. A villager that quietly stops working looks identical to one with
            // nothing to do, and this is the case a player most needs told about - it usually
            // means the ground between here and there cannot be walked.
            Core.Chatter.Say("stuck " + what,
                $"Somebody cannot reach {what} and has given up on it for now.");

            return JobOutcomes.Failed(state,
                $"cannot reach {what} - gave up after {stalledFor:0}s", out activity);
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
