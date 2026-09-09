namespace Kukolony.Jobs
{
    /// <summary>
    ///     Definitions written to the jobs folder on first run.
    ///
    ///     Shipping the built-in job as a real file makes the folder self-documenting: a
    ///     player who wants a new job copies this one rather than guessing the schema.
    /// </summary>
    internal static class DefaultJobDefinitions
    {
        internal const string HaulFileName = "haul.json";

        /// <summary>
        ///     Note there is no item or radius here. Those are configuration and live on
        ///     the work post; a definition describes the *shape* of the work only, which
        ///     is what lets one definition serve every post that uses it.
        /// </summary>
        internal const string Haul = @"{
  ""id"": ""haul"",
  ""displayName"": ""$kukolony_job_haul"",

  ""steps"": [
    { ""type"": ""find_ground_item"" },
    { ""type"": ""move_to_target"", ""stopDistance"": 2.0 },
    { ""type"": ""pick_up_item"" },
    { ""type"": ""resolve_destination"" },
    { ""type"": ""move_to_target"", ""stopDistance"": 2.0 },
    { ""type"": ""deposit_item"" }
  ]
}
";
    }
}
