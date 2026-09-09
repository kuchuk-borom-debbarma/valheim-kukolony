using System.Collections.Generic;
using Kukolony.Villagers;

namespace Kukolony.Jobs
{
    /// <summary>Persistent queue semantics shared by all concrete executors.</summary>
    internal static class QueueRunner
    {
        internal static string Current(VillagerState state, List<ColonyJobConfig> jobs)
        {
            List<string> queue = state.GetQueue();
            if (queue.Count == 0) return string.Empty;
            int index = state.QueuePosition % queue.Count;
            if (index < 0) index = 0;
            return queue[index];
        }

        internal static void Apply(VillagerState state, List<ColonyJobConfig> jobs, JobResult result)
        {
            List<string> queue = state.GetQueue();
            if (queue.Count == 0 || result == JobResult.Running || result == JobResult.Skipped) return;
            int index = state.QueuePosition % queue.Count;
            if (index < 0) index = 0;
            ColonyJobConfig job = jobs.Find(j => j.Id == queue[index]);
            int attempt = state.QueueAttempt + 1;
            state.SetQueueAttempt(attempt);
            if (job == null || attempt >= System.Math.Max(1, job.Count))
            {
                state.SetQueuePosition((index + 1) % queue.Count);
                state.SetQueueAttempt(0);
                state.SetQueueProgress(0);
                state.SetStepTarget(ZDOID.None);
            }
        }
    }
}
