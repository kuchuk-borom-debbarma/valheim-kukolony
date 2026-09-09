using System.Collections.Generic;

namespace Kukolony.Jobs
{
    /// <summary>
    ///     A job as written in a definition file, before it becomes runnable steps.
    ///
    ///     Kept separate from <see cref="Job" /> so parsing and validation can fail
    ///     cleanly without ever producing a half-built job.
    /// </summary>
    internal sealed class JobDefinition
    {
        internal JobDefinition(string id, string displayName, IReadOnlyList<JobStepSpec> steps, string source)
        {
            Id = id;
            DisplayName = displayName;
            Steps = steps;
            Source = source;
        }

        internal string Id { get; }

        internal string DisplayName { get; }

        internal IReadOnlyList<JobStepSpec> Steps { get; }

        /// <summary>File this came from, so errors can name it.</summary>
        internal string Source { get; }
    }

    /// <summary>One step entry: its type, and whatever literals that type reads.</summary>
    internal sealed class JobStepSpec
    {
        internal JobStepSpec(string type, IDictionary<string, object> parameters)
        {
            Type = type;
            Parameters = parameters;
        }

        internal string Type { get; }

        internal IDictionary<string, object> Parameters { get; }

        /// <summary>
        ///     Numbers arrive from JSON as double or long depending on how they were
        ///     written, so both are accepted rather than trusting the author to add a
        ///     decimal point.
        /// </summary>
        internal float GetFloat(string key, float fallback)
        {
            if (!Parameters.TryGetValue(key, out object raw) || raw == null)
            {
                return fallback;
            }

            switch (raw)
            {
                case double d: return (float)d;
                case long l: return l;
                case int i: return i;
                default:
                    return float.TryParse(raw.ToString(), out float parsed) ? parsed : fallback;
            }
        }
    }
}
