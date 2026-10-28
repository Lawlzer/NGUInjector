using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

public static partial class Program
{
    private static bool ObserveAutomaticTransfers;
    private static int AutomaticPaidAtTransfers, AutomaticPositiveTransfers;
    private static string AutomaticConflictCheckpoint;
    // Count evidence-removing boundaries: exact desired-map reductions and explicit native recalls.
    private static int AutomaticConflictRecalls, AutomaticUnsafeConflictRecalls;
    private static bool LockAutomaticConflictAfterRecall;
    private static FileStream AutomaticConflictFileLock;
    private static int AutomaticAtTarget, AutomaticEnergyWTarget, AutomaticMagicWTarget;
    private static bool AutomaticConflictModelPending;
    private static long AutomaticConflictEnergyTotal, AutomaticConflictMagicTotal;

    private static void ConfigureAutomaticReconciliationFixture(AssemblyDefinition plugin)
    {
        var targets = plugin.MainModule.Types.Single(x => x.FullName == "NGUInjector.Managers.ResourceTarget");
        AutomaticAtTarget = (int)targets.Fields.Single(x => x.Name == "AdvancedTraining").Constant;
        AutomaticEnergyWTarget = (int)targets.Fields.Single(x => x.Name == "WandoosEnergy").Constant;
        AutomaticMagicWTarget = (int)targets.Fields.Single(x => x.Name == "WandoosMagic").Constant;
        ResourceModelWriteBefore = ObserveAutomaticModelWriteBefore;
        ResourceModelWriteAfter = ObserveAutomaticModelWriteAfter;
        ResourcePresentationStarting = ObserveAutomaticResourcePresentation;
        // Explicit gear/legacy recalls still exist, but are distinct from desired-map mutations.
        var recall = plugin.MainModule.Types.Single(x => x.FullName == "NGUInjector.Managers.ResourceWriter").Methods.Single(x => x.Name == "Recall");
        var il = recall.Body.GetILProcessor(); var first = recall.Body.Instructions[0];
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first, il.Create(OpCodes.Call, plugin.MainModule.ImportReference(typeof(Program).GetMethod("ObserveAutomaticConflictRecall", All))));
    }

    private static void ObserveAutomaticModelWriteBefore(object c, int target, int index, long before, long desired)
    {
        if (AutomaticConflictCheckpoint == null || before <= desired || (target != AutomaticEnergyWTarget && target != AutomaticMagicWTarget)) return;
        AutomaticConflictRecalls++;
        var safe = false;
        try { safe = (bool)Get(Json(Json(Parse(File.ReadAllText(AutomaticConflictCheckpoint)), "automaticSetup"), "safeResources"), "AsBool"); }
        catch (IOException) { }
        if (!safe) AutomaticUnsafeConflictRecalls++;
        AutomaticConflictEnergyTotal = (long)Invoke(Plugin.GetType("NGUInjector.Managers.ResourceWriter", true), "Reclaimable", true);
        AutomaticConflictMagicTotal = (long)Invoke(Plugin.GetType("NGUInjector.Managers.ResourceWriter", true), "Reclaimable", false);
    }

    private static void ObserveAutomaticModelWriteAfter(object c, int target, int index, long before, long desired)
    {
        if (ObserveAutomaticTransfers && desired > before)
        {
            AutomaticPositiveTransfers++;
            if (target == AutomaticAtTarget && (index == 3 || index == 4)) AutomaticPaidAtTransfers++;
        }
        if (AutomaticConflictCheckpoint != null && before > desired && (target == AutomaticEnergyWTarget || target == AutomaticMagicWTarget))
            AutomaticConflictModelPending = true;
    }

    private static void ObserveAutomaticResourcePresentation(object c, int target, int index)
    {
        if (!AutomaticConflictModelPending) return;
        AutomaticConflictModelPending = false;
        Equal(AutomaticConflictEnergyTotal, Invoke(Plugin.GetType("NGUInjector.Managers.ResourceWriter", true), "Reclaimable", true), "conflict release completes conserved Energy accounting before presentation/filesystem fault");
        Equal(AutomaticConflictMagicTotal, Invoke(Plugin.GetType("NGUInjector.Managers.ResourceWriter", true), "Reclaimable", false), "conflict release completes conserved Magic accounting before presentation/filesystem fault");
        if (LockAutomaticConflictAfterRecall && AutomaticConflictFileLock == null)
            AutomaticConflictFileLock = new FileStream(AutomaticConflictCheckpoint, FileMode.Open, FileAccess.Read, FileShare.None);
    }

    public static void ObserveAutomaticConflictRecall(bool energy)
    {
        if (AutomaticConflictCheckpoint == null) return;
        var w = Get(Character, "wandoos98");
        if ((long)Get(w, energy ? "wandoosEnergy" : "wandoosMagic") == 0) return;
        AutomaticConflictRecalls++;
        var safe = false;
        try { safe = (bool)Get(Json(Json(Parse(File.ReadAllText(AutomaticConflictCheckpoint)), "automaticSetup"), "safeResources"), "AsBool"); }
        catch (IOException) { }
        if (!safe) AutomaticUnsafeConflictRecalls++;
        // Filesystem fault only; all native recall, cleanup and allocation instructions remain intact.
        if (LockAutomaticConflictAfterRecall && AutomaticConflictFileLock == null)
            AutomaticConflictFileLock = new FileStream(AutomaticConflictCheckpoint, FileMode.Open, FileAccess.Read, FileShare.None);
    }

    private static object ReplaceAutomaticCoordinator(object plan)
    {
        var old = MainInstance;
        MainInstance = Blank(MainType);
        Set(MainInstance, "_combatPrepEndedRuns", new HashSet<string>());
        Set(MainInstance, "_customIronPillPrepStartedAt", -1d);
        Set(MainInstance, "_resourceExecutionOwner", ""); Set(MainInstance, "_resourceExecutionReason", "");
        Set(MainType, "reference", MainInstance);
        CallMain("InitializeProtectedStageProgress", plan);
        return old;
    }

    private static object UnfinishedAtFixture(out object plan)
    {
        plan = ScheduledSetup(ScheduledRow + "," + StandardRow);
        Set(Get(Get(Character, "buttons"), "augmentation"), "m_Interactable", false);
        foreach (var name in new[] { "wandoosEnergy", "wandoosMagic" })
            Set(Get(Get(Character, "advancedTrainingController"), name), "levelFactor", 1000f);
        var menus = (long[])Get(Get(Character, "advancedTraining"), "levelTarget"); menus[3] = 5000; menus[4] = 6000;
        var run = StartScheduled(plan);
        Equal("AT", Get(ScheduledPhase(run), "Kind"), "reinjection fixture really acquired paid AT");
        return run;
    }

    private static void CheckAutomaticReconciliationRegressions()
    {
        var failures = new List<Exception>();
        foreach (var test in new Action[] { CheckDirectCleanupWConflict,
            () => CheckCleanupConflictWriteFailure(false), () => CheckCleanupConflictWriteFailure(true),
            () => CheckCleanLegacyInterruption(false), () => CheckCleanLegacyInterruption(true),
            CheckInvoluntaryAtCancellation, CheckCleanLegacyAtRecovery, CheckRestoredAtNativeWConflict })
        {
            try { test(); }
            catch (Exception error) { failures.Add(error); Console.Error.WriteLine(test.Method.Name + ": " + error.Message); }
        }
        if (failures.Count > 0) throw new AggregateException("Reviewed automatic recovery regressions failed.", failures);
        CheckUnfinishedAtReinjection(550);
        CheckUnfinishedAtReinjection(1050);
        CheckExpiredReinjectionFunding();
        CheckRecoveryPersistenceRetry();
        CheckAutomaticTransitionSaveFailure();
        CheckRecoveryLegacyAndMalformed();
        CheckRecoveryEntryIsolation();
        CheckRecoveryPauseAndSwitch();
        CheckRecoveryCleanupExhaustion();
        CheckFirstCleanupAuthorityLoss();
    }

    private static void CheckDirectCleanupWConflict()
    {
        foreach (var takeover in new[] { false, true })
        foreach (var energy in new[] { false, true })
        {
            object plan; var run = UnfinishedAtFixture(out plan); var entry = Get(run, "OwnerEntry"); var deadline = Get(run, "DeadlineRebirthSeconds");
            var path = (string)Get(MainInstance, "ProtectedCheckpointPath"); var store = Get(MainType, "CustomPlanStore");
            var args = takeover ? PrepareInterruptionPreset() : null;
            CallMain("RecallCombatPrepResources");
            var w = Get(Character, "wandoos98"); var pool = energy ? Character : Get(Character, "magic"); var idle = energy ? "idleEnergy" : "idleMagic";
            Set(w, energy ? "wandoosEnergy" : "wandoosMagic", 123L); Set(pool, idle, (long)Get(pool, idle) - 123);
            AutomaticConflictCheckpoint = path; AutomaticConflictRecalls = AutomaticUnsafeConflictRecalls = 0;
            try
            {
                if (takeover) EquipInterruptionPreset(args);
                else
                {
                    CallMain("Unload"); // Actual lifecycle, without a Tick before EndCombatPrep.
                    Set(MainType, "Character", Character); Set(MainType, "Controller", Controller);
                    Set(MainType, "Settings", Settings); Set(MainType, "CustomPlanStore", store);
                }
                if (takeover) Equal(true, AutomaticConflictRecalls > 0, "running takeover actually releases the native W conflict");
                else
                {
                    Equal(0, AutomaticConflictRecalls, "unload does not gain ordinary resource-release authority");
                    Equal(123L, Get(w, energy ? "wandoosEnergy" : "wandoosMagic"), "unload preserves native W evidence instead of erasing unsaved history");
                    AssertUnloadedCombatCallbacks(run);
                }
                Equal(0, AutomaticUnsafeConflictRecalls, "direct cleanup must durably record conflict BEFORE first recall: " + takeover + "/" + energy);
                var saved = Parse(File.ReadAllText(path));
                if (takeover) Equal(true, Get(Json(Json(saved, "automaticSetup"), "safeResources"), "AsBool"), "running takeover retains durable conservative history before erasure");
                Equal(deadline, Get(Json(Json(saved, "scheduler"), "expectedEnd"), "AsDouble"), "direct unload/takeover retains original deadline");
                AutomaticConflictCheckpoint = null;
                Setting("UseCustomRebirthPlan", true); ScheduledTime(550); ReplaceAutomaticCoordinator(plan);
                AutomaticPaidAtTransfers = AutomaticPositiveTransfers = 0; ObserveAutomaticTransfers = true;
                var resumed = StartScheduled(plan);
                Equal(0, AutomaticPaidAtTransfers, "reinjection after direct cleanup never resumes paid AT");
                Equal("NGU", Get(ScheduledPhase(resumed), "Kind"), "reinjection after direct cleanup uses safe background continuation");
                Equal(entry, Get(resumed, "OwnerEntry"), "direct cleanup reinjection preserves entry identity");
                Equal(deadline, Get(resumed, "DeadlineRebirthSeconds"), "direct cleanup reinjection preserves original deadline");
                Equal(false, Get(Get(MainInstance, "_protectedScheduler"), "AutoAtCompleted"), "direct cleanup never claims unresolved gate completed");
                CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(resumed, "Id") + "\"}"));
            }
            finally { AutomaticConflictCheckpoint = null; ObserveAutomaticTransfers = false; }
        }
    }

    private static void CheckCleanupConflictWriteFailure(bool failAfterRecall)
    {
        foreach (var unload in new[] { false, true })
        foreach (var energy in new[] { false, true })
        {
            object plan; var run = UnfinishedAtFixture(out plan); var entry = Get(run, "OwnerEntry"); var deadline = Get(run, "DeadlineRebirthSeconds");
            var path = (string)Get(MainInstance, "ProtectedCheckpointPath"); var store = Get(MainType, "CustomPlanStore");
            CallMain("RecallCombatPrepResources");
            var w = Get(Character, "wandoos98"); var pool = energy ? Character : Get(Character, "magic"); var idle = energy ? "idleEnergy" : "idleMagic";
            Set(w, energy ? "wandoosEnergy" : "wandoosMagic", 123L); Set(pool, idle, (long)Get(pool, idle) - 123);
            var recalls = Recalls; var swaps = Swaps; var writes = ResourceCommittedWrites;
            // Unload has no resource recall boundary. Exercise checkpoint failure
            // before quiesce and after bounded retirement, without inventing one.
            if (unload && failAfterRecall) CallMain("QuiesceExecution", "fixture bounded retirement before final checkpoint failure");
            AutomaticConflictCheckpoint = path; AutomaticConflictRecalls = AutomaticUnsafeConflictRecalls = 0;
            LockAutomaticConflictAfterRecall = failAfterRecall && !unload;
            if (!failAfterRecall || unload) AutomaticConflictFileLock = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            try
            {
                if (unload)
                {
                    CallMain("Unload");
                    Set(MainType, "Character", Character); Set(MainType, "Controller", Controller);
                    Set(MainType, "Settings", Settings); Set(MainType, "CustomPlanStore", store);
                }
                else ScheduledRefresh(run);
                Equal(true, AutomaticConflictFileLock != null, "checkpoint failure is exercised at intended cleanup boundary: afterRecall=" + failAfterRecall + "/unload=" + unload + "/energy=" + energy);
                Equal(0, AutomaticUnsafeConflictRecalls, "checkpoint failure must not erase unsaved W evidence: " + failAfterRecall + "/" + unload + "/" + energy);
                if (!failAfterRecall || unload)
                {
                    Equal(recalls, Recalls, "failed pre-recall checkpoint leaves all native resource evidence intact");
                    Equal(writes, ResourceCommittedWrites, "failed pre-release checkpoint prevents every desired-map model write");
                    Equal(swaps, Swaps, "failed pre-recall checkpoint cannot restore historical gear");
                    Equal(123L, Get(w, energy ? "wandoosEnergy" : "wandoosMagic"), "unsaved conflict retains its actual native allocation");
                    if (unload) AssertUnloadedCombatCallbacks(run);
                    else Equal(0, ((IList)Get(run, "RestoreTargets")).Count, "unsafe running cleanup retires historical target callbacks");
                    if (!unload)
                    {
                        var attempt = Get(MainInstance, "_protectedLastSavedAt");
                        for (var i = 0; i < 20; i++) CallMain("ServiceAutomaticRecovery");
                        Equal(attempt, Get(MainInstance, "_protectedLastSavedAt"), "failed conflict checkpoint uses controlled retry cadence");
                        Equal(recalls, Recalls, "waiting conflict retry cannot erase evidence");
                    }
                }
                AutomaticConflictFileLock.Dispose(); AutomaticConflictFileLock = null;
                var saved = Parse(File.ReadAllText(path));
                if (!unload) Equal(failAfterRecall, Get(Json(Json(saved, "automaticSetup"), "safeResources"), "AsBool"), "only successfully saved conflict is trusted after write failure");
                else Equal(123L, Get(w, energy ? "wandoosEnergy" : "wandoosMagic"), "failed unload checkpoint cannot destroy the native conflict witness");
                AutomaticConflictCheckpoint = null; LockAutomaticConflictAfterRecall = false;
                ScheduledTime(550); ReplaceAutomaticCoordinator(plan);
                AutomaticPaidAtTransfers = AutomaticPositiveTransfers = 0; ObserveAutomaticTransfers = true;
                var resumed = StartScheduled(plan);
                Equal(0, AutomaticPaidAtTransfers, "reinjection after failed checkpoint never loses irreversible W conflict");
                Equal("NGU", Get(ScheduledPhase(resumed), "Kind"), "write-failure recovery continues safe background");
                Equal(entry, Get(resumed, "OwnerEntry"), "write-failure recovery preserves entry");
                Equal(deadline, Get(resumed, "DeadlineRebirthSeconds"), "write-failure recovery cannot acquire new budget");
                Equal(false, Get(Get(MainInstance, "_protectedScheduler"), "AutoAtCompleted"), "write-failure recovery leaves gate unresolved");
                CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(resumed, "Id") + "\"}"));
            }
            finally
            {
                AutomaticConflictFileLock?.Dispose(); AutomaticConflictFileLock = null;
                AutomaticConflictCheckpoint = null; LockAutomaticConflictAfterRecall = false; ObserveAutomaticTransfers = false;
            }
        }
    }

    private static void AssertUnloadedCombatCallbacks(object run)
    {
        Equal("Unloaded", Get(MainInstance, "_executionLifecycle").ToString(), "completed unload revokes all ordinary and bounded-retirement admission");
        var targets = ((long[])Get(Get(Character, "advancedTraining"), "levelTarget")).ToArray();
        var writes = ResourceCommittedWrites; var recalls = Recalls;
        foreach (Action callback in ((IList)Get(run, "RestoreTargets")).Cast<Action>().ToArray()) callback();
        Equal(string.Join(",", targets), string.Join(",", (long[])Get(Get(Character, "advancedTraining"), "levelTarget")), "retained borrowed-setting callbacks have no post-unload write authority");
        Equal(writes, ResourceCommittedWrites, "retained callbacks cannot mutate resources after unload");
        Equal(recalls, Recalls, "retained callbacks cannot erase native W evidence after unload");
    }

    private static void CheckCleanLegacyInterruption(bool failedAcquire)
    {
        foreach (var invalid in new[] { false, true })
        foreach (var cancel in new[] { false, true })
        {
            object plan; var old = UnfinishedAtFixture(out plan); var entry = Get(old, "OwnerEntry"); var deadline = Get(old, "DeadlineRebirthSeconds");
            CallMain("EndCombatPrep", "cancelled", "Injector unloaded.", true);
            var path = (string)Get(MainInstance, "ProtectedCheckpointPath"); var saved = Parse(File.ReadAllText(path));
            if (invalid) Set(Json(Json(saved, "automaticAtContinuation"), "energyTarget"), "Value", "invalid");
            else saved.GetType().GetMethod("Remove", new[] { typeof(string) }).Invoke(saved, new object[] { "automaticAtContinuation" });
            File.WriteAllText(path, saved.ToString()); ScheduledTime(550); ReplaceAutomaticCoordinator(plan);
            var run = StartScheduled(plan); var scheduler = Get(MainInstance, "_protectedScheduler");
            Equal(null, Get(MainInstance, "_automaticAtContinuation"), "unknown-gate fallback genuinely has no continuation object");
            Equal("NGU", Get(ScheduledPhase(run), "Kind"), "unknown-gate interruption starts from conservative continuation");
            if (failedAcquire)
            {
                CallMain("EndCombatPrep", "cancelled", "Injector unloaded.", true);
                Setting("ManageMagic", false); var recalls = Recalls;
                CallMain("RunCustomPlanAutomation", plan);
                Equal(recalls, Recalls, "unknown-gate failed reacquisition never recalls unmanaged resources");
                Setting("ManageMagic", true); Set(MainInstance, "_automaticAcquisitionRetryAt", -1d);
            }
            else
            {
                var inactive = ((IList)Get(Get(Character, "diggers"), "diggers"))[11];
                Equal(false, Get(inactive, "active"), "unknown-gate interruption edits an inactive digger");
                Set(inactive, "maxLevel", (long)Get(inactive, "maxLevel") + 1); ScheduledRefresh(run);
                Equal(true, ((string)Get(run, "Reason")).Contains("purchased levels"), "unknown-gate regression reaches involuntary support-material cleanup");
                Equal(false, Get(run, "CleanupPending"), "unknown-gate involuntary cleanup safely finishes");
            }
            Equal(deadline, Get(scheduler, "ExpectedEndSeconds"), "unknown-gate interruption/reacquisition cannot complete conservative entry early: " + failedAcquire);
            Equal(false, ((bool[])Get(scheduler, "Completed"))[0], "unknown gate remains unresolved after interruption");
            run = StartScheduled(plan);
            Equal(entry, Get(run, "OwnerEntry"), "unknown-gate interruption preserves original entry");
            Equal(deadline, Get(run, "DeadlineRebirthSeconds"), "unknown-gate interruption grants no fresh budget");
            Equal("NGU", Get(ScheduledPhase(run), "Kind"), "unknown-gate interruption resumes only safe background resources");
            Equal(false, Get(scheduler, "AutoAtCompleted"), "unknown-gate interruption cannot fake gate completion");
            if (cancel) CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(run, "Id") + "\"}"));
            else ScheduledTime(1200);
            CallMain("ReconcileCustomResources", plan, true);
            if (cancel)
            {
                Equal(0, Get(scheduler, "StageIndex"), "explicit cancellation cannot fabricate earned entry completion");
                Equal(false, ((bool[])Get(scheduler, "Completed"))[0], "cancelled conservative entry remains unearned");
                Equal(deadline, Get(scheduler, "ExpectedEndSeconds"), "cancellation preserves original conservative deadline");
                ScheduledTime(1200); CallMain("ReconcileCustomResources", plan, true);
            }
            Equal(1, Get(scheduler, "StageIndex"), "original expiry retires conservative entry without a new budget: " + cancel);
        }
    }

    private static void CheckInvoluntaryAtCancellation()
    {
        object plan; var run = UnfinishedAtFixture(out plan); var phase = ScheduledPhase(run);
        var targetE = Get(phase, "AtEnergyTarget"); var targetM = Get(phase, "AtMagicTarget");
        var entry = Get(run, "OwnerEntry"); var deadline = Get(run, "DeadlineRebirthSeconds");
        var phaseEnd = (double)Get(run, "PhaseDeadline") - (double)Get(run, "Started");
        var scheduler = Get(MainInstance, "_protectedScheduler");
        var inactive = ((IList)Get(Get(Character, "diggers"), "diggers"))[11];
        Equal(false, Get(inactive, "active"), "support-material regression edits a genuinely inactive digger");
        Set(inactive, "maxLevel", (long)Get(inactive, "maxLevel") + 1);
        ScheduledRefresh(run);
        Equal("cancelled", Get(run, "Status"), "purchased support material change cancels pinned run");
        Equal(true, ((string)Get(run, "Reason")).Contains("purchased levels"), "regression reaches the actual support-material cancellation");
        Equal(false, Get(run, "CleanupPending"), "safe synchronous cleanup really completed");
        Equal(deadline, Get(scheduler, "ExpectedEndSeconds"), "involuntary cancellation cannot shorten unresolved AT entry");
        Equal(false, ((bool[])Get(scheduler, "Completed"))[0], "involuntary cancellation is not AT entry completion");
        Setting("ManageMagic", false);
        var recalls = Recalls; CallMain("RunCustomPlanAutomation", plan);
        Equal(deadline, Get(scheduler, "ExpectedEndSeconds"), "failed reacquisition cannot complete the still-unresolved AT entry");
        Equal(recalls, Recalls, "failed reacquisition never recalls the unmanaged lane");
        Equal(false, ((bool[])Get(scheduler, "Completed"))[0], "failed reacquisition leaves original AT obligation open");
        Setting("ManageMagic", true); Set(MainInstance, "_automaticAcquisitionRetryAt", -1d);
        ScheduledTime(550);
        var resumed = StartScheduled(plan); phase = ScheduledPhase(resumed);
        Equal(entry, Get(resumed, "OwnerEntry"), "subsequent dispatch retains cancelled entry identity");
        Equal(deadline, Get(resumed, "DeadlineRebirthSeconds"), "subsequent dispatch retains original deadline");
        Equal("NGU", Get(phase, "Kind"), "cancelled bounded preparation resumes only conservative background work");
        var continuation = Get(MainInstance, "_automaticAtContinuation");
        Equal(targetE.ToString(), JsonValue(continuation, "energyTarget"), "cancelled preparation retains exact unresolved energy gate");
        Equal(targetM.ToString(), JsonValue(continuation, "magicTarget"), "cancelled preparation retains exact unresolved magic gate");
        Near(phaseEnd, (double)Get(Json(continuation, "phaseEnd"), "AsDouble"), "cancelled preparation retains original AT phase deadline");
        Equal(false, Get(scheduler, "AutoAtCompleted"), "conservative continuation cannot fabricate cancelled AT completion");
        Equal(0L, Get(Get(Character, "wandoos98"), "wandoosEnergy"), "support refresh cannot fund W energy before gate");
        Equal(0L, Get(Get(Character, "wandoos98"), "wandoosMagic"), "support refresh cannot fund W magic before gate");
        CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(resumed, "Id") + "\"}"));
        CallMain("RunCustomPlanAutomation", plan);
        Equal(0, Get(scheduler, "StageIndex"), "explicit user cancel cannot complete unresolved AT entry");
        Equal(false, ((bool[])Get(scheduler, "Completed"))[0], "explicit user cancel leaves AT entry unearned");
        Equal(deadline, Get(scheduler, "ExpectedEndSeconds"), "explicit user cancel does not alter original entry deadline");
        ScheduledTime(1200); CallMain("ReconcileCustomResources", plan, true);
        Equal(1, Get(scheduler, "StageIndex"), "original deadline retires the cancelled AT entry");
    }

    private static void CheckCleanLegacyAtRecovery()
    {
        foreach (var kind in new[] { "missing", "invalid" })
        {
            object plan; var run = UnfinishedAtFixture(out plan); var entry = Get(run, "OwnerEntry"); var deadline = Get(run, "DeadlineRebirthSeconds");
            CallMain("EndCombatPrep", "cancelled", "Injector unloaded.", true);
            var path = (string)Get(MainInstance, "ProtectedCheckpointPath"); var saved = Parse(File.ReadAllText(path));
            Equal(false, Get(Json(Json(saved, "scheduler"), "autoCleanupRecoveryRequired"), "AsBool"), "clean unload has no cleanup marker: " + kind);
            Equal(true, Get(Json(Json(saved, "scheduler"), "autoEnergyPool"), "IsString"), "clean unload retains actual acquired-pool history: " + kind);
            Equal(entry.ToString().Insert(0, "fixture:"), JsonValue(Json(saved, "automaticSetup"), "entry"), "clean unload retains entry-bound setup history");
            if (kind == "missing") saved.GetType().GetMethod("Remove", new[] { typeof(string) }).Invoke(saved, new object[] { "automaticAtContinuation" });
            else Set(Json(Json(saved, "automaticAtContinuation"), "energyTarget"), "Value", "invalid");
            File.WriteAllText(path, saved.ToString()); ScheduledTime(550); ReplaceAutomaticCoordinator(plan);
            var resumed = StartScheduled(plan); var scheduler = Get(MainInstance, "_protectedScheduler");
            Equal("NGU", Get(ScheduledPhase(resumed), "Kind"), "marker-false legacy acquisition conservatively withholds unknown gate: " + kind);
            Equal(entry, Get(resumed, "OwnerEntry"), "marker-false fallback preserves original entry");
            Equal(deadline, Get(resumed, "DeadlineRebirthSeconds"), "marker-false fallback preserves original deadline");
            Equal(false, Get(scheduler, "AutoAtCompleted"), "marker-false fallback never claims unknown AT completed");
            Equal(0L, Get(Get(Character, "wandoos98"), "wandoosEnergy"), "marker-false fallback withholds W energy");
            Equal(0L, Get(Get(Character, "wandoos98"), "wandoosMagic"), "marker-false fallback withholds W magic");
            var durable = Parse(File.ReadAllText(path));
            Equal(true, Get(Json(Json(durable, "automaticSetup"), "safeResources"), "AsBool"), "marker-false fallback is durable");
            ScheduledTime(1200); CallMain("ReconcileCustomResources", plan, true);
            Equal(1, Get(scheduler, "StageIndex"), "marker-false fallback expires automatically");
        }
        foreach (var setupOnly in new[] { false, true })
        {
            var plan = ScheduledSetup(ScheduledRow + "," + StandardRow);
            Set(Get(Get(Character, "buttons"), "augmentation"), "m_Interactable", false);
            foreach (var name in new[] { "wandoosEnergy", "wandoosMagic" }) Set(Get(Get(Character, "advancedTrainingController"), name), "levelFactor", 1000f);
            var runtime = CallMain("GetProtectedStage", plan);
            if (setupOnly) Equal(true, CallMain("AutomaticStageSetupBudgetAvailable", runtime), "pre-acquisition setup clock is valid");
            CallMain("SaveProtectedStageProgress", true);
            var scheduler = Get(MainInstance, "_protectedScheduler");
            Equal(null, Get(scheduler, "AutoEnergyPool"), "untouched entry has no acquired pool");
            Equal(null, Get(MainInstance, "_combatPrepRun"), "untouched checkpoint has no native acquisition");
            ScheduledTime(550); ReplaceAutomaticCoordinator(plan);
            var run = StartScheduled(plan);
            Equal("AT", Get(ScheduledPhase(run), "Kind"), "fresh/setup-only checkpoints are not misclassified as lost AT: " + setupOnly);
            CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(run, "Id") + "\"}"));
        }
    }

    private static void CheckRestoredAtNativeWConflict()
    {
        foreach (var marker in new[] { false, true })
        foreach (var field in new[] { "energyLevel", "magicLevel", "energyProgress", "magicProgress", "wandoosEnergy", "wandoosMagic" })
        {
            ObserveAutomaticTransfers = true; AutomaticPaidAtTransfers = AutomaticPositiveTransfers = 0;
            try
            {
                object plan; var run = UnfinishedAtFixture(out plan);
                Equal(true, AutomaticPaidAtTransfers > 0, "read-only observer sees actual initial paid AT transfers");
                var entry = Get(run, "OwnerEntry"); var deadline = Get(run, "DeadlineRebirthSeconds");
                var path = (string)Get(MainInstance, "ProtectedCheckpointPath");
                var continuation = Json(Parse(File.ReadAllText(path)), "automaticAtContinuation").ToString();
                if (marker) CallMain("RecallCombatPrepResources");
                else CallMain("EndCombatPrep", "cancelled", "Injector unloaded.", true);
                var w = Get(Character, "wandoos98"); var os = Get(w, "os");
                var pinPath = Path.Combine((string)Get(MainType, "_dir"), "combat-prep-os.json"); var pin = "{retired-os-history";
                File.WriteAllText(pinPath, pin);
                if (field.EndsWith("Progress")) Set(w, field, .25f);
                else Set(w, field, 123L);
                if (field == "wandoosEnergy") Set(Character, "idleEnergy", (long)Get(Character, "idleEnergy") - 123);
                if (field == "wandoosMagic") Set(Get(Character, "magic"), "idleMagic", (long)Get(Get(Character, "magic"), "idleMagic") - 123);
                var eLevel = Get(w, "energyLevel"); var mLevel = Get(w, "magicLevel");
                var eProgress = Get(w, "energyProgress"); var mProgress = Get(w, "magicProgress");
                ScheduledTime(550); ReplaceAutomaticCoordinator(plan);
                AutomaticPaidAtTransfers = AutomaticPositiveTransfers = 0;
                CallMain("RunCustomPlanAutomation", plan); // Observe the FIRST dispatch, not a later cancellation/recall.
                Equal(0, AutomaticPaidAtTransfers, "native W conflict forbids even the first paid AT transfer: " + field + "/" + marker);
                Equal(true, AutomaticPositiveTransfers > 0, "native W conflict actually funds safe background resources");
                var resumed = Get(MainInstance, "_combatPrepRun"); var scheduler = Get(MainInstance, "_protectedScheduler");
                Equal("running", Get(resumed, "Status"), "native W conflict converges to running safe continuation");
                Equal("NGU", Get(ScheduledPhase(resumed), "Kind"), "native W conflict selects safe background, not paid AT");
                Equal(entry, Get(resumed, "OwnerEntry"), "native W conflict preserves unresolved entry");
                Equal(deadline, Get(resumed, "DeadlineRebirthSeconds"), "native W conflict preserves original deadline");
                Equal(false, Get(scheduler, "AutoAtCompleted"), "native W conflict does not fake AT completion");
                Equal(eLevel, Get(w, "energyLevel"), "native W energy levels are retained"); Equal(mLevel, Get(w, "magicLevel"), "native W magic levels are retained");
                Equal(eProgress, Get(w, "energyProgress"), "native W energy partial is retained"); Equal(mProgress, Get(w, "magicProgress"), "native W magic partial is retained");
                Equal(os, Get(w, "os"), "native W conflict cannot change OS"); Equal(pin, File.ReadAllText(pinPath), "native W conflict ignores retired OS history without rewriting it");
                var durable = Parse(File.ReadAllText(path));
                Equal(true, Get(Json(Json(durable, "automaticSetup"), "safeResources"), "AsBool"), "native W conflict safe continuation is durable before funding");
                Equal(continuation, Json(durable, "automaticAtContinuation").ToString(), "native W conflict retains exact unresolved AT continuation");
                ScheduledRefresh(resumed);
                Equal(0, AutomaticPaidAtTransfers, "native W conflict cannot retry paid AT on later tick");
                ScheduledTime(1200); CallMain("ReconcileCustomResources", plan, true);
                Equal(1, Get(scheduler, "StageIndex"), "native W conflict expires at original deadline");
            }
            finally { ObserveAutomaticTransfers = false; }
        }
        // Cleanup can erase a conflicting allocation. Its observed history must still survive the next dispatch.
        foreach (var energy in new[] { true, false })
        {
            object plan; var run = UnfinishedAtFixture(out plan); var deadline = Get(run, "DeadlineRebirthSeconds");
            var entry = Get(run, "OwnerEntry"); var scheduler = Get(MainInstance, "_protectedScheduler");
            CallMain("RecallCombatPrepResources");
            var w = Get(Character, "wandoos98"); Set(w, energy ? "wandoosEnergy" : "wandoosMagic", 123L);
            var pool = energy ? Character : Get(Character, "magic"); var idle = energy ? "idleEnergy" : "idleMagic";
            Set(pool, idle, (long)Get(pool, idle) - 123);
            ScheduledRefresh(run);
            Equal(true, ((string)Get(run, "Reason")).Contains("Wandoos work appeared"), "active AT guard observes native allocation conflict");
            Equal(0L, Get(w, energy ? "wandoosEnergy" : "wandoosMagic"), "safe owned cleanup recalls the conflicting allocation");
            AutomaticPaidAtTransfers = AutomaticPositiveTransfers = 0; ObserveAutomaticTransfers = true;
            try
            {
                var resumed = StartScheduled(plan);
                Equal(0, AutomaticPaidAtTransfers, "recalled native W conflict must not permit paid AT on the next dispatch");
                Equal("NGU", Get(ScheduledPhase(resumed), "Kind"), "active W conflict continues safe background until deadline");
                Equal(entry, Get(resumed, "OwnerEntry"), "active W conflict retains original entry");
                Equal(deadline, Get(scheduler, "ExpectedEndSeconds"), "active W conflict cannot shorten original deadline");
                Equal(false, Get(scheduler, "AutoAtCompleted"), "active W conflict leaves AT gate unresolved");
                Equal(true, ((IEnumerable)Get(Get(Character, "NGU"), "skills")).Cast<object>().Sum(x => (long)Get(x, "energy")) > 0,
                    "active W conflict continuation has actual native background funding, including a same-pass coordinator handoff");
                var saved = Parse(File.ReadAllText((string)Get(MainInstance, "ProtectedCheckpointPath")));
                Equal(true, Get(Json(Json(saved, "automaticSetup"), "safeResources"), "AsBool"), "active allocation conflict is durably retained after recall");
                CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(resumed, "Id") + "\"}"));
            }
            finally { ObserveAutomaticTransfers = false; }
        }
    }

    private static void CheckUnfinishedAtReinjection(double elapsed)
    {
        object plan; var run = UnfinishedAtFixture(out plan); var phase = ScheduledPhase(run);
        var targetE = Get(phase, "AtEnergyTarget"); var targetM = Get(phase, "AtMagicTarget");
        var phaseEnd = (double)Get(run, "PhaseDeadline") - (double)Get(run, "Started");
        var phaseStart = (double)Get(run, "PhaseStarted") - (double)Get(run, "Started");
        var deadline = Get(run, "DeadlineRebirthSeconds"); var entry = Get(run, "OwnerEntry");
        var os = Get(Get(Character, "wandoos98"), "os");
        var scheduler = Get(MainInstance, "_protectedScheduler"); Set(scheduler, "AutoAugmentsCompleted", true);
        CallMain("SaveProtectedStageProgress", true);
        var saved = Parse(File.ReadAllText((string)Get(MainInstance, "ProtectedCheckpointPath")));
        Equal(targetE.ToString(), JsonValue(Json(saved, "automaticAtContinuation"), "energyTarget"), "acquired AT target is durable despite native zero menu");
        Equal(0L, ((long[])Get(Get(Character, "advancedTraining"), "levelTarget"))[3], "native target menu cannot reconstruct acquired target");
        ScheduledTime(elapsed);
        var old = ReplaceAutomaticCoordinator(plan); var recalls = Recalls; var swaps = Swaps; var writes = ResourceCommittedWrites;
        var checkpointPath = (string)Get(MainInstance, "ProtectedCheckpointPath"); var checkpointBefore = File.ReadAllText(checkpointPath);
        Equal(true, Get(old, "_protectedCheckpointDurable"), "old coordinator had a previously successful checkpoint");
        Equal(false, Invoke(old, "SaveProtectedStageProgress", true), "stale coordinator save explicitly reports no transition was saved");
        Equal(checkpointBefore, File.ReadAllText(checkpointPath), "stale coordinator cannot rewrite successor checkpoint");
        Invoke(old, "TickCombatPrep"); Invoke(old, "QuickStuff"); Invoke(old, "AutomationRoutine");
        Equal(recalls, Recalls, "old coordinator has zero native recalls after reinjection"); Equal(swaps, Swaps, "old coordinator has zero swaps after reinjection");
        Equal(writes, ResourceCommittedWrites, "old coordinator has zero desired-map writes after reinjection");
        Equal(0, ((IList)Get(run, "RestoreTargets")).Count, "superseded coordinator permanently retires target callbacks");
        Equal(null, Get(run, "Setup"), "superseded coordinator permanently retires selected setup authority");
        Equal(false, Get(run, "NukeTimerOwned"), "superseded coordinator permanently retires timer permission");
        Equal(false, Get(run, "SetupVerified"), "superseded coordinator permanently retires setup verification");
        // Restart discarded cleanup authority; telemetry must remain observational.
        Equal(false, Get(MainInstance, "CombatPrepCleanupRecoveryRequired"), "restart imports no historical cleanup authority");
        CallMain("GetCombatPrepRuntime");
        Equal(false, Get(MainInstance, "CombatPrepCleanupRecoveryRequired"), "telemetry cannot recreate historical cleanup authority");
        Equal(checkpointBefore, File.ReadAllText(checkpointPath), "telemetry does not rewrite restarted stage progress");
        Equal(writes, ResourceCommittedWrites, "telemetry performs no native cleanup or funding");
        var resumed = StartScheduled(plan); phase = ScheduledPhase(resumed);
        Equal(false, Get(MainInstance, "CombatPrepCleanupRecoveryRequired"), "current coordinator clears recovery automatically before fresh acquisition");
        Equal(targetE, Get(phase, "AtEnergyTarget"), "energy acquired gate survives fresh capture");
        Equal(targetM, Get(phase, "AtMagicTarget"), "magic acquired gate survives fresh capture");
        Near(phaseEnd, (double)Get(resumed, "PhaseDeadline") - (double)Get(resumed, "Started"), "original AT phase deadline survives reinjection (expected " + phaseEnd + ")");
        Near(phaseStart, (double)Get(resumed, "PhaseStarted") - (double)Get(resumed, "Started"), "original AT phase origin survives reinjection");
        Equal(entry, Get(resumed, "OwnerEntry"), "fresh acquisition keeps entry"); Equal(deadline, Get(resumed, "DeadlineRebirthSeconds"), "fresh acquisition keeps deadline");
        Equal(os, Get(Get(Character, "wandoos98"), "os"), "fresh acquisition keeps pinned OS");
        Equal(true, Get(Get(MainInstance, "_protectedScheduler"), "AutoAugmentsCompleted"), "completed augment history survives fresh capture");
        var levels = (long[])Get(Get(Character, "advancedTraining"), "level"); levels[3] = (long)targetE;
        ScheduledRefresh(resumed);
        Equal("AT", Get(ScheduledPhase(resumed), "Kind"), "one reached gate cannot abandon acquired AT");
        Equal(0L, Get(Get(Character, "wandoos98"), "wandoosEnergy"), "W energy remains zero until both gates");
        Equal(0L, Get(Get(Character, "wandoos98"), "wandoosMagic"), "W magic remains zero until both gates");
        var targets = (long[])Get(Get(Character, "advancedTraining"), "levelTarget");
        targets[3] = targets[4] = 0; // Equal target values do not confer old callback authority.
        recalls = Recalls; writes = ResourceCommittedWrites; Invoke(old, "EndCombatPrep", "cancelled", "late callback", true);
        Equal(recalls, Recalls, "equal native targets do not authorize old coordinator cleanup");
        Equal(writes, ResourceCommittedWrites, "equal successor targets never authorize stale desired-map cleanup");
        Equal(0L, targets[3], "old restore callback cannot overwrite equal successor target");
        levels[4] = (long)targetM; Set(resumed, "PhaseDeadline", (double)Get(MainType, "CombatPrepClock") - 1);
        ScheduledRefresh(resumed);
        Equal(true, Get(Get(MainInstance, "_protectedScheduler"), "AutoAtCompleted"), "both known targets record actual AT completion");
        Equal(false, ((Array)Get(Get(resumed, "Policy"), "Phases")).Cast<object>().Any(x => (string)Get(x, "Kind") == "AT"), "completed AT is not bought again");
        CallMain("EndCombatPrep", "cancelled", "test cleanup", true);
    }

    private static void CheckExpiredReinjectionFunding()
    {
        object plan; var run = UnfinishedAtFixture(out plan); var originalDeadline = Get(run, "DeadlineRebirthSeconds");
        ScheduledTime(1200); ReplaceAutomaticCoordinator(plan);
        var scheduler = Get(MainInstance, "_protectedScheduler"); var recalls = Recalls; var writes = ResourceCommittedWrites;
        CallMain("ReconcileCustomResources", plan, true);
        Equal(1, Get(scheduler, "StageIndex"), "expired reinjection advances via existing elapsed scheduler rules");
        Equal(true, ((bool[])Get(scheduler, "Completed"))[0], "elapsed automatic entry completes without manual override");
        Equal("", Get(scheduler, "LastOverride"), "recovery invents no override history");
        Equal(0d, ((double[])Get(scheduler, "EarnedSeconds"))[0], "offline automatic recovery earns no protected time");
        Equal(true, ResourceCommittedWrites > writes, "same reconciliation pass performs actual successor desired-map funding");
        Equal(true, ((IEnumerable)Get(Get(Character, "NGU"), "skills")).Cast<object>().Sum(x => (long)Get(x, "energy")) > 0, "actual successor selected NGU energy is funded");
        Equal(true, ((IEnumerable)Get(Get(Character, "NGU"), "magicSkills")).Cast<object>().Sum(x => (long)Get(x, "magic")) > 0, "actual successor selected NGU magic is funded");
        Equal(1100d, originalDeadline, "expired entry had its original deadline");
    }

    private static void CheckRecoveryPersistenceRetry()
    {
        object plan; var run = UnfinishedAtFixture(out plan); var entry = Get(run, "OwnerEntry"); var deadline = Get(run, "DeadlineRebirthSeconds");
        ReplaceAutomaticCoordinator(plan); var scheduler = Get(MainInstance, "_protectedScheduler");
        Equal(false, Get(MainInstance, "CombatPrepCleanupRecoveryRequired"), "restart drops the old cleanup marker before testing a fresh transition");
        Set(scheduler, "AutoCleanupRecoveryRequired", true);
        Equal(true, CallMain("SaveProtectedStageProgress", true), "fresh current-session transition marker is durably recorded");
        var path = (string)Get(MainInstance, "ProtectedCheckpointPath"); var recalls = Recalls; var swaps = Swaps;
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            CallMain("ServiceAutomaticRecovery");
            Equal(true, Get(MainInstance, "CombatPrepCleanupRecoveryRequired"), "failed clear write retains entry-bound marker");
            Equal(false, Get(MainInstance, "_protectedCheckpointDurable"), "old durable state does not prove marker clear saved");
            Equal(deadline, Get(scheduler, "ExpectedEndSeconds"), "failed save does not complete automatic entry");
            var attempt = Get(MainInstance, "_protectedLastSavedAt");
            for (var i = 0; i < 20; i++) CallMain("ServiceAutomaticRecovery");
            Equal(attempt, Get(MainInstance, "_protectedLastSavedAt"), "write failures have controlled retry cadence");
            Equal(recalls, Recalls, "failed recovery save never funds native resources"); Equal(swaps, Swaps, "failed recovery save never restores gear");
            Equal(true, ((string)Get(MainInstance, "_resourceExecutionReason")).Contains("permissions"), "save blocker has actionable diagnostic");
            CallMain("RunCustomPlanAutomation", plan);
            Equal("Failed", Get(MainInstance, "_resourceExecutionState").ToString(), "blocked plan dispatch retains useful failed state, not empty waiting");
            Equal(true, ((string)Get(MainInstance, "_resourceExecutionReason")).Contains("permissions"), "blocked plan dispatch retains actionable retry reason");
        }
        Set(MainInstance, "_automaticRecoveryRetryAt", -1d);
        CallMain("ServiceAutomaticRecovery");
        Equal(false, Get(MainInstance, "CombatPrepCleanupRecoveryRequired"), "recovery retries automatically after file becomes writable");
        Equal(false, Get(Json(Json(Parse(File.ReadAllText(path)), "scheduler"), "autoCleanupRecoveryRequired"), "AsBool"), "retry durably clears exact marker");
        Equal(entry, Get(CallMain("GetProtectedStage", plan), "EntryIdentity"), "save retry does not fabricate new entry");
        var resumed = StartScheduled(plan); CallMain("EndCombatPrep", "cancelled", "test cleanup", true);
    }

    private static void CheckRecoveryLegacyAndMalformed()
    {
        foreach (var kind in new[] { "legacy", "malformed", "missing-setup", "malformed-at" })
        {
            object plan; var run = UnfinishedAtFixture(out plan); var deadline = Get(run, "DeadlineRebirthSeconds");
            var path = (string)Get(MainInstance, "ProtectedCheckpointPath"); var saved = Parse(File.ReadAllText(path));
            if (kind == "legacy") saved.GetType().GetMethod("Remove", new[] { typeof(string) }).Invoke(saved, new object[] { "automaticAtContinuation" });
            else if (kind == "missing-setup") saved.GetType().GetMethod("Remove", new[] { typeof(string) }).Invoke(saved, new object[] { "automaticSetup" });
            else if (kind == "malformed-at") Set(Json(Json(saved, "automaticAtContinuation"), "energyTarget"), "Value", "not-a-number");
            else Set(Json(Json(saved, "automaticSetup"), "entry"), "Value", "wrong-entry");
            File.WriteAllText(path, saved.ToString()); ScheduledTime(550); ReplaceAutomaticCoordinator(plan);
            var resumed = StartScheduled(plan); var scheduler = Get(MainInstance, "_protectedScheduler");
            Equal("NGU", Get(ScheduledPhase(resumed), "Kind"), "unknown gate/timing uses safe automatic fallback: " + kind);
            Equal(false, Get(scheduler, "AutoAtCompleted"), "fallback never claims unknown gate completed: " + kind);
            Equal(deadline, Get(resumed, "DeadlineRebirthSeconds"), "fallback never grants new deadline: " + kind);
            Equal(0L, Get(Get(Character, "wandoos98"), "wandoosEnergy"), "fallback cannot bypass unknown AT with W energy");
            Equal(0L, Get(Get(Character, "wandoos98"), "wandoosMagic"), "fallback cannot bypass unknown AT with W magic");
            var ngu = ((IEnumerable)Get(Get(Character, "NGU"), "skills")).Cast<object>().Sum(x => (long)Get(x, "energy"));
            Equal(true, (long)Get(Get(Character, "machine"), "speedEnergy") + ngu > 0, "safe fallback actually funds background resources");
            ScheduledTime(1200); CallMain("ReconcileCustomResources", plan, true);
            Equal(1, Get(scheduler, "StageIndex"), "fallback expires automatically without permanent latch: " + kind);
        }
    }

    private static void CheckAutomaticTransitionSaveFailure()
    {
        foreach (var expiry in new[] { "rebirth", "setup" })
        {
            object plan; var run = UnfinishedAtFixture(out plan);
            var scheduler = Get(MainInstance, "_protectedScheduler"); var deadline = (double)Get(scheduler, "ExpectedEndSeconds");
            var entry = Get(scheduler, "Entry"); var entered = Get(scheduler, "EnteredAt");
            var earned = ((double[])Get(scheduler, "EarnedSeconds")).ToArray();
            CallMain("EndCombatPrep", "cancelled", "Injector unloaded.", true);
            Equal(deadline, Get(scheduler, "ExpectedEndSeconds"), "clean cancellation retains the original entry deadline: " + expiry);
            var path = (string)Get(MainInstance, "ProtectedCheckpointPath");
            if (expiry == "rebirth") ScheduledTime(1200);
            else
            {
                Set(MainInstance, "_automaticStageSetupStarted", (double)Get(MainType, "CombatPrepClock") - 601);
                var targets = Plugin.GetType("NGUInjector.Managers.ResourceTarget", true);
                var paid = new HashSet<int>(new[] { "AdvancedTraining", "Augment", "AugmentUpgrade", "WandoosEnergy", "WandoosMagic" }
                    .Select(name => (int)Enum.Parse(targets, name)));
                var paidWrites = 0;
                Action<object, int, int, long, long> observe = (c, target, index, before, desired) => {
                    if (desired > before && paid.Contains(target)) paidWrites++;
                };
                ResourceModelWriteAfter += observe;
                try
                {
                    foreach (var elapsed in new[] { (double)Get(Get(Character, "rebirthTime"), "totalseconds"), deadline - .001d })
                    {
                        ScheduledTime(elapsed); CallMain("RunCustomPlanAutomation", plan);
                        Equal(0, Get(scheduler, "StageIndex"), "cancelled wall-clock expiry cannot select a successor before the original deadline");
                        Equal(entry, Get(scheduler, "Entry"), "cancelled wall-clock expiry preserves the original entry");
                        Equal(entered, Get(scheduler, "EnteredAt"), "cancelled wall-clock expiry cannot renew the entry start");
                        Equal(deadline, Get(scheduler, "ExpectedEndSeconds"), "cancelled wall-clock expiry cannot reset the original budget");
                        Equal(false, ((bool[])Get(scheduler, "Completed"))[0], "cancelled wall-clock expiry is not completed preparation");
                        Equal(false, Get(scheduler, "AutoAtCompleted"), "cancelled wall-clock expiry cannot fabricate the unfinished AT gate");
                        Equal(true, earned.SequenceEqual((double[])Get(scheduler, "EarnedSeconds")), "cancelled wall-clock expiry invents no earned stage credit");
                        Equal(0, paidWrites, "cancelled wall-clock expiry never reacquires paid AT, augments or Wandoos");
                    }
                }
                finally { ResourceModelWriteAfter -= observe; }
                ScheduledTime(deadline);
            }
            var recalls = Recalls; var swaps = Swaps; var writes = ResourceCommittedWrites;
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                CallMain("RunCustomPlanAutomation", plan);
                Equal(0, Get(scheduler, "StageIndex"), "failed transition save cannot select successor: " + expiry);
                Equal(false, ((bool[])Get(scheduler, "Completed"))[0], "failed transition save is not stage completion: " + expiry);
                Equal(deadline, Get(scheduler, "ExpectedEndSeconds"), "failed transition save cannot shorten original deadline: " + expiry);
                var last = Get(MainInstance, "_protectedLastSavedAt");
                for (var i = 0; i < 20; i++) CallMain("RunCustomPlanAutomation", plan);
                Equal(last, Get(MainInstance, "_protectedLastSavedAt"), "failed automatic transition retries at controlled cadence: " + expiry);
                Equal(recalls, Recalls, "unsaved transition cannot fund successor resources: " + expiry);
                Equal(writes, ResourceCommittedWrites, "unsaved transition prevents successor desired-map model writes: " + expiry);
                Equal(swaps, Swaps, "unsaved transition cannot acquire successor gear: " + expiry);
            }
            Set(MainInstance, "_automaticRecoveryRetryAt", -1d);
            CallMain("RunCustomPlanAutomation", plan); CallMain("RunCustomPlanAutomation", plan);
            Equal(1, Get(scheduler, "StageIndex"), "writable transition eventually funds selected successor: " + expiry);
            Equal(true, ResourceCommittedWrites > writes, "retry executes actual successor desired-map allocation: " + expiry);
        }
    }

    private static void CheckRecoveryEntryIsolation()
    {
        object plan; var oldRun = UnfinishedAtFixture(out plan); var scheduler = Get(MainInstance, "_protectedScheduler");
        var callbacks = (IList)Get(oldRun, "RestoreTargets"); Equal(true, callbacks.Count > 0, "entry isolation has acquired restoration callbacks");
        Invoke(scheduler, "OverrideStage", plan, "restart", 0, 500d, Get(MainType, "ProtectedHeartbeat"));
        var entry = Get(scheduler, "Entry"); var deadline = Get(scheduler, "ExpectedEndSeconds");
        var recalls = Recalls; var swaps = Swaps; var writes = ResourceCommittedWrites; CallMain("TickCombatPrep");
        Equal(0, callbacks.Count, "same-coordinator old entry permanently discards callbacks");
        Equal(0L, ((long[])Get(Get(Character, "advancedTraining"), "levelTarget"))[3], "equal new-entry energy menu cannot trigger old callback");
        Equal(0L, ((long[])Get(Get(Character, "advancedTraining"), "levelTarget"))[4], "equal new-entry magic menu cannot trigger old callback");
        Equal(null, Get(oldRun, "Setup"), "old entry discards selected application authority");
        Equal(false, Get(oldRun, "NukeTimerOwned"), "old entry discards timer permission");
        Equal(false, Get(oldRun, "SetupVerified"), "old entry cannot retain setup verification");
        Equal(recalls, Recalls, "same-coordinator entry guard prevents first cleanup recall"); Equal(swaps, Swaps, "same-coordinator entry guard prevents first cleanup swap");
        Equal(writes, ResourceCommittedWrites, "same-coordinator entry guard prevents every stale cleanup model write");
        Equal(entry, Get(scheduler, "Entry"), "old cleanup cannot transition successor entry"); Equal(deadline, Get(scheduler, "ExpectedEndSeconds"), "old cleanup cannot shorten successor deadline");
        var resumed = StartScheduled(plan); CallMain("EndCombatPrep", "cancelled", "test cleanup", true);
    }

    private static void CheckRecoveryPauseAndSwitch()
    {
        foreach (var change in new[] { "pause", "switch" })
        {
            object plan; var run = UnfinishedAtFixture(out plan); var deadline = Get(run, "DeadlineRebirthSeconds"); var entry = Get(run, "OwnerEntry");
            if (change == "pause") Setting("UseCustomRebirthPlan", false); else Setting("CustomPlanFile", "other");
            var recalls = Recalls; var swaps = Swaps; var writes = ResourceCommittedWrites; CallMain("TickCombatPrep");
            Equal(recalls, Recalls, "pause/switch retirement is write-free: " + change); Equal(swaps, Swaps, "pause/switch retirement does not restore gear: " + change);
            Equal(writes, ResourceCommittedWrites, "pause/switch retirement cannot clear desired-map allocations: " + change);
            Equal(deadline, Get(Get(MainInstance, "_protectedScheduler"), "ExpectedEndSeconds"), "pause/switch preserves original deadline");
            CallMain("ServiceAutomaticRecovery");
            Setting("UseCustomRebirthPlan", true); Setting("CustomPlanFile", "fixture"); ScheduledTime(550);
            var resumed = StartScheduled(plan); Equal(entry, Get(resumed, "OwnerEntry"), "pause/switch resumes original entry");
            Equal(deadline, Get(resumed, "DeadlineRebirthSeconds"), "pause/switch resumes only original budget");
            CallMain("EndCombatPrep", "cancelled", "test cleanup", true);
        }
    }

    private static void CheckRecoveryCleanupExhaustion()
    {
        object plan, original; var run = CleanupRecoveryFixture(out plan, out original);
        var entry = Get(run, "OwnerEntry"); var deadline = Get(run, "DeadlineRebirthSeconds");
        var phaseGear = CallMain("CombatPrepEquipped"); var support = CallMain("CombatPrepSupportKey"); var swaps = Swaps;
        Set(Controller, "midDrag", true); CallMain("EndCombatPrep", "cancelled", "Injector unloaded.", true);
        Equal(false, Get(run, "CleanupPending"), "drag does not manufacture a historical gear/support return obligation");
        for (var i = 0; i < 5; i++) { Set(run, "CleanupLastAttempt", (double)Get(MainType, "CombatPrepClock") - .51); CallMain("TickCombatPrep"); }
        Equal(false, Get(run, "CleanupPending"), "ended claim never creates delayed restoration retries");
        Equal(0, ((IList)Get(run, "RestoreTargets")).Count, "borrowed callbacks are released individually");
        Equal(swaps, Swaps, "later callbacks make no historical gear swaps");
        Equal(true, CallMain("CombatPrepGearMatches", phaseGear), "phase gear remains until a current successor requests a difference");
        Equal(support, CallMain("CombatPrepSupportKey"), "support remains without a historical return baseline");
        Set(Controller, "midDrag", false); CallMain("ServiceAutomaticRecovery");
        Equal(entry, Get(CallMain("GetProtectedStage", plan), "EntryIdentity"), "retired recovery does not complete unexpired entry");
        Equal(deadline, Get(Get(MainInstance, "_protectedScheduler"), "ExpectedEndSeconds"), "retirement keeps original deadline");
        var resumed = StartScheduled(plan); Equal("running", Get(resumed, "Status"), "fresh current capture can proceed without old original gear obligation");
        CallMain("EndCombatPrep", "cancelled", "test cleanup", true);
    }

    private static void CheckFirstCleanupAuthorityLoss()
    {
        foreach (var lost in new[] { "timer", "targets", "support" })
        {
            object plan; var run = UnfinishedAtFixture(out plan);
            var targets = (long[])Get(Get(Character, "advancedTraining"), "levelTarget");
            var timer = Get(Get(Character, "arbitrary"), "nukeTimer");
            if (lost == "timer") Set(timer, "totalseconds", 20d);
            if (lost == "targets") targets[3] = 123;
            if (lost == "support") Set(((IList)Get(Get(Character, "diggers"), "diggers"))[11], "active", true);
            var recalls = Recalls; var swaps = Swaps; var writes = ResourceCommittedWrites; var beforeTimer = Get(timer, "totalseconds");
            CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(run, "Id") + "\"}"));
            Equal(false, Get(MainInstance, "CombatPrepRunActive"), "first release removes the live combat claim: " + lost);
            Equal(recalls, Recalls, "first cleanup cannot recall after authority loss: " + lost);
            if (lost == "timer") Equal(writes, ResourceCommittedWrites, "lost native combat timer authority preserves funded prerequisites");
            else Equal(writes, ResourceCommittedWrites, "current successor owns resource reconciliation; old combat must not clear before it: " + lost);
            Equal(swaps, Swaps, "first cleanup cannot swap after authority loss: " + lost);
            Equal(beforeTimer, Get(timer, "totalseconds"), "lost authority retirement cannot restore native timer: " + lost);
            Equal(lost == "targets" ? 123L : lost == "timer" ? 0L : 5000L, targets[3], "each borrowed target returns only its own still-owned field: " + lost);
            Equal(0, ((IList)Get(run, "RestoreTargets")).Count, "lost first-cleanup authority discards callbacks permanently: " + lost);
            Equal(null, Get(run, "Setup"), "lost first-release authority discards application authority: " + lost);
        }
    }
}
