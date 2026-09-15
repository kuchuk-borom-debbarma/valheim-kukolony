using System.Collections.Generic;
using Kukolony.Core;
using Kukolony.Villagers;
using UnityEngine;

namespace Kukolony.Party
{
    /// <summary>
    ///     Who is in whose party, and joining and leaving one.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Membership is a single player id on the villager's own record - see
    ///         <see cref="VillagerState.PartyOwner" /> for why it lives there rather than on a
    ///         player. What is left here is resolving that id back to somebody standing in the
    ///         world, and the two gestures.
    ///     </para>
    ///     <para>
    ///         <b>An id that resolves to nobody is an ordinary state, not an error.</b> The player
    ///         may have logged off, may be on the far side of a server, or may simply not be
    ///         loaded. A villager whose player cannot be found stays in the party and stands
    ///         still; it does not conclude it has been abandoned and clear the field, because a
    ///         field cleared while somebody walks through a portal is a party silently dissolved.
    ///     </para>
    /// </remarks>
    internal static class PartyMembership
    {
        /// <summary>
        ///     The player this villager follows, or null when it follows nobody or they are not
        ///     loaded.
        /// </summary>
        internal static Player LeaderOf(VillagerState state)
        {
            if (!state.IsValid) return null;

            long owner = state.PartyOwner;
            return owner == 0L ? null : Find(owner);
        }

        /// <summary>The player with this id, if they are loaded.</summary>
        /// <remarks>
        ///     Walked rather than looked up, because Valheim offers no index by id and the list is
        ///     the number of people on the server - a length this mod can afford to walk and the
        ///     settlement's population is not.
        /// </remarks>
        internal static Player Find(long playerId)
        {
            if (playerId == 0L) return null;

            List<Player> players = Player.s_players;
            if (players == null) return null;

            for (int i = 0; i < players.Count; i++)
            {
                Player player = players[i];
                if (player != null && player.GetPlayerID() == playerId) return player;
            }

            return null;
        }

        /// <summary>
        ///     Puts a villager in a player's party, or takes it out of one.
        /// </summary>
        /// <returns>True when it joined, false when it left.</returns>
        /// <remarks>
        ///     One gesture for both, because there is one field: interacting with a villager that
        ///     follows you stops it, and interacting with one that does not starts it. A second
        ///     gesture for leaving would be a second thing to find at the moment you most want the
        ///     first one to work.
        /// </remarks>
        internal static bool Toggle(Villager villager, Player player)
        {
            if (villager == null || player == null) return false;

            VillagerState state = villager.State;
            if (!state.IsValid) return false;

            long who = player.GetPlayerID();

            // Already following somebody else: taken over rather than refused. Refusing would be
            // a villager that cannot be recruited for a reason invisible to the person trying,
            // and the field cannot hold two answers anyway.
            if (state.PartyOwner == who)
            {
                Leave(state);
                Report.Say($"{state.Name} is no longer following you.");
                return false;
            }

            state.SetPartyOwner(who);
            Report.Say($"{state.Name} is following you.");
            return true;
        }

        /// <summary>Takes a villager out of whatever party it is in.</summary>
        internal static void Leave(VillagerState state) => state.SetPartyOwner(0L);

        /// <summary>
        ///     How far this villager is from its player on the flat, or a negative number when
        ///     there is nobody to measure to.
        /// </summary>
        /// <remarks>
        ///     Flat, as every other distance in this mod is. A villager on a beach and a player on
        ///     the cliff above it are four metres apart by the only measure that matters to
        ///     something that has to walk there.
        /// </remarks>
        internal static float DistanceTo(Player player, Vector3 from) =>
            player == null ? -1f : Utils.DistanceXZ(player.transform.position, from);
    }
}
