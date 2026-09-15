using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Villagers
{
    /// <summary>
    ///     Watches for villagers that achieve nothing, and says so.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Its own driver, and that is the whole design.</b> A watchdog inside the thing it
    ///         watches cannot see it stop. This exists because a destroyed villager left in the
    ///         game's AI list threw out of a Harmony prefix and killed the update loop - so
    ///         <c>Villager.Run</c> never executed at all, and anything hooked into the job tick
    ///         would have stayed silent through precisely the failure that most needed catching.
    ///     </para>
    ///     <para>
    ///         So it ticks on its own, reads the record rather than the object's state, and never
    ///         asks a villager how it is getting on. The same arrangement
    ///         <see cref="Colonies.StructureReaperDriver" /> uses, for a related reason: noticing
    ///         that something has stopped is not a job for the thing that stopped.
    ///     </para>
    /// </remarks>
    internal sealed class IdleWatchDriver : MonoBehaviour
    {
        /// <summary>
        ///     How often to look.
        /// </summary>
        /// <remarks>
        ///     Rarely, because nothing here is urgent: the thing being measured is counted in
        ///     minutes, and a villager that has been quiet for ten of them will still be quiet
        ///     five seconds from now.
        /// </remarks>
        private const float IntervalSeconds = 5f;

        private float _next;

        internal static void Register(GameObject host) => host.AddComponent<IdleWatchDriver>();

        private void Update()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            if (Time.time < _next) return;
            _next = Time.time + IntervalSeconds;

            double now = ZNet.instance.GetTimeSeconds();
            float threshold = ModConfig.IdleWarnSeconds != null ? ModConfig.IdleWarnSeconds.Value : 300f;

            foreach (Villager villager in Villager.Instances)
            {
                Look(villager, now, threshold);
            }
        }

        private static void Look(Villager villager, double now, float threshold)
        {
            // Destroyed but still in the list, which is not hypothetical - it is the exact shape
            // that produced the bug this whole class exists because of.
            if (villager == null) return;
            if (!villager.TryGetComponent(out ZNetView view) || !view.IsValid()) return;

            ZDO zdo = view.GetZDO();
            VillagerState state = new VillagerState(zdo);
            if (!state.IsValid) return;

            // An unset stamp is a villager from a save written before any of this existed.
            // Stamped on first sight rather than read as "idle since the dawn of time", which
            // would greet a player with a settlement of complaints on the first load after an
            // update. Every old save migrates itself for free this way.
            if (state.WorkedAt <= 0d && view.IsOwner())
            {
                state.MarkWorked();
                return;
            }

            int queued = state.ActiveQueue().Count;

            switch (IdleWatch.Judge(now, state.WorkedAt, queued, threshold))
            {
                case Doing.Stalled:
                    double idle = IdleWatch.IdleFor(now, state.WorkedAt);
                    string named = state.HasName ? state.Name : "A villager";

                    // The villager's own words, which is what turns a number into something a
                    // player can act on - and which were the thing quietly lying in every bug
                    // this was built to catch. "Nothing to haul" reads as a settled settlement
                    // right up until you learn hauling was structurally impossible.
                    Chatter.Warn($"[idle] {zdo.m_uid}",
                        $"{named} has finished nothing for {IdleWatch.Spell(idle)} " +
                        $"and says \"{villager.Activity}\".");

                    IdleReports.Complain(zdo.m_uid, named, villager.Activity, idle);
                    break;

                case Doing.Working:
                    // Working again, so the next time it goes quiet is news rather than a
                    // continuation of something half an hour old.
                    Chatter.Forget($"[idle] {zdo.m_uid}");
                    IdleReports.Working(zdo.m_uid);
                    break;
            }
        }
    }
}
