using Kukolony.Jobs;

namespace Kukolony.Party
{
    /// <summary>
    ///     Which jobs a villager can still do while it is following somebody.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The rule is not a list of preferences, it falls out of what each job needs. A job
    ///         that needs a <b>registered structure</b> cannot work in a party, because out in the
    ///         field there are none. A job that needs only ground and a tool works anywhere.
    ///     </para>
    ///     <para>
    ///         <b>Refused out loud, never silently skipped.</b> "Tending needs a station, and
    ///         there is none out here" is a sentence a player can act on; a job that runs and
    ///         achieves nothing is the failure this codebase has paid for over and over - the cold
    ///         grill, the errand that fetched for ever, the check that passed while proving
    ///         nothing. A refusal is a Skipped, so it costs a repetition and the queue moves on to
    ///         something the villager can actually do.
    ///     </para>
    ///     <para>
    ///         Written as an explicit switch with no default that guesses. A new job kind should
    ///         arrive here and be decided, not be quietly allowed into the field because the
    ///         fall-through said yes - the same discipline the tool errand's naming keeps, for the
    ///         same reason it was added there.
    ///     </para>
    /// </remarks>
    internal static class PartyWork
    {
        /// <summary>Whether this kind of work is possible away from the settlement.</summary>
        internal static bool Allows(JobKind kind)
        {
            switch (kind)
            {
                case JobKind.Chop:
                case JobKind.Mine:
                case JobKind.Forage:
                    return true;

                case JobKind.Haul:
                case JobKind.Tend:
                case JobKind.Craft:
                case JobKind.Farm:
                case JobKind.Repair:
                    return false;

                default:
                    return false;
            }
        }

        /// <summary>Why it cannot, in words a player can do something about.</summary>
        internal static string WhyNot(JobKind kind)
        {
            switch (kind)
            {
                case JobKind.Haul: return "nowhere out here to put things";
                case JobKind.Tend: return "no station out here to tend";
                case JobKind.Craft: return "no station out here to work at";
                case JobKind.Farm: return "no field out here to sow";
                case JobKind.Repair: return "no crafting station out here";
                default: return "cannot be done away from the settlement";
            }
        }
    }
}
