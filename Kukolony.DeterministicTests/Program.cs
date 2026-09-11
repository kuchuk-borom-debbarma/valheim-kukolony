using Kukolony.Colonies;
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
