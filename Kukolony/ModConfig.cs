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

        /// <summary>Hard ceiling on zones held open at once, across all colonies.</summary>
        internal static ConfigEntry<int> KeepAliveMaxZones { get; private set; }

        /// <summary>How often to sweep the world for villagers that are not loaded.</summary>
        internal static ConfigEntry<float> KeepAliveScanSeconds { get; private set; }

        /// <summary>Load only colony-relevant objects in zones kept open for a villager.</summary>
        internal static ConfigEntry<bool> KeepAliveFilterObjects { get; private set; }

        /// <summary>Development aid. Off by default so it never fires for a normal install.</summary>
        internal static ConfigEntry<bool> DebugSpawnEnabled { get; private set; }
        internal static ConfigEntry<KeyCode> ColonyPickerHotkey { get; private set; }

        /// <summary>Boot straight into a world and run the villager acceptance test.</summary>
        internal static ConfigEntry<bool> AutoTestEnabled { get; private set; }

        /// <summary>Character to auto-boot with. Empty means "first available".</summary>
        internal static ConfigEntry<string> AutoTestCharacter { get; private set; }

        /// <summary>World to auto-boot into. Empty means "first available".</summary>
        internal static ConfigEntry<string> AutoTestWorld { get; private set; }

        /// <summary>Save and exit once the test has reported. Makes runs self-terminating.</summary>
        internal static ConfigEntry<bool> AutoTestQuitWhenDone { get; private set; }

        /// <summary>Build a populated colony, open the panel, and photograph it.</summary>
        internal static ConfigEntry<bool> DebugScreenshotEnabled { get; private set; }

        /// <summary>Where panel screenshots are written.</summary>
        internal static ConfigEntry<string> DebugScreenshotPath { get; private set; }

        /// <summary>One-shot prefab diagnostic. Replaces the acceptance test for that run.</summary>
        internal static ConfigEntry<bool> DebugProbeEnabled { get; private set; }
        internal static ConfigEntry<bool> BenchmarkMode { get; private set; }
        internal static ConfigEntry<string> BenchmarkStage { get; private set; }
        internal static ConfigEntry<string> BenchmarkOutputPath { get; private set; }

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
                "Enable the Ctrl+Shift+K hotkey that spawns a villager in front of you. Development aid.");

            ColonyPickerHotkey = config.Bind(
                "1 - Colony", nameof(ColonyPickerHotkey), KeyCode.C,
                "Press this key (outside chat) to open the colony picker.");

            AutoTestEnabled = config.Bind(
                "9 - Development",
                nameof(AutoTestEnabled),
                false,
                "Boot straight into a world on launch and run the villager acceptance test, "
                + "writing the result to the log. Development aid - never enable for normal play.");

            AutoTestCharacter = config.Bind(
                "9 - Development",
                nameof(AutoTestCharacter),
                string.Empty,
                "Character to auto-boot with. Leave empty to use the first available.");

            AutoTestWorld = config.Bind(
                "9 - Development",
                nameof(AutoTestWorld),
                "KukolonyTest",
                "World to auto-boot into. Created automatically if it does not exist, so "
                + "tests never write into a world you care about.");

            AutoTestQuitWhenDone = config.Bind(
                "9 - Development",
                nameof(AutoTestQuitWhenDone),
                true,
                "Save and exit the game once the test has reported.");

            DebugScreenshotEnabled = config.Bind(
                "9 - Development",
                nameof(DebugScreenshotEnabled),
                false,
                "Build a populated colony, open the panel and screenshot it. The harness can "
                + "verify every rule behind a button but not whether the panel reads well.");

            DebugScreenshotPath = config.Bind(
                "9 - Development",
                nameof(DebugScreenshotPath),
                "/tmp/kukolony-shots",
                "Directory for panel screenshots.");

            DebugProbeEnabled = config.Bind(
                "9 - Development",
                nameof(DebugProbeEnabled),
                false,
                "Dump what prefabs and components actually exist to the log, then quit. "
                + "Answers questions the decompiled assembly cannot, since prefab contents are asset data.");

            BenchmarkMode = config.Bind("9 - Development", nameof(BenchmarkMode), false,
                "Run the config-driven in-game benchmark. Development only; off by default.");
            BenchmarkStage = config.Bind("9 - Development", nameof(BenchmarkStage), "create",
                "Benchmark stage: create, reload, or ui.");
            BenchmarkOutputPath = config.Bind("9 - Development", nameof(BenchmarkOutputPath),
                "BepInEx/kukolony-benchmarks", "Canonical folder for benchmark reports and screenshots.");

            config.Save();
            config.SaveOnConfigSet = true;
        }
    }
}
