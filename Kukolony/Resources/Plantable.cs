using System.Collections.Generic;
using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Resources
{
    /// <summary>
    ///     Everything a villager needs to know to put one thing in the ground.
    /// </summary>
    /// <remarks>
    ///     A record rather than five parallel dictionaries. Unlike the other classifiers here,
    ///     which answer one question per prefab, sowing asks half a dozen about the same prefab
    ///     in the same tick - what it costs, how much room it wants, whether the ground must be
    ///     cultivated - and a lookup each would be five hash probes to describe one seed.
    /// </remarks>
    internal sealed class Plantable
    {
        /// <summary>The prefab to place. Kept so the job never resolves a name.</summary>
        internal GameObject Prefab;

        /// <summary>What the build menu calls it, localised.</summary>
        /// <remarks>
        ///     The only name a sapling has. Crops can be named by what they grow into - a
        ///     <c>Pickable</c> at the end of the chain - but a tree grows into a <c>TreeBase</c>
        ///     and has nothing pickable to be named after, so the piece's own name is the one
        ///     label that works for everything and is already the one a player has read in the
        ///     build menu.
        /// </remarks>
        internal string Label = string.Empty;

        /// <summary>The seed prefab this costs, and how many.</summary>
        internal string Seed = string.Empty;

        internal int SeedCount = 1;

        /// <summary>
        ///     How much room it wants, in metres.
        /// </summary>
        /// <remarks>
        ///     The larger of the plant's own radius and its vine radius, because either will
        ///     refuse a neighbour. Measured across what this game ships, this runs from 0.5 for
        ///     every crop to 3 for an oak - six to one - which is why the layout takes its pitch
        ///     from the thing being planted rather than using one number.
        /// </remarks>
        internal float Spacing = 1f;

        /// <summary>Whether the ground must be cultivated first.</summary>
        internal bool NeedsCultivated;

        /// <summary>Which biomes it will grow in, as the game's own mask.</summary>
        internal Heightmap.Biome Biomes;

        /// <summary>
        ///     What it eventually gives when picked, by item prefab name. Empty for a tree.
        /// </summary>
        /// <remarks>
        ///     Through the grown prefab's <c>Pickable</c>, which is how "grow carrots" is
        ///     expressible at all. Empty is a real answer rather than a gap: a sapling grows into
        ///     a tree, and a tree is not picked - so a stopping rule counted in the larder simply
        ///     does not apply to a field of birches, and the screen says so instead of offering a
        ///     number that could never be reached.
        /// </remarks>
        internal readonly List<string> Yields = new List<string>();
    }

    /// <summary>
    ///     What can be put in the ground, what it costs, and how much room it wants.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>One component and no ambiguity</b>, as <see cref="Forageable" /> has: a
    ///         <c>Plant</c> exists to be planted and nothing else carries one. So there is no
    ///         opt-in tail here either, and the crop list is the whole of what a player says.
    ///     </para>
    ///     <para>
    ///         <b>An unripe crop is a <c>Plant</c>; a ripe one is not.</b> When it is ready it
    ///         calls <c>Grow()</c> and replaces itself with a different prefab, and that one
    ///         carries <c>Pickable</c>. So this classifier never sees a ripe crop and
    ///         <see cref="Forageable" /> never sees an unripe one - two jobs, two predicates, no
    ///         shared state and no timers between them.
    ///     </para>
    ///     <para>
    ///         Indexed by prefab hash, rebuilt per session because hashes are, and cleared on
    ///         world unload - the arrangement every classifier here shares.
    ///     </para>
    /// </remarks>
    internal static class Planting
    {
        private static readonly Dictionary<int, Plantable> Kinds = new Dictionary<int, Plantable>();

        /// <summary>Insertion order, so a picker and a check see a stable list.</summary>
        private static readonly List<Plantable> Ordered = new List<Plantable>();

        /// <summary>
        ///     The piece that cultivates ground, or null when this game has none.
        /// </summary>
        /// <remarks>
        ///     Found once, because finding it means walking every item's build menu. See
        ///     <see cref="FindCultivator" /> for why it cannot be found where everything else is.
        /// </remarks>
        private static GameObject _cultivator;

        private static bool _looked;

        internal static bool IsReady => Kinds.Count > 0;

        internal static Plantable Of(int prefabHash) =>
            Kinds.TryGetValue(prefabHash, out Plantable found) ? found : null;

        /// <summary>Everything this world can grow, in a stable order.</summary>
        internal static IReadOnlyList<Plantable> All
        {
            get
            {
                if (!IsReady) Rebuild();
                return Ordered;
            }
        }

        /// <summary>The plantable whose prefab has this name, or null.</summary>
        internal static Plantable Named(string prefabName) =>
            string.IsNullOrEmpty(prefabName) ? null : Of(prefabName.GetStableHashCode());

        internal static void Rebuild()
        {
            Kinds.Clear();
            Ordered.Clear();
            if (ZNetScene.instance == null) return;

            int trees = 0, crops = 0;
            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                if (prefab == null) continue;

                Plantable plantable = Describe(prefab);
                if (plantable == null) continue;

                Kinds[prefab.name.GetStableHashCode()] = plantable;
                Ordered.Add(plantable);

                if (plantable.Yields.Count == 0) trees++;
                else crops++;
            }

            Log.Info($"[farm] {crops} crop(s) and {trees} other plantable thing(s) indexed");
        }

        /// <summary>
        ///     Dropped when a world unloads. Prefab hashes are per-session once mods can register
        ///     their own, so carrying entries across worlds risks pointing at whatever later takes
        ///     the same hash - and the cultivator is a live object reference, which does not
        ///     survive one at all.
        /// </summary>
        internal static void Clear()
        {
            Kinds.Clear();
            Ordered.Clear();
            _cultivator = null;
            _looked = false;
        }

        /// <summary>
        ///     Whether a prefab is something a villager could plant, asked straight rather than
        ///     through the index.
        /// </summary>
        /// <remarks>
        ///     Shared with the keep-alive allowlist, which is built from prefabs in its own pass
        ///     and cannot wait for this index to be ready. Two component checks that had to agree
        ///     about what a plant is, written twice, would be two answers to the one question that
        ///     decides whether a crop off-screen ever grows.
        /// </remarks>
        internal static bool IsPlantable(GameObject prefab) =>
            prefab != null && prefab.GetComponent<Plant>() != null;

        /// <summary>
        ///     The piece that turns ground into a field.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>Not in <c>ZNetScene</c>, and that is the trap.</b> A terrain operation
        ///         applies itself during <c>Awake</c> and then destroys its own GameObject, so it
        ///         never needs a <c>ZNetView</c> and is never registered as a networked prefab.
        ///         A sweep of the usual list finds <em>zero</em> - which reads exactly like "this
        ///         game has no cultivator" and would have made the whole setting do nothing while
        ///         looking correct.
        ///     </para>
        ///     <para>
        ///         They live in the build menu of the tool that places them, so this walks every
        ///         item's piece table. By paint type rather than by name: <c>cultivate_v2</c> is
        ///         a spelling, and <c>PaintType.Cultivate</c> is the game saying what it does.
        ///     </para>
        /// </remarks>
        internal static GameObject Cultivator()
        {
            if (_looked) return _cultivator;

            _looked = true;
            if (ObjectDB.instance?.m_items == null) return null;

            foreach (GameObject item in ObjectDB.instance.m_items)
            {
                if (item == null || !item.TryGetComponent(out ItemDrop drop)) continue;

                PieceTable table = drop.m_itemData?.m_shared?.m_buildPieces;
                if (table?.m_pieces == null) continue;

                foreach (GameObject piece in table.m_pieces)
                {
                    if (piece == null || !piece.TryGetComponent(out TerrainOp op)) continue;
                    if (op.m_settings == null) continue;
                    if (op.m_settings.m_paintType != TerrainModifier.PaintType.Cultivate) continue;

                    _cultivator = piece;
                    Log.Info($"[farm] ground is cultivated with '{piece.name}' " +
                             $"(radius {op.m_settings.m_paintRadius:0.##}m)");
                    return _cultivator;
                }
            }

            Log.Warning("[farm] no cultivating piece found - villagers cannot break new ground here");
            return null;
        }

        /// <summary>How far one cultivating covers, or zero when this game has no cultivator.</summary>
        internal static float CultivateRadius()
        {
            GameObject piece = Cultivator();
            return piece != null && piece.TryGetComponent(out TerrainOp op) && op.m_settings != null
                ? op.m_settings.m_paintRadius
                : 0f;
        }

        /// <summary>
        ///     A real prefab name for something this world can grow, or empty when it has none.
        /// </summary>
        /// <remarks>
        ///     So a check can ask for "a crop" and "a thing that needs a lot of room" without
        ///     naming either - the same service the other classifiers do, and for the same
        ///     reason: this mod does not ship the assets and has been wrong about a name before.
        /// </remarks>
        internal static string Sample(bool wantsCultivated, float leastSpacing = 0f)
        {
            if (!IsReady) Rebuild();

            foreach (Plantable plantable in Ordered)
            {
                if (plantable.NeedsCultivated != wantsCultivated) continue;
                if (plantable.Spacing < leastSpacing) continue;
                if (string.IsNullOrEmpty(plantable.Seed)) continue;

                return plantable.Prefab != null ? plantable.Prefab.name : string.Empty;
            }

            return string.Empty;
        }

        /// <summary>Everything about one prefab, or null when it is not something to plant.</summary>
        private static Plantable Describe(GameObject prefab)
        {
            if (!prefab.TryGetComponent(out Plant plant)) return null;

            Plantable plantable = new Plantable
            {
                Prefab = prefab,
                Spacing = Mathf.Max(plant.m_growRadius, plant.m_growRadiusVines),
                NeedsCultivated = plant.m_needCultivatedGround,
                Biomes = plant.m_biome,
                Label = Name(prefab)
            };

            // Every plantable this game ships carries a Piece and so has a seed cost. The guard
            // is for the one a mod adds that does not: free to place is a coherent answer, and
            // better than refusing to index it at all.
            if (prefab.TryGetComponent(out Piece piece) && piece.m_resources != null)
            {
                foreach (Piece.Requirement need in piece.m_resources)
                {
                    if (need?.m_resItem == null) continue;

                    plantable.Seed = need.m_resItem.name;
                    plantable.SeedCount = Mathf.Max(1, need.m_amount);
                    break;
                }
            }

            if (plant.m_grownPrefabs != null)
            {
                foreach (GameObject grown in plant.m_grownPrefabs)
                {
                    if (grown == null || !grown.TryGetComponent(out Pickable pick)) continue;
                    if (pick.m_itemPrefab == null) continue;

                    string gives = pick.m_itemPrefab.name;
                    if (!plantable.Yields.Contains(gives)) plantable.Yields.Add(gives);
                }
            }

            return plantable;
        }

        /// <summary>The piece's own name, localised, falling back to the prefab's.</summary>
        private static string Name(GameObject prefab)
        {
            string token = prefab.TryGetComponent(out Piece piece) ? piece.m_name : null;
            if (string.IsNullOrEmpty(token)) return prefab.name;

            return Localization.instance != null ? Localization.instance.Localize(token) : token;
        }
    }
}
