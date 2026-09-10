using Kukolony.Jobs;
using Kukolony.Jobs.Work;
using static Kukolony.Jobs.JobPieceKind;

static class Program
{
    static readonly JobPieceKind[] Haul =
        [Start, StopAtStockLimit, FindLooseItem, MoveToTarget, PickUp, SelectTarget, MoveToTarget, PutItem, End];
    static readonly JobPieceKind[] Transfer =
        [Start, StopAtStockLimit, SelectSource, MoveToTarget, TakeItem, SelectTarget, MoveToTarget, PutItem, End];
    static readonly JobPieceKind[] Collect =
        [Start, StopAtStockLimit, SelectTarget, MoveToTarget, OperateStation, WaitForDrop,
         FindLooseItem, MoveToTarget, PickUp, SelectTarget, MoveToTarget, PutItem, End];
    // Gather loose items into a pile, guarding against walking a pipeline empty-handed.
    static readonly JobPieceKind[] Gather =
        [Start, FindLooseItem, MoveToTarget, PickUp, StopUnlessCarrying, DropCarried, End];
    static readonly JobPieceKind[] Station =
        [Start, StopAtStockLimit, SelectSource, MoveToTarget, TakeItem, SelectTarget, MoveToTarget, OperateStation, End];

    static int Main()
    {
        int failures = RunShapeCases() + RunStepCases() + RunCycleCases() + RunContractCases()
                       + RunWorkCases() + RunTapCases();
        Console.WriteLine(failures == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({failures})");
        return failures == 0 ? 0 : 1;
    }

    static int RunShapeCases()
    {
        var cases = new (string Name, JobPieceKind[] Pieces, bool Expected)[]
        {
            ("haul", [Start, FindLooseItem, MoveToTarget, PickUp, SelectTarget, PutItem, End], true),
            ("transfer", [Start, SelectSource, TakeItem, SelectTarget, PutItem, End], true),
            ("missing start", [FindLooseItem, End], false),
            ("pickup without target", [Start, PickUp, End], false),
            ("take without source", [Start, TakeItem, End], false),
            ("put without destination", [Start, PutItem, End], false),
            ("dropping needs something carried", [Start, DropCarried, End], false),
            ("a guard satisfies what follows it", [Start, StopUnlessCarrying, DropCarried, End], true)
        };
        int failures = 0;
        foreach (var test in cases)
        {
            bool actual = PipelineShapeRules.IsValid(test.Pieces, out var reason);
            Report(actual == test.Expected, test.Name, reason);
            if (actual != test.Expected) failures++;
        }
        return failures;
    }

    // Single decisions, mostly re-entry: a villager resumes mid-pipeline after a reload, an
    // ownership change, or having its target destroyed under it.
    static int RunStepCases()
    {
        var cases = new (string Name, JobPieceKind[] Pieces, int Cursor, JobFacts Facts, StepAction Expected)[]
        {
            ("stock limit yields", Haul, 0, Facts(stockLimitReached: true), StepAction.StopAtLimit),
            ("fresh haul finds an item", Haul, 0, Facts(), StepAction.FindLooseItem),
            ("reload while carrying skips the fetch", Haul, 0, Facts(carrying: true), StepAction.SelectTarget),
            ("reload while carrying with a target walks", Haul, 0, Facts(carrying: true, hasTarget: true), StepAction.Move),
            ("cursor past the end still works", Haul, 99, Facts(), StepAction.FindLooseItem),
            ("negative cursor still works", Haul, -3, Facts(), StepAction.FindLooseItem),
            ("lost target restarts the cycle", Haul, 4, Facts(), StepAction.Restart),
            ("empty bag at the deposit completes", Haul, 7, Facts(), StepAction.CompleteCycle),
            ("station always asks the station", Station, 7, Facts(carrying: true, hasTarget: true, arrived: true), StepAction.OperateStation),
            ("malformed pipeline is refused", [Start, PickUp, End], 0, Facts(), StepAction.Invalid),
            ("a guard stops an empty-handed villager", Gather, 4, Facts(), StepAction.StopHere),
            ("the same guard passes when carrying", Gather, 4, Facts(carrying: true), StepAction.DropCarried),
            ("picking a roomy target needs something in hand",
                [Start, SelectSpaciousTarget, MoveToTarget, PutItem, End], 0, Facts(), StepAction.Invalid)
        };
        int failures = 0;
        foreach (var test in cases)
        {
            JobStep actual = JobWalker.Next(test.Pieces, test.Cursor, test.Facts);
            bool ok = actual.Action == test.Expected;
            Report(ok, "step: " + test.Name, ok ? "" : $"expected {test.Expected}, got {actual.Action}");
            if (!ok) failures++;
        }
        return failures;
    }

    // Whole cycles driven against a fake world. Sequencing was previously verified by
    // nothing: shape was checked here, leaf executors were checked in-game, and the ordering
    // between them by neither.
    static int RunCycleCases()
    {
        var cases = new (string Name, JobPieceKind[] Pieces, bool StartCarrying, StepAction[] Expected)[]
        {
            ("haul", Haul, false, [StepAction.FindLooseItem, StepAction.Move, StepAction.PickUp,
                StepAction.SelectTarget, StepAction.Move, StepAction.PutItem, StepAction.CompleteCycle]),
            ("transfer", Transfer, false, [StepAction.SelectSource, StepAction.Move, StepAction.TakeItem,
                StepAction.SelectTarget, StepAction.Move, StepAction.PutItem, StepAction.CompleteCycle]),
            ("station", Station, false, [StepAction.SelectSource, StepAction.Move, StepAction.TakeItem,
                StepAction.SelectTarget, StepAction.Move, StepAction.OperateStation, StepAction.CompleteCycle]),
            // Collecting is the only flow whose work lands on the ground, so it taps, waits,
            // then picks up and stores what appeared.
            ("collect", Collect, false, [StepAction.SelectTarget, StepAction.Move, StepAction.OperateStation,
                StepAction.Wait, StepAction.FindLooseItem, StepAction.Move, StepAction.PickUp,
                StepAction.SelectTarget, StepAction.Move, StepAction.PutItem, StepAction.CompleteCycle]),
            ("gather to a pile", Gather, false, [StepAction.FindLooseItem, StepAction.Move,
                StepAction.PickUp, StepAction.DropCarried, StepAction.CompleteCycle]),
            // Resuming a haul mid-cycle must finish the delivery, never start a second one.
            ("haul resumed carrying", Haul, true,
                [StepAction.SelectTarget, StepAction.Move, StepAction.PutItem, StepAction.CompleteCycle])
        };
        int failures = 0;
        foreach (var test in cases)
        {
            var actual = Drive(test.Pieces, test.StartCarrying);
            bool ok = actual.Count == test.Expected.Length;
            for (int i = 0; ok && i < actual.Count; i++) ok = actual[i] == test.Expected[i];
            Report(ok, "cycle: " + test.Name, ok ? "" : string.Join(" -> ", actual));
            if (!ok) failures++;
        }
        return failures;
    }

    /// <summary>Runs a pipeline to completion against an in-memory world.</summary>
    static List<StepAction> Drive(JobPieceKind[] pieces, bool carrying)
    {
        bool hasTarget = false, arrived = false;
        var performed = new List<StepAction>();
        int cursor = 0;
        for (int tick = 0; tick < 64; tick++)
        {
            JobStep step = JobWalker.Next(pieces, cursor, new JobFacts(hasTarget, arrived, carrying, false));
            performed.Add(step.Action);
            // The engine advances past a step that succeeded, and holds position while one
            // is still running. Restart drops back to the beginning.
            cursor = step.Cursor + 1;
            switch (step.Action)
            {
                case StepAction.FindLooseItem:
                case StepAction.SelectSource:
                case StepAction.SelectTarget: hasTarget = true; arrived = false; break;
                case StepAction.Move: arrived = true; break;
                case StepAction.PickUp:
                case StepAction.TakeItem: carrying = true; hasTarget = false; arrived = false; break;
                case StepAction.PutItem: carrying = false; hasTarget = false; arrived = false; break;
                case StepAction.DropCarried: carrying = false; hasTarget = false; arrived = false; break;
                case StepAction.SelectSpaciousTarget: hasTarget = true; arrived = false; break;
                case StepAction.OperateStation: carrying = false; hasTarget = false; arrived = false; break;
                case StepAction.Wait: break;   // the engine decides when the wait is over
                default: return performed;   // StopAtLimit, CompleteCycle, Invalid all end the cycle
            }
        }
        return performed;
    }

    // The customisation contract is what makes a piece well defined: what it reads, what it
    // hands on, and what it needs to have been handed. Validation is derived from it, so an
    // error here shows up as a pipeline that wrongly passes or fails rather than as a crash.
    static int RunContractCases()
    {
        var cases = new (string Name, bool Actual, bool Expected)[]
        {
            ("a fetch provides a target",
                Has(PieceCustomisation.Provides(FindLooseItem), JobCustomisation.Target), true),
            ("a move needs a target",
                Has(PieceCustomisation.Requires(MoveToTarget), JobCustomisation.Target), true),
            ("a deposit needs something carried",
                Has(PieceCustomisation.Requires(PutItem), JobCustomisation.CarriedItem), true),
            ("putting down needs something carried",
                Has(PieceCustomisation.Requires(DropCarried), JobCustomisation.CarriedItem), true),
            ("a roomy target provides a target",
                Has(PieceCustomisation.Provides(SelectSpaciousTarget), JobCustomisation.Target), true),
            ("a guard is configured by nothing",
                PieceCustomisation.Uses(StopUnlessCarrying) == JobCustomisation.None, true),
            // A guard establishes what it checks, so a pipeline handed a full bag is
            // expressible without pretending some earlier step fetched the contents.
            ("a guard provides what it guarantees",
                Has(PieceCustomisation.Provides(StopUnlessCarrying), JobCustomisation.CarriedItem), true),
            ("taking provides something carried",
                Has(PieceCustomisation.Provides(TakeItem), JobCustomisation.CarriedItem), true),
            ("a source selection is configured by its container",
                Has(PieceCustomisation.Uses(SelectSource), JobCustomisation.Container), true),
            ("a loose-item search is configured by its radius",
                Has(PieceCustomisation.Uses(FindLooseItem), JobCustomisation.SearchRadius), true),
            ("a move is configured by its stop distance",
                Has(PieceCustomisation.Uses(MoveToTarget), JobCustomisation.StopDistance), true),
            ("a limit is configured by its threshold",
                Has(PieceCustomisation.Uses(StopAtStockLimit), JobCustomisation.StockLimit), true),
            ("structural pieces are configured by nothing",
                PieceCustomisation.Uses(Start) == JobCustomisation.None &&
                PieceCustomisation.Uses(End) == JobCustomisation.None, true),
            // Control: a move is not configured by a stock limit. Without this the checks
            // above would pass just as well if Uses returned every flag for every kind.
            ("control: a move is not configured by a stock limit",
                Has(PieceCustomisation.Uses(MoveToTarget), JobCustomisation.StockLimit), false),
            ("control: a fetch does not itself require a target",
                Has(PieceCustomisation.Requires(FindLooseItem), JobCustomisation.Target), false)
        };
        int failures = 0;
        foreach (var test in cases)
        {
            bool ok = test.Actual == test.Expected;
            Report(ok, "contract: " + test.Name, ok ? "" : $"expected {test.Expected}");
            if (!ok) failures++;
        }
        return failures;
    }

    // The focused jobs sequence themselves rather than walking a piece list. Same property
    // being protected as the cycle cases above — that a whole work cycle runs in the right
    // order, and that an interrupted one resumes rather than restarting.
    static int RunWorkCases()
    {
        var cases = new (string Name, WorkState From, WorkFacts Facts, WorkAction[] Expected)[]
        {
            ("full cycle", WorkState.Choosing, Observed(), [WorkAction.ChooseSource, WorkAction.Move,
                WorkAction.Collect, WorkAction.ChooseTarget, WorkAction.Move, WorkAction.Deliver,
                WorkAction.Complete]),

            // The property the piece walker had and this must keep: a villager that reloads
            // holding something delivers it. Fetching a second load would strand the first.
            ("resumed carrying", WorkState.Choosing, Observed(carrying: true),
                [WorkAction.ChooseTarget, WorkAction.Move, WorkAction.Deliver, WorkAction.Complete]),

            // Saved mid-delivery, and the target survived: walk on and deliver.
            ("resumed delivering", WorkState.Delivering, Observed(carrying: true, hasTarget: true),
                [WorkAction.Move, WorkAction.Deliver, WorkAction.Complete]),

            // Saved mid-delivery and the destination is gone. The state says deliver, the
            // world says there is nowhere to deliver to, and the world wins.
            ("lost destination is chosen again", WorkState.Delivering, Observed(carrying: true),
                [WorkAction.ChooseTarget, WorkAction.Move, WorkAction.Deliver, WorkAction.Complete]),

            // Saved about to collect, but somebody else took it first.
            ("lost source restarts the fetch", WorkState.Collecting, Observed(),
                [WorkAction.ChooseSource, WorkAction.Move, WorkAction.Collect, WorkAction.ChooseTarget,
                 WorkAction.Move, WorkAction.Deliver, WorkAction.Complete]),

            // Standing on the target already, which is what a second cycle in the same spot
            // looks like: no walk, straight to the work.
            ("already arrived does not walk", WorkState.Travelling, Observed(hasTarget: true, arrived: true),
                [WorkAction.Collect, WorkAction.ChooseTarget, WorkAction.Move, WorkAction.Deliver,
                 WorkAction.Complete]),

            ("stock limit yields", WorkState.Choosing, Observed(stockLimitReached: true), [WorkAction.Yield]),
            ("a missing tool yields", WorkState.Choosing, Observed(hasTool: false), [WorkAction.Yield]),

            // Control: the limit only guards the start of a cycle. A villager already
            // carrying finishes its delivery rather than dropping the load where it stands.
            ("control: the limit does not strand a carried load", WorkState.Delivering,
                Observed(carrying: true, hasTarget: true, arrived: true, stockLimitReached: true),
                [WorkAction.Deliver, WorkAction.Complete]),

            // Control: nothing was worth picking up, so the cycle ends instead of walking on
            // to a destination empty-handed.
            ("control: an empty fetch ends the cycle", WorkState.ChoosingTarget, Observed(),
                [WorkAction.Complete])
        };
        int failures = 0;
        foreach (var test in cases)
        {
            var actual = DriveWork(test.From, test.Facts);
            bool ok = actual.Count == test.Expected.Length;
            for (int i = 0; ok && i < actual.Count; i++) ok = actual[i] == test.Expected[i];
            Report(ok, "work: " + test.Name, ok ? "" : string.Join(" -> ", actual));
            if (!ok) failures++;
        }
        return failures;
    }

    // Tapping a hive is two journeys in one cycle, and the failure it is guarding against is
    // the second one turning back into the first: a villager that taps, waits, then taps
    // again instead of picking up what fell out.
    static int RunTapCases()
    {
        var cases = new (string Name, WorkState From, WorkFacts Facts, WorkAction[] Expected)[]
        {
            ("full cycle", WorkState.Choosing, Observed(), [WorkAction.ChooseSource, WorkAction.Move,
                WorkAction.Collect, WorkAction.Wait, WorkAction.ChooseSource, WorkAction.Move,
                WorkAction.Collect, WorkAction.ChooseTarget, WorkAction.Move, WorkAction.Deliver,
                WorkAction.Complete]),

            // Reloaded between the tap and the honey landing. It must go back to picking up
            // what appeared, not to tapping the hive a second time.
            ("resumed waiting", WorkState.Waiting, Observed(),
                [WorkAction.Wait, WorkAction.ChooseSource, WorkAction.Move, WorkAction.Collect,
                 WorkAction.ChooseTarget, WorkAction.Move, WorkAction.Deliver, WorkAction.Complete]),

            // What fell out was taken by somebody else while the villager walked over.
            ("lost produce is chosen again", WorkState.Retrieving, Observed(),
                [WorkAction.ChooseSource, WorkAction.Move, WorkAction.Collect, WorkAction.ChooseTarget,
                 WorkAction.Move, WorkAction.Deliver, WorkAction.Complete]),

            // Already holding honey, so the hive is done with. Deliver and stop.
            ("resumed carrying skips the hive", WorkState.Choosing, Observed(carrying: true),
                [WorkAction.ChooseTarget, WorkAction.Move, WorkAction.Deliver, WorkAction.Complete]),

            ("stock limit yields", WorkState.Choosing, Observed(stockLimitReached: true), [WorkAction.Yield]),

            // Control: the tap phase must not be reachable from the gather phase. Standing at
            // the hive in Gathering means the honey is what to walk to, not the hive again.
            ("control: gathering never taps again", WorkState.Gathering, Observed(hasTarget: true, arrived: true),
                [WorkAction.Collect, WorkAction.ChooseTarget, WorkAction.Move, WorkAction.Deliver,
                 WorkAction.Complete])
        };
        int failures = 0;
        foreach (var test in cases)
        {
            // Tapping a hive collects nothing: the honey lands on the ground and is picked
            // up later, from Retrieving. Modelling that is the point of these cases.
            var actual = DriveWork(test.From, test.Facts, TapThenGather.Next,
                from => from == WorkState.Retrieving);
            bool ok = actual.Count == test.Expected.Length;
            for (int i = 0; ok && i < actual.Count; i++) ok = actual[i] == test.Expected[i];
            Report(ok, "tap: " + test.Name, ok ? "" : string.Join(" -> ", actual));
            if (!ok) failures++;
        }
        return failures;
    }

    /// <summary>
    ///     Runs a job to the end of one cycle against an in-memory world, mirroring what the
    ///     engine does to the facts after each action succeeds.
    /// </summary>
    static List<WorkAction> DriveWork(WorkState state, WorkFacts start) =>
        DriveWork(state, start, FetchAndDeliver.Next, _ => true);

    /// <param name="collectYields">
    ///     Whether collecting from this state leaves the villager carrying something. True for
    ///     ordinary work; false where the action starts something off instead, as tapping a
    ///     hive does.
    /// </param>
    static List<WorkAction> DriveWork(WorkState state, WorkFacts start,
        Func<WorkState, WorkFacts, WorkStep> next, Func<WorkState, bool> collectYields)
    {
        bool hasTarget = start.HasTarget, arrived = start.Arrived, carrying = start.Carrying;
        var performed = new List<WorkAction>();
        for (int tick = 0; tick < 32; tick++)
        {
            WorkStep step = next(state,
                new WorkFacts(hasTarget, arrived, carrying, start.StockLimitReached, start.HasTool));
            performed.Add(step.Action);
            state = step.Next;
            switch (step.Action)
            {
                case WorkAction.ChooseSource:
                case WorkAction.ChooseTarget: hasTarget = true; arrived = false; break;
                case WorkAction.Move: arrived = true; break;
                // The executors release the target as they finish with it, so the next
                // selection is free to choose somewhere else.
                case WorkAction.Collect:
                    carrying = collectYields(step.From); hasTarget = false; arrived = false; break;
                case WorkAction.Deliver: carrying = false; hasTarget = false; arrived = false; break;
                // The engine holds a villager here until something appears; the wait ending
                // is what the transitions have to handle, so that is what is modelled.
                case WorkAction.Wait: hasTarget = false; arrived = false; break;
                default: return performed;   // Yield and Complete both end the cycle
            }
        }
        return performed;
    }

    static WorkFacts Observed(bool hasTarget = false, bool arrived = false, bool carrying = false,
        bool stockLimitReached = false, bool hasTool = true) =>
        new(hasTarget, arrived, carrying, stockLimitReached, hasTool);

    static bool Has(JobCustomisation set, JobCustomisation flag) => (set & flag) != 0;

    static JobFacts Facts(bool hasTarget = false, bool arrived = false, bool carrying = false,
        bool stockLimitReached = false) => new(hasTarget, arrived, carrying, stockLimitReached);

    static void Report(bool ok, string name, string detail) =>
        Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}{(detail.Length == 0 ? "" : ": " + detail)}");
}
