using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
        CheckStageFightSchedules();
        CheckAutomaticEntryFightDispatch();
        CheckAutomaticEntryGuards();
        CheckAutomaticPreflightRetry();
        CheckAutomaticEntryReceiptRestore();
        CheckAutomaticEntryInvalidSetupCheckpoint();
        CheckAutomaticEntryReceiptFailure();
        CheckAutomaticEntryExpiry();
        CheckAutomaticEntryFruits();
        CheckStandardEntryFightUnchanged();
        CheckAutomaticCancellationRegressions();
    }

    private static object ScheduledFightPlan(bool automatic, int anchor, int delay, int interval)
    {
        var plan = EntryFightPlan(automatic);
        var stage = ((Array)Get(plan, "Stages")).GetValue(0);
        var definition = Activator.CreateInstance(Plugin.GetType("NGUInjector.CustomPlans.CustomFightSchedule", true));
        Set(definition, "Id", "schedule-fixture"); Set(definition, "Anchor", anchor);
        Set(definition, "DelaySeconds", delay); Set(definition, "IntervalSeconds", interval);
        Set(stage, "FightSchedule", definition);
        Invoke(Get(MainType, "CustomPlanStore"), "Save", "fixture", plan);
        return Invoke(Get(MainType, "CustomPlanStore"), "Load", "fixture");
    }

    private static object StartFightScheduleAt(out object plan)
    {
        plan = ScheduledFightPlan(true, 0, 10, 60);
        ControlledCombatPrepClock = 10000;
        Set(Get(Get(Character, "buttons"), "augmentation"), "m_Interactable", false);
        foreach (var name in new[] { "wandoosEnergy", "wandoosMagic" })
            Set(Get(Get(Character, "advancedTrainingController"), name), "levelFactor", 1000f);
        var menus = (long[])Get(Get(Character, "advancedTraining"), "levelTarget"); menus[3] = 5000; menus[4] = 6000;
        var run = StartScheduled(plan);
        Equal("AT", Get(ScheduledPhase(run), "Kind"), "scheduled interruption starts with actually funded paid AT");
        Equal(true, Get(Get(run, "ResourceOutcome"), "Verified"), "scheduled interruption has real resource proof");
        ScheduledTime(510); ControlledCombatPrepClock = 10010; CallMain("TickCombatPrep");
        Equal(true, Get(run, "FightSuspended"), "due fight suspends the existing automatic run");
        Equal(true, Get(MainInstance, "BossPushRunning"), "due fight acquires actual fight preparation");
        Equal(false, Get(run, "NukeTimerOwned"), "suspended run relinquishes native nuke pause");
        Equal(5000L, menus[3], "borrowed AT menu returned before fight acquisition");
        Equal(6000L, menus[4], "second borrowed AT menu returned before fight acquisition");
        return run;
    }

    private static void CheckStageFightSchedules()
    {
        Equal(-1L, CallMain("StageFightOccurrence", 109d, 100d, 10, 30), "delay is measured from the anchor");
        Equal(0L, CallMain("StageFightOccurrence", 110d, 100d, 10, 30), "exact delay boundary admits occurrence zero");
        Equal(3L, CallMain("StageFightOccurrence", 219d, 100d, 10, 30), "missed intervals coalesce to newest fixed-cadence slot");
        Equal(4L, CallMain("StageFightOccurrence", 230d, 100d, 10, 30), "completion never shifts fixed cadence");
        Equal(0L, CallMain("StageFightOccurrence", 99999d, 100d, 10, 0), "zero interval remains one occurrence");
        CheckScheduledFightRealHandoff();
        foreach (var change in new[] { "target", "gear", "deadline", "restart" }) CheckScheduledFightContinuationInvalidation(change);
        CheckScheduledFightFrozenDefinition();
        CheckScheduledFightFundingAnchor();
        CheckScheduledFightLegacyReceipt();
        CheckScheduledFightResumeSafety();
    }

    private static object BlockScheduledFightResume(out object plan, out FileStream held)
    {
        var run = StartFightScheduleAt(out plan);
        CallMain("StopBossPush");
        Set(Get(Get(Character, "arbitrary"), "nukeTimer"), "totalseconds", 12.5d);
        held = new FileStream((string)Get(MainInstance, "ProtectedCheckpointPath"), FileMode.Open, FileAccess.Read, FileShare.Read);
        CallMain("TickCombatPrep");
        Equal(false, Get(run, "FightSuspended"), "checkpoint fault occurs after real successor reacquisition");
        Equal(true, Get(run, "FightResuming"), "checkpoint fault retains partially resumed phase");
        Equal(true, Get(run, "SetupVerified"), "checkpoint fault occurs after real setup verification");
        Equal(-1, Get(run, "AppliedResourcePhase"), "checkpoint fault prevents resumed phase funding");
        Equal(false, Get(MainInstance, "_protectedCheckpointDurable"), "positive control: checkpoint really failed");
        var targets = (long[])Get(Get(Character, "advancedTraining"), "levelTarget");
        Equal(0L, targets[3], "partial resume already borrowed first target menu");
        Equal(0L, targets[4], "partial resume already borrowed second target menu");
        Equal(2, ((ICollection)Get(run, "RestoreTargets")).Count, "partial resume retains both owned return callbacks");
        return run;
    }

    private static void CheckScheduledFightResumeSafety()
    {
        CheckScheduledFightWandoosReacquisition();
        CheckScheduledFightResumeNukePause();
        CheckScheduledFightResumeTargetCleanup();
    }

    private static void CheckScheduledFightWandoosReacquisition()
    {
        foreach (var arrival in new[] { "fight", "retry" })
        foreach (var field in new[] { "energyLevel", "magicLevel", "energyProgress", "magicProgress", "wandoosEnergy", "wandoosMagic" })
        {
            FileStream held = null;
            try
            {
                object plan;
                var run = arrival == "retry" ? BlockScheduledFightResume(out plan, out held) : StartFightScheduleAt(out plan);
                held?.Dispose(); held = null;
                var scheduler = Get(MainInstance, "_protectedScheduler"); var deadline = Get(run, "DeadlineRebirthSeconds");
                var continuation = Get(MainInstance, "_automaticAtContinuation").ToString();
                if (arrival == "fight")
                {
                    Set(Character, "bossID", 200); Set(Character, "highestBoss", 200); Set(Character, "curHP", 1f);
                    Set(Child(Character, "API"), "character", Character);
                    CallMain("BossPushTick");
                    Equal(true, Get(Get(Character, "bossController"), "nukeBoss"), "W evidence arrives during actual native combat");
                }
                var w = Get(Character, "wandoos98");
                if (field.EndsWith("Progress", StringComparison.Ordinal)) Set(w, field, .25f);
                else Set(w, field, 17L);
                if (field == "wandoosEnergy") Set(Character, "idleEnergy", (long)Get(Character, "idleEnergy") - 17);
                if (field == "wandoosMagic")
                {
                    var magic = Get(Character, "magic"); Set(magic, "idleMagic", (long)Get(magic, "idleMagic") - 17);
                }
                var evidence = new[] { "energyLevel", "magicLevel", "energyProgress", "magicProgress", "wandoosEnergy", "wandoosMagic" }
                    .ToDictionary(name => name, name => Get(w, name));
                var recalls = Recalls; var writes = ResourceCommittedWrites; var swaps = Swaps;
                CallMain("TickCombatPrep");
                Equal("cancelled", Get(run, "Status"), "W work forbids retained AT reacquisition/" + arrival + "/" + field);
                Equal(true, Get(run, "Retired"), "W work retires the same retained run, not a new policy");
                Equal(recalls, Recalls, "W work is checked before any successor recall");
                Equal(writes, ResourceCommittedWrites, "W work cannot be erased by resource reconciliation");
                Equal(swaps, Swaps, "W work is checked before any successor gear acquisition");
                foreach (var pair in evidence) Equal(pair.Value, Get(w, pair.Key), "cancellation preserves native W evidence: " + pair.Key);
                Equal(false, Get(scheduler, "AutoAtCompleted"), "forbidden AT never receives completion credit");
                Equal(continuation, Get(MainInstance, "_automaticAtContinuation").ToString(), "unfinished acquired AT targets remain unresolved");
                Equal(deadline, Get(run, "DeadlineRebirthSeconds"), "W conflict never grants a fresh deadline");
                if (arrival == "fight")
                {
                    Equal(true, Get(Get(Character, "bossController"), "nukeBoss"), "AT cancellation does not stop a foreign/native busy fight");
                    Equal(false, Get(Get(Get(MainInstance, "_bossPushProcess"), "Claim"), "Ended"), "busy fight keeps its prerequisites");
                }
                var saved = Parse(File.ReadAllText((string)Get(MainInstance, "ProtectedCheckpointPath")));
                Equal(true, Get(Json(Json(saved, "automaticSetup"), "safeResources"), "AsBool"), "W conflict persists safe-background intent before any recall");
                ReplaceAutomaticCoordinator(plan);
                Equal(Get(MainInstance, "AutomaticRecoveryEntry"), Get(MainInstance, "_automaticSafeResourcesEntry"), "reload cannot reacquire paid AT after W conflict");
                Equal(false, Get(Get(MainInstance, "_protectedScheduler"), "AutoAtCompleted"), "reload preserves unresolved AT gate");
            }
            finally { held?.Dispose(); ControlledCombatPrepClock = null; }
        }
    }

    private static void CheckScheduledFightResumeNukePause()
    {
        FileStream held = null;
        try
        {
            object plan; var run = BlockScheduledFightResume(out plan, out held);
            var boss = Get(Character, "bossController"); var arbitrary = Get(Character, "arbitrary");
            var timer = Get(arbitrary, "nukeTimer");
            Set(Get(Character, "settings"), "autoNukeOn", true); Set(arbitrary, "boughtAutoNuke", true);
            var deadline = Get(run, "DeadlineRebirthSeconds"); var started = Get(run, "Started");
            var writes = ResourceCommittedWrites; var swaps = Swaps;
            for (var frame = 1; frame <= 260; frame++)
            {
                ControlledCombatPrepClock = 10010 + frame * .25; ScheduledTime(510 + frame * .25);
                Invoke(boss, "Update");
                Equal(false, Get(boss, "nukeBoss"), "native Update cannot auto-nuke during blocked resume/frame=" + frame);
                Equal(false, Get(boss, "isFighting"), "native Update remains idle during blocked resume/frame=" + frame);
                CallMain("TickCombatPrep");
                Equal(0d, Get(timer, "totalseconds"), "each resuming frame renews the owned nuke pause/frame=" + frame);
            }
            Equal("running", Get(run, "Status"), "65 seconds of blocked resume retains the bounded run");
            Equal(true, Get(run, "FightResuming"), "failed checkpoint still blocks funding after native Update loop");
            Equal(writes, ResourceCommittedWrites, "blocked resume cannot fund paid AT on retry");
            Equal(swaps, Swaps, "retry does not reselect gear");
            Equal(started, Get(run, "Started"), "pause maintenance never restarts the acquired run");
            Equal(deadline, Get(run, "DeadlineRebirthSeconds"), "pause maintenance never extends the deadline");
            Equal(12.5d, Get(run, "OriginalNukeSeconds"), "retry validates pause without reacquiring its original timer value");
            held.Dispose(); held = null;
            Equal(true, CallMain("SaveProtectedStageProgress", true), "checkpoint recovers after the real file lock is released");
            CallMain("TickCombatPrep");
            Equal(false, Get(run, "FightResuming"), "checkpoint recovery completes the original reacquisition");
            Equal(true, CallMain("CombatPrepRequirementsVerified", run, ScheduledPhase(run)), "checkpoint recovery retains verified phase authority");
        }
        finally { held?.Dispose(); ControlledCombatPrepClock = null; }

        foreach (var interference in new[] { "native-nuke", "timer-edit" })
        {
            held = null;
            try
            {
                object plan; var run = BlockScheduledFightResume(out plan, out held);
                var boss = Get(Character, "bossController"); var timer = Get(Get(Character, "arbitrary"), "nukeTimer");
                if (interference == "native-nuke") Invoke(boss, "startNuke");
                else Set(timer, "totalseconds", 30d);
                var before = Get(timer, "totalseconds"); var writes = ResourceCommittedWrites; var swaps = Swaps;
                CallMain("TickCombatPrep");
                Equal("cancelled", Get(run, "Status"), "resuming tick cancels on nuke ownership/preemption change: " + interference);
                Equal(before, Get(timer, "totalseconds"), "cancellation never rewinds an externally changed native timer");
                Equal(writes, ResourceCommittedWrites, "preempted resume makes no resource writes");
                Equal(swaps, Swaps, "preempted resume makes no gear changes");
                if (interference == "native-nuke") Equal(true, Get(boss, "nukeBoss"), "foreign native nuke remains untouched");
            }
            finally { held?.Dispose(); ControlledCombatPrepClock = null; }
        }
    }

    private static void CheckScheduledFightResumeTargetCleanup()
    {
        foreach (var ending in new[] { "deadline", "pause", "foreign-target", "explicit-cancel" })
        {
            FileStream held = null;
            try
            {
                object plan; var run = BlockScheduledFightResume(out plan, out held);
                var targets = (long[])Get(Get(Character, "advancedTraining"), "levelTarget");
                var writes = ResourceCommittedWrites; var recalls = Recalls; var swaps = Swaps;
                var deadline = Get(run, "DeadlineRebirthSeconds");
                if (ending == "deadline") { ScheduledTime((double)deadline); ControlledCombatPrepClock = 10600; }
                if (ending == "pause") Setting("UseCustomRebirthPlan", false);
                if (ending == "foreign-target") targets[3] = 777;
                if (ending == "explicit-cancel") CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(run, "Id") + "\"}"));
                else CallMain("TickCombatPrep");
                Equal("cancelled", Get(run, "Status"), "partial resume cancellation retires the run: " + ending);
                Equal(true, Get(run, "Retired"), "partial resume drops authority only after owned settings cleanup: " + ending);
                Equal(ending == "foreign-target" ? 777L : 5000L, targets[3], "partial cleanup returns only the still-owned first target: " + ending);
                Equal(6000L, targets[4], "partial cleanup returns the still-owned second target: " + ending);
                Equal(0, ((ICollection)Get(run, "RestoreTargets")).Count, "successful target cleanup leaves no orphan return callbacks");
                Equal(12.5d, Get(Get(Get(Character, "arbitrary"), "nukeTimer"), "totalseconds"), "partial cleanup returns its still-owned nuke timer");
                Equal(writes, ResourceCommittedWrites, "partial cleanup never restores historical resources");
                Equal(recalls, Recalls, "partial cleanup never recalls current resources");
                Equal(swaps, Swaps, "partial cleanup never restores historical gear");
                Equal(false, Get(Get(MainInstance, "_protectedScheduler"), "AutoAtCompleted"), "partial cleanup grants no AT completion");
                Equal(deadline, Get(run, "DeadlineRebirthSeconds"), "partial cleanup never renews the deadline");
                held.Dispose(); held = null;
                Equal(true, CallMain("SaveProtectedStageProgress", true), "cancelled-entry checkpoint recovers after cleanup");
                var saved = Parse(File.ReadAllText((string)Get(MainInstance, "ProtectedCheckpointPath")));
                Equal(true, Get(Json(Json(saved, "automaticSetup"), "safeResources"), "AsBool"), "cancelled partial resume durably permits only safe background work");
            }
            finally { held?.Dispose(); ControlledCombatPrepClock = null; }
        }
    }

    private static void CheckScheduledFightRealHandoff()
    {
        foreach (var edit in new[] { "rename", "disable" })
        try
        {
            object plan; var run = StartFightScheduleAt(out plan); var policy = Get(run, "Policy"); var phase = ScheduledPhase(run);
            var preserved = new[] { "Started", "Duration", "DeadlineRebirthSeconds", "PolicyStarted", "PhaseStarted", "PhaseDeadline", "PhaseIndex", "ReplanCount", "OsOutcome" }
                .ToDictionary(name => name, name => Get(run, name));
            var target = Get(phase, "AtEnergyTarget"); var secondTarget = Get(phase, "AtMagicTarget");
            var process = Get(MainInstance, "_bossPushProcess"); var claim = Get(process, "Claim");
            Set(Character, "bossID", 200); Set(Character, "highestBoss", 200); Set(Character, "curHP", 1f);
            Set(Child(Character, "API"), "character", Character);
            CallMain("BossPushTick");
            var boss = Get(Character, "bossController");
            Equal(true, Get(boss, "isFighting"), "actual native startNuke enters combat");
            Equal(true, Get(boss, "nukeBoss"), "actual native nuke owns its prerequisites");
            var stage = ((Array)Get(plan, "Stages")).GetValue(0);
            if (edit == "disable") Set(stage, "FightSchedule", null);
            else Set(Get(stage, "FightSchedule"), "Id", "next-entry-id");
            Invoke(Get(MainType, "CustomPlanStore"), "Save", "fixture", plan);
            Equal("schedule-fixture", Get(Get(MainInstance, "_stageFightSchedule"), "Id"), "schedule-only save preserves acquired identity while native combat is busy");
            var writes = ResourceCommittedWrites; var swaps = Swaps;
            ScheduledTime(695); ControlledCombatPrepClock = 10195;
            CallMain("TickCombatPrep"); CallMain("RunCustomPlanAutomation", plan); CallMain("TickCombatPrep");
            Equal(true, ReferenceEquals(process, Get(MainInstance, "_bossPushProcess")), "duplicate callbacks retain one native fight process");
            Equal(true, ReferenceEquals(run, Get(MainInstance, "_combatPrepRun")), "native fight does not cancel or replace the prep run");
            Equal(writes, ResourceCommittedWrites, "suspended native combat does not fund prep");
            Equal(swaps, Swaps, "suspended native combat retains fight gear");
            Invoke(boss, "nukeBosses");
            Equal(false, Get(boss, "nukeBoss"), "actual native nuke resolves");
            CallMain("BossPushTick");
            Equal(true, Get(boss, "isFighting"), "actual ordinary beginFight follows the nuke");
            CallMain("StopBossPush");
            Equal(false, Get(claim, "Ended"), "stop while native combat is busy retains prerequisites");
            Invoke(boss, "beginFight"); CallMain("BossPushTick");
            Equal(true, Get(claim, "Ended"), "native terminal witness releases the fight claim");
            Equal(true, Get(run, "FightReleaseVerified"), "fight release retains verified successor before claim fields disappear");
            CallMain("TickCombatPrep");
            Equal(false, Get(run, "FightSuspended"), "same retained run resumes after witnessed release");
            Equal(false, Get(run, "FightResuming"), "fresh phase setup and funding are verified before progression");
            Equal(true, ReferenceEquals(policy, Get(run, "Policy")), "fight does not replan the acquired policy");
            Equal(true, ReferenceEquals(phase, ScheduledPhase(run)), "fight retains the same phase object");
            foreach (var pair in preserved) Equal(pair.Value, Get(run, pair.Key), "fight retains original " + pair.Key);
            Equal(target, Get(phase, "AtEnergyTarget"), "fight does not substitute AT target");
            Equal(secondTarget, Get(phase, "AtMagicTarget"), "fight does not substitute second AT target");
            Equal(null, Get(run, "EndedAt"), "suspension never freezes the original elapsed clock");
            Equal(true, CallMain("CombatPrepRequirementsVerified", run, phase), "resumed native setup has fresh grants");
            Equal(0L, ((long[])Get(Get(Character, "advancedTraining"), "levelTarget"))[3], "resumption borrows the original phase's menu again");
            Equal(3L, Get(Get(MainInstance, "_stageFightSchedule"), "Consumed"), "intervals missed while fighting are coalesced on release");
            CallMain("TickCombatPrep");
            Equal(false, Get(MainInstance, "BossPushRunning"), "release cannot produce a catch-up burst");
            if (edit == "disable")
                Equal("disabled", JsonValue(CallMain("GetStageFightScheduleRuntime"), "state"), "disabled schedule preserves active handoff but admits no future occurrence");
            else
                Equal(750d, Get(Json(CallMain("GetStageFightScheduleRuntime"), "nextFightAtSeconds"), "AsDouble"), "schedule edit never changes the current entry's original cadence");
        }
        finally { ControlledCombatPrepClock = null; }
    }

    private static void CheckScheduledFightContinuationInvalidation(string change)
    {
        try
        {
            object plan; var run = StartFightScheduleAt(out plan); var deadline = Get(run, "DeadlineRebirthSeconds");
            if (change == "restart")
            {
                CallMain("SaveProtectedStageProgress", true);
                CallMain("StopBossPush");
                ReplaceAutomaticCoordinator(plan);
                Equal(Get(MainInstance, "AutomaticRecoveryEntry"), Get(MainInstance, "_automaticSafeResourcesEntry"), "restart drops suspended live authority and retains only safe background intent");
                Equal(deadline, Get(Get(MainInstance, "_protectedScheduler"), "ExpectedEndSeconds"), "restart never renews suspended deadline");
                Equal(false, Get(MainInstance, "CombatPrepRunActive"), "restart imports no suspended live run");
                return;
            }
            CallMain("StopBossPush");
            if (change == "target") ((long[])Get(Get(Character, "advancedTraining"), "levelTarget"))[3] = 777;
            if (change == "gear") Set(Get(Get(Character, "inventory"), "head"), "curAttack", 4321f);
            if (change == "deadline") { ScheduledTime((double)deadline); ControlledCombatPrepClock = 10600; }
            var writes = ResourceCommittedWrites;
            CallMain("TickCombatPrep");
            Equal("cancelled", Get(run, "Status"), "changed successor invalidates suspended continuation: " + change);
            Equal(true, Get(run, "Retired"), "changed successor cannot silently reacquire prep: " + change);
            Equal(writes, ResourceCommittedWrites, "invalid continuation does not fund paid work: " + change);
            Equal(deadline, Get(run, "DeadlineRebirthSeconds"), "invalid continuation never renews budget: " + change);
            if (change == "target") Equal(777L, ((long[])Get(Get(Character, "advancedTraining"), "levelTarget"))[3], "manual target survives cancelled continuation");
        }
        finally { ControlledCombatPrepClock = null; }
    }

    private static void CheckScheduledFightFrozenDefinition()
    {
        var plan = ScheduledFightPlan(false, 0, 30, 60); var runtime = CallMain("GetProtectedStage", plan);
        var schedule = Get(MainInstance, "_stageFightSchedule"); var entry = Get(runtime, "EntryIdentity");
        var stage = ((Array)Get(plan, "Stages")).GetValue(0); var definition = Get(stage, "FightSchedule");
        Set(definition, "Id", "edited-id"); Set(definition, "DelaySeconds", 0); Set(definition, "IntervalSeconds", 1);
        Invoke(Get(MainType, "CustomPlanStore"), "Save", "fixture", plan);
        runtime = CallMain("GetProtectedStage", plan);
        Equal(entry, Get(runtime, "EntryIdentity"), "schedule ID edits do not change stage structure or earned timing");
        Equal("schedule-fixture", Get(schedule, "Id"), "current entry retains acquired schedule ID");
        Equal(30, Get(schedule, "Delay"), "current entry retains acquired delay");
        Equal(60, Get(schedule, "Interval"), "current entry retains acquired cadence");
        Set(stage, "FightSchedule", null); CallMain("ServiceStageFightSchedule", plan);
        Equal(true, Get(schedule, "Disabled"), "removing schedule stops future admission");
        Set(stage, "FightSchedule", definition); CallMain("ServiceStageFightSchedule", plan);
        Equal(false, Get(MainInstance, "BossPushRunning"), "re-enabling does not silently reacquire in the same entry");
        var before = CallMain("ExportStageFightSchedule").ToString();
        CallMain("GetStageFightScheduleRuntime"); CallMain("GetStageFightScheduleRuntime");
        Equal(before, CallMain("ExportStageFightSchedule").ToString(), "schedule telemetry is read-only");
        Invoke(Get(MainType, "CustomPlanStore"), "Save", "fixture", plan);
        CallMain("OverrideCustomStage", Parse("{\"name\":\"fixture\",\"expectedRevision\":\"" + Get(plan, "RuntimeRevision")
            + "\",\"expectedEntry\":\"" + entry + "\",\"action\":\"restart\",\"stageIndex\":0}"));
        Equal("edited-id", Get(Get(MainInstance, "_stageFightSchedule"), "Id"), "explicit restart acquires edited schedule definition");
        Equal(1, Get(Get(MainInstance, "_stageFightSchedule"), "Interval"), "explicit restart acquires edited cadence without replaying the old schedule");
    }

    private static void CheckScheduledFightFundingAnchor()
    {
        var plan = ScheduledFightPlan(true, 1, 60, 0);
        Set(Get(Get(Character, "buttons"), "augmentation"), "m_Interactable", false);
        StartScheduled(plan);
        Equal(null, Get(Get(MainInstance, "_stageFightSchedule"), "AnchorAt"), "configured but unfunded Augments do not start the anchor");
        Equal("waiting-anchor", JsonValue(CallMain("GetStageFightScheduleRuntime"), "state"), "no-funded-augment schedule waits for evidence");

        plan = ScheduledFightPlan(true, 1, 0, 0);
        Set(Get(Get(Character, "buttons"), "advancedTraining"), "m_Interactable", false);
        Set(((Array)Get(Get(Character, "augmentsController"), "augments")).GetValue(0), "baseBoost", 100000000L);
        var head = Get(Get(Character, "inventory"), "head");
        SetEnum(head, "spec1Type", "Wandoos98"); Set(head, "spec1Cur", 1000000f); RefreshBonuses();
        CallMain("GetProtectedStage", plan);
        var path = (string)Get(MainInstance, "ProtectedCheckpointPath"); FileStream held = null;
        var targetType = Plugin.GetType("NGUInjector.Managers.ResourceTarget", true);
        var augmentTarget = (int)Enum.Parse(targetType, "Augment"); var upgradeTarget = (int)Enum.Parse(targetType, "AugmentUpgrade");
        Action<object, int, int, long, long> holdAfterFunding = (c, target, index, before, desired) => {
            if (held == null && desired > 0 && (target == augmentTarget || target == upgradeTarget))
                held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        };
        ResourceModelWriteAfter += holdAfterFunding;
        object augmentRun = null;
        try
        {
            augmentRun = StartScheduled(plan);
            Equal("Augments", Get(ScheduledPhase(augmentRun), "Kind"), "anchor fixture actually selects Augments");
            Equal(true, held != null, "actual positive native augmentation funding exercises checkpoint failure");
            Equal(500d, Get(Get(MainInstance, "_stageFightSchedule"), "AnchorAt"), "first successful funding latches despite checkpoint save failure");
            CallMain("TickCombatPrep");
            Equal(false, Get(MainInstance, "BossPushRunning"), "failed schedule checkpoint blocks action admission");
            Equal(-1L, Get(Get(MainInstance, "_stageFightSchedule"), "Consumed"), "failed save consumes no occurrence");
        }
        finally { ResourceModelWriteAfter -= holdAfterFunding; held?.Dispose(); }
        ScheduledTime(505); CallMain("TickCombatPrep");
        Equal(true, Get(MainInstance, "BossPushRunning"), "checkpoint recovery admits original anchored occurrence");
        Equal(500d, Get(Get(MainInstance, "_stageFightSchedule"), "AnchorAt"), "retry never shifts observed first-funding anchor");
        var saved = Parse(File.ReadAllText(path));
        Equal(500d, Get(Json(Json(saved, "fightSchedule"), "anchorAt"), "AsDouble"), "recovered checkpoint stores original anchor");
        Equal("0", JsonValue(Json(saved, "fightSchedule"), "consumed"), "recovered checkpoint stores occurrence before admission");
        var policy = Get(augmentRun, "Policy"); var phase = ScheduledPhase(augmentRun);
        var original = new[] { "AugmentTierId", "AugmentTarget", "UpgradeTarget" }.ToDictionary(name => name, name => Get(phase, name));
        var os = Get(policy, "OsId"); var phaseDeadline = Get(augmentRun, "PhaseDeadline");
        CallMain("StopBossPush"); CallMain("TickCombatPrep");
        Equal(true, ReferenceEquals(augmentRun, Get(MainInstance, "_combatPrepRun")), "funding-anchored fight resumes the retained Augments run");
        Equal(true, ReferenceEquals(policy, Get(augmentRun, "Policy")), "Augments continuation never invokes a new planner");
        Equal(false, Get(augmentRun, "FightSuspended"), "Augments successor verifies fresh acquisition");
        Equal(false, Get(augmentRun, "FightResuming"), "Augments continuation verifies actual funding before ordinary progression");
        foreach (var pair in original) Equal(pair.Value, Get(ScheduledPhase(augmentRun), pair.Key), "Augments continuation preserves " + pair.Key);
        Equal(os, Get(Get(augmentRun, "Policy"), "OsId"), "Augments continuation retains acquired OS");
        Equal(phaseDeadline, Get(augmentRun, "PhaseDeadline"), "Augments continuation retains original phase deadline");
        ReplaceAutomaticCoordinator(plan);
        Equal(500d, Get(Get(MainInstance, "_stageFightSchedule"), "AnchorAt"), "reload preserves observed funding anchor after storage recovery");
        Equal(0L, Get(Get(MainInstance, "_stageFightSchedule"), "Consumed"), "reload preserves consumed occurrence without live attempt authority");
        Equal(false, CallMain("ServiceStageFightSchedule", plan), "reload never retries a consumed once-only occurrence");
    }

    private static void CheckScheduledFightLegacyReceipt()
    {
        var plan = EntryFightPlan(); CallMain("RunCustomPlanAutomation", plan); CallMain("StopBossPush");
        CallMain("SaveProtectedStageProgress", true);
        var path = (string)Get(MainInstance, "ProtectedCheckpointPath"); var saved = Parse(File.ReadAllText(path));
        saved.GetType().GetMethod("Remove", new[] { typeof(string) }).Invoke(saved, new object[] { "fightSchedule" });
        File.WriteAllText(path, saved.ToString());
        ReplaceAutomaticCoordinator(plan);
        Equal(0L, Get(Get(MainInstance, "_stageFightSchedule"), "Consumed"), "legacy consumed fightEntry migrates without replay");
        Equal(false, CallMain("ServiceStageFightSchedule", plan), "legacy migration creates no live attempt");
        Equal(false, Get(MainInstance, "BossPushRunning"), "legacy checkpoint reload never repeats its consumed entry fight");
    }

    private static void CheckAutomaticCancellationRegressions()
    {
        foreach (var kind in new[] { "AT", "Augments", "Wandoos" })
        foreach (var restartBeforeBackground in new[] { false, true })
        {
            var plan = ScheduledSetup(StandardRow + "," + ScheduledRow + "," + StandardRow);
            ControlledCombatPrepClock = 10000;
            Setting("CustomAutoMoneyPitEnabled", false); Setting("CustomAutoBoostEnabled", false);
            Set(Settings, "_customAutoExpEnabled", false); Setting("CustomAutoBloodSpellsEnabled", false);
            Set(Settings, "_energyNguWeights", new[] { 1, 0, 0, 0, 0, 0, 0, 0, 0 });
            Set(Settings, "_magicNguWeights", new[] { 1, 0, 0, 0, 0, 0, 0 });
            foreach (var skill in (IEnumerable)Get(Get(Character, "NGU"), "skills")) Set(skill, "level", 100000L);
            foreach (var skill in (IEnumerable)Get(Get(Character, "NGU"), "magicSkills")) Set(skill, "level", 100000L);
            CallMain("RunCustomPlanAutomation", plan);
            var scheduler = Get(MainInstance, "_protectedScheduler");
            Set(scheduler, "_lastHeartbeat", (double)Get(MainType, "ProtectedHeartbeat") - 10);
            ScheduledTime(510); CallMain("GetProtectedStage", plan);
            Equal(10d, ((double[])Get(scheduler, "EarnedSeconds"))[0], "cancel timeline begins with actual earned opener time");
            CallMain("RecallCombatPrepResources");
            Set(Get(Get(Character, "buttons"), "augmentation"), "m_Interactable", kind == "Augments");
            Set(Get(Get(Character, "buttons"), "advancedTraining"), "m_Interactable", kind == "AT");
            if (kind == "AT")
                foreach (var name in new[] { "wandoosEnergy", "wandoosMagic" })
                    Set(Get(Get(Character, "advancedTrainingController"), name), "levelFactor", 1000f);
            if (kind == "Wandoos") Set(((IList)Get(Get(Character, "wishes"), "wishes"))[190], "level", 1L);
            if (kind == "Augments" || kind == "Wandoos")
            {
                Set(((Array)Get(Get(Character, "augmentsController"), "augments")).GetValue(0), "baseBoost", 100000000L);
                var head = Get(Get(Character, "inventory"), "head");
                SetEnum(head, "spec1Type", "Wandoos98"); Set(head, "spec1Cur", 1000000f); RefreshBonuses();
            }
            var run = StartScheduled(plan);
            Equal(kind, Get(ScheduledPhase(run), "Kind"), "cancel timeline acquires actual planned phase: " + kind);
            Equal(true, CallMain("CombatPrepRequirementsVerified", run, ScheduledPhase(run)), "cancel timeline starts with real granted native prerequisites");
            Equal(true, Get(Get(run, "ResourceOutcome"), "Verified"), "cancel timeline starts with verified native resource funding");
            var augment = ((Array)Get(Get(Character, "augments"), "augs")).GetValue(0);
            var initialPaid = kind == "AT" ? ((long[])Get(Get(Character, "advancedTraining"), "energy"))[3] + ((long[])Get(Get(Character, "advancedTraining"), "energy"))[4]
                : kind == "Augments" ? (long)Get(augment, "augEnergy") + (long)Get(augment, "upgradeEnergy")
                : (long)Get(Get(Character, "wandoos98"), "wandoosEnergy") + (long)Get(Get(Character, "wandoos98"), "wandoosMagic");
            Equal(true, initialPaid > 0, "positive control: native paid phase was actually funded before cancellation");
            var entry = Get(run, "OwnerEntry"); var deadline = Get(run, "DeadlineRebirthSeconds"); var entered = Get(scheduler, "EnteredAt");
            var atComplete = Get(scheduler, "AutoAtCompleted"); var augComplete = Get(scheduler, "AutoAugmentsCompleted");
            var wStarted = Get(scheduler, "AutoWandoosStarted");
            if (kind == "Wandoos")
            {
                Invoke(Get(Character, "wandoos98Controller"), "advanceEnergyProgress");
                Invoke(Get(Character, "wandoos98Controller"), "advanceMagicProgress");
            }
            var w = Get(Character, "wandoos98"); var os = Get(w, "os");
            var wEnergy = Get(w, "energyLevel"); var wMagic = Get(w, "magicLevel");
            var wEnergyProgress = Get(w, "energyProgress"); var wMagicProgress = Get(w, "magicProgress");
            var targets = Plugin.GetType("NGUInjector.Managers.ResourceTarget", true);
            var paidTargets = new HashSet<int>();
            foreach (var name in new[] { "AdvancedTraining", "Augment", "AugmentUpgrade", "WandoosEnergy", "WandoosMagic" })
                paidTargets.Add((int)Enum.Parse(targets, name));
            var paidWrites = 0; var backgroundWrites = 0;
            Action<object, int, int, long, long> observe = (c, target, index, before, desired) => {
                if (desired <= before) return;
                if (paidTargets.Contains(target)) paidWrites++; else backgroundWrites++;
            };
            ResourceModelWriteAfter += observe;
            try
            {
                ScheduledTime(530); ControlledCombatPrepClock = 10020;
                var args = Parse("{\"runId\":\"" + Get(run, "Id") + "\"}");
                var message = (string)CallMain("CancelCombatPrep", args);
                Equal(true, message.StartsWith("Combat prep cancelled:"), "actual cancel handler finishes clean native cancellation");
                Equal("cancelled", Get(run, "Status"), "user cancellation is not completed preparation");
                Equal(false, Get(MainInstance, "CombatPrepCleanupRecoveryRequired"), "clean cancellation has no orphan cleanup marker");
                Equal(deadline, Get(scheduler, "ExpectedEndSeconds"), "clean cancel retains original deadline");
                var saved = Parse(File.ReadAllText((string)Get(MainInstance, "ProtectedCheckpointPath")));
                Equal(true, Get(Json(Json(saved, "automaticSetup"), "safeResources"), "AsBool"), "cancel persists safe-background intent before restart");
                Equal(true, Get(Json(Json(saved, "automaticSetup"), "cancelled"), "AsBool"), "actual handler durably records cancelled entry intent");
                var continuation = Json(saved, "automaticAtContinuation").ToString();
                if (restartBeforeBackground) ReplaceAutomaticCoordinator(plan);
                foreach (var dispatch in new[] { "QuickStuff", "AutomationRoutine" })
                {
                    CallMain(dispatch);
                    var background = Get(MainInstance, "_combatPrepRun");
                    Equal("running", Get(background, "Status"), "cancelled entry still funds safe background through " + dispatch);
                    Equal("NGU", Get(ScheduledPhase(background), "Kind"), "cancelled entry never reacquires paid preparation through " + dispatch);
                    Equal(entry, Get(background, "OwnerEntry"), "background retains original automatic entry");
                    Equal(deadline, Get(background, "DeadlineRebirthSeconds"), "background retains original automatic deadline");
                }
                ScheduledTime(550); ControlledCombatPrepClock = 10040;
                ReplaceAutomaticCoordinator(plan); CallMain("AutomationRoutine"); CallMain("QuickStuff");
                scheduler = Get(MainInstance, "_protectedScheduler");
                var resumed = Get(MainInstance, "_combatPrepRun");
                Equal("NGU", Get(ScheduledPhase(resumed), "Kind"), "restart of cancelled/background entry remains safe");
                Equal(entry, Get(resumed, "OwnerEntry"), "restart preserves exact cancelled entry");
                Equal(entered, Get(scheduler, "EnteredAt"), "restart cannot renew entry start");
                Equal(Get(run, "Started"), Get(resumed, "Started"), "restart preserves original setup clock budget");
                Equal(Get(run, "Duration"), Get(resumed, "Duration"), "restart cannot grant a fresh duration");
                Equal(atComplete, Get(scheduler, "AutoAtCompleted"), "cancel/restart cannot fabricate missing AT credit");
                Equal(augComplete, Get(scheduler, "AutoAugmentsCompleted"), "cancel/restart cannot fabricate missing augment credit");
                Equal(wStarted, Get(scheduler, "AutoWandoosStarted"), "cancel/restart preserves native Wandoos history");
                Equal(continuation, Json(Parse(File.ReadAllText((string)Get(MainInstance, "ProtectedCheckpointPath"))), "automaticAtContinuation").ToString(), "cancel/restart retains acquired unresolved AT gate exactly");
                Equal(0, paidWrites, "cancel/restart makes no positive AT/AUG/W transfers");
                Equal(true, backgroundWrites > 0, "safe-background positive control observes real native funding");
                Equal(os, Get(w, "os"), "cancel/restart never repurchases or resets OS");
                Equal(wEnergy, Get(w, "energyLevel"), "cancel/restart preserves native earned W energy");
                Equal(wMagic, Get(w, "magicLevel"), "cancel/restart preserves native earned W magic");
                Equal(wEnergyProgress, Get(w, "energyProgress"), "cancel/restart preserves native W energy partial");
                Equal(wMagicProgress, Get(w, "magicProgress"), "cancel/restart preserves native W magic partial");
                ControlledCombatPrepClock = 10700; // Wall expiry cannot turn cancelled work into early stage completion.
                CallMain("QuickStuff"); CallMain("AutomationRoutine");
                Equal(deadline, Get(scheduler, "ExpectedEndSeconds"), "cancelled wall expiry never shortens original rebirth deadline");
                Equal(1, Get(scheduler, "StageIndex"), "cancelled entry stays selected before original deadline");
                Equal(false, ((bool[])Get(scheduler, "Completed"))[1], "cancelled entry cannot earn early completion");
                Equal(10d, ((double[])Get(scheduler, "EarnedSeconds"))[0], "cancel timeline preserves earned opener credit");
                Equal(0d, ((double[])Get(scheduler, "EarnedSeconds"))[2], "cancel timeline never invents successor credit");
                Equal(0, paidWrites, "wall expiry cannot restart paid preparation");
                ResourceModelWriteAfter -= observe;
                ScheduledTime((double)deadline); CallMain("QuickStuff"); CallMain("AutomationRoutine");
                Equal(2, Get(scheduler, "StageIndex"), "original deadline admits normal next entry");
                Equal(true, ((bool[])Get(scheduler, "Completed"))[1], "elapsed obligation completes only at original deadline");
                Equal(false, Get(MainInstance, "CombatPrepRunActive"), "normal successor does not restart cancelled automatic prep");
                Equal(10000L, Get(((IList)Get(Get(Character, "NGU"), "skills"))[0], "energy"),
                    "normal NGU successor receives actual native resources after deadline");
            }
            finally { ResourceModelWriteAfter -= observe; ControlledCombatPrepClock = null; }
        }
        CheckAutomaticCancellationGuards();
    }

    private static void CheckAutomaticCancellationGuards()
    {
        foreach (var domain in new[] { "Gear", "Support" })
        {
            object plan; var run = UnfinishedAtFixture(out plan);
            ControlledCombatPrepClock = (double)Get(MainType, "CombatPrepClock");
            Setting("CustomAutoMoneyPitEnabled", false); Setting("CustomAutoBoostEnabled", false);
            Set(Settings, "_customAutoExpEnabled", false); Setting("CustomAutoBloodSpellsEnabled", false);
            var deadline = Get(run, "DeadlineRebirthSeconds"); var entry = Get(run, "OwnerEntry");
            CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(run, "Id") + "\"}"));
            var claim = BeginFixtureClaim(domain, "cancelled-automatic-background");
            var writes = ResourceCommittedWrites; var swaps = Swaps;
            CallMain("QuickStuff"); CallMain("AutomationRoutine");
            Equal(writes, ResourceCommittedWrites, "cancelled continuation cannot write resources under foreign " + domain + " claim");
            Equal(swaps, Swaps, "cancelled continuation cannot swap under foreign " + domain + " claim");
            Equal(deadline, Get(Get(MainInstance, "_protectedScheduler"), "ExpectedEndSeconds"), "foreign claim cannot renew or shorten cancelled entry");
            Equal(false, Get(Get(MainInstance, "_protectedScheduler"), "AutoAtCompleted"), "foreign claim cannot certify cancelled AT gate");
            Equal(true, CallMain("EndExecutionClaim", claim, false), "foreign cancellation fixture releases only its own claim");
            ControlledCombatPrepClock += 2; CallMain("QuickStuff"); CallMain("AutomationRoutine");
            var background = Get(MainInstance, "_combatPrepRun");
            Equal("NGU", Get(ScheduledPhase(background), "Kind"), "released foreign claim permits only safe background continuation");
            Equal(entry, Get(background, "OwnerEntry"), "released foreign claim retains cancelled entry");
            Equal(true, CallMain("CombatPrepRequirementsVerified", background, ScheduledPhase(background)), "safe continuation re-verifies actual granted prerequisites");
            CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(background, "Id") + "\"}"));
            ControlledCombatPrepClock = null;
        }

        var pendingPlan = ScheduledSetup(); ControlledCombatPrepClock = 10000;
        Set(Get(Get(Character, "buttons"), "augmentation"), "m_Interactable", false);
        Set(Get(Get(Character, "buttons"), "advancedTraining"), "m_Interactable", false);
        Set(((IList)Get(Get(Character, "wishes"), "wishes"))[190], "level", 1L);
        var pendingHead = Get(Get(Character, "inventory"), "head");
        SetEnum(pendingHead, "spec1Type", "Wandoos98"); Set(pendingHead, "spec1Cur", 1000000f); RefreshBonuses();
        var pending = StartScheduled(pendingPlan);
        Equal("Wandoos", Get(ScheduledPhase(pending), "Kind"), "pending cancellation fixture starts real Wandoos");
        var originalDeadline = Get(pending, "DeadlineRebirthSeconds");
        ControlledCombatPrepClock = 10601; FailDesiredEnergyPreflight = true;
        CallMain("AdvanceCombatPrep");
        Equal(false, FailDesiredEnergyPreflight, "native cleanup reaches actual failing preflight");
        Equal(true, Get(pending, "CleanupPending"), "actual deadline release remains pending");
        Equal("completed", Get(pending, "CleanupStatus"), "pending release began with observed completed Wandoos phase");
        CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(pending, "Id") + "\"}"));
        ControlledCombatPrepClock += 1; CallMain("QuickStuff");
        Equal(false, Get(pending, "CleanupPending"), "user cancellation safely finishes pending native release");
        Equal("cancelled", Get(pending, "Status"), "pending completion cannot overwrite explicit user cancellation");
        Equal(originalDeadline, Get(Get(MainInstance, "_protectedScheduler"), "ExpectedEndSeconds"), "pending cancel never grants early entry completion");
        Equal(false, ((bool[])Get(Get(MainInstance, "_protectedScheduler"), "Completed"))[0], "pending cancel retains original elapsed obligation");
        ScheduledTime((double)originalDeadline); CallMain("QuickStuff");
        Equal(true, Get(Get(MainInstance, "_protectedScheduler"), "Finished"), "cancelled terminal entry finishes at original deadline");
        ControlledCombatPrepClock = null;
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
            var runtime = CallMain("GetProtectedStage", plan);
            var scheduler = Get(MainInstance, "_protectedScheduler");
            var entry = Get(runtime, "EntryIdentity"); var deadline = Get(scheduler, "ExpectedEndSeconds");
            if (eligible == "enabled")
            {
                var hold = BeginFixtureClaim("FiniteAction", "outer-fruit-entry-owner");
                var writes = ResourceCommittedWrites; var recalls = Recalls; var swaps = Swaps;
                try
                {
                    Equal(false, CallMain("ActivateFruitAction", 0), "fruit activation waits for an existing finite-action owner rather than acquiring a global request");
                    CallMain("RunCustomPlanAutomation", plan);
                    Equal(null, Get(MainInstance, "_combatPrepRun"), "held finite action defers scheduled acquisition without publishing a failed or nested run");
                    Equal(null, Get(MainInstance, "_automaticAcquisitionRetryEntry"), "ownership wait creates no failed-acquisition retry identity");
                    Equal(false, Get(hold, "Ended"), "scheduled wait preserves the original finite-action claim");
                    Equal(false, Get(fruitModels[0], "activated"), "ownership wait does not consume energy fruit");
                    Equal(false, Get(fruitModels[1], "activated"), "ownership wait does not consume magic fruit");
                    Equal(writes, ResourceCommittedWrites, "held finite action permits no scheduled resource writes");
                    Equal(recalls, Recalls, "held finite action permits no scheduled recalls");
                    Equal(swaps, Swaps, "held finite action permits no scheduled setup swaps");
                    Equal(deadline, Get(scheduler, "ExpectedEndSeconds"), "ownership wait preserves the original stage deadline");
                }
                finally { Equal(true, CallMain("EndExecutionClaim", hold, false), "outer finite-action owner explicitly retires without reconciliation"); }
            }
            var run = StartScheduled(plan);
            Equal(entry, Get(run, "OwnerEntry"), "fruit reconciliation acquires only the original scheduled entry");
            Equal(deadline, Get(run, "DeadlineRebirthSeconds"), "fruit reconciliation never renews the stage deadline");
            Equal(eligible == "enabled", Get(fruitModels[0], "activated"), "automatic entry preserves native affordable energy-fruit activation intent: " + eligible);
            Equal(eligible == "enabled", Get(fruitModels[1], "activated"), "automatic entry preserves native affordable magic-fruit activation intent: " + eligible);
            Equal(eligible == "enabled" ? 9000L : 10000L, Get(run, "EnergyPool"), "fresh planner captures energy after native fruit costs");
            Equal(eligible == "enabled" ? 18000L : 20000L, Get(run, "MagicPool"), "fresh planner captures magic after native fruit costs");
            Equal(support, Get(Get(Get(run, "Capture"), "OriginalSupportState"), "StableKey"), "fruit activation never installs ordinary stage support before capture");
            Equal(true, EqualMaps(gear, Get(Get(run, "Capture"), "OriginalGear")), "fruit activation never installs ordinary stage gear before capture");
            CallMain("RunCustomPlanAutomation", plan); CallMain("QuickStuff");
            Equal(true, ReferenceEquals(run, Get(MainInstance, "_combatPrepRun")), "ordinary and quick callbacks retain the one acquired run after fruit reconciliation");
            Equal("running", Get(run, "Status"), "outer startup cannot replace its running acquisition with a reentrant failure");
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
