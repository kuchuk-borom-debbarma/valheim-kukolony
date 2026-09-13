namespace Kukolony.Jobs.Tend
{
    /// <summary>
    ///     Where a tending villager has got to.
    /// </summary>
    /// <remarks>
    ///     Persisted as an int on the villager, so append rather than reorder - and the first five
    ///     deliberately share their numbers with <see cref="Haul.HaulState" />, because the legs
    ///     mean the same thing. A villager whose state was written by a haul job yesterday resumes
    ///     somewhere coherent rather than somewhere arbitrary, and <see cref="Choosing" /> is zero
    ///     as it is in every work-state enum here: an unwritten field reads as zero, and landing
    ///     in "decide what to do" is always safe.
    /// </remarks>
    internal enum TendState
    {
        Choosing = 0,
        Fetching = 1,
        Collecting = 2,
        Delivering = 3,
        Feeding = 4,
        Clearing = 5
    }

    /// <summary>What the engine should do about it.</summary>
    internal enum TendAction
    {
        /// <summary>Nothing worth doing. Yield without consuming a repetition.</summary>
        Yield,

        /// <summary>Pick a station, and where to get what it wants.</summary>
        ChooseWork,

        /// <summary>Walk to the container holding it.</summary>
        MoveToSupply,

        /// <summary>Take it.</summary>
        Collect,

        /// <summary>Walk to the station.</summary>
        MoveToStation,

        /// <summary>Put it in.</summary>
        Feed,

        /// <summary>Take what the station has finished, so it can accept more.</summary>
        TakeOutput,

        /// <summary>A full trip finished.</summary>
        Complete
    }

    /// <summary>
    ///     Whether issuing an action for a leg means the villager is entering that leg.
    /// </summary>
    /// <remarks>
    ///     The same rule hauling uses: the recorded state is the leg the villager was on, so a leg
    ///     it was not already on is one it is starting, and the walk's trip clock is restarted
    ///     then rather than every tick. A leg announced every tick resets the clock every tick,
    ///     and the bound that gives up on an impossible trip can never be reached.
    /// </remarks>
    internal static class TendLegs
    {
        internal static bool Entering(TendState recorded, TendState leg) => recorded != leg;
    }

    /// <summary>
    ///     What is true right now, gathered by the engine before it asks.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Plain data with no Unity and no colony types, so every combination is checked in
    ///         about a second rather than only inside a four-minute game run.
    ///     </para>
    ///     <para>
    ///         <b>Station and supply, never source and destination.</b> Tending inverts hauling's
    ///         mapping: the claim lives on <c>VillagerState.Target</c>, and what must not be
    ///         shared is the station rather than the chest - any number of villagers may take
    ///         wood from one chest, while two feeding one kiln is a wasted round trip. A reader
    ///         who mapped "source" onto Target out of habit would silently claim the chest and
    ///         let two villagers converge on one station, so the words are chosen to make that
    ///         mistake unspellable.
    ///     </para>
    /// </remarks>
    internal readonly struct TendFacts
    {
        internal TendFacts(bool hasSupply, bool hasStation, bool atSupply, bool atStation,
            bool carrying, bool stationWants, bool stationHasOutput, bool tired)
        {
            HasSupply = hasSupply;
            HasStation = hasStation;
            AtSupply = atSupply;
            AtStation = atStation;
            Carrying = carrying;
            StationWants = stationWants;
            StationHasOutput = stationHasOutput;
            Tired = tired;
        }

        /// <summary>A container chosen to fetch from.</summary>
        internal bool HasSupply { get; }

        /// <summary>A station chosen and claimed.</summary>
        internal bool HasStation { get; }

        internal bool AtSupply { get; }

        internal bool AtStation { get; }

        /// <summary>Carrying something the chosen station is short of.</summary>
        internal bool Carrying { get; }

        /// <summary>The station is still short of what this villager is bringing it.</summary>
        internal bool StationWants { get; }

        /// <summary>The station is holding something finished that has to come off.</summary>
        internal bool StationHasOutput { get; }

        internal bool Tired { get; }
    }

    /// <summary>One decision: what to do, and where to record having done it.</summary>
    internal readonly struct TendStep
    {
        internal TendStep(TendAction action, TendState next)
        {
            Action = action;
            Next = next;
        }

        internal TendAction Action { get; }

        /// <summary>
        ///     Recorded only when the action succeeds, because walking and taking ownership both
        ///     span several ticks and the villager must arrive back at the same state until they
        ///     finish.
        /// </summary>
        internal TendState Next { get; }
    }

    /// <summary>
    ///     The tending state machine, as a pure function.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Clearing comes before supplying, and it is not politeness.</b> A cooking station
    ///         full of finished food cannot accept anything at all, so taking it off is the only
    ///         move that makes progress - and food left on it burns. A table that supplied first
    ///         would send a villager to fetch meat for an oven that could not take it, for ever,
    ///         while looking exactly like one that was working.
    ///     </para>
    ///     <para>
    ///         <b>Facts outrank the recorded state.</b> A villager that reloads carrying coal
    ///         delivers it; one whose station was destroyed goes back to choosing rather than
    ///         walking to a hole in the ground; one whose station filled while it walked
    ///         re-chooses, which is how a load nothing wants any more gets filed rather than
    ///         carried for ever.
    ///     </para>
    ///     <para>
    ///         There is deliberately no "put it back" action. A load nothing wants reaches
    ///         <see cref="TendAction.ChooseWork" /> from Delivering, and the engine answers either
    ///         with another station that wants it or with a container that will take it - reusing
    ///         the destination question hauling already answers. A new leg for that would be a
    ///         second implementation of a decision that exists.
    ///     </para>
    /// </remarks>
    internal static class TendTransitions
    {
        internal static TendStep Next(TendState state, TendFacts facts)
        {
            for (int guard = 0; guard < 8; guard++)
            {
                switch (state)
                {
                    case TendState.Choosing:
                        // Being tired is not a failure, and neither is having nothing to do.
                        if (facts.Tired) return new TendStep(TendAction.Yield, TendState.Choosing);

                        // Already carrying means the fetch half is done, wherever it was
                        // interrupted. Going back for more would strand the load.
                        if (facts.Carrying) { state = TendState.Delivering; continue; }

                        if (!facts.HasStation) return new TendStep(TendAction.ChooseWork, TendState.Fetching);

                        // Clear before supplying. A station holding finished work has no room for
                        // more of anything, so this is the only move that can make progress.
                        if (facts.StationHasOutput) { state = TendState.Clearing; continue; }

                        // This station is satisfied, so the trip is over before it started and
                        // another station may not be. Choosing again is cheaper than a walk.
                        if (!facts.StationWants) return new TendStep(TendAction.ChooseWork, TendState.Fetching);

                        // It wants something and nowhere has it. The engine answers by finding a
                        // container or by finding none, and finding none is a Skipped rather than
                        // a failure - there is simply nothing useful to do.
                        if (!facts.HasSupply) return new TendStep(TendAction.ChooseWork, TendState.Fetching);

                        state = TendState.Fetching;
                        continue;

                    case TendState.Clearing:
                        if (!facts.HasStation) { state = TendState.Choosing; continue; }

                        // Somebody else took it off, or it was never there. Not a failure.
                        if (!facts.StationHasOutput) { state = TendState.Choosing; continue; }

                        if (!facts.AtStation) return new TendStep(TendAction.MoveToStation, TendState.Clearing);
                        return new TendStep(TendAction.TakeOutput, TendState.Clearing);

                    case TendState.Fetching:
                        // A loaded villager never walks back to a chest. Whatever it is holding
                        // has to be delivered before anything else is picked up - a bag that
                        // fills with oddments cannot work at all.
                        if (facts.Carrying) { state = TendState.Delivering; continue; }

                        if (!facts.HasSupply) { state = TendState.Choosing; continue; }
                        if (!facts.AtSupply) return new TendStep(TendAction.MoveToSupply, TendState.Fetching);

                        state = TendState.Collecting;
                        continue;

                    case TendState.Collecting:
                        // Taking what was wanted clears the supply, which is what ends the fetch.
                        if (!facts.HasSupply)
                        {
                            state = facts.Carrying ? TendState.Delivering : TendState.Choosing;
                            continue;
                        }

                        // Re-checked rather than assumed from having arrived once. Collecting is
                        // reached through Fetching, which tests arrival - but a villager that
                        // reloads with this state recorded is standing wherever the world left
                        // it, and the exhaustive sweep found the gap: without this it would take
                        // from a chest it was nowhere near.
                        if (!facts.AtSupply) { state = TendState.Fetching; continue; }

                        // Standing at the chest, so topping up costs nothing. This is the one
                        // place the "never walk back" rule above does not apply, because there is
                        // no walk.
                        return new TendStep(TendAction.Collect, TendState.Collecting);

                    case TendState.Delivering:
                        if (!facts.Carrying) return new TendStep(TendAction.Complete, TendState.Choosing);

                        // The station went, or filled while the villager walked. Either way this
                        // load needs somewhere else to go, and the engine finds another station
                        // that wants it or a container that will take it.
                        if (!facts.HasStation) return new TendStep(TendAction.ChooseWork, TendState.Delivering);
                        if (!facts.StationWants) return new TendStep(TendAction.ChooseWork, TendState.Delivering);

                        if (!facts.AtStation) return new TendStep(TendAction.MoveToStation, TendState.Delivering);

                        state = TendState.Feeding;
                        continue;

                    case TendState.Feeding:
                        if (!facts.Carrying) return new TendStep(TendAction.Complete, TendState.Choosing);
                        if (!facts.HasStation) { state = TendState.Delivering; continue; }

                        // Filled under the villager's hands, by another villager or the player.
                        if (!facts.StationWants) return new TendStep(TendAction.ChooseWork, TendState.Delivering);

                        // Checked between loads rather than only on arrival: a villager shoved
                        // away from the station walks back rather than feeding the air.
                        if (!facts.AtStation) return new TendStep(TendAction.MoveToStation, TendState.Feeding);

                        return new TendStep(TendAction.Feed, TendState.Feeding);

                    default:
                        // A state this table does not know - a save from an older build, or a
                        // field that belonged to a different job. Start over rather than run a
                        // step that has no meaning here.
                        state = TendState.Choosing;
                        continue;
                }
            }

            // Unreachable unless the table above gains a cycle. Yielding is the safe answer:
            // it consumes nothing and lets the next queue entry run.
            return new TendStep(TendAction.Yield, TendState.Choosing);
        }
    }
}
