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

        internal static string Random() => Pool[UnityEngine.Random.Range(0, Pool.Length)];
    }
}
