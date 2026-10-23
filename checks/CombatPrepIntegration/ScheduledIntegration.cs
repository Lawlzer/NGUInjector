using System;
using System.Collections;
using System.IO;
using System.Linq;

public static partial class Program
{
    private const string ScheduledRow = "{\"Name\":\"Augments + Wandoos\",\"ExecutionMode\":1,\"DurationSeconds\":600,\"GearGoal\":13,\"EnergyFocus\":1,\"MagicFocus\":1}";
    private const string StandardRow = "{\"Name\":\"Standard\",\"DurationSeconds\":10,\"GearGoal\":-1,\"EnergyFocus\":5,\"MagicFocus\":6}";

    private static object ScheduledSetup(string rows = ScheduledRow)
    {
        Setup();
        Setting("UseCustomRebirthPlan", true);
        Set(Get(Get(Character, "buttons"), "ngu"), "m_Interactable", true);
        var path = Path.Combine(Temp, "plans", "fixture.json");
        File.WriteAllText(path, "{\"Version\":17,\"Name\":\"fixture\",\"EnergyNguWeights\":[0,0,0,0,1,0,1,0,0],\"MagicNguWeights\":[1,0,0,0,0,0,0],\"AutoRebirthEnabled\":false,\"AutoIronPillEnabled\":false,\"NumberClimbEnabled\":false,\"TimeTriggerEnabled\":false,\"StageSchedulerEnabled\":true,\"StageDurationSeconds\":1200,\"StageLoopStartIndex\":-1,\"AutoActivateFruits\":false,\"Stages\":[" + rows + "]}");
        return Invoke(Get(MainType, "CustomPlanStore"), "Load", "fixture");
    }

    private static void ScheduledTime(double elapsed)
    {
        Set(Get(Character, "rebirthTime"), "totalseconds", elapsed);
        Set(Get(Character, "totalPlaytime"), "totalseconds", elapsed + 1000);
    }

    private static object StartScheduled(object plan)
    {
        CallMain("RunCustomPlanAutomation", plan);
        var run = Get(MainInstance, "_combatPrepRun");
        Equal("running", Get(run, "Status"), "scheduled stage starts through actual ordinary-stage dispatch: " + Get(run, "Reason"));
        Equal(true, Get(run, "Automatic"), "scheduled ownership uses shared executor");
        Equal("stage-start", Get(run, "ReplanReason"), "fresh automatic Plan is explicit");
        Equal(null, Get(MainInstance, "_combatPrepPreview"), "automatic execution consumes no manual preview token");
        return run;
    }

    private static object ScheduledPhase(object run)
    {
        return ((Array)Get(Get(run, "Policy"), "Phases")).GetValue((int)Get(run, "PhaseIndex"));
    }

    private static void ScheduledRefresh(object run)
    {
        Set(run, "LastRefresh", -1d); CallMain("TickCombatPrep");
    }

    private static void CheckScheduledCombatPrep()
    {
        CheckScheduledTransitions();
        CheckScheduledReplans();
        CheckScheduledOwnership();
        CheckScheduledRestore();
        CheckScheduledFailures();
        CheckScheduledLoops();
    }

    private static void CheckScheduledTransitions()
    {
        var plan = ScheduledSetup(StandardRow + "," + ScheduledRow + "," + StandardRow);
        var runtime = CallMain("GetProtectedStage", plan);
        var scheduler = Get(MainInstance, "_protectedScheduler");
        Equal(0, Get(scheduler, "StageIndex"), "standard opener selected");
        CallMain("RunCustomPlanAutomation", plan);
        Equal(true, Get(scheduler, "_armed"), "standard opener has actual applied resource ownership before timing credit");
        // A clock boundary is controlled; production application, planner and executor are not substituted.
        Set(scheduler, "_lastHeartbeat", (double)Get(MainType, "ProtectedHeartbeat") - 10);
        ScheduledTime(510);
        runtime = CallMain("GetProtectedStage", plan);
        Equal(1, Get(runtime, "StageIndex"), "protected opener transitions into combined stage");
        ScheduledTime(530); // Delayed start does not create a fresh 600-second window.
        var originalGear = CallMain("CombatPrepEquipped"); var support = CallMain("CombatPrepSupportKey");
        var run = StartScheduled(plan);
        Equal(1110d, Get(run, "DeadlineRebirthSeconds"), "combined budget starts at scheduler entry, not executor start");
        Equal(true, (double)Get(run, "Duration") - ((double)Get(MainType, "CombatPrepClock") - (double)Get(run, "Started")) <= 580,
            "planning/setup and start delay consume total elapsed budget");
        Equal(10d, ((double[])Get(scheduler, "EarnedSeconds"))[0], "standard credit survives automatic entry");
        Equal(0d, ((double[])Get(scheduler, "EarnedSeconds"))[1], "automatic stage earns no fake protected credit");
        var entry = Get(run, "OwnerEntry");
        ScheduledTime(1111);
        Equal(1, Get(CallMain("GetProtectedStage", plan), "StageIndex"), "scheduler cannot advance active executor before cleanup");
        CallMain("TickCombatPrep");
        Equal(false, Get(MainInstance, "CombatPrepActive"), "original deadline terminates executor");
        Equal(true, CallMain("CombatPrepGearMatches", originalGear), "automatic owner restores entry gear before successor");
        Equal(support, CallMain("CombatPrepSupportKey"), "automatic owner restores entry support before successor");
        Equal(0L, ((long[])Get(Get(Character, "advancedTraining"), "energy"))[3], "current standard successor does not retain paid combat AT");
        Equal(0L, ((long[])Get(Get(Character, "advancedTraining"), "energy"))[4], "current standard successor independently reconciles the second AT lane");
        Equal(true, (long)Get(Get(Character, "wandoos98"), "wandoosEnergy") > 0, "same coordinator pass actually funds current successor W intent rather than stopping after release");
        runtime = CallMain("GetProtectedStage", plan);
        Equal(2, Get(runtime, "StageIndex"), "standard successor selected only after cleanup");
        Equal(0d, ((double[])Get(scheduler, "EarnedSeconds"))[2], "standard successor still requires actual applied time");
        Equal(false, Equals(entry, Get(runtime, "EntryIdentity")), "successor has a distinct entry");
        CallMain("RunCustomPlanAutomation", plan);
        Equal(false, Get(MainInstance, "CombatPrepActive"), "standard successor uses ordinary allocation, not automatic restart");

        plan = ScheduledSetup(); run = StartScheduled(plan); scheduler = Get(MainInstance, "_protectedScheduler");
        ScheduledTime(1101); CallMain("TickCombatPrep"); CallMain("RunCustomPlanAutomation", plan);
        var ended = Get(MainInstance, "_combatPrepRun");
        CallMain("RunCustomPlanAutomation", plan);
        Equal(true, ReferenceEquals(ended, Get(MainInstance, "_combatPrepRun")), "terminal combined last row never restarts endlessly");
        Equal(true, Get(scheduler, "Finished"), "terminal combined row marks schedule complete");
    }

    private static void CheckScheduledReplans()
    {
        var plan = ScheduledSetup();
        // Make paid AT beneficial and exclude augment policies for this deterministic AT gate case.
        Set(Get(Get(Character, "buttons"), "augmentation"), "m_Interactable", false);
        foreach (var name in new[] { "wandoosEnergy", "wandoosMagic" })
            Set(Get(Get(Character, "advancedTrainingController"), name), "levelFactor", 1000f);
        var run = StartScheduled(plan);
        var phase = ScheduledPhase(run);
        Equal("AT", Get(phase, "Kind"), "fixture recommendation starts with real paid AT");
        var original = Get(run, "Capture"); var start = Get(run, "Started"); var deadline = Get(run, "DeadlineRebirthSeconds");
        var w = Get(Character, "wandoos98");
        Equal(0L, Get(w, "wandoosEnergy"), "no W energy before AT gate");
        Equal(0L, Get(w, "wandoosMagic"), "no W magic before AT gate");
        Set(run, "PhaseDeadline", (double)Get(MainType, "CombatPrepClock") - 1);
        ScheduledRefresh(run);
        Equal(0, Get(run, "ReplanCount"), "scheduled boundary waits for actual target completion");
        var levels = (long[])Get(Get(Character, "advancedTraining"), "level");
        levels[3] = (long)Get(phase, "AtEnergyTarget"); levels[4] = (long)Get(phase, "AtMagicTarget");
        ScheduledRefresh(run);
        Equal("running", Get(run, "Status"), "actual AT completion replans instead of ending");
        Equal(1, Get(run, "ReplanCount"), "actual phase boundary causes exactly one fresh Plan");
        Equal("phase-completed:AT", Get(run, "ReplanReason"), "boundary reason exposed");
        Equal(false, ((Array)Get(Get(run, "Policy"), "Phases")).Cast<object>().Any(x => (string)Get(x, "Kind") == "AT"), "completed AT never reintroduced");
        Equal(true, ReferenceEquals(original, Get(run, "Capture")), "original restoration capture is immutable across replans");
        Equal(false, ReferenceEquals(original, Get(run, "Forecast")), "fresh forecast is separate from original capture");
        Equal(start, Get(run, "Started"), "phase replan preserves original start"); Equal(deadline, Get(run, "DeadlineRebirthSeconds"), "phase replan preserves original deadline");
        var energyPool = (long)Get(run, "EnergyPool"); var magicPool = (long)Get(run, "MagicPool"); var os = Get(w, "os");
        // Capture uses native current GPS; alter the native TM model, not a future income estimate.
        var machine = Get(Character, "machine");
        var baseGold = (double)Get(machine, "realBaseGold");
        Set(machine, "realBaseGold", baseGold * 1.06);
        ScheduledRefresh(run);
        Equal(1, Get(run, "ReplanCount"), "small GPS fluctuation does not continuously replan");
        Set(machine, "realBaseGold", baseGold * 1.12);
        ScheduledRefresh(run);
        Equal(2, Get(run, "ReplanCount"), "cumulative material GPS drift is measured from the planned phase, not the previous refresh");
        Set(machine, "realBaseGold", (double)Get(machine, "realBaseGold") * 2 + 100);
        ScheduledRefresh(run);
        Equal(3, Get(run, "ReplanCount"), "material native GPS shift causes fresh Plan");
        Equal("material-gps-change", Get(run, "ReplanReason"), "GPS reason exposed");
        Equal(os, Get(w, "os"), "replanning preserves selected OS");
        Equal(energyPool, Get(run, "EnergyPool"), "GPS-only replan observes unchanged actual energy");
        Equal(magicPool, Get(run, "MagicPool"), "GPS-only replan observes unchanged actual magic");
        Equal(false, ((Array)Get(Get(run, "Policy"), "Phases")).Cast<object>().Any(x => (string)Get(x, "Kind") == "AT"), "no paid AT after W started");
        var state = CallMain("GetCombatPrepRuntime");
        Equal("scheduled", JsonValue(state, "owner"), "runtime owner field");
        Equal("fixture", JsonValue(state, "planName"), "runtime plan field");
        Equal(Get(run, "OwnerEntry"), JsonValue(state, "stageEntry"), "runtime entry field");
        Equal(3, Get(Json(state, "replanCount"), "AsInt"), "runtime replan counter");
        Equal(deadline, Get(Json(state, "deadlineRebirthSeconds"), "AsDouble"), "runtime original deadline field");
        CallMain("EndCombatPrep", "cancelled", "test cleanup", true);

        plan = ScheduledSetup();
        Set(Get(Get(Character, "buttons"), "advancedTraining"), "m_Interactable", false);
        var augController = ((Array)Get(Get(Character, "augmentsController"), "augments")).GetValue(0);
        Set(augController, "baseBoost", 100000000L);
        var head = Get(Get(Character, "inventory"), "head");
        SetEnum(head, "spec1Type", "Wandoos98"); Set(head, "spec1Cur", 1000000f); RefreshBonuses();
        run = StartScheduled(plan); phase = ScheduledPhase(run);
        Equal("Augments", Get(phase, "Kind"), "fixture recommendation exercises automatic augment completion");
        original = Get(run, "Capture");
        var augment = ((Array)Get(Get(Character, "augments"), "augs")).GetValue((int)Get(phase, "AugmentTierId"));
        Set(run, "PhaseDeadline", (double)Get(MainType, "CombatPrepClock") - 1);
        ScheduledRefresh(run); Equal(0, Get(run, "ReplanCount"), "automatic augment boundary waits for both actual targets");
        Set(augment, "augLevel", Get(phase, "AugmentTarget")); Set(augment, "upgradeLevel", Get(phase, "UpgradeTarget"));
        ScheduledRefresh(run);
        Equal("phase-completed:Augments", Get(run, "ReplanReason"), "actual augment completion triggers fresh planning");
        Equal("running", Get(run, "Status"), "augment continuation succeeds: " + Get(run, "Reason"));
        Equal(false, ((Array)Get(Get(run, "Policy"), "Phases")).Cast<object>().Any(x => (string)Get(x, "Kind") == "Augments"), "completed augment phase cannot restart on replan");
        Equal(true, ReferenceEquals(original, Get(run, "Capture")), "augment continuation retains original restoration capture");
        CallMain("EndCombatPrep", "cancelled", "test cleanup", true);
    }

    private static void CheckScheduledOwnership()
    {
        foreach (var edit in new[] { "pause", "switch", "disable", "entry", "target" })
        {
            var plan = ScheduledSetup(); var gear = CallMain("CombatPrepEquipped"); var run = StartScheduled(plan);
            if (edit == "pause") Setting("UseCustomRebirthPlan", false);
            if (edit == "switch") Setting("CustomPlanFile", "other");
            if (edit == "disable")
            {
                var json = File.ReadAllText(Path.Combine(Temp, "plans", "fixture.json"));
                File.WriteAllText(Path.Combine(Temp, "plans", "fixture.json"), json.Replace("\"StageSchedulerEnabled\":true", "\"StageSchedulerEnabled\":false"));
            }
            if (edit == "entry") Set(Get(MainInstance, "_protectedScheduler"), "Entry", 1234L);
            if (edit == "target")
            {
                var targets = (IDictionary)Get(run, "ExpectedTargets");
                if (targets.Count == 0) throw new Exception("target fixture requires a planned prep phase");
                var index = targets.Keys.Cast<int>().First(); var phase = ((Array)Get(Get(run, "Policy"), "Phases")).GetValue(index);
                if ((string)Get(phase, "Kind") == "AT") ((long[])Get(Get(Character, "advancedTraining"), "levelTarget"))[3] = 456;
                else Set(((Array)Get(Get(Character, "augments"), "augs")).GetValue((int)Get(phase, "AugmentTierId")), "augmentTarget", 456L);
            }
            var swaps = Swaps; ScheduledRefresh(run);
            Equal("cancelled", Get(run, "Status"), "stale scheduled owner/target cancels before funding: " + edit);
            Equal(0L, Get(Get(Character, "wandoos98"), "wandoosEnergy"), "owner loss leaves W unfunded: " + edit);
            Equal(true, CallMain("CombatPrepGearMatches", gear), "owner loss cleanup restores owned gear: " + edit);
            if (edit == "pause" || edit == "switch" || edit == "disable")
            {
                if (edit == "switch")
                {
                    File.WriteAllText(Path.Combine(Temp, "plans", "other.json"), File.ReadAllText(Path.Combine(Temp, "plans", "fixture.json")).Replace("\"Name\":\"fixture\"", "\"Name\":\"other\""));
                    var other = Invoke(Get(MainType, "CustomPlanStore"), "Load", "other");
                    CallMain("GetProtectedStage", other);
                    Setting("CustomPlanFile", "fixture");
                }
                if (edit == "disable")
                {
                    var path = Path.Combine(Temp, "plans", "fixture.json");
                    File.WriteAllText(path, File.ReadAllText(path).Replace("\"StageSchedulerEnabled\":false", "\"StageSchedulerEnabled\":true"));
                }
                Setting("UseCustomRebirthPlan", true); ScheduledTime(550);
                var resumed = StartScheduled(plan);
                Equal(Get(run, "DeadlineRebirthSeconds"), Get(resumed, "DeadlineRebirthSeconds"), "owner resume never grants a new full budget: " + edit);
                Equal(Get(run, "OwnerEntry"), Get(resumed, "OwnerEntry"), "owner resume preserves original entry identity: " + edit);
                CallMain("EndCombatPrep", "cancelled", "test cleanup", true);
            }
        }
    }

    private static void CheckScheduledRestore()
    {
        var plan = ScheduledSetup(); var run = StartScheduled(plan);
        var deadline = Get(run, "DeadlineRebirthSeconds"); var entry = Get(run, "OwnerEntry");
        var scheduler = Get(MainInstance, "_protectedScheduler");
        Set(scheduler, "AutoAtCompleted", true); Set(scheduler, "AutoWandoosStarted", true);
        Set(scheduler, "AutoEnergyPool", (long?)1234); Set(scheduler, "AutoMagicPool", (long?)2345);
        CallMain("EndCombatPrep", "cancelled", "Injector unloaded.", true);
        ScheduledTime(550);
        Set(MainInstance, "_protectedScheduler", null);
        CallMain("GetProtectedStage", plan);
        scheduler = Get(MainInstance, "_protectedScheduler");
        Equal(true, Get(scheduler, "AutoWandoosStarted"), "checkpoint restores no-AT-after-W history");
        var resumed = StartScheduled(plan);
        Equal(deadline, Get(resumed, "DeadlineRebirthSeconds"), "restart restores original deadline, not full new budget");
        Equal(entry, Get(resumed, "OwnerEntry"), "restart retains epoch/entry identity");
        Equal(10000L, Get(resumed, "EnergyPool"), "resume observes actual energy instead of stale checkpoint pool");
        Equal(20000L, Get(resumed, "MagicPool"), "resume observes actual magic instead of stale checkpoint pool");
        Equal(false, ((Array)Get(Get(resumed, "Policy"), "Phases")).Cast<object>().Any(x => (string)Get(x, "Kind") == "AT"), "restored continuation cannot buy AT");
        var store = Get(MainType, "CustomPlanStore");
        var edit = Invoke(store, "Load", "fixture"); Set(edit, "StageLoopStartIndex", 0);
        Reject(() => Invoke(store, "Save", "fixture", edit), "active structural edit cannot create a new deadline/entry");
        Equal(-1, Get(Invoke(store, "Load", "fixture"), "StageLoopStartIndex"), "rejected structural edit leaves saved plan unchanged");
        Equal(entry, Get(resumed, "OwnerEntry"), "rejected structure edit retains executor entry");
        // Live duration edit must not change the original deadline.
        var path = Path.Combine(Temp, "plans", "fixture.json"); File.WriteAllText(path, File.ReadAllText(path).Replace("\"DurationSeconds\":600", "\"DurationSeconds\":900"));
        ScheduledRefresh(resumed); Equal("running", Get(resumed, "Status"), "nonstructural live timing edit preserves run");
        Equal(deadline, Get(resumed, "DeadlineRebirthSeconds"), "active timing edit cannot reset deadline");
        CallMain("EndCombatPrep", "cancelled", "Injector unloaded.", true);
        ScheduledTime(1200); Set(MainInstance, "_protectedScheduler", null);
        plan = Invoke(Get(MainType, "CustomPlanStore"), "Load", "fixture");
        CallMain("RunCustomPlanAutomation", plan);
        Equal(false, Get(MainInstance, "CombatPrepActive"), "offline expiry cannot restart the terminal combined row");
    }

    private static void CheckScheduledFailures()
    {
        var plan = ScheduledSetup(); Setting("ManageMagic", false);
        var recalls = Recalls; var swaps = Swaps;
        CallMain("RunCustomPlanAutomation", plan);
        var run = Get(MainInstance, "_combatPrepRun");
        Equal("failed", Get(run, "Status"), "blocked automatic planning surfaces explicit failure");
        Equal(recalls, Recalls, "failed acquisition never recalls unmanaged resources"); Equal(swaps, Swaps, "failed acquisition never equips ordinary gear");
        CallMain("RunCustomPlanAutomation", plan);
        Equal(true, ReferenceEquals(run, Get(MainInstance, "_combatPrepRun")), "failed last row is consumed, not retried endlessly");

        plan = ScheduledSetup();
        Set(Character, "curEnergy", 0L); Set(Character, "idleEnergy", 0L);
        Set(Get(Character, "magic"), "curMagic", 0L); Set(Get(Character, "magic"), "idleMagic", 0L);
        recalls = Recalls; swaps = Swaps;
        CallMain("RunCustomPlanAutomation", plan); run = Get(MainInstance, "_combatPrepRun");
        Equal("failed", Get(run, "Status"), "baseline-only recommendation fails explicitly instead of pretending to run");
        Equal(true, ((string)Get(run, "Reason")).Contains("no executable phases"), "baseline-only failure explains missing phases");
        Equal(recalls, Recalls, "no-phase plan performs no resource recall"); Equal(swaps, Swaps, "no-phase plan performs no equipment mutation");
    }

    private static void CheckScheduledLoops()
    {
        var plan = ScheduledSetup();
        Setting("CustomAutoMoneyPitEnabled", false); Setting("CustomAutoBoostEnabled", false);
        Set(Settings, "_customAutoExpEnabled", false); Setting("CustomAutoBloodSpellsEnabled", false);
        CallMain("AutomationRoutine");
        var run = Get(MainInstance, "_combatPrepRun");
        Equal("running", Get(run, "Status"), "main automation pass starts a scheduled run");
        Equal(10f, Get(MainInstance, "_timeLeft"), "main pass returns immediately after starting the runtime owner");
        var scheduler = Get(MainInstance, "_protectedScheduler"); var entry = Get(scheduler, "Entry");
        var deadline = Get(run, "DeadlineRebirthSeconds"); var phaseDeadline = Get(run, "PhaseDeadline");
        ScheduledTime(510);
        foreach (var loop in new[] { "QuickStuff", "AutomationRoutine", "RunCustomSustainedFocusAllocation" })
        {
            CallMain(loop);
            Equal(true, ReferenceEquals(run, Get(MainInstance, "_combatPrepRun")), "active runtime owner retained across loop: " + loop);
            Equal(entry, Get(scheduler, "Entry"), "loop cannot advance scheduler under active executor: " + loop);
            Equal(deadline, Get(run, "DeadlineRebirthSeconds"), "loop preserves original deadline: " + loop);
            Equal(phaseDeadline, Get(run, "PhaseDeadline"), "loop does not restart the phase: " + loop);
        }
        var progress = CallMain("GetCustomPlanProgress");
        Equal(10d, Get(Json(progress, "stageElapsedSeconds"), "AsDouble"), "plan progress shows elapsed budget while scheduler is frozen");
        Equal("scheduled", JsonValue(Json(progress, "combatPrep"), "owner"), "plan progress carries automatic telemetry");
        CallMain("EndCombatPrep", "cancelled", "test cleanup", true);
        plan = ScheduledSetup();
        var support = CallMain("CombatPrepSupportKey");
        CallMain("QuickStuff"); run = Get(MainInstance, "_combatPrepRun");
        Equal("running", Get(run, "Status"), "quick loop acquires combined stage before ordinary beard/support refresh");
        Equal(support, Get(Get(Get(run, "Capture"), "OriginalSupportState"), "StableKey"), "automatic restoration capture precedes ordinary support mutation");
        CallMain("EndCombatPrep", "cancelled", "test cleanup", true);
    }
}
