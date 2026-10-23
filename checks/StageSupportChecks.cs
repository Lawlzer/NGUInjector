using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Compiled-injector coordination only: uninitialized Unity objects, locked feature
// buttons, disabled resource management, no injection, controller ticks, or game saves.
// Scheduler checkpoints are real files in a disposable temporary directory.
internal static class StageSupportChecks
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static int _assertions;

    internal static int Run(Assembly assembly)
    {
        _assertions = 0;
        var failures = new List<Exception>();
        foreach (var test in new Action<Assembly>[] { CheckStageEntry, CheckSingleStageLoop, CheckUnavailableSetupBlocksCredit,
            CheckSupportLocks, CheckManualAdventure, CheckInactiveManualAdventure, CheckFightPrep, CheckFightPrerequisiteRejection, CheckPrepLocks,
            CheckControllerFightOwnership, CheckPrepWaitsForController, CheckCompletedPrepReadyForRebirth,
            CheckResetAndInvalidation, CheckSupportGrant, CheckSynchronousRebirthMultipliers })
        {
            try { test(assembly); }
            catch (Exception error) { failures.Add(new Exception(test.Method.Name, error)); }
        }
        if (failures.Count != 0) throw new AggregateException("Stage support failed after " + _assertions + " assertions.", failures);
        Console.WriteLine("Stage support checks passed: " + _assertions + " assertions.");
        return _assertions;
    }

    private static void CheckStageEntry(Assembly assembly)
    {
        using (var f = new Fixture(assembly, 2))
        {
            // Resource state is already current, but equipment still belongs to
            // the prior owner. Entry must not depend on the resource-key branch.
            var runtime = f.Call("GetProtectedStage", f.Plan);
            var entry = Property(runtime, "EntryIdentity");
            var resourceKey = "stage-support:0:" + entry;
            f.Set("_customStageKey", resourceKey);
            f.SeedSupport("focus:Wandoos");
            f.Set("_customEnergyFruitCapLastRetry", f.Stamp);
            f.Run(0d);
            Equal(resourceKey, f.Get("_customStageKey"), "equipment entry leaves the current resource key unchanged");
            Equal(f.Stamp, f.Get("_customEnergyFruitCapLastRetry"), "equipment-only entry does not reset resource retry state");
            Equal("stage:stage-support-check:" + resourceKey + ":0", f.Get("_customStageEquipmentKey"), "entry establishes its independent equipment key");
            f.ExpectSupportSkipped("GearGoal=-1 stage entry");
            Equal(false, f.Call("InlineStageSetupVerified", f.Plan), "GearGoal=-1 cannot fabricate setup verification");
            Equal(false, Get(Get(f.Get("_inlineStageSelection"), "Baseline"), "ControlsGear"), "GearGoal=-1 leaves equipment uncontrolled");
            Contains(f.Status, "Support models unavailable", "GearGoal=-1 still reaches the real shared support observation");
            var firstKey = f.Get("_customStageEquipmentKey");
            f.SeedSupport((string) firstKey);
            f.Run(100d);
            Equal(1, Property(f.Get("_protectedScheduler"), "StageIndex"), "scheduler really enters the second flexible stage");
            Equal(false, Equals(entry, Property(f.Call("GetProtectedStage", f.Plan), "EntryIdentity")), "second stage receives a new stable entry identity");
            Equal(false, Equals(firstKey, f.Get("_customStageEquipmentKey")), "second stage has a new equipment key");
            f.ExpectSupportSkipped("second stage entry");
        }
    }

    private static void CheckSingleStageLoop(Assembly assembly)
    {
        using (var f = new Fixture(assembly))
        {
            var stage = ((Array) Get(f.Plan, "Stages")).GetValue(0);
            // Native mandatory AT completion changes entries without claiming fixed
            // applied-time credit from this deliberately unavailable support fixture.
            Set(stage, "CompletionMode", 1);
            Set(stage, "TargetAdvancedTrainingPower", 2L);
            Set(stage, "TargetAdvancedTrainingToughness", 1L);
            f.Run(0d);
            var resourceKey = f.Get("_customStageKey");
            var equipmentKey = f.Get("_customStageEquipmentKey");
            f.SeedSupport((string) equipmentKey);
            f.Run(1d);
            Equal(equipmentKey, f.Get("_customStageEquipmentKey"), "same entry keeps its equipment key");
            Equal(null, f.Get("_customSupportIntent"), "unavailable support cannot fabricate a selected support intent");
            Contains(f.Status, "Inline setup waiting", "same entry leaves shared support observation retryable");
            Equal(f.Stamp, f.Get("_customEquipmentLastDiggersAt"), "failed observation does not consume a legacy digger refresh");
            var levels = (long[]) Get(Get(f.Character, "advancedTraining"), "level");
            levels[0] = 1; levels[1] = 2;
            f.Run(3d);
            Equal(1, Property(f.Get("_protectedScheduler"), "Cycle"), "actual mandatory AT completion enters a single-stage repeat");
            Equal(false, Equals(resourceKey, f.Get("_customStageKey")), "loop entry receives a new resource identity");
            Equal("stage:stage-support-check:" + f.Get("_customStageKey") + ":1", f.Get("_customStageEquipmentKey"), "single-stage cycle contributes to the equipment key");
            Equal(false, Equals(equipmentKey, f.Get("_customStageEquipmentKey")), "loop entry invalidates equipment");
            f.ExpectSupportSkipped("single-stage loop re-entry");
            Contains(f.Status, "Support models unavailable", "loop retries shared observation despite previous legacy completion");
        }
    }

    private static void CheckUnavailableSetupBlocksCredit(Assembly assembly)
    {
        using (var f = new Fixture(assembly))
        {
            var stage = ((Array) Get(f.Plan, "Stages")).GetValue(0);
            Set(stage, "DurationSeconds", 2);
            Set(stage, "EnergyPriorities", new[] {"AT-1"});
            f.Run(0d);
            f.AssertCreditBlocked(2d);
        }
    }

    private static void CheckSupportLocks(Assembly assembly)
    {
        foreach (var boundary in new[] {"loadout", "digger"})
        using (var f = new Fixture(assembly))
        {
            f.PrepareFight();
            var domain = Enum.Parse(f.MainType.GetNestedType("ExecutionDomain", All), boundary == "loadout" ? "Gear" : "Support");
            var claim = f.Call("BeginExecutionClaim", "stage-support-owner", boundary, domain, (Func<bool>)(() => true));
            var support = Property(f.Call("CaptureSupportState"), "StableKey");
            f.Run(0d);
            Equal("stage:stage-support-check:stage-support:0:" + f.EntryIdentity + ":0", f.Get("_customStageEquipmentKey"), boundary + " lock still identifies pending stage equipment");
            Equal(null, f.Get("_customEquipmentOwner"), boundary + " lock does not commit support ownership");
            Equal(null, f.Get("_customSupportIntent"), boundary + " lock does not commit selected support");
            Equal(DateTime.MinValue, f.Get("_customEquipmentLastDiggersAt"), boundary + " lock does not commit digger cooldown");
            Equal(false, f.Call("InlineStageSetupVerified", f.Plan), boundary + " claim withholds actual setup verification");
            Equal(support, Property(f.Call("CaptureSupportState"), "StableKey"), boundary + " claim prevents native support mutation");
            Equal(false, f.Call("ProtectedStageOwnershipAvailable", f.Plan), boundary + " unverified claim cannot earn stage credit");
            Equal(true, f.Call("EndExecutionClaim", claim, false), boundary + " claim release performs no historical restoration");
            f.Run(1d);
            Equal(true, f.Call("InlineStageSetupVerified", f.Plan), boundary + " release verifies actual current native setup");
        }
    }

    private static void CheckManualAdventure(Assembly assembly)
    {
        using (var f = new Fixture(assembly))
        {
            f.Run(0d);
            f.SeedSupport("manual-adventure:2");
            var selection = f.Get("_inlineStageSelection");
            f.Setting("ManualAdventureEnabled", true);
            f.Set("_manualAdventureSetupActive", true);
            f.Run(1d);
            Equal(false, f.Call("InlineStageSetupVerified", f.Plan), "manual fighting cannot fabricate stage verification");
            Equal(true, ReferenceEquals(selection, f.Get("_inlineStageSelection")), "manual fighting retains the acquired inline policy");
            Equal("manual-adventure:2", f.Get("_customEquipmentOwner"), "stage automation does not steal manual support ownership");
            Equal(f.Stamp, f.Get("_customEquipmentLastDiggersAt"), "manual fighting bypasses stage support refresh");
            f.Call("ResetManualAdventureSetup");
            Equal(false, f.Get("_manualAdventureSetupActive"), "leaving manual fighting releases active route ownership");
            Equal(null, Get(selection, "Applied"), "leaving manual fighting leaves inline application invalidated");
            f.ExpectSupportCleared("leaving manual fighting");
            f.Setting("ManualAdventureEnabled", false);
            f.Run(2d);
            f.ExpectSupportSkipped("stage resumes after manual fighting");
        }
    }

    private static void CheckInactiveManualAdventure(Assembly assembly)
    {
        using (var f = new Fixture(assembly))
        {
            f.Run(0d);
            f.Setting("ManualAdventureEnabled", true);
            f.Set("_manualAdventureSetupActive", true);
            f.SeedSupport("manual-adventure:2");
            // Questing and unavailable routes use this real reset without turning
            // the user's manual-adventure setting off. Do not run live routing.
            f.Call("ResetManualAdventureSetup");
            Equal(false, f.Get("_manualAdventureSetupActive"), "paused manual route is inactive while still enabled");
            var selection = f.Get("_inlineStageSelection");
            f.Run(1d);
            Equal(true, ReferenceEquals(selection, f.Get("_inlineStageSelection")), "enabled but inactive manual routing retains the acquired stage selection");
            f.ExpectSupportSkipped("stage support resumes while manual adventure remains enabled");
            Contains(f.Status, "Support models unavailable", "inactive manual route permits real shared stage support observation");
        }
    }

    private static void CheckFightPrep(Assembly assembly)
    {
        foreach (var manual in new[] {true, false})
        using (var f = new Fixture(assembly))
        {
            f.PrepareFight();
            f.Run(0d);
            f.SeedSupport((string) f.Get("_customStageEquipmentKey"));
            var selection = f.Get("_inlineStageSelection");
            if (manual)
            {
                Contains((string)f.Call("RunManualFightPrep"), "identified explicit request", "raw manual preparation cannot invent its own opportunity");
                Equal(false, f.Get("_bossPushRunning"), "raw missing-request rejection starts no process");
                Equal(null, f.Get("_fightPreparationRecord"), "raw missing-request rejection creates no durable preparation identity");
            }
            var summary = manual ? f.Request("manualFightPrep", "stage-support-fight")
                : Convert.ToString(f.Call("RunCustomPreRebirthPrepIfNeeded", f.Plan));
            var scenario = manual ? "manual fight prep" : "automatic rebirth prep";
            Equal(true, ReferenceEquals(selection, f.Get("_inlineStageSelection")), scenario + " retains the acquired inline selection");
            Equal(true, f.Get("_bossPushRunning"), scenario + " reaches the real boss-push coordinator: " + summary);
            f.AssertPreparedFight(scenario);
            Equal(false, f.Call("ProtectedStageOwnershipAvailable", f.Plan), scenario + " withholds stage credit while its real claim owns prerequisites");
        }
    }

    private static void CheckFightPrerequisiteRejection(Assembly assembly)
    {
        using (var f = new Fixture(assembly))
        {
            f.PrepareFight();
            var inventory = Get(f.Character, "inventory"); var head = Get(inventory, "head");
            Set(Get(f.Character, "beards"), "disabled", true);
            Contains((string)f.Call("RunFightPrep", f.Plan, false, false, "negative required Fu", null, "disabled-required-fu"), "required fight beard does not fit", "disabled hard prerequisite rejects real identified preparation");
            Equal(false, f.Get("_bossPushRunning"), "unavailable Fu never starts a native boss process");
            Equal(null, f.Get("_fightPreparationClaim"), "unavailable Fu cannot acquire a writable preparation claim");
            Equal(true, ReferenceEquals(head, Get(inventory, "head")), "hard prerequisite rejection preserves native gear");
            Equal(0, ((IList)Get(Get(f.Character, "beards"), "activeBeards")).Count, "hard prerequisite rejection makes no native beard activation");
        }
    }

    private static void CheckControllerFightOwnership(Assembly assembly)
    {
        foreach (var flag in new[] {"isFighting", "nukeBoss"})
        using (var f = new Fixture(assembly, 2))
        {
            f.Run(0d);
            var resourceKey = f.Get("_customStageKey");
            var equipmentKey = f.Get("_customStageEquipmentKey");
            var selection = f.Get("_inlineStageSelection");
            f.SeedSupport("fight-prep");
            var boss = Child(f.Character, "bossController");
            Set(boss, flag, true);
            Equal(false, f.Get("_bossPushRunning"), flag + " fixture has no running boss-push coordinator");
            // Cross a flex deadline: an unguarded stage pass would reset gear and
            // support even though no actual equip can succeed in this fixture.
            f.Run(100d);
            Equal(resourceKey, f.Get("_customStageKey"), flag + " preserves resource identity during controller fight");
            Equal(equipmentKey, f.Get("_customStageEquipmentKey"), flag + " preserves pending stage equipment identity");
            Equal(true, ReferenceEquals(selection, f.Get("_inlineStageSelection")), flag + " preserves acquired stage selection during controller fight");
            Equal("fight-prep", f.Get("_customEquipmentOwner"), flag + " retains fight support ownership");
            Equal(null, f.Get("_customSupportIntent"), flag + " cannot invent a selected support intent during a native fight");
            Equal(f.Stamp, f.Get("_customEquipmentLastDiggersAt"), flag + " preserves fight digger cooldown");
            Equal("BossPush", f.Call("CurrentCustomGoal", f.Plan).ToString(), flag + " retains fight goal without a running push");
            Equal(true, f.Call("CustomQuickResourceRefreshBlocked", f.Plan), flag + " blocks quick refresh independently of prep flags");
            f.Set("_customLastQuickResourceRefresh", DateTime.MinValue);
            f.Call("RunQuickCustomResourceRefresh", f.Plan);
            Equal("Resource allocation is waiting for combat or rebirth preparation.", f.Status, flag + " quick dispatch reports its combat blocker");
            Equal(resourceKey, f.Get("_customStageKey"), flag + " blocked quick dispatch preserves actual resource ownership");
            Equal(equipmentKey, f.Get("_customStageEquipmentKey"), flag + " blocked quick dispatch preserves equipment ownership");
            Set(boss, flag, false);
            Equal(false, f.Call("CustomQuickResourceRefreshBlocked", f.Plan), flag + " release unblocks quick refresh");
            Equal("StagePlan", f.Call("CurrentCustomGoal", f.Plan).ToString(), flag + " release restores stage goal");
            f.Run(101d);
            Equal(1, Property(f.Get("_protectedScheduler"), "StageIndex"), flag + " release permits the pending flex transition");
            Equal(false, ReferenceEquals(selection, f.Get("_inlineStageSelection")), flag + " release acquires the new stage entry selection");
            f.ExpectSupportSkipped(flag + " release permits stage support again");
        }
    }

    private static void CheckPrepWaitsForController(Assembly assembly)
    {
        foreach (var manual in new[] {false, true})
        foreach (var flag in new[] {"isFighting", "nukeBoss"})
        using (var f = new Fixture(assembly))
        {
            f.Run(0d);
            f.SeedSupport("fight-prep");
            f.Set("_customPreRebirthPrepRunning", true);
            f.Set("_manualRebirthPending", manual);
            var boss = Child(f.Character, "bossController");
            Set(boss, flag, true);
            var scenario = (manual ? "manual" : "automatic") + " prep with " + flag;
            Equal(false, f.Get("_bossPushRunning"), scenario + " has stopped its boss-push coordinator");
            if (manual)
                Contains((string) f.Call("ContinueManualRebirth"), "still fighting bosses", scenario + " explicitly waits for the controller fight");
            else
                Equal(true, f.Call("RunCustomPreRebirthPrepIfNeeded", f.Plan), scenario + " remains in preparation");
            Equal(true, f.Get("_customPreRebirthPrepRunning"), scenario + " cannot finish preparation early");
            Equal(false, f.Get("_customPreRebirthPrepCompleted"), scenario + " cannot mark preparation complete early");
            Equal("fight-prep", f.Get("_customEquipmentOwner"), scenario + " retains support ownership");
            Equal(null, f.Get("_customSupportIntent"), scenario + " does not fabricate selected support from unavailable models");
            Equal(f.Stamp, f.Get("_customEquipmentLastDiggersAt"), scenario + " leaves digger cooldown intact");
            Equal(true, f.Call("CustomQuickResourceRefreshBlocked", f.Plan), scenario + " keeps quick refresh blocked");
            Set(boss, flag, false);
            if (manual)
            {
                // At zero rebirth seconds the real minimum-time check refuses
                // rebirth before touching the absent profile or any live API.
                f.AddRebirthData();
                Equal("Manual rebirth unavailable; minimum rebirth time or challenge state is blocking it.",
                    f.Call("ContinueManualRebirth"), scenario + " advances past prep only after the controller stops");
                Equal(false, f.Get("_manualRebirthPending"), scenario + " safely ends the unavailable manual request");
            }
            else
            {
                Equal(false, f.Call("RunCustomPreRebirthPrepIfNeeded", f.Plan), scenario + " completes after the controller stops");
                Equal(true, f.Get("_customPreRebirthPrepCompleted"), scenario + " records completed automatic prep");
            }
            Equal(false, f.Get("_customPreRebirthPrepRunning"), scenario + " releases preparation after actual fight completion");
            Equal(false, f.Call("CustomQuickResourceRefreshBlocked", f.Plan), scenario + " releases quick refresh after actual fight completion");
            f.Run(100d);
            f.ExpectSupportSkipped(scenario + " permits stage support after fight completion");
        }
    }

    private static void CheckCompletedPrepReadyForRebirth(Assembly assembly)
    {
        using (var f = new Fixture(assembly))
        {
            f.AddRebirthData();
            f.Setting("AutoRebirth", true);
            f.Run(0d);
            var equipmentKey = f.Get("_customStageEquipmentKey");
            Set(f.Plan, "AutoRebirthEnabled", true);
            Set(f.Plan, "TimeTriggerEnabled", true);
            Set(f.Plan, "TimeTriggerSeconds", 300);
            Set(f.Plan, "NumberTriggerEnabled", false);
            Set(f.Plan, "NumberClimbEnabled", false);
            f.Set("_customPreRebirthPrepCompleted", true);
            var selection = f.Get("_inlineStageSelection");
            f.SeedSupport("fight-prep");
            f.Run(300d);
            Equal(true, f.Call("CustomRebirthReady", f.Plan), "completed-prep fixture passes actual rebirth readiness");
            Equal(equipmentKey, f.Get("_customStageEquipmentKey"), "ready rebirth keeps completed prep equipment identity");
            Equal(true, ReferenceEquals(selection, f.Get("_inlineStageSelection")), "ready rebirth does not replace the retained stage selection after prep");
            Equal("fight-prep", f.Get("_customEquipmentOwner"), "ready rebirth retains prep support ownership");
            Equal(null, f.Get("_customSupportIntent"), "readiness cannot fabricate a selected support intent");
            Equal(f.Stamp, f.Get("_customEquipmentLastDiggersAt"), "ready rebirth retains prep digger cooldown");
            Equal("RebirthReady", f.Call("CurrentCustomGoal", f.Plan).ToString(), "completed prep yields to ready rebirth instead of stage automation");
            Equal(true, f.Call("CustomQuickResourceRefreshBlocked", f.Plan), "ready rebirth blocks quick refresh after prep");
            f.Set("_customLastQuickResourceRefresh", DateTime.MinValue);
            f.Call("RunQuickCustomResourceRefresh", f.Plan);
            Equal("Resource allocation is waiting for combat or rebirth preparation.", f.Status, "ready rebirth quick dispatch reports its blocker");
            Equal(equipmentKey, f.Get("_customStageEquipmentKey"), "ready rebirth blocked quick dispatch preserves completed prep equipment identity");
            Equal("fight-prep", f.Get("_customEquipmentOwner"), "ready rebirth blocked quick dispatch preserves fight support ownership");
            Set(f.Plan, "AutoRebirthEnabled", false);
            Equal(false, f.Call("CustomRebirthReady", f.Plan), "disabling automatic rebirth releases readiness");
            f.Run(301d);
            f.ExpectSupportSkipped("completed prep alone does not suppress a non-ready stage plan");
        }
    }

    private static void CheckPrepLocks(Assembly assembly)
    {
        foreach (var boundary in new[] {"loadout", "digger"})
        using (var f = new Fixture(assembly))
        {
            f.PrepareFight();
            f.Request("manualRebirth", "stage-support-prior-rebirth");
            f.AssertPreparedFight(boundary + " actual prior preparation");
            var priorProcess = f.Get("_bossPushProcess"); var priorRecord = f.Get("_fightPreparationRecord");
            var permission = boundary == "loadout" ? Get(f.Character, "inventoryController") : Get(f.Character, "beards");
            var field = boundary == "loadout" ? "midDrag" : "disabled";
            Set(permission, field, true);
            f.SeedSupport("stage-before-fight");
            Contains(f.Request("manualFightPrep", "stage-support-locked"), boundary == "loadout"
                ? "preparation blocked: Support is locked or inventory drag is active." : "setup waiting", boundary + " lock defers an actual identified manual request");
            Equal(true, f.Get("_manualRebirthPending"), boundary + " rejected Fight takeover preserves the pending rebirth intent");
            Equal(true, ReferenceEquals(priorProcess, f.Get("_bossPushProcess")), boundary + " lock starts no replacement or unprepared process");
            Equal(true, ReferenceEquals(priorRecord, f.Get("_fightPreparationRecord")), boundary + " rejection preserves the real prior ledger identity");
            Equal("stage-before-fight", f.Get("_customEquipmentOwner"), boundary + " lock preserves previous support");
            Equal(true, f.Call("RunCustomPreRebirthPrepIfNeeded", f.Plan), boundary + " lock keeps automatic rebirth waiting");
            Equal(false, f.Get("_customPreRebirthPrepCompleted"), boundary + " lock does not falsely complete rebirth prep");
            Set(permission, field, false);
            Contains(f.Request("manualFightPrep", "stage-support-unlocked"), "boss push running", boundary + " unlock admits the replacement fight push");
            Equal(false, f.Get("_manualRebirthPending"), boundary + " successful Fight takeover retires the old rebirth intent");
            Equal(false, f.Get("_customPreRebirthPrepRunning"), boundary + " successful Fight takeover clears old preparation ownership");
            Equal(true, f.Get("_bossPushRunning"), boundary + " retiring rebirth intent never stops the replacement push");
            Equal(false, ReferenceEquals(priorProcess, f.Get("_bossPushProcess")), boundary + " unlocked explicit request admits a genuinely new process");
            f.AssertPreparedFight(boundary + " admitted identified request");
        }
    }

    private static void CheckResetAndInvalidation(Assembly assembly)
    {
        foreach (var operation in new[] {"reset", "cache-invalidate"})
        using (var f = new Fixture(assembly))
        {
            f.Run(0d);
            f.SeedSupport((string) f.Get("_customStageEquipmentKey"));
            var selection = f.Get("_inlineStageSelection");
            var entry = f.EntryIdentity;
            var core = f.Get("_protectedScheduler");
            if (operation == "reset") f.Call("ResetCustomAutomationRuntime");
            else f.Call("InvalidateCustomRuntimePlanCache");
            Equal(operation == "reset" ? null : Get(selection, "Owner"), f.Get("_customStageEquipmentKey"), operation + " invalidates application without replacing retained stage intent");
            Equal(null, Get(selection, "Applied"), operation + " leaves inline application invalidated");
            Equal(false, f.Call("InlineStageSetupVerified", f.Plan), operation + " cannot retain setup verification after identity invalidation");
            if (operation == "reset") f.ExpectSupportCleared(operation);
            else Equal(Get(selection, "Owner"), f.Get("_customEquipmentOwner"), "cache invalidation retains the independently acquired support owner");
            f.Run(1d);
            f.ExpectSupportSkipped(operation + " retries support on the same stage");
            Equal(operation != "reset", ReferenceEquals(core, f.Get("_protectedScheduler")), operation + " has the intended engine lifetime");
            Equal(entry, f.EntryIdentity, operation + " preserves the acquired entry instead of silently renewing its budget");
        }
    }

    private static void CheckSupportGrant(Assembly assembly)
    {
        using (var f = new Fixture(assembly))
        {
            f.PrepareFight();
            var domain = Enum.Parse(f.MainType.GetNestedType("ExecutionDomain", All), "Support");
            var state = f.Call("CaptureSupportState"); var selection = Property(state, "Selection");
            var claim = f.Call("BeginExecutionClaim", "native-support-owner", "negative-actuator", domain, (Func<bool>)(() => true));
            var grant = f.Call("ResolveExecutionGrant", "foreign-support", "negative-actuator", domain, (Func<bool>)(() => true), null, null);
            Equal(false, f.Call("ValidateExecutionGrant", grant, domain), "actual support claim denies an unowned grant");
            f.Set("_selectedSetupSupportAuthorized", (Func<bool>)(() => (bool)f.Call("ValidateExecutionGrant", grant, domain)));
            try
            {
                f.Call("ApplyExactSupport", selection, .75d, true, null);
                throw new Exception("Unowned support grant unexpectedly reached the native actuator.");
            }
            catch (TargetInvocationException error)
            {
                Contains(error.InnerException.Message, "no longer authorized", "real support actuator checks permission before native writes");
            }
            Equal(Property(state, "StableKey"), Property(f.Call("CaptureSupportState"), "StableKey"), "denied support grant preserves all native support");
            var rejected = f.Get("_supportMutation");
            Equal(false, Get(rejected, "Completed"), "denied support grant cannot publish completed support");
            Equal(Property(state, "StableKey"), Property(Get(rejected, "After"), "StableKey"), "denied support journal records unchanged native state");
            Equal(0, ((IEnumerable)Get(rejected, "TouchedLevelIds")).Cast<int>().Count(), "denied support grant touches no native levels");
            Equal(false, Get(claim, "Ended"), "rejected actuator cannot retire another owner's claim");
            Equal(true, f.Call("EndExecutionClaim", claim, false), "real support claim release is explicit");
            grant = f.Call("ResolveExecutionGrant", "foreign-support", "released-actuator", domain, (Func<bool>)(() => true), null, null);
            Equal(true, f.Call("ValidateExecutionGrant", grant, domain), "released support domain admits a fresh current grant");
            f.Call("ApplyExactSupport", selection, .75d, true, null);
            Equal(true, Get(f.Get("_supportMutation"), "Completed"), "authorized native support actuator completes its real journal");
        }
    }

    private static void CheckSynchronousRebirthMultipliers(Assembly assembly)
    {
        // The runner loads the injector from bytes. Read the original input path
        // and verify its MVID so this checks that same built DLL, not a stale copy.
        // IL wiring only: never invoke rebirth/engage or any native Unity method.
        using (var built = AssemblyDefinition.ReadAssembly(Environment.GetCommandLineArgs()[1]))
        {
            Equal(assembly.ManifestModule.ModuleVersionId, built.MainModule.Mvid, "rebirth IL check uses the loaded injector build");
            var type = built.MainModule.GetType("NGUInjector.AllocationProfiles.RebirthStuff.BaseRebirth");
            var method = type.Methods.Single(x => x.Name == "DoRebirth");
            var calls = new List<string>();
            foreach (var instruction in method.Body.Instructions)
            {
                var target = instruction.Operand as MethodReference;
                if (target != null && (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) &&
                    new[] {"PreRebirth", "PrepareFinalRebirthAction", "RefreshFinalRebirthMultipliers", "TryStartChallenge", "EngageRebirth", "ReleaseFinalRebirthAction"}.Contains(target.Name))
                    calls.Add(target.Name);
            }
            Equal("PreRebirth,PrepareFinalRebirthAction,RefreshFinalRebirthMultipliers,TryStartChallenge,EngageRebirth,ReleaseFinalRebirthAction",
                string.Join(",", calls), "built rebirth requires selected final support and synchronous multiplier refresh before challenge or normal engagement");
            var refresh = built.MainModule.GetType("NGUInjector.Main").Methods.Single(x => x.Name == "RefreshFinalRebirthMultipliers");
            Equal("calculateTimeMulti,calculateNextMultis", string.Join(",", refresh.Body.Instructions.Where(x => x.OpCode == OpCodes.Ldstr)
                .Select(x => (string)x.Operand)), "shared final refresh retains both native cached multipliers in order");
            var refreshCalls = refresh.Body.Instructions.Select(x => x.Operand as MethodReference).Where(x => x != null).Select(x => x.Name).ToArray();
            var before = Array.IndexOf(refreshCalls, "VerifyExecutionPrerequisites"); var invoke = Array.IndexOf(refreshCalls, "Invoke");
            var after = Array.IndexOf(refreshCalls, "ValidateExecutionGrant");
            Equal(true, before >= 0 && invoke > before, "each native multiplier callback requires current setup proof");
            Equal(true, invoke >= 0 && after > invoke, "each native multiplier callback rechecks authority afterward");
        }
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly Type MainType;
        internal readonly object Character, Plan;
        internal readonly DateTime Stamp = DateTime.UtcNow.AddMinutes(5);
        private readonly object _main, _settings, _clock;
        private readonly Type _scheduler;
        private readonly Dictionary<FieldInfo, object> _saved = new Dictionary<FieldInfo, object>();
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "ngu-stage-support-" + Guid.NewGuid().ToString("N"));
        private readonly bool _setupBoundaryBefore = IronPillSetupBoundary.Enabled;

        internal Fixture(Assembly assembly, int count = 1)
        {
            MainType = assembly.GetType("NGUInjector.Main", true);
            _scheduler = assembly.GetType("NGUInjector.Managers.CustomStageAllocator", true);
            try
            {
                Directory.CreateDirectory(_directory);
                foreach (var name in new[] {"reference", "Character", "Controller", "Settings", "CustomPlanStore", "settingsForm", "OutputWriter", "_customAutomationStatus", "_dir"})
                {
                    var field = MainType.GetField(name, All);
                    _saved.Add(field, field.GetValue(null));
                    field.SetValue(null, null);
                }
                MainType.GetField("_dir", All).SetValue(null, _directory);
                MainType.GetMethod("SetCustomAutomationStatus", All).Invoke(null, new object[] { null });
                foreach (var type in new[] {_scheduler, assembly.GetType("NGUInjector.Managers.BlackBarAllocator", true),
                    assembly.GetType("NGUInjector.AllocationProfiles.RebirthStuff.CustomPlanRebirth", true)})
                foreach (var field in type.GetFields(All).Where(x => x.IsStatic && !x.IsInitOnly && !x.IsLiteral))
                    _saved.Add(field, field.GetValue(null));
                _main = New(MainType);
                NativeFightFixture.InitializeMain(_main);
                Set("_resourceExecutionOwner", ""); Set("_resourceExecutionReason", "");
                Character = New(MainType.GetField("Character", All).FieldType);
                _clock = Child(Character, "rebirthTime");
                StageSupportChecks.Set(Child(Character, "totalPlaytime"), "totalseconds", 100000d);
                StageSupportChecks.Set(Child(Character, "stats"), "rebirthNumber", 7L);
                StageSupportChecks.Set(Child(Character, "advancedTraining"), "level", new long[5]);
                var buttons = Child(Character, "buttons");
                Child(buttons, "beards");
                Child(buttons, "diggers");
                var trainingButton = Child(buttons, "advancedTraining");
                for (var type = trainingButton.GetType(); type != null; type = type.BaseType)
                {
                    var interactable = type.GetField("m_Interactable", All);
                    if (interactable != null) { interactable.SetValue(trainingButton, true); break; }
                }
                // The real manager validates model availability before the feature
                // lock. Supply empty data only; locked buttons prevent controller calls.
                var beards = Child(Character, "beards");
                StageSupportChecks.Set(beards, "beards", Activator.CreateInstance(beards.GetType().GetField("beards", All).FieldType));
                StageSupportChecks.Set(beards, "activeBeards", Activator.CreateInstance(beards.GetType().GetField("activeBeards", All).FieldType));
                Child(Character, "allBeards");
                _settings = Activator.CreateInstance(assembly.GetType("NGUInjector.SavedSettings", true), new object[] {null});
                Setting("UseCustomRebirthPlan", true);
                Setting("CustomPlanFile", "stage-support-check");
                Setting("ManageEnergy", false);
                Setting("ManageMagic", false);
                Setting("ManualAdventureEnabled", false);
                Plan = Activator.CreateInstance(assembly.GetType("NGUInjector.CustomPlans.CustomPlan", true));
                StageSupportChecks.Set(Plan, "Name", "stage-support");
                StageSupportChecks.Set(Plan, "AutoActivateFruits", false);
                StageSupportChecks.Set(Plan, "StageSchedulerEnabled", true);
                StageSupportChecks.Set(Plan, "TimeTriggerEnabled", false);
                StageSupportChecks.Set(Plan, "NumberClimbEnabled", false);
                StageSupportChecks.Set(Plan, "StageDurationSeconds", 100 * count);
                StageSupportChecks.Set(Plan, "StageLoopStartIndex", 0);
                var stageType = assembly.GetType("NGUInjector.CustomPlans.CustomPlanStage", true);
                var stages = Array.CreateInstance(stageType, count);
                for (var i = 0; i < count; i++)
                {
                    var stage = Activator.CreateInstance(stageType);
                    StageSupportChecks.Set(stage, "GearGoal", -1);
                    // Equipment-entry coverage uses flex deadlines, not a claim of
                    // successful protected allocation with resource managers disabled.
                    StageSupportChecks.Set(stage, "DurationSeconds", 0);
                    StageSupportChecks.Set(stage, "Percent", 1d);
                    stages.SetValue(stage, i);
                }
                StageSupportChecks.Set(Plan, "Stages", stages);
                MainType.GetField("Character", All).SetValue(null, Character);
                MainType.GetField("Settings", All).SetValue(null, _settings);
                MainType.GetField("reference", All).SetValue(null, _main);
                Set("_customIronPillPrepStartedAt", -1d);
            }
            catch { Dispose(); throw; }
        }

        internal object Get(string field) { return StageSupportChecks.Get(_main, field); }
        internal void Set(string field, object value) { StageSupportChecks.Set(_main, field, value); }
        internal object Call(string method, params object[] args) { return MainType.GetMethod(method, All).Invoke(_main, args); }
        internal void PrepareFight()
        {
            NativeFightFixture.Prepare(MainType.Assembly, Character);
            var store = New(MainType.GetField("CustomPlanStore", All).FieldType);
            var plans = Path.Combine(_directory, "plans"); Directory.CreateDirectory(plans); StageSupportChecks.Set(store, "_plansDir", plans);
            StageSupportChecks.Set(store, "<PresetLibrary>k__BackingField", Activator.CreateInstance(MainType.Assembly.GetType("NGUInjector.CustomPlans.PresetLibraryStore", true), All,
                null, new object[] { _directory, plans }, null));
            MainType.GetField("CustomPlanStore", All).SetValue(null, store);
            store.GetType().GetMethod("Save", All).Invoke(store, new object[] { "stage-support-check", Plan });
        }
        internal string Request(string command, string opportunity) => NativeFightFixture.Request(_main, _directory, command, opportunity);
        internal void AssertPreparedFight(string label)
        {
            Equal(true, Property(_main, "FightPreparationVerified"), label + " verifies actual selected native prerequisites");
            var claim = Property(_main, "FightPreparationClaim"); var process = Get("_bossPushProcess");
            Equal("fight-preparation", StageSupportChecks.Get(claim, "Owner"), label + " owns its actual admitted preparation claim");
            Equal(true, ReferenceEquals(claim, StageSupportChecks.Get(process, "Claim")), label + " boss process consumes the same preparation claim");
            var grant = Call("RouteProcessGrant", process, false);
            Equal(true, Call("ValidateExecutionGrant", grant, StageSupportChecks.Get(grant, "Domains")), label + " action grant validates through production ownership");
            Equal(true, ((IList)StageSupportChecks.Get(StageSupportChecks.Get(Character, "beards"), "activeBeards")).Contains(0), label + " actual hard-required Fu is active");
            var record = Get("_fightPreparationRecord");
            Equal("fight-requested", StageSupportChecks.Get(record, "After"), label + " durable preparation history precedes boss admission");
            var identity = StageSupportChecks.Get(record, "Identity");
            var hash = MainType.Assembly.GetType("NGUInjector.JsonFiles", true).GetMethod("ComputeRevision", All).Invoke(null,
                new object[] { System.Text.Encoding.UTF8.GetBytes((string)StageSupportChecks.Get(identity, "Key")) });
            Equal(true, File.ReadAllText(Path.Combine(_directory, "finite-actions-v1", hash + ".json")).Contains("fight-requested"), label + " real ledger file contains the admitted opportunity");
        }
        internal void Setting(string property, object value) { _settings.GetType().GetProperty(property, All).SetValue(_settings, value); }
        internal string Status { get { return (string) MainType.GetMethod("GetCustomAutomationStatus", All).Invoke(null, null); } }
        internal string EntryIdentity { get { return (string) Get("_protectedGeneration") + ":" + Property(Get("_protectedScheduler"), "Entry"); } }
        internal void Run(double seconds)
        {
            StageSupportChecks.Set(_clock, "totalseconds", seconds);
            StageSupportChecks.Set(StageSupportChecks.Get(Character, "totalPlaytime"), "totalseconds", seconds + 100000d);
            Call("RunCustomPlanAutomation", Plan);
            Equal(true, Get("_protectedCheckpointDurable"), "stage-support fixture retains a durable coordinator checkpoint");
            Equal(true, File.Exists((string) MainType.GetProperty("ProtectedCheckpointPath", All).GetValue(_main)), "stage-support fixture writes an actual checkpoint under temporary Main._dir");
        }
        internal void AssertCreditBlocked(double seconds)
        {
            var managed = (bool) _settings.GetType().GetProperty("ManageEnergy", All).GetValue(_settings);
            var externalInputs = IronPillSetupBoundary.Enabled;
            Equal(false, Call("ProtectedStageOwnershipAvailable", Plan), "unmanaged loop fixture cannot claim applied ownership");
            Setting("ManageEnergy", true);
            try
            {
                IronPillSetupBoundary.Enabled = true;
                IronPillReservationChecks.PrepareGearObservation(MainType.Assembly, Character);
                StageSupportChecks.Set(Child(Character, "machine"), "speedEnergy", 37L);
                Child(Character, "wandoos98");
                foreach (var name in new[] {"timeMachineController", "wandoos98Controller", "wishesController"})
                    StageSupportChecks.Set(Child(Character, name), "character", Character);
                var augs = Child(Character, "augmentsController");
                StageSupportChecks.Set(augs, "augments", Array.CreateInstance(augs.GetType().GetField("augments", All).FieldType.GetElementType(), 0));
                var ngu = Child(Character, "NGUController");
                StageSupportChecks.Set(ngu, "NGU", Array.CreateInstance(ngu.GetType().GetField("NGU", All).FieldType.GetElementType(), 0));
                var training = StageSupportChecks.Get(Character, "advancedTraining");
                StageSupportChecks.Set(training, "energy", new long[5]);
                StageSupportChecks.Set(training, "levelTarget", new[] {-1L, -1L, -1L, -1L, -1L});
                var controllers = Child(Character, "advancedTrainingController");
                var names = new[] {"defense", "attack", "block", "wandoosEnergy", "wandoosMagic"};
                for (var i = 0; i < names.Length; i++)
                {
                    var controller = Child(controllers, names[i]);
                    StageSupportChecks.Set(controller, "character", Character);
                    StageSupportChecks.Set(controller, "id", i);
                }
                var wishes = Child(Character, "wishes");
                var wishField = wishes.GetType().GetField("wishes", All);
                var list = (IList) Activator.CreateInstance(wishField.FieldType);
                for (var i = 0; i <= 190; i++) list.Add(New(wishField.FieldType.GetGenericArguments()[0]));
                wishField.SetValue(wishes, list);
                StageSupportChecks.Set(list[190], "level", 1); // Real free-AT cap, without native bonus/render calls.

                var runtime = Call("GetProtectedStage", Plan);
                var key = Call("ProtectedStageResourceKey", Plan);
                var core = Get("_protectedScheduler");
                var index = (int) Property(runtime, "StageIndex");
                var appliedBefore = ((double[]) Property(core, "AppliedSeconds"))[index];
                var idleBefore = (long) StageSupportChecks.Get(Character, "idleEnergy");
                Equal(false, Call("ProtectedStageOwnershipAvailable", Plan), "management alone cannot establish a current application receipt");
                Exception rejected = null;
                try { _scheduler.GetMethod("ApplyStage", All).Invoke(null, new[] {Plan, Property(runtime, "Stage"),
                    Property(runtime, "RemainingSeconds"), 0, key, Property(runtime, "EffectiveDurationSeconds")}); }
                catch (TargetInvocationException error) { rejected = error.InnerException; }
                Contains(rejected?.Message, "Support models unavailable", "missing native setup observation rejects before resource mutation");
                Equal(false, _scheduler.GetProperty("LastStageApplicationSucceeded", All).GetValue(null), "missing native setup cannot publish resource success");
                Equal(false, _scheduler.GetMethod("StageApplicationStillOwned", All).Invoke(null, new[] {key}), "failed setup cannot acquire a resource receipt");
                Equal(37L, StageSupportChecks.Get(StageSupportChecks.Get(Character, "machine"), "speedEnergy"), "rejected setup preserves actual native allocation");
                Equal(idleBefore, StageSupportChecks.Get(Character, "idleEnergy"), "rejected setup preserves exact idle energy");
                Equal(false, Call("ReconcileInlineStageSetup", Plan, runtime), "sparse native fixture cannot verify unavailable setup prerequisites");
                Equal(false, Call("InlineStageSetupVerified", Plan), "rejected resource application cannot manufacture native setup verification");
                Equal(false, Call("ProtectedStageOwnershipAvailable", Plan), "resource ownership alone cannot grant inline stage credit");
                Call("CommitProtectedStageApplication", Plan);
                var elapsed = (double) StageSupportChecks.Get(_clock, "totalseconds");
                var heartbeat = (double) Stopwatch.GetTimestamp() / Stopwatch.Frequency;
                Equal(false, StageSupportChecks.Get(core, "_armed"), "unverified setup prevents real application commit from arming the core");
                StageSupportChecks.Set(core, "_lastHeartbeat", heartbeat - seconds);
                StageSupportChecks.Set(_clock, "totalseconds", elapsed + seconds);
                StageSupportChecks.Set(StageSupportChecks.Get(Character, "totalPlaytime"), "totalseconds", elapsed + seconds + 100000d);
                Call("GetProtectedStage", Plan);
                Equal(appliedBefore, ((double[]) Property(core, "AppliedSeconds"))[index], "unverified setup earns no fixed credit after native resource rejection");
            }
            finally { Setting("ManageEnergy", managed); IronPillSetupBoundary.Enabled = externalInputs; }
        }
        internal void SeedSupport(string owner)
        {
            Set("_customEquipmentOwner", owner);
            Set("_customEquipmentLastDiggersAt", Stamp);
        }
        internal void AddRebirthData()
        {
            var rebirth = Child(Character, "rebirth");
            StageSupportChecks.Set(rebirth, "character", Character);
            var wishes = Child(Character, "wishes");
            var listType = wishes.GetType().GetField("wishes", All).FieldType;
            var list = (IList) Activator.CreateInstance(listType);
            for (var i = 0; i < 21; i++) list.Add(New(listType.GetGenericArguments()[0]));
            StageSupportChecks.Set(wishes, "wishes", list);
            Child(Child(Character, "challenges"), "noRebirthChallenge");
        }
        internal void ExpectSupportSkipped(string scenario)
        {
            var selection = Get("_inlineStageSelection");
            Equal(true, selection != null, scenario + " acquires inline intent independently of application evidence");
            Equal(Get("_customStageEquipmentKey"), StageSupportChecks.Get(selection, "Owner"), scenario + " retains the actual stage equipment owner");
            Equal(null, StageSupportChecks.Get(selection, "Applied"), scenario + " leaves unavailable setup unapplied");
            Equal(false, Call("InlineStageSetupVerified", Plan), scenario + " cannot claim verification from unavailable support");
            Contains(Status, "Inline setup waiting", scenario + " reports shared setup deferral");
        }
        internal void ExpectSupportCleared(string scenario)
        {
            Equal(null, Get("_customEquipmentOwner"), scenario + " clears support owner");
            Equal(null, Get("_customSupportIntent"), scenario + " cannot retain selected support from unavailable models");
        }
        public void Dispose()
        {
            IronPillSetupBoundary.Enabled = _setupBoundaryBefore;
            try
            {
                foreach (var saved in _saved) saved.Key.SetValue(null, saved.Value);
            }
            finally { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
        }
    }

    private static object New(Type type)
    {
        var value = FormatterServices.GetUninitializedObject(type);
        // Match the reservation fixture: Unity null checks see a managed object,
        // but this sentinel must never be passed to any native Unity method.
        for (var current = type; current != null; current = current.BaseType)
        {
            var pointer = current.GetField("m_CachedPtr", All);
            if (pointer != null) { pointer.SetValue(value, new IntPtr(1)); break; }
        }
        return value;
    }
    private static object Get(object target, string field) { return target.GetType().GetField(field, All).GetValue(target); }
    private static object Property(object target, string property) { return target.GetType().GetProperty(property, All).GetValue(target); }
    private static void Set(object target, string field, object value) { target.GetType().GetField(field, All).SetValue(target, value); }
    private static object Child(object parent, string field)
    {
        var value = New(parent.GetType().GetField(field, All).FieldType);
        Set(parent, field, value);
        return value;
    }
    private static void Equal(object expected, object actual, string scenario)
    {
        _assertions++;
        if (!Equals(expected, actual)) throw new Exception(scenario + ": expected " + expected + ", got " + actual);
    }
    private static void Contains(string value, string expected, string scenario)
    {
        Equal(true, value != null && value.Contains(expected), scenario + ": " + value);
    }
}
