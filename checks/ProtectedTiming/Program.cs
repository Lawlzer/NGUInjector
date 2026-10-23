using System;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;
using SimpleJSON;

internal static class Program
{
    private static int _passed;
    private static int _assertions;
    private static int _failures;

    private static int Main()
    {
        Run("35/50/15 matrix and gate", Matrix);
        Run("fixed earns full duration without early wall boundary", FixedFull);
        Run("inclusive 15-second continuity and resync", FifteenEdge);
        Run("restored partial fixed work needs fresh live credit to resync", RestoredResync);
        Run("interruptions and zero-credit intervals cannot resync", InterruptedResync);
        Run("no unstarted short-stage waiver", NoShortSkip);
        Run("every stage at most 15 seconds earns its full duration", ShortStagesFull);
        Run("no first-apply or offline credit", FirstApply);
        Run("RB and heartbeat long gaps disarm", Gaps);
        Run("ownership loss and suspend disarm", Ownership);
        Run("negative clock deltas disarm", NegativeClocks);
        Run("credit uses slower clock", SlowerClock);
        Run("fixed credit is capped and never crosses selections", CreditCap);
        Run("mandatory AT consumes pool without forgiveness", MandatoryAt);
        Run("AT actual exit anchors next fixed boundary", AtBoundary);
        Run("offline flexible expiry reserves fixed tail", OfflineFlex);
        Run("fixed tail reservation and skipped flex", TailReservation);
        Run("initial unfunded flex never selects an allocation", InitialSkip);
        Run("initial all-flex exhaustion has no held allocation", InitialAllFlexEmpty);
        Run("trailing unfunded flex holds actual allocation", TrailingSkip);
        Run("active flexible allocation is frozen", FrozenFlex);
        Run("future flex absorbs lateness at entry", FutureFlex);
        Run("duration edit preserves earned time and entry", DurationEdit);
        Run("late opener duration edit never infers historical work", LateDurationEdit);
        Run("near-limit duration edit preserves earned work without overflow", NearLimitDurationEdit);
        Run("flex edit uses unserved weight fraction", FlexEdit);
        Run("flex arithmetic preserves integral floor boundaries", FlexRounding);
        Run("runtime flex shares are scale invariant for subnormal weights", FlexScaleInvariance);
        Run("horizon edit reallocates without rewinding completed flex", HorizonEdit);
        Run("cosmetic edits and positional identity", CosmeticEdits);
        Run("adventure weight edits preserve scheduler identity and deadlines", AdventureWeightEdits);
        Run("invalid adventure weights reject transactionally", InvalidAdventureWeights);
        Run("structural edits reset explicitly", StructuralEdits);
        Run("snapshot properties are inert defensive copies", Snapshot);
        Run("state JSON roundtrip is unarmed", Roundtrip);
        Run("restored offline flex can expire but fixed cannot", RestoreOffline);
        Run("restore retains timing edits for next update", RestoreEdit);
        Run("malformed state is rejected transactionally", Malformed);
        Run("completed fixed checkpoints require positive earned time", CompletedFixedRestore);
        Run("loop resets suffix only and stops at horizon", Loops);
        Run("loop current-pass protected work finishes after horizon", LoopOverrun);
        Run("repeat admission closes without dropping current obligations", RepeatAdmission);
        Run("repeat admission is ephemeral across restore", RepeatRestore);
        Run("zero-time AT loop is bounded", AtLoop);
        Run("all-flex protected gate and horizon fallback", AllFlex);
        Run("invalid plans and clocks are rejected", InvalidInputs);
        Run("manual skip bypasses AT without evaluating target callbacks", ManualSkipAt);
        Run("manual skip passes unfunded flex without visits", ManualSkipUnfunded);
        Run("manual last skip holds allocation and opens protected gate", ManualLastSkip);
        Run("forward set records skipped obligations without forged credit", ManualForwardSet);
        Run("backward set reopens obligations while preserving lifetime history", ManualBackwardSet);
        Run("restart accrues outgoing time without auto-advancing", ManualRestart);
        Run("manual changes leave satisfied target evaluation to Update", ManualTargetEvaluation);
        Run("invalid manual requests leave state and arming unchanged", InvalidOverrides);
        Run("selected wall time and applied time track all stage kinds", AllKindHistory);
        Run("loop history accumulates while pass skip flags reset", LoopHistory);
        Run("codec 2 preserves manual state and restores unarmed", OverrideRoundtrip);
        Run("manual controls support terminal zero-selection and stage zero", ManualZeroSelection);
        Run("history credit requires continuous verified ownership for every kind", HistoryContinuity);
        Run("finished allocations keep applied history for every kind", FinishedKindHistory);
        Run("expired flex restart does not auto-advance before manual selection", ManualExpiredFlex);
        Run("combined elapsed budget consumes delays without protected credit", AutomaticBudget);
        Run("combined active deadline survives timing edits and checkpoint restore", AutomaticDeadline);
        Run("combined flex budgets reserve standard fixed tails", AutomaticFlex);
        Run("cleanup recovery checkpoint remains guarded until coordinator retirement or manual selection", AutomaticCleanupRecovery);
        Run("coordinator retirement preserves elapsed expiry rules without override or credit", AutomaticRecoveryExpiry);
        Console.WriteLine((_failures == 0 ? "PASS: " : "FAIL: ") + _passed + " protected timing regression scenarios, "
            + _assertions + " assertions, " + _failures + " failures.");
        return _failures == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            _passed++;
            Console.WriteLine("PASS " + name);
        }
        catch (Exception error)
        {
            _failures++;
            Console.Error.WriteLine("FAIL " + name + ": " + error);
        }
    }

    private static CustomPlanStage Fixed(int seconds) { return new CustomPlanStage { DurationSeconds = seconds }; }
    private static void AutomaticBudget()
    {
        var auto = new CustomPlanStage { ExecutionMode = 1, DurationSeconds = 20 };
        var p = Plan(100, Fixed(10), auto, Fixed(15));
        var s = Start(p, 0);
        AppliedTick(s, p, 0, 10); Equal(1, s.StageIndex); Equal(30, s.ExpectedEndSeconds);
        var entry = s.Entry;
        s.Update(p, 29, 29, false, null); Equal(1, s.StageIndex); Equal(0, s.EarnedSeconds[1]);
        Equal(19, s.StageElapsedSeconds); Equal(entry, s.Entry);
        s.Update(p, 50, 50, false, null); Equal(2, s.StageIndex); Equal(65, s.ExpectedEndSeconds);
        Equal(0, s.EarnedSeconds[1]); Equal(0, s.AppliedSeconds[1]); Check(!s.ProtectedWorkComplete);
        s.Update(p, 60, 60, false, null); Equal(0, s.EarnedSeconds[2]);
        AppliedTick(s, p, 60, 75); Check(s.Finished && s.ProtectedWorkComplete);
        entry = s.Entry; Tick(s, p, 200); Equal(entry, s.Entry);
        var terminal = Start(Plan(100, auto));
        Tick(terminal, Plan(100, auto), 21); Check(terminal.Finished); entry = terminal.Entry;
        Tick(terminal, Plan(100, auto), 90); Equal(entry, terminal.Entry);
    }

    private static void AutomaticCleanupRecovery()
    {
        foreach (var action in new[] { "skip", "set", "restart" })
        {
            var p = Plan(2000, new CustomPlanStage { ExecutionMode = 1, DurationSeconds = 600 }, Fixed(10));
            var s = Start(p, 500); s.AutoCleanupRecoveryRequired = true;
            var state = Copy(s.ExportState());
            var restored = new ProtectedStageScheduler(); Check(restored.RestoreState(p, state));
            Tick(restored, p, 1500); Equal(0, restored.StageIndex); Equal(1, restored.Entry);
            Equal(500, restored.EnteredAt); Equal(1100, restored.ExpectedEndSeconds);
            Equal(1000, restored.StageElapsedSeconds); Check(!restored.Finished && !restored.ProtectedWorkComplete);
            restored.MarkApplied(1500, 1500); restored.CompleteAutomaticEntry(1500);
            Equal(1100, restored.ExpectedEndSeconds); Check(restored.AutoCleanupRecoveryRequired);
            restored.OverrideStage(p, action, action == "set" ? 1 : 0, 1501, 1501);
            Check(!restored.AutoCleanupRecoveryRequired); Equal(2, restored.Entry);
            Equal(action == "restart" ? 0 : 1, restored.StageIndex);
            Equal(action == "restart" ? 2101 : 1511, restored.ExpectedEndSeconds);
            Check(new ProtectedStageScheduler().RestoreState(p, Copy(restored.ExportState())));
        }
        var plan = Plan(1000, new CustomPlanStage { ExecutionMode = 1, DurationSeconds = 600 });
        var scheduler = Start(plan); var saved = Copy(scheduler.ExportState());
        saved.Remove("autoCleanupRecoveryRequired");
        Check(new ProtectedStageScheduler().RestoreState(plan, saved));
        saved["autoCleanupRecoveryRequired"] = "true";
        Check(!new ProtectedStageScheduler().RestoreState(plan, saved));
        saved["autoCleanupRecoveryRequired"] = JSONNull.CreateOrGet();
        Check(!new ProtectedStageScheduler().RestoreState(plan, saved));
        scheduler.AutoCleanupRecoveryRequired = true;
        saved = Copy(scheduler.ExportState()); saved["lastHeartbeat"] = 1e9;
        var afterReboot = new ProtectedStageScheduler(); Check(afterReboot.RestoreState(plan, saved));
        afterReboot.Update(plan, 700, 10, false, null);
        Equal(600, afterReboot.ExpectedEndSeconds); Equal(0, afterReboot.EnteredAt);
        afterReboot.OverrideStage(plan, "restart", 0, 700, 10);
        Equal(1300, afterReboot.ExpectedEndSeconds); Check(!afterReboot.AutoCleanupRecoveryRequired);
    }

    private static void AutomaticRecoveryExpiry()
    {
        var p = Plan(2000, new CustomPlanStage { ExecutionMode = 1, DurationSeconds = 600 }, Fixed(10));
        var s = Start(p, 500); s.AutoCleanupRecoveryRequired = true;
        var restored = new ProtectedStageScheduler(); Check(restored.RestoreState(p, Copy(s.ExportState())));
        restored.AutoCleanupRecoveryRequired = false; // Coordinator has durably retired the entry-bound marker.
        restored.Update(p, 1500, 10, false, null);
        Equal(1, restored.StageIndex); Equal(2, restored.Entry); Equal(1510, restored.ExpectedEndSeconds);
        Equal(0, restored.EarnedSeconds[0]); Equal(0, restored.EarnedSeconds[1]); Equal(0, restored.AppliedSeconds[0]);
        Check(restored.Completed[0] && !restored.Completed[1]); Check(restored.LastOverride == string.Empty);
        var unexpired = new ProtectedStageScheduler(); Check(unexpired.RestoreState(p, Copy(s.ExportState())));
        unexpired.AutoCleanupRecoveryRequired = false;
        unexpired.Update(p, 550, 10, false, null);
        Equal(0, unexpired.StageIndex); Equal(1, unexpired.Entry); Equal(500, unexpired.EnteredAt); Equal(1100, unexpired.ExpectedEndSeconds);
        Equal(0, unexpired.EarnedSeconds[0]); Check(!unexpired.Completed[0]);
    }

    private static void AutomaticDeadline()
    {
        var p = Plan(100, new CustomPlanStage { ExecutionMode = 1, DurationSeconds = 20 }, Fixed(10));
        var s = Start(p, 50); s.AutoAtCompleted = s.AutoWandoosStarted = true;
        s.AutoEnergyPool = 9007199254740993L; s.AutoMagicPool = 123;
        p.Stages[0].DurationSeconds = 900; p.TimeTriggerSeconds = 2000;
        Tick(s, p, 55); Equal(70, s.ExpectedEndSeconds); Equal(20, s.Durations[0]); Equal(1, s.Entry);
        var saved = Copy(s.ExportState()); var restored = new ProtectedStageScheduler();
        Check(restored.RestoreState(p, saved)); Check(restored.AutoAtCompleted && restored.AutoWandoosStarted);
        Check(restored.AutoEnergyPool == 9007199254740993L && restored.AutoMagicPool == 123);
        Tick(restored, p, 65); Equal(0, restored.StageIndex); Equal(70, restored.ExpectedEndSeconds);
        Tick(restored, p, 80); Equal(1, restored.StageIndex); Equal(90, restored.ExpectedEndSeconds);
        Check(!restored.AutoAtCompleted && !restored.AutoWandoosStarted); Equal(0, restored.EarnedSeconds[1]);
        saved["autoWandoosStarted"] = "false"; Check(!new ProtectedStageScheduler().RestoreState(p, saved));
        p.Stages[0].ExecutionMode = 0; Check(!s.Matches(p));
    }

    private static void AutomaticFlex()
    {
        var p = Plan(100, new CustomPlanStage { ExecutionMode = 1, Percent = 1 }, Fixed(20));
        var s = Start(p); Equal(80, s.ExpectedEndSeconds);
        p.Stages[0].Percent = 9; p.TimeTriggerSeconds = 1000; Tick(s, p, 10);
        Equal(80, s.ExpectedEndSeconds); Equal(80, s.Durations[0]);
        var r = new ProtectedStageScheduler(); Check(r.RestoreState(p, Copy(s.ExportState())));
        Tick(r, p, 90); Equal(1, r.StageIndex); Equal(110, r.ExpectedEndSeconds);
        var empty = Plan(0, new CustomPlanStage { ExecutionMode = 1 });
        var t = Start(empty, 0, -1); Check(t.Finished);
        Check(new ProtectedStageScheduler().RestoreState(empty, Copy(t.ExportState())));
    }
    private static CustomPlanStage Flex(double weight = 1) { return new CustomPlanStage { Percent = weight }; }
    private static CustomPlanStage At() { return new CustomPlanStage { CompletionMode = 1, TargetAdvancedTrainingPower = 10, TargetAdvancedTrainingToughness = 20 }; }
    private static CustomPlan Plan(int horizon, params CustomPlanStage[] stages)
    {
        return new CustomPlan { Name = "test", Stages = stages, TimeTriggerSeconds = horizon, StageDurationSeconds = horizon, StageLoopStartIndex = -1 };
    }
    private static ProtectedStageScheduler Start(CustomPlan plan, double now = 0, int expectedIndex = 0)
    {
        var s = new ProtectedStageScheduler();
        s.Update(plan, now, now, true, null);
        Equal(expectedIndex, s.StageIndex);
        Equal(expectedIndex < 0 ? 0 : 1, s.Entry);
        return s;
    }
    private static void Tick(ProtectedStageScheduler s, CustomPlan p, double now, bool at = false)
    {
        s.Update(p, now, now, true, i => at);
    }
    private static void AppliedTick(ProtectedStageScheduler s, CustomPlan p, double from, double to)
    {
        s.MarkApplied(from, from);
        Tick(s, p, to);
    }
    private static void Check(bool condition, string message = "Assertion failed")
    {
        _assertions++;
        if (!condition) throw new Exception(message);
    }
    private static void Equal(double expected, double actual)
    {
        Check(Math.Abs(expected - actual) < 0.000001, "Expected " + expected + ", got " + actual);
    }

    private static void Matrix()
    {
        var p = Plan(100, Fixed(35), Flex(50), Fixed(15));
        var s = Start(p);
        Equal(35, s.Durations[0]); Equal(50, s.Durations[1]); Equal(15, s.Durations[2]);
        Check(!s.ProtectedWorkComplete);
        AppliedTick(s, p, 0, 10); Tick(s, p, 20); Tick(s, p, 30); Tick(s, p, 35);
        Equal(1, s.StageIndex); Equal(85, s.ExpectedEndSeconds);
        Tick(s, p, 85);
        Equal(2, s.StageIndex); Equal(100, s.ExpectedEndSeconds); Check(!s.ProtectedWorkComplete);
        AppliedTick(s, p, 85, 100);
        Check(s.ProtectedWorkComplete && s.Finished); Equal(15, s.EarnedSeconds[2]);
        var entry = s.Entry; Tick(s, p, 500); Equal(entry, s.Entry); Equal(2, s.StageIndex);
    }
    private static void FixedFull()
    {
        var p = Plan(200, Fixed(40)); var s = Start(p);
        s.MarkApplied(0, 0);
        s.Update(p, 10, 10, true, null); s.Update(p, 20, 20, true, null);
        Check(!s.Finished); Equal(20, s.EarnedSeconds[0]);
        s.Update(p, 30, 30, true, null); Check(!s.Finished);
        s.Update(p, 40, 40, true, null); Check(s.Finished); Equal(40, s.EarnedSeconds[0]);
    }
    private static void FifteenEdge()
    {
        var p = Plan(100, Fixed(40), Fixed(30)); var s = Start(p);
        AppliedTick(s, p, 0, 15); Tick(s, p, 24);
        s.Suspend(); Tick(s, p, 39); Check(!s.Completed[0]);
        AppliedTick(s, p, 39, 40);
        Equal(1, s.StageIndex); Equal(25, s.EarnedSeconds[0]); Equal(70, s.ExpectedEndSeconds);
        var q = Plan(100, Fixed(40)); var t = Start(q);
        AppliedTick(t, q, 0, 15); Tick(t, q, 23); t.Suspend(); Tick(t, q, 39);
        AppliedTick(t, q, 39, 40);
        Check(!t.Finished); Equal(24, t.EarnedSeconds[0]); // Remaining 16 is not forgiven.
        var r = Plan(100, Fixed(30), Fixed(30)); var u = Start(r);
        AppliedTick(u, r, 0, 15); Tick(u, r, 29); Tick(u, r, 44);
        Equal(1, u.StageIndex); Equal(60, u.ExpectedEndSeconds); // Small lateness is not compounded.
    }
    private static void RestoredResync()
    {
        var p = Plan(100, Fixed(40)); var s = Start(p);
        AppliedTick(s, p, 0, 15); Tick(s, p, 25); Equal(25, s.EarnedSeconds[0]);
        var r = new ProtectedStageScheduler(); Check(r.RestoreState(p, Copy(s.ExportState())));
        Tick(r, p, 500);
        Check(!r.Finished && !r.ProtectedWorkComplete && !r.Completed[0]);
        Equal(0, r.StageIndex); Equal(1, r.Entry); Equal(25, r.EarnedSeconds[0]); Equal(40, r.ExpectedEndSeconds);
        r.MarkApplied(500, 500); Tick(r, p, 500);
        Check(!r.Finished); Equal(25, r.EarnedSeconds[0]); // Applying alone cannot waive the remainder.
        Tick(r, p, 501);
        Check(r.Finished && r.ProtectedWorkComplete && r.Completed[0]);
        Equal(26, r.EarnedSeconds[0]); Equal(40, r.ExpectedEndSeconds); Equal(1, r.Entry);
    }
    private static void InterruptedResync()
    {
        Action<ProtectedStageScheduler, CustomPlan>[] interruptions =
        {
            (s, p) => { s.Suspend(); Tick(s, p, 40); Tick(s, p, 41); },
            (s, p) => { s.Update(p, 40, 40, false, null); Tick(s, p, 41); },
            (s, p) => { Tick(s, p, 41); Tick(s, p, 42); },
            (s, p) => { s.Update(p, 40, 41, true, null); s.Update(p, 41, 42, true, null); },
            (s, p) => s.Update(p, 40, 25, true, null),
            (s, p) => { s.Suspend(); Tick(s, p, 40); s.MarkApplied(40, 40); s.Update(p, 40, 41, true, null); },
            (s, p) => { s.MarkApplied(45, 45); s.Update(p, 44, 46, true, null); },
            (s, p) => { s.MarkApplied(39, 39); s.Update(p, 40, 38, true, null); }
        };
        foreach (var interrupt in interruptions)
        {
            var p = Plan(100, Fixed(40)); var s = Start(p);
            AppliedTick(s, p, 0, 15); Tick(s, p, 25);
            interrupt(s, p);
            Check(!s.Finished && !s.ProtectedWorkComplete && !s.Completed[0]);
            Equal(25, s.EarnedSeconds[0]); Equal(40, s.ExpectedEndSeconds); Equal(0, s.StageIndex); Equal(1, s.Entry);
            s.MarkApplied(600, 600); Tick(s, p, 600); Check(!s.Finished);
            // The coordinator can suspend before each apply without rebasing the planned deadline.
            s.Suspend(); AppliedTick(s, p, 600, 601);
            Check(s.Finished && s.ProtectedWorkComplete); Equal(26, s.EarnedSeconds[0]); Equal(40, s.ExpectedEndSeconds);
        }
    }
    private static void NoShortSkip()
    {
        var p = Plan(5, Fixed(5), Fixed(1)); var s = Start(p, 100);
        Tick(s, p, 101); Check(!s.Finished); Equal(0, s.EarnedSeconds[0]);
        AppliedTick(s, p, 101, 102); Equal(0, s.StageIndex); Check(!s.Finished); Equal(1, s.EarnedSeconds[0]);
        Tick(s, p, 106); Equal(1, s.StageIndex); Check(!s.Finished); Equal(0, s.EarnedSeconds[1]);
        AppliedTick(s, p, 106, 107); Check(s.Finished); Equal(1, s.EarnedSeconds[1]);
    }
    private static void ShortStagesFull()
    {
        foreach (var duration in new[] { 1, 5, 10, 15 })
        {
            var p = Plan(100, Fixed(duration)); var s = Start(p, 100);
            AppliedTick(s, p, 100, 100.25);
            Equal(0.25, s.EarnedSeconds[0]); Check(!s.Finished && !s.ProtectedWorkComplete);
            s.Suspend(); Tick(s, p, 300); Check(!s.Finished);
            s.MarkApplied(300, 300);
            for (var earned = 1d; earned < duration; earned++)
            {
                Tick(s, p, 300 + earned - 0.25);
                Equal(earned, s.EarnedSeconds[0]); Check(!s.Finished);
            }
            Tick(s, p, 300 + duration - 0.25);
            Equal(duration, s.EarnedSeconds[0]); Check(s.Finished && s.ProtectedWorkComplete);
        }
    }
    private static void FirstApply()
    {
        var p = Plan(100, Fixed(50)); var s = Start(p, 500);
        Tick(s, p, 510); Equal(0, s.EarnedSeconds[0]);
        s.MarkApplied(510, 510); Tick(s, p, 520); Equal(10, s.EarnedSeconds[0]);
    }
    private static void Gaps()
    {
        var p = Plan(100, Fixed(100)); var s = Start(p);
        s.MarkApplied(0, 0); s.Update(p, 16, 1, true, null); Equal(0, s.EarnedSeconds[0]);
        s.Update(p, 17, 2, true, null); Equal(0, s.EarnedSeconds[0]);
        s.MarkApplied(17, 2); s.Update(p, 18, 18, true, null); Equal(0, s.EarnedSeconds[0]);
        s.MarkApplied(18, 18); s.Update(p, 33, 33, true, null); Equal(15, s.EarnedSeconds[0]);
    }
    private static void Ownership()
    {
        var p = Plan(100, Fixed(100)); var s = Start(p);
        s.MarkApplied(0, 0); s.Update(p, 10, 10, false, null); Tick(s, p, 20); Equal(0, s.EarnedSeconds[0]);
        s.MarkApplied(20, 20); Tick(s, p, 30); s.Suspend(); Tick(s, p, 40); Equal(10, s.EarnedSeconds[0]);
        AppliedTick(s, p, 40, 50); Equal(20, s.EarnedSeconds[0]);
    }
    private static void NegativeClocks()
    {
        var p = Plan(100, Fixed(100)); var s = Start(p, 20);
        s.MarkApplied(20, 20); s.Update(p, 19, 21, true, null); Equal(0, s.EarnedSeconds[0]);
        s.MarkApplied(19, 21); s.Update(p, 20, 20, true, null); Equal(0, s.EarnedSeconds[0]);
    }
    private static void SlowerClock()
    {
        var p = Plan(100, Fixed(100)); var s = Start(p);
        s.MarkApplied(0, 0); s.Update(p, 15, 3, true, null); Equal(3, s.EarnedSeconds[0]);
        s.Update(p, 18, 18, true, null); Equal(6, s.EarnedSeconds[0]);
    }
    private static void CreditCap()
    {
        var p = Plan(100, Fixed(5), Fixed(20)); var s = Start(p);
        AppliedTick(s, p, 0, 15); Equal(5, s.EarnedSeconds[0]); Equal(1, s.StageIndex); Equal(0, s.EarnedSeconds[1]);
        Tick(s, p, 20); Equal(0, s.EarnedSeconds[1]);
        s.MarkApplied(double.NaN, 20); Tick(s, p, 25); Equal(0, s.EarnedSeconds[1]);
        AppliedTick(s, p, 25, 30); Equal(5, s.EarnedSeconds[1]); Check(s.Finished);
    }
    private static void MandatoryAt()
    {
        var p = Plan(100, At(), Flex(), Fixed(10)); var s = Start(p);
        Tick(s, p, 500); Check(!s.ProtectedWorkComplete && !s.Finished); Equal(0, s.StageIndex);
        Tick(s, p, 501, true); Equal(2, s.StageIndex); Check(!s.Finished && !s.ProtectedWorkComplete);
        Equal(511, s.ExpectedEndSeconds); AppliedTick(s, p, 501, 511);
        Check(s.ProtectedWorkComplete && s.Finished);
    }
    private static void AtBoundary()
    {
        var p = Plan(100, At(), Fixed(30)); var s = Start(p);
        Tick(s, p, 50, true); Equal(80, s.ExpectedEndSeconds); Equal(50, s.EnteredAt);
    }
    private static void OfflineFlex()
    {
        var p = Plan(100, Flex(), Fixed(20)); var s = Start(p);
        Equal(80, s.ExpectedEndSeconds); Tick(s, p, 500);
        Equal(1, s.StageIndex); Equal(0, s.EarnedSeconds[1]); Check(!s.Finished);
        Tick(s, p, 510); Equal(0, s.EarnedSeconds[1]);
    }
    private static void TailReservation()
    {
        var p = Plan(30, Flex(), Fixed(40), Flex(), Fixed(10)); var s = Start(p, 0, 1);
        Equal(0, s.Durations[0]); Check(s.Completed[0]); Tick(s, p, 0); Equal(1, s.StageIndex); Equal(1, s.Entry);
        AppliedTick(s, p, 0, 10); Tick(s, p, 20); Tick(s, p, 30); Tick(s, p, 40);
        Equal(3, s.StageIndex); Equal(2, s.Entry); Check(s.Completed[2] && !s.ProtectedWorkComplete);
        AppliedTick(s, p, 40, 50); Check(s.Finished && s.ProtectedWorkComplete);
    }
    private static void InitialSkip()
    {
        var p = Plan(10, Flex(), Flex(), At(), Fixed(10)); var calls = 0;
        var s = new ProtectedStageScheduler();
        s.Update(p, 0, 0, true, i => { calls++; return true; });
        Equal(2, s.StageIndex); Equal(1, s.Entry); Equal(0, calls);
        Check(s.Completed[0] && s.Completed[1] && !s.Completed[2] && !s.ProtectedWorkComplete);
        Check(new ProtectedStageScheduler().RestoreState(p, Copy(s.ExportState())));
        Tick(s, p, 0, true); Equal(3, s.StageIndex); Equal(2, s.Entry);
        Equal(0, s.EarnedSeconds[3]); Equal(10, s.ExpectedEndSeconds);
        AppliedTick(s, p, 0, 10); Check(s.Finished && s.ProtectedWorkComplete);
    }
    private static void InitialAllFlexEmpty()
    {
        foreach (var now in new[] { 0d, 100d })
        {
            var p = Plan(now == 0 ? 0 : 100, Flex(), Flex()); var s = Start(p, now, -1);
            Check(s.Finished && s.ProtectedWorkComplete && s.Completed[0] && s.Completed[1]);
            s.MarkApplied(now, now); Tick(s, p, now + 1);
            Equal(-1, s.StageIndex); Equal(0, s.Entry); Equal(0, s.StageElapsedSeconds);
            var state = Copy(s.ExportState()); var r = new ProtectedStageScheduler();
            Check(r.RestoreState(p, state)); Check(r.ExportState().ToString() == state.ToString());
            Tick(r, p, now + 2); Equal(-1, r.StageIndex); Equal(0, r.Entry);
            var bad = Copy(state); bad["finished"] = false; Check(!r.RestoreState(p, bad));
            bad = Copy(state); bad["completed"][0] = false; Check(!r.RestoreState(p, bad));
            bad = Copy(state); bad["durations"][0] = 1; Check(!r.RestoreState(p, bad));
        }
    }
    private static void TrailingSkip()
    {
        var p = Plan(10, Fixed(20), Flex()); var s = Start(p);
        AppliedTick(s, p, 0, 10); Tick(s, p, 20);
        Check(s.Finished && s.Completed[1]); Equal(0, s.StageIndex); Equal(1, s.Entry);
        Check(new ProtectedStageScheduler().RestoreState(p, s.ExportState()));
    }
    private static void FrozenFlex()
    {
        var p = Plan(100, Flex(), Flex(), Fixed(20)); var s = Start(p);
        Equal(40, s.ExpectedEndSeconds);
        for (var i = 1; i < 40; i++) { Tick(s, p, i); Equal(40, s.ExpectedEndSeconds); Equal(40, s.Durations[0]); }
        Tick(s, p, 40); Equal(1, s.StageIndex); Equal(80, s.ExpectedEndSeconds);
    }
    private static void FutureFlex()
    {
        var p = Plan(100, Fixed(40), Flex(), Flex(), Fixed(10)); var s = Start(p);
        Tick(s, p, 40); AppliedTick(s, p, 40, 50); Tick(s, p, 60); Tick(s, p, 70);
        Equal(1, s.StageIndex); Equal(10, s.Durations[1]); Equal(80, s.ExpectedEndSeconds);
        Tick(s, p, 85); Equal(2, s.StageIndex); Equal(5, s.Durations[2]); Equal(90, s.ExpectedEndSeconds);
    }
    private static void DurationEdit()
    {
        var p = Plan(100, Fixed(40), Flex()); var s = Start(p);
        AppliedTick(s, p, 0, 10); var entry = s.Entry;
        p.Stages[0].DurationSeconds = 60; Tick(s, p, 10);
        Equal(10, s.EarnedSeconds[0]); Equal(60, s.ExpectedEndSeconds); Equal(entry, s.Entry);
        p.Stages[0].DurationSeconds = 5; Tick(s, p, 10); Equal(1, s.StageIndex);
        p.Stages[0].DurationSeconds = 80; Tick(s, p, 11);
        Check(s.Completed[0] && s.ProtectedWorkComplete); Equal(1, s.StageIndex);
    }
    private static void LateDurationEdit()
    {
        var p = Plan(8000, Fixed(3600), Fixed(120), At(), Fixed(600));
        var s = Start(p, 1448); var entry = s.Entry;
        Tick(s, p, 3120);
        p.Stages[0].DurationSeconds = 2700; Tick(s, p, 3120);
        Equal(0, s.StageIndex); Equal(0, s.EarnedSeconds[0]); Equal(1448, s.EnteredAt);
        Equal(2700, s.ExpectedEndSeconds); Equal(entry, s.Entry);
        AppliedTick(s, p, 3120, 3130);
        Equal(10, s.EarnedSeconds[0]); Equal(0, s.StageIndex);
        p.Stages[0].DurationSeconds = 5; Tick(s, p, 3130);
        Equal(1, s.StageIndex); Equal(3130, s.EnteredAt); Equal(3250, s.ExpectedEndSeconds);
        Equal(0, s.EarnedSeconds[1]); Check(s.Completed[0] && !s.Completed[1]);
        Tick(s, p, 4000);
        Equal(1, s.StageIndex); Equal(0, s.EarnedSeconds[1]);
    }
    private static void NearLimitDurationEdit()
    {
        var p = Plan(8000, At(), Fixed(600)); var s = Start(p, 4000);
        Tick(s, p, 4010, true); AppliedTick(s, p, 4010, 4020);
        var entry = s.Entry;
        p.Stages[1].DurationSeconds = int.MaxValue; Tick(s, p, 4020);
        Equal(1, s.StageIndex); Equal(entry, s.Entry); Equal(4010, s.EnteredAt);
        Equal(10, s.EarnedSeconds[1]); Equal(int.MaxValue, s.Durations[1]);
        Equal((double) int.MaxValue + 4010, s.ExpectedEndSeconds);
        Check(!s.Finished && !s.ProtectedWorkComplete);
    }
    private static void FlexEdit()
    {
        var p = Plan(100, Flex(), Flex(), Fixed(20)); var s = Start(p);
        Tick(s, p, 20); p.Stages[1].Percent = 3; Tick(s, p, 20);
        // Pool 60; active weight 1 * (20/40), future weight 3 => 60/7 remaining.
        Equal(20 + 60d / 7, s.ExpectedEndSeconds); Equal(20 + 60d / 7, s.Durations[0]); Equal(1, s.Entry);
        Tick(s, p, 21); Equal(20 + 60d / 7, s.ExpectedEndSeconds);
    }
    private static void FlexRounding()
    {
        var p = Plan(700, Flex(35), Flex(50), Flex(15)); var s = Start(p);
        Check(s.Durations[0] == 245d); Equal(245, Math.Floor(s.Durations[0]));
        Check(s.Durations[1] == 350d && s.Durations[2] == 105d);
        Tick(s, p, 245); Equal(1, s.StageIndex);
        Equal(350, Math.Floor(s.Durations[1])); Equal(595, Math.Floor(s.ExpectedEndSeconds));
        var large = Start(Plan(100, Flex(1e307), Flex(1e307)));
        Equal(50, large.Durations[0]); Equal(50, large.Durations[1]);
    }
    private static void FlexScaleInvariance()
    {
        foreach (var weight in new[] {1e-100, 1e-308, double.Epsilon, 2 * double.Epsilon})
        {
            var p = Plan(1, Flex(weight), Flex(weight)); var s = Start(p);
            var unitPlan = Plan(1, Flex(), Flex()); var unit = Start(unitPlan);
            foreach (var now in new[] {0d, .25d, .5d, .75d, 1d})
            {
                Tick(s, p, now); Tick(unit, unitPlan, now);
                Equal(unit.StageIndex, s.StageIndex); Equal(unit.Entry, s.Entry);
                Equal(unit.EnteredAt, s.EnteredAt); Equal(unit.ExpectedEndSeconds, s.ExpectedEndSeconds);
                Equal(unit.StageElapsedSeconds, s.StageElapsedSeconds);
                Check(unit.Finished == s.Finished);
                for (var i = 0; i < 2; i++)
                {
                    Equal(unit.Durations[i], s.Durations[i]);
                    Check(unit.Completed[i] == s.Completed[i]);
                }
                if (now == .5d)
                {
                    Equal(1, s.StageIndex); Equal(.5d, s.Durations[1]); Equal(1, s.ExpectedEndSeconds);
                    Check(!s.Finished, "Second flex row must retain its funded half-second at weight " + weight);
                }
            }
            Check(s.Finished && s.Completed[0] && s.Completed[1]);
        }
    }
    private static void HorizonEdit()
    {
        var p = Plan(100, Flex(), Flex()); var s = Start(p);
        Tick(s, p, 50); Equal(1, s.StageIndex);
        p.TimeTriggerSeconds = 200; Tick(s, p, 60);
        Equal(200, s.ExpectedEndSeconds); Check(s.Completed[0]); Equal(1, s.StageIndex); Equal(2, s.Entry);
    }
    private static void CosmeticEdits()
    {
        var p = Plan(100, Fixed(30), Fixed(40)); var s = Start(p);
        AppliedTick(s, p, 0, 10); var key = ProtectedStageScheduler.StructureKey(p);
        p.Stages[0].Name = "renamed"; p.Stages[0].EnergyFocus = 9; p.Stages[0].GearGoal = 8;
        Check(s.Matches(p)); var first = p.Stages[0]; p.Stages[0] = p.Stages[1]; p.Stages[1] = first;
        Check(key == ProtectedStageScheduler.StructureKey(p)); Tick(s, p, 10);
        Equal(10, s.EarnedSeconds[0]); Equal(1, s.Entry); Equal(40, s.ExpectedEndSeconds);
    }
    private static void AdventureWeightEdits()
    {
        foreach (var stage in new[] {Fixed(60), Flex(), At(), new CustomPlanStage {ExecutionMode = 1, DurationSeconds = 60}})
        {
            var p = Plan(200, stage, Flex(), Fixed(20));
            var s = Start(p);
            AppliedTick(s, p, 0, 10);
            var before = Copy(s.ExportState());
            foreach (var value in new[] {0d, 0.125, 1e6})
            {
                p.Stages[0].AdventureDynamicWeight = value;
                p.Stages[1].AdventureDynamicWeight = value;
                Check(s.Matches(p));
                Check(before["structure"].Value == ProtectedStageScheduler.StructureKey(p));
                Tick(s, p, 10);
                var after = s.ExportState();
                Check(after["structure"].Value == before["structure"].Value && after["timing"].Value == before["timing"].Value,
                    "Adventure weight must not enter structural or timing keys");
                Equal(before["entry"].AsDouble, s.Entry); Equal(before["enteredAt"].AsDouble, s.EnteredAt);
                Equal(before["expectedEnd"].AsDouble, s.ExpectedEndSeconds);
                Check(after["durations"].ToString() == before["durations"].ToString());
                Check(after["earned"].ToString() == before["earned"].ToString());
                var restored = new ProtectedStageScheduler();
                Check(restored.RestoreState(p, Copy(before)), "Weight edit must allow restoring the original checkpoint");
                Tick(restored, p, 10);
                Equal(s.Entry, restored.Entry); Equal(s.EnteredAt, restored.EnteredAt);
                Equal(s.ExpectedEndSeconds, restored.ExpectedEndSeconds);
                Check(restored.ExportState()["timing"].Value == before["timing"].Value);
            }
        }
    }

    private static void InvalidAdventureWeights()
    {
        foreach (var index in new[] {0, 1})
        foreach (var value in new[] {-0.01, 1e6 + 0.01, double.NaN, double.PositiveInfinity, double.NegativeInfinity})
        {
            var p = Plan(200, Fixed(100), Flex());
            var s = Start(p);
            AppliedTick(s, p, 0, 10);
            var before = Copy(s.ExportState());
            p.Stages[index].AdventureDynamicWeight = value;
            foreach (var action in new Action[] {
                () => s.Update(p, 11, 11, true, null),
                () => s.OverrideStage(p, "restart", 0, 11, 11)
            })
            {
                var threw = false;
                try { action(); }
                catch (ArgumentException error) { threw = error.Message.Contains("AdventureDynamicWeight"); }
                Check(threw, "Invalid active or future adventure weight must reject before changing scheduler state");
                Check(s.ExportState().ToString() == before.ToString());
            }
            Check(!s.RestoreState(p, Copy(before)), "Checkpoint restore must reject invalid adventure weights");
            Check(s.ExportState().ToString() == before.ToString());
            var fresh = new ProtectedStageScheduler();
            var freshBefore = fresh.ExportState().ToString();
            Check(!fresh.RestoreState(p, Copy(before)) && fresh.ExportState().ToString() == freshBefore);
            p.Stages[index].AdventureDynamicWeight = 0d;
            Tick(s, p, 11);
            Equal(11, s.EarnedSeconds[0]); // Failed validation must not disarm or consume clock time.
        }
    }

    private static void StructuralEdits()
    {
        var p = Plan(100, Fixed(30), At()); var s = Start(p);
        AppliedTick(s, p, 0, 10); p.Stages[1].TargetAdvancedTrainingPower++;
        Check(!s.Matches(p)); Tick(s, p, 10); Equal(0, s.StageIndex); Equal(0, s.EarnedSeconds[0]); Equal(2, s.Entry);
        p.Name = "new"; Tick(s, p, 10); Equal(3, s.Entry);
        p.StageLoopStartIndex = 1; Tick(s, p, 10); Equal(4, s.Entry);
        p.Stages[0].DurationSeconds = 0; Tick(s, p, 10); Equal(5, s.Entry);
    }
    private static void Snapshot()
    {
        var p = Plan(100, At(), Fixed(10)); var s = new ProtectedStageScheduler(); var calls = 0;
        Check(!s.ProtectedWorkComplete); Equal(-1, s.StageIndex);
        s.Update(p, 0, 0, true, i => { calls++; return false; }); Equal(0, calls);
        s.Update(p, 1, 1, true, i => { calls++; return false; }); Equal(1, calls);
        var before = s.ExportState().ToString();
        for (var i = 0; i < 20; i++)
        {
            var durations = s.Durations; durations[1] = 999;
            var earned = s.EarnedSeconds; earned[1] = 999;
            var done = s.Completed; done[0] = true;
            var applied = s.AppliedSeconds; applied[0] = 999;
            var elapsed = s.ElapsedSecondsByStage; elapsed[0] = 999;
            var visits = s.Visits; visits[0] = 999;
            var skipped = s.Skipped; skipped[0] = true;
            Check(s.Matches(p) && !s.ProtectedWorkComplete && !s.Finished);
            Equal(0, s.StageIndex); Equal(1, s.Entry); Equal(0, s.Cycle); Equal(0, s.EnteredAt);
            Equal(1, s.StageElapsedSeconds); Equal(0, s.ExpectedEndSeconds); Check(s.Reason != null);
            Equal(0, s.AppliedSeconds[0]); Equal(1, s.ElapsedSecondsByStage[0]); Equal(1, s.Visits[0]);
            Check(!s.Skipped[0] && s.LastOverride == string.Empty); Equal(0, s.LastOverrideAt);
        }
        Check(before == s.ExportState().ToString()); Equal(1, calls);
    }
    private static JSONObject Copy(JSONObject root) { return (JSONObject) JSONNode.Parse(root.ToString()); }
    private static void Roundtrip()
    {
        var p = Plan(100, Fixed(50), At()); var s = Start(p); AppliedTick(s, p, 0, 10);
        var root = Copy(s.ExportState()); var r = new ProtectedStageScheduler();
        Check(r.RestoreState(p, root)); Check(r.ExportState().ToString() == root.ToString());
        Tick(r, p, 20); Equal(10, r.EarnedSeconds[0]);
        AppliedTick(r, p, 20, 30); Equal(20, r.EarnedSeconds[0]); Equal(1, r.Entry);
    }
    private static void RestoreOffline()
    {
        var p = Plan(100, Flex(), Fixed(20)); var s = Start(p); var r = new ProtectedStageScheduler();
        Check(r.RestoreState(p, Copy(s.ExportState()))); Tick(r, p, 500); Equal(1, r.StageIndex); Equal(0, r.EarnedSeconds[1]);
        var f = new ProtectedStageScheduler(); Check(f.RestoreState(p, Copy(r.ExportState())));
        Tick(f, p, 1000); Equal(0, f.EarnedSeconds[1]); Check(!f.Finished);
    }
    private static void RestoreEdit()
    {
        var p = Plan(100, Flex(), Flex(), Fixed(20)); var s = Start(p); Tick(s, p, 20);
        var root = Copy(s.ExportState()); p.Stages[1].Percent = 3;
        var r = new ProtectedStageScheduler(); Check(r.RestoreState(p, root)); Tick(r, p, 20);
        Equal(20 + 60d / 7, r.ExpectedEndSeconds);
    }
    private static void Malformed()
    {
        var p = Plan(100, Fixed(50), At()); var s = Start(p); AppliedTick(s, p, 0, 10);
        var original = s.ExportState().ToString();
        Action<JSONObject>[] mutations =
        {
            r => r["version"] = 1, r => r["version"] = 3, r => r["stage"] = 0.5, r => r["stage"] = 2,
            r => r["stage"] = "0", r => r["cycle"] = -1, r => r["cycle"] = 0.5,
            r => r["entry"] = "9223372036854775808", r => r["entry"] = 1,
            r => r["lastElapsed"] = double.NaN, r => r["lastHeartbeat"] = double.PositiveInfinity,
            r => r["expectedEnd"] = -1, r => r["enteredAt"] = 11,
            r => r["durations"] = new JSONArray(), r => r["earned"] = new JSONObject(),
            r => r["earned"][0] = double.NegativeInfinity, r => r["earned"][0] = "10",
            r => r["completed"][0] = "false", r => r["completed"][1] = true,
            r => r["structure"] = "wrong", r => r["finished"] = "true",
            r => r["earned"][1] = 1, r => r["durations"][1] = 1,
            r => r.Remove("timing"), r => r.Remove("stageElapsed"),
            r => r["durations"][0] = 0, r => r["durations"][0] = 1.5,
            r => r["stageElapsed"] = 11, r => r["earned"][0] = double.MaxValue,
            r => r.Remove("applied"), r => r["elapsedByStage"] = new JSONArray(),
            r => r["visits"] = new JSONObject(), r => r["skipped"] = new JSONArray(),
            r => r["applied"][0] = double.NaN, r => r["applied"][0] = double.PositiveInfinity,
            r => r["applied"][0] = -1, r => r["applied"][0] = "10", r => r["applied"][0] = 9,
            r => r["elapsedByStage"][0] = -1, r => r["elapsedByStage"][0] = double.PositiveInfinity,
            r => r["elapsedByStage"][0] = "10", r => r["elapsedByStage"][0] = 9,
            r => r["visits"][0] = -1, r => r["visits"][0] = .5,
            r => r["visits"][0] = (double) int.MaxValue + 1, r => r["visits"][0] = "1", r => r["visits"][0] = 0,
            r => r["skipped"][0] = "false", r => r["skipped"][0] = true,
            r => r.Remove("lastOverride"), r => r["lastOverride"] = 1,
            r => r.Remove("lastOverrideAt"), r => r["lastOverrideAt"] = -1,
            r => r["lastOverrideAt"] = double.NaN, r => r["lastOverrideAt"] = 11,
            r => r["lastOverrideAt"] = 1
        };
        foreach (var mutate in mutations)
        {
            var bad = Copy(s.ExportState()); mutate(bad);
            Check(!s.RestoreState(p, bad), "Malformed state was accepted: " + bad);
            Check(s.ExportState().ToString() == original, "Failed restore mutated runtime");
        }
        Check(!s.RestoreState(p, null)); Check(!s.RestoreState(null, s.ExportState()));
        Check(!new ProtectedStageScheduler().RestoreState(p, new ProtectedStageScheduler().ExportState()));
    }
    private static void CompletedFixedRestore()
    {
        var p = Plan(100, Fixed(10)); var s = Start(p);
        var pending = Copy(s.ExportState()); var original = s.ExportState().ToString();
        var bad = Copy(pending);
        bad["finished"] = true;
        bad["completed"][0] = true;
        Equal(0, bad["earned"][0].AsDouble); Equal(0, bad["stageElapsed"].AsDouble);
        Check(!s.RestoreState(p, bad), "Combined completion flags cannot fabricate completed fixed work with zero credit");
        Check(s.ExportState().ToString() == original, "Rejected completion corruption must not mutate the scheduler");
        Check(!s.Finished && !s.ProtectedWorkComplete && !s.Completed[0]);
        var r = new ProtectedStageScheduler();
        Check(!r.RestoreState(p, bad)); Check(!r.ProtectedWorkComplete); Equal(-1, r.StageIndex);
        Check(r.RestoreState(p, pending), "A pending fixed row may legitimately have zero earned time");

        AppliedTick(s, p, 0, 10);
        Check(s.Finished && s.ProtectedWorkComplete && s.Completed[0]); Equal(10, s.EarnedSeconds[0]);
        var completed = Copy(s.ExportState());
        Check(r.RestoreState(p, completed), "Credited fixed completion must roundtrip normally");
        Check(r.ExportState().ToString() == completed.ToString());
        Check(r.Finished && r.ProtectedWorkComplete && r.Completed[0]);

        p.Stages[0].DurationSeconds = 20; Tick(s, p, 11);
        Equal(10, s.EarnedSeconds[0]); Equal(20, s.Durations[0]);
        var edited = Copy(s.ExportState());
        Check(r.RestoreState(p, edited), "Later duration edits must not invalidate legitimately completed fixed work");
        Check(r.ExportState().ToString() == edited.ToString());
        Check(r.Finished && r.ProtectedWorkComplete && r.Completed[0]);
        Equal(10, r.EarnedSeconds[0]); Equal(20, r.Durations[0]);
    }
    private static void Loops()
    {
        var p = Plan(55, Fixed(10), Fixed(10), Fixed(10)); p.StageLoopStartIndex = 1; var s = Start(p);
        AppliedTick(s, p, 0, 10); AppliedTick(s, p, 10, 20); AppliedTick(s, p, 20, 30);
        Equal(1, s.Cycle); Equal(1, s.StageIndex); Equal(10, s.EarnedSeconds[0]); Equal(0, s.EarnedSeconds[1]);
        Check(s.Completed[0] && !s.ProtectedWorkComplete);
        AppliedTick(s, p, 30, 40); AppliedTick(s, p, 40, 50);
        Equal(2, s.Cycle); Equal(1, s.StageIndex);
        AppliedTick(s, p, 50, 60); AppliedTick(s, p, 60, 70);
        Check(s.Finished && s.ProtectedWorkComplete); Equal(2, s.Cycle); Equal(2, s.StageIndex);
        Check(new ProtectedStageScheduler().RestoreState(p, Copy(s.ExportState())));
        var entry = s.Entry; Tick(s, p, 500); Equal(entry, s.Entry);
    }
    private static void LoopOverrun()
    {
        var p = Plan(15, Fixed(10), At(), Fixed(10)); p.StageLoopStartIndex = 1; var s = Start(p);
        AppliedTick(s, p, 0, 10); Tick(s, p, 100); Check(!s.Finished && !s.ProtectedWorkComplete);
        Tick(s, p, 101, true); Equal(2, s.StageIndex); Equal(0, s.Cycle);
        AppliedTick(s, p, 101, 111); Check(s.Finished && s.ProtectedWorkComplete); Equal(0, s.Cycle);
    }
    private static void RepeatAdmission()
    {
        var p = Plan(100, Fixed(10), At(), Fixed(10)); p.StageLoopStartIndex = 0; var s = Start(p);
        Check(s.AllowRepeat); AppliedTick(s, p, 0, 10); s.AllowRepeat = false;
        Tick(s, p, 11); Check(!s.Finished && !s.ProtectedWorkComplete);
        Tick(s, p, 12, true); Equal(2, s.StageIndex); Check(!s.Finished);
        AppliedTick(s, p, 12, 22); Check(s.Finished && s.ProtectedWorkComplete);
        Equal(0, s.Cycle); Equal(3, s.Entry); Equal(10, s.EarnedSeconds[0]); Equal(10, s.EarnedSeconds[2]);
        s.AllowRepeat = true; Tick(s, p, 23); Check(s.Finished); Equal(0, s.Cycle);

        var q = Plan(100, Fixed(10), Fixed(10)); q.StageLoopStartIndex = 1; var r = Start(q);
        AppliedTick(r, q, 0, 10); AppliedTick(r, q, 10, 20); Equal(1, r.Cycle);
        r.AllowRepeat = false; AppliedTick(r, q, 20, 30);
        Check(r.Finished && r.ProtectedWorkComplete); Equal(1, r.Cycle); Equal(3, r.Entry);

        var t = new ProtectedStageScheduler { AllowRepeat = false };
        var at = Plan(100, At()); at.StageLoopStartIndex = 0;
        t.Update(at, 0, 0, true, i => true); Check(!t.AllowRepeat && !t.Finished);
        Tick(t, at, 1, true); Check(t.Finished && t.ProtectedWorkComplete); Equal(0, t.Cycle);
    }
    private static void RepeatRestore()
    {
        var p = Plan(100, Fixed(10)); p.StageLoopStartIndex = 0; var s = Start(p);
        var before = s.ExportState().ToString(); s.AllowRepeat = false;
        Check(s.ExportState().ToString() == before);
        var r = new ProtectedStageScheduler { AllowRepeat = false };
        Check(!r.RestoreState(p, null) && !r.AllowRepeat);
        Check(r.RestoreState(p, Copy(s.ExportState())) && r.AllowRepeat);
        AppliedTick(r, p, 0, 10); Equal(1, r.Cycle); Check(!r.Finished);
        r.AllowRepeat = false; AppliedTick(r, p, 10, 20); Check(r.Finished); Equal(1, r.Cycle);
    }
    private static void AtLoop()
    {
        var p = Plan(100, At()); p.StageLoopStartIndex = 0; var s = Start(p);
        var calls = 0; s.Update(p, 0, 0, true, i => { calls++; return true; });
        Check(calls == 1 && !s.Finished && !s.ProtectedWorkComplete); Equal(1, s.Cycle);
        var r = new ProtectedStageScheduler(); Check(r.RestoreState(p, Copy(s.ExportState())));
        Tick(r, p, 100, true); Check(r.Finished && r.ProtectedWorkComplete); Equal(1, r.Cycle);
    }
    private static void AllFlex()
    {
        var p = Plan(100, Flex(35), Flex(50), Flex(15)); p.TimeTriggerEnabled = false; p.TimeTriggerSeconds = 1;
        var s = Start(p); Check(s.ProtectedWorkComplete); Equal(35, s.Durations[0]); Equal(50, s.Durations[1]); Equal(15, s.Durations[2]);
        Tick(s, p, 100); Check(s.Finished && s.ProtectedWorkComplete); Equal(0, s.StageIndex);
        p.TimeTriggerEnabled = true; p.TimeTriggerSeconds = 0; var r = Start(p); Equal(35, r.ExpectedEndSeconds);
    }
    private static void ManualSkipAt()
    {
        var p = Plan(100, At(), Fixed(10)); var s = new ProtectedStageScheduler(); var calls = 0;
        s.Update(p, 0, 0, true, i => { calls++; return false; });
        var entry = s.Entry;
        s.OverrideStage(p, "skip", 0, 5, 5);
        Equal(0, calls); Equal(1, s.StageIndex); Equal(entry + 1, s.Entry);
        Check(s.Skipped[0] && s.Completed[0] && !s.ProtectedWorkComplete && !s.Finished);
        Equal(0, s.EarnedSeconds[0]); Equal(0, s.AppliedSeconds[0]); Equal(5, s.ElapsedSecondsByStage[0]);
        Equal(1, s.Visits[0]); Equal(1, s.Visits[1]); Equal(5, s.EnteredAt); Equal(15, s.ExpectedEndSeconds);
        Check(s.LastOverride == "skip stage 1 at 5"); Equal(5, s.LastOverrideAt);
        Tick(s, p, 10); Equal(0, s.EarnedSeconds[1]);
        AppliedTick(s, p, 10, 20); Check(s.ProtectedWorkComplete && s.Finished);
        Equal(0, s.EarnedSeconds[0]); Equal(10, s.EarnedSeconds[1]);
    }
    private static void ManualSkipUnfunded()
    {
        var p = Plan(5, At(), Flex(), Fixed(10), Flex()); var s = Start(p);
        s.OverrideStage(p, "skip", 0, 1, 1);
        Equal(2, s.StageIndex); Equal(2, s.Entry);
        Check(s.Skipped[0] && s.Completed[0] && s.Completed[1] && !s.Skipped[1]);
        Equal(0, s.Visits[1]); Equal(1, s.Visits[2]);
        s.OverrideStage(p, "skip", 2, 6, 6);
        Check(s.Finished && s.ProtectedWorkComplete && s.Completed[3] && !s.Skipped[3]);
        Equal(2, s.StageIndex); Equal(3, s.Entry); Equal(0, s.Visits[3]); Equal(0, s.EarnedSeconds[2]);
        Check(new ProtectedStageScheduler().RestoreState(p, Copy(s.ExportState())));
        p.TimeTriggerSeconds = 20; Tick(s, p, 7);
        s.OverrideStage(p, "skip", 2, 8, 8);
        Check(s.Finished && s.ProtectedWorkComplete); Equal(2, s.StageIndex); Equal(4, s.Entry); Equal(0, s.Visits[3]);
        Check(new ProtectedStageScheduler().RestoreState(p, Copy(s.ExportState())));
        var funded = Plan(5, Fixed(10), Flex()); var t = Start(funded);
        t.OverrideStage(funded, "skip", 0, 2, 2);
        Equal(1, t.StageIndex); Equal(3, t.Durations[1]); Equal(1, t.Visits[1]); Check(!t.Finished);
    }
    private static void ManualLastSkip()
    {
        var p = Plan(100, Fixed(10)); p.StageLoopStartIndex = 0; var s = Start(p);
        s.OverrideStage(p, "skip", 0, 2, 2);
        Equal(0, s.StageIndex); Equal(2, s.Entry); Equal(0, s.Cycle); Equal(1, s.Visits[0]);
        Check(s.Finished && s.ProtectedWorkComplete && s.Completed[0] && s.Skipped[0]);
        Equal(0, s.EarnedSeconds[0]); Equal(0, s.AppliedSeconds[0]); Equal(2, s.ElapsedSecondsByStage[0]);
        AppliedTick(s, p, 2, 7);
        Equal(5, s.AppliedSeconds[0]); Equal(7, s.ElapsedSecondsByStage[0]); Equal(0, s.EarnedSeconds[0]);
        Equal(2, s.Entry); Equal(0, s.Cycle);
        var r = new ProtectedStageScheduler(); Check(r.RestoreState(p, Copy(s.ExportState())));
        Check(r.ProtectedWorkComplete && r.Skipped[0]);
        s.OverrideStage(p, "restart", 0, 7, 7);
        Equal(3, s.Entry); Equal(2, s.Visits[0]);
        Check(!s.Finished && !s.ProtectedWorkComplete && !s.Completed[0] && !s.Skipped[0]);
        Equal(5, s.AppliedSeconds[0]); Equal(7, s.ElapsedSecondsByStage[0]);
    }
    private static void ManualForwardSet()
    {
        var p = Plan(100, Fixed(2), At(), Fixed(10), Fixed(5)); var s = Start(p);
        AppliedTick(s, p, 0, 2);
        s.MarkApplied(2, 2);
        var entry = s.Entry;
        s.OverrideStage(p, "set", 3, 4, 4);
        Equal(entry + 1, s.Entry); Equal(3, s.StageIndex); Equal(4, s.EnteredAt);
        Check(s.Completed[0] && !s.Skipped[0]); Equal(2, s.EarnedSeconds[0]);
        Check(s.Completed[1] && s.Skipped[1] && s.Completed[2] && s.Skipped[2]);
        Equal(0, s.EarnedSeconds[1]); Equal(0, s.EarnedSeconds[2]); Equal(0, s.Visits[2]);
        Equal(2, s.AppliedSeconds[1]); Equal(0, s.AppliedSeconds[2]);
        Check(!s.Completed[3] && !s.Skipped[3] && !s.ProtectedWorkComplete);
        Tick(s, p, 5); Equal(0, s.EarnedSeconds[3]);
        AppliedTick(s, p, 5, 10); Check(s.Finished && s.ProtectedWorkComplete);
        var r = new ProtectedStageScheduler(); Check(r.RestoreState(p, Copy(s.ExportState())));
        Check(r.Skipped[1] && r.Skipped[2] && !r.Skipped[0]); Equal(0, r.Visits[2]);
    }
    private static void ManualBackwardSet()
    {
        var p = Plan(100, Fixed(5), At(), Fixed(5)); var s = Start(p);
        AppliedTick(s, p, 0, 5); s.OverrideStage(p, "skip", 1, 6, 6);
        AppliedTick(s, p, 6, 11); Check(s.Finished && s.ProtectedWorkComplete);
        var entry = s.Entry;
        s.OverrideStage(p, "set", 0, 12, 12);
        Equal(entry + 1, s.Entry); Equal(0, s.StageIndex); Equal(12, s.EnteredAt); Equal(17, s.ExpectedEndSeconds);
        Check(!s.Finished && !s.ProtectedWorkComplete);
        for (var i = 0; i < 3; i++)
        {
            Check(!s.Completed[i] && !s.Skipped[i]); Equal(0, s.EarnedSeconds[i]);
        }
        Equal(5, s.AppliedSeconds[0]); Equal(5, s.AppliedSeconds[2]); Equal(1, s.ElapsedSecondsByStage[1]);
        Equal(2, s.Visits[0]); Equal(1, s.Visits[1]); Equal(1, s.Visits[2]);
        Tick(s, p, 13); Equal(0, s.EarnedSeconds[0]);
        var r = new ProtectedStageScheduler(); Check(r.RestoreState(p, Copy(s.ExportState())));
        Check(!r.ProtectedWorkComplete); Equal(0, r.StageIndex);
    }
    private static void ManualRestart()
    {
        var p = Plan(100, Fixed(5), At()); var s = Start(p); s.MarkApplied(0, 0);
        s.OverrideStage(p, "restart", 0, 10, 10);
        Equal(0, s.StageIndex); Equal(2, s.Entry); Equal(2, s.Visits[0]); Equal(0, s.Visits[1]);
        Equal(10, s.AppliedSeconds[0]); Equal(10, s.ElapsedSecondsByStage[0]); Equal(0, s.EarnedSeconds[0]);
        Equal(10, s.EnteredAt); Equal(15, s.ExpectedEndSeconds); Equal(0, s.StageElapsedSeconds);
        Check(!s.Completed[0] && !s.Skipped[0] && !s.ProtectedWorkComplete);
        Tick(s, p, 11); Equal(0, s.EarnedSeconds[0]);
        var entry = s.Entry;
        p.Stages[0].DurationSeconds = 6; Tick(s, p, 11);
        Equal(entry, s.Entry); Equal(16, s.ExpectedEndSeconds); Equal(10, s.AppliedSeconds[0]);
        AppliedTick(s, p, 11, 15); Equal(4, s.EarnedSeconds[0]); Equal(14, s.AppliedSeconds[0]);
        s.OverrideStage(p, "set", 0, 15, 15);
        Equal(entry + 1, s.Entry); Equal(0, s.EarnedSeconds[0]); Equal(14, s.AppliedSeconds[0]);
    }
    private static void ManualTargetEvaluation()
    {
        var p = Plan(100, Fixed(5), At(), At(), Fixed(5)); var s = new ProtectedStageScheduler(); var calls = 0;
        Func<int, bool> reached = i => { calls++; return true; };
        s.Update(p, 0, 0, true, reached);
        s.OverrideStage(p, "set", 1, 10, 10);
        Equal(0, calls); Equal(1, s.StageIndex); Equal(0, s.Visits[2]);
        s.OverrideStage(p, "restart", 1, 11, 11);
        Equal(0, calls); Equal(1, s.StageIndex);
        s.Update(p, 12, 12, true, reached);
        Equal(2, calls); Equal(3, s.StageIndex); Equal(1, s.Visits[2]);
        Check(!s.Skipped[1] && !s.Skipped[2] && s.Completed[1] && s.Completed[2]);
        Check(s.Skipped[0] && !s.ProtectedWorkComplete); Equal(0, s.EarnedSeconds[3]);
    }
    private static void InvalidOverrides()
    {
        var p = Plan(100, Fixed(10), At()); var s = Start(p, 10); AppliedTick(s, p, 10, 12);
        var other = Plan(100, Fixed(10), At()); other.Name = "other";
        Action[] invalid = {
            () => s.OverrideStage(p, null, 0, 15, 15), () => s.OverrideStage(p, "SKIP", 0, 15, 15),
            () => s.OverrideStage(p, "", 0, 15, 15), () => s.OverrideStage(p, "next", 0, 15, 15),
            () => s.OverrideStage(p, "skip", 1, 15, 15), () => s.OverrideStage(p, "restart", 1, 15, 15),
            () => s.OverrideStage(p, "set", -1, 15, 15), () => s.OverrideStage(p, "set", 2, 15, 15),
            () => s.OverrideStage(null, "skip", 0, 15, 15), () => s.OverrideStage(other, "skip", 0, 15, 15),
            () => s.OverrideStage(p, "skip", 0, double.NaN, 15), () => s.OverrideStage(p, "skip", 0, 15, double.NaN),
            () => s.OverrideStage(p, "skip", 0, double.PositiveInfinity, 15), () => s.OverrideStage(p, "skip", 0, 15, double.PositiveInfinity),
            () => s.OverrideStage(p, "skip", 0, 11, 15), () => s.OverrideStage(p, "skip", 0, 15, 11),
            () => s.OverrideStage(p, "skip", 0, -1, 15), () => s.OverrideStage(p, "skip", 0, 15, -1)
        };
        foreach (var action in invalid)
        {
            var state = s.ExportState().ToString(); var failed = false;
            try { action(); } catch (ArgumentException) { failed = true; } catch (InvalidOperationException) { failed = true; }
            Check(failed); Check(state == s.ExportState().ToString(), "Invalid override changed state");
        }
        Tick(s, p, 13); Equal(3, s.EarnedSeconds[0]); Equal(3, s.AppliedSeconds[0]);
        var fresh = new ProtectedStageScheduler(); var before = fresh.ExportState().ToString(); var rejected = false;
        try { fresh.OverrideStage(p, "set", 0, 0, 0); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected && before == fresh.ExportState().ToString());
    }
    private static void AllKindHistory()
    {
        var p = Plan(30, Fixed(5), Flex(), At(), Fixed(5)); var s = Start(p);
        AppliedTick(s, p, 0, 5); AppliedTick(s, p, 5, 10);
        Tick(s, p, 100); Equal(2, s.StageIndex);
        AppliedTick(s, p, 100, 105); s.OverrideStage(p, "skip", 2, 107, 107);
        AppliedTick(s, p, 107, 112); Check(s.Finished && s.ProtectedWorkComplete);
        AppliedTick(s, p, 112, 117); s.Suspend(); Tick(s, p, 120);
        var applied = new[] {5d, 5d, 7d, 10d}; var wall = new[] {5d, 95d, 7d, 13d};
        for (var i = 0; i < 4; i++)
        {
            Equal(applied[i], s.AppliedSeconds[i]); Equal(wall[i], s.ElapsedSecondsByStage[i]); Equal(1, s.Visits[i]);
        }
        Equal(5, s.EarnedSeconds[0]); Equal(0, s.EarnedSeconds[1]); Equal(0, s.EarnedSeconds[2]); Equal(5, s.EarnedSeconds[3]);
        Check(s.Skipped[2] && !s.Skipped[0] && !s.Skipped[1] && !s.Skipped[3]);
        var r = new ProtectedStageScheduler(); Check(r.RestoreState(p, Copy(s.ExportState())));
        Tick(r, p, 125); Equal(10, r.AppliedSeconds[3]); Equal(18, r.ElapsedSecondsByStage[3]);
        r.MarkApplied(126, 126); Equal(19, r.ElapsedSecondsByStage[3]);
        r.Update(p, 130, 128, true, null); Equal(12, r.AppliedSeconds[3]); Equal(23, r.ElapsedSecondsByStage[3]);
    }
    private static void LoopHistory()
    {
        var p = Plan(100, Fixed(2), At(), Fixed(2)); p.StageLoopStartIndex = 1; var s = Start(p);
        AppliedTick(s, p, 0, 2); s.OverrideStage(p, "skip", 1, 2, 2); AppliedTick(s, p, 2, 4);
        Equal(1, s.Cycle); Equal(1, s.StageIndex); Equal(2, s.Visits[1]);
        Check(s.Completed[0] && !s.Completed[1] && !s.Skipped[1] && !s.ProtectedWorkComplete);
        Equal(2, s.AppliedSeconds[0]); Equal(2, s.AppliedSeconds[2]); Equal(0, s.EarnedSeconds[2]);
        s.OverrideStage(p, "set", 0, 5, 5);
        Equal(1, s.Cycle); Equal(0, s.StageIndex); Equal(2, s.Visits[0]); Equal(1, s.ElapsedSecondsByStage[1]);
        var r = new ProtectedStageScheduler(); Check(r.RestoreState(p, Copy(s.ExportState())));
        AppliedTick(s, p, 5, 7); s.OverrideStage(p, "skip", 1, 7, 7); AppliedTick(s, p, 7, 9);
        Equal(2, s.Cycle); Equal(4, s.AppliedSeconds[0]); Equal(4, s.AppliedSeconds[2]);
        Equal(4, s.Visits[1]); Equal(2, s.Visits[2]); Check(!s.Skipped[1]);
        Check(new ProtectedStageScheduler().RestoreState(p, Copy(s.ExportState())));
    }
    private static void OverrideRoundtrip()
    {
        var p = Plan(100, Fixed(10), At(), Fixed(10)); var s = Start(p);
        AppliedTick(s, p, 0, 3); s.OverrideStage(p, "set", 2, 5, 5); AppliedTick(s, p, 5, 7);
        var root = Copy(s.ExportState()); Equal(2, root["version"].AsInt);
        var r = new ProtectedStageScheduler(); Check(r.RestoreState(p, root));
        Check(r.ExportState().ToString() == root.ToString());
        Check(r.LastOverride == "set stage 3 at 5"); Equal(5, r.LastOverrideAt);
        Equal(5, r.EarnedSeconds[0]); Equal(0, r.EarnedSeconds[1]); Equal(2, r.EarnedSeconds[2]);
        Check(r.Skipped[0] && r.Skipped[1] && !r.ProtectedWorkComplete);
        Tick(r, p, 9); Equal(2, r.EarnedSeconds[2]); Equal(2, r.AppliedSeconds[2]); Equal(4, r.ElapsedSecondsByStage[2]);
        AppliedTick(r, p, 9, 17); Check(r.ProtectedWorkComplete && r.Finished);
        var legacy = Copy(root); legacy["version"] = 1;
        var before = r.ExportState().ToString(); Check(!r.RestoreState(p, legacy)); Check(before == r.ExportState().ToString());
    }
    private static void ManualZeroSelection()
    {
        var p = Plan(0, Flex(), Flex()); var s = Start(p, 0, -1);
        s.OverrideStage(p, "set", 0, 0, 0);
        Equal(0, s.StageIndex); Equal(1, s.Entry); Equal(1, s.Visits[0]);
        Check(!s.Finished && !s.Completed[0] && !s.Completed[1] && s.ProtectedWorkComplete);
        Check(new ProtectedStageScheduler().RestoreState(p, Copy(s.ExportState())));
        Tick(s, p, 1); Check(s.Finished && s.Completed[0] && s.Completed[1]); Equal(0, s.StageIndex);
        Equal(1, s.Visits[0]); Equal(0, s.Visits[1]);
        s.OverrideStage(p, "restart", 0, 2, 2); Equal(2, s.Entry); Equal(2, s.Visits[0]); Check(!s.Finished);
        s.OverrideStage(p, "skip", 0, 3, 3); Equal(3, s.Entry); Check(s.Finished && s.Skipped[0] && !s.Skipped[1]);
        Check(new ProtectedStageScheduler().RestoreState(p, Copy(s.ExportState())));
        var empty = Start(p, 0, -1); var before = empty.ExportState().ToString(); var failed = false;
        try { empty.OverrideStage(p, "skip", 0, 0, 0); } catch (ArgumentOutOfRangeException) { failed = true; }
        Check(failed && before == empty.ExportState().ToString());
    }
    private static void HistoryContinuity()
    {
        foreach (var stage in new[] {Fixed(1000), Flex(), At()})
        {
            var p = Plan(10000, stage); var s = Start(p);
            Tick(s, p, 10); Equal(0, s.AppliedSeconds[0]); Equal(10, s.ElapsedSecondsByStage[0]);
            s.MarkApplied(12, 12); Equal(12, s.ElapsedSecondsByStage[0]);
            s.Update(p, 15, 13, true, null); Equal(1, s.AppliedSeconds[0]);
            s.Update(p, 18, 16, false, null); s.Update(p, 20, 18, true, null); Equal(1, s.AppliedSeconds[0]);
            s.MarkApplied(20, 18); s.Update(p, 36, 19, true, null); s.Update(p, 37, 20, true, null);
            Equal(1, s.AppliedSeconds[0]);
            s.MarkApplied(37, 20); s.Update(p, 38, 36, true, null); Equal(1, s.AppliedSeconds[0]);
            s.MarkApplied(38, 36); s.Update(p, 39, 37, true, null); Equal(2, s.AppliedSeconds[0]);
            s.Suspend(); s.Update(p, 40, 38, true, null); Equal(2, s.AppliedSeconds[0]); Equal(40, s.ElapsedSecondsByStage[0]);
            Equal(stage.DurationSeconds > 0 ? 2 : 0, s.EarnedSeconds[0]);
            Check(new ProtectedStageScheduler().RestoreState(p, Copy(s.ExportState())));
        }
    }
    private static void FinishedKindHistory()
    {
        foreach (var stage in new[] {Fixed(5), Flex(), At()})
        {
            var p = Plan(5, stage); var s = Start(p); s.MarkApplied(0, 0);
            Tick(s, p, 5, true); Check(s.Finished && s.ProtectedWorkComplete);
            Equal(5, s.AppliedSeconds[0]); Equal(5, s.ElapsedSecondsByStage[0]);
            s.MarkApplied(5, 5); Tick(s, p, 10, true);
            Equal(10, s.AppliedSeconds[0]); Equal(10, s.ElapsedSecondsByStage[0]); Equal(1, s.Visits[0]);
            Equal(stage.DurationSeconds > 0 ? 5 : 0, s.EarnedSeconds[0]);
            Check(s.Finished && s.Completed[0] && !s.Skipped[0]);
            Check(new ProtectedStageScheduler().RestoreState(p, Copy(s.ExportState())));
        }
    }
    private static void ManualExpiredFlex()
    {
        var p = Plan(10, Flex(), At()); var s = Start(p); s.MarkApplied(0, 0);
        s.OverrideStage(p, "restart", 0, 100, 100);
        Equal(0, s.StageIndex); Equal(2, s.Entry); Equal(2, s.Visits[0]); Equal(0, s.Visits[1]);
        Equal(100, s.ElapsedSecondsByStage[0]); Equal(0, s.AppliedSeconds[0]);
        Check(!s.Completed[0] && !s.Completed[1] && !s.Finished && !s.ProtectedWorkComplete);
        var r = new ProtectedStageScheduler(); Check(r.RestoreState(p, Copy(s.ExportState())));
        var calls = 0; s.Update(p, 101, 101, true, i => { calls++; return false; });
        Equal(1, s.StageIndex); Equal(1, calls); Equal(101, s.ElapsedSecondsByStage[0]);
        Equal(0, s.AppliedSeconds[0]); Equal(1, s.Visits[1]);
    }
    private static void InvalidInputs()
    {
        var s = new ProtectedStageScheduler(); var p = Plan(100, Flex());
        Action[] invalid =
        {
            () => s.Update(null, 0, 0, false, null),
            () => s.Update(Plan(100), 0, 0, false, null),
            () => s.Update(p, double.NaN, 0, false, null),
            () => s.Update(p, 0, double.PositiveInfinity, false, null),
            () => s.Update(Plan(100, Flex(double.NaN)), 0, 0, false, null),
            () => s.Update(Plan(100, Fixed(-1)), 0, 0, false, null)
        };
        foreach (var action in invalid)
        {
            var threw = false; try { action(); } catch (ArgumentException) { threw = true; }
            Check(threw);
        }
    }
}
