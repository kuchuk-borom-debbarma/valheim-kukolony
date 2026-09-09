using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Villagers
{
    /// <summary>
    ///     Gives a villager a bag that survives reloads and ownership transfer, by
    ///     reusing the component chests already use.
    ///
    ///     Why not just use Humanoid.m_inventory: every humanoid has one (8x4 slots), but
    ///     only Player writes its inventory to disk. A villager carrying wood would lose
    ///     it the moment its zone unloaded or another player took ownership. Container is
    ///     the component that solves this - it hooks Inventory.m_onChanged and writes the
    ///     whole inventory to ZDOVars.s_items as base64 on every change.
    ///
    ///     Why a child object rather than the villager itself: Container is
    ///     `MonoBehaviour, Hoverable, Interactable`, and Character is already Hoverable.
    ///     The game resolves both with GetComponentInParent, which takes the first match
    ///     in undefined component order - so a Container on the root would randomly steal
    ///     the villager's hover text and open a chest UI on right-click.
    ///
    ///     Container.m_rootObjectOverride exists for precisely this: it tells the
    ///     Container which ZNetView to persist through. Mounted on a collider-less child,
    ///     the Container saves into the villager's own ZDO while being invisible to the
    ///     interaction raycast, which only ever searches upward from what it hit.
    /// </summary>
    internal static class VillagerInventory
    {
        private const string HolderName = "KukolonyBag";
        private const int Width = 6;
        private const int Height = 4;

        /// <summary>
        ///     Attaches the bag if it is not already there.
        ///
        ///     Done at runtime rather than on the prefab because Container.Awake bails
        ///     permanently when the ZDO is not yet available, and Unity gives no ordering
        ///     guarantee between a child's Awake and the root ZNetView's. Adding the
        ///     component after the ZNetView is known-valid runs its Awake immediately,
        ///     with the ZDO in place.
        /// </summary>
        internal static Container Attach(GameObject villager, ZNetView nview)
        {
            Transform existing = villager.transform.Find(HolderName);
            if (existing != null && existing.TryGetComponent(out Container attached))
            {
                return attached;
            }

            GameObject holder = new GameObject(HolderName);
            holder.transform.SetParent(villager.transform, worldPositionStays: false);

            // Inactive during setup so Awake runs once, after the fields are set.
            holder.SetActive(false);

            Container container = holder.AddComponent<Container>();
            container.m_rootObjectOverride = nview;
            container.m_name = "$kukolony_villager_bag";
            container.m_width = Width;
            container.m_height = Height;

            // A villager is not a chest: it must not vanish when emptied, and it is not
            // subject to ward permissions.
            container.m_autoDestroyEmpty = false;
            container.m_checkGuardStone = false;
            container.m_privacy = Container.PrivacySetting.Public;

            holder.SetActive(true);

            Log.Debug($"Attached {Width}x{Height} bag to villager");
            return container;
        }
    }
}
