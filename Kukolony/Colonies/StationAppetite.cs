using System;

namespace Kukolony.Colonies
{
    /// <summary>
    ///     How much a station is short of, as arithmetic.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>This is where the job's whole honesty problem lives.</b> Tending's hazard is not
    ///         a villager that fails - it is one that succeeds at work nobody needed: every call
    ///         lands, the station reports a change, and the material is gone. A settlement looks
    ///         busy right up until the coal runs out.
    ///     </para>
    ///     <para>
    ///         So the two rules that prevent it are written as numbers rather than as intentions,
    ///         with no Unity in sight, and checked in the deterministic project in about a second.
    ///         "Never stoke an idle station" is one line here - <c>queued &lt;= 0</c> answers
    ///         zero - and a check for it cannot pass vacuously.
    ///     </para>
    /// </remarks>
    internal static class StationAppetite
    {
        /// <summary>
        ///     How much material the player asked this station to be kept holding.
        /// </summary>
        /// <remarks>
        ///     <b>This must round the way the screen rounds.</b> The structure screen already
        ///     shows "50% (5)" using <c>Mathf.RoundToInt(fraction * max)</c>, and that number is
        ///     the promise a player reads. <c>Mathf.RoundToInt</c> is <c>(int)Math.Round(f)</c>,
        ///     which rounds halves to even - so this uses the same call rather than a cast or an
        ///     add-a-half, both of which disagree at exactly the fractions a slider lands on.
        /// </remarks>
        internal static int TargetQueue(int maxOre, float keepFull)
        {
            if (maxOre <= 0) return 0;

            float wanted = keepFull < 0f ? 0f : keepFull > 1f ? 1f : keepFull;
            int target = (int)Math.Round(wanted * maxOre);
            return target < 0 ? 0 : target > maxOre ? maxOre : target;
        }

        /// <summary>
        ///     How much material to put in, which is the terminus this job otherwise lacks.
        /// </summary>
        /// <remarks>
        ///     At or above the line there is simply no work, which is what makes "keep it half
        ///     full" a stopping rule rather than a rate. Bounded by the station's own capacity as
        ///     well as by the target, because a record can outlive the asset data it was made
        ///     from and a target above capacity would ask for room that does not exist.
        /// </remarks>
        internal static int InputWanted(int queued, int maxOre, float keepFull)
        {
            if (maxOre <= 0) return 0;

            int room = maxOre - queued;
            int shortfall = TargetQueue(maxOre, keepFull) - queued;
            int wanted = shortfall < room ? shortfall : room;
            return wanted < 0 ? 0 : wanted;
        }

        /// <summary>
        ///     How much fuel to add, which is the rule that stops a settlement burning its stores
        ///     for nothing.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>Only fuel what is queued.</b> A smelter with nothing to smelt burns nothing
        ///         - and still accepts fuel, and still reports a change. Without this line a
        ///         villager keeps an idle furnace stoked for ever while every individual decision
        ///         it makes is correct, which is the failure this job was specified around.
        ///     </para>
        ///     <para>
        ///         Flooring the shortfall is deliberate: a station half a log short of its ceiling
        ///         is left alone rather than overshot. And the multiply is done in 64 bits,
        ///         because a queue size read from a ZDO written by something else is not a number
        ///         this code chose - a wrapped product would come back negative and read as
        ///         "wants nothing", which is the safe direction only by luck.
        ///     </para>
        /// </remarks>
        internal static int FuelWanted(int queued, int fuelPerProduct, int maxFuel, float fuel)
        {
            // A charcoal kiln burns nothing at all, so "what fuel does this want" has to be
            // allowed to answer "none" rather than being assumed to be a positive number.
            if (maxFuel <= 0) return 0;

            // Nothing to burn for. The one line this class exists for.
            if (queued <= 0) return 0;

            long perProduct = fuelPerProduct > 0 ? fuelPerProduct : maxFuel;
            long needed = (long)queued * perProduct;
            long ceiling = needed < maxFuel ? needed : maxFuel;

            double shortfall = Math.Floor(ceiling - fuel);
            return shortfall <= 0d ? 0 : (int)shortfall;
        }
    }
}
