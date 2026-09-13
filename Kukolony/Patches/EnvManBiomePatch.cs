using HarmonyLib;
using Kukolony.Gui;
using UnityEngine;

namespace Kukolony.Patches
{
    /// <summary>
    ///     Keeps the weather where the player is, while the camera is somewhere else.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Valheim decides what biome you are in by asking the camera.</b>
    ///         <c>EnvMan.GetBiome</c> reads <c>Utils.GetMainCamera().transform.position</c>, which
    ///         is a fair assumption in a game where the camera is always a few metres behind the
    ///         player - and false the moment a mod moves it. Watching a villager on a mountain
    ///         put the environment on that mountain: snow, the frost overlay, mountain music.
    ///     </para>
    ///     <para>
    ///         <b>And it is not cosmetic.</b> <c>Player.UpdateEnvStatusEffects</c> asks
    ///         <c>EnvMan.IsFreezing()</c>, so a player standing safely in the meadows took cold
    ///         damage for looking at somebody who was not. A camera must not be able to hurt the
    ///         body it left behind.
    ///     </para>
    ///     <para>
    ///         The camera is moved to the player for the length of that one call and put back
    ///         afterwards, rather than the answer being worked out again here. Nothing renders
    ///         between the two, and the alternative - reimplementing the ashlands and deep north
    ///         corrections - is a copy of vanilla logic that would drift the first time it
    ///         changed.
    ///     </para>
    /// </remarks>
    [HarmonyPatch(typeof(EnvMan), "GetBiome")]
    internal static class EnvManBiomePatch
    {
        private static Camera _moved;
        private static Vector3 _wasAt;

        private static void Prefix()
        {
            _moved = null;
            if (!WatchCamera.Watching) return;

            Player player = Player.m_localPlayer;
            if (player == null) return;

            Camera camera = Utils.GetMainCamera();
            if (camera == null) return;

            _moved = camera;
            _wasAt = camera.transform.position;
            camera.transform.position = player.transform.position;
        }

        private static void Postfix()
        {
            if (_moved == null) return;

            _moved.transform.position = _wasAt;
            _moved = null;
        }
    }
}
