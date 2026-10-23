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

// Compiled production coordinator, scheduler, rebirth policy and store.
// Manual credit checks isolate persistence; application checks execute the real
// allocation/commit path against data-only game controllers on a non-rendered menu.
internal static class ProtectedTimingIntegrationChecks
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static int _assertions;

    internal static Assembly LoadInjector(string path)
    {
        using (var assembly = AssemblyDefinition.ReadAssembly(path))
        using (var bytes = new MemoryStream())
        {
            Console.WriteLine("Production MVID: " + assembly.MainModule.Mvid);
            NativeSettingsJsonBoundary.Configure(assembly);
            foreach (var preparation in assembly.MainModule.GetType("NGUInjector.Main").Methods.Where(x => new[] {
                "RunFightPrep", "RunManualFightPrep", "ContinueManualFightPreparation" }.Contains(x.Name)))
            {
                foreach (var message in preparation.Body.Instructions.Where(x => x.Operand is MethodReference call
                    && call.DeclaringType.FullName == "System.Exception" && call.Name == "get_Message").ToArray())
                {
                    // Observe the original caught exception without changing its handling.
                    preparation.Body.GetILProcessor().InsertBefore(message, Instruction.Create(OpCodes.Dup));
                    preparation.Body.GetILProcessor().InsertBefore(message, Instruction.Create(OpCodes.Call,
                        assembly.MainModule.ImportReference(typeof(Console).GetMethod("WriteLine", new[] { typeof(object) }))));
                }
                foreach (var instruction in preparation.Body.Instructions.Where(x => x.Operand is Instruction && x.OpCode.Code.ToString().EndsWith("_S")))
                {
                    var name = instruction.OpCode.Code.ToString();
                    instruction.OpCode = (OpCode)typeof(OpCodes).GetField(name.Substring(0, name.Length - 2)).GetValue(null);
                }
                preparation.Body.MaxStackSize++;
            }
            var dispatch = assembly.MainModule.GetType("NGUInjector.Main").Methods.Single(x => x.Name == "ProcessExternalUiCommand");
            var execute = dispatch.Body.Instructions.Single(x => x.Operand is MethodReference call && call.Name == "ExecuteExternalUiCommand");
            var duplicateMessage = Instruction.Create(OpCodes.Dup); dispatch.Body.GetILProcessor().InsertAfter(execute, duplicateMessage);
            dispatch.Body.GetILProcessor().InsertAfter(duplicateMessage, Instruction.Create(OpCodes.Call,
                assembly.MainModule.ImportReference(typeof(NativeFightFixture).GetMethod(nameof(NativeFightFixture.ObserveCommandMessage)))));
            foreach (var instruction in dispatch.Body.Instructions.Where(x => x.Operand is Instruction && x.OpCode.Code.ToString().EndsWith("_S")))
            {
                var name = instruction.OpCode.Code.ToString();
                instruction.OpCode = (OpCode)typeof(OpCodes).GetField(name.Substring(0, name.Length - 2)).GetValue(null);
            }
            dispatch.Body.MaxStackSize++;
            var pill = assembly.MainModule.GetType("NGUInjector.Main").Methods.Single(x => x.Name == "TryCastCustomIronPill");
            var invokePill = pill.Body.Instructions.Single(x => x.Operand is MethodReference call && call.Name == "RunFiniteAction");
            var duplicateResult = Instruction.Create(OpCodes.Dup); pill.Body.GetILProcessor().InsertAfter(invokePill, duplicateResult);
            pill.Body.GetILProcessor().InsertAfter(duplicateResult, Instruction.Create(OpCodes.Call,
                assembly.MainModule.ImportReference(typeof(NativeFightFixture).GetMethod(nameof(NativeFightFixture.ObserveFiniteResult)))));
            pill.Body.MaxStackSize++;
            var classifier = assembly.MainModule.GetType("NGUInjector.Main").NestedTypes.Single(x => x.Name == "PillBloodAction")
                .Methods.Single(x => x.Name == "Classify");
            var classifierEntry = classifier.Body.Instructions[0];
            classifier.Body.GetILProcessor().InsertBefore(classifierEntry, Instruction.Create(OpCodes.Ldarg_0));
            classifier.Body.GetILProcessor().InsertBefore(classifierEntry, Instruction.Create(OpCodes.Call,
                assembly.MainModule.ImportReference(typeof(NativeFightFixture).GetMethod(nameof(NativeFightFixture.ObservePillProof)))));
            var method = assembly.MainModule.GetType("NGUInjector.Main").Methods.Single(x => x.Name == "QuickLoad");
            var il = method.Body.GetILProcessor();
            foreach (var instruction in method.Body.Instructions.ToArray())
            {
                var call = instruction.Operand as MethodReference;
                var boundary = call?.DeclaringType.FullName == "System.Windows.Forms.MessageBox" && call.Name == "Show" ? "Confirm"
                    : call?.DeclaringType.FullName == "UnityEngine.Application" && call.Name == "get_platform" ? "Platform"
                    : call?.Name == "getSaveDataFromString" ? "DecodeSave"
                    : call?.Name == "getDataFromString" ? "DecodeData"
                    : call?.Name == "getVersion" ? "Version"
                    : call?.Name == "loadintoGame" ? "Load" : null;
                if (boundary == null) continue;
                instruction.OpCode = OpCodes.Call;
                instruction.Operand = assembly.MainModule.ImportReference(typeof(ProtectedTimingLoadBoundary).GetMethod(boundary));
                if (boundary == "DecodeSave" || boundary == "DecodeData")
                    il.InsertAfter(instruction, il.Create(OpCodes.Castclass, call.ReturnType));
            }
            assembly.Write(bytes);
            return Assembly.Load(bytes.ToArray());
        }
    }

    internal static int RunSchedulingRegressions(Assembly assembly)
    {
        var before = _assertions;
        var failures = new List<Exception>();
        foreach (var change in new[] { "permission", "structure", "trigger" })
            Check("automatic preparation retires after " + change, () => CheckRetiredPreparation(assembly, change), failures);
        Check("readiness observation preserves valid automatic and explicit manual preparation", () => CheckPreparationObservationGuards(assembly), failures);
        foreach (var prior in new[] { "running", "completed", "manual", "loadout", "digger", "plan-error" })
            Check("manual fight preparation handoff from " + prior, () => CheckManualFightPreparationHandoff(assembly, prior), failures);
        foreach (var callback in new[] { "QuickStuff", "AutomationRoutine" })
            Check(callback + " confirms NUMBER before automatic repeat admission", () => CheckAutomaticNumberObservation(assembly, callback), failures);
        foreach (var outcome in new[] { "missing", "cancelled", "locked", "decode-error", "old-version", "future-version", "null-data", "accepted", "native-throw" })
            Check("QuickLoad " + outcome, () => CheckQuickLoadBoundary(assembly, outcome), failures);
        if (failures.Count != 0)
            throw new AggregateException("Scheduling regressions: " + failures.Count + " failed after " + _assertions + " assertions.", failures);
        Console.WriteLine("Scheduling regressions passed: " + (_assertions - before) + " assertions.");
        return _assertions - before;
    }

    private static void CheckRetiredPreparation(Assembly assembly, string change)
    {
        using (var f = new Fixture(assembly, 600))
        {
            f.Setting("CustomAutoBoostEnabled", false);
            Set(Child(Get(f.Character, "buttons"), "yggdrasil"), "m_Interactable", false);
            Set(Child(f.Character, "bossController"), "isFighting", false);
            f.PrepareFight();
            f.Save();
            f.Run();
            f.Override("skip", 0);
            Equal(true, f.Call("RunCustomRebirthAutomation", f.Plan), "actual ready rebirth starts preparation before engagement");
            Equal(true, f.Get("_customPreRebirthPrepRunning"), "actual prep acquires its running state");
            var core = f.Core;
            if (change == "permission") Set(f.Plan, "AutoRebirthEnabled", false);
            else if (change == "trigger") Set(f.Plan, "TimeTriggerSeconds", 10000);
            else
            {
                Set(f.Stage(0), "CompletionMode", 1);
                Set(f.Stage(0), "TargetAdvancedTrainingPower", 10L);
            }
            f.Save();
            f.Call("StopBossPush");
            f.Call("AutomationRoutine");
            Equal(false, f.Get("_customPreRebirthPrepRunning"), "withdrawn readiness retires automatic prep rather than permanently blocking the plan");
            Equal(false, f.Get("_customPreRebirthPrepCompleted"), "withdrawn readiness cannot retain a completed preparation receipt");
            Equal(false, f.Get("_manualRebirthPending"), "automatic prep retirement never invents a manual request");
            Equal(7L, Get(Get(f.Character, "stats"), "rebirthNumber"), "retiring prep never engages rebirth");
            Equal(true, f.Call("InlineStageSetupVerified", f.Load()), "actual ordinary callback resumes verified stage setup");
            Equal(true, f.ApplicationSucceeded, "actual ordinary callback resumes resource application");
            Equal(true, ReferenceEquals(core, f.Core) || core == null, "retirement does not replace scheduler state on its own");
        }
    }

    private static void CheckAutomaticNumberObservation(Assembly assembly, string callback)
    {
        using (var f = new Fixture(assembly, 60))
        {
            f.Setting("CustomAutoBoostEnabled", false);
            Set(Child(Get(f.Character, "buttons"), "yggdrasil"), "m_Interactable", false);
            Set(Child(f.Character, "bossController"), "isFighting", false);
            Set(f.Plan, "TimeTriggerEnabled", false);
            Set(f.Plan, "NumberTriggerEnabled", true);
            Set(f.Plan, "StageDurationSeconds", 86400);
            Set(f.Plan, "StageLoopStartIndex", 0);
            Set(f.Stage(0), "ExecutionMode", 1);
            f.Save();
            f.Lock("digger", true); // Real automatic-stage ownership wait, not a substituted dispatcher.
            f.Call(callback);
            var entry = Property(f.Core, "Entry");
            var deadline = Property(f.Core, "ExpectedEndSeconds");
            f.Advance(20d);
            f.Call(callback);
            Equal(true, f.Rebirth("PeekTriggersAvailable"), callback + " advances NUMBER confirmation despite the automatic-stage early return");
            Equal(entry, Property(f.Core, "Entry"), "confirmation does not renew the current automatic entry");
            Equal(deadline, Property(f.Core, "ExpectedEndSeconds"), "confirmation preserves the original automatic deadline");
            f.Advance(40d);
            f.Call(callback);
            Equal(true, Property(f.Core, "Finished"), "confirmed trigger finishes the current pass without another obligation");
            Equal(0, Property(f.Core, "Cycle"), "ready NUMBER does not admit another automatic-only cycle");
            Equal(entry, Property(f.Core, "Entry"), "completed pass has no extra entry or entry action");
        }
    }

    private static void CheckPreparationObservationGuards(Assembly assembly)
    {
        foreach (var manual in new[] { false, true })
        using (var f = new Fixture(assembly, 600))
        {
            Set(Child(f.Character, "bossController"), "isFighting", false);
            f.PrepareFight();
            f.Save(); f.Run(); f.Override("skip", 0);
            if (manual)
            {
                f.Setting("AutoRebirth", false);
                f.Request("manualRebirth", "observed-manual-rebirth");
            }
            else f.Call("RunCustomRebirthAutomation", f.Plan);
            var core = f.Parse(f.CoreState());
            f.Call("QuickStuff");
            Equal(true, f.Get("_customPreRebirthPrepRunning"), "readiness observation preserves an admitted " + (manual ? "manual" : "automatic") + " preparation");
            Equal(true, f.Get("_bossPushRunning"), "observation does not stop a still-valid push");
            Equal(manual, f.Get("_manualRebirthPending"), "manual intent remains independent of automatic permission");
            Equal(7L, Get(Get(f.Character, "stats"), "rebirthNumber"), "observation alone never engages rebirth");
            var after = f.Parse(f.CoreState());
            foreach (var field in new[] { "entry", "cycle", "enteredAt", "expectedEnd", "earned", "applied", "visits" })
                Equal(Json(core, field).ToString(), Json(after, field).ToString(), "busy preparation observation adds no scheduler credit or entry/" + field);
        }
    }

    private static void CheckQuickLoadBoundary(Assembly assembly, string outcome)
    {
        using (var f = new Fixture(assembly, 2, 600))
        {
            f.Run(); f.Credit(); f.Credit();
            Equal(1, Property(f.Core, "StageIndex"), "QuickLoad fixture has reached a later stage");
            Equal(2d, f.Earned(1), "QuickLoad fixture has actual partial work to preserve");
            f.Call("SaveProtectedStageProgress", true);
            var core = f.Core; var state = f.CoreState(); var generation = f.Get("_protectedGeneration");
            var files = f.Files();
            var checkpoint = f.Checkpoint; var checkpointText = File.ReadAllText(checkpoint);
            var path = Path.Combine(f.DirectoryPath, "NGUSave.txt");
            var decoder = Child(f.Character, "importExport"); Child(f.Character, "saveLoad");
            ProtectedTimingLoadBoundary.Save = New(decoder.GetType().GetMethod("getSaveDataFromString", All).ReturnType);
            ProtectedTimingLoadBoundary.Data = New(decoder.GetType().GetMethod("getDataFromString", All).ReturnType);
            Set(ProtectedTimingLoadBoundary.Data, "version", outcome == "old-version" ? 360 : outcome == "future-version" ? 401 : 400);
            if (outcome == "null-data") ProtectedTimingLoadBoundary.Data = null;
            ProtectedTimingLoadBoundary.Cancel = outcome == "cancelled";
            ProtectedTimingLoadBoundary.ThrowDecode = outcome == "decode-error";
            ProtectedTimingLoadBoundary.Loads = 0;
            var resetBeforeLoad = false;
            ProtectedTimingLoadBoundary.Loading = () => {
                resetBeforeLoad = f.Get("_executionLifecycle").ToString() == "LoadingSave"
                    && (bool)f.Get("_protectedTimingLoadingSave") && !(bool)Get(f.Core, "_armed");
                if (outcome != "native-throw") return;
                Set(Get(f.Character, "stats"), "rebirthNumber", 8L);
                Set(Get(f.Character, "rebirthTime"), "totalseconds", 1d);
                throw new IOException("offline native load mutated the save before failing");
            };
            FileStream locked = null;
            try
            {
                if (outcome != "missing") File.WriteAllText(path, "offline fixture save");
                if (outcome == "cancelled") File.SetLastWriteTime(path, DateTime.Now.AddMinutes(-3));
                if (outcome == "locked") locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                f.Call("QuickLoad");
                Equal(outcome == "accepted" || outcome == "native-throw" ? 1 : 0, ProtectedTimingLoadBoundary.Loads, "only accepted saves reach the native load boundary; " + f.LogText);
                if (outcome == "accepted" || outcome == "native-throw")
                {
                    Equal(true, resetBeforeLoad, "native load cannot observe old scheduler authority");
                    Equal(outcome == "accepted" ? "Running" : "LoadingSave", f.Get("_executionLifecycle").ToString(), "only completed native loading restores ordinary execution admission");
                    if (outcome == "accepted") Equal(null, f.Core, "accepted replacement invalidates the scheduler application");
                    if (outcome == "native-throw")
                    {
                        Equal(8L, Get(Get(f.Character, "stats"), "rebirthNumber"), "failed native loading does not pretend to roll back committed game changes");
                        Equal(1d, Get(Get(f.Character, "rebirthTime"), "totalseconds"), "failed native loading retains the changed native clock");
                        Equal(false, f.Gate(), "partially mutated native loading cannot reopen the old scheduler gate");
                    }
                }
                else
                {
                    Equal(true, ReferenceEquals(core, f.Core), outcome + " preserves the actual scheduler instance");
                    Equal(state, f.CoreState(), outcome + " preserves earned work, entry actions and history");
                    Equal(generation, f.Get("_protectedGeneration"), outcome + " preserves generation identity");
                    Equal("Running", f.Get("_executionLifecycle").ToString(), outcome + " preserves ordinary current-session admission");
                }
            }
            finally
            {
                locked?.Dispose();
                if (File.Exists(path)) File.Delete(path);
                ProtectedTimingLoadBoundary.Save = ProtectedTimingLoadBoundary.Data = null;
                ProtectedTimingLoadBoundary.Loading = null;
                ProtectedTimingLoadBoundary.Cancel = ProtectedTimingLoadBoundary.ThrowDecode = false;
            }
            Equal(checkpointText, File.ReadAllText(checkpoint), "load admission preserves the progress checkpoint bytes");
            if (outcome != "accepted" && outcome != "native-throw") Equal(files, f.Files(), "rejected load adds no execution evidence or checkpoint writes");
        }
    }

    private static void CheckManualFightPreparationHandoff(Assembly assembly, string prior)
    {
        using (var f = new Fixture(assembly, 600))
        {
            Set(Child(f.Character, "bossController"), "isFighting", false);
            f.PrepareFight();
            f.Save(); f.Run(); f.Override("skip", 0);
            var rejected = prior == "loadout" || prior == "digger" || prior == "plan-error";
            if (prior == "manual" || rejected) f.Request("manualRebirth", "initial-rebirth");
            else f.Call("RunCustomRebirthAutomation", f.Plan);
            Equal(true, f.Get("_bossPushRunning"), "handoff starts with an actual owned preparation push");
            if (prior == "completed")
            {
                f.Call("StopBossPush");
                f.Call("RunCustomPreRebirthPrepIfNeeded", f.Plan);
                Equal(true, f.Get("_customPreRebirthPrepCompleted"), "completed-prep handoff starts with a real completion receipt");
            }
            var running = f.Get("_customPreRebirthPrepRunning");
            var completed = f.Get("_customPreRebirthPrepCompleted");
            var pending = f.Get("_manualRebirthPending");
            var starts = f.BossPushStarts;
            var state = f.Parse(f.CoreState());
            if (prior == "loadout") Set(Get(f.Character, "inventoryController"), "midDrag", true);
            if (prior == "digger") Set(Get(f.Character, "beards"), "disabled", true);
            if (prior == "plan-error") File.Delete(f.PlanPath);
            try
            {
                var summary = f.Request("manualFightPrep", "manual-fight-takeover");
                if (prior == "plan-error") throw new Exception("Missing plan unexpectedly admitted manual fight takeover.");
                Contains(summary, prior == "loadout" ? "preparation blocked: Support is locked or inventory drag is active."
                    : rejected ? "setup waiting" : "boss push running", "handoff reports actual admission");
            }
            catch (InvalidOperationException error)
            {
                if (prior != "plan-error" || !error.Message.Contains("Plan does not exist: protected-integration")) throw;
            }
            if (rejected)
            {
                Equal(pending, f.Get("_manualRebirthPending"), "rejected takeover retains the previous explicit rebirth intent");
                Equal(running, f.Get("_customPreRebirthPrepRunning"), "rejected takeover retains preparation ownership");
                Equal(completed, f.Get("_customPreRebirthPrepCompleted"), "rejected takeover retains preparation completion state");
                Equal(starts, f.BossPushStarts, "rejected takeover starts no replacement push");
                Equal(true, f.Get("_bossPushRunning"), "rejected takeover leaves the previous push running");
            }
            else
            {
                Equal(starts + 1, f.BossPushStarts, "successful takeover starts exactly one replacement push");
                f.Setting("AutoRebirth", false);
                foreach (var callback in new[] { "QuickStuff", "AutomationRoutine" })
                {
                    f.Call(callback);
                    Equal(true, f.Get("_bossPushRunning"), "disabling automatic rebirth must not stop the replacement manual fight push/" + callback);
                }
                Equal(false, f.Get("_manualRebirthPending"), "successful manual fight takeover retires the old rebirth request");
                Equal(false, f.Get("_customPreRebirthPrepRunning"), "manual fight push cannot retain automatic prep ownership");
                Equal(false, f.Get("_customPreRebirthPrepCompleted"), "manual fight push cannot retain an old preparation receipt");
            }
            Equal(7L, Get(Get(f.Character, "stats"), "rebirthNumber"), "handoff cannot engage rebirth");
            var after = f.Parse(f.CoreState());
            foreach (var field in new[] { "entry", "cycle", "enteredAt", "expectedEnd", "earned", "applied", "visits" })
                Equal(Json(state, field).ToString(), Json(after, field).ToString(), "manual fight handoff preserves scheduler obligations/" + field);
        }
    }

    internal static int Run(Assembly assembly)
    {
        var main = assembly.GetType("NGUInjector.Main", true);
        if (main.GetMethod("GetProtectedStage", All) == null || assembly.GetType("NGUInjector.Managers.ProtectedStageScheduler") == null)
            throw new InvalidOperationException("Production protected timing build is not ready. Wait for the parent build; do not rebuild production from these checks.");
        if ((int) Get(Activator.CreateInstance(assembly.GetType("NGUInjector.CustomPlans.CustomPlan", true)), "Version") != 17)
            throw new InvalidOperationException("Production schema-17 build is not ready. Wait for the parent build; do not rebuild production from these checks.");
        if (main.GetMethod("OverrideCustomStage", All) == null
            || assembly.GetType("NGUInjector.Managers.ProtectedStageScheduler", true).GetProperty("Skipped", All) == null)
            throw new InvalidOperationException("Production manual-stage/codec-2 build is not ready. Wait for the parent build; do not rebuild production from these checks.");
        _assertions = 0;
        var game = main.GetField("Character", All).FieldType.Assembly;
        Equal(true, game.GetType("AdvancedTrainingController", true).GetMethod("refresh", All).GetMethodBody().GetILAsByteArray().SequenceEqual(new byte[] {0x2a}),
            "common loader substitutes only the inspected AT rendering boundary before real application checks");
        var failures = new List<Exception>();
        foreach (var mode in new[] {0, 1})
        foreach (var trigger in new[] {"time", "NUMBER", "climb", "all"})
            Check((mode == 0 ? "ANY " : "ALL ") + trigger + " late activation and shared rebirth gate", () => CheckTriggers(assembly, mode, trigger), failures);
        Check("read-only static snapshots and runtime capability", () => CheckReadOnly(assembly), failures);
        Check("zero-budget all-flex terminal progress", () => CheckZeroFlex(assembly), failures);
        Check("same-epoch restore without offline credit", () => CheckRestore(assembly), failures);
        foreach (var whileLoading in new[] {true, false})
            Check("cloud-load observation " + (whileLoading ? "during" : "after") + " loading restores saved progress",
                () => CheckObservedRestore(assembly, whileLoading), failures);
        foreach (var reset in new[] {"application invalidation", "runtime reset"})
            Check("cloud-load " + reset + " preserves original checkpoint commitments",
                () => CheckObservedRestoreAfterReset(assembly, reset), failures);
        foreach (var mismatch in new[] {"counter", "rollback", "origin"})
            Check(mismatch + " checkpoint rejection after cloud-load observation", () => CheckRejectedEpoch(assembly, mismatch), failures);
        Check("corrupt same-epoch checkpoint recovery", () => CheckCorruptCheckpoints(assembly), failures);
        Check("pause, invalidation and ownership locks", () => CheckSuspension(assembly), failures);
        Check("schema-17 universal allocator dispatch", () => CheckUniversalDispatch(assembly), failures);
        Check("universal nominal weights and AT metadata", () => CheckNominalDurations(assembly), failures);
        Check("stored edits preserve the actual applied entry", () => CheckStoredEdits(assembly), failures);
        Check("disk failure fails the rebirth gate closed", () => CheckDiskFailure(assembly), failures);
        Check("duration edits preserve entry identity", () => CheckDurationEdit(assembly), failures);
        Check("shorter stored plan resets an old tail selection", () => CheckShorterPlan(assembly), failures);
        Check("protected tail blocks rebirth during active flex", () => CheckProtectedTail(assembly), failures);
        foreach (var tailDuration in new[] {4, 2})
            Check("real application 10s/" + tailDuration + "s transitions", () => CheckAppliedTransitions(assembly, tailDuration), failures);
        Check("real fixed/flex/fixed/AT application transitions", () => CheckAppliedMixedTransitions(assembly), failures);
        Check("real failed, locked and unmanaged applications freeze credit", () => CheckApplicationFailures(assembly), failures);
        Check("native Energy NGU profile interleaves main and quick allocation", () => CheckInterleavedNguProfile(assembly), failures);
        Check("real fight once per entry, edit and loop", () => CheckAppliedFightEntry(assembly), failures);
        Check("manual skip defers actual application and publishes read-only history", () => CheckManualSkip(assembly), failures);
        Check("manual forward selection skips only prior required work", () => CheckManualForward(assembly), failures);
        Check("manual restart/back preserves durable cumulative history", () => CheckManualRestartAndRestore(assembly), failures);
        foreach (var guard in new[] {"entry", "revision", "name", "disabled", "paused", "boss", "controller-fight", "prep", "prep-complete", "manual-rebirth", "focus"})
            Check("manual override rejects " + guard + " before mutation", () => CheckManualGuard(assembly, guard), failures);
        Check("manual override rejects invalid argument types and targets", () => CheckManualInvalidArguments(assembly), failures);
        Check("manual persistence failure rolls back without a deferred override", () => CheckManualSaveFailure(assembly), failures);
        foreach (var action in new[] {"restart", "set"})
            Check("terminal skip does not refight; " + action + " opens one new fight", () => CheckTerminalSkipFightReceipt(assembly, action), failures);
        foreach (var owner in new[] {"loadout", "digger", "manual-adventure", "manual-overlay"})
            Check("manual override preserves " + owner + " ownership accounting", () => CheckOverrideOwnershipAccrual(assembly, owner), failures);
        Check("failed completed restart preserves durable gate and truthful timing reason", () => CheckCompletedRestartFailure(assembly), failures);
        Check("setPlanStage dispatcher requires startup and a real lease", () => CheckManualDispatcher(assembly), failures);
        Check("codec-2 rejects ghost finished/skipped metadata", () => CheckManualCheckpointShapes(assembly), failures);
        Check("deleted/recreated plans cannot inherit stale manual progress", () => CheckRecreatedPlan(assembly), failures);
        Check("scheduling callback and save admission regressions", () => RunSchedulingRegressions(assembly), failures);
        if (failures.Count != 0)
            throw new AggregateException("Protected timing integration: " + failures.Count + " scenario(s) failed after " + _assertions + " assertions.", failures);
        Console.WriteLine("Protected timing integration checks passed: " + _assertions + " assertions.");
        return _assertions;
    }

    private static void Check(string name, Action action, List<Exception> failures)
    {
        try { action(); }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL native scenario " + name + ": " + error.Message);
            failures.Add(new Exception(name, error));
        }
    }

    private static void CheckTriggers(Assembly assembly, int mode, string trigger)
    {
        using (var f = new Fixture(assembly, 4))
        {
            var label = (mode == 0 ? "ANY " : "ALL ") + trigger;
            var triggerReady = trigger != "climb";
            Set(f.Plan, "TriggerMode", mode);
            Set(f.Plan, "TimeTriggerEnabled", trigger == "time" || trigger == "all");
            Set(f.Plan, "NumberTriggerEnabled", trigger == "NUMBER" || trigger == "all");
            Set(f.Plan, "NumberClimbEnabled", trigger == "climb" || trigger == "all");
            var runtime = f.Start();
            Equal(0, Property(runtime, "StageIndex"), label + " late activation starts at the first protected row");
            Equal(0d, f.Earned(0), label + " does not infer historical training from late rebirth time");
            Equal(false, f.Gate(), label + " shared gate starts closed");
            f.ConfirmNumber();
            Equal(triggerReady, f.Rebirth("PeekTriggersAvailable"), label + " only active time/NUMBER triggers can be ready");
            Equal(false, f.Rebirth("PeekRebirthAvailable"), label + " peek cannot bypass protected work");
            Equal(false, f.Rebirth("RebirthAvailable"), label + " mutating readiness cannot bypass protected work");
            f.Credit();
            Equal(2d, f.Earned(0), label + " credits exactly two applied seconds");
            Equal(false, f.Gate(), label + " partial work remains blocked");
            f.Credit();
            Equal(4d, f.Earned(0), label + " completes only actual applied work");
            Equal(true, Property(f.Core, "ProtectedWorkComplete"), label + " real core completes");
            f.RequireCheckpoint(label + " coordinator durably commits completion");
            var checkpoint = f.Parse(File.ReadAllText(f.Checkpoint));
            Equal("7", JsonValue(checkpoint, "rebirth"), label + " persists the game rebirth counter");
            Equal("true", Json(Json(checkpoint, "scheduler"), "finished").ToString(), label + " persists scheduler completion");
            Equal(true, f.Gate(), label + " shared gate opens after durable completion");
            Equal(triggerReady, f.Rebirth("PeekTriggersAvailable"), label + " completed protected work cannot enable a retired trigger");
            Equal(triggerReady, f.Rebirth("PeekRebirthAvailable"), label + " peek readiness follows active triggers after durable completion");
            Equal(triggerReady, f.Rebirth("RebirthAvailable"), label + " actual rebirth readiness follows active triggers after durable completion");
            Equal("true", Json(f.Progress(), "protectedWorkComplete").ToString(), label + " telemetry shares the durable gate");
        }
    }

    private static void CheckReadOnly(Assembly assembly)
    {
        using (var f = new Fixture(assembly, 0, 4))
        {
            Set(f.Stage(0), "CompletionMode", 1);
            Set(f.Stage(0), "TargetAdvancedTrainingPower", 10L);
            var files = f.Files();
            f.Snapshot();
            f.Progress();
            Equal(null, f.Core, "static snapshot and progress do not initialize the coordinator");
            Equal(files, f.Files(), "uninitialized getters do not create sidecars");
            f.Start();
            ((long[]) Get(Get(f.Character, "advancedTraining"), "level"))[1] = 10L;
            f.Advance(2d);
            if (File.Exists(f.Checkpoint))
                File.SetLastWriteTimeUtc(f.Checkpoint, new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var state = f.CoreState();
            files = f.Files();
            var mainState = f.CoordinatorState();
            var confirmation = f.ConfirmationState();
            for (var i = 0; i < 3; i++)
            {
                Equal(0, Property(f.Snapshot(), "ActiveStageIndex"), "static getter does not consume a newly met AT gate");
                var progress = f.Progress();
                Equal("running", JsonValue(progress, "status"), "protected telemetry remains available");
                Equal("stages", JsonValue(progress, "schedulerMode"), "progress selects the sole stage coordinator");
                Equal("0", Json(progress, "activeStageIndex").ToString(), "progress does not advance the met AT gate");
                Equal("false", Json(progress, "protectedWorkComplete").ToString(), "pending protected work remains visible");
                Equal("1", Json(f.Call("BuildExternalUiRuntimeState"), "stageTimingVersion").ToString(), "actual runtime advertises unified stage timing capability");
                Equal(false, f.Gate(), "static shared gate is read-only and remains closed");
            }
            Equal(state, f.CoreState(), "all getters leave the actual scheduler byte-for-byte unchanged");
            Equal(mainState, f.CoordinatorState(), "all getters preserve coordinator epoch and persistence fields");
            Equal(confirmation, f.ConfirmationState(), "runtime queries do not mutate NUMBER confirmation");
            Equal(files, f.Files(), "getters preserve all temporary files, bytes and write timestamps");
            f.RequireCheckpoint("read-only fixture retains its real sidecar");
            Equal(1, Property(f.Start(), "StageIndex"), "allocator call, unlike getters, consumes the real met AT gate");
        }
    }

    private static void CheckZeroFlex(Assembly assembly)
    {
        using (var f = new Fixture(assembly, 0, 0))
        {
            Set(f.Plan, "StageDurationSeconds", 0);
            Set(f.Plan, "TimeTriggerEnabled", false);
            Equal(null, f.Start(), "zero-budget all-flex run has no active allocation");
            Equal(-1, Property(f.Core, "StageIndex"), "terminal core explicitly reports index -1");
            var progress = f.Progress();
            Equal("running", JsonValue(progress, "status"), "terminal index -1 is running, not unavailable");
            Equal("-1", Json(progress, "activeStageIndex").ToString(), "terminal progress preserves index -1");
            Equal("true", Json(progress, "scheduleComplete").ToString(), "terminal progress reports schedule completion");
            Equal("0", Json(progress, "protectedSecondsRemaining").ToString(), "no protected time remains");
            var state = f.CoreState();
            var files = f.Files();
            f.Snapshot();
            f.Progress();
            Equal(state, f.CoreState(), "terminal getters preserve scheduler state");
            Equal(files, f.Files(), "terminal getters do not rewrite checkpoints");
            Equal(true, f.Gate(), "vacuously completed protected work is durably committed");
            Equal("true", Json(progress, "protectedWorkComplete").ToString(), "terminal progress reports protected completion");
        }
    }

    private static void CheckRestore(Assembly assembly)
    {
        using (var f = new Fixture(assembly, 8))
        {
            f.Start();
            f.Credit();
            f.Call("SaveProtectedStageProgress", true);
            f.RequireCheckpoint("same-epoch restore fixture has saved actual earned work");
            var generation = f.Get("_protectedGeneration");
            var entry = Property(f.Core, "Entry");
            f.Advance(3600d);
            f.ReplaceMain();
            f.Start();
            Equal(generation, f.Get("_protectedGeneration"), "same-epoch reload retains checkpoint generation");
            Equal(entry, Property(f.Core, "Entry"), "same-epoch reload retains entry identity");
            Equal(2d, f.Earned(0), "one offline hour adds no protected credit");
            Equal(false, Get(f.Core, "_armed"), "restored checkpoint is always unarmed");
            Contains((string) f.Get("_protectedRecoveryReason"), "Restored saved progress", "real coordinator accepts matching epoch");
            f.Tick();
            Equal(2d, f.Earned(0), "first unarmed online interval also earns nothing");
            f.Credit();
            Equal(4d, f.Earned(0), "new successful application resumes credit without discarding earned seconds");
            Equal(false, f.Gate(), "partial restored work still blocks rebirth");
            f.Credit();
            f.Credit();
            Equal(true, f.Gate(), "restored work can reach durable completion");
            f.Advance(3600d);
            f.ReplaceMain();
            f.Start();
            Equal(8d, f.Earned(0), "completed same-epoch checkpoint retains exactly its earned credit");
            Equal(true, f.Gate(), "completed same-epoch checkpoint restores the durable shared gate");
        }
    }

    private static void CheckObservedRestore(Assembly assembly, bool whileLoading)
    {
        using (var f = new Fixture(assembly, 2, 8))
        {
            f.PrepareApplication();
            f.Run();
            f.Run(2d);
            f.Run(2d);
            f.ExpectApplied(1, 2d, "cloud-load checkpoint baseline");
            f.Call("SaveProtectedStageProgress", true);
            f.RequireCheckpoint("cloud-load fixture saves a later entry with partial earned work");
            var generation = f.Get("_protectedGeneration");
            var entry = Property(f.Core, "Entry");
            var enteredAt = Property(f.Core, "EnteredAt");
            var identity = Property(f.Start(), "EntryIdentity");
            var loadedElapsed = f.Elapsed + 3600d;
            var loadedTotal = (double) Get(Get(f.Character, "totalPlaytime"), "totalseconds") + 3600d;
            f.ReplaceMain();
            Equal(null, f.Get("_protectedOuterCheckpointIssue"), "startup has no imported checkpoint rejection");
            f.ObserveCloudLoad(loadedElapsed, loadedTotal, whileLoading);

            var restored = f.Start();
            Equal(1, Property(restored, "StageIndex"), "observed cloud load restores the later stage, not stage zero");
            Equal(generation, f.Get("_protectedGeneration"), "observed cloud load retains the saved generation");
            Equal(entry, Property(f.Core, "Entry"), "observed cloud load retains the saved entry");
            Equal(enteredAt, Property(f.Core, "EnteredAt"), "observed cloud load retains the entry start");
            Equal(identity, Property(restored, "EntryIdentity"), "observed cloud load retains runtime entry identity");
            Equal(2d, f.Earned(0), "observed cloud load retains completed opener credit");
            Equal(2d, f.Earned(1), "observed cloud load adds no offline credit to partial work");
            Equal(false, Get(f.Core, "_armed"), "observed cloud-load restore is unarmed");
            Contains((string) f.Get("_protectedRecoveryReason"), "Restored saved progress", "observer allows real checkpoint validation");
            Equal(false, f.Gate(), "restored partial work keeps rebirth blocked");
            f.Tick();
            Equal(2d, f.Earned(1), "first unarmed online interval earns nothing after observation");
            f.Run();
            f.ExpectApplied(1, 2d, "restored entry receives a real application without retroactive credit");
            f.Run(2d);
            f.ExpectApplied(1, 4d, "real application resumes live credit after observed cloud load");
            Equal(2d, f.Earned(0), "resumed credit does not replay the completed opener");
        }
    }

    private static void CheckObservedRestoreAfterReset(Assembly assembly, string reset)
    {
        using (var f = new Fixture(assembly, 2, 8))
        {
            f.Start();
            f.Credit();
            f.Credit();
            Equal(1, Property(f.Core, "StageIndex"), reset + " fixture reaches a later stage");
            Equal(2d, f.Earned(1), reset + " fixture has partial work to preserve");
            f.Call("SaveProtectedStageProgress", true);
            f.RequireCheckpoint(reset + " fixture saves restorable progress");
            var generation = f.Get("_protectedGeneration");
            var entry = Property(f.Core, "Entry"); var deadline = Property(f.Core, "ExpectedEndSeconds");
            var path = f.Checkpoint; var bytes = File.ReadAllText(path);
            var loadedElapsed = f.Elapsed;
            var loadedTotal = (double) Get(Get(f.Character, "totalPlaytime"), "totalseconds");
            f.ReplaceMain();
            f.ObserveCloudLoad(loadedElapsed, loadedTotal, true);
            f.Call(reset == "runtime reset" ? "ResetCustomAutomationRuntime" : "InvalidateCustomEquipmentApplication");
            Equal(bytes, File.ReadAllText(path), reset + " preserves the original checkpoint bytes");
            Equal(1, Property(f.Start(), "StageIndex"), reset + " restores the original stage rather than silently restarting");
            Equal(generation, f.Get("_protectedGeneration"), reset + " retains generation");
            Equal(entry, Property(f.Core, "Entry"), reset + " retains entry");
            Equal(deadline, Property(f.Core, "ExpectedEndSeconds"), reset + " cannot renew the deadline");
            Equal(2d, f.Earned(0), reset + " preserves completed opener credit");
            Equal(2d, f.Earned(1), reset + " preserves partial work without offline credit");
            Equal(false, Get(f.Core, "_armed"), reset + " restores unarmed");
            Equal(false, f.Gate(), reset + " cannot fabricate checkpoint completion");
        }
    }

    private static void CheckRejectedEpoch(Assembly assembly, string mismatch)
    {
        using (var f = new Fixture(assembly, 8))
        {
            f.Save();
            f.Start();
            f.Credit();
            f.Call("SaveProtectedStageProgress", true);
            f.RequireCheckpoint(mismatch + " rejection fixture has saved actual earned work");
            var path = f.Checkpoint; var bytes = File.ReadAllText(path);
            if (mismatch == "counter") Set(Get(f.Character, "stats"), "rebirthNumber", 8L);
            else if (mismatch == "rollback") f.Advance(-1d);
            else Set(Get(f.Character, "totalPlaytime"), "totalseconds", f.Elapsed + 200000d);
            Equal(false, f.Gate(), mismatch + " immediately invalidates the shared epoch gate");
            Equal(-1, Property(f.Snapshot(), "ActiveStageIndex"), mismatch + " immediately hides the old epoch's active row");
            var loadedElapsed = f.Elapsed;
            var loadedTotal = (double) Get(Get(f.Character, "totalPlaytime"), "totalseconds");
            f.ReplaceMain();
            f.ObserveCloudLoad(loadedElapsed, loadedTotal, true);
            f.ExpectCheckpointRejected(path, bytes, mismatch);
            f.RecoverCheckpoint(path, bytes, mismatch);
        }
    }

    private static void CheckCorruptCheckpoints(Assembly assembly)
    {
        var corruptions = new[]
        {
            new {Name = "unreadable", Scheduler = false, Field = (string) null, Json = ""},
            new {Name = "future-version", Scheduler = false, Field = "version", Json = "{\"value\":3}"},
            new {Name = "wrong-version-type", Scheduler = false, Field = "version", Json = "{\"value\":\"1\"}"},
            new {Name = "wrong-clock-type", Scheduler = false, Field = "elapsed", Json = "{\"value\":\"5902\"}"},
            new {Name = "wrong-row-type", Scheduler = true, Field = "completed", Json = "{\"value\":[\"false\"]}"},
            new {Name = "inconsistent-row-metadata", Scheduler = true, Field = "stageElapsed", Json = "{\"value\":3}"},
            new {Name = "obsolete-core-codec", Scheduler = true, Field = "version", Json = "{\"value\":1}"},
            new {Name = "future-core-codec", Scheduler = true, Field = "version", Json = "{\"value\":3}"},
            new {Name = "wrong-core-codec-type", Scheduler = true, Field = "version", Json = "{\"value\":\"2\"}"}
        };
        foreach (var corruption in corruptions)
        using (var f = new Fixture(assembly, 8))
        {
            f.Save();
            f.Start();
            f.Credit();
            f.Call("SaveProtectedStageProgress", true);
            f.RequireCheckpoint(corruption.Name + " fixture has actual saved credit");
            var path = f.Checkpoint;
            var checkpoint = f.Parse(File.ReadAllText(f.Checkpoint));
            Equal("2", Json(checkpoint, "version").ToString(), corruption.Name + " starts with the supported outer checkpoint version 2");
            Equal("2", Json(Json(checkpoint, "scheduler"), "version").ToString(), corruption.Name + " starts with the supported core codec 2");
            Equal("7", JsonValue(checkpoint, "rebirth"), corruption.Name + " starts with the matching rebirth counter");
            Equal(100000d, Property(Json(checkpoint, "origin"), "AsDouble"), corruption.Name + " starts with the matching run origin");
            Equal(2d, f.Earned(0), corruption.Name + " has real earned credit to reject");
            if (corruption.Field == null)
                File.WriteAllText(f.Checkpoint, "{\"version\":");
            else
            {
                var target = corruption.Scheduler ? Json(checkpoint, "scheduler") : checkpoint;
                target.GetType().GetProperty("Item", new[] {typeof(string)}).SetValue(target,
                    Json(f.Parse(corruption.Json), "value"), new object[] {corruption.Field});
                File.WriteAllText(f.Checkpoint, checkpoint.ToString());
            }
            var bytes = File.ReadAllText(path);
            f.Advance(3600d);
            f.ReplaceMain();
            f.ExpectCheckpointRejected(path, bytes, corruption.Name);
            f.RecoverCheckpoint(path, bytes, corruption.Name);
            f.RequireCheckpoint(corruption.Name + " explicit restart writes a valid durable checkpoint");
            Equal(0, Property(f.Snapshot(), "ActiveStageIndex"), corruption.Name + " recovered snapshot is valid");
            f.Tick();
            Equal(0d, f.Earned(0), corruption.Name + " does not credit an unapplied recovery interval");
            f.Credit();
            Equal(2d, f.Earned(0), corruption.Name + " resumes only fresh applied credit");
        }
    }

    private static void CheckSuspension(Assembly assembly)
    {
        foreach (var operation in new[] {"pause", "invalidate", "loadout", "digger"})
        using (var f = new Fixture(assembly, 8))
        {
            f.Start();
            f.Credit();
            f.MarkApplied();
            var core = f.Core;
            if (operation == "pause") f.Setting("UseCustomRebirthPlan", false);
            else if (operation == "invalidate") f.Call("InvalidateCustomRuntimePlanCache");
            else f.Lock(operation, true);
            f.Tick();
            Equal(true, ReferenceEquals(core, f.Core), operation + " preserves the coordinator's engine instance");
            Equal(2d, f.Earned(0), operation + " preserves earned credit without crediting the blocked interval");
            Equal(false, Get(f.Core, "_armed"), operation + " disarms protected timing");
            if (operation == "pause")
            {
                Equal("paused", JsonValue(f.Progress(), "status"), "actual pause appears in progress");
                f.Setting("UseCustomRebirthPlan", true);
            }
            else if (!operation.StartsWith("invalidate")) f.Lock(operation, false);
            f.Tick();
            Equal(2d, f.Earned(0), operation + " release does not count unapplied time");
            f.Credit();
            Equal(4d, f.Earned(0), operation + " resumes only after another successful application");
        }
    }

    private static void CheckUniversalDispatch(Assembly assembly)
    {
        using (var f = new Fixture(assembly, 8))
        {
            Equal(17, Get(f.Plan, "Version"), "new plans use schema 17");
            Equal(false, f.Plan.GetType().GetFields(All).Any(x => x.Name == "ProtectedStageTiming"), "there is no timing-mode flag in the universal model");
            Equal(null, f.Core, "allocator has not initialized the coordinator before dispatch");
            f.Save();
            Equal(17, Get(f.Load(), "Version"), "actual store round-trips the current schema");
            Equal(false, File.ReadAllText(f.PlanPath).Contains("\"ProtectedStageTiming\""), "schema-17 storage does not emit the removed timing-mode flag");
            Equal(0, Property(f.Active(), "StageIndex"), "GetActiveStage universally dispatches to the compiled coordinator");
            Equal(true, f.Core != null, "universal dispatch creates the actual engine");
            Equal(0d, f.Earned(0), "late activation never enables a historical-credit bypass");
            Equal(false, f.Gate(), "all fixed work is protected without an opt-in flag");
            var progress = f.Progress();
            Equal("stages", JsonValue(progress, "schedulerMode"), "runtime exposes the sole stages mode");
            Equal("17", Json(progress, "planVersion").ToString(), "runtime exposes schema 17");
            var row = Json(progress, "stages").GetType().GetProperty("Item", new[] {typeof(int)}).GetValue(Json(progress, "stages"), new object[] {0});
            Equal("fixed", JsonValue(row, "timingKind"), "fixed row metadata has the universal kind");
            f.RequireCheckpoint("universal dispatch persists its stage epoch");
        }
    }

    private static void CheckDiskFailure(Assembly assembly)
    {
        using (var f = new Fixture(assembly, 4))
        {
            f.Start();
            f.RequireCheckpoint("disk-failure fixture starts with a durable checkpoint");
            var original = f.Checkpoint;
            var bytes = File.ReadAllText(original);
            var recovery = f.Get("_protectedRecoveryReason");
            var blocked = Path.Combine(f.DirectoryPath, "not-a-directory");
            File.WriteAllText(blocked, "block checkpoint directory creation");
            f.SetStatic("_dir", blocked);
            f.Credit();
            f.Credit();
            Equal(true, Property(f.Core, "ProtectedWorkComplete"), "disk failure does not replace real core completion");
            Equal(false, f.Get("_protectedCheckpointDurable"), "failed atomic checkpoint is not durable");
            Equal(false, f.Gate(), "shared protected gate fails closed on disk failure");
            Equal(true, f.Rebirth("PeekTriggersAvailable"), "disk failure leaves actual time trigger ready");
            Equal(false, f.Rebirth("PeekRebirthAvailable"), "rebirth peek fails closed on disk failure");
            Equal(false, f.Rebirth("RebirthAvailable"), "rebirth action readiness fails closed on disk failure");
            var progress = f.Progress();
            Equal("false", Json(progress, "protectedWorkComplete").ToString(), "telemetry does not advertise non-durable completion");
            Contains(JsonValue(progress, "timingReason"), "could not be saved", "telemetry explains checkpoint failure");
            Equal(recovery, f.Get("_protectedRecoveryReason"), "ordinary write errors do not replace the recovery reason");
            Contains((string) f.Get("_protectedPersistenceError"), "could not be saved", "actual write failure has its separate persistence error");
            Equal(bytes, File.ReadAllText(original), "failed writes do not corrupt the previous real checkpoint");
            f.SetStatic("_dir", f.DirectoryPath);
            f.Call("SaveProtectedStageProgress", true);
            Equal(true, f.Gate(), "successful durable retry releases the gate without redoing earned work");
            Equal(string.Empty, f.Get("_protectedPersistenceError"), "successful ordinary checkpoint retry clears the old write error");
            Equal(recovery, f.Get("_protectedRecoveryReason"), "successful retry preserves the independent recovery reason");
            Equal(false, JsonValue(f.Progress(), "timingReason").Contains("automatic rebirth is blocked"), "normal retry removes the obsolete rebirth-blocking claim");
        }
    }

    private static void CheckNominalDurations(Assembly assembly)
    {
        using (var f = new Fixture(assembly, 0, 0))
        {
            Set(f.Plan, "TimeTriggerEnabled", false);
            Set(f.Plan, "StageDurationSeconds", 100);
            var preview = assembly.GetType("NGUInjector.Managers.CustomStageAllocator", true).GetMethod("EffectiveStageDurations", All);
            foreach (var weight in new[] {50d, 1d, .25d, .00000025d, double.Epsilon})
            {
                Set(f.Stage(0), "Percent", weight);
                Set(f.Stage(1), "Percent", weight);
                Equal("50,50", string.Join(",", (int[]) preview.Invoke(null, new[] {f.Plan})), "equal nominal weights are scale invariant: " + weight);
                Set(f.Stage(1), "Percent", weight * 3d);
                Equal("25,75", string.Join(",", (int[]) preview.Invoke(null, new[] {f.Plan})), "unequal nominal weights conserve the budget: " + weight);
            }
            foreach (var weight in new[] {double.NaN, double.PositiveInfinity, double.MaxValue})
            {
                Set(f.Stage(0), "Percent", weight);
                Set(f.Stage(1), "Percent", weight);
                try { preview.Invoke(null, new[] {f.Plan}); throw new Exception("Nominal preview accepted a non-finite weight or total."); }
                catch (TargetInvocationException error)
                {
                    Equal(true, error.InnerException is InvalidOperationException && error.InnerException.Message.Contains("finite"), "invalid nominal weights fail explicitly");
                }
            }
            Set(f.Stage(0), "Percent", 1d);
            Set(f.Stage(1), "Percent", 1d);
            Set(f.Stage(0), "DurationSeconds", 4);
            Equal("4,96", string.Join(",", (int[]) preview.Invoke(null, new[] {f.Plan})), "nominal flex reserves fixed work");
            Set(f.Stage(1), "CompletionMode", 1);
            Set(f.Stage(1), "DurationSeconds", int.MaxValue);
            Equal("4,0", string.Join(",", (int[]) preview.Invoke(null, new[] {f.Plan})), "dormant target-row duration is excluded from nominal fixed work");
            Equal(null, f.Core, "nominal previews never create or progress the actual engine");
        }
    }

    private static void CheckStoredEdits(Assembly assembly)
    {
        using (var f = new Fixture(assembly, 10, 4))
        {
            f.PrepareApplication();
            f.Save();
            f.Run();
            f.Run(2d);
            f.ExpectApplied(0, 2d, "stored-edit baseline");
            var core = f.Core;
            var identity = Property(f.Active(), "EntryIdentity");
            var resourceKey = f.Get("_customStageKey");
            f.Save();
            Equal(true, Get(f.Core, "_armed"), "unchanged actual Store.Save does not disarm a running entry");
            Equal(resourceKey, f.Get("_customStageKey"), "unchanged store write preserves the applied resource key");
            var store = f.Get("CustomPlanStore");
            store.GetType().GetMethod("Save", All).Invoke(store, new[] {"unselected-integration", f.Plan});
            Equal(true, Get(f.Core, "_armed"), "unselected actual Store.Save does not disarm the selected engine");
            foreach (var edit in new Action[]
            {
                () => Set(f.Stage(0), "Name", "Edited label"),
                () => Set(f.Stage(0), "DurationUnit", "min"),
                () => Set(f.Stage(0), "Percent", 7d),
                () => Set(f.Stage(0), "BlackBarLookaheadSeconds", 7)
            })
            {
                edit();
                f.Save();
                Equal(true, ReferenceEquals(core, f.Core), "selected non-structural store edits preserve the actual engine");
                Equal(false, Get(f.Core, "_armed"), "changed store data requires real reapplication");
                Equal(2d, f.Earned(0), "selected store edits preserve earned credit before evaluation");
                Equal(0, Property(f.Snapshot(), "ActiveStageIndex"), "selected store edits preserve the snapshot index");
                f.Run();
                f.ExpectApplied(0, 2d, "edited entry is really reapplied without first credit");
                Equal(identity, Property(f.Active(), "EntryIdentity"), "cosmetic/weight edits retain stable entry identity");
                Equal(resourceKey, f.Get("_customStageKey"), "resource key depends on entry identity rather than mutable row data");
            }
        }
    }

    private static void CheckDurationEdit(Assembly assembly)
    {
        using (var f = new Fixture(assembly, 8))
        {
            f.Save();
            var runtime = f.Start();
            var identity = Property(runtime, "EntryIdentity");
            f.Credit();
            Set(f.Stage(0), "DurationSeconds", 10);
            f.Save();
            runtime = f.Start();
            Equal(identity, Property(runtime, "EntryIdentity"), "duration-only Store.Save preserves generation plus entry identity");
            Equal(2d, f.Earned(0), "duration edit preserves actual earned work");
            Equal(10, Property(runtime, "EffectiveDurationSeconds"), "coordinator applies the new duration");
            Equal(8, Property(runtime, "RemainingSeconds"), "remaining duration subtracts existing credit");
            Equal(false, Get(f.Core, "_armed"), "store invalidation requires reapplication after edit");
            Equal(false, f.Gate(), "duration extension cannot bypass the protected gate");
        }
    }

    private static void CheckShorterPlan(Assembly assembly)
    {
        using (var f = new Fixture(assembly, 2, 2, 0))
        {
            Set(f.Plan, "TimeTriggerEnabled", false);
            Set(f.Plan, "NumberTriggerEnabled", true);
            f.Save();
            f.Start();
            f.ConfirmNumber();
            f.Credit();
            f.Credit();
            Equal(2, Property(f.Core, "StageIndex"), "old runtime selects the final funded flex row");
            Equal(false, Property(f.Core, "Finished"), "old tail is an active selection, not a terminal hold");
            Equal(2d, f.Earned(0), "first old protected row is actually complete");
            Equal(2d, f.Earned(1), "second old protected row is actually complete");
            Equal(true, f.Gate(), "prior protected completion has opened the durable gate");
            Equal(true, f.Rebirth("PeekRebirthAvailable"), "old flex tail has a genuinely ready rebirth trigger");
            var core = f.Core;
            var oldIdentity = Property(f.Start(), "EntryIdentity");
            var shorter = Array.CreateInstance(f.Stage(0).GetType(), 1);
            shorter.SetValue(f.Stage(0), 0);
            Set(f.Plan, "Stages", shorter);
            f.Save();
            Equal(17, Get(f.Load(), "Version"), "shorter actual Store.Save retains the universal schema");
            Equal(1, ((Array) Get(f.Load(), "Stages")).Length, "actual store persists the shorter structure");
            Equal(true, ReferenceEquals(core, f.Core), "same-mode save leaves the old engine pending structural reconciliation");
            Equal(2, Property(f.Core, "StageIndex"), "pre-update engine index is outside the newly saved stage array");
            Equal(false, f.Gate(), "structure mismatch immediately closes the previously open shared gate");
            for (var i = 0; i < 3; i++)
            {
                var runtime = f.Start();
                Equal(0, Property(runtime, "StageIndex"), "shorter-plan update safely selects row zero on pass " + i);
                Equal(false, Equals(oldIdentity, Property(runtime, "EntryIdentity")), "structural reset receives a new entry identity on pass " + i);
                Equal(0d, f.Earned(0), "shorter plan inherits no old or historical credit on pass " + i);
                Equal(false, Get(f.Core, "_armed"), "shorter plan requires fresh application on pass " + i);
                Equal(false, f.Gate(), "shorter plan cannot inherit the old completion gate on pass " + i);
                Equal(false, f.Rebirth("PeekRebirthAvailable"), "ready trigger cannot bypass the new protected work on pass " + i);
                var snapshot = f.Snapshot();
                Equal(0, Property(snapshot, "ActiveStageIndex"), "shorter-plan static snapshot has a valid active index on pass " + i);
                Equal("2", string.Join(",", (IEnumerable<int>) Property(snapshot, "DurationSeconds")), "shorter-plan snapshot has only the new row on pass " + i);
                var progress = f.Progress();
                Equal("running", JsonValue(progress, "status"), "shorter-plan progress stays available on pass " + i);
                Equal("0", Json(progress, "activeStageIndex").ToString(), "shorter-plan progress selects row zero on pass " + i);
                Equal(1, Property(Json(progress, "stages"), "Count"), "shorter-plan progress excludes removed row metadata on pass " + i);
                f.Tick();
            }
            f.RequireCheckpoint("shorter structure has a durable uncredited checkpoint");
        }
    }

    private static void CheckProtectedTail(Assembly assembly)
    {
        using (var f = new Fixture(assembly, 0, 4))
        {
            Set(f.Plan, "TimeTriggerEnabled", false);
            Set(f.Plan, "NumberTriggerEnabled", true);
            Set(f.Plan, "StageDurationSeconds", 6000);
            Equal(0, Property(f.Start(), "StageIndex"), "funded flex is active before the protected tail");
            f.ConfirmNumber();
            Equal(true, f.Rebirth("PeekTriggersAvailable"), "tail fixture has a genuinely ready confirmed NUMBER trigger");
            Equal(false, f.Gate(), "pending protected tail blocks shared gate even while flex is active");
            Equal(false, f.Rebirth("RebirthAvailable"), "ready NUMBER cannot skip a later protected row");
            Equal("4", Json(f.Progress(), "protectedSecondsRemaining").ToString(), "telemetry counts the inactive protected tail");
        }
    }

    private static void CheckAppliedTransitions(Assembly assembly, int tailDuration)
    {
        using (var f = new Fixture(assembly, 10, tailDuration))
        {
            f.PrepareApplication();
            Set(f.Plan, "StageLoopStartIndex", tailDuration == 2 ? 0 : -1);
            f.Run();
            f.ExpectApplied(0, 0d, "initial real application");
            var firstKey = f.Get("_customStageKey");
            var firstEquipment = f.Get("_customStageEquipmentKey");
            for (var tick = 1; tick < 5; tick++)
            {
                f.Run(2d);
                f.ExpectApplied(0, tick * 2d, "continuous fixed opener tick " + tick);
                Equal(firstKey, f.Get("_customStageKey"), "same entry keeps the resource key despite a changing remaining time");
            }
            f.Run(2d);
            f.ExpectApplied(1, 0d, "ten-second boundary selects AND applies the second stage");
            Equal(10d, f.Earned(0), "opener earns its full ten seconds through real commits");
            Equal(false, Equals(firstKey, f.Get("_customStageKey")), "next selected stage has a fresh resource key");
            Equal(false, Equals(firstEquipment, f.Get("_customStageEquipmentKey")), "next selected stage has fresh equipment ownership");
            Equal(true, f.Call("CustomStageReadyForQuickRefresh", f.Plan, null, null), "next-stage resource key is immediately ready for quick application");
            var secondKey = f.Get("_customStageKey");
            f.Run(2d);
            if (tailDuration == 4)
            {
                f.ExpectApplied(1, 2d, "second stage earns its own applied interval");
                Equal(secondKey, f.Get("_customStageKey"), "crediting stage two does not invent another entry");
                f.Run(2d);
                Equal(4d, f.Earned(1), "second stage finishes its own full applied duration");
                Equal(true, f.Gate(), "completed real applications open the durable shared gate");
            }
            else
            {
                f.ExpectApplied(0, 0d, "10s/2s loop re-entry is applied in the same pass");
                Equal(1, Property(f.Core, "Cycle"), "the second stage's two applied seconds complete exactly one cycle");
                Equal(false, Equals(firstKey, f.Get("_customStageKey")), "loop re-entry has a new stable entry identity");
                f.Run(2d);
                f.ExpectApplied(0, 2d, "new loop entry earns only its own live interval");
            }
        }
    }

    private static void CheckAppliedMixedTransitions(Assembly assembly)
    {
        using (var f = new Fixture(assembly, 2, 0, 2, 0, 4))
        {
            f.PrepareApplication();
            Set(f.Plan, "StageDurationSeconds", 12);
            Set(f.Stage(3), "CompletionMode", 1);
            Set(f.Stage(3), "EnergyPriorities", new[] {"AT"});
            Set(f.Stage(3), "TargetAdvancedTrainingPower", 5L);
            Set(f.Stage(3), "TargetAdvancedTrainingToughness", 3L);
            f.Run();
            f.ExpectApplied(0, 0d, "mixed first fixed row");
            f.Run(2d);
            f.ExpectApplied(1, 0d, "mixed flex row is applied after fixed completion");
            var flexKey = f.Get("_customStageKey");
            f.Run(2d);
            Equal(flexKey, f.Get("_customStageKey"), "flex remains on its frozen funded entry");
            f.Run(2d);
            f.ExpectApplied(2, 0d, "post-flex fixed row is applied before it can earn");
            Equal(2d, f.Earned(0), "flex transition preserves earlier fixed credit");
            f.Run(2d);
            f.ExpectApplied(3, 0d, "mandatory AT row receives a real application");
            Equal(2d, f.Earned(2), "AT transition preserves intervening fixed credit");
            var progress = f.Progress();
            var rows = Json(progress, "stages");
            var kinds = Enumerable.Range(0, 5).Select(i => JsonValue(rows.GetType().GetProperty("Item", new[] {typeof(int)}).GetValue(rows, new object[] {i}), "timingKind"));
            Equal("fixed,flexible,fixed,target,fixed", string.Join(",", kinds), "real progress exports every unified row kind consistently");
            var training = Get(f.Character, "advancedTraining");
            Equal(-1L, ((long[]) Get(training, "levelTarget"))[1], "AT application restores the unrelated menu power target");
            Equal(false, f.Gate(), "mandatory AT plus a future fixed row still block rebirth");
            var levels = (long[]) Get(training, "level");
            levels[1] = 5;
            levels[0] = 2;
            f.Run(2d);
            f.ExpectApplied(3, 0d, "partial AT targets cannot be forgiven by wall time");
            levels[0] = 3;
            Equal(3, Property(f.Snapshot(), "ActiveStageIndex"), "met AT is still read-only until actual automation runs");
            f.Run(2d);
            f.ExpectApplied(4, 0d, "AT exit applies the final fixed row without inheriting AT elapsed time");
            Equal(2d, f.Earned(0), "AT exit retains first fixed credit");
            Equal(2d, f.Earned(2), "AT exit retains middle fixed credit");
            f.Run(2d);
            f.ExpectApplied(4, 2d, "final row earns a fresh applied interval even after the horizon");
            f.Run(2d);
            Equal(4d, f.Earned(4), "final fixed row completes its full applied duration");
            Equal(true, f.Gate(), "completed mixed schedule opens the actual durable gate");
        }
    }

    private static void CheckApplicationFailures(Assembly assembly)
    {
        foreach (var failure in new[] {"loadout", "digger", "unmanaged", "locked-feature", "exception", "render", "interference"})
        using (var f = new Fixture(assembly, 10, 4))
        {
            f.PrepareApplication();
            f.Run();
            f.ExpectApplied(0, 0d, failure + " baseline actual application");
            var key = f.Get("_customStageKey");
            var controller = Get(f.Character, "timeMachineController");
            var button = Get(Get(f.Character, "buttons"), "advancedTraining");
            if (failure == "loadout")
            {
                Set(f.Stage(0), "GearGoal", 2);
                f.Lock("loadout", true);
            }
            else if (failure == "digger") f.Lock("digger", true);
            else if (failure == "unmanaged") f.Setting("ManageEnergy", false);
            else if (failure == "locked-feature") Set(button, "m_Interactable", false);
            else if (failure == "render")
            {
                Set(f.Character, "menuID", 18);
                var idle = (long)Get(f.Character, "idleEnergy");
                Equal(true, idle > 0, "render fault positive control has a real resource to displace");
                Set(Get(f.Character, "machine"), "speedEnergy", 1L); Set(f.Character, "idleEnergy", idle - 1);
            }
            else if (failure == "interference") Set(Get(f.Character, "machine"), "speedEnergy", 37L);
            else Set(f.Character, "timeMachineController", null);
            double earned = 0;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                if (failure == "exception")
                {
                    var idle = Get(f.Character, "idleEnergy");
                    f.Run(2d); earned = 0d; // Missing coupled prerequisites cannot certify continuity of the previous interval.
                    Equal(null, Get(f.Character, "timeMachineController"), "setup observation never reconstructs a missing native controller");
                    Equal(idle, Get(f.Character, "idleEnergy"), "failed support observation performs no resource transfer");
                    Equal(0L, Get(Get(f.Character, "machine"), "speedEnergy"), "failed support observation preserves exact existing allocation");
                    Contains((string) f.Call("GetCustomAutomationStatus"), "Support controllers/tables unavailable", "missing coupled support observation reports setup deferral");
                }
                else if (failure == "render")
                {
                    f.Run(2d); earned = attempt * 2d;
                    var outcome = assembly.GetType("NGUInjector.Managers.CustomStageAllocator", true).GetProperty("LastStageResourceOutcome", All).GetValue(null);
                    Equal("ModelAppliedRenderFailed", Get(outcome, "Disposition").ToString(), "the real unpatched TM renderer fails only after complete accounting");
                    Contains(Get(outcome, "Error").ToString(), "TimeMachineController.update", "positive control reaches the actual native rendering leaf");
                    Equal(true, f.ApplicationSucceeded, "render failure does not discard a verified model receipt");
                }
                else
                {
                    if (failure == "interference")
                    {
                        Set(Get(f.Character, "machine"), "speedEnergy", 37L);
                        Equal(false, f.Call("ProtectedStageOwnershipAvailable", f.Plan), "external allocation changes invalidate ongoing credit before refresh");
                    }
                    f.Run(2d);
                }
                Equal(earned, f.Earned(0), failure + " credit follows verified accounting, eligibility and live ownership at attempt " + attempt);
                Equal(failure == "interference" || failure == "render", Get(f.Core, "_armed"), failure + " only verified eligible application rearms the engine");
                Equal(key, f.Get("_customStageKey"), failure + " preserves the pending entry identity");
                Equal(false, f.Gate(), failure + " cannot authorize rebirth");
            }
            if (failure == "loadout") { Set(f.Stage(0), "GearGoal", -1); f.Lock("loadout", false); }
            else if (failure == "digger") f.Lock("digger", false);
            else if (failure == "unmanaged") f.Setting("ManageEnergy", true);
            else if (failure == "locked-feature") Set(button, "m_Interactable", true);
            else if (failure == "render") Set(f.Character, "menuID", 0);
            else if (failure != "interference") Set(f.Character, "timeMachineController", controller);
            if (failure == "interference" || failure == "render") earned += 2d;
            f.Run(2d);
            f.ExpectApplied(0, earned, failure + " recovery reapplies without retroactive credit");
            f.Run(2d);
            f.ExpectApplied(0, earned + 2d, failure + " recovery earns only the subsequent live interval");
        }
    }

    private static void CheckInterleavedNguProfile(Assembly assembly)
    {
        using (var f = new Fixture(assembly, 4, 4))
        {
            f.PrepareNguApplication();
            f.Save();
            var ngu = Get(f.Character, "NGU");
            var energy = (IList) Get(ngu, "skills");
            var magic = (IList) Get(ngu, "magicSkills");
            var machine = Get(f.Character, "machine");
            var magicState = Get(f.Character, "magic");
            Set(energy[4], "energy", 131L);
            Set(magic[0], "magic", 97L);
            Set(machine, "goldMultiMagic", 53L);
            Action<int, double, bool, double> apply = (index, earned, quick, seconds) =>
            {
                var idleEnergy = (long) Get(f.Character, "idleEnergy");
                var idleMagic = (long) Get(magicState, "idleMagic");
                var recalledEnergy = (long) Get(energy[4], "energy") + (long) Get(machine, "speedEnergy");
                var recalledMagic = (long) Get(magic[0], "magic") + (long) Get(machine, "goldMultiMagic");
                if (quick) f.RunQuick(seconds);
                else f.Run(seconds);
                Equal(index, Property(f.Core, "StageIndex"), "interleaved profile selects the actual expected row");
                Equal(earned, f.Earned(index), "main/quick interleaving credits each applied interval exactly once");
                Equal(true, f.ApplicationSucceeded, "actual two-lane ApplyStage succeeds");
                Equal(true, Get(f.Core, "_armed"), "actual two-lane commit arms fixed credit");
                Equal(0L, Get(energy[4], "energy"), "real game NGU controller recalls energy allocation");
                Equal(0L, Get(magic[0], "magic"), "real game NGU controller recalls magic allocation");
                Equal(idleEnergy + recalledEnergy, Get(f.Character, "idleEnergy"), "main and quick conserve both energy recall sources");
                Equal(idleMagic + recalledMagic, Get(magicState, "idleMagic"), "main and quick conserve both magic recall sources");
                var weights = f.Call("GetCustomNguRuntime");
                Equal("true", Json(weights, "energyWeightsApplied").ToString(), "real profile applies its configured energy weights");
                Equal("true", Json(weights, "magicWeightsApplied").ToString(), "real profile applies its configured magic weights");
            };
            apply(0, 0d, false, 0d);
            var firstKey = f.Get("_customStageKey");
            apply(0, 2d, true, 2d);
            Equal(firstKey, f.Get("_customStageKey"), "quick refresh uses the same stable entry as the main pass");
            f.RunQuick(2d);
            Equal(1, Property(f.Core, "StageIndex"), "quick refresh can observe the actual next fixed selection");
            Equal(0d, f.Earned(1), "quick boundary does not credit an unapplied new stage");
            Equal(true, Get(f.Core, "_armed"), "central quick dispatch applies the next entry before arming it");
            Equal(false, Equals(firstKey, f.Get("_customStageKey")), "central quick dispatch establishes the next real resource key");
            Equal(true, f.ApplicationSucceeded, "quick transition executes actual next-entry allocation");
            Equal(true, f.Call("ProtectedStageOwnershipAvailable", f.Plan), "quick transition owns the current scheduler generation and entry");
            apply(1, 0d, false, 0d);
            Equal(false, Equals(firstKey, f.Get("_customStageKey")), "main pass establishes the next actual two-lane resource key");
            apply(1, 2d, true, 2d);
            apply(1, 2d, false, 0d);
            f.Run(2d);
            Equal(4d, f.Earned(1), "interleaved second stage earns its full four applied seconds");
            Equal(true, f.Gate(), "interleaved profile durably completes without manual MarkApplied");
        }
    }

    private static void CheckAppliedFightEntry(Assembly assembly)
    {
        foreach (var boundary in new[] { "loadout", "digger" })
        using (var f = new Fixture(assembly, 4, 4))
        {
            f.PrepareApplication();
            f.PrepareFight();
            Set(f.Plan, "StageLoopStartIndex", 0);
            Set(f.Stage(0), "FightOnEntry", true);
            Set(f.Stage(1), "FightOnEntry", true);
            f.Save();
            f.Lock(boundary, true);
            f.Run();
            Equal(0, f.BossPushStarts, "entry fight waits for equipment ownership");
            Equal(null, f.Get("_customStageFightKey"), "deferred fight does not consume its entry receipt");
            Equal(null, f.Get("_fightPreparationRecord"), "denied entry creates no preparation acquisition");
            Equal(0d, f.Earned(0), "denied entry earns no protected credit");
            var entry = Property(f.Core, "Entry");
            var deadline = Property(f.Core, "ExpectedEndSeconds");
            f.Lock(boundary, false);
            f.Run();
            Equal(1, f.BossPushStarts, "unlocked entry starts the real boss-push coordinator once");
            Equal(entry, Property(f.Core, "Entry"), "released admission preserves the original entry");
            Equal(deadline, Property(f.Core, "ExpectedEndSeconds"), "released admission preserves the original deadline");
            Equal(false, Get(f.Core, "_armed"), "fight preparation cannot count as a successful stage application");
            var fightKey = f.Get("_customStageFightKey");
            f.Call("StopBossPush"); // Never tick combat or invoke a native boss controller.
            f.Run();
            f.ExpectApplied(0, 0d, "stage applies after its entry fight without first credit");
            f.Run(2d);
            f.ExpectApplied(0, 2d, "post-fight application earns normally");
            Set(f.Stage(0), "DurationSeconds", 6);
            f.Save();
            f.Run();
            f.ExpectApplied(0, 2d, "duration edit reapplies the same fight entry");
            Equal(fightKey, f.Get("_customStageFightKey"), "duration edit preserves the stable fight receipt");
            Equal(1, f.BossPushStarts, "duration/cache invalidation does not fight again");
            f.Run(2d);
            f.Run(2d);
            Equal(1, Property(f.Core, "StageIndex"), "completed first row selects its actual next fight entry");
            Equal(2, f.BossPushStarts, "next entry fights exactly once");
            Equal(0d, f.Earned(1), "next entry fight does not inherit preceding work");
            f.Call("StopBossPush");
            f.Run();
            f.ExpectApplied(1, 0d, "next entry is applied after its own fight");
            f.Run(2d);
            f.ExpectApplied(1, 2d, "second fight entry earns only its applied interval");
            Equal(2, f.BossPushStarts, "ordinary application retry does not repeat the second fight");
            f.Run(2d);
            Equal(1, Property(f.Core, "Cycle"), "completed real applications enter the next loop");
            Equal(3, f.BossPushStarts, "genuine loop re-entry receives one new fight");
            Equal(false, Equals(fightKey, f.Get("_customStageFightKey")), "loop re-entry has a distinct fight receipt");
        }
    }

    private static void CheckManualSkip(Assembly assembly)
    {
        using (var f = new Fixture(assembly, 10, 4))
        {
            f.PrepareApplication();
            f.Save();
            f.Run();
            f.Run(2d);
            var previousEntry = JsonValue(f.Progress(), "stageEntry");
            Contains(f.Override("skip", 0), "next automation pass", "manual skip acknowledges deferred allocation");
            Equal(1, Property(f.Core, "StageIndex"), "manual skip selects the second actual stage");
            Equal(true, ((bool[]) Property(f.Core, "Skipped"))[0], "manual skip is recorded separately from earned completion");
            Equal(2d, f.Earned(0), "skip preserves real prior work without fabricating the missing eight seconds");
            Equal(0d, f.Earned(1), "skip cannot credit the pending second stage");
            Equal(false, Get(f.Core, "_armed"), "manual selection requires actual reapplication");
            Equal(null, f.Get("_customStageKey"), "successful override invalidates the old applied resource receipt");
            Equal(null, f.Get("_customStageEquipmentKey"), "successful override invalidates old equipment ownership");
            Equal(false, f.Gate(), "remaining second-stage work still blocks rebirth");
            f.Run();
            f.ExpectApplied(1, 0d, "manual skip really applies stage two");
            f.Run(2d);
            f.ExpectApplied(1, 2d, "manually selected second stage earns its own real interval");
            var progress = f.Progress();
            Equal(false, previousEntry == JsonValue(progress, "stageEntry"), "UI entry token changes with manual selection");
            Equal(Property(f.Active(), "EntryIdentity"), JsonValue(progress, "stageEntry"), "UI entry token is the coordinator's real stable identity");
            Equal("skip stage 1 at 2", JsonValue(progress, "lastStageOverride"), "UI override history is one-based while API indices stay zero-based");
            Equal(2d, JsonNumber(progress, "lastStageOverrideAt"), "override timestamp uses the controlled rebirth clock");
            ExpectHistoryRow(progress, 0, "skipped", 2d, 2d, 1);
            ExpectHistoryRow(progress, 1, "current", 2d, 2d, 1);
            var state = f.CoreState();
            var files = f.Files();
            for (var i = 0; i < 3; i++)
            {
                Equal(progress.ToString(), f.Progress().ToString(), "history polling is a stable projection");
                Equal("true", Json(f.Call("BuildExternalUiRuntimeState"), "stageOverridesV1").ToString(), "actual runtime advertises manual stage overrides");
                f.Snapshot();
            }
            Equal(state, f.CoreState(), "history getters do not mutate applied/elapsed/visit/override state");
            Equal(files, f.Files(), "history polling does not rewrite sidecars or plans");
            f.Run(2d);
            Equal(true, f.Gate(), "manual skip plus real remaining work durably opens the gate");
            ExpectHistoryRow(f.Progress(), 1, "completed", 4d, 4d, 1);
        }
    }

    private static void CheckManualForward(Assembly assembly)
    {
        using (var f = new Fixture(assembly, 10, 8, 0, 4))
        {
            f.PrepareApplication();
            Set(f.Stage(2), "CompletionMode", 1);
            Set(f.Stage(2), "TargetAdvancedTrainingPower", 5L);
            Set(f.Stage(2), "TargetAdvancedTrainingToughness", 3L);
            f.Save();
            f.Run();
            f.Override("set", 2);
            Equal("set stage 3 at 0", Property(f.Core, "LastOverride"), "forward history uses human stage three for API index two");
            Equal("True,True,False,False", string.Join(",", (bool[]) Property(f.Core, "Skipped")), "forward selection explicitly skips only prior rows");
            Equal("0,0,0,0", string.Join(",", (double[]) Property(f.Core, "EarnedSeconds")), "forward selection does not invent protected training");
            ExpectHistoryRow(f.Progress(), 1, "skipped", 0d, 0d, 0);
            Equal(false, f.Gate(), "unmet AT and the remaining fixed suffix stay mandatory");
            f.Run();
            f.ExpectApplied(2, 0d, "forward-selected mandatory AT is actually applied");
            var levels = (long[]) Get(Get(f.Character, "advancedTraining"), "level");
            levels[1] = 5;
            levels[0] = 3;
            f.Run(2d);
            f.ExpectApplied(3, 0d, "forward selection still waits for real AT completion before its fixed suffix");
            Equal(false, f.Gate(), "AT completion alone cannot waive the remaining fixed row");
            f.Run(2d);
            f.Run(2d);
            Equal(true, f.Gate(), "only the remaining mandatory work is required after explicit forward skips");
            Equal(0d, f.Earned(0), "first skipped fixed row remains honestly uncredited");
            Equal(0d, f.Earned(1), "unvisited skipped fixed row remains honestly uncredited");
            f.Call("SaveProtectedStageProgress", true);
            f.ReplaceMain();
            f.Start();
            Equal("True,True,False,False", string.Join(",", (bool[]) Property(f.Core, "Skipped")), "codec 2 restores explicit zero-earned prefix skips");
            Equal(true, f.Gate(), "valid explicit skips survive durable finished-checkpoint restoration");
        }
    }

    private static void CheckManualRestartAndRestore(Assembly assembly)
    {
        using (var f = new Fixture(assembly, 4, 4, 4))
        {
            f.PrepareApplication();
            f.Save();
            f.Run();
            f.Run(2d);
            f.Override("restart", 0);
            Equal("restart stage 1 at 2", Property(f.Core, "LastOverride"), "restart history uses a one-based stage number");
            Equal(0d, f.Earned(0), "restart resets the current-pass earned obligation");
            Equal(2d, ((double[]) Property(f.Core, "AppliedSeconds"))[0], "restart retains cumulative real applied history");
            Equal(2, ((int[]) Property(f.Core, "Visits"))[0], "restart records another visit rather than erasing history");
            Equal(false, f.Gate(), "restart reopens current and suffix work");
            f.Run();
            f.ExpectApplied(0, 0d, "restarted row must really apply again");
            f.Run(2d);
            f.Run(2d);
            f.Run(2d);
            f.ExpectApplied(1, 2d, "later row has real history before selecting backward");
            f.Override("set", 0);
            Equal("set stage 1 at 8", Property(f.Core, "LastOverride"), "backward-selection history uses a one-based stage number");
            Equal("0,0,0", string.Join(",", (double[]) Property(f.Core, "EarnedSeconds")), "backward selection reopens the entire suffix");
            Equal("False,False,False", string.Join(",", (bool[]) Property(f.Core, "Completed")), "backward selection clears prior completion in its suffix");
            Equal("False,False,False", string.Join(",", (bool[]) Property(f.Core, "Skipped")), "backward selection does not preserve a waiver in reopened work");
            Equal("6,2,0", string.Join(",", (double[]) Property(f.Core, "AppliedSeconds")), "backward selection preserves cumulative work on all prior visits");
            f.Run();
            f.Run(2d);
            var progress = f.Progress();
            ExpectHistoryRow(progress, 0, "current", 8d, 8d, 3);
            ExpectHistoryRow(progress, 1, "pending", 2d, 2d, 1);
            ExpectHistoryRow(progress, 2, "pending", 0d, 0d, 0);
            var entry = JsonValue(progress, "stageEntry");
            var action = JsonValue(progress, "lastStageOverride");
            var at = JsonNumber(progress, "lastStageOverrideAt");
            f.Call("SaveProtectedStageProgress", true);
            f.RequireCheckpoint("manual cumulative history is durably saved");
            Equal("2", Json(Json(f.Parse(File.ReadAllText(f.Checkpoint)), "scheduler"), "version").ToString(), "manual history uses core codec 2");
            f.Advance(3600d);
            f.ReplaceMain();
            f.Start();
            progress = f.Progress();
            Equal(entry, JsonValue(progress, "stageEntry"), "restore retains the manual entry identity");
            Equal(action, JsonValue(progress, "lastStageOverride"), "restore retains the last manual action receipt");
            Equal(at, JsonNumber(progress, "lastStageOverrideAt"), "restore retains the manual action timestamp");
            Equal(2d, f.Earned(0), "manual history restoration gives no offline protected credit");
            Equal("8,2,0", string.Join(",", (double[]) Property(f.Core, "AppliedSeconds")), "offline time never enters cumulative applied history");
            Equal("3,1,0", string.Join(",", (int[]) Property(f.Core, "Visits")), "restoration invents no new visits");
            Equal(false, Get(f.Core, "_armed"), "restored manual history is unarmed");
            Equal(false, f.Gate(), "restored reopened suffix still blocks rebirth");
            ExpectHistoryRow(progress, 0, "current", 8d, 3608d, 3);
            ExpectHistoryRow(progress, 1, "pending", 2d, 2d, 1);
        }
    }

    private static void CheckManualGuard(Assembly assembly, string guard)
    {
        using (var f = new Fixture(assembly, 10, 4))
        {
            f.PrepareApplication();
            f.Save();
            f.Run();
            var args = f.OverrideArgs("skip", 0);
            if (guard == "entry") f.Override("restart", 0);
            else if (guard == "revision")
            {
                Set(f.Stage(0), "Name", "Saved after the command snapshot");
                f.Save();
            }
            else if (guard == "name") SetJson(args, "name", Json(f.Parse("{\"v\":\"not-selected\"}"), "v"));
            else if (guard == "disabled")
            {
                Set(f.Plan, "StageSchedulerEnabled", false);
                f.Save();
                args = f.OverrideArgs("skip", 0);
            }
            else if (guard == "paused") f.Setting("UseCustomRebirthPlan", false);
            else if (guard == "boss") f.SetRuntime("_bossPushRunning", true);
            else if (guard == "controller-fight") Set(Child(f.Character, "bossController"), "isFighting", true);
            else if (guard == "prep") f.SetRuntime("_customPreRebirthPrepRunning", true);
            else if (guard == "prep-complete") f.SetRuntime("_customPreRebirthPrepCompleted", true);
            else if (guard == "manual-rebirth") f.SetRuntime("_manualRebirthPending", true);
            else if (guard == "focus") f.SetRuntime("_customRequestedFocusGoal", Enum.Parse(assembly.GetType("NGUInjector.CustomFocusGoal", true), "EnergyNgu"));
            RejectOverride(f, args, typeof(InvalidOperationException), guard);
        }
    }

    private static void CheckManualInvalidArguments(Assembly assembly)
    {
        using (var f = new Fixture(assembly, 10, 4))
        {
            f.PrepareApplication();
            f.Save();
            f.Run();
            foreach (var invalid in new[]
            {
                new {Field = "name", Value = "1"}, new {Field = "name", Value = "\" \""},
                new {Field = "expectedRevision", Value = "false"}, new {Field = "expectedRevision", Value = "\"\""},
                new {Field = "expectedEntry", Value = "null"}, new {Field = "expectedEntry", Value = "\"\""},
                new {Field = "action", Value = "false"}, new {Field = "action", Value = "\"advance\""},
                new {Field = "stageIndex", Value = "\"0\""}, new {Field = "stageIndex", Value = "false"},
                new {Field = "stageIndex", Value = "null"}, new {Field = "stageIndex", Value = "-1"},
                new {Field = "stageIndex", Value = "0.5"}, new {Field = "stageIndex", Value = "2147483648"},
                new {Field = "stageIndex", Value = "1e309"}
            })
            {
                var args = f.OverrideArgs("skip", 0);
                SetJson(args, invalid.Field, Json(f.Parse("{\"v\":" + invalid.Value + "}"), "v"));
                RejectOverride(f, args, typeof(FormatException), invalid.Field + "=" + invalid.Value);
            }
            var extra = f.OverrideArgs("skip", 0);
            SetJson(extra, "unknown", Json(f.Parse("{\"v\":true}"), "v"));
            RejectOverride(f, extra, typeof(FormatException), "unknown override fields");
            var missing = f.OverrideArgs("skip", 0);
            missing.GetType().GetMethod("Remove", new[] {typeof(string)}).Invoke(missing, new object[] {"expectedEntry"});
            RejectOverride(f, missing, typeof(FormatException), "missing entry token");
            SetJson(missing, "unknown", Json(f.Parse("{\"v\":true}"), "v"));
            RejectOverride(f, missing, typeof(FormatException), "unknown field cannot replace a required field even when count is five");
            RejectOverride(f, null, typeof(FormatException), "null override args");
            RejectOverride(f, f.Parse("[]"), typeof(FormatException), "non-object override args");
            RejectOverride(f, f.OverrideArgs("skip", 1), typeof(InvalidOperationException), "skip noncurrent row");
            RejectOverride(f, f.OverrideArgs("restart", 1), typeof(InvalidOperationException), "restart noncurrent row");
            RejectOverride(f, f.OverrideArgs("set", 2), typeof(InvalidOperationException), "out-of-bounds forward selection");
        }
    }

    private static void RejectOverride(Fixture f, object args, Type exception, string label, bool dispatch = false)
    {
        var state = f.CoreState();
        var coordinator = f.CoordinatorState();
        var armed = Get(f.Core, "_armed");
        var files = f.Files();
        var key = f.Get("_customStageKey");
        try
        {
            if (dispatch) f.Dispatch(args);
            else f.Call("OverrideCustomStage", args);
            throw new Exception("Stage override unexpectedly accepted " + label);
        }
        catch (TargetInvocationException error) { Equal(exception, error.InnerException.GetType(), label + " reports its real policy rejection"); }
        Equal(state, f.CoreState(), label + " leaves every engine/history field unchanged");
        Equal(coordinator, f.CoordinatorState(), label + " leaves coordinator epoch/persistence state unchanged");
        Equal(armed, Get(f.Core, "_armed"), label + " does not disarm or rearm before validation");
        Equal(key, f.Get("_customStageKey"), label + " leaves the applied resource receipt unchanged");
        Equal(files, f.Files(), label + " does not write plans or checkpoints");
    }

    private static void CheckManualSaveFailure(Assembly assembly)
    {
        using (var f = new Fixture(assembly, 10, 4))
        {
            f.PrepareApplication();
            f.Save();
            f.Run();
            f.Run(2d);
            var args = f.OverrideArgs("skip", 0);
            f.Call("SaveProtectedStageProgress", true);
            var path = f.Checkpoint;
            var saved = File.ReadAllText(path);
            var state = f.CoreState();
            var key = f.Get("_customStageKey");
            var blocked = Path.Combine(f.DirectoryPath, "override-not-a-directory");
            File.WriteAllText(blocked, "block checkpoint creation");
            f.SetStatic("_dir", blocked);
            try { f.Call("OverrideCustomStage", args); throw new Exception("Non-durable override unexpectedly succeeded."); }
            catch (TargetInvocationException error) { Equal(typeof(IOException), error.InnerException.GetType(), "manual checkpoint write failure rejects the override"); }
            Equal(state, f.CoreState(), "failed persistence transaction restores the entire prior engine export");
            Equal(false, Get(f.Core, "_armed"), "rollback is safely unarmed rather than crediting an interrupted application");
            Equal(key, f.Get("_customStageKey"), "failed override does not clear a valid prior resource receipt");
            Equal(saved, File.ReadAllText(path), "failed override does not corrupt the prior durable checkpoint");
            Equal(string.Empty, Property(f.Core, "LastOverride"), "failed override leaves no committed history receipt");
            f.SetStatic("_dir", f.DirectoryPath);
            f.Run(2d);
            f.ExpectApplied(0, 2d, "next pass after failed override reapplies the original row without retroactive credit");
            Equal(false, ((bool[]) Property(f.Core, "Skipped"))[0], "failed skip cannot reappear on the next tick");
            f.Run(2d);
            f.ExpectApplied(0, 4d, "original row resumes earning after rollback");
            f.Call("SaveProtectedStageProgress", true);
            Equal(string.Empty, JsonValue(Json(f.Parse(File.ReadAllText(path)), "scheduler"), "lastOverride"), "later persistence cannot resurrect the failed override");
        }
    }

    private static void CheckTerminalSkipFightReceipt(Assembly assembly, string nextAction)
    {
        using (var f = new Fixture(assembly, 2))
        {
            f.PrepareApplication();
            f.PrepareFight();
            Set(f.Stage(0), "FightOnEntry", true);
            f.Save();
            f.Run();
            Equal(1, f.BossPushStarts, "flagged last row consumes its original entry fight");
            f.Call("StopBossPush");
            f.Run();
            f.ExpectApplied(0, 0d, "flagged terminal row applies after its original fight");
            f.Run(2d);
            Equal(true, Property(f.Core, "Finished"), "flagged last row has actually completed");
            Equal(true, f.Gate(), "completed flagged row has a durable open gate");
            var priorEntry = (long) Property(f.Core, "Entry");
            var priorFight = f.Get("_customStageFightKey");
            f.Override("skip", 0);
            Equal(priorEntry + 1, Property(f.Core, "Entry"), "terminal skip invalidates stale entry tokens");
            Equal(1, ((int[]) Property(f.Core, "Visits"))[0], "terminal skip does not fabricate another visit");
            var receipt = "protected-integration:" + f.Call("CustomStageRuntimeKey", f.Plan, f.Active());
            Equal(false, Equals(priorFight, receipt), "terminal skip has a new token despite holding the old allocation");
            Equal(receipt, f.Get("_customStageFightKey"), "terminal receipt exactly matches the real RunCustomPlanAutomation fight key");
            Equal(receipt, JsonValue(f.Parse(File.ReadAllText(f.Checkpoint)), "fightEntry"), "terminal fight receipt is persisted before the next automation pass");
            f.Run();
            f.ExpectApplied(0, 2d, "terminal skip still reapplies its held allocation");
            Equal(1, f.BossPushStarts, "next real pass does not refight a completed terminal allocation");
            f.ReplaceMain();
            f.Run();
            f.ExpectApplied(0, 2d, "restored terminal skip reapplies the held allocation");
            Equal(receipt, f.Get("_customStageFightKey"), "checkpoint restoration retains the exact terminal fight receipt");
            Equal(1, f.BossPushStarts, "restored terminal allocation does not fight again");
            f.Override(nextAction, 0);
            Equal(nextAction + " stage 1 at 2", Property(f.Core, "LastOverride"), "intentional re-entry history remains one-based");
            Equal(false, Property(f.Core, "Finished"), "intentional restart/set reopens the held last row");
            f.Run();
            Equal(2, f.BossPushStarts, "intentional restart/set starts exactly one new entry fight");
            f.Run();
            Equal(2, f.BossPushStarts, "running new fight cannot consume its entry twice");
            f.Call("StopBossPush");
            f.Run();
            f.ExpectApplied(0, 0d, "intentional re-entry requires its own application after the fight");
            f.Run(2d);
            Equal(2d, f.Earned(0), "intentional re-entry earns only its new applied interval");
            Equal(2, f.BossPushStarts, "completing the intentional re-entry starts no extra fight");
        }
    }

    private static void CheckOverrideOwnershipAccrual(Assembly assembly, string owner)
    {
        var overlay = owner == "manual-overlay";
        var manual = owner == "manual-adventure" || overlay;
        using (var f = new Fixture(assembly, 10, 4))
        {
            f.PrepareApplication();
            if (manual)
            {
                f.PrepareControlledGear();
                Set(f.Stage(0), "GearGoal", 2);
            }
            f.Save();
            if (manual)
            {
                // Select the real already-equipped native gear and verify its common
                // setup before crediting resources or transferring manual ownership.
                f.Run();
                f.ExpectApplied(0, 0d, "ownership fixture acquires actual setup before manual takeover");
                f.Run(2d);
                f.ExpectApplied(0, 2d, "ownership fixture earns only verified current-entry work");
                // Explicit overlay preserves training ownership; legacy manual rules do not.
                f.Setting("ManualAdventureEnabled", overlay);
                f.Setting("CustomAdventureEnabled", !overlay);
                f.SetRuntime("_manualAdventureSetupActive", true);
                if (overlay)
                {
                    f.Run();
                    Equal(true, f.Call("InlineStageSetupVerified", f.Plan), "explicit overlay is reconciled before acquiring training credit");
                }
            }
            else
            {
                f.Run();
                f.Run(2d);
                f.Lock(owner, true);
            }
            Equal(2d, f.Earned(0), owner + " starts with exactly two real earned seconds");
            Equal(true, Get(f.Core, "_armed"), owner + " takeover occurs while the previous actual application is armed");
            Equal(overlay, f.Call("ProtectedStageOwnershipAvailable", f.Plan), owner + " uses the real current-stage ownership predicate");
            Set(f.Core, "_lastHeartbeat", Heartbeat - 2d);
            f.Advance(2d); // Do not run an update that would disarm before the override itself.
            Contains(f.Override("skip", 0), "Manual skip", owner + " still permits the intentional manual selection");
            Equal(overlay ? 4d : 2d, f.Earned(0), owner + " override credits only an ownership-eligible interval");
            Equal(overlay ? 4d : 2d, ((double[]) Property(f.Core, "AppliedSeconds"))[0], owner + " applied history follows actual ownership eligibility");
            Equal(4d, ((double[]) Property(f.Core, "ElapsedSecondsByStage"))[0], owner + " still records honest wall-clock elapsed history");
            Equal(1, Property(f.Core, "StageIndex"), owner + " manual action really selects the next row");
            Equal(false, Get(f.Core, "_armed"), owner + " manual selection stays pending application");
            if (manual)
            {
                f.Setting("ManualAdventureEnabled", false);
                f.Setting("CustomAdventureEnabled", false);
                f.SetRuntime("_manualAdventureSetupActive", false);
            }
            else f.Lock(owner, false);
            f.Run();
            f.ExpectApplied(1, 0d, owner + " release really applies the manually selected row");
            f.Run(2d);
            f.ExpectApplied(1, 2d, owner + " new row earns only after its real application");
        }
    }

    private static void CheckCompletedRestartFailure(Assembly assembly)
    {
        using (var f = new Fixture(assembly, 4))
        {
            f.PrepareApplication();
            f.Save();
            f.Run();
            f.Run(2d);
            f.Run(2d);
            Equal(true, f.Gate(), "completed restart fixture begins with durable earned work");
            f.ReplaceMain();
            f.Start();
            var reason = JsonValue(f.Progress(), "timingReason");
            var recovery = f.Get("_protectedRecoveryReason");
            Contains((string) recovery, "Restored saved progress", "fixture has a real nonempty recovery reason to preserve");
            var args = f.OverrideArgs("restart", 0);
            var before = f.CoreState();
            var path = f.Checkpoint;
            var checkpoint = File.ReadAllText(path);
            var blocked = Path.Combine(f.DirectoryPath, "restart-not-a-directory");
            File.WriteAllText(blocked, "block restart checkpoint");
            f.SetStatic("_dir", blocked);
            try { f.Call("OverrideCustomStage", args); throw new Exception("Non-durable completed restart unexpectedly succeeded."); }
            catch (TargetInvocationException error) { Equal(typeof(IOException), error.InnerException.GetType(), "failed completed restart reports the actual checkpoint failure"); }
            Equal(before, f.CoreState(), "failed completed restart restores the previous finished engine and history");
            Equal(true, f.Get("_protectedCheckpointDurable"), "rollback retains the previous valid durable checkpoint");
            Equal(true, f.Gate(), "failed restart cannot close a previously valid completed gate");
            Equal(recovery, f.Get("_protectedRecoveryReason"), "failed restart preserves its previous recovery reason");
            Equal(string.Empty, f.Get("_protectedPersistenceError"), "rolled-back valid state must not claim its checkpoint is invalid");
            Equal(reason, JsonValue(f.Progress(), "timingReason"), "failed restart leaves truthful prior timing reason rather than a false rebirth block");
            Equal(checkpoint, File.ReadAllText(path), "failed completed restart preserves the actual durable sidecar bytes");
            f.SetStatic("_dir", f.DirectoryPath);
            f.Call("OverrideCustomStage", args);
            Equal(false, f.Gate(), "successful retry reopens the real restart obligation");
            Equal(true, f.Get("_protectedCheckpointDurable"), "successful restart retry is durably committed");
            Equal(string.Empty, f.Get("_protectedPersistenceError"), "successful restart retry has no stale checkpoint-write error");
            Equal(false, JsonValue(f.Progress(), "timingReason").Contains("automatic rebirth is blocked"), "pending restarted work is not mislabeled as a disk-write failure");
            f.Run();
            f.ExpectApplied(0, 0d, "successful retry actually reapplies the restarted row");
        }
    }

    private static void CheckManualDispatcher(Assembly assembly)
    {
        using (var f = new Fixture(assembly, 10, 4))
        {
            f.PrepareApplication();
            f.Save();
            f.Run();
            var args = f.OverrideArgs("skip", 0);
            RejectOverride(f, args, typeof(InvalidOperationException), "dispatcher before startup", true);
            f.SetRuntime("_started", true);
            RejectOverride(f, args, typeof(InvalidOperationException), "dispatcher without injector lease", true);
            f.AcquireLease();
            var exclusive = false;
            try
            {
                using (var duplicate = new FileStream(Path.Combine(f.DirectoryPath, ".injector.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            }
            catch (IOException) { exclusive = true; }
            Equal(true, exclusive, "dispatcher fixture holds an actual exclusive injector lease");
            f.SetRuntime("_unloaded", true);
            RejectOverride(f, args, typeof(InvalidOperationException), "unloaded dispatcher", true);
            f.SetRuntime("_unloaded", false);
            var owner = f.Get("reference");
            f.SetStatic("reference", null);
            try { RejectOverride(f, args, typeof(InvalidOperationException), "dispatcher non-owner", true); }
            finally { f.SetStatic("reference", owner); }
            Contains(f.Dispatch(args), "Manual skip", "actual setPlanStage dispatcher routes to the production override policy");
            Equal(1, Property(f.Core, "StageIndex"), "actual dispatcher commits the requested manual selection");
            Equal(false, Get(f.Core, "_armed"), "dispatcher action still defers real allocation to automation");
            f.Run();
            f.ExpectApplied(1, 0d, "dispatcher-selected row is actually applied by the next main pass");
        }
    }

    private static void CheckManualCheckpointShapes(Assembly assembly)
    {
        foreach (var corruption in new[]
        {
            new {Name = "skipped without completion", Count = 1, Patch = "{\"skipped\":[true]}"},
            new {Name = "completed current row without finish", Count = 1, Patch = "{\"completed\":[true]}"},
            new {Name = "finished without completion", Count = 1, Patch = "{\"finished\":true}"},
            new {Name = "unvisited applied history", Count = 1, Patch = "{\"visits\":[0]}"},
            new {Name = "zero-earned completion without explicit skip", Count = 1, Patch = "{\"finished\":true,\"completed\":[true],\"earned\":[0],\"stageElapsed\":0}"},
            new {Name = "ghost future skip", Count = 2, Patch = "{\"skipped\":[false,true]}"}
        })
        using (var f = new Fixture(assembly, corruption.Count == 1 ? new[] {4} : new[] {4, 4}))
        {
            f.PrepareApplication();
            f.Save();
            f.Run();
            f.Run(2d);
            f.Call("SaveProtectedStageProgress", true);
            var saved = f.Parse(File.ReadAllText(f.Checkpoint));
            var scheduler = Json(saved, "scheduler");
            var patch = f.Parse(corruption.Patch);
            foreach (var field in (IEnumerable) Property(patch, "Linq"))
                SetJson(scheduler, (string) Property(field, "Key"), Property(field, "Value"));
            var path = f.Checkpoint; var bytes = saved.ToString();
            File.WriteAllText(path, bytes);
            f.ReplaceMain();
            f.ExpectCheckpointRejected(path, bytes, corruption.Name);
            f.RecoverCheckpoint(path, bytes, corruption.Name);
        }
        using (var f = new Fixture(assembly, 4))
        {
            f.PrepareApplication();
            f.Save();
            f.Run();
            f.Override("skip", 0);
            Equal(true, Property(f.Core, "Finished"), "explicit terminal skip may finish a selected fixed row");
            Equal(0d, f.Earned(0), "explicit terminal skip honestly retains zero earned seconds");
            Equal(true, f.Gate(), "durable explicit terminal skip is a permitted protected waiver");
            var generation = f.Get("_protectedGeneration");
            f.ReplaceMain();
            f.Start();
            Equal(generation, f.Get("_protectedGeneration"), "codec 2 accepts the valid finished/skipped/zero-earned combination");
            Equal(true, f.Gate(), "valid explicit terminal waiver remains durable after restore");
            ExpectHistoryRow(f.Progress(), 0, "skipped", 0d, 0d, 1);
            f.Override("restart", 0);
            Equal(false, Property(f.Core, "Finished"), "restart reopens a previously finished manual selection");
            Equal(false, ((bool[]) Property(f.Core, "Skipped"))[0], "restart removes the terminal skip waiver");
            Equal(false, f.Gate(), "restarting a finished waiver blocks rebirth again");
            f.Run();
            f.ExpectApplied(0, 0d, "finished skipped row must actually apply again after restart");
            f.Run(2d);
            f.ExpectApplied(0, 2d, "reopened terminal row earns only new real application time");
        }
    }

    private static void CheckRecreatedPlan(Assembly assembly)
    {
        using (var f = new Fixture(assembly, 10, 4))
        {
            f.PrepareApplication();
            f.Save();
            f.Run();
            f.Run(2d);
            f.Override("skip", 0);
            f.Call("SaveProtectedStageProgress", true);
            var checkpoint = f.Checkpoint;
            var stale = File.ReadAllText(checkpoint);
            var generation = f.Get("_protectedGeneration");
            var store = f.Get("CustomPlanStore");
            f.Setting("UseCustomRebirthPlan", false);
            store.GetType().GetMethod("DeletePlan", All).Invoke(store, new object[] {"protected-integration"});
            Equal(null, f.Core, "actual plan deletion forgets selected runtime history");
            Equal(false, File.Exists(checkpoint), "actual deletion removes its stage sidecar");
            f.ReplaceMain();
            File.WriteAllText(checkpoint, stale); // Simulate a leftover same-epoch sidecar in the unique fixture directory.
            Equal("protected-integration", store.GetType().GetMethod("CreatePlan", All).Invoke(store, new[] {"protected-integration", f.Plan}), "actual store recreates the same selected filename");
            Equal(false, File.Exists(checkpoint), "new-plan Save invokes ForgetStageProgress even on a fresh Main with no selected engine");
            f.Setting("UseCustomRebirthPlan", true);
            f.Run();
            f.ExpectApplied(0, 0d, "recreated plan starts at its own uncredited first application");
            Equal(false, Equals(generation, f.Get("_protectedGeneration")), "recreation never inherits the deleted generation");
            Equal(string.Empty, Property(f.Core, "LastOverride"), "recreation cannot inherit a stale manual action");
            Equal("False,False", string.Join(",", (bool[]) Property(f.Core, "Skipped")), "recreation cannot inherit stale manual waivers");
            Equal("1,0", string.Join(",", (int[]) Property(f.Core, "Visits")), "recreation has only its actual first visit");
            Equal(false, f.Gate(), "recreated fixed work must be earned or explicitly waived again");
        }
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly string DirectoryPath;
        internal string PlanPath => Path.Combine(DirectoryPath, "plans", "protected-integration.json");
        internal readonly object Character, Plan;
        private readonly Type _mainType, _allocator, _rebirthType;
        private readonly object _settings, _store;
        private object _main;
        private readonly Dictionary<FieldInfo, object> _saved = new Dictionary<FieldInfo, object>();
        private readonly Dictionary<string, object> _claims = new Dictionary<string, object>();
        private readonly MemoryStream _log = new MemoryStream();
        private StreamWriter _writer;
        private IDictionary<string, long> _caps;
        private Dictionary<string, long> _savedCaps;
        private long _idleBeforeRecall, _energyBeforeRecall;
        private FileStream _lease;
        private readonly bool _setupBoundaryBefore = IronPillSetupBoundary.Enabled;

        internal Fixture(Assembly assembly, params int[] durations)
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), "opencode", "ngu-protected-integration-" + Guid.NewGuid().ToString("N"));
            _mainType = assembly.GetType("NGUInjector.Main", true);
            _allocator = assembly.GetType("NGUInjector.Managers.CustomStageAllocator", true);
            _rebirthType = assembly.GetType("NGUInjector.AllocationProfiles.RebirthStuff.CustomPlanRebirth", true);
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                IronPillSetupBoundary.Enabled = true;
                foreach (var name in new[] {"reference", "Character", "Controller", "Settings", "CustomPlanStore", "settingsForm", "OutputWriter", "_customAutomationStatus", "_dir"})
                {
                    var field = _mainType.GetField(name, All);
                    _saved.Add(field, field.GetValue(null));
                    field.SetValue(null, null);
                }
                foreach (var type in new[] {_allocator, _rebirthType, assembly.GetType("NGUInjector.Managers.BlackBarAllocator", true)})
                foreach (var field in type.GetFields(All).Where(x => x.IsStatic && !x.IsInitOnly && !x.IsLiteral))
                    _saved.Add(field, field.GetValue(null));
                _caps = (IDictionary<string, long>) assembly.GetType("NGUInjector.Managers.BlackBarAllocator", true).GetField("NguBufferedCaps", All).GetValue(null);
                _savedCaps = new Dictionary<string, long>(_caps);
                SetStatic("_dir", DirectoryPath);
                _writer = new StreamWriter(_log) {AutoFlush = true};
                SetStatic("OutputWriter", _writer);
                Character = New(_mainType.GetField("Character", All).FieldType);
                Set(Child(Character, "rebirthTime"), "totalseconds", 5900d);
                Set(Child(Character, "totalPlaytime"), "totalseconds", 105900d);
                Set(Child(Character, "stats"), "rebirthNumber", 7L);
                Set(Character, "attackMulti", 1d);
                Set(Character, "nextAttackMulti", 2d);
                Set(Child(Character, "rebirth"), "character", Character);
                var wishes = Child(Character, "wishes");
                var wishType = Field(wishes.GetType(), "wishes").FieldType;
                var list = (IList) Activator.CreateInstance(wishType);
                for (var i = 0; i < 21; i++) list.Add(New(wishType.GetGenericArguments()[0]));
                Set(wishes, "wishes", list);
                Child(Child(Character, "challenges"), "noRebirthChallenge");
                Set(Child(Character, "advancedTraining"), "level", new long[5]);
                Set(Child(Child(Character, "buttons"), "advancedTraining"), "m_Interactable", true);
                _settings = Activator.CreateInstance(assembly.GetType("NGUInjector.SavedSettings", true), new object[] {null});
                Setting("UseCustomRebirthPlan", true);
                Setting("CustomPlanFile", "protected-integration");
                Setting("AutoRebirth", true);
                Setting("ManageEnergy", true);
                Setting("ManageMagic", false);
                Setting("CustomAutoBloodSpellsEnabled", false);
                Setting("ManualAdventureEnabled", false);
                Plan = Activator.CreateInstance(assembly.GetType("NGUInjector.CustomPlans.CustomPlan", true));
                Set(Plan, "Name", "protected-integration");
                Set(Plan, "StageSchedulerEnabled", true);
                Set(Plan, "StageLoopStartIndex", -1);
                Set(Plan, "StageDurationSeconds", 6000);
                Set(Plan, "AutoRebirthEnabled", true);
                Set(Plan, "AutoIronPillEnabled", false);
                Set(Plan, "TimeTriggerEnabled", true);
                Set(Plan, "TimeTriggerSeconds", 300);
                Set(Plan, "NumberTriggerEnabled", false);
                Set(Plan, "NumberMultiplierTarget", 1.5d);
                Set(Plan, "NumberClimbEnabled", false);
                Set(Plan, "NumberClimbMinSeconds", 300);
                Set(Plan, "NumberClimbMultiplierTarget", 1.5d);
                Set(Plan, "AutoActivateFruits", false);
                var stageType = assembly.GetType("NGUInjector.CustomPlans.CustomPlanStage", true);
                var stages = Array.CreateInstance(stageType, durations.Length);
                for (var i = 0; i < durations.Length; i++)
                {
                    var stage = Activator.CreateInstance(stageType);
                    Set(stage, "Name", "stage-" + i);
                    Set(stage, "DurationSeconds", durations[i]);
                    Set(stage, "Percent", 1d);
                    Set(stage, "EnergyPriorities", new[] {"AT-1"});
                    Set(stage, "SourceJson", Parse("{\"PresetId\":\"\"}")); // Deliberate concrete inline stages, not legacy imports.
                    stages.SetValue(stage, i);
                }
                Set(Plan, "Stages", stages);
                Set(Plan, "SourceJson", Activator.CreateInstance(assembly.GetType("SimpleJSON.JSONObject", true)));
                // Bypass only constructor-created presets, not the actual Save/Load implementations.
                _store = New(_mainType.GetField("CustomPlanStore", All).FieldType);
                var plansDir = Path.Combine(DirectoryPath, "plans");
                Directory.CreateDirectory(plansDir);
                Set(_store, "_plansDir", plansDir);
                var library = Activator.CreateInstance(assembly.GetType("NGUInjector.CustomPlans.PresetLibraryStore", true), All,
                    null, new object[] { DirectoryPath, plansDir }, null);
                Set(_store, "<PresetLibrary>k__BackingField", library);
                SetStatic("Character", Character);
                SetStatic("Settings", _settings);
                SetStatic("CustomPlanStore", _store);
                _rebirthType.GetMethod("ResetRuntimeState", All).Invoke(null, null);
                ReplaceMain();
                PrepareApplicationModels();
            }
            catch { Dispose(); throw; }
        }

        internal object Core { get { return Get("_protectedScheduler"); } }
        internal bool ApplicationSucceeded { get { return (bool) _allocator.GetProperty("LastStageApplicationSucceeded", All).GetValue(null); } }
        internal int BossPushStarts { get { return System.Text.Encoding.UTF8.GetString(_log.ToArray()).Split(new[] {"Starting boss push"}, StringSplitOptions.None).Length - 1; } }
        internal string LogText => System.Text.Encoding.UTF8.GetString(_log.ToArray());
        internal double Elapsed { get { return (double) ProtectedTimingIntegrationChecks.Get(ProtectedTimingIntegrationChecks.Get(Character, "rebirthTime"), "totalseconds"); } }
        internal string Checkpoint { get { return (string) _mainType.GetProperty("ProtectedCheckpointPath", All).GetValue(_main); } }
        internal object Get(string name) { return ProtectedTimingIntegrationChecks.Get(_main, name); }
        internal void SetRuntime(string name, object value) { Field(_mainType, name).SetValue(_main, value); }
        internal void SetStatic(string name, object value) { _mainType.GetField(name, All).SetValue(null, value); }
        internal object Call(string name, params object[] args) { return _mainType.GetMethod(name, All).Invoke(_main, args); }
        internal void PrepareFight() => NativeFightFixture.Prepare(_mainType.Assembly, Character);
        internal string Request(string command, string opportunity) => NativeFightFixture.Request(_main, DirectoryPath, command, opportunity);
        internal object Stage(int index) { return ((Array) ProtectedTimingIntegrationChecks.Get(Plan, "Stages")).GetValue(index); }
        internal void Setting(string name, object value) { _settings.GetType().GetProperty(name, All).SetValue(_settings, value); }
        internal double Earned(int index) { return ((double[]) Property(Core, "EarnedSeconds"))[index]; }
        internal bool Gate() { return (bool) _allocator.GetMethod("ProtectedWorkComplete", All).Invoke(null, new[] {Plan}); }
        internal object Start() { return Active(); }
        internal void RequireCheckpoint(string label)
        {
            Equal(true, Get("_protectedCheckpointDurable"), label + "; production log: " + System.Text.Encoding.UTF8.GetString(_log.ToArray()));
            Equal(true, File.Exists(Checkpoint), label + " writes the real sidecar under Main._dir");
        }
        internal void ExpectCheckpointRejected(string path, string bytes, string label)
        {
            Equal(null, Start(), label + " cannot admit a new uncredited entry");
            Equal(true, !string.IsNullOrEmpty((string)Get("_protectedOuterCheckpointIssue")), label + " retains its rejection evidence");
            Equal(false, ProtectedTimingIntegrationChecks.Get(Core, "_armed"), label + " remains unarmed");
            Equal(false, Gate(), label + " cannot authorize rebirth");
            Equal(false, Call("SaveProtectedStageProgress", true), label + " refuses forced checkpoint replacement");
            Equal(bytes, File.ReadAllText(path), label + " preserves exact original checkpoint bytes");
        }
        internal void RecoverCheckpoint(string path, string bytes, string label)
        {
            Call("GetProtectedStage", Load());
            var recovery = Json(Call("GetExecutionPersistenceRuntime"), "recovery");
            var preview = Call("PreviewExecutionRestart", Parse("{\"stageIndex\":0,\"checkpointRevision\":" + Json(recovery, "checkpointRevision")
                + ",\"planRevision\":" + Json(recovery, "planRevision") + "}"));
            Equal(bytes, File.ReadAllText(path), label + " restart preview preserves evidence");
            var apply = Parse("{\"token\":" + Json(preview, "token") + ",\"checkpointRevision\":" + Json(preview, "checkpointRevision") + "}");
            Call("ApplyExecutionRebind", apply);
            Equal(null, Get("_protectedOuterCheckpointIssue"), label + " explicit reviewed restart clears rejection");
            var archives = Directory.GetFiles(DirectoryPath, "stage-recovery-*.json");
            Equal(1, archives.Length, label + " archives exactly one rejected checkpoint");
            Equal(bytes, File.ReadAllText(archives[0]), label + " archive retains exact original bytes");
            Equal(0, Property(Core, "StageIndex"), label + " starts only the explicitly selected row");
            Equal(0d, Earned(0), label + " restart grants no protected credit");
            Equal(false, ProtectedTimingIntegrationChecks.Get(Core, "_armed"), label + " new entry awaits native verification");
            try { Call("ApplyExecutionRebind", apply); throw new Exception("Consumed recovery token was accepted."); }
            catch (TargetInvocationException error) { Contains(error.InnerException.Message, "expired", label + " recovery token cannot renew a second budget"); }
        }
        internal object Active() { return _allocator.GetMethod("GetActiveStage", All).Invoke(null, new[] {Plan}); }
        internal object Snapshot() { return _allocator.GetMethod("GetSchedulerSnapshot", All).Invoke(null, new object[] {Plan, Elapsed}); }
        internal void Save() { _store.GetType().GetMethod("Save", All).Invoke(_store, new[] {"protected-integration", Plan}); }
        internal object Load() { return _store.GetType().GetMethod("Load", All).Invoke(_store, new object[] {"protected-integration"}); }
        internal object Parse(string json) { return _mainType.Assembly.GetType("SimpleJSON.JSON", true).GetMethod("Parse", All).Invoke(null, new object[] {json}); }
        internal string CoreState() { return Core.GetType().GetMethod("ExportState", All).Invoke(Core, null).ToString(); }
        internal string CoordinatorState()
        {
            return string.Join("|", _mainType.GetFields(All).Where(x => !x.IsStatic && x.Name.StartsWith("_protected"))
                .OrderBy(x => x.Name).Select(x => x.Name + "=" + x.GetValue(_main)));
        }
        internal string ConfirmationState()
        {
            return string.Join("|", _rebirthType.GetFields(All).Where(x => x.IsStatic && !x.IsLiteral)
                .OrderBy(x => x.Name).Select(x => x.Name + "=" + x.GetValue(null)));
        }
        internal string Files()
        {
            return string.Join("|", Directory.GetFiles(DirectoryPath, "*", SearchOption.AllDirectories).OrderBy(x => x)
                .Select(x => x + ":" + File.GetLastWriteTimeUtc(x).Ticks + ":" + (Path.GetFileName(x) == ".injector.lock" ? "exclusive lease" : Convert.ToBase64String(File.ReadAllBytes(x)))));
        }
        internal object OverrideArgs(string action, int index)
        {
            var revision = (string) ProtectedTimingIntegrationChecks.Get(Load(), "RuntimeRevision");
            var entry = JsonValue(Progress(), "stageEntry");
            return Parse("{\"name\":\"protected-integration\",\"expectedRevision\":\"" + revision
                + "\",\"expectedEntry\":\"" + entry + "\",\"action\":\"" + action + "\",\"stageIndex\":" + index + "}");
        }
        internal string Override(string action, int index) { return (string) Call("OverrideCustomStage", OverrideArgs(action, index)); }
        internal string Dispatch(object args) { return (string) Call("ExecuteExternalUiCommand", "setPlanStage", args, null); }
        internal void AcquireLease()
        {
            _lease = (FileStream) _mainType.Assembly.GetType("NGUInjector.JsonFiles", true).GetMethod("AcquireInjectorLease", All)
                .Invoke(null, new object[] {DirectoryPath, 0});
            SetRuntime("_injectorLease", _lease);
        }
        internal object Progress()
        {
            Set(_main, "_customRuntimePlanCache", Plan);
            Set(_main, "_customRuntimePlanCacheName", "protected-integration");
            Set(_main, "_customRuntimePlanCacheLoadedAt", DateTime.Now);
            return Call("GetCustomPlanProgress");
        }
        internal bool Rebirth(string method)
        {
            var rebirth = Activator.CreateInstance(_rebirthType, All, null, new[] {Plan}, null);
            return (bool) _rebirthType.GetMethod(method, All, null, Type.EmptyTypes, null).Invoke(rebirth, null);
        }
        internal void ConfirmNumber()
        {
            if (!(bool) ProtectedTimingIntegrationChecks.Get(Plan, "NumberTriggerEnabled") && !(bool) ProtectedTimingIntegrationChecks.Get(Plan, "NumberClimbEnabled")) return;
            Equal(false, Rebirth("RebirthAvailable"), "NUMBER initially requires the real confirmation window");
            _rebirthType.GetField("_numberConfirmationStartedAt", All).SetValue(null, Elapsed - 15d);
        }
        internal void ReplaceMain()
        {
            _main = New(_mainType);
            NativeFightFixture.InitializeMain(_main);
            Set(_main, "_customIronPillPrepStartedAt", -1d);
            SetStatic("reference", _main);
        }
        internal void ObserveCloudLoad(double loadedElapsed, double loadedTotal, bool whileLoading)
        {
            var files = Files();
            SetRuntime("_protectedTimingLoadingSave", true);
            Set(ProtectedTimingIntegrationChecks.Get(Character, "rebirthTime"), "totalseconds", 10d);
            Set(ProtectedTimingIntegrationChecks.Get(Character, "totalPlaytime"), "totalseconds", 100d);
            Call("ObserveCustomRebirthEpoch");
            Equal(null, Start(), "pre-load baseline cannot start protected timing while loading");
            Equal(null, Core, "pre-load baseline leaves the coordinator uninitialized");
            Equal(files, Files(), "pre-load observation and Start preserve checkpoint bytes and timestamps");

            // SnapshotEpoch tracks stats identity independently of the rebirth counter.
            var rebirth = ProtectedTimingIntegrationChecks.Get(ProtectedTimingIntegrationChecks.Get(Character, "stats"), "rebirthNumber");
            Set(Child(Character, "stats"), "rebirthNumber", rebirth);
            Set(ProtectedTimingIntegrationChecks.Get(Character, "rebirthTime"), "totalseconds", loadedElapsed);
            Set(ProtectedTimingIntegrationChecks.Get(Character, "totalPlaytime"), "totalseconds", loadedTotal);
            SetRuntime("_customStageKey", "pre-load-entry");
            SetRuntime("_customStageEquipmentKey", "pre-load-equipment");
            if (!whileLoading) SetRuntime("_protectedTimingLoadingSave", false);
            Call("ObserveCustomRebirthEpoch");
            Equal(null, Get("_customStageKey"), "real epoch observation clears stale resource ownership");
            Equal(null, Get("_customStageEquipmentKey"), "real epoch observation clears stale equipment ownership");
            Equal(null, Core, "loaded epoch observation does not initialize protected timing");
            if (whileLoading)
            {
                Equal(null, Start(), "loaded snapshot cannot start protected timing until loading clears");
                Equal(null, Core, "Start remains uninitialized while the loaded snapshot is gated");
            }
            Equal(files, Files(), "loaded observation and gated Start never rewrite the saved checkpoint");
            SetRuntime("_protectedTimingLoadingSave", false);
        }
        internal void Advance(double seconds)
        {
            foreach (var name in new[] {"rebirthTime", "totalPlaytime"})
            {
                var clock = ProtectedTimingIntegrationChecks.Get(Character, name);
                Set(clock, "totalseconds", (double) ProtectedTimingIntegrationChecks.Get(clock, "totalseconds") + seconds);
            }
        }
        internal void MarkApplied()
        {
            Call("RunCustomPlanAutomation", Plan);
            var key = Call("ProtectedStageResourceKey", Plan);
            Equal(true, Call("InlineStageSetupVerified", Plan), "native setup verifies before persistence scenario credit: " + _mainType.GetMethod("GetCustomAutomationStatus", All).Invoke(null, null));
            Equal(true, _allocator.GetMethod("StageApplicationStillOwned", All).Invoke(null, new[] {key}), "persistence scenario owns actual current-entry resource application");
            Equal(true, Call("ProtectedStageOwnershipAvailable", Plan), "actual AT application passes work eligibility and ownership");
            Equal(true, ProtectedTimingIntegrationChecks.Get(Core, "_armed"), "actual coordinator commit arms the engine");
        }
        internal void Tick()
        {
            // The coordinator samples Stopwatch independently. Seed only the last
            // heartbeat, so min(rebirth delta, heartbeat delta) is deterministically 2.
            Set(Core, "_lastHeartbeat", Heartbeat - 2d);
            Advance(2d);
            Start();
        }
        internal void Credit() { MarkApplied(); Tick(); }
        internal void PrepareApplication()
        {
            Advance(-Elapsed);
            Set(Plan, "AutoRebirthEnabled", false);
            Set(Plan, "TimeTriggerEnabled", false);
            Set(Plan, "StageDurationSeconds", 60);
            PrepareApplicationModels();
        }
        private void PrepareApplicationModels()
        {
            Set(Character, "menuID", 0); // Keep unsubstituted game rendering on its inactive menu path.
            Child(Character, "settings");
            Set(Child(Character, "machine"), "speedEnergy", 37L);
            Child(Child(Character, "wandoos98"), "bootupTime");
            foreach (var name in new[] {"wandoos98Controller", "timeMachineController", "wishesController"})
                Set(Child(Character, name), "character", Character);
            var augs = Child(Character, "augmentsController");
            Set(augs, "augments", Array.CreateInstance(Field(augs.GetType(), "augments").FieldType.GetElementType(), 0));
            var ngu = Child(Character, "NGUController");
            Set(ngu, "NGU", Array.CreateInstance(Field(ngu.GetType(), "NGU").FieldType.GetElementType(), 0));
            var training = ProtectedTimingIntegrationChecks.Get(Character, "advancedTraining");
            Set(training, "energy", new long[5]);
            Set(training, "levelTarget", new[] {-1L, -1L, -1L, -1L, -1L});
            var controllers = Child(Character, "advancedTrainingController");
            var names = new[] {"defense", "attack", "block", "wandoosEnergy", "wandoosMagic"};
            for (var i = 0; i < names.Length; i++)
            {
                var controller = Child(controllers, names[i]);
                Set(controller, "character", Character);
                Set(controller, "id", i);
            }
            var wishes = (IList) ProtectedTimingIntegrationChecks.Get(ProtectedTimingIntegrationChecks.Get(Character, "wishes"), "wishes");
            while (wishes.Count <= 190) wishes.Add(New(wishes.GetType().GetGenericArguments()[0]));
            // A completed real game wish caps AT allocation, without bypassing the
            // mandatory stage targets or replacing any game/controller algorithm.
            Set(wishes[190], "level", 1);
            IronPillReservationChecks.PrepareSetupObservation(_mainType.Assembly, Character);
        }
        internal void PrepareNguApplication()
        {
            PrepareApplication();
            Setting("ManageMagic", true);
            Child(Character, "magic");
            var buttons = ProtectedTimingIntegrationChecks.Get(Character, "buttons");
            Set(Child(buttons, "ngu"), "m_Interactable", true);
            foreach (var name in new[] {"wandoos", "brokenTimeMachine", "bloodMagic"}) Child(buttons, name);
            var challenges = ProtectedTimingIntegrationChecks.Get(Character, "challenges");
            // The real challenge guard gives zero gross gold without substituting
            // any economic/formula method; NGU primary work is still available.
            Set(Child(challenges, "timeMachineChallenge"), "inChallenge", true);
            var blood = Child(Character, "bloodMagicController");
            Set(blood, "character", Character);
            Set(blood, "bloodMagics", Array.CreateInstance(Field(blood.GetType(), "bloodMagics").FieldType.GetElementType(), 0));
            var ngu = Child(Character, "NGU");
            var controllers = ProtectedTimingIntegrationChecks.Get(Character, "NGUController");
            foreach (var energy in new[] {true, false})
            {
                var field = Field(ngu.GetType(), energy ? "skills" : "magicSkills");
                var skills = (IList) Activator.CreateInstance(field.FieldType);
                var controllerField = Field(controllers.GetType(), energy ? "NGU" : "NGUMagic");
                var bars = Array.CreateInstance(controllerField.FieldType.GetElementType(), energy ? 9 : 7);
                for (var i = 0; i < bars.Length; i++)
                {
                    var skill = New(field.FieldType.GetGenericArguments()[0]);
                    foreach (var target in new[] {"target", "evilTarget", "sadisticTarget"}) Set(skill, target, -1L);
                    skills.Add(skill);
                    var bar = New(bars.GetType().GetElementType());
                    Set(bar, "character", Character);
                    Set(bar, "id", i);
                    bars.SetValue(bar, i);
                }
                field.SetValue(ngu, skills);
                controllerField.SetValue(controllers, bars);
            }
            var preset = _mainType.Assembly.GetType("NGUInjector.SettingsForm", true).GetMethod("StagePresetDefinition", All).Invoke(null, new object[] {4});
            foreach (var stage in (Array) ProtectedTimingIntegrationChecks.Get(Plan, "Stages"))
            {
                Set(stage, "EnergyFocus", ProtectedTimingIntegrationChecks.Get(preset, "EnergyFocus"));
                Set(stage, "MagicFocus", ProtectedTimingIntegrationChecks.Get(preset, "MagicFocus"));
                Set(stage, "EnergyPriorities", null);
                Set(stage, "MagicPriorities", null);
            }
            Equal(5, ProtectedTimingIntegrationChecks.Get(Stage(0), "EnergyFocus"), "profile uses the existing native Energy NGU preset");
            Equal(6, ProtectedTimingIntegrationChecks.Get(Stage(0), "MagicFocus"), "native Energy NGU preset really owns both lanes");
        }
        internal void PrepareControlledGear()
        {
            Child(ProtectedTimingIntegrationChecks.Get(Character, "challenges"), "noEquipmentChallenge");
            Set(Character, "bossID", 100);
            var inventory = ProtectedTimingIntegrationChecks.Get(Character, "inventory");
            var slots = new[] {"head", "chest", "legs", "boots", "weapon"};
            var parts = new[] {"Head", "Chest", "Legs", "Boots", "Weapon"};
            for (var i = 0; i < slots.Length; i++)
            {
                var item = ProtectedTimingIntegrationChecks.Get(inventory, slots[i]);
                Set(item, "id", i + 40); Set(item, "removable", true);
                Set(item, "type", Enum.Parse(Field(item.GetType(), "type").FieldType, parts[i]));
            }
            var controller = ProtectedTimingIntegrationChecks.Get(Character, "inventoryController");
            Set(ProtectedTimingIntegrationChecks.Get(controller, "itemInfo"), "itemName", Enumerable.Range(0, 45).Select(i => "Fixture " + i).ToArray());
            var optimizer = _mainType.Assembly.GetType("NGUInjector.Managers.GearOptimizer", true);
            var optimize = optimizer.GetMethod("Optimize", All);
            var result = optimize.Invoke(null, new[] {Enum.ToObject(optimize.GetParameters()[0].ParameterType, 2), Enum.ToObject(optimize.GetParameters()[1].ParameterType, 0)});
            Equal(true, Property(result, "Success"), "native controlled-gear fixture has an actual feasible optimizer selection");
        }
        internal void Run(double seconds = 0d)
        {
            if (Core != null) Set(Core, "_lastHeartbeat", Heartbeat - seconds);
            Advance(seconds);
            _energyBeforeRecall = (long) ProtectedTimingIntegrationChecks.Get(ProtectedTimingIntegrationChecks.Get(Character, "machine"), "speedEnergy");
            _idleBeforeRecall = (long) ProtectedTimingIntegrationChecks.Get(Character, "idleEnergy");
            Call("RunCustomPlanAutomation", Plan);
        }
        internal void RunQuick(double seconds)
        {
            Set(Core, "_lastHeartbeat", Heartbeat - seconds);
            Advance(seconds);
            Set(_main, "_customLastQuickResourceRefresh", DateTime.MinValue);
            Call("RunQuickCustomResourceRefresh", Plan);
        }
        internal void ExpectApplied(int index, double earned, string label)
        {
            Equal(index, Property(Core, "StageIndex"), label + " selects the expected row");
            Equal(earned, Earned(index), label + " has only its own earned fixed time");
            Equal(true, _allocator.GetProperty("LastStageApplicationSucceeded", All).GetValue(null), label + " executes actual ApplyStage successfully: " + _mainType.GetMethod("GetCustomAutomationStatus", All).Invoke(null, null));
            Equal(true, ProtectedTimingIntegrationChecks.Get(Core, "_armed"), label + " is armed by actual CommitProtectedStageApplication");
            Equal(0L, ProtectedTimingIntegrationChecks.Get(ProtectedTimingIntegrationChecks.Get(Character, "machine"), "speedEnergy"), label + " executes real game resource recall, not just engine selection");
            Equal(_idleBeforeRecall + _energyBeforeRecall, ProtectedTimingIntegrationChecks.Get(Character, "idleEnergy"), label + " conserves the real recalled resource pool when AT is capped");
            Equal(true, Call("ProtectedStageOwnershipAvailable", Plan), label + " retains the exact current stage receipt");
            Equal(true, Call("InlineStageSetupVerified", Plan), label + " verifies actual inline gear and support for this entry");
            Equal(Get("_customStageEquipmentKey"), ProtectedTimingIntegrationChecks.Get(Get("_inlineStageSelection"), "Owner"), label + " retains the actual acquired equipment owner");
        }
        internal void Lock(string boundary, bool held)
        {
            if (held)
            {
                var domain = Enum.Parse(_mainType.GetNestedType("ExecutionDomain", All), boundary == "loadout" ? "Gear" : "Support");
                var claim = Call("BeginExecutionClaim", "protected-fixture-owner", boundary, domain, (Func<bool>)(() => true));
                _claims.Add(boundary, claim);
                var grant = Call("ResolveExecutionGrant", "unowned-stage-observer", boundary, domain, (Func<bool>)(() => true), null, null);
                Equal(false, Call("ValidateExecutionGrant", grant, domain), boundary + " admitted claim denies unrelated setup authority");
                Equal(false, Call("ExecutionSetupVerified"), boundary + " claim without acquired setup cannot verify current prerequisites");
            }
            else
            {
                Equal(true, Call("EndExecutionClaim", _claims[boundary], false), boundary + " claim retires without historical setup writes");
                _claims.Remove(boundary);
            }
        }
        public void Dispose()
        {
            try
            {
                foreach (var claim in _claims.Values) Call("EndExecutionClaim", claim, false);
            }
            finally
            {
                try
                {
                    foreach (var saved in _saved) saved.Key.SetValue(null, saved.Value);
                    if (_savedCaps != null)
                    {
                        _caps.Clear();
                        foreach (var saved in _savedCaps) _caps.Add(saved.Key, saved.Value);
                    }
                }
                finally
                {
                    IronPillSetupBoundary.Enabled = _setupBoundaryBefore;
                    if (_lease != null) _lease.Dispose();
                    if (_writer != null) _writer.Dispose();
                    _log.Dispose();
                    if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, true);
                }
            }
        }
    }

    private static double Heartbeat { get { return (double) Stopwatch.GetTimestamp() / Stopwatch.Frequency; } }
    private static FieldInfo Field(Type type, string name)
    {
        for (; type != null; type = type.BaseType)
        {
            var field = type.GetField(name, All);
            if (field != null) return field;
        }
        throw new MissingFieldException(name);
    }
    private static object Get(object target, string name) { return Field(target.GetType(), name).GetValue(target); }
    private static void Set(object target, string name, object value)
    {
        var field = Field(target.GetType(), name);
        field.SetValue(target, value == null || field.FieldType.IsInstanceOfType(value) ? value : Convert.ChangeType(value, field.FieldType));
    }
    private static object Property(object target, string name) { return target.GetType().GetProperty(name, All).GetValue(target); }
    private static object Child(object parent, string name)
    {
        var child = New(Field(parent.GetType(), name).FieldType);
        Set(parent, name, child);
        return child;
    }
    private static object New(Type type)
    {
        var value = FormatterServices.GetUninitializedObject(type);
        for (var current = type; current != null; current = current.BaseType)
        {
            var pointer = current.GetField("m_CachedPtr", All);
            if (pointer != null) { pointer.SetValue(value, new IntPtr(1)); break; }
        }
        return value;
    }
    private static object Json(object root, string key) { return root.GetType().GetProperty("Item", new[] {typeof(string)}).GetValue(root, new object[] {key}); }
    private static void SetJson(object root, string key, object value) { root.GetType().GetProperty("Item", new[] {typeof(string)}).SetValue(root, value, new object[] {key}); }
    private static string JsonValue(object root, string key) { return (string) Property(Json(root, key), "Value"); }
    private static double JsonNumber(object root, string key) { return (double) Property(Json(root, key), "AsDouble"); }
    private static void ExpectHistoryRow(object progress, int index, string state, double applied, double elapsed, int visits)
    {
        var rows = Json(progress, "stages");
        var row = rows.GetType().GetProperty("Item", new[] {typeof(int)}).GetValue(rows, new object[] {index});
        Equal(state, JsonValue(row, "state"), "history row " + index + " state");
        Equal(applied, JsonNumber(row, "appliedSeconds"), "history row " + index + " cumulative real applied seconds");
        Equal(elapsed, JsonNumber(row, "elapsedSeconds"), "history row " + index + " cumulative elapsed seconds");
        Equal(visits, Property(Json(row, "visits"), "AsInt"), "history row " + index + " actual visit count");
    }
    private static void Contains(string actual, string expected, string label) { Equal(true, actual != null && actual.Contains(expected), label + ": " + actual); }
    private static void Equal(object expected, object actual, string label)
    {
        _assertions++;
        if (!Equals(expected, actual)) throw new Exception(label + ": expected " + expected + ", got " + actual);
    }
}

// External dialog/decoder/load boundaries only. QuickLoad admission and all scheduler
// state transitions execute from the actual production method in the in-memory DLL.
public static class ProtectedTimingLoadBoundary
{
    public static object Save, Data;
    public static bool Cancel, ThrowDecode;
    public static int Loads;
    public static Action Loading;
    public static int Confirm(string text, string caption, int buttons) { return Cancel ? 7 : 6; }
    public static int Platform() { return 2; }
    public static int Version(object character) { return 400; }
    public static object DecodeSave(object decoder, string encoded)
    {
        if (ThrowDecode) throw new IOException("offline fixture decoder failure");
        return Save;
    }
    public static object DecodeData(object decoder, string encoded) { return Data; }
    public static bool Load(object loader, object save) { Loads++; Loading?.Invoke(); return true; }
}
