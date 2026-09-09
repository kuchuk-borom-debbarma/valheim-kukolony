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

            Jobs.JobLibrary.Load();
            Gui.WorkPostPanel.Register();
            Gui.ColonyPanel.Register();
            VillagerPrefab.Register();
            WorkPosts.WorkPostPrefab.Register();
            Colonies.ColonyPrefab.Register();
            _harmony.PatchAll(Assembly.GetExecutingAssembly());

            gameObject.AddComponent<KeepAlive.KeepAliveDriver>();
            gameObject.AddComponent<Debug.DebugHotkeys>();
            gameObject.AddComponent<Debug.AutoBoot>();
            gameObject.AddComponent<Debug.VillagerSelfTest>();
            gameObject.AddComponent<Debug.PrefabProbe>();
            gameObject.AddComponent<Debug.HaulJobSelfTest>();
            gameObject.AddComponent<Debug.DedicatedServerProbe>();

            Log.Info($"{ModInfo.Name} {ModInfo.Version} loaded");
        }

        private static void AddLocalization()
        {
            Localization.AddTranslation("English", "kukolony_villager", "Villager");
            Localization.AddTranslation("English", "kukolony_villager_bag", "Villager's bag");
            Localization.AddTranslation("English", "kukolony_workpost", "Work Post");
            Localization.AddTranslation("English", "kukolony_workpost_desc",
                "Villagers nearby will work this post.");
            Localization.AddTranslation("English", "kukolony_colony", "Colony Hearth");
            Localization.AddTranslation("English", "kukolony_colony_desc",
                "Records a colony: its people, storage, workstations and homes. "
                + "Members can be anywhere - the hearth is a ledger, not a boundary.");
        }
    }
}
