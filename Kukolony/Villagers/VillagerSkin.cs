using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Villagers
{
    /// <summary>
    ///     The player rig's skin and hair materials, found once per session.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         These come off a prefab, so they are the same for every villager and for the whole
    ///         session. Each villager used to fetch the Player prefab and scan its material list
    ///         on every repaint tick - the same answer, recomputed per villager per tick, which
    ///         is the shape of cost a settlement with no population cap cannot carry.
    ///     </para>
    ///     <para>
    ///         Taken at runtime rather than when the villager prefab is configured: at
    ///         configuration time the player rig is not built yet, which is how villagers came
    ///         to glow gold the first time this was attempted.
    ///     </para>
    /// </remarks>
    internal static class VillagerSkin
    {
        private static Material _skin;
        private static Material _hair;
        private static ZNetScene _owner;

        internal static bool TryGet(out Material skin, out Material hair)
        {
            if (!ReferenceEquals(_owner, ZNetScene.instance))
            {
                // A new world means new prefabs; holding the old ones would repaint villagers
                // with materials belonging to a scene that no longer exists.
                _skin = null;
                _hair = null;
                _owner = ZNetScene.instance;
            }

            if (_skin == null) Find();

            skin = _skin;
            hair = _hair;
            return _skin != null;
        }

        /// <summary>
        ///     Swaps any Fallen Warrior material under an object for the player's.
        /// </summary>
        /// <remarks>
        ///     Matched on the shader rather than the material name, and the shared material is
        ///     replaced on the renderer rather than edited - editing it would repaint every
        ///     Fallen Warrior in the world, including the ones that are supposed to look like
        ///     that.
        /// </remarks>
        internal static int Repaint(GameObject target, VisEquipment vis = null)
        {
            if (target == null || !TryGet(out Material skin, out Material hair)) return 0;

            int repainted = 0;

            // The body model first, and by slot. Which of its materials is hair is a position
            // rather than a name - see Find - so this is the one renderer that can be read
            // exactly instead of guessed at.
            SkinnedMeshRenderer body = vis != null ? vis.m_bodyModel : null;
            if (body != null && Swap(body, skin, hair, bySlot: true)) repainted++;

            foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || ReferenceEquals(renderer, body)) continue;
                if (Swap(renderer, skin, hair, bySlot: false)) repainted++;
            }

            return repainted;
        }

        /// <summary>
        ///     Repaints one renderer's Fallen Warrior materials, leaving everything else alone.
        /// </summary>
        /// <param name="bySlot">
        ///     True on a player-rig body model, where slot 1 is hair by the game's own contract.
        ///     False elsewhere, where the material name is the only clue there is - a guess, but
        ///     a guess that now only applies to renderers nothing better can be said about.
        /// </param>
        private static bool Swap(Renderer renderer, Material skin, Material hair, bool bySlot)
        {
            Material[] materials = renderer.sharedMaterials;
            bool changed = false;

            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material?.shader == null) continue;
                if (material.shader.name.IndexOf("Fallen Warrior",
                        System.StringComparison.OrdinalIgnoreCase) < 0) continue;

                bool isHair = bySlot
                    ? i == 1
                    : material.name.IndexOf("Hair", System.StringComparison.OrdinalIgnoreCase) >= 0;

                materials[i] = isHair && hair != null ? hair : skin;
                changed = true;
            }

            if (changed) renderer.sharedMaterials = materials;
            return changed;
        }

        private static void Find()
        {
            GameObject player = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("Player") : null;
            if (player == null || !player.TryGetComponent(out VisEquipment vis) || vis.m_bodyModel == null)
            {
                return;
            }

            // Read by slot, because that is how the game reads it. VisEquipment.UpdateColors
            // runs this every frame on any player rig:
            //
            //     m_bodyModel.materials[0].SetColor(s_skinColorID, skinColour);
            //     m_bodyModel.materials[1].SetColor(s_skinColorID, hairColour);
            //
            // so slot 0 is skin and slot 1 is hair by contract. This used to search the same
            // array for a material *named* "Hair" and find none, because the player's is not
            // named that - leaving hair null, which sent every hair mesh down the skin branch
            // instead. A hair mesh wearing skin, tinted each frame with a hair colour meant for
            // a different shader, is what glowed.
            Material[] materials = vis.m_bodyModel.sharedMaterials;
            if (materials.Length > 0) _skin = materials[0];
            if (materials.Length > 1) _hair = materials[1];

            if (_skin != null)
            {
                Log.Info($"[villager] player materials found once: skin='{_skin.name}' " +
                         $"hair='{(_hair != null ? _hair.name : "NONE - hair will wear skin")}'");
            }
        }
    }
}
