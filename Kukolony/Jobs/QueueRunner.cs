using System.Collections.Generic;
using Kukolony.Villagers;

namespace Kukolony.Jobs
{
    internal static class QueueRunner
    {
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

        private static void Advance(VillagerState state, int position, int count)
        {
            state.SetQueuePosition((position + 1) % count);
            state.SetQueueAttempt(0);
            state.ResetRuntime();
        }

        private static int Normalise(int position, int count) =>
            position < 0 ? 0 : position % count;
    }
}
