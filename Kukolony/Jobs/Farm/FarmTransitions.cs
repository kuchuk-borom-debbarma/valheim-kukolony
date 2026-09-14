namespace Kukolony.Jobs.Farm
{
    /// <summary>
    ///     Where a sowing villager has got to.
    /// </summary>
    /// <remarks>
    ///     Persisted as an int on the villager, so append rather than reorder - and note that
    ///     <see cref="Choosing" /> is deliberately zero, as it is in every work-state enum in this
    ///     mod. An unwritten field reads as zero, and so does the state left behind by a villager
    ///     that was doing some other job yesterday; landing either of them in "decide what to do"
    ///     is always safe.
    /// </remarks>
    internal enum FarmState
    {
        Choosing = 0,
        Approaching = 1,
        Sowing = 2
    }

    /// <summary>What the engine should do about it.</summary>
    internal enum FarmAction
    {
        /// <summary>Nothing worth doing. Yield without consuming a repetition.</summary>
        Yield,

        /// <summary>Find a field that wants something planted, and a square to put it in.</summary>
        ChooseWork,

        /// <summary>Walk to the square.</summary>
        MoveToSpot,

        /// <summary>Break the ground, because this crop needs a field and this is not one yet.</summary>
        Cultivate,

        /// <summary>Put the seed in.</summary>
        Sow,

        /// <summary>The field wants nothing more. That is what finishing looks like here.</summary>
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
    internal readonly struct FarmFacts
    {
        internal FarmFacts(bool hasField, bool wantsSowing, bool hasSpot, bool atSpot,
            bool hasSeed, bool ready, bool mayCultivate, bool tired)
        {
            HasField = hasField;
            WantsSowing = wantsSowing;
            HasSpot = hasSpot;
            AtSpot = atSpot;
            HasSeed = hasSeed;
            Ready = ready;
            MayCultivate = mayCultivate;
            Tired = tired;
        }

        /// <summary>A field this villager has settled on, which still exists and is in service.</summary>
        internal bool HasField { get; }

        /// <summary>
        ///     That field has an order that wants another plant.
        /// </summary>
        /// <remarks>
        ///     Separate from <see cref="HasField" /> because a field outlives being full, exactly
        ///     as a bush outlives being picked. Collapsing the two would give a villager that
        ///     finished a field no way to say so.
        /// </remarks>
        internal bool WantsSowing { get; }

        /// <summary>A square in it with nothing in the way.</summary>
        internal bool HasSpot { get; }

        internal bool AtSpot { get; }

        /// <summary>The seed this order costs, in the villager's own bag.</summary>
        internal bool HasSeed { get; }

        /// <summary>
        ///     The ground at that square is ready for this plant.
        /// </summary>
        /// <remarks>
        ///     Which for most crops means cultivated, and for a tree means nothing at all - a
        ///     sapling grows in open ground. So a tree field is always ready and never cultivates,
        ///     without the table knowing what a tree is.
        /// </remarks>
        internal bool Ready { get; }

        /// <summary>The field has been told villagers may break new ground in it.</summary>
        internal bool MayCultivate { get; }

        internal bool Tired { get; }
    }

    /// <summary>One decision: what to do, and where to record having done it.</summary>
    internal readonly struct FarmStep
    {
        internal FarmStep(FarmAction action, FarmState next)
        {
            Action = action;
            Next = next;
        }

        internal FarmAction Action { get; }

        /// <summary>
        ///     Recorded only when the action succeeds, because walking and cultivating both span
        ///     several ticks and the villager must arrive back at the same state until they
        ///     finish.
        /// </summary>
        internal FarmState Next { get; }
    }

    /// <summary>
    ///     The sowing state machine, as a pure function.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>The first job that consumes.</b> Every other one takes from the world and brings
    ///         it home; this takes from the settlement and puts it in the ground. So there is a
    ///         fact no other table has - whether the villager is carrying the seed - and losing it
    ///         mid-field is an ordinary event rather than a fault.
    ///     </para>
    ///     <para>
    ///         <b>Cultivating sits between walking and sowing, not before choosing.</b> A villager
    ///         only knows whether the ground is ready once it is standing on it, and asking
    ///         earlier would mean asking of every square in the field on every tick. So the table
    ///         arrives first and breaks ground second, which is also the order a person would do
    ///         it in.
    ///     </para>
    ///     <para>
    ///         <b>Facts outrank the recorded state.</b> The state says where the villager got to;
    ///         the world says what is still true, and the world wins. A villager that reloads
    ///         halfway to a square carries on; one whose field was filled by somebody else goes
    ///         back to choosing rather than planting into a full field.
    ///     </para>
    /// </remarks>
    internal static class FarmTransitions
    {
        internal static FarmStep Next(FarmState state, FarmFacts facts)
        {
            for (int guard = 0; guard < 8; guard++)
            {
                switch (state)
                {
                    case FarmState.Choosing:
                        if (facts.Tired) return new FarmStep(FarmAction.Yield, FarmState.Choosing);

                        // A field that wants nothing, or no field at all, is the same answer
                        // here: go and look for one. Asked in this arm as well as below because
                        // this is where a villager arrives holding a field that filled up while
                        // it was doing something else.
                        //
                        // **Before the seed, and that order is the whole of it.** Which seed a
                        // villager needs is a fact about the field it is working - there is no
                        // such thing as "the seed" until one is chosen. Asking first put the job
                        // in a knot it could not get out of: no field, so no seed, so yield - and
                        // a villager stood in front of a field it had been told to sow, reporting
                        // that no field was asking for anything.
                        if (!facts.HasField || !facts.WantsSowing || !facts.HasSpot)
                        {
                            return new FarmStep(FarmAction.ChooseWork, FarmState.Approaching);
                        }

                        // And now it is answerable. Having no seed is an ordinary answer rather
                        // than a failure - the errand that fetches more is asked before this
                        // table runs, so reaching here means the settlement has none either.
                        if (!facts.HasSeed) return new FarmStep(FarmAction.Yield, FarmState.Choosing);

                        state = FarmState.Approaching;
                        continue;

                    case FarmState.Approaching:
                        if (!facts.HasField) { state = FarmState.Choosing; continue; }

                        // Filled while this villager was walking - by another villager, or by the
                        // player. Going back to choosing rather than completing: nothing was
                        // sown, so nothing was finished.
                        if (!facts.WantsSowing) { state = FarmState.Choosing; continue; }

                        // The square was taken while walking to it. Another square in the same
                        // field will do, which is choosing's business.
                        if (!facts.HasSpot) { state = FarmState.Choosing; continue; }

                        if (!facts.AtSpot) return new FarmStep(FarmAction.MoveToSpot, FarmState.Approaching);
                        state = FarmState.Sowing;
                        continue;

                    case FarmState.Sowing:
                        // The field is gone, or it has everything it asked for. Either way the
                        // trip is done rather than broken - and this is the arm that runs on
                        // almost every successful field.
                        if (!facts.HasField) return new FarmStep(FarmAction.Complete, FarmState.Choosing);
                        if (!facts.WantsSowing) return new FarmStep(FarmAction.Complete, FarmState.Choosing);

                        // The seed ran out mid-field. Back to be told there is nothing to do,
                        // which is where the errand that fetches more is asked.
                        if (!facts.HasSeed) { state = FarmState.Choosing; continue; }

                        if (!facts.HasSpot) { state = FarmState.Choosing; continue; }

                        // Checked between plants, not only on arrival: a villager shoved away by
                        // something walks back rather than sowing into the air.
                        if (!facts.AtSpot) return new FarmStep(FarmAction.MoveToSpot, FarmState.Sowing);

                        // Standing on ground this plant cannot use. Breaking it is allowed or it
                        // is not, and when it is not this square is no good - which choosing will
                        // discover and step past.
                        if (!facts.Ready)
                        {
                            return facts.MayCultivate
                                ? new FarmStep(FarmAction.Cultivate, FarmState.Sowing)
                                : new FarmStep(FarmAction.ChooseWork, FarmState.Approaching);
                        }

                        return new FarmStep(FarmAction.Sow, FarmState.Sowing);

                    default:
                        // A state this table does not know - a save from an older build, or a
                        // field that belonged to a different job. Start over rather than run a
                        // step that has no meaning here.
                        state = FarmState.Choosing;
                        continue;
                }
            }

            // Unreachable unless the table above gains a cycle. Yielding is the safe answer: it
            // consumes nothing and lets the next queue entry run.
            return new FarmStep(FarmAction.Yield, FarmState.Choosing);
        }
    }
}
