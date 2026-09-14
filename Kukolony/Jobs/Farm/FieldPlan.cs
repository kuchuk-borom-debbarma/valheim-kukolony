using System.Collections.Generic;

namespace Kukolony.Jobs.Farm
{
    /// <summary>One square of a field, in metres from its centre.</summary>
    /// <remarks>
    ///     Flat, and deliberately: a field is a circle drawn on the ground, and height comes from
    ///     the terrain at the moment something is placed. Carrying a y here would be carrying a
    ///     number that is wrong as soon as anybody levels the ground.
    /// </remarks>
    internal readonly struct Furrow
    {
        internal Furrow(int index, float x, float z)
        {
            Index = index;
            X = x;
            Z = z;
        }

        /// <summary>
        ///     Which square this is, counted along the grid.
        /// </summary>
        /// <remarks>
        ///     Stable for a given centre, radius and pitch, which is what lets a villager claim
        ///     one: two villagers working one field must be able to name the same square the same
        ///     way, and a position compared with a tolerance is not a name.
        /// </remarks>
        internal int Index { get; }

        internal float X { get; }

        internal float Z { get; }
    }

    /// <summary>
    ///     Where the next thing goes in a field.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>A grid, because a field should look like a field.</b> Scattering at random
    ///         would plant just as much and read as a weed patch - and worse, it would make
    ///         "is there room for another" unanswerable without trying every point.
    ///     </para>
    ///     <para>
    ///         <b>The pitch comes from the crop.</b> Measured across what this game ships, the
    ///         room a plant wants runs from half a metre for every crop to three metres for an
    ///         oak - six to one. One pitch for everything would either pack saplings so tightly
    ///         that none of them grow, or spread carrots at a sixth of the density the ground
    ///         could hold.
    ///     </para>
    ///     <para>
    ///         <b>Pure, and checked without a world.</b> Everything here is arithmetic over a
    ///         circle; whether a square is actually free is a question for the game, asked once
    ///         of the square this chooses rather than of every square in the field.
    ///     </para>
    ///     <para>
    ///         <b>Row-major from one corner, never from the villager.</b> Nearest-first would
    ///         sound kinder and is wrong here: two villagers in a field would both walk to the
    ///         square nearest themselves, and a field sown from the middle outwards leaves an
    ///         edge that is never quite reached. A fixed order means the field fills the same way
    ///         every time, and it is the reason a square index means the same thing to everybody.
    ///     </para>
    /// </remarks>
    internal static class FieldPlan
    {
        /// <summary>
        ///     The most squares a field is allowed to be divided into.
        /// </summary>
        /// <remarks>
        ///     A thirty-two metre field at a crop's half-metre pitch is over sixteen thousand
        ///     squares, and every one of them is a candidate something has to walk past. The
        ///     radius is already clamped; this is the second bound, on the product rather than on
        ///     either number, because it is the product that costs.
        /// </remarks>
        internal const int MostSquares = 4096;

        /// <summary>
        ///     Every square of a field, in order.
        /// </summary>
        /// <remarks>
        ///     Written into a caller's list rather than returned, so a per-tick path can keep one
        ///     list rather than allocating a few thousand structs a second.
        /// </remarks>
        internal static void Squares(float radius, float pitch, List<Furrow> into)
        {
            if (into == null) return;

            into.Clear();
            if (radius <= 0f || pitch <= 0f) return;

            // How many squares fit either side of the centre. The centre itself is a square, so
            // a field always has at least one however tight the radius.
            int reach = (int)(radius / pitch);

            // Bounded before anything is built rather than while building it: a pitch of a
            // thousandth from a corrupt record would otherwise ask for a billion squares and the
            // list would be the thing that noticed.
            long wide = 2L * reach + 1L;
            if (wide * wide > MostSquares) return;

            int index = 0;
            for (int row = -reach; row <= reach; row++)
            {
                for (int column = -reach; column <= reach; column++)
                {
                    float x = column * pitch;
                    float z = row * pitch;

                    // Round, not square. The field's own edge is a radius, and the corners of the
                    // bounding box lie outside it - a plant there is outside the thing the player
                    // drew.
                    if (x * x + z * z > radius * radius) continue;

                    into.Add(new Furrow(index, x, z));
                    index++;
                }
            }
        }

        /// <summary>
        ///     The first square nothing is standing in, or none when the field is full.
        /// </summary>
        /// <param name="taken">Whether a square already holds something, by its index.</param>
        /// <param name="from">
        ///     Where to start looking, wrapping round the end.
        /// </param>
        /// <remarks>
        ///     <b>The start is how two villagers keep off each other's ground.</b> Scanning from
        ///     the same corner every time would send everybody in a field to the same square, and
        ///     the first one there would win while the rest walked for nothing. Starting each of
        ///     them somewhere else costs nothing, needs no claim to go stale, and still fills the
        ///     whole field because the scan wraps.
        /// </remarks>
        internal static bool Next(IReadOnlyList<Furrow> squares, System.Func<int, bool> taken,
            out Furrow furrow, int from = 0)
        {
            furrow = default;
            if (squares == null || taken == null || squares.Count == 0) return false;

            // Wrapped rather than clamped. A start past the end is an ordinary thing for a
            // caller to produce - it comes from an id divided by a count that changes when the
            // field is resized - and refusing it would leave that villager unable to work.
            int begin = ((from % squares.Count) + squares.Count) % squares.Count;

            for (int step = 0; step < squares.Count; step++)
            {
                int i = (begin + step) % squares.Count;
                if (taken(squares[i].Index)) continue;

                furrow = squares[i];
                return true;
            }

            return false;
        }

        /// <summary>How many squares a field of this shape has at all.</summary>
        /// <remarks>
        ///     So a screen can say "room for about 400" without building the list, and so a check
        ///     can assert that a tighter pitch really does fit more.
        /// </remarks>
        internal static int Capacity(float radius, float pitch)
        {
            if (radius <= 0f || pitch <= 0f) return 0;

            int reach = (int)(radius / pitch);
            long wide = 2L * reach + 1L;
            if (wide * wide > MostSquares) return 0;

            int count = 0;
            for (int row = -reach; row <= reach; row++)
            {
                for (int column = -reach; column <= reach; column++)
                {
                    float x = column * pitch;
                    float z = row * pitch;
                    if (x * x + z * z <= radius * radius) count++;
                }
            }

            return count;
        }
    }
}
