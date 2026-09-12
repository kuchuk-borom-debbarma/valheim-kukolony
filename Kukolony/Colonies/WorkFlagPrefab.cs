using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Colonies
{
    /// <summary>
    ///     Registers the Kolony Flag as a buildable piece.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Register on OnVanillaPrefabsAvailable so the prefab reaches ZNetScene, and
    ///         activate the clone or its instances never run Awake and never get a ZDO — the
    ///         same two rules the hearth already lives by.
    ///     </para>
    ///     <para>
    ///         A banner is tried before the standing stone deliberately. Banners carry no
    ///         <c>PrivateArea</c> to strip, and a thin pole costs less walkable ground than a
    ///         stone: a placed piece blocks metres of navmesh around itself, and this piece
    ///         stands in the middle of ground villagers work.
    ///     </para>
    ///     <para>
    ///         <c>PieceConfig.Category</c> is left unset on purpose. Jotunn's category system
    ///         is not ported to this Valheim version — its own release notes say so — and
    ///         setting a category schedules the broken refresh that once locked the build menu
    ///         shut for two sessions.
    ///     </para>
    /// </remarks>
    internal static class WorkFlagPrefab
    {
        internal const string PrefabName = "Kukolony_WorkFlag";

        /// <summary>
        ///     Tried in order, since prefab contents are asset data and a name that looks
        ///     right may not exist.
        /// </summary>
        private static readonly string[] BaseCandidates = { "piece_banner02", "piece_banner01", "guard_stone" };

        internal static void Register()
        {
            PrefabManager.OnVanillaPrefabsAvailable += CreateFlag;
        }

        private static void CreateFlag()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= CreateFlag;

            string baseName = FindBase();
            if (baseName == null)
            {
                Log.Error("No usable base piece found - Kolony flag not registered.");
                return;
            }

            PieceConfig config = new PieceConfig
            {
                Name = "$kukolony_flag",
                Description = "$kukolony_flag_desc",
                PieceTable = PieceTables.Hammer,
                Requirements = new[]
                {
                    new RequirementConfig { Item = "Wood", Amount = 5, Recover = true }
                }
            };

            CustomPiece flag = new CustomPiece(PrefabName, baseName, config);
            if (!flag.IsValid())
            {
                Log.Error($"'{PrefabName}' failed Jotunn validation - Kolony flag not registered.");
                return;
            }

            flag.PiecePrefab.SetActive(true);

            // Only present when the banner candidates were missing and the standing stone
            // was used. A ward on a work marker would apply access rules to every build
            // action near the outpost, which is not what planting a flag means.
            if (flag.PiecePrefab.TryGetComponent(out PrivateArea ward))
            {
                Object.DestroyImmediate(ward, allowDestroyingAssets: true);
            }

            flag.PiecePrefab.AddComponent<WorkFlag>();

            PieceManager.Instance.AddPiece(flag);
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
            }

            return null;
        }
    }
}
