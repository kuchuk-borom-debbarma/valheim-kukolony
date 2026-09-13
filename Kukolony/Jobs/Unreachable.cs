using System.Collections.Generic;
using UnityEngine;

namespace Kukolony.Jobs
{
    /// <summary>
    ///     What a villager has given up on reaching, and for how long.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Giving up unblocks the queue; it does not stop the villager choosing the same
    ///         thing again. Selection re-derives its answer from the same world on the very
    ///         next tick, so without this a villager walks the same unreachable route for
    ///         ever - a repetition at a time, delivering nothing, and looking from outside
    ///         exactly like one that is working.
    ///     </para>
    ///     <para>
    ///         <b>For a while, not for the session.</b> Chopping refuses a tree permanently
    ///         because "my axe cannot cut this" stays true until the axe changes. Not being
    ///         able to reach something is different: the player bridges the gully, levels the
    ///         slope, or builds a path, and the settlement should notice without being
    ///         reloaded. So it lapses.
    ///     </para>
    ///     <para>
    ///         Per villager, because one villager's failure says nothing about another's -
    ///         they stand somewhere else and the ground between differs.
    ///     </para>
    /// </remarks>
    internal static class Unreachable
    {
        /// <summary>
        ///     How long a thing stays refused.
        /// </summary>
        /// <remarks>
        ///     Long enough that the villager gets on with other work rather than cycling
        ///     straight back, short enough that a player who fixes the ground does not have to
        ///     wonder whether the mod noticed.
        /// </remarks>
        internal const float RefusedForSeconds = 300f;

        /// <summary>
        ///     How long something merely blocked is refused.
        /// </summary>
        /// <remarks>
        ///     Much shorter, because the verdict behind it is much weaker: inside a settlement
        ///     a path is given up on after four seconds of no progress, which is as easily
        ///     another villager standing in the doorway as it is a wall. Long enough to break
        ///     a tick-rate retry loop, short enough that a doorway is forgiven.
        /// </remarks>
        internal const float BlockedForSeconds = 20f;

        private static readonly Dictionary<ZDOID, Dictionary<ZDOID, float>> Refused =
            new Dictionary<ZDOID, Dictionary<ZDOID, float>>();

        /// <summary>Dropped when a world unloads; none of these identities survive one.</summary>
        internal static void Clear() => Refused.Clear();

        /// <summary>Drops what a villager that no longer exists had given up on.</summary>
        internal static void Forget(ZDOID villager)
        {
            if (!villager.IsNone()) Refused.Remove(villager);
        }

        internal static void Refuse(ZDOID villager, ZDOID target, float seconds = RefusedForSeconds)
        {
            if (villager.IsNone() || target.IsNone()) return;

            if (!Refused.TryGetValue(villager, out Dictionary<ZDOID, float> mine))
            {
                mine = new Dictionary<ZDOID, float>();
                Refused[villager] = mine;
            }

            // The longest standing refusal wins. A brief block arriving after a hard give-up
            // must not shorten it.
            float until = Time.time + seconds;
            if (mine.TryGetValue(target, out float already) && already > until) return;

            mine[target] = until;
        }

        /// <summary>Whether this villager has given up on reaching this, recently enough to count.</summary>
        internal static bool Refuses(ZDOID villager, ZDOID target)
        {
            if (villager.IsNone() || target.IsNone()) return false;
            if (!Refused.TryGetValue(villager, out Dictionary<ZDOID, float> mine)) return false;
            if (!mine.TryGetValue(target, out float until)) return false;

            if (Time.time < until) return true;

            // Lapsed. Dropped on the way past rather than swept, so the table cannot grow
            // without bound and nothing has to run on a timer to keep it small.
            mine.Remove(target);
            return false;
        }
    }
}
