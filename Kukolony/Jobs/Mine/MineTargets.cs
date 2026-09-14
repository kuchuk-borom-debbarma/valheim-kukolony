using System.Collections.Generic;

namespace Kukolony.Jobs.Mine
{
    /// <summary>Somewhere on a deposit that can still be struck, as plain numbers.</summary>
    /// <remarks>
    ///     Deliberately not a Unity position. This half of the job is checked without a game, so
    ///     it deals in the two coordinates that decide the answer and leaves the engine to
    ///     translate. Height is left out for the same reason every other distance in this mod is
    ///     measured flat: a villager standing at the foot of a vein is at it, whatever the top
    ///     of the rock is doing.
    /// </remarks>
    internal readonly struct Spot
    {
        internal Spot(int index, float x, float z)
        {
            Index = index;
            X = x;
            Z = z;
        }

        /// <summary>Which part of the deposit this is, as its component numbers them.</summary>
        internal int Index { get; }

        internal float X { get; }

        internal float Z { get; }
    }

    /// <summary>
    ///     Which part of a deposit to work next.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The one idea mining has that chopping does not. A tree is a target; a silver vein
    ///         is forty targets wearing one name, and the villager has to keep choosing among
    ///         them as they fall.
    ///     </para>
    ///     <para>
    ///         <b>Chosen afresh from what exists, never remembered.</b> Mining collapses -
    ///         unsupported parts die on their own, several at a time - so a remembered index is
    ///         wrong within seconds and points at a part that is not there. Passing in the parts
    ///         that exist right now makes that impossible to get wrong rather than merely
    ///         discouraged.
    ///     </para>
    /// </remarks>
    internal static class MineTargets
    {
        /// <summary>
        ///     The nearest part still standing, or false when the deposit is finished.
        /// </summary>
        /// <remarks>
        ///     Nearest, because a villager already standing at a vein should work the rock in
        ///     front of it rather than walk round to the far side - and because as each part
        ///     falls the next nearest is usually the one beside it, which is what makes mining a
        ///     vein look like mining rather than like pacing.
        /// </remarks>
        internal static bool Nearest(List<Spot> spots, float x, float z, out Spot chosen)
        {
            chosen = default;
            if (spots == null || spots.Count == 0) return false;

            float closest = float.MaxValue;
            bool found = false;

            foreach (Spot spot in spots)
            {
                float dx = spot.X - x;
                float dz = spot.Z - z;
                float distance = dx * dx + dz * dz;

                // Strictly nearer, so the first of two at equal distance wins and the answer is
                // stable while the villager stands still. A tie broken differently each tick is
                // a villager that shuffles between two rocks.
                if (distance >= closest) continue;

                closest = distance;
                chosen = spot;
                found = true;
            }

            return found;
        }

        /// <summary>Whether a deposit has anything left to hit.</summary>
        internal static bool Finished(List<Spot> spots) => spots == null || spots.Count == 0;
    }
}
