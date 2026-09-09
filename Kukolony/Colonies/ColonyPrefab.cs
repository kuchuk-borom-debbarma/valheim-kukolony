using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Colonies
{
    /// <summary>
    ///     Registers the colony hearth as a buildable piece.
    ///
    ///     Same shape as WorkPostPrefab, and the same two traps: register on
    ///     OnVanillaPrefabsAvailable so the prefab reaches ZNetScene, and activate the
    ///     clone or its instances never run Awake and never get a ZDO.
    /// </summary>
    internal static class ColonyPrefab
    {
        internal const string PrefabName = "Kukolony_ColonyHearth";

        /// <summary>
        ///     A standing stone reads as a settlement marker. Tried in order, since
        ///     prefab contents are asset data and a name that looks right may not exist.
        /// </summary>
        private static readonly string[] BaseCandidates = { "guard_stone", "piece_banner02", "itemstand" };

        internal static void Register()
        {
            PrefabManager.OnVanillaPrefabsAvailable += CreateColonyHearth;
        }

        private static void CreateColonyHearth()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= CreateColonyHearth;

            string baseName = FindBase();
            if (baseName == null)
            {
                Log.Error("No usable base piece found - colony hearth not registered.");
                return;
            }

            PieceConfig config = new PieceConfig
            {
                Name = "$kukolony_colony",
                Description = "$kukolony_colony_desc",
                PieceTable = PieceTables.Hammer,
                Requirements = new[]
                {
                    new RequirementConfig { Item = "Wood", Amount = 10, Recover = true },
                    new RequirementConfig { Item = "Stone", Amount = 10, Recover = true }
                }
            };

            CustomPiece hearth = new CustomPiece(PrefabName, baseName, config);
            if (!hearth.IsValid())
            {
                Log.Error($"'{PrefabName}' failed Jotunn validation - colony hearth not registered.");
                return;
            }

            hearth.PiecePrefab.SetActive(true);

            // guard_stone carries a PrivateArea, which would apply ward access rules to
            // the colony marker. A colony is bookkeeping, not a permission system.
            if (hearth.PiecePrefab.TryGetComponent(out PrivateArea ward))
            {
                Object.DestroyImmediate(ward, allowDestroyingAssets: true);
            }

            hearth.PiecePrefab.AddComponent<Colony>();

            PieceManager.Instance.AddPiece(hearth);
            Log.Info($"Registered piece '{PrefabName}' (cloned from '{baseName}')");
        }

        private static string FindBase()
        {
            foreach (string candidate in BaseCandidates)
            {
                if (PrefabManager.Instance.GetPrefab(candidate) != null)
                {
                    return candidate;
                }

                Log.Debug($"Base piece '{candidate}' not found, trying next");
            }

            return null;
        }
    }
}
