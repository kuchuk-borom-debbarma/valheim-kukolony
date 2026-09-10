namespace Kukolony.Jobs.Work
{
    /// <summary>
    ///     Work that ends when the villager has the thing: choose a source, walk to it, take
    ///     from it, done.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The ordinary shape carries its load somewhere. This one does not, because the
    ///         villager's own bag is the destination - fetching a helmet is finished the moment
    ///         the helmet is in the bag.
    ///     </para>
    ///     <para>
    ///         One item per cycle, deliberately. A job's count already says how many times to
    ///         repeat, and a cycle that fetched everything at once would give a villager no
    ///         chance to be interrupted by more urgent work.
    ///     </para>
    ///     <para>
    ///         There is no "nothing left to fetch" state, because the question belongs to the
    ///         world rather than to the sequence: choosing a source finds nothing and the job
    ///         yields, which is the same answer any job gives when there is no work.
    ///     </para>
    /// </remarks>
    internal static class FetchOnly
    {
        internal static WorkStep Next(WorkState state, WorkFacts facts)
        {
            for (int guard = 0; guard < 8; guard++)
            {
                switch (state)
                {
                    case WorkState.Choosing:
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
                        // Somebody else took it while the villager walked over. Choose again
                        // rather than reaching for a target that is gone.
                        if (!facts.HasTarget) { state = WorkState.Choosing; continue; }
                        return new WorkStep(WorkAction.Collect, state, WorkState.Finished);

                    case WorkState.Finished:
                        return new WorkStep(WorkAction.Complete, state, WorkState.Choosing);

                    default:
                        // Any state from a longer shape - a reload after this job replaced a
                        // different one on the same villager - begins again rather than
                        // running a step this sequence has no meaning for.
                        state = WorkState.Choosing;
                        continue;
                }
            }
            return new WorkStep(WorkAction.Yield, state, WorkState.Choosing);
        }
    }
}
