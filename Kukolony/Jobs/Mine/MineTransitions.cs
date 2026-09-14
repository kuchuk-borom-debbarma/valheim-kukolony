namespace Kukolony.Jobs.Mine
{
    /// <summary>Where a mining villager has got to.</summary>
    /// <remarks>
    ///     Persisted as an int on the villager, so append rather than reorder - and the three
    ///     deliberately share their numbers with <see cref="Chop.ChopState" />, because the legs
    ///     mean the same thing. A villager whose state was written by a chopping job yesterday
    ///     resumes somewhere coherent rather than somewhere arbitrary.
    /// </remarks>
    internal enum MineState
    {
        Choosing = 0,
        Approaching = 1,
        Mining = 2
    }

    /// <summary>What the engine should do about it.</summary>
    internal enum MineAction
    {
        /// <summary>Nothing worth doing. Yield without consuming a repetition.</summary>
        Yield,

        /// <summary>Pick a deposit.</summary>
        ChooseWork,

        /// <summary>Walk to the part of it being worked.</summary>
        MoveToArea,

        /// <summary>Swing.</summary>
        Strike,

        /// <summary>The deposit is finished.</summary>
        Complete
    }

    /// <summary>
    ///     What is true right now, gathered by the engine before it asks.
    /// </summary>
    /// <remarks>
    ///     Plain data with no Unity and no colony types, so the table compiles into the
    ///     Unity-free test project and every branch is checked in about a second instead of only
    ///     inside a four-minute game run.
    /// </remarks>
    internal readonly struct MineFacts
    {
        internal MineFacts(bool hasTool, bool hasDeposit, bool hasArea, bool atArea, bool enough,
            bool tired)
        {
            HasTool = hasTool;
            HasDeposit = hasDeposit;
            HasArea = hasArea;
            AtArea = atArea;
            Enough = enough;
            Tired = tired;
        }

        /// <summary>A pickaxe in the bag. Without one this job does not start.</summary>
        internal bool HasTool { get; }

        /// <summary>A deposit chosen and claimed.</summary>
        internal bool HasDeposit { get; }

        /// <summary>
        ///     Some part of it is still standing.
        /// </summary>
        /// <remarks>
        ///     Separate from <see cref="HasDeposit" />, and that separation is the whole
        ///     difference from chopping. A tree that is still there has something to hit; a vein
        ///     that is still there may have had its last part collapse a moment ago, and the
        ///     object survives until every one is gone.
        /// </remarks>
        internal bool HasArea { get; }

        internal bool AtArea { get; }

        /// <summary>The settlement already holds as much as this job was asked to gather.</summary>
        internal bool Enough { get; }

        internal bool Tired { get; }
    }

    /// <summary>One decision: what to do, and where to record having done it.</summary>
    internal readonly struct MineStep
    {
        internal MineStep(MineAction action, MineState next)
        {
            Action = action;
            Next = next;
        }

        internal MineAction Action { get; }

        /// <summary>
        ///     Recorded only when the action succeeds, because walking and taking ownership both
        ///     span several ticks and the villager must arrive back at the same state until they
        ///     finish.
        /// </summary>
        internal MineState Next { get; }
    }

    /// <summary>
    ///     The mining state machine, as a pure function.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Chopping's shape, with a part inside the target.</b> Both stay on one thing
    ///         through many blows and finish when it stops existing. The difference is that a
    ///         deposit stops existing part by part, so every leg asks about the part as well as
    ///         about the deposit - including the walk, because the part being worked moves as
    ///         the near ones fall and the villager follows it round the rock.
    ///     </para>
    ///     <para>
    ///         <b>A deposit with no parts left is finished, not broken.</b> The object outlives
    ///         its last part by a frame or two, and a table that only asked whether the deposit
    ///         existed would send a villager to swing at a rock that is entirely gone.
    ///     </para>
    ///     <para>
    ///         <b>Facts outrank the recorded state.</b> The state says where the villager got to;
    ///         the world says what is still true, and the world wins. Mining collapses, so this
    ///         is not a nicety: parts fall that nobody struck, and a villager holding an opinion
    ///         about which rock it was working would be wrong constantly.
    ///     </para>
    /// </remarks>
    internal static class MineTransitions
    {
        internal static MineStep Next(MineState state, MineFacts facts)
        {
            for (int guard = 0; guard < 8; guard++)
            {
                switch (state)
                {
                    case MineState.Choosing:
                        // Being tired, having enough, and having no pickaxe are all ordinary
                        // answers rather than failures: nothing useful can be done, so the
                        // villager yields to the next entry in its queue without spending a
                        // repetition on any of them.
                        if (facts.Tired) return new MineStep(MineAction.Yield, MineState.Choosing);
                        if (facts.Enough) return new MineStep(MineAction.Yield, MineState.Choosing);
                        if (!facts.HasTool) return new MineStep(MineAction.Yield, MineState.Choosing);

                        if (!facts.HasDeposit) return new MineStep(MineAction.ChooseWork, MineState.Approaching);

                        // Held a deposit with nothing left on it - the last part collapsed while
                        // this was being decided. Choosing again costs nothing and the walk has
                        // not started.
                        if (!facts.HasArea) return new MineStep(MineAction.ChooseWork, MineState.Approaching);

                        state = MineState.Approaching;
                        continue;

                    case MineState.Approaching:
                        if (!facts.HasDeposit) { state = MineState.Choosing; continue; }

                        // Mined out from under it, by this villager's own last blow bringing the
                        // rest down or by somebody else. The trip is over rather than broken.
                        if (!facts.HasArea) return new MineStep(MineAction.Complete, MineState.Choosing);

                        if (!facts.AtArea) return new MineStep(MineAction.MoveToArea, MineState.Approaching);

                        state = MineState.Mining;
                        continue;

                    case MineState.Mining:
                        // Nothing left to hit. Either this villager finished it or somebody else
                        // did, and either way the trip is done rather than broken.
                        if (!facts.HasDeposit) return new MineStep(MineAction.Complete, MineState.Choosing);
                        if (!facts.HasArea) return new MineStep(MineAction.Complete, MineState.Choosing);

                        // The pickaxe went. Swinging an empty hand at a rock forever is the kind
                        // of failure that looks exactly like working, so go back and be told
                        // there is nothing to do.
                        if (!facts.HasTool) { state = MineState.Choosing; continue; }

                        // Checked between blows, not only on arrival. The part being worked is
                        // re-chosen every tick from what is standing, so finishing the rock in
                        // front of the villager moves the work round the vein - and the villager
                        // walks the few steps rather than swinging at where a rock used to be.
                        if (!facts.AtArea) return new MineStep(MineAction.MoveToArea, MineState.Mining);

                        return new MineStep(MineAction.Strike, MineState.Mining);

                    default:
                        // A state this table does not know - a save from an older build, or a
                        // field that belonged to a different job. Start over rather than run a
                        // step that has no meaning here.
                        state = MineState.Choosing;
                        continue;
                }
            }

            // Unreachable unless the table above gains a cycle. Yielding is the safe answer: it
            // consumes nothing and lets the next queue entry run.
            return new MineStep(MineAction.Yield, MineState.Choosing);
        }
    }
}
