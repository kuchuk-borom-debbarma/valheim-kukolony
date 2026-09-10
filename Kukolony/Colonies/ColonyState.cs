using System.Collections.Generic;
using Kukolony.Core;
using Kukolony.Jobs;

namespace Kukolony.Colonies
{
    /// <summary>
    ///     Persistent colony root. Villagers are explicit members; placed structures are
    ///     named records whose live eligibility is bounded by the hearth radius.
    /// </summary>
    internal readonly struct ColonyState
    {
        private static readonly int NameKey = "kukolony.colony.name".GetStableHashCode();

        private static readonly int VillagersKey = "kukolony.colony.villagers".GetStableHashCode();
        private static readonly int StructuresKey = "kukolony.colony.structures.v2".GetStableHashCode();
        private static readonly int JobsKey = "kukolony.colony.jobs.v2".GetStableHashCode();
        private static readonly int PresetsKey = "kukolony.colony.presets.v2".GetStableHashCode();
        private static readonly int OutfitsKey = "kukolony.colony.outfits.v1".GetStableHashCode();

        private readonly ZDO _zdo;

        internal ColonyState(ZDO zdo)
        {
            _zdo = zdo;
        }

        internal bool IsValid => _zdo != null;

        internal ZDOID Id => _zdo?.m_uid ?? ZDOID.None;

        internal string Name => _zdo?.GetString(NameKey, string.Empty) ?? string.Empty;

        internal void SetName(string name) => _zdo.Set(NameKey, name);

        internal List<StructureRecord> GetStructures()
        {
            List<StructureRecord> result = new List<StructureRecord>();
            string encoded = _zdo?.GetString(StructuresKey, string.Empty) ?? string.Empty;
            if (string.IsNullOrEmpty(encoded)) return result;
            try
            {
                ZPackage p = new ZPackage(encoded);
                if (p.ReadInt() != 3) return result;
                int count = p.ReadInt();
                if (count < 0 || count > 4096) return result;
                for (int i = 0; i < count; i++)
                {
                    ZDOID saved = p.ReadZDOID();
                    string persistentId = p.ReadString();
                    result.Add(new StructureRecord { Id = PersistentZdoReference.Resolve(persistentId, saved),
                        PersistentId = persistentId, Name = p.ReadString(), Prefab = p.ReadString(),
                        Capabilities = (StructureCapability)p.ReadInt() });
                }
            }
            catch (System.Exception e) { Core.Log.Warning("[colony] invalid structure registry: " + e.Message); }
            return result;
        }

        internal void SetStructures(List<StructureRecord> records)
        {
            ZPackage p = new ZPackage(); p.Write(3); p.Write(records.Count);
            foreach (StructureRecord r in records)
            {
                ZDO zdo = ZDOMan.instance?.GetZDO(r.Id);
                string persistentId = !string.IsNullOrEmpty(r.PersistentId)
                    ? r.PersistentId : PersistentZdoReference.Ensure(zdo);
                p.Write(r.Id); p.Write(persistentId); p.Write(r.Name ?? string.Empty);
                p.Write(r.Prefab ?? string.Empty); p.Write((int)r.Capabilities);
            }
            _zdo.Set(StructuresKey, p.GetBase64());
        }

        internal List<ColonyJobConfig> GetJobs()
        {
            List<ColonyJobConfig> result = new List<ColonyJobConfig>(); string encoded = _zdo?.GetString(JobsKey, string.Empty) ?? string.Empty;
            if (string.IsNullOrEmpty(encoded)) return result;
            try { ZPackage p = new ZPackage(encoded); int version=p.ReadInt(); if (version < 4 || version > JobRecordVersion) return result; int count = p.ReadInt(); if (count < 0 || count > 512) return result;
                for (int i = 0; i < count; i++) result.Add(ReadJob(p, version)); }
            catch (System.Exception e) { Core.Log.Warning("[colony] invalid job registry: " + e.Message); }
            return result;
        }

        internal void SetJobs(List<ColonyJobConfig> jobs)
        {
            ZPackage p = new ZPackage(); p.Write(JobRecordVersion); p.Write(jobs.Count);
            foreach (ColonyJobConfig j in jobs) WriteJob(p, j);
            _zdo.Set(JobsKey, p.GetBase64());
        }

        internal List<ColonyJobConfig> GetEffectiveJobs()
        {
            List<ColonyJobConfig> jobs = GetJobs();
            return jobs.Count == 0 ? ColonyJobCatalog.CreateDefaults() : jobs;
        }

        /// <summary>
        ///     Outfit record format. Slot count is written, so adding a slot stays readable.
        /// </summary>
        /// <remarks>
        ///     Version 2 holds a list of acceptable items per slot instead of one name, because
        ///     "leather or troll leather, whichever we have" is what a colony wants. A version 1
        ///     record reads as a list of one, which means exactly what it used to.
        /// </remarks>
        private const int OutfitRecordVersion = 2;

        /// <summary>
        ///     Every outfit this colony defines. Villagers reference one by name, the way jobs
        ///     are referenced by id, so dressing a dozen villagers alike is one edit.
        /// </summary>
        internal List<Villagers.Outfit> GetOutfits()
        {
            List<Villagers.Outfit> result = new List<Villagers.Outfit>();
            string encoded = _zdo?.GetString(OutfitsKey, string.Empty) ?? string.Empty;
            if (string.IsNullOrEmpty(encoded)) return result;
            try
            {
                ZPackage p = new ZPackage(encoded);
                int version = p.ReadInt();
                if (version < 1 || version > OutfitRecordVersion) return result;
                int count = p.ReadInt();
                if (count < 0 || count > 256) throw new System.IO.InvalidDataException("invalid outfit count");
                for (int i = 0; i < count; i++)
                {
                    Villagers.Outfit outfit = new Villagers.Outfit { Name = p.ReadString() };
                    int slots = p.ReadInt();
                    if (slots < 0 || slots > 64) throw new System.IO.InvalidDataException("invalid slot count");
                    for (int slot = 0; slot < slots; slot++)
                    {
                        // A record written by a build with more slots than this one still
                        // reads: the extra entries are past the end and simply dropped.
                        bool kept = slot < Villagers.Outfit.SlotCount;
                        if (version == 1)
                        {
                            string only = p.ReadString();
                            if (kept && only.Length > 0) outfit.Choices[slot].Add(only);
                            continue;
                        }
                        int choices = p.ReadInt();
                        if (choices < 0 || choices > 64) throw new System.IO.InvalidDataException("invalid choice count");
                        for (int choice = 0; choice < choices; choice++)
                        {
                            string item = p.ReadString();
                            if (kept && item.Length > 0) outfit.Choices[slot].Add(item);
                        }
                    }
                    result.Add(outfit);
                }
            }
            catch (System.Exception e) { Core.Log.Warning("[colony] invalid outfit registry: " + e.Message); }
            return result;
        }

        /// <summary>The colony's outfits, with the starter one when a player has defined none.</summary>
        internal List<Villagers.Outfit> GetEffectiveOutfits()
        {
            List<Villagers.Outfit> outfits = GetOutfits();
            if (outfits.Count == 0) outfits.Add(Villagers.Outfit.Everyday());
            return outfits;
        }

        /// <summary>The outfit of this name, or the colony's first when there is no such name.</summary>
        internal Villagers.Outfit GetOutfit(string name)
        {
            List<Villagers.Outfit> outfits = GetEffectiveOutfits();
            foreach (Villagers.Outfit outfit in outfits)
                if (outfit.Name == name) return outfit;
            return outfits[0];
        }

        internal void SetOutfits(List<Villagers.Outfit> outfits)
        {
            ZPackage p = new ZPackage();
            p.Write(OutfitRecordVersion);
            p.Write(outfits.Count);
            foreach (Villagers.Outfit outfit in outfits)
            {
                p.Write(outfit.Name ?? string.Empty);
                p.Write(Villagers.Outfit.SlotCount);
                for (int slot = 0; slot < Villagers.Outfit.SlotCount; slot++)
                {
                    List<string> choices = outfit.Choices[slot];
                    p.Write(choices.Count);
                    foreach (string item in choices) p.Write(item ?? string.Empty);
                }
            }
            _zdo.Set(OutfitsKey, p.GetBase64());
        }

        internal List<JobPreset> GetPresets()
        {
            List<JobPreset> result = new List<JobPreset>();
            string encoded = _zdo?.GetString(PresetsKey, string.Empty) ?? string.Empty;
            if (string.IsNullOrEmpty(encoded)) return result;
            try
            {
                ZPackage p = new ZPackage(encoded);
                int version=p.ReadInt(); if (version < 4 || version > JobRecordVersion) return result;
                int count = p.ReadInt();
                if (count < 0 || count > 512) return result;
                for (int i = 0; i < count; i++)
                    result.Add(new JobPreset { Name = p.ReadString(), ColonyLocal = p.ReadBool(), Settings = ReadJob(p, version) });
            }
            catch (System.Exception e) { Core.Log.Warning("[colony] invalid preset registry: " + e.Message); }
            return result;
        }

        internal void SetPresets(List<JobPreset> presets)
        {
            ZPackage p = new ZPackage(); p.Write(JobRecordVersion); p.Write(presets.Count);
            foreach (JobPreset preset in presets)
            {
                p.Write(preset.Name ?? string.Empty);
                p.Write(preset.ColonyLocal);
                WriteJob(p, preset.Settings);
            }
            _zdo.Set(PresetsKey, p.GetBase64());
        }

        /// <summary>
        ///     Job record format.
        /// </summary>
        /// <remarks>
        ///     Version 6 dropped the pipeline: a job is named work with its own settings rather
        ///     than a list of pieces. Records from 3 to 5 still load - their settings mean the
        ///     same things - and their piece lists are read past and discarded, because the
        ///     fields after them cannot be found otherwise.
        /// </remarks>
        private const int JobRecordVersion = 6;

        private static ColonyJobConfig ReadJob(ZPackage p, int version)
        {
            ColonyJobConfig job = new ColonyJobConfig
            {
                Id = p.ReadString(),
                Type = (ColonyJobType)p.ReadInt(),
                Name = p.ReadString(),
                Targets = (TargetMode)p.ReadInt(),
                Source = ReadReference(p, version),
                Destination = ReadReference(p, version),
                StockLimit = p.ReadInt(),
                Count = p.ReadInt(),
                Reservations = p.ReadBool(),
                SearchRadius = p.ReadSingle(),
                StopDistance = p.ReadSingle()
            };
            int structures = p.ReadInt();
            if (structures < 0 || structures > 4096) throw new System.IO.InvalidDataException("invalid target count");
            for (int i = 0; i < structures; i++) job.SelectedStructures.Add(ReadReference(p, version));
            int filters = p.ReadInt();
            if (filters < 0 || filters > 4096) throw new System.IO.InvalidDataException("invalid filter count");
            for (int i = 0; i < filters; i++) job.ItemFilters.Add(p.ReadString());
            if (version >= 3 && version <= 5) SkipPipeline(p, version);
            if (version >= 6) job.DropOnGround = p.ReadBool();
            return job;
        }

        /// <summary>
        ///     Reads past a pipeline saved before jobs sequenced themselves. Nothing is kept:
        ///     what the pieces were configured with duplicated the job's own settings, and what
        ///     order they ran in is now the job's business. The read still has to happen, or
        ///     everything after it in the record is misaligned.
        /// </summary>
        private static void SkipPipeline(ZPackage p, int version)
        {
            int pieces = p.ReadInt();
            if (pieces < 0 || pieces > 128) throw new System.IO.InvalidDataException("invalid pipeline piece count");
            for (int i = 0; i < pieces; i++)
            {
                p.ReadInt();   // kind
                p.ReadInt();   // capability
                if (version < 5) continue;
                int pieceFilters = p.ReadInt();
                if (pieceFilters < 0 || pieceFilters > 256) throw new System.IO.InvalidDataException("invalid piece filter count");
                for (int f = 0; f < pieceFilters; f++) p.ReadString();
                ReadReference(p, version);
                int pieceStructures = p.ReadInt();
                if (pieceStructures < 0 || pieceStructures > 4096) throw new System.IO.InvalidDataException("invalid piece target count");
                for (int t = 0; t < pieceStructures; t++) ReadReference(p, version);
                p.ReadInt();      // stock limit
                p.ReadInt();      // amount
                p.ReadSingle();   // search radius
                p.ReadSingle();   // stop distance
                p.ReadInt();      // targets
                p.ReadInt();      // reservations
            }
        }

        private static void WriteJob(ZPackage p, ColonyJobConfig job)
        {
            p.Write(job.Id ?? string.Empty); p.Write((int)job.Type); p.Write(job.Name ?? string.Empty);
            p.Write((int)job.Targets); WriteReference(p, job.Source); WriteReference(p, job.Destination);
            p.Write(job.StockLimit); p.Write(System.Math.Max(1, job.Count)); p.Write(job.Reservations);
            p.Write(job.SearchRadius); p.Write(job.StopDistance);
            p.Write(job.SelectedStructures.Count); foreach (ZDOID id in job.SelectedStructures) WriteReference(p, id);
            p.Write(job.ItemFilters.Count); foreach (string item in job.ItemFilters) p.Write(item ?? string.Empty);
            p.Write(job.DropOnGround);
        }

        private static ZDOID ReadReference(ZPackage package, int version)
        {
            ZDOID saved = package.ReadZDOID();
            string persistentId = version >= 4 ? package.ReadString() : string.Empty;
            return PersistentZdoReference.Resolve(persistentId, saved);
        }

        private static void WriteReference(ZPackage package, ZDOID id)
        {
            package.Write(id);
            package.Write(id.IsNone() ? string.Empty :
                PersistentZdoReference.Ensure(ZDOMan.instance?.GetZDO(id)));
        }

        internal List<ZDOID> GetMembers(ColonyMemberKind kind) =>
            kind == ColonyMemberKind.Villager
                ? ColonyMembers.Decode(_zdo?.GetString(VillagersKey, string.Empty) ?? string.Empty)
                : new List<ZDOID>();

        internal int CountMembers(ColonyMemberKind kind) => GetMembers(kind).Count;

        /// <summary>
        ///     Adds a member if it is not already there. Caller must own the ZDO - a
        ///     non-owner write lands locally and is clobbered on the next sync.
        /// </summary>
        internal bool AddMember(ColonyMemberKind kind, ZDOID member)
        {
            if (member.IsNone())
            {
                return false;
            }

            List<ZDOID> members = GetMembers(kind);
            if (members.Contains(member))
            {
                return false;
            }

            members.Add(member);
            _zdo.Set(VillagersKey, ColonyMembers.Encode(members));
            return true;
        }

        internal bool RemoveMember(ColonyMemberKind kind, ZDOID member)
        {
            List<ZDOID> members = GetMembers(kind);
            if (!members.Remove(member))
            {
                return false;
            }

            _zdo.Set(VillagersKey, ColonyMembers.Encode(members));
            return true;
        }

        /// <summary>
        ///     Drops members whose ZDO no longer exists.
        ///
        ///     A destroyed chest leaves a dangling id, and "the ZDO is gone" is a
        ///     different thing from "it is not loaded right now" - only the former is
        ///     safe to prune, or a colony would forget everything the moment nobody was
        ///     nearby.
        /// </summary>
        internal int PruneMissing(ColonyMemberKind kind)
        {
            if (ZDOMan.instance == null)
            {
                return 0;
            }

            List<ZDOID> members = GetMembers(kind);
            List<ZDOID> alive = new List<ZDOID>(members.Count);

            foreach (ZDOID member in members)
            {
                if (ZDOMan.instance.GetZDO(member) != null)
                {
                    alive.Add(member);
                }
            }

            int removed = members.Count - alive.Count;
            if (removed > 0)
            {
                _zdo.Set(VillagersKey, ColonyMembers.Encode(alive));
            }

            return removed;
        }

    }
}
