using System;
using System.Linq;
using static NativePolicyFixture;

public static class TerminalHoldIntegrationChecks
{
    public static void Run()
    {
        var assertions = 0;
        void Assert(bool condition, string label)
        {
            assertions++;
            if (!condition) throw new Exception(label);
        }
        using (var f = new NativePolicyFixture(4, 0))
        {
            var c = f.Character;
            Assert(ReferenceEquals(c.inventoryController, Get(Production.GetType("NGUInjector.Main", true), "Controller")), "seed preserves native/static controller identity");
            Assert(c.challenges.timeMachineChallenge != null && c.challenges.trollChallenge != null, "seed preserves prepared support dependencies");
            Assert(c.wishes.wishes[190].level == 0 && c.advancedTraining.levelTarget.All(value => value == 0), "terminal W fixture permits paid AT, not passive/disabled training");
            Set(f.Plan, "StageDurationSeconds", 4);
            var stage = f.Stage(); Set(stage, "EnergyFocus", 2); Set(stage, "EnergyPriorities", new[] {"AUG"});
            var trailing = f.Stage(1); Set(trailing, "EnergyFocus", 5); Set(trailing, "EnergyPriorities", new[] {"NGU"});
            c.augments.augs[0].augmentTarget = c.augments.augs[0].upgradeTarget = 0;
            f.Fixture("Setting", "CustomAugmentsBloodEstimateSeconds", 17);
            f.Fixture("Run", 0d);
            Assert(c.augments.augs[0].augEnergy > 0, "compiled coordinator initially funds AUG before unfunded trailing row; " + f.Fixture("Get", "_inlineStageSetupError") + "; " + f.Fixture("Get", "_resourceExecutionReason"));
            Assert(Get<bool>(Production.GetType("NGUInjector.Managers.CustomStageAllocator", true), "LastStageApplicationSucceeded"), "actual application succeeds before credit");
            var entry = f.Fixture("Get", "_customStageKey");
            var runningState = f.Fixture("CoreState");
            var expired = Static("CustomStageAllocator", "ResolveStageResources", f.Plan, stage, 0, "unfinished", 4);
            Assert(PolicyChecks.DesiredAugments(expired) == 0, "unfinished nonfinal zero-time stage cannot enable terminal holding");
            Assert(Equals(runningState, f.Fixture("CoreState")), "expired policy resolution grants no scheduler credit");
            // Resolution invalidates application evidence. Reapply the CURRENT
            // entry through its real coordinator before asking it to earn time.
            f.Fixture("Run", 0d);
            Assert(Equals(entry, f.Fixture("Get", "_customStageKey"))
                && Get<bool>(Production.GetType("NGUInjector.Managers.CustomStageAllocator", true), "LastStageApplicationSucceeded"), "reapplying after read-only negative decision reacquires evidence, not a new entry");
            f.Fixture("RunQuick", 2d); f.Fixture("RunQuick", 2d);
            Assert(Get<bool>(f.Fixture("Snapshot"), "ScheduleComplete"), "coordinator completes four actual applied seconds");
            Assert(Get<int>(f.Fixture("Start"), "RemainingSeconds") == 0, "completed coordinator really supplies zero remaining seconds");
            Assert(Get<int>(f.Fixture("Snapshot"), "ActiveStageIndex") == 0, "coordinator holds last actual allocation, not physical final row");
            var core = f.Fixture("Get", "_protectedScheduler");
            Assert(Get<double[]>(core, "Durations")[1] == 0, "horizon leaves trailing flex row unfunded");
            Assert(Get<bool[]>(core, "Completed")[1] && Get<int[]>(core, "Visits")[1] == 0, "unfunded trailing row is skipped without allocation entry");
            for (var pass = 0; pass < 3; pass++)
            {
                f.Fixture("RunQuick", 2d);
                Assert(Get<int>(f.Fixture("Start"), "RemainingSeconds") == 0, "quick hold retains exhausted duration " + pass);
                Assert(c.augments.augs[0].augEnergy > 0 && c.augments.augs[0].upgradeEnergy > 0, "zero-remaining quick pass still funds AUG " + pass);
                Assert(Equals(entry, f.Fixture("Get", "_customStageKey")), "holding never fabricates new entry");
                Assert(c.advancedTraining.energy.All(amount => amount == 0), "holding never opens AT preparation");
                Assert(!(bool) f.Fixture("Rebirth", "PeekRebirthAvailable"), "completion does not enable disabled automatic rebirth");
                Assert(!(bool) f.Fixture("Get", "_manualRebirthPending") && !(bool) f.Fixture("Get", "_customPreRebirthPrepRunning"), "quick hold never launches rebirth preparation");
            }
            var state = f.Fixture("CoreState"); f.Fixture("Snapshot");
            Assert(Equals(state, f.Fixture("CoreState")), "holding snapshot is read-only on actual coordinator");
            Assert(!Get<bool>(Optimize(0, 1000), "Success"), "direct zero-time optimizer still rejects expired request");
            Set(f.Plan, "StageSchedulerEnabled", false);
            expired = Static("CustomStageAllocator", "ResolveStageResources", f.Plan, stage, 0, "disabled", 4);
            Assert(PolicyChecks.DesiredAugments(expired) == 0, "nonscheduled request cannot inherit terminal estimate");
            Set(f.Plan, "StageSchedulerEnabled", true);
            var other = NewStage(); Set(other, "EnergyPriorities", new[] {"AUG"});
            expired = Static("CustomStageAllocator", "ResolveStageResources", f.Plan, other, 0, "unrelated", 4);
            Assert(PolicyChecks.DesiredAugments(expired) == 0, "different stage object cannot borrow actual completed entry estimate");
            Set(trailing, "EnergyPriorities", new[] {"AUG"});
            expired = Static("CustomStageAllocator", "ResolveStageResources", f.Plan, trailing, 0, "wrong-active-row", 4);
            Assert(PolicyChecks.DesiredAugments(expired) == 0, "different active row cannot borrow completed stage estimate");
            Set(trailing, "EnergyPriorities", new[] {"NGU"});
            Assert(Equals(state, f.Fixture("CoreState")), "all negative holding decisions preserve actual scheduler state");
            c.augments.augs[0].augmentTarget = c.augments.augs[0].upgradeTarget = 1;
            c.augments.augs[0].augLevel = c.augments.augs[0].upgradeLevel = 1;
            var pool = c.idleEnergy + c.augments.augs.Sum(aug => aug.augEnergy + aug.upgradeEnergy);
            f.Fixture("RunQuick", 2d);
            Assert(c.augments.augs.All(aug => aug.augEnergy == 0 && aug.upgradeEnergy == 0) && c.idleEnergy == pool, "completed AUG targets legitimately leave holding pool idle");
            Assert(Get<bool>(Production.GetType("NGUInjector.Managers.CustomStageAllocator", true), "LastStageApplicationSucceeded"), "completed targets retain valid actual receipt");
            Assert(Get<bool>(f.Fixture("Snapshot"), "ScheduleComplete") && !(bool) f.Fixture("Rebirth", "PeekRebirthAvailable"), "completed targets neither reset scheduler nor trigger automatic rebirth");
            Set(stage, "EnergyFocus", 3); Set(stage, "EnergyPriorities", null);
            c.curEnergy = c.idleEnergy = 1;
            f.Fixture("RunQuick", 2d);
            Assert(Get<bool>(f.Fixture("Snapshot"), "ScheduleComplete") && Get<int>(f.Fixture("Start"), "RemainingSeconds") == 0, "terminal W retains completed entry and actual zero remaining time");
            Assert(c.wandoos98.wandoosEnergy == 1 && c.advancedTraining.energy[3] == 0 && c.advancedTraining.energy[4] == 0, "terminal W cannot reopen paid AT half-phase");
        }
        Console.WriteLine("PASS compiled terminal AUG hold: " + assertions + " assertions (real coordinator, main/quick allocation, checkpoints and rebirth readiness).");
    }
}
