namespace Kukolony.Villagers.Navigation
{
    /// <summary>What to do about a walk that has stopped.</summary>
    internal enum Approaching
    {
        /// <summary>Still going. Ask again next tick.</summary>
        KeepWalking,

        /// <summary>Close enough to work with whatever it was sent to.</summary>
        Arrived,

        /// <summary>Not going to get there. Someone else's problem now.</summary>
        GaveUp
    }

    /// <summary>
    ///     Deciding whether a villager has arrived, when "arrived" is not a fixed distance.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>How close a villager can get is a property of the world, not a setting.</b>
    ///         <c>BaseAI.MoveTo</c> is asked to come right up to the target and stops wherever
    ///         the navmesh runs out - measured at 4.1m from one chest and 7.2m from another,
    ///         because Valheim's path tiles are coarse and a placed piece blocks several metres
    ///         around itself. Judging that against a single number means a villager that is as
    ///         close as it will ever get reports that it cannot get there, forever. That was the
    ///         intermittent hauling failure.
    ///     </para>
    ///     <para>
    ///         So arrival has two ways to be true: near enough to be comfortable, or stopped,
    ///         within working reach, and no longer getting closer. The second is the honest
    ///         reading of "the pathfinder has taken me as far as it can" - and nothing a villager
    ///         does at a container needs an outstretched arm, because containers are worked
    ///         through their ZDO.
    ///     </para>
    ///     <para>
    ///         Pure, and compiled into the Unity-free test project. This decides whether work
    ///         happens at all, every tick, for every villager; it is worth being able to prove
    ///         rather than watch.
    ///     </para>
    /// </remarks>
    internal static class Arrival
    {
        /// <summary>
        ///     The furthest a villager may be and still count as having got there.
        /// </summary>
        /// <remarks>
        ///     Only ever reached when the pathfinder has given up closing the gap. A villager
        ///     that stops ten metres short of a chest with a wall in between is as arrived as it
        ///     is going to be, and the alternative is refusing the job forever.
        /// </remarks>
        internal const float WorkingReach = 10f;

        /// <summary>How long stopped counts as settled rather than pausing.</summary>
        internal const float SettledSeconds = 3f;

        internal static Approaching Judge(bool stopped, float distance, float stopDistance,
            float stalledFor, float patience)
        {
            // Comfortably there. True whether or not the walk has stopped, because a villager
            // that is close enough to work has arrived even if it is still shuffling.
            if (distance <= stopDistance) return Approaching.Arrived;

            if (!stopped) return Approaching.KeepWalking;

            // Stopped short, near enough to work, and no longer closing: this is as close as the
            // world allows. Requiring it to have settled first keeps a momentary stop - rounding
            // a corner, waiting for a path - from being read as the end of the journey.
            if (distance <= WorkingReach && stalledFor >= SettledSeconds) return Approaching.Arrived;

            return stalledFor >= patience ? Approaching.GaveUp : Approaching.KeepWalking;
        }
    }
}
