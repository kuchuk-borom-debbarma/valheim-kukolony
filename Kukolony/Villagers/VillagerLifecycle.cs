using System.Collections.Generic;
using Kukolony.Colonies;
using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Villagers
{
    /// <summary>
    ///     Creating and removing colony villagers.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This lives in Villagers rather than in ColonyOperations because Colonies knows
    ///         nothing about villagers and must keep it that way; the dependency already runs
    ///         Villagers to Colonies. Keeping it out of the panel is what lets the benchmark
    ///         exercise the same code a player does.
    ///     </para>
    ///     <para>
    ///         A spawned villager names, dresses, tames and equips itself on its first owned AI
    ///         tick, so this only has to place it and register it. The one thing placement must
    ///         get right is the ground: a villager's home is taken from where it stands on that
    ///         first tick, so a bad spawn point is permanent.
    ///     </para>
    /// </remarks>
    internal static class VillagerLifecycle
    {
        /// <summary>
        ///     Places a villager next to the hearth and enrols it in the colony. Returns null
        ///     when the colony, the scene or the prefab is unavailable, or when registration
        ///     fails; a villager with no colony can never work, so it is destroyed rather than
        ///     left wandering.
        /// </summary>
        internal static Villager Spawn(Colony colony)
        {
            if (colony == null || ZNetScene.instance == null) return null;

            GameObject prefab = ZNetScene.instance.GetPrefab(VillagerPrefab.PrefabName);
            if (prefab == null)
            {
                Log.Error($"Cannot spawn - prefab '{VillagerPrefab.PrefabName}' is not registered.");
                return null;
            }

            if (!TrySpawnPoint(colony, out Vector3 position))
            {
                Core.Report.Say("There is no solid ground beside the hearth to put a villager on.");
                return null;
            }

            // Instantiate directly rather than ZNetScene.SpawnObject, which broadcasts an
            // RPC to everyone. ZNetView.Awake creates the ZDO and we become its owner.
            GameObject spawned = Object.Instantiate(prefab, position, Quaternion.identity);
            if (spawned == null) return null;

            if (!spawned.TryGetComponent(out ZNetView view) || !view.IsValid() ||
                !colony.Register(ColonyMemberKind.Villager, view))
            {
                Log.Error("Spawned villager could not join the colony; removing it.");
                ZNetScene.instance.Destroy(spawned);
                return null;
            }

            Log.Info($"Villager spawned for colony '{colony.State.Name}'");
            return spawned.TryGetComponent(out Villager villager) ? villager : null;
        }

        /// <summary>
        ///     Removes a villager from the colony and destroys it, dropping whatever it was
        ///     carrying. Returns false when the villager is not a member of this colony.
        /// </summary>
        /// <remarks>
        ///     A villager outside loaded range has no live Container to read, so its bag is
        ///     decoded straight from its ZDO and dropped at the hearth instead. Removal still
        ///     succeeds either way: refusing would strand the villager, and destroying it
        ///     silently would eat whatever it was hauling.
        /// </remarks>
        internal static bool Remove(Colony colony, ZDOID villager)
        {
            if (colony == null || villager.IsNone() || ZDOMan.instance == null) return false;
            if (!colony.State.GetMembers(ColonyMemberKind.Villager).Contains(villager)) return false;

            ZDO zdo = ZDOMan.instance.GetZDO(villager);
            if (zdo == null || !zdo.IsValid()) return false;

            GameObject instance = ZNetScene.instance != null
                ? ZNetScene.instance.FindInstance(villager)
                : null;

            // Claim up front: the bag write, the back-pointer clear inside Unregister, and
            // DestroyZDO all write this ZDO, and a non-owner write is discarded on sync.
            zdo.SetOwner(ZDOMan.GetSessionID());

            if (instance != null) DropLoadedBag(instance);
            else DropStoredBag(zdo, colony.transform.position);

            colony.Unregister(ColonyMemberKind.Villager, villager);

            if (instance != null) ZNetScene.instance.Destroy(instance);
            else
            {
                // DestroyZDO is a silent no-op for a non-owner, so claim it first or the
                // villager survives with its membership already cleared.
                ZDOMan.instance.DestroyZDO(zdo);
            }

            // What the jobs were remembering about it. None of it outlives the villager, and a
            // long session that hires and dismisses would otherwise keep a refusal set per
            // dead villager plus an entry per target each of them ever gave up on.
            Jobs.Chop.ChopJob.Forget(villager);
            Jobs.Tend.TendJob.Forget(villager);
            Jobs.Craft.CraftJob.Forget(villager);
            Jobs.Mine.MineJob.Forget(villager);
            Jobs.Forage.ForageJob.Forget(villager);
            Jobs.Unreachable.Forget(villager);

            Log.Info($"Villager removed from colony '{colony.State.Name}'");
            return true;
        }

        /// <summary>
        ///     Harness hook for the stored-bag recovery branch. A genuinely unloaded villager
        ///     cannot be faked in-process: destroying the GameObject leaves a stale ZNetScene
        ///     instance entry, and vanilla's OnZDODestroyed dereferences it without a null
        ///     guard, which is a state a real unload never produces. The branch selection is a
        ///     null check; this exposes the half that can actually lose items.
        /// </summary>
        internal static void TestRecoverStoredBag(ZDO zdo, Vector3 origin) => DropStoredBag(zdo, origin);

        /// <summary>
        ///     Ground-snapped point in front of the hearth, and the villager's future home.
        ///     Uses the reporting overload rather than the plain one: the plain
        ///     <c>GetSolidHeight(Vector3)</c> silently returns the input height when the ray
        ///     misses, which would place a villager in mid-air with no way to tell. This
        ///     overload also rejects colliders with a rigidbody, so a villager cannot be
        ///     snapped onto a cart, a boat, or another creature. Falls back to the hearth,
        ///     which is on real ground by definition.
        /// </summary>
        /// <summary>
        ///     Where a new villager stands, and a complaint when that cannot be established.
        /// </summary>
        /// <remarks>
        ///     Uses the reporting <c>GetSolidHeight</c> overload: the plain one returns the
        ///     height it was given when the ray misses, which would put a villager in mid-air,
        ///     and the reporting form also refuses colliders with a rigidbody so nobody spawns
        ///     onto a cart or a boat.
        ///
        ///     A miss used to fall back to the hearth in silence. That is the worst place for
        ///     silence in this file: the spawn point becomes the villager's home on its first
        ///     tick and is permanent, so a miss is a villager who will walk to the wrong place
        ///     forever, and nothing said so.
        /// </remarks>
        private static bool TrySpawnPoint(Colony colony, out Vector3 position)
        {
            Vector3 hearth = colony.transform.position;
            position = hearth + colony.transform.forward * 3f;

            if (ZoneSystem.instance != null &&
                ZoneSystem.instance.GetSolidHeight(position, out float ground))
            {
                position.y = ground + .2f;
                return true;
            }

            Log.Warning($"[villager] no solid ground beside '{colony.State.Name}' at {position}; " +
                        "refusing to spawn rather than making that spot a permanent home");
            return false;
        }

        /// <summary>Spills a loaded villager's bag where it stands.</summary>
        private static void DropLoadedBag(GameObject instance)
        {
            Transform holder = instance.transform.Find(VillagerInventory.HolderName);
            if (holder == null || !holder.TryGetComponent(out Container container)) return;
            if (instance.TryGetComponent(out ZNetView view) && view.IsValid()) view.ClaimOwnership();

            Inventory inventory = container.GetInventory();
            if (inventory == null) return;
            Drop(inventory, instance.transform.position);
            VillagerInventory.Persist(container, view);
        }

        /// <summary>
        ///     Spills an unloaded villager's bag at the hearth, decoding it from the ZDO the
        ///     bag persists through, then clearing the record so it cannot be recovered twice.
        /// </summary>
        private static void DropStoredBag(ZDO zdo, Vector3 origin)
        {
            // Inventory decoding resolves every item through ObjectDB; without it the items
            // would come back with no drop prefab and be dropped as nothing.
            if (ObjectDB.instance == null)
            {
                Log.Warning("[villager] cannot recover a stored bag before ObjectDB is ready");
                return;
            }

            Drop(VillagerInventory.Stored(zdo), origin);
            VillagerInventory.ClearStored(zdo);
        }

        /// <summary>
        ///     Scatters an inventory as item drops, matching how vanilla empties a destroyed
        ///     container. Amount zero keeps each stack whole.
        /// </summary>
        private static void Drop(Inventory inventory, Vector3 origin)
        {
            List<ItemDrop.ItemData> items = inventory.GetAllItems();
            if (items.Count == 0) return;
            foreach (ItemDrop.ItemData item in items)
            {
                if (item == null || item.m_dropPrefab == null) continue;
                Vector3 at = origin + Vector3.up * .5f + Random.insideUnitSphere * .3f;
                ItemDrop.DropItem(item, 0, at, Quaternion.Euler(0f, Random.Range(0, 360), 0f));
            }
            inventory.RemoveAll();
        }
    }
}
