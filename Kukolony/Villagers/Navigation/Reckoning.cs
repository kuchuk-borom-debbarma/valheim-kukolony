namespace Kukolony.Villagers.Navigation
{
    /// <summary>
    ///     How far something moves in one tick, when nobody is watching it move.
    /// </summary>
    /// <remarks>
    ///     Pure and compiled into the Unity-free test project. It is three lines of arithmetic
    ///     and every one of its failure modes is a boundary: overshooting the destination on the
    ///     last tick, a negative or absurd delta after a frame hitch, a speed of zero from an
    ///     unconfigured character. A villager that overshoots arrives somewhere it was never
    ///     sent, and does so off-screen where nothing would notice.
    /// </remarks>
    internal static class Reckoning
    {
        /// <summary>The longest single step, in metres, however big a hitch the game just had.</summary>
        /// <remarks>
        ///     A frame that took a second - loading a zone, saving the world - must not teleport
        ///     a villager five metres through a wall it would have walked around.
        /// </remarks>
        internal const float MaximumStep = 1.5f;

        /// <summary>
        ///     How far to move this tick: never past the destination, never further than a step.
        /// </summary>
        internal static float StepLength(float remaining, float speed, float deltaTime)
        {
            if (remaining <= 0f || speed <= 0f || deltaTime <= 0f) return 0f;

            float step = speed * deltaTime;
            if (step > MaximumStep) step = MaximumStep;

            return step > remaining ? remaining : step;
        }
    }
}
