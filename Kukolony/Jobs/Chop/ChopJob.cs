using System.Collections.Generic;
using Kukolony.Colonies;
using Kukolony.Core;
using Kukolony.Resources;
using Kukolony.Villagers;
using Kukolony.Villagers.Navigation;
using UnityEngine;

namespace Kukolony.Jobs.Chop
{
    /// <summary>Everything one tick of chopping needs, gathered once.</summary>
    internal sealed class ChopContext
    {
        internal Villager Villager;
        internal Colony Colony;
        internal Container Bag;
        internal VillagerWalk Walk;
        internal VillagerAnimation Animation;
        internal JobDefinition Job;
        internal VillagerState State;

        /// <summary>
        ///     The visible equipment mirror, so the axe the villager is holding is the axe the
        ///     player can see it holding.
        /// </summary>
        internal VisEquipment Equipment;

        /// <summary>
        ///     The AI tick's own interval, for anything that moves by rate.
        /// </summary>
        /// <remarks>
        ///     Handed down rather than read from a clock, because neither available clock is
        ///     this interval: the render frame varies, and the physics step is two fifths of
        ///     it - a trap this project has already paid for once.
        /// </remarks>
        internal float DeltaTime;
    }

    /// <summary>
    ///     The doing half of chopping. The deciding half is <see cref="ChopTransitions" />,
    ///     which has no Unity in it and is tested exhaustively on its own.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Almost everything here exists to make an invisible failure loud.</b> Every way
    ///         this job breaks looks from outside like a villager standing still, and every one
    ///         of them works perfectly whenever somebody is watching. So the blow reads health
    ///         before and after, a run of blows that moves nothing gives up out loud, and the
    ///         one target a villager holds is refreshed by landed blows rather than by walking.
    ///     </para>
    ///     <para>
    ///         Nothing here orders logs before standing trees. A felled tree leaves its log
    ///         where the villager is already standing, so nearest-wins picks it up next by
    ///         itself — and the same accident handles sub-logs and stumps.
    ///     </para>
    /// </remarks>
    internal static class ChopJob
    {
        /// <summary>
        ///     How many blows may land without moving the health before the axe is judged
        ///     unable to bite.
        /// </summary>
        /// <remarks>
        ///     More than one, because the first blow at a fresh target is legitimately
        ///     discarded: a <c>TreeLog</c> is invulnerable for 0.2 s after it spawns and a
        ///     <c>Destructible</c> for its first frame. Giving up on one wasted swing would
        ///     abandon every log the moment it was felled.
        /// </remarks>
        private const int FruitlessBlowsAllowed = 4;

        /// <summary>
        ///     How long between blows.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The AI is driven at a fixed twenty ticks a second, and a step that returns
        ///         Running is asked again on the very next one - so without a cadence a
        ///         villager lands twenty blows a second. A beech falls in a fifth of a second,
        ///         the swing animation is retriggered before any of it plays, and every blow
        ///         broadcasts an animation RPC to every peer and makes the nearest player
        ///         noisy. Ten choppers would be two hundred broadcasts a second.
        ///     </para>
        ///     <para>
        ///         It also has to outlast the invulnerability a fresh target is born with,
        ///         because the give-up tolerance is counted in blows: four blows at twenty a
        ///         second is a window shorter than a new log's own 0.2 s of immunity, which
        ///         would have blacklisted every log at the moment it appeared.
        ///     </para>
        /// </remarks>
        private const float SecondsBetweenBlows = .6f;

        /// <summary>
        ///     Targets this villager has given up on, per villager, for this session.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Per session and in memory rather than on the ZDO, deliberately. The fact
        ///         being remembered is "my axe cannot cut this", which stops being true the
        ///         moment the player hands over a better axe — and a reload is exactly when
        ///         that is worth re-testing. Persisting it would make one stone-axe afternoon
        ///         permanent.
        ///     </para>
        ///     <para>
        ///         Keyed by villager as well as target, because the axe is the villager's: one
        ///         villager failing on a birch says nothing about the next one.
        ///     </para>
        /// </remarks>
        private static readonly Dictionary<ZDOID, HashSet<ZDOID>> GivenUp =
            new Dictionary<ZDOID, HashSet<ZDOID>>();

        /// <summary>
        ///     How many blows in a row have landed on nothing, and on what.
        /// </summary>
        /// <remarks>
        ///     Counted per target as well as per villager. Keyed by villager alone, a run of
        ///     fruitless blows on one tree carried onto the next thing chosen - so a villager
        ///     that had been refused three times by a birch would blacklist a perfectly good
        ///     log whose first blow happened to land inside its spawn invulnerability, and say
        ///     "needs a better axe" about it.
        /// </remarks>
        private sealed class Run
        {
            internal ZDOID Target;
            internal int Blows;
        }

        private static readonly Dictionary<ZDOID, Run> Fruitless = new Dictionary<ZDOID, Run>();

        /// <summary>
        ///     When each villager may swing again, and what the walk has settled on.
        /// </summary>
        /// <remarks>
        ///     <see cref="Settled" /> records the target a villager's walk has done its best
        ///     to reach. Working reach has to widen once a villager can get no closer - a
        ///     trunk's centre is inside the trunk - but the widening cannot be read off the
        ///     walk's stall clock the way hauling reads it, because a villager standing still
        ///     chopping is permanently "stalled" and would then fell every tree within ten
        ///     metres without taking a step.
        /// </remarks>
        private static readonly Dictionary<ZDOID, float> NextBlow = new Dictionary<ZDOID, float>();

        /// <summary>
        ///     Which area each villager is working, by its centre.
        /// </summary>
        /// <remarks>
        ///     By centre rather than by index, because the list is rebuilt every choose and an
        ///     index means a different place the moment a player reorders their areas or a flag
        ///     is destroyed - which would silently move a villager rather than lose its place.
        /// </remarks>
        private static readonly Dictionary<ZDOID, Vector3> Working = new Dictionary<ZDOID, Vector3>();

        private static readonly Dictionary<ZDOID, ZDOID> Settled = new Dictionary<ZDOID, ZDOID>();

        /// <summary>Dropped when a world unloads; none of these identities survive one.</summary>
        internal static void Clear()
        {
            Working.Clear();
            GivenUp.Clear();
            Fruitless.Clear();
            NextBlow.Clear();
            Settled.Clear();
        }

        /// <summary>
        ///     Drops what a villager that no longer exists was remembering.
        /// </summary>
        /// <remarks>
        ///     Called when a villager is removed, so a long session that hires and dismisses
        ///     does not accumulate a refusal set per dead villager plus an entry per tree each
        ///     of them ever gave up on.
        /// </remarks>
        internal static void Forget(ZDOID villager)
        {
            if (villager.IsNone()) return;

            Working.Remove(villager);
            GivenUp.Remove(villager);
            Fruitless.Remove(villager);
            NextBlow.Remove(villager);
            Settled.Remove(villager);
        }

        /// <summary>What a villager is holding, as far as can be told.</summary>
        internal static JobResult Tick(ChopContext context, out string activity)
        {
            VillagerState state = context.State;

            GameObject target = Resolve(state.Target, out bool lost);

            // Gone is not a fault here - it is what finishing looks like. A tree the villager
            // felled and a tree the player felled are the same news, and both mean the recorded
            // target is stale rather than the job broken.
            if (lost) state.ClearTarget();

            ItemDrop.ItemData axe = Axe(context);

            // Asked every tick, and cheap because the answer is cached per colony for a
            // moment rather than recounted. Gating it on the recorded state instead looked
            // like a saving and was a bug: the table reaches its choosing arm by falling
            // through from Approaching when the target is gone, and from an unrecognised
            // state - so a villager whose tree was felled by somebody else was told the
            // store was empty and went and felled another one past the stopping rule.
            bool enough = Enough(context);

            // Fetch an axe before doing anything else, as mining does. The table's answer when
            // there is no axe stays right when the settlement has none either, and the errand
            // returns null in exactly that case.
            //
            // Not when the store is already full, though: walking across the settlement for an
            // axe that is about to be put away again is the one errand nobody wants to watch.
            if (axe == null && !enough)
            {
                JobResult? fetching = ToolErrand.Run(context.Villager, context.Colony, context.Bag,
                    context.Walk, context.State, context.DeltaTime, ToolKind.Axe, out activity);

                if (fetching.HasValue) return fetching.Value;
            }

            ChopFacts facts = new ChopFacts(
                hasTool: axe != null,
                hasTarget: !state.Target.IsNone(),
                atTarget: Within(context, target),
                enough: enough,
                tired: false);

            ChopStep step = ChopTransitions.Next((ChopState)state.WorkState, facts);

            switch (step.Action)
            {
                case ChopAction.Yield:
                    return JobOutcomes.Skipped(state, WhyNothing(axe, enough), out activity);

                case ChopAction.ChooseWork:
                    return Record(state, step, Choose(context, out activity));

                case ChopAction.MoveToTarget:
                    return Record(state, step, Walk(context, target, out activity));

                case ChopAction.Chop:
                    return Record(state, step, Strike(context, target, axe, out activity));

                case ChopAction.Complete:
                    Reset(context, ZDOID.None);
                    return JobOutcomes.Completed(state, "that one is down", out activity);

                default:
                    // An action this engine does not handle is a programming error rather than
                    // a world state. Say so rather than silently idling.
                    return JobOutcomes.Failed(state, "unhandled chop action", out activity);
            }
        }

        /// <summary>
        ///     Records the next state when a step made progress.
        /// </summary>
        /// <remarks>
        ///     A step reporting Completed means <em>that step</em> finished, which is not the
        ///     job being done — so it becomes Running, and only the transition table's own
        ///     Complete ends a trip. Getting this backwards ends the cycle wherever the last
        ///     action happened to sit, and everything after it never runs.
        /// </remarks>
        private static JobResult Record(VillagerState state, ChopStep step, JobResult result)
        {
            if (result != JobResult.Running && result != JobResult.Completed) return result;
            state.SetWorkState((int)step.Next);
            return JobResult.Running;
        }

        /// <summary>
        ///     Takes the nearest choppable thing this job is allowed to take.
        /// </summary>
        /// <remarks>
        ///     Nearest to the <em>villager</em>, not to the hearth, which is the whole reason a
        ///     felled tree's log becomes the next target without a rule saying so.
        /// </remarks>
        private static JobResult Choose(ChopContext context, out string activity)
        {
            // Narrowed to what the search will actually return. Without this the invariant
            // below is merely asserted: a colony radius of 128 against the default 96 m scan,
            // or a flag set to 200, gives a work area wider than the scan that feeds it, and
            // the band between them is ground the job lists as in range and can never act on.
            List<WorkArea> areas = Areas(context.Colony, context.Job);
            List<ZDOID> candidates = ChoppingGround.Near(context.Colony);

            Vector3 here = context.Villager.transform.position;
            HashSet<ZDOID> refused = Refused(context);

            // In the job's own order: the first wood with anything left in it is the one
            // that gets worked, and the next is only looked at once this one is done. The
            // sparing rule is asked per area for the same reason - "leave ten standing" is a
            // promise about a place, and counting across two woods would let one be stripped
            // because the other is thick.
            // Every area, not any: the conservation message is only the truth when it is the
            // reason everywhere. One thick wood beside a wood of the wrong species reported
            // "that is enough felled here", which reads as the anti-clear-cut rule having
            // fired for a job that simply found nothing it wanted.
            bool allSparing = true;

            // The wood it is already in comes first, whatever the list says. The order picks
            // where to *start*; it is not a reason to walk back across the map between trees.
            //
            // Strict order re-asked every choose is how that happened: a villager working a far
            // flag would see the Kolony - first in the list - gain a single fallen branch, walk
            // the whole way home for it, and walk back. Every decision correct, the settlement
            // spending its day in transit.
            int started = Remembered(context, areas);

            for (int step = 0; step < areas.Count; step++)
            {
                WorkArea area = areas[(started + step) % areas.Count];

                JobResult chosen = ChooseIn(context, area, candidates, refused, here, out bool sparing,
                    out activity);
                if (chosen != JobResult.Skipped)
                {
                    Working[context.Villager.Id] = area.Centre;
                    return chosen;
                }

                allSparing &= sparing;
            }

            // Nothing anywhere, so nothing is being worked. Forgetting matters: a remembered
            // area that has been felled flat would otherwise be asked first for ever, and the
            // order the player wrote would never get its turn back.
            Working.Remove(context.Villager.Id);

            return JobOutcomes.Skipped(context.State,
                allSparing ? "that is enough felled here" : "nothing to chop", out activity);
        }

        /// <summary>
        ///     Takes the nearest thing worth chopping inside one area, or reports nothing here.
        /// </summary>
        /// <returns>Skipped when this area has nothing, so the caller may try the next.</returns>
        private static JobResult ChooseIn(ChopContext context, WorkArea area, List<ZDOID> candidates,
            HashSet<ZDOID> refused, Vector3 here, out bool sparingOut, out string activity)
        {
            activity = string.Empty;

            // Counted first, over the whole area, before anything is picked. "How much forest
            // is left here" is a fact about the place rather than about who is asking, so it
            // cannot be folded into the same pass that applies this villager's claims and
            // refusals - two villagers would each see a thin wood as untouched.
            // Only asked when the answer can matter. Counting is a second walk of the
            // candidate list with a ZDO lookup per entry, and the default job leaves nothing
            // standing - so for most jobs this was hundreds of lookups per choose, thrown
            // away, in the loop the lazy stopping rule above exists to keep cheap.
            int standing = context.Job != null && context.Job.LeaveStanding > 0
                ? StandingIn(context.Colony, area)
                : 0;

            // The anti-clear-cut rule removes standing trees from candidacy rather than
            // ending the search. Vetoing the winner instead meant a protected tree four
            // metres away masked a felled trunk twenty metres away: the job reported "that is
            // enough felled here" for ever and never cut up the logs it had already dropped,
            // so it stopped producing while sounding like conservation.
            bool sparing = context.Job != null && context.Job.LeaveStanding > 0 &&
                           standing <= context.Job.LeaveStanding;
            sparingOut = sparing;

            ZDOID best = ZDOID.None;
            float nearest = float.MaxValue;

            foreach (ZDOID id in candidates)
            {
                ZDO zdo = ZDOMan.instance?.GetZDO(id);
                if (zdo == null || !zdo.IsValid()) continue;

                ChopKind kind = Choppable.Of(zdo.GetPrefab());
                if (kind == ChopKind.None) continue;
                if (sparing && kind == ChopKind.Tree) continue;

                Vector3 at = zdo.GetPosition();

                // The work area narrows what the scan already returned. It cannot widen it
                // in the sense that matters - every candidate here came from inside an
                // anchor's circle, so nothing outside the search can be chosen whatever this
                // radius says. Area holds the radius to the search radius as well, which
                // keeps the two numbers from disagreeing; what it cannot do is make an area
                // centred away from any anchor cover only searched ground.
                if (!area.Contains(at)) continue;

                if (!Wanted(context.Job, kind, zdo)) continue;
                if (refused.Contains(id)) continue;
                if (Unreachable.Refuses(context.Villager.Id, id)) continue;
                if (TargetClaims.IsClaimedByOther(id, context.Villager)) continue;

                float distance = Utils.DistanceXZ(at, here);
                if (distance >= nearest) continue;

                nearest = distance;
                best = id;
            }

            // Nothing here. Reported without touching the villager's own state, because the
            // caller may yet find work in the next area and a "nothing to chop" written now
            // would be overwritten by "off to chop" a moment later - or, worse, kept.
            if (best.IsNone()) return JobResult.Skipped;

            // A new target is a new walk and a new tolerance. Without the walk being told,
            // its stall clock still holds the last target's timings and judges the first step
            // of this one as already stuck; without the tolerance being reset, a run of
            // refusals from the last target is spent against this one.
            context.Walk.Forget();
            context.Walk.NewLeg();
            Settled.Remove(context.Villager.Id);
            Reset(context, best);

            // Taking the target is also taking the claim, so no other villager walks here.
            context.State.SetTarget(best);
            activity = "off to chop";
            return JobResult.Running;
        }

        private static JobResult Walk(ChopContext context, GameObject target, out string activity)
        {
            if (target == null)
            {
                // Recorded but not instantiated. Its zone may still be streaming in, but
                // waiting forever is how the previous system hung a villager, so this yields.
                return JobOutcomes.Skipped(context.State, "waiting for the world", out activity);
            }

            // The tick's own interval, not a frame's. Left at the default the walk falls
            // back to Time.deltaTime, and a rescued villager covers ground at frame rate
            // instead of the AI rate - the trap that parameter's own doc warns about.
            switch (context.Walk.MoveTowards(target.transform.position, Approach.DistanceTo(target),
                deltaTime: context.DeltaTime))
            {
                case MoveResult.Arrived:
                    // The walk has done its best for this target, which is what licenses the
                    // wider working reach below. Recorded per target rather than inferred
                    // from a stall clock, because a villager standing still chopping stalls
                    // permanently and would inherit the widening for everything after it.
                    Settled[context.Villager.Id] = context.State.Target;
                    context.State.TouchClaim();
                    activity = "off to chop";
                    return JobResult.Running;

                case MoveResult.Moving:
                    // Bounded. Walking is the one step here that can go on saying "still
                    // going" for ever, and the queue ignores Running - so a villager that
                    // cannot get up a hillside would hold its job open and the next entry in
                    // its queue would never run.
                    ZDOID abandoned = context.State.Target;
                    JobResult? stuck = JobOutcomes.GiveUpIfStuck(context.Villager, context.State,
                        context.Walk.TripStalledFor, abandoned,
                        () => StructureRegistry.DisplayName(target), out string gaveUp);
                    if (stuck.HasValue)
                    {
                        // Refused for a while, not for the session. The session-long set means
                        // "my axe cannot cut this", which stays true until the axe changes;
                        // not being able to walk somewhere stops being true the moment the
                        // player bridges the gully, and the settlement should notice without
                        // being reloaded. Taken from the ending rather than read back off the
                        // trip, which the ending has already cleared.
                        Unreachable.Refuse(context.Villager.Id, abandoned);
                        Reset(context, ZDOID.None);
                        Settled.Remove(context.Villager.Id);
                        activity = gaveUp;
                        return stuck.Value;
                    }

                    context.State.TouchClaim();
                    activity = "off to chop";
                    return JobResult.Running;

                default:
                    // Refused briefly as well as failed, as hauling's own path failure is.
                    // Without it Choose picks the same nearest unreachable tree on the next
                    // lap and the job spends its repetitions on one thing. Short, because this
                    // verdict is reached after four seconds of no progress inside a
                    // settlement, which is as easily somebody in the way as a wall.
                    Unreachable.Refuse(context.Villager.Id, context.State.Target,
                        Unreachable.BlockedForSeconds);

                    // Says how far short it stopped and what it was asked for, because "cannot
                    // get there" is the same sentence for an unreachable target, a stop
                    // distance smaller than the thing itself, and a villager that never moved.
                    float gap = Utils.DistanceXZ(target.transform.position,
                        context.Villager.transform.position);
                    return JobOutcomes.Failed(context.State,
                        $"cannot get to it (stopped {gap:0.0}m away, needed " +
                        $"{Approach.DistanceTo(target):0.0}m, " +
                        $"{context.Villager.Explain(target.transform.position)})", out activity);
            }
        }

        /// <summary>
        ///     Lands one blow and reads what it achieved.
        /// </summary>
        /// <remarks>
        ///     The blow is struck from where the villager stands, which is also the direction a
        ///     felled trunk is pushed — so the log goes away from the chopper rather than
        ///     through it.
        /// </remarks>
        private static JobResult Strike(ChopContext context, GameObject target,
            ItemDrop.ItemData axe, out string activity)
        {
            if (target == null)
            {
                context.State.ClearTarget();
                return JobOutcomes.Running("it was gone", out activity);
            }

            // Turned to face it, because work done standing still has nothing else to turn it:
            // a villager that arrived and then stood chopping would swing at whatever bearing
            // it happened to stop on. Kept up between blows rather than only at the moment of
            // one, so a trunk that rolls does not leave the villager chopping past it.
            context.Villager.FaceTowards(target.transform.position, context.DeltaTime);

            // The walk is over. Said plainly so its stall clock stops running against a
            // villager that is standing still on purpose - which is what made the working
            // reach widen for everything it chose afterwards.
            context.Walk.Forget();

            ZDOID chopper = context.Villager.Id;
            if (!chopper.IsNone())
            {
                if (NextBlow.TryGetValue(chopper, out float when) && Time.time < when)
                {
                    // Between blows. Not a swing, and deliberately not animated: retriggering
                    // the animation every tick is how the swing never plays at all.
                    return JobOutcomes.Running("chopping", out activity);
                }

                NextBlow[chopper] = Time.time + SecondsBetweenBlows;
            }

            BlowResult blow = Felling.Strike(target, axe,
                context.Villager.transform.position, out string what);

            switch (blow)
            {
                case BlowResult.Claiming:
                    // Ownership is being taken; the blow lands on a later tick. Not a swing,
                    // so not animated - a villager miming a chop that did nothing is exactly
                    // the lie this job is built to avoid.
                    return JobOutcomes.Running(what, out activity);

                case BlowResult.Struck:
                    context.Animation.Swing();

                    // A landed blow is progress, and it is the only progress this job makes
                    // while standing still. Without this the claim ages against the length of
                    // the tree rather than against being stuck, and any trunk outlasting the
                    // TTL loses its claim halfway - which is two villagers on one trunk,
                    // arrived at by both of them behaving correctly.
                    context.State.TouchClaim();
                    Landed(context);
                    return JobOutcomes.Running(what, out activity);

                case BlowResult.Felled:
                    context.Animation.Swing();
                    Reset(context, ZDOID.None);

                    // The target is gone, so the recorded one is stale. Releasing it here lets
                    // the next choose take the log it just left - which is nearest, because
                    // the villager is standing in it.
                    context.State.ClearTarget();
                    return JobOutcomes.Running(what, out activity);

                case BlowResult.TooHard:
                    return GiveUp(context, what, out activity);

                default:
                    // Not something this knows how to hit. The classifier said otherwise when
                    // the target was chosen, so the world changed underneath - release it and
                    // choose again rather than swinging at it.
                    Reset(context, ZDOID.None);
                    context.State.ClearTarget();
                    return JobOutcomes.Running(what, out activity);
            }
        }

        /// <summary>
        ///     Counts a landed blow, which clears the fruitless run.
        /// </summary>
        private static void Landed(ChopContext context) => Reset(context, context.State.Target);

        /// <summary>Starts the tolerance over, against a named target.</summary>
        private static void Reset(ChopContext context, ZDOID target)
        {
            ZDOID villager = context.Villager.Id;
            if (villager.IsNone()) return;

            Fruitless[villager] = new Run { Target = target, Blows = 0 };
        }

        /// <summary>
        ///     A blow that changed nothing. Said once and given up on, not swung at forever.
        /// </summary>
        /// <remarks>
        ///     Tolerating a few first, because a fresh log is briefly invulnerable and a
        ///     <c>Destructible</c> discards its first frame - giving up on one wasted swing
        ///     would abandon every log at the moment it was felled. Past that the axe
        ///     demonstrably cannot bite, and swinging at it for the rest of the session is the
        ///     failure that looks most like working.
        /// </remarks>
        private static JobResult GiveUp(ChopContext context, string what, out string activity)
        {
            ZDOID villager = context.Villager.Id;
            ZDOID target = context.State.Target;

            int run = FruitlessBlowsAllowed;
            if (!villager.IsNone())
            {
                // Counted against this target. A run inherited from the last thing this
                // villager swung at would be spent here, so a log whose first blow fell
                // inside its spawn invulnerability could be blacklisted on arrival.
                if (!Fruitless.TryGetValue(villager, out Run had) || had.Target != target)
                {
                    had = new Run { Target = target, Blows = 0 };
                    Fruitless[villager] = had;
                }

                had.Blows++;
                run = had.Blows;
            }

            if (run < FruitlessBlowsAllowed)
            {
                // Still inside the tolerance. Swing again rather than announcing a problem the
                // next blow may disprove.
                context.Animation.Swing();
                return JobOutcomes.Running("chopping", out activity);
            }

            if (!target.IsNone()) Refused(context).Add(target);
            Reset(context, ZDOID.None);

            // Keyed by the villager, so two villagers with two bad axes are two complaints
            // rather than one confusing tally.
            Chatter.Say($"cannot chop {villager}",
                $"{context.Villager.State.Name} cannot cut that - it needs a better axe.");

            context.State.ClearTarget();
            return JobOutcomes.Skipped(context.State, what, out activity);
        }

        private static HashSet<ZDOID> Refused(ChopContext context)
        {
            ZDOID villager = context.Villager.Id;
            if (villager.IsNone()) return new HashSet<ZDOID>();

            if (!GivenUp.TryGetValue(villager, out HashSet<ZDOID> refused))
            {
                refused = new HashSet<ZDOID>();
                GivenUp[villager] = refused;
            }

            return refused;
        }

        /// <summary>
        ///     Whether this job would take this thing, asked from outside.
        /// </summary>
        /// <remarks>
        ///     The same predicate the choosing uses, exposed rather than reimplemented. A
        ///     check that called its own copy of this would go on passing while the job
        ///     quietly ignored the setting - which is exactly how a feature that never ran in
        ///     game survived a suite that counted it as covered.
        /// </remarks>
        internal static bool WouldTake(JobDefinition job, ZDO zdo) =>
            zdo != null && zdo.IsValid() && Wanted(job, Choppable.Of(zdo.GetPrefab()), zdo);

        /// <summary>
        ///     Whether this job's stopping rule says the settlement has enough, asked from
        ///     outside. The same predicate <see cref="Tick" /> consults when choosing.
        /// </summary>
        internal static bool WouldStop(Colony colony, JobDefinition job) => HasEnough(colony, job);

        /// <summary>
        ///     Whether this job would spare the standing trees in an area, asked from outside.
        /// </summary>
        /// <remarks>
        ///     Counts the same way <see cref="Choose" /> counts - over the place, before any
        ///     of the asking villager's own filters - so a check cannot agree with a count the
        ///     job does not make.
        /// </remarks>
        internal static bool WouldSpare(Colony colony, JobDefinition job, Vector3 centre, float radius)
        {
            if (job == null || job.LeaveStanding <= 0) return false;

            return StandingIn(colony, new WorkArea(centre, radius, "there")
                .NoWiderThan(ChoppingGround.SearchRadius)) <= job.LeaveStanding;
        }

        /// <summary>
        ///     Whether this job takes a thing of this kind and this species.
        /// </summary>
        /// <remarks>
        ///     The species list mirrors hauling's item list exactly, including that an empty
        ///     list means everything. It is matched against the prefab name because that is
        ///     what a player picks from a list of what the world actually contains - the mod
        ///     does not ship the assets and cannot name a tree it has not been shown.
        /// </remarks>
        private static bool Wanted(JobDefinition job, ChopKind kind, ZDO zdo)
        {
            if (job == null) return kind == ChopKind.Tree || kind == ChopKind.Log;

            switch (kind)
            {
                case ChopKind.Tree: if (!job.ChopTrees) return false; break;
                case ChopKind.Log: if (!job.ChopLogs) return false; break;
                case ChopKind.Undergrowth: if (!job.ChopUndergrowth) return false; break;
                default: return false;
            }

            if (job.Species == null || job.Species.Count == 0) return true;

            // Species names a tree, so it bounds standing trees. A log is what a felled tree
            // left behind and is already the consequence of an allowed choice - filtering it
            // by species again would strand the trunk of every tree the villager just felled,
            // because a log's prefab is not its tree's.
            if (kind != ChopKind.Tree) return true;

            string prefab = PrefabName(zdo);
            return prefab.Length > 0 && job.Species.Contains(prefab);
        }

        /// <summary>
        ///     Where this job works, held to what the search can reach.
        /// </summary>
        /// <remarks>
        ///     The one place chopping decides its own ground, so the screen and the scan are
        ///     told the same answer instead of each working one out - which is how three
        ///     rounds of review found them disagreeing in three different shapes.
        /// </remarks>
        internal static WorkArea Area(Colony colony, JobDefinition job) =>
            WorkArea.For(colony, job).NoWiderThan(ChoppingGround.SearchRadius);

        /// <summary>
        ///     Where in the list to start looking: the area this villager is already working,
        ///     or the top.
        /// </summary>
        private static int Remembered(ChopContext context, List<WorkArea> areas)
        {
            if (!Working.TryGetValue(context.Villager.Id, out Vector3 centre)) return 0;

            for (int i = 0; i < areas.Count; i++)
            {
                // The same place rather than the same numbers: an area's radius can be edited
                // between one tree and the next without it becoming somewhere else.
                if (Utils.DistanceXZ(areas[i].Centre, centre) <= 1f) return i;
            }

            // The place it was working is not on the list any more - unregistered, or the job
            // was re-pointed while it worked. Start from the top, which is what the player
            // asked for.
            Working.Remove(context.Villager.Id);
            return 0;
        }

        /// <summary>Every place this job works, each held to what the search can reach.</summary>
        internal static List<WorkArea> Areas(Colony colony, JobDefinition job)
        {
            List<WorkArea> areas = new List<WorkArea>();
            WorkArea.AllFor(colony, job, areas);

            for (int i = 0; i < areas.Count; i++)
            {
                areas[i] = areas[i].NoWiderThan(ChoppingGround.SearchRadius);
            }

            return areas;
        }

        /// <summary>How many standing trees an area still has.</summary>
        private static int StandingIn(Colony colony, WorkArea area)
        {
            int standing = 0;
            foreach (ZDOID id in ChoppingGround.Near(colony))
            {
                ZDO zdo = ZDOMan.instance?.GetZDO(id);
                if (zdo == null || !zdo.IsValid()) continue;
                if (Choppable.Of(zdo.GetPrefab()) != ChopKind.Tree) continue;
                if (!area.Contains(zdo.GetPosition())) continue;

                standing++;
            }

            return standing;
        }

        private static string PrefabName(ZDO zdo)
        {
            GameObject prefab = ZNetScene.instance?.GetPrefab(zdo.GetPrefab());
            return prefab == null ? string.Empty : prefab.name;
        }

        /// <summary>
        ///     Whether the settlement already holds as much as this job was asked to gather.
        /// </summary>
        /// <remarks>
        ///     The terminus the job otherwise lacks. Hauling stops when nothing is misplaced,
        ///     which is visible and self-limiting; a forest has no such point, and a woodcutter
        ///     without a stopping rule strips the map while looking correct the whole time.
        /// </remarks>
        private static bool Enough(ChopContext context) => HasEnough(context.Colony, context.Job);

        private static bool HasEnough(Colony colony, JobDefinition job)
        {
            if (job == null || job.StockTarget <= 0 || string.IsNullOrEmpty(job.StockItem))
            {
                return false;
            }

            return Stock.Held(colony, job.StockItem) >= job.StockTarget;
        }

        /// <summary>
        ///     Why there is nothing to do, said in the villager's own terms.
        /// </summary>
        /// <remarks>
        ///     Three different reasons reach one Yield, and "nothing to chop" for all of them
        ///     is how a player comes to believe the job is broken when it is waiting on an axe.
        /// </remarks>
        private static string WhyNothing(ItemDrop.ItemData axe, bool enough)
        {
            if (axe == null) return "I have no axe";
            if (enough) return "we have enough of that";
            return "nothing to chop";
        }

        /// <summary>
        ///     The axe this villager is working with, and the one it is shown holding.
        /// </summary>
        /// <remarks>
        ///     Found in the bag rather than in the creature's own inventory, which is where
        ///     everything a villager owns lives - the routine that equips a creature's best
        ///     weapon on load would strip anything placed there. The visible right hand is
        ///     written to match, so a chopping villager is seen to be holding an axe.
        /// </remarks>
        private static ItemDrop.ItemData Axe(ChopContext context)
        {
            ItemDrop.ItemData best = VillagerTool.Best(
                context.Bag != null ? context.Bag.GetInventory() : null, ToolKind.Axe);

            VillagerTool.Show(context.Equipment, context.Animation,
                context.Villager != null && context.Villager.TryGetComponent(out ZNetView view) &&
                view.IsValid() ? view.GetZDO() : null, best);
            return best;
        }

        /// <summary>
        ///     Whether the villager is close enough to work on this.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The same measure the walking used, from the same place. When arriving and
        ///         having arrived are two different numbers, a villager walks as far as it can,
        ///         is told it is not there yet, and tries again forever.
        ///     </para>
        ///     <para>
        ///         Widened once the walk has reported arriving at <em>this</em> target, because
        ///         a trunk's centre is inside the trunk and demanding it is demanding a
        ///         position no villager can stand in. Hauling reads the same widening off the
        ///         walk's stall clock, which cannot work here: a villager standing still
        ///         chopping is stalled by definition, so after its first tree every later
        ///         target within the wider reach would be chopped from wherever it happened to
        ///         be standing, without a step.
        ///     </para>
        /// </remarks>
        private static bool Within(ChopContext context, GameObject thing)
        {
            if (thing == null) return false;

            ZDOID villager = context.Villager.Id;
            bool arrived = !villager.IsNone() &&
                           Settled.TryGetValue(villager, out ZDOID settled) &&
                           !settled.IsNone() && settled == context.State.Target;

            float reach = arrived ? Arrival.WorkingReach : Approach.DistanceTo(thing);

            return Utils.DistanceXZ(thing.transform.position, context.Villager.transform.position) <= reach;
        }

        /// <summary>
        ///     Finds what an id refers to, distinguishing destroyed from merely not loaded.
        /// </summary>
        private static GameObject Resolve(ZDOID id, out bool lost)
        {
            lost = false;
            if (id.IsNone()) return null;

            ZDO zdo = ZDOMan.instance?.GetZDO(id);
            if (zdo == null || !zdo.IsValid())
            {
                lost = true;
                return null;
            }

            return ZNetScene.instance?.FindInstance(id);
        }
    }
}
