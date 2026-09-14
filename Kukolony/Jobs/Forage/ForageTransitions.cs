namespace Kukolony.Jobs.Forage
{
    /// <summary>
    ///     Where a foraging villager has got to.
    /// </summary>
    /// <remarks>
    ///     Persisted as an int on the villager, so append rather than reorder - and note that
    ///     <see cref="Choosing" /> is deliberately zero, as it is in every work-state enum in
    ///     this mod. An unwritten field reads as zero, and so does the state left behind by a
    ///     villager that was doing some other job yesterday; landing either of them in "decide
    ///     what to do" is always safe.
    /// </remarks>
    internal enum ForageState
    {
        Choosing = 0,
        Approaching = 1,
        Picking = 2
    }

    /// <summary>What the engine should do about it.</summary>
    internal enum ForageAction
    {
        /// <summary>Nothing worth doing. Yield without consuming a repetition.</summary>
        Yield,

        /// <summary>Find something to pick.</summary>
        ChooseWork,

        /// <summary>Walk to it.</summary>
        MoveToTarget,

        /// <summary>Take what is on it.</summary>
        Pick,

        /// <summary>There is nothing left on it. That is what finishing looks like here.</summary>
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
    internal readonly struct ForageFacts
    {
        internal ForageFacts(bool hasTarget, bool ripe, bool atTarget, bool enough, bool tired)
        {
            HasTarget = hasTarget;
            Ripe = ripe;
            AtTarget = atTarget;
            Enough = enough;
            Tired = tired;
        }

        /// <summary>A thing this villager has settled on, and which still exists.</summary>
        internal bool HasTarget { get; }

        /// <summary>
        ///     There is something on it to take.
        /// </summary>
        /// <remarks>
        ///     Separate from <see cref="HasTarget" /> and that is the whole shape of this job. A
        ///     felled tree stops existing; a picked bush stands exactly where it was, looking the
        ///     same to everything that works by object identity, and grows its berries back an
        ///     hour later. So "is it there" and "is there anything on it" are two questions, and
        ///     a job that asked only the first would pick one bush and then mime at it for ever.
        /// </remarks>
        internal bool Ripe { get; }

        internal bool AtTarget { get; }

        /// <summary>The settlement already holds as much as this job was asked to gather.</summary>
        internal bool Enough { get; }

        internal bool Tired { get; }
    }

    /// <summary>One decision: what to do, and where to record having done it.</summary>
    internal readonly struct ForageStep
    {
        internal ForageStep(ForageAction action, ForageState next)
        {
            Action = action;
            Next = next;
        }

        internal ForageAction Action { get; }

        /// <summary>
        ///     Recorded only when the action succeeds, because walking and taking ownership both
        ///     span several ticks and the villager must arrive back at the same state until they
        ///     finish.
        /// </summary>
        internal ForageState Next { get; }
    }

    /// <summary>
    ///     The foraging state machine, as a pure function.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Chopping's shape without the tool.</b> Nothing is held, nothing is swung, and
    ///         there is no tier to be too weak for - which removes three of chopping's arms and
    ///         every one of mining's refusals. What is left is: find one, walk to it, take what
    ///         is on it.
    ///     </para>
    ///     <para>
    ///         <b>The one difference is that the target outlives the work.</b> Everywhere else in
    ///         this mod, finishing means the thing is gone: a felled tree, an emptied vein, a
    ///         delivered load. A picked bush is still a bush. So <see cref="ForageFacts.Ripe" />
    ///         rather than <see cref="ForageFacts.HasTarget" /> is what ends a trip, and the two
    ///         are never collapsed - a table that treated them as one would either never finish
    ///         or would report every bush as destroyed.
    ///     </para>
    ///     <para>
    ///         <b>Facts outrank the recorded state.</b> The state says where the villager got to;
    ///         the world says what is still true, and the world wins. A villager that reloads
    ///         halfway to a bush carries on; one whose bush the player picked goes back to
    ///         choosing rather than walking to something with nothing on it.
    ///     </para>
    /// </remarks>
    internal static class ForageTransitions
    {
        internal static ForageStep Next(ForageState state, ForageFacts facts)
        {
            for (int guard = 0; guard < 8; guard++)
            {
                switch (state)
                {
                    case ForageState.Choosing:
                        // Being tired and having enough are ordinary answers rather than
                        // failures: nothing useful can be done, so the villager yields to the
                        // next entry in its queue without spending a repetition on either.
                        if (facts.Tired) return new ForageStep(ForageAction.Yield, ForageState.Choosing);
                        if (facts.Enough) return new ForageStep(ForageAction.Yield, ForageState.Choosing);

                        // A target with nothing on it is not a target. Asked here as well as in
                        // the picking arm, because this is where a villager arrives holding a
                        // bush that was stripped while it was doing something else.
                        if (!facts.HasTarget || !facts.Ripe)
                        {
                            return new ForageStep(ForageAction.ChooseWork, ForageState.Approaching);
                        }

                        state = ForageState.Approaching;
                        continue;

                    case ForageState.Approaching:
                        if (!facts.HasTarget) { state = ForageState.Choosing; continue; }

                        // Stripped while this villager was walking to it - by the player, or by
                        // somebody who got there first. Going back to choosing rather than
                        // completing: nothing was picked, so nothing was finished.
                        if (!facts.Ripe) { state = ForageState.Choosing; continue; }

                        if (!facts.AtTarget) return new ForageStep(ForageAction.MoveToTarget, ForageState.Approaching);
                        state = ForageState.Picking;
                        continue;

                    case ForageState.Picking:
                        // Gone entirely - some pickable things are destroyed rather than emptied
                        // when they are taken. Either this villager finished it or somebody else
                        // did, and either way the trip is done rather than broken.
                        if (!facts.HasTarget) return new ForageStep(ForageAction.Complete, ForageState.Choosing);

                        // And the ordinary ending: it is still standing, and bare. This is the
                        // arm that runs on almost every successful pick.
                        if (!facts.Ripe) return new ForageStep(ForageAction.Complete, ForageState.Choosing);

                        // Checked between reaches, not only on arrival: a villager shoved away
                        // by something walks back rather than reaching at empty air.
                        if (!facts.AtTarget) return new ForageStep(ForageAction.MoveToTarget, ForageState.Picking);

                        return new ForageStep(ForageAction.Pick, ForageState.Picking);

                    default:
                        // A state this table does not know - a save from an older build, or a
                        // field that belonged to a different job. Start over rather than run a
                        // step that has no meaning here.
                        state = ForageState.Choosing;
                        continue;
                }
            }

            // Unreachable unless the table above gains a cycle. Yielding is the safe answer: it
            // consumes nothing and lets the next queue entry run.
            return new ForageStep(ForageAction.Yield, ForageState.Choosing);
        }
    }
}
