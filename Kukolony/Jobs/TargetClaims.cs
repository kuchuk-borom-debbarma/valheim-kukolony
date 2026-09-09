using Kukolony.Villagers;

namespace Kukolony.Jobs
{
    /// <summary>
    ///     Stops two villagers walking to the same thing.
    ///
    ///     There is no separate claim store: a villager's current step target already
    ///     records what it is working on, so the claim is that target being read by
    ///     everyone else. No new state, and it cleans itself up - the target is cleared on
    ///     pickup, on deposit, and whenever a step fails.
    ///
    ///     The claim is deliberately not written onto the target. ZDO.Set ignores its
    ///     okForNotOwner argument, so a write to a ZDO we do not own lands locally and is
    ///     clobbered on the next sync from its owner. Claiming a loose item that way would
    ///     mean taking ownership first - an RPC round trip with exponential backoff,
    ///     before the villager has even started walking.
    ///
    ///     Whether something is exclusive is decided by who asks. Find steps ask, so two
    ///     villagers never target the same log. Deposit steps do not, so any number of
    ///     villagers can share one chest.
    /// </summary>
    internal static class TargetClaims
    {
        /// <summary>
        ///     Whether another villager is already working on this.
        /// </summary>
        /// <param name="target">The thing being considered.</param>
        /// <param name="asker">The villager asking, so its own claim does not block it.</param>
        internal static bool IsClaimedByOther(ZDOID target, Villager asker)
        {
            if (target.IsNone() || !ModConfig.ClaimsEnabled.Value)
            {
                return false;
            }

            double now = ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : 0d;
            double ttl = ModConfig.ClaimTtlSeconds.Value;

            foreach (Villager other in Villager.Instances)
            {
                if (other == null || ReferenceEquals(other, asker))
                {
                    continue;
                }

                VillagerState state = other.State;
                if (!state.IsValid || state.StepTarget != target)
                {
                    continue;
                }

                // A villager that got stuck must not hold a resource forever. Past the
                // TTL the claim is ignored and whoever wants it may take it.
                if (now > 0d && now - state.ClaimedSince > ttl)
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        /// <summary>Villagers currently holding a claim. Diagnostics only.</summary>
        internal static int ActiveClaimCount()
        {
            int count = 0;
            foreach (Villager villager in Villager.Instances)
            {
                if (villager != null && villager.State.IsValid && !villager.State.StepTarget.IsNone())
                {
                    count++;
                }
            }

            return count;
        }
    }
}
