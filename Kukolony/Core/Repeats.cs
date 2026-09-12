namespace Kukolony.Core
{
    /// <summary>
    ///     Saying a thing that keeps being true, without saying it constantly.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A villager decides what to do twenty times a second. Anything it says about a
    ///         situation that has not changed - no space for this, nowhere to put that, cannot
    ///         reach the other - is therefore said twenty times a second, and a log with that in
    ///         it is a log nobody can read. The settlement is not malfunctioning; it is repeating
    ///         itself.
    ///     </para>
    ///     <para>
    ///         So a repeat is collapsed into a count and a span: <em>"nowhere to put Wood (47
    ///         times in the last 2 minutes)"</em> rather than the same sentence 47 times. The
    ///         first one is always said immediately, because the first time something goes wrong
    ///         is the moment a player most wants to hear about it.
    ///     </para>
    ///     <para>
    ///         Pure, and compiled into the Unity-free test project. It is the phrasing and the
    ///         timing, both of which are read by a person and neither of which is worth a game
    ///         run to check.
    ///     </para>
    /// </remarks>
    internal static class Repeats
    {
        /// <summary>How long to stay quiet before summarising what has piled up.</summary>
        internal const double QuietForSeconds = 30d;

        /// <summary>Whether enough silence has passed to be worth speaking again.</summary>
        internal static bool DueAgain(double secondsSinceSaid, double quietFor = QuietForSeconds) =>
            secondsSinceSaid >= quietFor;

        /// <summary>
        ///     What to say about something that has happened more than once.
        /// </summary>
        /// <remarks>
        ///     A single repeat is not summarised - "(1 times in the last 30 seconds)" is worse
        ///     than just saying it again, and the plural would be wrong anyway.
        /// </remarks>
        internal static string Summarise(string message, int times, double spanSeconds)
        {
            if (times <= 1) return message;

            return $"{message} ({times} times in the last {Span(spanSeconds)})";
        }

        /// <summary>
        ///     A span of seconds as a person would say it.
        /// </summary>
        /// <remarks>
        ///     Rounded hard on purpose. Nobody reading a log cares whether it was ninety-four
        ///     seconds or ninety-six, and "1.6 minutes" reads as machinery talking to itself.
        /// </remarks>
        internal static string Span(double seconds)
        {
            if (seconds < 1d) return "moment";
            if (seconds < 90d) return $"{(int)seconds} seconds";

            int minutes = (int)((seconds + 30d) / 60d);
            if (minutes < 90) return minutes == 1 ? "minute" : $"{minutes} minutes";

            int hours = (int)((minutes + 30d) / 60d);
            return hours == 1 ? "hour" : $"{hours} hours";
        }
    }
}
