using System;
using System.Collections;
using System.Collections.Generic;

internal static partial class IronPillReservationChecks
{
    private static void CheckPlanScopedBloodRuntime()
    {
        Setup();
        Setting("ManageMagic", true);
        Setting("CustomAutoBloodSpellsEnabled", false);
        Setting("CustomBloodSpaghettiGoalPercent", 90);
        Setting("CustomBloodCounterfeitGoalPercent", 400);
        Set(_plan, "AutoIronPillEnabled", false);
        Set(_plan, "AutoBloodSpellsEnabled", true);
        Set(_plan, "BloodSpaghettiGoalPercent", 15);
        Set(_plan, "BloodCounterfeitGoalPercent", 0);
        Eq(true, GoalRequest(), "running plan enables catch-up over global opt-out");
        Call("RunCustomBloodSpellToggles", _plan);
        AssertToggles(true, false, false, "plan-scoped enabled and zero goals");
        Set(_blood, "lootSpellBlood", 163840000d);
        Call("RunCustomBloodSpellToggles", _plan);
        AssertToggles(false, false, true, "runtime uses plan threshold rather than larger global threshold");
        Eq(false, GoalRequest(), "plan goal reached releases runtime catch-up");

        var first = _plan;
        var second = Activator.CreateInstance(_planType);
        Set(second, "AutoBloodSpellsEnabled", false);
        Set(second, "BloodSpaghettiGoalPercent", 30);
        Setting("CustomAutoBloodSpellsEnabled", true);
        Call("RunCustomBloodSpellToggles", second);
        AssertToggles(false, false, true, "disabled independent plan leaves spell ownership untouched");
        Eq(false, Call("CustomBloodGoalCatchUpRequested", second), "second plan disables catch-up over global opt-in");
        Eq(15, Call("EffectiveCustomBloodSpaghettiGoalPercent", first), "second plan cannot mutate first goal");
        Eq(90, _settings.GetType().GetProperty("CustomBloodSpaghettiGoalPercent").GetValue(_settings), "plan goals never mutate globals");
        Set(first, "AutoBloodSpellsEnabled", null);
        Set(first, "BloodSpaghettiGoalPercent", null);
        Set(first, "BloodCounterfeitGoalPercent", null);
        Call("RunCustomBloodSpellToggles", first);
        AssertToggles(true, true, false, "null plan fields retain legacy spell behavior");
        Setting("UseCustomRebirthPlan", false);
        Call("RunCustomBloodSpellToggles", second);
        AssertToggles(true, true, false, "Quick ignores selected stopped plan opt-out and goals");
        Setting("CustomAutoBloodSpellsEnabled", false);
        Set(first, "AutoBloodSpellsEnabled", true);
        Eq(false, Call("CustomBloodGoalCatchUpRequested", first), "Quick cannot inherit stopped plan opt-in");

        Setting("UseCustomRebirthPlan", true);
        _plan = first;
        CachePlan();
        var state = Activator.CreateInstance(_assembly.GetType("SimpleJSON.JSONObject", true));
        Call("WriteExternalUiBloodState", state);
        Eq("\"iron-reservation-check\"", Json(Json(state, "bloodGoals"), "planName").ToString(), "blood telemetry identifies actual runtime file");
        Eq("true", Json(Json(state, "bloodGoals"), "autoBloodSpellsEnabled").ToString(), "blood telemetry uses effective plan enablement");
        Eq("1", Json(Call("BuildExternalUiRuntimeState"), "planBloodMagicVersion").ToString(), "runtime advertises plan Blood Magic capability");
        Setting("UseCustomRebirthPlan", false);
        Call("WriteExternalUiBloodState", state);
        Eq("\"\"", Json(Json(state, "bloodGoals"), "planName").ToString(), "Quick telemetry is not attributed to selected stopped plan");
        Eq("false", Json(Json(state, "bloodGoals"), "autoBloodSpellsEnabled").ToString(), "Quick telemetry retains global disabled");
    }

    private static void CheckPlanScopedIronPillRuntime()
    {
        Setup();
        Setting("CustomAutoBloodSpellsEnabled", false);
        Setting("CustomIronPillSaveSeconds", 0);
        Setting("CustomIronPillMinWorth", 1e8d);
        Set(_plan, "AutoBloodSpellsEnabled", true);
        Set(_plan, "IronPillSaveSeconds", 100);
        Set(_plan, "IronPillMinWorth", 0d);
        Set(Get(_blood, "adventureSpellTime"), "totalseconds", 3500d);
        Call("RunCustomBloodSpellAutomation", _plan);
        Eq(true, Saving(), "plan enables Iron Pill and uses plan cooldown lookahead");
        Set(_blood, "bloodPoints", 100d);
        AtTime(5999d);
        Set(Get(_blood, "adventureSpellTime"), "totalseconds", 3600d);
        Call("RunCustomBloodSpellAutomation", _plan);
        Eq(true, Saving(), "explicit plan zero worth suppresses global worth target");
        AtTime(6000d);
        Call("RunCustomBloodSpellAutomation", _plan);
        Eq(false, Saving(), "plan elapsed target casts despite global disabled and different target");
        Eq(3f, Get(Get(_character, "adventure"), "attack"), "plan target executes real pill");

        Setup();
        Set(_plan, "IronPillSaveSeconds", 0);
        Set(_plan, "IronPillMinWorth", 3d);
        Call("RunCustomBloodSpellAutomation", _plan);
        Set(_blood, "bloodPoints", 100d);
        Call("RunCustomBloodSpellAutomation", _plan);
        Eq(false, Saving(), "plan worth target casts without global saving wait");

        Setup();
        Set(_plan, "IronPillSaveSeconds", 1800);
        Set(_plan, "IronPillMinWorth", 0d);
        Call("RunCustomBloodSpellAutomation", _plan);
        AtTime(6860d);
        Call("RunCustomBloodSpellAutomation", _plan);
        Eq(true, Saving(), "plan prep deadline does not inherit shorter global deadline");
        AtTime(7760d);
        Call("RunCustomBloodSpellAutomation", _plan);
        Eq(false, Saving(), "plan prep deadline remains finite");
        Eq(8660d, Get(_main, "_customIronPillRetryAt"), "plan timeout establishes backoff");
        Setting("CustomPlanFile", "second-runtime-plan");
        Call("RunCustomBloodSpellAutomation", _plan);
        Eq(true, Saving(), "new plan does not inherit prior plan timeout backoff");
        Set(_blood, "bloodPoints", 321d);
        Setting("CustomPlanFile", "third-runtime-plan");
        Eq(false, Saving(), "reservation identity changes immediately before next loop");
        Call("RunCustomBloodSpellToggles", _plan);
        Eq(false, Saving(), "plan switch cancels rather than inherits active saving window");
        Eq(321d, Get(_blood, "bloodPoints"), "plan switch never immediately flushes old reserved blood");
        AtTime(7770d);
        Call("RunCustomBloodSpellToggles", _plan);
        Eq(true, Saving(), "new plan can start its own saving window after guard");
        Eq(7770d, Get(_main, "_customIronPillPrepStartedAt"), "new plan starts fresh saving timer");
        Call("InvalidateCustomRuntimePlanCache");
        Call("RunCustomBloodSpellToggles", _plan);
        Eq(7770d, Get(_main, "_customIronPillPrepStartedAt"), "same-plan cache reload preserves saving timer");
        Set(_plan, "AutoBloodSpellsEnabled", false);
        Call("RunCustomBloodSpellToggles", _plan);
        Eq(false, Saving(), "plan blood opt-out cancels reservation");
        Eq("Automatic blood spells are disabled.", Call("CustomIronPillEligibilityBlocker", _plan), "eligibility uses effective disabled plan");

        Setup();
        Set(_plan, "IronPillSaveSeconds", 1500);
        Set(_plan, "IronPillMinWorth", 9d);
        var state = Activator.CreateInstance(_assembly.GetType("SimpleJSON.JSONObject", true));
        Call("WriteExternalUiBloodState", state);
        var iron = Json(state, "ironPill");
        Eq("1500", Json(iron, "saveSeconds").ToString(), "Iron Pill telemetry exposes effective plan time");
        Eq("9", Json(iron, "minWorth").ToString(), "Iron Pill telemetry exposes effective plan worth");
        Eq("\"iron-reservation-check\"", Json(iron, "planName").ToString(), "Iron Pill telemetry has runtime identity");
        Eq("1560", Json(iron, "prepLimitSeconds").ToString(), "Iron Pill telemetry derives deadline from plan time");
        Eq("true", Json(iron, "available").ToString(), "independent Iron Pill telemetry survives unavailable optimizer state");
        Call("RunCustomBloodSpellAutomation", _plan);
        Set(_main, "_customIronPillRetryAt", 9999d);
        Setting("CustomPlanFile", "new-telemetry-plan");
        CachePlan();
        Call("WriteExternalUiBloodState", state);
        iron = Json(state, "ironPill");
        Eq("false", Json(iron, "reservingBlood").ToString(), "telemetry cannot report previous plan reservation");
        Eq("0", Json(iron, "retryInSeconds").ToString(), "telemetry cannot report previous plan backoff");
    }

    private static void CheckBloodGoalTimerIsolation()
    {
        Setup();
        Setting("AutoRebirth", false);
        Setting("ManageMagic", true);
        Setting("CustomBloodSpaghettiGoalPercent", 15);
        Setting("CustomBloodCounterfeitGoalPercent", 100);
        Set(_plan, "AutoIronPillEnabled", false);
        // At actual AT completion (5910), reserve 20s for the fixed tail and
        // assign the remaining 20s before this horizon to the casual flex row.
        Set(_plan, "StageDurationSeconds", 5950);
        var stages = Array.CreateInstance(_stageType, 3);
        for (var i = 0; i < 3; i++)
        {
            var stage = Activator.CreateInstance(_stageType);
            Set(stage, "Name", "blood-timer-isolation-" + i);
            Set(stage, "Percent", 1d);
            Set(stage, "DurationSeconds", i == 2 ? 20 : 0);
            Set(stage, "CompletionMode", i == 0 ? 1 : 0);
            Set(stage, "EnergyFocus", i == 0 ? 7 : i == 1 ? 5 : 0);
            Set(stage, "MagicFocus", i == 0 ? 4 : 6);
            Set(stage, "GearGoal", (int) Enum.Parse(_assembly.GetType("NGUInjector.Managers.GearOptimizationGoal", true),
                i == 0 ? "AdvancedTraining" : i == 1 ? "EnergyNgu" : "MagicNgu"));
            Set(stage, "TargetAdvancedTrainingPower", 100L);
            Set(stage, "TargetAdvancedTrainingToughness", 200L);
            stages.SetValue(stage, i);
        }
        Set(_plan, "Stages", stages);
        var training = Child(_character, "advancedTraining");
        Set(training, "level", new long[5]);
        Set(training, "levelTarget", new long[] {31, 47, 0, 0, 0});
        CachePlan();
        var runtime = ActiveStage();
        Eq(0, Property(runtime, "StageIndex"), "isolated fixture begins at an unmet actual AT gate");
        Eq(0d, Earned(2), "late activation does not pre-credit the fixed tail");
        var atEntry = Property(runtime, "EntryIdentity");
        Call("RunCustomBloodSpellToggles", _plan);
        Eq("false", Json(Call("GetCustomPlanProgress"), "bloodGoalsPrioritized").ToString(), "ordinary AT stage cannot claim casual catch-up");
        ((long[]) Get(training, "level"))[0] = 200;
        ((long[]) Get(training, "level"))[1] = 100;
        AtTime(5910d);
        var state = CoreState();
        Eq(0, Property(Snapshot(), "ActiveStageIndex"), "read-only snapshot does not consume newly met AT targets");
        Eq("0", Json(Call("GetCustomPlanProgress"), "activeStageIndex").ToString(), "progress also leaves the met AT gate pending allocation");
        Eq(state, CoreState(), "met-target telemetry preserves the actual engine byte-for-byte");
        runtime = ActiveStage();
        Eq(1, Property(runtime, "StageIndex"), "actual AT target completion enters the casual flex stage");
        var entry = Property(runtime, "EntryIdentity");
        Eq(false, Equals(atEntry, entry), "actual AT completion changes the stable entry identity");
        Eq(0d, Property(runtime, "StageElapsedSeconds"), "post-AT casual row starts at zero actual elapsed time");
        Eq(20, Property(runtime, "EffectiveDurationSeconds"), "casual flex allocation reserves the later fixed guarantee");
        var equipmentInvalidated = Get(_main, "_customEquipmentApplicationInvalidated");
        var selection = Get(_main, "_inlineStageSelection");
        state = CoreState();
        Eq(true, GoalRequest(), "post-AT casual stage requests ordinary catch-up without rebirth permission");
        Call("RunCustomBloodSpellToggles", _plan);
        var progress = Call("GetCustomPlanProgress");
        Eq("true", Json(progress, "bloodGoalsPrioritized").ToString(), "isolated catch-up telemetry follows actual active step");
        Eq("[\"RIT\",\"NGU\",\"WAN\",\"TM\"]", Json(Row(progress, 1), "magicPriorities").ToString(), "post-AT catch-up publishes the real effective order");
        var snapshot = Snapshot();
        Eq(1, Property(snapshot, "ActiveStageIndex"), "read-only snapshot shares the engine's actual active row");
        Eq(0d, Property(snapshot, "StageElapsedSeconds"), "read-only snapshot shares the current engine elapsed value");
        Eq(true, ((IList<int>) Property(snapshot, "DurationSeconds")).IsReadOnly, "duration snapshot is read-only");
        Eq(state, CoreState(), "goal reads, spell toggles, progress and snapshots preserve the actual engine byte-for-byte");
        Eq(1, Property(Core, "StageIndex"), "catch-up preserves the engine index");
        Eq(0d, Earned(1), "flex catch-up does not manufacture fixed credit");
        Eq(entry, Property(ActiveStage(), "EntryIdentity"), "catch-up preserves the current entry identity");
        Eq(31L, ((long[]) Get(training, "levelTarget"))[0], "catch-up leaves toughness menu target untouched");
        Eq(47L, ((long[]) Get(training, "levelTarget"))[1], "catch-up leaves power menu target untouched");
        Eq(100L, Get(stages.GetValue(1), "TargetAdvancedTrainingPower"), "catch-up leaves configured power target intact");
        Eq(200L, Get(stages.GetValue(1), "TargetAdvancedTrainingToughness"), "catch-up leaves configured toughness target intact");
        Eq(equipmentInvalidated, Get(_main, "_customEquipmentApplicationInvalidated"), "catch-up preserves equipment invalidation state");
        Eq(true, ReferenceEquals(selection, Get(_main, "_inlineStageSelection")), "catch-up does not replace acquired inline selection");
        Eq(123456L, Get(_character, "idleEnergy"), "catch-up reads and toggles never recall energy");
        Eq(10000d, Get(_blood, "bloodPoints"), "ordinary catch-up does not spend or flush banked blood");
        Set(_blood, "lootSpellBlood", 163840000d);
        Set(_blood, "goldSpellBlood", 512000000d);
        state = CoreState();
        Call("RunCustomBloodSpellToggles", _plan);
        progress = Call("GetCustomPlanProgress");
        Eq("false", Json(progress, "bloodGoalsPrioritized").ToString(), "real bonus thresholds immediately release catch-up");
        Eq("[\"NGU\",\"RIT\",\"WAN\",\"TM\"]", Json(Row(progress, 1), "magicPriorities").ToString(), "thresholds restore original stage order");
        Eq(state, CoreState(), "threshold crossing leaves the actual scheduler untouched");
        Eq(entry, Property(ActiveStage(), "EntryIdentity"), "threshold crossing preserves entry identity");
        Set(_blood, "lootSpellBlood", 0d);
        AtTime(5930d);
        state = CoreState();
        Eq(1, Property(Snapshot(), "ActiveStageIndex"), "deadline polling does not advance the casual flex stage");
        Eq("1", Json(Call("GetCustomPlanProgress"), "activeStageIndex").ToString(), "progress at the deadline is also read-only");
        Eq(state, CoreState(), "deadline queries preserve the entire actual engine");
        runtime = ActiveStage();
        Eq(2, Property(runtime, "StageIndex"), "real coordinator advances at the funded casual flex deadline");
        Eq(false, Equals(entry, Property(runtime, "EntryIdentity")), "fixed tail receives a fresh entry identity");
        Eq(0d, Earned(2), "neither AT wait nor flex time credits the fixed tail");
        Eq(false, ProtectedGate(), "unapplied fixed tail remains protected after the flex transition");
        Eq("false", Json(Call("GetCustomPlanProgress"), "bloodGoalsPrioritized").ToString(), "dedicated Magic NGU stage cannot retain catch-up from prior stage");
    }

    private static void CheckQuickBloodGoalTelemetry()
    {
        Setup();
        Setting("AutoRebirth", false);
        Setting("UseCustomRebirthPlan", false);
        Setting("ManageMagic", true);
        Setting("CustomBloodSpaghettiGoalPercent", 15);
        Set(_plan, "AutoIronPillEnabled", false);
        var ngu = Child(_character, "NGU");
        foreach (var name in new[] {"skills", "magicSkills"})
        {
            var field = ngu.GetType().GetField(name, All);
            var skills = (IList) Activator.CreateInstance(field.FieldType);
            for (var i = 0; i < (name == "skills" ? 9 : 7); i++) skills.Add(New(field.FieldType.GetGenericArguments()[0]));
            field.SetValue(ngu, skills);
        }
        CachePlan();
        var focusType = _assembly.GetType("NGUInjector.CustomFocusGoal", true);
        foreach (var goal in new[] {"EnergyNgu", "MagicNgu", "Ngu"})
        {
            Set(_main, "_customSustainedFocusGoal", Enum.Parse(focusType, goal));
            var runtime = Call("GetCustomNguRuntime");
            Eq(goal == "EnergyNgu" ? "true" : "false", Json(runtime, "bloodGoalsPrioritized").ToString(), goal + " real runtime wrapper reports narrowly scoped catch-up");
            Eq(goal == "EnergyNgu" ? "[\"RIT\",\"NGU\",\"WAN\",\"TM\"]" : "[\"NGU\",\"RIT\",\"WAN\",\"TM\"]",
                Json(runtime, "effectiveMagicPriorities").ToString(), goal + " real runtime wrapper shares actual priority boundary");
        }
        Set(_main, "_customSustainedFocusGoal", Enum.Parse(focusType, "EnergyNgu"));
        Call("RunCustomBloodSpellToggles", _plan);
        AssertToggles(true, false, false, "Quick Energy NGU ordinary goal spells");
        Set(_blood, "lootSpellBlood", 163840000d);
        Call("RunCustomBloodSpellToggles", _plan);
        AssertToggles(false, false, true, "Quick Energy NGU restores NUMBER after goals");
        Eq("false", Json(Call("GetCustomNguRuntime"), "bloodGoalsPrioritized").ToString(), "Quick Energy NGU telemetry clears catch-up when goals reached");
    }
}
