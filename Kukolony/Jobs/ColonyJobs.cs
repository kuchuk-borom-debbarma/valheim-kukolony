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

    // "Customisation" is the player-facing term.  These typed values are the
    // contract used to validate a pipeline without exposing an untyped JSON editor.
    internal enum JobCustomisation { ItemFilter, Target, Source, Destination, StockLimit, Movement, Reservation }
    /// <summary>
    ///     The guided pieces a pipeline is built from. Deliberately linear: no player-authored
    ///     loops, branches, variables, or async work. Queue semantics remain the only retry and
    ///     scheduling mechanism. Ordering constraints live in <see cref="PipelineShapeRules"/>.
    /// </summary>
    internal enum JobPieceKind { Start, StopAtStockLimit, FindLooseItem, SelectSource, SelectTarget, MoveToTarget, PickUp, TakeItem, PutItem, OperateStation, End }

    /// <summary>
    ///     One step in a pipeline. <see cref="Capability"/> narrows which registered
    ///     structures a selection piece may resolve to; it is unused by other kinds.
    /// </summary>
    internal sealed class JobPiece
    {
        internal JobPieceKind Kind;
        internal StructureCapability Capability;
        internal JobPiece Clone() => new JobPiece { Kind = Kind, Capability = Capability };
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
