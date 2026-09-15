namespace Kukolony.Villagers
{
    /// <summary>What the watch makes of one villager.</summary>
    internal enum Doing
    {
        /// <summary>Nothing is known yet - no stamp, so no opinion.</summary>
        Unknown,

        /// <summary>Meant to be idle: nothing is queued.</summary>
        Unemployed,

        /// <summary>Has work and has finished something recently enough.</summary>
        Working,

        /// <summary>Has work and has finished nothing for too long.</summary>
        Stalled
    }

    /// <summary>
    ///     Noticing a villager that achieves nothing.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>The mod's worst failure is silence.</b> Three unrelated bugs in one night all
    ///         presented the same way - villagers standing about, reporting something reasonable,
    ///         achieving nothing. A hauler livelocked and said "nothing to haul". A destroyed
    ///         villager left in the game's AI list threw out of a patch and killed the whole
    ///         update loop, and every villager after it said "idle". A container lookup could
    ///         never succeed against a villager's bag, so haulers had never once relieved a
    ///         crafter, and that said "nothing to haul" too. None raised an error; each was found
    ///         only because a four-minute run happened to count something afterwards.
    ///     </para>
    ///     <para>
    ///         <b>Completion is the only honest signal.</b> Running covers chopping a tree and
    ///         livelocking alike; Skipped covers "nothing to do" and "this can never succeed"
    ///         alike. Only <c>JobResult.Completed</c> means a repetition actually finished, so
    ///         what is measured is the time since the last one.
    ///     </para>
    ///     <para>
    ///         <b>Pure, and that is not incidental.</b> A thing whose job is to catch what runs do
    ///         not should not itself need a run to be trusted. Every branch here is checked in a
    ///         second by the Unity-free suite; the driver around it only supplies a clock, a
    ///         stamp and a queue.
    ///     </para>
    /// </remarks>
    internal static class IdleWatch
    {
        /// <summary>
        ///     What the watch makes of one villager, given the clock and its record.
        /// </summary>
        /// <param name="workedAt">
        ///     When it last finished a repetition, in world seconds, or zero when never.
        /// </param>
        /// <param name="queued">How many jobs it has been given.</param>
        internal static Doing Judge(double now, double workedAt, int queued, double threshold)
        {
            // Nothing queued is nothing expected. Complaining would bury the villagers that are
            // supposed to be working, which is the only reason anybody would read this at all.
            if (queued <= 0) return Doing.Unemployed;

            // No stamp is unknown, not ancient. A villager in a save from before this existed has
            // none, and reading zero as "idle since the dawn of time" would greet a player with a
            // settlement of complaints on the first load after an update.
            if (workedAt <= 0d) return Doing.Unknown;

            // A clock that has gone backwards - a reload, a peer with a different notion of world
            // time - is not evidence of idling. Treated as freshly seen rather than as a very long
            // wait, because the alternative reports every villager the instant a world reopens.
            double idle = now - workedAt;
            if (idle < 0d) return Doing.Working;

            return idle >= threshold ? Doing.Stalled : Doing.Working;
        }

        /// <summary>How long this villager has achieved nothing, or zero when that is unknown.</summary>
        internal static double IdleFor(double now, double workedAt)
        {
            if (workedAt <= 0d) return 0d;

            double idle = now - workedAt;
            return idle < 0d ? 0d : idle;
        }

        /// <summary>
        ///     A span of seconds as a person would say it.
        /// </summary>
        /// <remarks>
        ///     Shown on a row beside the villager's own words, so it has to be short - "14m", not
        ///     "14 minutes 3 seconds". Minutes once past one, because a settlement measured in
        ///     seconds is a settlement nobody is worried about yet.
        /// </remarks>
        internal static string Spell(double seconds)
        {
            if (seconds < 1d) return "just now";
            if (seconds < 60d) return $"{(int)seconds}s";
            if (seconds < 3600d) return $"{(int)(seconds / 60d)}m";

            return $"{(int)(seconds / 3600d)}h";
        }
    }
}
