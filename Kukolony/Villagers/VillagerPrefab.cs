using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Villagers
{
    /// <summary>
    ///     Registers the villager creature with the game.
    ///
    ///     The villager is a clone of the Dverger, which gives us a humanoid rig,
    ///     animations, a ragdoll, a Talker and a working MonsterAI with humanoid
    ///     pathfinding - none of which we could author without Unity.
    /// </summary>
    internal static class VillagerPrefab
    {
        internal const string PrefabName = "Kukolony_Villager";

        private const string BasePrefabName = "Dverger";
        private const string NameToken = "$kukolony_villager";

        /// <summary>
        ///     Subscribes registration to the point where vanilla creatures exist.
        ///     CustomCreature resolves its base through CreatureManager.GetCreaturePrefab,
        ///     so registering any earlier finds nothing to clone.
        /// </summary>
        internal static void Register()
        {
            CreatureManager.OnVanillaCreaturesAvailable += CreateVillager;
        }

        private static void CreateVillager()
        {
            // One-shot: unsubscribe first so an exception below cannot leave us
            // registered to run again.
            CreatureManager.OnVanillaCreaturesAvailable -= CreateVillager;

            CreatureConfig config = new CreatureConfig
            {
                // Character.m_name is rendered through Localization, so a raw string
                // would show literally. The token is registered in Kukolony.Awake.
                Name = NameToken,

                // Villagers belong to the player. Combined with being tamed, this makes
                // BaseAI.IsEnemy return false against the player and their other tame
                // creatures, while leaving monsters free to attack them - a colony that
                // Greydwarves ignored would be wrong.
                Faction = Character.Faction.Players
            };

            CustomCreature villager = new CustomCreature(PrefabName, BasePrefabName, config);
            if (!villager.IsValid())
            {
                Log.Error($"Could not create '{PrefabName}' from '{BasePrefabName}' - creature not registered.");
                return;
            }

            villager.Prefab.AddComponent<Villager>();

            if (!CreatureManager.Instance.AddCreature(villager))
            {
                Log.Error($"Jotunn rejected creature '{PrefabName}'.");
                return;
            }

            Log.Info($"Registered creature '{PrefabName}'");
        }
    }
}
