using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Villagers
{
    /// <summary>
    ///     Registers the villager creature.
    ///
    ///     Villagers are cloned from the <c>Player</c> prefab, because a runtime probe of
    ///     all 3459 prefabs found only <c>Player</c> and <c>Player_ragdoll</c> carry a
    ///     swappable body model. Every other humanoid has a fixed mesh, so a villager that
    ///     looks like a person - and can wear player armour - has to start here.
    ///     See docs/npc-design.md.
    ///
    ///     The clone then has its Player brain replaced with a plain Humanoid and a
    ///     MonsterAI, which is what turns a player avatar into an NPC.
    /// </summary>
    internal static class VillagerPrefab
    {
        internal const string PrefabName = "Kukolony_Villager";

        private const string BasePrefabName = "Player";
        private const string NameToken = "$kukolony_villager";

        internal static void Register()
        {
            CreatureManager.OnVanillaCreaturesAvailable += CreateVillager;
        }

        private static void CreateVillager()
        {
            // One-shot: unsubscribe first so a failure below cannot re-enter.
            CreatureManager.OnVanillaCreaturesAvailable -= CreateVillager;

            GameObject basePrefab = CreatureManager.Instance.GetCreaturePrefab(BasePrefabName);
            if (basePrefab == null)
            {
                Log.Error($"Cannot find '{BasePrefabName}' prefab - villager not registered.");
                return;
            }

            GameObject prefab = PrefabManager.Instance.CreateClonedPrefab(PrefabName, basePrefab);
            if (prefab == null)
            {
                Log.Error($"Failed to clone '{BasePrefabName}' - villager not registered.");
                return;
            }

            // The Player prefab is stored inactive - the game activates player objects
            // explicitly when spawning them. A clone inherits that, and an inactive
            // instance never runs Awake, so its ZNetView never creates a ZDO and the
            // villager is inert. Jotunn keeps prefabs under an inactive container, so
            // this does not cause the prefab itself to wake.
            prefab.SetActive(true);

            if (!MakeNpc(prefab))
            {
                return;
            }

            prefab.AddComponent<Villager>();

            // Config is applied after the transplant so it lands on the new Humanoid.
            // Applying it first would write to the Player component we then destroy.
            CreatureConfig config = new CreatureConfig
            {
                Name = NameToken,

                // Tamed villagers with Faction.Players are never enemies of the player or
                // their other tame creatures, while monsters may still attack them.
                Faction = Character.Faction.Players
            };

            CustomCreature villager = new CustomCreature(prefab, fixReference: false, config);
            if (!villager.IsValid())
            {
                Log.Error($"'{PrefabName}' failed Jotunn validation - villager not registered.");
                return;
            }

            if (!CreatureManager.Instance.AddCreature(villager))
            {
                Log.Error($"Jotunn rejected creature '{PrefabName}'.");
                return;
            }

            Log.Info($"Registered creature '{PrefabName}' (player model)");
        }

        /// <summary>
        ///     Turns a Player clone into an NPC.
        ///
        ///     The Player component cannot stay: Player.Awake registers into s_players,
        ///     and IsPlayer() is virtual-true on Player. Player.GetAllPlayers() feeds
        ///     spawners, event triggers, boss checks and AI targeting, so an NPC left
        ///     registered as a player would distort all of them.
        /// </summary>
        private static bool MakeNpc(GameObject prefab)
        {
            if (!prefab.TryGetComponent(out Player player))
            {
                Log.Error($"'{BasePrefabName}' clone has no Player component - cannot convert.");
                return false;
            }

            Humanoid humanoid = prefab.AddComponent<Humanoid>();

            // Player : Humanoid : Character, so the authored values are already on the
            // Player component. Copy one level of the hierarchy at a time so it is clear
            // what is being carried across.
            int character = ComponentTransplant.CopyDeclaredFields(typeof(Character), player, humanoid);
            int human = ComponentTransplant.CopyDeclaredFields(typeof(Humanoid), player, humanoid);
            Log.Info($"Transplanted {character} Character and {human} Humanoid fields");

            // Destroy Player before its companions - PlayerController and Skills exist to
            // serve it, and removing them first risks tripping a RequireComponent.
            Object.DestroyImmediate(player, allowDestroyingAssets: true);
            ComponentTransplant.RemoveIfPresent<PlayerController>(prefab);
            ComponentTransplant.RemoveIfPresent<Skills>(prefab);

            ClearInheritedStartingGear(humanoid);
            MakeWorldObject(prefab);

            MonsterAI ai = prefab.AddComponent<MonsterAI>();
            ai.m_character = humanoid;

            // Villagers are workers, not guards. They should not go hunting on their own.
            ai.m_huntPlayer = false;
            ai.m_avoidFire = true;
            ai.m_afraidOfFire = false;

            return true;
        }

        /// <summary>
        ///     Drops the starting kit the villager inherited from the player.
        ///
        ///     Humanoid.GiveDefaultItems hands out m_defaultItems on spawn, and a new
        ///     player starts in rags - so the clone would equip rags over whatever
        ///     appearance we chose, which is exactly what was observed. Clearing these
        ///     leaves VisEquipment as the only thing dressing a villager.
        /// </summary>
        private static void ClearInheritedStartingGear(Humanoid humanoid)
        {
            humanoid.m_defaultItems = new GameObject[0];
            humanoid.m_randomWeapon = new GameObject[0];
            humanoid.m_randomArmor = new GameObject[0];
            humanoid.m_randomShield = new GameObject[0];
            humanoid.m_randomSets = new Humanoid.ItemSet[0];
            humanoid.m_randomItems = new Humanoid.RandomItem[0];
        }

        /// <summary>
        ///     Makes the clone a saved world object.
        ///
        ///     The Player prefab is deliberately NOT persistent - player characters are
        ///     stored in their own profile, not as world ZDOs. Inheriting that is fatal
        ///     for a villager: ZNetScene.RemoveObjects destroys the ZDO of any
        ///     non-persistent object that leaves the active area, so the villager would
        ///     evaporate and never save.
        /// </summary>
        private static void MakeWorldObject(GameObject prefab)
        {
            if (!prefab.TryGetComponent(out ZNetView nview))
            {
                Log.Error("Player clone has no ZNetView - villager cannot exist in the world.");
                return;
            }

            Log.Info($"Base ZNetView: persistent={nview.m_persistent} type={nview.m_type} distant={nview.m_distant}");

            nview.m_persistent = true;
            nview.m_type = ZDO.ObjectType.Default;
            nview.m_distant = false;
        }
    }
}
