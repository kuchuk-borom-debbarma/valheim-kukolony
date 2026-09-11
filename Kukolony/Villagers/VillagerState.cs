using System.Collections.Generic;
using Kukolony.Core;
using UnityEngine;

namespace Kukolony.Villagers
{
    /// <summary>
    ///     A villager's persistent state, stored on its ZDO.
    ///
    ///     Everything that must survive lives here rather than in fields on
    ///     <see cref="Villager" />. Two reasons, both forced by the game:
    ///     the ZDO is what Valheim saves and replicates, and ownership of a villager can
    ///     transfer to another player mid-behaviour, at which point any C# field state on
    ///     the previous owner is simply gone. See docs/multiplayer.md.
    ///
    ///     A readonly struct wrapping the ZDO, so reading state per tick costs nothing.
    ///     Mutation is through explicit Set methods, which keeps "this writes to the save
    ///     file" visible at the call site.
    /// </summary>
    internal readonly struct VillagerState
    {
        // Cached hashes. ZDO's string overloads hash on every call, and these are read
        // on a 20Hz path. Prefixed to stay clear of vanilla and other mods' keys.
        private static readonly int HomeKey = "kukolony.home".GetStableHashCode();
        private static readonly int NameKey = "kukolony.name".GetStableHashCode();
        private static readonly int AppearanceKey = "kukolony.appearance".GetStableHashCode();

        // Job runtime. On the villager rather than the colony, and not for tidiness: a
        // villager simulated by another peer cannot write the colony's ZDO at all, because
        // ZDO.Set ignores its okForNotOwner argument and the write is clobbered on the next
        // sync. One colony ZDO written by every villager every tick would also be the exact
        // contention shape a settlement with no population cap cannot carry.
        private static readonly int QueueKey = "kukolony.job.queue.v1".GetStableHashCode();
        private static readonly int QueuePositionKey = "kukolony.job.position".GetStableHashCode();
        private static readonly int QueueAttemptKey = "kukolony.job.attempt".GetStableHashCode();
        private static readonly int WorkStateKey = "kukolony.job.state".GetStableHashCode();

        // A ZDOID occupies two ZDO slots, so its cached key is a hash pair.
        private static readonly KeyValuePair<int, int> TargetKey = ZDO.GetHashZDOID("kukolony.job.target");
        private static readonly int TargetTokenKey = "kukolony.job.target.token".GetStableHashCode();
        private static readonly int ClaimedSinceKey = "kukolony.job.claimed".GetStableHashCode();
        private static readonly KeyValuePair<int, int> DestinationKey = ZDO.GetHashZDOID("kukolony.job.destination");
        private static readonly int DestinationTokenKey = "kukolony.job.destination.token".GetStableHashCode();

        private readonly ZDO _zdo;

        internal VillagerState(ZDO zdo)
        {
            _zdo = zdo;
        }

        internal bool IsValid => _zdo != null;

        /// <summary>Where this villager belongs. Vector3.zero means "not yet assigned".</summary>
        internal Vector3 Home => _zdo?.GetVec3(HomeKey, Vector3.zero) ?? Vector3.zero;

        internal bool HasHome => Home != Vector3.zero;

        /// <summary>Display name. Empty until the owner assigns one.</summary>
        internal string Name => _zdo?.GetString(NameKey, string.Empty) ?? string.Empty;

        internal bool HasName => !string.IsNullOrEmpty(Name);

        /// <summary>
        ///     Whether this villager has already rolled its face and outfit. VisEquipment
        ///     persists the appearance itself, but nothing there says "chosen" versus
        ///     "default", so we record the decision.
        /// </summary>
        internal bool HasAppearance => _zdo?.GetBool(AppearanceKey, false) ?? false;

        internal void SetHome(Vector3 position) => _zdo.Set(HomeKey, position);

        internal void MarkAppearanceRolled() => _zdo.Set(AppearanceKey, true);

        internal void SetName(string name) => _zdo.Set(NameKey, name);

        /// <summary>The ordered job ids this villager works through, as a ring.</summary>
        internal List<string> GetQueue() => JobQueueCodec.Decode(_zdo?.GetString(QueueKey, string.Empty) ?? string.Empty);

        internal void SetQueue(List<string> jobs)
        {
            _zdo.Set(QueueKey, JobQueueCodec.Encode(jobs));
            SetQueuePosition(0);
            SetQueueAttempt(0);
            ResetJob();
        }

        internal int QueuePosition => _zdo?.GetInt(QueuePositionKey, 0) ?? 0;

        internal void SetQueuePosition(int position) => _zdo.Set(QueuePositionKey, position);

        /// <summary>Repetitions of the current entry already consumed.</summary>
        internal int QueueAttempt => _zdo?.GetInt(QueueAttemptKey, 0) ?? 0;

        internal void SetQueueAttempt(int attempt) => _zdo.Set(QueueAttemptKey, attempt);

        /// <summary>
        ///     How far through its current job this villager has got.
        /// </summary>
        /// <remarks>
        ///     Zero is the safe resume point for every job, which is why every work-state enum
        ///     puts its "decide what to do" state first. An unwritten field, a save from an
        ///     older build, and a villager that has never worked all read the same and all
        ///     resume somewhere harmless.
        /// </remarks>
        internal int WorkState => _zdo?.GetInt(WorkStateKey, 0) ?? 0;

        internal void SetWorkState(int state) => _zdo.Set(WorkStateKey, state);

        /// <summary>
        ///     What this villager is working on. Also its claim: nobody else may take it.
        /// </summary>
        /// <remarks>
        ///     The claim is the villager's own recorded target rather than a mark written onto
        ///     the target itself. Writing it onto the target would mean owning the target first
        ///     - an RPC round trip with exponential backoff before the villager has even started
        ///     walking - and a write to a ZDO we do not own is discarded anyway.
        /// </remarks>
        internal ZDOID Target => _zdo == null ? ZDOID.None : PersistentZdoReference.Resolve(
            _zdo.GetString(TargetTokenKey, string.Empty), _zdo.GetZDOID(TargetKey));

        /// <summary>Where the current load is bound. Not exclusive - chests are shared.</summary>
        internal ZDOID Destination => _zdo == null ? ZDOID.None : PersistentZdoReference.Resolve(
            _zdo.GetString(DestinationTokenKey, string.Empty), _zdo.GetZDOID(DestinationKey));

        /// <summary>Net time the current target was taken, so a stuck claim expires.</summary>
        internal double ClaimedSince => _zdo?.GetLong(ClaimedSinceKey, 0L) ?? 0L;

        /// <summary>
        ///     Takes a target, stamping when.
        /// </summary>
        /// <remarks>
        ///     The timestamp is written here rather than at call sites, so a future step cannot
        ///     set a target and silently create a claim that never expires.
        /// </remarks>
        internal void SetTarget(ZDOID target) => Remember(TargetKey, TargetTokenKey, target, stamp: true);

        internal void SetDestination(ZDOID destination) =>
            Remember(DestinationKey, DestinationTokenKey, destination, stamp: false);

        /// <summary>
        ///     Releases the target only, keeping the trip.
        /// </summary>
        /// <remarks>
        ///     What a villager wants after taking one item from a pile: the claim on that item
        ///     goes, the destination and the work state stay, and the sweep carries on.
        /// </remarks>
        internal void ClearTarget() => SetTarget(ZDOID.None);

        /// <summary>
        ///     Abandons the whole trip: target, destination and progress.
        /// </summary>
        /// <remarks>
        ///     What every ending wants. Because each of Completed, Failed and Skipped calls it,
        ///     the claim cleans itself up with no sweeper and no release path to forget.
        /// </remarks>
        internal void ResetJob()
        {
            SetTarget(ZDOID.None);
            SetDestination(ZDOID.None);
            SetWorkState(0);
        }

        private void Remember(KeyValuePair<int, int> idKey, int tokenKey, ZDOID value, bool stamp)
        {
            _zdo.Set(idKey, value);
            _zdo.Set(tokenKey, value.IsNone()
                ? string.Empty
                : PersistentZdoReference.Ensure(ZDOMan.instance?.GetZDO(value)));

            if (!stamp) return;
            _zdo.Set(ClaimedSinceKey, value.IsNone() || ZNet.instance == null
                ? 0L
                : (long)ZNet.instance.GetTimeSeconds());
        }
    }
}
