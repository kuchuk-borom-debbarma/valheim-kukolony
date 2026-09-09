using System.Collections.Generic;
using System.Text;
using Kukolony.Core;

namespace Kukolony.Debug
{
    /// <summary>
    ///     Collects pass/fail checks and prints one summary block.
    ///
    ///     A summary matters more than it sounds: these runs are read from
    ///     BepInEx/LogOutput.log after the fact, interleaved with thousands of vanilla
    ///     lines. A single delimited block is the difference between a readable result
    ///     and archaeology.
    /// </summary>
    internal sealed class TestReport
    {
        private readonly List<string> _lines = new List<string>();
        private readonly string _title;
        private int _failures;

        internal TestReport(string title)
        {
            _title = title;
        }

        internal bool HasFailures => _failures > 0;

        internal void Check(bool passed, string description, string detail = null)
        {
            string suffix = string.IsNullOrEmpty(detail) ? string.Empty : $" - {detail}";
            _lines.Add($"  [{(passed ? "PASS" : "FAIL")}] {description}{suffix}");

            if (!passed)
            {
                _failures++;
            }
        }

        internal void Note(string message) => _lines.Add($"  ....  {message}");

        internal void Print()
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine();
            builder.AppendLine("==================== KUKOLONY SELF TEST ====================");
            builder.AppendLine($"  {_title}");
            builder.AppendLine("------------------------------------------------------------");

            foreach (string line in _lines)
            {
                builder.AppendLine(line);
            }

            builder.AppendLine("------------------------------------------------------------");
            builder.AppendLine(_failures == 0
                ? "  RESULT: PASS"
                : $"  RESULT: FAIL ({_failures} failed)");
            builder.Append("============================================================");

            if (_failures == 0)
            {
                Log.Info(builder.ToString());
            }
            else
            {
                Log.Error(builder.ToString());
            }
        }
    }
}
