using System;
using System.Collections.Generic;

namespace Kukolony.Core
{
    /// <summary>
    ///     Stable identity for references between saved ZDOs. Valheim's chunked save
    ///     format deliberately assigns new ZDOIDs while loading, so a raw ZDOID is only
    ///     a runtime address. The token lives on the target ZDO and is resolved through
    ///     ZDOExtraData's indexed string registry—never through a scene scan.
    /// </summary>
    internal static class PersistentZdoReference
    {
        private static readonly int Key = "kukolony.persistent-id.v1".GetStableHashCode();
        private static readonly Dictionary<string, ZDOID> Cache = new Dictionary<string, ZDOID>();
        private static ZDOMan _cacheOwner;

        internal static string Ensure(ZDO zdo)
        {
            if (zdo == null || !zdo.IsValid()) return string.Empty;
            string value = zdo.GetString(Key, string.Empty);
            if (!string.IsNullOrEmpty(value)) return value;
            if (!zdo.IsOwner()) return string.Empty;
            value = Guid.NewGuid().ToString("N");
            zdo.Set(Key, value);
            PrepareCache();
            Cache[value] = zdo.m_uid;
            return value;
        }

        internal static string Get(ZDO zdo) =>
            zdo?.GetString(Key, string.Empty) ?? string.Empty;

        internal static ZDOID Resolve(string persistentId, ZDOID runtimeFallback = default(ZDOID))
        {
            if (ZDOMan.instance == null) return ZDOID.None;
            PrepareCache();
            // The raw id is trusted only when there is no token to check it against, or
            // when the object it finds actually carries that token.
            //
            // Without the token check this returned the wrong object outright: handed a
            // chest's token and another live object's address, it answered with the other
            // object. Demonstrated by reverting this guard and watching the paired check fail
            // while its three controls passed - one session, both objects loaded, no reload
            // involved. Nothing downstream could tell, because the answer resolves and is
            // valid; it is simply something else.
            //
            // Stale addresses are reachable because loading rewrites ZDOIDs: the benchmark's
            // villager is 597515522:9013 before a save and 1:2605 after it, and records
            // persist whatever they last resolved to. How often a stale address lands on a
            // live object has not been measured - the guard costs one string compare and does
            // not depend on knowing.
            if (!runtimeFallback.IsNone())
            {
                ZDO direct = ZDOMan.instance.GetZDO(runtimeFallback);
                if (direct != null &&
                    (string.IsNullOrEmpty(persistentId) ||
                     direct.GetString(Key, string.Empty) == persistentId))
                {
                    return runtimeFallback;
                }
            }

            if (string.IsNullOrEmpty(persistentId)) return ZDOID.None;

            if (Cache.TryGetValue(persistentId, out ZDOID cached))
            {
                ZDO cachedZdo = ZDOMan.instance.GetZDO(cached);
                if (cachedZdo != null && cachedZdo.GetString(Key, string.Empty) == persistentId)
                    return cached;
                Cache.Remove(persistentId);
            }

            List<ZDOID> candidates = ZDOExtraData.GetAllZDOIDsWithHash(
                ZDOExtraData.Type.String, Key);
            foreach (ZDOID candidate in candidates)
            {
                ZDO zdo = ZDOMan.instance.GetZDO(candidate);
                if (zdo != null && zdo.GetString(Key, string.Empty) == persistentId)
                {
                    Cache[persistentId] = candidate;
                    return candidate;
                }
            }
            // Preserve a dangling runtime address when the object was genuinely
            // deleted. Structure records intentionally remain visible as invalid until
            // the player removes them; callers can distinguish that from a live target
            // with ZDOMan.GetZDO.
            return runtimeFallback;
        }

        private static void PrepareCache()
        {
            if (ReferenceEquals(_cacheOwner, ZDOMan.instance)) return;
            Cache.Clear();
            _cacheOwner = ZDOMan.instance;
        }
    }
}
