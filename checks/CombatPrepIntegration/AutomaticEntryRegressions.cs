using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

public static partial class Program
{
    private static object EntryFightPlan(bool automatic = true)
    {
        var row = automatic ? ScheduledRow : StandardRow;
        var plan = ScheduledSetup(row.Substring(0, row.Length - 1) + ",\"FightOnEntry\":true}," + StandardRow);
        var inventory = Get(Character, "inventory");
        var head = Blank(Get(inventory, "head").GetType()); Set(head, "id", 70); SetEnum(head, "type", "Head");
        Set(head, "bossRequired", 1); Set(head, "removable", true);
        Set(head, "curAttack", 1000f); Set(head, "capAttack", 1000f); Set(head, "curDefense", 1000f); Set(head, "capDefense", 1000f);
        ((IList)Get(inventory, "inventory"))[0] = head;
        return plan;
    }

    private static void CheckAutomaticEntryRegressions()
    {
        CheckAutomaticEntryFightDispatch();
        CheckAutomaticEntryGuards();
        CheckAutomaticPreflightRetry();
        CheckAutomaticEntryReceiptRestore();
        CheckAutomaticEntryInvalidSetupCheckpoint();
        CheckAutomaticEntryReceiptFailure();
        CheckAutomaticEntryExpiry();
        CheckAutomaticEntryFruits();
        CheckStandardEntryFightUnchanged();
    }

    private static void CheckAutomaticEntryFightDispatch()
    {
        foreach (var dispatch in new[] { "QuickStuff", "RunCustomPlanAutomation" })
        foreach (var failSetup in new[] { false, true })
        {
            var plan = EntryFightPlan();
            var runtime = CallMain("GetProtectedStage", plan);
            var expectedKey = "fixture:" + CallMain("CustomStageRuntimeKey", plan, runtime);
            if (failSetup) FailRefreshAtSwap = Swaps + 1;
            if (dispatch == "QuickStuff") CallMain(dispatch); else CallMain(dispatch, plan);
            Equal(true, Get(MainInstance, "BossPushRunning"), "actual shared fight prep starts boss push before automatic capture: " + dispatch);
            Equal(null, Get(MainInstance, "_combatPrepRun"), "fight entry cannot capture or allocate CombatPrep in its preparation pass");
            Equal(expectedKey, Get(MainInstance, "_customStageFightKey"), "automatic fight receipt uses exact existing file/stage/entry key");
            if (failSetup) Equal(-1, FailRefreshAtSwap, "positive control: native fight gear setup fault was exercised; " + CallMain("GetCustomAutomationStatus"));
            var fightGear = CallMain("CombatPrepEquipped");
            var deadline = Get(Get(MainInstance, "_protectedScheduler"), "ExpectedEndSeconds");
            CallMain("RunCustomPlanAutomation", plan); CallMain("QuickStuff");
            Equal(null, Get(MainInstance, "_combatPrepRun"), "pending boss push blocks both automatic dispatch paths");
            CallMain("StopBossPush");
            Set(Get(Character, "bossController"), "isFighting", true);
            CallMain("RunCustomPlanAutomation", plan); CallMain("QuickStuff");
            Equal(null, Get(MainInstance, "_combatPrepRun"), "remaining native fight flags block capture after boss push stops");
            Set(Get(Character, "bossController"), "isFighting", false);
            Set(MainInstance, "_automaticStageSetupStarted", (double)Get(MainType, "CombatPrepClock") - 30);
            var run = StartScheduled(plan);
            Equal(false, Get(MainInstance, "BossPushRunning"), "fight prep is not repeated after its receipt, including failed gear setup");
            Equal(true, CallMain("CombatPrepGearMatches", Get(run, "ExpectedGear")), "automatic executor acquires only its own phase after entry prep");
            Equal(true, EqualMaps(fightGear, Get(Get(run, "Capture"), "OriginalGear")), "immutable original capture is taken after fight entry setup");
            Equal(deadline, Get(run, "DeadlineRebirthSeconds"), "fight entry cannot reset original stage deadline");
            Equal(true, (double)Get(MainType, "CombatPrepClock") - (double)Get(run, "Started") >= 30, "entry fight and failed setup time consume the total elapsed budget");
            CallMain("EndCombatPrep", "cancelled", "entry regression cleanup", true);
            if (dispatch == "QuickStuff" && !failSetup)
            {
                var args = Parse("{\"name\":\"fixture\",\"expectedRevision\":\"" + Get(plan, "RuntimeRevision")
                    + "\",\"expectedEntry\":\"" + Get(run, "OwnerEntry") + "\",\"action\":\"restart\",\"stageIndex\":0}");
                CallMain("OverrideCustomStage", args); CallMain("RunCustomPlanAutomation", plan);
                Equal(true, Get(MainInstance, "BossPushRunning"), "explicit new stage entry earns exactly one new entry fight");
                Equal(false, Equals(expectedKey, Get(MainInstance, "_customStageFightKey")), "new entry uses a distinct fight receipt");
                CallMain("StopBossPush");
            }
        }
    }

    private static bool EqualMaps(object first, object second)
    {
        var a = (Array)first; var b = (Array)second;
        if (a.Length != b.Length) return false;
        for (var i = 0; i < a.Length; i++)
            if (!Equals(Get(a.GetValue(i), "slot"), Get(b.GetValue(i), "slot"))
                || !ReferenceEquals(Get(a.GetValue(i), "equipment"), Get(b.GetValue(i), "equipment"))) return false;
        return true;
    }

    private static void CheckAutomaticEntryGuards()
    {
        foreach (var guard in new[] { "manual", "loadout", "digger" })
        {
            var plan = EntryFightPlan();
            if (guard == "manual") { Setting("ManualAdventureEnabled", true); Set(MainInstance, "_manualAdventureSetupActive", true); }
            else
            {
                BeginFixtureClaim(guard == "loadout" ? "Gear" : "Support", "automatic-entry-" + guard);
            }
            var recalls = Recalls; var swaps = Swaps;
            CallMain("QuickStuff"); CallMain("RunCustomPlanAutomation", plan);
            Equal(false, Get(MainInstance, "BossPushRunning"), "entry fight respects existing owner: " + guard);
            Equal(null, Get(MainInstance, "_customStageFightKey"), "blocked entry does not consume fight receipt: " + guard);
            Equal(null, Get(MainInstance, "_combatPrepRun"), "blocked fight cannot capture automatic run: " + guard);
            Equal(recalls, Recalls, "blocked entry never recalls resources: " + guard); Equal(swaps, Swaps, "blocked entry never swaps gear: " + guard);
        }
        var unmanagedPlan = EntryFightPlan(); Setting("ManageMagic", false);
        var before = Recalls; CallMain("RunCustomPlanAutomation", unmanagedPlan);
        Equal("failed", Get(Get(MainInstance, "_combatPrepRun"), "Status"), "managed-lane preflight still rejects unavailable automatic execution");
        Equal(false, Get(MainInstance, "BossPushRunning"), "FightOnEntry cannot bypass unmanaged-lane preflight");
        Equal(null, Get(MainInstance, "_customStageFightKey"), "unmanaged preflight does not consume fight receipt");
        Equal(before, Recalls, "entry fight never recalls an unmanaged lane");
    }

    private static void CheckAutomaticEntryReceiptRestore()
    {
        var plan = EntryFightPlan(); CallMain("RunCustomPlanAutomation", plan); CallMain("StopBossPush");
        var entry = Get(Get(MainInstance, "_protectedScheduler"), "Entry");
        var deadline = Get(Get(MainInstance, "_protectedScheduler"), "ExpectedEndSeconds");
        var fight = Get(MainInstance, "_customStageFightKey");
        Set(MainInstance, "_automaticStageSetupStarted", (double)Get(MainType, "CombatPrepClock") - 40);
        CallMain("SaveProtectedStageProgress", true);
        var saved = Parse(File.ReadAllText((string)Get(MainInstance, "ProtectedCheckpointPath")));
        Equal(fight, JsonValue(saved, "fightEntry"), "entry fight receipt is persisted before runtime capture");
        Equal(true, (double)Get(Json(Json(saved, "automaticSetup"), "elapsedSeconds"), "AsDouble") >= 40, "synchronous setup time is checkpointed independently of stalled game clock");
        MainInstance = Blank(MainType); Set(MainInstance, "_combatPrepEndedRuns", new HashSet<string>()); Set(MainInstance, "_customIronPillPrepStartedAt", -1d);
        Set(MainType, "reference", MainInstance);
        var run = StartScheduled(plan);
        Equal(false, Get(MainInstance, "BossPushRunning"), "fresh coordinator restores entry receipt rather than repeating fight prep");
        Equal(entry, Get(Get(MainInstance, "_protectedScheduler"), "Entry"), "entry identity survives setup checkpoint restore");
        Equal(deadline, Get(run, "DeadlineRebirthSeconds"), "setup checkpoint restore never renews original deadline");
        Equal(true, (double)Get(MainType, "CombatPrepClock") - (double)Get(run, "Started") >= 40, "setup checkpoint restore never grants a fresh full budget");
        CallMain("EndCombatPrep", "cancelled", "entry regression cleanup", true);
    }

    private static void CheckAutomaticPreflightRetry()
    {
        var plan = ScheduledSetup(); CallMain("GetProtectedStage", plan);
        var scheduler = Get(MainInstance, "_protectedScheduler"); var entry = Get(scheduler, "Entry");
        var deadline = Get(scheduler, "ExpectedEndSeconds"); var entered = Get(scheduler, "EnteredAt");
        Setting("ManageMagic", false);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            Set(MainInstance, "_automaticAcquisitionRetryAt", 0d); CallMain("RunCustomPlanAutomation", plan);
            Equal(0, Get(scheduler, "StageIndex"), "generic preflight rejection retries the same automatic row");
            Equal(false, ((bool[])Get(scheduler, "Completed"))[0], "preflight rejection is never stage completion");
            Equal(entry, Get(scheduler, "Entry"), "preflight retry retains exact acquired entry identity");
            Equal(deadline, Get(scheduler, "ExpectedEndSeconds"), "preflight retry never renews the original deadline");
            Equal(entered, Get(scheduler, "EnteredAt"), "preflight retry never restarts elapsed-budget accounting");
        }
        Setting("ManageMagic", true); Set(MainInstance, "_automaticAcquisitionRetryAt", 0d);
        var run = StartScheduled(plan);
        Equal(deadline, Get(run, "DeadlineRebirthSeconds"), "resolved preflight starts under the original deadline");
        Equal(entry, Get(scheduler, "Entry"), "resolved preflight does not manufacture a new stage entry");
        CallMain("EndCombatPrep", "cancelled", "preflight retry cleanup", true);
    }

    private static void CheckAutomaticEntryReceiptFailure()
    {
        var plan = EntryFightPlan(); CallMain("GetProtectedStage", plan);
        var path = (string)Get(MainInstance, "ProtectedCheckpointPath");
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            CallMain("RunCustomPlanAutomation", plan);
            Equal(false, Get(MainInstance, "BossPushRunning"), "automatic fight requires a durable once-per-entry receipt");
            Equal(null, Get(MainInstance, "_customStageFightKey"), "failed receipt save does not silently consume FightOnEntry");
            Equal(null, Get(MainInstance, "_combatPrepRun"), "failed receipt save cannot start fresh planning");
        }
        CallMain("RunCustomPlanAutomation", plan);
        Equal(true, Get(MainInstance, "BossPushRunning"), "entry fight remains eligible after transient checkpoint failure");
        CallMain("StopBossPush");
    }

    private static void CheckAutomaticEntryInvalidSetupCheckpoint()
    {
        var plan = EntryFightPlan(); CallMain("RunCustomPlanAutomation", plan); CallMain("StopBossPush");
        var path = (string)Get(MainInstance, "ProtectedCheckpointPath");
        var saved = Parse(File.ReadAllText(path)); Set(Json(Json(saved, "automaticSetup"), "entry"), "Value", "wrong-entry");
        File.WriteAllText(path, saved.ToString());
        MainInstance = Blank(MainType); Set(MainInstance, "_combatPrepEndedRuns", new HashSet<string>()); Set(MainInstance, "_customIronPillPrepStartedAt", -1d);
        Set(MainType, "reference", MainInstance);
        CallMain("GetProtectedStage", plan); CallMain("RunCustomPlanAutomation", plan);
        Equal(false, Get(MainInstance, "CombatPrepCleanupRecoveryRequired"), "invalid setup metadata is distinct from historical restoration ownership");
        var run = Get(MainInstance, "_combatPrepRun");
        Equal("NGU", Get(ScheduledPhase(run), "Kind"), "invalid setup uses conservative safe background allocation without fresh paid preparation");
        Equal(1100d, Get(run, "DeadlineRebirthSeconds"), "invalid setup never grants a fresh elapsed deadline");
        Equal(false, Get(Get(MainInstance, "_protectedScheduler"), "AutoAtCompleted"), "invalid setup cannot claim unknown paid AT gate completed");
        Equal(false, Get(MainInstance, "BossPushRunning"), "invalid setup metadata cannot repeat a checkpointed entry fight");
    }

    private static void CheckAutomaticEntryExpiry()
    {
        foreach (var clock in new[] { "rebirth", "wall" })
        {
            var plan = EntryFightPlan(); CallMain("RunCustomPlanAutomation", plan);
            if (clock == "rebirth") ScheduledTime(1200);
            else Set(MainInstance, "_automaticStageSetupStarted", (double)Get(MainType, "CombatPrepClock") - 601);
            CallMain("RunCustomPlanAutomation", plan);
            Equal(null, Get(MainInstance, "_combatPrepRun"), "no acquisition while entry fight remains pending");
            CallMain("StopBossPush");
            CallMain("RunCustomPlanAutomation", plan); CallMain("RunCustomPlanAutomation", plan);
            Equal(null, Get(MainInstance, "_combatPrepRun"), "entry setup expiry cannot create a new full-budget run: " + clock);
            Equal(1, Get(Get(MainInstance, "_protectedScheduler"), "StageIndex"), "expired preparation hands off without restarting automatic entry: " + clock);
            Equal(true, ((bool[])Get(Get(MainInstance, "_protectedScheduler"), "Completed"))[0], "entry fight time consumes the automatic stage budget");
        }
    }

    private static void CheckAutomaticEntryFruits()
    {
        foreach (var eligible in new[] { "enabled", "disabled", "short-plan" })
        {
            var plan = ScheduledSetup(); Set(plan, "AutoActivateFruits", eligible != "disabled");
            Set(plan, "StageDurationSeconds", eligible == "short-plan" ? 1200 : 3600);
            Invoke(Get(MainType, "CustomPlanStore"), "Save", "fixture", plan);
            Set(Child(Get(Character, "buttons"), "yggdrasil"), "m_Interactable", true);
            var fruitModels = ListField(Get(Character, "yggdrasil"), "fruits", 2);
            foreach (var fruit in fruitModels) Set(fruit, "maxTier", 1L);
            var controller = Get(Character, "yggdrasilController");
            Set(controller, "usesEnergy", new List<bool> { true, false }); Set(controller, "activationCost", new List<long> { 1000, 2000 });
            var controls = ArrayField(controller, "fruits", 2);
            for (var i = 0; i < controls.Length; i++) { var fruit = controls.GetValue(i); Set(fruit, "character", Character); Set(fruit, "id", i); Child(fruit, "tooltip"); }
            var support = CallMain("CombatPrepSupportKey"); var gear = CallMain("CombatPrepEquipped");
            var run = StartScheduled(plan);
            Equal(eligible == "enabled", Get(fruitModels[0], "activated"), "automatic entry preserves native affordable energy-fruit activation intent: " + eligible);
            Equal(eligible == "enabled", Get(fruitModels[1], "activated"), "automatic entry preserves native affordable magic-fruit activation intent: " + eligible);
            Equal(eligible == "enabled" ? 9000L : 10000L, Get(run, "EnergyPool"), "fresh planner captures energy after native fruit costs");
            Equal(eligible == "enabled" ? 18000L : 20000L, Get(run, "MagicPool"), "fresh planner captures magic after native fruit costs");
            Equal(support, Get(Get(Get(run, "Capture"), "OriginalSupportState"), "StableKey"), "fruit activation never installs ordinary stage support before capture");
            Equal(true, EqualMaps(gear, Get(Get(run, "Capture"), "OriginalGear")), "fruit activation never installs ordinary stage gear before capture");
            CallMain("EndCombatPrep", "cancelled", "entry regression cleanup", true);
        }
    }

    private static void CheckStandardEntryFightUnchanged()
    {
        var plan = EntryFightPlan(false); CallMain("RunCustomPlanAutomation", plan);
        Equal(true, Get(MainInstance, "BossPushRunning"), "standard stage still runs original entry fight");
        var receipt = Get(MainInstance, "_customStageFightKey"); CallMain("StopBossPush");
        CallMain("RunCustomPlanAutomation", plan);
        Equal(false, Get(MainInstance, "BossPushRunning"), "standard stage still attempts fight only once per entry");
        Equal(receipt, Get(MainInstance, "_customStageFightKey"), "standard receipt format and lifetime are unchanged");
        Equal(null, Get(MainInstance, "_combatPrepRun"), "standard stage remains ordinary allocation, not automatic mode");
    }
}
