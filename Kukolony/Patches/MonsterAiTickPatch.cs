using HarmonyLib;
using Kukolony.Villagers;

namespace Kukolony.Patches
{
    /// <summary>
    ///     Hands the game's own AI tick to <see cref="Villager" />.
    ///
    ///     MonoUpdaters drives BaseAI.Instances at a fixed 0.05s step, so patching here
    ///     gives villagers a real timestep with no scheduling of our own. Returning false
    ///     suppresses the vanilla monster logic - wandering, target seeking - which would
    ///     otherwise fight whatever the villager is trying to do.
    ///
    ///     Note this also skips BaseAI.UpdateAI's housekeeping (regeneration, flight
    ///     timers). Acceptable for a grounded villager; revisit if villagers need to heal.
    /// </summary>
    [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.UpdateAI))]
    internal static class MonsterAiTickPatch
    {
        private static bool Prefix(MonsterAI __instance, float dt)
        {
            // Cheapest possible guard: only our prefab carries this component, and this
            // runs for every creature in the world 20 times a second.
            if (!__instance.TryGetComponent(out Villager villager))
            {
                return true;
            }

            // Villager decides whether it is in a position to act. When it is not - no
            // valid ZDO, or someone else owns it - vanilla runs and correctly no-ops.
            return !villager.TryTakeOver(dt);
        }
    }
}
