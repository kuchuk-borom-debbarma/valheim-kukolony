using Kukolony.Core;
using Kukolony.Villagers;

namespace Kukolony.Jobs
{
    /// <summary>
    ///     Drives a villager through a job's steps.
    ///
    ///     The step index lives on the villager's ZDO, not here. That is what lets a
    ///     villager whose ownership moves to another player carry on from where it was
    ///     instead of restarting - the new owner reads the same index.
    /// </summary>
    internal sealed class JobRunner
    {
        /// <summary>Pause after a failed cycle, so a villager that cannot work does not spin.</summary>
        private const float RetryCooldownSeconds = 3f;

        private float _cooldown;
        private string _lastReportedStep;

        /// <summary>
        ///     Runs one step and reports what the villager is doing, in words - so the
        ///     log and the hover text cannot drift apart.
        /// </summary>
        internal string Tick(Job job, JobContext context)
        {
            if (_cooldown > 0f)
            {
                _cooldown -= context.DeltaTime;

                // No countdown in the label. A value that changes every tick makes every
                // tick look like a new activity, which floods the transition log - the
                // same trap that "walking home (34m)" fell into.
                return "waiting to retry";
            }

            VillagerState state = context.Villager.State;
            int index = state.StepIndex;
            if (index < 0 || index >= job.StepCount)
            {
                index = 0;
                state.SetStepIndex(0);
            }

            IJobStep step = job.Steps[index];
            string description = Describe(step, context);
            Report(context, step, index, description);

            switch (step.Tick(context))
            {
                case StepStatus.Running:
                    break;

                case StepStatus.Succeeded:
                    state.SetStepIndex((index + 1) % job.StepCount);
                    break;

                case StepStatus.Failed:
                    // Back to the top of the cycle. Anything carried stays in the bag and
                    // gets deposited next time round rather than being dropped.
                    state.SetStepIndex(0);
                    state.SetStepTarget(ZDOID.None);
                    _cooldown = RetryCooldownSeconds;
                    break;
            }

            return description;
        }

        /// <summary>
        ///     A step describing itself must never break the villager, so a throwing
        ///     Describe degrades to the step's machine name rather than killing the tick.
        /// </summary>
        private static string Describe(IJobStep step, JobContext context)
        {
            try
            {
                return step.Describe(context);
            }
            catch (System.Exception)
            {
                return step.Name;
            }
        }

        /// <summary>Logs on step change only - per-tick logging would flood at 20Hz.</summary>
        private void Report(JobContext context, IJobStep step, int index, string description)
        {
            string label = $"{index}:{step.Name}";
            if (_lastReportedStep == label)
            {
                return;
            }

            _lastReportedStep = label;
            Log.Info($"Villager '{context.Villager.State.Name}' -> {label} ({description})");
        }
    }
}
