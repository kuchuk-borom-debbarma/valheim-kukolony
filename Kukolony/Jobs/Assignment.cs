using System.Collections.Generic;
using Kukolony.Colonies;
using Kukolony.Core;
using Kukolony.Villagers;

namespace Kukolony.Jobs
{
    /// <summary>
    ///     Giving many villagers the same orders at once.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Works on villagers that are not loaded</b>, which is most of the point. A
    ///         settlement worth assigning in bulk is one spread over enough ground that some of
    ///         it is always out of memory, and orders that only reached whoever happened to be
    ///         standing nearby would be worse than none - a player would have no way to tell
    ///         which half took.
    ///     </para>
    ///     <para>
    ///         A queue lives on the villager's own ZDO, and <c>ZDO.Set</c> ignores its
    ///         <c>okForNotOwner</c> argument: a write by a peer that does not own the ZDO lands
    ///         locally and is discarded on the next sync. So ownership is claimed first, exactly
    ///         as renaming already does.
    ///     </para>
    /// </remarks>
    internal static class Assignment
    {
        /// <summary>
        ///     Gives each villager this queue, and returns how many actually took it.
        /// </summary>
        /// <remarks>
        ///     Counted rather than assumed, because a villager whose ZDO has been destroyed since
        ///     the list was built is a real possibility and "assigned to 12" when it was 11 is the
        ///     kind of small lie that makes a player distrust the whole screen.
        /// </remarks>
        internal static int Apply(IEnumerable<ZDOID> villagers, List<string> queue)
        {
            int assigned = 0;

            foreach (ZDOID id in villagers)
            {
                ZDO zdo = ZDOMan.instance?.GetZDO(id);
                if (zdo == null || !zdo.IsValid()) continue;

                zdo.SetOwner(ZDOMan.GetSessionID());

                VillagerState state = new VillagerState(zdo);
                state.SetQueue(queue);

                // Start at the beginning of the new orders. Without this a villager three jobs
                // into its old queue would resume at position three of a queue that may be one
                // job long - which QueueRunner repairs, but by silently skipping to whatever is
                // there rather than by doing what was just asked.
                state.SetQueuePosition(0);
                state.SetQueueAttempt(0);

                // And drop whatever errand it was on, because it was for the old orders. What it
                // is carrying is kept: the cargo manifest is about the bag, not the job, and a
                // villager holding wood should still deliver it.
                state.ResetJob();

                assigned++;
            }

            return assigned;
        }

        /// <summary>Everyone who belongs to this settlement, loaded or not.</summary>
        internal static List<ZDOID> Everyone(Colony colony) =>
            colony == null
                ? new List<ZDOID>()
                : colony.State.GetMembers(ColonyMemberKind.Villager);
    }
}
