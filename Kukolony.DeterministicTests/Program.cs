using Kukolony.Jobs;
using Kukolony.Jobs.Work;

static class Program
{
    static int Main()
    {
        int failures = RunWorkCases() + RunTapCases();
        Console.WriteLine(failures == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({failures})");
        return failures == 0 ? 0 : 1;
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

            // A job that piles its load has nowhere to deliver to and nothing to walk to, so
            // it must put it down where it stands rather than go looking for a chest.
            ("a pile needs no destination", WorkState.Choosing, Observed(carrying: true, deliversInPlace: true),
                [WorkAction.Deliver, WorkAction.Complete]),

            // Control: the same job without that setting goes and finds a container. Without
            // it the case above would pass just as well if delivery were skipped entirely.
            ("control: without it the load still goes in a container", WorkState.Choosing,
                Observed(carrying: true),
                [WorkAction.ChooseTarget, WorkAction.Move, WorkAction.Deliver, WorkAction.Complete]),

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
                new WorkFacts(hasTarget, arrived, carrying, start.StockLimitReached, start.HasTool,
                    start.DeliversInPlace));
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
        bool stockLimitReached = false, bool hasTool = true, bool deliversInPlace = false) =>
        new(hasTarget, arrived, carrying, stockLimitReached, hasTool, deliversInPlace);

    static void Report(bool ok, string name, string detail) =>
        Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}{(detail.Length == 0 ? "" : ": " + detail)}");
}
