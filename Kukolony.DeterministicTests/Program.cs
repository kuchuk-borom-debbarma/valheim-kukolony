using Kukolony.Colonies;
using Kukolony.Jobs;
using Kukolony.Villagers.Navigation;
using Kukolony.Jobs.Haul;

/// <summary>
///     Verification for logic that needs no game running.
/// </summary>
/// <remarks>
///     It reports the count rather than printing a bare pass, because a suite that asserts
///     nothing and says PASS is worse than one that says nothing at all - it reads like
///     coverage from every log and every CI summary that ever quotes it.
/// </remarks>
static class Program
{
    static int _cases;
    static int _failed;

    static int Main()
    {
        // Retired bits must never be readable as a capability this build understands.
        // Fireplace(2), CookingStation(8), Fermenter(16) and BeeHive(32) were dropped; Rest
        // deliberately took 64 rather than one of those, so an old record cannot come back as
        // a bed. These are the exact ints an existing save holds.
        Mask("a retired fireplace record reads as nothing", 2, StructureCapability.None);
        Mask("a retired cooking station reads as nothing", 8, StructureCapability.None);
        Mask("a retired fermenter reads as nothing", 16, StructureCapability.None);
        Mask("a retired beehive reads as nothing", 32, StructureCapability.None);
        Mask("every retired bit at once reads as nothing", 2 | 8 | 16 | 32, StructureCapability.None);

        // The two that carry over keep their meaning, which is the whole reason their bits
        // were left where they were.
        Mask("a container record is still storage", 1, StructureCapability.Storage);
        Mask("a smelter record is still processing", 4, StructureCapability.Processing);
        Mask("a chest that was also a fireplace keeps only storage", 1 | 2, StructureCapability.Storage);
        Mask("a bed reads as rest", 64, StructureCapability.Rest);
        Mask("a structure can carry two capabilities at once", 1 | 4,
            StructureCapability.Storage | StructureCapability.Processing);

        // Control: if Rest had taken a retired bit, this pair would be indistinguishable.
        Case("rest does not collide with any retired bit",
            ((int)StructureCapability.Rest & (2 | 8 | 16 | 32)) == 0);

        Label("no longer understood", StructureCapability.None);
        Label("Storage", StructureCapability.Storage);
        Label("Rest", StructureCapability.Rest);
        Label("Storage + Processing", StructureCapability.Storage | StructureCapability.Processing);
        // A retired bit must not reach the player as a name, or as a blank row.
        Label("no longer understood", (StructureCapability)2);
        Label("Storage", (StructureCapability)(1 | 32));

        Haul();
        Placing();
        Packing();
        Reckoning();
        Arriving();
        Rescuing();
        Locomoting();

        Console.WriteLine(_failed == 0
            ? $"RESULT: PASS ({_cases} cases)"
            : $"RESULT: FAIL ({_failed} of {_cases} cases)");
        return _failed == 0 ? 0 : 1;
    }

    /// <summary>
    ///     The hauling state machine, exhaustively.
    /// </summary>
    /// <remarks>
    ///     These are the cases that are painful to stage in a running game - a destination that
    ///     vanishes mid-trip, a bag that fills halfway through a sweep, a villager reloaded
    ///     holding someone else's stone. Each is one line here and a four-minute run otherwise,
    ///     which is the entire reason the decision half has no Unity in it.
    /// </remarks>
    static void Haul()
    {
        // Tiredness is a yield, and it is the only one the table can decide by itself.
        //
        // "There is nothing to haul" is deliberately NOT here: the table cannot know it. It
        // answers ChooseWork, the engine looks, finds nothing, and returns Skipped. Writing a
        // case that expected a Yield for an empty settlement contradicted the case below it,
        // which is how this comment came to exist.
        Step("a tired villager yields",
            HaulState.Choosing, Facts(tired: true), HaulAction.Yield);
        Step("a tired villager yields even with work available",
            HaulState.Choosing, Facts(hasSource: true, tired: true), HaulAction.Yield);

        // The ordinary cycle, start to finish.
        Step("with nothing chosen, choose",
            HaulState.Choosing, Facts(), HaulAction.ChooseWork);
        Step("with a source chosen, walk to it",
            HaulState.Choosing, Facts(hasSource: true), HaulAction.MoveToSource);
        Step("standing at the source, collect",
            HaulState.Fetching, Facts(hasSource: true, atSource: true), HaulAction.Collect);
        Step("carrying, with a destination, walk to it",
            HaulState.Delivering, Facts(carrying: true, hasDestination: true), HaulAction.MoveToDestination);
        Step("standing at the destination, deposit",
            HaulState.Delivering, Facts(carrying: true, hasDestination: true, atDestination: true),
            HaulAction.Deposit);
        Step("having put it down, the trip is complete",
            HaulState.Depositing, Facts(), HaulAction.Complete);

        // Facts outrank the recorded state. Each of these is a villager resuming into a state
        // the world has already moved past.
        Step("a villager reloaded carrying something delivers it rather than fetching more",
            HaulState.Choosing, Facts(carrying: true, hasDestination: true), HaulAction.MoveToDestination);
        Step("a source taken by someone else sends the villager back to choosing",
            HaulState.Fetching, Facts(), HaulAction.ChooseWork);
        Step("a destination that vanished is re-chosen, not failed",
            HaulState.Delivering, Facts(carrying: true), HaulAction.ChooseWork);
        Step("a destination that vanished while depositing is re-chosen",
            HaulState.Depositing, Facts(carrying: true), HaulAction.ChooseWork);
        Step("an empty-handed villager at the deposit step ends the trip",
            HaulState.Depositing, Facts(), HaulAction.Complete);
        Step("a sweep that found nothing ends the trip rather than stalling",
            HaulState.Delivering, Facts(), HaulAction.Complete);

        // A full bag stops collecting even with more to take - what is carried has to be
        // delivered first, or a villager sweeps forever and delivers nothing.
        Step("a full bag stops the sweep and delivers",
            HaulState.Collecting, Facts(hasSource: true, atSource: true, carrying: true,
                bagFull: true, hasDestination: true), HaulAction.MoveToDestination);
        Step("a full bag with nowhere to go chooses a destination",
            HaulState.Collecting, Facts(hasSource: true, carrying: true, bagFull: true),
            HaulAction.ChooseWork);
        Step("a source emptied mid-sweep while carrying moves to delivery",
            HaulState.Collecting, Facts(carrying: true, hasDestination: true),
            HaulAction.MoveToDestination);
        Step("a source emptied mid-sweep with nothing carried goes back to choosing",
            HaulState.Collecting, Facts(), HaulAction.ChooseWork);

        // A state from an older build, or from a different job, must not run a step that has
        // no meaning here.
        Step("an unknown state restarts rather than acting",
            (HaulState)99, Facts(), HaulAction.ChooseWork);

        // Control: the table always terminates. If it ever gains a cycle this is what catches
        // it, because the guard's fallback is the only path to a Yield with work available.
        Case("control: a decision is always reached with work available",
            HaulTransitions.Next(HaulState.Choosing, Facts(hasSource: true)).Action != HaulAction.Yield);
    }

    /// <summary>
    ///     The anti-shuffle rule. Proven at a table because the symptom in-game - a settlement
    ///     in permanent motion with nothing improving - looks exactly like villagers being busy.
    /// </summary>
    static void Placing()
    {
        Console.WriteLine("placement");

        Score("a container that names the item is its best home", 2,
            names: true, takesAnything: false, takesUnclaimed: false, atCap: false);
        Score("a container that takes anything is a home, but a lesser one", 1,
            names: false, takesAnything: true, takesUnclaimed: false, atCap: false);
        Score("the settlement dump is worth the same as an overflow chest", 1,
            names: false, takesAnything: false, takesUnclaimed: true, atCap: false);
        Score("a container that wants none of this is not a home at all", 0,
            names: false, takesAnything: false, takesUnclaimed: false, atCap: false);

        // A cap is not a special case anywhere else: it lands here, as a zero.
        Score("a container at its cap stops attracting more", 0,
            names: true, takesAnything: false, takesUnclaimed: false, atCap: true);
        Score("a cap silences the dump flag too, or the dump would never be full", 0,
            names: false, takesAnything: false, takesUnclaimed: true, atCap: true);

        Case("the ground is worse than any container that will have it",
            Placement.Ground < Placement.Overflow && Placement.Ground < Placement.Named);

        // The whole point. Equal is not good enough: equal is the shuffle.
        Case("wood in an overflow chest may move to the wood chest",
            Placement.MayMove(Placement.Overflow, Placement.Named));
        Case("wood in the wood chest may not move to an overflow chest",
            !Placement.MayMove(Placement.Named, Placement.Overflow));
        Case("wood may not move between two chests that both name wood",
            !Placement.MayMove(Placement.Named, Placement.Named));
        Case("nothing moves into a container that refuses it",
            !Placement.MayMove(Placement.Ground, Placement.Refused));
        Case("control: picking something up off the ground is still allowed",
            Placement.MayMove(Placement.Ground, Placement.Overflow));

        // Control: a rule that permitted everything would pass every line above except this
        // one. An assertion that has never failed proves nothing.
        Case("control: the rule refuses a move that does not improve anything",
            !Placement.MayMove(Placement.Overflow, Placement.Overflow));

        // Which mess gets fixed first.
        Case("rescuing an item from a chest that refuses it beats a lesser tidy-up",
            Placement.Improvement(Placement.Refused, Placement.Named) >
            Placement.Improvement(Placement.Overflow, Placement.Named));
        Case("picking an item up off the ground outranks shuffling it between chests",
            Placement.Improvement(Placement.Ground, Placement.Named) >
            Placement.Improvement(Placement.Overflow, Placement.Named));
        Case("a move that is not allowed is worth nothing, never a negative",
            Placement.Improvement(Placement.Named, Placement.Overflow) == 0);
        Case("control: an allowed move is always worth more than a forbidden one",
            Placement.Improvement(Placement.Overflow, Placement.Named) >
            Placement.Improvement(Placement.Named, Placement.Named));
    }

    /// <summary>
    ///     Packing part-used stacks together. All boundaries, which is why it is here.
    /// </summary>
    static void Packing()
    {
        Console.WriteLine("stacking");

        Pack("two half stacks become one", new[] { 30, 20 }, 50, new[] { 50 });
        Pack("a full stack and a remainder is the fewest slots the total can occupy",
            new[] { 40, 40, 20 }, 50, new[] { 50, 50 });
        Pack("an exact multiple leaves no remainder", new[] { 25, 25, 25, 25 }, 50, new[] { 50, 50 });
        Pack("more than fits in one slot spills into the next", new[] { 30, 30, 30 }, 50, new[] { 50, 40 });
        Pack("a full stack beside a partial one is already minimal", new[] { 50, 30 }, 50, new int[0]);

        // Nothing to do must be sayable, or a settlement rewrites every chest it looks at
        // forever - and rewriting a container makes it save and tells every watcher it changed.
        Pack("a single stack is left alone", new[] { 17 }, 50, new int[0]);
        Pack("a single full stack is left alone", new[] { 50 }, 50, new int[0]);
        Pack("already packed stacks are left alone", new[] { 50, 50, 12 }, 50, new int[0]);
        Pack("nothing at all is nothing to do", new int[0], 50, new int[0]);

        // Order is not packing's business - three stacks holding 112 occupy three slots
        // whichever way round they sit, and putting them in a sensible order is the sorting
        // step. Packing that rewrote for order alone would report work every time it looked.
        Pack("the same stacks in a different order are still minimal", new[] { 12, 50, 50 }, 50, new int[0]);

        // Control: the rule must actually distinguish the two. A version that always rewrote,
        // or never did, would pass half of the lines above.
        Case("control: packing tells tidy from untidy rather than answering the same way twice",
            Stacking.Pack(new List<int> { 50, 50, 12 }, 50).Count == 0 &&
            Stacking.Pack(new List<int> { 40, 40, 20 }, 50).Count > 0);
    }

    /// <summary>
    ///     Moving while unobserved. All boundaries, and all of them off-screen where nothing
    ///     would notice them going wrong.
    /// </summary>
    static void Reckoning()
    {
        Console.WriteLine("reckoning");

        Step("an ordinary tick moves at the given speed", 100f, 4f, .25f, 1f);
        Step("the last tick stops exactly on the destination rather than past it", .3f, 4f, .25f, .3f);
        Step("having arrived, there is nowhere further to go", 0f, 4f, .25f, 0f);

        // A frame that took a second - loading a zone, saving the world - must not fling a
        // villager through whatever it would have walked around.
        Step("a frame hitch does not become a teleport", 100f, 4f, 3f,
            global::Kukolony.Villagers.Navigation.Reckoning.MaximumStep);

        Step("a character with no speed does not drift", 100f, 0f, .25f, 0f);
        Step("time that did not pass moves nothing", 100f, 4f, 0f, 0f);
        Step("a negative delta after a clock adjustment moves nothing", 100f, 4f, -1f, 0f);

        // Control: a step that ignored its arguments would satisfy several lines above.
        Case("control: a faster walker covers more ground in the same tick",
            global::Kukolony.Villagers.Navigation.Reckoning.StepLength(100f, 4f, .2f) >
            global::Kukolony.Villagers.Navigation.Reckoning.StepLength(100f, 2f, .2f));
    }

    /// <summary>
    ///     Deciding whether a villager got there, when how close it can get depends on the world.
    /// </summary>
    static void Arriving()
    {
        Console.WriteLine("arriving");

        Arrive("close enough is arrived, walking or not", false, 3f, 5f, 0f, 20f, Approaching.Arrived);
        Arrive("still walking and not there yet keeps walking", false, 30f, 5f, 1f, 20f,
            Approaching.KeepWalking);

        // The intermittent hauling failure, as a table row: stopped 7.2m from a chest whose
        // comfortable range is 5m, not closing. That is as near as the navmesh goes.
        Arrive("stopped short, within reach and settled, is as arrived as it gets",
            true, 7.2f, 5f, 4f, 20f, Approaching.Arrived);

        // ...but a momentary stop is not the end of a journey.
        Arrive("stopped short but only just, keeps walking", true, 7.2f, 5f, 1f, 20f,
            Approaching.KeepWalking);

        Arrive("stopped far away and settled is not arrival, it is being stuck",
            true, 40f, 5f, 25f, 20f, Approaching.GaveUp);
        Arrive("stopped far away but still within patience keeps trying",
            true, 40f, 5f, 5f, 20f, Approaching.KeepWalking);

        // Control: the two ways of arriving must be genuinely different. A rule that answered
        // Arrived for anything stopped would pass four lines above and fail this one.
        Case("control: stopping a long way off is never mistaken for arriving",
            Arrival.Judge(true, 40f, 5f, 4f, 20f) != Approaching.Arrived);

        // Control: and one that never answered Arrived would pass that and fail this.
        Case("control: stopping just outside the comfortable range does count",
            Arrival.Judge(true, 6f, 5f, 4f, 20f) == Approaching.Arrived);
    }

    /// <summary>How long a rescue lasts when the same bad ground keeps needing one.</summary>
    static void Rescuing()
    {
        Console.WriteLine("rescuing");

        Burst("the first rescue is short, because most only need to be", 1, 20f);
        Burst("the second lasts twice as long", 2, 40f);
        Burst("the third, twice again", 3, 80f);
        Burst("and it stops doubling at the cap", 4, 160f);
        Burst("however many times it has been needed", 9, 160f);

        // A zeroth rescue is not a thing, but asking must not produce a zero-length one - that
        // would be a rescue that rescues nobody, retried forever.
        Burst("asking before any rescue has happened still gives a usable burst", 0, 20f);

        // Control: it must actually grow. A constant would pass the first line and the cap.
        Case("control: repeated rescues last longer than single ones",
            Rescue.BurstSeconds(3) > Rescue.BurstSeconds(1));

        // Control: and it must actually stop growing.
        Case("control: rescues are bounded however bad the ground is",
            Rescue.BurstSeconds(30) == Rescue.BurstSeconds(4));
    }

    /// <summary>
    ///     Walking versus being rescued. Six booleans, sixty-four combinations, all of them.
    /// </summary>
    static void Locomoting()
    {
        Console.WriteLine("locomotion");

        // The property worth having, checked exhaustively rather than argued: a villager is
        // never left doing neither. Every combination must produce something that moves it or
        // puts it somewhere it can move from.
        int idle = 0;
        for (int bits = 0; bits < 64; bits++)
        {
            TravelFacts facts = new TravelFacts(
                (bits & 1) != 0, (bits & 2) != 0, (bits & 4) != 0, (bits & 8) != 0,
                (bits & 16) != 0, (bits & 32) != 0, canStand: true);

            Locomotion move = Locomotor.Decide(facts);
            if (move != Locomotion.Walk && move != Locomotion.CoverGround &&
                move != Locomotion.BackOnFoot && move != Locomotion.PutBackOnNavmesh &&
                move != Locomotion.BeginRescue) idle++;
        }

        Case($"every combination of facts produces an action (idle in {idle})", idle == 0);

        // The five-minute standstill, as a single line: rescuing, in view, polite rescues still
        // available - but nowhere to stand. It must keep covering ground rather than stop.
        Case("a villager with nowhere to stand keeps going rather than waiting to be able to walk",
            Locomotor.Decide(new TravelFacts(true, true, false, true, true, true, false))
                == Locomotion.CoverGround);

        Case("and hands back the moment there is somewhere to stand",
            Locomotor.Decide(new TravelFacts(true, true, false, true, true, true, true))
                == Locomotion.BackOnFoot);

        // Walking is preferred wherever it is possible.
        Case("not stalled means walk, whoever is watching",
            Locomotor.Decide(new TravelFacts(false, true, false, false, false, true, true))
                == Locomotion.Walk);
        Case("a short errand is never a journey to be rescued from",
            Locomotor.Decide(new TravelFacts(false, false, false, false, true, true, true))
                == Locomotion.Walk);

        // Being seen ends a rescue early, which is what stops a villager gliding home in view.
        Case("coming into view ends a rescue that still had time left",
            Locomotor.Decide(new TravelFacts(true, true, false, true, true, true, true))
                == Locomotion.BackOnFoot);
        Case("but not once the polite rescues have been used up",
            Locomotor.Decide(new TravelFacts(true, true, false, true, true, false, true))
                == Locomotion.CoverGround);

        // Escalation: unseen and stalled goes straight to covering ground, because there is
        // nobody to be polite for.
        Case("stalled and unobserved starts a rescue without ceremony",
            Locomotor.Decide(new TravelFacts(false, true, true, false, true, true, true))
                == Locomotion.BeginRescue);
        Case("stalled in view tries the gentle option first",
            Locomotor.Decide(new TravelFacts(false, true, true, true, true, true, true))
                == Locomotion.PutBackOnNavmesh);

        // Control: the gentle option must not be offered where it cannot work.
        Case("control: nowhere to stand means no point being put back on the navmesh",
            Locomotor.Decide(new TravelFacts(false, true, true, true, true, true, false))
                == Locomotion.BeginRescue);

        // Control: arriving ends a rescue rather than continuing it forever.
        Case("control: a journey that is over stops being rescued",
            Locomotor.Decide(new TravelFacts(true, false, false, false, false, false, true))
                == Locomotion.BackOnFoot);

        // ...and does so even where there is nowhere good to stand, because otherwise whether a
        // villager walks in or slides in depends on whether the navmesh at its destination has
        // finished building. That is how a check comes to pass one run and fail the next.
        Case("a villager always finishes a journey on its feet",
            Locomotor.Decide(new TravelFacts(true, false, false, false, false, false, false))
                == Locomotion.BackOnFoot);

        // Control: mid-journey with nowhere to stand is still the case that must keep going.
        Case("control: nowhere to stand mid-journey still keeps covering ground",
            Locomotor.Decide(new TravelFacts(true, true, true, false, true, false, false))
                == Locomotion.CoverGround);
    }

    static void Burst(string what, int consecutive, float expected)
    {
        float actual = Rescue.BurstSeconds(consecutive);
        Case($"{what} (got {actual})", Math.Abs(actual - expected) < .001f);
    }

    static void Arrive(string what, bool stopped, float distance, float stopDistance,
        float stalledFor, float patience, Approaching expected)
    {
        Approaching actual = Arrival.Judge(stopped, distance, stopDistance, stalledFor, patience);
        Case($"{what} (got {actual})", actual == expected);
    }

    static void Step(string what, float remaining, float speed, float dt, float expected)
    {
        float actual = global::Kukolony.Villagers.Navigation.Reckoning.StepLength(remaining, speed, dt);
        Case($"{what} (got {actual})", Math.Abs(actual - expected) < .0001f);
    }

    static void Pack(string what, int[] stacks, int maximum, int[] expected)
    {
        List<int> actual = Stacking.Pack(new List<int>(stacks), maximum);
        bool same = actual.Count == expected.Length;
        for (int i = 0; same && i < expected.Length; i++) same = actual[i] == expected[i];
        Case($"{what} (got [{string.Join(",", actual)}])", same);
    }

    static void Score(string what, int expected, bool names, bool takesAnything, bool takesUnclaimed, bool atCap)
    {
        int actual = Placement.Score(names, takesAnything, takesUnclaimed, atCap);
        Case($"{what} (got {actual})", actual == expected);
    }

    static HaulFacts Facts(bool hasSource = false, bool hasDestination = false, bool atSource = false,
        bool atDestination = false, bool carrying = false, bool bagFull = false, bool tired = false) =>
        new HaulFacts(hasSource, hasDestination, atSource, atDestination, carrying, bagFull, tired);

    static void Step(string what, HaulState state, HaulFacts facts, HaulAction expected)
    {
        HaulAction actual = HaulTransitions.Next(state, facts).Action;
        Case($"{what} (got {actual})", actual == expected);
    }

    static void Mask(string what, int stored, StructureCapability expected) =>
        Case(what, ((StructureCapability)stored & StructureCapabilities.Known) == expected);

    static void Label(string expected, StructureCapability capabilities) =>
        Case($"\"{expected}\" is what {(int)capabilities} reads as",
            StructureCapabilities.Describe(capabilities) == expected);

    static void Case(string what, bool passed)
    {
        _cases++;
        if (passed) return;
        _failed++;
        Console.WriteLine($"  [FAIL] {what}");
    }
}
