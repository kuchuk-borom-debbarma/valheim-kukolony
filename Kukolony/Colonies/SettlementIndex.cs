using System.Collections.Generic;
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
        internal static List<StructureRecord> WhereDoesItGo(Colony colony, string itemPrefab, Vector3 from)
        {
            List<StructureRecord> answers = new List<StructureRecord>();
            if (colony == null || string.IsNullOrEmpty(itemPrefab)) return answers;

            foreach (StructureRecord record in Current(colony).Storage)
            {
                if (!Accepts(record, itemPrefab)) continue;
                if (record.StatusIn(colony) != StructureStatus.Ready) continue;
                if (!StructureInventory.HasRoomFor(record.Id, itemPrefab)) continue;
                answers.Add(record);
            }

            Sort(answers, from);
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
                if (record.StatusIn(colony) != StructureStatus.Ready) continue;

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

        /// <summary>Which processing stations are configured and below what they should hold.</summary>
        internal static List<StructureRecord> WhatWantsFeeding(Colony colony, Vector3 from)
        {
            List<StructureRecord> answers = new List<StructureRecord>();
            if (colony == null) return answers;

            foreach (StructureRecord record in Current(colony).Processing)
            {
                StructureSettings settings = record.Settings;
                if (settings.Input.Count == 0 && settings.Fuel.Count == 0) continue;
                if (record.StatusIn(colony) != StructureStatus.Ready) continue;
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
                if (record.Settings.HasSleeper) continue;
                answers.Add(record);
            }

            return answers;
        }

        /// <summary>The bed a villager was given, or null.</summary>
        internal static StructureRecord BedOf(Colony colony, ZDOID villager)
        {
            if (colony == null || villager.IsNone()) return null;
            foreach (StructureRecord record in Current(colony).Beds)
                if (record.Settings.Sleeper == villager) return record;
            return null;
        }

        /// <summary>
        ///     Whether a structure's settings claim this item. An empty list claims everything,
        ///     which is what an overflow chest is.
        /// </summary>
        private static bool Accepts(StructureRecord record, string itemPrefab) =>
            record.Settings.Accepts.Count == 0 || record.Settings.Accepts.Contains(itemPrefab);

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

            foreach (StructureRecord record in colony.State.GetStructures())
            {
                if ((record.Capabilities & StructureCapability.Storage) != 0) snapshot.Storage.Add(record);
                if ((record.Capabilities & StructureCapability.Processing) != 0) snapshot.Processing.Add(record);
                if ((record.Capabilities & StructureCapability.Rest) != 0) snapshot.Beds.Add(record);
            }

            Log.Debug($"[index] rebuilt '{colony.State.Name}' at revision {revision}: " +
                      $"{snapshot.Storage.Count} storage, {snapshot.Processing.Count} processing, " +
                      $"{snapshot.Beds.Count} beds");
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
