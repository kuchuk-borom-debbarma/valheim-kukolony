using Kukolony.Colonies;
using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Jobs
{
    /// <summary>
    ///     Where a job is allowed to look for work.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A work area is a registered structure used as a centre, plus a radius - not a new
    ///         kind of thing to place. Anything already registered to the colony can serve as
    ///         one, which means work areas inherit registration, durable tokens, the structures
    ///         screen and the reaper for free, and a player defines one by pointing at a chest or
    ///         a kiln they have already built.
    ///     </para>
    ///     <para>
    ///         <b>It bounds where work is found, not where it goes.</b> A villager assigned to the
    ///         far quarry gathers there and may still carry its load back into the settlement,
    ///         because a destination is chosen by what the settlement wants rather than by where
    ///         the villager was standing. Bounding both would make an outpost a place things go
    ///         to be forgotten.
    ///     </para>
    /// </remarks>
    internal readonly struct WorkArea
    {
        internal WorkArea(Vector3 centre, float radius, string name)
        {
            Centre = centre;
            Radius = radius;
            Name = name;
        }

        internal Vector3 Centre { get; }

        internal float Radius { get; }

        /// <summary>What to call it when saying why something was out of reach.</summary>
        internal string Name { get; }

        internal bool Contains(Vector3 point) => Utils.DistanceXZ(point, Centre) <= Radius;

        /// <summary>
        ///     The area a job works in, falling back to the whole settlement.
        /// </summary>
        /// <remarks>
        ///     A job whose work area has been destroyed or unregistered works the settlement
        ///     rather than stopping. Silently doing nothing is the failure mode a colony sim can
        ///     least afford - a villager that has quietly had no valid place to work for an hour
        ///     looks exactly like one with nothing to do.
        /// </remarks>
        internal static WorkArea For(Colony colony, JobDefinition job)
        {
            if (colony == null) return new WorkArea(Vector3.zero, 0f, "nowhere");

            WorkArea settlement = new WorkArea(colony.transform.position, colony.EffectiveRadius,
                colony.State.Name);

            if (job == null || string.IsNullOrEmpty(job.WorkArea)) return settlement;

            foreach (StructureRecord record in colony.State.GetStructures())
            {
                if (record.PersistentId != job.WorkArea) continue;

                ZDO zdo = ZDOMan.instance?.GetZDO(record.Id);
                if (zdo == null) break;

                float radius = job.WorkRadius > 0f ? job.WorkRadius : DefaultRadius;
                return new WorkArea(zdo.GetPosition(), radius, record.Name);
            }

            return settlement;
        }

        /// <summary>How far a work area reaches when the job has not said.</summary>
        /// <remarks>
        ///     The same as a settlement's own default reach, so pointing a job at an outpost
        ///     gives it an outpost-sized patch of ground rather than something arbitrary.
        /// </remarks>
        internal const float DefaultRadius = 48f;
    }
}
