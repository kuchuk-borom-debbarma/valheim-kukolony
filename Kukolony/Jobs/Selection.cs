using System.Collections.Generic;
using Kukolony.Colonies;
using Kukolony.Villagers;
using UnityEngine;

namespace Kukolony.Jobs
{
    /// <summary>
    ///     Choosing what to work on, and where the result goes.
    /// </summary>
    /// <remarks>
    ///     Kept apart from the job that uses it. The previous engine put selection, movement,
    ///     inventory and station adapters in one class that every job called back into
    ///     statically; it reached seven hundred lines and the interface stayed clean while the
    ///     thing behind it did not.
    /// </remarks>
    internal static class Selection
    {
        /// <summary>
        ///     A villager standing about with finished goods, and somewhere for them to go.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         What a crafter makes stays in its own bag, so somebody has to come and get
        ///         it. This is that somebody, and it is asked <em>first</em> - before the ground
        ///         and before the chests - because a crafter with a full bag has stopped
        ///         working, and everything else a hauler might do can wait a minute longer than
        ///         that can.
        ///     </para>
        ///     <para>
        ///         <b>Still bounded by the job's work areas</b>, through the same
        ///         <see cref="WorkArea.AllFor" /> every other search uses. Being first in
        ///         priority is not a licence to cross the map: a hauler working the settlement
        ///         should not walk to an outpost for one nail, and the areas are what say so.
        ///     </para>
        ///     <para>
        ///         <b>Only villagers that advertise.</b> A bag with things in it is not the same
        ///         question - a tender carrying coal to a kiln has a full bag and would be
        ///         stripped of its errand by the first hauler to pass. The carrier says when
        ///         what it holds is finished and for somebody else to carry.
        ///     </para>
        /// </remarks>
        internal static bool TryFindCarriedWork(Colony colony, JobDefinition job, Villager asker,
            out Villager carrier, out StructureRecord destination)
        {
            carrier = null;
            destination = null;
            if (colony == null || asker == null) return false;

            List<WorkArea> areas = new List<WorkArea>();
            WorkArea.AllFor(colony, job, asker, areas);

            Vector3 here = asker.transform.position;

            foreach (WorkArea area in areas)
            {
                Villager nearest = null;
                StructureRecord home = null;
                float closest = float.MaxValue;

                foreach (Villager other in Villager.Instances)
                {
                    if (other == null || ReferenceEquals(other, asker)) continue;

                    VillagerState theirs = other.State;
                    if (!theirs.IsValid || !theirs.HasGoods) continue;

                    // Ours. The instance list is every villager in the world, and two
                    // settlements can stand close enough for one's work area to cover the
                    // other's yard - at which point a hauler would quietly empty a neighbour's
                    // crafter into its own chests.
                    if (!Mine(colony, other)) continue;

                    Vector3 there = other.transform.position;
                    if (!area.Contains(there)) continue;

                    if (Unreachable.Refuses(asker.Id, other.Id)) continue;
                    if (TargetClaims.IsClaimedByOther(other.Id, asker)) continue;

                    float distance = Utils.DistanceXZ(here, there);
                    if (distance >= closest) continue;

                    // A destination before the walk, as everywhere else here: arriving and
                    // discovering nothing wants what they are holding fails the job after a
                    // walk, which is a worse answer than choosing something else.
                    StructureRecord goes = Somewhere(colony, job, other, there, asker.Id);
                    if (goes == null) continue;

                    nearest = other;
                    home = goes;
                    closest = distance;
                }

                if (nearest == null) continue;

                carrier = nearest;
                destination = home;
                return true;
            }

            return false;
        }

        /// <summary>Whether a villager belongs to this colony.</summary>
        private static bool Mine(Colony colony, Villager villager)
        {
            ZDO zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(villager.Id) : null;
            return zdo != null && ColonyMembership.BelongsTo(zdo, colony.Id);
        }

        /// <summary>Somewhere for the first thing in a carrier's bag that this job handles.</summary>
        private static StructureRecord Somewhere(Colony colony, JobDefinition job, Villager carrier,
            Vector3 there, ZDOID asker)
        {
            Container bag = carrier.Bag;
            Inventory inventory = bag != null ? bag.GetInventory() : null;
            if (inventory == null) return null;

            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
            {
                if (!Collectable(colony, job, carrier, item, there, asker, out StructureRecord goes)) continue;
                return goes;
            }

            return null;
        }

        /// <summary>
        ///     Whether one thing in a carrier's bag is something this job may take, and where to.
        /// </summary>
        /// <remarks>
        ///     <b>One question, asked by the chooser and again on arrival.</b> When the two were
        ///     written separately they disagreed: the chooser accepted a chest that takes
        ///     unclaimed oddments, the collector demanded the chest name the item, so a hauler
        ///     walked to a crafter, found nothing it was willing to take, let go, and - because
        ///     collecting is the first thing it looks for - immediately chose the same carrier
        ///     again. It never did another piece of work.
        /// </remarks>
        internal static bool Collectable(Colony colony, JobDefinition job, Villager carrier,
            ItemDrop.ItemData item, Vector3 there, ZDOID asker, out StructureRecord goes)
        {
            goes = null;

            string prefab = Carrying.NameOf(item);
            if (string.IsNullOrEmpty(prefab)) return false;

            // The job's own item list narrows this, so a hauler told to move wood does not
            // empty a crafter's bag of nails.
            if (!Handles(job, prefab)) return false;

            // And what the crafter is holding to work with is not finished goods, whatever its
            // advertisement says. The advertising side already knew this; the taking side did
            // not, so a hauler relieved a crafter of the iron it had just fetched.
            if (Craft.CraftJob.Reserved(carrier, prefab)) return false;

            foreach (StructureRecord record in SettlementIndex.WhereDoesItGo(colony, prefab, there, asker))
            {
                goes = record;
                return true;
            }

            return false;
        }

        /// <summary>
        ///     The nearest loose item worth hauling, with somewhere for it to go.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Distance is measured from the villager, but candidates are bounded by the
        ///         colony's reach rather than the villager's whim, so a settlement works its own
        ///         ground instead of wandering after whatever happens to be closest.
        ///     </para>
        ///     <para>
        ///         A destination is required <em>before</em> the villager sets off. Choosing an
        ///         item and discovering on arrival that nothing wants it fails the job after a
        ///         walk, which is a worse answer than choosing something else.
        ///     </para>
        ///     <para>
        ///         <b>The areas are tried in the order the job lists them.</b> Nearest wins
        ///         within an area, and a later area is only looked at once the ones before it
        ///         have nothing - which is what makes "here first, then the outpost" mean
        ///         something. Pooling them and taking the globally nearest would make the
        ///         order on screen decorative.
        ///     </para>
        /// </remarks>
        internal static bool TryFindGroundWork(Colony colony, JobDefinition job, Villager asker,
            out ItemDrop item, out StructureRecord destination, StructureRecord boundFor = null)
        {
            item = null;
            destination = null;
            if (colony == null || asker == null) return false;

            List<WorkArea> areas = new List<WorkArea>();
            WorkArea.AllFor(colony, job, asker, areas);

            ItemDrop nearestItem = null;
            StructureRecord nearestHome = null;
            float nearest = float.MaxValue;

            foreach (WorkArea area in areas)
            {
                if (!TryFindGroundWorkIn(colony, job, asker, area, boundFor, out ItemDrop found,
                        out StructureRecord home, out float distance))
                {
                    continue;
                }

                // Choosing fresh: the first area with anything wins, which is what the order
                // on the screen promises.
                if (boundFor == null)
                {
                    item = found;
                    destination = home;
                    return true;
                }

                // Topping up a load already bound somewhere is the exception, and it takes
                // the nearest across every area. Area order here retargeted a villager
                // standing in the far quarry with a half-full bag to a single item that had
                // just dropped at home, then back again - a hundred and fifty metres each
                // way for one ore.
                if (distance >= nearest) continue;

                nearest = distance;
                nearestItem = found;
                nearestHome = home;
            }

            item = nearestItem;
            destination = nearestHome;
            return item != null;
        }

        /// <summary>The nearest thing worth hauling inside one area, and how far off it is.</summary>
        private static bool TryFindGroundWorkIn(Colony colony, JobDefinition job, Villager asker,
            WorkArea area, StructureRecord boundFor, out ItemDrop item, out StructureRecord destination,
            out float away)
        {
            item = null;
            destination = null;

            Vector3 from = asker.transform.position;
            float best = float.MaxValue;

            // The game keeps its own registry of loose items, so this never walks every loaded
            // object. It holds only what is loaded, which is the right meaning of "nearby".
            foreach (ItemDrop drop in ItemDrop.s_instances)
            {
                if (drop == null || drop.m_itemData?.m_dropPrefab == null) continue;

                // Bounded by the job's work area rather than by the settlement. A job pointed at
                // an outpost gathers there and nowhere else, which is the whole point of being
                // able to point one.
                if (!area.Contains(drop.transform.position)) continue;

                if (!drop.TryGetComponent(out ZNetView view) || !view.IsValid()) continue;

                string prefab = Utils.GetPrefabName(drop.m_itemData.m_dropPrefab);
                if (!Wanted(job, prefab)) continue;

                // Refused for now: this villager has already failed to walk to it. Read from
                // the view validated above rather than fetching it again - and skipping the
                // item rather than the check, so a component that cannot be read is not
                // quietly treated as reachable.
                if (Unreachable.Refuses(asker.Id, view.GetZDO().m_uid)) continue;

                ZDOID id = view.GetZDO().m_uid;
                if (TargetClaims.IsClaimedByOther(id, asker)) continue;

                float distance = Utils.DistanceXZ(drop.transform.position, from);
                if (distance >= best) continue;

                List<StructureRecord> homes = SettlementIndex.WhereDoesItGo(colony, prefab, from, asker.Id);
                if (homes.Count == 0) continue;

                // Topping up a load already bound somewhere: only things going to that same
                // chest count, so one trip keeps one destination.
                if (boundFor != null && homes[0].Id != boundFor.Id) continue;

                best = distance;
                item = drop;
                destination = homes[0];
            }

            away = best;
            return item != null;
        }

        /// <summary>
        ///     Where a carried item should go, or null if nothing will take it.
        /// </summary>
        internal static StructureRecord WhereFor(Colony colony, string itemPrefab, Vector3 from,
            ZDOID asker = default)
        {
            List<StructureRecord> homes = SettlementIndex.WhereDoesItGo(colony, itemPrefab, from, asker);
            return homes.Count == 0 ? null : homes[0];
        }

        /// <summary>
        ///     The worst-placed item in the settlement's own containers, and where it belongs.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Ranked by how much the move improves things rather than by how near it is, so
        ///         a villager fixes real mistakes before minor ones: a flint in the wood chest -
        ///         somewhere that actively refuses it - outranks wood sitting in an overflow
        ///         chest, and is worth walking further for. Distance breaks ties, because two
        ///         equally wrong items are both worth fixing and the walk is then the only thing
        ///         that separates them.
        ///     </para>
        ///     <para>
        ///         <b>This is where the shuffle loop would live.</b> Nothing is chosen unless
        ///         <see cref="Placement.MayMove" /> allows it, so two chests that both name wood
        ///         never trade with each other and a settlement with everything in its right
        ///         place finds no work at all - which is the behaviour worth proving.
        ///     </para>
        ///     <para>
        ///         A source chest is claimed whole. One villager tidies one chest, which reuses
        ///         the existing per-object claim exactly and makes it impossible for two
        ///         villagers to walk to the same stack.
        ///     </para>
        /// </remarks>
        internal static bool TryFindContainerWork(Colony colony, JobDefinition job, Villager asker,
            out GameObject source, out StructureRecord destination)
        {
            source = null;
            destination = null;
            if (colony == null || asker == null || job == null || !job.TidyContainers) return false;

            // In the job's own order, as loose items are: a settlement that keeps its
            // outpost's chests tidy only once its own are is the same promise the row makes.
            List<WorkArea> areas = new List<WorkArea>();
            WorkArea.AllFor(colony, job, asker, areas);

            foreach (WorkArea area in areas)
            {
                if (TryFindContainerWorkIn(colony, job, asker, area, out source, out destination))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The worst-placed item in one area's containers.</summary>
        private static bool TryFindContainerWorkIn(Colony colony, JobDefinition job, Villager asker,
            WorkArea area, out GameObject source, out StructureRecord destination)
        {
            source = null;
            destination = null;

            Vector3 from = asker.transform.position;
            int bestImprovement = 0;
            float bestDistance = float.MaxValue;

            foreach (StructureRecord record in SettlementIndex.WhatMayBeTidied(colony))
            {
                GameObject instance = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(record.Id) : null;
                if (instance == null) continue;

                // Same rule as loose items: a job only tidies the containers it was pointed at.
                // Destinations are deliberately not bounded - see WorkArea.
                if (!area.Contains(instance.transform.position)) continue;
                if (TargetClaims.IsClaimedByOther(record.Id, asker)) continue;

                // And not something this villager has just spent two minutes failing to
                // reach. Giving up unblocks the queue; without this the next choice is the
                // same unreachable chest and the villager never gets past it.
                if (Unreachable.Refuses(asker.Id, record.Id)) continue;

                Container container = instance.GetComponentInChildren<Container>(true);
                Inventory inventory = container != null ? container.GetInventory() : null;
                if (inventory == null) continue;

                float distance = Utils.DistanceXZ(instance.transform.position, from);

                foreach (ItemDrop.ItemData held in inventory.GetAllItems())
                {
                    string prefab = Carrying.NameOf(held);
                    if (prefab.Length == 0 || !Wanted(job, prefab)) continue;

                    // Scored as where the item already is, so this chest's own cap does not
                    // count against the thing it is holding - which read as a reason to carry
                    // it out, and then straight back once the chest was under its cap again.
                    int here = SettlementIndex.ScoreOf(record, prefab, holding: true);

                    foreach (StructureRecord elsewhere in SettlementIndex.WhereDoesItGo(colony, prefab,
                                 instance.transform.position, asker.Id))
                    {
                        // A chest is never a move to itself, however well it scores.
                        if (elsewhere.Id == record.Id) continue;

                        int improvement = Placement.Improvement(here, SettlementIndex.ScoreOf(elsewhere, prefab));
                        if (improvement <= 0) continue;
                        if (improvement < bestImprovement) continue;
                        if (improvement == bestImprovement && distance >= bestDistance) continue;

                        bestImprovement = improvement;
                        bestDistance = distance;
                        source = instance;
                        destination = elsewhere;
                        break;
                    }
                }
            }

            return source != null;
        }

        /// <summary>
        ///     What may be taken out of this container and moved to that one, right now.
        /// </summary>
        /// <remarks>
        ///     Re-derived at the chest rather than remembered from when the trip was chosen. The
        ///     contents can change while the villager walks - another villager, the player, a
        ///     processing station - and a remembered item is a promise the world need not keep.
        ///     It also batches for free: everything in this chest bound for that one is taken in
        ///     the same visit.
        /// </remarks>
        internal static ItemDrop.ItemData WhatToTakeFrom(Colony colony, JobDefinition job,
            StructureRecord source, StructureRecord destination, Inventory contents)
        {
            if (colony == null || source == null || destination == null || contents == null) return null;

            foreach (ItemDrop.ItemData held in contents.GetAllItems())
            {
                string prefab = Carrying.NameOf(held);
                if (prefab.Length == 0 || !Wanted(job, prefab)) continue;

                // The same asymmetry the trip was chosen under: the source holds this item,
                // the destination is being offered it. Re-deriving both as destinations here
                // would have confirmed on arrival a trip that should never have been taken.
                if (Placement.MayMove(SettlementIndex.ScoreOf(source, prefab, holding: true),
                        SettlementIndex.ScoreOf(destination, prefab)))
                {
                    return held;
                }
            }

            return null;
        }

        /// <summary>
        ///     The first thing in a load that the settlement will actually take, and where.
        /// </summary>
        /// <remarks>
        ///     <b>The head of the load must not be able to block the rest of it.</b> Choosing
        ///     and depositing both used to take the first item held, which meant one oddment
        ///     nothing claimed - a flint among the firewood - parked itself at the front and the
        ///     villager reported "nowhere to put what I am carrying" forever, with a full load
        ///     of perfectly deliverable wood behind it. Skipping past it costs one pass over a
        ///     load that is at most a bag deep.
        /// </remarks>
        internal static bool FirstDeliverable(Colony colony, List<ItemDrop.ItemData> carried, Vector3 from,
            ZDOID asker,
            out ItemDrop.ItemData item, out StructureRecord home)
        {
            item = null;
            home = null;
            if (carried == null) return false;

            foreach (ItemDrop.ItemData held in carried)
            {
                StructureRecord where = WhereFor(colony, Carrying.NameOf(held), from, asker);
                if (where == null) continue;

                item = held;
                home = where;
                return true;
            }

            return false;
        }

        /// <summary>Whether a job handles this item. An empty list means everything.</summary>
        /// <summary>Whether a job handles this item at all. No list means everything.</summary>
        internal static bool Handles(JobDefinition job, string prefab) =>
            job?.Items == null || job.Items.Count == 0 || job.Items.Contains(prefab);

        private static bool Wanted(JobDefinition job, string prefab) => Handles(job, prefab);
    }
}
