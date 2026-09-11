using Kukolony.Colonies;

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

        Console.WriteLine(_failed == 0
            ? $"RESULT: PASS ({_cases} cases)"
            : $"RESULT: FAIL ({_failed} of {_cases} cases)");
        return _failed == 0 ? 0 : 1;
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
