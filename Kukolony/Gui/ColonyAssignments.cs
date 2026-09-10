using System.Collections.Generic;
using Kukolony.Colonies;
using Kukolony.Jobs;
using Kukolony.Villagers;
using UnityEngine;

namespace Kukolony.Gui
{
    /// <summary>
    ///     Panel-side writes to a villager's queue, plus the read helpers the roster renders.
    ///     Every write claims the villager ZDO first, because only the current owner may
    ///     write; a claim that cannot be taken fails the operation instead of writing blind.
    /// </summary>
    internal static class ColonyAssignments
    {
        /// <summary>Replaces the villager's queue. Resets position and attempt state.</summary>
        internal static bool SetQueue(ZDOID villager, List<string> jobIds)
        {
            ZDO zdo = Claim(villager);
            if (zdo == null) return false;
            new VillagerState(zdo).SetQueue(jobIds);
            return true;
        }

        /// <summary>Appends one job, preserving the villager's current position in the queue.</summary>
        internal static bool AppendJob(ZDOID villager, string jobId)
        {
            ZDO zdo = Claim(villager);
            if (zdo == null || string.IsNullOrEmpty(jobId)) return false;
            VillagerState state = new VillagerState(zdo);
            List<string> queue = state.GetQueue();
            queue.Add(jobId);
            state.SetQueue(queue);
            return true;
        }

        /// <summary>
        ///     Display name for a member, resolved straight from the ZDO so unloaded villagers
        ///     still render. Distinguishes a missing record from an unnamed one.
        /// </summary>
        internal static string NameOf(ZDOID villager)
        {
            ZDO zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(villager) : null;
            if (zdo == null) return "(missing)";
            string name = new VillagerState(zdo).Name;
            return string.IsNullOrEmpty(name) ? "(unnamed)" : name;
        }

        /// <summary>
        ///     Live activity when the villager is loaded, falling back to its persisted runtime
        ///     phase when it is not — so the roster stays meaningful outside loaded zones.
        /// </summary>
        internal static string DescribeActivity(ZDOID villager)
        {
            GameObject instance = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(villager) : null;
            if (instance != null && instance.TryGetComponent(out Villager loaded)) return loaded.Activity;
            ZDO zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(villager) : null;
            if (zdo == null) return "missing";
            VillagerState state = new VillagerState(zdo);
            return string.IsNullOrEmpty(state.RuntimePhase) ? "idle" : state.RuntimePhase;
        }

        /// <summary>Takes ownership so this peer may legally write the villager's ZDO.</summary>
        private static ZDO Claim(ZDOID id)
        {
            ZDO zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(id) : null;
            if (zdo == null || !zdo.IsValid()) return null;
            zdo.SetOwner(ZDOMan.GetSessionID());
            return zdo;
        }
    }
}
