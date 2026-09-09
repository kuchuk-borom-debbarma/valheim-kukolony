using System.Collections.Generic;
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
        private static readonly KeyValuePair<int, int> PostKey = ZDO.GetHashZDOID("kukolony.post");
        private static readonly int StepKey = "kukolony.step".GetStableHashCode();
        private static readonly KeyValuePair<int, int> StepTargetKey = ZDO.GetHashZDOID("kukolony.step.target");
        private static readonly int ClaimedSinceKey = "kukolony.step.since".GetStableHashCode();
        private static readonly int ActiveItemKey = "kukolony.step.item".GetStableHashCode();

        // Distinct from HomeKey, which is the spawn-position fallback for a villager with
        // no bed assigned.
        private static readonly KeyValuePair<int, int> HomeBedKey = ZDO.GetHashZDOID("kukolony.home.bed");

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

        /// <summary>The work post this villager is bound to. None means unemployed.</summary>
        internal ZDOID Post => _zdo?.GetZDOID(PostKey) ?? ZDOID.None;

        internal bool HasPost => !Post.IsNone();

        /// <summary>How far through the current job's step list this villager is.</summary>
        internal int StepIndex => _zdo?.GetInt(StepKey, 0) ?? 0;

        /// <summary>What the current step is acting on.</summary>
        internal ZDOID StepTarget => _zdo?.GetZDOID(StepTargetKey) ?? ZDOID.None;

        /// <summary>The bed this villager calls home, or None if it has not been given one.</summary>
        internal ZDOID HomeBed => _zdo?.GetZDOID(HomeBedKey) ?? ZDOID.None;

        internal string ActiveItem => _zdo?.GetString(ActiveItemKey, string.Empty) ?? string.Empty;

        internal bool HasHomeBed => !HomeBed.IsNone();

        internal void SetHome(Vector3 position) => _zdo.Set(HomeKey, position);

        internal void SetHomeBed(ZDOID bed) => _zdo.Set(HomeBedKey, bed);

        internal void SetPost(ZDOID post) => _zdo.Set(PostKey, post);

        internal void SetStepIndex(int index) => _zdo.Set(StepKey, index);

        internal void SetActiveItem(string prefabName) => _zdo.Set(ActiveItemKey, prefabName);

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
            _zdo.Set(ClaimedSinceKey, target.IsNone() || ZNet.instance == null
                ? 0L
                : (long)ZNet.instance.GetTimeSeconds());
        }

        internal void MarkAppearanceRolled() => _zdo.Set(AppearanceKey, true);

        internal void SetName(string name) => _zdo.Set(NameKey, name);
    }
}
