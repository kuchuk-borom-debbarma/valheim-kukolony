using System;
using System.Collections.Generic;
using System.Linq;
using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Debug
{
    /// <summary>
    ///     Drives the main menu into a world without a human clicking through it, so an
    ///     automated run is: launch the game, read the log.
    ///
    ///     This is development scaffolding, not a feature. It is config-gated off, and it
    ///     pokes private FejdStartup state, so it is version-sensitive and expected to
    ///     need fixing after game updates. That cost is worth paying because the
    ///     spawn/behave/reload loop gets run dozens of times before the mod is done.
    /// </summary>
    internal sealed class AutoBoot : MonoBehaviour
    {
        /// <summary>Give the menu a moment to populate its profile and world lists.</summary>
        private const float StartDelaySeconds = 3f;

        /// <summary>
        ///     Fixed seed so every fresh test world generates identical terrain. A test
        ///     that runs on different ground each time is not a repeatable test.
        /// </summary>
        private const string TestWorldSeed = "kukolony";

        private static bool _alreadyBooted;

        private float _elapsed;

        private void Update()
        {
            if (!(ModConfig.BenchmarkMode.Value && ModConfig.BenchmarkAutoBoot.Value) || _alreadyBooted)
            {
                return;
            }

            // Only meaningful at the main menu. Once a world is loading this is gone.
            FejdStartup startup = FejdStartup.instance;
            if (startup == null)
            {
                return;
            }

            _elapsed += Time.deltaTime;
            if (_elapsed < StartDelaySeconds)
            {
                return;
            }

            _alreadyBooted = true;
            Boot(startup);
        }

        private static void Boot(FejdStartup startup)
        {
            List<PlayerProfile> profiles = SaveSystem.GetAllPlayerProfiles();
            List<World> worlds = SaveSystem.GetWorldList();

            // Log what is available before choosing. When a configured name does not
            // match, this is what tells us the correct spelling.
            Log.Info($"[AutoBoot] characters: {Describe(profiles.Select(p => p.GetName()))}");
            Log.Info($"[AutoBoot] worlds: {Describe(worlds.Select(w => w.m_name))}");

            PlayerProfile profile = Select(profiles, ModConfig.BenchmarkCharacter.Value, p => p.GetName());
            World world = ResolveWorld(worlds);

            if (profile == null || world == null)
            {
                Log.Error("[AutoBoot] no character or no world available - cannot boot.");
                return;
            }

            Log.Info($"[AutoBoot] starting world '{world.m_name}' as '{profile.GetName()}'");

            startup.SetSelectedProfile(profile.GetFilename());
            startup.m_world = world;
            startup.OnWorldStart();
        }

        /// <summary>
        ///     Finds the configured test world, creating it if it does not exist.
        ///
        ///     Creating our own means the test never writes into a world the player
        ///     cares about, and every run starts from a known state instead of whatever
        ///     a previous manual session happened to leave lying around.
        /// </summary>
        private static World ResolveWorld(List<World> worlds)
        {
            string wanted = ModConfig.BenchmarkWorld.Value;
            if (string.IsNullOrEmpty(wanted))
            {
                return worlds.Count > 0 ? worlds[0] : null;
            }

            World existing = worlds.FirstOrDefault(w => w.m_name == wanted);
            if (existing != null)
            {
                return existing;
            }

            Log.Info($"[AutoBoot] creating test world '{wanted}' (seed '{TestWorldSeed}')");

            World created = new World(wanted, TestWorldSeed)
            {
                // Local rather than cloud: this is scratch data and should not consume
                // the player's Steam Cloud quota.
                m_fileSource = FileHelpers.FileSource.Local,
                m_needsDB = false
            };

            created.SaveWorldFWLData(DateTime.Now);
            return created;
        }

        /// <summary>
        ///     Picks the configured entry by name, falling back to the first available.
        ///     Falling back rather than failing keeps a fresh checkout usable without
        ///     anyone having to configure names first.
        /// </summary>
        private static T Select<T>(List<T> candidates, string wanted, System.Func<T, string> nameOf)
            where T : class
        {
            if (candidates == null || candidates.Count == 0)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(wanted))
            {
                T match = candidates.FirstOrDefault(c => nameOf(c) == wanted);
                if (match != null)
                {
                    return match;
                }

                Log.Warning($"[AutoBoot] '{wanted}' not found; falling back to '{nameOf(candidates[0])}'");
            }

            return candidates[0];
        }

        private static string Describe(IEnumerable<string> names)
        {
            string joined = string.Join(", ", names.ToArray());
            return string.IsNullOrEmpty(joined) ? "(none)" : joined;
        }
    }
}
