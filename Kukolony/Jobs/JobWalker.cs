using System.Collections.Generic;

namespace Kukolony.Jobs
{
    /// <summary>What the engine should do for the piece a villager has stopped on.</summary>
    internal enum StepAction
    {
        FindLooseItem,
        SelectSource,
        SelectTarget,
        Move,
        PickUp,
        TakeItem,
        PutItem,
        OperateStation,
        /// <summary>Waiting for work to produce something to collect.</summary>
        Wait,
        /// <summary>Destination already holds enough. Yields without consuming an attempt.</summary>
        StopAtLimit,
        /// <summary>Reached the end of the pipeline; the cycle is done.</summary>
        CompleteCycle,
        /// <summary>
        ///     The world no longer matches this piece — usually a target destroyed or
        ///     unloaded mid-cycle. Yields and begins the pipeline again from the start.
        /// </summary>
        Restart,
        /// <summary>The pipeline cannot be executed. Yields rather than failing the job.</summary>
        Invalid
    }

    /// <summary>
    ///     The observable facts a step decision depends on, reduced to primitives so the
    ///     decision can be tested without a world.
    /// </summary>
    internal readonly struct JobFacts
    {
        internal JobFacts(bool hasTarget, bool arrivedAtTarget, bool carrying, bool stockLimitReached)
        {
            HasTarget = hasTarget;
            ArrivedAtTarget = arrivedAtTarget;
            Carrying = carrying;
            StockLimitReached = stockLimitReached;
        }

        /// <summary>A target has been chosen and still resolves.</summary>
        internal bool HasTarget { get; }

        /// <summary>The villager is already standing within stop distance of that target.</summary>
        internal bool ArrivedAtTarget { get; }

        /// <summary>The bag holds something this job cares about.</summary>
        internal bool Carrying { get; }

        /// <summary>The destination already holds at least the configured stock limit.</summary>
        internal bool StockLimitReached { get; }
    }

    /// <summary>An action to perform, and the piece it belongs to.</summary>
    internal readonly struct JobStep
    {
        internal JobStep(StepAction action, int cursor)
        {
            Action = action;
            Cursor = cursor;
        }

        internal StepAction Action { get; }

        /// <summary>
        ///     Index of the piece this action came from. The engine persists this while the
        ///     action is still running, and this plus one once it succeeds, so a finished step
        ///     is not offered again. On Restart the engine persists zero.
        /// </summary>
        internal int Cursor { get; }
    }

    /// <summary>
    ///     Decides which step of a pipeline a villager should perform next.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This is the entire sequencing rule for a job, and it is deliberately pure: an
    ///         enum list, an integer, and four booleans. No Unity, no ZDO, no colony types.
    ///         That is what lets multi-step behaviour be verified in the Unity-free project in
    ///         about a second instead of only inside a four-minute in-game run.
    ///     </para>
    ///     <para>
    ///         <b>Facts decide, the cursor only hints.</b> The walker scans forward from the
    ///         cursor and skips any piece whose outcome already holds — a villager that is
    ///         carrying skips the pieces that would fetch something, and one that is not
    ///         carrying skips the piece that would deposit. So a cursor of zero is always
    ///         safe: whatever went wrong, the scan re-derives the right place from the world.
    ///     </para>
    ///     <para>
    ///         That property is not a nicety. The engine this replaces re-infers a villager's
    ///         intent every tick from whether its bag is full, which is what silently repairs
    ///         a reload halfway through a haul. A cursor that were trusted blindly would send
    ///         a villager carrying wood off to find more wood.
    ///     </para>
    /// </remarks>
    internal static class JobWalker
    {
        internal static JobStep Next(IList<JobPieceKind> pieces, int cursor, JobFacts facts)
        {
            // A saved job can carry an arbitrary piece list, so refuse a malformed one rather
            // than guessing. The caller yields on Invalid; it never fails the job outright.
            if (!PipelineShapeRules.IsValid(pieces, out _)) return new JobStep(StepAction.Invalid, 0);

            int index = cursor < 0 || cursor >= pieces.Count ? 0 : cursor;
            for (int scanned = 0; scanned < pieces.Count; scanned++)
            {
                switch (pieces[index])
                {
                    case JobPieceKind.Start:
                        break;

                    case JobPieceKind.StopAtStockLimit:
                        if (facts.StockLimitReached) return new JobStep(StepAction.StopAtLimit, index);
                        break;

                    // Fetch pieces are skipped once the bag holds something, so a villager
                    // that reloads mid-cycle carries on to the deposit rather than going off
                    // to collect a second load. A selection already made is never remade:
                    // choosing again would strand whatever it walked to or claimed.
                    case JobPieceKind.FindLooseItem:
                        if (!facts.Carrying && !facts.HasTarget) return new JobStep(StepAction.FindLooseItem, index);
                        break;
                    case JobPieceKind.SelectSource:
                        if (!facts.Carrying && !facts.HasTarget) return new JobStep(StepAction.SelectSource, index);
                        break;
                    case JobPieceKind.SelectTarget:
                        if (!facts.HasTarget) return new JobStep(StepAction.SelectTarget, index);
                        break;

                    // No target means the fetch before this was skipped, so there is nothing
                    // to walk to yet; fall through to the piece that picks the next one.
                    case JobPieceKind.MoveToTarget:
                        if (facts.HasTarget && !facts.ArrivedAtTarget) return new JobStep(StepAction.Move, index);
                        break;

                    // Acting on nothing is a stale cursor, not a decision: begin again and
                    // let the fetch pieces choose a fresh target.
                    case JobPieceKind.PickUp:
                        if (facts.Carrying) break;
                        return new JobStep(facts.HasTarget ? StepAction.PickUp : StepAction.Restart, index);
                    case JobPieceKind.TakeItem:
                        if (facts.Carrying) break;
                        return new JobStep(facts.HasTarget ? StepAction.TakeItem : StepAction.Restart, index);

                    // The mirror of the above: an empty bag means the deposit already
                    // happened (or there was never anything to deposit), so fall through to
                    // End and complete the cycle rather than stalling here.
                    case JobPieceKind.PutItem:
                        if (!facts.Carrying) break;
                        return new JobStep(facts.HasTarget ? StepAction.PutItem : StepAction.Restart, index);

                    // Whether the thing being waited for has appeared is a question about the
                    // world, so the engine answers it and advances the cursor when it has.
                    case JobPieceKind.WaitForDrop:
                        return new JobStep(StepAction.Wait, index);

                    // Only the station knows whether it wants fuel, input, or emptying.
                    case JobPieceKind.OperateStation:
                        return new JobStep(facts.HasTarget ? StepAction.OperateStation : StepAction.Restart, index);

                    case JobPieceKind.End:
                        return new JobStep(StepAction.CompleteCycle, index);

                    default:
                        return new JobStep(StepAction.Invalid, index);
                }
                index = index + 1 >= pieces.Count ? 0 : index + 1;
            }

            // Every piece was skipped without reaching End, which shape validation should
            // have prevented. Yield rather than spin.
            return new JobStep(StepAction.Invalid, 0);
        }

        /// <summary>
        ///     Nearest piece of a kind at or before the cursor, or -1. Lets a station that
        ///     reports it needs input rewind to the piece that fetches it, instead of encoding
        ///     that intent in a side-channel counter.
        /// </summary>
        internal static int IndexOfPreceding(IList<JobPieceKind> pieces, int cursor, JobPieceKind kind)
        {
            if (pieces == null) return -1;
            int start = cursor >= pieces.Count ? pieces.Count - 1 : cursor;
            for (int i = start; i >= 0; i--) if (pieces[i] == kind) return i;
            return -1;
        }
    }
}
