namespace Kukolony.Villagers
{
    /// <summary>
    ///     How long a villager is still fed for, and when that runs out.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Satiety is seconds, because that is the unit the game already gives.</b> Every
    ///         food asset carries <c>m_foodBurnTime</c> - how long it lasts - so eating adds its
    ///         own number and "cooked is better than raw" needs no figure invented here. A modded
    ///         food works on the day it is installed, and the balance stays the game's rather
    ///         than this mod's.
    ///     </para>
    ///     <para>
    ///         <b>It goes negative, and that is the whole design.</b> Below zero the number is
    ///         how long the villager has gone hungry, so one float carries fed, hungry, starving
    ///         and how near death - with no second timestamp to be written out of step with the
    ///         first. Every question below is a comparison against that one number.
    ///     </para>
    ///     <para>
    ///         <b>The debt has a floor.</b> Without one, a villager left starving while the
    ///         killing is switched off runs up hours of debt that no reachable meal can repay,
    ///         and the settlement is dead as soon as it is turned back on. Floored, the worst a
    ///         long famine can do is leave everyone one meal from recovery.
    ///     </para>
    ///     <para>
    ///         Pure, and compiled into the Unity-free test project, as <see cref="Energy" /> is:
    ///         it is arithmetic with a boundary at each end, and every boundary is somebody who
    ///         starves when they should not or eats forever.
    ///     </para>
    /// </remarks>
    internal static class Hunger
    {
        /// <summary>
        ///     The most hunger a single step may charge, however long it has been.
        /// </summary>
        /// <remarks>
        ///     <b>This is what stops an absence from being a massacre.</b> Hunger is only advanced
        ///     while a villager is loaded and ticking, so the gap between one step and the next is
        ///     normally seconds - but a villager whose zone was unloaded for six hours arrives
        ///     with six hours on the clock. Charging that would kill a settlement for the crime of
        ///     the player walking away, or of the keep-alive reaching its zone cap. Capped, an
        ///     absence of any length costs a minute.
        /// </remarks>
        internal const float MaxStepSeconds = 60f;

        /// <summary>
        ///     The least time worth charging for, so this does not write a ZDO every frame.
        /// </summary>
        /// <remarks>
        ///     The work tick runs at the AI's rate and satiety is stored, so applying decay on
        ///     every pass would be a replicated write per villager per frame - the one shape a
        ///     settlement with no population cap cannot carry. Charging in steps of a few seconds
        ///     is identical arithmetic with a thousandth of the writes.
        /// </remarks>
        internal const float MinStepSeconds = 5f;

        /// <summary>
        ///     The seconds of hunger a step should actually charge, or zero for "not yet".
        /// </summary>
        /// <remarks>
        ///     Zero for a clock that went backwards, too. Server time can step back across a
        ///     reconnect, and a negative charge is a villager being fed by the network.
        /// </remarks>
        internal static float Step(double secondsSince)
        {
            if (secondsSince < MinStepSeconds) return 0f;

            return secondsSince > MaxStepSeconds ? MaxStepSeconds : (float)secondsSince;
        }

        /// <summary>
        ///     Satiety after going this long without eating, never below the floor.
        /// </summary>
        /// <param name="floor">
        ///     How far into debt hunger may run, as a negative number - normally minus the grace
        ///     a villager gets before starving kills it.
        /// </param>
        internal static float Left(float stored, float seconds, float floor)
        {
            if (seconds <= 0f) return Sink(stored, floor);

            return Sink(stored - seconds, floor);
        }

        /// <summary>
        ///     Satiety after eating something worth this many seconds, never past the cap.
        /// </summary>
        /// <remarks>
        ///     A villager in debt pays it off first, because the sum is the sum: a meal eaten
        ///     after an hour of starving buys an hour less than the same meal eaten full. That is
        ///     the honest arithmetic and it is also the one that makes a famine cost something
        ///     after the food arrives.
        /// </remarks>
        internal static float Ate(float stored, float worth, float cap)
        {
            if (worth <= 0f) return stored > cap ? cap : stored;

            float fed = stored + worth;
            return fed > cap ? cap : fed;
        }

        /// <summary>Whether it should stop and go and find something to eat.</summary>
        internal static bool Wants(float left, float hungryBelow) => left < hungryBelow;

        /// <summary>Whether it has nothing left at all, and is now running on nothing.</summary>
        internal static bool IsStarving(float left) => left <= 0f;

        /// <summary>
        ///     How long it has gone with nothing, in seconds, or zero while it still has food in it.
        /// </summary>
        internal static float StarvingFor(float left) => left < 0f ? -left : 0f;

        /// <summary>
        ///     Whether it has starved long enough to die.
        /// </summary>
        /// <remarks>
        ///     A grace of zero would mean dying the instant satiety ran out, with no window in
        ///     which anybody could be told or could act - so it is treated as the smallest real
        ///     grace rather than as none. The alternative is a config value of 0 quietly turning
        ///     a survivable shortage into an execution.
        /// </remarks>
        internal static bool Starved(float left, float grace)
        {
            float window = grace > 0f ? grace : 1f;

            return StarvingFor(left) >= window;
        }

        private static float Sink(float value, float floor)
        {
            // A floor above zero would mean a villager that can never be hungry, which is a
            // misconfiguration rather than an intent; treat it as no floor at all.
            if (floor >= 0f) return value;

            return value < floor ? floor : value;
        }
    }
}
