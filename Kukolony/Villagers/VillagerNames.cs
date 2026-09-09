using System.Collections.Generic;
using UnityEngine;

namespace Kukolony.Villagers
{
    /// <summary>
    ///     Names given to villagers on first sight. Identity is what turns a pool of
    ///     workers into a settlement, so every villager gets one and keeps it.
    /// </summary>
    internal static class VillagerNames
    {
        private static readonly string[] Pool =
        {
            "Bjorn", "Sigrun", "Hakon", "Astrid", "Ivar", "Freydis", "Olaf", "Thora",
            "Leif", "Gudrun", "Erik", "Solveig", "Ragnar", "Ingrid", "Sten", "Hilda",
            "Torvald", "Runa", "Alvar", "Yrsa"
        };

        /// <summary>
        ///     Picks a name nobody nearby is using. The colony panel identifies a villager
        ///     by name and nothing else, so two "Leif"s in one colony make the rows
        ///     ambiguous - which beds and posts belong to which is then a guess.
        /// </summary>
        internal static string Pick(ICollection<string> taken)
        {
            // Random starting point, then walk: the result is still unpredictable but
            // cannot collide while the pool has anything left.
            int start = UnityEngine.Random.Range(0, Pool.Length);
            for (int i = 0; i < Pool.Length; i++)
            {
                string candidate = Pool[(start + i) % Pool.Length];
                if (!taken.Contains(candidate))
                {
                    return candidate;
                }
            }

            // A colony larger than the pool numbers its people rather than repeating a
            // name, since a duplicate is worse than an inelegant one.
            string fallback = Pool[start];
            for (int n = 2; n <= taken.Count + 2; n++)
            {
                string candidate = $"{fallback} {n}";
                if (!taken.Contains(candidate))
                {
                    return candidate;
                }
            }

            return fallback;
        }
    }
}
