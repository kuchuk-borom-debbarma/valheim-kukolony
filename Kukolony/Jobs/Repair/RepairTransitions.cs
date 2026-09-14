namespace Kukolony.Jobs.Repair
{
    /// <summary>
    ///     Where a mending villager has got to.
    /// </summary>
    /// <remarks>
    ///     Persisted as an int on the villager, so append rather than reorder - and note that
    ///     <see cref="Choosing" /> is deliberately zero, as it is in every work-state enum in this
    ///     mod. An unwritten field reads as zero, and so does the state left behind by a villager
    ///     that was doing some other job yesterday; landing either of them in "decide what to do"
    ///     is always safe.
    /// </remarks>
    internal enum RepairState
    {
        Choosing = 0,
        Approaching = 1,
        Mending = 2
    }

    /// <summary>What the engine should do about it.</summary>
    internal enum RepairAction
    {
        /// <summary>Nothing worth doing. Yield without consuming a repetition.</summary>
        Yield,

        /// <summary>Find something that needs mending.</summary>
        ChooseWork,

        /// <summary>Walk to it.</summary>
        MoveToTarget,

        /// <summary>Mend it, and everything damaged within reach of where this puts us.</summary>
        Mend,

        /// <summary>Nothing left damaged here. That is what finishing looks like.</summary>
        Complete
    }

    /// <summary>
    ///     What is true right now, gathered by the engine before it asks.
    /// </summary>
    /// <remarks>
    ///     Plain data with no Unity and no colony types, so the table compiles into the Unity-free
    ///     test project and every branch is checked in about a second instead of only inside a
    ///     four-minute game run.
    /// </remarks>
    internal readonly struct RepairFacts
    {
        internal RepairFacts(bool hasTool, bool hasTarget, bool damaged, bool atTarget,
            bool inStationRange, bool tired)
        {
            HasTool = hasTool;
            HasTarget = hasTarget;
            Damaged = damaged;
            AtTarget = atTarget;
            InStationRange = inStationRange;
            Tired = tired;
        }

        /// <summary>A hammer in the bag. Without one this job does not start.</summary>
        internal bool HasTool { get; }

        /// <summary>A piece this villager has settled on, which still exists.</summary>
        internal bool HasTarget { get; }

        /// <summary>
        ///     It is still below the threshold this job was given.
        /// </summary>
        /// <remarks>
        ///     Separate from <see cref="HasTarget" />, for the reason foraging keeps ripeness
        ///     separate: a mended wall is still a wall, standing where it was and looking the same
        ///     to anything that works by object identity. Collapsing the two would leave a
        ///     villager hammering a whole roof for ever, or reporting every repair as a demolition.
        /// </remarks>
        internal bool Damaged { get; }

        internal bool AtTarget { get; }

        /// <summary>
        ///     A crafting station of the kind this piece needs stands close enough to it.
        /// </summary>
        /// <remarks>
        ///     The rule that binds the player binds the villager. Asked before the walk, as
        ///     mining asks about tool tier and farming about biome, because both halves are
        ///     knowable from where the villager is standing - and the alternative is a walk across
        ///     the settlement to be refused on arrival, every time, for ever.
        /// </remarks>
        internal bool InStationRange { get; }

        internal bool Tired { get; }
    }

    /// <summary>One decision: what to do, and where to record having done it.</summary>
    internal readonly struct RepairStep
    {
        internal RepairStep(RepairAction action, RepairState next)
        {
            Action = action;
            Next = next;
        }

        internal RepairAction Action { get; }

        /// <summary>
        ///     Recorded only when the action succeeds, because walking spans several ticks and the
        ///     villager must arrive back at the same state until it finishes.
        /// </summary>
        internal RepairState Next { get; }
    }

    /// <summary>
    ///     The mending state machine, as a pure function.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Chopping's shape, with the target outliving the work.</b> A felled tree is gone;
    ///         a mended wall is still a wall. So <see cref="RepairFacts.Damaged" /> rather than
    ///         <see cref="RepairFacts.HasTarget" /> is what ends a trip - the same distinction
    ///         foraging draws, and the same one a job that collapsed them would fail at silently.
    ///     </para>
    ///     <para>
    ///         <b>This job cannot run out of world.</b> Every other producing job needs a stopping
    ///         rule because a forest or a vein will keep offering work until it is stripped.
    ///         Nothing here is consumed and nothing is produced: when there is no damage there is
    ///         no work, which is a terminus the world provides for free.
    ///     </para>
    ///     <para>
    ///         <b>Facts outrank the recorded state.</b> The state says where the villager got to;
    ///         the world says what is still true, and the world wins. A villager that reloads
    ///         halfway to a wall carries on; one whose wall the player repaired goes back to
    ///         choosing rather than swinging at something already whole.
    ///     </para>
    /// </remarks>
    internal static class RepairTransitions
    {
        internal static RepairStep Next(RepairState state, RepairFacts facts)
        {
            for (int guard = 0; guard < 8; guard++)
            {
                switch (state)
                {
                    case RepairState.Choosing:
                        // Being tired and having no hammer are ordinary answers rather than
                        // failures: nothing useful can be done, so the villager yields to the
                        // next entry in its queue without spending a repetition on either.
                        if (facts.Tired) return new RepairStep(RepairAction.Yield, RepairState.Choosing);
                        if (!facts.HasTool) return new RepairStep(RepairAction.Yield, RepairState.Choosing);

                        // A target that is whole, gone, or out of reach of the station it needs
                        // is not a target. Asked here as well as below because this is where a
                        // villager arrives holding something the player mended in the meantime.
                        if (!facts.HasTarget || !facts.Damaged || !facts.InStationRange)
                        {
                            return new RepairStep(RepairAction.ChooseWork, RepairState.Approaching);
                        }

                        state = RepairState.Approaching;
                        continue;

                    case RepairState.Approaching:
                        if (!facts.HasTarget) { state = RepairState.Choosing; continue; }

                        // Mended while this villager was walking - by the player, or by somebody
                        // who got there first. Back to choosing rather than completing: nothing
                        // was mended, so nothing was finished.
                        if (!facts.Damaged) { state = RepairState.Choosing; continue; }

                        // The bench it depended on came down while the walk was happening.
                        if (!facts.InStationRange) { state = RepairState.Choosing; continue; }

                        if (!facts.AtTarget) return new RepairStep(RepairAction.MoveToTarget, RepairState.Approaching);
                        state = RepairState.Mending;
                        continue;

                    case RepairState.Mending:
                        // Taken down entirely, by a troll or by the player. Either way the trip
                        // is done rather than broken.
                        if (!facts.HasTarget) return new RepairStep(RepairAction.Complete, RepairState.Choosing);

                        // And the ordinary ending: it is whole again. This is the arm that runs
                        // on almost every successful trip.
                        if (!facts.Damaged) return new RepairStep(RepairAction.Complete, RepairState.Choosing);

                        // The hammer went. Miming at a wall for ever is the kind of failure that
                        // looks exactly like working, so go back and be told there is nothing to
                        // do.
                        if (!facts.HasTool) { state = RepairState.Choosing; continue; }

                        if (!facts.InStationRange) { state = RepairState.Choosing; continue; }

                        // Checked between swings, not only on arrival: a villager shoved away by
                        // something walks back rather than hammering the air.
                        if (!facts.AtTarget) return new RepairStep(RepairAction.MoveToTarget, RepairState.Mending);

                        return new RepairStep(RepairAction.Mend, RepairState.Mending);

                    default:
                        // A state this table does not know - a save from an older build, or a
                        // field that belonged to a different job. Start over rather than run a
                        // step that has no meaning here.
                        state = RepairState.Choosing;
                        continue;
                }
            }

            // Unreachable unless the table above gains a cycle. Yielding is the safe answer: it
            // consumes nothing and lets the next queue entry run.
            return new RepairStep(RepairAction.Yield, RepairState.Choosing);
        }
    }
}
