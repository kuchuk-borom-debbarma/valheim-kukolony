using System.Collections.Generic;
using Kukolony.Villagers;

namespace Kukolony.Jobs
{
    /// <summary>
    ///     Scheduling for a villager's ordered queue of colony job IDs. The queue is a ring:
    ///     the entry after the last is the first. All position and attempt state lives on the
    ///     villager ZDO, so scheduling survives a save/reload without a runtime cursor.
    /// </summary>
    internal static class QueueRunner
    {
        /// <summary>
        ///     The job the villager should run now, or null if none of its entries resolve.
        ///     Entries whose job was deleted are bypassed rather than stalling the villager,
        ///     and the persisted position is repaired when a bypass moves it. Scans at most
        ///     one full lap so an all-missing queue terminates.
        /// </summary>
        internal static ColonyJobConfig Current(VillagerState state, List<ColonyJobConfig> jobs)
        {
            List<string> queue = state.GetQueue();
            if (queue.Count == 0) return null;
            int position = Normalise(state.QueuePosition, queue.Count);
            for (int checkedEntries = 0; checkedEntries < queue.Count; checkedEntries++)
            {
                ColonyJobConfig found = jobs.Find(job => job.Id == queue[position]);
                if (found != null)
                {
                    if (position != state.QueuePosition) state.SetQueuePosition(position);
                    return found;
                }
                position = (position + 1) % queue.Count;
            }
            return null;
        }

        /// <summary>
        ///     Folds a tick's outcome into queue state. <c>Running</c> changes nothing.
        ///     <c>Skipped</c> yields immediately without consuming an attempt, so a job with no
        ///     useful work cannot monopolise the villager. <c>Completed</c> and <c>Failed</c>
        ///     each consume one attempt and advance only once the job's configured count is
        ///     exhausted — failures are counted so an impossible job cannot loop forever.
        /// </summary>
        internal static void Apply(VillagerState state, List<ColonyJobConfig> jobs, JobResult result)
        {
            List<string> queue = state.GetQueue();
            if (queue.Count == 0 || result == JobResult.Running) return;

            int position = Normalise(state.QueuePosition, queue.Count);
            ColonyJobConfig job = jobs.Find(candidate => candidate.Id == queue[position]);

            if (result == JobResult.Skipped)
            {
                Advance(state, position, queue.Count);
                return;
            }

            int consumed = state.QueueAttempt + 1;
            if (job != null && consumed < System.Math.Max(1, job.Count))
            {
                state.SetQueueAttempt(consumed);
                state.ResetRuntime();
                return;
            }

            Advance(state, position, queue.Count);
        }

        /// <summary>Moves to the next entry and clears per-entry attempt and runtime state.</summary>
        private static void Advance(VillagerState state, int position, int count)
        {
            state.SetQueuePosition((position + 1) % count);
            state.SetQueueAttempt(0);
            state.ResetRuntime();
        }

        /// <summary>
        ///     Clamps a persisted position into range. The queue may have shrunk since the
        ///     position was written, so a stored index is never trusted directly.
        /// </summary>
        private static int Normalise(int position, int count) =>
            position < 0 ? 0 : position % count;
    }
}
