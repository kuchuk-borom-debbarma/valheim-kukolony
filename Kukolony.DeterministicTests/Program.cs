using Kukolony.Core;
using Kukolony.Colonies;
using Kukolony.Jobs;
using Kukolony.Villagers.Navigation;
using Kukolony.Villagers;
using Kukolony.Jobs.Haul;
using Kukolony.Jobs.Chop;
using Kukolony.Jobs.Tend;
using Kukolony.Jobs.Craft;
using Kukolony.Jobs.Mine;
using Kukolony.Jobs.Forage;
using Kukolony.Jobs.Farm;
using Kukolony.Jobs.Repair;

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

        // The same control for every bit added since, asked once rather than a line per
        // capability - the next one to be added is covered by this without anybody remembering.
        Case("no capability this build knows sits on a retired bit",
            ((int)StructureCapabilities.Known & (2 | 8 | 16 | 32)) == 0);

        Mask("a field record reads as a field", 512, StructureCapability.Field);

        // A field is a place *and* can be other things: nothing stops somebody registering a
        // piece that is both. The mask must carry them together rather than choosing one.
        Mask("a field that is also storage keeps both", 1 | 512,
            StructureCapability.Storage | StructureCapability.Field);

        Label("no longer understood", StructureCapability.None);
        Label("Storage", StructureCapability.Storage);
        Label("Rest", StructureCapability.Rest);
        Label("Storage + Processing", StructureCapability.Storage | StructureCapability.Processing);
        // A retired bit must not reach the player as a name, or as a blank row.
        Label("no longer understood", (StructureCapability)2);
        Label("Storage", (StructureCapability)(1 | 32));
        Label("Field", StructureCapability.Field);
        Label("Storage + Field", StructureCapability.Storage | StructureCapability.Field);

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
        OrderArithmetic();
        CraftingPlan();
        Crafting();
        MiningParts();
        Mining();
        Foraging();
        FieldOrders_();
        FieldLayout();
        Farming();
        Mending();
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

        // Escalation, and the gentle option comes first whether or not anybody is watching.
        // It used to go straight to covering ground when unseen, on the reasoning that there
        // was nobody to be polite for - but politeness was never the point. Being put back on
        // the navmesh is a step sideways; covering ground is a body sliding across country, and
        // a settlement that does that unwatched is one whose villagers arrive by means nobody
        // would accept if they saw it.
        Case("stalled and unobserved is still put back on the navmesh rather than carried",
            Locomotor.Decide(new TravelFacts(false, true, true, false, true, true, true))
                == Locomotion.PutBackOnNavmesh);
        Case("stalled in view tries the gentle option first",
            Locomotor.Decide(new TravelFacts(false, true, true, true, true, true, true))
                == Locomotion.PutBackOnNavmesh);

        // And a villager that can still walk somewhere is never carried at all, which is the
        // rule the whole corridor exists to make true: a stall while holding a full path to the
        // next leg is the navmesh building or a doorway blocked, not a villager that needs
        // lifting over the terrain.
        Case("stalled while holding a walkable route keeps walking",
            Locomotor.Decide(new TravelFacts(false, true, true, false, true, false, false,
                hasRoute: true)) == Locomotion.Walk);
        Case("control: the same stall without a route does not keep walking",
            Locomotor.Decide(new TravelFacts(false, true, true, false, true, false, false))
                != Locomotion.Walk);
        Case("water ahead is not crossed by carrying when there is a way round",
            Locomotor.Decide(new TravelFacts(false, true, false, false, false, true, true,
                waterAhead: true, hasRoute: true)) == Locomotion.Walk);
        Case("control: water ahead with no way round is still crossed",
            Locomotor.Decide(new TravelFacts(false, true, false, false, false, true, true,
                waterAhead: true)) == Locomotion.BeginRescue);

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
    ///     What a station's orders still want.
    /// </summary>
    /// <remarks>
    ///     The stop rule for everything a settlement produces, and the one place the difference
    ///     between a standing order and a one-off is decided. Worth checking without a game
    ///     because getting it wrong is not visible as a failure: it is a kiln that quietly never
    ///     stops, or a forge that quietly never starts.
    /// </remarks>
    static void OrderArithmetic()
    {
        Console.WriteLine("station orders");

        Func<string, int> nothing = _ => 0;
        Func<string, int> hundred = _ => 100;

        // A station nobody has given an order to has no limit - not a limit already reached.
        // The distinction is the whole of this function: a kiln registered before orders
        // existed must go on being fed.
        Case("a station with no orders always wants more",
            Orders.WantsMore(new List<StructureOrder>(), hundred));

        List<StructureOrder> fifty = new List<StructureOrder>
            { new StructureOrder { Item = "Coal", Count = 50 } };

        Case("an order wants more while the settlement is short", Orders.WantsMore(fifty, nothing));
        Case("and stops once it has enough", !Orders.WantsMore(fifty, hundred));

        // Maintain resumes; that is what makes it a standing order rather than a finished one.
        Case("a standing order starts again when the stock is spent",
            Orders.WantsMore(fifty, _ => 49));

        Case("exactly the target is enough", !Orders.WantsMore(fifty, _ => 50));

        List<StructureOrder> once = new List<StructureOrder>
            { new StructureOrder { Item = "Nails", Count = 20, Mode = OrderMode.Once } };

        Case("a one-off order wants making before it is made", Orders.WantsMore(once, nothing));

        once[0].Done = true;
        Case("and never again once it is latched, however empty the shelves",
            !Orders.WantsMore(once, nothing));

        // The difference between the two modes, asserted against each other rather than
        // separately - the failure worth catching is one becoming the other.
        Case("control: the same emptiness restarts a standing order",
            Orders.WantsMore(fifty, nothing) && !Orders.WantsMore(once, nothing));

        Case("an order for nothing is not an order",
            !Orders.WantsMore(new List<StructureOrder>
                { new StructureOrder { Item = "Coal", Count = 0 } }, nothing));

        Case("an order for nothing in particular is not an order",
            !Orders.WantsMore(new List<StructureOrder>
                { new StructureOrder { Item = string.Empty, Count = 10 } }, nothing));

        // One outstanding line is enough to keep a station working, which is what lets a forge
        // hold a finished order and a live one at the same time.
        List<StructureOrder> mixed = new List<StructureOrder>
        {
            new StructureOrder { Item = "Nails", Count = 20, Mode = OrderMode.Once, Done = true },
            new StructureOrder { Item = "Coal", Count = 50 }
        };
        Case("a finished line does not silence the one beside it",
            Orders.WantsMore(mixed, _ => 10));

        List<StructureOrder> outstanding = Orders.Outstanding(mixed, _ => 10);
        Case("and only the line that still wants something is offered",
            outstanding.Count == 1 && outstanding[0].Item == "Coal");

        Case("nothing is outstanding when everything is satisfied",
            Orders.Outstanding(mixed, hundred).Count == 0);
    }


    /// <summary>
    ///     How much a villager should make, and whether it can.
    /// </summary>
    static void CraftingPlan()
    {
        Console.WriteLine("craft plan");

        List<CraftNeed> nail = new List<CraftNeed> { new CraftNeed("Iron", 1) };
        Func<string, int> plenty = _ => 1000;
        Func<string, int> none = _ => 0;

        // Enough and Missing answer the two questions a villager asks either side of a walk.
        Case("everything to hand is enough", CraftPlan.Enough(nail, plenty));
        Case("nothing to hand is not", !CraftPlan.Enough(nail, none));

        Case("short of one of two is not enough",
            !CraftPlan.Enough(new List<CraftNeed>
            {
                new CraftNeed("Iron", 2),
                new CraftNeed("Wood", 1)
            }, item => item == "Iron" ? 1 : 100));

        Case("the thing it is short of is the thing it names",
            CraftPlan.Missing(new List<CraftNeed>
            {
                new CraftNeed("Wood", 1),
                new CraftNeed("Iron", 2)
            }, item => item == "Iron" ? 1 : 100) == "Iron");

        Case("and it names nothing when it is short of nothing",
            CraftPlan.Missing(nail, plenty) == string.Empty);
    }

    static CraftFacts Craft(bool hasStation = false, bool hasSupply = false, bool atSupply = false,
        bool atStation = false, bool hasMaterials = false, bool stationUsable = true,
        bool wantsMade = true, bool bagRoom = true, bool tired = false) =>
        new CraftFacts(hasStation, hasSupply, atSupply, atStation, hasMaterials, stationUsable,
            wantsMade, bagRoom, tired);

    static void Step(string what, CraftState state, CraftFacts facts, CraftAction expected)
    {
        CraftAction actual = CraftTransitions.Next(state, facts).Action;
        Case($"{what} (got {actual})", actual == expected);
    }

    /// <summary>
    ///     The crafting state machine, exhaustively.
    /// </summary>
    static void Crafting()
    {
        Console.WriteLine("crafting");

        Step("with nothing chosen it looks for work", CraftState.Choosing, Craft(), CraftAction.ChooseWork);

        Step("with a station wanting something and a chest to fetch from, it sets off",
            CraftState.Choosing, Craft(hasStation: true, hasSupply: true), CraftAction.MoveToSupply);

        Step("at the chest it takes what the recipe needs",
            CraftState.Fetching, Craft(hasStation: true, hasSupply: true, atSupply: true),
            CraftAction.Collect);

        Step("holding the materials it carries them to the station",
            CraftState.Collecting, Craft(hasStation: true, hasMaterials: true),
            CraftAction.MoveToStation);

        Step("at the station it makes one",
            CraftState.Delivering, Craft(hasStation: true, hasMaterials: true, atStation: true),
            CraftAction.Craft);

        Step("and goes on making while it still has materials",
            CraftState.Working, Craft(hasStation: true, hasMaterials: true, atStation: true),
            CraftAction.Craft);

        Step("materials spent, the trip is over",
            CraftState.Working, Craft(hasStation: true, atStation: true), CraftAction.Complete);

        // The three ways a station stops being worth walking to, each of which must send the
        // villager back to choosing rather than on to a forge that cannot help it.
        Step("a station that lost its fire is abandoned mid-walk",
            CraftState.Delivering, Craft(hasStation: true, hasMaterials: true, stationUsable: false),
            CraftAction.ChooseWork);

        Step("an order filled by somebody else is abandoned mid-walk",
            CraftState.Delivering, Craft(hasStation: true, hasMaterials: true, wantsMade: false),
            CraftAction.ChooseWork);

        Step("a station that is gone is abandoned mid-walk",
            CraftState.Delivering, Craft(hasMaterials: true), CraftAction.ChooseWork);

        // A full bag stops the job where it stands. The product waits with its maker until a
        // hauler comes, which is the arrangement - so this is a yield and not a delivery.
        Step("a full bag stops the job rather than diverting it",
            CraftState.Choosing, Craft(hasStation: true, hasSupply: true, bagRoom: false),
            CraftAction.Yield);

        Step("and stops it between crafts, because the last one can be the one that fills it",
            CraftState.Working, Craft(hasStation: true, hasMaterials: true, atStation: true, bagRoom: false),
            CraftAction.Complete);

        Step("being tired is not a failure", CraftState.Choosing,
            Craft(hasStation: true, hasSupply: true, tired: true), CraftAction.Yield);

        // Already holding what it needs - a reload mid-trip. Going back for more would fetch a
        // second set of materials nothing asked for.
        Step("a villager that reloads holding materials carries on to the station",
            CraftState.Choosing, Craft(hasStation: true, hasMaterials: true, hasSupply: true),
            CraftAction.MoveToStation);

        // Reached with the state recorded but standing nowhere near the chest.
        Step("it will not take from a chest it is not at",
            CraftState.Collecting, Craft(hasStation: true, hasSupply: true), CraftAction.MoveToSupply);

        Step("an unknown state starts over", (CraftState)99, Craft(), CraftAction.ChooseWork);

        // Every combination, against the things that must never happen.
        int madeWithNothing = 0, madeUnwanted = 0, madeFromAfar = 0, madeUnusable = 0;
        int madeWithNoRoom = 0, tookFromNowhere = 0, idle = 0;

        CraftState[] states =
        {
            CraftState.Choosing, CraftState.Fetching, CraftState.Collecting,
            CraftState.Delivering, CraftState.Working
        };

        for (int bits = 0; bits < 512; bits++)
        {
            CraftFacts facts = new CraftFacts(
                hasStation: (bits & 1) != 0,
                hasSupply: (bits & 2) != 0,
                atSupply: (bits & 4) != 0,
                atStation: (bits & 8) != 0,
                hasMaterials: (bits & 16) != 0,
                stationUsable: (bits & 32) != 0,
                wantsMade: (bits & 64) != 0,
                bagRoom: (bits & 128) != 0,
                tired: (bits & 256) != 0);

            foreach (CraftState from in states)
            {
                CraftStep step = CraftTransitions.Next(from, facts);

                if (step.Action == CraftAction.Craft)
                {
                    if (!facts.HasMaterials) madeWithNothing++;
                    if (!facts.WantsMade) madeUnwanted++;
                    if (!facts.AtStation) madeFromAfar++;
                    if (!facts.StationUsable) madeUnusable++;
                    if (!facts.BagRoom) madeWithNoRoom++;
                }

                if (step.Action == CraftAction.Collect && !(facts.HasSupply && facts.AtSupply))
                {
                    tookFromNowhere++;
                }
            }

            // Every combination produces an action. A state machine that can fall through
            // returns whatever the default was and the villager stands still for ever.
            foreach (CraftState from in states)
            {
                if (!Enum.IsDefined(typeof(CraftAction), CraftTransitions.Next(from, facts).Action)) idle++;
            }
        }

        Case($"no combination ever crafts empty-handed (did {madeWithNothing})", madeWithNothing == 0);
        Case($"no combination ever crafts what nothing wants (did {madeUnwanted})", madeUnwanted == 0);
        Case($"no combination ever crafts away from the station (did {madeFromAfar})", madeFromAfar == 0);
        Case($"no combination ever crafts at an unusable station (did {madeUnusable})", madeUnusable == 0);
        Case($"no combination ever crafts with nowhere to put it (did {madeWithNoRoom})", madeWithNoRoom == 0);
        Case($"no combination ever takes from a chest it is not at (did {tookFromNowhere})", tookFromNowhere == 0);
        Case($"every combination produces an action (undefined in {idle})", idle == 0);
    }

    /// <summary>
    ///     What a station is short of, which is the whole of the tending job's honesty.
    /// </summary>
    /// <remarks>
    ///     Every rule here has its positive control beside it. "An idle smelter is not stoked"
    ///     passes for a function that always answers zero, so the case after it asks the same
    ///     question of a station that <em>is</em> running and requires a number back.
    /// </remarks>

    /// <summary>
    ///     Which part of a deposit to work next.
    /// </summary>
    /// <remarks>
    ///     The one idea mining has that chopping does not: a tree is a target, a silver vein is
    ///     forty targets wearing one name. Worth checking without a game because the failure is
    ///     invisible - a villager that keeps choosing a part which is no longer there looks
    ///     exactly like one that is working.
    /// </remarks>
    static void MiningParts()
    {
        Console.WriteLine("mining parts");

        List<Spot> none = new List<Spot>();
        Case("a deposit with no parts left is finished", MineTargets.Finished(none));
        Case("and offers nothing to hit", !MineTargets.Nearest(none, 0f, 0f, out Spot _));

        List<Spot> vein = new List<Spot>
        {
            new Spot(0, 10f, 0f),
            new Spot(1, 2f, 0f),
            new Spot(2, 5f, 0f)
        };

        Case("control: a deposit with parts is not finished", !MineTargets.Finished(vein));

        Case("the nearest part is the one chosen",
            MineTargets.Nearest(vein, 0f, 0f, out Spot near) && near.Index == 1);

        // Nearest to the *villager*, not to the deposit: the whole point is that a villager
        // works the rock in front of it and follows the face round as parts fall.
        Case("and nearest means nearest to where the villager stands",
            MineTargets.Nearest(vein, 12f, 0f, out Spot far) && far.Index == 0);

        // Flat and three-dimensional must disagree here, or this asserts nothing: the part
        // overhead is nearer in the plane and further in space, so only a flat measure picks it.
        List<Spot> overhead = new List<Spot> { new Spot(7, 1f, 0f), new Spot(8, 2f, 0f) };
        Case("distance is measured flat, so a part overhead is the one at hand",
            MineTargets.Nearest(overhead, 0f, 0f, out Spot above) && above.Index == 7);

        // The answer must *change* when the near part goes. Asserted as a pair, because the
        // one-element version of this case was unfalsifiable: MineTargets holds no state, so
        // there was nothing that could have remembered an index and nothing to catch.
        List<Spot> before = new List<Spot> { new Spot(1, 2f, 0f), new Spot(2, 5f, 0f) };
        List<Spot> after = new List<Spot> { new Spot(2, 5f, 0f) };

        Case("the near part is chosen while it stands",
            MineTargets.Nearest(before, 0f, 0f, out Spot near2) && near2.Index == 1);

        Case("and the next one over is chosen once it falls",
            MineTargets.Nearest(after, 0f, 0f, out Spot left) && left.Index == 2);

        // Stable under a tie, or a villager standing between two rocks shuffles between them.
        List<Spot> tied = new List<Spot> { new Spot(4, 3f, 0f), new Spot(9, -3f, 0f) };
        Case("a tie goes to the first, so the answer does not flicker",
            MineTargets.Nearest(tied, 0f, 0f, out Spot first) && first.Index == 4 &&
            MineTargets.Nearest(tied, 0f, 0f, out Spot again) && again.Index == 4);
    }

    static MineFacts Mine(bool hasTool = true, bool hasDeposit = false, bool hasArea = true,
        bool atArea = false, bool enough = false, bool tired = false) =>
        new MineFacts(hasTool, hasDeposit, hasArea, atArea, enough, tired);

    static void Step(string what, MineState state, MineFacts facts, MineAction expected)
    {
        MineAction actual = MineTransitions.Next(state, facts).Action;
        Case($"{what} (got {actual})", actual == expected);
    }

    /// <summary>The mining state machine, exhaustively.</summary>
    static void Mining()
    {
        Console.WriteLine("mining");

        Step("with nothing chosen it looks for work", MineState.Choosing, Mine(), MineAction.ChooseWork);

        Step("with a deposit chosen it walks to the part it is working",
            MineState.Choosing, Mine(hasDeposit: true), MineAction.MoveToArea);

        Step("standing at it, it swings",
            MineState.Approaching, Mine(hasDeposit: true, atArea: true), MineAction.Strike);

        Step("and goes on swinging",
            MineState.Mining, Mine(hasDeposit: true, atArea: true), MineAction.Strike);

        // The difference from chopping, in three cases. A deposit outlives its last part, so
        // "the rock is still there" is not "there is something to hit".
        Step("a deposit whose last part fell is finished, not walked to",
            MineState.Approaching, Mine(hasDeposit: true, hasArea: false), MineAction.Complete);

        Step("and finished mid-swing too",
            MineState.Mining, Mine(hasDeposit: true, hasArea: false, atArea: true), MineAction.Complete);

        Step("a deposit chosen and already empty is dropped before the walk starts",
            MineState.Choosing, Mine(hasDeposit: true, hasArea: false), MineAction.ChooseWork);

        // Parts fall that nobody struck, so the walk is re-aimed between blows rather than only
        // on arrival - the villager follows the face round the vein.
        Step("the part being worked can move, so it walks again between blows",
            MineState.Mining, Mine(hasDeposit: true, atArea: false), MineAction.MoveToArea);

        Step("no pickaxe is an ordinary answer, not a failure",
            MineState.Choosing, Mine(hasTool: false), MineAction.Yield);

        Step("losing the pickaxe mid-swing sends it back to be told there is nothing to do",
            MineState.Mining, Mine(hasTool: false, hasDeposit: true, atArea: true), MineAction.Yield);

        Step("having enough stops it", MineState.Choosing, Mine(enough: true), MineAction.Yield);
        Step("being tired stops it", MineState.Choosing, Mine(tired: true), MineAction.Yield);

        Step("a deposit that was destroyed under it ends the trip",
            MineState.Mining, Mine(hasDeposit: false, atArea: true), MineAction.Complete);

        // Deliberate, and previously unasserted: having enough stops a villager *starting*,
        // never mid-vein. Abandoning half a deposit would leave the rock broken open and the
        // ore unmined, and mining is the one job where that cannot be undone.
        Step("a villager already at a vein finishes it even once the store is full",
            MineState.Mining, Mine(hasDeposit: true, atArea: true, enough: true), MineAction.Strike);

        Step("and finishes it even once it is tired",
            MineState.Mining, Mine(hasDeposit: true, atArea: true, tired: true), MineAction.Strike);

        Step("an unknown state starts over", (MineState)99, Mine(), MineAction.ChooseWork);

        // Every combination. The four "never" properties below are all counted inside
        // `action == Strike`, so a table that never struck at all would satisfy every one of
        // them - which is why the liveness counter and the two-way check beside them are not
        // decoration. A suite that cannot tell a correct table from a dead one is not a suite.
        int struckWithNothing = 0, struckEmpty = 0, struckFromAfar = 0, struckWithoutTool = 0;
        int swung = 0, disagreed = 0, yieldedWithoutReason = 0;
        bool[] reached = new bool[5];
        MineState[] states = { MineState.Choosing, MineState.Approaching, MineState.Mining };

        for (int bits = 0; bits < 64; bits++)
        {
            MineFacts facts = new MineFacts(
                hasTool: (bits & 1) != 0,
                hasDeposit: (bits & 2) != 0,
                hasArea: (bits & 4) != 0,
                atArea: (bits & 8) != 0,
                enough: (bits & 16) != 0,
                tired: (bits & 32) != 0);

            foreach (MineState from in states)
            {
                MineAction action = MineTransitions.Next(from, facts).Action;

                reached[(int)action] = true;

                if (action == MineAction.Strike)
                {
                    swung++;
                    if (!facts.HasDeposit) struckWithNothing++;
                    if (!facts.HasArea) struckEmpty++;
                    if (!facts.AtArea) struckFromAfar++;
                    if (!facts.HasTool) struckWithoutTool++;
                }

                // The same rule stated as an if-and-only-if, so it fails for a table that
                // swings when it should not *and* for one that never swings. Written out here
                // rather than derived from the table, which would only prove the table agrees
                // with itself.
                bool should = facts.HasTool && facts.HasDeposit && facts.HasArea && facts.AtArea &&
                              (from != MineState.Choosing || (!facts.Enough && !facts.Tired));

                if (should != (action == MineAction.Strike)) disagreed++;

                // Yielding is for the three ordinary reasons and nothing else. Without this the
                // stock rule could be ignored entirely and every other case would still pass.
                //
                // Not conditioned on the state it started in: losing the pickaxe mid-swing sends
                // the villager back through the choosing arm and out again as a yield, which is
                // the table working. Requiring it to have *started* in Choosing failed forty
                // combinations that were all correct - the assertion was wrong, not the table.
                if (action == MineAction.Yield && !facts.Tired && !facts.Enough && facts.HasTool)
                {
                    yieldedWithoutReason++;
                }
            }
        }

        Case($"no combination ever swings at a deposit it has not got (did {struckWithNothing})",
            struckWithNothing == 0);
        Case($"no combination ever swings at a deposit with nothing left (did {struckEmpty})",
            struckEmpty == 0);
        Case($"no combination ever swings from away from the rock (did {struckFromAfar})",
            struckFromAfar == 0);
        Case($"no combination ever swings without a pickaxe (did {struckWithoutTool})",
            struckWithoutTool == 0);

        // The control the four above need. Every one of them is satisfied by a table that does
        // nothing at all, which is a suite that cannot fail.
        Case($"control: something does swing when everything is right (swung {swung} times)",
            swung > 0);

        Case($"swinging happens exactly when it should, and never otherwise ({disagreed} disagreed)",
            disagreed == 0);

        Case($"yielding is always for a reason ({yieldedWithoutReason} were not)",
            yieldedWithoutReason == 0);

        // Every arm is reachable. A reordered condition that made one dead - Complete, say -
        // would leave a villager unable to finish and no other case would notice.
        bool every = true;
        foreach (MineAction action in Enum.GetValues(typeof(MineAction))) every &= reached[(int)action];
        Case("every action the table can name is reached by some combination", every);
    }

    static ForageFacts Pick(bool hasTarget = false, bool ripe = true, bool atTarget = false,
        bool enough = false, bool tired = false) =>
        new ForageFacts(hasTarget, ripe, atTarget, enough, tired);

    static void Reach(string what, ForageState state, ForageFacts facts, ForageAction expected)
    {
        ForageAction actual = ForageTransitions.Next(state, facts).Action;
        Case($"{what} (got {actual})", actual == expected);
    }

    /// <summary>
    ///     The foraging state machine, exhaustively.
    /// </summary>
    /// <remarks>
    ///     The cases worth reading twice are the ones about ripeness, because that is the only
    ///     thing this table has that the other gathering tables do not. Everywhere else in this
    ///     mod finishing means the thing stopped existing; a picked bush is still a bush, so the
    ///     two questions must never collapse into one - and a table that collapsed them would
    ///     pass every case that did not ask.
    /// </remarks>
    static void Foraging()
    {
        Console.WriteLine("foraging");

        Reach("with nothing chosen it looks for work", ForageState.Choosing, Pick(),
            ForageAction.ChooseWork);

        Reach("with something chosen it walks to it", ForageState.Choosing, Pick(hasTarget: true),
            ForageAction.MoveToTarget);

        Reach("standing at it, it picks", ForageState.Approaching,
            Pick(hasTarget: true, atTarget: true), ForageAction.Pick);

        Reach("and goes on picking while there is anything on it", ForageState.Picking,
            Pick(hasTarget: true, atTarget: true), ForageAction.Pick);

        // The three that are this job's whole shape. A bush outlives being picked, so bare is
        // what finishing looks like - and it is finishing, not failure.
        Reach("a bush that is bare when the picking is done ends the trip", ForageState.Picking,
            Pick(hasTarget: true, ripe: false, atTarget: true), ForageAction.Complete);

        Reach("one stripped by somebody else while walking is dropped, not completed",
            ForageState.Approaching, Pick(hasTarget: true, ripe: false), ForageAction.ChooseWork);

        Reach("and one already bare when chosen is never walked to", ForageState.Choosing,
            Pick(hasTarget: true, ripe: false), ForageAction.ChooseWork);

        // The other way of finishing: some pickable things are destroyed rather than emptied.
        Reach("something destroyed under it ends the trip too", ForageState.Picking,
            Pick(hasTarget: false, atTarget: true), ForageAction.Complete);

        Reach("shoved away from it, it walks back", ForageState.Picking,
            Pick(hasTarget: true, atTarget: false), ForageAction.MoveToTarget);

        Reach("having enough stops it", ForageState.Choosing, Pick(enough: true), ForageAction.Yield);
        Reach("being tired stops it", ForageState.Choosing, Pick(tired: true), ForageAction.Yield);

        // Deliberate, and the same call chopping and mining make: a stopping rule stops a
        // villager starting, never mid-reach. Walking away from a bush already stood at wastes
        // the walk and leaves the berries.
        Reach("a villager already at a bush finishes it even once the store is full",
            ForageState.Picking, Pick(hasTarget: true, atTarget: true, enough: true),
            ForageAction.Pick);

        Reach("an unknown state starts over", (ForageState)99, Pick(), ForageAction.ChooseWork);

        // Every combination. The "never" properties below are all counted inside
        // `action == Pick`, so a table that never picked at all would satisfy every one of them -
        // which is why the liveness counter and the two-way check beside them are not
        // decoration. A suite that cannot tell a correct table from a dead one is not a suite.
        int pickedNothing = 0, pickedBare = 0, pickedFromAfar = 0, walkedToNothing = 0;
        int picked = 0, disagreed = 0, yieldedWithoutReason = 0, completedRipe = 0;
        bool[] reached = new bool[5];
        ForageState[] states = { ForageState.Choosing, ForageState.Approaching, ForageState.Picking };

        for (int bits = 0; bits < 32; bits++)
        {
            ForageFacts facts = new ForageFacts(
                hasTarget: (bits & 1) != 0,
                ripe: (bits & 2) != 0,
                atTarget: (bits & 4) != 0,
                enough: (bits & 8) != 0,
                tired: (bits & 16) != 0);

            foreach (ForageState from in states)
            {
                ForageAction action = ForageTransitions.Next(from, facts).Action;

                reached[(int)action] = true;

                if (action == ForageAction.Pick)
                {
                    picked++;
                    if (!facts.HasTarget) pickedNothing++;
                    if (!facts.Ripe) pickedBare++;
                    if (!facts.AtTarget) pickedFromAfar++;
                }

                // Finishing means there was nothing left to take. A table that reported a full
                // bush as done would strip nothing and look busy the whole time - and no case
                // above asks it of the choosing arm, which is where it would be easiest to get
                // wrong.
                if (action == ForageAction.Complete && facts.HasTarget && facts.Ripe) completedRipe++;

                // The engine walks to `held.transform.position`, so this arm carrying no target
                // is a null reference twenty times a second rather than a wrong answer. Asserted
                // here because the engine reads it off the table and cannot check it itself.
                if (action == ForageAction.MoveToTarget && !facts.HasTarget) walkedToNothing++;

                // The same rule stated as an if-and-only-if, so it fails for a table that picks
                // when it should not *and* for one that never picks. Written out here rather
                // than derived from the table, which would only prove the table agrees with
                // itself.
                bool should = facts.HasTarget && facts.Ripe && facts.AtTarget &&
                              (from != ForageState.Choosing || (!facts.Enough && !facts.Tired));

                if (should != (action == ForageAction.Pick)) disagreed++;

                // Yielding is for the two ordinary reasons and nothing else. Without this the
                // stock rule could be ignored entirely and every other case would still pass.
                if (action == ForageAction.Yield && !facts.Tired && !facts.Enough)
                {
                    yieldedWithoutReason++;
                }
            }
        }

        Case($"no combination ever reaches for something it has not got (did {pickedNothing})",
            pickedNothing == 0);
        Case($"no combination ever reaches for a bush with nothing on it (did {pickedBare})",
            pickedBare == 0);
        Case($"no combination ever reaches from away from it (did {pickedFromAfar})",
            pickedFromAfar == 0);
        Case($"nothing is ever reported finished while there is still something on it (did {completedRipe})",
            completedRipe == 0);
        Case($"no combination ever walks to a target it has not got (did {walkedToNothing})",
            walkedToNothing == 0);

        // The control the four above need. Every one of them is satisfied by a table that does
        // nothing at all, which is a suite that cannot fail.
        Case($"control: something does get picked when everything is right (picked {picked} times)",
            picked > 0);

        Case($"picking happens exactly when it should, and never otherwise ({disagreed} disagreed)",
            disagreed == 0);

        Case($"yielding is always for a reason ({yieldedWithoutReason} were not)",
            yieldedWithoutReason == 0);

        // Every arm is reachable. A reordered condition that made one dead - Complete, say -
        // would leave a villager unable to finish and no other case would notice.
        bool every = true;
        foreach (ForageAction action in Enum.GetValues(typeof(ForageAction))) every &= reached[(int)action];
        Case("every action the table can name is reached by some combination", every);
    }

    /// <summary>What a field has been told to grow, and when it is content.</summary>
    /// <remarks>
    ///     The three modes are the four stopping rules a settlement was asked for, minus the one
    ///     that is counted in the larder rather than in the ground - that one is the stock rule
    ///     every producing job already shares, and it is checked where that is checked.
    /// </remarks>
    static void FieldOrders_()
    {
        Console.WriteLine("field orders");

        FieldOrder fill = new FieldOrder { Plant = "sapling_carrot", Mode = SowMode.Fill };

        Case("a field told to fill wants more while there is room", fill.WantsMore(0, room: true));
        Case("and still wants more with a hundred already growing", fill.WantsMore(100, room: true));
        Case("but not once the ground is full", !fill.WantsMore(0, room: false));

        FieldOrder keep = new FieldOrder { Plant = "sapling_turnip", Mode = SowMode.Keep, Count = 5 };

        Case("keeping five wants more at four", keep.WantsMore(4, room: true));
        Case("and stops at five", !keep.WantsMore(5, room: true));

        // The rule that makes Keep worth having. Harvesting is what takes one out of the ground,
        // and a field told to keep five must notice - this is "replant what was harvested" and
        // it is the same arithmetic asked a moment later.
        Case("and wants one again the moment one is taken", keep.WantsMore(4, room: true));

        // Zero is not "keep none", it is an order nobody finished setting. Treating it as a
        // target would make an unfinished row silently stop the field.
        FieldOrder unset = new FieldOrder { Plant = "sapling_turnip", Mode = SowMode.Keep, Count = 0 };
        Case("a keep order with no number set does nothing", !unset.WantsMore(0, room: true));

        FieldOrder once = new FieldOrder { Plant = "Birch_Sapling", Mode = SowMode.Once, Count = 3 };

        Case("a one-off wants more until it has sown its number", once.WantsMore(0, room: true));
        once.Sown = 3;
        Case("and stops for good once it has", !once.WantsMore(0, room: true));

        // The whole difference between Once and Keep, stated as the thing that separates them: a
        // one-off that read the ground would start again the moment somebody harvested.
        Case("and does not start again when what it sowed is taken away",
            !once.WantsMore(0, room: true));

        Case("an order naming no plant is ignored",
            !new FieldOrder { Mode = SowMode.Fill }.WantsMore(0, room: true));

        // The list is read in the player's order, because the order on the screen is the only
        // thing that makes "carrots, then turnips" a sentence.
        FieldOrder carrots = new FieldOrder { Plant = "sapling_carrot", Mode = SowMode.Keep, Count = 2 };
        FieldOrder turnips = new FieldOrder { Plant = "sapling_turnip", Mode = SowMode.Keep, Count = 2 };
        List<FieldOrder> both = new List<FieldOrder> { carrots, turnips };

        Case("the first order that wants something is the one taken",
            FieldOrders.Next(both, _ => 0, room: true) == carrots);

        Case("and the next only once the first is content",
            FieldOrders.Next(both, plant => plant == "sapling_carrot" ? 2 : 0, room: true) == turnips);

        Case("a content field asks for nothing",
            FieldOrders.Next(both, _ => 2, room: true) == null);

        Case("and a full one asks for nothing whatever it was told",
            FieldOrders.Next(both, _ => 0, room: false) == null);
    }

    /// <summary>Where the next thing goes in a field.</summary>
    static void FieldLayout()
    {
        Console.WriteLine("field layout");

        List<Furrow> squares = new List<Furrow>();

        FieldPlan.Squares(radius: 5f, pitch: 1f, squares);
        Case($"a field is divided into squares (got {squares.Count})", squares.Count > 0);

        // Round, not square. The corners of the bounding box lie outside the circle a player
        // drew, and a plant there is outside the field.
        bool allInside = true;
        foreach (Furrow furrow in squares)
        {
            if (furrow.X * furrow.X + furrow.Z * furrow.Z > 5f * 5f + .001f) allInside = false;
        }

        Case("every square is inside the field's own edge", allInside);

        // The control the line above needs: a test that only checks "inside" passes for a plan
        // that returns the centre and nothing else.
        bool reachesTheEdge = false;
        foreach (Furrow furrow in squares)
        {
            if (furrow.X * furrow.X + furrow.Z * furrow.Z > 16f) reachesTheEdge = true;
        }

        Case("control: and the squares reach the edge rather than huddling at the centre",
            reachesTheEdge);

        // Spacing is the whole reason the pitch is per crop. Six to one between an oak and a
        // carrot, so a tighter pitch must genuinely fit more.
        int tight = FieldPlan.Capacity(radius: 6f, pitch: .5f);
        int loose = FieldPlan.Capacity(radius: 6f, pitch: 3f);

        Case($"a crop's tight pitch fits far more than an oak's ({tight} vs {loose})",
            tight > loose * 4);

        // No two squares closer than the pitch, which is what the pitch means. Checked over the
        // whole field rather than on a sample, because it is cheap here and impossible in game.
        FieldPlan.Squares(radius: 4f, pitch: 2f, squares);
        float closest = float.MaxValue;
        for (int i = 0; i < squares.Count; i++)
        {
            for (int j = i + 1; j < squares.Count; j++)
            {
                float dx = squares[i].X - squares[j].X;
                float dz = squares[i].Z - squares[j].Z;
                float distance = (float)Math.Sqrt(dx * dx + dz * dz);
                if (distance < closest) closest = distance;
            }
        }

        Case($"no two squares are closer than the pitch (closest {closest:0.##} of 2)",
            squares.Count < 2 || closest >= 2f - .001f);

        // Stable, because a square index is how two villagers name the same square. An order
        // that shifted between calls would have them claiming each other's ground.
        List<Furrow> again = new List<Furrow>();
        FieldPlan.Squares(radius: 4f, pitch: 2f, again);

        bool same = again.Count == squares.Count;
        for (int i = 0; same && i < squares.Count; i++)
        {
            same = again[i].Index == squares[i].Index &&
                   Math.Abs(again[i].X - squares[i].X) < .001f &&
                   Math.Abs(again[i].Z - squares[i].Z) < .001f;
        }

        Case("the same field divides the same way twice, so a square has a name", same);

        FieldPlan.Squares(radius: 5f, pitch: 1f, squares);

        Case("the first free square is offered",
            FieldPlan.Next(squares, _ => false, out Furrow first) && first.Index == squares[0].Index);

        // And the next one along once it is taken - the arm that makes a field fill rather than
        // a villager planting into the same square for ever.
        Case("and the next one along once that is taken",
            FieldPlan.Next(squares, index => index == squares[0].Index, out Furrow second) &&
            second.Index == squares[1].Index);

        Case("a full field offers nothing", !FieldPlan.Next(squares, _ => true, out Furrow _));

        // Two villagers in one field keep off each other's ground by starting somewhere else.
        // There is no claim behind this, so if the start were ignored they would converge on the
        // same square every tick and one of them would walk for nothing all afternoon.
        Case("starting further along gives a different square",
            FieldPlan.Next(squares, _ => false, out Furrow mine, 0) &&
            FieldPlan.Next(squares, _ => false, out Furrow theirs, squares.Count / 2) &&
            mine.Index != theirs.Index);

        // And it still fills the whole field, because the scan wraps. Without this a villager
        // starting near the end would work four squares and report the field full.
        Case("and the scan wraps, so a late start still finds the early squares",
            FieldPlan.Next(squares, index => index != squares[0].Index, out Furrow wrapped,
                squares.Count - 1) && wrapped.Index == squares[0].Index);

        // A start past the end is ordinary: it comes from an id divided by a count that changes
        // whenever the field is resized. Refusing it would leave that villager unable to work.
        Case("a start past the end is wrapped rather than refused",
            FieldPlan.Next(squares, _ => false, out Furrow _, squares.Count * 3 + 2));

        Case("and so is a negative one",
            FieldPlan.Next(squares, _ => false, out Furrow _, -7));

        Case("and so does a field with no squares at all",
            !FieldPlan.Next(new List<Furrow>(), _ => false, out Furrow _));

        // Bounded before anything is built. A pitch from a corrupt record would otherwise ask for
        // a billion squares and the list would be the thing that noticed.
        FieldPlan.Squares(radius: 32f, pitch: .001f, squares);
        Case($"an absurd pitch is refused rather than attempted (got {squares.Count})",
            squares.Count == 0);

        Case("control: and a sane one at the same radius is not",
            FieldPlan.Capacity(32f, 2f) > 0);
    }

    static FarmFacts Sow(bool hasField = true, bool wantsSowing = true, bool hasSpot = true,
        bool atSpot = false, bool hasSeed = true, bool ready = true, bool mayCultivate = false,
        bool tired = false) =>
        new FarmFacts(hasField, wantsSowing, hasSpot, atSpot, hasSeed, ready, mayCultivate, tired);

    static void Plant_(string what, FarmState state, FarmFacts facts, FarmAction expected)
    {
        FarmAction actual = FarmTransitions.Next(state, facts).Action;
        Case($"{what} (got {actual})", actual == expected);
    }

    /// <summary>The sowing state machine, exhaustively.</summary>
    static void Farming()
    {
        Console.WriteLine("farming");

        Plant_("with nothing chosen it looks for work", FarmState.Choosing,
            Sow(hasField: false), FarmAction.ChooseWork);

        Plant_("with a field and a square it walks there", FarmState.Choosing, Sow(),
            FarmAction.MoveToSpot);

        Plant_("standing on it, it sows", FarmState.Approaching, Sow(atSpot: true), FarmAction.Sow);

        Plant_("and goes on sowing while the field wants more", FarmState.Sowing,
            Sow(atSpot: true), FarmAction.Sow);

        // A field outlives being full, exactly as a bush outlives being picked - so "the field is
        // there" and "the field wants something" are two questions and finishing is the second.
        Plant_("a field that has everything it asked for is finished", FarmState.Sowing,
            Sow(atSpot: true, wantsSowing: false), FarmAction.Complete);

        Plant_("and one filled while walking is dropped, not completed", FarmState.Approaching,
            Sow(wantsSowing: false), FarmAction.ChooseWork);

        Plant_("a field already content when chosen is never walked to", FarmState.Choosing,
            Sow(wantsSowing: false), FarmAction.ChooseWork);

        // The fact no other job has. Running out mid-field is ordinary, and the errand that
        // fetches more is asked from the choosing arm.
        Plant_("no seed is an ordinary answer, not a failure", FarmState.Choosing,
            Sow(hasSeed: false), FarmAction.Yield);

        // The knot this job tied itself in once, and the reason the seed test comes second.
        // Which seed a villager needs is a fact about the field it is working, so asking before
        // one is chosen means no field, so no seed, so yield - and a villager stands in front of
        // the field it was told to sow, reporting that no field is asking for anything.
        Plant_("with no field, an empty bag does not stop it looking for one",
            FarmState.Choosing, Sow(hasField: false, hasSeed: false), FarmAction.ChooseWork);

        Plant_("nor does it stop it letting go of a field that wants nothing",
            FarmState.Choosing, Sow(wantsSowing: false, hasSeed: false), FarmAction.ChooseWork);

        Plant_("and running out mid-field sends it back to be told so", FarmState.Sowing,
            Sow(atSpot: true, hasSeed: false), FarmAction.Yield);

        // Cultivating sits between arriving and sowing, because a villager only knows whether the
        // ground is ready once it is standing on it.
        Plant_("ground that is not ready is broken when that is allowed", FarmState.Sowing,
            Sow(atSpot: true, ready: false, mayCultivate: true), FarmAction.Cultivate);

        Plant_("and the square is given up when it is not", FarmState.Sowing,
            Sow(atSpot: true, ready: false, mayCultivate: false), FarmAction.ChooseWork);

        Plant_("the square being taken while walking sends it back to choose another",
            FarmState.Approaching, Sow(hasSpot: false), FarmAction.ChooseWork);

        Plant_("shoved away from the square, it walks back", FarmState.Sowing,
            Sow(atSpot: false), FarmAction.MoveToSpot);

        Plant_("being tired stops it", FarmState.Choosing, Sow(tired: true), FarmAction.Yield);

        // Deliberate, and the same call every other producing job makes: a stopping rule stops a
        // villager starting, never mid-square.
        Plant_("a villager already standing on a square finishes it even once tired",
            FarmState.Sowing, Sow(atSpot: true, tired: true), FarmAction.Sow);

        Plant_("the field being destroyed under it ends the trip", FarmState.Sowing,
            Sow(hasField: false, atSpot: true), FarmAction.Complete);

        Plant_("an unknown state starts over", (FarmState)99, Sow(hasField: false),
            FarmAction.ChooseWork);

        // Every combination. The four "never" properties are all counted inside `action == Sow`,
        // so a table that never sowed at all would satisfy every one of them - which is why the
        // liveness counter and the two-way check beside them are not decoration.
        int neverLooked = 0;
        int sowedWithoutField = 0, sowedUnwanted = 0, sowedWithoutSpot = 0;
        int sowedFromAfar = 0, sowedWithoutSeed = 0, sowedOnBareGround = 0;
        int cultivatedUnasked = 0, cultivatedFromAfar = 0;
        int sowed = 0, disagreed = 0, yieldedWithoutReason = 0, completedWanting = 0;
        bool[] reached = new bool[6];
        FarmState[] states = { FarmState.Choosing, FarmState.Approaching, FarmState.Sowing };

        for (int bits = 0; bits < 256; bits++)
        {
            FarmFacts facts = new FarmFacts(
                hasField: (bits & 1) != 0,
                wantsSowing: (bits & 2) != 0,
                hasSpot: (bits & 4) != 0,
                atSpot: (bits & 8) != 0,
                hasSeed: (bits & 16) != 0,
                ready: (bits & 32) != 0,
                mayCultivate: (bits & 64) != 0,
                tired: (bits & 128) != 0);

            foreach (FarmState from in states)
            {
                FarmAction action = FarmTransitions.Next(from, facts).Action;

                reached[(int)action] = true;

                if (action == FarmAction.Sow)
                {
                    sowed++;
                    if (!facts.HasField) sowedWithoutField++;
                    if (!facts.WantsSowing) sowedUnwanted++;
                    if (!facts.HasSpot) sowedWithoutSpot++;
                    if (!facts.AtSpot) sowedFromAfar++;
                    if (!facts.HasSeed) sowedWithoutSeed++;
                    if (!facts.Ready) sowedOnBareGround++;
                }

                if (action == FarmAction.Cultivate)
                {
                    // Terrain edits are the one thing here a player cannot undo by unregistering
                    // something, so the two conditions on them are asserted rather than trusted.
                    if (!facts.MayCultivate) cultivatedUnasked++;
                    if (!facts.AtSpot) cultivatedFromAfar++;
                }

                // Finishing means the field wanted nothing more. A table that reported a hungry
                // field as done would sow nothing and look busy the whole time.
                if (action == FarmAction.Complete && facts.HasField && facts.WantsSowing)
                {
                    completedWanting++;
                }

                // The same rule stated as an if-and-only-if, so it fails for a table that sows
                // when it should not *and* for one that never sows. Written out here rather than
                // derived from the table, which would only prove the table agrees with itself.
                bool should = facts.HasField && facts.WantsSowing && facts.HasSpot &&
                              facts.AtSpot && facts.HasSeed && facts.Ready &&
                              (from != FarmState.Choosing || !facts.Tired);

                if (should != (action == FarmAction.Sow)) disagreed++;

                // Yielding is for the two ordinary reasons and nothing else. Without this the
                // seed rule could be ignored entirely and every other case would still pass.
                if (action == FarmAction.Yield && !facts.Tired && facts.HasSeed)
                {
                    yieldedWithoutReason++;
                }

                // **A villager with no field must always go and look for one.** Every assertion
                // above is about what happens once a field is held, so all of them were satisfied
                // by a table that never chose a field at all - which is exactly the knot this job
                // tied itself in: it yielded for want of a seed it could not name, because naming
                // it needed the field it had not chosen.
                if (!facts.HasField && !facts.Tired && action != FarmAction.ChooseWork &&
                    action != FarmAction.Complete)
                {
                    neverLooked++;
                }
            }
        }

        Case($"no combination ever sows without a field (did {sowedWithoutField})",
            sowedWithoutField == 0);
        Case($"no combination ever sows into a field that asked for nothing (did {sowedUnwanted})",
            sowedUnwanted == 0);
        Case($"no combination ever sows without a square (did {sowedWithoutSpot})",
            sowedWithoutSpot == 0);
        Case($"no combination ever sows from away from the square (did {sowedFromAfar})",
            sowedFromAfar == 0);
        Case($"no combination ever sows without a seed (did {sowedWithoutSeed})",
            sowedWithoutSeed == 0);
        Case($"no combination ever sows into ground the plant cannot use (did {sowedOnBareGround})",
            sowedOnBareGround == 0);
        Case($"no combination ever breaks ground it was not told it could (did {cultivatedUnasked})",
            cultivatedUnasked == 0);
        Case($"and never breaks ground it is not standing on (did {cultivatedFromAfar})",
            cultivatedFromAfar == 0);
        Case($"nothing is reported finished while the field still wants something (did {completedWanting})",
            completedWanting == 0);

        // The control the lines above need. Every one of them is satisfied by a table that does
        // nothing at all, which is a suite that cannot fail.
        Case($"control: something does get sown when everything is right (sowed {sowed} times)",
            sowed > 0);

        Case($"sowing happens exactly when it should, and never otherwise ({disagreed} disagreed)",
            disagreed == 0);

        Case($"yielding is always for a reason ({yieldedWithoutReason} were not)",
            yieldedWithoutReason == 0);

        Case($"a villager with no field always goes and looks for one ({neverLooked} did not)",
            neverLooked == 0);

        // Every arm is reachable. A reordered condition that made one dead - Cultivate, say -
        // would leave villagers unable to break ground and no other case would notice.
        bool every = true;
        foreach (FarmAction action in Enum.GetValues(typeof(FarmAction))) every &= reached[(int)action];
        Case("every action the table can name is reached by some combination", every);
    }

    static RepairFacts Mend(bool hasTool = true, bool hasTarget = true, bool damaged = true,
        bool atTarget = false, bool inStationRange = true, bool tired = false) =>
        new RepairFacts(hasTool, hasTarget, damaged, atTarget, inStationRange, tired);

    static void Swing(string what, RepairState state, RepairFacts facts, RepairAction expected)
    {
        RepairAction actual = RepairTransitions.Next(state, facts).Action;
        Case($"{what} (got {actual})", actual == expected);
    }

    /// <summary>
    ///     The mending state machine, exhaustively.
    /// </summary>
    /// <remarks>
    ///     Two things here are not in any other table. A mended wall is still a wall, so finishing
    ///     is "it is whole" rather than "it is gone" - the distinction foraging draws about a
    ///     picked bush. And the station rule is a fact rather than a discovery, so it has to be
    ///     honoured in every arm: a bench can come down while a villager is walking to the wall
    ///     that depended on it.
    /// </remarks>
    static void Mending()
    {
        Console.WriteLine("mending");

        Swing("with nothing chosen it looks for work", RepairState.Choosing,
            Mend(hasTarget: false), RepairAction.ChooseWork);

        Swing("with something chosen it walks to it", RepairState.Choosing, Mend(),
            RepairAction.MoveToTarget);

        Swing("standing at it, it mends", RepairState.Approaching, Mend(atTarget: true),
            RepairAction.Mend);

        Swing("and goes on mending while it is still worn", RepairState.Mending,
            Mend(atTarget: true), RepairAction.Mend);

        // The ending this job shares with foraging and with no other: the thing outlives the work.
        Swing("a piece that is whole again is finished", RepairState.Mending,
            Mend(atTarget: true, damaged: false), RepairAction.Complete);

        Swing("one mended by somebody else while walking is dropped, not completed",
            RepairState.Approaching, Mend(damaged: false), RepairAction.ChooseWork);

        Swing("and one already whole when chosen is never walked to", RepairState.Choosing,
            Mend(damaged: false), RepairAction.ChooseWork);

        // The other ending: a troll took it down, or the player did.
        Swing("something destroyed under it ends the trip", RepairState.Mending,
            Mend(hasTarget: false, atTarget: true), RepairAction.Complete);

        Swing("no hammer is an ordinary answer, not a failure", RepairState.Choosing,
            Mend(hasTool: false), RepairAction.Yield);

        Swing("and losing it mid-swing sends it back to be told so", RepairState.Mending,
            Mend(hasTool: false, atTarget: true), RepairAction.Yield);

        // The station rule, in all three arms. A bench is a piece too, so it can come down while
        // the villager that depends on it is halfway to the wall.
        Swing("a piece out of reach of the station it needs is never chosen",
            RepairState.Choosing, Mend(inStationRange: false), RepairAction.ChooseWork);

        Swing("and one whose bench came down mid-walk is let go of", RepairState.Approaching,
            Mend(inStationRange: false), RepairAction.ChooseWork);

        Swing("and one whose bench came down mid-swing is let go of too", RepairState.Mending,
            Mend(inStationRange: false, atTarget: true), RepairAction.ChooseWork);

        Swing("shoved away from it, it walks back", RepairState.Mending, Mend(atTarget: false),
            RepairAction.MoveToTarget);

        Swing("being tired stops it", RepairState.Choosing, Mend(tired: true), RepairAction.Yield);

        // Deliberate, and the call every other job makes: tiredness stops a villager starting,
        // never mid-swing. Walking away from a half-mended wall wastes the walk.
        Swing("a villager already at a wall finishes it even once tired", RepairState.Mending,
            Mend(atTarget: true, tired: true), RepairAction.Mend);

        Swing("an unknown state starts over", (RepairState)99, Mend(hasTarget: false),
            RepairAction.ChooseWork);

        // Every combination. The five "never" properties are all counted inside
        // `action == Mend`, so a table that never mended at all would satisfy every one of them -
        // which is why the liveness counter and the two-way check beside them are not decoration.
        int mendedWithoutTool = 0, mendedNothing = 0, mendedWhole = 0;
        int mendedFromAfar = 0, mendedOutOfRange = 0, neverLooked = 0;
        int mended = 0, disagreed = 0, yieldedWithoutReason = 0, completedWhileWorn = 0;
        bool[] reached = new bool[5];
        RepairState[] states = { RepairState.Choosing, RepairState.Approaching, RepairState.Mending };

        for (int bits = 0; bits < 64; bits++)
        {
            RepairFacts facts = new RepairFacts(
                hasTool: (bits & 1) != 0,
                hasTarget: (bits & 2) != 0,
                damaged: (bits & 4) != 0,
                atTarget: (bits & 8) != 0,
                inStationRange: (bits & 16) != 0,
                tired: (bits & 32) != 0);

            foreach (RepairState from in states)
            {
                RepairAction action = RepairTransitions.Next(from, facts).Action;

                reached[(int)action] = true;

                if (action == RepairAction.Mend)
                {
                    mended++;
                    if (!facts.HasTool) mendedWithoutTool++;
                    if (!facts.HasTarget) mendedNothing++;
                    if (!facts.Damaged) mendedWhole++;
                    if (!facts.AtTarget) mendedFromAfar++;
                    if (!facts.InStationRange) mendedOutOfRange++;
                }

                // Finishing means it is whole or it is gone. A table that reported a worn piece as
                // done would mend nothing and look busy the entire time.
                if (action == RepairAction.Complete && facts.HasTarget && facts.Damaged)
                {
                    completedWhileWorn++;
                }

                // A villager with nothing chosen must always go and look. Every assertion above is
                // about what happens once something is held, so all of them are satisfied by a
                // table that never chooses - which is the knot the farm job tied itself in twice.
                if (!facts.HasTarget && !facts.Tired && facts.HasTool &&
                    action != RepairAction.ChooseWork && action != RepairAction.Complete)
                {
                    neverLooked++;
                }

                // The same rule stated as an if-and-only-if, so it fails for a table that mends
                // when it should not *and* for one that never mends. Written out here rather than
                // derived from the table, which would only prove the table agrees with itself.
                bool should = facts.HasTool && facts.HasTarget && facts.Damaged &&
                              facts.AtTarget && facts.InStationRange &&
                              (from != RepairState.Choosing || !facts.Tired);

                if (should != (action == RepairAction.Mend)) disagreed++;

                // Yielding is for the two ordinary reasons and nothing else. Without this the
                // hammer rule could be ignored entirely and every other case would still pass.
                if (action == RepairAction.Yield && !facts.Tired && facts.HasTool)
                {
                    yieldedWithoutReason++;
                }
            }
        }

        Case($"no combination ever mends without a hammer (did {mendedWithoutTool})",
            mendedWithoutTool == 0);
        Case($"no combination ever mends something it has not got (did {mendedNothing})",
            mendedNothing == 0);
        Case($"no combination ever mends what is already whole (did {mendedWhole})",
            mendedWhole == 0);
        Case($"no combination ever mends from away from it (did {mendedFromAfar})",
            mendedFromAfar == 0);
        Case($"no combination ever mends out of reach of the station it needs (did {mendedOutOfRange})",
            mendedOutOfRange == 0);
        Case($"nothing is reported finished while it is still worn (did {completedWhileWorn})",
            completedWhileWorn == 0);
        Case($"a villager with nothing chosen always goes and looks ({neverLooked} did not)",
            neverLooked == 0);

        // The control the lines above need. Every one of them is satisfied by a table that does
        // nothing at all, which is a suite that cannot fail.
        Case($"control: something does get mended when everything is right (mended {mended} times)",
            mended > 0);

        Case($"mending happens exactly when it should, and never otherwise ({disagreed} disagreed)",
            disagreed == 0);

        Case($"yielding is always for a reason ({yieldedWithoutReason} were not)",
            yieldedWithoutReason == 0);

        // Every arm is reachable. A reordered condition that made one dead - Complete, say - would
        // leave a villager unable to finish and no other case would notice.
        bool every = true;
        foreach (RepairAction action in Enum.GetValues(typeof(RepairAction))) every &= reached[(int)action];
        Case("every action the table can name is reached by some combination", every);
    }

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
