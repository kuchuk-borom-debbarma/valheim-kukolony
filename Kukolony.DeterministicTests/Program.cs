using Kukolony.Jobs;

static class Program
{
    static int Main()
    {
        var cases = new (string Name, JobPieceKind[] Pieces, bool Expected)[]
        {
            ("haul", [JobPieceKind.Start, JobPieceKind.FindLooseItem, JobPieceKind.MoveToTarget, JobPieceKind.PickUp, JobPieceKind.SelectTarget, JobPieceKind.PutItem, JobPieceKind.End], true),
            ("transfer", [JobPieceKind.Start, JobPieceKind.SelectSource, JobPieceKind.TakeItem, JobPieceKind.SelectTarget, JobPieceKind.PutItem, JobPieceKind.End], true),
            ("missing start", [JobPieceKind.FindLooseItem, JobPieceKind.End], false),
            ("pickup without target", [JobPieceKind.Start, JobPieceKind.PickUp, JobPieceKind.End], false),
            ("take without source", [JobPieceKind.Start, JobPieceKind.TakeItem, JobPieceKind.End], false),
            ("put without destination", [JobPieceKind.Start, JobPieceKind.PutItem, JobPieceKind.End], false)
        };
        int failures = 0;
        foreach (var test in cases)
        {
            bool actual = PipelineShapeRules.IsValid(test.Pieces, out var reason);
            Console.WriteLine($"[{(actual == test.Expected ? "PASS" : "FAIL")}] {test.Name}{(reason.Length == 0 ? "" : ": " + reason)}");
            if (actual != test.Expected) failures++;
        }
        Console.WriteLine(failures == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({failures})");
        return failures == 0 ? 0 : 1;
    }
}
