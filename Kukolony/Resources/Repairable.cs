using System.Collections.Generic;
using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Resources
{
    /// <summary>What is known about one kind of thing that can be mended.</summary>
    internal sealed class Mendable
    {
        /// <summary>
        ///     What it is worth when whole, so damage is a subtraction rather than a lookup.
        /// </summary>
        /// <remarks>
        ///     Health on a piece runs from fifty to several thousand across what this game ships,
        ///     which is why the job's threshold is a fraction rather than a number: "mend anything
        ///     under two hundred" would mean every stone wall always and no armour stand ever.
        /// </remarks>
        internal float Full = 1f;

        /// <summary>
        ///     The crafting station this piece must stand near to be mended, or empty.
        /// </summary>
        /// <remarks>
        ///     A localisation token - <c>$piece_workbench</c>, <c>$piece_stonecutter</c> - because
        ///     that is what <c>CraftingStation.m_name</c> holds and what the range test compares.
        ///     Of the six hundred odd pieces this game ships, four hundred and fifty name one.
        /// </remarks>
        internal string Station = string.Empty;
    }

    /// <summary>
    ///     What decays and can be put right, by prefab hash.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Built <em>and</em> breakable, not merely breakable.</b> Eight hundred prefabs in
    ///         this game carry <c>WearNTear</c> and only six hundred and thirty are pieces; the
    ///         rest are scenery - pots, altars, stairs, and a run of prefabs whose whole purpose
    ///         is to look wrecked: <c>Ashlands_Arch2_Broken1</c>, <c>Ashlands_ArchRoofDamaged</c>.
    ///         A job that admitted those would send villagers out to mend ruins that are supposed
    ///         to be ruins. So a thing qualifies by being something somebody built, which is what
    ///         <c>Piece</c> means.
    ///     </para>
    ///     <para>
    ///         <b>And whether one is damaged needs nothing loaded.</b> <c>WearNTear</c> keeps
    ///         health at <c>ZDOVars.s_health</c> and defaults it to the prefab's own, so the
    ///         question is one float read against a number already in this index - no scene
    ///         lookup and no <c>GetComponent</c>. That is what makes it affordable to sweep a base
    ///         of several thousand pieces, which is the widest net in this mod.
    ///     </para>
    ///     <para>
    ///         Indexed by hash, rebuilt per session because hashes are, and cleared on world
    ///         unload - the arrangement every classifier here shares.
    ///     </para>
    /// </remarks>
    internal static class Repairable
    {
        private static readonly Dictionary<int, Mendable> Kinds = new Dictionary<int, Mendable>();

        /// <summary>
        ///     The prefabs behind those hashes, kept so a check can pick a real piece rather than
        ///     name one. Prefab names are asset data this mod cannot see from the managed
        ///     assembly, and it has been wrong about one before.
        /// </summary>
        private static readonly List<GameObject> Prefabs = new List<GameObject>();

        internal static bool IsReady => Kinds.Count > 0;

        internal static Mendable Of(int prefabHash) =>
            Kinds.TryGetValue(prefabHash, out Mendable found) ? found : null;

        /// <summary>
        ///     Whether a record says this thing is worn below a fraction of what it is worth.
        /// </summary>
        /// <remarks>
        ///     Asked of the ZDO, which is the whole point - see the class docstring. An unwritten
        ///     health reads as the prefab's own, so a piece nobody has ever damaged answers no
        ///     without anything having to be written when it was built.
        /// </remarks>
        internal static bool Worn(ZDO zdo, float below)
        {
            if (zdo == null || !zdo.IsValid()) return false;

            Mendable kind = Of(zdo.GetPrefab());
            if (kind == null || kind.Full <= 0f) return false;

            return zdo.GetFloat(ZDOVars.s_health, kind.Full) < kind.Full * below;
        }

        /// <summary>How worn a thing is, as a fraction, or one when it is whole or unknown.</summary>
        internal static float Wear(ZDO zdo)
        {
            if (zdo == null || !zdo.IsValid()) return 1f;

            Mendable kind = Of(zdo.GetPrefab());
            if (kind == null || kind.Full <= 0f) return 1f;

            return Mathf.Clamp01(zdo.GetFloat(ZDOVars.s_health, kind.Full) / kind.Full);
        }

        internal static void Rebuild()
        {
            Kinds.Clear();
            Prefabs.Clear();
            if (ZNetScene.instance == null) return;

            int needingStation = 0;
            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                if (prefab == null || !Classify(prefab)) continue;

                Mendable kind = Describe(prefab);
                if (kind == null) continue;

                Kinds[prefab.name.GetStableHashCode()] = kind;
                Prefabs.Add(prefab);

                if (kind.Station.Length > 0) needingStation++;
            }

            Log.Info($"[repair] {Kinds.Count} built thing(s) that wear out, " +
                     $"{needingStation} needing a station in range");
        }

        /// <summary>
        ///     Dropped when a world unloads. Prefab hashes are per-session once mods can register
        ///     their own, so carrying entries across worlds risks pointing at whatever later takes
        ///     the same hash.
        /// </summary>
        internal static void Clear()
        {
            Kinds.Clear();
            Prefabs.Clear();
        }

        /// <summary>
        ///     Whether a prefab is something a villager could mend, asked straight rather than
        ///     through the hash index.
        /// </summary>
        /// <remarks>
        ///     Shared with whatever needs it before the index is ready. Two component checks that
        ///     had to agree about what a repairable thing is, written twice, would be two answers
        ///     to one question.
        /// </remarks>
        internal static bool Classify(GameObject prefab)
        {
            if (prefab == null) return false;

            // Both, and the Piece half is the one doing the work: it is the difference between a
            // wall somebody put up and a ruin the world generated to look like one.
            return prefab.GetComponent<WearNTear>() != null && prefab.GetComponent<Piece>() != null;
        }

        /// <summary>
        ///     A real prefab name for something that wears out, or empty when this world has none.
        /// </summary>
        /// <param name="needingStation">
        ///     Whether to name one that demands a crafting station, or one that demands none - the
        ///     two halves a station check needs, neither of which may be guessed at.
        /// </param>
        internal static string Sample(bool needingStation)
        {
            if (!IsReady) Rebuild();

            foreach (GameObject prefab in Prefabs)
            {
                Mendable kind = Of(prefab.name.GetStableHashCode());
                if (kind == null) continue;
                if (kind.Station.Length > 0 != needingStation) continue;

                return prefab.name;
            }

            return string.Empty;
        }

        private static Mendable Describe(GameObject prefab)
        {
            if (!prefab.TryGetComponent(out WearNTear wear)) return null;

            Mendable kind = new Mendable { Full = Mathf.Max(1f, wear.m_health) };

            if (prefab.TryGetComponent(out Piece piece) && piece.m_craftingStation != null)
            {
                kind.Station = piece.m_craftingStation.m_name ?? string.Empty;
            }

            return kind;
        }
    }
}
