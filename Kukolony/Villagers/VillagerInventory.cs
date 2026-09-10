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
    ///     it the moment its zone unloaded or another player took ownership. Container
    ///     supplies the grid, the interaction UI and ownership-safe access.
    ///
    ///     Container is documented as writing the inventory to ZDOVars.s_items on every
    ///     change, but measured in-game it never does so for this component, and villagers
    ///     lost everything they carried across a save. Persistence is therefore ours: see
    ///     Persist/Restore below.
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
        internal const string HolderName = "KukolonyBag";

        // Internal so an unloaded villager's bag can be decoded from its ZDO against the
        // same grid it was saved with; a mismatched size silently drops items.
        internal const int Width = 6;
        internal const int Height = 4;

        // Kukolony's own record of the bag. Container is supposed to flush the inventory to
        // ZDOVars.s_items on every change, but measured in-game it never writes for this
        // component: with the villager owned and the container reporting itself as owner,
        // s_items stays empty after a change and after an explicit Save, while the live
        // inventory holds the items. The contents were then gone after a real save and
        // relaunch. Persisting through our own key uses the same ZDO write that villager
        // queue state already survives on, instead of depending on that flush.
        private static readonly int BagKey = "kukolony.bag.v1".GetStableHashCode();

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

            Restore(container, nview);
            Inventory inventory = container.GetInventory();
            if (inventory != null)
            {
                inventory.m_onChanged = (System.Action)System.Delegate.Combine(
                    inventory.m_onChanged, new System.Action(() => Persist(container, nview)));
            }

            Log.Debug($"Attached {Width}x{Height} bag to villager");
            return container;
        }

        /// <summary>
        ///     Writes the bag to the villager's ZDO. Only the owner may write, so a peer that
        ///     does not own the villager leaves the record alone rather than writing a copy
        ///     that would be discarded on the next sync.
        /// </summary>
        internal static void Persist(Container container, ZNetView nview)
        {
            if (container == null || nview == null || !nview.IsValid() || !nview.IsOwner()) return;
            Inventory inventory = container.GetInventory();
            if (inventory == null) return;
            ZPackage package = new ZPackage();
            inventory.Save(package);
            nview.GetZDO().Set(BagKey, package.GetBase64());
        }

        /// <summary>Reloads the bag from the villager's ZDO after the container is built.</summary>
        private static void Restore(Container container, ZNetView nview)
        {
            if (container == null || nview == null || !nview.IsValid()) return;
            Inventory inventory = container.GetInventory();
            if (inventory == null) return;
            string encoded = nview.GetZDO().GetString(BagKey, string.Empty);
            if (string.IsNullOrEmpty(encoded)) return;
            try { inventory.Load(new ZPackage(encoded)); }
            catch (System.Exception e) { Log.Warning("[villager] unreadable bag record: " + e.Message); }
        }

        /// <summary>The bag a villager carries, decoded from its ZDO without loading it.</summary>
        internal static Inventory Stored(ZDO zdo)
        {
            Inventory inventory = new Inventory("bag", null, Width, Height);
            string encoded = zdo != null ? zdo.GetString(BagKey, string.Empty) : string.Empty;
            if (string.IsNullOrEmpty(encoded)) return inventory;
            try { inventory.Load(new ZPackage(encoded)); }
            catch (System.Exception e) { Log.Warning("[villager] unreadable stored bag: " + e.Message); }
            return inventory;
        }

        /// <summary>Clears the persisted bag so its contents cannot be recovered twice.</summary>
        internal static void ClearStored(ZDO zdo)
        {
            if (zdo != null) zdo.Set(BagKey, string.Empty);
        }
    }
}
