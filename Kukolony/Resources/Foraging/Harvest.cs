using UnityEngine;

namespace Kukolony.Resources.Foraging
{
    /// <summary>What came of reaching for something.</summary>
    internal enum PickResult
    {
        /// <summary>It is gone, or it was never a pickable thing.</summary>
        Unworkable,

        /// <summary>Ownership is being taken. The reach lands on a later tick.</summary>
        Claiming,

        /// <summary>Nothing on it. Already picked, or switched off by whatever gates it.</summary>
        Bare,

        /// <summary>Reached for, and still there afterwards. Something absorbed it.</summary>
        NoEffect,

        /// <summary>Taken, and whatever it held is on the ground.</summary>
        Picked
    }

    /// <summary>
    ///     Taking what a <c>Pickable</c> holds.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>No protocol and no implementations.</b> Mining needed three because health
    ///         lives in three shapes; foraging needs none, because one component answers the
    ///         whole question. <c>CanBePicked()</c> is public and says exactly whether there is
    ///         anything there - the visible part is active, it has not been picked, and whatever
    ///         gates it is on - which is the single predicate the component doctrine asks for and
    ///         rarely gets.
    ///     </para>
    ///     <para>
    ///         <b>The RPC, not <c>Interact</c>.</b> <c>Pickable.Interact</c> is written for the
    ///         person at the keyboard: it credits a foraging skill, increments a player profile
    ///         statistic, and rolls a level bonus, all against <c>Player.m_localPlayer</c> -
    ///         which is a villager stealing the player's skill-ups on a good day and a null
    ///         reference on a dedicated server. What it does at the end is send
    ///         <c>RPC_Pick</c> to the owner, and that is all a villager wants: the drop, the
    ///         effect, and the picked flag broadcast to everybody.
    ///     </para>
    ///     <para>
    ///         <b>So ownership is taken first.</b> <c>RPC_Pick</c> begins by refusing if it is
    ///         not the owner, and an unowned bush - which is every bush the world generated - has
    ///         no owner to route to. This is the same rule felling found and mining wrote down:
    ///         work sent to nobody is absorbed in silence.
    ///     </para>
    ///     <para>
    ///         <b>And the result is read back from the world.</b> An RPC returns nothing, so the
    ///         only honest way to know a reach landed is to ask the thing afterwards - the same
    ///         doctrine tending uses to prove a feed landed and mining uses to prove a blow did.
    ///     </para>
    /// </remarks>
    internal static class Harvest
    {
        /// <summary>The RPC name <c>Pickable</c> registers, spelled as the component spells it.</summary>
        private const string PickRpc = "RPC_Pick";

        /// <summary>The pickable on an object, or null when it is not one.</summary>
        /// <remarks>
        ///     <c>GetComponentInChildren</c>, because a berry bush keeps its component on the
        ///     root but nothing promises a modded one does - and the same lookup already finds
        ///     containers on pieces that nest them.
        /// </remarks>
        internal static bool TryFind(GameObject instance, out Pickable pickable)
        {
            pickable = instance != null ? instance.GetComponentInChildren<Pickable>(true) : null;
            return pickable != null;
        }

        /// <summary>
        ///     The network view behind a pickable.
        /// </summary>
        /// <remarks>
        ///     Looked up rather than read off <c>Pickable.m_nview</c>, which is <b>private</b> in
        ///     the shipped assembly. The publicized copy this mod compiles against would take it
        ///     happily and the game would be the one to find out, which is the worst place for
        ///     that to happen - nothing else in this mod reaches for a private Valheim member,
        ///     and this is not the feature to start with.
        ///
        ///     Exact, not approximate: <c>Pickable.Awake</c> fills that field from
        ///     <c>GetComponent&lt;ZNetView&gt;()</c>, so the two are the same object by
        ///     construction.
        /// </remarks>
        internal static ZNetView ViewOf(Pickable pickable) =>
            pickable != null && pickable.TryGetComponent(out ZNetView view) ? view : null;

        /// <summary>Whether there is anything on it right now.</summary>
        internal static bool Ripe(Pickable pickable)
        {
            ZNetView view = ViewOf(pickable);
            return view != null && view.IsValid() && pickable.CanBePicked();
        }

        /// <summary>
        ///     Whether a record says there is something to pick, without loading anything.
        /// </summary>
        /// <remarks>
        ///     The cheap half of <see cref="Ripe" />, asked of every candidate while a villager
        ///     chooses. A picked bush stays in the world and stays in the sweep - a forager
        ///     strips a clearing and then every candidate in it is spent - so without an answer
        ///     that costs nothing the job would either pay a scene lookup per bush per tick or
        ///     walk to empty bushes all afternoon.
        ///
        ///     The default matters and is the prefab's own: <c>Pickable.Awake</c> reads this key
        ///     with <c>m_defaultPicked</c> behind it, so treating an unwritten record as "not
        ///     picked" would offer work on things that start empty.
        /// </remarks>
        internal static bool RecordSaysRipe(ZDO zdo)
        {
            if (zdo == null || !zdo.IsValid()) return false;

            return !zdo.GetBool(ZDOVars.s_picked, Forageable.StartsPicked(zdo.GetPrefab()));
        }

        /// <summary>Reaches for it, and says what actually happened.</summary>
        internal static PickResult Take(Pickable pickable, out string what)
        {
            what = string.Empty;

            ZNetView view = ViewOf(pickable);
            if (view == null || !view.IsValid())
            {
                what = "it is gone";
                return PickResult.Unworkable;
            }

            if (!pickable.CanBePicked())
            {
                what = "there is nothing on it";
                return PickResult.Bare;
            }

            // Reported rather than waited on, so the villager keeps its claim and reaches again
            // on the next tick.
            if (!view.IsOwner())
            {
                view.ClaimOwnership();
                what = "reaching for it";
                return PickResult.Claiming;
            }

            // With the bonus at zero, and the argument is not optional. The handler is
            // registered as taking one int - the extra yield a player's foraging skill rolls -
            // so an invocation with no argument does not match it and does nothing at all,
            // silently, which is the exact shape of failure this mod keeps finding. Zero is
            // also the honest number: the bonus comes from a skill, and a villager has none.
            view.InvokeRPC(PickRpc, 0);

            // Asked afterwards, because an RPC says nothing. Owning it is what makes the answer
            // meaningful this soon: an owner invoking its own RPC runs it here rather than
            // sending it away and hearing back later.
            if (!view.IsValid() || !pickable.CanBePicked())
            {
                what = "picked";
                return PickResult.Picked;
            }

            // Reached, and still there. Not called a failure on the strength of one tick: the
            // picked flag travels back as a routed call to everybody including us, and a reach
            // that overlaps that is indistinguishable here from one that was refused. The
            // caller counts these and gives up on the bush after a few, which is the same
            // tolerance mining gives a blow that moves nothing.
            what = "reaching for it";
            return PickResult.NoEffect;
        }
    }
}
