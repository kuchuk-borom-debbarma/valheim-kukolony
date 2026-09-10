using System.Collections.Generic;
using Kukolony.Colonies;
using Kukolony.Villagers;

namespace Kukolony.Jobs.Work
{
    /// <summary>
    ///     Fetches the clothes and tools a villager's outfit asks for and it does not yet own.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The only job whose destination is the villager itself, so it ends when the item
    ///         is in the bag rather than when it has been carried somewhere. Putting it on is
    ///         not a step here at all: what a villager shows is a mirror of what it holds, kept
    ///         by <see cref="VillagerWardrobe"/> on every owned tick.
    ///     </para>
    ///     <para>
    ///         What to fetch comes from the outfit rather than from the job's item filters, so
    ///         one equip job serves a colony whose villagers are dressed differently. That is
    ///         also why the job declares no item setting: it would be ignored.
    ///     </para>
    /// </remarks>
    internal sealed class EquipWork : ColonyWork
    {
        public override ColonyJobType Type => ColonyJobType.Equip;

        /// <summary>
        ///     A source container is the only choice worth offering. There is no destination -
        ///     the villager is one - and nothing is searched for on the ground.
        /// </summary>
        public override JobSetting Reads => JobSetting.Source;

        public override WorkStep Next(WorkState state, WorkFacts facts) => FetchOnly.Next(state, facts);

        public override JobResult ChooseSource(WorkContext context, out string activity)
        {
            List<string> missing = Missing(context.Villager, context.Colony, context.Bag);
            if (missing.Count == 0) return JobOutcomes.Skipped(context.State, "already equipped", out activity);
            return ColonyJobEngine.ChooseStockedContainer(context, missing, out activity);
        }

        public override JobResult Collect(WorkContext context, out string activity) =>
            ColonyJobEngine.TakeFromTarget(context, Missing(context.Villager, context.Colony, context.Bag),
                out activity);

        /// <summary>Outfit items this villager does not have yet.</summary>
        private static List<string> Missing(Villager villager, Colony colony, Inventory bag) =>
            VillagerWardrobe.Missing(bag, colony.State.GetOutfit(villager.State.OutfitName));
    }
}
