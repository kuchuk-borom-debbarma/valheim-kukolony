using System.Collections.Generic;
using Kukolony.Villagers;

namespace Kukolony.Jobs
{
    /// <summary>
    ///     The villager's queue: an ordered ring of job ids, each run a configured number of
    ///     times before the next.
    /// </summary>
    internal static class QueueRunner
    {
        /// <summary>
        ///     The job this villager should be working on, or null.
        /// </summary>
        /// <remarks>
        ///     A queue entry whose job the colony no longer has is bypassed rather than left to
        ///     stall the villager — but it is <em>not</em> silently removed, because a queue
        ///     half full of deleted jobs should look half broken on the screen rather than
        ///     quietly repair itself into something the player did not ask for.
        ///
        ///     The scan runs at most one full lap, so a queue of nothing but missing jobs
        ///     terminates instead of spinning.
        /// </remarks>
        internal static JobDefinition Current(VillagerState state, List<JobDefinition> jobs)
        {
            List<string> queue = state.ActiveQueue();
            if (queue.Count == 0 || jobs == null || jobs.Count == 0) return null;

            int position = Normalise(state.QueuePosition, queue.Count);
            for (int scanned = 0; scanned < queue.Count; scanned++)
            {
                int index = (position + scanned) % queue.Count;
                JobDefinition job = jobs.Find(candidate => candidate.Id == queue[index]);
                if (job == null) continue;

                // Repair the stored position so the bypass is not repaid every tick.
                if (index != state.QueuePosition)
                {
                    state.SetQueuePosition(index);
                    state.SetQueueAttempt(0);
                }

                return job;
            }

            return null;
        }

        /// <summary>
        ///     Applies an outcome to the queue.
        /// </summary>
        internal static void Apply(VillagerState state, List<JobDefinition> jobs, JobResult result)
        {
            // The one place a repetition is known to have finished, which makes it the one place
            // worth stamping. Everything else a villager reports - running, skipped, failed -
            // is compatible with achieving nothing at all, and a mod whose worst failure mode is
            // a villager quietly achieving nothing needs a signal that is not.
            if (result == JobResult.Completed) state.MarkWorked();

            if (result == JobResult.Running) return;

            List<string> queue = state.ActiveQueue();
            if (queue.Count == 0) return;

            int position = Normalise(state.QueuePosition, queue.Count);

            // Skipped yields immediately without spending anything, so a job with nothing to do
            // hands the villager to the next entry rather than burning through its own count
            // and falling out of the rotation.
            if (result == JobResult.Skipped)
            {
                Advance(state, position, queue.Count);
                return;
            }

            JobDefinition job = jobs?.Find(candidate => candidate.Id == queue[position]);
            int consumed = state.QueueAttempt + 1;
            int wanted = job == null ? 1 : System.Math.Max(1, job.Repeat);

            if (consumed < wanted)
            {
                state.SetQueueAttempt(consumed);
                return;
            }

            Advance(state, position, queue.Count);
        }

        private static void Advance(VillagerState state, int position, int count)
        {
            state.SetQueuePosition((position + 1) % count);
            state.SetQueueAttempt(0);
            state.ResetJob();
        }

        /// <summary>
        ///     Brings a stored position back into range.
        /// </summary>
        /// <remarks>
        ///     The queue may have shrunk since the position was written, so a stored index is
        ///     never trusted directly.
        /// </remarks>
        private static int Normalise(int position, int count) =>
            count <= 0 ? 0 : ((position % count) + count) % count;
    }
}
