using BepInEx.Configuration;
using UnityEngine;

namespace Kukolony
{
    /// <summary>
    ///     Every configurable value in one place, so the settings a user sees are
    ///     discoverable from a single file rather than scattered across features.
    ///
    ///     Sections are numbered because BepInEx sorts them alphabetically and we want
    ///     to control the order shown in the configuration manager.
    /// </summary>
    internal static class ModConfig
    {
        /// <summary>How far a villager may drift from home before walking back.</summary>
        internal static ConfigEntry<float> GoHomeRadius { get; private set; }
        internal static ConfigEntry<float> ColonyRadius { get; private set; }
        internal static ConfigEntry<float> FlagRadius { get; private set; }

        /// <summary>
        ///     Whether villagers reserve what they are working on. Off is the old
        ///     behaviour and exists so the claim test can be shown to fail without it -
        ///     an assertion that has never failed proves nothing.
        /// </summary>
        internal static ConfigEntry<bool> ClaimsEnabled { get; private set; }

        /// <summary>How long a villager may hold a target before others may take it.</summary>
        internal static ConfigEntry<float> ClaimTtlSeconds { get; private set; }

        internal static ConfigEntry<float> IdleWarnSeconds { get; private set; }

        /// <summary>Whether colonies keep working when no player is nearby.</summary>
        internal static ConfigEntry<bool> KeepAliveEnabled { get; private set; }

        /// <summary>Rings of zones held open around each villager. 1 means a 3x3 block.</summary>
        internal static ConfigEntry<int> KeepAliveHaloRings { get; private set; }

        /// <summary>How near a player must be for a travelling villager to walk rather than reckon.</summary>
        internal static ConfigEntry<float> TravelObservedRange { get; private set; }

        /// <summary>Opens the settings of whatever registered thing the player is looking at.</summary>
        internal static ConfigEntry<KeyCode> StructureScreenHotkey { get; private set; }

        /// <summary>Whether a crafting station only offers what this player has discovered.</summary>
        internal static ConfigEntry<bool> OnlyKnownRecipes { get; private set; }

        /// <summary>Energy spent per action, successful or not.</summary>
        internal static ConfigEntry<float> EnergyPerAction { get; private set; }

        /// <summary>Energy at which a villager stops working.</summary>
        internal static ConfigEntry<float> TiredBelow { get; private set; }

        /// <summary>Energy a resting villager must reach before working again.</summary>
        internal static ConfigEntry<float> RestedAbove { get; private set; }

        /// <summary>How far a party villager may drift from its player before it closes the gap.</summary>
        internal static ConfigEntry<float> PartyLeashDistance { get; private set; }

        /// <summary>How near a party villager gets before it stops closing.</summary>
        internal static ConfigEntry<float> PartyComfortDistance { get; private set; }

        /// <summary>How far around their player a villager in a party will work.</summary>
        internal static ConfigEntry<float> PartyWorkRadius { get; private set; }

        /// <summary>Seconds of food a villager can hold at once.</summary>
        internal static ConfigEntry<float> FedCapSeconds { get; private set; }

        /// <summary>Seconds of food left at which a villager stops to eat.</summary>
        internal static ConfigEntry<float> HungryBelowSeconds { get; private set; }

        /// <summary>How long a villager may starve before it dies of it.</summary>
        internal static ConfigEntry<float> StarvingGraceSeconds { get; private set; }

        /// <summary>
        ///     Whether starving actually kills. Off leaves the warnings and the refusal to work.
        /// </summary>
        internal static ConfigEntry<bool> StarvingKills { get; private set; }

        /// <summary>In-game hours of sleep in a bed to go from exhausted to fully rested.</summary>
        internal static ConfigEntry<float> BedHoursToRest { get; private set; }

        internal static ConfigEntry<float> HearthHoursToRest { get; private set; }

        internal static ConfigEntry<float> GroundHoursToRest { get; private set; }

        /// <summary>Hard ceiling on zones held open at once, across all colonies.</summary>
        internal static ConfigEntry<int> KeepAliveMaxZones { get; private set; }

        /// <summary>How often to sweep the world for villagers that are not loaded.</summary>
        internal static ConfigEntry<float> KeepAliveScanSeconds { get; private set; }
        internal static ConfigEntry<float> ResourceScanRadius { get; private set; }

        /// <summary>Load only colony-relevant objects in zones kept open for a villager.</summary>
        internal static ConfigEntry<bool> KeepAliveFilterObjects { get; private set; }

        /// <summary>Development aid. Off by default so it never fires for a normal install.</summary>
        internal static ConfigEntry<bool> DebugSpawnEnabled { get; private set; }
        internal static ConfigEntry<KeyCode> ColonyScreenHotkey { get; private set; }

        /// <summary>One-shot prefab diagnostic. Replaces the acceptance test for that run.</summary>
        internal static ConfigEntry<bool> DebugProbeEnabled { get; private set; }
        internal static ConfigEntry<bool> BenchmarkMode { get; private set; }
        internal static ConfigEntry<bool> BenchmarkAutoBoot { get; private set; }
        internal static ConfigEntry<string> BenchmarkCharacter { get; private set; }
        internal static ConfigEntry<string> BenchmarkWorld { get; private set; }
        internal static ConfigEntry<string> BenchmarkRunId { get; private set; }
        internal static ConfigEntry<string> BenchmarkOutputPath { get; private set; }
        internal static ConfigEntry<float> BenchmarkSettleSeconds { get; private set; }
        internal static ConfigEntry<float> BenchmarkPhaseTimeoutSeconds { get; private set; }
        internal static ConfigEntry<float> BenchmarkSaveGraceSeconds { get; private set; }
        internal static ConfigEntry<bool> BenchmarkScreenshots { get; private set; }
        internal static ConfigEntry<bool> BenchmarkAutoExit { get; private set; }

        /// <summary>Which slice of the acceptance run to execute. Empty runs all of it.</summary>
        internal static ConfigEntry<string> BenchmarkFocus { get; private set; }

        internal static void Bind(ConfigFile config)
        {
            // Binding writes the file once per entry by default; batch it instead.
            config.SaveOnConfigSet = false;

            GoHomeRadius = config.Bind(
                "1 - Villagers",
                nameof(GoHomeRadius),
                8f,
                new ConfigDescription(
                    "How far a villager may wander from its home before it walks back, in metres.",
                    new AcceptableValueRange<float>(2f, 64f)));

            FlagRadius = config.Bind("1 - Kolony", nameof(FlagRadius), 48f,
                new ConfigDescription(
                    "Default reach of a Kolony Flag, in metres. Each placed flag can be given its own "
                    + "radius from its screen; this is what a fresh one starts with.",
                    new AcceptableValueRange<float>(Colonies.WorkFlag.MinRadius, Colonies.WorkFlag.MaxRadius)));

            ColonyRadius = config.Bind("1 - Kolony", nameof(ColonyRadius), 48f,
                new ConfigDescription("Live registration radius around a Kolony Hearth. Registered records outside it remain visible but are ineligible.", new AcceptableValueRange<float>(8f, 128f)));

            KeepAliveEnabled = config.Bind(
                "3 - Off-screen simulation",
                nameof(KeepAliveEnabled),
                true,
                "Villagers keep a small area around themselves loaded, so Kolonies carry on "
                + "working when no player is nearby. Disable to compare against vanilla behaviour.");

            StructureScreenHotkey = config.Bind(
                "1 - Kolony", nameof(StructureScreenHotkey), KeyCode.V,
                "Looking at something the Kolony has registered and pressing this opens its "
                + "settings, rather than the Kolony screen and a walk down the list.");

            OnlyKnownRecipes = config.Bind(
                "4 - Work",
                nameof(OnlyKnownRecipes),
                true,
                "A crafting station offers only recipes this player has discovered, as their own "
                + "crafting menu does. Turn off to order anything the station could make - useful "
                + "on a server where the discoveries belong to somebody else.");

            EnergyPerAction = config.Bind(
                "4 - Work",
                nameof(EnergyPerAction),
                2f,
                new ConfigDescription(
                    "Energy a villager spends on each thing it does. Failed attempts cost the "
                    + "same as successful ones, or a villager thrashing at something unreachable "
                    + "would work forever and never tire.",
                    new AcceptableValueRange<float>(0f, 25f)));

            TiredBelow = config.Bind(
                "4 - Work",
                nameof(TiredBelow),
                20f,
                new ConfigDescription("Energy at which a villager stops work and goes to rest.",
                    new AcceptableValueRange<float>(0f, 90f)));

            RestedAbove = config.Bind(
                "4 - Work",
                nameof(RestedAbove),
                70f,
                new ConfigDescription(
                    "Energy a resting villager must reach before working again. Must be above "
                    + "TiredBelow: one threshold makes a villager flicker between the two.",
                    new AcceptableValueRange<float>(10f, 100f)));

            PartyLeashDistance = config.Bind(
                "4 - Work",
                nameof(PartyLeashDistance),
                12f,
                new ConfigDescription(
                    "How far a villager following you may drift before it stops and closes the "
                    + "gap. Must be above PartyComfortDistance, which is what it closes to: one "
                    + "distance makes a villager flicker between walking and standing. Raised "
                    + "automatically to at least PartyWorkRadius, because a villager cannot be "
                    + "sent to work further away than it is allowed to stand.",
                    new AcceptableValueRange<float>(3f, 64f)));

            PartyComfortDistance = config.Bind(
                "4 - Work",
                nameof(PartyComfortDistance),
                4f,
                new ConfigDescription(
                    "How near a villager following you gets before it stops. Smaller means it "
                    + "crowds you; larger means it strings out behind.",
                    new AcceptableValueRange<float>(1f, 32f)));

            PartyWorkRadius = config.Bind(
                "4 - Work",
                nameof(PartyWorkRadius),
                28f,
                new ConfigDescription(
                    "How far around you a villager in your party will go looking for work. Kept "
                    + "smaller than a work area's usual reach on purpose: a villager that wanders "
                    + "forty metres off to a better tree has stopped being in your party in every "
                    + "sense that matters.",
                    new AcceptableValueRange<float>(4f, 96f)));

            FedCapSeconds = config.Bind(
                "4 - Work",
                nameof(FedCapSeconds),
                1800f,
                new ConfigDescription(
                    "Seconds of food a villager can hold at once, and how full a new one starts. "
                    + "Food is worth its own burn time from the game's own data, so a cap below "
                    + "what a cooked meal is worth throws the rest of that meal away.",
                    new AcceptableValueRange<float>(60f, 86400f)));

            HungryBelowSeconds = config.Bind(
                "4 - Work",
                nameof(HungryBelowSeconds),
                600f,
                new ConfigDescription(
                    "Seconds of food left at which a villager stops what it is doing and goes to "
                    + "eat. Higher means it eats earlier and more often, and is further from ever "
                    + "starving.",
                    new AcceptableValueRange<float>(0f, 86400f)));

            StarvingGraceSeconds = config.Bind(
                "4 - Work",
                nameof(StarvingGraceSeconds),
                1800f,
                new ConfigDescription(
                    "How long a villager may go with nothing to eat before it dies of it. This is "
                    + "the window you have to notice and act, so it is also the floor on how far "
                    + "into hunger a villager can fall: however long a famine lasts, one meal "
                    + "brings a survivor back.",
                    new AcceptableValueRange<float>(60f, 86400f)));

            StarvingKills = config.Bind(
                "4 - Work",
                nameof(StarvingKills),
                false,
                "Whether starving actually kills a villager. Off by default, because a settlement "
                + "that works unattended for hours should not be able to lose people before you "
                + "have watched it feed itself once. Off still stops a starving villager working "
                + "and still says so - only the dying is withheld.");

            BedHoursToRest = config.Bind(
                "4 - Work",
                nameof(BedHoursToRest),
                4f,
                new ConfigDescription(
                    "In-game hours of sleep in an assigned bed to go from exhausted to fully "
                    + "rested. In-game, so it scales with the world's day length rather than "
                    + "with how long you happen to be watching.",
                    new AcceptableValueRange<float>(.5f, 24f)));

            HearthHoursToRest = config.Bind(
                "4 - Work",
                nameof(HearthHoursToRest),
                10f,
                new ConfigDescription(
                    "The same, resting at the hearth with no bed. Longer than a bed, which is "
                    + "what makes building beds worth doing.",
                    new AcceptableValueRange<float>(.5f, 48f)));

            GroundHoursToRest = config.Bind(
                "4 - Work",
                nameof(GroundHoursToRest),
                20f,
                new ConfigDescription(
                    "The same, resting where it stands - for a villager still walking to its bed, "
                    + "or one that can reach neither bed nor hearth.",
                    new AcceptableValueRange<float>(.5f, 96f)));

            TravelObservedRange = config.Bind(
                "3 - Off-screen simulation",
                nameof(TravelObservedRange),
                96f,
                new ConfigDescription(
                    "How near a player has to be for a travelling villager to walk rather than "
                    + "cover ground unseen. Walking needs a navmesh, which needs loaded terrain, "
                    + "which distant ground may never have - so out of sight a villager advances "
                    + "at its own walking speed instead. Zero makes every journey unseen.",
                    new AcceptableValueRange<float>(0f, 300f)));

            KeepAliveHaloRings = config.Bind(
                "3 - Off-screen simulation",
                nameof(KeepAliveHaloRings),
                5,
                new ConfigDescription(
                    "Rings of zones held open around each villager. 1 is a 3x3 block of 64m zones, "
                    + "2 is 5x5, and so on - (2r+1) squared, so it grows fast: 5 is 121 zones and "
                    + "704m across per villager, minus whatever neighbours already hold. "
                    + "Villagers cannot path into unloaded ground, so this needs to be at least 1, "
                    + "and every held zone is every object in it alive and ticking.",
                    new AcceptableValueRange<int>(1, 8)));

            KeepAliveMaxZones = config.Bind(
                "3 - Off-screen simulation",
                nameof(KeepAliveMaxZones),
                0,
                new ConfigDescription(
                    "Hard ceiling on zones held open at once. 0 means no ceiling, and is the "
                    + "default. Reaching a ceiling is logged rather than passed over in silence, "
                    + "and villagers are held before hearths and flags, so what a bound ceiling "
                    + "drops is an outpost's far edge rather than somebody's legs. The zones are "
                    + "a set, so villagers working the same settlement share theirs and cost "
                    + "nothing extra; it is villagers spread across the map that multiply. Every "
                    + "held zone is every object in it alive and ticking, so 0 is a promise "
                    + "about your machine rather than about the mod.",
                    new AcceptableValueRange<int>(0, 1024)));

            KeepAliveScanSeconds = config.Bind(
                "3 - Off-screen simulation",
                nameof(KeepAliveScanSeconds),
                4f,
                new ConfigDescription(
                    "How often to sweep the world for villagers that are not currently loaded.",
                    new AcceptableValueRange<float>(1f, 30f)));

            KeepAliveFilterObjects = config.Bind(
                "3 - Off-screen simulation",
                nameof(KeepAliveFilterObjects),
                true,
                "In zones kept loaded only for a villager, load just what the Kolony needs - "
                + "villagers, registered structures, buildings and loose items - and skip trees, "
                + "rocks and wildlife. Disable to load everything, as chunk loader mods do.");

            ResourceScanRadius = config.Bind(
                "2 - Jobs",
                nameof(ResourceScanRadius),
                96f,
                new ConfigDescription(
                    "How far from a hearth to look for trees and other gatherable world objects. "
                    + "Every Kolony Flag anchors the search too, out to this same distance, which "
                    + "is how an outpost further off than this still has work. This bounds the "
                    + "scan itself; a gathering job's own search radius narrows it further, and "
                    + "cannot reach past this.",
                    new AcceptableValueRange<float>(16f, 256f)));

            ClaimsEnabled = config.Bind(
                "2 - Jobs",
                nameof(ClaimsEnabled),
                true,
                "Villagers reserve what they are working on so two never walk to the same item. "
                + "Disable only to compare against the unclaimed behaviour.");

            ClaimTtlSeconds = config.Bind(
                "2 - Jobs",
                nameof(ClaimTtlSeconds),
                30f,
                new ConfigDescription(
                    "How long a villager may reserve something before other villagers may take it. "
                    + "Stops a stuck villager locking a resource forever.",
                    new AcceptableValueRange<float>(5f, 300f)));

            IdleWarnSeconds = config.Bind(
                "2 - Jobs",
                nameof(IdleWarnSeconds),
                300f,
                new ConfigDescription(
                    "How long a villager with work assigned may finish nothing before the Kolony "
                    + "says so. A settlement that has everything it asked for will trip this "
                    + "honestly; so will one that is stuck, and the mod cannot tell them apart - "
                    + "which is the point, because the second kind used to be invisible.",
                    new AcceptableValueRange<float>(30f, 3600f)));

            DebugSpawnEnabled = config.Bind(
                "9 - Development",
                nameof(DebugSpawnEnabled),
                false,
                "Enable the Ctrl+Shift+K hotkey that spawns a Kolony-less villager in front of you. "
                + "Development aid for the unassigned-villager path, which this does not affect.");

            ColonyScreenHotkey = config.Bind(
                "1 - Kolony", nameof(ColonyScreenHotkey), KeyCode.C,
                "Press this key (outside chat) to open the Kolony screen on the nearest Kolony.");


            DebugProbeEnabled = config.Bind(
                "9 - Development",
                nameof(DebugProbeEnabled),
                false,
                "Dump what prefabs and components actually exist to the log, then quit. "
                + "Answers questions the decompiled assembly cannot, since prefab contents are asset data.");

            BenchmarkMode = config.Bind("9 - Development", nameof(BenchmarkMode), false,
                "Run the config-driven in-game benchmark. Development only; off by default.");
            BenchmarkAutoBoot = config.Bind("9 - Development", nameof(BenchmarkAutoBoot), false,
                "Let automation enter BenchmarkWorld from the menu. Leave false for a manual run in the world you choose.");
            BenchmarkCharacter = config.Bind("9 - Development", nameof(BenchmarkCharacter), string.Empty,
                "Character used only by automated menu boot. Empty selects the first available character.");
            BenchmarkWorld = config.Bind("9 - Development", nameof(BenchmarkWorld), "KukolonyBenchmark",
                "World used only by automated menu boot. Manual benchmark mode runs in the world you enter.");
            BenchmarkRunId = config.Bind("9 - Development", nameof(BenchmarkRunId), string.Empty,
                "Stable ID shared by create and reload processes. Empty generates one in-game.");
            BenchmarkFocus = config.Bind("9 - Development", nameof(BenchmarkFocus), string.Empty,
                "Run one slice of the acceptance checks instead of all of them: 'chop' or "
                + "'travel'. Empty runs everything. A focused run does the minimum setup its "
                + "own checks need and skips the reload phase, so it costs a couple of minutes "
                + "rather than most of an hour - which is what makes it usable while iterating "
                + "on one feature.");
            BenchmarkOutputPath = config.Bind("9 - Development", nameof(BenchmarkOutputPath),
                "BepInEx/kukolony-benchmarks", "Canonical folder for benchmark reports and screenshots.");
            BenchmarkSettleSeconds = config.Bind("9 - Development", nameof(BenchmarkSettleSeconds), 10f,
                new ConfigDescription("Seconds to wait after the active area loads.", new AcceptableValueRange<float>(1f, 60f)));
            // Raised as the functional phase grew. Hauling and tidying are watched in real
            // time, and one villager now walks a hundred and sixty metres at its own pace,
            // which is most of three minutes on its own. Still a hang detector: nothing here
            // takes seven minutes unless it has stopped.
            BenchmarkPhaseTimeoutSeconds = config.Bind("9 - Development", nameof(BenchmarkPhaseTimeoutSeconds), 600f,
                new ConfigDescription("Maximum time for an in-game phase.", new AcceptableValueRange<float>(15f, 900f)));
            BenchmarkSaveGraceSeconds = config.Bind("9 - Development", nameof(BenchmarkSaveGraceSeconds), 15f,
                new ConfigDescription("Grace period after requesting save/logout.", new AcceptableValueRange<float>(5f, 120f)));
            BenchmarkScreenshots = config.Bind("9 - Development", nameof(BenchmarkScreenshots), true,
                "Capture all UI evidence during the create run.");
            BenchmarkAutoExit = config.Bind("9 - Development", nameof(BenchmarkAutoExit), true,
                "Save and close Valheim after the terminal report.");

            // The section rename would otherwise reset these two and leave the old values
            // in the file as a dead [1 - Colony] block that BepInEx preserves forever - the
            // first thing a player greps for and edits in vain. Copy a changed value across
            // once, then remove the old entry so the dead section disappears with it.
            Migrate(config, "1 - Colony", ColonyRadius);
            Migrate(config, "1 - Colony", ColonyScreenHotkey);

            config.Save();
            config.SaveOnConfigSet = true;
        }

        /// <summary>
        ///     Carries one entry's value from a renamed section to its new home, once.
        /// </summary>
        /// <remarks>
        ///     Binding the old definition is what reads any value the player's file still
        ///     holds; removing it afterwards is what stops the file keeping a dead section. A
        ///     value the player never changed is not copied, so a fresh install never writes
        ///     the old section at all - the bind-then-remove leaves no trace.
        /// </remarks>
        private static void Migrate<T>(ConfigFile config, string oldSection, ConfigEntry<T> current)
        {
            ConfigEntry<T> old = config.Bind(oldSection, current.Definition.Key, (T)current.DefaultValue);

            bool oldChanged = !Equals(old.Value, (T)old.DefaultValue);
            bool currentUntouched = Equals(current.Value, (T)current.DefaultValue);
            if (oldChanged && currentUntouched)
            {
                current.Value = old.Value;
            }

            config.Remove(old.Definition);
        }
    }
}
