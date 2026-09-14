using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Villagers
{
    /// <summary>What kind of work a tool is for.</summary>
    internal enum ToolKind
    {
        /// <summary>Something that chops - an axe, whoever put it there.</summary>
        Axe,

        /// <summary>Something that breaks rock - a pickaxe.</summary>
        Pickaxe
    }

    /// <summary>What a villager is holding, as far as can be told.</summary>
    internal enum HandItem
    {
        /// <summary>The hash resolves to nothing this build knows about.</summary>
        Unknown,

        /// <summary>A tool one of this mod's jobs would have put there.</summary>
        Tool,

        /// <summary>Something else the villager was dressed in.</summary>
        Other
    }

    /// <summary>
    ///     The villager's right hand: what is in it, and what ought to be.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Here rather than inside the chopping job, because it is a fact about the villager
    ///         and two jobs now need it. The rules below were all learned by chopping and none
    ///         of them are about chopping.
    ///     </para>
    ///     <para>
    ///         <b>The slot persists in the save, so it has to be bared as deliberately as it is
    ///         filled.</b> A villager whose job was deleted while it slept would otherwise carry
    ///         an axe for the rest of the world, with nothing left running that would ever write
    ///         the slot again.
    ///     </para>
    ///     <para>
    ///         <b>And a player may dress a villager.</b> The villager screen can put a sword, a
    ///         torch or a shield in that hand, so baring it merely because no tool-using job is
    ///         current would silently strip their choice on the next work tick. What is worn is
    ///         therefore identified, and left alone unless it is something one of these jobs
    ///         would have put there.
    ///     </para>
    ///     <para>
    ///         <b>Asked of the item rather than remembered.</b> A note of "what we equipped" is
    ///         process memory guarding a slot that survives in the save, so it is empty in
    ///         exactly the cases that matter: after a reload, on a second client, or when the
    ///         job was deleted overnight. The cost is that a player cannot dress a villager in
    ///         an axe for show, which is a smaller loss than any of those.
    ///     </para>
    /// </remarks>
    internal static class VillagerTool
    {
        /// <summary>
        ///     The best tool of a kind in a bag, or null.
        /// </summary>
        /// <remarks>
        ///     Best by tool tier, so handing a villager a better one is enough to make it able
        ///     to work what it could not - no setting, no re-assignment, just a better pickaxe
        ///     in the chest.
        /// </remarks>
        internal static ItemDrop.ItemData Best(Inventory bag, ToolKind kind)
        {
            if (bag == null) return null;

            ItemDrop.ItemData best = null;
            foreach (ItemDrop.ItemData held in bag.GetAllItems())
            {
                if (held?.m_shared == null) continue;
                if (!Does(held.m_shared, kind)) continue;

                if (best == null || held.m_shared.m_toolTier > best.m_shared.m_toolTier) best = held;
            }

            return best;
        }

        /// <summary>
        ///     Shows a tool in the hand, and tells the rig what is in it.
        /// </summary>
        /// <remarks>
        ///     Drawing the tool and being able to swing it are two different systems: one decides
        ///     what is rendered, the other decides which animations the controller can reach from
        ///     here. Setting only the first is a villager visibly holding an axe and chopping
        ///     with an invisible gesture.
        /// </remarks>
        internal static void Show(VisEquipment equipment, VillagerAnimation animation, ZDO zdo,
            ItemDrop.ItemData tool)
        {
            if (equipment != null)
            {
                // Written only when there is one to show. Writing null here instead would bare
                // the hand every tick a villager had no tool, which is also every tick after a
                // player dressed it in something else.
                if (tool != null) VillagerWardrobe.Set(equipment, WearSlot.RightHand, tool);
                else PutAway(equipment, zdo);
            }

            animation?.Hold(tool);
        }

        /// <summary>Bares the hand, if what is in it is one of ours.</summary>
        internal static void PutAway(VisEquipment equipment, ZDO zdo)
        {
            // No record to read means no way to tell an axe from a torch, and the safe answer
            // when the question cannot be asked is to change nothing.
            if (equipment == null || zdo == null) return;

            int worn = VillagerWardrobe.Worn(zdo, WearSlot.RightHand);
            if (worn == 0) return;

            switch (Identify(worn))
            {
                case HandItem.Tool:
                    VillagerWardrobe.Set(equipment, WearSlot.RightHand, null);
                    return;

                case HandItem.Other:
                    // The player's choice. Left alone.
                    return;

                default:
                    // Could not tell, which is a different answer from "not ours" and must not
                    // share its branch: the slot persists in the save and nothing else writes
                    // it, so quietly leaving an unidentifiable item would leave a tool in that
                    // hand for the rest of the world with nobody any the wiser.
                    Chatter.Warn("[villager] unknown held item",
                        $"cannot identify held item {worn}; leaving it in place");
                    return;
            }
        }

        /// <summary>What a villager is holding, as far as can be told.</summary>
        internal static HandItem Identify(int prefabHash)
        {
            if (ObjectDB.instance?.m_itemByHash == null) return HandItem.Unknown;

            // The table's own index, not a walk of it. This runs per villager per work tick -
            // and every frame for a villager whose hearth was destroyed, which is ahead of the
            // throttle - so scanning five hundred entries and hashing each name was a per-tick
            // cost in a file whose whole argument is against paying those.
            if (!ObjectDB.instance.m_itemByHash.TryGetValue(prefabHash, out GameObject prefab) ||
                prefab == null)
            {
                return HandItem.Unknown;
            }

            if (!prefab.TryGetComponent(out ItemDrop drop) || drop.m_itemData?.m_shared == null)
            {
                return HandItem.Other;
            }

            ItemDrop.ItemData.SharedData shared = drop.m_itemData.m_shared;
            return Does(shared, ToolKind.Axe) || Does(shared, ToolKind.Pickaxe)
                ? HandItem.Tool
                : HandItem.Other;
        }

        private static bool Does(ItemDrop.ItemData.SharedData shared, ToolKind kind) =>
            kind == ToolKind.Axe ? shared.m_damages.m_chop > 0f : shared.m_damages.m_pickaxe > 0f;
    }
}
