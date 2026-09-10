namespace Kukolony.Jobs.Work
{
    /// <summary>
    ///     Work that keeps hitting the same thing until it is no longer there.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Gathering is not fetch-and-carry. A tree takes many blows and produces nothing
    ///         until the last one, so the cycle stays on one target rather than choosing a new
    ///         one each time round, and it ends when the target is gone rather than when
    ///         something has been delivered.
    ///     </para>
    ///     <para>
    ///         The target going away is success here, not failure - which is why the executor
    ///         releases it on the blow that lands, and this reads the empty hand next tick as
    ///         a finished cycle. A job that treated a missing target as a fault would report
    ///         every felled tree as an error.
    ///     </para>
    /// </remarks>
    internal static class WorkUntilGone
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
                        if (!facts.HasTarget) return new WorkStep(WorkAction.ChooseSource, state, WorkState.Travelling);
                        state = WorkState.Travelling;
                        continue;

                    case WorkState.Travelling:
                        if (!facts.HasTarget) { state = WorkState.Choosing; continue; }
                        if (!facts.Arrived) return new WorkStep(WorkAction.Move, state, WorkState.Travelling);
                        state = WorkState.Collecting;
                        continue;

                    case WorkState.Collecting:
                        // Nothing to hit: either this cycle finished it off, or somebody else
                        // did. Either way the cycle is done and the next one chooses afresh.
                        if (!facts.HasTarget) return new WorkStep(WorkAction.Complete, state, WorkState.Choosing);
                        // Staying put between blows: a villager pushed away from a tree walks
                        // back rather than swinging at nothing.
                        if (!facts.Arrived) return new WorkStep(WorkAction.Move, state, WorkState.Collecting);
                        return new WorkStep(WorkAction.Collect, state, WorkState.Collecting);

                    default:
                        // Any state from a different shape - a reload after this job replaced
                        // another on the same villager - begins again.
                        state = WorkState.Choosing;
                        continue;
                }
            }
            return new WorkStep(WorkAction.Yield, state, WorkState.Choosing);
        }
    }
}
