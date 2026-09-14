using System.Collections.Generic;
using UnityEngine;

namespace Kukolony.Resources.Mining
{
    /// <summary>One part of a deposit that can still be struck.</summary>
    /// <remarks>
    ///     Carries a position as well as the collider, because everything that consumes this -
    ///     which part is nearest, where to stand, whether anything is left - is geometry. The
    ///     part that decides those questions takes the positions alone and is checked without a
    ///     world; only striking needs the collider.
    /// </remarks>
    internal readonly struct MineArea
    {
        internal MineArea(int index, Vector3 at, Collider part = null)
        {
            Index = index;
            At = at;
            Part = part;
        }

        /// <summary>Which part this is, as the component numbers them.</summary>
        internal int Index { get; }

        /// <summary>Where to aim, and roughly where to stand.</summary>
        internal Vector3 At { get; }

        /// <summary>
        ///     The collider to name in the hit, where the component wants one.
        /// </summary>
        /// <remarks>
        ///     Naming it is exact. The alternative the newer component offers - a point and a
        ///     radius - resolves the part by overlapping a sphere, which finds nothing at all if
        ///     the aim point falls outside every collider and then does nothing while reporting
        ///     nothing. Null for things that have no parts, which take the plain path.
        /// </remarks>
        internal Collider Part { get; }
    }

    /// <summary>
    ///     How to work one minable thing.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Chopping needs no such abstraction and mining does.</b> A tree, a log and a
    ///         stump each keep one health float under the same ZDO key, which is why
    ///         <see cref="Felling" /> can hit all three through one function and read the result
    ///         the same way. The three minable things keep their health in three different
    ///         shapes: a <c>MineRock5</c> writes every part's health as a base64 ZPackage under
    ///         <c>s_health</c>, a <c>MineRock</c> writes a float per part under
    ///         <c>"Health&lt;index&gt;"</c>, and a <c>Destructible</c> writes one float under
    ///         <c>s_health</c>. <c>Felling</c> says so in a comment; this is the consequence.
    ///     </para>
    ///     <para>
    ///         <b>Reading the health back is the only way to know a blow landed.</b>
    ///         <c>IDestructible.Damage</c> returns void, and refuses silently when the tool is
    ///         too weak - it shows the player a "too hard" message and returns. So each
    ///         implementation measures before and after, the same doctrine tending uses to prove
    ///         a feed landed.
    ///     </para>
    /// </remarks>
    internal abstract class MineProtocol
    {
        protected MineProtocol(ZNetView view)
        {
            View = view;
        }

        internal ZNetView View { get; }

        /// <summary>The tool tier this demands. A weaker pickaxe does nothing at all.</summary>
        internal abstract int MinToolTier { get; }

        internal bool IsValid => View != null && View.IsValid();

        /// <summary>
        ///     The parts still standing.
        /// </summary>
        /// <remarks>
        ///     Read from the world every time rather than remembered. Mining collapses: a
        ///     <c>MineRock5</c> with support checking kills parts that lose their footing, using
        ///     a synthetic tool-tier-100 structural hit - so parts vanish that nobody struck, and
        ///     several can go at once. Anything that counted its own blows would be wrong within
        ///     seconds of the first one landing.
        /// </remarks>
        internal abstract void Areas(List<MineArea> into);

        /// <summary>Strikes one part, and says what actually happened.</summary>
        internal abstract BlowResult Strike(MineArea area, HitData hit, out string what);

        /// <summary>
        ///     How much of this is left, according to its record.
        /// </summary>
        /// <remarks>
        ///     Read from the ZDO rather than from the object, which is the whole point: what is
        ///     left of a deposit lives on the record, so it is the same answer whether or not
        ///     anybody is looking at the rock. Each component stores it differently - a base64
        ///     package of every part, a float per part, one float - which is why this is asked
        ///     of the protocol and not of a field.
        /// </remarks>
        internal abstract float Remaining();

        /// <summary>Whether anything is left to hit.</summary>
        internal bool Spent()
        {
            List<MineArea> areas = new List<MineArea>();
            Areas(areas);
            return areas.Count == 0;
        }

        /// <summary>
        ///     Takes ownership if we do not have it, because damage goes to the owner.
        /// </summary>
        /// <remarks>
        ///     The same rule felling found: an unowned target absorbs work with no effect, and a
        ///     rock the world generated has no owner at all. Reported rather than waited on, so
        ///     the villager keeps its claim and tries again on the next tick.
        /// </remarks>
        protected bool Mine(out BlowResult result, out string what)
        {
            result = BlowResult.Claiming;
            what = "taking hold of the rock";

            if (View.IsOwner()) return true;

            View.ClaimOwnership();
            return false;
        }
    }
}
