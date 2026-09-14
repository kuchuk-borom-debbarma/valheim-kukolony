using System.Collections.Generic;
using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Resources
{
    /// <summary>What a pickaxe can usefully be swung at.</summary>
    /// <remarks>Persisted in job settings as a bit per kind, so append rather than reorder.</remarks>
    internal enum MineKind
    {
        None = 0,

        /// <summary>
        ///     An ore deposit or a rock formation - anything built on MineRock or MineRock5.
        /// </summary>
        /// <remarks>
        ///     Unambiguous, which is the whole reason this kind is separate. These components
        ///     exist for exactly one purpose and nothing else in the game carries them, so a
        ///     villager pointed at them is never pointed at scenery.
        /// </remarks>
        Deposit = 1,

        /// <summary>
        ///     Loose rock: a Destructible a pickaxe is not outright immune against.
        /// </summary>
        /// <remarks>
        ///     The ambiguous tail, and opt-in for the same reason undergrowth is. The game
        ///     offers no way to tell a boulder from a crate - <c>DestructibleType</c> is
        ///     None, Default, Tree and Character, with no Stone - so this admits a great deal
        ///     of scenery along with the stone somebody actually wanted.
        /// </remarks>
        Boulder = 2
    }

    /// <summary>
    ///     Which prefabs are worth swinging a pickaxe at, and what they yield.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Built from components, never from names</b>, as <see cref="Choppable" /> is and
    ///         for the same reasons: a name list misses every modded deposit, and this mod does
    ///         not ship the assets so it cannot check its own spelling.
    ///     </para>
    ///     <para>
    ///         <b>And what a deposit yields is asset data too.</b> <c>DropTable.m_drops</c> is a
    ///         public list of drops on the prefab, so "which rocks give tin" is answerable with
    ///         nothing loaded. That is what lets a job be set up as <em>mine tin</em> rather than
    ///         as a list of rock names a player would have to know - and it covers a modded
    ///         deposit that drops copper without this mod ever hearing of it.
    ///     </para>
    ///     <para>
    ///         Indexed by hash so finding work costs an integer compare per candidate rather
    ///         than a <c>GetComponent</c>, which is what makes it affordable to look through
    ///         everything the game has loaded.
    ///     </para>
    /// </remarks>
    internal static class Mineable
    {
        private static readonly Dictionary<int, MineKind> Kinds = new Dictionary<int, MineKind>();

        /// <summary>What each indexed prefab drops, by prefab name.</summary>
        private static readonly Dictionary<int, List<string>> Drops = new Dictionary<int, List<string>>();

        /// <summary>
        ///     The prefabs behind those hashes, kept so a check can pick a real deposit rather
        ///     than name one. Prefab names are asset data this mod cannot see from the managed
        ///     assembly, and it has been wrong about one before.
        /// </summary>
        private static readonly List<GameObject> Prefabs = new List<GameObject>();

        internal static bool IsReady => Kinds.Count > 0;

        internal static MineKind Of(int prefabHash) =>
            Kinds.TryGetValue(prefabHash, out MineKind kind) ? kind : MineKind.None;

        /// <summary>What this prefab drops when it is broken, by prefab name.</summary>
        internal static List<string> Yields(int prefabHash) =>
            Drops.TryGetValue(prefabHash, out List<string> found) ? found : new List<string>();

        /// <summary>Whether this prefab drops any of these, or whether nothing was asked for.</summary>
        /// <remarks>
        ///     An empty list means every ore, which is the same reading every other allow-list in
        ///     this mod has - and the same reason: "I did not narrow it" is not "I wanted none".
        /// </remarks>
        internal static bool DropsAny(int prefabHash, List<string> wanted)
        {
            if (wanted == null || wanted.Count == 0) return true;

            foreach (string dropped in Yields(prefabHash))
            {
                if (wanted.Contains(dropped)) return true;
            }

            return false;
        }

        internal static void Rebuild()
        {
            Kinds.Clear();
            Drops.Clear();
            Prefabs.Clear();
            if (ZNetScene.instance == null) return;

            int deposits = 0, boulders = 0;
            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                if (prefab == null) continue;

                MineKind kind = Classify(prefab);
                if (kind == MineKind.None) continue;

                int hash = prefab.name.GetStableHashCode();
                Kinds[hash] = kind;
                Drops[hash] = Yield(prefab);
                Prefabs.Add(prefab);

                if (kind == MineKind.Deposit) deposits++;
                else boulders++;
            }

            Log.Info($"[mine] {deposits} deposit(s) and {boulders} other minable thing(s) indexed");
        }

        /// <summary>
        ///     Dropped when a world unloads. Prefab hashes are per-session once mods can register
        ///     their own, so carrying entries across worlds risks pointing at whatever later
        ///     takes the same hash.
        /// </summary>
        internal static void Clear()
        {
            Kinds.Clear();
            Drops.Clear();
            Prefabs.Clear();
        }

        /// <summary>
        ///     Every ore any deposit in this world drops, for a player choosing what to mine.
        /// </summary>
        /// <remarks>
        ///     Deposits only. Loose rock drops stone, and offering "stone" beside the metals
        ///     would suggest a job set to stone would go and find some - while what it would
        ///     actually do depends on a separate setting being switched on.
        /// </remarks>
        internal static void Ores(List<string> into)
        {
            if (into == null) return;
            if (!IsReady) Rebuild();

            foreach (GameObject prefab in Prefabs)
            {
                if (prefab == null) continue;

                int hash = prefab.name.GetStableHashCode();
                if (Of(hash) != MineKind.Deposit) continue;

                foreach (string dropped in Yields(hash))
                {
                    if (!into.Contains(dropped)) into.Add(dropped);
                }
            }

            into.Sort(System.StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        ///     A real prefab name for a deposit whose minimum tool tier falls in this range, or
        ///     empty when the game has none.
        /// </summary>
        /// <remarks>
        ///     Lets a check ask for "a deposit a bronze pickaxe can break" and "one it cannot"
        ///     without naming either, which is the only way to write that check honestly - the
        ///     same service <see cref="Choppable.SampleTree" /> does for trees.
        /// </remarks>
        internal static string SampleDeposit(int lowestTier, int highestTier)
        {
            foreach (GameObject prefab in Prefabs)
            {
                if (prefab == null) continue;
                if (Of(prefab.name.GetStableHashCode()) != MineKind.Deposit) continue;

                int tier = TierOf(prefab);
                if (tier < lowestTier || tier > highestTier) continue;

                return prefab.name;
            }

            return string.Empty;
        }

        /// <summary>The tool tier a prefab demands, whichever component says so.</summary>
        internal static int TierOf(GameObject prefab)
        {
            if (prefab == null) return 0;

            if (prefab.TryGetComponent(out MineRock5 rock5)) return rock5.m_minToolTier;
            if (prefab.TryGetComponent(out MineRock rock)) return rock.m_minToolTier;
            if (prefab.TryGetComponent(out Destructible destructible)) return destructible.m_minToolTier;

            return 0;
        }

        /// <summary>
        ///     What a pickaxe would make of this prefab, asked straight rather than through the
        ///     hash index.
        /// </summary>
        /// <remarks>
        ///     Shared with the keep-alive allowlist, which is built from prefabs in its own pass
        ///     and cannot wait for this index to be ready. Two component checks that had to agree
        ///     about what a deposit is, written twice, would be two answers to the one question
        ///     that decides whether an off-screen villager can find work at all.
        /// </remarks>
        internal static MineKind Classify(GameObject prefab)
        {
            if (prefab == null) return MineKind.None;

            // A creature and a loose item are never scenery to be mined, whatever they carry.
            if (prefab.GetComponent<Character>() != null) return MineKind.None;
            if (prefab.GetComponent<ItemDrop>() != null) return MineKind.None;

            // Either mining component is unambiguous: nothing else in the game carries them.
            if (prefab.GetComponent<MineRock5>() != null) return MineKind.Deposit;
            if (prefab.GetComponent<MineRock>() != null) return MineKind.Deposit;

            // Everything else has to earn its place by being demonstrably vulnerable to a
            // pickaxe - and even then it is only a candidate, because the game cannot tell a
            // boulder from a crate and this admits both.
            if (prefab.TryGetComponent(out Destructible destructible) &&
                Bites(destructible.m_damages.m_pickaxe))
            {
                return MineKind.Boulder;
            }

            return MineKind.None;
        }

        /// <summary>What a prefab's drop table holds, by prefab name.</summary>
        /// <remarks>
        ///     The table's own list rather than a roll of it. <c>GetDropList</c> picks at random
        ///     and would answer differently every time it was asked, which is no use for a
        ///     question a player is answering once on a screen.
        /// </remarks>
        private static List<string> Yield(GameObject prefab)
        {
            List<string> dropped = new List<string>();
            Add(dropped, Table(prefab));
            return dropped;
        }

        private static DropTable Table(GameObject prefab)
        {
            if (prefab.TryGetComponent(out MineRock5 rock5)) return rock5.m_dropItems;
            if (prefab.TryGetComponent(out MineRock rock)) return rock.m_dropItems;

            // A Destructible keeps its drops on a separate component, which a great many of
            // them simply do not have - so "nothing known to drop" is an ordinary answer here
            // rather than a sign something is wrong.
            return prefab.TryGetComponent(out DropOnDestroyed drops) ? drops.m_dropWhenDestroyed : null;
        }

        private static void Add(List<string> into, DropTable table)
        {
            if (table?.m_drops == null) return;

            foreach (DropTable.DropData drop in table.m_drops)
            {
                if (drop.m_item == null) continue;

                string name = drop.m_item.name;
                if (name.Length > 0 && !into.Contains(name)) into.Add(name);
            }
        }

        /// <summary>Whether pickaxe damage does anything at all to something with these resistances.</summary>
        private static bool Bites(HitData.DamageModifier modifier) =>
            modifier != HitData.DamageModifier.Immune && modifier != HitData.DamageModifier.Ignore;
    }
}
