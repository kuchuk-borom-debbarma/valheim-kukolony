using System.Collections.Generic;
using Kukolony.Colonies;
using Kukolony.Core;
using Kukolony.Resources;
using Kukolony.Villagers;
using Kukolony.Villagers.Navigation;
using UnityEngine;

namespace Kukolony.Jobs.Repair
{
    /// <summary>What one mending tick needs. Assembled by the villager, never stored.</summary>
    internal sealed class RepairContext
    {
        internal Villager Villager;
        internal Colony Colony;
        internal Container Bag;
        internal VillagerWalk Walk;
        internal VillagerAnimation Animation;
        internal VisEquipment Equipment;
        internal JobDefinition Job;
        internal VillagerState State;
        internal float DeltaTime;
    }

    /// <summary>
    ///     Keeping the settlement standing.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>This job exists because the mod created the need for it.</b> Valheim decays what
    ///         you build, and the keep-alive holds a Kolony's zones open so villagers can work
    ///         off-screen - which means an outpost nobody has visited for a week has been loaded
    ///         and rotting the whole time, where vanilla would have left it frozen.
    ///     </para>
    ///     <para>
    ///         <b>It cannot run out of world</b>, which no other producing job can say. Chopping
    ///         and mining need a stopping rule because a forest and a vein go on offering work
    ///         until they are stripped; nothing here is consumed or produced, so when there is no
    ///         damage there is no work.
    ///     </para>
    ///     <para>
    ///         <b>And it mends in batches.</b> A base is hundreds of pieces and a repair is one
    ///         swing, so a trip per plank would be absurd walking and would spend a repetition on
    ///         each. Arriving anywhere, the villager puts right everything worn within reach
    ///         before the trip ends - which is what a person would do, and does more to keep a
    ///         villager from wandering than any rule about choosing.
    ///     </para>
    /// </remarks>
    internal static class RepairJob
    {
        /// <summary>
        ///     How long between swings.
        /// </summary>
        /// <remarks>
        ///     The same reason every other working job waits: a step reporting Running is re-asked
        ///     at 20 Hz, so without this a villager mends a house in a second and broadcasts a
        ///     hammer effect for every piece of it.
        /// </remarks>
        private const float SecondsBetweenSwings = .7f;

        /// <summary>
        ///     How far a villager reaches while mending one thing.
        /// </summary>
        /// <remarks>
        ///     What makes a trip worth taking. Everything worn inside this is put right on the one
        ///     visit, which is the difference between maintaining a hall and walking to each of
        ///     its four hundred planks in turn.
        /// </remarks>
        private const float BatchReach = 4f;

        private static readonly Dictionary<ZDOID, float> NextSwing = new Dictionary<ZDOID, float>();

        /// <summary>The piece each villager's walk has done its best at.</summary>
        /// <remarks>
        ///     The same latch every working job here keeps: mending calls <c>Walk.Forget()</c>
        ///     every tick, which zeroes the stall clock the working reach would otherwise be read
        ///     from - so the reach would collapse the moment a villager arrived and it would
        ///     shuffle back and forth in front of a wall.
        /// </remarks>
        private static readonly Dictionary<ZDOID, ZDOID> Settled = new Dictionary<ZDOID, ZDOID>();

        private static readonly List<WorkArea> Areas = new List<WorkArea>();

        /// <summary>Dropped when a world unloads; none of these identities survive one.</summary>
        internal static void Clear()
        {
            NextSwing.Clear();
            Settled.Clear();
        }

        /// <summary>Drops what a villager that no longer exists was holding.</summary>
        internal static void Forget(ZDOID villager)
        {
            if (villager.IsNone()) return;

            NextSwing.Remove(villager);
            Settled.Remove(villager);
        }

        internal static JobResult Tick(RepairContext context, out string activity)
        {
            VillagerState state = context.State;

            GameObject target = Resolve(state.Target, out bool lost);

            // Gone is not a fault - a troll took it down, or the player did, and either way the
            // recorded target is stale rather than the job broken.
            if (lost) state.ClearTarget();

            ItemDrop.ItemData hammer = Hammer(context);

            // Fetched before anything else, as chopping and mining fetch theirs. Answers null when
            // the settlement has no hammer either, so the table's own "no hammer" reply stands.
            if (hammer == null)
            {
                JobResult? fetching = ToolErrand.Run(context.Villager, context.Colony, context.Bag,
                    context.Walk, state, context.DeltaTime, ToolKind.Hammer, out activity);

                if (fetching.HasValue) return fetching.Value;
            }

            ZDO record = Record(state.Target);
            float below = Threshold(context.Job);

            RepairFacts facts = new RepairFacts(
                hasTool: hammer != null,
                hasTarget: !state.Target.IsNone() && record != null,
                damaged: record != null && Repairable.Worn(record, below),
                atTarget: target != null && Within(context, target.transform.position),
                inStationRange: record != null && Reachable(record),
                tired: false);

            RepairStep step = RepairTransitions.Next((RepairState)state.WorkState, facts);

            switch (step.Action)
            {
                case RepairAction.Yield:
                    return JobOutcomes.Skipped(state,
                        hammer == null ? "no hammer" : "nothing needs mending", out activity);

                case RepairAction.ChooseWork:
                    return Record(state, step, Choose(context, below, out activity));

                case RepairAction.MoveToTarget:
                    return Record(state, step, Walk(context, target, out activity));

                case RepairAction.Mend:
                    return Record(state, step, Mend(context, target, below, out activity));

                case RepairAction.Complete:
                    Release(context);
                    return JobOutcomes.Completed(state, "that is mended", out activity);

                default:
                    // An action this engine does not handle is a programming error rather than a
                    // world state. Say so rather than silently idling.
                    return JobOutcomes.Failed(state, "unhandled repair action", out activity);
            }
        }

        /// <summary>Records the next state when a step made progress.</summary>
        private static JobResult Record(VillagerState state, RepairStep step, JobResult result)
        {
            if (result != JobResult.Running && result != JobResult.Completed) return result;

            state.SetWorkState((int)step.Next);
            return JobResult.Running;
        }

        /// <summary>
        ///     Takes the nearest worn thing, in the job's own order of places - and where the
        ///     villager is standing first.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>An area the villager is already in, that still has work, is tried before the
        ///         list.</b> Every other job here takes its areas strictly in the order the player
        ///         set, which is right when the work is a copse or a quarry you visit and clear.
        ///         Mending is spread over a whole settlement and never finishes, so the ordinary
        ///         rule would have a villager in the far outpost walk home the moment a plank near
        ///         the hearth dropped below the threshold - and then walk back, and then home
        ///         again.
        ///     </para>
        ///     <para>
        ///         The player's order still decides everything else. This only says: do not leave
        ///         where you are standing while it still needs you.
        ///     </para>
        /// </remarks>
        private static JobResult Choose(RepairContext context, float below, out string activity)
        {
            Vector3 here = context.Villager.transform.position;
            Places(context.Colony, context.Job, here);

            RepairGround.Below = below;
            List<ZDOID> candidates = RepairGround.Near(context.Colony);
            Tally tally = new Tally();

            foreach (WorkArea area in Areas)
            {
                ZDOID best = Nearest(context, area, candidates, here, below, tally);
                if (best.IsNone()) continue;

                // A new target is a new walk and a new tolerance. Without the walk being told, its
                // stall clock still holds the last target's timings and judges the first step of
                // this one as already stuck.
                context.Walk.Forget();
                context.Walk.NewLeg();

                Settled.Remove(context.Villager.Id);
                context.State.SetTarget(best);

                activity = "off to mend";
                return JobResult.Running;
            }

            return JobOutcomes.Skipped(context.State, tally.Say(candidates.Count), out activity);
        }

        /// <summary>
        ///     The job's places, with the one the villager is standing in brought to the front.
        /// </summary>
        private static void Places(Colony colony, JobDefinition job, Vector3 here)
        {
            WorkArea.AllFor(colony, job, Areas);

            for (int i = 0; i < Areas.Count; i++)
            {
                Areas[i] = Areas[i].NoWiderThan(RepairGround.SearchRadius);
            }

            for (int i = 0; i < Areas.Count; i++)
            {
                if (!Areas[i].Contains(here)) continue;

                // Moved rather than sorted, so the rest keep the player's order exactly.
                WorkArea standing = Areas[i];
                Areas.RemoveAt(i);
                Areas.Insert(0, standing);
                return;
            }
        }

        /// <summary>
        ///     Why nothing was chosen, counted as the choosing happens.
        /// </summary>
        /// <remarks>
        ///     The arrangement mining and farming keep. Every count is a question a player will
        ///     ask standing in front of a wall that is visibly falling down while a villager with
        ///     a hammer says it has nothing to do - and the station one is the answer they will
        ///     never guess, because the rule is vanilla's rather than this mod's.
        /// </remarks>
        private sealed class Tally
        {
            internal int Outside;
            internal int Whole;
            internal int NoStation;
            internal int Refused;
            internal int Taken;

            internal string Say(int seen)
            {
                if (seen == 0) return "nothing needs mending";

                // In the order a player can act on. The station first: it is the only one whose
                // answer is "build a workbench over there", and the only one they cannot see.
                if (NoStation > 0)
                {
                    return $"no workbench near what needs mending ({NoStation})";
                }

                if (Outside > 0) return $"what is worn is outside where this job works ({Outside})";
                if (Taken > 0) return "somebody else is mending it";
                if (Refused > 0) return "cannot get to what needs mending";
                if (Whole > 0) return "nothing is worn enough to bother with";

                return "nothing needs mending";
            }
        }

        private static ZDOID Nearest(RepairContext context, WorkArea area, List<ZDOID> candidates,
            Vector3 here, float below, Tally tally)
        {
            ZDOID best = ZDOID.None;
            float closest = float.MaxValue;

            foreach (ZDOID id in candidates)
            {
                ZDO zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(id) : null;
                if (zdo == null || !zdo.IsValid()) continue;

                Vector3 at = zdo.GetPosition();
                if (!area.Contains(at))
                {
                    tally.Outside++;
                    continue;
                }

                // The sweep answers a looser threshold than any one job may want, because one
                // list serves every villager. Narrowed here, by this job's own number.
                if (!Repairable.Worn(zdo, below))
                {
                    tally.Whole++;
                    continue;
                }

                // The station, before the walk. Both halves are knowable from here: which station
                // the piece needs is on its prefab, and whether one stands close enough is a
                // question the game will answer about any point.
                if (!Reachable(zdo))
                {
                    tally.NoStation++;
                    continue;
                }

                if (Unreachable.Refuses(context.Villager.Id, id))
                {
                    tally.Refused++;
                    continue;
                }

                if (TargetClaims.IsClaimedByOther(id, context.Villager))
                {
                    tally.Taken++;
                    continue;
                }

                float distance = Utils.DistanceXZ(at, here);
                if (distance >= closest) continue;

                closest = distance;
                best = id;
            }

            return best;
        }

        /// <summary>
        ///     Whether a crafting station of the kind this piece needs stands close enough to it.
        /// </summary>
        /// <remarks>
        ///     The rule that binds the player binds the villager. A piece that names no station -
        ///     a campfire, a workbench itself - may be mended anywhere, which is also vanilla's
        ///     answer.
        /// </remarks>
        private static bool Reachable(ZDO zdo)
        {
            Mendable kind = Repairable.Of(zdo.GetPrefab());
            if (kind == null) return false;
            if (kind.Station.Length == 0) return true;

            return CraftingStation.HaveBuildStationInRange(kind.Station, zdo.GetPosition()) != null;
        }

        private static JobResult Mend(RepairContext context, GameObject target, float below,
            out string activity)
        {
            if (target == null)
            {
                context.State.ClearTarget();
                return JobOutcomes.Running("it was gone", out activity);
            }

            Vector3 at = target.transform.position;
            context.Villager.FaceTowards(at, context.DeltaTime);

            // The walk is over. Said plainly so its stall clock stops running against a villager
            // that is standing still on purpose.
            context.Walk.Forget();

            ZDOID who = context.Villager.Id;
            if (!who.IsNone())
            {
                if (NextSwing.TryGetValue(who, out float when) && Time.time < when)
                {
                    // Between swings. Not a swing, and deliberately not animated: retriggering the
                    // animation every tick is how it never plays at all.
                    return JobOutcomes.Running("mending", out activity);
                }

                NextSwing[who] = Time.time + SecondsBetweenSwings;
            }

            int mended = Batch(context, at, below);

            if (mended == 0)
            {
                // Nothing here would take a repair after all. The table's Complete arm runs next
                // tick once the target reads whole; this only has to not pretend otherwise.
                return JobOutcomes.Running("nothing to mend here", out activity);
            }

            context.State.TouchClaim();
            context.Animation?.Swing();

            return JobOutcomes.Running(mended == 1 ? "mending" : $"mending ({mended})", out activity);
        }

        /// <summary>
        ///     Mends everything worn within reach, and says how many.
        /// </summary>
        /// <remarks>
        ///     <b>Ownership first, for the fourth time in this mod.</b> <c>Repair()</c> sends an
        ///     RPC to the record's owner, and a piece the world owns has no owner to send it to -
        ///     the same rule felling, mining and picking each found by watching work vanish into
        ///     silence. Written in advance here rather than discovered again.
        ///
        ///     <b>And the result is read back.</b> <c>Repair()</c> returning true says the call
        ///     was made, not that health moved.
        /// </remarks>
        private static int Batch(RepairContext context, Vector3 at, float below)
        {
            if (ZNetScene.instance == null) return 0;

            int mended = 0;

            foreach (ZNetView view in ZNetScene.instance.m_instances.Values)
            {
                if (view == null || !view.IsValid()) continue;

                ZDO zdo = view.GetZDO();
                if (Repairable.Of(zdo.GetPrefab()) == null) continue;
                if (Utils.DistanceXZ(zdo.GetPosition(), at) > BatchReach) continue;
                if (!Repairable.Worn(zdo, below)) continue;
                if (!Reachable(zdo)) continue;
                if (!view.TryGetComponent(out WearNTear wear)) continue;

                if (!view.IsOwner())
                {
                    // Claimed and left for the next tick, which is what every other job here does
                    // rather than block on a round trip.
                    view.ClaimOwnership();
                    continue;
                }

                float health = Repairable.Wear(zdo);
                if (!wear.Repair()) continue;

                // Measured, not trusted. A call that returned true and moved nothing would be a
                // villager hammering happily at something that never improves.
                if (Repairable.Wear(zdo) <= health) continue;

                mended++;

                // The piece's own placement effect, which is exactly what the game plays when a
                // player repairs one - Player.Repair swings the tool's animation and then creates
                // this. WearNTear.Repair() plays nothing itself, so without this a villager mends
                // a wall in complete silence with no puff of dust, and the only sign anything
                // happened is a number nobody can see.
                if (view.TryGetComponent(out Piece piece))
                {
                    piece.m_placeEffect?.Create(view.transform.position, view.transform.rotation);
                }
            }

            return mended;
        }

        /// <summary>The best hammer the villager owns, shown in its hand.</summary>
        private static ItemDrop.ItemData Hammer(RepairContext context)
        {
            ItemDrop.ItemData best = VillagerTool.Best(
                context.Bag != null ? context.Bag.GetInventory() : null, ToolKind.Hammer);

            VillagerTool.Show(context.Equipment, context.Animation, Worn(context), best);
            return best;
        }

        private static ZDO Worn(RepairContext context) =>
            context.Villager != null && context.Villager.TryGetComponent(out ZNetView view) &&
            view.IsValid()
                ? view.GetZDO()
                : null;

        /// <summary>How worn something must be before this job bothers with it.</summary>
        /// <remarks>
        ///     A fraction rather than a number, because health across what this game ships runs
        ///     from fifty to two thousand - "mend anything under two hundred" would mean every
        ///     stone wall always and no armour stand ever.
        /// </remarks>
        private static float Threshold(JobDefinition job) =>
            job == null || job.RepairBelow <= 0f ? JobDefinition.DefaultRepairBelow
                : Mathf.Clamp(job.RepairBelow, .05f, 1f);

        private static void Release(RepairContext context)
        {
            context.State.ClearTarget();
            Forget(context.Villager.Id);
        }

        private static JobResult Walk(RepairContext context, GameObject target, out string activity)
        {
            if (target == null)
            {
                context.State.ClearTarget();
                return JobOutcomes.Running("it was gone", out activity);
            }

            switch (context.Walk.MoveTowards(target.transform.position, Approach.ToStructure,
                        deltaTime: context.DeltaTime))
            {
                case MoveResult.Arrived:
                    context.State.TouchClaim();

                    if (!context.Villager.Id.IsNone()) Settled[context.Villager.Id] = context.State.Target;

                    return JobOutcomes.Running("off to mend", out activity);

                case MoveResult.PathFailed:
                    Unreachable.Refuse(context.Villager.Id, context.State.Target,
                        Unreachable.BlockedForSeconds);
                    Release(context);
                    return JobOutcomes.Skipped(context.State, "cannot get there", out activity);

                default:
                    context.State.TouchClaim();

                    // Bounded, as every other walking job bounds this arm: a trip that can never
                    // arrive otherwise returns Running for ever, which consumes no repetition, so
                    // the queue never advances and every later entry stops running too.
                    ZDOID abandoned = context.State.Target;
                    JobResult? stuck = JobOutcomes.GiveUpIfStuck(context.Villager, context.State,
                        context.Walk.TripStalledFor, abandoned, () => "something to mend",
                        out string gaveUp);

                    if (stuck.HasValue)
                    {
                        Unreachable.Refuse(context.Villager.Id, abandoned);
                        Release(context);
                        activity = gaveUp;
                        return stuck.Value;
                    }

                    return JobOutcomes.Running("off to mend", out activity);
            }
        }

        /// <summary>Whether the villager is close enough to work on this.</summary>
        private static bool Within(RepairContext context, Vector3 at)
        {
            ZDOID villager = context.Villager.Id;
            bool arrived = !villager.IsNone() &&
                           Settled.TryGetValue(villager, out ZDOID settled) &&
                           !settled.IsNone() && settled == context.State.Target;

            float reach = arrived ? Arrival.WorkingReach : Approach.ToStructure;
            return Utils.DistanceXZ(at, context.Villager.transform.position) <= reach;
        }

        private static ZDO Record(ZDOID id)
        {
            if (id.IsNone() || ZDOMan.instance == null) return null;

            ZDO zdo = ZDOMan.instance.GetZDO(id);
            return zdo != null && zdo.IsValid() ? zdo : null;
        }

        /// <summary>Finds what an id refers to, distinguishing destroyed from merely not loaded.</summary>
        private static GameObject Resolve(ZDOID id, out bool lost)
        {
            lost = false;
            if (id.IsNone()) return null;

            ZDO zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(id) : null;
            if (zdo == null || !zdo.IsValid())
            {
                lost = true;
                return null;
            }

            return ZNetScene.instance != null ? ZNetScene.instance.FindInstance(id) : null;
        }

        /// <summary>The predicates a check may ask, rather than reimplement.</summary>
        internal static bool WouldMend(ZDO zdo, float below) => Repairable.Worn(zdo, below);

        internal static bool HasStation(ZDO zdo) => Reachable(zdo);
    }
}
