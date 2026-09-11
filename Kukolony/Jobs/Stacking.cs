using System.Collections.Generic;

namespace Kukolony.Jobs
{
    /// <summary>
    ///     How several part-used stacks of one item should be packed together.
    /// </summary>
    /// <remarks>
    ///     Pure, and compiled into the Unity-free test project, because packing is all
    ///     arithmetic and every interesting case is a boundary: a stack exactly at the maximum,
    ///     a remainder that needs a slot of its own, a single stack that is already right and
    ///     must not be touched. Proving those at a table takes a second; staging them in a game
    ///     chest takes a run each.
    /// </remarks>
    internal static class Stacking
    {
        /// <summary>
        ///     The stack sizes <paramref name="stacks" /> should become, largest first.
        /// </summary>
        /// <remarks>
        ///     Fills stacks to the maximum and leaves one remainder, which is the fewest slots
        ///     the same total can occupy. Returns an empty list when there is nothing to do, so
        ///     a caller can tell "already tidy" from "needs rewriting" without comparing lists -
        ///     and a chest that is already in order is never rewritten, which matters because
        ///     rewriting one makes it save itself and tells every watcher it changed.
        /// </remarks>
        internal static List<int> Pack(List<int> stacks, int maximum)
        {
            List<int> packed = new List<int>();
            if (stacks == null || stacks.Count == 0 || maximum <= 0) return packed;

            int total = 0;
            foreach (int stack in stacks) total += stack;
            if (total <= 0) return packed;

            int slots = (total + maximum - 1) / maximum;

            // Already as tight as it can be. Saying so is the point: the alternative is a
            // settlement that rewrites every chest it looks at, forever.
            if (slots == stacks.Count && stacks.Count == 1) return packed;
            if (slots == stacks.Count && IsAlreadyPacked(stacks, maximum)) return packed;

            int left = total;
            while (left > 0)
            {
                int take = left > maximum ? maximum : left;
                packed.Add(take);
                left -= take;
            }

            return packed;
        }

        /// <summary>Whether every stack but the last is already full.</summary>
        private static bool IsAlreadyPacked(List<int> stacks, int maximum)
        {
            int partial = 0;
            foreach (int stack in stacks)
            {
                if (stack < maximum) partial++;
            }

            return partial <= 1;
        }
    }
}
