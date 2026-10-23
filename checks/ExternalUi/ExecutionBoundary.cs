using System;
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
        private JSONObject GetExecutionStateRuntime() => new JSONObject();
        private JSONObject GetSaveSafetyRuntime() => new JSONObject();
        private string ExecuteSaveSafetyCommand(string command, JSONNode args, out JSONNode data)
        { data = null; throw new NotSupportedException("Native save safety is outside this transport fixture."); }
        private string AutomaticRecoveryEntry => null;
        private string ExecutionPersistenceEntry => null;
        private void PersistLifecycleExecutionEvidence() { }
        private bool ServiceLifecycleRetirement() => !_manualTitanActive && !(_combatPrepRun?.AdventureRouteOwned == true);
        private JSONObject ExportAcceptedExecutionProcess()
        {
            var result = new JSONObject(); result["id"] = _currentExecutionCommandId; result["boundary"] = "transport-only"; return result;
        }
        private Managers.AugmentOptimizerResult RequestAugmentAllocation(Managers.AugmentOptimizerResult selected)
        {
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
            var id = "durable-async-acceptance";
            main.Process(id, Envelope(id, "startMaxItopodPush", JsonFiles.ParseObject("{\"deaths\":3}")));
            var path = System.IO.Path.Combine(_dir, "commands", "claimed", id + ".accepted.json");
            var accepted = JsonFiles.ParseObject(System.IO.File.ReadAllText(path));
            Assert(Result(id)["success"].AsBool && accepted["state"].Value == "accepted", "async semantic acceptance is durable before successful ACK");
            Assert(accepted["commandId"].Value == id && accepted["process"]["id"].Value == id, "accepted process retains the actual command opportunity");
            var calls = main.PushStartCalls;
            main.Process(id, Envelope(id, "startMaxItopodPush", JsonFiles.ParseObject("{\"deaths\":3}")));
            Assert(main.PushStartCalls == calls, "durable accepted command remains claimed and is not replayed");
            var blocked = "durable-async-blocked";
            var blockedPath = System.IO.Path.Combine(_dir, "commands", "claimed", blocked + ".accepted.json");
            System.IO.File.WriteAllText(blockedPath, "{}");
            using (var locked = new System.IO.FileStream(blockedPath, System.IO.FileMode.Open, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None))
                main.Process(blocked, Envelope(blocked, "startMaxItopodPush", JsonFiles.ParseObject("{\"deaths\":3}")));
            Assert(!Result(blocked)["success"].AsBool && main.PushStartCalls == calls, "admission persistence failure precedes the async process callback");
            main.Process(blocked, Envelope(blocked, "startMaxItopodPush", JsonFiles.ParseObject("{\"deaths\":3}")));
            Assert(main.PushStartCalls == calls, "unknown/rejected claimed command is never automatically replayed");
        }
    }
}
