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
        internal static int Repaint(GameObject target)
        {
            if (target == null || !TryGet(out Material skin, out Material hair)) return 0;

            int repainted = 0;
            foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;

                Material[] materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material material = materials[i];
                    if (material?.shader == null) continue;
                    if (material.shader.name.IndexOf("Fallen Warrior",
                            System.StringComparison.OrdinalIgnoreCase) < 0) continue;

                    bool isHair = material.name.IndexOf("Hair",
                        System.StringComparison.OrdinalIgnoreCase) >= 0;
                    materials[i] = isHair && hair != null ? hair : skin;
                    changed = true;
                }

                if (!changed) continue;
                renderer.sharedMaterials = materials;
                repainted++;
            }

            return repainted;
        }

        private static void Find()
        {
            GameObject player = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("Player") : null;
            if (player == null || !player.TryGetComponent(out VisEquipment vis) || vis.m_bodyModel == null)
            {
                return;
            }

            foreach (Material candidate in vis.m_bodyModel.sharedMaterials)
            {
                if (candidate == null) continue;
                if (candidate.name.IndexOf("Hair", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    _hair = candidate;
                else if (_skin == null) _skin = candidate;
            }

            if (_skin != null) Log.Info($"[villager] player materials found once: skin='{_skin.name}'");
        }
    }
}
