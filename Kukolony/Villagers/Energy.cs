namespace Kukolony.Villagers
{
    /// <summary>
    ///     How tired a villager is, and whether it should stop.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Energy is stored with a timestamp, not ticked down.</b> A value and the time it
    ///         was written; what it is now is worked out on demand. That is O(1), it is correct
    ///         for a villager nobody has looked at in ten minutes, and it does not get more
    ///         expensive as a settlement grows - which the no-population-cap rule requires. A
    ///         ticking counter would be none of those things.
    ///     </para>
    ///     <para>
    ///         Work costs energy per action rather than per second, because a villager standing
    ///         still is not tiring and a villager crossing the world on foot is. Rest recovers it
    ///         per second, because resting is a duration.
    ///     </para>
    ///     <para>
    ///         Pure, and compiled into the Unity-free test project: it is arithmetic with
    ///         boundaries at both ends, and every one of them is a villager that never rests or
    ///         never wakes.
    ///     </para>
    /// </remarks>
    internal static class Energy
    {
        /// <summary>A villager at its best.</summary>
        internal const float Full = 100f;

        /// <summary>
        ///     Energy after resting for a while, never past full.
        /// </summary>
        internal static float Recovered(float stored, float seconds, float ratePerSecond, float max = Full)
        {
            if (seconds <= 0f || ratePerSecond <= 0f) return Clamp(stored, max);

            return Clamp(stored + seconds * ratePerSecond, max);
        }

        /// <summary>
        ///     Energy after paying for an action, never below empty.
        /// </summary>
        /// <remarks>
        ///     <b>A failed action costs the same as a successful one.</b> Charging only for
        ///     success leaves a villager thrashing at a chest it cannot reach working forever and
        ///     never tiring, which is worse than one that gets tired - it never stops, never
        ///     rests, and never gives the queue a chance to hand it something it can do.
        /// </remarks>
        internal static float Spend(float current, float cost)
        {
            if (cost <= 0f) return Clamp(current, Full);

            float left = current - cost;
            return left < 0f ? 0f : left;
        }

        /// <summary>
        ///     Whether a villager should be resting, given that it is or is not already.
        /// </summary>
        /// <remarks>
        ///     <b>Two thresholds, not one.</b> A single threshold makes a villager flicker: it
        ///     stops at 20, recovers a hundredth of a point, starts work, drops below 20 on the
        ///     first action, stops again. Tired below one number and rested above a higher one
        ///     means each state has to be earned.
        /// </remarks>
        internal static bool ShouldRest(float current, bool alreadyResting, float tiredBelow, float restedAbove)
        {
            if (alreadyResting) return current < restedAbove;

            return current < tiredBelow;
        }

        private static float Clamp(float value, float max)
        {
            if (value < 0f) return 0f;
            return value > max ? max : value;
        }
    }
}
