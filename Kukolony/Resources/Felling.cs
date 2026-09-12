using UnityEngine;

namespace Kukolony.Resources
{
    /// <summary>What one blow achieved.</summary>
    internal enum BlowResult
    {
        /// <summary>It landed and there is more to do.</summary>
        Struck,

        /// <summary>Ownership is being taken; the blow lands on a later tick.</summary>
        Claiming,

        /// <summary>It came down. There is nothing left to hit.</summary>
        Felled,

        /// <summary>The axe cannot cut this, so hitting it again would never work.</summary>
        TooHard,

        /// <summary>Not something this knows how to hit.</summary>
        Unworkable
    }

    /// <summary>
    ///     Hitting trees, logs and undergrowth with an axe.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>An unowned target absorbs work with no effect.</b> Damage is routed to
    ///         whoever owns the object, and a tree the world generated has no owner at all, so
    ///         every peer decides the blow is somebody else's business and drops it. The
    ///         villager swings, the health does not move, and nothing anywhere reports a
    ///         problem. Ownership is claimed first and the blow waits a tick — this is the
    ///         single worst trap in the job, because its symptom is a villager working hard.
    ///     </para>
    ///     <para>
    ///         <b>A landed blow and a discarded one are otherwise identical.</b>
    ///         <c>IDestructible.Damage</c> returns <c>void</c>, and the two ways it can quietly
    ///         do nothing — a tool tier below the target's minimum, and resistances that zero
    ///         the damage — are both decided privately inside the concrete class after the RPC.
    ///         Health before and health after is the only honest check, and it is readable only
    ///         because taking ownership first makes the call synchronous.
    ///     </para>
    ///     <para>
    ///         <b>A tree does not produce wood.</b> Felling one leaves a log — a separate
    ///         object, which has to be cut up before any wood exists. Worth knowing before
    ///         wondering why a chopping colony fills no chests.
    ///     </para>
    /// </remarks>
    internal static class Felling
    {
        /// <summary>
        ///     Lands one blow, taking ownership first if the target is not already ours.
        /// </summary>
        internal static BlowResult Strike(GameObject target, ItemDrop.ItemData axe, Vector3 from, out string what)
        {
            what = string.Empty;
            if (target == null || !target.TryGetComponent(out ZNetView view) || !view.IsValid())
            {
                return Unworkable(out what);
            }

            ChopKind kind = Choppable.Of(view.GetZDO().GetPrefab());
            if (kind == ChopKind.None) return Unworkable(out what);

            if (!view.IsOwner())
            {
                view.ClaimOwnership();
                what = kind == ChopKind.Log ? "taking hold of a log" : "taking hold of a tree";
                return BlowResult.Claiming;
            }

            int tier = axe?.m_shared != null ? axe.m_shared.m_toolTier : 0;
            HitData hit = Blow(axe, target.transform.position, from);

            // Each of the three reads the same ZDO float, which is why chopping-only is a
            // coherent job. Mining would not share this: a MineRock keeps a float per hit
            // area under a runtime-hashed key, and a MineRock5 keeps a base64 package of them.
            if (kind == ChopKind.Tree)
            {
                if (!target.TryGetComponent(out TreeBase tree)) return Unworkable(out what);
                if (tree.m_minToolTier > tier) return Blunt(out what);

                float before = Health(view, tree.m_health);
                tree.Damage(hit);
                return Settle(view, before, "chopping", out what);
            }

            if (kind == ChopKind.Log)
            {
                if (!target.TryGetComponent(out TreeLog log)) return Unworkable(out what);
                if (log.m_minToolTier > tier) return Blunt(out what);

                float before = Health(view, log.m_health);
                log.Damage(hit);
                return Settle(view, before, "cutting up a log", out what);
            }

            if (!target.TryGetComponent(out Destructible destructible)) return Unworkable(out what);
            if (destructible.m_minToolTier > tier) return Blunt(out what);

            float had = Health(view, destructible.m_health);
            destructible.Damage(hit);
            return Settle(view, had, "clearing", out what);
        }

        /// <summary>
        ///     Reads the health back and says what the blow actually did.
        /// </summary>
        /// <remarks>
        ///     A destroyed object's view stops being valid, which is how the last blow is told
        ///     from the ones before it. The default matters as much as the read: an undamaged
        ///     object has never written the field, so defaulting to zero would make a felled
        ///     tree and an untouched one report exactly the same number.
        /// </remarks>
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
                // Accepted and changed nothing, which means this axe cannot cut this thing.
                // Saying so beats swinging at it for the rest of the session.
                what = "the axe does not bite";
                return BlowResult.TooHard;
            }

            what = doing;
            return BlowResult.Struck;
        }

        /// <summary>
        ///     The blow, taken from the axe rather than invented.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>The direction is also the fall direction.</b> A felled trunk is pushed
        ///         along <c>m_dir</c>, so striking from where the villager stands sends the log
        ///         away from it. That matters: a falling log damages any character it hits and
        ///         only checks <c>IsPlayer()</c> before deciding whether to care, so there is
        ///         no opt-out for a villager standing in the wrong place.
        ///     </para>
        ///     <para>
        ///         <b>No push force.</b> A log applies double whatever it is given, which
        ///         launches trunks down slopes and into buildings.
        ///     </para>
        ///     <para>
        ///         <b>No attacker.</b> Naming the villager credits it with the kill, trips
        ///         ward alarms on any protected ground, and on a world with the difficulty
        ///         modifiers raised scales the damage the game thinks it is dealing. Leaving it
        ///         unset costs only stat attribution, which nothing here reads.
        ///     </para>
        /// </remarks>
        private static HitData Blow(ItemDrop.ItemData axe, Vector3 at, Vector3 from)
        {
            HitData hit = new HitData();
            if (axe != null)
            {
                hit.m_damage = axe.GetDamage();
                // The hit's tier is a short while the item's is an int. They are the same
                // small numbers, and the cast is what the game does to itself.
                hit.m_toolTier = (short)(axe.m_shared != null ? axe.m_shared.m_toolTier : 0);
            }

            Vector3 away = at - from;
            away.y = 0f;
            hit.m_dir = away.sqrMagnitude > .001f ? away.normalized : Vector3.forward;
            hit.m_point = at;
            hit.m_pushForce = 0f;
            return hit;
        }

        private static float Health(ZNetView view, float whenUntouched) =>
            view.GetZDO().GetFloat(ZDOVars.s_health, whenUntouched);

        private static BlowResult Blunt(out string what)
        {
            what = "needs a better axe";
            return BlowResult.TooHard;
        }

        private static BlowResult Unworkable(out string what)
        {
            what = "nothing to chop here";
            return BlowResult.Unworkable;
        }
    }
}
