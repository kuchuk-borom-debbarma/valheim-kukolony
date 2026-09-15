using System.Collections.Generic;
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
    ///         <b>A job may have several, tried in order.</b> One area is the common case and
    ///         this type describes one - the list of them lives on the job, and the rule for
    ///         working it is that the first area with anything to do is the one that gets
    ///         worked. See <see cref="AllFor" />.
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
        ///     The same area, no wider than a limit.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         For work whose search has a ceiling of its own. A work area wider than the
        ///         search that feeds it is a band of ground the job reports as in range and
        ///         will never act on - not a hypothetical: a colony radius of 128 against the
        ///         default 96 m scan, or a flag set to 200, both produce it, and the villager
        ///         stands reporting nothing to do about trees its own screen has listed.
        ///     </para>
        ///     <para>
        ///         Narrowing is the job's to apply rather than this type's to assume, because
        ///         hauling has no such ceiling and must keep the radius it was given.
        ///     </para>
        ///     <para>
        ///         <b>This shrinks the mismatch; it does not abolish it.</b> The search is
        ///         circles around the hearth and the flags, while a work area may be centred
        ///         on any registered structure - so an area on a chest near the edge still
        ///         reaches ground no circle covers. Nothing breaks there: the search simply
        ///         never offers those candidates, so the job works what it can see. Closing
        ///         the last of it would mean testing the area against the anchor set, which
        ///         is machinery for a promise a row makes rather than for anything a villager
        ///         does wrong.
        ///     </para>
        /// </remarks>
        internal WorkArea NoWiderThan(float limit) =>
            limit > 0f && limit < Radius ? new WorkArea(Centre, limit, Name) : this;

        /// <summary>
        ///     The first area a job works in, falling back to the whole settlement.
        /// </summary>
        /// <remarks>
        ///     For callers that want one answer - a screen stating a reach, a check naming a
        ///     place. Work itself asks <see cref="AllFor" />, because a job may be pointed at
        ///     several places and answering with the first one would quietly work one of them.
        /// </remarks>
        internal static WorkArea For(Colony colony, JobDefinition job)
        {
            List<WorkArea> areas = new List<WorkArea>();
            AllFor(colony, job, areas);
            return areas[0];
        }

        /// <summary>
        ///     Every area a job works, in the order it should try them.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Never empty. A job whose areas have all been destroyed or unregistered works
        ///         the settlement rather than stopping, and so does one that was never pointed
        ///         anywhere. Silently doing nothing is the failure mode a colony sim can least
        ///         afford - a villager that has quietly had no valid place to work for an hour
        ///         looks exactly like one with nothing to do.
        ///     </para>
        ///     <para>
        ///         The caller supplies the list so a hot path may reuse a buffer. None does
        ///         yet - every caller hands in a fresh one - which is fine at the rate work is
        ///         chosen, and leaves the door open without pretending it has been walked
        ///         through.
        ///     </para>
        /// </remarks>
        internal static void AllFor(Colony colony, JobDefinition job, List<WorkArea> into) =>
            AllFor(colony, job, null, into);

        /// <summary>
        ///     Every area a job works for one particular villager, which matters when it is in a
        ///     party.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>A party is an area that walks.</b> A villager following somebody works the
        ///         ground around them, and it goes <em>first</em> - the list is tried in order, so
        ///         first is what "do not wander off while there is work here" means. The
        ///         settlement's own areas stay on the list behind it and cost nothing: they are
        ///         usually far away, the sweep returns nothing in them, and on the occasion the
        ///         party is standing in its own base they are exactly right.
        ///     </para>
        ///     <para>
        ///         Null villager is the ordinary case and the old behaviour. Everything that is
        ///         not about one villager's work - a screen stating a reach, a pin on the map -
        ///         should keep asking that way, because a reach that changed depending on who was
        ///         being followed would be a number the screen could not honestly print.
        ///     </para>
        /// </remarks>
        internal static void AllFor(Colony colony, JobDefinition job, Villagers.Villager villager,
            List<WorkArea> into)
        {
            if (into == null) return;
            into.Clear();

            AddPartyArea(villager, into);

            if (colony == null)
            {
                // A villager in a party has somewhere to work even with no Kolony behind it.
                if (into.Count == 0) into.Add(new WorkArea(Vector3.zero, 0f, "nowhere"));
                return;
            }

            WorkArea settlement = new WorkArea(colony.transform.position, colony.EffectiveRadius,
                colony.State.Name);

            List<string> tokens = job?.Areas;
            if (tokens == null || tokens.Count == 0)
            {
                into.Add(settlement);
                return;
            }

            int beforeTokens = into.Count;

            List<StructureRecord> records = colony.State.GetStructures();
            bool settlementAdded = false;

            foreach (string token in tokens)
            {
                if (string.IsNullOrEmpty(token))
                {
                    // The settlement itself, chosen alongside outposts. Once only: a list is
                    // worked in order, and the same ground twice is a second fruitless sweep
                    // between two places that do have work.
                    if (settlementAdded) continue;

                    settlementAdded = true;
                    into.Add(settlement);
                    continue;
                }

                if (TryResolve(records, job, token, out WorkArea area)) into.Add(area);
            }

            // Every place named is gone. Falls back rather than leaving an empty list, which
            // no caller checks for and every caller would read as "nothing to do here".
            //
            // Counted from where the tokens began rather than from zero, so a party villager
            // whose job names a demolished outpost still falls back to the settlement instead of
            // its party area silently standing in for one.
            if (into.Count == beforeTokens) into.Add(settlement);
        }

        /// <summary>
        ///     The ground around the player this villager follows, if it follows one.
        /// </summary>
        /// <remarks>
        ///     Nothing is added when the leader is not loaded. A party villager whose player has
        ///     gone through a portal should work the settlement's ground if it is standing on it
        ///     and otherwise find nothing - not work a circle around where somebody used to be.
        /// </remarks>
        private static void AddPartyArea(Villagers.Villager villager, List<WorkArea> into)
        {
            if (villager == null) return;

            Villagers.VillagerState state = villager.State;
            if (!state.IsValid || !state.InAParty) return;

            Player leader = Party.PartyMembership.LeaderOf(state);
            if (leader == null) return;

            into.Add(new WorkArea(leader.transform.position, PartyRadius, leader.GetPlayerName()));
        }

        /// <summary>How far around its player a villager in a party will work.</summary>
        /// <remarks>
        ///     Smaller than a work area's default on purpose. A party is somebody you are standing
        ///     with, and a villager that wanders forty metres off to a better tree has stopped
        ///     being in a party in every sense that matters to the person it is following.
        /// </remarks>
        internal static float PartyRadius =>
            ModConfig.PartyWorkRadius != null ? ModConfig.PartyWorkRadius.Value : 28f;

        /// <summary>One named place, if it is still registered and still exists.</summary>
        private static bool TryResolve(List<StructureRecord> records, JobDefinition job,
            string token, out WorkArea area)
        {
            area = default;

            foreach (StructureRecord record in records)
            {
                if (record.PersistentId != token) continue;

                ZDO zdo = ZDOMan.instance?.GetZDO(record.Id);
                if (zdo == null) return false;

                // A flag switched off is a place the settlement is not working, which is the
                // plainest reading of "villagers may not use this" - and the only way to pause
                // an outpost without deleting the job that names it.
                if (!record.Settings.InService) return false;

                // A flag brings its own reach - its screen says how far, and a job pointed
                // at it working a default-sized patch of a larger outpost contradicted the
                // number the player set. The job's own radius still wins when given, because
                // "work the near half of the quarry" is a legitimate instruction.
                //
                // Deliberately not capped at the flag's radius. That was tried, on the
                // reasoning that a flag's radius is what makes ground the Kolony's - and it
                // is the wrong place to enforce it. A work area is the only bound hauling
                // has, so capping it meant shrinking a flag silently stopped a haul job
                // tidying chests it had tidied the day before; and the Kolony's ground is the
                // hearth's reach together with every flag's, so a job pointed at one flag was
                // refused work that sat plainly inside the settlement. What bounds the search
                // is the config ceiling, and what narrows it is this radius. Nothing else.
                float radius = job != null && job.WorkRadius > 0f ? job.WorkRadius
                    : (record.Capabilities & StructureCapability.WorkArea) != 0
                        ? WorkFlag.RadiusOf(zdo)
                        : DefaultRadius;
                area = new WorkArea(zdo.GetPosition(), radius, record.Name);
                return true;
            }

            return false;
        }

        /// <summary>How far a work area reaches when the job has not said.</summary>
        /// <remarks>
        ///     The same as a settlement's own default reach, so pointing a job at an outpost
        ///     gives it an outpost-sized patch of ground rather than something arbitrary.
        /// </remarks>
        internal const float DefaultRadius = 48f;
    }
}
