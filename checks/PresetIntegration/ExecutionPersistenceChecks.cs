using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public static partial class PresetIntegrationChecks
{
    private static object ExecutionReviewApplyArgs(object preview) => JsonParse("{\"token\":" + Json(preview, "token")
        + ",\"checkpointRevision\":" + Json(preview, "checkpointRevision") + "}");

    private static void CheckExecutionPersistence()
    {
        var before = Assertions;
        foreach (var autoUpdate in new[] { false, true }) CheckCurrentStageResume(autoUpdate);
        CheckQuickSessionSelection();
        CheckFocusSessionReview();
        CheckExecutionLifecycleRevocation();
        foreach (var autoUpdate in new[] { false, true }) CheckBusyStageCheckpoint(autoUpdate);
        CheckExecutionRebirthRollover();
        CheckAutomaticRestartProgress();
        CheckInvalidOuterExecutionCheckpoint();
        CheckExplicitExecutionRestart();
        CheckScopedExecutionRecovery();
        CheckForeignNativeSettingsReviewScope();
        Console.WriteLine("Execution persistence: " + (Assertions - before) + " native assertions.");
    }

    private static void CheckCurrentStageResume(bool autoUpdate)
    {
        var plan = SetupResourceCoordination(true, autoUpdate, "none");
        var acquired = Get(Get(MainInstance, "_globalPresetReceipt"), "Policy");
        var acquisition = Get(acquired, "Acquisition");
        Set(MainInstance, "_customStageFightKey", "consumed-entry-fight");
        Equal(true, CallMain("SaveProtectedStageProgress", true), "save original entry and consumed fight");
        var path = (string)Property(MainInstance, "ProtectedCheckpointPath"); var bytes = File.ReadAllText(path);
        var state = Invoke(Get(MainInstance, "_protectedScheduler"), "ExportState");
        var root = (string)Get(MainType, "_dir");
        var retired = new[] { "lifecycle-route-commitments-v1.json", "route-execution-v1.json", "execution-selection.json",
            "execution-fight-prep-selection.json", "execution-quick-selection.json", "execution-focus-selection.json", "execution-manual-selection.json" };
        var ledger = Path.Combine(root, "finite-actions-v1"); Directory.CreateDirectory(ledger);
        File.WriteAllText(Path.Combine(ledger, "unknown.json"), "{malformed retired invocation");
        File.SetAttributes(Path.Combine(ledger, "unknown.json"), FileAttributes.ReadOnly);
        try
        {
            foreach (var name in retired)
            {
                File.WriteAllText(Path.Combine(root, name), "{malformed retired record");
                File.SetAttributes(Path.Combine(root, name), FileAttributes.ReadOnly);
            }
            Set(MainInstance, "_globalPresetReceipt", null);
            Set(MainInstance, "_globalPresetApplyingPolicy", null);
            ((IDictionary)Get(MainInstance, "_globalPresetPolicies")).Clear();
            var searches = PresetSearches; var writes = ResourceTransfers; var swaps = Get(typeof(Program), "Swaps");
            CallMain("InitializeProtectedStageProgress", plan);
            Equal(null, Get(MainInstance, "_protectedOuterCheckpointIssue"), "old action/route/prep files do not corrupt startup");
            Equal(null, Get(MainInstance, "_protectedSelectedPolicyIssue"), "valid frozen stage resumes without mandatory review/AutoUpdate=" + autoUpdate);
            Equal(false, Property(Json(CallMain("GetExecutionPersistenceRuntime"), "reviewRequired"), "AsBool"), "old scope creates no review quarantine");
            Equal(acquisition, Get(Get(MainInstance, "_globalPresetApplyingPolicy"), "Acquisition"), "restore retains acquired intent, not action history");
            Equal(null, Get(MainInstance, "_globalPresetReceipt"), "restore does not fabricate native application evidence");
            Equal(bytes, File.ReadAllText(path), "loading does not rewrite earned progress");
            Equal(writes, ResourceTransfers, "loading never writes native accounting");
            Equal(swaps, Get(typeof(Program), "Swaps"), "loading never equips gear");
            var library = Get(Get(MainType, "CustomPlanStore"), "<PresetLibrary>k__BackingField");
            var edited = Invoke(library, "ReadExternalLibrary");
            var definition = ((IEnumerable)GetChildren(Json(edited, "Presets"))).Cast<object>()
                .Single(x => Equals(Property(Json(x, "Id"), "Value"), Get(Get(acquired, "Preset"), "Id")));
            SetJson(definition, "Weights", Json(JsonParse("{\"v\":[0,0,1,0,0,0,0,0,0,0,0,0,0,0]}"), "v"));
            Invoke(library, "SaveExternalLibrary", edited, Property(Json(edited, "__externalUiRevision"), "Value"));
            CallMain("ReconcileCustomResources", plan, true);
            var resumed = Get(Get(MainInstance, "_globalPresetReceipt"), "Policy");
            Check(resumed != null, "ordinary native reconciliation resumes without old-scope approval");
            foreach (var field in new[] { "Acquisition", "Definition", "Horizon" })
                Equal(Get(acquired, field), Get(resumed, field), "resume preserves selected intent/" + field);
            Check(((double[])Get(Get(acquired, "Plan"), "Normalization")).SequenceEqual((double[])Get(Get(resumed, "Plan"), "Normalization")), "resume preserves frozen scoring references");
            var restored = Invoke(Get(MainInstance, "_protectedScheduler"), "ExportState");
            foreach (var field in new[] { "entry", "cycle", "enteredAt", "expectedEnd", "durations", "earned", "completed", "skipped", "autoEnergyPool", "autoMagicPool", "autoAugmentsCompleted", "autoAtCompleted", "autoWandoosStarted" })
                Equal(Json(state, field).ToString(), Json(restored, field).ToString(), "resume preserves earned progress/" + field);
            Equal("consumed-entry-fight", Get(MainInstance, "_customStageFightKey"), "normal resume cannot repeat consumed entry fight");
            Equal(searches, PresetSearches, "normal resume never searches replacement gear/support");
            var telemetry = CallMain("GetExecutionStateRuntime");
            Equal(false, ((IDictionary)Get(telemetry, "m_Dict")).Contains("pendingActions"), "telemetry does not project retired action history");
            foreach (var name in retired) Equal("{malformed retired record", File.ReadAllText(Path.Combine(root, name)), "old record is inert and need not be cleaned/" + name);
            Equal("{malformed retired invocation", File.ReadAllText(Path.Combine(ledger, "unknown.json")), "old unknown evidence is untouched");
        }
        finally
        {
            foreach (var name in retired) File.SetAttributes(Path.Combine(root, name), FileAttributes.Normal);
            File.SetAttributes(Path.Combine(ledger, "unknown.json"), FileAttributes.Normal);
        }
    }

    private static void CheckQuickSessionSelection()
    {
        SetupInlineStagePlan();
        var settings = Get(MainType, "Settings");
        Set(settings, "_quickLoadout", new[] { 552 }); Set(settings, "_quickDiggers", new int[0]);
        var inventory = Get(Character, "inventory"); var original = Get(inventory, "head");
        var searches = PresetSearches;
        CallMain("ToggleQuickExecutionClaim");
        var claim = Get(MainInstance, "_quickExecutionClaim");
        Equal(552, Get(Get(inventory, "head"), "id"), "F8 applies the exact selected native gear");
        Check(Get(claim, "ExplicitReturnBaseline") != null, "F8 retains its same-session explicit return baseline");
        Equal(false, File.Exists(Path.Combine((string)Get(MainType, "_dir"), "execution-quick-selection.json")), "F8 writes no restart intent");
        CallMain("ToggleQuickExecutionClaim");
        Check(ReferenceEquals(original, Get(inventory, "head")), "second same-session F8 returns to its explicit baseline");
        CallMain("ToggleQuickExecutionClaim");
        CallMain("ResetCustomAutomationRuntime");
        Equal(null, Get(MainInstance, "_quickExecutionClaim"), "reset discards Quick return authority without native cleanup");
        Equal(552, Get(Get(inventory, "head"), "id"), "discarded Quick leaves current native equipment untouched");
        Equal(0, ((IDictionary)Get(MainInstance, "_executionSelections")).Count, "reset clears bounded live selections");
        Equal(searches, PresetSearches, "F8 and reset perform no replacement search");
    }

    private static void CheckFocusSessionReview()
    {
        var plan = SetupInlineStagePlan(); RunVerifiedInline(plan, "live focus baseline");
        var focus = Enum.Parse(Plugin.GetType("NGUInjector.CustomFocusGoal", true), "MagicNgu");
        CallMain("ApplyCustomFocusGoal", focus);
        Equal("applied", CallMain("GetCustomFocusStatus"), "focus is acquired in this session");
        var acquisition = Get(Get(Get(MainInstance, "_focusSetupSelection"), "Desired"), "Acquisition");
        var state = Invoke(Get(MainInstance, "_protectedScheduler"), "ExportState");
        Set(Get(MainType, "Settings"), "_manageEnergy", false);
        var energy = CallMain("GlobalPresetLaneState", true);
        InjectNativeReviewConflict(false);
        var preview = CallMain("PreviewExecutionRebind", JsonParse("{}"));
        Equal(false, CallMain("ValidateExecutionGrant", Get(Get(MainInstance, "_executionRebindReview"), "Grant"), Domain("Energy")), "live Magic review does not acquire unmanaged Energy");
        CallMain("ApplyExecutionRebind", ExecutionReviewApplyArgs(preview));
        CallMain("RunCustomSustainedFocusAllocation", true);
        Equal("applied", CallMain("GetCustomFocusStatus"), "explicit review still recovers a real foreign focus allocation");
        Equal(energy, CallMain("GlobalPresetLaneState", true), "live focus review preserves unmanaged Energy");
        Equal(acquisition, Get(Get(Get(MainInstance, "_focusSetupSelection"), "Desired"), "Acquisition"), "live focus keeps the original selection");
        foreach (var field in new[] { "entry", "enteredAt", "expectedEnd", "earned", "completed", "autoWandoosStarted" })
            Equal(Json(state, field).ToString(), Json(Invoke(Get(MainInstance, "_protectedScheduler"), "ExportState"), field).ToString(), "focus review preserves paused stage/" + field);
        Equal(false, File.Exists(Path.Combine((string)Get(MainType, "_dir"), "execution-focus-selection.json")), "focus keeps no restart file");
        CallMain("StopCustomFocusGoal");
        Equal(false, ((IDictionary)Get(MainInstance, "_executionSelections")).Contains("focus"), "focus stop clears only memory");
    }

    private static void CheckExecutionRebirthRollover()
    {
        foreach (var mode in new[] { "crash-before-hook", "verified-hook", "same-run-checkpoint", "backward", "corrupt" })
        {
            var plan = SetupInlineStagePlan(); RunVerifiedInline(plan, "rebirth rollover baseline");
            Equal(true, CallMain("SaveProtectedStageProgress", true), "original run has a durable checkpoint");
            var path = (string)Property(MainInstance, "ProtectedCheckpointPath"); var original = File.ReadAllText(path);
            var stats = Get(Character, "stats"); var rebirth = (long)Get(stats, "rebirthNumber");
            var total = (double)Get(Get(Character, "totalPlaytime"), "totalseconds");
            if (mode == "backward") Set(stats, "rebirthNumber", rebirth - 1);
            else
            {
                Set(stats, "rebirthNumber", rebirth + 1);
                Set(Get(Character, "rebirthTime"), "totalseconds", 0d);
                Set(Get(Character, "totalPlaytime"), "totalseconds", total + 1);
            }
            if (mode == "corrupt") File.WriteAllText(path, "{corrupt stage progress");
            if (mode == "verified-hook") CallMain("CommitExecutionRebirthCheckpoint");
            else CallMain("InitializeProtectedStageProgress", plan);
            if (mode == "backward" || mode == "corrupt")
            {
                Check(Get(MainInstance, "_protectedOuterCheckpointIssue") != null, mode + " requires explicit recovery");
                Equal(mode == "corrupt" ? "{corrupt stage progress" : original, File.ReadAllText(path), mode + " preserves original evidence");
                continue;
            }
            Equal(null, Get(MainInstance, "_protectedOuterCheckpointIssue"), mode + " accepts exactly one native forward rebirth");
            Equal((rebirth + 1).ToString(), Property(Json(JsonParse(File.ReadAllText(path)), "rebirth"), "Value"), mode + " writes the current run checkpoint");
            var scheduler = Get(MainInstance, "_protectedScheduler");
            Check(((double[])Property(scheduler, "EarnedSeconds")).All(x => x == 0), mode + " does not import old-run credit");
            if (mode == "same-run-checkpoint")
            {
                var state = Invoke(scheduler, "ExportState");
                foreach (var field in new[] { "earned", "applied", "elapsedByStage" }) SetJson(state, field, Json(JsonParse("{\"v\":[7,0]}"), "v"));
                foreach (var field in new[] { "stageElapsed", "lastElapsed" }) SetJson(state, field, Json(JsonParse("{\"v\":7}"), "v"));
                Set(Get(Character, "rebirthTime"), "totalseconds", 7d);
                Set(Get(Character, "totalPlaytime"), "totalseconds", total + 8);
                Set(MainInstance, "_protectedLastElapsed", 7d); Set(MainInstance, "_protectedLastTotal", total + 8);
                Equal(true, Invoke(scheduler, "RestoreState", plan, state), "same-run earned credit is valid");
                Equal(true, CallMain("SaveProtectedStageProgress", true), "new run has already earned durable credit");
                var bytes = File.ReadAllText(path);
                Set(MainInstance, "_protectedRebirthNumber", rebirth);
                CallMain("CommitExecutionRebirthCheckpoint");
                Equal(bytes, File.ReadAllText(path), "late rebirth hook cannot remove a same-run checkpoint");
                Equal(7d, ((double[])Property(Get(MainInstance, "_protectedScheduler"), "EarnedSeconds"))[0], "late hook retains same-run earned credit");
            }
        }
        foreach (var autoUpdate in new[] { false, true })
        {
            var presetPlan = SetupResourceCoordination(true, autoUpdate, "none");
            var oldAcquisition = Get(Get(Get(MainInstance, "_globalPresetReceipt"), "Policy"), "Acquisition");
            var currentRebirth = (long)Get(Get(Character, "stats"), "rebirthNumber");
            Set(Get(Character, "stats"), "rebirthNumber", currentRebirth + 1);
            Set(Get(Character, "rebirthTime"), "totalseconds", 0d);
            CallMain("CommitExecutionRebirthCheckpoint");
            CallMain("ResetCustomAutomationRuntime");
            CallMain("InitializeProtectedStageProgress", presetPlan);
            Equal(null, Get(MainInstance, "_protectedSelectedPolicyIssue"), "fresh rebirth checkpoint with no acquired selection is not quarantined");
            Equal(null, Get(MainInstance, "_protectedSelectedPolicy"), "fresh run cannot import the preceding run's frozen choice");
            var searches = PresetSearches; var writes = ResourceTransfers; var swaps = Get(typeof(Program), "Swaps");
            CallMain("ReconcileCustomResources", presetPlan, true);
            var newReceipt = Get(MainInstance, "_globalPresetReceipt");
            if (!autoUpdate)
            {
                Equal(null, newReceipt, "AutoUpdate off cannot silently acquire a missing new-run policy");
                Check(((string)Get(MainInstance, "_globalPresetWaitingReason")).Contains("No reusable policy"), "missing cache waits for explicit Preview + Apply");
                Equal(searches, PresetSearches, "AutoUpdate off never searches after rebirth");
                Equal(writes, ResourceTransfers, "missing new-run selection writes no native accounting");
                Equal(swaps, Get(typeof(Program), "Swaps"), "missing new-run selection equips no old gear");
                continue;
            }
            Check(newReceipt != null, "first ordinary pass acquires the new run's selection normally; " + CallMain("GetResourceCoordinationRuntime")
                + "; " + Get(MainInstance, "_globalPresetReconcileException"));
            Check(!Equals(oldAcquisition, Get(Get(newReceipt, "Policy"), "Acquisition")), "new run uses a new acquisition, not a replayed old effect");
        }
    }

    private static void CheckAutomaticRestartProgress()
    {
        var plan = SetupInlineStagePlan();
        CallMain("InitializeProtectedStageProgress", plan);
        File.Delete((string)Property(MainInstance, "ProtectedCheckpointPath"));
        var stage = ((Array)Get(plan, "Stages")).GetValue(0); Set(stage, "ExecutionMode", 1);
        CallMain("InitializeProtectedStageProgress", plan);
        CallMain("GetProtectedStage", plan);
        var scheduler = Get(MainInstance, "_protectedScheduler");
        Set(scheduler, "<AutoEnergyPool>k__BackingField", 123L); Set(scheduler, "<AutoMagicPool>k__BackingField", 456L);
        Set(scheduler, "<AutoAugmentsCompleted>k__BackingField", true); Set(scheduler, "<AutoAtCompleted>k__BackingField", true);
        Set(scheduler, "<AutoWandoosStarted>k__BackingField", true); Set(scheduler, "<AutoCleanupRecoveryRequired>k__BackingField", true);
        var entry = Property(MainInstance, "AutomaticRecoveryEntry");
        Set(MainInstance, "_automaticStageSetupEntry", entry); Set(MainInstance, "_automaticCancelledEntry", entry);
        Set(MainInstance, "_automaticStageSetupStarted", (double)Property(MainInstance, "CombatPrepClock") - 20);
        Equal(true, CallMain("SaveProtectedStageProgress", true), "interrupted automatic stage saves original phase facts");
        var state = Invoke(scheduler, "ExportState");
        CallMain("InitializeProtectedStageProgress", plan);
        scheduler = Get(MainInstance, "_protectedScheduler");
        Equal(null, Get(MainInstance, "_protectedOuterCheckpointIssue"), "cancelled transient cleanup does not quarantine the stage");
        Equal(false, Property(scheduler, "AutoCleanupRecoveryRequired"), "restart imports no historical cleanup authority");
        Equal(null, Get(MainInstance, "_automaticCancelledEntry"), "restart cannot convert cancellation into completion");
        var restored = Invoke(scheduler, "ExportState");
        foreach (var field in new[] { "entry", "cycle", "enteredAt", "expectedEnd", "earned", "completed", "skipped", "autoEnergyPool", "autoMagicPool", "autoAugmentsCompleted", "autoAtCompleted", "autoWandoosStarted" })
            Equal(Json(state, field).ToString(), Json(restored, field).ToString(), "restart retains original automatic budget/progress/" + field);
    }

    private static void CheckExecutionLifecycleRevocation()
    {
        SetupInlineStagePlan();
        var grant = CallMain("ResolveExecutionGrant", "lifecycle-check", "existing", Domain("Energy"), (Func<bool>)(() => true), null, null);
        var boss = Get(Character, "bossController"); var head = Get(Get(Character, "inventory"), "head");
        Set(boss, "isFighting", true);
        Throws(() => CallMain("QuiesceExecution", "test bounded quiesce"), "Quiesced", "native busy guard still withholds prerequisite retirement");
        Equal(false, CallMain("ValidateExecutionGrant", grant, Domain("Energy")), "quiescence revokes previous callbacks");
        Check(ReferenceEquals(head, Get(Get(Character, "inventory"), "head")), "native boss prerequisites stay intact");
        var transfers = ResourceTransfers;
        CallMain("QuickStuff"); CallMain("AutomationRoutine"); CallMain("ReconcileCustomResources", null, true);
        Equal(transfers, ResourceTransfers, "quiesced callbacks cannot write resources");
        Set(boss, "isFighting", false);
        CallMain("QuiesceExecution", "retry after observed completion");
        Equal("Quiescing", Get(MainInstance, "_executionLifecycle").ToString(), "retirement does not bypass lifecycle admission");
        Equal(transfers, ResourceTransfers, "retirement never clears ordinary allocations");
        Equal(0, Directory.GetFiles((string)Get(MainType, "_dir"), "lifecycle-*").Length, "quiescence accumulates no lifecycle journal or archives");
    }

    private static void CheckBusyStageCheckpoint(bool autoUpdate)
    {
        foreach (var busy in new[] { "isFighting", "nukeBoss" })
        {
            var label = "busy checkpoint/" + busy + "/AutoUpdate=" + autoUpdate;
            var plan = SetupResourceCoordination(true, autoUpdate, "none");
            ListField(Get(Character, "hacks"), "hacks", 16);
            Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), label + " starts from verified native application");
            // Earn through ordinary verified service and both real clock boundaries,
            // rather than writing earned/applied arrays or arming the scheduler here.
            System.Threading.Thread.Sleep(1100);
            foreach (var clock in new[] { "rebirthTime", "totalPlaytime" })
                Set(Get(Character, clock), "totalseconds", (double)Get(Get(Character, clock), "totalseconds") + 1);
            CallMain("RunCustomPlanAutomation", plan);
            var scheduler = Get(MainInstance, "_protectedScheduler"); var state = Invoke(scheduler, "ExportState");
            Check(((double[])Property(scheduler, "EarnedSeconds"))[0] > 0, label + " contains actually earned fixed-stage credit");
            Check(((double[])Property(scheduler, "AppliedSeconds"))[0] > 0, label + " contains verified applied time");
            var acquired = Get(Get(MainInstance, "_globalPresetReceipt"), "Policy");
            Set(MainInstance, "_customStageFightKey", "consumed-busy-entry-fight");
            var settings = Get(MainType, "Settings"); var inventory = Get(Character, "inventory"); var head = Get(inventory, "head");
            var quickHead = Invoke(head, "MemberwiseClone"); Set(quickHead, "id", 552);
            ((IList)Get(inventory, "inventory"))[0] = quickHead;
            Set(settings, "_quickLoadout", new[] { 552 }); Set(settings, "_quickDiggers", new int[0]);
            CallMain("ToggleQuickExecutionClaim");
            var claim = Get(MainInstance, "_quickExecutionClaim");
            Check(ReferenceEquals(quickHead, Get(inventory, "head")) && !ReferenceEquals(head, quickHead), label + " Quick really replaces the stage head before native activity becomes busy");
            Equal(true, CallMain("ExecutionClaimSetupOwned", claim), label + " has a genuinely applied bounded Quick claim");
            Set(Get(Character, "res3"), "curRes3", 73L); Set(Get(Character, "res3"), "idleRes3", 43L);
            Set(((IList)Get(Get(Character, "wishes"), "wishes"))[0], "res3", 30L);
            var grant = CallMain("ResolveExecutionGrant", "busy-checkpoint", "original", Domain("Energy"), (Func<bool>)(() => true), null, null);
            var boss = Get(Character, "bossController"); Set(boss, busy, true);
            var energy = ExecutionStopLaneState(true); var magic = ExecutionStopLaneState(false);
            var protectedState = ExecutionStopProtectedState(); var support = CallMain("GlobalPresetSupportState");
            var native = CallMain("ExecutionRebindNativeKey"); var material = CallMain("ExactExecutionMaterialKey");
            var w = Get(Character, "wandoos98"); var wEnergy = Get(w, "energyLevel"); var wMagic = Get(w, "magicLevel");
            var writes = ResourceTransfers; var swaps = Get(typeof(Program), "Swaps"); var recalls = Get(typeof(Program), "Recalls");
            var magicRecalls = Get(typeof(Program), "MagicRecalls"); var routes = AdventureZoneEntries; var podRefreshes = PodRefreshCalls;
            var searches = PresetSearches;
            Throws(() => CallMain("QuiesceExecution", label), "Quiesced", label + " cannot retire prerequisites while native activity is busy");
            Equal(false, CallMain("ValidateExecutionGrant", grant, Domain("Energy")), label + " revokes old callbacks before retirement");
            var path = (string)Property(MainInstance, "ProtectedCheckpointPath"); var bytes = File.ReadAllText(path);
            var saved = JsonParse(bytes);
            Equal(Get(acquired, "Acquisition"), Property(Json(Json(saved, "selectedPolicy"), "acquisition"), "Value"), label + " saves the frozen current-stage intent");
            Equal("consumed-busy-entry-fight", Property(Json(saved, "fightEntry"), "Value"), label + " saves the consumed entry fight");
            Equal(false, ((IDictionary)Get(saved, "m_Dict")).Contains("quick"), label + " never persists Quick return authority");
            CallMain("QuickStuff"); CallMain("AutomationRoutine"); CallMain("ReconcileCustomResources", plan, true);
            CallMain("GetExecutionStateRuntime"); CallMain("GetExecutionPersistenceRuntime");
            CallMain("ResetCustomAutomationRuntime");
            Equal(null, Get(MainInstance, "_quickExecutionClaim"), label + " reset discards the live Quick claim");
            Equal(true, Get(claim, "Ended"), label + " discarded Quick cannot return its old baseline");
            Equal(0, ((IList)Property(MainInstance, "_executionClaims")).Count, label + " reset retains no transient claims");
            Equal(0, ((IDictionary)Get(MainInstance, "_executionSelections")).Count, label + " reset retains no transient selections");
            CallMain("InitializeProtectedStageProgress", plan);
            Equal(null, Get(MainInstance, "_protectedOuterCheckpointIssue"), label + " reload accepts the saved stage");
            Equal(null, Get(MainInstance, "_protectedSelectedPolicyIssue"), label + " reload requires no historical action review");
            Equal(null, Get(MainInstance, "_globalPresetReceipt"), label + " reload never imports application authority");
            Equal(false, Get(Get(MainInstance, "_protectedScheduler"), "_armed"), label + " reload cannot credit unverified native work");
            var observation = CallMain("GetExecutionStateRuntime");
            Equal("Quiescing", Property(Json(observation, "lifecycle"), "Value"), label + " observation cannot reopen lifecycle admission");
            Equal(0, ((IEnumerable)GetChildren(Json(observation, "claims"))).Cast<object>().Count(), label + " observation exposes no discarded claim");
            var resourceRows = ((IEnumerable)GetChildren(Json(observation, "resources"))).Cast<object>().ToArray();
            Check(resourceRows.Length > 0, label + " observes the actual retained native resource map");
            Check(resourceRows.All(x => !(bool)Property(Json(x, "verified"), "AsBool")), label + " observation does not fabricate verified resource authority");
            Equal(false, Property(Json(Json(observation, "support"), "verified"), "AsBool"), label + " observation does not fabricate support authority");
            Equal(false, Property(Json(CallMain("GetExecutionPersistenceRuntime"), "reviewRequired"), "AsBool"), label + " valid restart does not demand action-history approval");
            var restored = Get(MainInstance, "_globalPresetApplyingPolicy");
            foreach (var field in new[] { "Acquisition", "Definition", "Horizon" })
                Equal(Get(acquired, field), Get(restored, field), label + " retains frozen selection/" + field);
            Check(((double[])Get(Get(acquired, "Plan"), "Normalization")).SequenceEqual((double[])Get(Get(restored, "Plan"), "Normalization")), label + " retains frozen scoring references");
            foreach (var field in new[] { "entry", "cycle", "enteredAt", "expectedEnd", "durations", "earned", "applied", "completed", "skipped", "visits", "elapsedByStage" })
                Equal(Json(state, field).ToString(), Json(Invoke(Get(MainInstance, "_protectedScheduler"), "ExportState"), field).ToString(), label + " preserves actual progress/" + field);
            Equal(bytes, File.ReadAllText(path), label + " reset and reload never rewrite the checkpoint");
            Equal("consumed-busy-entry-fight", Get(MainInstance, "_customStageFightKey"), label + " cannot replay the entry fight");
            Equal(true, Get(boss, busy), label + " does not cancel native boss activity");
            Equal(energy, ExecutionStopLaneState(true), label + " preserves native Energy accounting");
            Equal(magic, ExecutionStopLaneState(false), label + " preserves native Magic accounting");
            Equal(protectedState, ExecutionStopProtectedState(), label + " preserves Basic Training and R3");
            Equal(support, CallMain("GlobalPresetSupportState"), label + " preserves native support");
            Equal(native, CallMain("ExecutionRebindNativeKey"), label + " preserves route, AT targets and installed OS");
            Equal(material, CallMain("ExactExecutionMaterialKey"), label + " preserves complete gear material");
            Check(ReferenceEquals(quickHead, Get(inventory, "head")), label + " preserves the equipped Quick instance without returning the stage head");
            Equal(wEnergy, Get(w, "energyLevel"), label + " preserves Wandoos Energy history");
            Equal(wMagic, Get(w, "magicLevel"), label + " preserves Wandoos Magic history");
            Equal(writes, ResourceTransfers, label + " performs zero native resource writes");
            Equal(swaps, Get(typeof(Program), "Swaps"), label + " performs zero native swaps");
            Equal(recalls, Get(typeof(Program), "Recalls"), label + " performs zero Energy recalls");
            Equal(magicRecalls, Get(typeof(Program), "MagicRecalls"), label + " performs zero Magic recalls");
            Equal(routes, AdventureZoneEntries, label + " performs zero route entries");
            Equal(podRefreshes, PodRefreshCalls, label + " performs zero support refresh writes");
            Equal(searches, PresetSearches, label + " performs zero replacement searches");
            Equal(0, Directory.GetFiles(Path.GetDirectoryName(path), "lifecycle-*").Length, label + " writes no lifecycle cleanup journal");
            Console.WriteLine("PASS " + label + "; earned=" + Json(state, "earned") + "; applied=" + Json(state, "applied") + "; native cleanup writes=0");
        }
    }

    private static void CheckInvalidOuterExecutionCheckpoint()
    {
        var plan = SetupResourceCoordination(true, true, "none");
        var path = (string)Property(MainInstance, "ProtectedCheckpointPath");
        foreach (var bytes in new[] { "{unreadable", "{\"version\":999}" })
        {
            File.WriteAllText(path, bytes); CallMain("InitializeProtectedStageProgress", plan);
            var searches = PresetSearches; var transfers = ResourceTransfers;
            Equal(null, CallMain("GetProtectedStage", plan), "corrupt real stage checkpoint cannot admit a fresh entry");
            Equal(false, CallMain("SaveProtectedStageProgress", true), "corrupt stage checkpoint blocks overwrite");
            Equal(bytes, File.ReadAllText(path), "corrupt stage evidence survives observation and persistence");
            Equal(searches, PresetSearches, "corrupt stage cannot authorize a replacement selection");
            Equal(transfers, ResourceTransfers, "corrupt stage cannot fund a new budget");
        }
    }

    private static void CheckExplicitExecutionRestart()
    {
        var plan = SetupInlineStagePlan(); var settings = Get(MainType, "Settings"); var name = (string)Property(settings, "CustomPlanFile");
        Invoke(Get(MainType, "CustomPlanStore"), "Save", name, plan); RunVerifiedInline(plan, "explicit recovery entry");
        var path = (string)Property(MainInstance, "ProtectedCheckpointPath");
        foreach (var corrupt in new[] { "{original corrupt checkpoint", "{second corrupt checkpoint" })
        {
            File.WriteAllText(path, corrupt); CallMain("InitializeProtectedStageProgress", plan);
            var recovery = Json(CallMain("GetExecutionPersistenceRuntime"), "recovery");
            var args = JsonParse("{\"stageIndex\":0,\"checkpointRevision\":" + Json(recovery, "checkpointRevision") + ",\"planRevision\":" + Json(recovery, "planRevision") + "}");
            var preview = CallMain("PreviewExecutionRestart", args); var writes = ResourceTransfers;
            Equal(corrupt, File.ReadAllText(path), "preview preserves corrupt stage bytes");
            CallMain("ApplyExecutionRebind", ExecutionReviewApplyArgs(preview));
            Equal(null, Get(MainInstance, "_protectedOuterCheckpointIssue"), "explicit tokenized recovery releases corrupt stage block");
            Equal(corrupt, File.ReadAllText(path + ".recovery.json"), "bounded recovery copy retains exact latest rejected evidence");
            Check(((double[])Property(Get(MainInstance, "_protectedScheduler"), "EarnedSeconds")).All(x => x == 0), "explicit restart cannot fabricate earned credit");
            Equal(writes, ResourceTransfers, "explicit restart never performs hidden native cleanup");
            Throws(() => CallMain("ApplyExecutionRebind", ExecutionReviewApplyArgs(preview)), "expired", "recovery token is single use");
        }
        Equal(1, Directory.GetFiles(Path.GetDirectoryName(path), "*.recovery.json").Length, "repeated explicit recovery remains bounded");
    }

    private static void InjectNativeReviewConflict(bool energy)
    {
        var writer = Plugin.GetType("NGUInjector.Managers.ResourceWriter", true);
        var map = (IDictionary)Invoke(writer, "CaptureAllocations", Character);
        var target = Enum.Parse(Plugin.GetType("NGUInjector.Managers.ResourceTarget", true), energy ? "EnergyNgu" : "MagicNgu");
        var amounts = (long[])map[target]; var index = Array.FindIndex(amounts, value => value > 2);
        if (index < 0)
        {
            index = 0; amounts[index] = 20;
            Set(((IList)Get(Get(Character, "NGU"), energy ? "skills" : "magicSkills"))[index], energy ? "energy" : "magic", 20L);
            var pool = energy ? Character : Get(Character, "magic"); var idle = energy ? "idleEnergy" : "idleMagic";
            Set(pool, idle, (long)Get(pool, idle) - 20);
        }
        amounts[index]--;
        var native = ((IList)Get(Get(Character, "NGU"), energy ? "skills" : "magicSkills"))[index]; var fired = false;
        var desired = (IDictionary)Activator.CreateInstance(map.GetType());
        foreach (DictionaryEntry pair in map)
            if ((int)Invoke(writer, "Lane", pair.Key) == (energy ? 0 : 1) && pair.Key.ToString() != "BasicAttack" && pair.Key.ToString() != "BasicDefense") desired.Add(pair.Key, pair.Value);
        var model = Character; var domain = Domain(energy ? "Energy" : "Magic");
        var grant = CallMain("ResolveExecutionGrant", "native-review-boundary", "current-context", domain, (Func<bool>)(() => ReferenceEquals(model, Get(MainType, "Character"))), null, null);
        var application = Get(Property(MainInstance, "_resourceSetupExecutor"), "Application");
        var outcome = Invoke(application, "Apply", desired, energy, !energy, false,
            (Func<bool>)(() => (bool)CallMain("ValidateExecutionGrant", grant, domain)), (Action)(() => {
                fired = true; Set(native, energy ? "energy" : "magic", (long)Get(native, energy ? "energy" : "magic") - 1);
                var pool = energy ? Character : Get(Character, "magic"); var idle = energy ? "idleEnergy" : "idleMagic"; Set(pool, idle, (long)Get(pool, idle) + 1);
            }));
        Equal(true, fired, "foreign edit occurs inside the actual native actuator");
        Equal("ForeignConflict", Get(outcome, "Disposition").ToString(), "foreign callback remains a real resource conflict");
        Equal(energy ? 1 : 2, Get(outcome, "AffectedLanes"), "foreign conflict stays scoped to one resource lane");
    }

    private static void CheckScopedExecutionRecovery()
    {
        foreach (var energy in new[] { true, false })
        {
            var plan = SetupScopedExecutionReview(energy ? 0 : 1);
            var acquisition = Get(Get(MainInstance, "_globalPresetReceipt"), "Policy");
            var state = Invoke(Get(MainInstance, "_protectedScheduler"), "ExportState");
            var searches = PresetSearches;
            var unmanaged = CallMain("GlobalPresetLaneState", !energy);
            var protectedState = ExecutionStopProtectedState();
            InjectNativeReviewConflict(energy);
            var application = Get(Property(MainInstance, "_resourceSetupExecutor"), "Application");
            var preview = CallMain("PreviewExecutionRebind", JsonParse("{}")); var writes = ResourceTransfers;
            Equal(false, CallMain("ValidateExecutionGrant", Get(Get(MainInstance, "_executionRebindReview"), "Grant"), Domain(energy ? "Magic" : "Energy")), "single-lane review requests no unmanaged resource authority");
            var invalid = ExecutionReviewApplyArgs(preview); SetJson(invalid, "token", Json(JsonParse("{\"v\":\"00000000000000000000000000000000\"}"), "v"));
            Throws(() => CallMain("ApplyExecutionRebind", invalid), "expired", "invalid token cannot clear a native conflict");
            var settings = Get(MainType, "Settings"); var permission = energy ? "_manageEnergy" : "_manageMagic";
            Set(settings, permission, false);
            Throws(() => CallMain("ApplyExecutionRebind", ExecutionReviewApplyArgs(preview)), "authority changed", "withdrawn permission revokes review");
            Set(settings, permission, true);
            preview = CallMain("PreviewExecutionRebind", JsonParse("{}")); CallMain("InvalidateExecutionObservations", Domain(energy ? "Energy" : "Magic"));
            Throws(() => CallMain("ApplyExecutionRebind", ExecutionReviewApplyArgs(preview)), "authority changed", "domain revision revokes review");
            preview = CallMain("PreviewExecutionRebind", JsonParse("{}"));
            var stats = Get(Character, "stats"); Set(Character, "stats", Invoke(stats, "MemberwiseClone"));
            Throws(() => CallMain("ApplyExecutionRebind", ExecutionReviewApplyArgs(preview)), "authority changed", "model replacement revokes review");
            Set(Character, "stats", stats);
            preview = CallMain("PreviewExecutionRebind", JsonParse("{}"));
            var nativeSkills = (IList)Get(Get(Character, "NGU"), energy ? "skills" : "magicSkills");
            var nativeIndex = Enumerable.Range(0, nativeSkills.Count).First(i => (long)Get(nativeSkills[i], energy ? "energy" : "magic") > 0);
            Set(nativeSkills[nativeIndex], energy ? "energy" : "magic", (long)Get(nativeSkills[nativeIndex], energy ? "energy" : "magic") - 1);
            var nativePool = energy ? Character : Get(Character, "magic"); var nativeIdle = energy ? "idleEnergy" : "idleMagic";
            Set(nativePool, nativeIdle, (long)Get(nativePool, nativeIdle) + 1);
            Throws(() => CallMain("ApplyExecutionRebind", ExecutionReviewApplyArgs(preview)), "scope changed", "unobserved native bar edit cannot be adopted by stale approval");
            Equal(writes, ResourceTransfers, "rejected review performs no native writes");
            Equal(energy ? 1 : 2, Property(application, "BlockedLanes"), "rejected review retains the native conflict");
            preview = CallMain("PreviewExecutionRebind", JsonParse("{}"));
            var old = CallMain("ResolveExecutionGrant", "review-old", "original", Domain(energy ? "Energy" : "Magic"), (Func<bool>)(() => true), null, null);
            var grant = Get(Get(MainInstance, "_executionRebindReview"), "Grant");
            CallMain("ApplyExecutionRebind", ExecutionReviewApplyArgs(preview));
            Equal(0, Property(application, "BlockedLanes"), "explicit review reauthorizes the reviewed native allocation");
            Equal(false, CallMain("ValidateExecutionGrant", old, Domain(energy ? "Energy" : "Magic")), "old grant cannot survive review");
            Equal(false, CallMain("ValidateExecutionGrant", grant, Domain(energy ? "Energy" : "Magic")), "spent review grant cannot authorize writes");
            CallMain("ReconcileCustomResources", plan, true);
            var current = Get(MainInstance, "_globalPresetReceipt"); Check(current != null, "same entry reconverges after real foreign review");
            Equal(Get(acquisition, "Acquisition"), Get(Get(current, "Policy"), "Acquisition"), "foreign review never acquires a replacement policy");
            Equal(true, CallMain("LoadoutPresetAllocationsMatch", Get(current, "Execution")), "actual native allocations converge");
            Equal(unmanaged, CallMain("GlobalPresetLaneState", !energy), "foreign review/reconciliation preserves the unmanaged lane exactly");
            Equal(protectedState, ExecutionStopProtectedState(), "foreign review preserves Basic Training and R3");
            foreach (var field in new[] { "entry", "enteredAt", "expectedEnd", "earned", "autoWandoosStarted" })
                Equal(Json(state, field).ToString(), Json(Invoke(Get(MainInstance, "_protectedScheduler"), "ExportState"), field).ToString(), "foreign review retains original stage/" + field);
            Equal(searches, PresetSearches, "foreign review performs no replacement search");
            preview = CallMain("PreviewExecutionRebind", JsonParse("{}"));
            var path = (string)Property(MainInstance, "ProtectedCheckpointPath"); var bytes = File.ReadAllText(path); File.AppendAllText(path, " ");
            Throws(() => CallMain("ApplyExecutionRebind", ExecutionReviewApplyArgs(preview)), "Checkpoint changed", "review remains bound to exact checkpoint bytes");
            File.WriteAllText(path, bytes);
            preview = CallMain("PreviewExecutionRebind", JsonParse("{}")); Set(Get(MainInstance, "_executionRebindReview"), "Expires", 0d);
            Throws(() => CallMain("ApplyExecutionRebind", ExecutionReviewApplyArgs(preview)), "expired", "review tokens still expire");
        }
        var scoped = SetupScopedExecutionReview(0, true);
        InjectNativeReviewConflict(true); InjectNativeReviewConflict(false);
        var magic = CallMain("GlobalPresetLaneState", false);
        var magicGrant = CallMain("ResolveExecutionGrant", "unreviewed-magic", "original", Domain("Magic"), (Func<bool>)(() => true), null, null);
        var exact = CallMain("PreviewExecutionRebind", JsonParse("{}"));
        CallMain("ApplyExecutionRebind", ExecutionReviewApplyArgs(exact));
        Equal(true, CallMain("ValidateExecutionGrant", magicGrant, Domain("Magic")), "Energy review does not revoke an unreviewed Magic grant");
        Equal(2, Property(Get(Property(MainInstance, "_resourceSetupExecutor"), "Application"), "BlockedLanes"), "unreviewed Magic conflict survives Energy recovery");
        CallMain("ReconcileCustomResources", scoped, true);
        Equal(magic, CallMain("GlobalPresetLaneState", false), "later ordinary execution preserves the unreviewed foreign lane");
        var capacity = SetupScopedExecutionReview(12);
        Set(Get(MainType, "Settings"), "_manageEnergy", false);
        var before = CallMain("GlobalPresetAllocationState"); var noResourceWrites = ResourceTransfers;
        var cap = CallMain("PreviewExecutionRebind", JsonParse("{}"));
        var capGrant = Get(Get(MainInstance, "_executionRebindReview"), "Grant");
        Equal(false, CallMain("ValidateExecutionGrant", capGrant, Domain("Energy")), "capacity-only review claims no Energy authority");
        Equal(false, CallMain("ValidateExecutionGrant", capGrant, Domain("Magic")), "capacity-only review claims no Magic authority");
        CallMain("ApplyExecutionRebind", ExecutionReviewApplyArgs(cap)); CallMain("ReconcileCustomResources", capacity, true);
        Equal(before, CallMain("GlobalPresetAllocationState"), "capacity-only review preserves all native allocations");
        Equal(noResourceWrites, ResourceTransfers, "capacity-only reviewed setup gains no resource-write authority");
        Equal(550, Get(Get(Get(Character, "inventory"), "head"), "id"), "capacity-only ordinary service retains actual acquired equipment");
    }

    private static object SetupScopedExecutionReview(int objective, bool bothPermissions = false)
    {
        var plan = SetupResourceCoordination(false, true, "none"); CallMain("StopManualGlobalPreset");
        var settings = Get(MainType, "Settings");
        Set(settings, "_manageEnergy", bothPermissions || objective != 1); Set(settings, "_manageMagic", bothPermissions || objective == 1);
        ((long[])Property(Get(Character, "training"), "attackEnergy"))[0] = 7;
        Set(Character, "idleEnergy", (long)Get(Character, "idleEnergy") - 7);
        var perks = Get(Get(Character, "adventureController"), "itopod"); Set(perks, "character", Character);
        Sequence(Get(Get(Character, "adventure"), "itopod"), "perkLevel", 512, 0L);
        Sequence(perks, "effectPerLevel", 512, 0f); ListField(perks, "perkDifficultyReq", 512);
        Set(Get(Character, "wishesController"), "character", Character); ListField(Get(Character, "wishesController"), "properties", 512);
        ListField(Get(Character, "wishes"), "wishes", 512); ListField(Get(Character, "hacks"), "hacks", 16);
        var quirks = Get(Character, "beastQuestPerkController"); Set(quirks, "character", Character);
        Sequence(Get(Character, "beastQuest"), "quirkLevel", 512, 0L); Sequence(quirks, "effectPerLevel", 512, 0f); ListField(quirks, "quirkDifficultyReq", 512);
        var r3 = Get(Character, "res3"); Set(r3, "capRes3", 1000L); Set(r3, "curRes3", 73L); Set(r3, "idleRes3", 43L);
        Set(((IList)Get(Get(Character, "wishes"), "wishes"))[0], "res3", 30L);
        if (objective == 12)
        {
            var item = Invoke(Get(Get(Character, "inventory"), "head"), "MemberwiseClone");
            Set(item, "id", 550); Set(item, "spec1Type", Enum.Parse(Field(item, "spec1Type").FieldType, "EnergyCap"));
            Set(item, "spec1Cur", 10000f); Set(item, "spec1Cap", 10000000f); ((IList)Get(Get(Character, "inventory"), "inventory"))[0] = item;
        }
        var library = Get(Get(MainType, "CustomPlanStore"), "<PresetLibrary>k__BackingField"); var root = Invoke(library, "ReadExternalLibrary");
        var id = "scoped-review-" + Guid.NewGuid().ToString("N");
        var weights = Enumerable.Range(0, 14).Select(i => i == objective ? "1" : "0");
        Invoke(Json(root, "Presets"), "Add", "", JsonParse("{\"Id\":\"" + id + "\",\"Name\":\"Scoped original policy\",\"Weights\":[" + string.Join(",", weights)
            + "],\"MagicBloodPercent\":0,\"SupportMode\":\"current\",\"SwapSeconds\":0,\"AutoUpdate\":false}"));
        var revision = Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
        var args = JsonParse("{\"presetId\":\"" + id + "\",\"libraryRevision\":\"" + revision + "\"}"); var preview = CallMain("PreviewGlobalPreset", args);
        var apply = JsonParse(args.ToString().TrimEnd('}') + ",\"previewId\":" + Json(preview, "previewId") + ",\"candidateKey\":\"" + (objective == 12 ? "balanced" : "current") + "\"}");
        Invoke(MainInstance, "ApplyGlobalPreset", apply, null);
        Set(((Array)Get(plan, "Stages")).GetValue(0), "PresetId", id);
        Set(MainInstance, "_customRuntimePlanCache", plan); Set(MainInstance, "_customRuntimePlanCacheName", Property(settings, "CustomPlanFile"));
        Set(MainInstance, "_customRuntimePlanCacheLoadedAt", DateTime.Now); Set(settings, "_useCustomRebirthPlan", true);
        CallMain("RunCustomPlanAutomation", plan); RequireInitialScheduledReceipt(plan, "scoped exact review/" + objective);
        return plan;
    }

    private static void CheckForeignNativeSettingsReviewScope()
    {
        SetupResourceCoordination(true, true, "none", true);
        var targets = (long[])Get(Get(Character, "advancedTraining"), "levelTarget"); targets[3] = 777;
        CallMain("ObserveGlobalPresetTargetConflict", "foreign-native-setting-owner");
        var preview = CallMain("PreviewExecutionRebind", JsonParse("{}")); var writes = ResourceTransfers; var swaps = Get(typeof(Program), "Swaps");
        Throws(() => CallMain("ApplyExecutionRebind", ExecutionReviewApplyArgs(preview)), "scope changed", "review cannot clear another native-setting owner's conflict");
        Equal("foreign-native-setting-owner", Get(MainInstance, "_globalPresetNativeTargetConflict"), "foreign native-setting evidence remains");
        Equal(777L, targets[3], "foreign native target stays untouched");
        Equal(writes, ResourceTransfers, "rejected native-setting review performs zero allocations");
        Equal(swaps, Get(typeof(Program), "Swaps"), "rejected native-setting review retains prerequisites");
    }
}
