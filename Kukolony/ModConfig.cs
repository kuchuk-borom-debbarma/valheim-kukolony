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

            ColonyRadius = config.Bind("1 - Colony", nameof(ColonyRadius), 48f,
                new ConfigDescription("Live registration radius around a colony hearth. Registered records outside it remain visible but are ineligible.", new AcceptableValueRange<float>(8f, 128f)));

            KeepAliveEnabled = config.Bind(
                "3 - Off-screen simulation",
                nameof(KeepAliveEnabled),
                true,
                "Villagers keep a small area around themselves loaded, so colonies carry on "
                + "working when no player is nearby. Disable to compare against vanilla behaviour.");

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
                48,
                new ConfigDescription(
                    "Hard ceiling on zones held open at once. Reaching it is logged rather than "
                    + "silently dropping villagers.",
                    new AcceptableValueRange<int>(9, 256)));

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
                "In zones kept loaded only for a villager, load just what the colony needs - "
                + "villagers, registered structures, buildings and loose items - and skip trees, "
                + "rocks and wildlife. Disable to load everything, as chunk loader mods do.");

            ResourceScanRadius = config.Bind(
                "2 - Jobs",
                nameof(ResourceScanRadius),
                96f,
                new ConfigDescription(
                    "How far from a hearth to look for trees and other gatherable world objects. "
                    + "This bounds the scan itself; a gathering job's own search radius narrows it "
                    + "further, and cannot reach past this.",
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
                "Enable the Ctrl+Shift+K hotkey that spawns a colony-less villager in front of you. "
                + "Development aid for the unassigned-villager path, which this does not affect.");

            ColonyScreenHotkey = config.Bind(
                "1 - Colony", nameof(ColonyScreenHotkey), KeyCode.C,
                "Press this key (outside chat) to open the colony screen on the nearest colony.");


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

            config.Save();
            config.SaveOnConfigSet = true;
        }
    }
}
