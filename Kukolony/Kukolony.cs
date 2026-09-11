using System.Reflection;
using BepInEx;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using Kukolony.Core;
using Kukolony.Villagers;
using UnityEngine;

namespace Kukolony
{
    /// <summary>
    ///     Plugin entry point. Wiring only - no game logic lives here.
    /// </summary>
    [BepInPlugin(ModInfo.Guid, ModInfo.Name, ModInfo.Version)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    // Villager simulation follows ZDO ownership, and ownership moves to whichever player
    // is nearby. A client without the mod would own a villager it cannot tick, so it
    // would freeze where it stands. See docs/multiplayer.md.
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    internal sealed class Kukolony : BaseUnityPlugin
    {
        private readonly Harmony _harmony = new Harmony(ModInfo.Guid);

        private static CustomLocalization Localization =>
            LocalizationManager.Instance.GetLocalization();

        private void Awake()
        {
            ModConfig.Bind(Config);
            if (ModConfig.BenchmarkMode.Value || ModConfig.DebugProbeEnabled.Value)
            {
                Application.runInBackground = true;
            }
            AddLocalization();

            Gui.ColonyScreen.Register();
            VillagerPrefab.Register();
            Colonies.ColonyPrefab.Register();
            _harmony.PatchAll(Assembly.GetExecutingAssembly());

            gameObject.AddComponent<KeepAlive.KeepAliveDriver>();
            Gui.ColonyScreenHotkey.Register(gameObject);
            Debug.TravelCommands.Register();
            Colonies.StructureReaperDriver.Register(gameObject);
            gameObject.AddComponent<Debug.DebugHotkeys>();
            gameObject.AddComponent<Debug.AutoBoot>();
            gameObject.AddComponent<Debug.PrefabProbe>();
            gameObject.AddComponent<Debug.ColonyBenchmarkController>();
            gameObject.AddComponent<Debug.DedicatedServerProbe>();

            Log.Info($"{ModInfo.Name} {ModInfo.Version} loaded");
        }

        private static void AddLocalization()
        {
            Localization.AddTranslation("English", "kukolony_villager", "Villager");
            Localization.AddTranslation("English", "kukolony_villager_bag", "Villager's bag");
            Localization.AddTranslation("English", "kukolony_colony", "Colony Hearth");
            Localization.AddTranslation("English", "kukolony_colony_desc",
                "Records a colony, its villagers and its registered structures. "
                + "Only placed structures inside its live radius can be registered.");
        }
    }
}
