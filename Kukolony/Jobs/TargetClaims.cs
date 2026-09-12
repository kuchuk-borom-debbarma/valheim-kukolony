using System.Collections.Generic;
using Kukolony.Villagers;

namespace Kukolony.Jobs
{
    /// <summary>
    ///     Stops two villagers walking to the same thing.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>There is no claim store.</b> A villager's current target already records what
    ///         it is working on, so the claim is that target being read by everyone else. No new
    ///         state, no sweeper, and no release path anyone can forget: every ending calls
    ///         <see cref="VillagerState.ResetJob" />, so the claim cleans itself up.
    ///     </para>
    ///     <para>
    ///         The claim is deliberately <em>not</em> written onto the target. <c>ZDO.Set</c>
    ///         ignores its <c>okForNotOwner</c> argument, so a write to a ZDO this peer does not
    ///         own lands locally and is clobbered on the next sync. Marking a loose item would
    ///         mean taking ownership first — an RPC round trip with exponential backoff, before
    ///         the villager has even started walking.
    ///     </para>
    ///     <para>
    ///         Whether something is exclusive is decided by who asks. Picking up asks, so two
    ///         villagers never target one stack; depositing does not, so any number may share a
    ///         chest.
    ///     </para>
    /// </remarks>
    internal static class TargetClaims
    {
        /// <summary>
        ///     A process-local index of who holds what, rebuilt when the loaded set changes.
        /// </summary>
        /// <remarks>
        ///     Purely a cache — the villagers' own ZDOs remain the truth. Without it every
        ///     decision scans every loaded villager, which is fine for a handful and is exactly
        ///     the cost a settlement with no population cap must not pay per decision.
        /// </remarks>
        private static readonly Dictionary<ZDOID, Villager> Held = new Dictionary<ZDOID, Villager>();
        private static int _builtFor = -1;
        private static float _builtAt;

        /// <summary>
        ///     How long an index is trusted before it is rebuilt, absent anyone saying it changed.
        /// </summary>
        /// <remarks>
        ///     A backstop only. Every target write calls <see cref="Invalidate" />, so the
        ///     timer exists for the case a villager stops existing without clearing its target
        ///     - not for ordinary claiming, which must be visible immediately.
        /// </remarks>
        private const float FreshnessSeconds = .5f;

        internal static bool IsClaimedByOther(ZDOID target, Villager asker)
        {
            if (target.IsNone() || !ModConfig.ClaimsEnabled.Value) return false;

            Refresh();
            if (!Held.TryGetValue(target, out Villager holder)) return false;
            if (holder == null || ReferenceEquals(holder, asker)) return false;

            // A villager that got stuck must not hold something forever. Past the TTL the
            // claim is ignored and whoever wants it may take it.
            double now = ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : 0d;
            if (now > 0d && now - holder.State.ClaimedSince > ModConfig.ClaimTtlSeconds.Value)
            {
                return false;
            }

            return true;
        }

        /// <summary>How many villagers are holding something, for diagnostics and checks.</summary>
        internal static int ActiveClaims()
        {
            Refresh();
            return Held.Count;
        }

        /// <summary>
        ///     Says the index is out of date, because somebody took or released a target.
        /// </summary>
        /// <remarks>
        ///     <b>Called on every target write, and that is not optional.</b> The index used to
        ///     be trusted for half a second on the reasoning that a claim taken this instant is
        ///     not something another villager could have known. That is wrong in the one case
        ///     the class exists for: villagers decide in the same frame, so two of them reaching
        ///     for one log both read an index in which neither holds anything, and both walk to
        ///     it. Measured at eleven collisions in twenty-three samples before this - the claim
        ///     was answering correctly and being asked too late to matter.
        /// </remarks>
        internal static void Invalidate() => _builtFor = -1;

        private static void Refresh()
        {
            // Rebuilt on a short timer rather than on every mutation: a claim taken this
            // instant by another villager is not something this one could have known anyway,
            // and the TTL already covers the stale direction.
            if (_builtFor == Villager.Instances.Count &&
                UnityEngine.Time.time - _builtAt < FreshnessSeconds)
            {
                return;
            }

            Held.Clear();
            foreach (Villager villager in Villager.Instances)
            {
                if (villager == null) continue;

                VillagerState state = villager.State;
                if (!state.IsValid) continue;

                ZDOID target = state.Target;
                if (target.IsNone()) continue;

                Held[target] = villager;
            }

            _builtFor = Villager.Instances.Count;
            _builtAt = UnityEngine.Time.time;
        }
    }
}
