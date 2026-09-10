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
        CollectBeehives
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
    ///     One step in a pipeline, plus the settings it runs with.
    /// </summary>
    /// <remarks>
    ///     Every setting has an "inherit" value - an empty list, no container, a negative
    ///     number - meaning "use the job's". That is what a pipeline saved before pieces had
    ///     settings means, so older records need no conversion, and it keeps a piece's
    ///     configuration to only what someone deliberately changed.
    ///
    ///     Which of these a given kind actually reads is declared by
    ///     <see cref="PieceCustomisation.Uses"/> rather than left implicit.
    /// </remarks>
    internal sealed class JobPiece
    {
        internal JobPieceKind Kind;

        /// <summary>Narrows which registered structures a selection piece may resolve to.</summary>
        internal StructureCapability Capability;

        /// <summary>Items this step works with. Empty inherits the job's list.</summary>
        internal readonly List<string> ItemFilters = new List<string>();

        /// <summary>Container this step uses, overriding the job's source or destination.</summary>
        internal ZDOID Container = ZDOID.None;

        /// <summary>Structures this step may choose between. Empty inherits the job's.</summary>
        internal readonly List<ZDOID> SelectedStructures = new List<ZDOID>();

        /// <summary>Stock threshold for this step. Negative inherits the job's.</summary>
        internal int StockLimit = -1;

        /// <summary>Items to move in one visit. Negative inherits, and one is the floor.</summary>
        internal int Amount = -1;

        /// <summary>How far this step searches. Negative inherits the job's.</summary>
        internal float SearchRadius = -1f;

        /// <summary>How close to stand before acting. Negative inherits the job's.</summary>
        internal float StopDistance = -1f;

        /// <summary>How this step scopes structures. Negative inherits the job's mode.</summary>
        internal int Targets = -1;

        /// <summary>Whether this step reserves its target: -1 inherit, 0 never, 1 always.</summary>
        internal int Reservations = -1;

        internal JobPiece Clone()
        {
            JobPiece copy = new JobPiece
            {
                Kind = Kind, Capability = Capability, Container = Container,
                StockLimit = StockLimit, Amount = Amount, SearchRadius = SearchRadius,
                StopDistance = StopDistance, Targets = Targets, Reservations = Reservations
            };
            copy.ItemFilters.AddRange(ItemFilters);
            copy.SelectedStructures.AddRange(SelectedStructures);
            return copy;
        }
    }

    /// <summary>
    ///     Resolves a setting for one step: the piece's own value when it has one, otherwise
    ///     the job's. Keeping the fallback in one place stops each executor inventing its own
    ///     idea of what an unset override means.
    /// </summary>
    internal static class PieceSettings
    {
        internal static List<string> Filters(ColonyJobConfig job, JobPiece piece) =>
            piece != null && piece.ItemFilters.Count > 0 ? piece.ItemFilters : job.ItemFilters;

        internal static ZDOID Container(ColonyJobConfig job, JobPiece piece, ZDOID jobValue) =>
            piece != null && !piece.Container.IsNone() ? piece.Container : jobValue;

        internal static List<ZDOID> Structures(ColonyJobConfig job, JobPiece piece) =>
            piece != null && piece.SelectedStructures.Count > 0 ? piece.SelectedStructures : job.SelectedStructures;

        internal static int StockLimit(ColonyJobConfig job, JobPiece piece) =>
            piece != null && piece.StockLimit >= 0 ? piece.StockLimit : job.StockLimit;

        internal static int Amount(JobPiece piece) =>
            piece != null && piece.Amount > 0 ? piece.Amount : 1;

        internal static float SearchRadius(ColonyJobConfig job, JobPiece piece) =>
            piece != null && piece.SearchRadius >= 0f ? piece.SearchRadius : job.SearchRadius;

        internal static float StopDistance(ColonyJobConfig job, JobPiece piece) =>
            piece != null && piece.StopDistance >= 0f ? piece.StopDistance : job.StopDistance;

        internal static TargetMode Targets(ColonyJobConfig job, JobPiece piece) =>
            piece != null && piece.Targets >= 0 ? (TargetMode)piece.Targets : job.Targets;

        internal static bool Reservations(ColonyJobConfig job, JobPiece piece) =>
            piece != null && piece.Reservations >= 0 ? piece.Reservations != 0 : job.Reservations;

        /// <summary>
        ///     Every item any step of this job cares about. Whether a villager is carrying
        ///     something worth keeping is a question about the job as a whole rather than one
        ///     step. An empty result means anything counts.
        /// </summary>
        internal static List<string> AllFilters(ColonyJobConfig job)
        {
            List<string> all = new List<string>(job.ItemFilters);
            foreach (JobPiece piece in job.Pieces)
                foreach (string item in piece.ItemFilters)
                    if (!all.Contains(item)) all.Add(item);
            return all;
        }
    }

    /// <summary>Builds and validates the piece sequence behind a job.</summary>
    internal static class JobPipeline
    {
        /// <summary>
        ///     The starter pipeline for a job type. Haul and Transfer differ in how they
        ///     acquire (find a loose item versus take from a chosen source); everything else
        ///     shares select → move → operate against the type's required capability.
        /// </summary>
        internal static List<JobPiece> For(ColonyJobType type)
        {
            List<JobPiece> pieces = new List<JobPiece> { new JobPiece { Kind = JobPieceKind.Start }, new JobPiece { Kind = JobPieceKind.StopAtStockLimit } };
            switch (type)
            {
                case ColonyJobType.HaulLoose:
                    pieces.Add(new JobPiece { Kind = JobPieceKind.FindLooseItem }); pieces.Add(new JobPiece { Kind = JobPieceKind.MoveToTarget }); pieces.Add(new JobPiece { Kind = JobPieceKind.PickUp }); pieces.Add(new JobPiece { Kind = JobPieceKind.SelectTarget, Capability = StructureCapability.Container }); pieces.Add(new JobPiece { Kind = JobPieceKind.MoveToTarget }); pieces.Add(new JobPiece { Kind = JobPieceKind.PutItem }); break;
                case ColonyJobType.Transfer:
                    pieces.Add(new JobPiece { Kind = JobPieceKind.SelectSource, Capability = StructureCapability.Container }); pieces.Add(new JobPiece { Kind = JobPieceKind.MoveToTarget }); pieces.Add(new JobPiece { Kind = JobPieceKind.TakeItem }); pieces.Add(new JobPiece { Kind = JobPieceKind.SelectTarget, Capability = StructureCapability.Container }); pieces.Add(new JobPiece { Kind = JobPieceKind.MoveToTarget }); pieces.Add(new JobPiece { Kind = JobPieceKind.PutItem }); break;
                default:
                    pieces.Add(new JobPiece { Kind = JobPieceKind.SelectTarget, Capability = ColonyJobCatalog.RequiredCapability(type) }); pieces.Add(new JobPiece { Kind = JobPieceKind.MoveToTarget }); pieces.Add(new JobPiece { Kind = JobPieceKind.OperateStation, Capability = ColonyJobCatalog.RequiredCapability(type) }); break;
            }
            pieces.Add(new JobPiece { Kind = JobPieceKind.End }); return pieces;
        }

        /// <summary>
        ///     True when the pipeline's shape is executable. Delegates to the pure rules so the
        ///     deterministic tests validate exactly what the mod does, with no Unity present.
        /// </summary>
        internal static bool IsValid(ColonyJobConfig job, out string message)
        {
            List<JobPiece> pieces = job.Pieces;
            List<JobPieceKind> kinds = pieces.ConvertAll(piece => piece.Kind);
            return PipelineShapeRules.IsValid(kinds, out message);
        }
    }

    /// <summary>
    ///     A player-editable job: the pipeline plus the customisation its pieces read.
    ///     Owned by a colony and persisted on the hearth ZDO as a versioned record.
    ///     Each executor interprets only the settings it needs.
    /// </summary>
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
        internal readonly List<JobPiece> Pieces = new List<JobPiece>();

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
                StopDistance = StopDistance
            };
            copy.ItemFilters.AddRange(ItemFilters);
            if (includeTargets) copy.SelectedStructures.AddRange(SelectedStructures);
            foreach (JobPiece piece in Pieces) copy.Pieces.Add(piece.Clone());
            if (copy.Pieces.Count == 0) copy.Pieces.AddRange(JobPipeline.For(copy.Type));
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
                job.Pieces.AddRange(JobPipeline.For(type));
            }
            return jobs;
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
                default: return "Collect beehives";
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
