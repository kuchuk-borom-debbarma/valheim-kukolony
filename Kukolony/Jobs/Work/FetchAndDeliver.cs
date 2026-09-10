namespace Kukolony.Jobs.Work
{
    /// <summary>
    ///     The shape almost every colony job has: choose something, walk to it, take from it,
    ///     choose somewhere to put the result, walk there, put it down.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Hauling, transferring and feeding a station are the same journey with different
    ///         ends, so they share this rather than each restating it. Jobs whose shape genuinely
    ///         differs — collecting from a hive waits for something to appear, chopping repeats
    ///         until the tree falls — bring their own transitions instead of bending this one.
    ///     </para>
    ///     <para>
    ///         Pure by design: an enum, five booleans, no Unity and no colony types. That is what
    ///         lets a whole work cycle be verified in about a second rather than only inside a
    ///         four-minute game run.
    ///     </para>
    ///     <para>
    ///         <b>Facts outrank the recorded state.</b> A villager that reloads holding something
    ///         skips straight to delivering it, and one whose target was taken by somebody else
    ///         goes back to choosing. The state says where it got to; the world says what is
    ///         still true, and the world wins.
    ///     </para>
    /// </remarks>
    internal static class FetchAndDeliver
    {
        internal static WorkStep Next(WorkState state, WorkFacts facts)
        {
            // Bounded because every arm either returns or moves to a different state; the
            // guard is here so a future arm that forgets cannot spin a villager.
            for (int guard = 0; guard < 8; guard++)
            {
                switch (state)
                {
                    case WorkState.Choosing:
                        if (facts.StockLimitReached) return new WorkStep(WorkAction.Yield, state, WorkState.Choosing);
                        if (!facts.HasTool) return new WorkStep(WorkAction.Yield, state, WorkState.Choosing);
                        // Already holding something: the fetch half is done, wherever it got
                        // interrupted. Going back for more would strand what it carries.
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
                        return new WorkStep(WorkAction.Collect, state, WorkState.ChoosingTarget);

                    case WorkState.ChoosingTarget:
                        // Nothing in hand by this point means the fetch found nothing worth
                        // carrying, so the cycle is over rather than stuck.
                        if (!facts.Carrying) return new WorkStep(WorkAction.Complete, state, WorkState.Choosing);
                        if (!facts.HasTarget) return new WorkStep(WorkAction.ChooseTarget, state, WorkState.Delivering);
                        state = WorkState.Delivering;
                        continue;

                    case WorkState.Delivering:
                        if (!facts.HasTarget) { state = WorkState.ChoosingTarget; continue; }
                        if (!facts.Arrived) return new WorkStep(WorkAction.Move, state, WorkState.Delivering);
                        return new WorkStep(WorkAction.Deliver, state, WorkState.Finished);

                    case WorkState.Finished:
                        return new WorkStep(WorkAction.Complete, state, WorkState.Choosing);

                    default:
                        return new WorkStep(WorkAction.Yield, state, WorkState.Choosing);
                }
            }
            return new WorkStep(WorkAction.Yield, state, WorkState.Choosing);
        }
    }
}
