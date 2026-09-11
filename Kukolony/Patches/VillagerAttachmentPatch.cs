using HarmonyLib;
using Kukolony.Villagers;
using UnityEngine;

namespace Kukolony.Patches
{
    /// <summary>
    ///     Repaints a villager's hair, beard and armour the moment the game attaches them.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The rig villagers are cloned from draws with the Fallen Warrior shader, which is
    ///         what made them glow gold. Everything the game attaches at runtime - hair, beards,
    ///         armour pieces - arrives with that shader too, and arrives *late*: which is why the
    ///         first fix polled, walking every renderer on the villager for up to 240 ticks after
    ///         each load, per villager, hoping to catch each attachment as it appeared.
    ///     </para>
    ///     <para>
    ///         <c>VisEquipment.AttachItem</c> returns the object it instantiates, so a postfix
    ///         here is handed exactly the thing that just appeared, exactly when it appears. The
    ///         work becomes proportional to how often a villager changes clothes rather than to
    ///         how long it has been alive, which is what a settlement with no population cap
    ///         needs.
    ///     </para>
    ///     <para>
    ///         This runs for every character in the game, so it must be cheap to decline: one
    ///         component lookup on an object that was just instantiated anyway, and nothing else
    ///         for players and monsters.
    ///     </para>
    /// </remarks>
    [HarmonyPatch(typeof(VisEquipment), "AttachItem")]
    internal static class VillagerAttachmentPatch
    {
        private static void Postfix(VisEquipment __instance, GameObject __result)
        {
            if (__result == null || __instance == null) return;
            if (__instance.GetComponentInParent<Villager>() == null) return;

            VillagerSkin.Repaint(__result);
        }
    }
}
