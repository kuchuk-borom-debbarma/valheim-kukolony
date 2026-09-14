using UnityEngine;

namespace Kukolony.Resources.Mining
{
    /// <summary>
    ///     The only definition of "this can be mined, and here is how" in this mod.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         One predicate, asked by the job and by the classifier alike, for the reason
    ///         <c>StationProbe</c> gives: two tests drift apart, and the symptom is a rock a
    ///         villager walks to and cannot work. See docs/components.md.
    ///     </para>
    ///     <para>
    ///         <b>Order matters, because the components overlap.</b> A <c>MineRock5</c> deposit
    ///         may carry a <c>Destructible</c> as well, and answering with the second would give
    ///         a villager one health float to watch for a rock that keeps forty of them - it
    ///         would report the whole vein finished after the first part fell. The specific
    ///         component speaks first, as it does for stations.
    ///     </para>
    /// </remarks>
    internal static class MineProbe
    {
        /// <summary>Whether this object can be mined, and how to work it.</summary>
        internal static bool TryFind(GameObject candidate, out MineProtocol protocol)
        {
            protocol = null;
            if (candidate == null) return false;

            // A creature and a loose item are never scenery, whatever they carry.
            if (candidate.GetComponent<Character>() != null) return false;
            if (candidate.GetComponent<ItemDrop>() != null) return false;

            if (!candidate.TryGetComponent(out ZNetView view) || !view.IsValid()) return false;

            if (candidate.TryGetComponent(out MineRock5 parts))
            {
                protocol = new DepositOfParts(view, parts);
                return true;
            }

            if (candidate.TryGetComponent(out MineRock rock))
            {
                protocol = new DepositOfRock(view, rock);
                return true;
            }

            if (candidate.TryGetComponent(out Destructible loose) &&
                loose.m_damages.m_pickaxe != HitData.DamageModifier.Immune &&
                loose.m_damages.m_pickaxe != HitData.DamageModifier.Ignore)
            {
                protocol = new LooseRock(view, loose);
                return true;
            }

            return false;
        }
    }
}
