using System;
using HarmonyLib;
using Kukolony.Core;

namespace Kukolony.Patches
{
    /// <summary>
    ///     Keeps a fault in the build menu from being a build menu that does not open.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This mod adds a piece to the hammer's table, and a mod that adds a piece can
    ///         break the window that lists them. When it does, the symptom is uniquely
    ///         misleading: the key does nothing at all, while the player walks around, swaps
    ///         items and selects pieces perfectly normally. That is because movement is driven
    ///         from elsewhere, and the build menu's key is read part-way through
    ///         <c>Player.Update</c> — so anything that throws earlier in that same update stops
    ///         the key ever being read, and nothing else looks wrong.
    ///     </para>
    ///     <para>
    ///         It cost a player two sessions to find, and was only settled by removing the mod
    ///         and watching the menu work. Guessing had already produced two confident wrong
    ///         answers before that.
    ///     </para>
    ///     <para>
    ///         <b>A finalizer rather than a try/catch around our own code</b>, because the
    ///         throw is not necessarily in our code — it is in the game's, reached through data
    ///         we added. Swallowing it here means a broken piece costs its own row in the menu
    ///         instead of the whole menu, and the log says which call failed and why. A feature
    ///         degrading is worth a hundred of a feature silently disappearing.
    ///     </para>
    /// </remarks>
    internal static class BuildMenuPatch
    {
        [HarmonyPatch(typeof(Hud), nameof(Hud.TogglePieceSelection))]
        private static class Toggling
        {
            private static Exception Finalizer(Exception __exception)
            {
                if (__exception != null)
                {
                    Chatter.Warn("[build] toggle threw",
                        "opening the build menu threw: " + __exception);
                }

                return null;
            }
        }

        [HarmonyPatch(typeof(Hud), nameof(Hud.UpdateBuild))]
        private static class Updating
        {
            private static Exception Finalizer(Exception __exception)
            {
                if (__exception != null)
                {
                    // Coalesced, because this one is called every frame a hammer is out: an
                    // uncoalesced report would be the same line twenty times a second, which
                    // is its own way of hiding the answer.
                    Chatter.Warn("[build] update threw",
                        "refreshing the build menu threw: " + __exception);
                }

                return null;
            }
        }

        /// <summary>
        ///     The other two doors into the same routine.
        /// </summary>
        /// <remarks>
        ///     Jotunn drives its category refresh from three Hud hooks — <c>Awake</c>,
        ///     <c>UpdateBuild</c> and <c>LateUpdate</c> — and the first version of this patch
        ///     guarded only one of them. <c>LateUpdate</c> is the likeliest of the three to
        ///     throw first, because it is the one that runs with a live local player.
        ///
        ///     Worth being honest about the limit of this: the refresh mutates the live piece
        ///     table <em>before</em> it reaches the code that can throw, so catching the
        ///     exception does not undo the damage. That is why the original failure was
        ///     permanent rather than intermittent — a menu that never opened again until the
        ///     mod was removed. These finalizers buy a named cause in the log, not a repair.
        /// </remarks>
        [HarmonyPatch(typeof(Hud), nameof(Hud.LateUpdate))]
        private static class LateUpdating
        {
            private static Exception Finalizer(Exception __exception)
            {
                if (__exception != null)
                {
                    Chatter.Warn("[build] late update threw",
                        "the build menu's late update threw: " + __exception);
                }

                return null;
            }
        }

        [HarmonyPatch(typeof(Hud), nameof(Hud.Awake))]
        private static class Waking
        {
            private static Exception Finalizer(Exception __exception)
            {
                if (__exception != null)
                {
                    Chatter.Warn("[build] awake threw",
                        "the build menu threw while starting up: " + __exception);
                }

                return null;
            }
        }
    }
}
