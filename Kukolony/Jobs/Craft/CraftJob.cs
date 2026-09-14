using System.Collections.Generic;
using Kukolony.Colonies;
using Kukolony.Colonies.Stations;
using Kukolony.Core;
using Kukolony.Gui;
using Kukolony.Villagers;
using Kukolony.Villagers.Navigation;
using UnityEngine;

namespace Kukolony.Jobs.Craft
{
    /// <summary>What one crafting tick needs. Assembled by the villager, never stored.</summary>
    internal sealed class CraftContext
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
    ///     Making what the settlement's stations have been told to make.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>The craft is ours.</b> Valheim has no component that crafts and no server-side
    ///         path for it - <c>InventoryGui.DoCrafting</c> is the only implementation and it is
    ///         welded to the local player. So this reproduces the arithmetic rather than calling
    ///         it, which is why the consumption below is written out with vanilla's own
    ///         <c>ConsumeResources</c> beside it.
    ///     </para>
    ///     <para>
    ///         <b>The target is the station, not the chest</b>, as in tending: any number of
    ///         villagers may take iron from one chest, while two at one forge is a wasted walk.
    ///         <see cref="VillagerState.Cargo" /> holds the <em>product</em> this trip is making,
    ///         which is how the villager remembers which recipe it went shopping for.
    ///     </para>
    ///     <para>
    ///         <b>What it makes stays in its bag.</b> A hauler collects it, and collecting it is
    ///         the first thing a hauler looks for. So a full bag ends the job where it stands and
    ///         says so, rather than walking the product to a chest - which would be a second
    ///         delivery implementation and would quietly undo the arrangement.
    ///     </para>
    /// </remarks>
    internal static class CraftJob
    {
        /// <summary>
        ///     How long one craft takes.
        /// </summary>
        /// <remarks>
        ///     A step reporting Running is re-asked at 20 Hz, so without this a villager makes a
        ///     hundred nails in five seconds and the whole job is invisible. Long enough to
        ///     watch, short enough that a standing order finishes in an evening - and it is the
        ///     window the crafting animation plays in.
        /// </remarks>
        private const float SecondsPerCraft = 1.5f;

        /// <summary>
        ///     The highest station level any recipe asks for.
        /// </summary>
        /// <remarks>
        ///     Vanilla clamps to this when it compares - <c>Mathf.Min(GetLevel(), 4)</c> - so a
        ///     station with five extensions is not better than one with three. Mirrored rather
        ///     than dropped, or a villager would refuse a craft the player can do by hand.
        /// </remarks>
        private const int HighestLevel = 4;

        /// <summary>
        ///     Marks a trip as a repair rather than a craft.
        /// </summary>
        /// <remarks>
        ///     Cargo holds what the trip is for, and the two kinds of trip need to be told apart
        ///     from one string. A prefix rather than a second ZDO field because the distinction
        ///     is the villager's own business for the length of one errand - nothing outside
        ///     this job reads it, and a persisted field would be one more thing to keep true.
        /// </remarks>
        private const string RepairMark = "repair:";

        private static readonly Dictionary<ZDOID, float> NextCraft = new Dictionary<ZDOID, float>();

        /// <summary>Dropped when a world unloads; none of these identities survive one.</summary>
        internal static void Clear() => NextCraft.Clear();

        /// <summary>Drops what a villager that no longer exists was waiting on.</summary>
        internal static void Forget(ZDOID villager)
        {
            if (!villager.IsNone()) NextCraft.Remove(villager);
        }

        internal static JobResult Tick(CraftContext context, out string activity)
        {
            VillagerState state = context.State;

            GameObject target = Resolve(state.Target, out bool targetLost);
            GameObject supply = Resolve(state.Destination, out bool supplyLost);

            // Gone is different from not loaded. Something destroyed is worth giving up on;
            // something merely out of memory is worth waiting for.
            if (targetLost) state.ClearTarget();
            if (supplyLost) state.SetDestination(ZDOID.None);

            string cargo = state.Cargo ?? string.Empty;
            bool repairing = cargo.StartsWith(RepairMark, System.StringComparison.Ordinal);
            string product = repairing ? cargo.Substring(RepairMark.Length) : cargo;

            Recipe recipe = repairing ? null : CraftCatalogue.RecipeFor(product);

            // A repair carries one thing and spends nothing, so its "recipe" is the item itself.
            // That is what lets the fetching half of this job be shared without a branch in it.
            List<CraftNeed> needs = repairing
                ? new List<CraftNeed> { new CraftNeed(product, 1) }
                : Needs(recipe);

            StructureRecord record = SettlementIndex.Find(context.Colony, state.Target);
            CraftStation station = Station(target);
            Inventory bag = context.Bag.GetInventory();

            CraftFacts facts = new CraftFacts(
                hasStation: !state.Target.IsNone() && (recipe != null || repairing),
                hasSupply: !state.Destination.IsNone(),
                atSupply: Within(context, supply),
                atStation: Within(context, target),
                // A repair needs a *worn* one. Counting any copy let a villager holding the
                // axe it had just mended report that it had materials for ever, while the
                // station said there was nothing to mend - two facts that never agree again.
                hasMaterials: repairing
                    ? HeldWorn(bag, product) > 0
                    : recipe != null && CraftPlan.Enough(needs, item => Held(bag, item)),
                stationUsable: station != null && station.Usable(out string _) &&
                               (repairing || Level(station, recipe)),

                // A repair wants doing while the station still offers to do it and the thing is
                // still worn; a craft wants doing while its order is short.
                wantsMade: repairing
                    ? record != null && record.Settings.Repairs && Repairing.Anything(bag, station)
                    : record != null && Wanted(context.Colony, record, product),

                // Nothing comes out of a repair, so there is always room for it. And with no
                // recipe chosen yet the question is only whether the bag has any room at all -
                // asking whether there is room for a product nobody has picked answered no,
                // and the table yields on no room *before* it reaches the branch that would
                // have picked one. A fresh villager stood still saying its bag was full.
                bagRoom: repairing || (recipe != null ? Room(bag, recipe) > 0 : Space(bag)),
                tired: false);

            // Anything held that this recipe does not need is finished work, and a hauler is
            // told so. Recomputed each tick rather than latched when a craft lands, because the
            // bag changes for reasons this job does not see - a hauler taking half of it, a
            // player helping themselves - and a stale advertisement sends every hauler in the
            // settlement to an empty person.
            state.SetHasGoods(Goods(bag, needs));

            CraftStep step = CraftTransitions.Next((CraftState)state.WorkState, facts);

            switch (step.Action)
            {
                case CraftAction.Yield:
                    return JobOutcomes.Skipped(state, WhyNothing(facts), out activity);

                case CraftAction.ChooseWork:
                    return Record(state, step, Choose(context, needs, out activity));

                case CraftAction.MoveToSupply:
                    BeginLeg(context, CraftState.Fetching);
                    return Record(state, step, Walk(context, supply, "fetching", out activity));

                case CraftAction.Collect:
                    return Record(state, step, Collect(context, supply, needs, repairing, out activity));

                case CraftAction.MoveToStation:
                    BeginLeg(context, CraftState.Delivering);
                    return Record(state, step, Walk(context, target, "off to make " +
                        ItemCatalogue.Label(product), out activity));

                case CraftAction.Craft:
                    return Record(state, step, repairing
                        ? Mend(context, station, out activity)
                        : Make(context, station, recipe, needs, out activity));

                case CraftAction.Complete:
                    Rest(context);
                    return JobOutcomes.Completed(state, "done crafting", out activity);

                default:
                    // An action this engine does not handle is a programming error, not a world
                    // state. Say so rather than silently idling.
                    return JobOutcomes.Failed(state, "unhandled craft action", out activity);
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
        private static JobResult Record(VillagerState state, CraftStep step, JobResult result)
        {
            if (result != JobResult.Running && result != JobResult.Completed) return result;

            state.SetWorkState((int)step.Next);
            return JobResult.Running;
        }

        /// <summary>
        ///     Picks a station with something outstanding, and where its materials are.
        /// </summary>
        /// <remarks>
        ///     Keeps a station it already holds. A villager part-way through gathering a recipe
        ///     has a claim and a half-filled bag, and re-choosing would drop both - it would let
        ///     go of the forge it is shopping for and be as likely to come back to a different
        ///     one, carrying materials for a recipe nobody there is making.
        /// </remarks>
        private static JobResult Choose(CraftContext context, List<CraftNeed> needs, out string activity)
        {
            VillagerState state = context.State;
            Vector3 here = context.Villager.transform.position;

            // Still shopping for a station already claimed: find the next thing it is short of
            // rather than starting over.
            StructureRecord held = SettlementIndex.Find(context.Colony, state.Target);
            if (held != null && needs != null && Wanted(context.Colony, held, state.Cargo))
            {
                string short_of = CraftPlan.Missing(needs, item => Held(context.Bag.GetInventory(), item));
                if (short_of.Length > 0 && Point(context, short_of, here))
                {
                    activity = "fetching " + ItemCatalogue.Label(short_of);
                    return JobResult.Running;
                }
            }

            List<WorkArea> areas = new List<WorkArea>();
            WorkArea.AllFor(context.Colony, context.Job, areas);

            // What a station wanted that nowhere had, kept for the villager to say afterwards
            // rather than announced. A settlement short of iron is a standing state and not an
            // event, and a message that interrupts is wrong for something still true in ten
            // minutes.
            string missing = string.Empty;

            foreach (WorkArea area in areas)
            {
                foreach (StructureRecord record in Stations(context, area))
                {
                    foreach (StructureOrder order in Outstanding(context.Colony, record))
                    {
                        Recipe recipe = CraftCatalogue.RecipeFor(order.Item);
                        if (recipe == null) continue;

                        // Answered here rather than on arrival, which saves a walk to a bench
                        // that cannot do the work - and the level is the *station's* rather than
                        // the prefab's, so this is also the first point it can be answered at
                        // all. An order can outlive the reason it made sense: a station renamed
                        // by a game update, or a basic recipe on a bench that stopped offering
                        // them, would otherwise be fetched for and carried to for ever.
                        CraftStation workshop = Station(ZNetScene.instance != null
                            ? ZNetScene.instance.FindInstance(record.Id)
                            : null);

                        if (workshop == null) continue;
                        if (!CraftCatalogue.MadeAt(recipe, workshop.StationName, workshop.ShowsBasic)) continue;
                        if (!Level(workshop, recipe)) continue;

                        List<CraftNeed> wanted = Needs(recipe);
                        Inventory bag = context.Bag.GetInventory();

                        if (Room(bag, recipe) <= 0) continue;

                        // Already holding everything it needs - a chest passed on the way, or a
                        // reload. Nothing to fetch; go and make it.
                        if (CraftPlan.Enough(wanted, item => Held(bag, item)))
                        {
                            Begin(context, record.Id, order.Item);
                            activity = "off to make " + ItemCatalogue.Label(order.Item);
                            return JobResult.Running;
                        }

                        string first = CraftPlan.Missing(wanted, item => Held(bag, item));
                        if (first.Length == 0) continue;

                        StructureRecord from = Keeping(context, first, here);
                        if (from == null)
                        {
                            if (missing.Length == 0) missing = first;
                            continue;
                        }

                        Begin(context, record.Id, order.Item);
                        state.SetDestination(from.Id);
                        activity = "fetching " + ItemCatalogue.Label(first);
                        return JobResult.Running;
                    }
                }
            }

            // Nothing to make anywhere. Only now is mending worth looking for: crafting is
            // what a player asked for by writing an order, and a settlement that mended axes
            // while a standing order went unmade would be answering a question nobody asked.
            if (Repairing.Find(context.Colony, areas, context.Villager,
                    out StructureRecord bench, out StructureRecord kept, out string worn))
            {
                Begin(context, bench.Id, RepairMark + worn);
                state.SetDestination(kept.Id);
                activity = "fetching " + ItemCatalogue.Label(worn) + " to mend";
                return JobResult.Running;
            }

            // Nothing to make. Anything carried is left in the bag for a hauler, which is where
            // it was always going to end up.
            state.ClearTarget();
            state.SetDestination(ZDOID.None);
            state.SetCargo(string.Empty);
            return JobOutcomes.Skipped(state, Nothing(context, missing), out activity);
        }

        /// <summary>Takes a station and remembers what this trip is for.</summary>
        private static void Begin(CraftContext context, ZDOID station, string product)
        {
            // A new target is a new walk and a new tolerance. Without the walk being told, its
            // stall clock still holds the last target's timings and judges the first step of
            // this one as already stuck.
            context.Walk.Forget();
            context.Walk.NewLeg();
            context.State.SetTarget(station);
            context.State.SetCargo(product);
        }

        private static bool Point(CraftContext context, string item, Vector3 here)
        {
            StructureRecord from = Keeping(context, item, here);
            if (from == null) return false;

            context.State.SetDestination(from.Id);
            return true;
        }

        /// <summary>Stations this job may work, in the order the settlement offers them.</summary>
        private static IEnumerable<StructureRecord> Stations(CraftContext context, WorkArea area)
        {
            foreach (StructureRecord record in context.Colony.State.GetStructures())
            {
                if ((record.Capabilities & StructureCapability.Crafting) == 0) continue;
                if (!record.WorkableIn(context.Colony)) continue;
                if (Unreachable.Refuses(context.Villager.Id, record.Id)) continue;
                if (TargetClaims.IsClaimedByOther(record.Id, context.Villager)) continue;

                ZDO zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(record.Id) : null;
                if (zdo == null || !zdo.IsValid() || !area.Contains(zdo.GetPosition())) continue;

                // Usable, and not merely registered. Without this the table would refuse a
                // station whose fire had gone out, send the villager back to choosing, and get
                // handed the same station again - a loaded crafter standing at a cold forge
                // for ever, reporting that it was off to make something. The table can only
                // refuse; refusing has to mean something here or it means nothing.
                GameObject instance = ZNetScene.instance != null
                    ? ZNetScene.instance.FindInstance(record.Id)
                    : null;

                if (instance == null || !CraftProbe.TryFind(instance, out CraftStation usable)) continue;
                if (!usable.Usable(out string _)) continue;

                yield return record;
            }
        }

        private static List<StructureOrder> Outstanding(Colony colony, StructureRecord record) =>
            Orders.Outstanding(record.Settings.Orders, item => Stock.Held(colony, item));

        /// <summary>Whether this station still wants this item made.</summary>
        private static bool Wanted(Colony colony, StructureRecord record, string product)
        {
            if (record == null || string.IsNullOrEmpty(product)) return false;

            foreach (StructureOrder order in record.Settings.Orders)
            {
                if (order.Item != product) continue;
                return order.Wants && !order.SatisfiedBy(Stock.Held(colony, product));
            }

            return false;
        }

        /// <summary>The nearest container the settlement may take this out of.</summary>
        private static StructureRecord Keeping(CraftContext context, string prefab, Vector3 here)
        {
            foreach (StructureRecord record in SettlementIndex.WhereIsItKept(context.Colony, prefab, here))
            {
                if (Unreachable.Refuses(context.Villager.Id, record.Id)) continue;
                return record;
            }

            return null;
        }

        /// <summary>
        ///     Takes everything this chest has of what the recipe still needs.
        /// </summary>
        /// <remarks>
        ///     Everything it has, rather than one item: a recipe wants several things and the
        ///     chest holding one of them often holds another. Clearing the destination when the
        ///     chest has nothing more sends the villager back to choosing, which points it at a
        ///     chest for whatever it is still short of - the station stays claimed throughout.
        /// </remarks>
        private static JobResult Collect(CraftContext context, GameObject chest,
            List<CraftNeed> needs, bool repairing, out string activity)
        {
            VillagerState state = context.State;

            if (chest == null || needs == null || needs.Count == 0)
            {
                state.SetDestination(ZDOID.None);
                activity = "it was gone";
                return JobResult.Running;
            }

            Container container = chest.GetComponentInChildren<Container>(true);
            Inventory from = container != null ? container.GetInventory() : null;
            Inventory bag = context.Bag.GetInventory();

            if (from == null)
            {
                state.SetDestination(ZDOID.None);
                activity = "it was gone";
                return JobResult.Running;
            }

            bool took = false;
            foreach (CraftNeed need in needs)
            {
                int short_of = need.Amount -
                               (repairing ? HeldWorn(bag, need.Item) : Held(bag, need.Item));
                if (short_of <= 0) continue;

                // isPrefabName, or this finds nothing at all: Inventory.GetItem matches the
                // localised display name by default - "$item_wood", not "Wood" - and a recipe's
                // requirements are read in prefab names here. Without it a villager stands at a
                // full chest and reports the iron gone.
                while (short_of > 0)
                {
                    // A repair fetches the *worn* one. Asking by name alone would as happily
                    // carry a pristine axe to the bench, find nothing to mend, and carry it
                    // back - which looks exactly like a villager doing something useful.
                    ItemDrop.ItemData item = repairing
                        ? Damaged(from, need.Item)
                        : from.GetItem(need.Item, isPrefabName: true);

                    if (item == null) break;

                    switch (Carrying.TakeFromContainer(container, item, bag, out string _))
                    {
                        case TakeResult.Took:
                            took = true;
                            short_of = need.Amount -
                                       (repairing ? HeldWorn(bag, need.Item) : Held(bag, need.Item));
                            continue;

                        default:
                            short_of = 0;
                            continue;
                    }
                }
            }

            // Nothing more here, whether or not the recipe is complete. Choosing decides what
            // happens next: another chest for what is still missing, or the walk to the station.
            state.SetDestination(ZDOID.None);
            activity = took ? "gathering" : "nothing here";
            return JobResult.Running;
        }

        /// <summary>
        ///     Makes one, consuming the materials first.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>Counted either side, because the removal cannot be trusted to fail.</b>
        ///         <c>Inventory.RemoveItem</c> returns <c>void</c>, and it silently skips any
        ///         item whose <c>m_worldLevel</c> is below the world's - so on an NG+ world a
        ///         villager would consume nothing, produce everything, and no call would report
        ///         an error. Everything is checked against that same rule before anything is
        ///         removed, and the removal is measured afterwards.
        ///     </para>
        ///     <para>
        ///         <b>Consume before producing</b>, never after: a production that fails after
        ///         the materials are gone costs materials, while a consumption that fails before
        ///         the product exists costs nothing.
        ///     </para>
        /// </remarks>
        private static JobResult Make(CraftContext context, CraftStation station, Recipe recipe,
            List<CraftNeed> needs, out string activity)
        {
            VillagerState state = context.State;

            if (station == null || recipe == null)
            {
                state.ClearTarget();
                activity = "it was gone";
                return JobResult.Running;
            }

            if (!station.Usable(out string why))
            {
                // Not a failure and not the villager's fault. Another station may be usable, so
                // this lets go and says what was wrong with this one.
                state.ClearTarget();
                activity = why;
                return JobResult.Running;
            }

            // Paced, so a craft is something a player can watch rather than a hundred nails in
            // five seconds. The station's own in-use visual runs for the same reason.
            ZDOID who = context.Villager.Id;
            station.PokeInUse();
            context.Animation.Crafting(station.UseAnimation);

            if (NextCraft.TryGetValue(who, out float next) && Time.time < next)
            {
                activity = "making " + ItemCatalogue.Label(state.Cargo);
                return JobResult.Running;
            }

            NextCraft[who] = Time.time + SecondsPerCraft;

            Inventory bag = context.Bag.GetInventory();

            // Everything, before anything. A partial consumption would destroy materials for a
            // craft that never happened.
            foreach (CraftNeed need in needs)
            {
                if (Held(bag, need.Item) < need.Amount)
                {
                    activity = "short of " + ItemCatalogue.Label(need.Item);
                    return JobResult.Running;
                }
            }

            foreach (Piece.Requirement requirement in recipe.m_resources)
            {
                if (requirement?.m_resItem == null) continue;

                int amount = requirement.GetAmount(1);
                if (amount <= 0) continue;

                string named = requirement.m_resItem.m_itemData.m_shared.m_name;
                int before = HeldByName(bag, named);

                // By the shared name and not the prefab name, because that is what vanilla's
                // own ConsumeResources matches on - the two are different spellings of the same
                // item and only one of them works here.
                bag.RemoveItem(named, amount);

                int after = HeldByName(bag, named);
                if (before - after >= amount) continue;

                // The removal did not land. Said loudly, because the alternative is a
                // settlement quietly crafting for free.
                Log.Warning($"[craft] {state.Name} could not spend {amount} {named} " +
                            $"({before} to {after}); nothing was made.");
                state.ClearTarget();
                activity = "could not spend the materials";
                return JobResult.Running;
            }

            int made = Mathf.Max(1, recipe.m_amount);
            string product = recipe.m_item.gameObject.name;

            // Named as its maker, so a village's gear says who made it. Quality 1 always -
            // villagers do not upgrade.
            bag.AddItem(product, made, 1, 0, who.UserID, state.Name, cheated: false);

            Report.Say($"{state.Name} made {made} {ItemCatalogue.Label(product)}.");
            Settle(context.Colony, state.Target, product);

            activity = "making " + ItemCatalogue.Label(product);
            return JobResult.Running;
        }

        /// <summary>
        ///     Mends one worn thing at a station that mends.
        /// </summary>
        /// <remarks>
        ///     Paced like a craft, and for the same reason: a villager that silently restored a
        ///     chest's worth of gear in one frame would be indistinguishable from one that did
        ///     nothing at all. Costs no materials, which is vanilla's rule rather than a
        ///     simplification of it.
        /// </remarks>
        private static JobResult Mend(CraftContext context, CraftStation station, out string activity)
        {
            VillagerState state = context.State;

            if (station == null)
            {
                state.ClearTarget();
                activity = "it was gone";
                return JobResult.Running;
            }

            if (!station.Usable(out string why))
            {
                state.ClearTarget();
                activity = why;
                return JobResult.Running;
            }

            ZDOID who = context.Villager.Id;
            station.PokeInUse();
            context.Animation.Crafting(station.UseAnimation);

            if (NextCraft.TryGetValue(who, out float next) && Time.time < next)
            {
                activity = "mending";
                return JobResult.Running;
            }

            NextCraft[who] = Time.time + SecondsPerCraft;

            if (!Repairing.Mend(context.Bag.GetInventory(), station, out string mended))
            {
                // Nothing left worn. The trip is over, and the gear goes back in a chest the way
                // everything else a villager holds does - a hauler collects it.
                state.SetCargo(string.Empty);
                activity = "nothing left to mend";
                return JobResult.Running;
            }

            Report.Say($"{state.Name} mended {Localization.instance.Localize(mended)}.");
            activity = "mending";
            return JobResult.Running;
        }

        /// <summary>
        ///     Marks a one-off order finished, once it is.
        /// </summary>
        /// <remarks>
        ///     A latch rather than a recomputation, because "have we made fifty" cannot be
        ///     answered by looking at the settlement: fifty arrows made and fifty arrows fired
        ///     leave no trace. Written here - at the moment work lands, which is rare - rather
        ///     than from the predicates that read it, which run every tick.
        /// </remarks>
        private static void Settle(Colony colony, ZDOID station, string product)
        {
            StructureRecord record = SettlementIndex.Find(colony, station);
            StructureOrder order = record?.Settings.Orders.Find(o => o.Item == product);

            if (order == null || order.Mode != OrderMode.Once || order.Done) return;
            if (!order.SatisfiedBy(Stock.Held(colony, product))) return;

            ColonyOperations.EditSettings(colony, station, s =>
            {
                StructureOrder saved = s.Orders.Find(o => o.Item == product);
                if (saved != null) saved.Done = true;
            });

            Report.Say($"{record.Name} has made the {ItemCatalogue.Label(product)} that were asked for.");
        }

        /// <summary>Stops the crafting animation when the villager stops crafting.</summary>
        private static void Rest(CraftContext context)
        {
            context.Animation.Crafting(0);
            NextCraft.Remove(context.Villager.Id);
        }

        /// <summary>
        ///     Why there is nothing to do, said in the villager's own words.
        /// </summary>
        /// <remarks>
        ///     Several silences reach one yield, and "nothing to craft" for all of them is how a
        ///     player comes to believe the job is broken when it is waiting for a hauler. It
        ///     goes in the villager's activity rather than on the message line, because it is a
        ///     standing state rather than an event.
        /// </remarks>
        private static string WhyNothing(CraftFacts facts) =>
            facts.BagRoom ? "nothing to craft" : "bag full, waiting to be collected";

        private static string Nothing(CraftContext context, string missing)
        {
            if (missing.Length > 0)
            {
                // Named as a chest and not as an absence: iron on the ground or in a player's
                // own pockets is not somewhere a villager may take from.
                return $"no {ItemCatalogue.Label(missing)} in any chest";
            }

            return Full(context) ? "bag full, waiting to be collected" : "nothing to craft";
        }

        private static bool Full(CraftContext context)
        {
            Inventory bag = context.Bag.GetInventory();
            return bag != null && bag.GetEmptySlots() <= 0;
        }

        /// <summary>
        ///     Whether the bag holds anything that is not materials for the job in hand.
        /// </summary>
        /// <remarks>
        ///     The definition of "finished goods", and deliberately a wide one. Anything a
        ///     crafter is holding that its current recipe does not call for is something it is
        ///     not going to use, whether that is the nails it just made or a stray apple - and
        ///     both are better in a chest than in a working villager's bag.
        /// </remarks>
        private static bool Goods(Inventory bag, List<CraftNeed> needs)
        {
            if (bag == null) return false;

            foreach (ItemDrop.ItemData item in bag.GetAllItems())
            {
                string prefab = Carrying.NameOf(item);
                if (string.IsNullOrEmpty(prefab)) continue;

                bool material = false;
                if (needs != null)
                {
                    foreach (CraftNeed need in needs)
                    {
                        if (need.Item != prefab) continue;
                        material = true;
                        break;
                    }
                }

                if (!material) return true;
            }

            return false;
        }

        /// <summary>The first worn one of a kind in a chest, or null when they are all sound.</summary>
        private static ItemDrop.ItemData Damaged(Inventory inventory, string prefab)
        {
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
            {
                if (Carrying.NameOf(item) != prefab) continue;
                if (Repairing.Worn(item)) return item;
            }

            return null;
        }

        /// <summary>What one craft consumes, in prefab names.</summary>
        private static List<CraftNeed> Needs(Recipe recipe)
        {
            List<CraftNeed> needs = new List<CraftNeed>();
            if (recipe?.m_resources == null) return needs;

            foreach (Piece.Requirement requirement in recipe.m_resources)
            {
                if (requirement?.m_resItem == null) continue;

                int amount = requirement.GetAmount(1);
                if (amount > 0) needs.Add(new CraftNeed(requirement.m_resItem.gameObject.name, amount));
            }

            return needs;
        }

        /// <summary>
        ///     How much of an item is held, under the same rule that removing it will use.
        /// </summary>
        /// <remarks>
        ///     The world-level test is the point. <c>Inventory.RemoveItem</c> skips anything
        ///     below <c>Game.m_worldLevel</c>, so counting without it would say the materials
        ///     are there, the removal would take nothing, and the craft would be free.
        /// </remarks>
        private static int Held(Inventory inventory, string prefab)
        {
            if (inventory == null || string.IsNullOrEmpty(prefab)) return 0;

            int total = 0;
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
            {
                if (Carrying.NameOf(item) != prefab) continue;
                if (item.m_worldLevel < Game.m_worldLevel) continue;

                total += item.m_stack;
            }

            return total;
        }

        /// <summary>
        ///     How much is held under the name <c>RemoveItem</c> will match on.
        /// </summary>
        /// <remarks>
        ///     The shared name, not the prefab name, because that is what the removal compares -
        ///     and the removal is what this is used to verify. Two prefabs can share one shared
        ///     name, so counting by prefab either side of a removal that matched by shared name
        ///     would report a spend that did not happen and a spend that did as a failure.
        /// </remarks>
        private static int HeldByName(Inventory inventory, string sharedName)
        {
            if (inventory == null || string.IsNullOrEmpty(sharedName)) return 0;

            int total = 0;
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
            {
                if (item?.m_shared == null || item.m_shared.m_name != sharedName) continue;
                if (item.m_worldLevel < Game.m_worldLevel) continue;

                total += item.m_stack;
            }

            return total;
        }

        /// <summary>How many worn copies of a thing are held, for a repair trip.</summary>
        private static int HeldWorn(Inventory inventory, string prefab)
        {
            if (inventory == null || string.IsNullOrEmpty(prefab)) return 0;

            int worn = 0;
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
            {
                if (Carrying.NameOf(item) == prefab && Repairing.Worn(item)) worn++;
            }

            return worn;
        }

        /// <summary>Whether the bag has any room at all, for when nothing has been chosen yet.</summary>
        private static bool Space(Inventory bag) => bag != null && bag.GetEmptySlots() > 0;

        /// <summary>
        ///     Whether a villager is holding this for work in hand rather than for collection.
        /// </summary>
        /// <remarks>
        ///     Asked by a hauler before it takes anything off a crafter. The advertising side
        ///     already excludes materials, but advertising and taking are two decisions and only
        ///     one of them had the guard: a crafter holding iron for nails and the nails it had
        ///     just made would advertise correctly and then be relieved of the iron, walk back
        ///     to the chest the hauler had just filed it into, and fetch it again.
        /// </remarks>
        internal static bool Reserved(Villager carrier, string prefab)
        {
            if (carrier == null || string.IsNullOrEmpty(prefab)) return false;

            VillagerState state = carrier.State;
            if (!state.IsValid) return false;

            string cargo = state.Cargo ?? string.Empty;
            if (cargo.Length == 0) return false;

            if (cargo.StartsWith(RepairMark, System.StringComparison.Ordinal))
            {
                return cargo.Substring(RepairMark.Length) == prefab;
            }

            foreach (CraftNeed need in Needs(CraftCatalogue.RecipeFor(cargo)))
            {
                if (need.Item == prefab) return true;
            }

            return false;
        }

        /// <summary>How many of the product there is room to carry.</summary>
        private static int Room(Inventory bag, Recipe recipe)
        {
            if (bag == null || recipe?.m_item == null) return 0;

            int made = Mathf.Max(1, recipe.m_amount);
            return bag.CanAddItem(recipe.m_item.gameObject, made) ? made : 0;
        }

        /// <summary>Whether the station is good enough for this recipe.</summary>
        /// <remarks>
        ///     Clamped to four, as vanilla clamps it, or a villager would refuse a craft the
        ///     player can do standing at the same bench.
        /// </remarks>
        private static bool Level(CraftStation station, Recipe recipe) =>
            recipe == null || station == null ||
            Mathf.Min(station.Level, HighestLevel) >= recipe.GetRequiredStationLevel(1);

        private static CraftStation Station(GameObject instance) =>
            instance != null && CraftProbe.TryFind(instance, out CraftStation found) ? found : null;

        private static void BeginLeg(CraftContext context, CraftState leg)
        {
            if (CraftLegs.Entering((CraftState)context.State.WorkState, leg)) context.Walk.NewLeg();
        }

        private static JobResult Walk(CraftContext context, GameObject target, string doing,
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
                    return JobOutcomes.Skipped(context.State, "cannot get there", out activity);

                default:
                    activity = doing;
                    return JobResult.Running;
            }
        }

        private static ZDOID Nearest(CraftContext context, GameObject thing) =>
            thing != null && thing.TryGetComponent(out ZNetView view) && view.IsValid()
                ? view.GetZDO().m_uid
                : ZDOID.None;

        private static bool Within(CraftContext context, GameObject thing)
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
    }
}
