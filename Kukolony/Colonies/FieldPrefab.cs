using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Colonies
{
    /// <summary>
    ///     Registers the Kolony Field as a buildable piece.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The same two rules the hearth and the flag already live by: register on
    ///         OnVanillaPrefabsAvailable so the prefab reaches ZNetScene, and activate the clone
    ///         or its instances never run Awake and never get a ZDO.
    ///     </para>
    ///     <para>
    ///         Cloned from a sign rather than a banner. A field marker stands in the middle of
    ///         ground villagers walk across every few seconds to plant the next seed, and a
    ///         placed piece blocks metres of navmesh around itself - a sign is the smallest
    ///         footprint of the candidates, and being able to read which field this is from a
    ///         distance is worth something on top.
    ///     </para>
    ///     <para>
    ///         <c>PieceConfig.Category</c> is left unset on purpose. Jotunn's category system is
    ///         not ported to this Valheim version - its own release notes say so - and setting a
    ///         category schedules the broken refresh that once locked the build menu shut for two
    ///         sessions.
    ///     </para>
    /// </remarks>
    internal static class FieldPrefab
    {
        internal const string PrefabName = "Kukolony_Field";

        /// <summary>
        ///     Tried in order, since prefab contents are asset data and a name that looks right
        ///     may not exist.
        /// </summary>
        private static readonly string[] BaseCandidates =
            { "sign", "piece_banner01", "piece_banner02", "guard_stone" };

        internal static void Register()
        {
            PrefabManager.OnVanillaPrefabsAvailable += CreateField;
        }

        private static void CreateField()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= CreateField;

            string baseName = FindBase();
            if (baseName == null)
            {
                Log.Error("No usable base piece found - Kolony field not registered.");
                return;
            }

            PieceConfig config = new PieceConfig
            {
                Name = "$kukolony_field",
                Description = "$kukolony_field_desc",
                PieceTable = PieceTables.Hammer,
                Requirements = new[]
                {
                    new RequirementConfig { Item = "Wood", Amount = 3, Recover = true }
                }
            };

            CustomPiece field = new CustomPiece(PrefabName, baseName, config);
            if (!field.IsValid())
            {
                Log.Error($"'{PrefabName}' failed Jotunn validation - Kolony field not registered.");
                return;
            }

            field.PiecePrefab.SetActive(true);

            // Only present when the sign candidates were missing and the standing stone was used.
            // A ward on a field marker would apply access rules to every build action across the
            // farm, which is not what marking out a field means.
            if (field.PiecePrefab.TryGetComponent(out PrivateArea ward))
            {
                Object.DestroyImmediate(ward, allowDestroyingAssets: true);
            }

            // A sign carries its own text component and its own Interactable, which would take
            // the Use key before the field ever saw it - and leave a player editing sign text
            // where they expected the field's settings.
            if (field.PiecePrefab.TryGetComponent(out Sign sign))
            {
                Object.DestroyImmediate(sign, allowDestroyingAssets: true);
            }

            field.PiecePrefab.AddComponent<Field>();

            PieceManager.Instance.AddPiece(field);
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
