using System.Collections.Generic;

namespace Kukolony.Colonies
{
    /// <summary>
    ///     A colony's record, stored on the hearth's ZDO.
    ///
    ///     **A colony has no radius.** Its position means nothing: members are assigned
    ///     explicitly and may be anywhere. A radius would invent rules the player fights -
    ///     "this bed is two metres outside" - and would need retuning whenever a base
    ///     grows. The colony's extent is emergent: wherever its members happen to be.
    ///
    ///     It still exists as a placed object because colony data has to live on a ZDO,
    ///     and in Valheim ZDOs belong to objects. That gives somewhere to write, something
    ///     findable without loading, and something to interact with.
    ///
    ///     Read-only through a ZDO, like WorkPostState, so a colony can be inspected
    ///     whether or not its hearth is instantiated.
    /// </summary>
    internal readonly struct ColonyState
    {
        private static readonly int NameKey = "kukolony.colony.name".GetStableHashCode();

        private static readonly int VillagersKey = "kukolony.colony.villagers".GetStableHashCode();
        private static readonly int ContainersKey = "kukolony.colony.containers".GetStableHashCode();
        private static readonly int StationsKey = "kukolony.colony.stations".GetStableHashCode();
        private static readonly int HomesKey = "kukolony.colony.homes".GetStableHashCode();

        private readonly ZDO _zdo;

        internal ColonyState(ZDO zdo)
        {
            _zdo = zdo;
        }

        internal bool IsValid => _zdo != null;

        internal ZDOID Id => _zdo?.m_uid ?? ZDOID.None;

        internal string Name => _zdo?.GetString(NameKey, string.Empty) ?? string.Empty;

        internal void SetName(string name) => _zdo.Set(NameKey, name);

        internal List<ZDOID> GetMembers(ColonyMemberKind kind) =>
            ColonyMembers.Decode(_zdo?.GetString(KeyFor(kind), string.Empty) ?? string.Empty);

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
            _zdo.Set(KeyFor(kind), ColonyMembers.Encode(members));
            return true;
        }

        internal bool RemoveMember(ColonyMemberKind kind, ZDOID member)
        {
            List<ZDOID> members = GetMembers(kind);
            if (!members.Remove(member))
            {
                return false;
            }

            _zdo.Set(KeyFor(kind), ColonyMembers.Encode(members));
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
                _zdo.Set(KeyFor(kind), ColonyMembers.Encode(alive));
            }

            return removed;
        }

        private static int KeyFor(ColonyMemberKind kind)
        {
            switch (kind)
            {
                case ColonyMemberKind.Villager: return VillagersKey;
                case ColonyMemberKind.Container: return ContainersKey;
                case ColonyMemberKind.Station: return StationsKey;
                default: return HomesKey;
            }
        }
    }
}
