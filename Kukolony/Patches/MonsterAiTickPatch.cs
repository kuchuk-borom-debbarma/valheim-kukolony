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
            // A destroyed component that is still in the list, which is not a rare case: the
            // game keeps BaseAI.Instances and a villager removed from a colony leaves its entry
            // behind for at least a frame. Component.TryGetComponent reads gameObject, and
            // reading gameObject on a destroyed component throws - so without this line the
            // guard below is itself the crash.
            //
            // **And the crash does not stop at this creature.** The exception leaves the prefix,
            // leaves MonoUpdaters.FixedUpdate, and takes the rest of the loop with it - so every
            // creature after the dead one never gets its AI tick at all. Measured: one removed
            // villager produced six thousand identical stack traces and left the next villager
            // spawned standing still, reporting "idle", for the whole two minutes a check
            // watched it. It looked exactly like a job that did not work.
            //
            // Unity's == is overloaded to answer true for a destroyed object, which is why this
            // is a comparison rather than a null check and why it cannot itself throw.
            if (__instance == null) return true;

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
