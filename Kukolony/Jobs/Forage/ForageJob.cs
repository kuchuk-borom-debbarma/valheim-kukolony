using System.Collections.Generic;
using Kukolony.Colonies;
using Kukolony.Core;
using Kukolony.Resources;
using Kukolony.Resources.Foraging;
using Kukolony.Villagers;
using Kukolony.Villagers.Navigation;
using UnityEngine;

namespace Kukolony.Jobs.Forage
{
    /// <summary>What one foraging tick needs. Assembled by the villager, never stored.</summary>
    internal sealed class ForageContext
    {
        internal Villager Villager;
        internal Colony Colony;
        internal Container Bag;
        internal VillagerWalk Walk;
        internal VillagerAnimation Animation;
        internal JobDefinition Job;
        internal VillagerState State;
        internal float DeltaTime;
    }

    /// <summary>
    ///     Picking what is there to be picked: berries, mushrooms, herbs, and a farm at harvest.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>The simplest of the gathering jobs, and deliberately.</b> No tool, so no
    ///         fetching one and no tier to be too weak for. No parts, so no sub-target. No
    ///         health, so nothing to read back but a single flag. What is left is the walk and
    ///         the reach, which is what foraging is.
    ///     </para>
    ///     <para>
    ///         <b>The target outlives the work.</b> This is the one thing it does not share with
    ///         chopping and mining: a picked bush is still a bush, standing where it was, and it
    ///         grows its berries back. So the job asks "is there anything on it" rather than "is
    ///         it still there", and the difference runs from the table through the choosing loop
    ///         into what the villager says when it finishes.
    ///     </para>
    ///     <para>
    ///         <b>Ripeness is asked of the record, not of the object.</b> A stripped clearing is
    ///         a clearing full of candidates, so the loop that chooses would otherwise pay a
    ///         scene lookup and a <c>GetComponent</c> per bush per tick - which is precisely the
    ///         cost that dropped the frame rate when mining's loose rock was switched on. The
    ///         picked flag lives on the ZDO, so the loop asks the ZDO.
    ///     </para>
    /// </remarks>
    internal static class ForageJob
    {
        /// <summary>
        ///     How long between reaches.
        /// </summary>
        /// <remarks>
        ///     The same reason chopping and mining wait: a step reporting Running is re-asked at
        ///     20 Hz, so without this a villager would reach for one bush twenty times in the
        ///     moment before the picked flag comes back, and broadcast a pick effect for each.
        /// </remarks>
        private const float SecondsBetweenReaches = .8f;

        /// <summary>
        ///     How many reaches that change nothing before a bush is set aside.
        /// </summary>
        /// <remarks>
        ///     Mining's number, for a version of mining's reason. The picked flag comes back as a
        ///     routed call, so a reach that overlaps the answer looks exactly like one that was
        ///     refused; acting on the first of those writes off a perfectly good bush.
        /// </remarks>
        private const int BarrenReachesAllowed = 4;

        private static readonly Dictionary<ZDOID, float> NextReach = new Dictionary<ZDOID, float>();

        /// <summary>
        ///     The thing each villager's walk has done its best at.
        /// </summary>
        /// <remarks>
        ///     The same latch chopping and mining keep, and for the same reason: picking calls
        ///     <c>Walk.Forget()</c> every tick, which zeroes the stall clock the working reach
        ///     would otherwise be read from - so the reach would collapse the moment a villager
        ///     arrived, and it would shuffle back and forth in front of a bush for ever.
        /// </remarks>
        private static readonly Dictionary<ZDOID, ZDOID> Settled = new Dictionary<ZDOID, ZDOID>();

        /// <summary>Reaches that changed nothing, and the thing they were spent on.</summary>
        /// <remarks>
        ///     The thing is half the key, and has to be: a count kept per villager alone is spent
        ///     against whatever that villager picks up next, so three refusals here would write
        ///     off the first good bush after them. Mining keys its own count this way for exactly
        ///     that reason.
        /// </remarks>
        private static readonly Dictionary<ZDOID, Reaches> Barren = new Dictionary<ZDOID, Reaches>();

        private struct Reaches
        {
            internal ZDOID On;
            internal int Count;
        }

        /// <summary>Dropped when a world unloads; none of these identities survive one.</summary>
        internal static void Clear()
        {
            NextReach.Clear();
            Settled.Clear();
            Barren.Clear();
        }

        /// <summary>Drops what a villager that no longer exists was holding.</summary>
        internal static void Forget(ZDOID villager)
        {
            if (villager.IsNone()) return;

            NextReach.Remove(villager);
            Settled.Remove(villager);
            Barren.Remove(villager);
        }

        internal static JobResult Tick(ForageContext context, out string activity)
        {
            VillagerState state = context.State;

            GameObject target = Resolve(state.Target, out bool lost);

            // Gone is not a fault. Some pickable things are destroyed rather than emptied when
            // they are taken, so this is one of the two ways finishing looks.
            if (lost) state.ClearTarget();

            Pickable held = target != null && Harvest.TryFind(target, out Pickable found) ? found : null;

            // Ripe by the record and bare in fact. Rare - it means whatever gates the thing is
            // switched off, or its visible half is inactive - and it is the one shape here that
            // loops for ever. The choosing loop reads the record, so it would pick this again,
            // walk to it, find nothing on it, go back to choosing and pick the same one: a
            // villager shuttling back and forth, spending no repetition, with its queue unable
            // to advance past a job that never ends. Set aside for a while rather than for good,
            // because whatever is closing the gate can open it.
            //
            // The ordinary case - a bush this villager or somebody else just picked - never
            // reaches here, because picking writes the record and the loop reads it.
            if (held != null && !state.Target.IsNone() && !Harvest.Ripe(held) &&
                Harvest.RecordSaysRipe(Record(held)))
            {
                Unreachable.Refuse(context.Villager.Id, state.Target);
            }

            bool enough = Enough(context);

            ForageFacts facts = new ForageFacts(
                hasTarget: !state.Target.IsNone() && held != null,
                ripe: held != null && Harvest.Ripe(held),
                atTarget: held != null && Within(context, held.transform.position),
                enough: enough,
                tired: false);

            ForageStep step = ForageTransitions.Next((ForageState)state.WorkState, facts);

            switch (step.Action)
            {
                case ForageAction.Yield:
                    return JobOutcomes.Skipped(state, enough ? "we have enough" : "nothing to gather",
                        out activity);

                case ForageAction.ChooseWork:
                    return Record(state, step, Choose(context, out activity));

                case ForageAction.MoveToTarget:
                    return Record(state, step, Walk(context, held.transform.position, out activity));

                case ForageAction.Pick:
                    return Record(state, step, Pick(context, held, out activity));

                case ForageAction.Complete:
                    Release(context);
                    return JobOutcomes.Completed(state, "picked", out activity);

                default:
                    // An action this engine does not handle is a programming error rather than a
                    // world state. Say so rather than silently idling.
                    return JobOutcomes.Failed(state, "unhandled forage action", out activity);
            }
        }

        /// <summary>
        ///     Records the next state when a step made progress.
        /// </summary>
        /// <remarks>
        ///     A step reporting Completed means <em>that step</em> finished, which is not the job
        ///     being done - so it becomes Running, and only the table's own Complete ends a trip.
        /// </remarks>
        private static JobResult Record(VillagerState state, ForageStep step, JobResult result)
        {
            if (result != JobResult.Running && result != JobResult.Completed) return result;

            state.SetWorkState((int)step.Next);
            return JobResult.Running;
        }

        /// <summary>Takes the nearest thing worth picking, in the job's own order of places.</summary>
        private static JobResult Choose(ForageContext context, out string activity)
        {
            List<WorkArea> areas = Areas(context.Colony, context.Job, context.Villager);
            List<ZDOID> candidates = ForagingGround.Near(context.Colony);
            Vector3 here = context.Villager.transform.position;

            // Counted while choosing, so "nothing to gather" can say which nothing it means. A
            // villager standing in a berry patch being told there is nothing to gather is a fair
            // question, and the answer - they are bare, or they are the wrong berry, or the job
            // was told to leave what does not grow back - is one the job already knows.
            Tally tally = new Tally();

            foreach (WorkArea area in areas)
            {
                ZDOID best = Nearest(context, area, candidates, here, tally);
                if (best.IsNone()) continue;

                // A new target is a new walk and a new tolerance. Without the walk being told,
                // its stall clock still holds the last target's timings and judges the first
                // step of this one as already stuck.
                context.Walk.Forget();
                context.Walk.NewLeg();

                // The latch belongs to the thing that was walked to, so a new one starts
                // un-arrived. Without this a villager re-choosing something it had reached
                // earlier is granted the wider working reach from the first tick and picks it
                // from ten metres away, never walking in.
                Settled.Remove(context.Villager.Id);
                context.State.SetTarget(best);

                activity = "off to gather";
                return JobResult.Running;
            }

            return JobOutcomes.Skipped(context.State, tally.Say(candidates.Count), out activity);
        }

        /// <summary>
        ///     Why nothing was chosen, counted as the choosing happens.
        /// </summary>
        /// <remarks>
        ///     Every count here is a question a player will ask out loud the first time a
        ///     villager stands in a raspberry patch saying it has nothing to do. The job knows
        ///     all of them, and saying "nothing to gather" to all of them is how a setting that
        ///     is doing exactly what it was told looks like a bug.
        /// </remarks>
        private sealed class Tally
        {
            internal int Outside;
            internal int Bare;
            internal int Finite;
            internal int Other;
            internal int Refused;
            internal int Taken;

            internal string Say(int seen)
            {
                if (seen == 0) return "nothing growing nearby";

                // In the order a player can act on. The two settings first, because each is one
                // switch and each is the answer whenever somebody is looking straight at the
                // thing they expected to be picked.
                if (Finite > 0) return $"only things that do not grow back here ({Finite}) - switch that on to take them";
                if (Other > 0) return $"nothing here gives what this job gathers ({Other} other)";
                if (Outside > 0) return $"it is outside where this job works ({Outside})";
                if (Bare > 0) return $"it has all been picked already ({Bare})";
                if (Taken > 0) return "somebody else is gathering it";
                if (Refused > 0) return "cannot get to it";

                return "nothing to gather";
            }
        }

        /// <summary>
        ///     The nearest thing in an area that is worth walking to.
        /// </summary>
        /// <remarks>
        ///     Every question here is answered from the record - the prefab hash for what it is
        ///     and what it yields, one bool for whether anything is on it - so choosing costs no
        ///     scene lookups at all. That matters more here than anywhere else: a forager strips
        ///     a clearing and then every candidate in it is a bush that has to be rejected, every
        ///     tick, for as long as the villager stands there.
        /// </remarks>
        private static ZDOID Nearest(ForageContext context, WorkArea area, List<ZDOID> candidates,
            Vector3 here, Tally tally)
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

                if (!Wanted(context.Job, zdo.GetPrefab(), tally)) continue;

                if (!Harvest.RecordSaysRipe(zdo))
                {
                    tally.Bare++;
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
        ///     Whether this job picks this prefab: the right kind, and the right harvest.
        /// </summary>
        /// <remarks>
        ///     <b>Nothing is opt-in here, and that is not an oversight.</b> Chopping and mining
        ///     each gate a tail of scenery they cannot tell from real work, because the game has
        ///     no way to say whether a <c>Destructible</c> is a stump or a wagon. <c>Pickable</c>
        ///     carries no such doubt: it exists to be walked up to and taken. What the setting
        ///     here governs instead is what happens <em>after</em> - whether the job takes things
        ///     that never come back.
        /// </remarks>
        private static bool Wanted(JobDefinition job, int prefabHash, Tally tally = null)
        {
            switch (Forageable.Of(prefabHash))
            {
                case ForageKind.Regrows:
                    break;

                case ForageKind.Once:
                    if (job != null && job.ForageRegrowingOnly)
                    {
                        if (tally != null) tally.Finite++;
                        return false;
                    }

                    break;

                default:
                    return false;
            }

            if (Forageable.DropsAny(prefabHash, job?.Harvest)) return true;

            if (tally != null) tally.Other++;
            return false;
        }

        private static JobResult Pick(ForageContext context, Pickable held, out string activity)
        {
            if (held == null)
            {
                context.State.ClearTarget();
                return JobOutcomes.Running("it was gone", out activity);
            }

            Vector3 at = held.transform.position;

            // Turned to face it, because work done standing still has nothing else to turn it.
            context.Villager.FaceTowards(at, context.DeltaTime);

            // The walk is over. Said plainly so its stall clock stops running against a villager
            // that is standing still on purpose.
            context.Walk.Forget();

            ZDOID picker = context.Villager.Id;
            if (!picker.IsNone())
            {
                if (NextReach.TryGetValue(picker, out float when) && Time.time < when)
                {
                    // Between reaches. Not a reach, and deliberately not animated: retriggering
                    // the animation every tick is how it never plays at all.
                    return JobOutcomes.Running("gathering", out activity);
                }

                NextReach[picker] = Time.time + SecondsBetweenReaches;
            }

            switch (Harvest.Take(held, out string what))
            {
                case PickResult.Claiming:
                    // Ownership is being taken; the reach lands on a later tick. Not animated -
                    // a villager miming a pick that did nothing is exactly the thing that makes
                    // a broken job look like a working one.
                    return JobOutcomes.Running(what, out activity);

                case PickResult.Picked:
                    // Progress, so the claim is refreshed here rather than only on arrival.
                    context.State.TouchClaim();
                    Barren.Remove(picker);
                    context.Animation?.Reach();
                    return JobOutcomes.Running(what, out activity);

                case PickResult.NoEffect:
                    return Refused(context, picker, what, out activity);

                case PickResult.Bare:
                    // Emptied between arriving and reaching. The table's own Complete arm runs
                    // next tick and ends the trip; nothing to do here but say so.
                    return JobOutcomes.Running(what, out activity);

                default:
                    Release(context);
                    return JobOutcomes.Running(what, out activity);
            }
        }

        /// <summary>
        ///     A reach that changed nothing, and what to make of it.
        /// </summary>
        /// <remarks>
        ///     <b>Not on the first one.</b> The picked flag travels back as a routed call, so a
        ///     reach landing in the same moment as the answer is indistinguishable here from one
        ///     that was refused outright. And when it is a verdict it is said once and the thing
        ///     is set aside for a while rather than for good, because whatever was blocking it -
        ///     the component names tar underfoot - can stop being true.
        /// </remarks>
        private static JobResult Refused(ForageContext context, ZDOID picker, string what,
            out string activity)
        {
            if (picker.IsNone()) return JobOutcomes.Running(what, out activity);

            ZDOID on = context.State.Target;
            Barren.TryGetValue(picker, out Reaches spent);

            // A count against something else says nothing about this one.
            int reaches = spent.On == on ? spent.Count + 1 : 1;
            Barren[picker] = new Reaches { On = on, Count = reaches };

            if (reaches < BarrenReachesAllowed) return JobOutcomes.Running(what, out activity);

            Chatter.Say($"[forage] will not come loose: {on}",
                $"{context.State.Name} cannot pick that.");

            Unreachable.Refuse(context.Villager.Id, on);
            Release(context);

            // Skipped rather than completed: releasing the target sends the table to its
            // Complete arm next tick, which would report something never picked as finished and
            // spend a repetition on it.
            return JobOutcomes.Skipped(context.State, "cannot pick that", out activity);
        }

        /// <summary>The record behind a pickable, or null when it has none.</summary>
        private static ZDO Record(Pickable held)
        {
            ZNetView view = Harvest.ViewOf(held);
            return view != null && view.IsValid() ? view.GetZDO() : null;
        }

        private static void Release(ForageContext context)
        {
            context.State.ClearTarget();
            Forget(context.Villager.Id);
        }

        /// <summary>
        ///     Whether the settlement already holds as much as this job was asked to gather.
        /// </summary>
        /// <remarks>
        ///     Counted in registered storage only, so berries still lying where they fell do not
        ///     count towards the target - which is right, because a settlement does not have what
        ///     nobody has carried home.
        /// </remarks>
        private static bool Enough(ForageContext context) => HasEnough(context.Colony, context.Job);

        private static bool HasEnough(Colony colony, JobDefinition job)
        {
            if (job == null || job.StockTarget <= 0 || string.IsNullOrEmpty(job.StockItem)) return false;

            return Stock.Held(colony, job.StockItem) >= job.StockTarget;
        }

        /// <summary>
        ///     The predicates a check may ask, rather than reimplement.
        /// </summary>
        /// <remarks>
        ///     The same arrangement chopping and mining expose, and for the reason they record: a
        ///     check that writes out the rule again passes while the job quietly ignores the
        ///     setting. Asked of a prefab hash rather than of an object, which is what lets a
        ///     settings check assert things it never has to spawn.
        /// </remarks>
        internal static bool WouldTake(JobDefinition job, int prefabHash) => Wanted(job, prefabHash);

        internal static bool WouldStop(Colony colony, JobDefinition job) => HasEnough(colony, job);

        private static List<WorkArea> Areas(Colony colony, JobDefinition job, Villager villager)
        {
            List<WorkArea> areas = new List<WorkArea>();
            WorkArea.AllFor(colony, job, villager, areas);

            // Narrowed to what the sweep will actually return, for the reason chopping gives: a
            // work area wider than the scan that feeds it is a band of ground the job lists as
            // in range and can never act on.
            for (int i = 0; i < areas.Count; i++) areas[i] = areas[i].NoWiderThan(ForagingGround.SearchRadius);

            return areas;
        }

        private static JobResult Walk(ForageContext context, Vector3 to, out string activity)
        {
            switch (context.Walk.MoveTowards(to, Approach.ToStructure, deltaTime: context.DeltaTime))
            {
                case MoveResult.Arrived:
                    context.State.TouchClaim();

                    // Latched here, and only here. This is the walk saying it has got as close as
                    // it is going to; everything after it works from that answer rather than from
                    // a clock the picking keeps resetting.
                    if (!context.Villager.Id.IsNone()) Settled[context.Villager.Id] = context.State.Target;

                    return JobOutcomes.Running("off to gather", out activity);

                case MoveResult.PathFailed:
                    Unreachable.Refuse(context.Villager.Id, context.State.Target,
                        Unreachable.BlockedForSeconds);
                    Release(context);
                    return JobOutcomes.Skipped(context.State, "cannot get there", out activity);

                default:
                    // The claim is refreshed while walking, not only on arrival: the sweep reaches
                    // ninety-six metres and a claim lives thirty seconds, so a long approach would
                    // lose the bush to somebody else before getting there.
                    context.State.TouchClaim();

                    // And bounded. Every other walking job bounds this arm, because a trip that
                    // can never arrive otherwise returns Running for ever - which consumes no
                    // repetition, so the queue never advances and every later entry in that
                    // villager's queue stops running too.
                    ZDOID abandoned = context.State.Target;
                    JobResult? stuck = JobOutcomes.GiveUpIfStuck(context.Villager, context.State,
                        context.Walk.TripStalledFor, abandoned,
                        () => "something to pick", out string gaveUp);

                    if (stuck.HasValue)
                    {
                        // Refused for a while rather than for good: not being able to walk
                        // somewhere stops being true the moment a path opens, and the settlement
                        // should notice without being reloaded.
                        Unreachable.Refuse(context.Villager.Id, abandoned);
                        Release(context);
                        activity = gaveUp;
                        return stuck.Value;
                    }

                    return JobOutcomes.Running("off to gather", out activity);
            }
        }

        /// <summary>
        ///     Whether the villager is close enough to reach it.
        /// </summary>
        /// <remarks>
        ///     The wider reach applies once the walk has done its best at <em>this thing</em>,
        ///     recorded per target rather than read off the stall clock - picking resets that
        ///     clock every tick, so reading it would make the reach collapse after every pick.
        /// </remarks>
        private static bool Within(ForageContext context, Vector3 at)
        {
            ZDOID villager = context.Villager.Id;
            bool arrived = !villager.IsNone() &&
                           Settled.TryGetValue(villager, out ZDOID settled) &&
                           !settled.IsNone() && settled == context.State.Target;

            float reach = arrived ? Arrival.WorkingReach : Approach.ToStructure;
            return Utils.DistanceXZ(at, context.Villager.transform.position) <= reach;
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
    }
}
