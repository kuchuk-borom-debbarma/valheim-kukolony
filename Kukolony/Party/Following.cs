namespace Kukolony.Party
{
    /// <summary>What a party villager is doing about the distance to its player.</summary>
    internal enum Keeping
    {
        /// <summary>Near enough. Get on with whatever else there is to do.</summary>
        Holding,

        /// <summary>Too far, or not yet back within comfort. Walk towards them.</summary>
        Closing
    }

    /// <summary>
    ///     Staying near the player whose party this villager is in.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Two distances, not one</b>, for the reason <see cref="Villagers.Energy" /> has
    ///         two: a single threshold makes a villager flicker. It would stop at the leash, drift
    ///         half a metre, start closing, arrive, drift again - re-aiming a walk every tick while
    ///         standing essentially still. With no cap on party size that is five or twenty
    ///         villagers doing it at once, and the jitter is visible long before the cost is.
    ///     </para>
    ///     <para>
    ///         So the leash is what <em>starts</em> a villager closing and comfort is what
    ///         <em>stops</em> it, and each state has to be earned. The same shape as tired-below
    ///         and rested-above, which took a bug to arrive at and should not need a second one.
    ///     </para>
    ///     <para>
    ///         Pure, and compiled into the Unity-free test project: it is one comparison with a
    ///         boundary at each end, and both boundaries are a villager that never catches up or
    ///         never stops walking.
    ///     </para>
    /// </remarks>
    internal static class Following
    {
        /// <summary>
        ///     Whether to walk towards the player, given whether it already was.
        /// </summary>
        /// <param name="distance">How far the villager is from its player, on the flat.</param>
        /// <param name="leash">Beyond this, a holding villager starts closing.</param>
        /// <param name="comfort">Inside this, a closing villager stops.</param>
        /// <param name="alreadyClosing">What it decided last time, which is what makes this stick.</param>
        /// <remarks>
        ///     <b>A comfort at or beyond the leash is treated as no band at all.</b> Two
        ///     independent config numbers can be set that way by anybody who has not thought about
        ///     it, and the result would be a villager that starts closing at the leash and is
        ///     immediately already comfortable - the exact flicker the band exists to remove. The
        ///     pairing is enforced here rather than trusted to the person editing the file, as the
        ///     hunger threshold's is.
        /// </remarks>
        /// <summary>
        ///     The leash actually in force, never inside the ground the villager works.
        /// </summary>
        /// <remarks>
        ///     <b>The leash and the party work radius describe the same circle from opposite
        ///     ends</b>, and a leash inside the radius makes a villager fight itself: the job
        ///     sends it to a tree at the edge of its area, arriving breaches the leash, the escort
        ///     drags it back, the job sends it out again. Measured doing exactly that - seventy
        ///     seconds reported as "following" beside a tree it never touched, with every
        ///     individual part behaving exactly as written.
        /// </remarks>
        internal static float LeashFor(float configured, float workRadius)
        {
            // Strictly outside, not merely equal. Work at the very edge of the area is work the
            // villager must be able to stand at, and a leash exactly on the boundary means
            // arriving is breaching - the same oscillation, moved one metre. Measured: with the
            // two set equal it still reported "following" for seventy seconds beside a tree it
            // never touched.
            float needed = workRadius + Margin;

            return configured > needed ? configured : needed;
        }

        /// <summary>Room to stand at the far edge of the work area without being dragged back.</summary>
        internal const float Margin = 8f;

        internal static Keeping Decide(float distance, float leash, float comfort, bool alreadyClosing)
        {
            float stop = comfort < leash ? comfort : leash;

            if (alreadyClosing) return distance > stop ? Keeping.Closing : Keeping.Holding;

            return distance > leash ? Keeping.Closing : Keeping.Holding;
        }
    }
}
