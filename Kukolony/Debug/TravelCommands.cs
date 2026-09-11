using System.Collections.Generic;
using Kukolony.Colonies;
using Kukolony.Core;
using Kukolony.Villagers;
using UnityEngine;

namespace Kukolony.Debug
{
    /// <summary>
    ///     Console commands for watching a villager travel, by hand.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The automated run cannot check the half of travelling that matters most to a
    ///         player: what it looks like when you are standing next to it. The benchmark's
    ///         player stands still at the hearth for the whole run, so every journey it can
    ///         observe is a short one across ground it has already loaded.
    ///     </para>
    ///     <para>
    ///         So the observed half is verified by a person, and these exist to make that a
    ///         thirty second job rather than a project: walk somewhere, type a word, watch.
    ///     </para>
    /// </remarks>
    internal static class TravelCommands
    {
        private static readonly List<Villager> Nearby = new List<Villager>();

        internal static void Register()
        {
            new Terminal.ConsoleCommand("kukolony_come",
                "sends the nearest villager to where you are standing, and reports what it does",
                args => Send(args.Context), isCheat: false, isNetwork: false, onlyServer: false);

            new Terminal.ConsoleCommand("kukolony_where",
                "says what every loaded villager is doing and how far away it is",
                args => Where(args.Context), isCheat: false, isNetwork: false, onlyServer: false);
        }

        /// <summary>Sends the nearest villager to the player, however far that is.</summary>
        private static void Send(Terminal console)
        {
            Player player = Player.m_localPlayer;
            if (player == null) return;

            Villager nearest = Nearest(player.transform.position);
            if (nearest == null)
            {
                Tell(console, "no villager is loaded to send for");
                return;
            }

            Vector3 target = player.transform.position;
            nearest.SendOnErrand(target);

            float distance = Utils.DistanceXZ(nearest.transform.position, target);
            Tell(console, $"{nearest.State.Name} is coming - {distance:0}m away. Walks while you can " +
                          $"see it, covers ground unseen beyond {ModConfig.TravelObservedRange.Value:0}m.");
        }

        /// <summary>Says what every loaded villager is doing, so a journey can be followed.</summary>
        private static void Where(Terminal console)
        {
            Player player = Player.m_localPlayer;
            if (player == null) return;

            int found = 0;
            foreach (Villager villager in Villager.Instances)
            {
                if (villager == null) continue;

                found++;
                float distance = Utils.DistanceXZ(villager.transform.position, player.transform.position);
                Tell(console, $"{villager.State.Name}: {distance:0}m away, '{villager.Activity}'" +
                              (villager.IsTravelling ? " - travelling" : string.Empty));
            }

            if (found == 0) Tell(console, "no villagers are loaded");
        }

        /// <summary>
        ///     Answers where the question was asked.
        /// </summary>
        /// <remarks>
        ///     Into the console, not the log. The first version of these printed the numbers to
        ///     the log file and a bare count to the screen, which is no use at all to somebody
        ///     standing in the world watching a villager walk - the answer arrived somewhere
        ///     they would have to stop and go and read. The log line is kept as well, because
        ///     afterwards the file is exactly where the answer should be.
        /// </remarks>
        private static void Tell(Terminal console, string what)
        {
            if (console != null) console.AddString(what);
            Log.Info("[travel] " + what);
        }

        private static Villager Nearest(Vector3 to)
        {
            Nearby.Clear();
            Villager best = null;
            float closest = float.MaxValue;

            foreach (Villager villager in Villager.Instances)
            {
                if (villager == null) continue;

                float distance = Utils.DistanceXZ(villager.transform.position, to);
                if (distance >= closest) continue;

                closest = distance;
                best = villager;
            }

            return best;
        }
    }
}
