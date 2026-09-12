namespace Kukolony.Jobs.Chop
{
    /// <summary>
    ///     Where a chopping villager has got to.
    /// </summary>
    /// <remarks>
    ///     Persisted as an int on the villager, so append rather than reorder — and note that
    ///     <see cref="Choosing" /> is deliberately zero, as it is in every work-state enum in
    ///     this mod. An unwritten field reads as zero, and so does the state left behind by a
    ///     villager that was doing some other job yesterday; landing either of them in "decide
    ///     what to do" is always safe.
    /// </remarks>
    internal enum ChopState
    {
        Choosing = 0,
        Approaching = 1,
        Chopping = 2
    }

    /// <summary>What the engine should do about it.</summary>
    internal enum ChopAction
    {
        /// <summary>Nothing worth doing. Yield without consuming a repetition.</summary>
        Yield,

        /// <summary>Find something to chop.</summary>
        ChooseWork,

        /// <summary>Walk to it.</summary>
        MoveToTarget,

        /// <summary>Hit it.</summary>
        Chop,

        /// <summary>It is gone. That is what finishing looks like here.</summary>
        Complete
    }

    /// <summary>
    ///     What is true right now, gathered by the engine before it asks.
    /// </summary>
    /// <remarks>
    ///     Plain data with no Unity and no colony types, so the table compiles into the
    ///     Unity-free test project and every branch is checked in about a second instead of
    ///     only inside a four-minute game run.
    /// </remarks>
    internal readonly struct ChopFacts
    {
        internal ChopFacts(bool hasTool, bool hasTarget, bool atTarget, bool enough, bool tired)
        {
            HasTool = hasTool;
            HasTarget = hasTarget;
            AtTarget = atTarget;
            Enough = enough;
            Tired = tired;
        }

        /// <summary>An axe in the bag. Without one this job does not start.</summary>
        internal bool HasTool { get; }

        internal bool HasTarget { get; }

        internal bool AtTarget { get; }

        /// <summary>The settlement already holds as much as this job was asked to gather.</summary>
        internal bool Enough { get; }

        internal bool Tired { get; }
    }

    /// <summary>One decision: what to do, and where to record having done it.</summary>
    internal readonly struct ChopStep
    {
        internal ChopStep(ChopAction action, ChopState next)
        {
            Action = action;
            Next = next;
        }

        internal ChopAction Action { get; }

        /// <summary>
        ///     Recorded only when the action succeeds, because walking and taking ownership
        ///     both span several ticks and the villager must arrive back at the same state
        ///     until they finish.
        /// </summary>
        internal ChopState Next { get; }
    }

    /// <summary>
    ///     The chopping state machine, as a pure function.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>This is not fetch-and-deliver, and the difference is not cosmetic.</b> Hauling
    ///         chooses a thing, carries it somewhere, and is finished when it arrives. Chopping
    ///         stays on one target through many blows, produces nothing until the last of them,
    ///         and is finished when <em>the target stops existing</em>. A job built on the
    ///         hauling shape would report every felled tree as a failure.
    ///     </para>
    ///     <para>
    ///         <b>Nothing here orders logs before standing trees, and nothing needs to.</b> A
    ///         felled tree leaves its log where the villager is already standing, so choosing
    ///         the nearest thing picks it up next by itself. The same accident handles sub-logs
    ///         and stumps. The predecessor spent a rule on this; the loop gives it away.
    ///     </para>
    ///     <para>
    ///         <b>Facts outrank the recorded state.</b> The state says where the villager got
    ///         to; the world says what is still true, and the world wins. A villager that
    ///         reloads mid-tree carries on; one whose tree the player felled goes back to
    ///         choosing rather than obeying a target that is no longer there.
    ///     </para>
    /// </remarks>
    internal static class ChopTransitions
    {
        internal static ChopStep Next(ChopState state, ChopFacts facts)
        {
            for (int guard = 0; guard < 8; guard++)
            {
                switch (state)
                {
                    case ChopState.Choosing:
                        // Being tired, having enough, and having no axe are all ordinary
                        // answers rather than failures: nothing useful can be done, so the
                        // villager yields to the next entry in its queue without spending a
                        // repetition on any of them.
                        if (facts.Tired) return new ChopStep(ChopAction.Yield, ChopState.Choosing);
                        if (facts.Enough) return new ChopStep(ChopAction.Yield, ChopState.Choosing);
                        if (!facts.HasTool) return new ChopStep(ChopAction.Yield, ChopState.Choosing);

                        if (!facts.HasTarget) return new ChopStep(ChopAction.ChooseWork, ChopState.Approaching);
                        state = ChopState.Approaching;
                        continue;

                    case ChopState.Approaching:
                        if (!facts.HasTarget) { state = ChopState.Choosing; continue; }
                        if (!facts.AtTarget) return new ChopStep(ChopAction.MoveToTarget, ChopState.Approaching);
                        state = ChopState.Chopping;
                        continue;

                    case ChopState.Chopping:
                        // Nothing left to hit. Either this villager finished it or somebody
                        // else did, and either way the trip is done rather than broken.
                        if (!facts.HasTarget) return new ChopStep(ChopAction.Complete, ChopState.Choosing);

                        // The axe went. Swinging an empty hand at a tree forever is the kind
                        // of failure that looks exactly like working, so go back and be told
                        // there is nothing to do.
                        if (!facts.HasTool) { state = ChopState.Choosing; continue; }

                        // Checked between blows, not only on arrival: a villager shoved away
                        // from the trunk by a falling log walks back rather than swinging at
                        // the air where a tree used to be.
                        if (!facts.AtTarget) return new ChopStep(ChopAction.MoveToTarget, ChopState.Chopping);

                        return new ChopStep(ChopAction.Chop, ChopState.Chopping);

                    default:
                        // A state this table does not know - a save from an older build, or a
                        // field that belonged to a different job. Start over rather than run a
                        // step that has no meaning here.
                        state = ChopState.Choosing;
                        continue;
                }
            }

            // Unreachable unless the table above gains a cycle. Yielding is the safe answer:
            // it consumes nothing and lets the next queue entry run.
            return new ChopStep(ChopAction.Yield, ChopState.Choosing);
        }
    }
}
