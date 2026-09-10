namespace Kukolony.Colonies
{
    /// <summary>
    ///     The back-pointer every member keeps to its colony.
    ///
    ///     Membership is deliberately bidirectional. The colony holds the authoritative
    ///     lists, which is what lets us enumerate members without loading them. Each
    ///     member also records which colony it belongs to, which makes "whose am I?" a
    ///     single read rather than a search, and lets a member re-register itself if a
    ///     list ever goes stale.
    /// </summary>
    internal static class ColonyMembership
    {
        private static readonly System.Collections.Generic.KeyValuePair<int, int> ColonyKey =
            ZDO.GetHashZDOID("kukolony.colony");
        private static readonly int ColonyPersistentKey =
            "kukolony.colony.persistent-id.v1".GetStableHashCode();

        internal static ZDOID GetColony(ZDO memberZdo)
        {
            if (memberZdo == null) return ZDOID.None;
            return Core.PersistentZdoReference.Resolve(
                memberZdo.GetString(ColonyPersistentKey, string.Empty),
                memberZdo.GetZDOID(ColonyKey));
        }

        internal static void SetColony(ZDO memberZdo, ZDOID colony)
        {
            if (memberZdo == null) return;
            memberZdo.Set(ColonyKey, colony);
            ZDO target = ZDOMan.instance?.GetZDO(colony);
            memberZdo.Set(ColonyPersistentKey,
                colony.IsNone() ? string.Empty : Core.PersistentZdoReference.Ensure(target));
        }

        internal static bool BelongsTo(ZDO memberZdo, ZDOID colony) =>
            !colony.IsNone() && GetColony(memberZdo) == colony;
    }
}
