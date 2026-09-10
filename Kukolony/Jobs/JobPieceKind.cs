namespace Kukolony.Jobs
{
    /// <summary>
    ///     The guided pieces a pipeline is built from. Deliberately linear: no player-authored
    ///     loops, branches, variables, or async work. Queue semantics remain the only retry and
    ///     scheduling mechanism. Ordering constraints live in <see cref="PipelineShapeRules"/>.
    /// </summary>
    /// <remarks>
    ///     These ordinals are the persisted wire format, written and read as plain ints by
    ///     the colony job record. Append new kinds at the end and never reorder or remove one,
    ///     or every saved pipeline decodes as something else. The values are written out
    ///     explicitly so that is hard to do by accident.
    ///
    ///     This enum lives alone, free of Unity and colony types, so the deterministic test
    ///     project can link the real file instead of keeping a hand-written copy in step.
    /// </remarks>
    internal enum JobPieceKind
    {
        Start = 0,
        StopAtStockLimit = 1,
        FindLooseItem = 2,
        SelectSource = 3,
        SelectTarget = 4,
        MoveToTarget = 5,
        PickUp = 6,
        TakeItem = 7,
        PutItem = 8,
        OperateStation = 9,
        End = 10,
        /// <summary>Waits for work to produce a world drop, such as honey from a hive.</summary>
        WaitForDrop = 11
    }
}
