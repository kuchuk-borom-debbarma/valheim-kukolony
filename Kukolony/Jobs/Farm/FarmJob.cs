using System.Collections.Generic;
using Kukolony.Colonies;
using Kukolony.Core;
using Kukolony.Gui;
using Kukolony.Resources;
using Kukolony.Resources.Farming;
using Kukolony.Villagers;
using Kukolony.Villagers.Navigation;
using UnityEngine;

namespace Kukolony.Jobs.Farm
{
    /// <summary>What one sowing tick needs. Assembled by the villager, never stored.</summary>
    internal sealed class FarmContext
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
    ///     Putting things in the ground, and keeping them there.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>The first job that consumes.</b> Everything else takes from the world and brings
    ///         it home; this takes from the settlement and puts it in the ground. So a villager
    ///         carries seed, and a settlement whose only seed is already planted is a real state
    ///         the job has to say clearly rather than stand in.
    ///     </para>
    ///     <para>
    ///         <b>The square is never remembered.</b> It is re-derived from the world every tick,
    ///         the same doctrine mining uses for a vein's parts and for the same reason: things
    ///         appear and vanish in a field that nobody here planted or picked, and anything
    ///         holding an opinion about which square was next would be wrong within seconds and
    ///         wrong in the way that looks like working.
    ///     </para>
    ///     <para>
    ///         <b>A field is shared, not claimed.</b> Whether something is exclusive is decided by
    ///         who asks, and this does not ask - a field is a patch of ground with hundreds of
    ///         squares in it, and the point of marking out a big one is that several people can
    ///         work it. They keep off each other's ground by starting their scan of the grid at
    ///         different places, which costs nothing and needs no claim to go stale.
    ///     </para>
    /// </remarks>
    internal static class FarmJob
    {
        /// <summary>
        ///     How long between plants.
        /// </summary>
        /// <remarks>
        ///     The same reason every other working job waits: a step reporting Running is re-asked
        ///     at 20 Hz, so without this a villager would fill a field in a second and broadcast a
        ///     placement effect for every square of it.
        /// </remarks>
        private const float SecondsBetweenPlants = 1.1f;

        /// <summary>
        ///     How long a field's grid is trusted before it is laid out again.
        /// </summary>
        /// <remarks>
        ///     Long enough that the arithmetic is cheap, short enough that a field somebody has
        ///     resized stops being the old shape before a villager has walked across it. The
        ///     squares themselves are a function of the centre, the radius and the pitch and do
        ///     not move; what expires is the belief that those three are still what they were.
        /// </remarks>
        private const float LayoutSeconds = 5f;

        /// <summary>
        ///     How much room the occupancy test looks in, as a share of the pitch.
        /// </summary>
        /// <remarks>
        ///     Slightly under half, so two neighbouring squares do not each see the other's
        ///     plant. The exact answer comes from the plant afterwards - this only has to be
        ///     close enough to avoid walking to squares that are obviously taken.
        /// </remarks>
        private const float OccupiedShare = .45f;

        private static readonly Dictionary<ZDOID, float> NextPlant = new Dictionary<ZDOID, float>();

        /// <summary>The field each villager's walk has done its best at.</summary>
        /// <remarks>
        ///     The same latch chopping, mining and foraging keep: sowing calls
        ///     <c>Walk.Forget()</c> every tick, which zeroes the stall clock the working reach
        ///     would otherwise be read from - so the reach would collapse the moment a villager
        ///     arrived and it would shuffle back and forth over one square for ever.
        /// </remarks>
        private static readonly Dictionary<ZDOID, ZDOID> Settled = new Dictionary<ZDOID, ZDOID>();

        private static readonly Dictionary<ZDOID, Layout> Layouts = new Dictionary<ZDOID, Layout>();

        /// <summary>
        ///     Squares the ground would not take, and the field they are in.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>Per square, not per field.</b> The first version counted refusals against
        ///         the field and gave up on the whole thing after five - and the whole thing is
        ///         exactly what it gave up on, because the scan is deterministic: refused at a
        ///         square, it offered the same square again, five times, and then wrote off a
        ///         field with four hundred perfectly good squares in it.
        ///     </para>
        ///     <para>
        ///         The disagreement behind it is worth naming. This job's own occupancy test and
        ///         <c>Plant.HaveGrowSpace</c> do not ask the same question - mine looks for plants
        ///         and pickables, the game's sweeps several physics layers - so there will always
        ///         be squares one calls free and the other refuses. Remembering them makes the
        ///         scan self-correcting whatever the disagreement is, which is better than trying
        ///         to predict the game's answer.
        ///     </para>
        /// </remarks>
        private static readonly Dictionary<ZDOID, Refusals> Barren = new Dictionary<ZDOID, Refusals>();

        /// <summary>
        ///     How many <em>different</em> squares may refuse before the field is set aside.
        /// </summary>
        /// <remarks>
        ///     Bounded, because a refusal returns Running and spends no repetition - so without a
        ///     bound a villager works through a whole field of refusals for ever while the screen
        ///     says it is sowing. Generous, because one refused square says nothing about the next
        ///     one: they fail for local reasons - a rock, a wall, a neighbour's roots.
        /// </remarks>
        private const int RefusalsAllowed = 12;

        private sealed class Refusals
        {
            internal ZDOID In;
            internal readonly HashSet<int> Squares = new HashSet<int>();
        }

        /// <summary>Squares this villager has already been refused in this field.</summary>
        /// <remarks>
        ///     Dropped when the field changes, because a square index only means anything within
        ///     one field's grid - carrying it across would refuse arbitrary ground in the next.
        /// </remarks>
        private static HashSet<int> Spurned(ZDOID villager, ZDOID field)
        {
            if (villager.IsNone() || !Barren.TryGetValue(villager, out Refusals refused)) return null;

            return refused.In == field ? refused.Squares : null;
        }

        /// <summary>Reused so a 20 Hz path does not allocate a few thousand structs a second.</summary>
        private static readonly List<Collider> Nearby = new List<Collider>(64);

        private sealed class Layout
        {
            internal float Drawn;
            internal float Radius;
            internal float Pitch;
            internal Vector3 Centre;
            internal readonly List<Furrow> Squares = new List<Furrow>();
        }

        /// <summary>Dropped when a world unloads; none of these identities survive one.</summary>
        internal static void Clear()
        {
            NextPlant.Clear();
            Settled.Clear();
            Layouts.Clear();
            Barren.Clear();
        }

        /// <summary>Drops what a villager that no longer exists was holding.</summary>
        internal static void Forget(ZDOID villager)
        {
            if (villager.IsNone()) return;

            NextPlant.Remove(villager);
            Settled.Remove(villager);
            Barren.Remove(villager);
        }

        internal static JobResult Tick(FarmContext context, out string activity)
        {
            VillagerState state = context.State;

            StructureRecord field = SettlementIndex.Find(context.Colony, state.Target);

            // Registered as something else, unregistered, or switched off while this villager was
            // walking to it. Letting go here rather than in the table, which cannot tell a field
            // that is gone from one that was never chosen.
            if (field != null && ((field.Capabilities & StructureCapability.Field) == 0 ||
                                  !field.WorkableIn(context.Colony)))
            {
                field = null;
                state.ClearTarget();
            }

            Wanted wanted = What(context, field);

            // Fetched before anything else, and only when a field is actually asking for it -
            // otherwise a settlement with no fields would send every farmer to a chest for seed
            // it has no use for. Answers null when there is none to fetch, so the table's own
            // "no seed" reply stands.
            if (wanted.Plant != null && !Has(context, wanted.Plant))
            {
                string seed = wanted.Plant.Seed;
                int amount = wanted.Plant.SeedCount;

                JobResult? fetching = Errand.Fetch(context.Villager, context.Colony, context.Bag,
                    context.Walk, state, context.DeltaTime,
                    inside => Seed(inside, seed, amount), ItemCatalogue.Label(seed), out activity);

                if (fetching.HasValue) return fetching.Value;
            }

            Vector3 at = wanted.At;

            FarmFacts facts = new FarmFacts(
                hasField: field != null,
                wantsSowing: wanted.Order != null,
                hasSpot: wanted.HasSquare,
                atSpot: wanted.HasSquare && Within(context, at),
                hasSeed: wanted.Plant != null && Has(context, wanted.Plant),
                ready: !wanted.HasSquare || Sowing.GroundIsReady(wanted.Plant, at),
                mayCultivate: field != null && field.Settings.MayCultivate,
                tired: false);

            FarmStep step = FarmTransitions.Next((FarmState)state.WorkState, facts);

            switch (step.Action)
            {
                case FarmAction.Yield:
                    return JobOutcomes.Skipped(state, Why(context, wanted), out activity);

                case FarmAction.ChooseWork:
                    return Record(state, step, Choose(context, out activity));

                case FarmAction.MoveToSpot:
                    return Record(state, step, Walk(context, at, out activity));

                case FarmAction.Cultivate:
                    return Record(state, step, Break(context, at, out activity));

                case FarmAction.Sow:
                    return Record(state, step, Sow(context, field, wanted, at, out activity));

                case FarmAction.Complete:
                    Release(context);
                    return JobOutcomes.Completed(state, "that field is sown", out activity);

                default:
                    // An action this engine does not handle is a programming error rather than a
                    // world state. Say so rather than silently idling.
                    return JobOutcomes.Failed(state, "unhandled farm action", out activity);
            }
        }

        /// <summary>The order a field wants served next, and what that means planting.</summary>
        private struct Wanted
        {
            internal FieldOrder Order;
            internal Plantable Plant;

            /// <summary>The square to put it in, already found.</summary>
            internal Vector3 At;

            /// <summary>Which square that is, so a refusal can name the one that failed.</summary>
            internal int Index;

            internal bool HasSquare;
        }

        /// <summary>
        ///     Why a field was passed over, counted as the choosing happens.
        /// </summary>
        /// <remarks>
        ///     Every count here is a question a player will ask out loud the first time a villager
        ///     stands beside a field they just marked out and says it has nothing to do. The job
        ///     knows all of them, and the first version said "no field asks for anything" to every
        ///     one - which is how a setting doing exactly what it was told looks like a bug.
        ///
        ///     The same arrangement mining keeps, and for the reason mining wrote down: a
        ///     settlement standing in a quarry being told there is nothing to mine is a fair
        ///     question, and the answer is one the job already had and used to throw away.
        /// </remarks>
        private sealed class Tally
        {
            internal int WrongBiome;
            internal int Unbroken;
            internal int Full;
            internal int Sown;
            internal int Refused;

            /// <summary>What this land is, so the biome answer can name it.</summary>
            internal Heightmap.Biome Land = Heightmap.Biome.None;

            internal string Say(int fields)
            {
                if (fields == 0) return "no field asks for anything";

                // In the order a player can act on, and the two that are one switch first -
                // because each is the answer whenever somebody is looking straight at the field
                // they expected to be sown.
                if (WrongBiome > 0)
                {
                    return $"this is {Land} - what that field grows will not grow here";
                }

                if (Unbroken > 0)
                {
                    return $"that ground is not broken ({Unbroken}) - let them break it, or cultivate it";
                }

                // Seed is deliberately not one of these. An order is chosen whether or not the
                // villager is carrying its seed, so "no seed" is said at the point it is actually
                // true - by the villager standing in the field it chose, naming the seed it wants.
                if (Full > 0) return "there is no room left in the fields";
                if (Sown > 0) return "the fields are sown";
                if (Refused > 0) return "cannot get to the fields";

                return "nothing to sow";
            }
        }

        /// <summary>
        ///     What a field wants next: the order, the plant, and the square to put it in.
        /// </summary>
        /// <remarks>
        ///     All three together, because they are one question asked three ways and answering
        ///     them separately meant scanning the field's grid twice a tick - once to ask whether
        ///     there was room and again to ask where.
        /// </remarks>
        private static Wanted What(FarmContext context, StructureRecord field, Tally tally = null)
        {
            Wanted wanted = new Wanted();
            if (field == null) return wanted;

            // What this land is, asked once per field rather than per square. A field is one
            // patch of ground and its biome is a property of where the player put it.
            Heightmap.Biome land = BiomeAt(Where(field));
            if (tally != null) tally.Land = land;

            bool anyGrows = false, anyBroken = false, anyRoom = false;

            // Twice over: once preferring an order this villager can act on now, then without
            // that preference.
            //
            // **Seed is never a reason to pass an order over entirely.** It was, briefly, and it
            // deadlocked the job in the second way this file has recorded: with an empty bag no
            // order was chosen, so no plant was named, so the errand that fetches seed had
            // nothing to fetch - and a villager stood beside a full chest reporting that it had
            // no seed. What the bag holds decides the *order* of the orders; the errand decides
            // whether there is any to be had.
            for (int pass = 0; pass < 2; pass++)
            {
                bool carrying = pass == 0;

                foreach (FieldOrder order in field.Settings.Sowing)
                {
                    if (order == null || !order.IsValid) continue;

                    Plantable plant = Planting.Named(order.Plant);

                    // An order naming a plant this world does not have - a mod removed since it
                    // was set. Skipped rather than crashed on, so the rest of the field works.
                    if (plant == null) continue;

                    // The biome, before anything walks anywhere. Both the plant's biomes and the
                    // ground's are readable without moving, so this costs a lookup and saves the
                    // whole round trip - which is what mining's tier gate buys, and for the same
                    // reason: the alternative is a villager that walks out, plants, is refused,
                    // uproots, and does it again every second for ever.
                    if ((plant.Biomes & land) == 0) continue;

                    anyGrows = true;

                    // On the first pass, only what is already in hand. A villager carrying turnip
                    // seed should sow turnips rather than stall on the carrots listed above them.
                    if (carrying && !Has(context, plant)) continue;

                    // And somewhere to put it. A square that is not broken counts only when this
                    // field may break it - otherwise the villager would arrive, find the ground
                    // bare, be sent back to choose, and pick the same field again.
                    if (!Square(context, field, plant, out Vector3 at, out int index,
                            out bool unbroken))
                    {
                        if (unbroken) anyBroken = true;
                        continue;
                    }

                    anyRoom = true;

                    if (!order.WantsMore(Growing(context, field, order.Plant), room: true)) continue;

                    wanted.Order = order;
                    wanted.Plant = plant;
                    wanted.At = at;
                    wanted.Index = index;
                    wanted.HasSquare = true;
                    return wanted;
                }
            }

            if (tally == null) return wanted;

            if (!anyGrows) tally.WrongBiome++;
            else if (anyBroken && !anyRoom) tally.Unbroken++;
            else if (!anyRoom) tally.Full++;
            else tally.Sown++;

            return wanted;
        }

        /// <summary>What biome a point is in, or None when it is outside the loaded world.</summary>
        /// <remarks>
        ///     Through the heightmap tile, because that is where the answer lives - the same
        ///     lesson <c>IsCultivated</c> taught, which belongs to a tile rather than to the class.
        /// </remarks>
        private static Heightmap.Biome BiomeAt(Vector3 at)
        {
            Heightmap tile = Heightmap.FindHeightmap(at);
            return tile != null ? tile.GetBiome(at) : Heightmap.Biome.None;
        }

        private static Vector3 Where(StructureRecord field)
        {
            ZDO zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(field.Id) : null;
            return zdo != null && zdo.IsValid() ? zdo.GetPosition() : Vector3.zero;
        }

        /// <summary>Records the next state when a step made progress.</summary>
        private static JobResult Record(VillagerState state, FarmStep step, JobResult result)
        {
            if (result != JobResult.Running && result != JobResult.Completed) return result;

            state.SetWorkState((int)step.Next);
            return JobResult.Running;
        }

        /// <summary>Takes the nearest field that wants something grown.</summary>
        private static JobResult Choose(FarmContext context, out string activity)
        {
            Vector3 here = context.Villager.transform.position;
            List<StructureRecord> fields = SettlementIndex.Fields(context.Colony, here);
            Tally tally = new Tally();

            foreach (StructureRecord field in fields)
            {
                if (Unreachable.Refuses(context.Villager.Id, field.Id))
                {
                    tally.Refused++;
                    continue;
                }

                Wanted wanted = What(context, field, tally);
                if (wanted.Order == null) continue;

                // A new target is a new walk and a new tolerance. Without the walk being told,
                // its stall clock still holds the last target's timings and judges the first step
                // of this one as already stuck.
                context.Walk.Forget();
                context.Walk.NewLeg();

                // The latch belongs to the field that was walked to, so a new one starts
                // un-arrived - otherwise a villager re-choosing a field it had reached earlier is
                // granted the wider working reach from the first tick and sows it from ten metres
                // away, never walking in.
                Settled.Remove(context.Villager.Id);
                context.State.SetTarget(field.Id);

                activity = "off to the field";
                return JobResult.Running;
            }

            return JobOutcomes.Skipped(context.State, tally.Say(fields.Count), out activity);
        }

        /// <summary>
        ///     The square to work next, chosen afresh from what is standing.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>Started from the villager's own place in the grid.</b> Scanning from the
        ///         same corner every time would send every villager in a field to the same square,
        ///         and the first one there would win while the rest walked for nothing. An offset
        ///         taken from the villager's own id spreads them without a claim, a store or a
        ///         release path - and the scan still wraps, so each of them still fills the whole
        ///         field.
        ///     </para>
        ///     <para>
        ///         <b>Unbroken ground is refused here when the field may not break it.</b> The
        ///         alternative is the loop it replaced: the villager arrives, finds bare ground,
        ///         is sent back to choose, picks the same field, and walks the same walk for ever
        ///         while reporting that it is working.
        ///     </para>
        /// </remarks>
        /// <param name="unbroken">
        ///     Set when the search failed and unbroken ground was the reason, so the tally can say
        ///     which of the several ways a field can be unworkable this one was.
        /// </param>
        private static bool Square(FarmContext context, StructureRecord field, Plantable what,
            out Vector3 at, out int square, out bool unbroken)
        {
            at = Vector3.zero;
            square = -1;
            unbroken = false;
            if (field == null || what == null) return false;

            Layout layout = Grid(field, what);
            if (layout == null || layout.Squares.Count == 0) return false;

            bool mayBreak = field.Settings.MayCultivate;
            int from = Offset(context.Villager.Id, layout.Squares.Count);
            float room = layout.Pitch * OccupiedShare;
            bool sawUnbroken = false;
            HashSet<int> spurned = Spurned(context.Villager.Id, field.Id);

            bool found = FieldPlan.Next(layout.Squares, index =>
            {
                // Tried and refused by the plant itself. Skipped rather than offered again,
                // because the alternative is offering the same square for ever - see Turned.
                if (spurned != null && spurned.Contains(index)) return true;

                if (Taken(layout, index, room)) return true;
                if (mayBreak) return false;

                // Ground this plant cannot use and this field may not break. Counted so the
                // villager can be told which switch to throw rather than "nothing to sow".
                if (Sowing.GroundIsReady(what, Point(layout, index))) return false;

                sawUnbroken = true;
                return true;
            }, out Furrow furrow, from);

            unbroken = !found && sawUnbroken;
            if (!found) return false;

            square = furrow.Index;
            at = Ground(layout.Centre + new Vector3(furrow.X, 0f, furrow.Z));
            return true;
        }

        /// <summary>Where a square is, in the world.</summary>
        private static Vector3 Point(Layout layout, int index)
        {
            foreach (Furrow furrow in layout.Squares)
            {
                if (furrow.Index != index) continue;

                return Ground(layout.Centre + new Vector3(furrow.X, 0f, furrow.Z));
            }

            return layout.Centre;
        }

        /// <summary>Where a villager starts looking, so two of them do not stand on one square.</summary>
        /// <remarks>
        ///     From the id rather than from a counter, because it has to survive a reload and be
        ///     the same answer on every peer. Two villagers can still land on one square - the ids
        ///     are not spread evenly - and that is fine: the world is the arbiter, and the loser
        ///     finds the square taken on its next tick.
        /// </remarks>
        private static int Offset(ZDOID villager, int squares)
        {
            if (squares <= 0) return 0;

            long id = (long)villager.UserID ^ villager.ID;
            return (int)(((id % squares) + squares) % squares);
        }

        /// <summary>The field's grid, laid out once and kept for a moment.</summary>
        private static Layout Grid(StructureRecord field, Plantable what)
        {
            ZDO zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(field.Id) : null;
            if (zdo == null || !zdo.IsValid()) return null;

            float radius = Colonies.Field.RadiusOf(zdo);

            // The pitch is the widest thing this field grows, not the thing being planted right
            // now. A carrot dropped into a gap between oaks fits and then stops the oak that
            // square was for from ever growing - the plant that needs the most room is the one
            // the spacing has to satisfy.
            float pitch = Mathf.Max(.25f, Widest(field, what));

            Vector3 centre = zdo.GetPosition();

            if (!Layouts.TryGetValue(field.Id, out Layout layout))
            {
                layout = new Layout();
                Layouts[field.Id] = layout;
            }

            bool same = Mathf.Approximately(layout.Radius, radius) &&
                        Mathf.Approximately(layout.Pitch, pitch) &&
                        (layout.Centre - centre).sqrMagnitude < .01f;

            if (same && Time.time - layout.Drawn < LayoutSeconds) return layout;

            layout.Drawn = Time.time;
            layout.Radius = radius;
            layout.Pitch = pitch;
            layout.Centre = centre;
            FieldPlan.Squares(radius, pitch, layout.Squares);
            return layout;
        }

        /// <summary>The most room anything this field grows will want.</summary>
        private static float Widest(StructureRecord field, Plantable fallback)
        {
            float widest = fallback?.Spacing ?? 1f;

            foreach (FieldOrder order in field.Settings.Sowing)
            {
                Plantable plant = Planting.Named(order.Plant);
                if (plant != null && plant.Spacing > widest) widest = plant.Spacing;
            }

            return widest;
        }

        /// <summary>Whether anything already stands in a square.</summary>
        /// <remarks>
        ///     A physics query, which is why the grid is cached and why this is asked of squares
        ///     one at a time as the scan walks past them rather than of the whole field at once.
        /// </remarks>
        private static bool Taken(Layout layout, int index, float room)
        {
            foreach (Furrow furrow in layout.Squares)
            {
                if (furrow.Index != index) continue;

                Vector3 at = Ground(layout.Centre + new Vector3(furrow.X, 0f, furrow.Z));

                Nearby.Clear();
                Collider[] hits = Physics.OverlapSphere(at, room);
                foreach (Collider hit in hits)
                {
                    if (hit == null) continue;

                    // Anything with a plant or a piece in it is something somebody put there. The
                    // ground itself and loose vegetation are not, which is why this asks for a
                    // component rather than for any collider at all.
                    if (hit.GetComponentInParent<Plant>() != null) return true;
                    if (hit.GetComponentInParent<Pickable>() != null) return true;
                }

                return false;
            }

            return true;
        }

        /// <summary>A point put back on the ground, since a grid is drawn flat.</summary>
        private static Vector3 Ground(Vector3 at)
        {
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(at, out float height))
            {
                at.y = height;
            }

            return at;
        }

        /// <summary>How many of a plant stand in this field now.</summary>
        /// <remarks>
        ///     Counted in the ground rather than in the larder, which is the whole difference
        ///     between a field's order and a station's. Both are useful and they answer different
        ///     questions - "keep twenty carrots growing" is not "keep twenty carrots in the
        ///     chest" - and the stock rule on the job says the second.
        /// </remarks>
        private static int Growing(FarmContext context, StructureRecord field, string plant)
        {
            ZDO zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(field.Id) : null;
            if (zdo == null || !zdo.IsValid() || ZNetScene.instance == null) return 0;

            int hash = plant.GetStableHashCode();
            float radius = Colonies.Field.RadiusOf(zdo);
            Vector3 centre = zdo.GetPosition();

            int count = 0;
            foreach (ZNetView view in ZNetScene.instance.m_instances.Values)
            {
                if (view == null || !view.IsValid()) continue;

                ZDO record = view.GetZDO();
                if (record.GetPrefab() != hash) continue;
                if (Utils.DistanceXZ(record.GetPosition(), centre) > radius) continue;

                count++;
            }

            return count;
        }

        private static bool Has(FarmContext context, Plantable what) =>
            string.IsNullOrEmpty(what.Seed) ||
            Spending.Held(context.Bag != null ? context.Bag.GetInventory() : null, what.Seed)
                >= what.SeedCount;

        /// <summary>Enough seed for one planting, out of a chest.</summary>
        private static ItemDrop.ItemData Seed(Inventory inside, string seed, int amount)
        {
            if (inside == null || string.IsNullOrEmpty(seed)) return null;
            if (Spending.Held(inside, seed) < amount) return null;

            foreach (ItemDrop.ItemData item in inside.GetAllItems())
            {
                if (Carrying.NameOf(item) == seed) return item;
            }

            return null;
        }

        private static JobResult Break(FarmContext context, Vector3 at, out string activity)
        {
            context.Villager.FaceTowards(at, context.DeltaTime);
            context.Walk.Forget();

            ZDOID who = context.Villager.Id;
            if (!who.IsNone())
            {
                if (NextPlant.TryGetValue(who, out float when) && Time.time < when)
                {
                    return JobOutcomes.Running("breaking the ground", out activity);
                }

                NextPlant[who] = Time.time + SecondsBetweenPlants;
            }

            context.Animation?.Reach();

            if (!Sowing.Cultivate(at))
            {
                // No cultivating piece in this game at all. Said rather than retried for ever,
                // because nothing about it will change.
                Chatter.Warn("[farm] nothing to cultivate with",
                    $"{context.State.Name} has no way to break ground here.");

                Release(context);
                return JobOutcomes.Skipped(context.State, "cannot break this ground", out activity);
            }

            context.State.TouchClaim();
            return JobOutcomes.Running("breaking the ground", out activity);
        }

        private static JobResult Sow(FarmContext context, StructureRecord field, Wanted wanted,
            Vector3 at, out string activity)
        {
            context.Villager.FaceTowards(at, context.DeltaTime);
            context.Walk.Forget();

            ZDOID who = context.Villager.Id;
            if (!who.IsNone())
            {
                if (NextPlant.TryGetValue(who, out float when) && Time.time < when)
                {
                    // Between plants. Not a planting, and deliberately not animated: retriggering
                    // the animation every tick is how it never plays at all.
                    return JobOutcomes.Running("sowing", out activity);
                }

                NextPlant[who] = Time.time + SecondsBetweenPlants;
            }

            switch (Sowing.Place(wanted.Plant, at, context.Bag.GetInventory(), out string why))
            {
                case SowResult.Sown:
                    context.State.TouchClaim();
                    Barren.Remove(context.Villager.Id);
                    context.Animation?.Reach();
                    VillagerInventory.Persist(context.Bag, View(context));
                    Count(context, field, wanted.Order);
                    return JobOutcomes.Running("sowing", out activity);

                case SowResult.Refused:
                    // The ground would not have it, and the seed was not spent. Counted, because
                    // a refusal costs a repetition nothing and would otherwise repeat for ever.
                    return Turned(context, field, wanted.Index, why, out activity);

                case SowResult.NoSeed:
                    return JobOutcomes.Running(why, out activity);

                default:
                    Release(context);
                    return JobOutcomes.Running(why, out activity);
            }
        }

        /// <summary>
        ///     Ground that would not take a plant, and what to make of it.
        /// </summary>
        /// <remarks>
        ///     <b>Not on the first.</b> A square can be refused because somebody else filled it in
        ///     the same moment, and acting immediately writes off a field that is perfectly good.
        ///     <b>And when it is a verdict it lasts</b>, but not for ever: whatever refuses -
        ///     a roof, a frost, a neighbour - can stop being true, and a settlement should notice
        ///     without being reloaded.
        ///
        ///     Skipped rather than completed. Releasing the field sends the table to its Complete
        ///     arm next tick, which would report a field that was never sown as finished and spend
        ///     a repetition on it.
        /// </remarks>
        private static JobResult Turned(FarmContext context, StructureRecord field, int square,
            string why, out string activity)
        {
            ZDOID who = context.Villager.Id;
            if (who.IsNone() || field == null) return JobOutcomes.Running(why, out activity);

            if (!Barren.TryGetValue(who, out Refusals spent) || spent.In != field.Id)
            {
                // A set against a different field says nothing about this one, and its indices
                // would point at unrelated ground.
                spent = new Refusals { In = field.Id };
                Barren[who] = spent;
            }

            spent.Squares.Add(square);

            // The square is set aside first and always, so the next tick offers a different one.
            // Only when many different squares have refused is the field itself in question.
            if (spent.Squares.Count < RefusalsAllowed) return JobOutcomes.Running(why, out activity);

            Chatter.Say($"[farm] will not take: {field.Id}",
                $"{context.State.Name} cannot get anything to grow in {field.Name}: {why}. " +
                $"Tried {spent.Squares.Count} spots.");

            Unreachable.Refuse(context.Villager.Id, field.Id);
            Release(context);
            return JobOutcomes.Skipped(context.State, why, out activity);
        }

        /// <summary>
        ///     Records a planting against a one-off order.
        /// </summary>
        /// <remarks>
        ///     Counted rather than read back off the ground, which is the whole difference between
        ///     <em>sow fifty</em> and <em>keep fifty growing</em>: a one-off that counted the
        ///     ground would start again the moment somebody harvested, which is exactly what it
        ///     was told not to do.
        /// </remarks>
        private static void Count(FarmContext context, StructureRecord field, FieldOrder order)
        {
            if (field == null || order == null || order.Mode != SowMode.Once) return;

            ColonyOperations.EditSettings(context.Colony, field.Id, settings =>
            {
                FieldOrder live = settings.Sowing.Find(o => o.Plant == order.Plant);
                if (live != null) live.Sown++;
            });
        }

        private static void Release(FarmContext context)
        {
            context.State.ClearTarget();
            Forget(context.Villager.Id);
        }

        private static ZNetView View(FarmContext context) =>
            context.Villager != null && context.Villager.TryGetComponent(out ZNetView view) &&
            view.IsValid()
                ? view
                : null;

        private static string Why(FarmContext context, Wanted wanted)
        {
            if (wanted.Plant == null) return "nothing to sow";

            return Has(context, wanted.Plant)
                ? "nothing to sow"
                : "no " + ItemCatalogue.Label(wanted.Plant.Seed);
        }

        private static JobResult Walk(FarmContext context, Vector3 to, out string activity)
        {
            switch (context.Walk.MoveTowards(to, Approach.ToStructure, deltaTime: context.DeltaTime))
            {
                case MoveResult.Arrived:
                    context.State.TouchClaim();

                    if (!context.Villager.Id.IsNone()) Settled[context.Villager.Id] = context.State.Target;

                    return JobOutcomes.Running("off to the field", out activity);

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
                        context.Walk.TripStalledFor, abandoned, () => "a field", out string gaveUp);

                    if (stuck.HasValue)
                    {
                        Unreachable.Refuse(context.Villager.Id, abandoned);
                        Release(context);
                        activity = gaveUp;
                        return stuck.Value;
                    }

                    return JobOutcomes.Running("off to the field", out activity);
            }
        }

        /// <summary>Whether the villager is close enough to work this square.</summary>
        /// <remarks>
        ///     The wider reach applies once the walk has done its best at <em>this field</em>,
        ///     recorded per target rather than read off the stall clock - sowing resets that clock
        ///     every tick, so reading it would make the reach collapse after every plant. The
        ///     latch is on the field rather than on the square for the reason mining's is on the
        ///     deposit: a villager standing in a field has arrived at the field, whichever square
        ///     of it is next.
        /// </remarks>
        private static bool Within(FarmContext context, Vector3 at)
        {
            ZDOID villager = context.Villager.Id;
            bool arrived = !villager.IsNone() &&
                           Settled.TryGetValue(villager, out ZDOID settled) &&
                           !settled.IsNone() && settled == context.State.Target;

            float reach = arrived ? Arrival.WorkingReach : Approach.ToStructure;
            return Utils.DistanceXZ(at, context.Villager.transform.position) <= reach;
        }

        /// <summary>The predicates a check may ask, rather than reimplement.</summary>
        internal static int WouldGrow(Colony colony, StructureRecord field, string plant) =>
            colony == null || field == null
                ? 0
                : Growing(new FarmContext { Colony = colony }, field, plant);
    }
}
