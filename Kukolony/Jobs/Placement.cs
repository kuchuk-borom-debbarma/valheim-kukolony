namespace Kukolony.Jobs
{
    /// <summary>
    ///     How good a home a place is for an item, and whether moving it there is allowed.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>The shuffle loop is the failure mode of the whole hauling job.</b> Chest A says
    ///         move this to B; B says move it back; two villagers pass a stack of wood between
    ///         them forever, and every individual decision was correct. This file is the thing
    ///         that makes it impossible rather than a bug to be found later.
    ///     </para>
    ///     <para>
    ///         Every placement is scored, and a move is legal only if the destination scores
    ///         <em>strictly higher</em> than where the item is now. That one rule buys three
    ///         things at once. Items cannot oscillate, because every move raises a bounded
    ///         score. Specificity beats proximity, so wood sitting in an overflow chest migrates
    ///         to the wood chest - which is what organising means. And two chests that both name
    ///         wood score the same, so nothing moves between them and villagers do not invent
    ///         work.
    ///     </para>
    ///     <para>
    ///         Pure by design, and compiled into the Unity-free test project: this is the part
    ///         of hauling whose failure is a settlement that never settles, and that is far
    ///         cheaper to prove at a table than to watch for.
    ///     </para>
    /// </remarks>
    internal static class Placement
    {
        /// <summary>Lying on the ground. Worse than any container that will have it.</summary>
        internal const int Ground = -1;

        /// <summary>This container will not take it, or is at its cap for it.</summary>
        internal const int Refused = 0;

        /// <summary>A container that takes anything - an overflow chest, or the settlement's dump.</summary>
        internal const int Overflow = 1;

        /// <summary>A container that names this item. The best home an item has.</summary>
        internal const int Named = 2;

        /// <summary>
        ///     What a container is worth as a home for one kind of item.
        /// </summary>
        /// <param name="names">The container's own list names this item.</param>
        /// <param name="takesAnything">The list is empty, which is what an overflow chest is.</param>
        /// <param name="takesUnclaimed">Marked as the settlement's dump for items nothing claims.</param>
        /// <param name="atCap">Already holding as many of this item as it was told to.</param>
        /// <remarks>
        ///     A cap participates naturally rather than as a special case: a container at its cap
        ///     scores <see cref="Refused" /> for <em>further</em> items of that kind, so it stops
        ///     attracting them, while the items already inside it still score
        ///     <see cref="Named" /> and are left where they are. Being full is not a reason to
        ///     start moving things out.
        /// </remarks>
        internal static int Score(bool names, bool takesAnything, bool takesUnclaimed, bool atCap)
        {
            if (atCap) return Refused;
            if (names) return Named;
            return takesAnything || takesUnclaimed ? Overflow : Refused;
        }

        /// <summary>
        ///     Whether an item may be moved from one place to another.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Strictly higher, never "at least as good". Equal scores are exactly the case
        ///         that produces a settlement in permanent motion with nothing improving.
        ///     </para>
        ///     <para>
        ///         A refusal is absolute rather than merely low. Ranking the ground below a
        ///         container that will not take the item is honest - the ground really is the
        ///         worse place for it - but ordering alone then makes "off the ground into a
        ///         chest that refuses it" a legal move, because it raises the score. Refusal has
        ///         to be a floor for destinations, not a rung on the ladder.
        ///     </para>
        /// </remarks>
        internal static bool MayMove(int from, int to) => to > Refused && to > from;

        /// <summary>
        ///     How much better a move makes things, or zero if it is not allowed at all.
        /// </summary>
        /// <remarks>
        ///     What decides which mess a villager fixes first. A flint sitting in the wood chest
        ///     - somewhere that actively refuses it - is a worse mistake than wood sitting in an
        ///     overflow chest, and is worth walking further for. Zero for an illegal move rather
        ///     than a negative number, so a caller that sorts on this can never rank one above
        ///     doing nothing.
        /// </remarks>
        internal static int Improvement(int from, int to) => MayMove(from, to) ? to - from : 0;
    }
}
