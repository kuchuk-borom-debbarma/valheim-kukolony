using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Kukolony.Core;
using Kukolony.Villagers;

namespace Kukolony.Debug
{
    /// <summary>
    ///     Valheim mounts the Steam depot even when it is writing a local world. During
    ///     unattended menu boot Steam can still own a profile-cache batch, and the save
    ///     thread then returns without touching the local files. The benchmark is
    ///     explicitly isolated to local worlds, so skip that unrelated cloud mount only
    ///     for this development run. Cloud worlds and normal gameplay are unchanged.
    /// </summary>
    [HarmonyPatch(typeof(FileHelpers), "get_CloudStorageSupported")]
    internal static class BenchmarkLocalSavePatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!ModConfig.BenchmarkMode.Value || ZNet.instance == null ||
                ZNet.instance.GetWorld() == null ||
                !ZNet.instance.GetWorld().m_fileSource.IsNotCloud()) return true;

            __result = false;
            return false;
        }
    }

    /// <summary>
    ///     Verifies that Valheim's prepared save snapshot actually contains every live
    ///     villager. A successful Save call alone cannot prove this in the chunked world
    ///     format because only dirty sector snapshots are written.
    /// </summary>
    [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.PrepareSave))]
    internal static class BenchmarkSaveProbe
    {
        private static void Postfix(ZDOMan __instance)
        {
            if (!ModConfig.BenchmarkMode.Value) return;

            HashSet<ZDOID> live = new HashSet<ZDOID>();
            List<ZDO> villagers = new List<ZDO>();
            int index = 0;
            while (!__instance.GetAllZDOsWithPrefabIterative(
                       VillagerPrefab.PrefabName, villagers, ref index)) { }
            foreach (ZDO villager in villagers)
            {
                if (villager.Persistent) live.Add(villager.m_uid);
            }

            HashSet<ZDOID> saved = new HashSet<ZDOID>();
            FieldInfo saveDataField = AccessTools.Field(typeof(ZDOMan), "m_saveData");
            object saveData = saveDataField?.GetValue(__instance);
            FieldInfo chunksField = saveData?.GetType().GetField("m_objectsByChunk",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (chunksField?.GetValue(saveData) is IEnumerable chunks)
            {
                foreach (object chunk in chunks)
                {
                    PropertyInfo item2 = chunk.GetType().GetProperty("Item2");
                    if (!(item2?.GetValue(chunk, null) is IEnumerable zdos)) continue;
                    foreach (object candidate in zdos)
                    {
                        if (candidate is ZDO zdo && live.Contains(zdo.m_uid)) saved.Add(zdo.m_uid);
                    }
                }
            }

            Log.Info($"[Benchmark] save snapshot villagers live={live.Count} saved={saved.Count}; " +
                     $"liveIds={string.Join(",", live)} savedIds={string.Join(",", saved)}");
        }
    }
}
