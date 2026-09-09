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

        internal static ZDOID GetColony(ZDO memberZdo) =>
            memberZdo?.GetZDOID(ColonyKey) ?? ZDOID.None;

        internal static void SetColony(ZDO memberZdo, ZDOID colony) =>
            memberZdo?.Set(ColonyKey, colony);

        internal static bool BelongsTo(ZDO memberZdo, ZDOID colony) =>
            !colony.IsNone() && GetColony(memberZdo) == colony;
    }
}
