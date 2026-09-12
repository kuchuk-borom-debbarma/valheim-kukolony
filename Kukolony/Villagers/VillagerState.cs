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
        private static readonly int CargoKey = "kukolony.job.cargo.v1".GetStableHashCode();
        private static readonly int EnergyKey = "kukolony.energy.v1".GetStableHashCode();
        private static readonly int EnergyAtKey = "kukolony.energy.at.v1".GetStableHashCode();
        private static readonly int RestingKey = "kukolony.energy.resting.v1".GetStableHashCode();
        private static readonly int RestRateKey = "kukolony.energy.rate.v1".GetStableHashCode();

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

        /// <summary>
        ///     What this trip is carrying: the prefab names taken as cargo, comma separated.
        /// </summary>
        /// <remarks>
        ///     Cargo is recorded rather than inferred from the bag, because a villager's bag is
        ///     also its wardrobe - equipment is a mirror of the bag, so the clothes it is
        ///     wearing are bag items too. A hauler that read the bag picked up its own leather
        ///     chestpiece, found that no chest in the settlement had asked for one, and
        ///     reported that it had nowhere to put what it was carrying. It was wearing it.
        ///
        ///     The name is only a hint, in the usual way: it counts as cargo when the bag
        ///     actually holds some, and a trip whose goods were taken out from under it goes
        ///     back to choosing rather than delivering nothing.
        /// </remarks>
        internal string Cargo => _zdo?.GetString(CargoKey, string.Empty) ?? string.Empty;

        internal void SetCargo(string manifest) => _zdo.Set(CargoKey, manifest ?? string.Empty);

        /// <summary>
        ///     Adds a kind of item to the trip's manifest.
        /// </summary>
        /// <remarks>
        ///     A set rather than a single name, because a trip carries several items. When this
        ///     held one name, taking a second kind of item overwrote the first, and everything
        ///     picked up before it stopped being recognised as cargo - so it rode around in the
        ///     bag forever, undeliverable and invisible to the job carrying it.
        /// </remarks>
        internal void AddCargo(string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return;

            string manifest = Cargo;
            foreach (string listed in manifest.Split(','))
            {
                if (listed == prefab) return;
            }

            SetCargo(manifest.Length == 0 ? prefab : manifest + "," + prefab);
        }

        /// <summary>
        ///     How much energy this villager had when its energy last changed, and when that was.
        /// </summary>
        /// <remarks>
        ///     Two fields rather than one ticking number, because a villager nobody is watching
        ///     must still be tiring and resting correctly - and because a settlement with no
        ///     population cap cannot afford to tick anything per villager per frame. A villager
        ///     that has never worked reads as fully rested, which is the right answer for one
        ///     that has just been born.
        /// </remarks>
        internal float StoredEnergy => _zdo?.GetFloat(EnergyKey, Energy.Full) ?? Energy.Full;

        internal double EnergyAt => _zdo?.GetLong(EnergyAtKey, 0L) ?? 0d;

        /// <summary>Whether it is currently resting, which decides which threshold applies.</summary>
        internal bool Resting => _zdo?.GetBool(RestingKey, false) ?? false;

        /// <summary>
        ///     Records energy and the moment it was true.
        /// </summary>
        /// <remarks>
        ///     The stamp is written here rather than at call sites, so no future caller can
        ///     record a value without recording when - which would make it decay from the wrong
        ///     instant, silently and forever after.
        /// </remarks>
        internal void SetEnergy(float energy)
        {
            if (_zdo == null) return;

            _zdo.Set(EnergyKey, energy);
            _zdo.Set(EnergyAtKey, ZNet.instance == null ? 0L : (long)ZNet.instance.GetTimeSeconds());
        }

        internal void SetResting(bool resting) => _zdo?.Set(RestingKey, resting);

        /// <summary>
        ///     How fast energy is coming back, so the sum can be done on demand.
        /// </summary>
        /// <remarks>
        ///     Stored because the rate changes during a rest - a villager walking to its bed
        ///     recovers at the slowest rate, and at the bed's rate once it lies down. Recording
        ///     the rate alongside the value and the time is what lets all of this stay three
        ///     numbers and one multiplication rather than something that has to be ticked.
        /// </remarks>
        internal float RestRate => _zdo?.GetFloat(RestRateKey, 0f) ?? 0f;

        internal void SetRestRate(float rate) => _zdo?.Set(RestRateKey, rate);

        /// <summary>Net time the current target was taken, so a stuck claim expires.</summary>
        internal double ClaimedSince => _zdo?.GetLong(ClaimedSinceKey, 0L) ?? 0L;

        /// <summary>
        ///     Takes a target, stamping when.
        /// </summary>
        /// <remarks>
        ///     The timestamp is written here rather than at call sites, so a future step cannot
        ///     set a target and silently create a claim that never expires.
        /// </remarks>
        /// <summary>
        ///     Records what this villager is working on, which is also its claim on the thing.
        /// </summary>
        /// <remarks>
        ///     The claim index is invalidated here rather than by callers, so no route to a
        ///     target can forget to do it - and every clearing path (<see cref="ClearTarget" />,
        ///     <see cref="ResetJob" />) runs through this one method.
        /// </remarks>
        internal void SetTarget(ZDOID target)
        {
            Remember(TargetKey, TargetTokenKey, target, stamp: true);
            Jobs.TargetClaims.Invalidate();
        }

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
            // The manifest is deliberately NOT cleared here. It describes what is in the bag,
            // and the bag survives a job being skipped, failed or restarted - so forgetting it
            // here left real goods in a real inventory that no job recognised as cargo any
            // more. The villager then reported nothing to haul while carrying ten wood, and
            // dropped the lot when it was eventually removed. It clears itself in Tick, the
            // moment the bag genuinely stops holding any of it.
            SetTarget(ZDOID.None);
            SetDestination(ZDOID.None);
            SetWorkState(0);
        }

        /// <summary>
        ///     Says this villager is still working on what it claimed.
        /// </summary>
        /// <remarks>
        ///     The claim's age is what stops a stuck villager holding a resource for ever, and
        ///     it was stamped once when the target was taken - so the clock ran against the
        ///     walk rather than against being stuck, and a villager on any errand longer than
        ///     the timeout had its claim expire underneath it while it was walking perfectly
        ///     well. A second villager then set off for the same thing.
        ///
        ///     Refreshed on progress only, which keeps the timeout doing exactly the job it was
        ///     added for: a villager that is getting nowhere stops refreshing, and its claim
        ///     ages out as before.
        /// </remarks>
        internal void TouchClaim()
        {
            if (_zdo == null || Target.IsNone() || ZNet.instance == null) return;

            // Rewritten rarely, not every tick. This is called from the walk, so a per-tick
            // write would mark every walking villager's ZDO dirty every frame - one hot field
            // written by the whole population, which is the contention shape a settlement with
            // no population cap cannot pay for. Refreshing once the stamp is a third of the way
            // to expiring keeps the claim comfortably alive at a fraction of the writes.
            double now = ZNet.instance.GetTimeSeconds();
            double age = now - ClaimedSince;
            if (age < ModConfig.ClaimTtlSeconds.Value / 3f) return;

            _zdo.Set(ClaimedSinceKey, (long)now);
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
