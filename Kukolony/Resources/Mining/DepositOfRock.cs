using System.Collections.Generic;
using UnityEngine;

namespace Kukolony.Resources.Mining
{
    /// <summary>
    ///     A <c>MineRock</c>: the older deposit, also made of parts.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Same idea as its successor and a stricter contract. <c>Damage</c> <b>requires</b>
    ///         a named collider - there is no point-and-radius path at all - and refuses with a
    ///         log line if it is handed anything else. And each part's health is its own ZDO
    ///         float under <c>"Health&lt;index&gt;"</c>, hashed at runtime, rather than a package
    ///         holding all of them.
    ///     </para>
    ///     <para>
    ///         The index a part answers to is its position in the component's own area list, and
    ///         that list is built from the children of <c>m_areaRoot</c>. Reading the children in
    ///         the same order is how an index here means the same thing there.
    ///     </para>
    /// </remarks>
    internal sealed class DepositOfRock : MineProtocol
    {
        private readonly MineRock _rock;

        /// <summary>
        ///     The parts, found once, in the order the component numbers them.
        /// </summary>
        /// <remarks>
        ///     <b>Here the index is load-bearing</b>, unlike its successor's: this component
        ///     keeps each part's health under a ZDO key built from the index, so reading the
        ///     wrong slot would report a neighbour's health and every blow would look fruitless.
        ///     The component builds its list once in Start from the children of m_areaRoot; this
        ///     reads the same children once, so the two agree for as long as the object lives.
        /// </remarks>
        private readonly Collider[] _parts;

        internal DepositOfRock(ZNetView view, MineRock rock) : base(view)
        {
            _rock = rock;

            GameObject root = rock == null ? null : rock.m_areaRoot != null ? rock.m_areaRoot : rock.gameObject;
            _parts = root != null ? root.GetComponentsInChildren<Collider>(true) : new Collider[0];
        }

        internal override int MinToolTier => _rock != null ? _rock.m_minToolTier : 0;

        internal override void Areas(List<MineArea> into)
        {
            if (into == null || _rock == null) return;

            for (int i = 0; i < _parts.Length; i++)
            {
                Collider collider = _parts[i];
                if (collider == null || !collider.gameObject.activeInHierarchy) continue;
                if (Health(i) <= 0f) continue;

                into.Add(new MineArea(i, collider.bounds.center, collider));
            }
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

            if (area.Part == null)
            {
                // Without one this component does nothing and says so only to the log. Refusing
                // here is the difference between a villager that reports a problem and one that
                // swings at a rock all afternoon.
                what = "nothing to aim at";
                return BlowResult.Unworkable;
            }

            if (!Mine(out BlowResult claiming, out what)) return claiming;

            float before = Health(area.Index);

            hit.m_point = area.At;
            hit.m_hitCollider = area.Part;
            hit.m_radius = 0f;

            _rock.Damage(hit);

            if (!IsValid || _rock == null)
            {
                what = "that one is out";
                return BlowResult.Felled;
            }

            float after = Health(area.Index);
            if (after < before)
            {
                bool done = Spent();
                what = done ? "that one is out" : "mining";
                return done ? BlowResult.Felled : BlowResult.Struck;
            }

            // Accepted and changed nothing: this pickaxe does not bite, whatever the tier said.
            what = "the pickaxe does not bite";
            return BlowResult.TooHard;
        }

        /// <summary>
        ///     One part's health, from the ZDO.
        /// </summary>
        /// <remarks>
        ///     The key is built the way the component builds it - the string "Health" and the
        ///     index, hashed at runtime.
        ///
        ///     A part nobody has hit answers "more than any blow could leave" rather than the
        ///     prefab's figure, because the prefab's figure is not the truth: the game scales
        ///     starting health by the world level, so on an NG+ world reading it as "before"
        ///     makes the first blow look like an increase. The maximum needs no scaling factor
        ///     and is right either way - a landed blow writes a real number below it, a refused
        ///     one writes nothing at all.
        /// </remarks>
        private float Health(int index)
        {
            ZDO zdo = View != null ? View.GetZDO() : null;
            if (zdo == null || _rock == null) return 0f;

            return zdo.GetFloat(("Health" + index).GetStableHashCode(), float.MaxValue);
        }
    }
}
