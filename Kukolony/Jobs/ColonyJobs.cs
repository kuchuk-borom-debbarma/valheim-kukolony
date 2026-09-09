using System;
using System.Collections.Generic;
using Kukolony.Colonies;

namespace Kukolony.Jobs
{
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

    internal enum TargetMode { All, Selected, Ignore }
    internal enum JobResult { Running, Completed, Failed, Skipped }

    // "Customisation" is the player-facing term.  These typed values are the
    // contract used to validate a pipeline without exposing an untyped JSON editor.
    internal enum JobCustomisation { ItemFilter, Target, Source, Destination, StockLimit, Movement, Reservation }
    internal enum JobPieceKind { Start, StopAtStockLimit, FindLooseItem, SelectSource, SelectTarget, MoveToTarget, PickUp, TakeItem, PutItem, OperateStation, End }

    internal sealed class JobPiece
    {
        internal JobPieceKind Kind;
        internal StructureCapability Capability;
        internal JobPiece Clone() => new JobPiece { Kind = Kind, Capability = Capability };
    }

    internal static class JobPipeline
    {
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

        internal static bool IsValid(ColonyJobConfig job, out string message)
        {
            List<JobPiece> pieces = job.Pieces;
            if (pieces.Count < 2 || pieces[0].Kind != JobPieceKind.Start || pieces[pieces.Count - 1].Kind != JobPieceKind.End) { message = "A job must start with Start and end with End."; return false; }
            for (int i = 1; i < pieces.Count; i++)
                if (pieces[i].Kind == JobPieceKind.PickUp && !HasBefore(pieces, i, JobPieceKind.FindLooseItem) ||
                    pieces[i].Kind == JobPieceKind.TakeItem && !HasBefore(pieces, i, JobPieceKind.SelectSource) ||
                    pieces[i].Kind == JobPieceKind.PutItem && !HasBefore(pieces, i, JobPieceKind.SelectTarget)) { message = "This piece is missing compatible customisation from an earlier selection piece."; return false; }
            message = string.Empty; return true;
        }
        private static bool HasBefore(List<JobPiece> pieces, int index, JobPieceKind kind) { for (int i = 0; i < index; i++) if (pieces[i].Kind == kind) return true; return false; }
    }

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

    internal sealed class JobPreset
    {
        internal string Name = string.Empty;
        internal bool ColonyLocal;
        internal ColonyJobConfig Settings = new ColonyJobConfig();
    }

    internal static class ColonyJobCatalog
    {
        internal static readonly ColonyJobType[] All =
            (ColonyJobType[])Enum.GetValues(typeof(ColonyJobType));

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
