using System.Collections.Generic;
using Kukolony.Jobs;
using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Colonies
{
    /// <summary>
    ///     Where things go, what wants feeding, and which beds are free.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Jobs do not search the settlement; they ask it. With no cap on population, a
    ///         hundred villagers each walking every chest is the difference between a settlement
    ///         and a slideshow - so the answers are built once from the records and reused,
    ///         never rebuilt per villager.
    ///     </para>
    ///     <para>
    ///         Rebuilt when the records change, detected by a revision the colony bumps on every
    ///         write. A revision rather than a timer, so an edit is reflected immediately and a
    ///         quiet settlement costs nothing; and read from the colony's own ZDO, so a change
    ///         made by another peer invalidates it here too.
    ///     </para>
    /// </remarks>
    internal static class SettlementIndex
    {
        private sealed class Snapshot
        {
            internal int Revision = -1;
            internal readonly List<StructureRecord> Storage = new List<StructureRecord>();
            internal readonly List<StructureRecord> Processing = new List<StructureRecord>();
            internal readonly List<StructureRecord> Beds = new List<StructureRecord>();
            internal readonly List<StructureRecord> Crafting = new List<StructureRecord>();
            internal readonly List<StructureRecord> Fields = new List<StructureRecord>();
        }

        private static readonly Dictionary<ZDOID, Snapshot> Snapshots = new Dictionary<ZDOID, Snapshot>();
        private static ZDOMan _owner;

        /// <summary>
        ///     Which structures would accept this item, nearest first.
        /// </summary>
        /// <remarks>
        ///     Capacity is part of the question, because discovering a chest is full on arrival
        ///     wastes the walk. A container that cannot be read - not loaded, which is unusual
        ///     for a registered structure but happens when the keep-alive is off or budgeted
        ///     out - counts as room rather than as none: refusing to answer would cost the
        ///     settlement a destination it really had.
        /// </remarks>
        internal static List<StructureRecord> WhereDoesItGo(Colony colony, string itemPrefab, Vector3 from,
            ZDOID asker = default)
        {
            List<StructureRecord> answers = new List<StructureRecord>();
            if (colony == null || string.IsNullOrEmpty(itemPrefab)) return answers;

            List<int> scores = new List<int>();
            foreach (StructureRecord record in Current(colony).Storage)
            {
                if (!record.WorkableIn(colony)) continue;

                // Not somewhere this villager has just spent two minutes failing to reach.
                // A destination is chosen afresh from the same world every tick, so without
                // this a villager that gave up on a walled-in chest sets straight off to it
                // again, and again, burning a repetition each time.
                if (Jobs.Unreachable.Refuses(asker, record.Id)) continue;
                if (!StructureInventory.HasRoomFor(record.Id, itemPrefab)) continue;

                int score = ScoreOf(record, itemPrefab);
                if (score <= Placement.Refused) continue;

                answers.Add(record);
                scores.Add(score);
            }

            SortByScoreThenDistance(answers, scores, from);
            return answers;
        }

        /// <summary>Which structures the settlement may take this item out of, nearest first.</summary>
        internal static List<StructureRecord> WhereIsItKept(Colony colony, string itemPrefab, Vector3 from)
        {
            List<StructureRecord> answers = new List<StructureRecord>();
            if (colony == null || string.IsNullOrEmpty(itemPrefab)) return answers;

            foreach (StructureRecord record in Current(colony).Storage)
            {
                if (!record.Settings.MayTakeFrom) continue;
                if (!record.WorkableIn(colony)) continue;

                // Taking from a container needs its contents, which is a loaded-only
                // question - so unlike "where does it go", an unreadable container is not an
                // answer here. Sending a villager to fetch from a chest we cannot see the
                // inside of is a walk with nothing at the end of it.
                if (StructureInventory.Count(record.Id, itemPrefab) <= 0) continue;
                answers.Add(record);
            }

            Sort(answers, from);
            return answers;
        }

        /// <summary>
        ///     The containers the settlement is allowed to reorganise.
        /// </summary>
        /// <remarks>
        ///     Registered, reachable, and not marked as keeping. A chest the player told the
        ///     settlement not to take from is never a source, which is how "this one is mine"
        ///     is expressed - so it is excluded here rather than checked later, where a new
        ///     caller could forget.
        /// </remarks>
        internal static List<StructureRecord> WhatMayBeTidied(Colony colony)
        {
            List<StructureRecord> answers = new List<StructureRecord>();
            if (colony == null) return answers;

            foreach (StructureRecord record in Current(colony).Storage)
            {
                if (!record.Settings.MayTakeFrom) continue;
                if (!record.WorkableIn(colony)) continue;
                answers.Add(record);
            }

            return answers;
        }

        /// <summary>
        ///     The fields this colony may work, nearest first.
        /// </summary>
        /// <remarks>
        ///     <b>Not exclusive, and deliberately.</b> Whether something is claimed is decided by
        ///     who asks - hauling asks so two villagers never target one stack, and depositing
        ///     does not so any number may share a chest. A field is the second kind: it is a patch
        ///     of ground with hundreds of squares in it, and the point of marking out a big one is
        ///     that several people can work it. Two villagers land on different squares because
        ///     each starts its scan of the grid somewhere else, not because anything is held.
        /// </remarks>
        internal static List<StructureRecord> Fields(Colony colony, Vector3 from)
        {
            List<StructureRecord> answers = new List<StructureRecord>();
            if (colony == null) return answers;

            foreach (StructureRecord record in Current(colony).Fields)
            {
                if (!record.WorkableIn(colony)) continue;
                if (record.Settings.Sowing.Count == 0) continue;

                answers.Add(record);
            }

            Sort(answers, from);
            return answers;
        }

        /// <summary>Which processing stations are configured and below what they should hold.</summary>
        internal static List<StructureRecord> WhatWantsFeeding(Colony colony, Vector3 from)
        {
            List<StructureRecord> answers = new List<StructureRecord>();
            if (colony == null) return answers;

            foreach (StructureRecord record in Current(colony).Processing)
            {
                StructureSettings settings = record.Settings;
                if (settings.Input.Count == 0 && settings.Fuel.Count == 0) continue;
                if (!record.WorkableIn(colony)) continue;
                answers.Add(record);
            }

            Sort(answers, from);
            return answers;
        }

        /// <summary>Beds nobody has been assigned to.</summary>
        internal static List<StructureRecord> FreeBeds(Colony colony)
        {
            List<StructureRecord> answers = new List<StructureRecord>();
            if (colony == null) return answers;

            foreach (StructureRecord record in Current(colony).Beds)
            {
                // Resting is work too, as far as the switch is concerned. "Villagers may use
                // this" was honoured by every job and by no bed, which is exactly the five
                // places out of six the switch's own remark warns about - a player who
                // switched off a bed to stop it being used watched somebody sleep in it.
                if (record.Settings.HasSleeper || !record.WorkableIn(colony)) continue;
                answers.Add(record);
            }

            return answers;
        }

        /// <summary>
        ///     The record for a registered structure, from the cached snapshot.
        /// </summary>
        /// <remarks>
        ///     Asked of the index rather than of the colony, because <c>GetStructures</c>
        ///     decodes the whole registry blob on every call - fine for a screen drawn on a key
        ///     press, ruinous for a job that asks once per villager per tick. The settlement has
        ///     no population cap, and this is exactly the shape that would stop being affordable.
        /// </remarks>
        internal static StructureRecord Find(Colony colony, ZDOID id)
        {
            if (colony == null || id.IsNone()) return null;

            // Every list, because a record is only in the lists its capabilities put it in and
            // this answers "which record is this" rather than "what is it for". A crafting
            // station carries no Container, no Bed and no processing component, so leaving it
            // out made Find return null for every workbench in the settlement - and the craft
            // job, which asks this to find out whether its own station still wants anything,
            // read that as "it wants nothing" and looped for ever while saying it was working.
            Snapshot snapshot = Current(colony);
            return FindIn(snapshot.Storage, id) ?? FindIn(snapshot.Processing, id)
                ?? FindIn(snapshot.Beds, id) ?? FindIn(snapshot.Crafting, id)
                ?? FindIn(snapshot.Fields, id);
        }

        private static StructureRecord FindIn(List<StructureRecord> records, ZDOID id)
        {
            foreach (StructureRecord record in records)
            {
                if (record.Id == id) return record;
            }

            return null;
        }

        /// <summary>The bed a villager was given, or null.</summary>
        internal static StructureRecord BedOf(Colony colony, ZDOID villager)
        {
            if (colony == null || villager.IsNone()) return null;
            foreach (StructureRecord record in Current(colony).Beds)
                if (record.Settings.Sleeper == villager && record.WorkableIn(colony)) return record;
            return null;
        }

        /// <summary>
        ///     What this container is worth as a home for one kind of item.
        /// </summary>
        /// <remarks>
        ///     The cap is read against what the container actually holds, and an unreadable
        ///     container is never treated as being at its cap - unknown is not full, and
        ///     refusing a destination we cannot see into would cost the settlement a chest it
        ///     really had. The walk is the cheaper mistake.
        /// </remarks>
        /// <summary>
        ///     What a container is worth for an item, as a place to put one or as the place
        ///     one already is.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>A cap refuses arrivals, never residents.</b> <paramref name="holding" />
        ///         is what tells the two apart, and without it the cap was a shuffle loop -
        ///         the precise failure the scoring exists to make impossible, arrived at by
        ///         the one route the score could not see.
        ///     </para>
        ///     <para>
        ///         A wood shed capped at ten, filled to ten, scored Refused for the wood
        ///         inside it. Refused is below an overflow chest, so the move read as an
        ///         improvement and the shed's own stack was carried out; emptied, the shed
        ///         scored Named again, which beats overflow, so the same stack came straight
        ///         back. Two villagers could pass one stack between them for ever, each
        ///         decision correct. <c>Placement.Score</c> has always documented the right
        ///         rule - "the items already inside it still score Named and are left where
        ///         they are" - and nothing implemented it.
        ///     </para>
        /// </remarks>
        internal static int ScoreOf(StructureRecord record, string itemPrefab, bool holding = false)
        {
            StructureSettings settings = record.Settings;
            bool names = settings.Accepts.Contains(itemPrefab);
            bool takesAnything = settings.Accepts.Count == 0;

            // Asked only of a destination. For something already here the cap has nothing to
            // say: being full is not a reason to start moving things out.
            bool atCap = false;
            if (!holding)
            {
                int cap = settings.CapFor(itemPrefab);
                int held = cap < 0 ? 0 : StructureInventory.Count(record.Id, itemPrefab);
                atCap = cap >= 0 && held != StructureInventory.Unknown && held >= cap;
            }

            return Placement.Score(names, takesAnything, settings.TakeUnclaimed, atCap);
        }

        /// <summary>
        ///     How many more of an item a container was told to take, or -1 for no limit.
        /// </summary>
        /// <remarks>
        ///     The cap as a bound on arriving goods, which is the only thing now enforcing it.
        ///     It used to be enforced by accident: an overshoot scored the chest Refused for
        ///     its own contents and the excess was carried away - the shuffle loop. With a
        ///     chest keeping what it holds, a delivery that overshoots stays overshot, so the
        ///     limit has to be applied where the goods go in. Unknown contents mean no limit,
        ///     the same answer the score gives, because refusing a chest we cannot see into
        ///     costs the settlement a destination it really had.
        /// </remarks>
        internal static int RoomUnderCap(StructureRecord record, string itemPrefab)
        {
            if (record?.Settings == null || string.IsNullOrEmpty(itemPrefab)) return -1;

            int cap = record.Settings.CapFor(itemPrefab);
            if (cap < 0) return -1;

            int held = StructureInventory.Count(record.Id, itemPrefab);
            if (held == StructureInventory.Unknown) return -1;

            return Mathf.Max(0, cap - held);
        }

        /// <summary>
        ///     Best home first, and among equals the nearest.
        /// </summary>
        /// <remarks>
        ///     Score before distance is what "organising" means: a chest that names the item
        ///     wins over a nearer overflow chest, which is the difference between a settlement
        ///     that sorts itself and one that merely tidies up. Two chests that score the same
        ///     are both right, so the walk decides.
        /// </remarks>
        private static void SortByScoreThenDistance(List<StructureRecord> records, List<int> scores, Vector3 from)
        {
            for (int i = 1; i < records.Count; i++)
            {
                StructureRecord record = records[i];
                int score = scores[i];
                float distance = Distance(record, from);

                int j = i - 1;
                while (j >= 0 && (scores[j] < score ||
                                  (scores[j] == score && Distance(records[j], from) > distance)))
                {
                    records[j + 1] = records[j];
                    scores[j + 1] = scores[j];
                    j--;
                }

                records[j + 1] = record;
                scores[j + 1] = score;
            }
        }

        /// <summary>
        ///     Two chests claiming the same item is not a conflict - both are valid answers, and
        ///     the nearest usable one wins.
        /// </summary>
        private static void Sort(List<StructureRecord> records, Vector3 from)
        {
            records.Sort((a, b) => Distance(a, from).CompareTo(Distance(b, from)));
        }

        private static float Distance(StructureRecord record, Vector3 from)
        {
            ZDO zdo = ZDOMan.instance?.GetZDO(record.Id);
            return zdo == null ? float.MaxValue : Utils.DistanceXZ(zdo.GetPosition(), from);
        }

        private static Snapshot Current(Colony colony)
        {
            Forget();

            ZDOID id = colony.Id;
            if (!Snapshots.TryGetValue(id, out Snapshot snapshot))
            {
                snapshot = new Snapshot();
                Snapshots[id] = snapshot;
            }

            int revision = colony.State.StructuresRevision;
            if (snapshot.Revision == revision) return snapshot;

            snapshot.Revision = revision;
            Rebuilds++;
            snapshot.Storage.Clear();
            snapshot.Processing.Clear();
            snapshot.Beds.Clear();
            snapshot.Crafting.Clear();
            snapshot.Fields.Clear();

            foreach (StructureRecord record in colony.State.GetStructures())
            {
                if ((record.Capabilities & StructureCapability.Storage) != 0) snapshot.Storage.Add(record);
                if ((record.Capabilities & StructureCapability.Processing) != 0) snapshot.Processing.Add(record);
                if ((record.Capabilities & StructureCapability.Rest) != 0) snapshot.Beds.Add(record);
                if ((record.Capabilities & StructureCapability.Crafting) != 0) snapshot.Crafting.Add(record);
                if ((record.Capabilities & StructureCapability.Field) != 0) snapshot.Fields.Add(record);
            }

            Log.Debug($"[index] rebuilt '{colony.State.Name}' at revision {revision}: " +
                      $"{snapshot.Storage.Count} storage, {snapshot.Processing.Count} processing, " +
                      $"{snapshot.Beds.Count} beds, {snapshot.Crafting.Count} crafting, " +
                      $"{snapshot.Fields.Count} field(s)");
            return snapshot;
        }

        /// <summary>Drops everything learned about a world when the world goes.</summary>
        private static void Forget()
        {
            if (ReferenceEquals(_owner, ZDOMan.instance)) return;
            Snapshots.Clear();
            _owner = ZDOMan.instance;
        }

        /// <summary>Discards the cache, for checks that need a known starting point.</summary>
        internal static void ResetForTest() => Snapshots.Clear();

        /// <summary>How many times the index has been rebuilt, so a check can prove it is not per-query.</summary>
        internal static int Rebuilds { get; private set; }
    }
}
