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

        private static readonly Dictionary<ZDOID, float> NextBlow = new Dictionary<ZDOID, float>();

        /// <summary>Reused so a 20Hz path does not allocate a list per villager per tick.</summary>
        private static readonly List<MineArea> Parts = new List<MineArea>();

        private static readonly List<Spot> Spots = new List<Spot>();

        /// <summary>Dropped when a world unloads; none of these identities survive one.</summary>
        internal static void Clear() => NextBlow.Clear();

        /// <summary>Drops what a villager that no longer exists was waiting on.</summary>
        internal static void Forget(ZDOID villager)
        {
            if (!villager.IsNone()) NextBlow.Remove(villager);
        }

        internal static JobResult Tick(MineContext context, out string activity)
        {
            VillagerState state = context.State;

            GameObject target = Resolve(state.Target, out bool lost);

            // Gone is not a fault here - it is what finishing looks like. A deposit this
            // villager emptied and one the player emptied are the same news.
            if (lost) state.ClearTarget();

            ItemDrop.ItemData pick = Pickaxe(context);
            MineProtocol rock = Working(target);

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

            foreach (WorkArea area in areas)
            {
                ZDOID best = ZDOID.None;
                float closest = float.MaxValue;

                foreach (ZDOID id in candidates)
                {
                    ZDO zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(id) : null;
                    if (zdo == null || !zdo.IsValid()) continue;

                    Vector3 at = zdo.GetPosition();
                    if (!area.Contains(at)) continue;

                    if (!Wanted(context.Job, zdo.GetPrefab())) continue;
                    if (Unreachable.Refuses(context.Villager.Id, id)) continue;
                    if (TargetClaims.IsClaimedByOther(id, context.Villager)) continue;

                    float distance = Utils.DistanceXZ(at, here);
                    if (distance >= closest) continue;

                    closest = distance;
                    best = id;
                }

                if (best.IsNone()) continue;

                // A new target is a new walk and a new tolerance. Without the walk being told,
                // its stall clock still holds the last target's timings and judges the first
                // step of this one as already stuck.
                context.Walk.Forget();
                context.Walk.NewLeg();
                context.State.SetTarget(best);

                activity = "off to mine";
                return JobResult.Running;
            }

            return JobOutcomes.Skipped(context.State, "nothing to mine", out activity);
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
                    context.Animation?.Swing();
                    return JobOutcomes.Running(what, out activity);

                case BlowResult.Felled:
                    context.Animation?.Swing();
                    return JobOutcomes.Running(what, out activity);

                case BlowResult.TooHard:
                    // Refused, and it will be refused again for ever. Letting go and saying so
                    // beats standing at a rock this pickaxe cannot break.
                    Unreachable.Refuse(context.Villager.Id, context.State.Target,
                        Unreachable.BlockedForSeconds);
                    Release(context);
                    return JobOutcomes.Running(what, out activity);

                default:
                    Release(context);
                    return JobOutcomes.Running(what, out activity);
            }
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
        private static bool Enough(MineContext context)
        {
            JobDefinition job = context.Job;
            if (job == null || job.StockTarget <= 0 || string.IsNullOrEmpty(job.StockItem)) return false;

            return Stock.Held(context.Colony, job.StockItem) >= job.StockTarget;
        }

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

        private static MineProtocol Working(GameObject target) =>
            target != null && MineProbe.TryFind(target, out MineProtocol found) ? found : null;

        private static JobResult Walk(MineContext context, Vector3 to, out string activity)
        {
            switch (context.Walk.MoveTowards(to, Approach.ToStructure, deltaTime: context.DeltaTime))
            {
                case MoveResult.Arrived:
                    context.State.TouchClaim();
                    return JobOutcomes.Running("off to mine", out activity);

                case MoveResult.PathFailed:
                    Unreachable.Refuse(context.Villager.Id, context.State.Target,
                        Unreachable.BlockedForSeconds);
                    Release(context);
                    return JobOutcomes.Skipped(context.State, "cannot get there", out activity);

                default:
                    return JobOutcomes.Running("off to mine", out activity);
            }
        }

        private static bool Within(MineContext context, Vector3 at)
        {
            // Arriving and having arrived must be the same number, or a villager walks as far
            // as it can, is told it is not there yet, and tries again for ever. The wider
            // working reach only applies once the walk has given up, which is what lets a
            // villager wedged against the rock get on with it.
            float reach = context.Walk.StalledFor >= Arrival.SettledSeconds
                ? Arrival.WorkingReach
                : Approach.ToStructure;

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
