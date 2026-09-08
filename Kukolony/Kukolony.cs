using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;

namespace Kukolony
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    internal class Kukolony : BaseUnityPlugin
    {
        public const string PluginGUID = "com.kuku.kukolony";
        public const string PluginName = "Kukolony";
        public const string PluginVersion = "0.0.1";

        private readonly Harmony _harmony = new Harmony(PluginGUID);

        // Use this class to add your own localization to the game
        // https://valheim-modding.github.io/Jotunn/tutorials/localization.html
        public static CustomLocalization Localization = LocalizationManager.Instance.GetLocalization();

        /// <summary>
        ///     Diagnostic only. See Spikes/DvergerBrainSpike.cs - remove with the spike.
        /// </summary>
        internal static ConfigEntry<bool> SpikeBrainEnabled;

        private void Awake()
        {
            Jotunn.Logger.LogInfo("Kukolony has landed");

            SpikeBrainEnabled = Config.Bind(
                "9 - Diagnostics",
                "SpikeBrainEnabled",
                false,
                "Spike B: take over vanilla Dverger AI and walk them to the player. Diagnostic, remove after the spike.");

            _harmony.PatchAll(Assembly.GetExecutingAssembly());

            // Spike B support - remove with the spike.
            gameObject.AddComponent<Spikes.SpikeHotkey>();
        }
    }
}
