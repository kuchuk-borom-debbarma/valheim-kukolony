namespace Kukolony.Jobs.Haul
{
    /// <summary>
    ///     Where a hauling villager has got to.
    /// </summary>
    /// <remarks>
    ///     Persisted as an int on the villager, so append rather than reorder — and note that
    ///     <see cref="Choosing" /> is deliberately zero. An unwritten field reads as zero, and a
    ///     villager that resumes into "decide what to do" is always safe, whatever went wrong
    ///     before. That is what lets new state be added without migrating a single save.
    /// </remarks>
    internal enum HaulState
    {
        Choosing = 0,
        Fetching = 1,
        Collecting = 2,
        Delivering = 3,
        Depositing = 4
    }

    /// <summary>What the engine should do about it.</summary>
    internal enum HaulAction
    {
        /// <summary>Nothing worth doing. Yield without consuming a repetition.</summary>
        Yield,

        /// <summary>Pick a destination and the items bound for it.</summary>
        ChooseWork,

        /// <summary>Walk to the thing being collected.</summary>
        MoveToSource,

        /// <summary>Take what is there.</summary>
        Collect,

        /// <summary>Walk to the destination.</summary>
        MoveToDestination,

        /// <summary>Put the load down.</summary>
        Deposit,

        /// <summary>A full trip finished.</summary>
        Complete
    }

    /// <summary>
    ///     What is true right now, gathered by the engine before it asks.
    /// </summary>
    /// <remarks>
    ///     Deliberately plain data with no Unity or colony types, so the decision table can be
    ///     compiled into the Unity-free test project and a whole work cycle verified in about a
    ///     second instead of only inside a four-minute game run.
    /// </remarks>
    internal readonly struct HaulFacts
    {
        internal HaulFacts(bool hasSource, bool hasDestination, bool atSource,
            bool atDestination, bool carrying, bool bagFull, bool tired, bool fillBagFirst = false)
        {
            HasSource = hasSource;
            HasDestination = hasDestination;
            AtSource = atSource;
            AtDestination = atDestination;
            Carrying = carrying;
            BagFull = bagFull;
            Tired = tired;
            FillBagFirst = fillBagFirst;
        }

        internal bool HasSource { get; }
        internal bool HasDestination { get; }
        internal bool AtSource { get; }
        internal bool AtDestination { get; }
        internal bool Carrying { get; }
        internal bool BagFull { get; }
        internal bool Tired { get; }

        /// <summary>
        ///     Whether the job wants a full load before setting out.
        /// </summary>
        /// <remarks>
        ///     The job has offered this on its screen since it was written and nothing read it,
        ///     so every villager delivered after a single item however it was set. A job must
        ///     not offer a setting it ignores.
        /// </remarks>
        internal bool FillBagFirst { get; }
    }

    /// <summary>One decision: what to do, and where to record having done it.</summary>
    internal readonly struct HaulStep
    {
        internal HaulStep(HaulAction action, HaulState next)
        {
            Action = action;
            Next = next;
        }

        internal HaulAction Action { get; }

        /// <summary>
        ///     Recorded only when the action succeeds, because walking and taking ownership both
        ///     span several ticks and the villager must arrive back at the same state until they
        ///     finish.
        /// </summary>
        internal HaulState Next { get; }
    }

    /// <summary>
    ///     The hauling state machine, as a pure function.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Facts outrank the recorded state.</b> The state says where the villager got to;
    ///         the world says what is still true, and the world wins. A villager that reloads
    ///         holding something goes straight to delivering it; one whose item was taken by the
    ///         player goes back to choosing. That single rule is what makes reloads, ownership
    ///         transfers and stolen targets repair themselves without a line of special-casing.
    ///     </para>
    ///     <para>
    ///         States whose outcome already holds are skipped through within one call, so the
    ///         loop below <c>continue</c>s rather than returning a no-op. The iteration guard is
    ///         not decoration: a transition table that can cycle would hang the villager, and an
    ///         unbounded loop inside a fixed tick hangs the game.
    ///     </para>
    /// </remarks>
    internal static class HaulTransitions
    {
        internal static HaulStep Next(HaulState state, HaulFacts facts)
        {
            for (int guard = 0; guard < 8; guard++)
            {
                switch (state)
                {
                    case HaulState.Choosing:
                        // Being tired is not a failure, and neither is having nothing to do.
                        if (facts.Tired) return new HaulStep(HaulAction.Yield, HaulState.Choosing);

                        // Already carrying means the fetch half is done, wherever it was
                        // interrupted. Going back for more would strand the load.
                        if (facts.Carrying) { state = HaulState.Delivering; continue; }

                        if (!facts.HasSource) return new HaulStep(HaulAction.ChooseWork, HaulState.Fetching);
                        state = HaulState.Fetching;
                        continue;

                    case HaulState.Fetching:
                        if (!facts.HasSource) { state = HaulState.Choosing; continue; }
                        if (!facts.AtSource) return new HaulStep(HaulAction.MoveToSource, HaulState.Fetching);
                        state = HaulState.Collecting;
                        continue;

                    case HaulState.Collecting:
                        // A full bag ends the collecting half even mid-sweep; what is carried
                        // must be delivered before anything else is picked up.
                        if (facts.BagFull) { state = HaulState.Delivering; continue; }

                        if (!facts.HasSource)
                        {
                            // Taking one thing clears the source, which is what ends a sweep.
                            // With a full load wanted, look for another item bound for the same
                            // chest before walking; the engine answers by finding one or not,
                            // and finding none simply falls through to delivering on the next
                            // pass. The bag filling is the other way out, so this cannot spin.
                            if (facts.Carrying && facts.FillBagFirst)
                                return new HaulStep(HaulAction.ChooseWork, HaulState.Fetching);

                            state = facts.Carrying ? HaulState.Delivering : HaulState.Choosing;
                            continue;
                        }

                        return new HaulStep(HaulAction.Collect, HaulState.Collecting);

                    case HaulState.Delivering:
                        // Nothing in hand by now means the sweep found nothing worth carrying,
                        // so the trip is over rather than stuck.
                        if (!facts.Carrying) return new HaulStep(HaulAction.Complete, HaulState.Choosing);
                        if (!facts.HasDestination) return new HaulStep(HaulAction.ChooseWork, HaulState.Delivering);
                        if (!facts.AtDestination) return new HaulStep(HaulAction.MoveToDestination, HaulState.Delivering);
                        state = HaulState.Depositing;
                        continue;

                    case HaulState.Depositing:
                        if (!facts.Carrying) return new HaulStep(HaulAction.Complete, HaulState.Choosing);
                        if (!facts.HasDestination) { state = HaulState.Delivering; continue; }
                        return new HaulStep(HaulAction.Deposit, HaulState.Depositing);

                    default:
                        // A state this table does not know - a save from an older build, or a
                        // field that belonged to a different job. Start over rather than run a
                        // step that has no meaning here.
                        state = HaulState.Choosing;
                        continue;
                }
            }

            // Unreachable unless the table above gains a cycle. Yielding is the safe answer:
            // it consumes nothing and lets the next queue entry run.
            return new HaulStep(HaulAction.Yield, HaulState.Choosing);
        }
    }
}
