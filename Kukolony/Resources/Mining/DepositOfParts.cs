using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kukolony.Resources.Mining
{
    /// <summary>
    ///     A <c>MineRock5</c>: a deposit made of many separately-destructible parts.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This is what a silver vein is. <c>Awake</c> walks every child collider and gives
    ///         each one its own health, its own drop roll and its own destruction; the object
    ///         itself is only destroyed once every part is gone. So a vein spends nearly its
    ///         whole life partly mined, and "is it finished" is a question about parts.
    ///     </para>
    ///     <para>
    ///         <b>Which parts are left needs no private state.</b> <c>UpdateMesh</c> does
    ///         <c>collider.gameObject.SetActive(health &gt; 0f)</c>, so the active child colliders
    ///         are exactly the parts still standing - and their bounds give both the point to aim
    ///         at and somewhere to stand.
    ///     </para>
    ///     <para>
    ///         <b>Health is a base64 package on the ZDO.</b> <c>SaveHealth</c> writes a count and
    ///         a float per part into a <c>ZPackage</c> and stores the base64 under
    ///         <c>s_health</c>. Decoding it is how a blow is proved to have landed - and it means
    ///         progress is readable from the record alone, without the rock being loaded.
    ///     </para>
    /// </remarks>
    internal sealed class DepositOfParts : MineProtocol
    {
        private readonly MineRock5 _rock;

        internal DepositOfParts(ZNetView view, MineRock5 rock) : base(view)
        {
            _rock = rock;
        }

        internal override int MinToolTier => _rock != null ? _rock.m_minToolTier : 0;

        internal override void Areas(List<MineArea> into)
        {
            if (into == null || _rock == null) return;

            // Every collider, in the order Awake found them, because that order *is* the index
            // the component uses. Inactive ones are the parts already gone.
            Collider[] colliders = _rock.gameObject.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null || !collider.gameObject.activeInHierarchy) continue;

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

            if (!Mine(out BlowResult claiming, out what)) return claiming;

            float before = Total();

            // Aimed at a named collider, which is the component's exact path. Its other path -
            // a point with a radius - resolves the part by overlapping a sphere, and if the aim
            // point happens to fall outside every collider it finds nothing, does nothing, and
            // says nothing. Naming the part cannot miss.
            hit.m_point = area.At;
            hit.m_hitCollider = area.Part;
            hit.m_radius = 0f;

            _rock.Damage(hit);

            if (!IsValid || _rock == null)
            {
                // The last part went and the component destroyed the whole object.
                what = "that one is out";
                return BlowResult.Felled;
            }

            float after = Total();
            if (after <= 0f)
            {
                what = "that one is out";
                return BlowResult.Felled;
            }

            if (after < before)
            {
                what = "mining";
                return BlowResult.Struck;
            }

            // Accepted and changed nothing, which means this pickaxe cannot break this rock -
            // the same conclusion felling draws for an axe that does not bite. Saying so beats
            // swinging at it for the rest of the session, and the tier check above cannot catch
            // every case: damage modifiers can reduce a blow to nothing on their own.
            what = "the pickaxe does not bite";
            return BlowResult.TooHard;
        }

        /// <summary>
        ///     Every part's health added up, read from the ZDO rather than the instance.
        /// </summary>
        /// <remarks>
        ///     The sum rather than one part's, because a hit with a radius can damage several at
        ///     once and a blow that only chipped a neighbour still landed. Read from the record
        ///     so it works the same whether or not the mesh has caught up - the component's own
        ///     fields are updated by an RPC that may not have run locally yet.
        /// </remarks>
        private float Total()
        {
            ZDO zdo = View.GetZDO();
            string encoded = zdo != null ? zdo.GetString(ZDOVars.s_health, string.Empty) : string.Empty;

            if (string.IsNullOrEmpty(encoded))
            {
                // Untouched: nothing has been saved yet, so every part is at full health.
                List<MineArea> areas = new List<MineArea>();
                Areas(areas);
                return areas.Count * (_rock != null ? _rock.m_health : 0f);
            }

            try
            {
                ZPackage package = new ZPackage(Convert.FromBase64String(encoded));
                int count = package.ReadInt();
                if (count < 0 || count > 4096) return 0f;

                float total = 0f;
                for (int i = 0; i < count; i++)
                {
                    float health = package.ReadSingle();
                    if (health > 0f) total += health;
                }

                return total;
            }
            catch (Exception)
            {
                // A package we cannot read is not a reason to stop mining; it is a reason not to
                // claim we measured anything. Zero would read as "finished", so the untouched
                // answer is the safer lie - the caller re-reads the parts either way.
                return float.MaxValue;
            }
        }
    }
}
