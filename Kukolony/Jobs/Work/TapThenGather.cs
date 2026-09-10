namespace Kukolony.Jobs.Work
{
    /// <summary>
    ///     The shape of work whose result lands on the ground: start it, wait, then pick up
    ///     what appeared and store it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Tapping a beehive is the only job with this shape today, and it is worth its own
    ///         transitions rather than a flag on the ordinary fetch: the honey is a separate
    ///         object that does not exist yet when the villager acts, so there are two journeys
    ///         in one cycle and the second cannot begin until the first has produced something.
    ///     </para>
    ///     <para>
    ///         The two journeys look alike, which is exactly why the states are named apart.
    ///         Collecting is tapping the hive; Retrieving is picking up what fell out. A job
    ///         reads <see cref="WorkContext.Phase"/> to tell them apart, so neither has to be
    ///         inferred from what the bag happens to hold.
    ///     </para>
    ///     <para>
    ///         Once something is in hand the cycle is an ordinary delivery, so the last three
    ///         states are handed to <see cref="FetchAndDeliver"/> rather than restated here.
    ///     </para>
    /// </remarks>
    internal static class TapThenGather
    {
        internal static WorkStep Next(WorkState state, WorkFacts facts)
        {
            for (int guard = 0; guard < 8; guard++)
            {
                switch (state)
                {
                    case WorkState.Choosing:
                        if (facts.StockLimitReached) return new WorkStep(WorkAction.Yield, state, WorkState.Choosing);
                        if (!facts.HasTool) return new WorkStep(WorkAction.Yield, state, WorkState.Choosing);
                        // Already holding the produce: whatever interrupted the cycle, the
                        // hive has been tapped and the honey picked up. Deliver it.
                        if (facts.Carrying) { state = WorkState.ChoosingTarget; continue; }
                        if (!facts.HasTarget) return new WorkStep(WorkAction.ChooseSource, state, WorkState.Travelling);
                        state = WorkState.Travelling;
                        continue;

                    case WorkState.Travelling:
                        if (!facts.HasTarget) { state = WorkState.Choosing; continue; }
                        if (!facts.Arrived) return new WorkStep(WorkAction.Move, state, WorkState.Travelling);
                        state = WorkState.Collecting;
                        continue;

                    case WorkState.Collecting:
                        if (facts.Carrying) { state = WorkState.ChoosingTarget; continue; }
                        if (!facts.HasTarget) { state = WorkState.Choosing; continue; }
                        return new WorkStep(WorkAction.Collect, state, WorkState.Waiting);

                    case WorkState.Waiting:
                        return new WorkStep(WorkAction.Wait, state, WorkState.Gathering);

                    case WorkState.Gathering:
                        if (facts.Carrying) { state = WorkState.ChoosingTarget; continue; }
                        if (!facts.HasTarget) return new WorkStep(WorkAction.ChooseSource, state, WorkState.Retrieving);
                        state = WorkState.Retrieving;
                        continue;

                    case WorkState.Retrieving:
                        // What appeared can be taken by somebody else, or despawn. Go back to
                        // choosing rather than to tapping: the hive has already been tapped.
                        if (!facts.HasTarget) { state = WorkState.Gathering; continue; }
                        if (!facts.Arrived) return new WorkStep(WorkAction.Move, state, WorkState.Retrieving);
                        return new WorkStep(WorkAction.Collect, state, WorkState.ChoosingTarget);

                    default:
                        return FetchAndDeliver.Next(state, facts);
                }
            }
            return new WorkStep(WorkAction.Yield, state, WorkState.Choosing);
        }
    }
}
