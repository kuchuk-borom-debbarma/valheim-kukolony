using Kukolony.Core;
using Kukolony.Colonies;
using Kukolony.Jobs;
using Kukolony.Villagers.Navigation;
using Kukolony.Villagers;
using Kukolony.Jobs.Haul;
using Kukolony.Jobs.Chop;
using Kukolony.Jobs.Tend;

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
        Legs();
        Tiring();
        Repeating();
        Chopping();
        Appetite();
        Tending();

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

        // Filling the bag before setting out. The setting was persisted and shown on the job
        // screen from the day it was written and read by nothing, so every villager delivered
        // after a single item whichever way it was set.
        Step("wanting a full load looks for more before setting out",
            HaulState.Collecting, Facts(carrying: true, hasDestination: true, fillBagFirst: true),
            HaulAction.ChooseWork);
        Step("control: without it the same villager sets straight off",
            HaulState.Collecting, Facts(carrying: true, hasDestination: true),
            HaulAction.MoveToDestination);
        Step("a full bag beats wanting a fuller one",
            HaulState.Collecting, Facts(carrying: true, hasDestination: true, bagFull: true,
                fillBagFirst: true), HaulAction.MoveToDestination);
        Step("wanting a full load carries nothing back to choosing when the bag is empty",
            HaulState.Collecting, Facts(fillBagFirst: true), HaulAction.ChooseWork);
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
    ///     Which ticks begin a leg, which is what starts the trip's clock afresh.
    /// </summary>
    /// <remarks>
    ///     Pure, and worth having pure: this is the rule a round of review got wrong by
    ///     announcing the leg at one of the four places a haul can reach its delivery, and
    ///     the failure it produced - a delivery inheriting the fetch's stall and giving up on
    ///     a reachable chest - is invisible until a villager does it.
    /// </remarks>
    static void Legs()
    {
        Console.WriteLine("haul legs");

        // The three routes that never pass through choosing. Each records the leg it was on,
        // so each is entering a different one.
        Case("a full bag begins the delivery leg",
            HaulLegs.Entering(HaulState.Collecting, HaulState.Delivering));
        Case("a sorted chest begins the delivery leg",
            HaulLegs.Entering(HaulState.Collecting, HaulState.Delivering));
        Case("choosing begins the fetch leg",
            HaulLegs.Entering(HaulState.Choosing, HaulState.Fetching));

        // And the one that must not fire, because a leg announced every tick resets the clock
        // every tick and the abandon bound can never be reached.
        Case("control: walking on the leg it is already on begins nothing",
            !HaulLegs.Entering(HaulState.Delivering, HaulState.Delivering));
        Case("control: nor on the fetch leg",
            !HaulLegs.Entering(HaulState.Fetching, HaulState.Fetching));

        // A trip resumed after a reload records where it got to, so it begins nothing either -
        // it carries on the leg it was on, which is what resuming means.
        Case("control: a resumed delivery carries on rather than starting again",
            !HaulLegs.Entering(HaulState.Delivering, HaulState.Delivering));
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
        for (int bits = 0; bits < 512; bits++)
        {
            TravelFacts facts = new TravelFacts(
                (bits & 1) != 0, (bits & 2) != 0, (bits & 4) != 0, (bits & 8) != 0,
                (bits & 16) != 0, (bits & 32) != 0, canStand: (bits & 64) != 0,
                waterAhead: (bits & 128) != 0, nearLand: (bits & 256) != 0);

            Locomotion move = Locomotor.Decide(facts);
            if (move != Locomotion.Walk && move != Locomotion.CoverGround &&
                move != Locomotion.BackOnFoot && move != Locomotion.PutBackOnNavmesh &&
                move != Locomotion.BeginRescue) idle++;
        }

        Case($"every combination of facts produces an action (idle in {idle})", idle == 0);

        // Water. Ground that can never be walked is known in advance, so an ocean is a
        // decision rather than a forty-five-second stall at the shoreline - but only where
        // nobody can see it, because crossing water is covering ground, and the probe is a
        // straight chord that reads a walkable route around a bay as water.
        Case("water ahead on an unseen journey starts the crossing without waiting to stall",
            Locomotor.Decide(new TravelFacts(false, true, false, false, false, true, true,
                waterAhead: true)) == Locomotion.BeginRescue);
        Case("control: the same villager on dry ground walks",
            Locomotor.Decide(new TravelFacts(false, true, false, false, false, true, true))
                == Locomotion.Walk);
        Case("watched, water ahead does not start a glide - the villager keeps walking",
            Locomotor.Decide(new TravelFacts(false, true, false, true, false, true, true,
                waterAhead: true)) == Locomotion.Walk);
        Case("watched and genuinely blocked, the stall ladder still takes over",
            Locomotor.Decide(new TravelFacts(false, true, false, true, true, true, true,
                waterAhead: true)) == Locomotion.PutBackOnNavmesh);
        Case("an unseen crossing does not hand back onto its feet in the middle of the sea",
            Locomotor.Decide(new TravelFacts(true, true, true, false, true, true, true,
                waterAhead: true)) == Locomotion.CoverGround);
        Case("control: the same crossing with land ahead comes back on foot",
            Locomotor.Decide(new TravelFacts(true, true, true, false, true, true, true))
                == Locomotion.BackOnFoot);
        Case("a player walking up to a crossing sees it step ashore when the shore is a stride away",
            Locomotor.Decide(new TravelFacts(true, true, false, true, true, true, true,
                waterAhead: true, nearLand: true)) == Locomotion.BackOnFoot);
        Case("watched mid-sea far from any shore keeps crossing rather than snapping to one",
            Locomotor.Decide(new TravelFacts(true, true, false, true, true, true, true,
                waterAhead: true)) == Locomotion.CoverGround);
        Case("control: watched mid-sea with nowhere to stand at all still keeps going",
            Locomotor.Decide(new TravelFacts(true, true, false, true, true, true, false,
                waterAhead: true)) == Locomotion.CoverGround);
        Case("water near home is not a journey and is not crossed",
            Locomotor.Decide(new TravelFacts(false, false, false, true, false, true, true,
                waterAhead: true)) == Locomotion.Walk);
        Case("arrival still ends a crossing - a finished journey stops, wet or not",
            Locomotor.Decide(new TravelFacts(true, false, false, false, false, false, true,
                waterAhead: true)) == Locomotion.BackOnFoot);

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

    /// <summary>Getting tired, recovering, and not flickering between the two.</summary>
    static void Tiring()
    {
        Console.WriteLine("energy");

        Case("an action costs what it costs",
            Math.Abs(Energy.Spend(50f, 5f) - 45f) < .001f);
        Case("a villager cannot be more tired than exhausted",
            Math.Abs(Energy.Spend(3f, 5f)) < .001f);
        Case("a free action costs nothing",
            Math.Abs(Energy.Spend(50f, 0f) - 50f) < .001f);

        Case("resting recovers with time",
            Math.Abs(Energy.Recovered(50f, 10f, 2f) - 70f) < .001f);
        Case("resting never goes past full",
            Math.Abs(Energy.Recovered(95f, 100f, 2f) - Energy.Full) < .001f);
        Case("no time passing recovers nothing",
            Math.Abs(Energy.Recovered(50f, 0f, 2f) - 50f) < .001f);
        Case("a negative span after a clock adjustment recovers nothing",
            Math.Abs(Energy.Recovered(50f, -10f, 2f) - 50f) < .001f);

        // The hysteresis, which is the whole reason there are two numbers. Tired below 20,
        // rested above 60.
        Case("a working villager keeps working until it is tired",
            !Energy.ShouldRest(25f, false, 20f, 60f));
        Case("and stops once it is",
            Energy.ShouldRest(19f, false, 20f, 60f));
        Case("a resting villager keeps resting past the point it stopped at",
            Energy.ShouldRest(25f, true, 20f, 60f));
        Case("and goes back to work once properly rested",
            !Energy.ShouldRest(61f, true, 20f, 60f));

        // Control: with one threshold this is the flicker. A villager that stopped at 20 and
        // recovered a hundredth of a point would start work, spend one action, and stop again.
        Case("control: the two thresholds genuinely differ, or a villager flickers",
            Energy.ShouldRest(21f, true, 20f, 60f) && !Energy.ShouldRest(21f, false, 20f, 60f));
    }

    /// <summary>Saying a thing that keeps being true, without saying it constantly.</summary>
    static void Repeating()
    {
        Console.WriteLine("repeats");

        Case("the first time something happens is said immediately",
            Repeats.DueAgain(1000d));
        Case("and the next one, a moment later, is not",
            !Repeats.DueAgain(.1d));
        Case("but silence earns the right to speak again",
            Repeats.DueAgain(Repeats.QuietForSeconds));

        Said("a single occurrence is said plainly", "nowhere to put Wood", 1, 30d,
            "nowhere to put Wood");
        Said("a pile of them is summarised", "nowhere to put Wood", 47, 120d,
            "nowhere to put Wood (47 times in the last 2 minutes)");

        // Spans as a person would say them, not as a machine would.
        Spans("seconds stay seconds", 45d, "45 seconds");
        Spans("a minute and a half rounds to minutes", 92d, "2 minutes");
        Spans("one minute is singular", 60d, "60 seconds");
        // Minutes hold on until well past the hour, on purpose: "60 minutes" is exactly true
        // and "an hour" would be a rounding, which is the wrong trade in a line whose whole job
        // is to say how long something has been going wrong.
        Spans("an hour is still counted in minutes, which is exact", 3600d, "60 minutes");
        Spans("and past that it counts hours", 7400d, "2 hours");
        Spans("something instantaneous is a moment", .2d, "moment");

        // Control: summarising must actually depend on the count, or it would be decoration.
        Case("control: two occurrences read differently from one",
            Repeats.Summarise("x", 2, 30d) != Repeats.Summarise("x", 1, 30d));
    }

    static void Said(string what, string message, int times, double span, string expected)
    {
        string actual = Repeats.Summarise(message, times, span);
        Case($"{what} (got \"{actual}\")", actual == expected);
    }

    static void Spans(string what, double seconds, string expected)
    {
        string actual = Repeats.Span(seconds);
        Case($"{what} (got \"{actual}\")", actual == expected);
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
        bool atDestination = false, bool carrying = false, bool bagFull = false, bool tired = false,
        bool fillBagFirst = false) =>
        new HaulFacts(hasSource, hasDestination, atSource, atDestination, carrying, bagFull, tired,
            fillBagFirst);

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

    static void Chopping()
    {
        Console.WriteLine("chopping");

        // The ordinary way round: find something, walk to it, hit it until it is not there.
        Step("a villager with an axe and nothing chosen goes looking",
            ChopState.Choosing, Chop(hasTool: true), ChopAction.ChooseWork);
        Step("having chosen, it walks",
            ChopState.Approaching, Chop(hasTool: true, hasTarget: true), ChopAction.MoveToTarget);
        Step("arriving, it swings",
            ChopState.Approaching, Chop(hasTool: true, hasTarget: true, atTarget: true), ChopAction.Chop);
        Step("and keeps swinging",
            ChopState.Chopping, Chop(hasTool: true, hasTarget: true, atTarget: true), ChopAction.Chop);

        // The target going away is what success looks like here. A job built on hauling's
        // shape would call this a failure and report every felled tree as an error.
        Step("the target being gone finishes the trip rather than failing it",
            ChopState.Chopping, Chop(hasTool: true), ChopAction.Complete);
        Step("control: a target still standing is not a finished trip",
            ChopState.Chopping, Chop(hasTool: true, hasTarget: true, atTarget: true), ChopAction.Chop);

        // Three ordinary reasons to do nothing, none of them a failure, none of them
        // spending a repetition.
        Step("no axe means no work rather than a failed job",
            ChopState.Choosing, Chop(), ChopAction.Yield);
        Step("a tired villager yields even with an axe and work to do",
            ChopState.Choosing, Chop(hasTool: true, tired: true), ChopAction.Yield);
        Step("enough in store means there is nothing worth cutting",
            ChopState.Choosing, Chop(hasTool: true, enough: true), ChopAction.Yield);
        Step("control: below that, the same villager goes looking",
            ChopState.Choosing, Chop(hasTool: true), ChopAction.ChooseWork);

        // Having enough is checked when choosing, not mid-trunk. A villager that abandoned a
        // half-chopped tree the moment the store filled would leave it standing at half
        // health for the next one to start again from.
        Step("a villager already at a tree finishes it even once the store is full",
            ChopState.Chopping, Chop(hasTool: true, hasTarget: true, atTarget: true, enough: true),
            ChopAction.Chop);

        // Losing the axe mid-tree. Swinging an empty hand forever is indistinguishable from
        // working, which is the whole reason this branch exists.
        Step("losing the axe mid-tree stops the swinging",
            ChopState.Chopping, Chop(hasTarget: true, atTarget: true), ChopAction.Yield);

        // Pushed off the trunk by a falling log, or shoved by anything else.
        Step("a villager knocked away from its tree walks back",
            ChopState.Chopping, Chop(hasTool: true, hasTarget: true), ChopAction.MoveToTarget);

        // Facts outrank the recorded state: each of these resumes into a state the world has
        // already moved past.
        Step("a target taken by somebody else sends it back to choosing",
            ChopState.Approaching, Chop(hasTool: true), ChopAction.ChooseWork);
        Step("an unknown state restarts rather than acting",
            (ChopState)99, Chop(hasTool: true), ChopAction.ChooseWork);

        // Control: the table always terminates. If it ever gains a cycle this is what catches
        // it, because the guard's fallback is the only path to a Yield with work available.
        Case("control: a decision is always reached with an axe and a target",
            ChopTransitions.Next(ChopState.Choosing, Chop(hasTool: true, hasTarget: true)).Action
                != ChopAction.Yield);

        // Exhaustive, because the cost of one wrong branch here is a villager swinging at
        // nothing forever and looking busy while it does. Sixty-four combinations is cheap.
        int swingingWithoutAnAxe = 0;
        for (int bits = 0; bits < 32; bits++)
        {
            ChopFacts facts = Chop(
                hasTool: (bits & 1) != 0,
                hasTarget: (bits & 2) != 0,
                atTarget: (bits & 4) != 0,
                enough: (bits & 8) != 0,
                tired: (bits & 16) != 0);

            foreach (ChopState from in new[] { ChopState.Choosing, ChopState.Approaching, ChopState.Chopping })
            {
                if (facts.HasTool) continue;
                if (ChopTransitions.Next(from, facts).Action == ChopAction.Chop) swingingWithoutAnAxe++;
            }
        }

        Case($"no combination of facts ever swings without an axe (swung in {swingingWithoutAnAxe})",
            swingingWithoutAnAxe == 0);
    }

    /// <summary>
    ///     What a station is short of, which is the whole of the tending job's honesty.
    /// </summary>
    /// <remarks>
    ///     Every rule here has its positive control beside it. "An idle smelter is not stoked"
    ///     passes for a function that always answers zero, so the case after it asks the same
    ///     question of a station that <em>is</em> running and requires a number back.
    /// </remarks>
    static void Appetite()
    {
        Console.WriteLine("station appetite");

        // The rule this class exists for: nothing queued means nothing to burn for, whatever
        // the station could hold.
        Case("an idle station wants no fuel",
            StationAppetite.FuelWanted(queued: 0, fuelPerProduct: 4, maxFuel: 10, fuel: 0f) == 0);

        // The control. Without it the line above passes for a function that never wants fuel.
        Case("control: a station with one thing queued wants fuel for it",
            StationAppetite.FuelWanted(1, 4, 10, 0f) == 4);

        Case("fuel is capped by what the station holds, not by what the queue would burn",
            StationAppetite.FuelWanted(5, 4, 10, 0f) == 10);

        Case("fuel already burning counts against the ceiling",
            StationAppetite.FuelWanted(2, 4, 10, 6f) == 2);

        Case("a station fuller than its queue justifies wants none",
            StationAppetite.FuelWanted(2, 4, 10, 8f) == 0);

        Case("an over-full station never wants a negative amount",
            StationAppetite.FuelWanted(3, 4, 10, 12f) == 0);

        Case("half a log short of the ceiling is left alone rather than overshot",
            StationAppetite.FuelWanted(1, 4, 10, 3.5f) == 0);

        Case("a station that burns nothing is never fuelled",
            StationAppetite.FuelWanted(5, 4, maxFuel: 0, fuel: 0f) == 0);

        // A queue size is read from a ZDO, so it is not a number this code chose. In 32 bits
        // this product wraps negative and the answer becomes "wants nothing" by luck.
        Case("a queue large enough to overflow the product is still capped, not wrapped",
            StationAppetite.FuelWanted(int.MaxValue, 4, 10, 0f) == 10);

        Case("a station with no fuel-per-product burns up to its own capacity",
            StationAppetite.FuelWanted(1, fuelPerProduct: 0, maxFuel: 10, fuel: 0f) == 10);

        // The terminus. At the line there is no work, which is what makes "keep it half full"
        // a stopping rule rather than a rate.
        Case("an empty station wants material up to the line", StationAppetite.InputWanted(0, 10, .5f) == 5);
        Case("a station at the line wants nothing", StationAppetite.InputWanted(5, 10, .5f) == 0);
        Case("a station above the line wants nothing", StationAppetite.InputWanted(6, 10, .5f) == 0);
        Case("zero per cent is a real setting and asks for nothing",
            StationAppetite.InputWanted(0, 10, 0f) == 0);
        Case("a hundred per cent asks for the whole capacity", StationAppetite.InputWanted(0, 10, 1f) == 10);
        Case("what is wanted is bounded by the room left", StationAppetite.InputWanted(9, 10, 1f) == 1);
        Case("a station with no capacity wants nothing", StationAppetite.InputWanted(0, 0, 1f) == 0);
        Case("a fraction outside the range is clamped rather than trusted",
            StationAppetite.InputWanted(0, 10, 5f) == 10 && StationAppetite.InputWanted(0, 10, -1f) == 0);

        // The screen shows the player this exact number, so the two must agree. Mathf.RoundToInt
        // is (int)Math.Round, which rounds halves to even - 25% of 10 is 2, not 3.
        Case("the target is the number the structure screen shows", StationAppetite.TargetQueue(10, .25f) == 2);
        Case("control: three quarters of ten rounds the other way", StationAppetite.TargetQueue(10, .75f) == 8);
    }

    /// <summary>
    ///     The tending state machine, exhaustively.
    /// </summary>
    static void Tending()
    {
        Console.WriteLine("tending");

        Step("with nothing chosen it looks for work",
            TendState.Choosing, Tend(), TendAction.ChooseWork);

        Step("with a station wanting something and a chest to get it from, it sets off",
            TendState.Choosing, Tend(hasStation: true, stationWants: true, hasSupply: true),
            TendAction.MoveToSupply);

        Step("at the chest it takes what it came for",
            TendState.Fetching, Tend(hasStation: true, stationWants: true, hasSupply: true, atSupply: true),
            TendAction.Collect);

        Step("having taken it, it carries it to the station",
            TendState.Collecting, Tend(hasStation: true, stationWants: true, carrying: true),
            TendAction.MoveToStation);

        Step("at the station it puts it in",
            TendState.Delivering, Tend(hasStation: true, stationWants: true, carrying: true, atStation: true),
            TendAction.Feed);

        Step("and keeps putting it in while the station still wants it",
            TendState.Feeding, Tend(hasStation: true, stationWants: true, carrying: true, atStation: true),
            TendAction.Feed);

        Step("an empty-handed villager at the station has finished the trip",
            TendState.Feeding, Tend(hasStation: true, atStation: true), TendAction.Complete);

        // Clearing, which is the half that makes cooking possible at all.
        Step("a station holding finished work is cleared before anything is fetched for it",
            TendState.Choosing, Tend(hasStation: true, stationWants: true, hasSupply: true, stationHasOutput: true),
            TendAction.MoveToStation);

        Step("standing at it, the finished work comes off",
            TendState.Choosing, Tend(hasStation: true, stationHasOutput: true, atStation: true),
            TendAction.TakeOutput);

        Step("and it keeps coming off until there is none",
            TendState.Clearing, Tend(hasStation: true, stationHasOutput: true, atStation: true),
            TendAction.TakeOutput);

        Step("a station somebody else cleared sends it back to choosing",
            TendState.Clearing, Tend(hasStation: true, atStation: true), TendAction.ChooseWork);

        // Facts outrank the recorded state.
        Step("a villager that reloads carrying something delivers it rather than fetching more",
            TendState.Fetching, Tend(hasStation: true, stationWants: true, hasSupply: true, carrying: true),
            TendAction.MoveToStation);

        Step("a station that filled while it walked sends it looking for somewhere else",
            TendState.Delivering, Tend(hasStation: true, carrying: true), TendAction.ChooseWork);

        Step("a station that filled under its hands does the same",
            TendState.Feeding, Tend(hasStation: true, carrying: true, atStation: true), TendAction.ChooseWork);

        Step("a station destroyed mid-trip does not leave it walking to a hole in the ground",
            TendState.Delivering, Tend(carrying: true), TendAction.ChooseWork);

        Step("a chest emptied by somebody else sends it back to choosing",
            TendState.Fetching, Tend(hasStation: true, stationWants: true), TendAction.ChooseWork);

        Step("standing at the chest it may still top up",
            TendState.Collecting, Tend(hasStation: true, stationWants: true, hasSupply: true,
                atSupply: true, carrying: true), TendAction.Collect);

        Step("shoved away from the station mid-feed, it walks back",
            TendState.Feeding, Tend(hasStation: true, stationWants: true, carrying: true),
            TendAction.MoveToStation);

        Step("a satisfied station is given up rather than walked to",
            TendState.Choosing, Tend(hasStation: true, hasSupply: true), TendAction.ChooseWork);

        Step("wanting something nowhere holds is a look for work, not a walk",
            TendState.Choosing, Tend(hasStation: true, stationWants: true), TendAction.ChooseWork);

        Step("being tired yields, however much there is to do",
            TendState.Choosing, Tend(hasStation: true, stationWants: true, hasSupply: true, tired: true),
            TendAction.Yield);

        Step("a state written by an older build starts over rather than acting",
            (TendState)99, Tend(hasStation: true, stationWants: true, hasSupply: true),
            TendAction.MoveToSupply);

        // Every combination, against the four things that must never happen. A settlement that
        // feeds a station nobody asked it to is the failure this whole job is arranged against,
        // so it is asserted over the facts rather than over a happy path.
        int fedWithNothing = 0, fedUnwanted = 0, fedFromAfar = 0, tookFromNowhere = 0, clearedNothing = 0;
        TendState[] states =
        {
            TendState.Choosing, TendState.Fetching, TendState.Collecting,
            TendState.Delivering, TendState.Feeding, TendState.Clearing
        };

        for (int bits = 0; bits < 256; bits++)
        {
            TendFacts facts = new TendFacts(
                hasSupply: (bits & 1) != 0,
                hasStation: (bits & 2) != 0,
                atSupply: (bits & 4) != 0,
                atStation: (bits & 8) != 0,
                carrying: (bits & 16) != 0,
                stationWants: (bits & 32) != 0,
                stationHasOutput: (bits & 64) != 0,
                tired: (bits & 128) != 0);

            foreach (TendState from in states)
            {
                TendAction action = TendTransitions.Next(from, facts).Action;

                if (action == TendAction.Feed)
                {
                    if (!facts.Carrying) fedWithNothing++;
                    if (!facts.StationWants) fedUnwanted++;
                    if (!facts.AtStation) fedFromAfar++;
                }

                if (action == TendAction.Collect && !(facts.HasSupply && facts.AtSupply)) tookFromNowhere++;
                if (action == TendAction.TakeOutput && !(facts.StationHasOutput && facts.AtStation)) clearedNothing++;
            }
        }

        Case($"no combination ever feeds a station empty-handed (did {fedWithNothing})", fedWithNothing == 0);
        Case($"no combination ever feeds a station that wants nothing (did {fedUnwanted})", fedUnwanted == 0);
        Case($"no combination ever feeds a station it is not standing at (did {fedFromAfar})", fedFromAfar == 0);
        Case($"no combination ever takes from a chest it is not at (did {tookFromNowhere})", tookFromNowhere == 0);
        Case($"no combination ever clears a station with nothing on it (did {clearedNothing})", clearedNothing == 0);
    }

    static TendFacts Tend(bool hasSupply = false, bool hasStation = false, bool atSupply = false,
        bool atStation = false, bool carrying = false, bool stationWants = false,
        bool stationHasOutput = false, bool tired = false) =>
        new TendFacts(hasSupply, hasStation, atSupply, atStation, carrying, stationWants,
            stationHasOutput, tired);

    static void Step(string what, TendState state, TendFacts facts, TendAction expected)
    {
        TendAction actual = TendTransitions.Next(state, facts).Action;
        Case($"{what} (got {actual})", actual == expected);
    }

    static ChopFacts Chop(bool hasTool = false, bool hasTarget = false, bool atTarget = false,
        bool enough = false, bool tired = false) =>
        new ChopFacts(hasTool, hasTarget, atTarget, enough, tired);

    static void Step(string what, ChopState state, ChopFacts facts, ChopAction expected)
    {
        ChopAction actual = ChopTransitions.Next(state, facts).Action;
        Case($"{what} (got {actual})", actual == expected);
    }

    static void Case(string what, bool passed)
    {
        _cases++;
        if (passed) return;
        _failed++;
        Console.WriteLine($"  [FAIL] {what}");
    }
}
