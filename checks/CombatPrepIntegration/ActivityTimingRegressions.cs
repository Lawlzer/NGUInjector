using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public static partial class Program
{
    private static readonly string[] ActivityKinds = { "AT", "Augments", "Wandoos", "NGU", "Background" };

    private static void CheckActivityTimingRegressions()
    {
        CheckIdleActivityTiming();
        CheckPreparationActivityTiming();
        CheckExclusivePhaseTiming();
        CheckLatePhaseActivityTiming();
        CheckActivityTimingAcrossReplans();
        CheckFailedReplanTiming();
        CheckFailedPhaseSetupTiming();
        CheckTerminalActivityTiming();
        CheckCleanupActivityTiming();
        CheckRunLocalActivityTiming();
        Setup();
    }

    private static void AssertActivityTimes(object state, IDictionary<string, double> expected, string active = null)
    {
        Equal(1, Get(Json(state, "schemaVersion"), "AsInt"), "activity timing is additive schema v1");
        Equal("run", JsonValue(state, "activityTimingScope"), "activity scope explicitly excludes stage/history totals");
        Equal("exclusive-phase-wall-time-including-setup", JsonValue(state, "activityTimingSemantics"), "activity semantics are explicit");
        var rows = ((IEnumerable)Get(Json(state, "activityTimes"), "Children")).Cast<object>().ToArray();
        Equal(5, rows.Length, "activity telemetry has a fixed five-row bound");
        for (var i = 0; i < ActivityKinds.Length; i++)
        {
            var kind = ActivityKinds[i];
            Equal(kind, JsonValue(rows[i], "kind"), "canonical activity kind/order");
            Equal(true, Get(Json(rows[i], "elapsedSeconds"), "IsNumber"), "activity seconds are JSON numbers");
            Equal(expected.ContainsKey(kind) ? expected[kind] : 0d, Get(Json(rows[i], "elapsedSeconds"), "AsDouble"), "exact exclusive wall seconds for " + kind);
            Equal(kind == active, Get(Json(rows[i], "active"), "AsBool"), "only the current interval is active: " + kind);
        }
    }

    private static void CheckIdleActivityTiming()
    {
        Setup(); ControlledCombatPrepClock = 10000;
        AssertActivityTimes(CallMain("GetCombatPrepRuntime"), new Dictionary<string, double>());
        Equal(null, Get(MainInstance, "_combatPrepRun"), "timing reads do not manufacture an idle run");
    }

    private static void FinishTimingPhase(object run)
    {
        var phase = ScheduledPhase(run); var kind = (string)Get(phase, "Kind");
        if (kind == "AT")
        {
            var levels = (long[])Get(Get(Character, "advancedTraining"), "level");
            levels[3] = (long)Get(phase, "AtEnergyTarget"); levels[4] = (long)Get(phase, "AtMagicTarget");
        }
        if (kind == "Augments")
        {
            var augment = ((Array)Get(Get(Character, "augments"), "augs")).GetValue((int)Get(phase, "AugmentTierId"));
            Set(augment, "augLevel", Get(phase, "AugmentTarget")); Set(augment, "upgradeLevel", Get(phase, "UpgradeTarget"));
        }
        Set(run, "PhaseDeadline", ControlledCombatPrepClock.Value); ScheduledRefresh(run);
    }

    private static void CheckPreparationActivityTiming()
    {
        Setup(); ControlledCombatPrepClock = 10000;
        CombatPrepClockRead = () => {
            var current = Get(MainInstance, "_combatPrepRun");
            if (current != null && (int)Get(current, "PhaseIndex") < 0)
            { CombatPrepClockRead = null; ControlledCombatPrepClock += 17; }
        };
        var run = StartServerPolicy(CallMain("CaptureCombatPrep", .03, .03, null), Phase("Wandoos", "current", 60));
        Equal(null, CombatPrepClockRead, "preparation clock boundary actually advanced");
        Equal(17d, Get(Json(CallMain("GetCombatPrepRuntime"), "elapsedSeconds"), "AsDouble"), "preparation consumes run budget");
        AssertActivityTimes(CallMain("GetCombatPrepRuntime"), new Dictionary<string, double>(), "Wandoos");
        CallMain("EndCombatPrep", "cancelled", "preparation timing cleanup", true);
    }

    private static void CheckExclusivePhaseTiming()
    {
        foreach (var kinds in new[] { new[] { "AT", "Augments", "Wandoos" }, new[] { "Augments", "AT", "NGU" }, new[] { "AT", "Augments", "Background" } })
        {
            Setup(); ControlledCombatPrepClock = 10000;
            var run = StartServerPolicy(CallMain("CaptureCombatPrep", .03, .03, null), kinds.Select(x => Phase(x, "current", 60)).ToArray());
            var expected = new Dictionary<string, double>();
            // Advance the clock during phase setup, after the interval starts but before funding.
            CombatPrepClockRead = () => {
                if ((int)Get(run, "ActivityIndex") == Array.IndexOf(ActivityKinds, kinds[1]))
                { CombatPrepClockRead = null; ControlledCombatPrepClock += 2; }
            };
            ControlledCombatPrepClock += 3;
            expected[kinds[0]] = 3;
            AssertActivityTimes(CallMain("GetCombatPrepRuntime"), expected, kinds[0]);
            var counters = (double[])Get(run, "ActivitySeconds");
            Equal(true, counters.All(x => x == 0), "reading an open interval does not settle any counters");
            FinishTimingPhase(run);
            Equal(null, CombatPrepClockRead, "setup clock boundary actually advanced");
            expected[kinds[1]] = 2;
            AssertActivityTimes(CallMain("GetCombatPrepRuntime"), expected, kinds[1]);
            Equal(2d, Get(Json(CallMain("GetCombatPrepRuntime"), "phaseElapsedSeconds"), "AsDouble"), "setup belongs to the new phase, consistent with phase elapsed");
            ControlledCombatPrepClock += 5; expected[kinds[1]] += 5;
            FinishTimingPhase(run);
            if (!expected.ContainsKey(kinds[2])) expected[kinds[2]] = 0;
            ControlledCombatPrepClock += 7; expected[kinds[2]] += 7;
            AssertActivityTimes(CallMain("GetCombatPrepRuntime"), expected, kinds[2]);
            var before = counters.ToArray(); var interval = Get(run, "ActivityStarted");
            var snapshot = CallMain("GetCombatPrepRuntime"); var snapshotText = snapshot.ToString();
            var cache = Get(MainInstance, "_combatPrepRuntime").ToString();
            var resources = Resources(); var swaps = Swaps; var recalls = Recalls;
            for (var read = 0; read < 5; read++) AssertActivityTimes(CallMain("GetCombatPrepRuntime"), expected, kinds[2]);
            Equal(true, before.SequenceEqual(counters), "repeated reads never commit time");
            Equal(interval, Get(run, "ActivityStarted"), "repeated reads never restart the open interval");
            Equal(cache, Get(MainInstance, "_combatPrepRuntime").ToString(), "open timing snapshots do not mutate the published cache");
            Equal(resources, Resources(), "timing reads preserve resource/gear state");
            Equal(swaps, Swaps, "timing reads never swap gear"); Equal(recalls, Recalls, "timing reads never recall resources");
            ControlledCombatPrepClock += 1;
            expected[kinds[2]] += 1;
            AssertActivityTimes(CallMain("GetCombatPrepRuntime"), expected, kinds[2]);
            Equal(snapshotText, snapshot.ToString(), "a later timing read does not rewrite the retained snapshot");
            FinishTimingPhase(run);
            Equal("completed", Get(run, "Status"), "manual phase exhaustion completes");
            AssertActivityTimes(CallMain("GetCombatPrepRuntime"), expected);
        }
    }

    private static void CheckLatePhaseActivityTiming()
    {
        Setup(); ControlledCombatPrepClock = 10000;
        var run = StartServerPolicy(CallMain("CaptureCombatPrep", .03, .03, null), Phase("AT", "current", 60), Phase("Wandoos", "current", 60));
        ControlledCombatPrepClock += 65;
        ScheduledRefresh(run);
        Equal("AT", Get(ScheduledPhase(run), "Kind"), "unfinished targets hold a late phase past its scheduled budget");
        AssertActivityTimes(CallMain("GetCombatPrepRuntime"), new Dictionary<string, double> { { "AT", 65 } }, "AT");
        CallMain("EndCombatPrep", "cancelled", "late target timing cleanup", true);
        AssertActivityTimes(CallMain("GetCombatPrepRuntime"), new Dictionary<string, double> { { "AT", 65 } });
    }

    private static void CheckActivityTimingAcrossReplans()
    {
        var plan = ScheduledSetup(); ControlledCombatPrepClock = 10000;
        Set(Get(Get(Character, "buttons"), "augmentation"), "m_Interactable", false);
        foreach (var name in new[] { "wandoosEnergy", "wandoosMagic" }) Set(Get(Get(Character, "advancedTrainingController"), name), "levelFactor", 1000f);
        var run = StartScheduled(plan);
        Equal("AT", Get(ScheduledPhase(run), "Kind"), "timing replan fixture acquires paid AT");
        var expected = new Dictionary<string, double> { { "AT", 11 } };
        ControlledCombatPrepClock += 11;
        // The next clock read after settlement represents 20 seconds of capture/search/preflight.
        CombatPrepClockRead = () => {
            if ((int)Get(run, "ActivityIndex") < 0)
            { CombatPrepClockRead = null; ControlledCombatPrepClock += 20; }
        };
        FinishTimingPhase(run);
        Equal("running", Get(run, "Status"), "timed boundary replan succeeds");
        Equal(1, Get(run, "ReplanCount"), "actual boundary ran the real planner");
        Equal(false, ((Array)Get(Get(run, "Policy"), "Phases")).Cast<object>().Any(x => (string)Get(x, "Kind") == "AT"), "outgoing AT was removed from the new policy");
        var kind = (string)Get(ScheduledPhase(run), "Kind");
        AssertActivityTimes(CallMain("GetCombatPrepRuntime"), expected, kind);
        for (var replan = 0; replan < 2; replan++)
        {
            ControlledCombatPrepClock += 7;
            expected[kind] = (expected.ContainsKey(kind) ? expected[kind] : 0) + 7;
            if (replan == 0)
            {
                var machine = Get(Character, "machine"); Set(machine, "realBaseGold", (double)Get(machine, "realBaseGold") * 2 + 100);
            }
            else
            {
                var revise = (Action)CallMain("CaptureCombatPrepMaterialRevision");
                var head = Get(Get(Character, "inventory"), "head"); Set(head, "level", (int)Get(head, "level") + 1);
                revise?.Invoke();
            }
            ScheduledRefresh(run);
            Equal("running", Get(run, "Status"), "material replan remains active");
            Equal(replan == 0 ? "material-gps-change" : "material-equipment-change", Get(run, "ReplanReason"), "both material replan paths execute");
            kind = (string)Get(ScheduledPhase(run), "Kind");
            AssertActivityTimes(CallMain("GetCombatPrepRuntime"), expected, kind);
        }
        Equal(3, Get(run, "ReplanCount"), "three replans retain prior visits instead of resetting counters");
        var state = CallMain("GetCombatPrepRuntime");
        Equal(Get(run, "OwnerPlan"), JsonValue(state, "planName"), "activity telemetry retains plan owner match");
        Equal(Get(run, "OwnerStage"), Get(Json(state, "stageIndex"), "AsInt"), "activity telemetry retains stage owner match");
        Equal(Get(run, "OwnerEntry"), JsonValue(state, "stageEntry"), "activity telemetry retains entry owner match");
        CallMain("EndCombatPrep", "cancelled", "timing test cleanup", true);
        AssertActivityTimes(CallMain("GetCombatPrepRuntime"), expected);
    }

    private static void CheckFailedReplanTiming()
    {
        var plan = ScheduledSetup(); ControlledCombatPrepClock = 10000;
        var run = StartScheduled(plan); var kind = (string)Get(ScheduledPhase(run), "Kind");
        ControlledCombatPrepClock += 9;
        // Exhaust actual resources only after returning every owned allocation to idle.
        CallMain("RecallCombatPrepResources");
        Equal(Get(Character, "curEnergy"), Get(Character, "idleEnergy"), "failed replan fixture recalls all current energy before exhaustion");
        var magic = Get(Character, "magic");
        Equal(Get(magic, "curMagic"), Get(magic, "idleMagic"), "failed replan fixture recalls all current magic before exhaustion");
        Set(Character, "curEnergy", 0L); Set(Character, "idleEnergy", 0L);
        Set(magic, "curMagic", 0L); Set(magic, "idleMagic", 0L);
        Equal(0L, OwnedResourceTotal(true), "failed replan fixture leaves no energy in owned destinations");
        Equal(0L, OwnedResourceTotal(false), "failed replan fixture leaves no magic in owned destinations");
        var machine = Get(Character, "machine"); Set(machine, "realBaseGold", (double)Get(machine, "realBaseGold") * 2 + 100);
        CombatPrepClockRead = () => {
            if ((int)Get(run, "ActivityIndex") < 0)
            { CombatPrepClockRead = null; ControlledCombatPrepClock += 30; }
        };
        ScheduledRefresh(run);
        Equal("failed", Get(run, "Status"), "failed remaining-budget planner terminates explicitly");
        Equal(true, ((string)Get(run, "Reason")).Contains("no executable phases"), "failure occurs after outgoing interval settlement");
        AssertActivityTimes(CallMain("GetCombatPrepRuntime"), new Dictionary<string, double> { { kind, 9 } });
    }

    private static void CheckTerminalActivityTiming()
    {
        foreach (var status in new[] { "cancelled", "completed", "failed" })
        {
            Setup(); ControlledCombatPrepClock = 10000;
            var run = StartServerPolicy(CallMain("CaptureCombatPrep", .03, .03, null), Phase("Wandoos", "current", 60));
            ControlledCombatPrepClock += 8;
            // Simulate a slow cleanup after the first terminal timestamp has been taken.
            CombatPrepClockRead = () => {
                if (Get(run, "EndedAt") != null)
                { CombatPrepClockRead = null; ControlledCombatPrepClock += 40; }
            };
            CallMain("EndCombatPrep", status, "terminal timing regression", true);
            Equal(status, Get(run, "Status"), "requested terminal outcome retained");
            AssertActivityTimes(CallMain("GetCombatPrepRuntime"), new Dictionary<string, double> { { "Wandoos", 8 } });
            Equal(8d, Get(Json(CallMain("GetCombatPrepRuntime"), "elapsedSeconds"), "AsDouble"), "terminal elapsed stops before cleanup");
            var frozen = CallMain("GetCombatPrepRuntime"); var times = Json(frozen, "activityTimes").ToString();
            var phaseTime = Json(frozen, "phaseElapsedSeconds").ToString();
            ControlledCombatPrepClock += 100;
            CallMain("CacheCombatPrepRuntime");
            var later = CallMain("GetCombatPrepRuntime");
            Equal(times, Json(later, "activityTimes").ToString(), "explicit terminal recache cannot extend activity time");
            Equal(phaseTime, Json(later, "phaseElapsedSeconds").ToString(), "explicit terminal recache cannot extend phase time");
            CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(run, "Id") + "\"}"));
            Equal(times, Json(CallMain("GetCombatPrepRuntime"), "activityTimes").ToString(), "idempotent terminal cancellation preserves time");
        }
    }

    private static void CheckFailedPhaseSetupTiming()
    {
        foreach (var partial in new[] { false, true })
        {
            Setup(); ControlledCombatPrepClock = 10000;
            var inventory = Get(Character, "inventory"); var head = Blank(Get(inventory, "head").GetType());
            Set(head, "id", 60); SetEnum(head, "type", "Head"); Set(head, "bossRequired", 1); Set(head, "removable", true);
            SetEnum(head, "spec1Type", "AdvTraining"); Set(head, "spec1Cur", 1e30f);
            ((IList)Get(inventory, "inventory"))[0] = head;
            if (partial)
            {
                var chest = Blank(head.GetType()); Set(chest, "id", 61); SetEnum(chest, "type", "Chest");
                Set(chest, "bossRequired", 1); Set(chest, "removable", true);
                SetEnum(chest, "spec1Type", "AdvTraining"); Set(chest, "spec1Cur", 1e30f);
                ((IList)Get(inventory, "inventory"))[1] = chest;
            }
            var capture = CallMain("CaptureCombatPrep", .03, .03, null);
            var wanted = ((IDictionary)Get(capture, "Gear"))["at-current"];
            Equal(partial ? 2 : 1, ((Array)wanted).Cast<object>().Count(x => (int)Get(x, "id") == 60 || (int)Get(x, "id") == 61),
                "timing fault distinguishes a complete one-swap model from a genuinely unfinished two-swap model");
            FailRefreshAtSwap = Swaps + 1;
            CombatPrepClockRead = () => {
                var current = Get(MainInstance, "_combatPrepRun");
                if (current != null && (int)Get(current, "ActivityIndex") >= 0)
                { CombatPrepClockRead = null; ControlledCombatPrepClock += 4; }
            };
            StartServerPolicy(capture, Phase("AT", "at-current", 60));
            var run = Get(MainInstance, "_combatPrepRun");
            Equal(-1, FailRefreshAtSwap, "native setup fault actually fires after the first owned swap");
            Equal("running", Get(run, "Status"), "unfinished shared application retains its acquired phase and elapsed deadline");
            Equal(!partial, Get(run, "SetupVerified"), "unfinished native gear cannot masquerade as rendering-only success");
            if (partial)
            {
                Equal(true, ((string)Get(run, "Reason")).Contains("fixture controller refresh failure"), "failure came from real partial phase acquisition");
                Equal(0L, ((long[])Get(Get(Character, "advancedTraining"), "energy")).Sum(), "partial setup never authorizes dependent AT funding");
                CallMain("EndCombatPrep", "cancelled", "partial setup timing cleanup", true);
            }
            else
            {
                AssertActivityTimes(CallMain("GetCombatPrepRuntime"), new Dictionary<string, double> { { "AT", 4 } }, "AT");
                CallMain("EndCombatPrep", "cancelled", "verified render-only timing cleanup", true);
            }
            AssertActivityTimes(CallMain("GetCombatPrepRuntime"), new Dictionary<string, double> { { "AT", 4 } });
            ControlledCombatPrepClock += 80; CallMain("CacheCombatPrepRuntime");
            AssertActivityTimes(CallMain("GetCombatPrepRuntime"), new Dictionary<string, double> { { "AT", 4 } });
        }
    }

    private static void CheckCleanupActivityTiming()
    {
        foreach (var handoff in new[] { false, true })
        {
            object plan, original;
            var run = CleanupRecoveryFixture(out plan, out original, false);
            // Adopt deterministic time at the already-established interval origin.
            ControlledCombatPrepClock = (double)Get(run, "PhaseStarted") + 6;
            var kind = (string)Get(ScheduledPhase(run), "Kind");
            FailDesiredEnergyPreflight = true;
            CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(run, "Id") + "\"}"));
            Equal(true, Get(run, "CleanupPending"), "real native resource observation failure defers narrow release");
            var expected = new Dictionary<string, double> { { kind, 6 } };
            AssertActivityTimes(CallMain("GetCombatPrepRuntime"), expected);
            ControlledCombatPrepClock += 50;
            for (var retry = 0; retry < 3; retry++) { FailDesiredEnergyPreflight = true; CallMain("TickCombatPrep"); ControlledCombatPrepClock += 1; }
            AssertActivityTimes(CallMain("GetCombatPrepRuntime"), expected);
            Set(Controller, "midDrag", false);
            if (handoff) Set(Get(Character, "bossController"), "isFighting", true);
            CallMain("TickCombatPrep");
            Equal(false, Get(run, "CleanupPending"), "retry either finishes or relinquishes cleanup");
            Equal(false, Get(MainInstance, "CombatPrepCleanupRecoveryRequired"), "same-pass coordinator retires resolved/relinquished narrow release metadata");
            Equal(handoff, Get(run, "Retired"), "native combat handoff retires old authority; healthy release completes without historical restoration");
            CallMain("ServiceAutomaticRecovery");
            Equal(false, Get(MainInstance, "CombatPrepCleanupRecoveryRequired"), "coordinator automatically retires exhausted cleanup marker");
            CallMain("CacheCombatPrepRuntime");
            AssertActivityTimes(CallMain("GetCombatPrepRuntime"), expected);
        }
    }

    private static void CheckRunLocalActivityTiming()
    {
        var plan = ScheduledSetup(); ControlledCombatPrepClock = 10000;
        var first = StartScheduled(plan); ControlledCombatPrepClock += 13;
        CallMain("EndCombatPrep", "cancelled", "Injector unloaded.", true);
        var files = Directory.GetFiles((string)Get(MainType, "_dir"), "stage-progress-*.json");
        Equal(true, files.Length > 0, "continuation checkpoint really exists");
        foreach (var file in files)
        {
            var json = File.ReadAllText(file);
            Equal(false, json.Contains("activityTimes") || json.Contains("ActivitySeconds") || json.Contains("activityTimingScope"), "activity time is not persisted");
        }
        ControlledCombatPrepClock += 50; ScheduledTime(550);
        Set(MainInstance, "_protectedScheduler", null);
        CallMain("GetProtectedStage", plan);
        var resumed = StartScheduled(plan);
        Equal(Get(first, "OwnerEntry"), Get(resumed, "OwnerEntry"), "resumed timing fixture retains scheduled entry");
        Equal(false, Equals(Get(first, "Id"), Get(resumed, "Id")), "continuation has a distinct run identity");
        AssertActivityTimes(CallMain("GetCombatPrepRuntime"), new Dictionary<string, double>(), (string)Get(ScheduledPhase(resumed), "Kind"));
        CallMain("EndCombatPrep", "cancelled", "timing regression cleanup", true);
        plan = ScheduledSetup(); ControlledCombatPrepClock = 10000; Setting("ManageMagic", false);
        CallMain("RunCustomPlanAutomation", plan);
        Equal("failed", Get(Get(MainInstance, "_combatPrepRun"), "Status"), "preparation failure publishes a failed run");
        ControlledCombatPrepClock += 100; CallMain("CacheCombatPrepRuntime");
        AssertActivityTimes(CallMain("GetCombatPrepRuntime"), new Dictionary<string, double>());
    }
}
