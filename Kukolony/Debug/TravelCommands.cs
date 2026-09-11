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
                _ => Send(), isCheat: false, isNetwork: false, onlyServer: false);

            new Terminal.ConsoleCommand("kukolony_where",
                "says what every loaded villager is doing and how far away it is",
                _ => Where(), isCheat: false, isNetwork: false, onlyServer: false);
        }

        /// <summary>Sends the nearest villager to the player, however far that is.</summary>
        private static void Send()
        {
            Player player = Player.m_localPlayer;
            if (player == null) return;

            Villager nearest = Nearest(player.transform.position);
            if (nearest == null)
            {
                Report.Say("no villager is loaded to send for");
                return;
            }

            Vector3 target = player.transform.position;
            nearest.SendOnErrand(target);

            float distance = Utils.DistanceXZ(nearest.transform.position, target);
            Report.Say($"{nearest.State.Name} is coming - {distance:0}m away. " +
                       $"It will walk while you can see it and cover ground unseen beyond " +
                       $"{ModConfig.TravelObservedRange.Value:0}m. Watch it with kukolony_where.");
        }

        /// <summary>Says what every loaded villager is doing, so a journey can be followed.</summary>
        private static void Where()
        {
            Player player = Player.m_localPlayer;
            if (player == null) return;

            int found = 0;
            foreach (Villager villager in Villager.Instances)
            {
                if (villager == null) continue;

                found++;
                float distance = Utils.DistanceXZ(villager.transform.position, player.transform.position);
                Log.Info($"[travel] {villager.State.Name}: {distance:0}m away, '{villager.Activity}'" +
                         (villager.IsTravelling ? " (travelling)" : string.Empty));
            }

            Report.Say(found == 0
                ? "no villagers are loaded"
                : $"{found} villager(s) reported to the log");
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
