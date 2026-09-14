using System.Collections.Generic;
using Kukolony.Colonies;
using Kukolony.Core;
using Kukolony.Gui;
using Kukolony.Resources;
using Kukolony.Resources.Mining;
using Kukolony.Villagers;
using Kukolony.Villagers.Navigation;
using UnityEngine;

namespace Kukolony.Jobs.Mine
{
    /// <summary>What one mining tick needs. Assembled by the villager, never stored.</summary>
    internal sealed class MineContext
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
    ///     Breaking rock, and everything a deposit being made of parts implies.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Chopping's shape with one difference that runs through all of it: the target is a
    ///         deposit and the <em>work</em> is a part of it. A tree is one thing with one
    ///         health; a silver vein is forty rocks wearing one name, each with its own health
    ///         and its own drops, and the object survives until the last of them is gone.
    ///     </para>
    ///     <para>
    ///         <b>The part is never remembered.</b> Mining collapses - a deposit kills its own
    ///         unsupported parts with a synthetic structural hit, several at a time - so the part
    ///         being worked is re-chosen from what is standing on every tick. Anything that held
    ///         an opinion about which rock it was hitting would be wrong within seconds, and
    ///         wrong in the way that looks like working.
    ///     </para>
    ///     <para>
    ///         <b>The claim is the deposit.</b> One villager per vein: two on one is a wasted
    ///         walk while another stands untouched, and the claim already works per object.
    ///     </para>
    /// </remarks>
    internal static class MineJob
    {
        /// <summary>
        ///     How long between blows.
        /// </summary>
        /// <remarks>
        ///     The same reason chopping waits: a step reporting Running is re-asked at 20 Hz, so
        ///     without this a villager empties a vein in a second and broadcasts a hit effect to
        ///     every peer for each part of it.
        /// </remarks>
        private const float SecondsBetweenBlows = .9f;

        /// <summary>How many fruitless blows before a deposit is written off.</summary>
        /// <remarks>
        ///     Chopping's number, for chopping's reasons. A blow can land and move nothing for
        ///     causes that are not "this tool cannot do it": a Destructible ignores damage
        ///     during its first frame, and a peer that has just taken ownership can be holding a
        ///     collider set up to ten seconds stale and aim at a part that is already gone.
        ///     Acting on the first of those is how a working deposit gets blacklisted.
        /// </remarks>
        private const int FruitlessBlowsAllowed = 4;

        private static readonly Dictionary<ZDOID, float> NextBlow = new Dictionary<ZDOID, float>();

        /// <summary>
        ///     The deposit each villager's walk has done its best at.
        /// </summary>
        /// <remarks>
        ///     The working reach cannot be read off the walk's stall clock the way hauling reads
        ///     it - striking calls <c>Walk.Forget()</c> every tick, which zeroes that clock, so
        ///     the reach would collapse back to the tight one the moment a blow landed. The
        ///     villager would then be told it had left the rock, walk the same two steps, wait
        ///     out the settling time and swing again: one blow every few seconds instead of one
        ///     a second, with a shuffle after each. Chopping found this and solved it with this
        ///     latch; this is the same latch.
        /// </remarks>
        private static readonly Dictionary<ZDOID, ZDOID> Settled = new Dictionary<ZDOID, ZDOID>();

        /// <summary>
        ///     Fruitless blows, and the deposit they were spent on.
        /// </summary>
        /// <remarks>
        ///     The deposit is half the key, and has to be. Counting per villager alone means a
        ///     run of blows started on rock that cannot be broken is spent against whatever the
        ///     villager picks up next: three refusals on an obsidian vein, a trip that ends for
        ///     an unrelated reason, and then one benign fruitless blow on a good copper vein
        ///     writes it off for five minutes. Chopping keys its own count this way for exactly
        ///     that reason.
        /// </remarks>
        private static readonly Dictionary<ZDOID, Blows> Fruitless = new Dictionary<ZDOID, Blows>();

        /// <summary>
        ///     The protocol for the deposit each villager holds, built once per deposit.
        /// </summary>
        /// <remarks>
        ///     Building one costs a handful of GetComponent calls and an allocation, and the
        ///     answer cannot change while the villager keeps working the same rock - so doing it
        ///     twenty times a second per miner is exactly the per-tick cost this mod argues
        ///     against everywhere else. Dropped whenever the target changes or the object goes.
        /// </remarks>
        private static readonly Dictionary<ZDOID, MineProtocol> Holding =
            new Dictionary<ZDOID, MineProtocol>();

        /// <summary>Reused so a 20Hz path does not allocate a list per villager per tick.</summary>
        private static readonly List<MineArea> Parts = new List<MineArea>();

        private static readonly List<Spot> Spots = new List<Spot>();

        /// <summary>How many wasted blows a villager has landed, and on what.</summary>
        private struct Blows
        {
            internal ZDOID On;
            internal int Count;
        }

        /// <summary>Dropped when a world unloads; none of these identities survive one.</summary>
        internal static void Clear()
        {
            NextBlow.Clear();
            Settled.Clear();
            Fruitless.Clear();
            Holding.Clear();
        }

        /// <summary>Drops what a villager that no longer exists was waiting on.</summary>
        internal static void Forget(ZDOID villager)
        {
            if (villager.IsNone()) return;

            NextBlow.Remove(villager);
            Settled.Remove(villager);
            Fruitless.Remove(villager);
            Holding.Remove(villager);
        }

        internal static JobResult Tick(MineContext context, out string activity)
        {
            VillagerState state = context.State;

            GameObject target = Resolve(state.Target, out bool lost);

            // Gone is not a fault here - it is what finishing looks like. A deposit this
            // villager emptied and one the player emptied are the same news.
            if (lost) state.ClearTarget();

            ItemDrop.ItemData pick = Pickaxe(context);
            MineProtocol rock = Working(context, target);

            // The part to work, chosen afresh from what is standing. This is the line the whole
            // job is arranged around: everything below asks about *this* part, and next tick it
            // may be a different one because the rock moved under it.
            bool has = Part(context, rock, out MineArea part);

            bool enough = Enough(context);

            MineFacts facts = new MineFacts(
                hasTool: pick != null,
                hasDeposit: !state.Target.IsNone() && rock != null,
                hasArea: has,
                atArea: has && Within(context, part.At),
                enough: enough,
                tired: false);

            MineStep step = MineTransitions.Next((MineState)state.WorkState, facts);

            switch (step.Action)
            {
                case MineAction.Yield:
                    return JobOutcomes.Skipped(state, WhyNothing(pick, enough), out activity);

                case MineAction.ChooseWork:
                    return Record(state, step, Choose(context, out activity));

                case MineAction.MoveToArea:
                    return Record(state, step, Walk(context, part.At, out activity));

                case MineAction.Strike:
                    return Record(state, step, Strike(context, rock, part, pick, out activity));

                case MineAction.Complete:
                    Release(context);
                    return JobOutcomes.Completed(state, "that one is out", out activity);

                default:
                    // An action this engine does not handle is a programming error rather than a
                    // world state. Say so rather than silently idling.
                    return JobOutcomes.Failed(state, "unhandled mine action", out activity);
            }
        }

        /// <summary>
        ///     Records the next state when a step made progress.
        /// </summary>
        /// <remarks>
        ///     A step reporting Completed means <em>that step</em> finished, which is not the job
        ///     being done - so it becomes Running, and only the table's own Complete ends a trip.
        /// </remarks>
        private static JobResult Record(VillagerState state, MineStep step, JobResult result)
        {
            if (result != JobResult.Running && result != JobResult.Completed) return result;

            state.SetWorkState((int)step.Next);
            return JobResult.Running;
        }

        /// <summary>
        ///     The nearest part of the held deposit that is still standing.
        /// </summary>
        /// <remarks>
        ///     Two steps, and the split is deliberate: the protocol says which parts exist, which
        ///     needs a world, and the choosing is arithmetic that is checked without one.
        /// </remarks>
        private static bool Part(MineContext context, MineProtocol rock, out MineArea part)
        {
            part = default;
            if (rock == null || !rock.IsValid) return false;

            Parts.Clear();
            rock.Areas(Parts);
            if (Parts.Count == 0) return false;

            Spots.Clear();
            foreach (MineArea area in Parts) Spots.Add(new Spot(area.Index, area.At.x, area.At.z));

            Vector3 here = context.Villager.transform.position;
            if (!MineTargets.Nearest(Spots, here.x, here.z, out Spot chosen)) return false;

            foreach (MineArea area in Parts)
            {
                if (area.Index != chosen.Index) continue;

                part = area;
                return true;
            }

            return false;
        }

        /// <summary>
        ///     Takes the nearest deposit worth working, in the job's own order of places.
        /// </summary>
        private static JobResult Choose(MineContext context, out string activity)
        {
            List<WorkArea> areas = Areas(context.Colony, context.Job);
            List<ZDOID> candidates = MiningGround.Near(context.Colony);
            Vector3 here = context.Villager.transform.position;

            // What this villager could actually break, asked before it walks anywhere. Both
            // numbers are asset data, so this costs a dictionary lookup and saves the whole
            // round trip - and the alternative is a villager that walks to an obsidian vein
            // with a bronze pickaxe, lands four refused blows, gives up, and comes back when
            // the refusal lapses.
            ItemDrop.ItemData pick = VillagerTool.Best(
                context.Bag != null ? context.Bag.GetInventory() : null, ToolKind.Pickaxe);

            int tier = pick?.m_shared != null ? pick.m_shared.m_toolTier : 0;

            foreach (WorkArea area in areas)
            {
                ZDOID best = Nearest(context, area, candidates, here, tier);
                if (best.IsNone()) continue;

                // A new target is a new walk and a new tolerance. Without the walk being told,
                // its stall clock still holds the last target's timings and judges the first
                // step of this one as already stuck.
                context.Walk.Forget();
                context.Walk.NewLeg();

                // The latch belongs to the deposit that was walked to, so a new one starts
                // un-arrived. Without this a villager re-choosing a deposit it had reached
                // earlier is granted the wider working reach from the first tick and mines it
                // from ten metres away, never walking in.
                Settled.Remove(context.Villager.Id);
                context.State.SetTarget(best);

                activity = "off to mine";
                return JobResult.Running;
            }

            return JobOutcomes.Skipped(context.State, "nothing to mine", out activity);
        }

        /// <summary>
        ///     The nearest deposit in an area that is worth walking to.
        /// </summary>
        /// <remarks>
        ///     <b>Emptiness is asked of the winner, not of every candidate.</b> Answering it
        ///     means building a protocol, which walks a deposit's whole collider hierarchy - so
        ///     asking it inside the distance loop put that cost on every rock in range, on every
        ///     tick an idle miner spends choosing, which is the shape of cost the caching two
        ///     files away was written to remove. The loop below asks the cheap questions of
        ///     everything and the expensive one of one thing, retrying only when that one turns
        ///     out to be spent.
        /// </remarks>
        private static ZDOID Nearest(MineContext context, WorkArea area, List<ZDOID> candidates,
            Vector3 here, int tier)
        {
            HashSet<ZDOID> spent = null;

            for (int attempt = 0; attempt < 4; attempt++)
            {
                ZDOID best = ZDOID.None;
                float closest = float.MaxValue;

                foreach (ZDOID id in candidates)
                {
                    if (spent != null && spent.Contains(id)) continue;

                    ZDO zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(id) : null;
                    if (zdo == null || !zdo.IsValid()) continue;

                    Vector3 at = zdo.GetPosition();
                    if (!area.Contains(at)) continue;

                    if (!Wanted(context.Job, zdo.GetPrefab())) continue;
                    if (!Breakable(zdo.GetPrefab(), tier)) continue;
                    if (Unreachable.Refuses(context.Villager.Id, id)) continue;
                    if (TargetClaims.IsClaimedByOther(id, context.Villager)) continue;

                    float distance = Utils.DistanceXZ(at, here);
                    if (distance >= closest) continue;

                    closest = distance;
                    best = id;
                }

                if (best.IsNone()) return ZDOID.None;
                if (Worth(best)) return best;

                // Hollowed out but still standing. Set aside and ask the next one - bounded,
                // because a field of spent rock should cost a few checks rather than a full
                // second pass per tick.
                spent = spent ?? new HashSet<ZDOID>();
                spent.Add(best);
            }

            return ZDOID.None;
        }

        /// <summary>
        ///     Whether this job works this prefab: the right kind, and the right ore.
        /// </summary>
        /// <remarks>
        ///     Loose rock is opt-in, for the reason undergrowth is: the game cannot tell a
        ///     boulder from a crate, so admitting every Destructible a pickaxe bites would hand
        ///     villagers a licence to demolish scenery nobody asked them to touch.
        /// </remarks>
        private static bool Wanted(JobDefinition job, int prefabHash)
        {
            switch (Mineable.Of(prefabHash))
            {
                case MineKind.Deposit:
                    // An ore list narrows deposits only. Loose rock is governed by its own
                    // switch, and asking a stone boulder whether it yields tin would exclude it
                    // from every job that named an ore - which is not what that switch means.
                    return Mineable.DropsAny(prefabHash, job?.Ores);

                case MineKind.Boulder:
                    return job != null && job.MineBoulders;

                default:
                    return false;
            }
        }

        /// <summary>Whether a pickaxe of this tier can break this prefab at all.</summary>
        /// <remarks>
        ///     Read from the prefab rather than the instance, so it answers for a deposit before
        ///     anybody has walked to it - which is the only point at which the answer is worth
        ///     anything.
        /// </remarks>
        private static bool Breakable(int prefabHash, int tier)
        {
            GameObject prefab = ZNetScene.instance != null
                ? ZNetScene.instance.GetPrefab(prefabHash)
                : null;

            return prefab == null || Mineable.TierOf(prefab) <= tier;
        }

        private static JobResult Strike(MineContext context, MineProtocol rock, MineArea part,
            ItemDrop.ItemData pick, out string activity)
        {
            if (rock == null || !rock.IsValid)
            {
                context.State.ClearTarget();
                return JobOutcomes.Running("it was gone", out activity);
            }

            // Turned to face it, because work done standing still has nothing else to turn it -
            // and kept up between blows, because the part being worked moves round the rock as
            // the near ones fall.
            context.Villager.FaceTowards(part.At, context.DeltaTime);

            // The walk is over. Said plainly so its stall clock stops running against a villager
            // that is standing still on purpose.
            context.Walk.Forget();

            ZDOID miner = context.Villager.Id;
            if (!miner.IsNone())
            {
                if (NextBlow.TryGetValue(miner, out float when) && Time.time < when)
                {
                    // Between blows. Not a swing, and deliberately not animated: retriggering
                    // the animation every tick is how the swing never plays at all.
                    return JobOutcomes.Running("mining", out activity);
                }

                NextBlow[miner] = Time.time + SecondsBetweenBlows;
            }

            HitData hit = Blow(pick, part.At, context.Villager.transform.position);
            BlowResult blow = rock.Strike(part, hit, out string what);

            switch (blow)
            {
                case BlowResult.Claiming:
                    // Ownership is being taken; the blow lands on a later tick. Not a swing, so
                    // not animated - a villager miming a blow that did nothing is exactly the
                    // thing that makes a broken job look like a working one.
                    return JobOutcomes.Running(what, out activity);

                case BlowResult.Struck:
                case BlowResult.Felled:
                    // A landed blow is progress, so the claim is refreshed here. Without it the
                    // claim ages against the length of the vein rather than against being
                    // stuck, and any deposit outlasting the thirty-second life loses its claim
                    // half-mined - which is two villagers on one vein, arrived at by both of
                    // them behaving correctly.
                    context.State.TouchClaim();
                    Fruitless.Remove(miner);
                    context.Animation?.Swing();
                    return JobOutcomes.Running(what, out activity);

                case BlowResult.TooHard:
                    return Blunt(context, miner, what, out activity);

                default:
                    Release(context);
                    return JobOutcomes.Running(what, out activity);
            }
        }

        /// <summary>
        ///     A blow that moved nothing, and what to make of it.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>Not on the first one.</b> A blow can land and change nothing for reasons
        ///         that are not "this tool cannot do it": a Destructible ignores damage during
        ///         its first frame, and a peer that has just taken ownership may be working from
        ///         a collider set up to ten seconds stale and aim at a part already gone. Acting
        ///         immediately is how a perfectly good deposit gets written off.
        ///     </para>
        ///     <para>
        ///         <b>And when it is a verdict, it lasts.</b> The long refusal rather than the
        ///         twenty seconds meant for somebody standing in a doorway - otherwise the
        ///         villager walks back every twenty seconds to be refused again. Long rather
        ///         than permanent, and deliberately: a better pickaxe can arrive, and a
        ///         settlement should notice that without being reloaded.
        ///     </para>
        ///     <para>
        ///         Tier is checked before the walk now, so what reaches here is the rest: damage
        ///         modifiers that reduce a blow to nothing on their own, which no number on the
        ///         prefab announces in advance.
        ///     </para>
        ///     <para>
        ///         <b>Skipped, not completed.</b> Releasing the target sends the table to its
        ///         Complete arm on the next tick, which would report a deposit that was never
        ///         touched as finished and spend a repetition on it. Ending the trip here is
        ///         what keeps a job from exhausting its own count on work it cannot do.
        ///     </para>
        /// </remarks>
        private static JobResult Blunt(MineContext context, ZDOID miner, string what,
            out string activity)
        {
            if (miner.IsNone()) return JobOutcomes.Running(what, out activity);

            ZDOID on = context.State.Target;
            Fruitless.TryGetValue(miner, out Blows spent);

            // A count against a different deposit says nothing about this one.
            int blows = spent.On == on ? spent.Count + 1 : 1;
            Fruitless[miner] = new Blows { On = on, Count = blows };

            if (blows < FruitlessBlowsAllowed) return JobOutcomes.Running(what, out activity);

            Chatter.Say($"[mine] blunt: {context.State.Target}",
                $"{context.State.Name} cannot break that with the pickaxe it has.");

            Unreachable.Refuse(context.Villager.Id, context.State.Target);
            Release(context);
            return JobOutcomes.Skipped(context.State, what, out activity);
        }

        /// <summary>
        ///     The blow, taken from the pickaxe rather than invented.
        /// </summary>
        /// <remarks>
        ///     The same shape felling uses. The direction matters less here than it does for a
        ///     falling trunk, but a hit with no direction at all is one the game cannot attribute
        ///     and effects read from.
        /// </remarks>
        private static HitData Blow(ItemDrop.ItemData pick, Vector3 at, Vector3 from)
        {
            HitData hit = new HitData();
            if (pick != null)
            {
                hit.m_damage = pick.GetDamage();

                // The hit's tier is a short while the item's is an int. They are the same small
                // numbers, and the cast is what the game does to itself.
                hit.m_toolTier = (short)(pick.m_shared != null ? pick.m_shared.m_toolTier : 0);
            }

            Vector3 away = at - from;
            away.y = 0f;
            hit.m_dir = away.sqrMagnitude > .001f ? away.normalized : Vector3.forward;
            hit.m_point = at;
            hit.m_pushForce = 0f;
            return hit;
        }

        private static void Release(MineContext context)
        {
            context.State.ClearTarget();
            Forget(context.Villager.Id);
        }

        /// <summary>
        ///     Whether a candidate has anything left to work.
        /// </summary>
        /// <remarks>
        ///     A deposit outlives its parts, and one kind of them outlives them permanently -
        ///     a MineRock built with <c>m_removeWhenDestroyed</c> false stands there for ever
        ///     with nothing on it. Without this the nearest such rock is chosen every tick,
        ///     reported finished, and chosen again: a villager standing still, spending its
        ///     whole mining allowance, announcing success the entire time.
        /// </remarks>
        private static bool Worth(ZDOID id)
        {
            GameObject instance = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(id) : null;

            // Not loaded is not the same as not worth it. Something out of memory is still work,
            // and refusing it here would make discovery depend on what happens to be resident.
            if (instance == null) return true;

            return MineProbe.TryFind(instance, out MineProtocol rock) && !rock.Spent();
        }

        /// <summary>The best pickaxe the villager owns, shown in its hand.</summary>
        private static ItemDrop.ItemData Pickaxe(MineContext context)
        {
            ItemDrop.ItemData best = VillagerTool.Best(
                context.Bag != null ? context.Bag.GetInventory() : null, ToolKind.Pickaxe);

            VillagerTool.Show(context.Equipment, context.Animation, Record(context), best);
            return best;
        }

        private static ZDO Record(MineContext context) =>
            context.Villager != null && context.Villager.TryGetComponent(out ZNetView view) &&
            view.IsValid()
                ? view.GetZDO()
                : null;

        /// <summary>
        ///     Whether the settlement already holds as much as this job was asked to gather.
        /// </summary>
        /// <remarks>
        ///     The same terminus chopping has, and mining needs it more: a forest grows back and
        ///     a vein does not. Counted in registered storage only, so ore still lying where it
        ///     was mined does not count towards the target - which is right, because a settlement
        ///     does not have what nobody has carried home.
        /// </remarks>
        private static bool Enough(MineContext context) => HasEnough(context.Colony, context.Job);

        private static bool HasEnough(Colony colony, JobDefinition job)
        {
            if (job == null || job.StockTarget <= 0 || string.IsNullOrEmpty(job.StockItem)) return false;

            return Stock.Held(colony, job.StockItem) >= job.StockTarget;
        }

        /// <summary>
        ///     The predicates a check may ask, rather than reimplement.
        /// </summary>
        /// <remarks>
        ///     The same arrangement <c>ChopJob</c> exposes and for the reason it records: a check
        ///     that writes out the rule again passes while the job quietly ignores the setting.
        ///     Asked of a prefab hash rather than of an object, because mining's answer needs no
        ///     instance - which is what lets a settings check assert things it never has to
        ///     spawn.
        /// </remarks>
        internal static bool WouldTake(JobDefinition job, int prefabHash) => Wanted(job, prefabHash);

        internal static bool WouldTake(JobDefinition job, ZDO zdo) =>
            zdo != null && zdo.IsValid() && Wanted(job, zdo.GetPrefab());

        internal static bool WouldBreak(int prefabHash, int toolTier) => Breakable(prefabHash, toolTier);

        internal static bool WouldStop(Colony colony, JobDefinition job) => HasEnough(colony, job);

        private static string WhyNothing(ItemDrop.ItemData pick, bool enough)
        {
            if (enough) return "we have enough";

            return pick == null ? "no pickaxe" : "nothing to mine";
        }

        private static List<WorkArea> Areas(Colony colony, JobDefinition job)
        {
            List<WorkArea> areas = new List<WorkArea>();
            WorkArea.AllFor(colony, job, areas);

            // Narrowed to what the sweep will actually return, for the reason chopping gives: a
            // work area wider than the scan that feeds it is a band of ground the job lists as
            // in range and can never act on.
            for (int i = 0; i < areas.Count; i++) areas[i] = areas[i].NoWiderThan(MiningGround.SearchRadius);

            return areas;
        }

        /// <summary>
        ///     How to work the deposit this villager holds, remembered while it holds it.
        /// </summary>
        private static MineProtocol Working(MineContext context, GameObject target)
        {
            ZDOID villager = context.Villager.Id;
            ZDOID held = context.State.Target;

            if (!villager.IsNone() && Holding.TryGetValue(villager, out MineProtocol cached))
            {
                // Still the same rock, and still there. Anything else and it is rebuilt, which
                // is the cheap case precisely because it happens once per deposit rather than
                // once per tick.
                if (cached != null && cached.IsValid && cached.View.GetZDO().m_uid == held) return cached;

                Holding.Remove(villager);
            }

            if (target == null || !MineProbe.TryFind(target, out MineProtocol found)) return null;

            if (!villager.IsNone()) Holding[villager] = found;
            return found;
        }

        private static JobResult Walk(MineContext context, Vector3 to, out string activity)
        {
            switch (context.Walk.MoveTowards(to, Approach.ToStructure, deltaTime: context.DeltaTime))
            {
                case MoveResult.Arrived:
                    context.State.TouchClaim();

                    // Latched here, and only here. This is the walk saying it has got as close
                    // as it is going to; everything after it works from that answer rather than
                    // from a clock the striking keeps resetting.
                    if (!context.Villager.Id.IsNone()) Settled[context.Villager.Id] = context.State.Target;

                    return JobOutcomes.Running("off to mine", out activity);

                case MoveResult.PathFailed:
                    Unreachable.Refuse(context.Villager.Id, context.State.Target,
                        Unreachable.BlockedForSeconds);
                    Release(context);
                    return JobOutcomes.Skipped(context.State, "cannot get there", out activity);

                default:
                    // The claim is refreshed while walking, not only on arrival: the sweep
                    // reaches ninety-six metres and a claim lives thirty seconds, so a long
                    // approach would lose the deposit to somebody else before getting there.
                    context.State.TouchClaim();

                    // And bounded. Every other walking job bounds this arm, because a trip that
                    // can never arrive otherwise returns Running for ever - which consumes no
                    // repetition, so the queue never advances and every later entry in that
                    // villager's queue stops running too.
                    ZDOID abandoned = context.State.Target;
                    JobResult? stuck = JobOutcomes.GiveUpIfStuck(context.Villager, context.State,
                        context.Walk.TripStalledFor, abandoned,
                        () => "a rock", out string gaveUp);

                    if (stuck.HasValue)
                    {
                        // Refused for a while rather than for good: not being able to walk
                        // somewhere stops being true the moment a path opens, and the
                        // settlement should notice without being reloaded.
                        Unreachable.Refuse(context.Villager.Id, abandoned);
                        Release(context);
                        activity = gaveUp;
                        return stuck.Value;
                    }

                    return JobOutcomes.Running("off to mine", out activity);
            }
        }

        /// <summary>
        ///     Whether the villager is close enough to work this part.
        /// </summary>
        /// <remarks>
        ///     The wider reach applies once the walk has done its best at <em>this deposit</em>,
        ///     recorded per target rather than read off the stall clock - striking resets that
        ///     clock every tick, so reading it would make the reach collapse after every blow.
        ///     And the part being worked moves as the near ones fall, which is why the latch is
        ///     on the deposit rather than on the part: a villager standing at a vein has arrived
        ///     at the vein, whichever rock of it is next.
        /// </remarks>
        private static bool Within(MineContext context, Vector3 at)
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
