namespace Kukolony.Jobs.Craft
{
    /// <summary>
    ///     Where a crafting villager has got to.
    /// </summary>
    /// <remarks>
    ///     Persisted as an int on the villager, so append rather than reorder - and the first
    ///     four deliberately share their numbers with <see cref="Haul.HaulState" /> and
    ///     <see cref="Tend.TendState" />, because the legs mean the same thing. A villager whose
    ///     state was written by a haul job yesterday resumes somewhere coherent rather than
    ///     somewhere arbitrary, and <see cref="Choosing" /> is zero as it is in every work-state
    ///     enum here: an unwritten field reads as zero, and landing in "decide what to do" is
    ///     always safe.
    /// </remarks>
    internal enum CraftState
    {
        Choosing = 0,
        Fetching = 1,
        Collecting = 2,
        Delivering = 3,
        Working = 4
    }

    /// <summary>What the engine should do about it.</summary>
    internal enum CraftAction
    {
        /// <summary>Nothing worth doing. Yield without consuming a repetition.</summary>
        Yield,

        /// <summary>Pick a station with an outstanding order, and where its materials are.</summary>
        ChooseWork,

        /// <summary>Walk to the container holding what the recipe needs.</summary>
        MoveToSupply,

        /// <summary>Take it.</summary>
        Collect,

        /// <summary>Walk to the station.</summary>
        MoveToStation,

        /// <summary>Make one.</summary>
        Craft,

        /// <summary>A full trip finished.</summary>
        Complete
    }

    /// <summary>Whether issuing an action for a leg means the villager is entering that leg.</summary>
    internal static class CraftLegs
    {
        internal static bool Entering(CraftState recorded, CraftState leg) => recorded != leg;
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
    ///         <b>Station and supply, never source and destination.</b> As in tending, the claim
    ///         lives on the villager's target and what must not be shared is the <em>station</em>:
    ///         any number of villagers may take iron from one chest, while two at one forge is a
    ///         wasted walk. The words are chosen so that mapping "source" onto the target out of
    ///         habit is unspellable.
    ///     </para>
    /// </remarks>
    internal readonly struct CraftFacts
    {
        internal CraftFacts(bool hasStation, bool hasSupply, bool atSupply, bool atStation,
            bool hasMaterials, bool stationUsable, bool wantsMade, bool bagRoom, bool tired)
        {
            HasStation = hasStation;
            HasSupply = hasSupply;
            AtSupply = atSupply;
            AtStation = atStation;
            HasMaterials = hasMaterials;
            StationUsable = stationUsable;
            WantsMade = wantsMade;
            BagRoom = bagRoom;
            Tired = tired;
        }

        /// <summary>A station chosen and claimed.</summary>
        internal bool HasStation { get; }

        /// <summary>A container chosen to fetch from.</summary>
        internal bool HasSupply { get; }

        internal bool AtSupply { get; }

        internal bool AtStation { get; }

        /// <summary>Carrying everything one craft needs.</summary>
        internal bool HasMaterials { get; }

        /// <summary>The station has its roof and its fire, and is still there.</summary>
        internal bool StationUsable { get; }

        /// <summary>The station still has an order wanting something made.</summary>
        internal bool WantsMade { get; }

        /// <summary>Somewhere to put what comes out.</summary>
        internal bool BagRoom { get; }

        internal bool Tired { get; }
    }

    /// <summary>One decision: what to do, and where to record having done it.</summary>
    internal readonly struct CraftStep
    {
        internal CraftStep(CraftAction action, CraftState next)
        {
            Action = action;
            Next = next;
        }

        internal CraftAction Action { get; }

        /// <summary>
        ///     Recorded only when the action succeeds, because walking and taking ownership both
        ///     span several ticks and the villager must arrive back at the same state until they
        ///     finish.
        /// </summary>
        internal CraftState Next { get; }
    }

    /// <summary>
    ///     The crafting state machine, as a pure function.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>The shape is tending's, with the ending changed.</b> Both fetch from a chest and
    ///         carry to a station; the difference is what happens on arrival. Tending hands its
    ///         load over and walks away empty. Crafting consumes its load and walks away
    ///         <em>holding the product</em> - which is why there is no delivery leg after the
    ///         work, and why room in the bag is a fact the table has to know about.
    ///     </para>
    ///     <para>
    ///         <b>A full bag stops the job rather than diverting it.</b> Finished goods stay with
    ///         the crafter until a hauler collects them, so the honest end of a full bag is to
    ///         stand and say so. Walking the product to a chest would be a second delivery
    ///         implementation and would quietly undo the arrangement.
    ///     </para>
    ///     <para>
    ///         <b>Facts outrank the recorded state.</b> A villager that reloads holding iron
    ///         carries on to the forge; one whose forge burned down goes back to choosing rather
    ///         than walking to a hole in the ground; one whose order was filled by somebody else
    ///         while it walked re-chooses rather than making a fifty-first nail.
    ///     </para>
    /// </remarks>
    internal static class CraftTransitions
    {
        internal static CraftStep Next(CraftState state, CraftFacts facts)
        {
            for (int guard = 0; guard < 8; guard++)
            {
                switch (state)
                {
                    case CraftState.Choosing:
                        // Being tired is not a failure, and neither is having nothing to make.
                        if (facts.Tired) return new CraftStep(CraftAction.Yield, CraftState.Choosing);

                        // Nowhere to put what comes out. Said by the engine and not worked
                        // around here, because the arrangement is deliberate: the product waits
                        // with its maker until somebody carries it off.
                        if (!facts.BagRoom) return new CraftStep(CraftAction.Yield, CraftState.Choosing);

                        if (!facts.HasStation) return new CraftStep(CraftAction.ChooseWork, CraftState.Fetching);

                        // The order was filled, or the fire went out, while this was being
                        // decided. Another station may still want something.
                        if (!facts.WantsMade || !facts.StationUsable)
                            return new CraftStep(CraftAction.ChooseWork, CraftState.Fetching);

                        // Already holding what the recipe needs - a reload mid-trip, or a chest
                        // that happened to be the station's own. Going back for more would
                        // fetch a second set of materials nothing has asked for.
                        if (facts.HasMaterials) { state = CraftState.Delivering; continue; }

                        if (!facts.HasSupply) return new CraftStep(CraftAction.ChooseWork, CraftState.Fetching);

                        state = CraftState.Fetching;
                        continue;

                    case CraftState.Fetching:
                        if (facts.HasMaterials) { state = CraftState.Delivering; continue; }
                        if (!facts.HasStation) { state = CraftState.Choosing; continue; }
                        if (!facts.HasSupply) { state = CraftState.Choosing; continue; }
                        if (!facts.AtSupply) return new CraftStep(CraftAction.MoveToSupply, CraftState.Fetching);

                        state = CraftState.Collecting;
                        continue;

                    case CraftState.Collecting:
                        if (!facts.HasSupply)
                        {
                            state = facts.HasMaterials ? CraftState.Delivering : CraftState.Choosing;
                            continue;
                        }

                        // Re-checked rather than assumed from having arrived once. Collecting is
                        // reached through Fetching, which tests arrival - but a villager that
                        // reloads with this state recorded is standing wherever the world left
                        // it, and without this it would take from a chest it was nowhere near.
                        // The same gap the tending sweep found.
                        if (!facts.AtSupply) { state = CraftState.Fetching; continue; }

                        return new CraftStep(CraftAction.Collect, CraftState.Collecting);

                    case CraftState.Delivering:
                        // Materials gone - taken by somebody else, or never really there.
                        if (!facts.HasMaterials) { state = CraftState.Choosing; continue; }

                        // The station went, was switched off, or lost its fire while the
                        // villager walked. The load stays in the bag and another station is
                        // chosen; hauling files it if nothing wants it.
                        if (!facts.HasStation || !facts.StationUsable || !facts.WantsMade)
                            return new CraftStep(CraftAction.ChooseWork, CraftState.Delivering);

                        if (!facts.AtStation) return new CraftStep(CraftAction.MoveToStation, CraftState.Delivering);

                        state = CraftState.Working;
                        continue;

                    case CraftState.Working:
                        // One craft's worth is gone, so the trip is over. Whether to make
                        // another is the next trip's question, asked with fresh facts.
                        if (!facts.HasMaterials) return new CraftStep(CraftAction.Complete, CraftState.Choosing);

                        if (!facts.HasStation) { state = CraftState.Delivering; continue; }

                        // Filled under the villager's hands, or the fire went out mid-craft.
                        if (!facts.WantsMade || !facts.StationUsable)
                            return new CraftStep(CraftAction.ChooseWork, CraftState.Delivering);

                        // Checked between crafts rather than only on arrival: a villager shoved
                        // away from the forge walks back rather than hammering the air.
                        if (!facts.AtStation) return new CraftStep(CraftAction.MoveToStation, CraftState.Working);

                        // Room is re-checked here and not only at Choosing, because each craft
                        // adds to the bag and the last one can be the one that fills it.
                        if (!facts.BagRoom) return new CraftStep(CraftAction.Complete, CraftState.Choosing);

                        return new CraftStep(CraftAction.Craft, CraftState.Working);

                    default:
                        // A state this table does not know - a save from an older build, or a
                        // field that belonged to a different job. Start over rather than run a
                        // step that has no meaning here.
                        state = CraftState.Choosing;
                        continue;
                }
            }

            // Unreachable unless the table above gains a cycle. Yielding is the safe answer:
            // it consumes nothing and lets the next queue entry run.
            return new CraftStep(CraftAction.Yield, CraftState.Choosing);
        }
    }
}
