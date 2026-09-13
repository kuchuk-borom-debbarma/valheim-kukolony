using System.Collections.Generic;
using Kukolony.Core;

namespace Kukolony.Colonies
{
    /// <summary>
    ///     Persistent colony root. Villagers are explicit members; placed structures are
    ///     named records whose live eligibility is bounded by the hearth radius.
    ///
    ///     Format version 2 adds per-structure settings. The key was renamed alongside the
    ///     version, as at version 1, so a record written by an older build is not found rather
    ///     than misparsed - registrations have to be redone, which was the deliberate choice
    ///     over carrying a second decoder for a format nothing has shipped on.
    /// </summary>
    internal readonly struct ColonyState
    {
        private static readonly int NameKey = "kukolony.colony.name".GetStableHashCode();

        private static readonly int VillagersKey = "kukolony.colony.villagers".GetStableHashCode();
        /// <summary>
        ///     Bumped on every structure write, so anything caching the records can tell it is
        ///     looking at a stale copy. On the colony's own ZDO rather than in memory, so an
        ///     edit made by another peer invalidates the cache here too.
        /// </summary>
        private static readonly int StructuresRevisionKey =
            "kukolony.colony.structures.revision".GetStableHashCode();

        /// <summary>
        ///     The jobs this colony offers. Shared by every villager queued onto them, which is
        ///     what stops a settlement of a hundred being a hundred configurations.
        /// </summary>
        private static readonly int JobsKey = "kukolony.colony.jobs.v1".GetStableHashCode();
        private static readonly int PresetsKey = "kukolony.colony.presets.v1".GetStableHashCode();

        private static readonly int StructuresKey = "kukolony.colony.structures.v2".GetStableHashCode();

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
                // The record format's own version, bumped whenever the settings gain a
                // field. A blob written by an older build is discarded rather than decoded
                // against the wrong layout, which would not fail - it would produce records
                // full of plausible nonsense.
                if (p.ReadInt() != 3) return result;
                int count = p.ReadInt();
                if (count < 0 || count > 4096) return result;
                for (int i = 0; i < count; i++)
                {
                    ZDOID saved = p.ReadZDOID();
                    string persistentId = p.ReadString();
                    result.Add(new StructureRecord { Id = PersistentZdoReference.Resolve(persistentId, saved),
                        PersistentId = persistentId, Name = p.ReadString(), Prefab = p.ReadString(),
                        Capabilities = (StructureCapability)p.ReadInt() & StructureCapabilities.Known,
                        Settings = StructureSettings.Read(p) });
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
                // Records arrive with their token already minted, by the one path that claims
                // the object first. Minting here instead would mean rewriting this list - which
                // renaming a single structure does - claimed ownership of every structure in
                // the colony at once.
                //
                // A record without one is a record built off the registration path. Saying so
                // is the point: it persists, and it will resolve only while its runtime address
                // happens to still be right, which is a bug that otherwise surfaces two reloads
                // later as a reference to somebody else's chest.
                if (string.IsNullOrEmpty(r.PersistentId))
                {
                    Core.Log.Warning($"[colony] structure '{r.Name}' has no durable id; " +
                                     "it was not registered through ColonyOperations");
                }

                p.Write(r.Id); p.Write(r.PersistentId ?? string.Empty); p.Write(r.Name ?? string.Empty);
                p.Write(r.Prefab ?? string.Empty); p.Write((int)r.Capabilities);
                (r.Settings ?? new StructureSettings()).Write(p);
            }
            _zdo.Set(StructuresKey, p.GetBase64());
            _zdo.Set(StructuresRevisionKey, StructuresRevision + 1);
        }

        internal List<Jobs.JobDefinition> GetJobs()
        {
            List<Jobs.JobDefinition> result = new List<Jobs.JobDefinition>();
            string encoded = _zdo?.GetString(JobsKey, string.Empty) ?? string.Empty;
            if (string.IsNullOrEmpty(encoded)) return result;

            try
            {
                ZPackage p = new ZPackage(encoded);

                // Every version this mod has written is decoded, which every other format in
                // this file refuses to do on purpose - a blob written by an older build is
                // normally discarded rather than read against the wrong layout, because that
                // does not fail, it produces records full of plausible nonsense.
                //
                // That trade is right when nobody is playing. It is wrong here: chopping added
                // settings to a job while the mod was in use, and discarding would have thrown
                // away a player's configured work to make room for a feature they could not use
                // yet. Version 2 stops after the item list; version 3 keeps one work area where
                // 4 keeps an ordered list; the fields a version does not carry keep their
                // defaults, and JobDefinition.Read is the one place that knows which those are.
                int version = p.ReadInt();
                if (version < 2 || version > 4) return result;

                int count = p.ReadInt();
                if (count < 0 || count > 256) return result;
                for (int i = 0; i < count; i++) result.Add(Jobs.JobDefinition.Read(p, version));
            }
            catch (System.Exception e)
            {
                // A corrupt job list must not take the colony down with it. An empty list is
                // idle, which a player can see and fix.
                Core.Log.Warning("[colony] invalid job list: " + e.Message);
                result.Clear();
            }

            return result;
        }

        /// <summary>The named queues this settlement keeps, for assigning work in bulk.</summary>
        internal List<Jobs.JobPreset> GetPresets()
        {
            List<Jobs.JobPreset> result = new List<Jobs.JobPreset>();
            string encoded = _zdo?.GetString(PresetsKey, string.Empty) ?? string.Empty;
            if (string.IsNullOrEmpty(encoded)) return result;

            try
            {
                ZPackage p = new ZPackage(encoded);
                if (p.ReadInt() != 1) return result;

                int count = p.ReadInt();
                if (count < 0 || count > 256) return result;

                for (int i = 0; i < count; i++) result.Add(Jobs.JobPreset.Read(p));
            }
            catch (System.Exception e)
            {
                Core.Log.Warning("[colony] invalid preset record: " + e.Message);
            }

            return result;
        }

        internal void SetPresets(List<Jobs.JobPreset> presets)
        {
            ZPackage p = new ZPackage();
            p.Write(1);
            p.Write(presets.Count);
            foreach (Jobs.JobPreset preset in presets) preset.Write(p);

            _zdo.Set(PresetsKey, p.GetBase64());
        }

        internal void SetJobs(List<Jobs.JobDefinition> jobs)
        {
            ZPackage p = new ZPackage();
            p.Write(4);
            p.Write(jobs.Count);
            foreach (Jobs.JobDefinition job in jobs) job.Write(p);
            _zdo.Set(JobsKey, p.GetBase64());
        }

        internal int StructuresRevision => _zdo?.GetInt(StructuresRevisionKey, 0) ?? 0;

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
