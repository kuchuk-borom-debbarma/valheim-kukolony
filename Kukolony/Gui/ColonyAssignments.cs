using System.Collections.Generic;
using Kukolony.Colonies;
using Kukolony.Jobs;
using Kukolony.Villagers;
using UnityEngine;

namespace Kukolony.Gui
{
    internal static class ColonyAssignments
    {
        internal static bool SetQueue(ZDOID villager, List<string> jobIds)
        {
            ZDO zdo = Claim(villager);
            if (zdo == null) return false;
            new VillagerState(zdo).SetQueue(jobIds);
            return true;
        }

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

        internal static string NameOf(ZDOID villager)
        {
            ZDO zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(villager) : null;
            if (zdo == null) return "(missing)";
            string name = new VillagerState(zdo).Name;
            return string.IsNullOrEmpty(name) ? "(unnamed)" : name;
        }

        internal static string DescribeActivity(ZDOID villager)
        {
            GameObject instance = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(villager) : null;
            if (instance != null && instance.TryGetComponent(out Villager loaded)) return loaded.Activity;
            ZDO zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(villager) : null;
            if (zdo == null) return "missing";
            VillagerState state = new VillagerState(zdo);
            return string.IsNullOrEmpty(state.RuntimePhase) ? "idle" : state.RuntimePhase;
        }

        private static ZDO Claim(ZDOID id)
        {
            ZDO zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(id) : null;
            if (zdo == null || !zdo.IsValid()) return null;
            zdo.SetOwner(ZDOMan.GetSessionID());
            return zdo;
        }
    }
}
