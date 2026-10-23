using System;
using System.Collections;
using System.Linq;

public static partial class Program
{
    private static void CheckReviewRegressions()
    {
        CheckUnfinishedAtGpsGate();
        CheckAutomaticEquipmentWait();
        CheckAutomaticCleanupRetry();
        CheckAutomaticAllocatedBudgetLimit();
        CheckPreviewResourceFocusFields();
        CheckFreeAtGateTelemetry();
    }

    private static void CheckUnfinishedAtGpsGate()
    {
        var plan = ScheduledSetup();
        Set(Get(Get(Character, "buttons"), "augmentation"), "m_Interactable", false);
        foreach (var name in new[] { "wandoosEnergy", "wandoosMagic" })
            Set(Get(Get(Character, "advancedTrainingController"), name), "levelFactor", 1000f);
        var run = StartScheduled(plan); var phase = ScheduledPhase(run); var policy = Get(run, "Policy");
        Equal("AT", Get(phase, "Kind"), "real planner acquires AT for GPS gate regression");
        Equal(true, Get(Json(CallMain("GetCombatPrepRuntime"), "atGateRequired"), "AsBool"), "executable AT phase exposes a required gate");
        var deadline = Get(run, "DeadlineRebirthSeconds");
        var targets = (long[])Get(Get(Character, "advancedTraining"), "levelTarget");
        var levels = (long[])Get(Get(Character, "advancedTraining"), "level");
        var targetE = (long)Get(phase, "AtEnergyTarget"); var targetM = (long)Get(phase, "AtMagicTarget");
        Equal(true, targetE > 0 && targetM > 0, "both acquired AT gates are nonzero");
        Set(Get(Character, "machine"), "realBaseGold", 1000000d);
        foreach (var elapsedBoundary in new[] { false, true })
        {
            if (elapsedBoundary) Set(run, "PhaseDeadline", (double)Get(MainType, "CombatPrepClock") - 1);
            ScheduledRefresh(run);
            Equal(true, ReferenceEquals(policy, Get(run, "Policy")), "GPS shift cannot replace unfinished acquired AT policy");
            Equal(0, Get(run, "ReplanCount"), "GPS replan is deferred until original AT gate passes");
            Equal(targetE, Get(ScheduledPhase(run), "AtEnergyTarget"), "energy gate is not reduced");
            Equal(targetM, Get(ScheduledPhase(run), "AtMagicTarget"), "magic gate is not reduced");
            Equal(0L, targets[3], "acquired AT target ownership retained");
            Equal(0L, Get(Get(Character, "wandoos98"), "wandoosEnergy"), "GPS shift never funds W energy before AT");
            Equal(0L, Get(Get(Character, "wandoos98"), "wandoosMagic"), "GPS shift never funds W magic before AT");
            Equal(false, Get(Json(CallMain("GetCombatPrepRuntime"), "atTargetsReached"), "AsBool"), "unfinished executable gate reports false");
        }
        levels[3] = targetE; levels[4] = targetM - 1; ScheduledRefresh(run);
        Equal(0, Get(run, "ReplanCount"), "one remaining magic AT level still defers GPS replan");
        levels[4] = targetM; ScheduledRefresh(run);
        Equal(1, Get(run, "ReplanCount"), "original gate completion admits one fresh planner call");
        Equal(true, Get(Json(CallMain("GetCombatPrepRuntime"), "atTargetsReached"), "AsBool"), "completed gate remains satisfied after continuation replan");
        Equal(deadline, Get(run, "DeadlineRebirthSeconds"), "deferred replan cannot renew deadline");
        CallMain("EndCombatPrep", "cancelled", "review regression cleanup", true);
    }

    private static void CheckAutomaticEquipmentWait()
    {
        foreach (var dispatch in new[] { "QuickStuff", "RunCustomPlanAutomation" })
        {
            var plan = ScheduledSetup();
            Setting("ManualAdventureEnabled", true); Set(MainInstance, "_manualAdventureSetupActive", true);
            var original = CallMain("CombatPrepEquipped"); var support = CallMain("CombatPrepSupportKey");
            var recalls = Recalls; var swaps = Swaps;
            if (dispatch == "QuickStuff") CallMain(dispatch); else CallMain(dispatch, plan);
            var scheduler = Get(MainInstance, "_protectedScheduler"); var entry = Get(scheduler, "Entry");
            var deadline = Get(scheduler, "ExpectedEndSeconds");
            Equal(null, Get(MainInstance, "_combatPrepRun"), "manual equipment owner blocks acquisition without a failed run: " + dispatch);
            Equal(recalls, Recalls, "manual owner prevents resource recall: " + dispatch);
            Equal(swaps, Swaps, "manual owner prevents gear swaps: " + dispatch);
            Equal(support, CallMain("CombatPrepSupportKey"), "manual owner prevents support mutation: " + dispatch);
            ScheduledTime(550);
            if (dispatch == "QuickStuff") CallMain(dispatch); else CallMain(dispatch, plan);
            Equal(entry, Get(scheduler, "Entry"), "blocked entry is not failed or consumed");
            Equal(false, ((bool[])Get(scheduler, "Completed"))[0], "blocked owner leaves stage pending");
            Equal(50d, Get(scheduler, "StageElapsedSeconds"), "waiting consumes original elapsed window");
            Set(MainInstance, "_manualAdventureSetupActive", false);
            var run = StartScheduled(plan);
            Equal(deadline, Get(run, "DeadlineRebirthSeconds"), "owner release does not grant a fresh budget");
            CallMain("EndCombatPrep", "cancelled", "review regression cleanup", true);
            Equal(true, CallMain("CombatPrepGearMatches", original), "entry equipment still restores after delayed acquisition");
        }
    }

    private static void CheckAutomaticCleanupRetry()
    {
        foreach (var successor in new[] { "", "," + StandardRow })
        {
            var plan = ScheduledSetup(ScheduledRow + successor);
            Set(Get(Get(Character, "buttons"), "augmentation"), "m_Interactable", false);
            Set(((IList)Get(Get(Character, "wishes"), "wishes"))[190], "level", 1L);
            var inventory = Get(Character, "inventory"); var originalHead = Get(inventory, "head");
            var head = Blank(originalHead.GetType()); Set(head, "id", 60); SetEnum(head, "type", "Head");
            Set(head, "bossRequired", 1); Set(head, "removable", true); SetEnum(head, "spec1Type", "Wandoos98"); Set(head, "spec1Cur", 1e20f);
            ((IList)Get(inventory, "inventory"))[0] = head;
            var original = CallMain("CombatPrepEquipped"); var support = CallMain("CombatPrepSupportKey");
            var run = StartScheduled(plan); var scheduler = Get(MainInstance, "_protectedScheduler");
            Equal(false, CallMain("CombatPrepGearMatches", original), "positive control: automatic run actually equipped phase gear");
            var entry = Get(scheduler, "Entry");
            ScheduledTime(1101); FailDesiredEnergyPreflight = true; CallMain("TickCombatPrep");
            Equal(1, DesiredEnergyPreflightFailures, "positive control reaches the real desired-map preflight rejection");
            Equal(false, FailDesiredEnergyPreflight, "the desired resource fault was consumed, not an unused legacy recall flag");
            if (successor.Length != 0)
            {
                Equal(false, Get(run, "CleanupPending"), "old combat release cannot hold a current successor's resource failure");
                Equal(1, Get(scheduler, "StageIndex"), "current successor is selected under the original elapsed boundary");
                var current = Get(Plugin.GetType("NGUInjector.Managers.CustomStageAllocator", true), "LastStageResourceOutcome");
                Equal("RejectedUnchanged", Get(current, "Disposition").ToString(), "fault belongs to actual current successor preflight");
                Equal(0, Get(current, "ModelWrites"), "rejected current requirements perform no accounting writes");
                var beforeRetry = ResourceCommittedWrites;
                CallMain("ReconcileCustomResources", plan, true);
                Equal(true, ResourceCommittedWrites > beforeRetry, "current successor retries direct differences after its own rejected preflight");
                Equal(0d, ((double[])Get(scheduler, "EarnedSeconds"))[1], "rejected successor did not manufacture fixed credit");
                continue;
            }
            var release = Get(run, "ResourceOutcome");
            Equal("RejectedUnchanged", Get(release, "Disposition").ToString(), "transient observation failure is known unchanged, not unknown model mutation");
            Equal(0, Get(release, "ModelWrites"), "failed preflight never credits or debits a native resource destination");
            Equal(true, Get(run, "CleanupPending"), "one unchanged resource rejection retains cleanup ownership");
            Equal(true, Get(MainInstance, "CombatPrepActive"), "cleanup pending keeps normal allocation overlaid");
            Equal(false, CallMain("CombatPrepGearMatches", original), "release failure does not restore historical gear");
            Equal(support, CallMain("CombatPrepSupportKey"), "release failure preserves actual support without rollback");
            Equal(false, ((bool[])Get(scheduler, "Completed"))[0], "cleanup exception cannot complete the automatic entry");
            CallMain("GetProtectedStage", plan); CallMain("RunCustomPlanAutomation", plan);
            Equal(entry, Get(scheduler, "Entry"), "expired deadline cannot advance scheduler while cleanup is pending");
            Equal(true, Get(Json(CallMain("GetCombatPrepRuntime"), "cleanupPending"), "AsBool"), "cleanup blocker is explicit in runtime JSON");
            Set(run, "CleanupLastAttempt", (double)Get(MainType, "CombatPrepClock") - .51);
            CallMain("TickCombatPrep");
            Equal(false, Get(run, "CleanupPending"), "next due cleanup tick reobserves and finishes the rejected resource release");
            Equal(false, Get(MainInstance, "CombatPrepActive"), "overlay releases only after safe cleanup");
            Equal(false, CallMain("CombatPrepGearMatches", original), "release retry has no historical gear detour");
            CallMain("RunCustomPlanAutomation", plan);
            Equal(successor.Length == 0 ? 0 : 1, Get(scheduler, "StageIndex"), "terminal or no-gear successor selected after cleanup only");
            Equal(true, ((bool[])Get(scheduler, "Completed"))[0], "automatic entry completes after successful cleanup retry");
            Equal(true, ReferenceEquals(run, Get(MainInstance, "_combatPrepRun")), "cleanup retry never starts another automatic run");
            Equal(false, CallMain("CombatPrepGearMatches", original), "uncontrolled successor/terminal gear preserves actual phase setup without an explicit baseline");
        }
    }

    private static void CheckAutomaticAllocatedBudgetLimit()
    {
        var plan = ScheduledSetup("{\"Percent\":1},{\"ExecutionMode\":1,\"Percent\":1},{\"Percent\":1}");
        Set(plan, "StageDurationSeconds", 200000);
        Invoke(Get(MainType, "CustomPlanStore"), "Save", "fixture", plan); // Each nominal share is below 24h.
        CallMain("GetProtectedStage", plan); var scheduler = Get(MainInstance, "_protectedScheduler");
        Invoke(scheduler, "OverrideStage", plan, "skip", 0, 500d, Get(MainType, "ProtectedHeartbeat"));
        Equal(true, ((double[])Get(scheduler, "Durations"))[1] > 86400, "actual redistribution can exceed a legal nominal share");
        var entry = Get(scheduler, "Entry"); var deadline = Get(scheduler, "ExpectedEndSeconds");
        var recalls = Recalls; var swaps = Swaps;
        CallMain("RunCustomPlanAutomation", plan);
        Equal(null, Get(MainInstance, "_combatPrepRun"), "unsupported effective allocation is validation-blocked before planner/acquisition");
        Equal(false, ((bool[])Get(scheduler, "Completed"))[1], "invalid effective budget is not consumed as a planner failure");
        Equal(entry, Get(scheduler, "Entry"), "budget validation preserves entry identity");
        Equal(deadline, Get(scheduler, "ExpectedEndSeconds"), "budget validation does not silently clip deadline");
        Equal(recalls, Recalls, "budget validation never recalls resources"); Equal(swaps, Swaps, "budget validation never swaps gear");
        Equal(true, ((string)CallMain("GetCustomAutomationStatus")).Contains("86400"), "runtime validation explains limit for UI");
    }

    private static void CheckPreviewResourceFocusFields()
    {
        var positive = false; var zero = false; var unknown = false;
        foreach (var zeroReference in new[] { false, true })
        {
            Setup();
            if (zeroReference)
            {
                var inventory = Get(Character, "inventory"); var head = Get(inventory, "head");
                SetEnum(head, "spec1Type", "EnergyCap"); Set(head, "spec1Cur", 90000f);
                var wHead = Blank(head.GetType()); Set(wHead, "id", 60); SetEnum(wHead, "type", "Head"); Set(wHead, "bossRequired", 1); Set(wHead, "removable", true);
                SetEnum(wHead, "spec1Type", "Wandoos98"); Set(wHead, "spec1Cur", 1e10f); ((IList)Get(inventory, "inventory"))[0] = wHead;
                Set(Character, "capEnergy", 100L); Set(Character, "curEnergy", 1000L); Set(Character, "idleEnergy", 900L);
                ((long[])Get(Get(Character, "training"), "_attackEnergy"))[0] = 100;
                RefreshBonuses();
            }
            var preview = Preview(10);
            var policies = (Array)Get(Get(Get(MainInstance, "_combatPrepPreview"), "Plan"), "Alternatives");
            foreach (var candidate in ((IEnumerable)Get(Json(preview, "candidates"), "Children")).Cast<object>())
            {
                var policy = policies.GetValue(int.Parse(JsonValue(candidate, "id").Substring("candidate-".Length)));
                Equal(Get(policy, "ResourceReferenceProfileKey"), JsonValue(candidate, "resourceReferenceProfileKey"), "wire reference key matches server planner");
                foreach (var lane in new[] { "Energy", "Magic" })
                {
                    var key = lane.ToLowerInvariant() + "ResourceFocusEquivalentSeconds";
                    var knownKey = lane.ToLowerInvariant() + "ResourceFocusCostKnown";
                    var seconds = (double)Get(policy, lane + "ResourceFocusEquivalentSeconds");
                    var known = (bool)Get(policy, lane + "ResourceFocusCostKnown");
                    Equal(true, Get(Json(candidate, key), "IsNumber"), "resource opportunity seconds remain numeric, including unknown");
                    Equal(seconds, Get(Json(candidate, key), "AsDouble"), "wire opportunity seconds match planner");
                    Equal(true, Get(Json(candidate, knownKey), "IsBoolean"), "resource known flag has boolean wire type");
                    Equal(known, Get(Json(candidate, knownKey), "AsBool"), "wire known flag matches planner");
                    positive |= known && seconds > 0; zero |= known && seconds == 0; unknown |= !known;
                }
            }
        }
        Equal(true, positive, "actual preview exercises positive resource opportunity cost");
        Equal(true, zero, "actual preview exercises known zero cost");
        Equal(true, unknown, "actual preview exercises zero reference pool with unknown resource cost");
    }

    private static void CheckFreeAtGateTelemetry()
    {
        var plan = ScheduledSetup();
        Set(((IList)Get(Get(Character, "wishes"), "wishes"))[190], "level", 1L);
        var run = StartScheduled(plan);
        Equal(false, ((Array)Get(Get(run, "Policy"), "Phases")).Cast<object>().Any(x => (string)Get(x, "Kind") == "AT"), "Wish190 has no executable paid AT gate");
        Equal(true, (long)Get(Get(run, "Policy"), "AtEnergyLevel") > ((long[])Get(Get(Character, "advancedTraining"), "level"))[3], "positive control: terminal forecast includes passive future AT");
        for (var i = 0; i < 3; i++)
        {
            if (i == 1) ScheduledRefresh(run);
            if (i == 2)
            { Set(Get(Character, "machine"), "realBaseGold", 1000000d); ScheduledRefresh(run); }
            var runtime = CallMain("GetCombatPrepRuntime");
            Equal(false, Get(Json(runtime, "atGateRequired"), "AsBool"), "free AT never invents a paid target gate");
            Equal(true, Get(Json(runtime, "atTargetsReached"), "AsBool"), "no AT gate is vacuously satisfied immediately, during execution, and after replan");
        }
        Equal(1, Get(run, "ReplanCount"), "free AT telemetry regression exercised a real GPS replan");
        CallMain("EndCombatPrep", "cancelled", "review regression cleanup", true);
    }
}
