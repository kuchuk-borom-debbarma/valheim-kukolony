namespace Kukolony.Villagers.Navigation
{
    /// <summary>
    ///     How long to keep rescuing a villager that keeps needing rescuing.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A rescue is bounded so that walking gets another chance - the ground is where a
    ///         villager belongs, and a rescue that never ended would never give it back. But a
    ///         fixed bound is wrong at both ends: a villager that needed help rounding one rock
    ///         should be walking again within seconds, and one crossing genuinely impassable
    ///         country should not spend two fifths of its journey standing still rediscovering
    ///         that fact.
    ///     </para>
    ///     <para>
    ///         Measured: with a flat twenty-second burst and a fifteen-second stall before the
    ///         next one, a hundred-and-sixty-metre journey covered a hundred and thirty-seven
    ///         metres in five minutes - all of the lost time spent re-proving that walking does
    ///         not work here.
    ///     </para>
    ///     <para>
    ///         So each consecutive rescue lasts twice as long as the last, capped. Real progress
    ///         on foot resets it, so the doubling only ever describes the stretch of ground the
    ///         villager is actually struggling with.
    ///     </para>
    /// </remarks>
    internal static class Rescue
    {
        /// <summary>How long the first rescue of a bad patch lasts.</summary>
        internal const float FirstBurstSeconds = 20f;

        /// <summary>The longest any single rescue may run before walking is tried again.</summary>
        /// <remarks>
        ///     A cap rather than unbounded doubling, because walking must always get another
        ///     chance eventually - terrain that could not be walked a minute ago may be loaded,
        ///     built and perfectly walkable now.
        /// </remarks>
        internal const float LongestBurstSeconds = 160f;

        /// <summary>
        ///     How long the <paramref name="consecutive" />th rescue in a row should last.
        /// </summary>
        internal static float BurstSeconds(int consecutive)
        {
            if (consecutive <= 1) return FirstBurstSeconds;

            float seconds = FirstBurstSeconds;
            for (int i = 1; i < consecutive && seconds < LongestBurstSeconds; i++)
            {
                seconds *= 2f;
            }

            return seconds > LongestBurstSeconds ? LongestBurstSeconds : seconds;
        }
    }
}
