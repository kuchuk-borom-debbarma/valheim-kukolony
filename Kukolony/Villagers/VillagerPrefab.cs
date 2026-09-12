using System.Collections.Generic;
using System.Reflection;
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
            HarvestNames(prefab);
            RemoveIfPresent<WarriorNames>(prefab);
            RemoveIfPresent<NpcTalk>(prefab);
            Deghost(prefab);
            ClearInheritedCombatGear(humanoid);
            WearLikeAPlayer(prefab);

            // Enforce colony persistence even if vanilla changes the source prefab.
            Log.Info($"Base ZNetView: persistent={nview.m_persistent} type={nview.m_type} distant={nview.m_distant}");
            nview.m_persistent = true;
            nview.m_type = ZDO.ObjectType.Default;
            nview.m_distant = false;

            WalkLikeAPlayer(humanoid);

            ai.m_character = humanoid;
            ai.m_huntPlayer = false;
            ai.m_avoidFire = true;
            ai.m_afraidOfFire = false;
            return ClearEventDespawn(ai);
        }

        /// <summary>
        ///     Gives the villager the player's pace instead of the warrior's trudge.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The rig is a <c>FallenWarrior</c>, which is authored to advance on you
        ///         menacingly — and a settlement of people moving at menacing-advance speed reads
        ///         as a settlement of people wading through treacle. A villager crossing its own
        ///         hearth radius should take about as long as the player would.
        ///     </para>
        ///     <para>
        ///         Copied from the Player prefab rather than typed in as numbers, so the pace
        ///         stays the player's if a game update changes it, and so the three speeds keep
        ///         their relationship to each other. Only the speeds are taken: turn rate and
        ///         acceleration belong to the body the animation was authored for, and a rig that
        ///         moves faster than its legs can carry it is the skating that this project has
        ///         already photographed once.
        ///     </para>
        /// </remarks>
        private static void WalkLikeAPlayer(Humanoid villager)
        {
            GameObject player = ZNetScene.instance != null
                ? ZNetScene.instance.GetPrefab("Player")
                : PrefabManager.Instance.GetPrefab("Player");

            if (player == null || !player.TryGetComponent(out Player reference))
            {
                Log.Warning("[villager] no Player prefab to take a walking pace from; keeping the rig's own");
                return;
            }

            Log.Info($"[villager] pace: was walk={villager.m_walkSpeed:0.##} jog={villager.m_speed:0.##} " +
                     $"run={villager.m_runSpeed:0.##}, now walk={reference.m_walkSpeed:0.##} " +
                     $"jog={reference.m_speed:0.##} run={reference.m_runSpeed:0.##}");

            villager.m_walkSpeed = reference.m_walkSpeed;
            villager.m_speed = reference.m_speed;
            villager.m_runSpeed = reference.m_runSpeed;
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
        /// <summary>
        ///     Takes the game's own names off the rig before the component carrying them goes.
        /// </summary>
        /// <remarks>
        ///     The roadmap asks for names from the game's pool rather than a list written by
        ///     hand, and the rig arrives with one - but the component is stripped, so the names
        ///     have to be read first or they leave with it.
        ///
        ///     Read by reflection because <c>WarriorNames</c> is absent from the decompiled
        ///     reference while being present in the shipped assembly, which is the third time
        ///     that reference has been found stale. Whatever is found is reported, so the shape
        ///     is answered by the build rather than assumed from a decompile that does not have
        ///     it.
        /// </remarks>
        private static void HarvestNames(GameObject prefab)
        {
            WarriorNames names = prefab.GetComponentInChildren<WarriorNames>(true);
            if (names == null)
            {
                Log.Info("[villager] the rig carries no WarriorNames; keeping the written pool");
                return;
            }

            // Only the two name arrays. The component also holds m_prefixes and m_suffixes,
            // which the game combines with a name rather than using alone - taking every
            // string field gathered 370 "names", most of them fragments like a suffix, and
            // would have produced villagers called "the Bold". Found by logging the shape
            // instead of assuming it, which is why the field names are still reported.
            List<string> found = new List<string>();
            foreach (FieldInfo field in names.GetType()
                         .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                Log.Info($"[villager] WarriorNames.{field.Name} : {field.FieldType.Name}");
                if (field.Name != "m_maleNames" && field.Name != "m_femaleNames") continue;

                if (field.GetValue(names) is IEnumerable<string> strings)
                {
                    foreach (string name in strings)
                        if (!string.IsNullOrWhiteSpace(name)) found.Add(name.Trim());
                }
            }

            if (found.Count == 0)
            {
                Log.Warning("[villager] WarriorNames held no readable names; keeping the written pool");
                return;
            }

            VillagerNames.UseGamePool(found);
            Log.Info($"[villager] took {found.Count} name(s) from the game's own pool");
        }

        /// <summary>
        ///     Makes the rig render what it is wearing.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <c>VisEquipment.UpdateVisuals</c> applies the body model, the skin and hair
        ///         colours, and reads the hair and beard hashes from the ZDO <em>only</em> when
        ///         <c>m_isPlayer</c> is set. The source rig is a creature, so it is not - which
        ///         meant a villager wrote a complete appearance to its ZDO and then rendered
        ///         bald and bare.
        ///     </para>
        ///     <para>
        ///         That is why the data looked right everywhere it was checked: the hashes were
        ///         all there, all distinct, all craftable. Only a photograph disagreed. The flag
        ///         is reported rather than assumed, because it is asset data the managed
        ///         assembly cannot answer for.
        ///     </para>
        /// </remarks>
        private static void WearLikeAPlayer(GameObject prefab)
        {
            VisEquipment vis = prefab.GetComponentInChildren<VisEquipment>(true);
            if (vis == null)
            {
                Log.Error("[villager] the rig has no VisEquipment; it cannot be dressed");
                return;
            }

            Log.Info($"[villager] VisEquipment.m_isPlayer was {vis.m_isPlayer}, " +
                     $"bodyModel={(vis.m_bodyModel == null ? "none" : vis.m_bodyModel.name)}");
            vis.m_isPlayer = true;
        }

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
