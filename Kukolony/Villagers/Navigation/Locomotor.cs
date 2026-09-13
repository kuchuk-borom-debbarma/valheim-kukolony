namespace Kukolony.Villagers.Navigation
{
    /// <summary>What a travelling villager should do about moving this tick.</summary>
    internal enum Locomotion
    {
        /// <summary>Use its legs, which is how a villager is supposed to get anywhere.</summary>
        Walk,

        /// <summary>Cover ground without walking it, because walking is not working.</summary>
        CoverGround,

        /// <summary>Stop covering ground and go back to walking.</summary>
        BackOnFoot,

        /// <summary>Put it back on the navmesh where it stands and let it try again.</summary>
        PutBackOnNavmesh,

        /// <summary>Give up on the ground for a while.</summary>
        BeginRescue
    }

    /// <summary>What is true about a journey right now.</summary>
    internal readonly struct TravelFacts
    {
        internal TravelFacts(bool rescuing, bool travelling, bool burstSpent, bool observed,
            bool stalled, bool politeRescuesLeft, bool canStand, bool waterAhead = false,
            bool nearLand = false, bool hasRoute = false)
        {
            HasRoute = hasRoute;
            Rescuing = rescuing;
            Travelling = travelling;
            BurstSpent = burstSpent;
            Observed = observed;
            Stalled = stalled;
            PoliteRescuesLeft = politeRescuesLeft;
            CanStand = canStand;
            WaterAhead = waterAhead;
            NearLand = nearLand;
        }

        /// <summary>Already covering ground rather than walking.</summary>
        internal bool Rescuing { get; }

        /// <summary>On a journey longer than the settlement is wide.</summary>
        internal bool Travelling { get; }

        /// <summary>This rescue has run for as long as it was given.</summary>
        internal bool BurstSpent { get; }

        /// <summary>A player is close enough to see what happens.</summary>
        internal bool Observed { get; }

        /// <summary>Has not meaningfully closed the distance for a while.</summary>
        internal bool Stalled { get; }

        /// <summary>Has not yet used up the rescues a player may watch.</summary>
        internal bool PoliteRescuesLeft { get; }

        /// <summary>There is somewhere within reach an agent of this kind can stand.</summary>
        internal bool CanStand { get; }

        /// <summary>
        ///     The pathfinder has given this villager a leg it can actually walk.
        /// </summary>
        /// <remarks>
        ///     The answer to a question the ladder could not previously ask. Every rescue here
        ///     exists because a villager could not get somewhere on foot, and the evidence for
        ///     that used to be circumstantial - it has stopped making progress, or a straight
        ///     line to the next leg crosses water. Now the leg is chosen by asking the navmesh
        ///     for a full path, so "there is a way and it is this one" is a fact rather than an
        ///     inference, and a villager holding one has no business being carried.
        /// </remarks>
        internal bool HasRoute { get; }

        /// <summary>
        ///     There is somewhere to stand within a stride or two - close enough that being
        ///     put down there reads as stepping ashore rather than teleporting.
        /// </summary>
        /// <remarks>
        ///     <see cref="CanStand" /> searches out to sixty metres, because a rescue on
        ///     land is better placed far than not placed at all. A watched water crossing
        ///     is the one case where that generosity is wrong: putting a mid-sea villager
        ///     down on a shore sixty metres away - possibly the shore it left - is a snap
        ///     in front of the player, and landing on the departure shore restarts the
        ///     whole crossing. Near land, landing is believable; far from it, the crossing
        ///     continues.
        /// </remarks>
        internal bool NearLand { get; }

        /// <summary>
        ///     The next stretch of the route is under water.
        /// </summary>
        /// <remarks>
        ///     Water is not "stuck". A villager facing an ocean used to be handled by the
        ///     rescue ladder - forty-five seconds of stalling at the shoreline, then escalating
        ///     glides - which arrives eventually and looks broken the whole way. Ground that
        ///     can never be walked is known in advance by sampling the route, so crossing it
        ///     unseen is a decision rather than a recovery.
        /// </remarks>
        internal bool WaterAhead { get; }
    }

    /// <summary>
    ///     Choosing between walking and being rescued, as a pure function.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This decision has been got wrong four times, in four different ways, each of which
    ///         looked correct while being written and cost a five-minute run to disprove. It is
    ///         six booleans; it belongs in the Unity-free project where all sixty-four
    ///         combinations can be checked in a second.
    ///     </para>
    ///     <para>
    ///         <b>The property that matters is that a villager is never left doing neither.</b>
    ///         The worst failure was not a villager that glided when it should have walked - it
    ///         was one forbidden from gliding because it was in view and unable to walk because
    ///         there was nowhere to put it, standing at nought metres per second for five minutes
    ///         reporting that it was travelling. Walking is preferred everywhere it is possible;
    ///         where it is not, something else must happen.
    ///     </para>
    /// </remarks>
    internal static class Locomotor
    {
        internal static Locomotion Decide(TravelFacts facts)
        {
            if (facts.Rescuing)
            {
                // A route has appeared. Put it down and let it walk, whatever else is true -
                // this is checked before everything below because everything below is a reason
                // to keep carrying somebody, and none of them outrank being able to walk. It is
                // also the difference between a rescue and a mode: without it, a villager
                // carried out of one bad patch stayed carried until its burst ran out, which
                // measured at half the journey.
                if (facts.HasRoute && facts.CanStand) return Locomotion.BackOnFoot;

                // The journey is over: stop, whether or not there is anywhere good to stand.
                // Continuing to cover ground towards somewhere already reached is pointless, and
                // the villager would arrive sliding - which made the whole thing flaky, because
                // whether the navmesh at the destination had finished building decided whether
                // it walked in or skated in. A villager always finishes on its feet.
                if (!facts.Travelling) return Locomotion.BackOnFoot;

                // Otherwise stop when the rescue is spent, or when somebody can see it and it has
                // not yet earned the right to be seen doing this - but only where there is ground
                // to stand on. Otherwise keep going, because the alternative is standing still.
                // Never back on foot into the sea: while the route ahead is water the crossing
                // continues, unseen unconditionally, and watched unless there is land within a
                // stride - because "land where landing is possible" through CanStand's
                // sixty-metre search meant a mid-sea villager snapping onto the shore it left,
                // in front of the player, and starting the crossing over. Stepping ashore is
                // believable; teleporting to a shore is not.
                if (facts.WaterAhead && !(facts.Observed && facts.NearLand))
                    return Locomotion.CoverGround;

                bool wantsToWalk = facts.BurstSpent || (facts.Observed && facts.PoliteRescuesLeft);

                return wantsToWalk && facts.CanStand ? Locomotion.BackOnFoot : Locomotion.CoverGround;
            }

            // Water ahead on a journey is crossed deliberately, not stalled at. This is
            // before the stall test on purpose: the shoreline is exactly where a villager
            // stops making progress, and waiting for the stall means waiting at the beach.
            //
            // But only unseen. Crossing water is covering ground, and covering ground in
            // view is the one thing the whole ladder exists to prevent - and the probe is a
            // straight chord to the waypoint, so a walkable route that merely curves around
            // a bay reads as water. Watched, the villager keeps walking; if the water really
            // does block it, the stall ladder below takes over with its measured politeness.
            // ...but only when there is no way round. The probe is a straight chord to the
            // waypoint, so a route that merely curves around a bay reads as water - which, on a
            // coastal settlement, is most routes: the travel gate measured a villager gliding
            // part of a hundred-and-sixty-metre walk over open meadow for exactly this reason.
            // A villager holding a walkable leg walks it, and the crossing is for water that
            // genuinely has no way round.
            if (facts.Travelling && facts.WaterAhead && !facts.Observed && !facts.HasRoute)
                return Locomotion.BeginRescue;

            if (!facts.Travelling || !facts.Stalled) return Locomotion.Walk;

            // Stalled while holding a route it can walk. Whatever is wrong, gliding is not the
            // answer to it: a villager with a full path to a leg forty-five metres ahead is not
            // a villager that needs carrying, and the stall is more likely the navmesh still
            // building under it or a doorway somebody else is standing in. It keeps walking.
            if (facts.HasRoute) return Locomotion.Walk;

            // Stalled with nowhere to walk. The gentlest thing that might help is being put back
            // on the navmesh, and it is only worth trying where there is somewhere to be put.
            // A snap is a teleport of a metre or two rather than a body sliding across country,
            // so it is preferred to covering ground wherever it is available at all - the
            // politeness counter only decides whether it is done in view.
            if (facts.CanStand && (facts.PoliteRescuesLeft || !facts.Observed))
            {
                return Locomotion.PutBackOnNavmesh;
            }

            return Locomotion.BeginRescue;
        }
    }
}
