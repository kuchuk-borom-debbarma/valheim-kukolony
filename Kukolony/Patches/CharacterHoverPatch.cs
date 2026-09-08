using HarmonyLib;
using Kukolony.Villagers;

namespace Kukolony.Patches
{
    /// <summary>
    ///     Shows a villager's name and current activity when you look at it.
    ///
    ///     Done by patching Character rather than by adding our own Hoverable component.
    ///     Character already implements Hoverable, and the game resolves it with
    ///     GetComponentInParent&lt;Hoverable&gt;() - first match wins, and component order
    ///     is not guaranteed. A second Hoverable would be a coin flip.
    ///
    ///     Character.GetHoverText and GetHoverName are virtual and not overridden by
    ///     Humanoid, so a postfix here is unambiguous.
    /// </summary>
    internal static class CharacterHoverPatch
    {
        [HarmonyPatch(typeof(Character), nameof(Character.GetHoverText))]
        private static class HoverText
        {
            private static void Postfix(Character __instance, ref string __result)
            {
                if (__instance.TryGetComponent(out Villager villager))
                {
                    __result = villager.DescribeForHover();
                }
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.GetHoverName))]
        private static class HoverName
        {
            private static void Postfix(Character __instance, ref string __result)
            {
                if (__instance.TryGetComponent(out Villager villager))
                {
                    __result = villager.DisplayName();
                }
            }
        }
    }
}
