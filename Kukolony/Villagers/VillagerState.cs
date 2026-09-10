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
        // A ZDOID occupies two ZDO slots (user id + object id), so its cached key is a
        // hash pair rather than a single hash.
        private static readonly KeyValuePair<int, int> StepTargetKey = ZDO.GetHashZDOID("kukolony.step.target");
        private static readonly int StepTargetPersistentKey = "kukolony.step.target.persistent-id.v1".GetStableHashCode();
        private static readonly int ClaimedSinceKey = "kukolony.step.since".GetStableHashCode();
        private static readonly int ActiveItemKey = "kukolony.step.item".GetStableHashCode();
        private static readonly int QueueKey = "kukolony.queue.v2".GetStableHashCode();
        private static readonly int QueuePositionKey = "kukolony.queue.position".GetStableHashCode();
        private static readonly int QueueAttemptKey = "kukolony.queue.attempt".GetStableHashCode();
        private static readonly int QueueProgressKey = "kukolony.queue.progress".GetStableHashCode();
        private static readonly int RuntimePhaseKey = "kukolony.queue.phase".GetStableHashCode();

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

        /// <summary>What the current step is acting on.</summary>
        internal ZDOID StepTarget => _zdo == null ? ZDOID.None : PersistentZdoReference.Resolve(
            _zdo.GetString(StepTargetPersistentKey, string.Empty), _zdo.GetZDOID(StepTargetKey));

        internal string ActiveItem => _zdo?.GetString(ActiveItemKey, string.Empty) ?? string.Empty;
        internal int QueuePosition => _zdo?.GetInt(QueuePositionKey, 0) ?? 0;
        internal int QueueAttempt => _zdo?.GetInt(QueueAttemptKey, 0) ?? 0;
        internal int QueueProgress => _zdo?.GetInt(QueueProgressKey, 0) ?? 0;
        internal string RuntimePhase => _zdo?.GetString(RuntimePhaseKey, string.Empty) ?? string.Empty;

        internal List<string> GetQueue()
        {
            List<string> result = new List<string>(); string encoded = _zdo?.GetString(QueueKey, string.Empty) ?? string.Empty;
            if (string.IsNullOrEmpty(encoded)) return result;
            try { ZPackage p = new ZPackage(encoded); if (p.ReadInt() != 2) return result; int count = p.ReadInt(); if(count < 0 || count > 256) return result; for(int i=0;i<count;i++) result.Add(p.ReadString()); }
            catch (System.Exception) { result.Clear(); }
            return result;
        }

        internal void SetHome(Vector3 position) => _zdo.Set(HomeKey, position);

        internal void SetActiveItem(string prefabName) => _zdo.Set(ActiveItemKey, prefabName);
        internal void SetQueue(List<string> jobs)
        {
            ZPackage p = new ZPackage(); p.Write(2); p.Write(jobs.Count); foreach (string id in jobs) p.Write(id ?? string.Empty);
            _zdo.Set(QueueKey, p.GetBase64()); SetQueuePosition(0); SetQueueAttempt(0); SetQueueProgress(0);
        }
        internal void SetQueuePosition(int position) => _zdo.Set(QueuePositionKey, position);
        internal void SetQueueAttempt(int attempt) => _zdo.Set(QueueAttemptKey, attempt);
        internal void SetQueueProgress(int progress) => _zdo.Set(QueueProgressKey, progress);
        internal void SetRuntimePhase(string phase) => _zdo.Set(RuntimePhaseKey, phase ?? string.Empty);
        internal void ResetRuntime()
        {
            SetQueueProgress(0);
            SetRuntimePhase(string.Empty);
            SetStepTarget(ZDOID.None);
            SetActiveItem(string.Empty);
        }

        /// <summary>
        ///     Net time this villager took its current target, used to expire claims held
        ///     by a villager that got stuck. Net time rather than local time because it is
        ///     shared across clients.
        /// </summary>
        internal double ClaimedSince => _zdo?.GetLong(ClaimedSinceKey, 0L) ?? 0L;

        /// <summary>
        ///     Sets the current target and stamps when it was taken.
        ///
        ///     The timestamp is written here rather than at call sites so a future step
        ///     cannot set a target and silently create a claim that never expires.
        /// </summary>
        internal void SetStepTarget(ZDOID target)
        {
            _zdo.Set(StepTargetKey, target);
            _zdo.Set(StepTargetPersistentKey, target.IsNone() ? string.Empty :
                PersistentZdoReference.Ensure(ZDOMan.instance?.GetZDO(target)));
            _zdo.Set(ClaimedSinceKey, target.IsNone() || ZNet.instance == null
                ? 0L
                : (long)ZNet.instance.GetTimeSeconds());
        }

        internal void MarkAppearanceRolled() => _zdo.Set(AppearanceKey, true);

        internal void SetName(string name) => _zdo.Set(NameKey, name);
    }
}
