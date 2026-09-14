using System.Collections.Generic;
using UnityEngine;

namespace Kukolony.Resources.Mining
{
    /// <summary>
    ///     A plain <c>Destructible</c> a pickaxe can break: loose rock, and a great deal else.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         One part, one health float under <c>s_health</c> - which is to say, exactly what
    ///         felling already deals with, reached through the mining protocol so the job has one
    ///         way of working rather than a branch for the simple case.
    ///     </para>
    ///     <para>
    ///         <b>This is the ambiguous kind.</b> The game cannot tell a boulder from a crate:
    ///         <c>DestructibleType</c> offers None, Default, Tree and Character, and stone is
    ///         none of them. So everything reached through here is opt-in, and the setting that
    ///         opts in is the only thing standing between a settlement and its own scenery.
    ///     </para>
    /// </remarks>
    internal sealed class LooseRock : MineProtocol
    {
        private readonly Destructible _rock;

        internal LooseRock(ZNetView view, Destructible rock) : base(view)
        {
            _rock = rock;
        }

        internal override int MinToolTier => _rock != null ? _rock.m_minToolTier : 0;

        internal override void Areas(List<MineArea> into)
        {
            if (into == null || _rock == null || !IsValid) return;
            if (Health() <= 0f) return;

            // One part, and no collider: this component's Damage takes the hit as it comes and
            // never looks for an area.
            into.Add(new MineArea(0, _rock.transform.position));
        }

        internal override BlowResult Strike(MineArea area, HitData hit, out string what)
        {
            what = string.Empty;
            if (_rock == null || !IsValid)
            {
                what = "it is gone";
                return BlowResult.Unworkable;
            }

            if (hit.m_toolTier < MinToolTier)
            {
                what = "needs a better pickaxe";
                return BlowResult.TooHard;
            }

            if (!Mine(out BlowResult claiming, out what)) return claiming;

            float before = Health();
            hit.m_point = area.At;
            _rock.Damage(hit);

            if (!IsValid || _rock == null)
            {
                what = "that one is out";
                return BlowResult.Felled;
            }

            float after = Health();
            if (after <= 0f)
            {
                what = "that one is out";
                return BlowResult.Felled;
            }

            if (after < before)
            {
                what = "breaking rock";
                return BlowResult.Struck;
            }

            what = "the pickaxe does not bite";
            return BlowResult.TooHard;
        }

        internal override float Remaining()
        {
            float health = Health();
            return health == float.MaxValue ? (_rock != null ? _rock.m_health : 0f)
                : Mathf.Max(0f, health);
        }

        /// <summary>
        ///     What is left of it, or the maximum when nothing has been written yet.
        /// </summary>
        /// <remarks>
        ///     Not the prefab's own health as the default: the game scales starting health by
        ///     the world level, so that figure is wrong on any NG+ world and reading it as
        ///     "before" makes the first blow look like an increase.
        /// </remarks>
        private float Health()
        {
            ZDO zdo = View != null ? View.GetZDO() : null;
            return zdo == null ? 0f : zdo.GetFloat(ZDOVars.s_health, float.MaxValue);
        }
    }
}
