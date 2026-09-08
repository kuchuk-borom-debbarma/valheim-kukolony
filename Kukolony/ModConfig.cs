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

        /// <summary>Development aid. Off by default so it never fires for a normal install.</summary>
        internal static ConfigEntry<bool> DebugSpawnEnabled { get; private set; }

        /// <summary>Boot straight into a world and run the villager acceptance test.</summary>
        internal static ConfigEntry<bool> AutoTestEnabled { get; private set; }

        /// <summary>Character to auto-boot with. Empty means "first available".</summary>
        internal static ConfigEntry<string> AutoTestCharacter { get; private set; }

        /// <summary>World to auto-boot into. Empty means "first available".</summary>
        internal static ConfigEntry<string> AutoTestWorld { get; private set; }

        /// <summary>Save and exit once the test has reported. Makes runs self-terminating.</summary>
        internal static ConfigEntry<bool> AutoTestQuitWhenDone { get; private set; }

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

            DebugSpawnEnabled = config.Bind(
                "9 - Development",
                nameof(DebugSpawnEnabled),
                false,
                "Enable the Ctrl+Shift+K hotkey that spawns a villager in front of you. Development aid.");

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

            config.Save();
            config.SaveOnConfigSet = true;
        }
    }
}
