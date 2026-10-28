using System;
using System.IO;
using SimpleJSON;

namespace NGUInjector
{
    internal static class SaveSafetyDependencies { internal static void Register() { } }
    // This source-linked transport fixture has no native executor. Native authority,
    // semantic import and review tokens execute in ExecutionPersistenceChecks against
    // the production assembly. Never return fabricated review success here.
    internal partial class Main
    {
        private enum ExecutionLifecycle { Running, Quiescing, LoadingSave, Unloaded }
        private ExecutionLifecycle _executionLifecycle;
        private object _executionRebindReview;
        private bool _customTitanFightActive, _customTitanGoldActive, _customAdventureGoldPushActive;
        private void SetExecutionLifecycle(ExecutionLifecycle state) { _executionLifecycle = state; }
        private void SuspendProtectedStageProgress() { }
        private static void SetCustomAutomationStatus(string reason) { }
        private JSONObject PreviewExecutionRebind(JSONNode args) => throw new NotSupportedException("Native review is outside this transport fixture.");
        private JSONObject PreviewExecutionRestart(JSONNode args) => throw new NotSupportedException("Native recovery is outside this transport fixture.");
        private string ApplyExecutionRebind(JSONNode args) => throw new NotSupportedException("Native review is outside this transport fixture.");
        private JSONObject GetExecutionPersistenceRuntime() => new JSONObject();
        internal bool ExecutionRuntimeFails;
        private JSONObject GetExecutionStateRuntime()
        {
            if (ExecutionRuntimeFails) throw new InvalidOperationException("fixture execution observation failed");
            return new JSONObject();
        }
        private JSONObject GetSaveSafetyRuntime() => new JSONObject();
        internal static bool SaveTestActive => false;
        internal static bool SaveTestInternalRestart => false;
        internal static bool SaveTestCommandAllowed(string command, JSONNode args = null) => true;
        internal static bool SaveTestCommandCreatedAtAllowed(string command, DateTime createdAt) => true;
        internal bool IsSaveTestRecoveryAdapter(string command) => false;
        internal static JSONObject GetSaveTestRuntime() { var state = new JSONObject(); state["supported"] = false; return state; }
        private static JSONObject GetActiveSaveTestSafetyRuntime() => new JSONObject();
        internal void PumpSaveTestRecovery() { }
        internal void RecordSaveTestFailure(Exception error) { }
        private string ExecuteSaveTestCommand(string command, JSONNode args, out JSONNode data)
        { data = null; throw new NotSupportedException("Native save sessions are outside this transport fixture."); }
        private string ExecuteSaveSafetyCommand(string command, JSONNode args, out JSONNode data)
        { data = null; throw new NotSupportedException("Native save safety is outside this transport fixture."); }
        private string AutomaticRecoveryEntry => null;
        private string ExecutionPersistenceEntry => null;
        private bool ServiceLifecycleRetirement() => !_manualTitanActive && !(_combatPrepRun?.AdventureRouteOwned == true);
        internal string AugmentExecutionCommandId;
        private Managers.AugmentOptimizerResult RequestAugmentAllocation(Managers.AugmentOptimizerResult selected)
        {
            AugmentExecutionCommandId = CurrentExecutionCommandId;
            if (selected.Success) Managers.AugmentOptimizer.Allocations++;
            return selected;
        }
    }
}

namespace NGUInjector
{
    internal static partial class ExternalUiChecks
    {
        private static void CheckExecutionAcceptance(Main main)
        {
            var before = _assertions;
            var claims = Path.Combine(_dir, "commands", "claimed");
            Assert(Directory.GetFiles(claims, "*.accepted.json").Length == 0, "ordinary commands create no retired acceptance journals");
            Assert(main.CurrentExecutionCommandId == null, "command identity is absent outside dispatch");
            var id = "durable-async-claim";
            var envelope = Envelope(id, "startMaxItopodPush", JsonFiles.ParseObject("{\"deaths\":3}"));
            var calls = main.PushStartCalls;
            main.Process(id, envelope);
            Assert(Result(id)["success"].AsBool && main.PushStartCalls == calls + 1, "claimed async command reaches its action boundary once");
            Assert(File.ReadAllText(Path.Combine(claims, id + ".claimed")) == envelope, "atomic claim retains the actual command envelope");
            Assert(main.CurrentExecutionCommandId == null, "successful dispatch clears command identity");
            File.Delete(Path.Combine(_dir, "command-results", id + ".json"));
            main.Process(id, Envelope(id, "startMaxItopodPush", JsonFiles.ParseObject("{\"deaths\":9}")));
            Assert(main.PushStartCalls == calls + 1 && main.LastPushDeaths == 3, "persistent claim alone rejects a second delivery with the same ID");
            Assert(File.ReadAllText(Path.Combine(claims, id + ".claimed")) == envelope, "duplicate delivery never replaces the original claim");
            Assert(!File.Exists(Path.Combine(_dir, "command-results", id + ".json")), "duplicate delivery does not fabricate a result");

            var failedResult = "async-result-publication-fails";
            var resultPath = Path.Combine(_dir, "command-results", failedResult + ".json");
            Directory.CreateDirectory(resultPath);
            calls = main.PushStartCalls;
            main.Process(failedResult, Envelope(failedResult, "startMaxItopodPush", JsonFiles.ParseObject("{\"deaths\":4}")));
            Assert(main.PushStartCalls == calls + 1 && !File.Exists(resultPath), "result publication failure happens after exactly one callback");
            Assert(File.Exists(Path.Combine(claims, failedResult + ".claimed")), "result publication failure retains persistent deduplication");
            Directory.Delete(resultPath);
            main.Process(failedResult, Envelope(failedResult, "startMaxItopodPush", JsonFiles.ParseObject("{\"deaths\":4}")));
            Assert(main.PushStartCalls == calls + 1 && !File.Exists(resultPath), "redelivery after result obstruction is removed never repeats the callback");
            Assert(main.CurrentExecutionCommandId == null, "result publication failure leaks no dispatch identity");

            main.PushStartFails = true;
            try
            {
                var failedCallback = "async-callback-fails";
                calls = main.PushStartCalls;
                main.Process(failedCallback, Envelope(failedCallback, "startMaxItopodPush", JsonFiles.ParseObject("{\"deaths\":1}")));
                Assert(!Result(failedCallback)["success"].AsBool && main.PushStartCalls == calls + 1, "callback failure reports the actual failed attempt");
                Assert(main.CurrentExecutionCommandId == null, "throwing dispatch clears command identity");
                main.Process(failedCallback, Envelope(failedCallback, "startMaxItopodPush", JsonFiles.ParseObject("{\"deaths\":1}")));
                Assert(main.PushStartCalls == calls + 1, "failed claimed callback is not replayed");
            }
            finally { main.PushStartFails = false; }
            Assert(Directory.GetFiles(claims, "*.accepted.json").Length == 0, "successful and failed dispatch create no acceptance files");

            foreach (var locked in new[] { false, true })
            {
                var retired = locked ? "async-locked-retired-acceptance" : "async-malformed-retired-acceptance";
                var retiredPath = Path.Combine(claims, retired + ".accepted.json");
                File.WriteAllText(retiredPath, "{malformed retired record");
                calls = main.PushStartCalls;
                using (var held = locked ? new FileStream(retiredPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None) : null)
                    main.Process(retired, Envelope(retired, "startMaxItopodPush", JsonFiles.ParseObject("{\"deaths\":3}")));
                Assert(Result(retired)["success"].AsBool && main.PushStartCalls == calls + 1, "retired acceptance does not block dispatch, locked=" + locked);
                Assert(File.Exists(Path.Combine(claims, retired + ".claimed")), "retired acceptance does not replace the command claim, locked=" + locked);
                Assert(File.ReadAllText(retiredPath) == "{malformed retired record", "retired acceptance is never rewritten, locked=" + locked);
                main.Process(retired, Envelope(retired, "startMaxItopodPush", JsonFiles.ParseObject("{\"deaths\":3}")));
                Assert(main.PushStartCalls == calls + 1, "retired acceptance cannot grant replay, locked=" + locked);
            }
            main.Process("dispatch-scope", Envelope("dispatch-scope", "allocateAugments"));
            Assert(Result("dispatch-scope")["success"].AsBool && main.AugmentExecutionCommandId == "dispatch-scope", "action boundary observes the current command ID during dispatch");
            Assert(main.CurrentExecutionCommandId == null, "action boundary identity ends with dispatch");
            Assert(Directory.GetFiles(claims, "*.accepted.json").Length == 2, "only the two deliberately seeded retired acceptance files exist");
            Console.WriteLine("Execution command transport: " + (_assertions - before) + " assertions.");
        }
    }
}
