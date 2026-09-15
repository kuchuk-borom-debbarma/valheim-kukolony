using Kukolony.Villagers;
using Kukolony.Villagers.Navigation;
using UnityEngine;

namespace Kukolony.Party
{
    /// <summary>
    ///     Staying with the player whose party this villager is in.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Asked on the work tick beside <see cref="Villagers.Resting" /> and
    ///         <see cref="Villagers.Eating" />, and in the same shape: it answers true when it has
    ///         taken the tick. It sits below eating - a hungry villager should still eat, and it
    ///         already can, because eating looks in the bag before it looks for a chest - and
    ///         above resting, which is suspended in a party entirely.
    ///     </para>
    ///     <para>
    ///         <b>A party villager does not run its job queue yet.</b> Its work is still bounded
    ///         by its Kolony's ground while it is being walked away from it, so letting the queue
    ///         run would send it back to a tree at home, out past the leash, and back again for
    ///         ever. Work in a party needs the player to <em>be</em> the work area, which is the
    ///         next stage. Until then this takes the tick whenever a villager is in a party, and
    ///         says which of the two things it is doing.
    ///     </para>
    /// </remarks>
    internal static class Escort
    {
        /// <summary>
        ///     Keeps up, if it is in a party.
        /// </summary>
        /// <param name="closing">
        ///     Whether it decided to close the gap last tick, which is what gives the two
        ///     distances their grip. A plain field rather than anything persisted: losing it on an
        ///     ownership transfer costs one re-evaluation against the leash, and a villager
        ///     standing beside its player evaluates to Holding either way.
        /// </param>
        /// <returns>True when the villager is dealing with its party and must not be given work.</returns>
        internal static bool Tick(Villager villager, VillagerState state, VillagerWalk walk,
            ref bool closing, float deltaTime, out string doing)
        {
            doing = string.Empty;
            if (villager == null || !state.IsValid || !state.InAParty) 
            {
                closing = false;
                return false;
            }

            Player leader = PartyMembership.LeaderOf(state);

            // In a party, with nobody to be found. An ordinary state rather than an error: they
            // may have logged off, gone through a portal, or simply not be loaded. It waits
            // rather than concluding it has been abandoned - a party that dissolves itself
            // because somebody took a portal is a party you cannot rely on.
            if (leader == null)
            {
                closing = false;
                walk.Stop();
                doing = "waiting for someone";
                return true;
            }

            float distance = PartyMembership.DistanceTo(leader, villager.transform.position);
            closing = Following.Decide(distance, Leash, Comfort, closing) == Keeping.Closing;

            string who = leader.GetPlayerName();

            if (!closing)
            {
                // Near enough. Stopped explicitly rather than left mid-walk, so it does not creep
                // the last metre for the rest of the session.
                walk.Stop();
                doing = $"with {who}";
                return true;
            }

            // Run when it has real ground to make up. A villager that jogs the last three metres
            // looks panicked; one that walks forty looks lost.
            walk.MoveTowards(leader.transform.position, Comfort, distance > Leash * 2f, deltaTime);
            doing = $"following {who}";
            return true;
        }

        private static float Leash =>
            ModConfig.PartyLeashDistance != null ? ModConfig.PartyLeashDistance.Value : 12f;

        /// <summary>
        ///     How near it gets before it stops, never at or beyond the leash.
        /// </summary>
        /// <remarks>
        ///     The clamp lives in <see cref="Following.Decide" /> too, because that is where it
        ///     can be tested. Here it only keeps the walk's own stop distance honest: handing
        ///     <c>MoveTowards</c> a stop distance larger than the leash is a villager that arrives
        ///     the instant it sets off.
        /// </remarks>
        private static float Comfort
        {
            get
            {
                float wanted = ModConfig.PartyComfortDistance != null
                    ? ModConfig.PartyComfortDistance.Value
                    : 4f;
                float leash = Leash;

                return wanted < leash ? wanted : leash;
            }
        }
    }
}
