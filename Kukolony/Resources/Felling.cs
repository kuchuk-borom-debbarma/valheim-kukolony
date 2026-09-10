using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Resources
{
    /// <summary>What one blow achieved.</summary>
    internal enum BlowResult
    {
        /// <summary>Still working on it.</summary>
        Struck,
        /// <summary>Ownership is being taken; the blow lands on a later tick.</summary>
        Claiming,
        /// <summary>It came down. There is nothing left to hit.</summary>
        Felled,
        /// <summary>The tool cannot cut this at all, so hitting it again would never work.</summary>
        TooHard,
        /// <summary>The target is not something this knows how to hit.</summary>
        Unworkable
    }

    /// <summary>
    ///     Hitting trees and logs with an axe.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>An unowned target absorbs work with no effect.</b> Damage is routed to whoever
    ///         owns the object, and a tree the world generated has no owner at all, so every
    ///         peer decides the blow is somebody else's business and drops it. The villager
    ///         swings, the health does not move, and nothing anywhere reports a problem. So
    ///         ownership is claimed first and the blow waits a tick.
    ///     </para>
    ///     <para>
    ///         <b>A tree does not produce wood.</b> Felling it leaves a log - a separate object,
    ///         which then has to be cut up in a second pass before any wood exists. This is
    ///         worth knowing before wondering why a chopping job fills no chests.
    ///     </para>
    ///     <para>
    ///         The attacker is deliberately left unset. Naming the villager would credit it with
    ///         the kill, which on a world with the difficulty modifiers raised also scales the
    ///         damage the game thinks it is dealing. Leaving it unset costs only the stat
    ///         attribution, which nothing here reads.
    ///     </para>
    /// </remarks>
    internal static class Felling
    {
        /// <summary>
        ///     Lands one blow, taking ownership first if it is not already ours. Reports what
        ///     happened rather than assuming, because a blow that does nothing looks exactly
        ///     like one that worked.
        /// </summary>
        internal static BlowResult Strike(GameObject target, ItemDrop.ItemData axe, out string what)
        {
            what = string.Empty;
            if (target == null || !target.TryGetComponent(out ZNetView view) || !view.IsValid())
                return Unworkable(out what);

            ResourceKind kind = ResourceIndex.Of(view.GetZDO().GetPrefab());
            if (kind == ResourceKind.None) return Unworkable(out what);

            if (!view.IsOwner())
            {
                view.ClaimOwnership();
                what = "claiming " + Name(kind);
                return BlowResult.Claiming;
            }

            HitData hit = Blow(axe);
            int tier = axe != null && axe.m_shared != null ? axe.m_shared.m_toolTier : 0;

            if (kind == ResourceKind.Tree)
            {
                if (!target.TryGetComponent(out TreeBase tree)) return Unworkable(out what);
                if (tree.m_minToolTier > tier)
                {
                    what = "needs a better axe";
                    return BlowResult.TooHard;
                }
                // Health before and after is the only honest way to tell a landed blow from
                // one the game quietly discarded, and it is readable because the call is
                // synchronous once the object is ours.
                float before = tree.m_nview != null ? tree.m_nview.GetZDO().GetFloat(ZDOVars.s_health, tree.m_health) : 0f;
                tree.Damage(hit);
                return Settle(tree.m_nview, before, "chopping", out what);
            }

            if (!target.TryGetComponent(out TreeLog log)) return Unworkable(out what);
            if (log.m_minToolTier > tier)
            {
                what = "needs a better axe";
                return BlowResult.TooHard;
            }
            float logBefore = log.m_nview != null ? log.m_nview.GetZDO().GetFloat(ZDOVars.s_health, log.m_health) : 0f;
            log.Damage(hit);
            return Settle(log.m_nview, logBefore, "cutting up a log", out what);
        }

        /// <summary>
        ///     Reads the health back. A destroyed object's view stops being valid, which is how
        ///     the last blow is told from the ones before it.
        /// </summary>
        private static BlowResult Settle(ZNetView view, float before, string doing, out string what)
        {
            if (view == null || !view.IsValid() || view.GetZDO() == null)
            {
                what = "felled it";
                return BlowResult.Felled;
            }
            float after = view.GetZDO().GetFloat(ZDOVars.s_health, before);
            if (after <= 0f)
            {
                what = "felled it";
                return BlowResult.Felled;
            }
            if (after >= before)
            {
                // The blow was accepted and changed nothing, which means the tool cannot cut
                // this. Saying so beats swinging forever.
                what = "the axe does not bite";
                return BlowResult.TooHard;
            }
            what = doing;
            return BlowResult.Struck;
        }

        /// <summary>
        ///     The blow itself, taken from the axe rather than invented. Chop damage is what
        ///     trees read; everything else on the tool is irrelevant to them.
        /// </summary>
        private static HitData Blow(ItemDrop.ItemData axe)
        {
            HitData hit = new HitData();
            if (axe != null)
            {
                hit.m_damage = axe.GetDamage();
                // The hit's tier is a short while the item's is an int; they are the same
                // small numbers, and the cast is what the game does to itself.
                hit.m_toolTier = (short)(axe.m_shared != null ? axe.m_shared.m_toolTier : 0);
            }
            return hit;
        }

        private static string Name(ResourceKind kind) => kind == ResourceKind.Log ? "a log" : "a tree";

        private static BlowResult Unworkable(out string what)
        {
            what = "nothing to chop here";
            return BlowResult.Unworkable;
        }
    }
}
