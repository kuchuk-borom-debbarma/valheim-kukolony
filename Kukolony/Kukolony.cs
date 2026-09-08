using System.Reflection;
using BepInEx;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using Kukolony.Core;
using Kukolony.Villagers;

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
            AddLocalization();

            VillagerPrefab.Register();
            _harmony.PatchAll(Assembly.GetExecutingAssembly());

            gameObject.AddComponent<Debug.DebugHotkeys>();
            gameObject.AddComponent<Debug.AutoBoot>();
            gameObject.AddComponent<Debug.VillagerSelfTest>();

            Log.Info($"{ModInfo.Name} {ModInfo.Version} loaded");
        }

        private static void AddLocalization()
        {
            Localization.AddTranslation("English", "kukolony_villager", "Villager");
        }
    }
}
