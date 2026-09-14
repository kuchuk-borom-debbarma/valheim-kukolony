using Kukolony.Core;
using Kukolony.Jobs;
using UnityEngine;

namespace Kukolony.Resources.Farming
{
    /// <summary>What came of trying to put something in the ground.</summary>
    internal enum SowResult
    {
        /// <summary>Nothing to plant, or nowhere to plant it.</summary>
        Unworkable,

        /// <summary>The seed is not in the bag.</summary>
        NoSeed,

        /// <summary>The ground will not have it. Nothing was planted and no seed was spent.</summary>
        Refused,

        /// <summary>In the ground, and the seed is gone.</summary>
        Sown
    }

    /// <summary>
    ///     Putting one thing in the ground, and breaking ground to put it in.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>The plant is asked, never second-guessed.</b> <c>Plant.UpdateHealth</c> and
    ///         <c>Plant.GetStatus()</c> are both public, and between them they are the whole
    ///         planting rulebook: biome, cultivated ground, heat, cold, roof, and room for the
    ///         thing to grow. So this places one and asks it, rather than carrying a copy of
    ///         seven rules that would be wrong the day the game changed any of them.
    ///     </para>
    ///     <para>
    ///         <b>Placed, asked, and undone if the answer is no.</b> The status is a property of
    ///         a plant standing in a particular spot, so there is no way to ask without standing
    ///         one there. The cost is one instantiate that may be immediately destroyed, and the
    ///         benefit is that a villager is never wrong about what will grow.
    ///     </para>
    ///     <para>
    ///         <b>And the seed is spent last.</b> A refusal costs nothing, which is the whole
    ///         difference between a villager that tries a bad square and one that quietly eats a
    ///         settlement's seed stock trying the same bad square all afternoon.
    ///     </para>
    /// </remarks>
    internal static class Sowing
    {
        /// <summary>
        ///     Puts one plant in the ground at a point, or says why it would not grow there.
        /// </summary>
        /// <param name="bag">Where the seed comes from. Untouched unless the plant takes.</param>
        internal static SowResult Place(Plantable what, Vector3 at, Inventory bag, out string why)
        {
            why = string.Empty;

            if (what?.Prefab == null || ZNetScene.instance == null)
            {
                why = "there is nothing to plant";
                return SowResult.Unworkable;
            }

            // Asked before anything is instantiated. Checking afterwards would mean creating and
            // destroying an object every tick a villager stood in a field with an empty bag.
            if (!string.IsNullOrEmpty(what.Seed) && Spending.Held(bag, what.Seed) < what.SeedCount)
            {
                why = "no " + what.Seed;
                return SowResult.NoSeed;
            }

            // Turned at random about the upright, because a field of saplings all facing one way
            // reads as a mod rather than as a farm. The position is exact; only the facing is not.
            Quaternion facing = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            GameObject planted = Object.Instantiate(what.Prefab, at, facing);

            if (planted == null || !planted.TryGetComponent(out Plant plant))
            {
                why = "that will not plant";
                Uproot(planted);
                return SowResult.Unworkable;
            }

            // The rulebook, asked of the thing itself. Zero seconds since planting, because that
            // is true - and because the alternative, waiting for its own slow update, is several
            // seconds during which a villager has no idea whether it worked.
            plant.UpdateHealth(0d);
            Plant.Status status = plant.GetStatus();

            if (status != Plant.Status.Healthy)
            {
                why = Explain(status);
                Uproot(planted);
                return SowResult.Refused;
            }

            // It will grow. Only now does the seed go, and if the spend fails - which it can, on
            // an NG+ world, silently - the plant comes straight back out rather than being left
            // as something the settlement got for nothing.
            if (!string.IsNullOrEmpty(what.Seed) &&
                !Spending.Spend(bag, what.Seed, what.SeedCount, out string failed))
            {
                why = failed;
                Uproot(planted);
                return SowResult.NoSeed;
            }

            Credit(planted);

            if (planted.TryGetComponent(out Piece piece)) piece.m_placeEffect?.Create(at, facing);

            why = "planted";
            return SowResult.Sown;
        }

        /// <summary>
        ///     Breaks ground so something can be grown in it.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         One instantiate and no cleanup: <c>TerrainOp.Awake</c> applies its operation to
        ///         every heightmap in range and then destroys its own GameObject. That is also why
        ///         the piece is not in <c>ZNetScene</c> and has to be found in the cultivator's
        ///         build menu - see <see cref="Planting.Cultivator" />.
        ///     </para>
        ///     <para>
        ///         <b>The one thing this mod does that a player cannot undo</b> by unregistering
        ///         something. It runs only when a field has been told it may, and only inside that
        ///         field's own radius.
        ///     </para>
        /// </remarks>
        internal static bool Cultivate(Vector3 at)
        {
            GameObject piece = Planting.Cultivator();
            if (piece == null) return false;

            Object.Instantiate(piece, at, Quaternion.identity);
            return true;
        }

        /// <summary>Whether the ground at a point is already what this plant needs.</summary>
        /// <remarks>
        ///     <para>
        ///         The cheap half of the question, asked before a villager walks anywhere. The
        ///         expensive and exact half is <see cref="Place" />, which asks the plant.
        ///     </para>
        ///     <para>
        ///         <c>IsCultivated</c> belongs to a <em>heightmap</em> rather than to the class,
        ///         so the tile has to be found first - and a point with no tile is a point outside
        ///         the loaded world, which is not cultivated in any useful sense.
        ///     </para>
        /// </remarks>
        internal static bool GroundIsReady(Plantable what, Vector3 at)
        {
            if (what == null || !what.NeedsCultivated) return true;

            Heightmap ground = Heightmap.FindHeightmap(at);
            return ground != null && ground.IsCultivated(at);
        }

        /// <summary>Takes a plant back out, ZDO and all.</summary>
        /// <remarks>
        ///     Through <c>ZNetScene.Destroy</c> rather than Unity's, because the object carries a
        ///     ZDO and destroying the GameObject alone leaves the record behind - a plant every
        ///     peer still believes in, on ground this one thinks is empty.
        /// </remarks>
        private static void Uproot(GameObject planted)
        {
            if (planted == null) return;

            if (planted.TryGetComponent(out ZNetView view) && view.IsValid())
            {
                view.ClaimOwnership();
                ZNetScene.instance.Destroy(planted);
                return;
            }

            Object.Destroy(planted);
        }

        /// <summary>
        ///     Records who planted it, so the settlement's work is the player's work.
        /// </summary>
        /// <remarks>
        ///     Written straight to the ZDO rather than through <c>Piece.SetCreator</c>, which
        ///     also reaches into the world's player history and wants a platform id a villager
        ///     does not have. What the creator is actually read for is whether a piece was placed
        ///     by a player at all, and a sapling a villager put in on the settlement's behalf
        ///     should answer yes.
        ///
        ///     Nobody to credit on a dedicated server, where there is no local player. Left unset
        ///     rather than invented, which is the honest answer and costs nothing this job needs.
        /// </remarks>
        private static void Credit(GameObject planted)
        {
            Player player = Player.m_localPlayer;
            if (player == null) return;
            if (!planted.TryGetComponent(out ZNetView view) || !view.IsValid()) return;

            view.GetZDO().Set(ZDOVars.s_creator, player.GetPlayerID());
        }

        /// <summary>What a refusal means, in the plant's own terms.</summary>
        /// <remarks>
        ///     Explicit, with no fallback that invents a plausible reason: an unnamed status
        ///     should read as one nobody has handled rather than as one of the others. This mod
        ///     has already had a default branch make a new thing display as an existing one.
        /// </remarks>
        private static string Explain(Plant.Status status)
        {
            switch (status)
            {
                case Plant.Status.NotCultivated: return "the ground is not broken here";
                case Plant.Status.NoSpace: return "no room for it here";
                case Plant.Status.WrongBiome: return "it will not grow in this land";
                case Plant.Status.NoSun: return "there is a roof over this";
                case Plant.Status.TooCold: return "it is too cold here";
                case Plant.Status.TooHot: return "it is too hot here";
                case Plant.Status.NoAttachPiece: return "there is nothing for it to grow on";
                default: return "it will not grow here";
            }
        }
    }
}
