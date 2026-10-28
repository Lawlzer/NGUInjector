using System;
using System.Text;
using System.Threading.Tasks;

namespace NGUInjector
{
    internal static partial class ExternalUiChecks
    {
        private static void CheckAutomationErrors(Main main)
        {
            AutomationErrorLog.Clear();
            Assert(AutomationErrorLog.Snapshot().Count == 0, "new session has no exception history");
            Exception error;
            try { throw new InvalidOperationException("native allocation failed"); }
            catch (Exception caught) { error = caught; }
            AutomationErrorLog.Record("Allocation", error);
            var first = AutomationErrorLog.Snapshot();
            Assert(first.Count == 1 && first[0]["count"].AsInt == 1, "exception is recorded once");
            Assert(first[0]["details"].Value.Contains(nameof(CheckAutomationErrors)), "exception retains its stack trace");
            AutomationErrorLog.Record("Allocation", error);
            var repeated = AutomationErrorLog.Snapshot();
            Assert(repeated.Count == 1 && repeated[0]["count"].AsInt == 2, "repeated exception is grouped");
            Assert(repeated[0]["firstSeenAt"].Value == first[0]["firstSeenAt"].Value
                && DateTime.Parse(repeated[0]["lastSeenAt"].Value) >= DateTime.Parse(first[0]["lastSeenAt"].Value), "repeat preserves first occurrence and advances last occurrence");
            first[0]["message"] = "mutated snapshot";
            Assert(AutomationErrorLog.Snapshot()[0]["message"].Value == error.Message, "readers cannot mutate retained errors");
            Parallel.For(0, 64, _ => AutomationErrorLog.Record("Allocation", error));
            Assert(AutomationErrorLog.Snapshot()[0]["count"].AsInt == 66, "concurrent failures retain exact repeat count");
            AutomationErrorLog.Record("Adventure", error);
            Assert(AutomationErrorLog.Snapshot().Count == 2, "different failing activities stay distinct");
            for (var i = 0; i < 24; i++) AutomationErrorLog.Record("Unique " + i, new Exception("failure " + i));
            var bounded = AutomationErrorLog.Snapshot();
            Assert(bounded.Count == 16 && bounded[0]["context"].Value == "Unique 23"
                && bounded[15]["context"].Value == "Unique 8", "history is bounded and newest failures are first");
            AutomationErrorLog.Record(new string('c', 10000), new Exception(new string('m', 20000)));
            var oversized = AutomationErrorLog.Snapshot()[0];
            Assert(oversized["context"].Value.Length == 160 && oversized["message"].Value.Length == 1024
                && oversized["details"].Value.Length <= 4096, "error payload is bounded independently of exception size");
            main.Process("error-telemetry-status", Envelope("error-telemetry-status", "status"));
            var result = Result("error-telemetry-status");
            Assert(result["success"].AsBool && result["data"]["runtime"]["automationErrors"].Count == 16,
                "status publishes actual bounded exception history without a game action");
            AutomationErrorLog.Clear();
            for (var i = 0; i < 16; i++)
                AutomationErrorLog.Record("escaped " + i, new Exception(new string('\u0001', 10000)));
            var escaped = AutomationErrorLog.Snapshot();
            Assert(escaped.Count > 0 && escaped[0]["context"].Value == "escaped 15", "byte-budgeted history retains the latest actual failure");
            Assert(Encoding.UTF8.GetByteCount(escaped.ToString()) <= 32 * 1024, "serialized UTF-8 error history stays within its envelope budget including JSON escapes");
            AutomationErrorLog.Clear();
            Assert(AutomationErrorLog.Snapshot().Count == 0, "session reset clears only exception history");
            main.ExecutionRuntimeFails = true;
            try
            {
                main.Process("error-telemetry-partial", Envelope("error-telemetry-partial", "status"));
                var partial = Result("error-telemetry-partial");
                var runtime = partial["data"]["runtime"];
                Assert(partial["success"].AsBool && runtime["status"].Value == "running", "one failed projection does not suppress the heartbeat");
                Assert(runtime["execution"]["status"].Value == "error" && !runtime["execution"]["available"].AsBool
                    && runtime["execution"]["reason"].Value == "fixture execution observation failed", "failed projection is unavailable with the actual reason, never an empty success");
                Assert(runtime["resourceAllocation"].IsObject && runtime["planProgress"].IsObject, "unrelated runtime projections remain available");
            }
            finally { main.ExecutionRuntimeFails = false; }
            main.Process("error-telemetry-recovered", Envelope("error-telemetry-recovered", "status"));
            Assert(!Result("error-telemetry-recovered")["data"]["runtime"]["execution"]["status"].IsString,
                "fresh successful projection replaces the failed section");
        }
    }
}
