namespace Kukolony.Jobs
{
    /// <summary>
    ///     Everything the jobs remember about one villager, forgotten in one call.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Each job keeps a little state per villager outside the ZDO - which target it gave
    ///         up on, which wood it was working, what it refused and when. All of it is an
    ///         optimisation over asking again, and all of it is wrong the moment the villager's
    ///         situation changes out from under it.
    ///     </para>
    ///     <para>
    ///         <b>Gathered here because forgetting has two callers now and they must not drift.</b>
    ///         Removal had the list to itself; joining a party needs exactly the same thing, and a
    ///         second hand-written copy would be a job added to one and not the other - which
    ///         fails silently, because stale memory looks like a decision.
    ///     </para>
    ///     <para>
    ///         The bug that produced it is worth keeping: chopping remembers which wood a villager
    ///         is in and offers it first, so that a villager working a far flag does not walk home
    ///         for a single fallen branch. Take that villager into a party and the remembered wood
    ///         is the settlement - so it walked sixty metres back to its Kolony to fell a tree
    ///         there while standing next to the one it had been brought out for. Every part
    ///         behaving exactly as designed.
    ///     </para>
    /// </remarks>
    internal static class JobMemory
    {
        internal static void ForgetAll(ZDOID villager)
        {
            if (villager.IsNone()) return;

            Chop.ChopJob.Forget(villager);
            Tend.TendJob.Forget(villager);
            Craft.CraftJob.Forget(villager);
            Mine.MineJob.Forget(villager);
            Forage.ForageJob.Forget(villager);
            Farm.FarmJob.Forget(villager);
            Repair.RepairJob.Forget(villager);
            Unreachable.Forget(villager);
        }
    }
}
