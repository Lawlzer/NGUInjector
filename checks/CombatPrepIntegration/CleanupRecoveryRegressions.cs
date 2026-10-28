using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

public static partial class Program
{
    private static object CleanupRecoveryFixture(out object plan, out object original, bool successor = true)
    {
        plan = ScheduledSetup(ScheduledRow + (successor ? "," + StandardRow : ""));
        Set(Get(Get(Character, "buttons"), "augmentation"), "m_Interactable", false);
        Set(((IList)Get(Get(Character, "wishes"), "wishes"))[190], "level", 1L);
        var inventory = Get(Character, "inventory");
        var head = Blank(Get(inventory, "head").GetType()); Set(head, "id", 60); SetEnum(head, "type", "Head");
        Set(head, "bossRequired", 1); Set(head, "removable", true); SetEnum(head, "spec1Type", "Wandoos98"); Set(head, "spec1Cur", 1e20f);
        ((IList)Get(inventory, "inventory"))[0] = head;
        Set(Get(Get(Character, "arbitrary"), "nukeTimer"), "totalseconds", 17d);
        original = CallMain("CombatPrepEquipped");
        var run = StartScheduled(plan);
        Equal(false, CallMain("CombatPrepGearMatches", original), "cleanup recovery fixture actually owns non-original phase gear");
        return run;
    }

    private static void CheckCleanupRecoveryRegressions()
    {
        CheckCleanupCadence();
        CheckCleanupCombatHandoff();
        CheckCleanupUnloadRecovery();
    }

    private static void CheckCleanupCadence()
    {
        object plan, original;
        var run = CleanupRecoveryFixture(out plan, out original, false);
        ControlledCombatPrepClock = (double)Get(MainType, "CombatPrepClock");
        ScheduledTime((double)Get(run, "DeadlineRebirthSeconds"));
        Equal(false, CallMain("CombatPrepHasResourceSuccessor", run, "cancelled", "Cancelled by user."), "terminal deadline leaves only the unfinished owned cleanup, not a safe-background successor");
        FailDesiredEnergyPreflight = true;
        var oldWriter = Get(MainType, "OutputWriter");
        using (var log = new MemoryStream())
        using (var writer = new StreamWriter(log, new UTF8Encoding(false), 1024, true) { AutoFlush = true })
        {
            try
            {
                Set(MainType, "OutputWriter", writer);
                var args = Parse("{\"runId\":\"" + Get(run, "Id") + "\"}");
                CallMain("EndCombatPrep", "cancelled", "Injector unloaded.", true);
                var message = (string)CallMain("CancelCombatPrep", args);
                Equal(false, FailDesiredEnergyPreflight, "unload release reaches and consumes the native resource preflight fault");
                Equal(true, message.Contains("pending"), "cancel reports pending cleanup rather than releasing ownership");
                Equal(true, Get(run, "CleanupPending"), "an actual resource preflight failure leaves narrow release pending");
                Equal(true, Get(run, "NukeTimerOwned"), "nuke lease is retained while equipment cleanup is unfinished");
                var timer = Get(Get(Character, "arbitrary"), "nukeTimer");
                Equal(0d, Get(timer, "totalseconds"), "original timer is not restored before equipment cleanup");
                var recalls = Recalls; var writes = ResourceCommittedWrites; var attempts = Get(run, "CleanupAttemptCount");
                var warnings = ((IList)Get(run, "Warnings")).Count; var bytes = log.Length;
                Set(run, "CleanupLastAttempt", (double)Get(MainType, "CombatPrepClock") + 100);
                for (var frame = 0; frame < 1000; frame++)
                {
                    CallMain("TickCombatPrep");
                    if (frame % 100 == 0) CallMain("CancelCombatPrep", args);
                }
                Equal(attempts, Get(run, "CleanupAttemptCount"), "per-frame ticks and repeated cancel cannot bypass cleanup cadence");
                Equal(recalls, Recalls, "completed resource recall is never repeated while waiting for drag");
                Equal(writes, ResourceCommittedWrites, "waiting for drag never replays completed desired-map resource release");
                Equal(bytes, log.Length, "waiting frames and repeated cancel emit no repeated logs");
                Equal(warnings, ((IList)Get(run, "Warnings")).Count, "waiting frames append no diagnostics");
                for (var due = 0; due < 2; due++)
                {
                    FailDesiredEnergyPreflight = true;
                    Set(run, "CleanupLastAttempt", (double)Get(MainType, "CombatPrepClock") - .51);
                    CallMain("TickCombatPrep");
                }
                Equal((int)attempts + 2, Get(run, "CleanupAttemptCount"), "short safe cleanup attempts remain retryable without sleeping");
                Equal(recalls, Recalls, "only unfinished cleanup components are retried");
                Equal(writes, ResourceCommittedWrites, "due cleanup retries also preserve already-completed model accounting");
                Equal(warnings, ((IList)Get(run, "Warnings")).Count, "repeated identical release warnings remain deduplicated");
                Set(Controller, "midDrag", false);
                Set(run, "CleanupLastAttempt", (double)Get(MainType, "CombatPrepClock") - .51);
                CallMain("AdvanceCombatPrep"); // Observe this release before ordinary same-entry continuation starts.
                Equal(false, Get(run, "CleanupPending"), "cleanup recovers when drag finishes");
                Equal(false, Get(MainInstance, "CombatPrepCleanupRecoveryRequired"), "safe cleanup clears durable recovery requirement");
                Equal(false, CallMain("CombatPrepGearMatches", original), "release never restores original gear");
                Equal(17d, Get(timer, "totalseconds"), "nuke lease restores only after equipment restoration finishes");
            }
            finally { ControlledCombatPrepClock = null; Set(MainType, "OutputWriter", oldWriter); Set(Controller, "midDrag", false); }
        }
    }

    private static void CheckCleanupCombatHandoff()
    {
        foreach (var handoff in new[] { "fight", "nuke", "epoch", "timer" })
        {
            object plan, original;
            var run = CleanupRecoveryFixture(out plan, out original, false);
            FailDesiredEnergyPreflight = true;
            CallMain("EndCombatPrep", "cancelled", "Injector unloaded.", true);
            Equal(false, FailDesiredEnergyPreflight, "handoff begins from an actually failed resource release");
            Equal(true, Get(run, "CleanupPending"), "handoff fixture retains narrow current-session release authority");
            Set(Controller, "midDrag", false);
            var phaseGear = CallMain("CombatPrepEquipped"); var swaps = Swaps; var recalls = Recalls; var writes = ResourceCommittedWrites;
            var timer = Get(Get(Character, "arbitrary"), "nukeTimer");
            var boss = Get(Character, "bossController");
            if (handoff == "epoch") Set(Get(Character, "stats"), "rebirthNumber", 8L);
            else if (handoff == "timer") Set(timer, "totalseconds", 20d);
            else Set(boss, handoff == "fight" ? "isFighting" : "nukeBoss", true);
            CallMain("AdvanceCombatPrep"); // Isolate the old claim from legitimate current-successor reconciliation.
            Equal(swaps, Swaps, "pending cleanup cannot swap during a fight/nuke or across epochs: " + handoff);
            Equal(recalls, Recalls, "pending cleanup cannot recall a successor owner's resources: " + handoff);
            Equal(writes, ResourceCommittedWrites, "pending cleanup cannot mutate a successor's desired-map resources: " + handoff);
            Equal(handoff == "timer" ? 20d : 0d, Get(timer, "totalseconds"), "handoff releases lease without rewriting native timer: " + handoff);
            Equal(false, Get(run, "NukeTimerOwned"), "handoff retires native nuke ownership: " + handoff);
            Equal(true, Get(run, "Retired"), "handoff permanently retires old claim authority: " + handoff);
            Equal(false, Get(run, "CleanupPending"), "handoff retires unsafe live retries: " + handoff);
            Equal(false, Get(run, "SetupVerified"), "same-pass recovery never recreates the old combat verification: " + handoff);
            if (handoff == "epoch") Equal(false, Get(MainInstance, "CombatPrepCleanupRecoveryRequired"), "recovery metadata is bound to its original save epoch");
            Set(boss, "isFighting", false); Set(boss, "nukeBoss", false);
            for (var tick = 0; tick < 10; tick++) CallMain("AdvanceCombatPrep");
            CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(run, "Id") + "\"}"));
            Equal(swaps, Swaps, "no delayed old-owner writes after combat handoff: " + handoff);
            Equal(writes, ResourceCommittedWrites, "no delayed desired-map writes after combat handoff: " + handoff);
            Equal(true, CallMain("CombatPrepGearMatches", phaseGear), "retired coordinator never restores its old gear map: " + handoff);
        }
    }

    private static void CheckCleanupUnloadRecovery()
    {
        foreach (var dispatch in new[] { "RunCustomPlanAutomation", "QuickStuff", "AutomationRoutine" })
        {
            object plan, original;
            var run = CleanupRecoveryFixture(out plan, out original);
            var entry = Get(run, "OwnerEntry"); var deadline = Get(run, "DeadlineRebirthSeconds");
            FailDesiredEnergyPreflight = true;
            CallMain("EndCombatPrep", "cancelled", "Injector unloaded.", true);
            var phaseGear = CallMain("CombatPrepEquipped"); var store = Get(MainType, "CustomPlanStore");
            ScheduledTime(1200);
            var oldCoordinator = MainInstance;
            CallMain("Unload"); // Actual lifecycle, including checkpoint save and static-reference disposal.
            Equal(null, Get(MainType, "reference"), "unload discards the old coordinator");
            Equal("Unloaded", Get(oldCoordinator, "_executionLifecycle").ToString(), "unload revokes ordinary and bounded-retirement retry authority even when diagnostic cleanup metadata remains");
            MainInstance = Blank(MainType); Set(MainInstance, "_combatPrepEndedRuns", new HashSet<string>());
            Set(MainInstance, "_customIronPillPrepStartedAt", -1d);
            Set(MainType, "reference", MainInstance); Set(MainType, "Character", Character); Set(MainType, "Controller", Controller);
            Set(MainType, "Settings", Settings); Set(MainType, "CustomPlanStore", store);
            Set(Controller, "midDrag", false);
            var recalls = Recalls; var swaps = Swaps; var writes = ResourceCommittedWrites;
            Invoke(oldCoordinator, "TickCombatPrep");
            Equal(swaps, Swaps, "discarded coordinator cannot write through old references after reinjection");
            Equal(writes, ResourceCommittedWrites, "discarded coordinator cannot write through old resource applications after reinjection");
            var runtime = CallMain("GetProtectedStage", plan);
            var scheduler = Get(MainInstance, "_protectedScheduler");
            Equal(1, Get(runtime, "StageIndex"), "expired restored entry selects its ordinary successor without acknowledgement");
            Equal(false, Equals(entry, Get(runtime, "EntryIdentity")), "successor has its own entry identity");
            Equal(1100d, deadline, "original automatic deadline was not renewed");
            Equal(false, Get(MainInstance, "CombatPrepCleanupRecoveryRequired"), "coordinator durably retires orphan restoration marker");
            Equal(recalls, Recalls, "marker retirement itself never recalls native resources");
            Equal(writes, ResourceCommittedWrites, "marker retirement itself never applies desired-map mutations");
            Equal(swaps, Swaps, "marker retirement never recreates old Unity restoration writes");
            Equal(true, CallMain("CombatPrepGearMatches", phaseGear), "retirement preserves native phase gear until current legitimate intent applies");
            if (dispatch == "RunCustomPlanAutomation") CallMain(dispatch, plan); else CallMain(dispatch);
            Equal(true, ResourceCommittedWrites > writes, "normal selected successor actually receives desired-map resource funding: " + dispatch);
            Equal(true, Get(MainInstance, "_protectedCheckpointDurable"), "recovery transition is persisted before allocation resumes");
        }
    }
}
