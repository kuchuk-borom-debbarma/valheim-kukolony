using System.Collections.Generic;

namespace Kukolony.Jobs
{
    /// <summary>
    ///     The settings a job piece can carry. "Customisation" is the player-facing word.
    /// </summary>
    /// <remarks>
    ///     Two separate questions are asked with these flags, and keeping them apart is what
    ///     makes a piece well defined rather than a bag of fields:
    ///
    ///     what a piece <b>uses</b> - which settings it reads, so the editor shows those and
    ///     nothing else, and a reader can tell at a glance what configuring it means;
    ///
    ///     what a piece <b>provides</b> and <b>requires</b> - the handover between steps. A
    ///     step that takes from a source needs some earlier step to have chosen one.
    /// </remarks>
    [System.Flags]
    internal enum JobCustomisation
    {
        None = 0,
        /// <summary>Which items this step works with.</summary>
        ItemFilter = 1,
        /// <summary>A specific container, overriding the job's source or destination.</summary>
        Container = 2,
        /// <summary>Stock threshold this step compares against.</summary>
        StockLimit = 4,
        /// <summary>How many items to move in one visit.</summary>
        Amount = 8,
        /// <summary>How far to search for something.</summary>
        SearchRadius = 16,
        /// <summary>How close to stand before acting.</summary>
        StopDistance = 32,
        /// <summary>Which registered structures are eligible.</summary>
        TargetScope = 64,
        /// <summary>Whether this step reserves what it picks, so two villagers do not collide.</summary>
        Reservation = 128,
        /// <summary>A chosen thing to walk to or act on.</summary>
        Target = 256,
        /// <summary>Something in the bag worth depositing.</summary>
        CarriedItem = 512
    }

    /// <summary>
    ///     What each piece kind is configured by, and how pieces hand work to one another.
    /// </summary>
    /// <remarks>
    ///     Pure by design - enums and collections only - so the deterministic project links
    ///     this file and the whole contract is verified in about a second. It is also the one
    ///     place that answers "what can I set on this piece?", which the editor reads rather
    ///     than hardcoding its own list.
    /// </remarks>
    internal static class PieceCustomisation
    {
        /// <summary>Settings this kind reads. Anything else on the piece is ignored.</summary>
        internal static JobCustomisation Uses(JobPieceKind kind)
        {
            switch (kind)
            {
                case JobPieceKind.StopAtStockLimit:
                    return JobCustomisation.ItemFilter | JobCustomisation.StockLimit | JobCustomisation.Container;
                case JobPieceKind.FindLooseItem:
                    return JobCustomisation.ItemFilter | JobCustomisation.SearchRadius | JobCustomisation.Reservation;
                case JobPieceKind.SelectSource:
                case JobPieceKind.SelectTarget:
                    return JobCustomisation.ItemFilter | JobCustomisation.Container |
                           JobCustomisation.TargetScope | JobCustomisation.Reservation;
                case JobPieceKind.MoveToTarget:
                    return JobCustomisation.StopDistance;
                case JobPieceKind.PickUp:
                    return JobCustomisation.ItemFilter;
                case JobPieceKind.TakeItem:
                case JobPieceKind.PutItem:
                    return JobCustomisation.ItemFilter | JobCustomisation.Amount;
                case JobPieceKind.OperateStation:
                    return JobCustomisation.ItemFilter;
                case JobPieceKind.WaitForDrop:
                    return JobCustomisation.ItemFilter | JobCustomisation.SearchRadius;
                default:
                    return JobCustomisation.None;
            }
        }

        /// <summary>What this kind makes available to the steps after it.</summary>
        internal static JobCustomisation Provides(JobPieceKind kind)
        {
            switch (kind)
            {
                case JobPieceKind.FindLooseItem:
                case JobPieceKind.SelectSource:
                case JobPieceKind.SelectTarget:
                    return JobCustomisation.Target;
                case JobPieceKind.PickUp:
                case JobPieceKind.TakeItem:
                    return JobCustomisation.CarriedItem;
                default:
                    return JobCustomisation.None;
            }
        }

        /// <summary>What must already be available before this kind can run.</summary>
        internal static JobCustomisation Requires(JobPieceKind kind)
        {
            switch (kind)
            {
                case JobPieceKind.MoveToTarget:
                case JobPieceKind.PickUp:
                case JobPieceKind.TakeItem:
                case JobPieceKind.OperateStation:
                    return JobCustomisation.Target;
                case JobPieceKind.PutItem:
                    return JobCustomisation.Target | JobCustomisation.CarriedItem;
                default:
                    return JobCustomisation.None;
            }
        }

        /// <summary>Every setting name a kind uses, for display and for the editor.</summary>
        internal static List<JobCustomisation> Settings(JobPieceKind kind)
        {
            List<JobCustomisation> result = new List<JobCustomisation>();
            JobCustomisation uses = Uses(kind);
            foreach (JobCustomisation candidate in All)
                if ((uses & candidate) != 0) result.Add(candidate);
            return result;
        }

        private static readonly JobCustomisation[] All =
        {
            JobCustomisation.ItemFilter, JobCustomisation.Container, JobCustomisation.StockLimit,
            JobCustomisation.Amount, JobCustomisation.SearchRadius, JobCustomisation.StopDistance,
            JobCustomisation.TargetScope, JobCustomisation.Reservation
        };
    }
}
