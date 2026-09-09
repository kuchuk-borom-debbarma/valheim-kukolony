using System.Collections.Generic;
using Kukolony.Colonies;
using Kukolony.Core;
using Kukolony.Villagers;
using UnityEngine;

namespace Kukolony.Gui
{
    /// <summary>
    ///     Who lives where and who works what.
    ///
    ///     All of the rules, none of the UI. Keeping them here rather than in click
    ///     handlers is what lets the harness test assignment without driving a mouse -
    ///     the tests call exactly what the buttons call.
    ///
    ///     Everything is addressed by ZDOID and read through ZDOMan, never through a
    ///     GameObject. Managing a colony from the far side of the map is the point of
    ///     colonies, so assignment has to work on villagers that are not instantiated.
    /// </summary>
    internal static class ColonyAssignments
    {
        /// <summary>A bed belongs to exactly one villager. Stations are shared.</summary>
        internal static ZDOID HomeOwner(ColonyState colony, ZDOID bed)
        {
            if (bed.IsNone() || !colony.IsValid)
            {
                return ZDOID.None;
            }

            foreach (ZDOID villager in colony.GetMembers(ColonyMemberKind.Villager))
            {
                ZDO zdo = ZDOMan.instance?.GetZDO(villager);
                if (zdo != null && new VillagerState(zdo).HomeBed == bed)
                {
                    return villager;
                }
            }

            return ZDOID.None;
        }

        internal static int StationWorkerCount(ColonyState colony, ZDOID station)
        {
            if (station.IsNone() || !colony.IsValid)
            {
                return 0;
            }

            int count = 0;
            foreach (ZDOID villager in colony.GetMembers(ColonyMemberKind.Villager))
            {
                ZDO zdo = ZDOMan.instance?.GetZDO(villager);
                if (zdo != null && new VillagerState(zdo).Post == station)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        ///     Gives a villager a home, taking the bed from whoever had it.
        ///
        ///     Stealing is deliberate and visible - the picker labels a bed with its
        ///     current owner before you click it - but the previous owner does silently
        ///     revert to its spawn position, so it is logged.
        /// </summary>
        internal static bool AssignHome(ColonyState colony, ZDOID villager, ZDOID bed)
        {
            ZDO villagerZdo = Claim(villager);
            if (villagerZdo == null)
            {
                return false;
            }

            ZDOID previousOwner = HomeOwner(colony, bed);
            if (!previousOwner.IsNone() && previousOwner != villager)
            {
                ZDO previousZdo = Claim(previousOwner);
                if (previousZdo != null)
                {
                    new VillagerState(previousZdo).SetHomeBed(ZDOID.None);
                    Log.Info($"'{new VillagerState(previousZdo).Name}' lost their bed to "
                             + $"'{new VillagerState(villagerZdo).Name}'");
                }
            }

            new VillagerState(villagerZdo).SetHomeBed(bed);
            Log.Info($"'{new VillagerState(villagerZdo).Name}' now sleeps at {bed}");
            return true;
        }

        internal static bool ClearHome(ZDOID villager)
        {
            ZDO villagerZdo = Claim(villager);
            if (villagerZdo == null)
            {
                return false;
            }

            new VillagerState(villagerZdo).SetHomeBed(ZDOID.None);
            return true;
        }

        /// <summary>
        ///     Assigns a workstation. Nothing is stolen - several villagers on one post is
        ///     supported, because TargetClaims stops them contending over the same item.
        /// </summary>
        internal static bool AssignStation(ZDOID villager, ZDOID station)
        {
            ZDO villagerZdo = Claim(villager);
            if (villagerZdo == null)
            {
                return false;
            }

            VillagerState state = new VillagerState(villagerZdo);
            state.SetPost(station);

            // Starting a new job part-way through the last one's cycle would leave a
            // stale step index and target pointing at the old post's work.
            state.SetStepIndex(0);
            state.SetStepTarget(ZDOID.None);

            Log.Info($"'{state.Name}' assigned to a workstation");
            return true;
        }

        internal static bool ClearStation(ZDOID villager) => AssignStation(villager, ZDOID.None);

        /// <summary>Villager name, readable whether or not it is loaded.</summary>
        internal static string NameOf(ZDOID villager)
        {
            ZDO zdo = ZDOMan.instance?.GetZDO(villager);
            if (zdo == null)
            {
                return "(missing)";
            }

            string name = new VillagerState(zdo).Name;
            return string.IsNullOrEmpty(name) ? "(unnamed)" : name;
        }

        /// <summary>One line describing a villager's current assignments.</summary>
        internal static string DescribeVillager(ColonyState colony, ZDOID villager)
        {
            ZDO zdo = ZDOMan.instance?.GetZDO(villager);
            if (zdo == null)
            {
                return "(missing)";
            }

            VillagerState state = new VillagerState(zdo);
            string home = state.HasHomeBed
                ? LabelFor(colony, ColonyMemberKind.Home, state.HomeBed)
                : "-";
            string work = !state.Post.IsNone()
                ? LabelFor(colony, ColonyMemberKind.Station, state.Post)
                : "-";

            return $"{NameOf(villager)}   home: {home}   work: {work}";
        }

        /// <summary>
        ///     Beds and posts have no names, so they are labelled by position in the
        ///     colony's list - stable, because the list order is.
        /// </summary>
        internal static string LabelFor(ColonyState colony, ColonyMemberKind kind, ZDOID member)
        {
            if (member.IsNone())
            {
                return "-";
            }

            List<ZDOID> members = colony.GetMembers(kind);
            int index = members.IndexOf(member);
            string prefix = kind == ColonyMemberKind.Home ? "Bed" : "Post";

            return index >= 0 ? $"{prefix} {index + 1}" : $"{prefix} ?";
        }

        /// <summary>Picker label: what it is, and who already has it.</summary>
        internal static string DescribeChoice(ColonyState colony, ColonyMemberKind kind, ZDOID member, int index)
        {
            string prefix = kind == ColonyMemberKind.Home ? "Bed" : "Post";
            string distance = DistanceLabel(colony, member);

            if (kind == ColonyMemberKind.Home)
            {
                ZDOID owner = HomeOwner(colony, member);
                string held = owner.IsNone() ? "free" : NameOf(owner);
                return $"{prefix} {index + 1}{distance} ({held})";
            }

            int workers = StationWorkerCount(colony, member);
            return $"{prefix} {index + 1}{distance} ({workers} working)";
        }

        private static string DistanceLabel(ColonyState colony, ZDOID member)
        {
            ZDO memberZdo = ZDOMan.instance?.GetZDO(member);
            ZDO colonyZdo = ZDOMan.instance?.GetZDO(colony.Id);
            if (memberZdo == null || colonyZdo == null)
            {
                return string.Empty;
            }

            float distance = Utils.DistanceXZ(memberZdo.GetPosition(), colonyZdo.GetPosition());
            return $" - {distance:F0}m";
        }

        /// <summary>
        ///     Resolves a ZDO and takes ownership, because a write by a non-owner lands
        ///     locally and is clobbered on the next sync. Works without the object being
        ///     instantiated, which is the whole point.
        /// </summary>
        private static ZDO Claim(ZDOID id)
        {
            if (id.IsNone() || ZDOMan.instance == null)
            {
                return null;
            }

            ZDO zdo = ZDOMan.instance.GetZDO(id);
            if (zdo == null || !zdo.IsValid())
            {
                return null;
            }

            if (!zdo.IsOwner())
            {
                zdo.SetOwner(ZDOMan.GetSessionID());
            }

            return zdo;
        }
    }
}
