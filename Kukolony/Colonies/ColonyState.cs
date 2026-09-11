using System.Collections.Generic;
using Kukolony.Core;

namespace Kukolony.Colonies
{
    /// <summary>
    ///     Persistent colony root. Villagers are explicit members; placed structures are
    ///     named records whose live eligibility is bounded by the hearth radius.
    ///
    ///     Format version 1: the redesign at docs/roadmap.md milestone 1 reset it. Nothing
    ///     written by the earlier design is readable, deliberately - the key was renamed as
    ///     well as the version raised, so an old record is not found rather than misparsed.
    /// </summary>
    internal readonly struct ColonyState
    {
        private static readonly int NameKey = "kukolony.colony.name".GetStableHashCode();

        private static readonly int VillagersKey = "kukolony.colony.villagers".GetStableHashCode();
        private static readonly int StructuresKey = "kukolony.colony.structures.v1".GetStableHashCode();

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
                if (p.ReadInt() != 1) return result;
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
            ZPackage p = new ZPackage(); p.Write(1); p.Write(records.Count);
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
