using System.Collections.Generic;
using Kukolony.Core;
using Kukolony.Jobs.Steps;

namespace Kukolony.Jobs
{
    /// <summary>
    ///     The jobs a work post can be set to, loaded from definition files.
    ///
    ///     A built-in haul job is kept as a fallback. If every definition on disk is
    ///     broken, a player should still have working villagers and an error explaining
    ///     what to fix - not a colony that silently stops.
    /// </summary>
    internal static class JobLibrary
    {
        internal const string Haul = "haul";

        private static readonly Dictionary<string, Job> Jobs = new Dictionary<string, Job>();

        /// <summary>Whether the jobs in use came from disk or from the built-in fallback.</summary>
        internal static bool LoadedFromDefinitions { get; private set; }

        internal static IEnumerable<string> Ids => Jobs.Keys;

        internal static int Count => Jobs.Count;

        internal static void Load()
        {
            Jobs.Clear();
            LoadedFromDefinitions = false;

            foreach (JobDefinition definition in JobDefinitionLoader.LoadAll())
            {
                List<IJobStep> steps = new List<IJobStep>();
                foreach (JobStepSpec spec in definition.Steps)
                {
                    // The loader already rejected unknown types, so this cannot be null.
                    steps.Add(JobStepFactory.TryBuild(spec));
                }

                Jobs[definition.Id] = new Job(definition.Id, steps);
                LoadedFromDefinitions = true;
            }

            if (Jobs.Count == 0)
            {
                Log.Warning("No usable job definitions - falling back to the built-in haul job.");
                Jobs[Haul] = BuildFallbackHaul();
            }
        }

        internal static Job Find(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            return Jobs.TryGetValue(id, out Job job) ? job : null;
        }

        /// <summary>
        ///     Mirrors haul.json. Kept in code purely so a broken or missing definitions
        ///     folder cannot leave a world with no jobs at all.
        /// </summary>
        private static Job BuildFallbackHaul()
        {
            return new Job(Haul, new IJobStep[]
            {
                new FindGroundItemStep(),
                new MoveToTargetStep(stopDistance: 2f),
                new PickUpItemStep(),
                new ResolveDestinationStep(),
                new MoveToTargetStep(stopDistance: 2f),
                new DepositItemStep()
            });
        }
    }
}
