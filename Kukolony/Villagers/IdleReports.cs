using System.Collections.Generic;

namespace Kukolony.Villagers
{
    /// <summary>
    ///     Who has gone quiet this session, so a check can assert that nobody has.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Kept apart from <see cref="IdleWatch" /> because this half needs a ZDOID and that
    ///         half must not. The judgement is arithmetic over a clock and a stamp and is checked
    ///         without a game running; a thing whose whole purpose is to catch what runs do not
    ///         should not itself need a run to be trusted.
    ///     </para>
    ///     <para>
    ///         Process memory rather than a record: this is about the session in front of you,
    ///         and a fresh game is a fresh start.
    ///     </para>
    /// </remarks>
    internal static class IdleReports
    {
        private static readonly Dictionary<ZDOID, string> Quiet = new Dictionary<ZDOID, string>();

        private static readonly HashSet<ZDOID> Excused = new HashSet<ZDOID>();

        /// <summary>Records that a villager went quiet, and what it claimed to be doing.</summary>
        internal static void Complain(ZDOID villager, string named, string reason, double idle)
        {
            if (villager.IsNone() || Excused.Contains(villager)) return;

            Quiet[villager] = $"{named} did nothing for {IdleWatch.Spell(idle)}: \"{reason}\"";
        }

        /// <summary>Forgets a villager that has started working again.</summary>
        internal static void Working(ZDOID villager)
        {
            if (villager.IsNone()) return;
            Quiet.Remove(villager);
        }

        /// <summary>
        ///     Excuses one villager, for a check that stalls one on purpose.
        /// </summary>
        /// <remarks>
        ///     Per villager rather than a switch that turns the watch off, deliberately: a check
        ///     that means to stall one villager should still catch a second one going quiet
        ///     beside it. A global off switch would have hidden every bug this was built for, in
        ///     exactly the runs that were meant to find them.
        /// </remarks>
        internal static void Excuse(ZDOID villager)
        {
            if (villager.IsNone()) return;

            Excused.Add(villager);
            Quiet.Remove(villager);
        }

        /// <summary>Everything the watch has to complain about, for a check to assert on.</summary>
        internal static List<string> Complaints()
        {
            List<string> said = new List<string>(Quiet.Values);
            said.Sort(System.StringComparer.OrdinalIgnoreCase);
            return said;
        }

        /// <summary>Dropped when a world unloads, and between checks that want a clean slate.</summary>
        internal static void Clear()
        {
            Quiet.Clear();
            Excused.Clear();
        }
    }
}
