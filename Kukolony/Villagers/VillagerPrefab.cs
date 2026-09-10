using System.Collections.Generic;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using HarmonyLib;
using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Villagers
{
    /// <summary>
    ///     Registers a worker built from vanilla's human NPC rig.
    ///
    ///     FallenWarrior is a genuine Humanoid + MonsterAI creature with the same
    ///     swappable male/female body model used for human characters. Starting from it
    ///     avoids Player lifecycle state entirely: no Player, PlayerController or Skills
    ///     component is copied, transplanted, awakened or destroyed.
    /// </summary>
    internal static class VillagerPrefab
    {
        internal const string PrefabName = "Kukolony_Villager";

        private const string BasePrefabName = "FallenWarrior";
        private const string NameToken = "$kukolony_villager";

        internal static void Register() => CreatureManager.OnVanillaCreaturesAvailable += CreateVillager;

        private static void CreateVillager()
        {
            CreatureManager.OnVanillaCreaturesAvailable -= CreateVillager;

            GameObject basePrefab = CreatureManager.Instance.GetCreaturePrefab(BasePrefabName);
            if (basePrefab == null)
            {
                Log.Error($"Cannot find NPC prefab '{BasePrefabName}' - villager not registered.");
                return;
            }

            GameObject prefab = PrefabManager.Instance.CreateClonedPrefab(PrefabName, basePrefab);
            if (prefab == null)
            {
                Log.Error($"Failed to clone '{BasePrefabName}' - villager not registered.");
                return;
            }

            // Jotunn stores prefabs below an inactive container. Its own active flag must
            // be true so an instantiated villager runs its ordinary NPC Awake methods.
            prefab.SetActive(true);
            if (!PrepareNpc(prefab)) return;

            prefab.AddComponent<Villager>();
            CreatureConfig config = new CreatureConfig
            {
                Name = NameToken,
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

            Log.Info($"Registered creature '{PrefabName}' (FallenWarrior NPC rig)");
        }

        /// <summary>
        ///     Removes behavior belonging to the vanilla warrior while retaining its
        ///     authored Humanoid, MonsterAI, network, animation and human visual graph.
        /// </summary>
        private static bool PrepareNpc(GameObject prefab)
        {
            if (prefab.GetComponent<Player>() != null || prefab.GetComponent<PlayerController>() != null ||
                prefab.GetComponent<Skills>() != null)
            {
                Log.Error($"'{BasePrefabName}' unexpectedly contains player-only components.");
                return false;
            }

            if (!prefab.TryGetComponent(out Humanoid humanoid) ||
                !prefab.TryGetComponent(out MonsterAI ai) ||
                !prefab.TryGetComponent(out ZNetView nview))
            {
                Log.Error($"'{BasePrefabName}' is missing the Humanoid, MonsterAI or ZNetView NPC contract.");
                return false;
            }

            RemoveIfPresent<CharacterDrop>(prefab);
            RemoveIfPresent<WarriorNames>(prefab);
            RemoveIfPresent<NpcTalk>(prefab);
            Deghost(prefab);
            ClearInheritedCombatGear(humanoid);

            // Enforce colony persistence even if vanilla changes the source prefab.
            Log.Info($"Base ZNetView: persistent={nview.m_persistent} type={nview.m_type} distant={nview.m_distant}");
            nview.m_persistent = true;
            nview.m_type = ZDO.ObjectType.Default;
            nview.m_distant = false;

            ai.m_character = humanoid;
            ai.m_huntPlayer = false;
            ai.m_avoidFire = true;
            ai.m_afraidOfFire = false;
            return ClearEventDespawn(ai);
        }

        private static bool ClearEventDespawn(MonsterAI ai)
        {
            // FallenWarrior is authored for a world event. These private policy flags
            // cause a perfectly persistent ZDO to delete itself after reload when that
            // event/day condition is absent. They are prefab configuration, not runtime
            // internals, and must be neutralized for a permanent colony resident.
            var eventCreature = AccessTools.Field(typeof(MonsterAI), "m_eventCreature");
            var despawnInDay = AccessTools.Field(typeof(MonsterAI), "m_despawnInDay");
            if (eventCreature == null || despawnInDay == null)
            {
                Log.Error("MonsterAI despawn contract changed; villager prefab is unsafe to register.");
                return false;
            }
            Log.Info($"FallenWarrior despawn policy: event={eventCreature.GetValue(ai)}, " +
                     $"day={despawnInDay.GetValue(ai)}; clearing both");
            eventCreature.SetValue(ai, false);
            despawnInDay.SetValue(ai, false);
            return true;
        }

        private static void ClearInheritedCombatGear(Humanoid humanoid)
        {
            humanoid.m_defaultItems = new GameObject[0];
            humanoid.m_randomWeapon = new GameObject[0];
            humanoid.m_randomArmor = new GameObject[0];
            humanoid.m_randomShield = new GameObject[0];
            humanoid.m_randomSets = new Humanoid.ItemSet[0];
            humanoid.m_randomItems = new Humanoid.RandomItem[0];
        }

        /// <summary>
        ///     Strips the spectral effects the base creature is built with.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The rig this clones is a ghost: it carries a blue aura, a light, and drifting
        ///         particles, and villagers inherited all of it. A colonist is a person, not an
        ///         apparition, so the effects go while the human body, skeleton and animation
        ///         that made this rig worth cloning stay.
        ///     </para>
        ///     <para>
        ///         What is actually attached is asset data, which the managed assembly cannot
        ///         answer, so this reports what it finds as well as removing it. Guessing at
        ///         assets is how the last few asset-shaped problems here started.
        ///     </para>
        /// </remarks>
        private static void Deghost(GameObject prefab)
        {
            List<string> removed = new List<string>();

            foreach (ParticleSystem particles in prefab.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (particles == null) continue;
                removed.Add("particles:" + particles.name);
                Object.DestroyImmediate(particles.gameObject, allowDestroyingAssets: true);
            }

            foreach (Light light in prefab.GetComponentsInChildren<Light>(true))
            {
                if (light == null) continue;
                removed.Add("light:" + light.name);
                Object.DestroyImmediate(light, allowDestroyingAssets: true);
            }

            // Anything still drawing with a see-through or glowing shader is reported rather
            // than rewritten: swapping a material blind is how a villager ends up invisible.
            List<string> suspicious = new List<string>();
            foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null || material.shader == null) continue;
                    string shader = material.shader.name;
                    if (shader.IndexOf("Particle", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                        shader.IndexOf("Transparent", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                        shader.IndexOf("Alpha", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        suspicious.Add(renderer.name + "=" + shader);
                }
            }

            Log.Info($"[villager] removed {removed.Count} effect(s): {string.Join(", ", removed.ToArray())}");
            Log.Info($"[villager] see-through renderers: " +
                     (suspicious.Count == 0 ? "none" : string.Join(", ", suspicious.ToArray())));
        }

        private static void RemoveIfPresent<T>(GameObject prefab) where T : Component
        {
            if (prefab.TryGetComponent(out T component))
                Object.DestroyImmediate(component, allowDestroyingAssets: true);
        }
    }
}
