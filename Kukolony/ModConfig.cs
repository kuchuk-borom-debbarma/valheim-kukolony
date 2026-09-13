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

        /// <summary>Whether colonies keep working when no player is nearby.</summary>
        internal static ConfigEntry<bool> KeepAliveEnabled { get; private set; }

        /// <summary>Rings of zones held open around each villager. 1 means a 3x3 block.</summary>
        internal static ConfigEntry<int> KeepAliveHaloRings { get; private set; }

        /// <summary>How near a player must be for a travelling villager to walk rather than reckon.</summary>
        internal static ConfigEntry<float> TravelObservedRange { get; private set; }

        /// <summary>Energy spent per action, successful or not.</summary>
        internal static ConfigEntry<float> EnergyPerAction { get; private set; }

        /// <summary>Energy at which a villager stops working.</summary>
        internal static ConfigEntry<float> TiredBelow { get; private set; }

        /// <summary>Energy a resting villager must reach before working again.</summary>
        internal static ConfigEntry<float> RestedAbove { get; private set; }

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
                1,
                new ConfigDescription(
                    "Rings of zones held open around each villager. 1 is a 3x3 block of 64m zones. "
                    + "Villagers cannot path into unloaded ground, so this needs to be at least 1.",
                    new AcceptableValueRange<int>(1, 3)));

            KeepAliveMaxZones = config.Bind(
                "3 - Off-screen simulation",
                nameof(KeepAliveMaxZones),
                96,
                new ConfigDescription(
                    "Hard ceiling on zones held open at once, or 0 for no ceiling. Reaching it is "
                    + "logged rather than silently dropping anything. Each villager holds a 3x3 "
                    + "block and a hearth or flag holds every zone its radius touches plus a "
                    + "ring - but the zones are a set, so villagers working the same settlement "
                    + "share theirs and cost nothing extra. A held zone is every object in it "
                    + "alive and ticking, so 0 is a promise about your machine rather than about "
                    + "the mod.",
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
