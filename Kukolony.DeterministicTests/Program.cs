using Kukolony.Jobs;
using static Kukolony.Jobs.JobPieceKind;

static class Program
{
    static readonly JobPieceKind[] Haul =
        [Start, StopAtStockLimit, FindLooseItem, MoveToTarget, PickUp, SelectTarget, MoveToTarget, PutItem, End];
    static readonly JobPieceKind[] Transfer =
        [Start, StopAtStockLimit, SelectSource, MoveToTarget, TakeItem, SelectTarget, MoveToTarget, PutItem, End];
    static readonly JobPieceKind[] Station =
        [Start, StopAtStockLimit, SelectSource, MoveToTarget, TakeItem, SelectTarget, MoveToTarget, OperateStation, End];

    static int Main()
    {
        int failures = RunShapeCases() + RunStepCases() + RunCycleCases() + RunContractCases();
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
            ("put without destination", [Start, PutItem, End], false)
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
            ("malformed pipeline is refused", [Start, PickUp, End], 0, Facts(), StepAction.Invalid)
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
                case StepAction.OperateStation: carrying = false; hasTarget = false; arrived = false; break;
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

    static bool Has(JobCustomisation set, JobCustomisation flag) => (set & flag) != 0;

    static JobFacts Facts(bool hasTarget = false, bool arrived = false, bool carrying = false,
        bool stockLimitReached = false) => new(hasTarget, arrived, carrying, stockLimitReached);

    static void Report(bool ok, string name, string detail) =>
        Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}{(detail.Length == 0 ? "" : ": " + detail)}");
}
