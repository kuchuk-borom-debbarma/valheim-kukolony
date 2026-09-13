using System.Collections.Generic;
using Kukolony.Colonies;
using Kukolony.Colonies.Stations;
using Kukolony.Core;
using Kukolony.Gui;
using Kukolony.Villagers;
using Kukolony.Villagers.Navigation;
using UnityEngine;

namespace Kukolony.Jobs.Tend
{
    /// <summary>What one tending tick needs. Assembled by the villager, never stored.</summary>
    internal sealed class TendContext
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
    ///     Keeping the settlement's stations supplied, and taking off what they have finished.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>The target is the station, not the chest.</b> A claim is whatever sits in
    ///         <see cref="VillagerState.Target" />, and what must not be shared here is the
    ///         station: any number of villagers may take wood from one chest, while two feeding
    ///         one kiln is a wasted round trip. So this job inverts hauling's mapping, and the
    ///         words everywhere below say station and supply rather than destination and source.
    ///     </para>
    ///     <para>
    ///         <b>A target may also be a chest.</b> When a load stops being wanted - the station
    ///         filled while the villager walked - the trip is re-pointed at somewhere that will
    ///         take it, which may be another station or may be a container. The table never
    ///         learns the difference; it asks whether the target wants what is carried, and this
    ///         engine knows whether that means an RPC or a deposit. That is what keeps "a load
    ///         nothing wants is filed rather than carried for ever" from needing its own leg.
    ///     </para>
    /// </remarks>
    internal static class TendJob
    {
        /// <summary>
        ///     How long between loads.
        /// </summary>
        /// <remarks>
        ///     A step reporting Running is re-asked on the next 20 Hz tick, so without this a
        ///     villager fills a ten-slot smelter in half a second and broadcasts ten effect
        ///     events to every peer. The same reason chopping waits between blows.
        /// </remarks>
        private const float SecondsBetweenLoads = .4f;

        /// <summary>
        ///     How long a station that swallowed a load is left alone.
        /// </summary>
        /// <remarks>
        ///     Time-bounded rather than permanent: a swallow can be a transient ownership race as
        ///     easily as a station that will never take anything, and a settlement that wrote off
        ///     its furnace for the session because of one bad tick would be worse than one that
        ///     tried again in five minutes.
        /// </remarks>
        private const float DeafForSeconds = 300f;

        /// <summary>How many swallowed loads before a station is left alone.</summary>
        /// <remarks>
        ///     Two, and each costs exactly one item - the honest price of consuming before the
        ///     call. One would write off a station for a single race; ten would be ten items.
        /// </remarks>
        private const int SwallowsAllowed = 2;

        private static readonly Dictionary<ZDOID, float> Deaf = new Dictionary<ZDOID, float>();

        private static readonly Dictionary<ZDOID, int> Swallows = new Dictionary<ZDOID, int>();

        private static readonly Dictionary<ZDOID, float> NextLoad = new Dictionary<ZDOID, float>();

        /// <summary>Dropped when a world unloads; none of these identities survive one.</summary>
        internal static void Clear()
        {
            Deaf.Clear();
            Swallows.Clear();
            NextLoad.Clear();
        }

        /// <summary>Drops what a villager that no longer exists was waiting on.</summary>
        internal static void Forget(ZDOID villager)
        {
            if (!villager.IsNone()) NextLoad.Remove(villager);
        }

        internal static JobResult Tick(TendContext context, out string activity)
        {
            VillagerState state = context.State;

            GameObject target = Resolve(state.Target, out bool targetLost);
            GameObject supply = Resolve(state.Destination, out bool supplyLost);

            // Gone is different from not loaded. Something destroyed is worth giving up on;
            // something merely out of memory is worth waiting for.
            if (targetLost) state.ClearTarget();
            if (supplyLost) state.SetDestination(ZDOID.None);

            List<ItemDrop.ItemData> carried = Carrying.Cargo(context.Bag.GetInventory(), state.Cargo);
            if (carried.Count == 0 && !string.IsNullOrEmpty(state.Cargo)) state.SetCargo(string.Empty);

            StationProtocol protocol = Operating(target);
            StructureRecord record = SettlementIndex.Find(context.Colony, state.Target);
            StationWant want = Wanted(context, record, protocol, carried);

            TendFacts facts = new TendFacts(
                hasSupply: !state.Destination.IsNone(),
                hasStation: !state.Target.IsNone(),
                atSupply: Within(context, supply),
                atStation: Within(context, target),
                carrying: carried.Count > 0,
                stationWants: want.Any,
                stationHasOutput: Clearing(context.Job) && protocol != null && protocol.HasOutput(),
                tired: false);

            TendStep step = TendTransitions.Next((TendState)state.WorkState, facts);

            switch (step.Action)
            {
                case TendAction.Yield:
                    return JobOutcomes.Skipped(state, "nothing to tend", out activity);

                case TendAction.ChooseWork:
                    return Record(state, step, Choose(context, carried, out activity));

                case TendAction.MoveToSupply:
                    BeginLeg(context, TendState.Fetching);
                    return Record(state, step, Walk(context, supply, "fetching", out activity));

                case TendAction.Collect:
                    return Record(state, step, Collect(context, supply, want, out activity));

                case TendAction.MoveToStation:
                    BeginLeg(context, (TendState)state.WorkState == TendState.Clearing
                        ? TendState.Clearing
                        : TendState.Delivering);
                    return Record(state, step, Walk(context, target,
                        carried.Count > 0 ? "carrying" : "off to the station", out activity));

                case TendAction.Feed:
                    return Record(state, step, Feed(context, target, protocol, carried, want, out activity));

                case TendAction.TakeOutput:
                    return Record(state, step, TakeOutput(context, protocol, out activity));

                case TendAction.Complete:
                    return JobOutcomes.Completed(state, "done tending", out activity);

                default:
                    // An action this engine does not handle is a programming error, not a world
                    // state. Say so rather than silently idling.
                    return JobOutcomes.Failed(state, "unhandled tend action", out activity);
            }
        }

        /// <summary>
        ///     Records the next state when a step made progress.
        /// </summary>
        /// <remarks>
        ///     A step reporting Completed means <em>that step</em> finished, which is not the job
        ///     being done - so it becomes Running, and only the transition table's own Complete
        ///     ends a trip.
        /// </remarks>
        private static JobResult Record(VillagerState state, TendStep step, JobResult result)
        {
            if (result != JobResult.Running && result != JobResult.Completed) return result;

            state.SetWorkState((int)step.Next);
            return JobResult.Running;
        }

        /// <summary>
        ///     Picks a station to work, and where to get what it wants - or, for a villager
        ///     already carrying something, somewhere that load can go.
        /// </summary>
        private static JobResult Choose(TendContext context, List<ItemDrop.ItemData> carried,
            out string activity)
        {
            VillagerState state = context.State;

            // Carrying already means the fetch half is done and the only question left is where
            // this goes. Another station that wants it first, then anywhere that will take it -
            // because a bag that fills with oddments cannot work at all.
            if (carried.Count > 0) return Rehome(context, carried, out activity);

            List<WorkArea> areas = new List<WorkArea>();
            WorkArea.AllFor(context.Colony, context.Job, areas);

            Vector3 here = context.Villager.transform.position;

            foreach (WorkArea area in areas)
            {
                foreach (StructureRecord record in SettlementIndex.WhatWantsFeeding(context.Colony, here))
                {
                    if (!Eligible(context, area, record)) continue;

                    GameObject instance = ZNetScene.instance != null
                        ? ZNetScene.instance.FindInstance(record.Id)
                        : null;

                    StationProtocol protocol = Operating(instance);
                    if (protocol == null) continue;
                    if (!Kind(context.Job, protocol.Kind)) continue;

                    // Clearing before supplying, because a station holding finished work cannot
                    // accept anything at all - so this is the only move that makes progress.
                    if (Clearing(context.Job) && protocol.HasOutput())
                    {
                        Take(context, record.Id);
                        activity = "off to clear " + record.Name;
                        return JobResult.Running;
                    }

                    StationWant want = Allowed(context, protocol.WhatItWants(record.Settings,
                        string.Empty));
                    if (!want.Any) continue;

                    StructureRecord from = Holding(context, want.Item, here);
                    if (from == null)
                    {
                        // Nothing in the settlement has it. Said once rather than every tick,
                        // and keyed on the item so two stations short of coal say it together.
                        Chatter.Say($"no {want.Item} to fetch",
                            $"nothing has {ItemCatalogue.Label(want.Item)} for {record.Name}.");
                        continue;
                    }

                    Take(context, record.Id);
                    state.SetDestination(from.Id);
                    activity = "fetching " + ItemCatalogue.Label(want.Item);
                    return JobResult.Running;
                }
            }

            return JobOutcomes.Skipped(state,
                HasEnough(context.Colony, context.Job) ? "we have enough" : "nothing to tend",
                out activity);
        }

        /// <summary>
        ///     Somewhere for a load the villager is already holding.
        /// </summary>
        /// <remarks>
        ///     A station that wants it beats a chest, because the load was fetched for a station
        ///     and putting it away would be undone on the next trip. Falling back to a chest is
        ///     what stops a villager carrying an orphaned load for the rest of the session.
        /// </remarks>
        private static JobResult Rehome(TendContext context, List<ItemDrop.ItemData> carried,
            out string activity)
        {
            Vector3 here = context.Villager.transform.position;
            string prefab = Carrying.NameOf(carried[0]);

            List<WorkArea> areas = new List<WorkArea>();
            WorkArea.AllFor(context.Colony, context.Job, areas);

            foreach (WorkArea area in areas)
            {
                foreach (StructureRecord record in SettlementIndex.WhatWantsFeeding(context.Colony, here))
                {
                    if (!Eligible(context, area, record)) continue;

                    GameObject instance = ZNetScene.instance != null
                        ? ZNetScene.instance.FindInstance(record.Id)
                        : null;

                    StationProtocol protocol = Operating(instance);
                    if (protocol == null || !Kind(context.Job, protocol.Kind)) continue;

                    StationWant want = Allowed(context, protocol.WhatItWants(record.Settings, prefab));
                    if (!want.Any || want.Item != prefab) continue;

                    Take(context, record.Id);
                    activity = "carrying " + ItemCatalogue.Label(prefab);
                    return JobResult.Running;
                }
            }

            // Nowhere wants it as material any more, so it is filed exactly as a hauler would
            // file it - the settlement already answers "where does this go" and a second answer
            // here would be a second thing to keep in step.
            foreach (StructureRecord home in SettlementIndex.WhereDoesItGo(context.Colony, prefab, here,
                         context.Villager.Id))
            {
                Take(context, home.Id);
                activity = "putting " + ItemCatalogue.Label(prefab) + " back";
                return JobResult.Running;
            }

            return JobOutcomes.Skipped(context.State,
                $"nowhere to put {ItemCatalogue.Label(prefab)}", out activity);
        }

        /// <summary>Takes a target, which is also taking the claim on it.</summary>
        private static void Take(TendContext context, ZDOID target)
        {
            // A new target is a new walk and a new tolerance. Without the walk being told, its
            // stall clock still holds the last target's timings and judges the first step of
            // this one as already stuck.
            context.Walk.Forget();
            context.Walk.NewLeg();
            context.State.SetTarget(target);
        }

        /// <summary>Whether a record is one this job may work, before anything is instantiated.</summary>
        private static bool Eligible(TendContext context, WorkArea area, StructureRecord record)
        {
            if (record == null) return false;
            if ((record.Capabilities & StructureCapability.Processing) == 0) return false;

            // Named stations narrow the work area rather than replacing it: an area says where
            // and a name says which.
            List<string> named = context.Job != null ? context.Job.Stations : null;
            if (named != null && named.Count > 0 && !named.Contains(record.PersistentId)) return false;

            if (Deaf.TryGetValue(record.Id, out float until) && Time.time < until) return false;
            if (Unreachable.Refuses(context.Villager.Id, record.Id)) return false;
            if (TargetClaims.IsClaimedByOther(record.Id, context.Villager)) return false;

            ZDO zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(record.Id) : null;
            return zdo != null && zdo.IsValid() && area.Contains(zdo.GetPosition());
        }

        /// <summary>The nearest container the settlement may take this out of.</summary>
        private static StructureRecord Holding(TendContext context, string prefab, Vector3 here)
        {
            foreach (StructureRecord record in SettlementIndex.WhereIsItKept(context.Colony, prefab, here))
            {
                if (Unreachable.Refuses(context.Villager.Id, record.Id)) continue;
                return record;
            }

            return null;
        }

        private static JobResult Collect(TendContext context, GameObject chest, StationWant want,
            out string activity)
        {
            if (chest == null || !want.Any)
            {
                context.State.SetDestination(ZDOID.None);
                activity = "it was gone";
                return JobResult.Running;
            }

            Container container = chest.GetComponentInChildren<Container>(true);
            Inventory inventory = container != null ? container.GetInventory() : null;
            // isPrefabName, or this finds nothing at all: Inventory.GetItem matches the
            // localised display name by default - "$item_wood", not "Wood" - and a station's
            // conversion list speaks in prefab names. Without it a villager walks to a full
            // chest, reports the wood gone, and chooses the same errand again for ever.
            ItemDrop.ItemData item = inventory != null
                ? inventory.GetItem(want.Item, isPrefabName: true)
                : null;

            if (item == null)
            {
                // Emptied while the villager walked. Clearing the supply is what sends the
                // table back to choosing rather than standing at a chest that has nothing.
                context.State.SetDestination(ZDOID.None);
                activity = "it was gone";
                return JobResult.Running;
            }

            context.Animation.Reach();

            switch (Carrying.TakeFromContainer(container, item, context.Bag.GetInventory(),
                        out string taken, want.Amount))
            {
                case TakeResult.Took:
                    context.State.AddCargo(taken);

                    // Enough for what the station asked for, so the fetch is over. Taking more
                    // would be a load the station cannot use and somebody has to file.
                    if (Held(context, want.Item) >= want.Amount) context.State.SetDestination(ZDOID.None);

                    activity = "taking " + ItemCatalogue.Label(want.Item);
                    return JobResult.Running;

                case TakeResult.Waiting:
                    activity = "opening the chest";
                    return JobResult.Running;

                case TakeResult.Full:
                    context.State.SetDestination(ZDOID.None);
                    activity = "my bag is full";
                    return JobResult.Running;

                default:
                    context.State.SetDestination(ZDOID.None);
                    activity = "it was gone";
                    return JobResult.Running;
            }
        }

        /// <summary>
        ///     Puts one load in, whether the target is a station or the chest a load was filed
        ///     into.
        /// </summary>
        private static JobResult Feed(TendContext context, GameObject target, StationProtocol protocol,
            List<ItemDrop.ItemData> carried, StationWant want, out string activity)
        {
            VillagerState state = context.State;

            if (target == null || carried.Count == 0 || !want.Any)
            {
                state.ClearTarget();
                activity = "it was gone";
                return JobResult.Running;
            }

            // Standing still on purpose, so the walk's stall clock is stopped rather than left
            // running - a feeding villager that looks stuck would inherit a widened working
            // reach for everything it did afterwards.
            context.Walk.Forget();
            context.Villager.FaceTowards(target.transform.position, context.DeltaTime);

            if (!Ready(context.Villager.Id))
            {
                activity = "loading";
                return JobResult.Running;
            }

            ItemDrop.ItemData item = Carried(carried, want.Item);
            if (item == null)
            {
                state.SetCargo(string.Empty);
                activity = "nothing left to load";
                return JobResult.Running;
            }

            context.Animation.Reach();

            // A chest rather than a station: the load was filed, which is the ordinary end of a
            // trip whose station filled up on the way.
            if (protocol == null) return Stow(context, target, item, out activity);

            switch (protocol.Give(context.Bag.GetInventory(), item, want.AsFuel))
            {
                case FeedResult.Fed:
                    // A landed load is the only progress this job makes standing still, so the
                    // claim is refreshed here - without it a slow fill outlasts the claim and a
                    // second villager joins in, which is the gap chopping found on long trees.
                    state.TouchClaim();
                    Swallows.Remove(state.Target);
                    activity = "loading " + ItemCatalogue.Label(want.Item);
                    return JobResult.Running;

                case FeedResult.Waiting:
                    activity = "reaching for it";
                    return JobResult.Running;

                case FeedResult.Full:
                    state.ClearTarget();
                    activity = "it is full";
                    return JobResult.Running;

                case FeedResult.Refused:
                    state.ClearTarget();
                    activity = "it will not take that";
                    return JobResult.Running;

                case FeedResult.Swallowed:
                    return Swallowed(context, out activity);

                default:
                    state.ClearTarget();
                    activity = "it was gone";
                    return JobResult.Running;
            }
        }

        /// <summary>
        ///     A station that took a load and did not change.
        /// </summary>
        /// <remarks>
        ///     The failure this job is arranged against: every call succeeds and the material is
        ///     gone. Struck rather than written off at once, because a swallow can be a transient
        ///     ownership race - and left alone once it is clearly not.
        /// </remarks>
        private static JobResult Swallowed(TendContext context, out string activity)
        {
            ZDOID station = context.State.Target;
            Swallows.TryGetValue(station, out int strikes);
            strikes++;
            Swallows[station] = strikes;

            if (strikes < SwallowsAllowed)
            {
                activity = "that did not take";
                return JobResult.Running;
            }

            Deaf[station] = Time.time + DeafForSeconds;
            Swallows.Remove(station);

            StructureRecord record = SettlementIndex.Find(context.Colony, station);
            string name = record != null ? record.Name : "a station";
            Chatter.Say($"deaf station {station}", $"{name} takes what I give it and nothing changes.");

            context.State.ClearTarget();
            return JobOutcomes.Skipped(context.State, "it takes nothing in", out activity);
        }

        /// <summary>Puts a load into a container, which is where an orphaned load ends up.</summary>
        /// <remarks>
        ///     Bounded by what that container was told to hold, as hauling's deposit is. Without
        ///     the cap a shed told to keep ten, holding nine, takes a villager's fifty and sits
        ///     at fifty-nine for good with its own screen still reading "at most 10" - and
        ///     nothing brings it back down, because a chest keeps what it holds.
        ///
        ///     Re-checked against the record on arrival rather than trusted from when the trip
        ///     was chosen: a villager can be carrying two kinds at once - a haul job's leftovers
        ///     ahead of this one in the queue - and the second kind was never what this chest
        ///     was chosen for.
        /// </remarks>
        private static JobResult Stow(TendContext context, GameObject target, ItemDrop.ItemData item,
            out string activity)
        {
            Container container = target.GetComponentInChildren<Container>(true);
            StructureRecord record = SettlementIndex.Find(context.Colony, context.State.Target);
            if (container == null || record == null)
            {
                context.State.ClearTarget();
                activity = "it was gone";
                return JobResult.Running;
            }

            string prefab = Carrying.NameOf(item);
            int allowed = SettlementIndex.RoomUnderCap(record, prefab);
            if (SettlementIndex.ScoreOf(record, prefab) <= 0 || allowed == 0)
            {
                // This chest was chosen for something else in the bag. Choosing again is right:
                // the settlement may still have somewhere for this, and dropping it here would
                // put it somewhere its own screen says it does not belong.
                context.State.ClearTarget();
                activity = "it does not belong here";
                return JobResult.Running;
            }

            switch (Carrying.Deposit(context.Bag.GetInventory(), item, container, allowed))
            {
                case TakeResult.Took:
                    activity = "putting it away";
                    return JobResult.Running;

                case TakeResult.Waiting:
                    activity = "opening the chest";
                    return JobResult.Running;

                default:
                    context.State.ClearTarget();
                    activity = "it will not fit";
                    return JobResult.Running;
            }
        }

        private static JobResult TakeOutput(TendContext context, StationProtocol protocol,
            out string activity)
        {
            if (protocol == null)
            {
                context.State.ClearTarget();
                activity = "it was gone";
                return JobResult.Running;
            }

            context.Walk.Forget();

            if (!Ready(context.Villager.Id))
            {
                activity = "clearing";
                return JobResult.Running;
            }

            context.Animation.Reach();

            if (!protocol.TakeOutput())
            {
                // It would not come off. Not a failure - the station may simply have finished
                // nothing - but not worth standing here for either.
                context.State.ClearTarget();
                activity = "nothing came off";
                return JobResult.Running;
            }

            context.State.TouchClaim();
            activity = "taking it off";
            return JobResult.Running;
        }

        /// <summary>Whether enough time has passed since this villager's last load.</summary>
        private static bool Ready(ZDOID villager)
        {
            if (NextLoad.TryGetValue(villager, out float next) && Time.time < next) return false;

            NextLoad[villager] = Time.time + SecondsBetweenLoads;
            return true;
        }

        private static JobResult Walk(TendContext context, GameObject target, string doing,
            out string activity)
        {
            if (target == null)
            {
                // Recorded but not instantiated. Its zone may still be streaming in, and waiting
                // for ever is how the previous system hung a villager, so this yields.
                return JobOutcomes.Skipped(context.State, "waiting for the world", out activity);
            }

            switch (context.Walk.MoveTowards(target.transform.position, Approach.DistanceTo(target),
                        deltaTime: context.DeltaTime))
            {
                case MoveResult.Arrived:
                    context.State.TouchClaim();
                    activity = doing;
                    return JobResult.Running;

                case MoveResult.PathFailed:
                    // Briefly, not for long: inside a settlement a path given up on is as easily
                    // another villager in the doorway as it is a wall.
                    Unreachable.Refuse(context.Villager.Id, Nearest(context, target),
                        Unreachable.BlockedForSeconds);
                    context.State.ClearTarget();
                    context.State.SetDestination(ZDOID.None);
                    activity = "I cannot get there";
                    return JobResult.Running;

                default:
                    JobResult? given = JobOutcomes.GiveUpIfStuck(context.Villager, context.State,
                        context.Walk.TripStalledFor, Nearest(context, target),
                        () => Named(context, target), out activity);

                    if (given.HasValue) return given.Value;

                    activity = doing;
                    return JobResult.Running;
            }
        }

        private static ZDOID Nearest(TendContext context, GameObject target) =>
            target != null && target.TryGetComponent(out ZNetView view) && view.IsValid()
                ? view.GetZDO().m_uid
                : ZDOID.None;

        private static string Named(TendContext context, GameObject target)
        {
            StructureRecord record = SettlementIndex.Find(context.Colony, Nearest(context, target));
            return record != null ? record.Name : StructureRegistry.DisplayName(target);
        }

        private static void BeginLeg(TendContext context, TendState leg)
        {
            if (TendLegs.Entering((TendState)context.State.WorkState, leg)) context.Walk.NewLeg();
        }

        private static bool Within(TendContext context, GameObject thing)
        {
            if (thing == null) return false;

            float reach = context.Walk.StalledFor >= Arrival.SettledSeconds
                ? Arrival.WorkingReach
                : Approach.DistanceTo(thing);

            return Utils.DistanceXZ(thing.transform.position, context.Villager.transform.position) <= reach;
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

        private static StationProtocol Operating(GameObject target) =>
            target != null && StationProbe.TryFind(target, out StationProtocol protocol) ? protocol : null;

        /// <summary>
        ///     What the current target wants from this villager, filtered by what the job allows.
        /// </summary>
        /// <remarks>
        ///     A target that is not a station is a container being filed into, and what it
        ///     "wants" is whatever is being carried - which is what lets one table cover both.
        /// </remarks>
        private static StationWant Wanted(TendContext context, StructureRecord record,
            StationProtocol protocol, List<ItemDrop.ItemData> carried)
        {
            if (record == null) return StationWant.Nothing;

            if (protocol == null)
            {
                if (carried.Count == 0) return StationWant.Nothing;

                string filing = Carrying.NameOf(carried[0]);
                return filing.Length == 0
                    ? StationWant.Nothing
                    : new StationWant(filing, false, carried[0].m_stack);
            }

            // Holding something narrows the question to "do you still want this", so a station
            // that has moved on to wanting something else does not send a loaded villager back
            // to the chest with what it came for.
            string holding = carried.Count > 0 ? Carrying.NameOf(carried[0]) : string.Empty;
            return Allowed(context, protocol.WhatItWants(record.Settings, holding));
        }

        /// <summary>What the job will carry, of what the station asked for.</summary>
        private static StationWant Allowed(TendContext context, StationWant want) =>
            Allowed(context.Colony, context.Job, want);

        private static StationWant Allowed(Colony colony, JobDefinition job, StationWant want)
        {
            if (job == null || !want.Any) return want;

            // The settlement has as much as it asked for, so there is nothing worth putting in
            // - which is a reason to stop supplying and not a reason to stop clearing. A station
            // holding finished work still has to be emptied: an oven left full burns what is on
            // it and then accepts nothing ever again, and "we have enough" is a poor epitaph for
            // a kitchen that set itself alight.
            if (HasEnough(colony, job)) return StationWant.Nothing;

            if (want.AsFuel && job.Carries == TendCargo.Material) return StationWant.Nothing;
            if (!want.AsFuel && job.Carries == TendCargo.Fuel) return StationWant.Nothing;

            // The item list narrows and never widens - the station has already said what it
            // takes, and this can only refuse some of it.
            if (job.Items != null && job.Items.Count > 0 && !job.Items.Contains(want.Item))
            {
                return StationWant.Nothing;
            }

            return job.Work == TendWork.Collect ? StationWant.Nothing : want;
        }

        /// <summary>
        ///     Whether the settlement already holds as much as this job was asked to make.
        /// </summary>
        /// <remarks>
        ///     The terminus a producing job otherwise lacks, and the same one chopping uses -
        ///     an item and a count, measured over the settlement's own containers. Without it a
        ///     kiln is kept topped up for ever and a woodpile becomes a coal pile becomes
        ///     nothing anybody asked for, while every individual decision is correct.
        ///
        ///     Counted in registered storage only, which is worth knowing: material sitting in
        ///     a station's own queue is not counted, so a job set to stop at fifty coal will
        ///     keep a kiln loaded that is about to produce the fiftieth.
        /// </remarks>
        internal static bool HasEnough(Colony colony, JobDefinition job)
        {
            if (job == null || job.StockTarget <= 0 || string.IsNullOrEmpty(job.StockItem)) return false;

            return Stock.Held(colony, job.StockItem) >= job.StockTarget;
        }

        private static bool Clearing(JobDefinition job) =>
            job == null || job.Work != TendWork.Supply;

        /// <summary>Whether this job works this kind of station. Nothing chosen means all of them.</summary>
        private static bool Kind(JobDefinition job, StationKind kind) =>
            job == null || job.StationKinds == 0 || (job.StationKinds & (1 << (int)kind)) != 0;

        private static bool Holds(List<ItemDrop.ItemData> carried, string prefab) =>
            Carried(carried, prefab) != null;

        private static ItemDrop.ItemData Carried(List<ItemDrop.ItemData> carried, string prefab)
        {
            foreach (ItemDrop.ItemData item in carried)
            {
                if (Carrying.NameOf(item) == prefab) return item;
            }

            return null;
        }

        private static int Held(TendContext context, string prefab)
        {
            int held = 0;
            foreach (ItemDrop.ItemData item in Carrying.Cargo(context.Bag.GetInventory(), context.State.Cargo))
            {
                if (Carrying.NameOf(item) == prefab) held += item.m_stack;
            }

            return held;
        }

        /// <summary>
        ///     What a station would ask this job for, asked from outside.
        /// </summary>
        /// <remarks>
        ///     The same predicate the choosing uses, exposed rather than reimplemented, so a
        ///     check cannot agree with a decision the job does not make.
        /// </remarks>
        internal static StationWant WouldWant(Colony colony, JobDefinition job, ZDOID station)
        {
            StructureRecord record = SettlementIndex.Find(colony, station);
            GameObject instance = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(station) : null;
            StationProtocol protocol = Operating(instance);

            return record == null || protocol == null || !Kind(job, protocol.Kind)
                ? StationWant.Nothing
                : Allowed(colony, job, protocol.WhatItWants(record.Settings, string.Empty));
        }
    }
}
