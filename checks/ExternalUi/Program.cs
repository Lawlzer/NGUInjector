using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using NGUInjector.CustomPlans;
using SimpleJSON;

namespace NGUInjector
{
    internal static partial class ExternalUiChecks
    {
        private static int _assertions;
        private static string _dir;

        private static void Assert(bool condition, string name)
        {
            _assertions++;
            if (!condition) throw new Exception("FAIL: " + name);
        }

        private static void Reject(Action action, string name)
        {
            try { action(); } catch (Exception) { _assertions++; return; }
            throw new Exception("FAIL (accepted): " + name);
        }

        private static JSONObject Result(string id)
        {
            return JsonFiles.ParseObject(File.ReadAllText(Path.Combine(_dir, "command-results", id + ".json")));
        }

        private static string Envelope(string id, string command, JSONNode args = null)
        {
            if (command == "applySettingsPatch" && args != null && !args.Linq.Any(item => item.Key == "expectedRevision"))
            {
                var path = Path.Combine(_dir, "settings.json");
                args["expectedRevision"] = File.Exists(path) ? JsonFiles.ComputeRevision(File.ReadAllBytes(path)) : string.Empty;
            }
            var root = new JSONObject(); root["id"] = id; root["command"] = command;
            if (args != null) root["args"] = args;
            return root.ToString();
        }

        public static int Main(string[] arguments)
        {
            if (!OperatingSystem.IsWindows())
            {
                Console.Error.WriteLine("Windows is required to check file-sharing and atomic replacement semantics.");
                return 1;
            }
            if (arguments.Contains("--plan-blood")) return RunPlanBloodPersistenceOnly();
            if (arguments.Contains("--fight-schedules")) return RunFightSchedulesOnly();
            if (arguments.Contains("--plan-catalog")) return RunPlanCatalogOnly();
            if (arguments.Contains("--adventure-dynamic-weight")) return RunAdventureDynamicWeightOnly();
            if (arguments.Contains("--adventure-rules")) return AdventureRuleSettingsChecks.RunOnly();
            if (arguments.Contains("--native-settings")) return RunNativeSettingsPersistenceOnly(arguments.Last() == "--native-settings" ? null : arguments.Last());
            if (arguments.Contains("--settings-cas")) return RunSettingsCasOnly();
            var originalCulture = Thread.CurrentThread.CurrentCulture;
            var originalExternalUi = Environment.GetEnvironmentVariable("NGUINJECTOR_EXTERNAL_UI");
            var exitCode = 0;
            try
            {
                _dir = Path.Combine(Path.GetTempPath(), "opencode", "ngu-external-ui-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_dir);
                foreach (var text in new[] {
                    "", "{", "{\"command\":\"quickSave\"", "{\"a\":1,}", "{\"a\":[1,]}",
                    "{\"a\":01}", "{\"a\":+1}", "{\"a\":NaN}", "{\"a\":1e999}", "{a:1}",
                    "{\"a\":truex}", "{\"a\":1} trailing", "{\"a\":1}{\"b\":2}", "{\"a\":1,\"a\":2}",
                    "{\"a\":\"line\nfeed\"}", "{\"a\":\"\\x\"}", "{\"a\":\"\\u123\"}", "[]", "null", "{\"a\":[1}}" })
                    Reject(() => JsonFiles.ParseObject(text), "malformed JSON " + text);
                var valid = JsonFiles.ParseObject("{\"\":1,\"a\":[true,false,null,{},[],\"\\u0041\\n\"],\"n\":-1.25e2}");
                Assert(valid[""].AsInt == 1 && valid["a"].Count == 6 && valid["a"][5].Value == "A\n" && valid["n"].AsDouble == -125, "JSON structure and escapes");
                Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
                Assert(JsonFiles.ParseObject("{\"n\":1.25}").ToString() == "{\"n\":1.25}", "invariant numeric round trip");
                Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
                var atomic = Path.Combine(_dir, "atomic.json");
                JsonFiles.WriteAtomic(atomic, "{\"n\":1}"); JsonFiles.WriteAtomic(atomic, "{\"n\":2}");
                Assert(File.ReadAllText(atomic) == "{\"n\":2}" && !Directory.GetFiles(_dir, "*.tmp").Any(), "atomic create and replace");
                using (var locked = new FileStream(atomic, FileMode.Open, FileAccess.Read, FileShare.None))
                    Reject(() => JsonFiles.WriteAtomic(atomic, "{\"n\":3}"), "locked atomic replacement");
                Assert(File.ReadAllText(atomic) == "{\"n\":2}", "failed replace preserves destination");

                var leaseDir = Path.Combine(_dir, "lease-check"); Directory.CreateDirectory(leaseDir);
                var leaseSettings = Path.Combine(leaseDir, "settings.json"); File.WriteAllText(leaseSettings, "offline editor data");
                using (var offlineLease = new FileStream(Path.Combine(leaseDir, ".injector.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                {
                    var wait = Stopwatch.StartNew();
                    Reject(() => { using (var lease = JsonFiles.AcquireInjectorLease(leaseDir, 100)) { } }, "offline lease prevents injector ownership");
                    Assert(wait.ElapsedMilliseconds >= 90 && wait.ElapsedMilliseconds < 2000, "lease acquisition waits a bounded interval");
                    Assert(File.ReadAllText(leaseSettings) == "offline editor data", "failed lease acquisition never changes settings");
                }
                using (var injectorLease = JsonFiles.AcquireInjectorLease(leaseDir, 0))
                    Reject(() => { using (var offlineLease = new FileStream(Path.Combine(leaseDir, ".injector.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) { } }, "injector lifetime excludes offline editors");
                var retiringLease = JsonFiles.AcquireInjectorLease(leaseDir, 0);
                var releaseThread = new Thread(() => { Thread.Sleep(100); retiringLease.Dispose(); }); releaseThread.Start();
                using (var waitingLease = JsonFiles.AcquireInjectorLease(leaseDir, 1000))
                    Assert(waitingLease.CanWrite, "startup can acquire ownership after an offline edit finishes");
                releaseThread.Join();
                Assert(File.Exists(Path.Combine(leaseDir, ".injector.lock")), "lease release does not delete the ownership file");
                Assert(JsonFiles.ComputeRevision(Encoding.UTF8.GetBytes("abc")) == "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", "revision uses standard lowercase SHA256");

                var main = new NGUInjector.Main(); main.Initialize(_dir, false);
                Assert(JsonFiles.ParseObject(File.ReadAllText(Path.Combine(_dir, "runtime.json")))["status"].Value == "starting", "startup advertises starting before readiness");
                foreach (var command in new[] { "quickSave", "status", "safeProbe", "readPlan", "applySettingsPatch", "savePlan", "createPlan", "deletePlan", "allocateAugments", "setPlanStage", "startMaxItopodPush", "cancelMaxItopodPush", "unload" })
                {
                    var id = "not-ready-" + command;
                    main.Process(id, Envelope(id, command));
                    Assert(!Result(id)["success"].AsBool && Result(id)["message"].Value.Contains("not ready"), "dispatcher gates " + command + " before readiness");
                }
                Assert(NGUInjector.Main.Saves == 0 && Managers.AugmentOptimizer.Allocations == 0 && NGUInjector.Main.StageOverrides == 0, "not-ready commands execute no game boundary actions");
                Assert(main.PushStartCalls == 0 && main.PushCancelCalls == 0, "not-ready push commands execute no action boundaries");
                main.SetReady(true);
                CheckAutomationErrors(main);
                Assert(JsonFiles.ParseObject(File.ReadAllText(Path.Combine(_dir, "runtime.json")))["status"].Value == "running", "readiness advertises running");
                main.Process("once", Envelope("once", "quickSave"));
                Assert(NGUInjector.Main.Saves == 1 && Result("once")["success"].AsBool, "command executes and reports");
                File.Delete(Path.Combine(_dir, "command-results", "once.json"));
                main.Process("once", Envelope("once", "quickSave"));
                Assert(NGUInjector.Main.Saves == 1, "claim prevents replay when result removed");
                Directory.CreateDirectory(Path.Combine(_dir, "command-results", "report-fails.json"));
                main.Process("report-fails", Envelope("report-fails", "quickSave"));
                var reportedSaves = NGUInjector.Main.Saves;
                main.Process("report-fails", Envelope("report-fails", "quickSave"));
                Assert(NGUInjector.Main.Saves == reportedSaves && reportedSaves == 2, "no replay after result write failure");
                var duplicatePath = Path.Combine(_dir, "commands", "once.json");
                File.WriteAllText(duplicatePath, Envelope("once", "quickSave"));
                using (var locked = new FileStream(duplicatePath, FileMode.Open, FileAccess.Read, FileShare.None)) main.Pump();
                Assert(File.Exists(duplicatePath) && NGUInjector.Main.Saves == reportedSaves, "no replay when duplicate deletion fails");
                File.Delete(duplicatePath);
                main.Process("bad.dot", "{\"command\":\"quickSave\"}");
                Assert(Directory.GetFiles(Path.Combine(_dir, "command-results"), "invalid-*.json").Length == 1 && NGUInjector.Main.Saves == reportedSaves, "invalid filename quarantined with safe result ID");
                main.Process("bad-id", "{\"id\":\"../../escaped\",\"command\":\"quickSave\"}");
                Assert(!Result("bad-id")["success"].AsBool && NGUInjector.Main.Saves == reportedSaves, "path injection rejected");
                main.Process("mismatch", Envelope("different", "quickSave"));
                Assert(!Result("mismatch")["success"].AsBool, "ID mismatch rejected");
                main.Process("broken", "{\"command\":\"quickSave\"");
                Assert(!Result("broken")["success"].AsBool, "truncated command rejected");
                main.Process("expired", "{\"command\":\"quickSave\",\"createdAt\":\"1\"}");
                Assert(!Result("expired")["success"].AsBool, "expired epoch command rejected");
                main.Process("deadline", "{\"command\":\"quickSave\",\"expiresAt\":\"2000-01-01T00:00:00Z\"}");
                Assert(!Result("deadline")["success"].AsBool, "explicit expiry rejected");
                main.Process("future", "{\"command\":\"quickSave\",\"createdAt\":\"2100-01-01T00:00:00Z\"}");
                Assert(!Result("future")["success"].AsBool, "future timestamp rejected");
                main.Process("unknown", Envelope("unknown", "notACommand"));
                Assert(!Result("unknown")["success"].AsBool, "unknown command rejected");
                main.Process("wrongargs", "{\"command\":\"quickSave\",\"args\":[]}");
                Assert(!Result("wrongargs")["success"].AsBool, "nonobject args rejected");
                var maxCommandBytes = JsonFiles.ParseObject(File.ReadAllText(Path.Combine(_dir, "runtime.json")))["maxCommandBytes"].AsInt;
                Assert(maxCommandBytes == 3 * 1024 * 1024, "runtime advertises the 3 MiB release command bound");
                main.Process("too-big", "{\"command\":\"quickSave\",\"padding\":\"" + new string('a', maxCommandBytes) + "\"}");
                Assert(!Result("too-big")["success"].AsBool, "oversized command rejected");
                var epoch = (long)(DateTime.UtcNow - new DateTime(1970,1,1)).TotalMilliseconds;
                main.Process("rust", "{\"id\":\"rust\",\"command\":\"quickSave\",\"args\":null,\"createdAt\":\"" + epoch + "\"}");
                Assert(Result("rust")["success"].AsBool && NGUInjector.Main.Saves == reportedSaves + 1, "Rust timestamp and null args");
                main.Process("legacy", "{\"command\":\"quickSave\"}");
                Assert(Result("legacy")["success"].AsBool && NGUInjector.Main.Saves == reportedSaves + 2, "legacy envelope fallback");

                NGUInjector.Main.Settings = new SavedSettings(_dir);
                NGUInjector.Main.Settings.AllocationFile = "default";
                NGUInjector.Main.Settings.CustomPlanFile = "default";
                NGUInjector.Main.Settings.CombatEnabled = true;
                CheckLoadoutPresetBridge(main);
                CheckMaxItopodPushCommands(main);
                var heartbeatBeforePatch = JsonFiles.ParseObject(File.ReadAllText(Path.Combine(_dir, "runtime.json")));
                main.Process("patch", Envelope("patch", "applySettingsPatch", JsonFiles.ParseObject("{\"patch\":{\"_autoRebirth\":true}}")));
                Assert(Result("patch")["success"].AsBool && NGUInjector.Main.Settings.AutoRebirth && NGUInjector.Main.Settings.CombatEnabled, "patch preserves unpatched live fields: " + Result("patch"));
                var settingsText = File.ReadAllText(Path.Combine(_dir, "settings.json"));
                Assert(Result("patch")["data"]["runtime"]["settings"]["autoRebirth"].AsBool, "settings command returns post-effect runtime without waiting for heartbeat");
                var compactAck = Result("patch")["data"]["runtime"];
                Assert(compactAck["startedAt"].IsString && !string.IsNullOrWhiteSpace(compactAck["startedAt"].Value)
                    && compactAck["startedAt"].Value == heartbeatBeforePatch["startedAt"].Value, "compact acknowledgement carries the heartbeat's explicit session identity");
                Assert(compactAck["externalUiEnabled"].IsBoolean && compactAck["externalUiEnabled"].AsBool, "compact acknowledgement explicitly enables external UI heartbeat consumption");
                Assert(compactAck["protocolVersion"].IsNumber && compactAck["protocolVersion"].AsInt == 3, "compact acknowledgement advertises additive protocol 3");
                Assert(compactAck["planVersion"].IsNumber && compactAck["planVersion"].AsInt == 18 && compactAck["stageTimingVersion"].AsInt == 1
                    && compactAck["fightScheduleVersion"].AsInt == 1, "compact acknowledgement advertises plan schema 18, universal timing and fight schedules");
                Assert(compactAck["stageOverridesV1"].IsBoolean && compactAck["stageOverridesV1"].AsBool, "compact acknowledgement advertises manual stage override capability");
                Assert(compactAck["loadoutPresetsVersion"].AsInt == 2 && compactAck["globalPresetsVersion"].AsInt == 2
                    && compactAck["globalNguWeightsVersion"].AsInt == 1, "compact acknowledgement advertises global-only NGU weights and ratio-free presets");
                Assert(compactAck["runtimeRevision"].IsNumber && compactAck["runtimeRevision"].AsDouble > heartbeatBeforePatch["runtimeRevision"].AsDouble
                    && compactAck["runtimeRevision"].AsDouble <= 9007199254740991d
                    && compactAck["runtimeRevision"].AsDouble == Math.Truncate(compactAck["runtimeRevision"].AsDouble), "compact acknowledgement carries a newer JavaScript-safe integer runtime revision");
                Assert(!compactAck.Linq.Any(item => item.Key == "supportedCommands"), "command acknowledgement is the compact builder result, not a full heartbeat fallback");
                Assert(JsonFiles.ParseObject(File.ReadAllText(Path.Combine(_dir, "runtime.json")))["updatedAt"].Value == Result("patch")["data"]["runtime"]["updatedAt"].Value, "command publishes the same immediate runtime timestamp");
                Assert(JsonFiles.ParseObject(File.ReadAllText(Path.Combine(_dir, "runtime.json")))["runtimeRevision"].AsDouble == compactAck["runtimeRevision"].AsDouble,
                    "compact command result and corresponding full file share one publication revision");
                main.Process("patch-bad", Envelope("patch-bad", "applySettingsPatch", JsonFiles.ParseObject("{\"patch\":{\"_autoRebirth\":false,\"_savePath\":\"bad\"}}")));
                Assert(!Result("patch-bad")["success"].AsBool && NGUInjector.Main.Settings.AutoRebirth && File.ReadAllText(Path.Combine(_dir, "settings.json")) == settingsText, "bad patch has no partial mutation");
                foreach (var patch in new[] { "{\"_combatEnabled\":1}", "{\"_customAdventureZone\":1.5}", "{\"_priorityBoosts\":null}", "{\"_titanGoldTargets\":[true]}", "{\"_allocationFile\":\"../bad\"}" })
                    Reject(() => NGUInjector.Main.Settings.ApplyPatch(JsonFiles.ParseObject(patch)), "invalid patch " + patch);
                main.Watch(0); main.ForceWatchDue(0); var allocations = NGUInjector.Main.Allocations; main.PumpWatches();
                Assert(NGUInjector.Main.Allocations == allocations, "own settings writes do not reload runtime");
                File.WriteAllText(Path.Combine(_dir, "settings.json"), "{\"_autoRebirth\":");
                main.Watch(0); main.ForceWatchDue(0); main.PumpWatches();
                Assert(main.WatchPending(0) && NGUInjector.Main.Settings.AutoRebirth, "partial settings reload retained and retried");
                var reloadedSettings = JsonFiles.ParseObject(settingsText); reloadedSettings["_autoRebirth"] = false;
                File.WriteAllText(Path.Combine(_dir, "settings.json"), reloadedSettings.ToString());
                main.ForceWatchDue(0); main.PumpWatches();
                Assert(!main.WatchPending(0) && !NGUInjector.Main.Settings.AutoRebirth, "atomic replace reload succeeds on retry");
                var worker = new Thread(() => main.Watch(3)); worker.Start(); worker.Join();
                Assert(NGUInjector.Main.ZoneReloads == 0, "watcher worker only queues");
                File.WriteAllText(Path.Combine(_dir, "zoneOverride.json"), "{}"); main.ForceWatchDue(3); main.PumpWatches();
                Assert(NGUInjector.Main.ZoneReloads == 1, "watcher work runs in pump");

                var settingsPath = Path.Combine(_dir, "settings.json");
                var retainedSettings = JsonFiles.ParseObject(File.ReadAllText(settingsPath));
                retainedSettings["futureSetting"] = JsonFiles.ParseObject("{\"_customAdventureGoldPush\":false,\"nested\":[null,true,{\"precise\":9223372036854775807,\"decimal\":1.234567890123456789}]} ");
                retainedSettings["futureNullable"] = JSONNull.CreateOrGet();
                File.WriteAllText(settingsPath, retainedSettings.ToString());
                Assert(NGUInjector.Main.Settings.LoadSettings(), "load settings with unknown fields");
                NGUInjector.Main.Settings.CombatEnabled = false;
                var afterSave = JsonFiles.ParseObject(File.ReadAllText(settingsPath));
                Assert(afterSave["futureSetting"].ToString() == retainedSettings["futureSetting"].ToString(), "SaveSettings overlays known fields without changing nested unknowns");
                Assert(afterSave["futureSetting"]["nested"][2]["precise"].Value == "9223372036854775807", "unknown integers retain exact precision");
                Assert(afterSave["futureSetting"]["nested"][2]["decimal"].Value == "1.234567890123456789", "unknown decimals retain exact precision");
                var editedNumber = JsonFiles.ParseObject("{\"number\":9223372036854775807}"); editedNumber["number"].AsDouble = 1.5;
                Assert(editedNumber.ToString() == "{\"number\":1.5}", "editing a retained numeric node invalidates its original token");
                Assert(afterSave.Linq.Any(item => item.Key == "futureNullable" && item.Value.IsNull), "unknown null field remains present");
                NGUInjector.Main.Settings.QuickLoadout = new[] { 40, 41 };
                main.Process("unsaved-native-patch", Envelope("unsaved-native-patch", "applySettingsPatch", JsonFiles.ParseObject("{\"patch\":{\"_autoRebirth\":true}}")));
                Assert(!Result("unsaved-native-patch")["success"].AsBool, "external patch refuses unsaved native state");
                NGUInjector.Main.Settings.SaveSettings();
                main.Process("retained-patch", Envelope("retained-patch", "applySettingsPatch", JsonFiles.ParseObject("{\"patch\":{\"_autoRebirth\":true}}")));
                var afterPatch = JsonFiles.ParseObject(File.ReadAllText(settingsPath));
                Assert(Result("retained-patch")["success"].AsBool && afterPatch["futureSetting"].ToString() == retainedSettings["futureSetting"].ToString(), "ApplyPatch retains unknown fields");
                Assert(afterPatch["_quickLoadout"].ToString() == "[40,41]", "external patch retains explicitly committed native settings");
                main.Process("unknown-settings-patch", Envelope("unknown-settings-patch", "applySettingsPatch", JsonFiles.ParseObject("{\"patch\":{\"futureSetting\":false}}")));
                Assert(!Result("unknown-settings-patch")["success"].AsBool && File.ReadAllText(settingsPath) == afterPatch.ToString(2), "unknown settings patch rejected without changing retained fields");
                var migrationDir = Path.Combine(_dir, "migration"); Directory.CreateDirectory(migrationDir);
                File.WriteAllText(Path.Combine(migrationDir, "settings.json"), "{\"future\":{\"_customAdventureGoldPush\":false}}");
                var migrationSettings = new SavedSettings(migrationDir);
                Assert(migrationSettings.LoadSettings() && migrationSettings.CustomAdventureGoldPush, "unknown nested keys do not defeat known-field defaults");
                CheckSettingsAudit(main, settingsPath);
                CheckAutoRebirthDefaults();
                CheckNativeSettingsPersistence();
                _assertions += IronPillHistoryChecks.Run(_dir);
                _assertions += AutoExpSettingsChecks.Run(_dir);
                _assertions += AdventureSettingsChecks.Run(_dir, main);
                _assertions += AdventureRuleSettingsChecks.Run(_dir);

                NGUInjector.Main.CustomPlanStore = new CustomPlanStore(Path.Combine(_dir, "custom-plans"));
                CheckPlanCatalog();
                SeedLegacyDefault(NGUInjector.Main.CustomPlanStore);
                NGUInjector.Main.Settings.CustomPlanFile = "default";
                CheckRunningRebirthPermission();
                CheckNguWeights();
                CheckRetiredGoalPresetRoundtrip(main);
                CheckGlobalNguWeightsCommands(main);
                CheckStageDurations(main);
                CheckStageTiming();
                CheckStageExecutionModes();
                CheckStageProgressPersistence();
                CheckStageProgressCreationAndDeletion();
                CheckFightSchedules();
                CheckAdventureDynamicWeight();
                CheckAutoIronPillEnabled();
                CheckPlanBloodPersistence();
                CheckBloodMetadata(main);
                CheckLoadoutPresets();
                foreach (var action in new[] {"skip", "set", "restart"})
                {
                    var id = "stage-override-" + action;
                    var args = JsonFiles.ParseObject("{\"name\":\"default\",\"expectedRevision\":\"" + NGUInjector.Main.CustomPlanStore.ReadExternalPlan("default")[CustomPlanStore.ExternalUiRevisionField].Value
                        + "\",\"expectedEntry\":\"Epoch:7/Entry:23\",\"action\":\"" + action + "\",\"stageIndex\":2}");
                    var overridesBefore = NGUInjector.Main.StageOverrides;
                    var revisionBefore = main.RuntimeRevision;
                    main.Process(id, Envelope(id, "setPlanStage", args));
                    var result = Result(id);
                    Assert(result["success"].AsBool && result["message"].Value == "stage override boundary", "dispatcher recognizes manual stage action and returns boundary message: " + action);
                    Assert(NGUInjector.Main.StageOverrides == overridesBefore + 1, "manual stage action reaches its boundary exactly once: " + action);
                    Assert(NGUInjector.Main.StageOverrideArgs.ToString() == args.ToString(), "manual stage dispatch forwards all immutable guards and action args unchanged: " + action);
                    var ack = result["data"]["runtime"];
                    Assert(ack["stageOverridesV1"].IsBoolean && ack["stageOverridesV1"].AsBool && ack["runtimeRevision"].AsDouble > revisionBefore,
                        "manual stage action publishes fresh runtime with override capability: " + action);
                    var published = JsonFiles.ParseObject(File.ReadAllText(Path.Combine(_dir, "runtime.json")));
                    Assert(published["stageOverridesV1"].IsBoolean && published["stageOverridesV1"].AsBool && published["runtimeRevision"].AsDouble == ack["runtimeRevision"].AsDouble,
                        "manual stage acknowledgement matches the published full runtime capability and revision: " + action);
                }
                CheckMissingPlans(main);
                NGUInjector.Main.CustomPlanStore.Save("edited", new CustomPlan { RebirthHistory = new[] { new CustomRebirthHistoryEntry { TimestampLocal = "2026-10-14 12:00:00", Reason = "live", BossId = 42 } } });
                NGUInjector.Main.Settings.CustomPlanFile = "edited";
                var invalidations = NGUInjector.Main.Invalidations;
                main.Process("plan", Envelope("plan", "savePlan", PlanArgs("edited", PlanWithNguWeights("{\"Version\":18,\"TimeTriggerSeconds\":99,\"RebirthHistory\":[{\"Reason\":\"stale\"}]}"))));
                var plan = NGUInjector.Main.CustomPlanStore.Load("edited");
                Assert(Result("plan")["success"].AsBool && plan.TimeTriggerSeconds == 99 && plan.RebirthHistory[0].Reason == "live" && plan.RebirthHistory[0].BossId == 42, "plan saves preserve live history");
                Assert(NGUInjector.Main.Invalidations > invalidations, "plan save invalidates runtime cache");
                invalidations = NGUInjector.Main.Invalidations;
                NGUInjector.Main.CustomPlanStore.Save("edited", NGUInjector.Main.CustomPlanStore.Load("edited"));
                Assert(NGUInjector.Main.Invalidations == invalidations, "unchanged selected-plan save preserves scheduler");
                NGUInjector.Main.CustomPlanStore.Save("unselected", new CustomPlan());
                Assert(NGUInjector.Main.Invalidations == invalidations, "unselected-plan save preserves scheduler");
                NGUInjector.Main.Settings.CustomPlanFile = "default";
                Assert(Result("plan")["data"]["revision"].Value == JsonFiles.ComputeRevision(File.ReadAllBytes(Path.Combine(_dir, "custom-plans", "edited.json"))), "savePlan returns the exact saved revision");
                main.Process("plan-new", Envelope("plan-new", "savePlan", PlanArgs("fresh", JsonFiles.ParseObject("{\"RebirthHistory\":[{\"Reason\":\"injected\"}]}"))));
                Assert(NGUInjector.Main.CustomPlanStore.Load("fresh").RebirthHistory.Length == 0, "new plan ignores supplied history");
                Reject(() => NGUInjector.Main.CustomPlanStore.SaveExternalPlan("../bad", new JSONObject(), ""), "unsafe plan name");

                main.Process("read-edited", Envelope("read-edited", "readPlan", JsonFiles.ParseObject("{\"name\":\"edited\"}")));
                var readPlan = Result("read-edited")["data"]["plan"];
                Assert(Result("read-edited")["success"].AsBool && readPlan["__externalUiRevision"].Value == Result("plan")["data"]["revision"].Value, "readPlan returns normalized plan and its revision metadata");
                var editedPath = Path.Combine(_dir, "custom-plans", "edited.json");
                var editedBytes = File.ReadAllBytes(editedPath);
                var absentRevision = PlanArgs("edited", readPlan.AsObject); absentRevision.Remove("expectedRevision");
                main.Process("cas-missing", Envelope("cas-missing", "savePlan", absentRevision));
                Assert(!Result("cas-missing")["success"].AsBool && editedBytes.SequenceEqual(File.ReadAllBytes(editedPath)), "existing save requires an explicit revision without mutation");
                var staleRevision = PlanArgs("edited", readPlan.AsObject); staleRevision["expectedRevision"] = new string('0', 64);
                main.Process("cas-stale", Envelope("cas-stale", "savePlan", staleRevision));
                Assert(!Result("cas-stale")["success"].AsBool && Result("cas-stale")["message"].Value.Contains("revision conflict") && editedBytes.SequenceEqual(File.ReadAllBytes(editedPath)), "stale revision fails without mutation");
                var emptyExistingRevision = PlanArgs("edited", readPlan.AsObject); emptyExistingRevision["expectedRevision"] = "";
                main.Process("cas-existing-empty", Envelope("cas-existing-empty", "savePlan", emptyExistingRevision));
                Assert(!Result("cas-existing-empty")["success"].AsBool, "nonexistent sentinel cannot overwrite an existing plan");
                var staleHistoryArgs = PlanArgs("edited", readPlan.AsObject);
                NGUInjector.Main.CustomPlanStore.AddRebirthHistory("edited", new CustomRebirthHistoryEntry { Reason = "new live history" });
                var historyBytes = File.ReadAllBytes(editedPath);
                main.Process("cas-history", Envelope("cas-history", "savePlan", staleHistoryArgs));
                Assert(!Result("cas-history")["success"].AsBool && historyBytes.SequenceEqual(File.ReadAllBytes(editedPath)), "history-only changes also invalidate a revision");
                main.Process("cas-refreshed", Envelope("cas-refreshed", "savePlan", PlanArgs("edited", readPlan.AsObject)));
                Assert(Result("cas-refreshed")["success"].AsBool && NGUInjector.Main.CustomPlanStore.Load("edited").RebirthHistory[0].Reason == "new live history", "refreshed revision still preserves live history");
                Assert(!JsonFiles.ParseObject(File.ReadAllText(editedPath)).Linq.Any(item => item.Key == "__externalUiRevision"), "revision metadata is stripped from saved plans");
                var absentArgs = PlanArgs("cas-absent", new JSONObject()); absentArgs["expectedRevision"] = Result("plan")["data"]["revision"];
                main.Process("cas-absent-conflict", Envelope("cas-absent-conflict", "savePlan", absentArgs));
                Assert(!Result("cas-absent-conflict")["success"].AsBool && !NGUInjector.Main.CustomPlanStore.PlanExists("cas-absent"), "nonempty expected revision cannot create a nonexistent plan");
                main.Process("cas-absent-create", Envelope("cas-absent-create", "savePlan", PlanArgs("cas-absent", new JSONObject())));
                Assert(Result("cas-absent-create")["success"].AsBool, "empty sentinel creates a nonexistent plan");
                var malformedToken = PlanArgs("edited", new JSONObject()); malformedToken["expectedRevision"] = "not-a-hash";
                main.Process("cas-malformed", Envelope("cas-malformed", "savePlan", malformedToken));
                Assert(!Result("cas-malformed")["success"].AsBool, "malformed revision token rejected");

                var legacyPath = Path.Combine(_dir, "custom-plans", "legacy-cas.json");
                File.WriteAllText(legacyPath, "{\"Version\":5,\"Name\":\"legacy-cas\",\"NguGoal\":1,\"NumberClimbEnabled\":false,\"AutoActivateFruits\":false,\"future\":9223372036854775807}", new UTF8Encoding(true));
                var legacyBytes = File.ReadAllBytes(legacyPath);
                main.Process("read-legacy", Envelope("read-legacy", "readPlan", JsonFiles.ParseObject("{\"name\":\"legacy-cas\"}")));
                var normalized = Result("read-legacy")["data"]["plan"].AsObject;
                Assert(Result("read-legacy")["success"].AsBool && normalized["Version"].AsInt == 18
                    && !normalized.Linq.Any(item => item.Key == "EnergyNguWeights" || item.Key == "MagicNguWeights" || item.Key == "NguGoal")
                    && !normalized["NumberClimbEnabled"].AsBool && normalized["AutoActivateFruits"].AsBool, "legacy migrations run without re-enabling the retired trigger");
                Assert(normalized["__externalUiRevision"].Value == JsonFiles.ComputeRevision(legacyBytes), "normalized read hashes original bytes including UTF8 BOM");
                Assert(legacyBytes.SequenceEqual(File.ReadAllBytes(legacyPath)), "normalized read does not write the legacy plan");
                Assert(normalized["future"].Value == "9223372036854775807", "normalized read preserves unsafe JS integer tokens");
                normalized["NumberClimbEnabled"] = false; normalized["AutoActivateFruits"] = false;
                main.Process("save-normalized", Envelope("save-normalized", "savePlan", PlanArgs("legacy-cas", normalized)));
                var savedLegacy = NGUInjector.Main.CustomPlanStore.Load("legacy-cas");
                Assert(Result("save-normalized")["success"].AsBool && !savedLegacy.NumberClimbEnabled && !savedLegacy.AutoActivateFruits,
                    "saving the ratio-free normalized edited baseline does not rerun old migrations");
                Assert(!File.ReadAllText(legacyPath).Contains("__externalUiRevision") && File.ReadAllText(legacyPath).Contains("9223372036854775807"), "normalized save strips metadata without losing large numeric tokens");
                var oldBaseline = JsonFiles.ParseObject("{\"Version\":5,\"NguGoal\":1}");
                main.Process("save-old-baseline", Envelope("save-old-baseline", "savePlan", PlanArgs("legacy-cas", oldBaseline)));
                Assert(!Result("save-old-baseline")["success"].AsBool && Result("save-old-baseline")["message"].Value.Contains("readPlan"), "existing plans reject unmigrated editor snapshots");
                main.Process("save-versionless", Envelope("save-versionless", "savePlan", PlanArgs("legacy-cas", new JSONObject())));
                Assert(!Result("save-versionless")["success"].AsBool && Result("save-versionless")["message"].Value.Contains("readPlan"), "existing plans require an explicit normalized version in the editor snapshot");

                var richRoot = JsonFiles.ParseObject("{\"Version\":14,\"Name\":\"rich\",\"futureRoot\":{\"integer\":9223372036854775807},\"Stages\":[{\"Name\":\"duplicate\",\"Percent\":50,\"futureRow\":\"A\"},{\"Name\":\"duplicate\",\"Percent\":50,\"futureRow\":\"B\"}],\"GoalPresets\":[{\"Name\":\"duplicate\",\"GearGoal\":9,\"futurePreset\":\"P\"},{\"Name\":\"duplicate\",\"GearGoal\":10,\"futurePreset\":\"Q\"}]}");
                main.Process("rich-save", Envelope("rich-save", "savePlan", PlanArgs("rich", richRoot)));
                var richPath = Path.Combine(_dir, "custom-plans", "rich.json");
                var richSaved = JsonFiles.ParseObject(File.ReadAllText(richPath));
                Assert(Result("rich-save")["success"].AsBool && richSaved["futureRoot"]["integer"].Value == "9223372036854775807", "external plan keeps unknown root data");
                Assert(richSaved["Stages"][0]["futureRow"].Value == "A" && richSaved["Stages"][1]["futureRow"].Value == "B", "external plan keeps per-stage data");
                Assert(richSaved["GoalPresets"][0]["futurePreset"].Value == "P" && richSaved["GoalPresets"][1]["futurePreset"].Value == "Q", "external plan keeps per-preset data");
                var richPlan = NGUInjector.Main.CustomPlanStore.Load("rich");
                richPlan.Stages = richPlan.Stages.Reverse().ToArray(); richPlan.GoalPresets = richPlan.GoalPresets.Reverse().ToArray();
                NGUInjector.Main.CustomPlanStore.Save("rich", richPlan);
                richSaved = JsonFiles.ParseObject(File.ReadAllText(richPath));
                Assert(richSaved["Stages"][0]["futureRow"].Value == "B" && richSaved["Stages"][1]["futureRow"].Value == "A", "stage metadata follows objects across duplicate-name reorder");
                Assert(richSaved["GoalPresets"][0]["futurePreset"].Value == "Q" && richSaved["GoalPresets"][1]["futurePreset"].Value == "P", "preset metadata follows objects across reorder");
                richPlan.Stages = new[] { richPlan.Stages[1] }; richPlan.GoalPresets = new[] { richPlan.GoalPresets[1] };
                NGUInjector.Main.CustomPlanStore.Save("rich", richPlan);
                richSaved = JsonFiles.ParseObject(File.ReadAllText(richPath));
                Assert(richSaved["Stages"].Count == 1 && richSaved["Stages"][0]["futureRow"].Value == "A", "stage deletion never merges the removed row by index");
                Assert(richSaved["GoalPresets"].Count == 1 && richSaved["GoalPresets"][0]["futurePreset"].Value == "P", "preset deletion never merges the removed row by index");
                NGUInjector.Main.CustomPlanStore.AddRebirthHistory("rich", new CustomRebirthHistoryEntry { Reason = "latest" });
                richSaved = JsonFiles.ParseObject(File.ReadAllText(richPath));
                Assert(richSaved["futureRoot"]["integer"].Value == "9223372036854775807" && richSaved["Stages"][0]["futureRow"].Value == "A" && richSaved["GoalPresets"][0]["futurePreset"].Value == "P", "AddRebirthHistory preserves root and row extensions");
                richSaved["RebirthHistory"][0]["futureHistory"] = "owned-by-history";
                File.WriteAllText(richPath, richSaved.ToString());
                var submission = JsonFiles.ParseObject(richSaved.ToString());
                var newRow = JsonFiles.ParseObject("{\"Name\":\"fresh row\",\"Percent\":80,\"futureRow\":\"C\"}");
                submission["Stages"] = new JSONArray(); submission["Stages"].Add(newRow);
                submission["GoalPresets"] = new JSONArray(); submission["GoalPresets"].Add(JsonFiles.ParseObject("{\"Name\":\"fresh preset\",\"futurePreset\":\"R\"}"));
                submission["RebirthHistory"] = new JSONArray();
                var beforeProvenanceEdit = File.ReadAllBytes(richPath);
                main.Process("rich-submit-provenance", Envelope("rich-submit-provenance", "savePlan", PlanArgs("rich", submission)));
                Assert(!Result("rich-submit-provenance")["success"].AsBool && beforeProvenanceEdit.SequenceEqual(File.ReadAllBytes(richPath)),
                    "external plan edits cannot rewrite immutable legacy preset provenance");
                submission["GoalPresets"] = richSaved["GoalPresets"];
                main.Process("rich-submit", Envelope("rich-submit", "savePlan", PlanArgs("rich", submission)));
                richSaved = JsonFiles.ParseObject(File.ReadAllText(richPath));
                Assert(Result("rich-submit")["success"].AsBool && richSaved["Stages"].Count == 1 && richSaved["Stages"][0]["futureRow"].Value == "C"
                    && richSaved["GoalPresets"][0]["futurePreset"].Value == "P", "submitted stage rows are authoritative while historical preset provenance remains unchanged");
                Assert(richSaved["RebirthHistory"][0]["futureHistory"].Value == "owned-by-history", "external save retains live history extensions");

                var copyArgs = new JSONObject(); copyArgs["requestedName"] = "rich-copy"; copyArgs["source"] = richSaved;
                main.Process("create-copy", Envelope("create-copy", "createPlan", copyArgs));
                Assert(Result("create-copy")["success"].AsBool && Result("create-copy")["data"]["name"].Value == "rich-copy", "createPlan returns its actual name");
                var copied = JsonFiles.ParseObject(File.ReadAllText(Path.Combine(_dir, "custom-plans", "rich-copy.json")));
                Assert(copied["futureRoot"].ToString() == richSaved["futureRoot"].ToString() && copied["Stages"][0]["futureRow"].Value == "C" && copied["GoalPresets"][0]["futurePreset"].Value == "P", "createPlan copies unknown root and row fields");
                Assert(copied["RebirthHistory"].Count == 0, "createPlan copies no history");
                var reservedSource = JsonFiles.ParseObject(copied.ToString()); reservedSource["__externalUiRevision"] = "transport-only";
                var reservedCreate = new JSONObject(); reservedCreate["requestedName"] = "reserved-copy"; reservedCreate["source"] = reservedSource;
                main.Process("copy-reserved", Envelope("copy-reserved", "createPlan", reservedCreate));
                Assert(Result("copy-reserved")["success"].AsBool && !File.ReadAllText(Path.Combine(_dir, "custom-plans", "reserved-copy.json")).Contains("__externalUiRevision"), "createPlan also strips reserved revision metadata");
                main.Process("create-collision", Envelope("create-collision", "createPlan", copyArgs));
                Assert(Result("create-collision")["data"]["name"].Value == "rich-copy-2", "createPlan resolves name collisions without overwriting");
                Assert(NGUInjector.Main.Settings.CustomPlanFile == "default", "createPlan does not change selected running state");
                main.Process("create-empty", Envelope("create-empty", "createPlan", JsonFiles.ParseObject("{\"requestedName\":\"brand-new\"}")));
                Assert(Result("create-empty")["success"].AsBool && NGUInjector.Main.CustomPlanStore.Load("brand-new").RebirthHistory.Length == 0, "createPlan without source creates defaults");
                main.Process("create-unsafe", Envelope("create-unsafe", "createPlan", JsonFiles.ParseObject("{\"requestedName\":\"../escape\"}")));
                Assert(!Result("create-unsafe")["success"].AsBool, "createPlan rejects unsafe names");
                main.Process("create-device", Envelope("create-device", "createPlan", JsonFiles.ParseObject("{\"requestedName\":\"CON.extra\"}")));
                Assert(!Result("create-device")["success"].AsBool, "createPlan rejects reserved Windows device names");
                main.Process("create-invalid-source", Envelope("create-invalid-source", "createPlan", JsonFiles.ParseObject("{\"requestedName\":\"bad-source\",\"source\":[]}")));
                Assert(!Result("create-invalid-source")["success"].AsBool && !NGUInjector.Main.CustomPlanStore.PlanExists("bad-source"), "createPlan rejects non-object source");
                var maximumName = new string('z',128);
                var fullName = NGUInjector.Main.CustomPlanStore.CreateExternalPlan(maximumName, null);
                var suffixedName = NGUInjector.Main.CustomPlanStore.CreateExternalPlan(maximumName, null);
                Assert(fullName.Length == 128 && suffixedName.Length == 128 && suffixedName.EndsWith("-2"), "collision suffix remains a saveable 128-character name");
                NGUInjector.Main.Settings.CustomPlanFile = "rich-copy"; NGUInjector.Main.Settings.UseCustomRebirthPlan = true;
                main.Process("delete-running", Envelope("delete-running", "deletePlan", JsonFiles.ParseObject("{\"name\":\"rich-copy\"}")));
                Assert(!Result("delete-running")["success"].AsBool && NGUInjector.Main.CustomPlanStore.PlanExists("rich-copy") && NGUInjector.Main.Settings.CustomPlanFile == "rich-copy", "deletePlan rejects the selected running plan");
                main.Process("delete-unselected", Envelope("delete-unselected", "deletePlan", JsonFiles.ParseObject("{\"name\":\"rich-copy-2\"}")));
                Assert(Result("delete-unselected")["success"].AsBool && !NGUInjector.Main.CustomPlanStore.PlanExists("rich-copy-2") && NGUInjector.Main.Settings.CustomPlanFile == "rich-copy", "deletePlan leaves other running plans selected");
                NGUInjector.Main.Settings.UseCustomRebirthPlan = false;
                using (var locked = new FileStream(settingsPath, FileMode.Open, FileAccess.Read, FileShare.None))
                    main.Process("delete-fallback-fails", Envelope("delete-fallback-fails", "deletePlan", JsonFiles.ParseObject("{\"name\":\"rich-copy\"}")));
                Assert(!Result("delete-fallback-fails")["success"].AsBool && NGUInjector.Main.CustomPlanStore.PlanExists("rich-copy") && NGUInjector.Main.Settings.CustomPlanFile == "rich-copy", "failed fallback persistence prevents selected-plan deletion");
                main.Process("delete-paused", Envelope("delete-paused", "deletePlan", JsonFiles.ParseObject("{\"name\":\"rich-copy\"}")));
                Assert(Result("delete-paused")["success"].AsBool && !NGUInjector.Main.CustomPlanStore.PlanExists("rich-copy") && NGUInjector.Main.Settings.CustomPlanFile == "24hr", "deletePlan moves paused selection to 24hr");
                Assert(Result("delete-paused")["data"]["selectedPlan"].Value == "24hr" && JsonFiles.ParseObject(File.ReadAllText(settingsPath))["_customPlanFile"].Value == "24hr", "deletePlan returns and persists its fallback selection");
                foreach (var builtin in PlanCatalogNames)
                {
                    var deleteArgs = new JSONObject(); deleteArgs["name"] = builtin;
                    main.Process("delete-" + builtin, Envelope("delete-" + builtin, "deletePlan", deleteArgs));
                    Assert(!Result("delete-" + builtin)["success"].AsBool && NGUInjector.Main.CustomPlanStore.PlanExists(builtin), "deletePlan protects builtin " + builtin);
                }
                main.Process("delete-missing", Envelope("delete-missing", "deletePlan", JsonFiles.ParseObject("{\"name\":\"not-there\"}")));
                Assert(!Result("delete-missing")["success"].AsBool, "deletePlan reports nonexistent plans");
                main.Process("delete-unsafe", Envelope("delete-unsafe", "deletePlan", JsonFiles.ParseObject("{\"name\":\"../bad\"}")));
                Assert(!Result("delete-unsafe")["success"].AsBool, "deletePlan rejects unsafe names");
                main.Process("focus", Envelope("focus", "focusGoal", JsonFiles.ParseObject("{\"goal\":\"TimeMachine\",\"mode\":\"apply\"}")));
                Assert(Result("focus")["success"].AsBool && NGUInjector.Main.Focuses == 1 && Result("focus")["message"].Value == "apply TimeMachine", "focus dispatch");
                Assert(Result("focus")["data"]["runtime"]["activeFocusGoal"].Value == "TimeMachine" && Result("focus")["data"]["runtime"]["appliedFocusGoal"].Value == "TimeMachine"
                    && Result("focus")["data"]["runtime"]["focusStatus"].Value == "applied", "focus result reflects actual applied action before heartbeat");
                main.Process("focus-bad", Envelope("focus-bad", "focusGoal", JsonFiles.ParseObject("{\"goal\":99}")));
                Assert(!Result("focus-bad")["success"].AsBool && NGUInjector.Main.Focuses == 1, "invalid enum rejected before dispatch");
                NGUInjector.Main.FocusFails = true;
                main.Process("focus-fails", Envelope("focus-fails", "focusGoal", JsonFiles.ParseObject("{\"goal\":\"Wandoos\",\"mode\":\"apply\"}")));
                Assert(!Result("focus-fails")["success"].AsBool, "returned LogError is a failed command result");
                Assert(Result("focus-fails")["data"]["runtime"]["focusStatus"].Value == "failed" && Result("focus-fails")["data"]["runtime"]["appliedFocusGoal"].Value == "", "failed focus is never reported applied");
                NGUInjector.Main.FocusFails = false;
                main.Process("focus-stop", Envelope("focus-stop", "stopFocus"));
                Assert(Result("focus-stop")["data"]["runtime"]["activeFocusGoal"].Value == "" && Result("focus-stop")["data"]["runtime"]["focusStatus"].Value == "idle", "stop focus feedback immediately clears requested goal");
                CheckRuntimeRevisions(main);
                main.Process("preview", Envelope("preview", "previewGear", JsonFiles.ParseObject("{\"goal\":9,\"fightScoreMode\":\"PowerTimesToughness\"}")));
                Assert(Result("preview")["success"].AsBool && Result("preview")["data"]["itemIds"].Count == 2 && NGUInjector.Main.Equips == 0 && NGUInjector.Main.Previews == 1, "gear preview only calls read boundary");
                var gearMetrics = Result("preview")["data"];
                Assert(gearMetrics["evaluatedCombinations"].AsInt == 2 && gearMetrics["visitedNodes"].AsInt == 3
                    && gearMetrics["prunedBranches"].AsInt == 4 && gearMetrics["elapsedMilliseconds"].AsInt == 5,
                    "gear preview preserves complete scores, visited nodes, prunes and elapsed milliseconds");
                main.Process("preview-numeric-token", Envelope("preview-numeric-token", "previewGear", JsonFiles.ParseObject("{\"goal\":9.0,\"fightScoreMode\":1e0}")));
                Assert(Result("preview-numeric-token")["success"].AsBool && NGUInjector.Main.Equips == 0, "retained numeric tokens still decode integer enum arguments");
                epoch = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var timestampArgs = "{\"command\":\"status\",\"createdAt\":" + epoch + ".0}";
                main.Process("numeric-time", timestampArgs);
                Assert(Result("numeric-time")["success"].AsBool, "retained numeric tokens still decode integer timestamps");
                Assert(Result("numeric-time")["data"]["runtime"]["updatedAt"].IsString && Result("numeric-time")["data"]["runtime"]["settings"].IsObject, "status returns immediate pull runtime");
                var statusAck = Result("numeric-time")["data"]["runtime"];
                Assert(statusAck["startedAt"].Value == compactAck["startedAt"].Value && statusAck["externalUiEnabled"].AsBool
                    && statusAck["protocolVersion"].AsInt == 3 && statusAck["planVersion"].AsInt == 18 && statusAck["stageTimingVersion"].AsInt == 1
                    && statusAck["fightScheduleVersion"].AsInt == 1, "status compact pull retains session and protocol metadata");
                NGUInjector.Main.Settings.CustomAugmentsBloodEstimateSeconds = 450;
                main.Process("augment-preview", Envelope("augment-preview", "previewAugments"));
                var augmentData = Result("augment-preview")["data"];
                Assert(augmentData["result"]["augmentIndex"].AsInt == 2 && augmentData["runtime"]["status"].Value == "running", "result metrics survive additive runtime envelope");
                Assert(Result("augment-preview")["success"].AsBool && Managers.AugmentOptimizer.Previews == 1 && Managers.AugmentOptimizer.Allocations == 0 && augmentData["expectedSeconds"].AsInt == 450, "previewAugments uses estimate settings without allocating");
                Assert(augmentData["augmentIndex"].AsInt == 2 && augmentData["usesUpgrade"].AsBool && augmentData["energyToAugment"].AsInt == 100 && augmentData["energyToUpgrade"].AsInt == 200, "augment result identifies target and energy split");
                Assert(augmentData["energyPower"].AsInt == 9 && augmentData["augmentSpeedMultiplier"].AsInt == 10 && augmentData["projectedWork"].AsInt == 11 && augmentData["expectedValue"].AsInt == 5 && augmentData["valuePerSecond"].AsInt == 4, "augment result supplies old work/energy preview fields");
                Assert(augmentData["projectedGoldCost"].AsInt == 6 && augmentData["projectedGoldBudget"].AsInt == 7 && augmentData["goldPerSecond"].AsInt == 8, "augment result supplies old gold preview fields");
                Assert(Result("augment-preview")["message"].Value.Contains("Energy: aug 100, upgrade 200") && Result("augment-preview")["message"].Value.Contains("Expected work: 11 over 450s") && Result("augment-preview")["message"].Value.Contains("net GPS 8"), "augment message matches legacy preview formatting");
                main.Process("augment-allocate", Envelope("augment-allocate", "allocateAugments", JsonFiles.ParseObject("{\"expectedSeconds\":600}")));
                Assert(Result("augment-allocate")["success"].AsBool && Managers.AugmentOptimizer.Allocations == 1 && Managers.AugmentOptimizer.LastExpectedSeconds == 600, "allocateAugments dispatches allocation with explicit estimate");
                main.Process("augment-int32", Envelope("augment-int32", "previewAugments", JsonFiles.ParseObject("{\"expectedSeconds\":2147483647}")));
                Assert(Result("augment-int32")["success"].AsBool && Managers.AugmentOptimizer.LastExpectedSeconds == int.MaxValue, "command override remains positive Int32, independent of saved estimate limit");
                foreach (var invalidSeconds in new[] { "0", "-1", "1.5", "2147483648", "\"300\"" })
                {
                    var id = "bad-seconds-" + _assertions;
                    main.Process(id, Envelope(id, "allocateAugments", JsonFiles.ParseObject("{\"expectedSeconds\":" + invalidSeconds + "}")));
                    Assert(!Result(id)["success"].AsBool && Managers.AugmentOptimizer.Allocations == 1, "invalid estimate rejected before allocation " + invalidSeconds);
                }
                Managers.AugmentOptimizer.Fail = true;
                main.Process("augment-fail", Envelope("augment-fail", "previewAugments"));
                Assert(!Result("augment-fail")["success"].AsBool, "optimizer rejection reports command failure");
                Managers.AugmentOptimizer.Fail = false;
                for (var i = 0; i < 10; i++) File.WriteAllText(Path.Combine(_dir, "commands", "bounded" + i + ".json"), Envelope("bounded" + i, "quickSave"));
                var saves = NGUInjector.Main.Saves; main.Pump();
                Assert(NGUInjector.Main.Saves > saves && NGUInjector.Main.Saves <= saves + 4 && Directory.GetFiles(Path.Combine(_dir, "commands"), "*.json").Length >= 6, "bounded command pump");
                foreach (var file in Directory.GetFiles(Path.Combine(_dir, "commands"), "*.json")) File.Delete(file);
                var disposalChecks = 0;
                CheckExecutionAcceptance(main);
                main.Process("push-before-unload", Envelope("push-before-unload", "startMaxItopodPush", JsonFiles.ParseObject("{\"deaths\":3}")));
                Assert(Result("push-before-unload")["success"].AsBool, "push active before real lifecycle unload");
                var pushCancelsBeforeUnload = main.PushCancelCalls;
                var previousCombatCharacter = NGUInjector.Main.Character;
                NGUInjector.Main.Character = new FakeCharacter { adventure = new FakeAdventure { zone = 1000 } };
                main._combatPrepRun = new NGUInjector.Main.CombatPrepBoundaryRun { AdventureRouteOwned = true, Zone = 1000 };
                main.CombatPrepRetirementFails = true;
                Reject(() => main.Unload(), "pending combat route retirement refuses real lifecycle unload");
                Assert(NGUInjector.Main.reference == main && main.LeaseHeld && main.CancelCalls == 0 && main.CoroutineStops == 0,
                    "combat retirement refusal retains current runtime authority and lease");
                Assert(main.PushCancelCalls == pushCancelsBeforeUnload, "combat retirement refusal precedes unrelated cleanup/disposal");
                NGUInjector.Main.Character = previousCombatCharacter;
                main.CombatPrepRetirementFails = false;
                main._manualTitanActive = main.ManualTitanCleanupFails = true;
                Reject(() => main.Unload(), "pending exact titan recovery refuses unload");
                Assert(NGUInjector.Main.reference == main && main.LeaseHeld && main.CancelCalls == 0 && main.CoroutineStops == 0,
                    "refused unload retains runtime lease and cleanup timers");
                Assert(main.PushCancelCalls == pushCancelsBeforeUnload, "refused unload does not tear down other runtime state");
                main.ManualTitanCleanupFails = false;
                // Native retirement is exercised by the route fixture, not this
                // transport boundary. Disposal may proceed only after it is observed.
                main._manualTitanActive = false;
                main._combatPrepRun = null;
                NGUInjector.Main.settingsForm = new FakeForm { OnDispose = () => {
                    Assert(main.PushCancelCalls == pushCancelsBeforeUnload && main.PushRuntime["active"].AsBool,
                        "lifecycle disposal cannot call an ordinary push cleanup writer after quiescence");
                    Reject(() => { using (var lease = JsonFiles.AcquireInjectorLease(_dir, 0)) { } }, "lease is held during resource disposal");
                    disposalChecks++; throw new IOException("simulated form disposal failure");
                } };
                var shutdownWriter = new StreamWriter(Path.Combine(_dir, "shutdown-log.txt")); NGUInjector.Main.OutputWriter = shutdownWriter;
                File.WriteAllText(Path.Combine(_dir, "commands", "unload.json"), Envelope("unload", "unload"));
                main.PumpRuntime();
                var runtime = File.ReadAllText(Path.Combine(_dir, "runtime.json"));
                Assert(JsonFiles.ParseObject(runtime)["status"].Value == "unloaded", "unload status snapshot");
                Assert(JsonFiles.ParseObject(runtime)["maxItopodPush"].ToString() == main.PushRuntime.ToString(),
                    "terminal full runtime retains unresolved push evidence instead of fabricating cancellation");
                Assert(Result("unload")["data"]["runtime"]["maxItopodPush"].ToString() == JsonFiles.ParseObject(runtime)["maxItopodPush"].ToString(),
                    "terminal acknowledgement carries the cancelled push state");
                Assert(Result("unload")["data"]["runtime"]["runtimeRevision"].AsDouble == JsonFiles.ParseObject(runtime)["runtimeRevision"].AsDouble,
                    "terminal command result reuses the actual final publication revision");
                Assert(disposalChecks == 1 && NGUInjector.Main.reference == null && !main.LeaseHeld && main.CancelCalls == 1 && main.CoroutineStops == 1, "Unload clears ownership and cancels work after partial resource failures");
                Reject(() => shutdownWriter.WriteLine("after shutdown"), "Unload continues disposing logs after another resource throws");
                using (var offlineLease = JsonFiles.AcquireInjectorLease(_dir, 0)) Assert(offlineLease.CanWrite, "offline editing can acquire the lease after Unload completes");
                main.Unload(); Assert(main.CancelCalls == 1, "Unload is idempotent");
                Assert(main.PushCancelCalls == pushCancelsBeforeUnload, "idempotent unload never invokes a hidden push cleanup");
                Thread.Sleep(10); main.PumpRuntime();
                Assert(File.ReadAllText(Path.Combine(_dir, "runtime.json")) == runtime, "no snapshots after unload");
                var protocolMetadata = JsonFiles.ParseObject(runtime);
                var advertisedCommands = protocolMetadata["supportedCommands"].Children.Select(command => command.Value).ToArray();
                Assert(protocolMetadata["protocolVersion"].AsInt == 3 && protocolMetadata["supportedCommands"].IsArray
                    && advertisedCommands.Distinct(StringComparer.Ordinal).Count() == advertisedCommands.Length, "protocol metadata has unique command names");
                foreach (var advertised in new[] {
                    "applySettingsPatch", "savePlan", "focusGoal", "stopFocus", "optimizeGear", "previewGear",
                    "createPlan", "deletePlan", "readPlan", "previewAugments", "allocateAugments",
                    "manualFightPrep", "manualRebirth", "safeProbe", "probeCommands", "precacheLoadouts",
                    "resetRebirthPrep", "reloadSettings", "quickSave", "status", "unload", "setPlanStage",
                    "startMaxItopodPush", "cancelMaxItopodPush", "previewCombatPrep", "startCombatPrep", "cancelCombatPrep",
                    "previewLoadoutPreset", "applyLoadoutPreset" })
                    Assert(advertisedCommands.Contains(advertised), "advertised command " + advertised);
                Assert(JsonFiles.ParseObject(runtime)["planVersion"].AsInt == 18 && JsonFiles.ParseObject(runtime)["stageTimingVersion"].AsInt == 1
                    && JsonFiles.ParseObject(runtime)["fightScheduleVersion"].AsInt == 1
                    && !JsonFiles.ParseObject(runtime).Linq.Any(item => item.Key == "protectedStageTimingV1")
                    && JsonFiles.ParseObject(runtime)["planRevisionField"].Value == "__externalUiRevision" && JsonFiles.ParseObject(runtime)["planRevisionAlgorithm"].Value == "sha256", "runtime advertises revision and universal timing contract without obsolete capability");

                Loader.Unload(); Loader.Init(); var partialLoad = UnityEngine.GameObject.Last;
                var createdCount = UnityEngine.GameObject.Created; Loader.Init();
                Assert(UnityEngine.GameObject.Created == createdCount, "Loader prevents duplicate initialization before Start");
                Loader.Unload(); Loader.Unload();
                Assert(partialLoad.Destroyed && !partialLoad.Active && NGUInjector.Main.reference == null, "Loader.Unload safely removes an instance that never started");
                Loader.Init(); var failureLoad = UnityEngine.GameObject.Last;
                var failureMain = (NGUInjector.Main)failureLoad.Component; failureMain.Initialize(_dir, false);
                failureMain._manualTitanActive = failureMain.ManualTitanCleanupFails = true;
                Reject(() => Loader.Unload(), "loader respects pending titan recovery refusal");
                Assert(!failureLoad.Destroyed && failureLoad.Active && NGUInjector.Main.reference == failureMain && failureMain.LeaseHeld,
                    "loader retains cleanup component after refused unload");
                createdCount = UnityEngine.GameObject.Created;
                Loader.Init();
                Assert(UnityEngine.GameObject.Created == createdCount, "refused unload does not allow duplicate injection");
                failureMain.ManualTitanCleanupFails = false;
                failureMain._manualTitanActive = false;
                var reinitialized = JsonFiles.ParseObject(File.ReadAllText(Path.Combine(_dir, "runtime.json")));
                Assert(reinitialized["startedAt"].Value == JsonFiles.ParseObject(runtime)["startedAt"].Value
                    && reinitialized["runtimeRevision"].AsDouble > JsonFiles.ParseObject(runtime)["runtimeRevision"].AsDouble,
                    "same-assembly initialization preserves revision ordering within its retained startedAt session");
                var failureSettingsBytes = File.ReadAllBytes(settingsPath);
                failureMain.StartupFailure();
                var failedRuntime = File.ReadAllText(Path.Combine(_dir, "runtime.json"));
                Assert(JsonFiles.ParseObject(failedRuntime)["status"].Value == "failed" && JsonFiles.ParseObject(failedRuntime)["startupError"].Value == "simulated startup failure", "failed Start publishes a terminal failed status and reason");
                Assert(NGUInjector.Main.reference == null && failureLoad.Destroyed && !failureLoad.Active && !failureMain.LeaseHeld && failureMain.CancelCalls == 1 && failureMain.CoroutineStops == 1, "failed Start cleans its reference, object, lease, and scheduled work");
                failureMain.PumpRuntime(); Assert(File.ReadAllText(Path.Combine(_dir, "runtime.json")) == failedRuntime, "failed Start does not resume snapshots");
                Assert(failureSettingsBytes.SequenceEqual(File.ReadAllBytes(settingsPath)), "failure cleanup does not overwrite settings");
                using (var offlineLease = JsonFiles.AcquireInjectorLease(_dir, 0))
                {
                    Loader.Init(); var blockedLoad = UnityEngine.GameObject.Last;
                    var blockedMain = (NGUInjector.Main)blockedLoad.Component;
                    Reject(() => blockedMain.Initialize(_dir, false), "offline editor lease blocks startup before any settings access");
                    blockedMain.StartupFailure();
                    Assert(NGUInjector.Main.reference == null && blockedLoad.Destroyed && !blockedMain.LeaseHeld, "lease-blocked startup leaves no reference or object");
                    Assert(File.ReadAllText(Path.Combine(_dir, "runtime.json")) == failedRuntime && failureSettingsBytes.SequenceEqual(File.ReadAllBytes(settingsPath)), "lease-blocked startup does not overwrite shared state");
                }
            }
            catch (Exception error) { Console.Error.WriteLine(error); exitCode = 1; }
            finally
            {
                try { Loader.Unload(); NGUInjector.Main.reference?.Unload(); }
                catch (Exception error) { Console.Error.WriteLine("Cleanup failed: " + error); exitCode = 1; }
                try { if (_dir != null && Directory.Exists(_dir)) Directory.Delete(_dir, true); }
                catch (Exception error) { Console.Error.WriteLine("Temporary directory cleanup failed: " + error); exitCode = 1; }
                Environment.SetEnvironmentVariable("NGUINJECTOR_EXTERNAL_UI", originalExternalUi);
                Thread.CurrentThread.CurrentCulture = originalCulture;
            }
            if (exitCode == 0)
                Console.WriteLine("PASS: " + _assertions + " assertions against actual external UI sources; Unity/action boundaries stubbed.");
            return exitCode;
        }

        private static void CheckSettingsAudit(NGUInjector.Main main, string path)
        {
            var settings = NGUInjector.Main.Settings;
            var invalidations = NGUInjector.Main.Invalidations;
            main.Process("audit-tuning", Envelope("audit-tuning", "applySettingsPatch", JsonFiles.ParseObject(
                "{\"patch\":{\"_customIronPillSaveSeconds\":1234,\"_customIronPillMinWorth\":567.5,\"_customAutoBoostEnabled\":false}}")));
            Assert(Result("audit-tuning")["success"].AsBool && NGUInjector.Main.Invalidations == invalidations, "ordinary tuning patch does not invalidate sequential runtime");
            main.Process("audit-reload", Envelope("audit-reload", "reloadSettings"));
            Assert(Result("audit-reload")["success"].AsBool && NGUInjector.Main.Invalidations == invalidations, "unchanged settings reload does not invalidate sequential runtime");
            var changed = JsonFiles.ParseObject(File.ReadAllText(path)); changed["_customIronPillSaveSeconds"] = 2345;
            File.WriteAllText(path, changed.ToString());
            main.Process("audit-reload-tuning", Envelope("audit-reload-tuning", "reloadSettings"));
            Assert(Result("audit-reload-tuning")["success"].AsBool && settings.CustomIronPillSaveSeconds == 2345 && NGUInjector.Main.Invalidations == invalidations, "disk tuning reload preserves sequential runtime");
            main.Process("audit-noop-run", Envelope("audit-noop-run", "applySettingsPatch", JsonFiles.ParseObject("{\"patch\":{\"_customPlanFile\":\"default\",\"_useCustomRebirthPlan\":false,\"_autoRebirth\":true}}")));
            Assert(Result("audit-noop-run")["success"].AsBool && NGUInjector.Main.Invalidations == invalidations, "unchanged plan/run fields do not invalidate runtime");
            main.Process("audit-start", Envelope("audit-start", "applySettingsPatch", JsonFiles.ParseObject("{\"patch\":{\"_useCustomRebirthPlan\":true}}")));
            Assert(Result("audit-start")["success"].AsBool && NGUInjector.Main.Invalidations == invalidations + 1, "run-state patch invalidates exactly once, not for its candidate");
            changed = JsonFiles.ParseObject(File.ReadAllText(path)); changed["_useCustomRebirthPlan"] = false;
            File.WriteAllText(path, changed.ToString());
            main.Process("audit-stop-reload", Envelope("audit-stop-reload", "reloadSettings"));
            Assert(Result("audit-stop-reload")["success"].AsBool && NGUInjector.Main.Invalidations == invalidations + 2, "run-state reload invalidates exactly once");

            foreach (var key in new[] { "_customAutoBoostCubePowerRatio", "_customAugmentsBloodEstimateSeconds", "_customBloodSpaghettiGoalPercent", "_customBloodCounterfeitGoalPercent" })
            {
                var min = key.Contains("Goal") ? 0 : 1;
                var max = key.Contains("Cube") ? 4 : 100000;
                foreach (var value in new[] { min - 1, max + 1 })
                {
                    var before = File.ReadAllText(path);
                    var patch = new JSONObject(); patch[key] = value;
                    Reject(() => settings.ApplyPatch(patch), "out-of-range saved setting " + key + "=" + value);
                    Assert(File.ReadAllText(path) == before, "range rejection is atomic for " + key);
                }
                foreach (var value in new[] { min, max })
                {
                    var patch = new JSONObject(); patch[key] = value; settings.ApplyPatch(patch);
                    Assert(JsonFiles.ParseObject(File.ReadAllText(path))[key].AsInt == value, "inclusive saved-setting bound " + key + "=" + value);
                }
            }
            settings.CustomAutoBoostCubePowerRatio = int.MaxValue;
            settings.CustomAugmentsBloodEstimateSeconds = int.MaxValue;
            settings.CustomBloodSpaghettiGoalPercent = int.MaxValue;
            settings.CustomBloodCounterfeitGoalPercent = -1;
            var nativeSaved = JsonFiles.ParseObject(File.ReadAllText(path));
            Assert(nativeSaved["_customAutoBoostCubePowerRatio"].AsInt == 4 && nativeSaved["_customAugmentsBloodEstimateSeconds"].AsInt == 100000,
                "native setters persist bounded cube ratio and estimate");
            Assert(nativeSaved["_customBloodSpaghettiGoalPercent"].AsInt == 100000 && nativeSaved["_customBloodCounterfeitGoalPercent"].AsInt == 0,
                "native setters persist bounded blood goals");
            var legacySettings = JsonFiles.ParseObject(nativeSaved.ToString());
            legacySettings["_customAutoBoostCubePowerRatio"] = 99; legacySettings["_customAugmentsBloodEstimateSeconds"] = 100001;
            legacySettings["_customBloodSpaghettiGoalPercent"] = 100001; legacySettings["_customBloodCounterfeitGoalPercent"] = 100001;
            File.WriteAllText(path, legacySettings.ToString());
            Assert(settings.LoadSettings() && settings.CustomAutoBoostCubePowerRatio == 4 && settings.CustomAugmentsBloodEstimateSeconds == 100000
                && settings.CustomBloodSpaghettiGoalPercent == 100000 && settings.CustomBloodCounterfeitGoalPercent == 100000, "legacy out-of-range loads normalize to supported upper bounds");

            for (var i = 1; i <= 12; i++)
                settings.AddIronPillHistory(new IronPillHistoryEntry {
                    CastAtUtc = "2026-10-14T12:00:" + i.ToString("00") + "Z", WaitSeconds = i * 60.25, Worth = i * 10.5, RebirthSeconds = 3600 + i });
            var loaded = new SavedSettings(_dir);
            Assert(loaded.LoadSettings() && loaded.CustomIronPillSaveSeconds == 2345 && loaded.CustomIronPillMinWorth == 567.5, "Iron Pill thresholds survive disk save/load");
            Assert(loaded.CustomIronPillHistory.Length == 10 && loaded.CustomIronPillHistory[0].Worth == 126 && loaded.CustomIronPillHistory[9].Worth == 31.5, "disk history retains ten newest casts in order");
            var newest = loaded.CustomIronPillHistory[0];
            Assert(newest.CastAtUtc == "2026-10-14T12:00:12Z" && newest.WaitSeconds == 723 && newest.RebirthSeconds == 3612, "history timestamp and elapsed fields survive disk round trip");
            settings.ApplyPatch(JsonFiles.ParseObject("{\"_customAutoBoostEnabled\":true}"));
            Assert(new SavedSettings(_dir).LoadSettings(), "settings remain loadable after history-preserving patch");
            var historyReload = new SavedSettings(_dir); historyReload.LoadSettings();
            Assert(historyReload.CustomIronPillHistory.Length == 10 && historyReload.CustomIronPillHistory[0].Worth == 126, "ordinary patch preserves persisted backend-owned history");
            Reject(() => settings.ApplyPatch(JsonFiles.ParseObject("{\"_customIronPillHistory\":[]}")), "external settings cannot replace Iron Pill history");
            var historyBytes = File.ReadAllText(path);
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                Reject(() => settings.ApplyPatch(JsonFiles.ParseObject("{\"_customIronPillMinWorth\":999}")), "failed threshold persistence reports error");
            Assert(settings.CustomIronPillMinWorth == 567.5 && File.ReadAllText(path) == historyBytes, "failed threshold patch retains live and saved history/settings");
        }

        private static void CheckRuntimeRevisions(NGUInjector.Main main)
        {
            main.Process("revision-status", Envelope("revision-status", "status"));
            var ack = Result("revision-status")["data"]["runtime"];
            var timestamp = DateTimeOffset.Parse(ack["updatedAt"].Value, CultureInfo.InvariantCulture).LocalDateTime;
            var beforeBuild = main.RuntimeRevision;
            main.BuildRuntime(); main.BuildRuntime();
            Assert(main.RuntimeRevision == beforeBuild, "read-only runtime builds consume no publication revision");

            main.ApplyCustomFocusGoal(CustomFocusGoal.EnergyNgu);
            var first = main.PublishRuntimeAt(timestamp);
            main.ApplyCustomFocusGoal(CustomFocusGoal.MagicNgu);
            var second = main.PublishRuntimeAt(timestamp);
            Assert(first["updatedAt"].Value == second["updatedAt"].Value, "two immediate publications can have identical wall-clock timestamps");
            Assert(DateTimeOffset.Parse(second["updatedAt"].Value, CultureInfo.InvariantCulture).ToUnixTimeMilliseconds()
                == DateTimeOffset.Parse(ack["updatedAt"].Value, CultureInfo.InvariantCulture).ToUnixTimeMilliseconds(),
                "later applied state can share the command acknowledgement's exact JavaScript millisecond");
            Assert(first["runtimeRevision"].AsDouble == ack["runtimeRevision"].AsDouble + 1
                && second["runtimeRevision"].AsDouble == first["runtimeRevision"].AsDouble + 1,
                "every immediate state publication advances independently of timestamp resolution");
            Assert(first["startedAt"].Value == ack["startedAt"].Value && second["startedAt"].Value == ack["startedAt"].Value,
                "publication revisions retain explicit same-session context");
            Assert(first["activeFocusGoal"].Value == "EnergyNgu" && first["appliedFocusGoal"].Value == "EnergyNgu"
                && second["activeFocusGoal"].Value == "MagicNgu" && second["appliedFocusGoal"].Value == "MagicNgu",
                "revision publication captures actual applied action state rather than optimistic goal overrides");
            var full = JsonFiles.ParseObject(File.ReadAllText(Path.Combine(_dir, "runtime.json")));
            Assert(full["runtimeRevision"].AsDouble == second["runtimeRevision"].AsDouble && full["appliedFocusGoal"].Value == "MagicNgu",
                "full runtime file carries the latest immediate publication revision and action state");
            Assert(full["runtimeRevision"].IsNumber && full["protocolVersion"].AsInt == 3 && full["supportedCommands"].IsArray,
                "immediate publication preserves full metadata with an additive numeric revision");
            main.StopCustomFocusGoal();
            main.SetReady(true);
            var heartbeat = JsonFiles.ParseObject(File.ReadAllText(Path.Combine(_dir, "runtime.json")));
            Assert(heartbeat["runtimeRevision"].AsDouble == second["runtimeRevision"].AsDouble + 1
                && heartbeat["startedAt"].Value == second["startedAt"].Value, "regular heartbeat advances the same publication sequence");
        }

        private static void CheckBloodMetadata(NGUInjector.Main main)
        {
            var unavailable = main.BloodState();
            Assert(!unavailable["bloodGoals"]["available"].AsBool && unavailable["bloodGoals"]["status"].Value == "unavailable"
                && unavailable["bloodGoals"]["reason"].Value.Contains("game state") && unavailable["bloodGoals"]["spaghetti"].IsNull
                && unavailable["bloodGoals"]["counterfeit"].IsNull, "blood metadata explicitly represents unavailable state");
            Assert(!unavailable["ironPill"]["available"].AsBool && unavailable["ironPill"]["eligible"].IsNull
                && unavailable["ironPill"]["status"].Value == "unavailable", "Iron Pill eligibility is unknown, not false, without game state");
            main.SetReady(true);
            var unavailableSnapshot = JsonFiles.ParseObject(File.ReadAllText(Path.Combine(_dir, "runtime.json")));
            Assert(unavailableSnapshot["ironPill"].ToString() == unavailable["ironPill"].ToString(),
                "runtime.json parses with unavailable Iron Pill game state");
            var settings = NGUInjector.Main.Settings;
            var managedMagic = settings.ManageMagic;
            NGUInjector.Main.CustomPlanStore.Save("eligibility", new CustomPlan { AutoRebirthEnabled = true });
            settings.CustomPlanFile = "eligibility"; settings.UseCustomRebirthPlan = true;
            settings.AutoRebirth = true; settings.CustomAutoBloodSpellsEnabled = true;
            settings.ManageMagic = true;
            NGUInjector.Main.Character = new FakeCharacter {
                rebirthTime = new FakeTime { totalseconds = 3600 }, bossID = 100,
                bloodMagic = new FakeBlood {
                    bloodPoints = 1234567.5, lootAutoSpell = true, goldAutoSpell = false, rebirthAutoSpell = true,
                    ritual = new System.Collections.Generic.List<FakeRitual> {
                        new FakeRitual { magic = 0 }, new FakeRitual { magic = 9007199254740993L }, new FakeRitual { magic = long.MaxValue } } },
                bloodMagicController = new FakeBloodController(), bloodSpells = new FakeBloodSpells { MinimumAdventureBlood = 765.5 } };
            main.IronPillCooldownSeconds = 123.75;
            main.IronPillWorth = 42.125;
            var effects = LoadoutPresetBridgeEffects(main);
            var available = main.BloodState();
            Assert(available["bloodGoals"]["available"].AsBool && available["bloodGoals"]["status"].Value == "available"
                && available["bloodGoals"]["reason"].Value == "" && available["bloodGoals"]["spaghetti"].IsObject && available["bloodGoals"]["counterfeit"].IsObject,
                "available blood metadata retains both goal objects");
            Assert(available["ironPill"]["available"].AsBool && available["ironPill"]["eligible"].AsBool
                && available["ironPill"]["status"].Value == "eligible" && available["ironPill"]["reason"].Value == "", "eligible metadata uses the actual reservation predicate");
            main.SetReady(true);
            var snapshot = JsonFiles.ParseObject(File.ReadAllText(Path.Combine(_dir, "runtime.json")));
            Assert(snapshot["bloodGoals"].ToString() == available["bloodGoals"].ToString(), "runtime.json publishes the exact blood goal metadata contract");
            Assert(snapshot["ironPill"].ToString() == available["ironPill"].ToString(), "runtime.json publishes the exact Iron Pill eligibility contract");
            var telemetry = snapshot["ironPill"];
            Assert(telemetry["cooldownRemainingSeconds"].IsNumber && telemetry["cooldownRemainingSeconds"].AsDouble == 123.75,
                "runtime.json parses a finite fractional Iron Pill cooldown");
            Assert(telemetry["bloodPoints"].IsNumber && telemetry["bloodPoints"].AsDouble == 1234567.5
                && telemetry["currentWorth"].IsNumber && telemetry["currentWorth"].AsDouble == 42.125
                && telemetry["minimumBlood"].IsNumber && telemetry["minimumBlood"].AsDouble == 765.5,
                "Iron Pill telemetry forwards actual blood, worth and minimum-blood boundary values");
            Assert(telemetry["ritualMagic"].IsArray && telemetry["ritualMagic"].Count == 3
                && telemetry["ritualMagic"].Children.All(value => value.IsString)
                && telemetry["ritualMagic"].ToString() == "[\"0\",\"9007199254740993\",\"9223372036854775807\"]",
                "actual ritual magic is serialized in order as exact long strings, including values beyond JavaScript precision");
            Assert(telemetry["lootAutoSpell"].IsBoolean && telemetry["lootAutoSpell"].AsBool
                && telemetry["goldAutoSpell"].IsBoolean && !telemetry["goldAutoSpell"].AsBool
                && telemetry["numberAutoSpell"].IsBoolean && telemetry["numberAutoSpell"].AsBool,
                "Iron Pill telemetry exposes actual loot, gold and rebirth/number auto-spell toggles");
            foreach (var cooldown in new[] { double.PositiveInfinity, double.NegativeInfinity, double.NaN, 0d })
            {
                main.IronPillCooldownSeconds = cooldown;
                var revision = main.RuntimeRevision;
                main.SetReady(true);
                var cooldownSnapshot = JsonFiles.ParseObject(File.ReadAllText(Path.Combine(_dir, "runtime.json")));
                Assert(cooldownSnapshot["runtimeRevision"].AsDouble > revision && cooldownSnapshot["ironPill"]["status"].Value == "eligible",
                    "runtime.json publishes and parses fresh Iron Pill telemetry with cooldown " + cooldown);
                Assert(double.IsFinite(cooldown)
                    ? cooldownSnapshot["ironPill"]["cooldownRemainingSeconds"].IsNumber && cooldownSnapshot["ironPill"]["cooldownRemainingSeconds"].AsDouble == cooldown
                    : cooldownSnapshot["ironPill"].Linq.Any(item => item.Key == "cooldownRemainingSeconds" && item.Value.IsNull),
                    "unavailable/non-finite cooldown is explicit JSON null while ready cooldown remains numeric: " + cooldown);
            }
            main.IronPillCooldownSeconds = 123.75;
            var blood = NGUInjector.Main.Character.bloodMagic;
            blood.lootAutoSpell = false; blood.goldAutoSpell = true; blood.rebirthAutoSpell = false;
            main.SetReady(true);
            telemetry = JsonFiles.ParseObject(File.ReadAllText(Path.Combine(_dir, "runtime.json")))["ironPill"];
            Assert(telemetry["lootAutoSpell"].IsBoolean && !telemetry["lootAutoSpell"].AsBool
                && telemetry["goldAutoSpell"].IsBoolean && telemetry["goldAutoSpell"].AsBool
                && telemetry["numberAutoSpell"].IsBoolean && !telemetry["numberAutoSpell"].AsBool,
                "runtime telemetry follows changed game toggle values instead of configured automation policy");
            blood.lootAutoSpell = true; blood.goldAutoSpell = false; blood.rebirthAutoSpell = true;
            Assert(effects.SequenceEqual(LoadoutPresetBridgeEffects(main)), "Iron Pill telemetry reads and publication execute no action boundaries");
            Action<string> blocked = reason => {
                var iron = main.BloodState()["ironPill"];
                Assert(iron["available"].AsBool && !iron["eligible"].AsBool && iron["status"].Value == "blocked" && iron["reason"].Value == reason, "Iron Pill blocker: " + reason + "; actual: " + iron.ToString());
            };
            settings.AutoRebirth = false;
            Assert(main.BloodState()["ironPill"]["eligible"].AsBool, "active resource allocation permits Iron Pill with global auto-rebirth disabled");
            var plan = NGUInjector.Main.CustomPlanStore.Load("eligibility"); plan.AutoRebirthEnabled = false;
            NGUInjector.Main.CustomPlanStore.Save("eligibility", plan);
            Assert(main.BloodState()["ironPill"]["eligible"].AsBool, "active resource allocation permits Iron Pill with both auto-rebirth switches disabled");
            settings.AutoRebirth = true;
            Assert(main.BloodState()["ironPill"]["eligible"].AsBool, "plan auto-rebirth opt-out alone does not disable resource Iron Pill");
            settings.ManageMagic = false; blocked("Magic management is disabled."); settings.ManageMagic = true;
            settings.UseCustomRebirthPlan = false;
            blocked("No custom plan is loaded.");
            Assert((string)typeof(NGUInjector.Main).GetMethod("CustomIronPillEligibilityBlocker",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(main, new object[] {plan})
                == "No resource allocation activity is selected.", "a known plan without active resource selection cannot enable Iron Pill");
            var paused = main.BloodState();
            Assert(paused["ironPill"]["planName"].Value == string.Empty
                && paused["ironPill"]["saveSeconds"].AsInt == settings.CustomIronPillSaveSeconds
                && paused["ironPill"]["minWorth"].AsDouble == settings.CustomIronPillMinWorth,
                "paused blood metadata deliberately uses global fallback values and no active plan context");
            settings.UseCustomRebirthPlan = true;
            plan.AutoRebirthEnabled = true; NGUInjector.Main.CustomPlanStore.Save("eligibility", plan);
            plan.AutoIronPillEnabled = false; NGUInjector.Main.CustomPlanStore.Save("eligibility", plan);
            blocked("The selected plan's automatic Iron Pill is disabled.");
            Assert(settings.CustomAutoBloodSpellsEnabled && settings.AutoRebirth && settings.UseCustomRebirthPlan
                && NGUInjector.Main.CustomPlanStore.Load("eligibility").AutoRebirthEnabled,
                "Iron Pill opt-out leaves automatic blood spells and rebirth switches enabled");
            Assert(main.BloodState()["bloodGoals"].ToString() == available["bloodGoals"].ToString(),
                "Iron Pill opt-out leaves blood goal metadata unchanged");
            Assert(!Managers.IronPillAutomation.ShouldReserve(main.BloodState()["ironPill"]["eligible"].AsBool, true, 0, 900, 0),
                "plan opt-out blocks reservation even when already saving with cooldown ready");
            plan.AutoIronPillEnabled = true; NGUInjector.Main.CustomPlanStore.Save("eligibility", plan);
            Assert(main.BloodState()["ironPill"]["eligible"].AsBool, "explicit Iron Pill opt-in restores eligibility");
            settings.CustomAutoBloodSpellsEnabled = false; blocked("Automatic blood spells are disabled."); settings.CustomAutoBloodSpellsEnabled = true;
            NGUInjector.Main.Character.rebirthTime.totalseconds = 3599.99; blocked("Requires at least one hour in the current rebirth.");
            NGUInjector.Main.Character.rebirthTime.totalseconds = 3600;
            NGUInjector.Main.Character.bossID = 99; blocked("Requires boss stage 100 or later."); NGUInjector.Main.Character.bossID = 100;
            main.ManualRebirthPending = true; blocked("Manual rebirth is pending."); main.ManualRebirthPending = false;
            main.PreRebirthPrepRunning = true; blocked("Pre-rebirth preparation is running."); main.PreRebirthPrepRunning = false;
            main.BossPush = true; blocked("Boss push is running."); main.BossPush = false;
            NGUInjector.Main.Character.bloodMagicController.Fail = true;
            var error = main.BloodState();
            Assert(!error["bloodGoals"]["available"].AsBool && error["bloodGoals"]["status"].Value == "error"
                && error["bloodGoals"]["reason"].Value.Contains("simulated blood formula failure") && error["bloodGoals"]["spaghetti"].IsNull,
                "blood formula exceptions are reported instead of silently omitting metadata");
            Assert(error["ironPill"]["eligible"].AsBool, "blood estimate failure does not suppress independent Iron Pill metadata");
            NGUInjector.Main.Character.bloodMagicController.Fail = false; main.BossPushFails = true;
            error = main.BloodState();
            Assert(error["ironPill"]["status"].Value == "error" && error["ironPill"]["eligible"].IsNull && !error["ironPill"]["available"].AsBool
                && error["ironPill"]["reason"].Value.Contains("simulated boss state failure"), "eligibility exceptions are explicit metadata errors");
            Assert(error["bloodGoals"]["available"].AsBool, "Iron Pill failure does not suppress blood goal metadata");
            main.BossPushFails = false;
            NGUInjector.Main.Character = null;
            settings.CustomPlanFile = "default"; settings.UseCustomRebirthPlan = false;
            settings.ManageMagic = managedMagic;

            var invalidPlan = new CustomPlan { Stages = new[] { new CustomPlanStage { Percent = double.NaN }, new CustomPlanStage { Percent = double.PositiveInfinity } } };
            NGUInjector.Main.CustomPlanStore.Save("normalized-weights", invalidPlan);
            Assert(NGUInjector.Main.CustomPlanStore.Load("normalized-weights").Stages.All(stage => stage.Percent == 1d), "native non-finite stage weights normalize before serialization");
            invalidPlan.Stages = new[] { new CustomPlanStage { Percent = double.MaxValue }, new CustomPlanStage { Percent = double.MaxValue } };
            Reject(() => NGUInjector.Main.CustomPlanStore.Save("overflow-weights", invalidPlan), "overflowing stage total rejected before save");
            Assert(!NGUInjector.Main.CustomPlanStore.PlanExists("overflow-weights"), "invalid stage total never creates a saved plan");
        }

        private static void CheckMissingPlans(NGUInjector.Main main)
        {
            var store = NGUInjector.Main.CustomPlanStore;
            var missingName = "missing-source";
            var missingPath = Path.Combine(_dir, "custom-plans", missingName + ".json");
            var defaultPath = Path.Combine(_dir, "custom-plans", "24hr.json");
            var defaultBytes = File.ReadAllBytes(defaultPath);
            store.Save("missing-sentinel", new CustomPlan {
                SourceJson = JsonFiles.ParseObject("{\"future\":{\"precise\":9223372036854775807}}"),
                TimeTriggerSeconds = 12345,
                RebirthHistory = new[] {new CustomRebirthHistoryEntry {Reason = "sentinel"}} });
            var sentinelPath = Path.Combine(_dir, "custom-plans", "missing-sentinel.json");
            var sentinelBytes = File.ReadAllBytes(sentinelPath);
            Assert(!File.Exists(missingPath), "missing-plan fixture starts absent");
            Action<Action, string> rejectMissing = (action, label) => {
                try { action(); }
                catch (FileNotFoundException error)
                {
                    Assert(error.FileName == missingPath, label + " identifies the requested path");
                    return;
                }
                throw new Exception("FAIL (accepted): " + label);
            };
            rejectMissing(() => store.Load(missingName), "missing store load throws FileNotFoundException");
            Assert(NGUInjector.Main.LastLog.Contains("Failed to load custom plan '" + missingName + "'"), "missing store load logs the requested name");
            rejectMissing(() => store.ReadExternalPlan(missingName), "missing normalized editor read throws FileNotFoundException");
            rejectMissing(() => store.AddRebirthHistory(missingName, new CustomRebirthHistoryEntry {Reason = "must not create"}), "missing history RMW throws FileNotFoundException");
            Assert(!File.Exists(missingPath) && defaultBytes.SequenceEqual(File.ReadAllBytes(defaultPath)), "missing history append neither creates its target nor changes default bytes");
            rejectMissing(() => store.Save(missingName, store.Load(missingName)), "missing native RMW throws FileNotFoundException");
            Assert(!File.Exists(missingPath), "missing native RMW cannot recreate its target");
            rejectMissing(() => store.Save("missing-sentinel", store.Load(missingName)), "unrelated native save rejects missing source");
            Assert(sentinelBytes.SequenceEqual(File.ReadAllBytes(sentinelPath)), "missing source cannot replace unrelated sentinel trigger/history/unknowns");
            foreach (var name in new[] {null, "", "   "})
            {
                var plan = store.Load(name);
                Assert(plan.Name == "24hr" && plan.SourceJson != null && plan.RuntimeRevision == JsonFiles.ComputeRevision(defaultBytes), "empty normalized name loads the existing 24hr fallback");
            }

            var settings = NGUInjector.Main.Settings;
            var selected = settings.CustomPlanFile;
            var running = settings.UseCustomRebirthPlan;
            var originalCharacter = NGUInjector.Main.Character;
            var allocations = NGUInjector.Main.Allocations;
            var focuses = NGUInjector.Main.Focuses;
            try
            {
                settings.CustomPlanFile = missingName;
                settings.UseCustomRebirthPlan = true;
                NGUInjector.Main.Character = new FakeCharacter {
                    rebirthTime = new FakeTime {totalseconds = 3600}, bossID = 100, bloodMagic = new FakeBlood(),
                    bloodMagicController = new FakeBloodController(), bloodSpells = new FakeBloodSpells() };
                var state = main.BloodState();
                Assert(state["ironPill"]["status"].Value == "error" && !state["ironPill"]["available"].AsBool && state["ironPill"]["eligible"].IsNull
                    && state["ironPill"]["reason"].Value.Contains(missingName), "runtime store load reports the missing selected plan instead of evaluating default");
                Assert(!state["bloodGoals"]["available"].AsBool && state["bloodGoals"]["status"].Value == "error"
                    && state["bloodGoals"]["reason"].Value.Contains(missingName),
                    "missing running plan fails closed for plan-owned blood goals rather than using unrelated global goals");
                main.SetReady(true);
                var snapshot = JsonFiles.ParseObject(File.ReadAllText(Path.Combine(_dir, "runtime.json")));
                Assert(snapshot["ironPill"].ToString() == state["ironPill"].ToString() && snapshot["settings"]["customPlanFile"].Value == missingName,
                    "runtime snapshot retains selected name and publishes missing-plan error");
                main.Process("read-missing-source", Envelope("read-missing-source", "readPlan", JsonFiles.ParseObject("{\"name\":\"missing-source\"}")));
                Assert(!Result("read-missing-source")["success"].AsBool && Result("read-missing-source")["message"].Value.Contains(missingName), "readPlan command reports missing target without default fallback");
                Assert(NGUInjector.Main.Allocations == allocations && NGUInjector.Main.Focuses == focuses, "missing runtime/editor reads invoke no allocation or focus actions");
                Assert(!File.Exists(missingPath) && defaultBytes.SequenceEqual(File.ReadAllBytes(defaultPath)) && sentinelBytes.SequenceEqual(File.ReadAllBytes(sentinelPath)),
                    "all missing-plan reads and RMW attempts leave target absent and default/sentinel bytes unchanged");
            }
            finally
            {
                NGUInjector.Main.Character = originalCharacter;
                settings.CustomPlanFile = selected;
                settings.UseCustomRebirthPlan = running;
                main.SetReady(true);
            }
        }

        private static JSONObject PlanArgs(string name, JSONObject plan)
        {
            var args = new JSONObject(); args["name"] = name; args["plan"] = plan;
            var path = Path.Combine(_dir, "custom-plans", name + ".json");
            if (File.Exists(path))
            {
                var saved = NGUInjector.Main.CustomPlanStore.ReadExternalPlan(name);
                // Existing editor submissions must round-trip injector-owned historical provenance.
                foreach (var field in new[] { "LoadoutPresets", "GoalPresets", "ActiveGoalPresetIndex" })
                    if (!plan.Linq.Any(x => x.Key == field)) plan[field] = saved[field];
            }
            args["expectedRevision"] = File.Exists(path) ? JsonFiles.ComputeRevision(File.ReadAllBytes(path)) : string.Empty;
            return args;
        }

        private static JSONObject PlanWithNguWeights(string text)
        {
            // Deliberately exercise legacy input; normalized plans must strip these retired fields.
            var root = JsonFiles.ParseObject(text);
            root["EnergyNguWeights"] = JsonFiles.ParseObject("{\"weights\":[0,0,0,0,1,0,1,0,0]}")["weights"];
            root["MagicNguWeights"] = JsonFiles.ParseObject("{\"weights\":[1,0,0,0,0,0,0]}")["weights"];
            return root;
        }

        private static void CheckNguWeights()
        {
            CheckGlobalNguSettings();
            var store = NGUInjector.Main.CustomPlanStore;
            var defaults = new CustomPlan();
            Assert(defaults.Version == 18 && CustomPlanStore.CurrentPlanVersion == 18, "new model and store use version 18");
            Assert(typeof(CustomPlan).GetField("EnergyNguWeights") == null && typeof(CustomPlan).GetField("MagicNguWeights") == null,
                "plans have no runtime-owned NGU ratio fields");
            Assert(typeof(CustomPlan).GetField("NguGoal") == null && typeof(CustomPlan).Assembly.GetType("NGUInjector.CustomPlans.CustomNguGoal") == null, "legacy goal field and enum are absent from runtime");
            var settingsPath = Path.Combine(_dir, "settings.json");
            var settingsBytes = File.ReadAllBytes(settingsPath);
            foreach (var name in PlanCatalogNames)
            {
                var builtin = store.ReadExternalPlan(name);
                Assert(!builtin.Linq.Any(item => item.Key == "EnergyNguWeights" || item.Key == "MagicNguWeights"), "built-in leaves NGU ratios global: " + name);
            }

            var migrationPath = Path.Combine(_dir, "custom-plans", "ngu-migration.json");
            foreach (var version in new[] {0, 5, 10, 11, 14, 15})
            foreach (var goal in new[] {-1, 0, 1, 2})
            {
                File.WriteAllText(migrationPath, "{\"Version\":" + version + ",\"NguGoal\":" + goal + "}", new UTF8Encoding(true));
                var bytes = File.ReadAllBytes(migrationPath);
                var model = store.Load("ngu-migration");
                Assert(model.Version == 18, "legacy plan normalizes schema without materializing NGU ratios " + version + "/" + goal);
                var read = store.ReadExternalPlan("ngu-migration");
                Assert(!read.Linq.Any(item => item.Key == "NguGoal" || item.Key == "EnergyNguWeights" || item.Key == "MagicNguWeights")
                    && read["Version"].AsInt == 18, "normalized read strips retired NGU configuration " + version + "/" + goal);
                Assert(bytes.SequenceEqual(File.ReadAllBytes(migrationPath)) && model.RuntimeRevision == JsonFiles.ComputeRevision(bytes)
                    && read[CustomPlanStore.ExternalUiRevisionField].Value == model.RuntimeRevision, "migration preserves bytes and original-byte CAS " + version + "/" + goal);
                store.AddRebirthHistory("ngu-migration", new CustomRebirthHistoryEntry {Reason = "migrated history"});
                var persisted = JsonFiles.ParseObject(File.ReadAllText(migrationPath));
                Assert(persisted["Version"].AsInt == 18 && persisted["RebirthHistory"][0]["Reason"].Value == "migrated history"
                    && !persisted.Linq.Any(item => item.Key == "NguGoal" || item.Key == "EnergyNguWeights" || item.Key == "MagicNguWeights"),
                    "history RMW removes retired plan NGU configuration " + version + "/" + goal);
            }
            File.WriteAllText(migrationPath, "{\"NguGoal\":1}");
            Assert(!store.ReadExternalPlan("ngu-migration").Linq.Any(item => item.Key == "NguGoal" || item.Key == "EnergyNguWeights" || item.Key == "MagicNguWeights"),
                "versionless plans also normalize without plan-owned ratios");

            foreach (var version in new[] {5, 10, 11, 15})
            foreach (var lane in new[] {"EnergyNguWeights", "MagicNguWeights"})
            {
                var root = PlanWithNguWeights("{\"Version\":" + version + ",\"NguGoal\":1}");
                var missing = lane == "EnergyNguWeights" ? "MagicNguWeights" : "EnergyNguWeights";
                root.Remove(missing);
                File.WriteAllText(migrationPath, root.ToString());
                var read = store.ReadExternalPlan("ngu-migration");
                Assert(!read.Linq.Any(item => item.Key == lane || item.Key == missing), "partial legacy ratios are stripped without filling missing lane " + version + "/" + lane);
            }

            foreach (var version in new[] {5, 15, 16})
            foreach (var value in new[] {0, int.MaxValue})
            {
                var root = PlanWithNguWeights("{\"Version\":" + version + ",\"NguGoal\":1}");
                foreach (var lane in new[] {"EnergyNguWeights", "MagicNguWeights"})
                    for (var i = 0; i < root[lane].Count; i++) root[lane][i] = value;
                File.WriteAllText(migrationPath, root.ToString());
                var model = store.Load("ngu-migration");
                store.Save("ngu-migration", model);
                var read = store.ReadExternalPlan("ngu-migration");
                Assert(!read.Linq.Any(item => item.Key == "EnergyNguWeights" || item.Key == "MagicNguWeights")
                    && !JsonFiles.ParseObject(File.ReadAllText(migrationPath)).Linq.Any(item => item.Key == "EnergyNguWeights" || item.Key == "MagicNguWeights"),
                    "valid retired arrays at both inclusive bounds are stripped on native save " + version + "/" + value);
            }
            var numeric = PlanWithNguWeights("{\"Version\":16}");
            var numericEntries = JsonFiles.ParseObject("{\"weights\":[1.0,1E+0,10e-1,0e-999,2147483647.0,21474836470e-1,-0,0.000,2.147483647E9]}")["weights"];
            numeric["EnergyNguWeights"] = numericEntries;
            File.WriteAllText(migrationPath, numeric.ToString());
            Assert(!store.ReadExternalPlan("ngu-migration").Linq.Any(item => item.Key == "EnergyNguWeights"), "retired decimal/exponent legacy tokens are stripped");

            var rich = PlanWithNguWeights("{\"Version\":15,\"NguGoal\":0,\"futureRoot\":{\"precise\":9223372036854775807,\"decimal\":1.234567890123456789},\"Stages\":[{\"futureStage\":\"keep\"}],\"GoalPresets\":[{\"futurePreset\":\"keep\"}],\"RebirthHistory\":[{\"Reason\":\"live\",\"futureHistory\":{\"precise\":9223372036854775807}}]}");
            File.WriteAllText(migrationPath, rich.ToString(), new UTF8Encoding(true));
            var original = File.ReadAllBytes(migrationPath);
            var richModel = store.Load("ngu-migration");
            richModel.TimeTriggerSeconds = 37;
            store.Save("ngu-migration", richModel);
            var saved = store.ReadExternalPlan("ngu-migration");
            Assert(saved["TimeTriggerSeconds"].AsInt == 37 && !saved.Linq.Any(item => item.Key == "NguGoal" || item.Key == "EnergyNguWeights" || item.Key == "MagicNguWeights"),
                "native RMW persists plan edits while stripping retired NGU configuration");
            Assert(saved["futureRoot"].ToString() == rich["futureRoot"].ToString() && saved["Stages"][0]["futureStage"].Value == "keep"
                && saved["GoalPresets"][0]["futurePreset"].Value == "keep" && saved["RebirthHistory"][0]["futureHistory"].ToString() == rich["RebirthHistory"][0]["futureHistory"].ToString(), "native weights RMW preserves root, row and history source tokens");
            store.AddRebirthHistory("ngu-migration", new CustomRebirthHistoryEntry {Reason = "new live"});
            var historyBytes = File.ReadAllBytes(migrationPath);
            Reject(() => store.SaveExternalPlan("ngu-migration", saved, JsonFiles.ComputeRevision(original)), "weights migration rejects original-byte revision after native/history writes");
            Assert(historyBytes.SequenceEqual(File.ReadAllBytes(migrationPath)), "stale weights CAS preserves all bytes");
            var submission = store.ReadExternalPlan("ngu-migration");
            submission["RebirthHistory"] = new JSONArray(); submission["NguGoal"] = 1;
            submission["MagicNguWeights"] = PlanWithNguWeights("{}")["MagicNguWeights"];
            store.SaveExternalPlan("ngu-migration", submission, submission[CustomPlanStore.ExternalUiRevisionField].Value);
            saved = store.ReadExternalPlan("ngu-migration");
            Assert(saved["TimeTriggerSeconds"].AsInt == 37 && !saved.Linq.Any(item => item.Key == "NguGoal" || item.Key == "EnergyNguWeights" || item.Key == "MagicNguWeights"),
                "normalized editor save never resurrects submitted retired NGU configuration");
            Assert(saved["RebirthHistory"][0]["Reason"].Value == "new live" && saved["RebirthHistory"][1]["futureHistory"].ToString() == rich["RebirthHistory"][0]["futureHistory"].ToString()
                && saved["futureRoot"].ToString() == rich["futureRoot"].ToString(), "editor weights save preserves live history source and unknown numeric tokens");
            var copyName = store.CreatePlan("ngu-copy", store.Load("ngu-migration"));
            var copy = store.ReadExternalPlan(copyName);
            Assert(!copy.Linq.Any(item => item.Key == "EnergyNguWeights" || item.Key == "MagicNguWeights")
                && copy["futureRoot"].ToString() == saved["futureRoot"].ToString() && copy["RebirthHistory"].Count == 0, "native copy preserves unknowns without history or retired NGU arrays");

            var sentinelPath = Path.Combine(_dir, "custom-plans", "ngu-sentinel.json");
            var sentinel = PlanWithNguWeights("{\"Version\":16,\"future\":{\"precise\":9223372036854775807},\"RebirthHistory\":[{\"Reason\":\"sentinel\",\"future\":true}]}");
            File.WriteAllText(sentinelPath, sentinel.ToString(), new UTF8Encoding(true));
            foreach (var lane in new[] {"EnergyNguWeights", "MagicNguWeights"})
            {
                var count = lane == "EnergyNguWeights" ? 9 : 7;
                foreach (var version in new[] {15, 16})
                {
                    foreach (var invalidArray in new[] {"null", "[]", "{}", "0", "true", "\"weights\"", "[" + string.Join(",", Enumerable.Repeat("0", count - 1)) + "]", "[" + string.Join(",", Enumerable.Repeat("0", count + 1)) + "]"})
                    {
                        var invalid = PlanWithNguWeights("{\"Version\":" + version + "}");
                        invalid[lane] = JsonFiles.ParseObject("{\"invalid\":" + invalidArray + "}")["invalid"];
                        CheckRetiredNguPlan(invalid, version + "/" + lane + "/" + invalidArray);
                    }
                    foreach (var invalidEntry in new[] {"null", "true", "\"1\"", "{}", "[]", "-1", "1.5", "2147483648", "1e10", "2147483647.0000000001", "1.000000000000000000001", "1e-400", "-1e-400", "1e-9223372036854775808"})
                    for (var i = 0; i < count; i++)
                    {
                        var invalid = PlanWithNguWeights("{\"Version\":" + version + "}");
                        invalid[lane][i] = JsonFiles.ParseObject("{\"invalid\":" + invalidEntry + "}")["invalid"];
                        CheckRetiredNguPlan(invalid, version + "/" + lane + "[" + i + "]/" + invalidEntry);
                    }
                }
            }
            foreach (var version in new[] {16, 17})
            {
                var name = "ngu-ratio-free-" + version;
                store.SaveExternalPlan(name, JsonFiles.ParseObject("{\"Version\":" + version + "}"), "");
                Assert(!store.ReadExternalPlan(name).Linq.Any(item => item.Key == "EnergyNguWeights" || item.Key == "MagicNguWeights"),
                    "current plans do not require retired NGU arrays: " + version);
            }
            var invalidPath = Path.Combine(_dir, "custom-plans", "ngu-invalid.json");
            foreach (var malformed in new[] {"", "{", "[]"})
            {
                File.WriteAllText(invalidPath, malformed, new UTF8Encoding(true));
                var bytes = File.ReadAllBytes(invalidPath);
                Reject(() => store.Load("ngu-invalid"), "malformed load throws instead of fabricating defaults");
                Reject(() => store.AddRebirthHistory("ngu-invalid", new CustomRebirthHistoryEntry {Reason = "must not write"}), "malformed history RMW throws");
                Reject(() => store.Save("ngu-invalid", store.Load("ngu-invalid")), "malformed native RMW throws");
                Reject(() => store.SaveExternalPlan("ngu-invalid", sentinel, JsonFiles.ComputeRevision(bytes)), "malformed live history prevents external overwrite");
                Assert(bytes.SequenceEqual(File.ReadAllBytes(invalidPath)), "malformed RMW paths retain original bytes");
            }
            Assert(settingsBytes.SequenceEqual(File.ReadAllBytes(settingsPath)), "plan reads, migrations, copies, edits and failures never change durable global NGU settings");
        }

        private static void CheckGlobalNguSettings()
        {
            var directory = Path.Combine(_dir, "global-ngu-settings");
            Directory.CreateDirectory(Path.Combine(directory, "custom-plans"));
            var path = Path.Combine(directory, "settings.json");
            var planPath = Path.Combine(directory, "custom-plans", "selected.json");
            var energyDefault = new[] {0,0,0,0,1,0,1,0,0};
            var magicDefault = new[] {1,0,0,0,0,0,0};
            var defaults = new SavedSettings(null);
            Assert(defaults.EnergyNguWeights.SequenceEqual(energyDefault) && defaults.MagicNguWeights.SequenceEqual(magicDefault),
                "fresh global settings use adventure/drop energy and NGU magic defaults");
            var detached = defaults.EnergyNguWeights; detached[0] = 99;
            var assigned = new[] {7,6,5,4,3,2,1}; defaults.MagicNguWeights = assigned; assigned[0] = 99;
            Assert(defaults.EnergyNguWeights.SequenceEqual(energyDefault) && defaults.MagicNguWeights[0] == 7,
                "global ratio getters and setters do not expose mutable backing arrays");

            foreach (var version in new[] {-1, 0, 5, 10, 11, 14, 15, 16, 17})
            foreach (var goal in new[] {-1, 0, 1, 2})
            {
                var legacy = JsonFiles.ParseObject("{\"NguGoal\":" + goal + "}");
                if (version >= 0) legacy["Version"] = version;
                File.WriteAllText(planPath, legacy.ToString(), new UTF8Encoding(true));
                var planBytes = File.ReadAllBytes(planPath);
                File.WriteAllText(path, "{\"_customPlanFile\":\"selected\",\"future\":{\"precise\":9223372036854775807,\"decimal\":1.234567890123456789}}");
                var settings = new SavedSettings(directory);
                var adventure = (version == -1 || version >= 11) && version <= 15 && goal == 1;
                var expectedEnergy = version >= 16 || adventure ? energyDefault : new[] {1,1,0,1,0,1,0,0,0};
                var expectedMagic = version >= 16 ? magicDefault : adventure ? new[] {0,0,0,0,0,0,1} : new[] {0,0,1,0,0,0,0};
                Assert(settings.LoadSettings() && settings.EnergyNguWeights.SequenceEqual(expectedEnergy) && settings.MagicNguWeights.SequenceEqual(expectedMagic),
                    "first settings load migrates selected raw plan version/goal " + version + "/" + goal);
                var saved = JsonFiles.ParseObject(File.ReadAllText(path));
                Assert(saved["_energyNguWeights"].Children.Select(item => item.AsInt).SequenceEqual(expectedEnergy)
                    && saved["_magicNguWeights"].Children.Select(item => item.AsInt).SequenceEqual(expectedMagic)
                    && saved["future"]["precise"].Value == "9223372036854775807" && saved["future"]["decimal"].Value == "1.234567890123456789",
                    "migration durably saves both global lanes without losing unknown settings tokens");
                Assert(planBytes.SequenceEqual(File.ReadAllBytes(planPath)), "global migration never rewrites its raw source plan");
                File.WriteAllText(planPath, PlanWithNguWeights("{\"Version\":17}").ToString());
                var restarted = new SavedSettings(directory);
                Assert(restarted.LoadSettings() && restarted.EnergyNguWeights.SequenceEqual(expectedEnergy) && restarted.MagicNguWeights.SequenceEqual(expectedMagic),
                    "persisted global lanes survive restart independently of changed selected-plan arrays");
            }

            var explicitPlan = PlanWithNguWeights("{\"Version\":15}");
            explicitPlan["EnergyNguWeights"] = JsonFiles.ParseObject("{\"v\":[9,8,7,6,5,4,3,2,1]}")["v"];
            explicitPlan["MagicNguWeights"] = JsonFiles.ParseObject("{\"v\":[7,6,5,4,3,2,1]}")["v"];
            File.WriteAllText(planPath, explicitPlan.ToString());
            foreach (var lane in new[] {"_energyNguWeights", "_magicNguWeights"})
            {
                var root = JsonFiles.ParseObject("{\"_customPlanFile\":\"selected\"}");
                var count = lane == "_energyNguWeights" ? 9 : 7;
                root[lane] = new JSONArray(); foreach (var value in Enumerable.Repeat(23, count)) root[lane].Add(value);
                File.WriteAllText(path, root.ToString());
                var settings = new SavedSettings(directory);
                Assert(settings.LoadSettings()
                    && settings.EnergyNguWeights.SequenceEqual(lane == "_energyNguWeights" ? Enumerable.Repeat(23, 9) : new[] {9,8,7,6,5,4,3,2,1})
                    && settings.MagicNguWeights.SequenceEqual(lane == "_magicNguWeights" ? Enumerable.Repeat(23, 7) : new[] {7,6,5,4,3,2,1}),
                    "partial global migration retains explicit lane and imports only missing lane: " + lane);
                var energy = settings.EnergyNguWeights; var magic = settings.MagicNguWeights;
                root = JsonFiles.ParseObject(File.ReadAllText(path)); root.Remove(lane);
                File.WriteAllText(planPath, "{\"Version\":15,\"NguGoal\":0}");
                File.WriteAllText(path, root.ToString());
                Assert(settings.LoadSettings() && settings.EnergyNguWeights.SequenceEqual(energy) && settings.MagicNguWeights.SequenceEqual(magic),
                    "removing a global key in-process cannot replay selected-plan migration");
                File.WriteAllText(planPath, explicitPlan.ToString());
            }

            var global = new SavedSettings(directory);
            Assert(global.LoadSettings(), "global setter checks explicitly load the existing settings baseline");
            global.SaveSettings();
            foreach (var value in new[] {0, int.MaxValue})
            {
                global.EnergyNguWeights = Enumerable.Repeat(value, 9).ToArray();
                global.MagicNguWeights = Enumerable.Repeat(value, 7).ToArray();
                var restarted = new SavedSettings(directory);
                Assert(restarted.LoadSettings() && restarted.EnergyNguWeights.All(weight => weight == value) && restarted.MagicNguWeights.All(weight => weight == value),
                    "both inclusive global ratio bounds persist through property setters and restart: " + value);
            }
            global.ApplyPatch(JsonFiles.ParseObject("{\"_energyNguWeights\":[1.0,1E+0,10e-1,0e-999,2147483647.0,21474836470e-1,-0,0.000,2.147483647E9]}"));
            Assert(global.EnergyNguWeights.SequenceEqual(new[] {1,1,1,0,int.MaxValue,int.MaxValue,0,0,int.MaxValue}),
                "global patch accepts exact whole decimal/exponent numeric tokens");
            var baseline = File.ReadAllBytes(path);
            var liveEnergy = global.EnergyNguWeights; var liveMagic = global.MagicNguWeights;
            foreach (var lane in new[] {"_energyNguWeights", "_magicNguWeights"})
            {
                var count = lane == "_energyNguWeights" ? 9 : 7;
                foreach (var invalid in new[] {"null", "[]", "{}", "true", "0", "\"weights\"",
                    "[" + string.Join(",", Enumerable.Repeat("0", count - 1)) + "]", "[" + string.Join(",", Enumerable.Repeat("0", count + 1)) + "]"}
                    .Concat(new[] {"null", "true", "\"1\"", "{}", "[]", "-1", "1.5", "2147483648", "2147483647.0000000001", "1e-400", "-1e-400"}
                        .Select(entry => "[" + entry + "," + string.Join(",", Enumerable.Repeat("0", count - 1)) + "]")))
                {
                    var patch = JsonFiles.ParseObject("{\"_autoRebirth\":false,\"" + lane + "\":" + invalid + "}");
                    RejectLoadout(() => global.ApplyPatch(patch), "invalid global ratio patch " + lane + "=" + invalid);
                    Assert(baseline.SequenceEqual(File.ReadAllBytes(path)) && global.EnergyNguWeights.SequenceEqual(liveEnergy)
                        && global.MagicNguWeights.SequenceEqual(liveMagic) && global.AutoRebirth,
                        "invalid global ratio patch cannot partially mutate live or persisted settings");
                    var corrupt = JsonFiles.ParseObject(System.Text.Encoding.UTF8.GetString(baseline)); corrupt[lane] = patch[lane];
                    File.WriteAllText(path, corrupt.ToString()); var corruptBytes = File.ReadAllBytes(path);
                    Assert(!global.LoadSettings() && global.EnergyNguWeights.SequenceEqual(liveEnergy) && global.MagicNguWeights.SequenceEqual(liveMagic)
                        && corruptBytes.SequenceEqual(File.ReadAllBytes(path)), "invalid global settings reload fails without mutation or silent repair");
                    File.WriteAllBytes(path, baseline);
                }
                foreach (var invalid in new[] {null, new int[count - 1], new int[count + 1], Enumerable.Repeat(-1, count).ToArray()})
                {
                    RejectLoadout(() => { if (lane == "_energyNguWeights") global.EnergyNguWeights = invalid; else global.MagicNguWeights = invalid; },
                        "global property setter rejects invalid shape or negative ratio: " + lane);
                    Assert(baseline.SequenceEqual(File.ReadAllBytes(path)) && global.EnergyNguWeights.SequenceEqual(liveEnergy) && global.MagicNguWeights.SequenceEqual(liveMagic),
                        "invalid global setter leaves live ratios and persisted bytes unchanged");
                }
            }
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                Reject(() => global.ApplyPatch(JsonFiles.ParseObject("{\"_magicNguWeights\":[1,2,3,4,5,6,7]}")), "failed global ratio persistence is rejected");
            Assert(global.EnergyNguWeights.SequenceEqual(liveEnergy) && global.MagicNguWeights.SequenceEqual(liveMagic)
                && baseline.SequenceEqual(File.ReadAllBytes(path)), "failed global ratio persistence publishes no live mutation");
        }

        private static void CheckGlobalNguWeightsCommands(NGUInjector.Main main)
        {
            var settings = NGUInjector.Main.Settings;
            var restore = new JSONObject();
            restore["_energyNguWeights"] = CustomPlanStore.SerializeNguWeights(settings.EnergyNguWeights, 9, "energy");
            restore["_magicNguWeights"] = CustomPlanStore.SerializeNguWeights(settings.MagicNguWeights, 7, "magic");
            var equips = NGUInjector.Main.Equips; var saves = NGUInjector.Main.Saves;
            var focuses = NGUInjector.Main.Focuses; var reloads = NGUInjector.Main.Allocations;
            try
            {
                var patch = JsonFiles.ParseObject("{\"patch\":{\"_energyNguWeights\":[9,8,7,6,5,4,3,2,1],\"_magicNguWeights\":[7,6,5,4,3,2,1]}}");
                main.Process("global-ngu-patch", Envelope("global-ngu-patch", "applySettingsPatch", patch));
                var result = Result("global-ngu-patch"); var compact = result["data"]["runtime"];
                var full = JsonFiles.ParseObject(File.ReadAllText(Path.Combine(_dir, "runtime.json")));
                Assert(result["success"].AsBool && settings.EnergyNguWeights.SequenceEqual(new[] {9,8,7,6,5,4,3,2,1})
                    && settings.MagicNguWeights.SequenceEqual(new[] {7,6,5,4,3,2,1}), "settings command changes the sole live global NGU configuration");
                foreach (var runtime in new[] {compact, full})
                {
                    Assert(runtime["globalNguWeightsVersion"].AsInt == 1 && runtime["loadoutPresetsVersion"].AsInt == 2 && runtime["globalPresetsVersion"].AsInt == 2,
                        "compact and full runtime advertise ratio-free presets with global NGU settings");
                    Assert(runtime["settings"]["_energyNguWeights"].ToString() == patch["patch"]["_energyNguWeights"].ToString()
                        && runtime["settings"]["_magicNguWeights"].ToString() == patch["patch"]["_magicNguWeights"].ToString(),
                        "compact and full runtime publish the just-persisted global NGU ratios");
                }
                var restarted = new SavedSettings(_dir);
                Assert(restarted.LoadSettings() && restarted.EnergyNguWeights.SequenceEqual(settings.EnergyNguWeights)
                    && restarted.MagicNguWeights.SequenceEqual(settings.MagicNguWeights), "global settings command persists both lanes across restart");
                var baseline = File.ReadAllBytes(Path.Combine(_dir, "settings.json"));
                main.Process("global-ngu-bad-patch", Envelope("global-ngu-bad-patch", "applySettingsPatch",
                    JsonFiles.ParseObject("{\"patch\":{\"_energyNguWeights\":[1,1,1,1,1,1,1,1,1],\"_magicNguWeights\":[-1,0,0,0,0,0,0]}}")));
                Assert(!Result("global-ngu-bad-patch")["success"].AsBool && settings.EnergyNguWeights.SequenceEqual(new[] {9,8,7,6,5,4,3,2,1})
                    && settings.MagicNguWeights.SequenceEqual(new[] {7,6,5,4,3,2,1})
                    && baseline.SequenceEqual(File.ReadAllBytes(Path.Combine(_dir, "settings.json"))), "invalid second global lane rejects the entire command without partial persistence");
                Assert(NGUInjector.Main.Equips == equips && NGUInjector.Main.Saves == saves && NGUInjector.Main.Focuses == focuses,
                    "editing global ratios invokes no equipment, save-game or focus action");
                Assert(NGUInjector.Main.Allocations == reloads + 1, "only the successful global patch reloads the allocation profile");
            }
            finally { settings.ApplyPatch(restore); }
        }

        private static void CheckRetiredGoalPresetRoundtrip(NGUInjector.Main main)
        {
            var settingsPath = Path.Combine(_dir, "settings.json");
            var settingsBytes = File.ReadAllBytes(settingsPath);
            foreach (var malformed in new[] {false, true})
            {
                var name = "retired-goal-roundtrip-" + malformed;
                var path = Path.Combine(_dir, "custom-plans", name + ".json");
                var raw = PlanWithNguWeights("{\"Version\":17,\"ActivePresetId\":\"\",\"TimeTriggerSeconds\":123,\"GoalPresets\":[{\"Name\":\"Historical\",\"GearGoal\":4,\"future\":{\"EnergyNguWeights\":\"extension\",\"MagicNguWeights\":true,\"integer\":9007199254740993}}],\"LoadoutPresets\":[{\"Id\":\"legacy\",\"Name\":\"Legacy\",\"future\":1.2300e+004}]}");
                foreach (var row in new[] {raw, raw["GoalPresets"][0], raw["LoadoutPresets"][0]})
                {
                    row["EnergyNguWeights"] = malformed ? JsonFiles.ParseObject("{\"v\":{\"ignored\":true}} ")["v"] : raw["EnergyNguWeights"];
                    row["MagicNguWeights"] = malformed ? JsonFiles.ParseObject("{\"v\":[-1,\"retired\"]}")["v"] : raw["MagicNguWeights"];
                }
                File.WriteAllText(path, raw.ToString()); var bytes = File.ReadAllBytes(path);
                var readId = "read-" + name;
                main.Process(readId, Envelope(readId, "readPlan", JsonFiles.ParseObject("{\"name\":\"" + name + "\"}")));
                Assert(Result(readId)["success"].AsBool, "connected read ignores retired ratios in historical rows: " + malformed);
                var projected = Result(readId)["data"]["plan"].AsObject;
                Assert(bytes.SequenceEqual(File.ReadAllBytes(path)), "connected read never rewrites historical source: " + malformed);
                Assert(!projected["GoalPresets"][0].Linq.Any(field => field.Key == "EnergyNguWeights" || field.Key == "MagicNguWeights"), "connected historical goal projection is ratio-free: " + malformed);
                // Match the TS/Rust editor, which removes retired goal-row fields before submitting.
                projected["GoalPresets"][0].Remove("EnergyNguWeights");
                projected["GoalPresets"][0].Remove("MagicNguWeights");
                projected["TimeTriggerSeconds"] = 456;
                var saveId = "save-" + name;
                main.Process(saveId, Envelope(saveId, "savePlan", PlanArgs(name, projected)));
                Assert(Result(saveId)["success"].AsBool, "cleaned historical goal rows allow an unrelated connected edit: " + malformed);
                var saved = JsonFiles.ParseObject(File.ReadAllText(path));
                Assert(saved["Version"].AsInt == 18 && saved["TimeTriggerSeconds"].AsInt == 456
                    && new[] {saved, saved["GoalPresets"][0], saved["LoadoutPresets"][0]}.All(row => !row.Linq.Any(field => field.Key == "EnergyNguWeights" || field.Key == "MagicNguWeights")),
                    "connected save strips retired arrays from all historical owners without changing version: " + malformed);
                Assert(saved["GoalPresets"][0]["future"].ToString() == raw["GoalPresets"][0]["future"].ToString()
                    && saved["LoadoutPresets"][0]["future"].Value == "1.2300e+004", "retired-field cleanup preserves unrelated nested extensions and exact numbers: " + malformed);
                projected = NGUInjector.Main.CustomPlanStore.ReadExternalPlan(name);
                projected["GoalPresets"][0]["future"]["integer"] = 7;
                bytes = File.ReadAllBytes(path);
                var rejectId = "reject-" + name;
                main.Process(rejectId, Envelope(rejectId, "savePlan", PlanArgs(name, projected)));
                Assert(!Result(rejectId)["success"].AsBool && Result(rejectId)["message"].Value.Contains("historical provenance")
                    && bytes.SequenceEqual(File.ReadAllBytes(path)), "connected historical unknown fields remain immutable: " + malformed);
            }
            Assert(settingsBytes.SequenceEqual(File.ReadAllBytes(settingsPath)), "retired connected plan fields cannot overwrite existing global settings");
        }

        private static void CheckRetiredNguPlan(JSONObject invalid, string label)
        {
            var store = NGUInjector.Main.CustomPlanStore;
            var sentinelPath = Path.Combine(_dir, "custom-plans", "ngu-sentinel.json");
            var submission = store.ReadExternalPlan("ngu-sentinel");
            foreach (var key in new[] {"EnergyNguWeights", "MagicNguWeights"}) submission[key] = invalid[key];
            store.SaveExternalPlan("ngu-sentinel", submission, submission[CustomPlanStore.ExternalUiRevisionField].Value);
            var saved = JsonFiles.ParseObject(File.ReadAllText(sentinelPath));
            Assert(!saved.Linq.Any(item => item.Key == "EnergyNguWeights" || item.Key == "MagicNguWeights")
                && saved["future"]["precise"].Value == "9223372036854775807" && saved["RebirthHistory"][0]["Reason"].Value == "sentinel",
                "malformed retired submission is stripped while history and unknowns survive " + label);
            store.SaveExternalPlan("ngu-invalid-new", invalid, "");
            Assert(!store.ReadExternalPlan("ngu-invalid-new").Linq.Any(item => item.Key == "EnergyNguWeights" || item.Key == "MagicNguWeights"), "new plans ignore malformed retired arrays " + label);
            File.Delete(Path.Combine(_dir, "custom-plans", "ngu-invalid-new.json"));
            var copy = store.CreateExternalPlan("ngu-invalid-copy", invalid);
            Assert(!store.ReadExternalPlan(copy).Linq.Any(item => item.Key == "EnergyNguWeights" || item.Key == "MagicNguWeights"), "source copies ignore malformed retired arrays " + label);
            File.Delete(Path.Combine(_dir, "custom-plans", copy + ".json"));
            var invalidPath = Path.Combine(_dir, "custom-plans", "ngu-invalid.json");
            File.WriteAllText(invalidPath, invalid.ToString(), new UTF8Encoding(true));
            var invalidBytes = File.ReadAllBytes(invalidPath);
            var loaded = store.Load("ngu-invalid");
            Assert(loaded.Version == 18 && !store.ReadExternalPlan("ngu-invalid").Linq.Any(item => item.Key == "EnergyNguWeights" || item.Key == "MagicNguWeights")
                && invalidBytes.SequenceEqual(File.ReadAllBytes(invalidPath)), "normal reads ignore malformed retired fields without rewriting source " + label);
            store.Save("ngu-invalid", loaded);
            Assert(!JsonFiles.ParseObject(File.ReadAllText(invalidPath)).Linq.Any(item => item.Key == "EnergyNguWeights" || item.Key == "MagicNguWeights"), "native RMW strips malformed retired ratios " + label);
            File.WriteAllBytes(invalidPath, invalidBytes);
            store.AddRebirthHistory("ngu-invalid", new CustomRebirthHistoryEntry {Reason = "retired fields ignored"});
            saved = JsonFiles.ParseObject(File.ReadAllText(invalidPath));
            Assert(saved["RebirthHistory"][0]["Reason"].Value == "retired fields ignored"
                && !saved.Linq.Any(item => item.Key == "EnergyNguWeights" || item.Key == "MagicNguWeights"), "history RMW ignores and strips malformed retired arrays " + label);
        }

        private static void CheckStageExecutionModes()
        {
            var store = NGUInjector.Main.CustomPlanStore;
            const string weights = "\"EnergyNguWeights\":[0,0,0,0,1,0,1,0,0],\"MagicNguWeights\":[1,0,0,0,0,0,0],";
            var legacy = store.CreateExternalPlan("execution-legacy", JsonFiles.ParseObject("{\"Version\":17," + weights + "\"Stages\":[{\"EnergyFocus\":2,\"MagicFocus\":3}]}"));
            var read = store.ReadExternalPlan(legacy);
            Assert(read["Stages"][0]["ExecutionMode"].IsNumber && read["Stages"][0]["ExecutionMode"].AsInt == 0,
                "missing execution mode remains Standard even for AUG/WAN lane focuses");
            var plan = new CustomPlan { StageLoopStartIndex = -1, Stages = new[] {
                new CustomPlanStage { ExecutionMode = 1, DurationSeconds = 600, EnergyFocus = 0, MagicFocus = 0 },
                new CustomPlanStage { ExecutionMode = 0, EnergyFocus = 2, MagicFocus = 3 } } };
            store.Save("execution-auto", plan);
            Assert(store.Load("execution-auto").Stages.Select(x => x.ExecutionMode).SequenceEqual(new[] {1, 0}), "native save/load retains execution modes independently of lane focuses");
            read = store.ReadExternalPlan("execution-auto");
            store.SaveExternalPlan("execution-auto", read, read[CustomPlanStore.ExternalUiRevisionField].Value);
            Assert(store.Load("execution-auto").Stages[0].ExecutionMode == 1, "external roundtrip retains explicit automatic execution");
            Assert(store.Load(legacy).Stages[0].ExecutionMode == 0, "new automatic stage never silently converts existing saved rows");
            foreach (var value in new[] { "-1", "2", "0.5", "null", "true", "\"1\"", "{}", "[]" })
                Reject(() => store.CreateExternalPlan("execution-invalid", JsonFiles.ParseObject("{\"Version\":17," + weights + "\"Stages\":[{\"ExecutionMode\":" + value + "}]}")), "explicit invalid execution mode rejected: " + value);
            Reject(() => store.Save("execution-invalid-native", new CustomPlan { Stages = new[] { new CustomPlanStage { ExecutionMode = 2 } } }), "native invalid execution mode rejected");
            Reject(() => store.Save("execution-invalid-gate", new CustomPlan { Stages = new[] { new CustomPlanStage { ExecutionMode = 1, CompletionMode = 1 } } }), "automatic elapsed stage cannot use a mandatory stat gate");
            var budgetPlan = new CustomPlan { TimeTriggerEnabled = false, StageDurationSeconds = 86400, StageLoopStartIndex = -1,
                Stages = new[] { new CustomPlanStage { ExecutionMode = 1, DurationSeconds = 86400 } } };
            store.Save("automatic-budget", budgetPlan);
            Assert(store.Load("automatic-budget").Stages[0].DurationSeconds == 86400, "automatic fixed budget accepts exactly 24 hours");
            budgetPlan.Stages[0].DurationSeconds = 86401;
            Reject(() => store.Save("automatic-budget", budgetPlan), "automatic fixed budget above 24 hours rejected before save");
            Assert(store.Load("automatic-budget").Stages[0].DurationSeconds == 86400, "invalid automatic duration cannot replace a valid saved plan");
            budgetPlan.Stages[0].DurationSeconds = 0; budgetPlan.StageDurationSeconds = 86401;
            Reject(() => store.Save("automatic-budget", budgetPlan), "automatic flexible budget above 24 hours rejected before execution");
            budgetPlan.StageDurationSeconds = 86400; store.Save("automatic-budget", budgetPlan);
            budgetPlan.TimeTriggerEnabled = true; budgetPlan.TimeTriggerSeconds = 86401;
            Reject(() => store.Save("automatic-budget", budgetPlan), "enabled rebirth horizon determines automatic flexible validation");
            budgetPlan.TimeTriggerEnabled = false; budgetPlan.StageDurationSeconds = 182800;
            budgetPlan.Stages = new[] { new CustomPlanStage { DurationSeconds = 10000 }, new CustomPlanStage { Percent = 1 }, new CustomPlanStage { ExecutionMode = 1, Percent = 1 } };
            store.Save("automatic-budget", budgetPlan);
            budgetPlan.StageDurationSeconds = 182802;
            Reject(() => store.Save("automatic-budget", budgetPlan), "fixed reservations and flexible shares are included in automatic budget validation");
            read = store.ReadExternalPlan("automatic-budget"); read["StageDurationSeconds"] = 200000;
            Reject(() => store.SaveExternalPlan("automatic-budget", read, read[CustomPlanStore.ExternalUiRevisionField].Value), "external editor receives the same flexible budget validation");
        }

        private static void CheckStageTiming()
        {
            var store = NGUInjector.Main.CustomPlanStore;
            Assert(new CustomPlan().Version == 18, "raw models use fight schedule schema 18");
            var fresh = store.CreatePlan("timing-new");
            Assert(store.Load(fresh).Version == 18, "new native plans use schema 18");
            var external = store.CreateExternalPlan("timing-new-external", null);
            Assert(store.Load(external).Version == 18, "new external plans use schema 18");
            foreach (var name in PlanCatalogNames)
                Assert(store.Load(name).Version == 18 && !store.ReadExternalPlan(name).Linq.Any(item => item.Key == "ProtectedStageTiming"), "built-in has universal timing without an opt-in: " + name);
            foreach (var name in new[] {"24hr"})
            {
                var preset = store.Load(name);
                Assert(preset.StageDurationSeconds == 86400 && preset.StageLoopStartIndex == 4,
                    "production long preset retains its horizon and sustain loop: " + name);
                Assert(preset.Stages.Select(stage => stage.DurationSeconds).SequenceEqual(new[] {3600, 1800, 1800, 10800, 0})
                    && preset.Stages[4].Percent == 1, "production preset protects openers and gives sustain weight one: " + name);
            }

            foreach (var flag in new[] {"false", "true", "null", "0", "\"obsolete\"", "{}", "[]"})
            {
                var path = Path.Combine(_dir, "custom-plans", "timing-old.json");
                File.WriteAllText(path, "{\"Version\":14,\"ProtectedStageTiming\":" + flag + ",\"StageDurationSeconds\":0,\"future\":9223372036854775807,\"Stages\":[{\"DurationSeconds\":200,\"futureRow\":true},{\"Percent\":3}]}");
                var original = File.ReadAllBytes(path);
                var read = store.ReadExternalPlan("timing-old");
                Assert(read["Version"].AsInt == 18 && read["StageDurationSeconds"].AsInt == 0 && !read.Linq.Any(item => item.Key == "ProtectedStageTiming"), "obsolete flag cannot alter timing or survive normalization: " + flag);
                Assert(original.SequenceEqual(File.ReadAllBytes(path)), "normalized read never rewrites source bytes");
                var model = store.Load("timing-old");
                store.Save("timing-old", model);
                Assert(!File.ReadAllText(path).Contains("ProtectedStageTiming"), "native save removes obsolete flag retained in SourceJson");
                read = store.ReadExternalPlan("timing-old");
                read["ProtectedStageTiming"] = JSON.Parse(flag);
                store.SaveExternalPlan("timing-old", read, read[CustomPlanStore.ExternalUiRevisionField].Value);
                var copy = store.CreateExternalPlan("timing-old-copy", JsonFiles.ParseObject(Encoding.UTF8.GetString(original)));
                var nativeCopy = store.CreatePlan("timing-native-copy", model);
                foreach (var name in new[] {"timing-old", copy, nativeCopy})
                {
                    var saved = store.ReadExternalPlan(name);
                    Assert(!saved.Linq.Any(item => item.Key == "ProtectedStageTiming") && saved["Version"].AsInt == 18 && saved["StageDurationSeconds"].AsInt == 0
                        && saved["Stages"][0]["DurationSeconds"].AsInt == 200 && saved["Stages"][1]["Percent"].AsInt == 3,
                        "save/copy preserves universal fixed and zero-flex semantics: " + flag);
                    Assert(saved["future"].Value == "9223372036854775807" && saved["Stages"][0]["futureRow"].AsBool, "obsolete flag cleanup preserves unrelated unknown fields");
                }
            }

            foreach (var version in new[] {5, 7, 14, 15, 16, 17})
            foreach (var budget in new[] {0, 100, 1000})
            {
                var root = PlanWithNguWeights("{\"Version\":" + version + ",\"ProtectedStageTiming\":false,\"StageDurationSeconds\":" + budget
                    + ",\"StageSchedulerEnabled\":false,\"StageLoopStartIndex\":1,\"Stages\":[{\"Name\":\"Time Machine\",\"DurationSeconds\":200},{\"Name\":\"Augments\",\"DurationSeconds\":61},{\"Name\":\"Wandoos\",\"CompletionMode\":1}]}");
                var name = store.CreateExternalPlan("timing-no-migration", root);
                var loaded = store.Load(name);
                Assert(loaded.Version == 18 && loaded.StageDurationSeconds == budget && !loaded.StageSchedulerEnabled && loaded.StageLoopStartIndex == 1
                    && loaded.Stages.Select(stage => stage.DurationSeconds).SequenceEqual(new[] {200, 61, 0}),
                    "old version and false flag never trigger scheduling migration or all-fixed budget rewriting: " + version + "/" + budget);
            }

            var fractional = new CustomPlan {StageDurationSeconds = 1, Stages = new[] {
                new CustomPlanStage {Percent = .0001}, new CustomPlanStage {Percent = .0003}}};
            store.Save("timing-fractional", fractional);
            Assert(store.Load("timing-fractional").Stages.Select(stage => stage.Percent).SequenceEqual(new[] {.0001, .0003}),
                "subsecond flex weights persist without one-second rounding or rescaling");

            foreach (var fixedSeconds in new[] {100, 101})
            {
                var plan = new CustomPlan {StageDurationSeconds = 100, TimeTriggerSeconds = 50,
                    Stages = new[] {new CustomPlanStage {DurationSeconds = fixedSeconds, DurationUnit = "%"},
                        new CustomPlanStage {DurationSeconds = 0, DurationUnit = "h", Percent = 1},
                        new CustomPlanStage {CompletionMode = 1, DurationSeconds = int.MaxValue}}};
                store.Save("protected-zero-flex", plan);
                var saved = store.Load("protected-zero-flex");
                Assert(saved.StageDurationSeconds == 100 && saved.TimeTriggerSeconds == 50 && saved.Stages[0].DurationSeconds == fixedSeconds
                    && saved.Stages[1].DurationSeconds == 0 && saved.Stages[1].Percent == 1 && saved.Stages[2].CompletionMode == 1,
                    "protected exhausted budget retains fixed work, flex weight, AT and independent horizons: " + fixedSeconds);
            }
            var allFixed = new CustomPlan {TimeTriggerEnabled = false, StageDurationSeconds = 100,
                Stages = new[] {new CustomPlanStage {DurationSeconds = 200}, new CustomPlanStage {CompletionMode = 1}}};
            store.Save("protected-all-fixed", allFixed);
            Assert(store.Load("protected-all-fixed").StageDurationSeconds == 100, "protected all-fixed normalization never replaces saved horizon");

            foreach (var triggerEnabled in new[] {false, true})
            foreach (var triggerSeconds in new[] {0, 50})
            foreach (var fixedSeconds in new[] {0, 100})
            {
                var zero = new CustomPlan {StageDurationSeconds = 0,
                    TimeTriggerEnabled = triggerEnabled, TimeTriggerSeconds = triggerSeconds,
                    Stages = new[] {new CustomPlanStage {DurationSeconds = fixedSeconds}, new CustomPlanStage {Percent = 3}}};
                store.Save("protected-zero-budget", zero);
                var zeroRead = store.ReadExternalPlan("protected-zero-budget");
                Assert(zeroRead["StageDurationSeconds"].AsInt == 0
                    && zeroRead["TimeTriggerSeconds"].AsInt == triggerSeconds && zeroRead["TimeTriggerEnabled"].AsBool == triggerEnabled,
                    "protected zero fallback survives native normalization independently of time trigger");
                store.SaveExternalPlan("protected-zero-budget", zeroRead, zeroRead[CustomPlanStore.ExternalUiRevisionField].Value);
                var zeroLoaded = store.Load("protected-zero-budget");
                Assert(zeroLoaded.StageDurationSeconds == 0 && zeroLoaded.Stages[0].DurationSeconds == fixedSeconds
                    && zeroLoaded.Stages[1].Percent == 3, "protected zero CAS preserves fixed work and zero-duration flex weights");
                var zeroCopy = store.CreateExternalPlan("protected-zero-budget-copy", zeroRead);
                Assert(store.Load(zeroCopy).Version == 18 && store.Load(zeroCopy).StageDurationSeconds == 0,
                    "copying a protected zero budget never applies new-plan fallback");
            }
            allFixed.StageDurationSeconds = 0;
            allFixed.TimeTriggerSeconds = 0;
            store.Save("protected-all-fixed-zero", allFixed);
            Assert(store.Load("protected-all-fixed-zero").StageDurationSeconds == 0
                && store.Load("protected-all-fixed-zero").Stages[0].DurationSeconds == 200,
                "zero horizon retains mandatory fixed and AT work without normalizing to their sum");
            Reject(() => store.Save("protected-negative-budget", new CustomPlan {StageDurationSeconds = -1}),
                "protected native budgets must be nonnegative");
            foreach (var version in new[] {5, 14, 16, 17})
            foreach (var invalid in new[] {"-1", "1.5", "2147483648", "null", "\"0\"", "false"})
                Reject(() => store.SaveExternalPlan("protected-invalid-budget", PlanWithNguWeights(
                    "{\"Version\":" + version + ",\"StageDurationSeconds\":" + invalid + "}"), ""),
                    "protected serialized budgets reject invalid values even before version 15: " + invalid);
            var preservedDir = Path.Combine(_dir, "protected-existing-builtins");
            var preserved = new CustomPlanStore(preservedDir);
            preserved.Save("24hr", CustomPlan.TwentyFourHourPlan());
            preserved.Save("infinite", CustomPlan.InfinitePlan());
            foreach (var name in new[] {"24hr", "infinite"})
            {
                var existingPath = Path.Combine(preservedDir, name + ".json");
                var bytes = File.ReadAllBytes(existingPath);
                var reopened = new CustomPlanStore(preservedDir);
                Assert(bytes.SequenceEqual(File.ReadAllBytes(existingPath)) && reopened.Load(name).Version == 18
                    && reopened.Load(name).Stages.All(stage => stage.DurationSeconds == 0), "existing built-ins retain their saved rows without timing migration: " + name);
            }
        }

        private static void CheckStageProgressPersistence()
        {
            var store = NGUInjector.Main.CustomPlanStore;
            const string name = "NativeMode";
            var path = Path.Combine(_dir, "custom-plans", name + ".json");
            var checkpoint = Path.Combine(_dir, "stage-progress-" + JsonFiles.ComputeRevision(Encoding.UTF8.GetBytes(name.ToLowerInvariant())) + ".json");
            var otherCheckpoint = Path.Combine(_dir, "stage-progress-" + JsonFiles.ComputeRevision(Encoding.UTF8.GetBytes("other-mode")) + ".json");
            Assert(!string.Equals(NGUInjector.Main.Settings.CustomPlanFile, name, StringComparison.OrdinalIgnoreCase), "progress fixture is an unselected plan");
            store.Save(name, new CustomPlan());
            File.WriteAllText(checkpoint, "old progress");
            File.WriteAllText(otherCheckpoint, "other progress");
            var plan = store.Load(name);
            plan.Stages[0].Name = "cosmetic rename";
            store.Save(name, plan);
            plan.StageDurationSeconds += 100;
            plan.Stages[0].Percent += 1;
            store.Save(name, plan);
            Assert(File.ReadAllText(checkpoint) == "old progress", "native cosmetic and timing edits retain stage progress");
            var copy = store.CreatePlan("native-mode-copy", plan);
            Assert(store.Load(copy).Version == 18 && File.ReadAllText(checkpoint) == "old progress", "native copy does not clear source progress");

            foreach (var obsolete in new[] {false, true})
            {
                var normalized = store.ReadExternalPlan(name);
                normalized["ProtectedStageTiming"] = obsolete;
                store.SaveExternalPlan(name, normalized, normalized[CustomPlanStore.ExternalUiRevisionField].Value);
                Assert(!store.ReadExternalPlan(name).Linq.Any(item => item.Key == "ProtectedStageTiming") && File.ReadAllText(checkpoint) == "old progress", "obsolete flag cleanup never clears saved progress: " + obsolete);
                Assert(File.ReadAllText(otherCheckpoint) == "other progress", "saving never clears another plan's checkpoint");
            }
            File.WriteAllText(checkpoint, "must survive failed writes");
            var original = File.ReadAllBytes(path);
            plan.Stages[0].Name = "failed edit";
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                Reject(() => store.Save(name, plan), "failed native atomic write rejects");
            Assert(original.SequenceEqual(File.ReadAllBytes(path)) && File.ReadAllText(checkpoint) == "must survive failed writes",
                "failed native atomic writes never clear checkpoint or mutate persisted plan");
            var read = store.ReadExternalPlan(name);
            read["ProtectedStageTiming"] = false;
            store.SaveExternalPlan(name, read, read[CustomPlanStore.ExternalUiRevisionField].Value);
            Assert(File.ReadAllText(checkpoint) == "must survive failed writes" && !File.ReadAllText(path).Contains("ProtectedStageTiming"), "native external CAS cleanup retains progress");

            const string legacyName = "native-legacy-mode";
            var legacyPath = Path.Combine(_dir, "custom-plans", legacyName + ".json");
            var legacyCheckpoint = Path.Combine(_dir, "stage-progress-" + JsonFiles.ComputeRevision(Encoding.UTF8.GetBytes(legacyName)) + ".json");
            File.WriteAllText(legacyPath, "{\"Version\":14}");
            File.WriteAllText(legacyCheckpoint, "legacy progress");
            var legacy = store.Load(legacyName);
            store.Save(legacyName, legacy);
            Assert(File.ReadAllText(legacyCheckpoint) == "legacy progress", "schema normalization preserves existing saved progress");
            File.Delete(checkpoint);
            Directory.CreateDirectory(checkpoint);
            store.Save(name, store.Load(name));
            Assert(Directory.Exists(checkpoint), "saving does not inspect or delete checkpoint sidecars");
            Directory.Delete(checkpoint);
        }

        private static void CheckStageProgressCreationAndDeletion()
        {
            var store = NGUInjector.Main.CustomPlanStore;
            const string name = "RecreatedPlan";
            var path = Path.Combine(_dir, "custom-plans", name + ".json");
            Func<string, string> checkpointFor = planName => Path.Combine(_dir, "stage-progress-"
                + JsonFiles.ComputeRevision(Encoding.UTF8.GetBytes(planName.ToLowerInvariant())) + ".json");
            var checkpoint = checkpointFor(name);
            const string completed = "{\"completed\":true,\"stageIndex\":1}";
            var sourceName = store.CreatePlan("checkpoint-source");
            var source = store.Load(sourceName);
            File.WriteAllText(checkpointFor(sourceName), completed);

            File.WriteAllText(checkpoint, completed);
            store.Save(name, source);
            Assert(store.PlanExists(name) && !File.Exists(checkpoint), "new native save clears a destination's old completed checkpoint");
            foreach (var external in new[] {false, true})
            {
                File.WriteAllText(checkpoint, completed);
                using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (external) Reject(() => store.DeleteExternalPlan(name), "failed external deletion rejects before forgetting progress");
                    else Reject(() => store.DeletePlan(name), "failed native deletion rejects before forgetting progress");
                }
                Assert(store.PlanExists(name) && File.ReadAllText(checkpoint) == completed, "failed deletion preserves the plan and its progress");
                if (external) store.DeleteExternalPlan(name); else store.DeletePlan(name);
                Assert(!store.PlanExists(name) && !File.Exists(checkpoint), "successful deletion forgets the removed plan's completed checkpoint");
                File.WriteAllText(checkpoint, completed);
                var recreated = external
                    ? store.CreateExternalPlan(name, store.ReadExternalPlan(sourceName))
                    : store.CreatePlan(name, source);
                Assert(recreated == name && !File.Exists(checkpoint), "recreated copy cannot inherit stale destination progress");
                Assert(File.ReadAllText(checkpointFor(sourceName)) == completed, "copy and destination cleanup preserve source progress");
            }

            var collisionCheckpoint = checkpointFor(name + "-2");
            File.WriteAllText(checkpoint, completed);
            File.WriteAllText(collisionCheckpoint, completed);
            var collision = store.CreatePlan(name, source);
            Assert(collision == name + "-2" && !File.Exists(collisionCheckpoint) && File.ReadAllText(checkpoint) == completed,
                "collision-resolved copies clear only the actual new destination checkpoint");

            store.DeleteExternalPlan(name);
            File.WriteAllText(checkpoint, completed);
            store.SaveExternalPlan(name, store.ReadExternalPlan(sourceName), "");
            Assert(store.PlanExists(name) && !File.Exists(checkpoint), "new external CAS save clears stale destination progress");
            File.WriteAllText(checkpoint, completed);
            var existing = File.ReadAllBytes(path);
            Reject(() => store.SaveExternalPlan(name, store.ReadExternalPlan(name), new string('0', 64)), "stale CAS never forgets progress");
            Assert(existing.SequenceEqual(File.ReadAllBytes(path)) && File.ReadAllText(checkpoint) == completed, "failed CAS preserves both plan and checkpoint");

            store.DeletePlan(name);
            File.WriteAllText(checkpoint, completed);
            Directory.CreateDirectory(path);
            Reject(() => store.Save(name, source), "failed atomic creation does not forget destination progress");
            Assert(Directory.Exists(path) && File.ReadAllText(checkpoint) == completed, "new-path write failure preserves stale progress until a plan is actually created");
            Directory.Delete(path);
            File.Delete(checkpoint);
            Directory.CreateDirectory(checkpoint);
            Reject(() => store.Save(name, source), "creation reports checkpoint cleanup failure");
            Assert(store.PlanExists(name) && Directory.Exists(checkpoint), "creation cleanup runs only after the new plan is persisted");
            Reject(() => store.DeletePlan(name), "deletion reports checkpoint cleanup failure");
            Assert(!store.PlanExists(name) && Directory.Exists(checkpoint), "deletion cleanup runs only after the plan is removed");
            Directory.Delete(checkpoint);
        }

        private static void CheckAutoIronPillEnabled()
        {
            var store = NGUInjector.Main.CustomPlanStore;
            Assert(new CustomPlan().AutoIronPillEnabled, "new plans default automatic Iron Pill to true");
            foreach (var name in PlanCatalogNames)
                Assert(store.Load(name).AutoIronPillEnabled == (name == "24hr"), "only 24hr inherits automatic Iron Pill: " + name);

            var path = Path.Combine(_dir, "custom-plans", "iron-pill-optout.json");
            File.WriteAllText(path, "{\"Version\":14,\"Name\":\"iron-pill-optout\",\"future\":42,\"Stages\":[{\"FightOnEntry\":true}]}");
            var original = File.ReadAllText(path);
            Assert(store.Load("iron-pill-optout").AutoIronPillEnabled, "absent legacy Iron Pill flag loads as true");
            var read = store.ReadExternalPlan("iron-pill-optout");
            Assert(read["AutoIronPillEnabled"].IsBoolean && read["AutoIronPillEnabled"].AsBool,
                "normalized legacy reads explicitly default automatic Iron Pill to true");
            Assert(File.ReadAllText(path) == original, "Iron Pill normalization never rewrites on read");
            store.SaveExternalPlan("iron-pill-optout", read, read[CustomPlanStore.ExternalUiRevisionField].Value);
            Assert(store.Load("iron-pill-optout").AutoIronPillEnabled, "normalized legacy CAS save retains true");
            var plan = store.Load("iron-pill-optout");
            plan.AutoIronPillEnabled = false;
            store.Save("iron-pill-optout", plan);
            read = store.ReadExternalPlan("iron-pill-optout");
            Assert(read["AutoIronPillEnabled"].IsBoolean && !read["AutoIronPillEnabled"].AsBool,
                "native false overrides retained source true through normalization and serialization");
            store.SaveExternalPlan("iron-pill-optout", read, read[CustomPlanStore.ExternalUiRevisionField].Value);
            Assert(!store.Load("iron-pill-optout").AutoIronPillEnabled, "explicit false survives CAS save/load");
            var copy = store.CreatePlan("iron-pill-optout-copy", store.Load("iron-pill-optout"));
            var copied = store.Load(copy);
            Assert(!copied.AutoIronPillEnabled && copied.Stages[0].FightSchedule.Id == "legacy-entry-v1-0" && copied.SourceJson["future"].AsInt == 42,
                "native plan clone preserves Iron Pill opt-out, migrated fight schedule, and unknown fields");
            var externalCopy = store.CreateExternalPlan("iron-pill-optout-external-copy", read);
            Assert(!store.Load(externalCopy).AutoIronPillEnabled, "external plan copy preserves Iron Pill opt-out");
            copied.AutoIronPillEnabled = true;
            store.Save(copy, copied);
            Assert(store.Load(copy).AutoIronPillEnabled, "explicit true overrides retained source false");
            Assert(store.Load(copy).Version == 18, "Iron Pill flag is preserved in the current schema");
            foreach (var invalid in new[] {"null", "0", "1", "\"false\"", "{}", "[]"})
            {
                var bad = store.ReadExternalPlan("iron-pill-optout");
                bad["AutoIronPillEnabled"] = JSON.Parse(invalid);
                var before = File.ReadAllText(path);
                Reject(() => store.SaveExternalPlan("iron-pill-optout", bad, bad[CustomPlanStore.ExternalUiRevisionField].Value),
                    "automatic Iron Pill rejects non-boolean " + invalid);
                Assert(File.ReadAllText(path) == before, "invalid Iron Pill flag never changes saved plan");
                Reject(() => store.CreateExternalPlan("iron-pill-invalid", bad), "plan creation rejects invalid Iron Pill flag " + invalid);
                Assert(!store.PlanExists("iron-pill-invalid"), "invalid Iron Pill flag cannot create a plan");
            }
        }

        private static void CheckStageDurations(NGUInjector.Main main)
        {
            var store = NGUInjector.Main.CustomPlanStore;
            var legacyPath = Path.Combine(_dir, "custom-plans", "duration-legacy.json");
            File.WriteAllText(legacyPath, "{\"Version\":14,\"Name\":\"duration-legacy\",\"NguGoal\":1,\"future\":{\"big\":9223372036854775807},\"Stages\":[{\"Name\":\"weighted\",\"Percent\":0.25,\"futureRow\":\"keep\"}]}");
            var original = File.ReadAllBytes(legacyPath);
            var read = store.ReadExternalPlan("duration-legacy");
            Assert(read["Version"].AsInt == 18 && !read.Linq.Any(item => item.Key == "EnergyNguWeights" || item.Key == "MagicNguWeights" || item.Key == "NguGoal"),
                "version 14 read returns 18 without retired plan-owned NGU configuration");
            Assert(read["Stages"][0]["DurationSeconds"].AsInt == 0 && read["Stages"][0]["DurationUnit"].Value == "%" && read["Stages"][0]["Percent"].AsDouble == .25, "legacy duration defaults retain canonical positive weights");
            Assert(original.SequenceEqual(File.ReadAllBytes(legacyPath)), "duration migration read never writes source");
            read["Stages"][0]["DurationSeconds"] = 61;
            read["Stages"][0]["DurationUnit"] = "min";
            store.SaveExternalPlan("duration-legacy", read, read["__externalUiRevision"].Value);
            var loaded = store.Load("duration-legacy");
            Assert(loaded.StageDurationSeconds == 1800 && loaded.Stages[0].DurationSeconds == 61 && loaded.Stages[0].DurationUnit == "min", "all-fixed plan preserves saved budget and exact seconds, independent of display unit");
            store.Save("duration-legacy", loaded);
            var saved = store.ReadExternalPlan("duration-legacy");
            Assert(saved["future"]["big"].Value == "9223372036854775807" && saved["Stages"][0]["futureRow"].Value == "keep", "duration Save/CAS retains source unknown fields");
            Reject(() => store.SaveExternalPlan("duration-legacy", read, read["__externalUiRevision"].Value), "duration CAS rejects stale original bytes");
            foreach (var unit in new[] {"%", "s", "min", "h"})
            {
                var plan = new CustomPlan { StageDurationSeconds = 100, Stages = new[] {
                    new CustomPlanStage {DurationSeconds = 20, DurationUnit = unit}, new CustomPlanStage {Percent = 3d} } };
                store.Save("duration-" + unit.Replace("%", "percent"), plan);
                Assert(plan.StageDurationSeconds == 100 && plan.Stages[0].DurationSeconds == 20, "display unit never changes canonical seconds: " + unit);
            }
            foreach (var invalid in new[] {"-1", "1.5", "2147483648", "\"60\"", "null"})
            {
                var root = PlanWithNguWeights("{\"Version\":16,\"Stages\":[{\"DurationSeconds\":" + invalid + "}]}");
                Reject(() => store.SaveExternalPlan("duration-invalid", root, ""), "reject invalid canonical seconds " + invalid);
                Assert(!store.PlanExists("duration-invalid"), "duration rejection cannot create a file " + invalid);
            }
            foreach (var total in new[] {100, 101})
            {
                var root = PlanWithNguWeights("{\"Version\":16,\"StageDurationSeconds\":100,\"Stages\":[{\"DurationSeconds\":" + total + "},{\"Percent\":1}]}");
                var name = "duration-zero-flex-" + total;
                store.SaveExternalPlan(name, root, "");
                Assert(store.Load(name).StageDurationSeconds == 100 && store.Load(name).Stages[0].DurationSeconds == total,
                    "exhausted weighted budget preserves fixed work and allows zero flex " + total);
            }
            Reject(() => store.Save("duration-overflow", new CustomPlan { Stages = new[] {
                new CustomPlanStage {DurationSeconds = int.MaxValue}, new CustomPlanStage {DurationSeconds = 1} } }), "fixed duration sum cannot overflow");
            Reject(() => store.Save("duration-unit-invalid", new CustomPlan { Stages = new[] { new CustomPlanStage {DurationUnit = "days"} } }), "unknown display unit rejected");
            foreach (var invalidBudget in new[] {"2147483648", "1.5", "null"})
                Reject(() => store.SaveExternalPlan("duration-budget-invalid", PlanWithNguWeights(
                    "{\"Version\":16,\"StageDurationSeconds\":" + invalidBudget + "}"), ""), "reference duration cannot overflow or truncate " + invalidBudget);
            main.Process("duration-command", Envelope("duration-command", "savePlan", PlanArgs("duration-mixed", PlanWithNguWeights(
                "{\"Version\":16,\"StageDurationSeconds\":100,\"Stages\":[{\"DurationSeconds\":20,\"DurationUnit\":\"s\"},{\"Percent\":1},{\"Percent\":3}]}"))));
            Assert(Result("duration-command")["success"].AsBool && Result("duration-command")["data"]["runtime"]["settings"].IsObject, "mixed duration command saves and returns post-effect runtime");
            var atMixed = new CustomPlan { StageDurationSeconds = 100, Stages = new[] {
                new CustomPlanStage {CompletionMode = 1, DurationSeconds = int.MaxValue, Percent = double.MaxValue},
                new CustomPlanStage {DurationSeconds = 20}, new CustomPlanStage {Percent = 1}, new CustomPlanStage {Percent = 3} } };
            store.Save("duration-at-mixed", atMixed);
            var atLoaded = store.Load("duration-at-mixed");
            Assert(atLoaded.StageDurationSeconds == 100 && atLoaded.Stages[0].DurationSeconds == int.MaxValue,
                "AT dormant seconds neither consume mixed budget nor lose their stored value");
            Assert(atLoaded.Stages[0].Percent == double.MaxValue, "AT dormant weight remains finite stored metadata without joining the timed weight total");
            var atFixed = new CustomPlan { StageDurationSeconds = 100, Stages = new[] {
                new CustomPlanStage {CompletionMode = 1}, new CustomPlanStage {DurationSeconds = 10}, new CustomPlanStage {DurationSeconds = 25} } };
            store.Save("duration-at-fixed", atFixed);
            Assert(atFixed.StageDurationSeconds == 100, "all-fixed timed budget is preserved even with a percent-valued AT row");
            var atOnly = new CustomPlan { StageDurationSeconds = 1, Stages = new[] {
                new CustomPlanStage {CompletionMode = 1, DurationSeconds = int.MaxValue, Percent = double.MaxValue},
                new CustomPlanStage {CompletionMode = 1, DurationSeconds = int.MaxValue, Percent = double.MaxValue} } };
            store.Save("duration-at-only", atOnly);
            var atOnlyRead = store.ReadExternalPlan("duration-at-only");
            Assert(atOnlyRead["StageDurationSeconds"].AsInt == 1 && atOnlyRead["Stages"].Count == 2,
                "all-AT plans validate without inventing a timed pool or overflowing dormant totals");
            foreach (var invalid in new[] {"-1", "1.5", "2147483648", "\"60\"", "null"})
                Reject(() => store.SaveExternalPlan("duration-at-invalid", PlanWithNguWeights(
                    "{\"Version\":16,\"Stages\":[{\"CompletionMode\":1,\"DurationSeconds\":" + invalid + "}]}"), ""),
                    "AT dormant duration still enforces stored integer type and bounds " + invalid);
            Reject(() => store.Save("duration-at-unit-invalid", new CustomPlan { Stages = new[] {
                new CustomPlanStage {CompletionMode = 1, DurationUnit = "days"} } }), "AT dormant display unit still enforces the schema");
        }
    }
}
