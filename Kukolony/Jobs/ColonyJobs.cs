using System;
using System.Collections.Generic;
using Kukolony.Colonies;

namespace Kukolony.Jobs
{
    /// <summary>
    ///     The concrete jobs the engine can execute. A type fixes the executor and the
    ///     structure capability a target must expose; it is not the unit players edit —
    ///     they edit a <see cref="ColonyJobConfig"/> pipeline built from these.
    /// </summary>
    internal enum ColonyJobType
    {
        HaulLoose,
        Transfer,
        FuelFireplaces,
        OperateSmelters,
        OperateCookingStations,
        OperateFermenters,
        CollectBeehives,
        Equip,
        Chop
    }

    /// <summary>How a job chooses among the colony's registered structures.</summary>
    internal enum TargetMode { All, Selected, Ignore }

    /// <summary>
    ///     Outcome of one engine tick, and the input to queue scheduling.
    ///     <c>Running</c> keeps the current entry and consumes nothing.
    ///     <c>Completed</c> and <c>Failed</c> each consume one configured count.
    ///     <c>Skipped</c> means no work is currently useful — no eligible target, missing
    ///     input, or stock already at its limit — and consumes no count before yielding to
    ///     the next entry, so an idle job cannot starve the rest of the queue.
    /// </summary>
    internal enum JobResult { Running, Completed, Failed, Skipped }

    /// <summary>
    ///     A player-editable job: which work to do, and every setting that work reads.
    ///     Owned by a colony and persisted on the hearth ZDO as a versioned record.
    /// </summary>
    /// <remarks>
    ///     Jobs used to be assembled from pieces, each carrying its own copy of these settings.
    ///     They are now named work with one settings screen, so a setting means the same thing
    ///     wherever the job reads it and there is one place to change it.
    /// </remarks>
    internal sealed class ColonyJobConfig
    {
        internal string Id = Guid.NewGuid().ToString("N");
        internal ColonyJobType Type;
        internal string Name = string.Empty;
        internal TargetMode Targets = TargetMode.All;
        internal readonly List<ZDOID> SelectedStructures = new List<ZDOID>();
        internal readonly List<string> ItemFilters = new List<string>();
        internal ZDOID Source = ZDOID.None;
        internal ZDOID Destination = ZDOID.None;
        internal int StockLimit;
        internal int Count = 1;
        internal bool Reservations = true;
        internal float SearchRadius = 32f;
        internal float StopDistance = 2f;

        /// <summary>
        ///     Puts the load down where the villager stands instead of into a container, so a
        ///     job can gather to a pile without one being registered for it.
        /// </summary>
        internal bool DropOnGround;

        /// <summary>
        ///     Copies the job. With <paramref name="includeTargets"/> false this strips every
        ///     ZDO-specific target — the portable-preset contract — so the copy cannot carry a
        ///     reference that is meaningless in another colony or after a reload. Callers assign
        ///     a fresh <see cref="Id"/>; the clone deliberately keeps the original so preset
        ///     application can decide.
        /// </summary>
        internal ColonyJobConfig Clone(bool includeTargets)
        {
            ColonyJobConfig copy = new ColonyJobConfig
            {
                Id = Id,
                Type = Type,
                Name = Name,
                Targets = includeTargets ? Targets : TargetMode.All,
                Source = includeTargets ? Source : ZDOID.None,
                Destination = includeTargets ? Destination : ZDOID.None,
                StockLimit = StockLimit,
                Count = Count,
                Reservations = Reservations,
                SearchRadius = SearchRadius,
                StopDistance = StopDistance,
                DropOnGround = DropOnGround
            };
            copy.ItemFilters.AddRange(ItemFilters);
            if (includeTargets) copy.SelectedStructures.AddRange(SelectedStructures);
            return copy;
        }
    }

    /// <summary>
    ///     A saved job configuration. Colony-local presets retain exact structure IDs;
    ///     portable presets clear them. Applying either mints a new job ID, so a preset
    ///     never aliases a live mutable configuration.
    /// </summary>
    internal sealed class JobPreset
    {
        internal string Name = string.Empty;
        internal bool ColonyLocal;
        internal ColonyJobConfig Settings = new ColonyJobConfig();
    }

    /// <summary>
    ///     Code-owned system defaults. A colony that has never been edited stores no job
    ///     copies; these are materialised on demand, so defaults can change between
    ///     versions without migrating untouched colonies.
    /// </summary>
    internal static class ColonyJobCatalog
    {
        internal static readonly ColonyJobType[] All =
            (ColonyJobType[])Enum.GetValues(typeof(ColonyJobType));

        /// <summary>
        ///     The seven starter jobs, each a valid editable pipeline with a sensible item
        ///     filter. IDs are stable (<c>default.&lt;type&gt;</c>) so a villager queue that
        ///     references a default keeps resolving across sessions.
        /// </summary>
        internal static List<ColonyJobConfig> CreateDefaults()
        {
            List<ColonyJobConfig> jobs = new List<ColonyJobConfig>();
            foreach (ColonyJobType type in All)
            {
                ColonyJobConfig job = new ColonyJobConfig
                {
                    Id = "default." + type.ToString().ToLowerInvariant(),
                    Type = type,
                    Name = DisplayName(type),
                    Count = 1
                };
                switch (type)
                {
                    case ColonyJobType.HaulLoose:
                    case ColonyJobType.Transfer:
                        job.ItemFilters.Add("Wood");
                        break;
                    case ColonyJobType.FuelFireplaces:
                        job.ItemFilters.Add("Wood");
                        break;
                    case ColonyJobType.OperateSmelters:
                        job.ItemFilters.Add("Coal");
                        job.ItemFilters.Add("CopperOre");
                        break;
                    case ColonyJobType.OperateCookingStations:
                        job.ItemFilters.Add("RawMeat");
                        break;
                    case ColonyJobType.OperateFermenters:
                        job.ItemFilters.Add("BarleyWineBase");
                        break;
                    case ColonyJobType.CollectBeehives:
                        job.ItemFilters.Add("Honey");
                        break;
                }
                jobs.Add(job);
            }
            return jobs;
        }

        /// <summary>The next job type in the catalogue, so a job's work can be changed in place.</summary>
        internal static ColonyJobType Next(ColonyJobType type)
        {
            int index = System.Array.IndexOf(All, type);
            return index < 0 ? All[0] : All[(index + 1) % All.Length];
        }

        /// <summary>
        ///     One line saying what this work actually does, shown where a job's pipeline used
        ///     to be listed. A player can no longer read the steps off the screen, so the job
        ///     has to say them - short enough to fit the panel column, because a sentence cut
        ///     off halfway explains nothing.
        /// </summary>
        internal static string Describe(ColonyJobType type)
        {
            switch (type)
            {
                case ColonyJobType.HaulLoose: return "Picks items off the ground and stores them.";
                case ColonyJobType.Transfer: return "Moves items between two containers.";
                case ColonyJobType.FuelFireplaces: return "Fetches fuel and feeds a fireplace.";
                case ColonyJobType.OperateSmelters: return "Loads ore or fuel into a smelter or kiln.";
                case ColonyJobType.OperateCookingStations: return "Puts raw food on a cooking station.";
                case ColonyJobType.OperateFermenters: return "Starts a fermenter with a mead base.";
                case ColonyJobType.CollectBeehives: return "Taps a hive and stores the honey.";
                case ColonyJobType.Equip: return "Fetches the outfit this villager lacks.";
                case ColonyJobType.Chop: return "Fells trees and cuts up logs. Needs an axe.";
                default: return string.Empty;
            }
        }

        internal static string DisplayName(ColonyJobType type)
        {
            switch (type)
            {
                case ColonyJobType.HaulLoose: return "Haul loose items";
                case ColonyJobType.Transfer: return "Transfer containers";
                case ColonyJobType.FuelFireplaces: return "Fuel fireplaces";
                case ColonyJobType.OperateSmelters: return "Operate smelters and kilns";
                case ColonyJobType.OperateCookingStations: return "Operate cooking stations";
                case ColonyJobType.OperateFermenters: return "Operate fermenters";
                case ColonyJobType.CollectBeehives: return "Collect beehives";
                case ColonyJobType.Equip: return "Fetch outfit";
                case ColonyJobType.Chop: return "Chop wood";
                // Work with no name here is work someone has not finished adding. Empty
                // rather than a plausible-looking fallback: a fallback puts a wrong but
                // convincing label in the panel instead of failing the catalogue check that
                // exists to catch exactly this. It already did once.
                default: return string.Empty;
            }
        }

        /// <summary>
        ///     The capability a structure must expose to be a legal target for this job type.
        ///     Drives both starter-pipeline construction and target-picker filtering.
        /// </summary>
        internal static StructureCapability RequiredCapability(ColonyJobType type)
        {
            switch (type)
            {
                case ColonyJobType.FuelFireplaces: return StructureCapability.Fireplace;
                case ColonyJobType.OperateSmelters: return StructureCapability.Smelter;
                case ColonyJobType.OperateCookingStations: return StructureCapability.CookingStation;
                case ColonyJobType.OperateFermenters: return StructureCapability.Fermenter;
                case ColonyJobType.CollectBeehives: return StructureCapability.BeeHive;
                default: return StructureCapability.Container;
            }
        }
    }
}
