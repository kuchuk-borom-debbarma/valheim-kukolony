using System.Collections.Generic;
using Kukolony.Core;
using Kukolony.Gui;
using Kukolony.Villagers;

namespace Kukolony.Colonies
{
    /// <summary>
    ///     The colony's people, as something a screen can offer and a bed can hold.
    /// </summary>
    /// <remarks>
    ///     Works from ZDOIDs rather than loaded villagers, so a roster is complete whether or
    ///     not anyone is standing near them - the same reason the member list stores ids.
    /// </remarks>
    internal static class VillagerRoster
    {
        /// <summary>A villager's name, or a phrase saying why there is none.</summary>
        internal static string Name(ZDOID villager)
        {
            if (villager.IsNone()) return string.Empty;

            ZDO zdo = ZDOMan.instance?.GetZDO(villager);
            if (zdo == null) return "(gone)";

            string name = new VillagerState(zdo).Name;
            return string.IsNullOrEmpty(name) ? "(unnamed)" : name;
        }

        internal static List<PickerScreen.Option> Options(Colony colony, string filter)
        {
            List<PickerScreen.Option> options = new List<PickerScreen.Option>();
            if (colony == null) return options;

            // Offered first and never filtered out, because Assign has always understood an
            // empty choice - it clears the sleeper and says so - and nothing ever gave a player
            // a way to make one. A bed with somebody in it could not be emptied from the
            // screen that fills it, which is the same trap the stopping rule had.
            options.Add(new PickerScreen.Option(string.Empty, "nobody"));

            foreach (ZDOID member in colony.State.GetMembers(ColonyMemberKind.Villager))
            {
                string label = Name(member);
                if (filter.Length > 0 &&
                    label.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                options.Add(new PickerScreen.Option(member.ToString(), label));
            }

            return options;
        }

        /// <summary>
        ///     Puts a villager in a bed, taking them out of whichever bed they were in.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         One villager sleeps in one bed, so assigning a villager who already has one
        ///         moves them and says which bed they left. Two beds both believing they hold
        ///         the same person is the sort of thing nothing notices until a villager walks
        ///         to the wrong one every night.
        ///     </para>
        ///     <para>
        ///         The assignment is ours, written on the record. Valheim's own bed ownership is
        ///         a <c>long</c> player id used for spawn points, and writing a villager into it
        ///         would stop a player being able to claim that bed themselves.
        ///     </para>
        /// </remarks>
        internal static void Assign(Colony colony, StructureRecord bed, List<string> chosen)
        {
            if (colony == null || bed == null) return;

            ZDOID villager = chosen != null && chosen.Count > 0 ? Parse(chosen[0]) : ZDOID.None;
            if (villager.IsNone())
            {
                ColonyOperations.EditSettings(colony, bed.Id, s =>
                {
                    s.Sleeper = ZDOID.None;
                    s.SleeperToken = string.Empty;
                });
                Report.Say($"Nobody sleeps in {bed.Name} now.");
                return;
            }

            string vacated = Vacate(colony, villager, bed.Id);

            ZDO villagerZdo = ZDOMan.instance?.GetZDO(villager);
            if (villagerZdo != null) villagerZdo.SetOwner(ZDOMan.GetSessionID());
            string token = PersistentZdoReference.Ensure(villagerZdo);

            ColonyOperations.EditSettings(colony, bed.Id, s =>
            {
                s.Sleeper = villager;
                s.SleeperToken = token;
            });

            string who = Name(villager);
            Report.Say(vacated.Length > 0
                ? $"{who} sleeps in {bed.Name} now, and no longer in {vacated}."
                : $"{who} sleeps in {bed.Name}.");
        }

        /// <summary>
        ///     Clears this villager out of any other bed. Returns the one they left, or empty.
        /// </summary>
        private static string Vacate(Colony colony, ZDOID villager, ZDOID keep)
        {
            string vacated = string.Empty;
            foreach (StructureRecord record in colony.State.GetStructures())
            {
                if (record.Id == keep || record.Settings == null) continue;
                if (record.Settings.Sleeper != villager) continue;

                vacated = record.Name;
                ColonyOperations.EditSettings(colony, record.Id, s =>
                {
                    s.Sleeper = ZDOID.None;
                    s.SleeperToken = string.Empty;
                });
            }

            return vacated;
        }

        /// <summary>
        ///     Reads back a ZDOID the picker handed out as a string.
        /// </summary>
        /// <remarks>
        ///     The picker stores strings, so the id makes the round trip as text. Parsed
        ///     strictly - a value that does not parse yields None rather than a plausible id
        ///     pointing at nothing in particular.
        /// </remarks>
        private static ZDOID Parse(string value)
        {
            if (string.IsNullOrEmpty(value)) return ZDOID.None;

            int split = value.IndexOf(':');
            if (split <= 0 || split == value.Length - 1) return ZDOID.None;

            if (!long.TryParse(value.Substring(0, split), out long user)) return ZDOID.None;
            if (!uint.TryParse(value.Substring(split + 1), out uint id)) return ZDOID.None;

            return new ZDOID(user, id);
        }
    }
}
