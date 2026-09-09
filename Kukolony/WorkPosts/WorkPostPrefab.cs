using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Kukolony.Core;
using UnityEngine;

namespace Kukolony.WorkPosts
{
    /// <summary>
    ///     Registers the work post as a buildable piece.
    ///
    ///     Cloned from a vanilla piece rather than built from scratch, because
    ///     CustomPiece.IsValid requires the Piece to have an icon and authoring one needs
    ///     Unity. A banner reads naturally as a marker for a work area.
    /// </summary>
    internal static class WorkPostPrefab
    {
        internal const string PrefabName = "Kukolony_WorkPost";

        /// <summary>
        ///     Tried in order. Prefab contents are asset data, so a name that looks right
        ///     may not exist in a given build - falling through a list and logging the
        ///     outcome beats failing silently.
        /// </summary>
        private static readonly string[] BaseCandidates = { "piece_banner01", "itemstand", "guard_stone" };

        /// <summary>
        ///     Hooks OnVanillaPrefabsAvailable, not PieceManager.OnPiecesRegistered.
        ///     The latter fires after PrefabManager has already pushed custom prefabs into
        ///     ZNetScene, so a piece created there is registered in the build table but
        ///     absent from ZNetScene - it can be built, but never resolved by name.
        /// </summary>
        internal static void Register()
        {
            PrefabManager.OnVanillaPrefabsAvailable += CreateWorkPost;
        }

        private static void CreateWorkPost()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= CreateWorkPost;

            string baseName = FindBase();
            if (baseName == null)
            {
                Log.Error("No usable base piece found - work post not registered.");
                return;
            }

            PieceConfig config = new PieceConfig
            {
                Name = "$kukolony_workpost",
                Description = "$kukolony_workpost_desc",
                PieceTable = PieceTables.Hammer,
                Requirements = new[]
                {
                    new RequirementConfig { Item = "Wood", Amount = 5, Recover = true },
                    new RequirementConfig { Item = "Stone", Amount = 2, Recover = true }
                }
            };

            CustomPiece post = new CustomPiece(PrefabName, baseName, config);
            if (!post.IsValid())
            {
                Log.Error($"'{PrefabName}' failed Jotunn validation - work post not registered.");
                return;
            }

            // Cloned prefabs can come back inactive, and an inactive instance never runs
            // Awake - so its ZNetView never creates a ZDO. Same trap as the villager.
            post.PiecePrefab.SetActive(true);
            post.PiecePrefab.AddComponent<WorkPost>();

            PieceManager.Instance.AddPiece(post);
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
