using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

public static partial class Program
{
    private static int CombatOsInvocations;
    private static bool ObserveCombatOs;
    private static string CombatOsFault;
    private static Action CombatOsAfter;
    private static Action<int> CombatRouteAfter;
    private static string CombatTimerFault;
    private static int CombatTimerWrites;
    private static int CombatFullCaptures;
    private static Action CombatIdleAttackAfter;
    private static int CombatIdleAttackWrites;

    private static void ConfigureCombatSetupPluginFixture(AssemblyDefinition plugin)
    {
        var main = plugin.MainModule.Types.Single(x => x.FullName == "NGUInjector.Main");
        foreach (var method in main.Methods.Where(x => new[] { "CaptureCombatPrepCore", "CaptureCombatPrepEquipmentMaterial", "SelectCombatPrepRequirements" }.Contains(x.Name)))
        {
            var il = method.Body.GetILProcessor();
            il.InsertBefore(method.Body.Instructions[0], il.Create(OpCodes.Call,
                plugin.MainModule.ImportReference(typeof(Program).GetMethod("ObserveCombatFullCapture", All))));
        }
    }

    public static void ObserveCombatFullCapture() { CombatFullCaptures++; }

    public static void CombatRouteCommitted(int zone) { CombatRouteAfter?.Invoke(zone); }

    private static void ConfigureCombatSetupGameFixture(AssemblyDefinition game)
    {
        var method = game.MainModule.Types.Single(x => x.Name == "Wandoos98").Methods.Single(x => x.Name == "changeOS");
        var il = method.Body.GetILProcessor();
        il.InsertBefore(method.Body.Instructions[0], il.Create(OpCodes.Call,
            game.MainModule.ImportReference(typeof(Program).GetMethod("CombatOsBefore", All))));
        foreach (var ret in method.Body.Instructions.Where(x => x.OpCode == OpCodes.Ret).ToArray())
            il.InsertBefore(ret, il.Create(OpCodes.Call, game.MainModule.ImportReference(typeof(Program).GetMethod("CombatOsCommitted", All))));
        var timer = game.MainModule.Types.Single(x => x.Name == "PlayerTime").Methods.Single(x => x.Name == "setTime"
            && x.Parameters.Count == 1 && x.Parameters[0].ParameterType.FullName == "System.Double");
        var timerIl = timer.Body.GetILProcessor(); var first = timer.Body.Instructions[0];
        timerIl.InsertBefore(first, timerIl.Create(OpCodes.Ldarg_0));
        timerIl.InsertBefore(first, timerIl.Create(OpCodes.Call, game.MainModule.ImportReference(typeof(Program).GetMethod("CombatTimerBefore", All))));
        foreach (var ret in timer.Body.Instructions.Where(x => x.OpCode == OpCodes.Ret).ToArray())
        {
            timerIl.InsertBefore(ret, timerIl.Create(OpCodes.Ldarg_0));
            timerIl.InsertBefore(ret, timerIl.Create(OpCodes.Call, game.MainModule.ImportReference(typeof(Program).GetMethod("CombatTimerCommitted", All))));
        }
        var idle = game.MainModule.Types.Single(x => x.Name == "AdventureController").Fields.Single(x => x.Name == "idleAttackMove").FieldType.Resolve()
            .Methods.Single(x => x.Name == "setToggle" && x.Parameters.Count == 0);
        var idleModelWrites = idle.Body.Instructions.Where(x => x.OpCode == OpCodes.Stfld && (x.Operand as FieldReference)?.Name == "autoattacking").ToArray();
        if (idleModelWrites.Length < 1 || idleModelWrites.Length > 2) throw new Exception("Unsupported native idle toggle model-write shape.");
        var borders = idle.Body.Instructions.Where(x => x.OpCode == OpCodes.Ldfld && (x.Operand as FieldReference)?.Name == "Border").ToArray();
        if (borders.Length != 2) throw new Exception("Unsupported native idle toggle rendering boundary.");
        foreach (var border in borders)
        {
            if (border.Previous.OpCode != OpCodes.Ldarg_0) throw new Exception("Unsupported native idle rendering stack.");
            border.Previous.OpCode = OpCodes.Call;
            border.Previous.Operand = game.MainModule.ImportReference(typeof(Program).GetMethod("CombatIdleAttackCommitted", All));
            border.OpCode = OpCodes.Ret; border.Operand = null;
        }
    }

    public static void CombatIdleAttackCommitted()
    {
        if (CombatIdleAttackAfter == null) return;
        Equal(true, Get(Get(Character, "adventure"), "autoattacking"), "native idle model committed before the rendering callback");
        CombatIdleAttackWrites++; CombatIdleAttackAfter();
    }

    public static void CombatTimerBefore(object timer)
    {
        if (CombatTimerFault == null || !ReferenceEquals(timer, Get(Get(Character, "arbitrary"), "nukeTimer"))) return;
        CombatTimerWrites++;
        if (CombatTimerFault == "before") throw new InvalidOperationException("fixture before native timer setting");
    }

    public static void CombatTimerCommitted(object timer)
    {
        if (CombatTimerFault == null || !ReferenceEquals(timer, Get(Get(Character, "arbitrary"), "nukeTimer"))) return;
        if (CombatTimerFault == "foreign") Set(timer, "totalseconds", 23d);
        if (CombatTimerFault == "after") throw new InvalidOperationException("fixture after native timer setting");
    }

    public static void CombatOsBefore()
    {
        if (!ObserveCombatOs) return;
        CombatOsInvocations++;
        var run = Get(MainInstance, "_combatPrepRun");
        Equal(false, Get(run, "OsInvoked"), "native OS entry belongs to the current unconsumed session step");
        Equal(true, Get(run, "SetupVerified"), "native OS invocation requires actual verified shared setup");
        Equal(true, CallMain("CombatPrepRequirementsVerified", run, ScheduledPhase(run)), "native OS entry validates the real current setup grant");
        if (CombatOsFault == "before") throw new InvalidOperationException("fixture before native OS reset");
    }

    public static void CombatOsCommitted()
    {
        if (!ObserveCombatOs) return;
        CombatOsAfter?.Invoke();
        if (CombatOsFault == "after") throw new InvalidOperationException("fixture after native OS reset");
    }

    private static object FreshCombatOsPreview()
    {
        var inventory = Get(Character, "inventory"); var w = Get(Character, "wandoos98");
        Set(Get(inventory, "itemList"), "jakeComplete", true); Set(w, "XLLevels", 1L);
        Set(Character, "capEnergy", 2000000000000L); Set(Character, "curEnergy", 2000000000000L); Set(Character, "idleEnergy", 2000000000000L);
        var magic = Get(Character, "magic");
        Set(magic, "capMagic", 2000000000000L); Set(magic, "curMagic", 2000000000000L); Set(magic, "idleMagic", 2000000000000L);
        return Preview(60);
    }

    private static void CheckSharedCombatSetup()
    {
        var before = Assertions;
        var failures = new List<Exception>();
        foreach (var acceptance in new Action[] { CheckCombatPartialCurrentSuccessor, CheckCombatForeignCurrentSuccessor,
            CheckCombatReviewedRestart, CheckCombatOsOutcomes, CheckCombatOsLegacyStorageIgnored, CheckCombatLegacyOsPin,
            CheckCombatBorrowedTimerOutcomes, CheckCombatBorrowedPrerequisiteCallback, CheckCombatRouteScopedConflict,
            CheckCombatRetirementCallbacks, CheckCombatForeignRouteRetry, CheckCombatIdleSettingVerification,
            CheckCombatBaselineRouteEntry, CheckCombatSuspendedBaselineGrowth, CheckCombatFocusAdmission,
            CheckRetainedFocusCombatTransition, CheckCombatPausedForeignBorrowedFields, CheckCombatUnloadRouteRetirement,
            CheckCombatSteadyObservation, CheckSharedCombatResourceExecution, CheckCombatCurrentResourceHandoff, CheckCombatPausedBorrowedFields })
        {
            try { acceptance(); }
            catch (Exception error) { failures.Add(new Exception(acceptance.Method.Name, error)); Console.Error.WriteLine(acceptance.Method.Name + ": " + error.Message); }
            finally
            {
                ObserveCombatOs = false; CombatOsFault = CombatTimerFault = null; CombatOsAfter = null;
                CombatRouteAfter = null; CombatIdleAttackAfter = null; NativeGearSwapped = null;
                AdventureWeightFailRouteAfterWrite = AdventureWeightFailRetirement = AdventureWeightTrackRoutes = false;
            }
        }
        Console.WriteLine("Shared combat setup: " + (Assertions - before) + " native assertions; " + failures.Count + " failed groups.");
        if (failures.Count != 0) throw new AggregateException("Combat handoff acceptance remains blocked.", failures);
    }

    private static void CheckCombatPartialCurrentSuccessor()
    {
        foreach (var failure in new[] { "gear", "support" })
        {
            Setup(); ControlledCombatPrepClock = 10000;
            Set(Get(Get(Character, "buttons"), "ngu"), "m_Interactable", true);
            var inventory = Get(Character, "inventory"); var bag = (IList)Get(inventory, "inventory");
            var originalHead = Get(inventory, "head");
            var phaseHead = Blank(originalHead.GetType()); Set(phaseHead, "id", 60); SetEnum(phaseHead, "type", "Head");
            Set(phaseHead, "bossRequired", 1); Set(phaseHead, "removable", true);
            SetEnum(phaseHead, "spec1Type", "AdvTraining"); Set(phaseHead, "spec1Cur", 1e30f); bag[0] = phaseHead;
            var phaseChest = Blank(originalHead.GetType()); Set(phaseChest, "id", 61); SetEnum(phaseChest, "type", "Chest");
            Set(phaseChest, "bossRequired", 1); Set(phaseChest, "removable", true);
            SetEnum(phaseChest, "spec1Type", "AdvTraining"); Set(phaseChest, "spec1Cur", 1e30f); bag[1] = phaseChest;
            Set(((IList)Get(Get(Character, "diggers"), "diggers"))[5], "maxLevel", 20L);
            Set(((IList)Get(Get(Character, "diggers"), "diggers"))[1], "maxLevel", 20L);
            var w = Get(Character, "wandoos98"); Set(w, "energyLevel", 7L); Set(w, "energyProgress", .25f);
            var heads = new List<object>(); NativeGearSwapped = item => heads.Add(item);
            try
            {
                var capture = CallMain("CaptureCombatPrep", .03, .03, null);
                if (failure == "gear") FailRefreshAtSwap = Swaps + 1;
                else Set(typeof(PresetIntegrationChecks), "PodInterference", (Action<object>)(pod => {
                    Set(typeof(PresetIntegrationChecks), "PodInterference", null);
                    throw new InvalidOperationException("fixture combat support callback after committed native level");
                }));
                var run = StartServerPolicy(capture, Phase("AT", "at-planned", 60));
                var entry = Get(run, "Started"); var deadline = Get(run, "PhaseDeadline");
                Equal(false, Get(run, "SetupVerified"), "actual committed " + failure + " failure withholds coupled prerequisites; planned="
                    + Get(Get(Get(run, "Setup"), "Support"), "StableKey") + "; pendingFault=" + Get(typeof(PresetIntegrationChecks), "FailPodRefresh"));
                Equal(-1, Get(run, "AppliedResourcePhase"), "partial setup never manufactures phase credit");
                Equal(0L, ((long[])Get(Get(Character, "advancedTraining"), "energy")).Sum(), "partial setup never funds dependent AT");
                Equal(true, ReferenceEquals(phaseHead, Get(inventory, "head")), "real phase head committed before the fault");
                var committed = CallMain("CombatPrepSupportKey"); var swaps = Swaps;
                CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(run, "Id") + "\"}"));
                Equal(swaps, Swaps, "cancel performs zero historical setup swaps after committed " + failure);
                Equal(committed, CallMain("CombatPrepSupportKey"), "cancel preserves actual committed support without historical detour");
                Equal(entry, Get(run, "Started"), "cancellation retains original acquired entry time");
                Equal(deadline, Get(run, "PhaseDeadline"), "cancellation cannot renew acquired phase deadline");
                var successor = Blank(originalHead.GetType()); Set(successor, "id", 62); SetEnum(successor, "type", "Head");
                Set(successor, "bossRequired", 1); Set(successor, "removable", true);
                SetEnum(successor, "spec1Type", "EnergyCap"); Set(successor, "spec1Cur", 1000000f); bag[2] = successor;
                EquipInterruptionPreset(PrepareInterruptionPreset());
                Equal(true, ReferenceEquals(successor, Get(inventory, "head")), "current acquired B applies directly after partial combat cancellation");
                Equal(false, heads.Any(x => ReferenceEquals(x, originalHead)), "A never flashes between committed combat setup and current B");
                Equal(7L, Get(w, "energyLevel"), "shared handoff never rolls back native W history");
                Equal(.25f, Get(w, "energyProgress"), "shared handoff never resets native partial W progression");
                var writes = ResourceCommittedWrites; swaps = Swaps;
                CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(run, "Id") + "\"}"));
                Equal(writes, ResourceCommittedWrites, "late cancellation cannot replay resource accounting over current B");
                Equal(swaps, Swaps, "late cancellation cannot restore old gear over B");
            }
            finally { NativeGearSwapped = null; Set(typeof(PresetIntegrationChecks), "PodInterference", null); }
        }
    }

    private static void CheckCombatOsOutcomes()
    {
        foreach (var fault in new[] { "before", "after", "foreign", "epoch", "deadline" })
        {
            Setup(); ControlledCombatPrepClock = 10000;
            ObserveCombatOs = true; CombatOsInvocations = 0; CombatOsFault = fault; CombatOsAfter = null;
            try
            {
                var preview = FreshCombatOsPreview();
                CombatOsAfter = () => {
                    if (fault == "foreign") Set(((IList)Get(Get(Character, "diggers"), "diggers"))[11], "curLevel", 1L);
                    if (fault == "epoch") Set(Get(Character, "stats"), "rebirthNumber", 8L);
                    if (fault == "deadline") ControlledCombatPrepClock += 61;
                };
                Exception rejected = null;
                try { Start(JsonValue(preview, "previewId"), FindCandidate(preview, "WandoosNoAT")); }
                catch (Exception error) { rejected = error; }
                Equal(fault != "after", rejected != null, "only exact committed/current OS proof admits dependent work/" + fault + ": " + rejected);
                var run = Get(MainInstance, "_combatPrepRun");
                Equal(1, CombatOsInvocations, "OS effect invokes exactly once under " + fault);
                if (fault == "after")
                {
                    Equal("ModelAppliedRenderFailed", Get(run, "OsOutcome"), "known committed OS reset is distinct from unknown native outcome");
                    Equal(true, (long)Get(Get(Character, "wandoos98"), "wandoosEnergy") > 0, "known committed OS permits actual native W funding");
                    Set(Get(Character, "wandoos98"), "energyLevel", 9L);
                }
                else
                {
                    Equal("UnknownOutcome", Get(run, "OsOutcome"), "unverified OS/authority effect cannot admit dependent resources");
                    Equal(0L, Get(Get(Character, "wandoos98"), "wandoosEnergy"), "unverified OS cannot fund W energy");
                    Equal("cancelled", Get(run, "Status"), "unverified OS retires only this bounded preparation attempt");
                }
                for (var retry = 0; retry < 3; retry++) ScheduledRefresh(run);
                Equal(1, CombatOsInvocations, "executor retry never repeats irreversible OS reset under " + fault);
                if (fault == "after") Equal(9L, Get(Get(Character, "wandoos98"), "energyLevel"), "later native W progress survives OS presentation recovery");
                if (fault == "before")
                {
                    CombatOsFault = null; CombatOsAfter = null;
                    var fresh = Preview(60);
                    Equal(1, CombatOsInvocations, "preview after cancellation never replays native OS work");
                    Start(JsonValue(fresh, "previewId"), FindCandidate(fresh, "WandoosNoAT"));
                    Equal(2, CombatOsInvocations, "a fresh explicit acquisition can attempt the current native OS change");
                    Equal(false, ReferenceEquals(run, Get(MainInstance, "_combatPrepRun")), "fresh acquisition is not historical attempt restoration");
                    Equal(1, Convert.ToInt32(Get(Get(Character, "wandoos98"), "os")), "fresh native attempt applies the selected OS exactly");
                }
                Equal(false, File.Exists(Path.Combine((string)Get(MainType, "_dir"), "combat-prep-os.json")), "OS outcomes create no per-action disk state/" + fault);
            }
            finally { ObserveCombatOs = false; CombatOsFault = null; CombatOsAfter = null; }
        }
    }

    private static void CheckCombatForeignCurrentSuccessor()
    {
        foreach (var domain in new[] { "gear", "support" })
        {
            Setup(); ControlledCombatPrepClock = 10000; DiggerSlots = 2;
            Set(Get(Get(Character, "buttons"), "ngu"), "m_Interactable", true);
            var rows = (IList)Get(Get(Character, "diggers"), "diggers"); Set(rows[11], "maxLevel", 2L);
            var inventory = Get(Character, "inventory"); var bag = (IList)Get(inventory, "inventory");
            var baseline = Blank(Get(inventory, "head").GetType()); Set(baseline, "id", 62); SetEnum(baseline, "type", "Head");
            Set(baseline, "bossRequired", 1); Set(baseline, "removable", true); SetEnum(baseline, "spec1Type", "EnergyCap");
            Set(baseline, "spec1Cur", 1000000f); bag[0] = baseline;
            var args = PrepareInterruptionPreset(); var receipt = EquipInterruptionPreset(args);
            Set(Get(Get(receipt, "Policy"), "Preset"), "AutoUpdate", false);
            var phaseHead = Blank(baseline.GetType()); Set(phaseHead, "id", 60); SetEnum(phaseHead, "type", "Head");
            Set(phaseHead, "bossRequired", 1); Set(phaseHead, "removable", true); SetEnum(phaseHead, "spec1Type", "AdvTraining");
            Set(phaseHead, "spec1Cur", 1e30f); bag[1] = phaseHead;
            var run = StartServerPolicy(CallMain("CaptureCombatPrep", .03, .03, null), Phase("AT", "at-current", 60));
            Equal(true, ReferenceEquals(phaseHead, Get(inventory, "head")), "foreign handoff test acquires actual combat gear over retained B");
            if (domain == "gear")
            {
                var foreign = Blank(baseline.GetType()); Set(foreign, "id", 63); SetEnum(foreign, "type", "Head");
                Set(foreign, "bossRequired", 1); Set(foreign, "removable", true);
                bag[2] = Get(inventory, "head"); Set(inventory, "head", foreign); RefreshBonuses();
            }
            else { Set(rows[11], "curLevel", 1L); Invoke(Get(Character, "allDiggers"), "activateDigger", 11); }
            var foreignGear = CallMain("CombatPrepEquipped"); var foreignSupport = CallMain("CombatPrepSupportKey");
            var swaps = Swaps;
            ScheduledRefresh(run);
            Equal(false, Get(MainInstance, "CombatPrepRunActive"), "foreign " + domain + " retires the combat claim without adopting manual state");
            Equal(swaps, Swaps, "automatic remaining B cannot overwrite foreign " + domain + " on claim release");
            Equal(true, CallMain("CombatPrepGearMatches", foreignGear), "foreign native gear is preserved across current-intent reconciliation");
            Equal(foreignSupport, CallMain("CombatPrepSupportKey"), "foreign native support is preserved across current-intent reconciliation");
            Set(MainInstance, "_selectedSetup", null);
            var writes = ResourceCommittedWrites;
            var desired = Activator.CreateInstance(MainType.GetNestedType("SelectedSetupRequirements", System.Reflection.BindingFlags.NonPublic), true);
            Set(desired, "Owner", "retained-current-B"); Set(desired, "Gear", Get(receipt, "Gear"));
            Set(desired, "Support", Get(CallMain("CaptureSupportState"), "Selection"));
            Set(desired, "Authorized", (Func<bool>)(() => true));
            RejectInterruptionEquip(() => CallMain("ApplySelectedSetup", desired), "Selected setup requires review");
            CallMain("ReconcileCustomResources", null, true);
            Equal(swaps, Swaps, "selected application reset cannot acknowledge shared " + domain + " uncertainty");
            Equal(writes, ResourceCommittedWrites, "unreviewed coupled successor cannot fund from a reset setup receipt");
            Equal(true, CallMain("CombatPrepGearMatches", foreignGear), "reset retains actual foreign gear, not historical B or phase snapshots");
            EquipInterruptionPreset(args);
            Equal(true, CallMain("GlobalPresetReceiptValid", "dashboard"), "explicit freshly preflighted Equip can review current shared setup authority");
            Equal(true, ReferenceEquals(baseline, Get(inventory, "head")), "reviewed current B applies directly after foreign setup was preserved");
            if (domain == "support") Equal(foreignSupport, CallMain("CombatPrepSupportKey"), "reviewed Keep current retains actual foreign support rather than restoring historical support");
        }
    }

    private static void CheckCombatLegacyOsPin()
    {
        foreach (var storage in new[] { "valid", "malformed", "locked" })
        {
            Setup(); ControlledCombatPrepClock = 10000; ObserveCombatOs = true; CombatOsInvocations = 0;
            FileStream held = null;
            try
            {
                var preview = FreshCombatOsPreview();
                Start(JsonValue(preview, "previewId"), FindCandidate(preview, "WandoosNoAT"));
                var run = Get(MainInstance, "_combatPrepRun"); var w = Get(Character, "wandoos98");
                Set(w, "energyLevel", 9L); Set(w, "magicProgress", .25f);
                var resources = Resources(); var swaps = Swaps;
                var path = Path.Combine((string)Get(MainType, "_dir"), "combat-prep-os.json");
                var legacy = storage == "valid" ? "{\"version\":1,\"rebirth\":\"7\",\"origin\":1000,\"elapsed\":500,\"total\":1500,\"os\":0}" : "{invalid";
                File.WriteAllText(path, legacy);
                if (storage == "locked") held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                var plan = Invoke(Get(MainType, "CustomPlanStore"), "Load", "fixture");
                var old = ReplaceAutomaticCoordinator(plan);
                Equal(null, Get(MainInstance, "_combatPrepRun"), "restart imports no native combat attempt/" + storage);
                Equal(null, Get(MainInstance, "_combatPrepOsEpoch"), "restart imports no OS invocation authority/" + storage);
                preview = Preview(60);
                foreach (var candidate in (IEnumerable)Get(Json(preview, "candidates"), "Children")) Equal("1", JsonValue(candidate, "os"), "current native W work alone forbids destructive OS reset/" + storage);
                Invoke(old, "TickCombatPrep");
                Equal(1, CombatOsInvocations, "restart and stale callbacks never repeat native OS reset/" + storage);
                Equal(9L, Get(w, "energyLevel"), "restart preserves earned native W levels/" + storage);
                Equal(.25f, Get(w, "magicProgress"), "restart preserves native partial W progress/" + storage);
                Equal(resources, Resources(), "restart and preview cannot import resource cleanup authority/" + storage);
                Equal(swaps, Swaps, "restart and stale callbacks perform no historical gear swaps/" + storage);
                Equal(false, Get(run, "SetupVerified"), "stale run loses native setup authority/" + storage);
                held?.Dispose(); held = null;
                Equal(legacy, File.ReadAllText(path), "retired OS metadata is never read/repaired/deleted/" + storage);
            }
            finally { ObserveCombatOs = false; held?.Dispose(); }
        }
    }

    private static void CheckCombatReviewedRestart()
    {
        var plan = ScheduledSetup(); ControlledCombatPrepClock = 10000;
        var old = StartScheduled(plan); var oldEntry = Get(old, "OwnerEntry");
        var inventory = Get(Character, "inventory"); var foreign = Blank(Get(inventory, "head").GetType());
        Set(foreign, "id", 63); SetEnum(foreign, "type", "Head"); Set(foreign, "bossRequired", 1); Set(foreign, "removable", true);
        ((IList)Get(inventory, "inventory"))[2] = Get(inventory, "head"); Set(inventory, "head", foreign); RefreshBonuses();
        ScheduledRefresh(old);
        Equal(false, Get(MainInstance, "CombatPrepRunActive"), "foreign setup ends the automatic claim without granting a new acquisition");
        Equal(true, ((string)CallMain("CombatPrepStartBlocker")).Contains("requires review"), "automatic retry cannot clear shared setup uncertainty");
        var scheduler = Get(MainInstance, "_protectedScheduler");
        var entry = Get(MainInstance, "_protectedGeneration") + ":" + Get(scheduler, "Entry");
        var args = Parse("{\"name\":\"fixture\",\"expectedRevision\":\"" + Get(plan, "RuntimeRevision")
            + "\",\"expectedEntry\":\"" + entry + "\",\"action\":\"restart\",\"stageIndex\":0}");
        CallMain("OverrideCustomStage", args);
        CallMain("RunCustomPlanAutomation", plan);
        var current = Get(MainInstance, "_combatPrepRun");
        Equal("running", Get(current, "Status"), "durably accepted explicit restart admits its fresh current automatic requirements");
        Equal(false, Equals(oldEntry, Get(current, "OwnerEntry")), "only explicit restart creates the new entry and budget");
        Equal(true, Get(current, "SetupVerified"), "reviewed restart obtains actual complete shared setup proof");
        Equal(true, Get(Get(current, "ResourceOutcome"), "Verified"), "reviewed restart obtains real shared resource verification");
        Equal(true, OwnedResourceTotal(true) > 0, "reviewed automatic restart actually funds native Energy");
        CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(current, "Id") + "\"}"));
        Equal(null, CallMain("CombatPrepStartBlocker"), "fully verified reviewed restart clears only its accepted setup uncertainty");
    }

    private static void CheckCombatOsLegacyStorageIgnored()
    {
        Setup(); ControlledCombatPrepClock = 10000; ObserveCombatOs = true; CombatOsInvocations = 0;
        var preview = FreshCombatOsPreview(); FileStream pinLock = null;
        var path = Path.Combine((string)Get(MainType, "_dir"), "combat-prep-os.json"); File.WriteAllText(path, "{invalid");
        try
        {
            pinLock = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            Start(JsonValue(preview, "previewId"), FindCandidate(preview, "WandoosNoAT"));
            var current = Get(MainInstance, "_combatPrepRun"); var started = Get(current, "Started"); var deadline = Get(current, "PhaseDeadline");
            Equal(1, CombatOsInvocations, "locked retired OS history cannot withhold the current native action");
            Equal(1, Convert.ToInt32(Get(Get(Character, "wandoos98"), "os")), "native OS reset follows current verified setup despite retired storage");
            ScheduledRefresh(current);
            Equal(1, CombatOsInvocations, "same acquired session never repeats the native OS reset");
            Equal(started, Get(current, "Started"), "refresh preserves the original run entry");
            Equal(deadline, Get(current, "PhaseDeadline"), "refresh never renews phase timing");
            pinLock.Dispose(); pinLock = null;
            Equal("{invalid", File.ReadAllText(path), "native OS work neither repairs nor rewrites retired metadata");
        }
        finally { ObserveCombatOs = false; CombatPrepClockRead = null; pinLock?.Dispose(); }
    }

    private static void CheckCombatRouteScopedConflict()
    {
        var plan = AdventureWeightSetup();
        object changed = null; long expected = 0;
        try
        {
            CombatRouteAfter = zone => {
                if (zone < 0) return;
                CombatRouteAfter = null;
                var w = Get(Character, "wandoos98");
                if ((long)Get(w, "wandoosMagic") > 0)
                { changed = w; expected = (long)Get(w, "wandoosMagic") - 1; Set(w, "wandoosMagic", expected); }
                else
                {
                    changed = ((IEnumerable)Get(Get(Character, "NGU"), "magicSkills")).Cast<object>().First(x => (long)Get(x, "magic") > 0);
                    expected = (long)Get(changed, "magic") - 1; Set(changed, "magic", expected);
                }
                Set(Get(Character, "magic"), "idleMagic", (long)Get(Get(Character, "magic"), "idleMagic") + 1);
            };
            var run = StartScheduled(plan);
            Equal(true, changed != null, "foreign resource fault runs after an actual native route commit");
            Equal(-1, Get(Get(Character, "adventure"), "zone"), "resource uncertainty cannot prevent safe owned route retirement");
            Equal(2, Get(Get(Get(MainInstance, "_sharedResourceSetup"), "Application"), "BlockedLanes"), "route callback records only real affected Magic in shared model-scoped evidence");
            var phase = ScheduledPhase(run); var start = Get(run, "Started"); var deadline = Get(run, "PhaseDeadline");
            var writer = Plugin.GetType("NGUInjector.Managers.ResourceWriter", true);
            var allocations = (IDictionary)Invoke(writer, "CaptureAllocations", Character);
            var destination = allocations.Keys.Cast<object>().First(key => (int)Invoke(writer, "Lane", key) == 0
                && !key.ToString().StartsWith("Basic", StringComparison.Ordinal) && allocations[key] != null && ((long[])allocations[key]).Any(x => x > 0));
            var values = (long[])allocations[destination]; var index = Array.FindIndex(values, x => x > 0); var funded = values[index];
            Equal(true, funded > 0, "independent continuation has a real funded Energy destination");
            Invoke(writer, "SetAmount", Character, destination, index, funded - 1);
            Set(Character, "idleEnergy", (long)Get(Character, "idleEnergy") + 1);
            Set(MainInstance, "_sharedResourceSetup", null);
            ScheduledRefresh(run);
            Equal(funded, ((long[])((IDictionary)Invoke(writer, "CaptureAllocations", Character))[destination])[index], "healthy Energy actually reconciles after shared application replacement");
            Equal(expected, Get(changed, ReferenceEquals(changed, Get(Character, "wandoos98")) ? "wandoosMagic" : "magic"), "foreign Magic survives independent continuation");
            Equal(2, Get(Get(Get(MainInstance, "_sharedResourceSetup"), "Application"), "BlockedLanes"), "application reset cannot erase scoped native Magic uncertainty");
            Equal(-1, Get(run, "AppliedResourcePhase"), "partial lane continuation cannot admit whole-phase credit");
            Equal(start, Get(run, "Started"), "scoped uncertainty preserves original start");
            Equal(deadline, Get(run, "PhaseDeadline"), "scoped uncertainty preserves original phase deadline");
            Equal(true, ReferenceEquals(phase, ScheduledPhase(run)), "scoped retry retains the acquired phase object");
        }
        finally { CombatRouteAfter = null; AdventureWeightTrackRoutes = false; }
    }

    private static void CheckCombatBorrowedTimerOutcomes()
    {
        foreach (var fault in new[] { "before", "after", "return-before", "return-after", "return-foreign" })
        {
            Setup(); ControlledCombatPrepClock = 10000; CombatTimerWrites = 0;
            var timer = Get(Get(Character, "arbitrary"), "nukeTimer"); Set(timer, "totalseconds", 17d);
            try
            {
                CombatTimerFault = fault.StartsWith("return-", StringComparison.Ordinal) ? null : fault;
                if (fault == "before") Reject(() => StartServerPolicy(CallMain("CaptureCombatPrep", .03, .03, null), Phase("Wandoos", "current", 60)), "uncommitted native timer setting cannot acquire combat preparation");
                else StartServerPolicy(CallMain("CaptureCombatPrep", .03, .03, null), Phase("Wandoos", "current", 60));
                var run = Get(MainInstance, "_combatPrepRun");
                if (fault == "before")
                {
                    Equal(17d, Get(timer, "totalseconds"), "failed pre-model timer write preserves actual native counter");
                    Equal(false, Get(run, "NukeTimerOwned"), "unverified timer setting creates no return authority");
                    Equal(0L, Get(Get(Character, "wandoos98"), "wandoosEnergy"), "unverified timer cannot authorize resource funding");
                }
                else
                {
                    Equal(true, Get(run, "NukeTimerOwned"), "committed native timer setting has observed narrow return authority");
                    Equal(0d, Get(timer, "totalseconds"), "native automatic nuke is actually held at zero");
                    if (fault.StartsWith("return-", StringComparison.Ordinal)) CombatTimerFault = fault.Substring("return-".Length);
                    var args = Parse("{\"runId\":\"" + Get(run, "Id") + "\"}");
                    CallMain("CancelCombatPrep", args);
                    Equal(fault == "return-before" ? 0d : fault == "return-foreign" ? 23d : 17d, Get(timer, "totalseconds"), "borrowed timer outcome is observed without replay: " + fault);
                    var writes = CombatTimerWrites;
                    for (var retry = 0; retry < 3; retry++) CallMain("CancelCombatPrep", args);
                    Equal(writes, CombatTimerWrites, "late cancel never repeats a failed/committed native timer return: " + fault);
                    Equal(fault == "return-before", Get(run, "NukeTimerOwned"), "only rejected-unchanged native timer return retains its bounded lease");
                    if (fault == "return-before")
                    {
                        CombatTimerFault = null; ControlledCombatPrepClock += 1;
                        CallMain("CancelCombatPrep", args);
                        Equal(17d, Get(timer, "totalseconds"), "same owned return recovers after a before-model failure");
                        Equal(false, Get(run, "NukeTimerOwned"), "verified return consumes its lease exactly once");
                    }
                }
            }
            finally { CombatTimerFault = null; }
        }
    }

    private static void CheckCombatRetirementCallbacks()
    {
        foreach (var domain in new[] { "gear", "support", "magic", "inventory" })
        {
            var plan = AdventureWeightSetup(); Set(((IList)Get(Get(Character, "diggers"), "diggers"))[11], "maxLevel", 2L);
            var run = StartScheduled(plan); Equal(true, Get(run, "AdventureRouteOwned"), "retirement callback begins with a real native owned route");
            var inventory = Get(Character, "inventory"); var writer = Plugin.GetType("NGUInjector.Managers.ResourceWriter", true);
            var swaps = Swaps; var writes = ResourceCommittedWrites; object foreignHead = null; object magicTarget = null; long magicAmount = 0; var magicIndex = -1;
            var fired = false;
            try
            {
                CombatRouteAfter = zone => {
                    if (zone != -1) return;
                    CombatRouteAfter = null; fired = true;
                    if (domain == "gear")
                    {
                        foreignHead = Blank(Get(inventory, "head").GetType()); Set(foreignHead, "id", 63); SetEnum(foreignHead, "type", "Head");
                        Set(foreignHead, "bossRequired", 1); Set(foreignHead, "removable", true);
                        var bag = (IList)Get(inventory, "inventory"); var slot = Enumerable.Range(0, bag.Count).First(i => (int)Get(bag[i], "id") == 0);
                        bag[slot] = foreignHead; Set(inventory, "item1", -1); Set(inventory, "item2", slot); SwapGear();
                    }
                    if (domain == "support") Set(((IList)Get(Get(Character, "diggers"), "diggers"))[11], "curLevel", 1L);
                    if (domain == "magic")
                    {
                        var allocations = (IDictionary)Invoke(writer, "CaptureAllocations", Character);
                        magicTarget = allocations.Keys.Cast<object>().First(key => (int)Invoke(writer, "Lane", key) == 1
                            && allocations[key] != null && ((long[])allocations[key]).Any(x => x > 0));
                        var values = (long[])allocations[magicTarget]; magicIndex = Array.FindIndex(values, x => x > 0); magicAmount = values[magicIndex] - 1;
                        Invoke(writer, "SetAmount", Character, magicTarget, magicIndex, magicAmount);
                        Set(Get(Character, "magic"), "idleMagic", (long)Get(Get(Character, "magic"), "idleMagic") + 1);
                    }
                    if (domain == "inventory")
                        Set(Character, "inventory", typeof(object).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(inventory, null));
                };
                CallMain("EndCombatPrep", "cancelled", "retirement callback regression", true);
                Equal(true, fired, "fault runs after the native safe-zone write: " + domain);
                Equal(-1, Get(Get(Character, "adventure"), "zone"), "known native route retirement remains committed: " + domain);
                Equal(swaps + (domain == "gear" ? 1 : 0), Swaps, "retirement never equips over the callback's foreign gear");
                if (domain == "gear") Equal(true, ReferenceEquals(foreignHead, Get(inventory, "head")), "actual foreign native head remains equipped after retirement");
                if (domain == "magic")
                {
                    Equal("ForeignConflict", Get(Get(run, "ResourceOutcome"), "Disposition").ToString(), "retirement callback uses shared typed resource uncertainty");
                    Equal(2, Get(Get(Get(MainInstance, "_sharedResourceSetup"), "Application"), "BlockedLanes"), "only the real changed Magic lane is scoped unresolved");
                }
                ControlledCombatPrepClock += 1; CallMain("AdvanceCombatPrep");
                Equal(swaps + (domain == "gear" ? 1 : 0), Swaps, "old release retry never restores a historical setup: " + domain);
                if (domain == "support") Equal(1L, Get(((IList)Get(Get(Character, "diggers"), "diggers"))[11], "curLevel"), "foreign inactive support survives safe release");
                if (domain == "magic") Equal(magicAmount, ((long[])((IDictionary)Invoke(writer, "CaptureAllocations", Character))[magicTarget])[magicIndex], "foreign Magic is not cleared after retirement retry");
                if (domain == "inventory")
                {
                    Equal(false, CallMain("CombatPrepEpochMatches", Get(run, "Capture")), "same-valued replacement inventory is a different native application epoch");
                    Equal(writes, ResourceCommittedWrites, "old capture cannot release allocations after native inventory replacement");
                }
            }
            finally { CombatRouteAfter = null; AdventureWeightTrackRoutes = false; }
        }
    }

    private static void CheckCombatForeignRouteRetry()
    {
        foreach (var change in new[] { "zone", "selector", "autoattack" })
        {
            var plan = AdventureWeightSetup(); var run = StartScheduled(plan);
            var adventure = Get(Character, "adventure"); var controller = Get(Character, "adventureController");
            if (change == "zone") Set(adventure, "zone", 0);
            if (change == "selector") Set(controller, "zoneSelector", typeof(object).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(Get(controller, "zoneSelector"), null));
            if (change == "autoattack") Set(adventure, "autoattacking", false);
            var zone = Get(adventure, "zone"); var auto = Get(adventure, "autoattacking"); var writes = ResourceCommittedWrites; var swaps = Swaps;
            for (var retry = 0; retry < 3; retry++) { ControlledCombatPrepClock += 1; CallMain("RouteCombatPrepAdventure"); }
            Equal(zone, Get(adventure, "zone"), "current combat intent does not steal a foreign " + change + " route on retry");
            Equal(auto, Get(adventure, "autoattacking"), "current combat intent preserves actual foreign idle preference");
            Equal(false, Get(run, "AdventureRouteOwned"), "matching zone values cannot adopt a foreign native controller/settings lease");
            CallMain("EndCombatPrep", "cancelled", "foreign route regression", true);
            Equal(zone, Get(adventure, "zone"), "handoff never retires a foreign route");
            Equal(writes, ResourceCommittedWrites, "unknown active route retains funded prerequisites");
            Equal(swaps, Swaps, "unknown active route retains physical gear prerequisites");
        }
    }

    private static void CheckCombatIdleSettingVerification()
    {
        var plan = AdventureWeightSetup(); Set(Get(Character, "adventure"), "autoattacking", false); CombatIdleAttackWrites = 0;
        var controller = Get(Character, "adventureController"); var idle = Child(controller, "idleAttackMove");
        Set(idle, "character", Character); Set(idle, "pc", Get(controller, "playerController"));
        Set(Get(controller, "playerController"), "character", Character);
        foreach (var model in new[] { Get(controller, "playerController"), Get(Character, "adventure") })
            foreach (var field in model.GetType().GetFields(All).Where(x => !x.IsStatic && x.FieldType.Name == "PlayerTime"))
                if (field.GetValue(model) == null) Child(model, field.Name);
        try
        {
            CombatIdleAttackAfter = () => { CombatIdleAttackAfter = null; Set(Get(Character, "adventure"), "autoattacking", false); };
            var run = StartScheduled(plan);
            Equal(1, CombatIdleAttackWrites, "native idle-setting fault fires after its real setter; status=" + Get(run, "AdventureStatus")
                + "; warnings=" + string.Join(";", ((IEnumerable)Get(run, "Warnings")).Cast<object>()));
            Equal(1d, Convert.ToDouble(Get(Get(controller, "playerController"), "moveTimer")), "native idle toggle retains its real used-move/cooldown effect");
            Equal(-1, Get(Get(Character, "adventure"), "zone"), "unverified native idle setting cannot activate farming");
            Equal(false, Get(Get(Character, "adventure"), "autoattacking"), "foreign idle preference is retained");
            Equal("ownership-lost", Get(run, "AdventureStatus"), "route setting uncertainty is explicit without blocking primary resource proof");
            Equal(true, Get(Get(run, "ResourceOutcome"), "Verified"), "independent primary Energy/Magic remain genuinely verified");
            for (var retry = 0; retry < 3; retry++) { ControlledCombatPrepClock += 1; ScheduledRefresh(run); }
            Equal(false, Get(Get(Character, "adventure"), "autoattacking"), "ordinary retry cannot acknowledge foreign route-setting uncertainty");
            Equal(-1, Get(Get(Character, "adventure"), "zone"), "ordinary retry never enters farming over a failed setting");
        }
        finally { CombatIdleAttackAfter = null; }
    }

    private static void CheckCombatBaselineRouteEntry()
    {
        var args = Invoke(typeof(PresetIntegrationChecks), "SetupAdventureDynamic", "combat-entry-baseline", "[1,1,0,0,0,0,0,0,0,0,0,0]", true);
        Set(Settings, "_savePath", null); PrepareLiveResourceFixture();
        Set(Get(Character, "adventure"), "zone", -1);
        var receipt = EquipInterruptionPreset(args);
        Equal(1000, Get(Get(Character, "adventure"), "zone"), "entry timeline starts from a genuinely applied baseline farming route");
        var inventory = Get(Character, "inventory"); var head = Blank(Get(inventory, "head").GetType());
        Set(head, "id", 60); SetEnum(head, "type", "Head"); Set(head, "bossRequired", 1); Set(head, "removable", true);
        SetEnum(head, "spec1Type", "AdvTraining"); Set(head, "spec1Cur", 1e30f); ((IList)Get(inventory, "inventory"))[0] = head;
        var safeSwaps = 0;
        NativeGearSwapped = item => { Equal(-1, Get(Get(Character, "adventure"), "zone"), "baseline route actually retires before the first combat gear write"); safeSwaps++; };
        try
        {
            var run = StartServerPolicy(CallMain("CaptureCombatPrep", .03, .03, null), Phase("AT", "at-current", 60));
            Equal(true, safeSwaps > 0 && ReferenceEquals(head, Get(inventory, "head")), "combat really applies its acquired gear after current baseline retirement");
            Equal(true, Get(run, "SetupVerified"), "retired current baseline transitions through complete shared setup proof");
            Equal(true, Get(Get(run, "ResourceOutcome"), "Verified"), "current baseline transition obtains real native resource proof");
            Equal(null, Get(run, "EntryRouteAuthorized"), "observed retirement consumes the borrowed baseline route witness");
            CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(run, "Id") + "\"}"));
            Equal(true, ReferenceEquals(head, Get(inventory, "head")), "cancel itself has no historical gear return even when baseline A remains selected");
        }
        finally { NativeGearSwapped = null; }
    }

    private static void CheckCombatSuspendedBaselineGrowth()
    {
        var args = Invoke(typeof(PresetIntegrationChecks), "SetupAdventureDynamic", "combat-suspended-baseline", "[1,1,0,0,0,0,0,0,0,0,0,0]", true);
        Set(Settings, "_savePath", null); PrepareLiveResourceFixture();
        var baselineHead = PrepareAutoBoostInventory(); Set(baselineHead, "removable", false);
        SetEnum(baselineHead, "spec1Type", "EnergyPower"); Set(baselineHead, "spec1Cur", 1f); Set(baselineHead, "spec1Cap", 10000000f); RefreshBonuses();
        Set(Get(Character, "adventure"), "zone", -1); Setting("UseCustomRebirthPlan", true);
        File.WriteAllText(Path.Combine(Temp, "plans", "fixture.json"), "{\"Version\":17,\"Name\":\"fixture\",\"StageSchedulerEnabled\":true,\"StageDurationSeconds\":1200,\"AutoRebirthEnabled\":false,\"AutoActivateFruits\":false,\"Stages\":[{\"Name\":\"A\",\"PresetId\":\""
            + JsonValue(args, "presetId") + "\",\"DurationSeconds\":1}," + ScheduledRow + "]}");
        var plan = Invoke(Get(MainType, "CustomPlanStore"), "Load", "fixture"); CallMain("RunCustomPlanAutomation", plan);
        var receipt = Get(MainInstance, "_globalPresetReceipt"); var policy = Get(receipt, "Policy");
        Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), "scheduled A has real setup/resource proof before its timing boundary");
        Equal(1000, Get(Get(Character, "adventure"), "zone"), "scheduled A owns actual farming before automatic combat");
        Set(Get(policy, "Preset"), "AutoUpdate", false);
        var library = Get(Get(MainType, "CustomPlanStore"), "PresetLibrary"); var root = Invoke(library, "ReadExternalLibrary");
        var definition = ((IEnumerable)Get(Json(root, "Presets"), "Children")).Cast<object>().Single(row => JsonValue(row, "Id") == JsonValue(args, "presetId"));
        Set(Json(definition, "AutoUpdate"), "AsBool", false); Invoke(library, "SaveExternalLibrary", root, JsonValue(root, "__externalUiRevision"));
        var inventory = Get(Character, "inventory"); var phaseHead = Blank(baselineHead.GetType());
        Set(phaseHead, "id", 60); SetEnum(phaseHead, "type", "Head"); Set(phaseHead, "bossRequired", 1); Set(phaseHead, "removable", true);
        SetEnum(phaseHead, "spec1Type", "AdvTraining"); Set(phaseHead, "spec1Cur", 10000f); Set(phaseHead, "spec1Cap", 10000f);
        ((IList)Get(inventory, "inventory"))[0] = phaseHead;
        Set(Get(Get(Character, "buttons"), "augmentation"), "m_Interactable", false);
        foreach (var name in new[] { "wandoosEnergy", "wandoosMagic" }) Set(Get(Get(Character, "advancedTrainingController"), name), "levelFactor", 1000f);
        var scheduler = Get(MainInstance, "_protectedScheduler");
        Set(scheduler, "_lastHeartbeat", (double)Get(MainType, "ProtectedHeartbeat") - 1); ScheduledTime(501);
        CallMain("GetProtectedStage", plan); CallMain("RunCustomPlanAutomation", plan);
        var run = Get(MainInstance, "_combatPrepRun");
        Equal("running", Get(run, "Status"), "automatic combat really acquires after the applied A boundary");
        Equal("AT", Get(ScheduledPhase(run), "Kind"), "suspended baseline timeline retains actual unfinished paid AT");
        Equal(true, Get(run, "SetupVerified"), "automatic phase obtains actual shared setup proof after baseline retirement: " + Get(run, "Reason"));
        Equal(-1, Get(Get(Character, "adventure"), "zone"), "automatic acquisition retires the actual old baseline route even when Current gear wins");
        var phaseGear = CallMain("CombatPrepEquipped");
        Equal(true, CallMain("CombatPrepGearMatches", Get(Get(run, "Setup"), "Gear")), "native phase gear matches the acquired planner choice, not a forced alternative");
        var material = ((string[])Get(policy, "Material")).ToArray(); var phase = ScheduledPhase(run); var deadline = Get(run, "PhaseDeadline");
        var slot = PutAutoBoost(27, 123f); CallMain("RunCustomAutoBoost");
        Equal(124f, Get(baselineHead, "spec1Cur"), "real native boosting revises the same suspended locked A instance");
        Equal(0, Get(((IList)Get(inventory, "inventory"))[slot], "id"), "suspended baseline growth actually consumes the native boost");
        Equal(false, material.SequenceEqual((string[])Get(policy, "Material")), "causal shared observation updates retained A material without a replacement choice");
        Equal(true, ReferenceEquals(phase, ScheduledPhase(run)), "baseline material revision cannot discard unfinished current AT");
        Equal(deadline, Get(run, "PhaseDeadline"), "baseline revision cannot renew acquired AT timing");
        CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(run, "Id") + "\"}"));
        Equal(true, CallMain("CombatPrepGearMatches", phaseGear), "automatic cancel itself preserves current phase gear without historical restoration");
        var entry = Get(MainInstance, "_protectedGeneration") + ":" + Get(scheduler, "Entry");
        CallMain("OverrideCustomStage", Parse("{\"name\":\"fixture\",\"expectedRevision\":\"" + Get(plan, "RuntimeRevision")
            + "\",\"expectedEntry\":\"" + entry + "\",\"action\":\"set\",\"stageIndex\":0}"));
        var searches = AdventureWeightSearches; var legacySearches = AdventureWeightLegacySearches;
        CallMain("RunCustomPlanAutomation", plan);
        Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), "current A intent genuinely reuses its approved acquired policy after combat");
        Equal(true, ReferenceEquals(baselineHead, Get(inventory, "head")), "current A receives the original selected, natively grown instance");
        Equal(searches, AdventureWeightSearches, "AutoUpdate=false return performs no replacement preset search");
        Equal(legacySearches, AdventureWeightLegacySearches, "AutoUpdate=false return performs no legacy optimizer search");
    }

    private static void CheckCombatFocusAdmission()
    {
        foreach (var change in new[] { "incomplete", "gear", "support", "epoch", "resources", "clock" })
        {
            Setup(); Set(Get(Get(Character, "buttons"), "ngu"), "m_Interactable", true);
            using (var lease = new FileStream(Path.Combine(Temp, "focus-command-fixture.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                Set(MainInstance, "_started", true); Set(MainInstance, "_injectorLease", lease);
                if (change == "support") Set(((IList)Get(Get(Character, "diggers"), "diggers"))[11], "maxLevel", 2L);
                if (change == "incomplete")
                {
                    Set(((IList)Get(Get(Character, "diggers"), "diggers"))[5], "maxLevel", 2L);
                    Set(typeof(PresetIntegrationChecks), "PodInterference", (Action<object>)(pod => {
                        Set(typeof(PresetIntegrationChecks), "PodInterference", null);
                        throw new InvalidOperationException("fixture incomplete native focus support");
                    }));
                }
                try { CombatFocusCommand("focusGoal", "{\"goal\":\"MagicNgu\",\"mode\":\"apply\"}"); }
                finally { Set(typeof(PresetIntegrationChecks), "PodInterference", null); }
                Equal(change == "incomplete" ? "failed" : "applied", Get(MainInstance, "_customFocusStatus"), "admission starts from real native focus outcome: " + change);
                var preview = Json(CombatFocusCommand("previewCombatPrep", "{\"durationSeconds\":60}"), "combatPrepPreview");
                var cache = Get(MainInstance, "_combatPrepPreview"); var candidate = FindCandidate(preview, "ATThenWandoos");
                if (change == "gear") Set(Get(Get(Character, "inventory"), "head"), "spec1Cur", 123f);
                if (change == "support")
                {
                    var digger = ((IList)Get(Get(Character, "diggers"), "diggers"))[11];
                    Set(digger, "curLevel", (long)Get(digger, "curLevel") == 1 ? 2L : 1L);
                }
                if (change == "epoch") Set(Get(Character, "stats"), "rebirthNumber", 8L);
                if (change == "resources")
                {
                    var skills = (IList)Get(Get(Character, "NGU"), "magicSkills");
                    Set(skills[0], "magic", (long)Get(skills[0], "magic") - 1);
                    Set(Get(Character, "magic"), "idleMagic", (long)Get(Get(Character, "magic"), "idleMagic") + 1);
                }
                if (change == "clock")
                {
                    Set(Get(Character, "rebirthTime"), "totalseconds", 499d);
                    Set(Get(Character, "totalPlaytime"), "totalseconds", 1499d);
                }
                var gear = CallMain("CombatPrepEquipped"); var support = CallMain("CombatPrepSupportKey");
                var allocations = CallMain("GlobalPresetAllocationState"); var writes = ResourceCommittedWrites; var swaps = Swaps;
                var selected = Get(MainInstance, "_selectedSetup");
                Reject(() => CombatFocusCommand("startCombatPrep", "{\"previewId\":\"" + JsonValue(preview, "previewId") + "\",\"candidateId\":\"" + candidate + "\"}"), "unsafe focus rejects public Start before acquisition: " + change);
                Equal(true, ReferenceEquals(cache, Get(MainInstance, "_combatPrepPreview")), "unsafe focus never consumes preview token: " + change);
                Equal(null, Get(MainInstance, "_combatPrepRun"), "unsafe focus never acquires a run");
                Equal(true, ReferenceEquals(selected, Get(MainInstance, "_selectedSetup")), "rejected admission preserves setup publication");
                Equal(true, CallMain("CombatPrepGearMatches", gear), "rejected admission preserves actual gear");
                Equal(support, CallMain("CombatPrepSupportKey"), "rejected admission preserves support");
                Equal(allocations, CallMain("GlobalPresetAllocationState"), "rejected admission preserves funding");
                Equal(writes, ResourceCommittedWrites, "rejected admission performs zero accounting writes");
                Equal(swaps, Swaps, "rejected admission performs zero gear swaps");
                var rejectedPreview = Json(CombatFocusCommand("previewCombatPrep", "{\"durationSeconds\":60}"), "combatPrepPreview");
                foreach (var row in ((IEnumerable)Get(Json(rejectedPreview, "candidates"), "Children")).Cast<object>()
                    .Where(row => (int)Get(Json(row, "phases"), "Count") > 0))
                {
                    Equal(false, Get(Json(row, "available"), "AsBool"), "unsafe focus remains unavailable in public preview: " + change);
                    Equal(true, JsonValue(row, "reason").Contains("Current focus setup"), "preview exposes concrete retained-focus readiness blocker");
                }
            }
        }
    }

    private static object CombatFocusCommand(string command, string json)
    {
        var args = new object[] { command, Parse(json), null };
        Invoke(MainInstance, "ExecuteExternalUiCommand", args);
        return args[2];
    }

    private static void CheckRetainedFocusCombatTransition()
    {
        var before = Assertions;
        foreach (var goalName in new[] { "MagicNgu", "EnergyNgu" })
        foreach (var outcome in new[] { "resume", "complete", "stop", "clear", "foreign", "epoch", "partial" })
        {
            Setup(); ControlledCombatPrepClock = 10000;
            using (var lease = new FileStream(Path.Combine(Temp, "focus-command-fixture.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
            Set(MainInstance, "_started", true); Set(MainInstance, "_injectorLease", lease);
            Set(Get(Get(Character, "buttons"), "ngu"), "m_Interactable", true);
            var goal = Enum.Parse(Plugin.GetType("NGUInjector.CustomFocusGoal", true), goalName);
            CombatFocusCommand("focusGoal", "{\"goal\":\"" + goalName + "\",\"mode\":\"apply\"}");
            Equal("applied", Get(MainInstance, "_customFocusStatus"), "retained focus starts genuinely applied: " + outcome);
            var selection = Get(MainInstance, "_focusSetupSelection");
            var focusGear = CallMain("CombatPrepEquipped"); var focusSupport = CallMain("CombatPrepSupportKey");
            var inventory = Get(Character, "inventory"); var phaseHead = Blank(Get(inventory, "head").GetType());
            Set(phaseHead, "id", 60); SetEnum(phaseHead, "type", "Head"); Set(phaseHead, "bossRequired", 1); Set(phaseHead, "removable", true);
            SetEnum(phaseHead, "spec1Type", "AdvTraining"); Set(phaseHead, "spec1Cur", 1e30f); ((IList)Get(inventory, "inventory"))[0] = phaseHead;
            var preview = Json(CombatFocusCommand("previewCombatPrep", "{\"durationSeconds\":60}"), "combatPrepPreview");
            var candidate = FindCandidate(preview, "ATThenWandoos");
            Equal(true, Get(Json(((IEnumerable)Get(Json(preview, "candidates"), "Children")).Cast<object>().Single(row => JsonValue(row, "id") == candidate), "available"), "AsBool"), "valid retained focus admits a public preview candidate");
            CombatFocusCommand("startCombatPrep", "{\"previewId\":\"" + JsonValue(preview, "previewId") + "\",\"candidateId\":\"" + candidate + "\"}");
            var run = Get(MainInstance, "_combatPrepRun");
            Equal(true, Get(run, "SetupVerified"), "combat overlay obtains actual native setup proof: " + outcome);
            Equal(true, ReferenceEquals(phaseHead, Get(inventory, "head")), "combat actually replaces focus gear: " + outcome);
            Equal(true, ReferenceEquals(selection, Get(MainInstance, "_focusSetupSelection")), "combat cannot replace producer-owned selection");
            Equal(false, ReferenceEquals(Get(selection, "Desired"), Get(MainInstance, "_selectedSetup")), "retained selection is independent of combat publication");
            var started = Get(run, "Started"); var deadline = Get(run, "PhaseDeadline");
            var phaseBefore = Get(run, "PhaseIndex"); var policy = Get(run, "Policy"); var selected = Get(MainInstance, "_selectedSetup");
            var allocations = CallMain("GlobalPresetAllocationState"); var supportBefore = CallMain("CombatPrepSupportKey");
            var writesBefore = ResourceCommittedWrites; var swapsBefore = Swaps; var searchesBefore = AdventureWeightLegacySearches;
            var w = Get(Character, "wandoos98"); Set(w, "energyLevel", 7L); Set(w, "energyProgress", .25f);
            Reject(() => CallMain("ApplyCustomFocusGoal", Enum.Parse(goal.GetType(), "EnergyNgu")), "direct focus replacement remains combat-idle only");
            Reject(() => CombatFocusCommand("focusGoal", "{\"goal\":\"EnergyNgu\",\"mode\":\"apply\"}"), "public focus replacement is rejected while combat owns setup");
            Reject(() => CombatFocusCommand("stopFocus", "{}"), "public stopFocus preserves existing combat-idle policy");
            Equal(true, ReferenceEquals(selection, Get(MainInstance, "_focusSetupSelection")), "rejected commands preserve acquired focus choices");
            Equal(true, ReferenceEquals(run, Get(MainInstance, "_combatPrepRun")) && ReferenceEquals(policy, Get(run, "Policy")), "rejected commands preserve current run and acquired policy");
            Equal(true, ReferenceEquals(selected, Get(MainInstance, "_selectedSetup")), "rejected commands preserve combat publication");
            Equal(phaseBefore, Get(run, "PhaseIndex"), "rejected commands preserve current phase");
            Equal(deadline, Get(run, "PhaseDeadline"), "rejected commands preserve deadline");
            Equal(allocations, CallMain("GlobalPresetAllocationState"), "rejected commands preserve allocations");
            Equal(supportBefore, CallMain("CombatPrepSupportKey"), "rejected commands preserve support");
            Equal(writesBefore, ResourceCommittedWrites, "rejected commands perform zero resource writes");
            Equal(swapsBefore, Swaps, "rejected commands perform zero gear writes");
            Equal(searchesBefore, AdventureWeightLegacySearches, "rejected commands perform zero searches");
            if (outcome == "complete")
            {
                // Advance actual targets/clock through every acquired phase without
                // rewriting policy, entry or deadline to manufacture completion.
                for (var step = 0; step < 12 && (bool)Get(MainInstance, "CombatPrepRunActive"); step++)
                {
                    var current = ScheduledPhase(run);
                    if ((string)Get(current, "Kind") == "AT")
                    {
                        var levels = (long[])Get(Get(Character, "advancedTraining"), "level");
                        levels[3] = (long)Get(current, "AtEnergyTarget"); levels[4] = (long)Get(current, "AtMagicTarget");
                    }
                    ControlledCombatPrepClock = Math.Max((double)Get(run, "PhaseDeadline"), ControlledCombatPrepClock.Value + 1);
                    CallMain("TickCombatPrep");
                }
                Equal("completed", Get(run, "Status"), "publicly acquired run completes under its original clock budget");
            }
            else
            {
                if (outcome == "clear") CallMain("ClearCustomSustainedFocus"); // Existing authorized lifecycle cancellation, not a new public command.
                CombatFocusCommand("cancelCombatPrep", "{\"runId\":\"" + Get(run, "Id") + "\"}");
            }
            if (outcome == "stop") CombatFocusCommand("stopFocus", "{}");
            if (outcome != "complete") Equal(true, ReferenceEquals(phaseHead, Get(inventory, "head")), "cancellation has no historical physical return");
            if (outcome == "foreign")
            {
                Set(phaseHead, "spec1Cur", 123f);
                var support = CallMain("CombatPrepSupportKey");
                Reject(() => CallMain("RefreshSelectedFocusSupport", goal), "periodic support selection cannot adopt changed native setup");
                Equal(support, CallMain("CombatPrepSupportKey"), "foreign setup blocks support refresh before mutation");
            }
            if (outcome == "epoch") Set(Get(Character, "stats"), "rebirthNumber", 8L);
            var searches = AdventureWeightLegacySearches; var swaps = Swaps;
            if (outcome == "partial") FailRefreshAtSwap = Swaps + 1;
            CallMain("ReconcileCustomResources", null, true);
            if (outcome == "partial")
            {
                Set(MainInstance, "_customFocusNextRetry", DateTime.MinValue);
                CallMain("ReconcileCustomResources", null, true);
            }
            if (outcome == "resume" || outcome == "complete" || outcome == "partial")
            {
                Equal(true, CallMain("CombatPrepGearMatches", focusGear), "current acquired focus gear resumes directly without a search");
                Equal(focusSupport, CallMain("CombatPrepSupportKey"), "current acquired focus support is verified");
                Equal("applied", Get(MainInstance, "_customFocusStatus"), "focus successor has actual setup and funding proof");
                Equal(true, Get(Get(MainInstance, "_customFocusResourceOutcome"), "Verified"), "focus resources are genuinely verified");
            }
            else
            {
                Equal(swaps, Swaps, "stopped/foreign/epoch selection never restores old focus gear: " + outcome);
                Equal(true, ReferenceEquals(phaseHead, Get(inventory, "head")), "newer native setup remains intact: " + outcome);
                if (outcome == "stop" || outcome == "clear")
                {
                    Equal(null, Get(MainInstance, "_focusSetupSelection"), "authorized cancellation leaves no retained focus");
                    Equal(null, Get(MainInstance, "_customRequestedFocusGoal"), "cancelled focus cannot become the successor");
                }
            }
            Equal(searches, AdventureWeightLegacySearches, "handoff never invokes a replacement optimizer search");
            Equal(searchesBefore, AdventureWeightLegacySearches, "public cancellation/completion and current-focus reconciliation never search again");
            Equal(started, Get(run, "Started"), "focus handoff preserves combat entry");
            if (outcome != "complete") Equal(deadline, Get(run, "PhaseDeadline"), "focus handoff preserves combat deadline");
            Equal(7L, Get(w, "energyLevel"), "rejected commands and handoff preserve native W history");
            Equal(.25f, Get(w, "energyProgress"), "rejected commands and handoff preserve partial W progress");
            }
        }
        Console.WriteLine("Retained focus PUBLIC combat transition: " + (Assertions - before) + " native assertions.");
    }

    private static void CheckCombatCurrentResourceHandoff()
    {
        Setup(); PrepareLiveResourceFixture();
        Set(Get(Get(Character, "buttons"), "brokenTimeMachine"), "m_Interactable", true);
        Set(Settings, "_energyNguWeights", new int[9]); Set(Settings, "_magicNguWeights", new int[7]);
        Set(Character, "curEnergy", 10L); Set(Character, "idleEnergy", 10L);
        Set(Get(Character, "magic"), "curMagic", 20L); Set(Get(Character, "magic"), "idleMagic", 20L);
        Set(Character, "realGold", 1e300d);
        PrepareInterruptionPreset();
        var library = Get(Get(MainType, "CustomPlanStore"), "PresetLibrary"); var root = Invoke(library, "ReadExternalLibrary");
        var id = "combat-resource-handoff";
        Invoke(Json(root, "Presets"), "Add", "", Parse("{\"Id\":\"" + id + "\",\"Name\":\"Current TM baseline\",\"Weights\":[0,0,0,0,0,0,1,0,0,0,0,0,0,0],\"SupportMode\":\"current\",\"AutoUpdate\":true,\"SwapSeconds\":0}"));
        var revision = Invoke(library, "SaveExternalLibrary", root, JsonValue(root, "__externalUiRevision"));
        EquipInterruptionPreset(Parse("{\"presetId\":\"" + id + "\",\"libraryRevision\":\"" + revision + "\"}"));
        var baseline = CallMain("GlobalPresetAllocationState");
        Equal(10L, Get(Get(Character, "machine"), "speedEnergy"), "handoff control really funds the current baseline's entire Energy pool");
        Equal(20L, Get(Get(Character, "machine"), "goldMultiMagic"), "handoff control really funds the current baseline's entire Magic pool");
        var run = StartServerPolicy(CallMain("CaptureCombatPrep", .03, .03, null), Phase("NGU", "current", 60));
        Equal(baseline, CallMain("GlobalPresetAllocationState"), "combat phase and current successor have identical actual native resource requirements");
        var writes = ResourceCommittedWrites; var swaps = Swaps;
        CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(run, "Id") + "\"}"));
        CallMain("ReconcileCustomResources", null, true);
        Equal(baseline, CallMain("GlobalPresetAllocationState"), "current baseline remains the actual final funded state");
        Equal(swaps, Swaps, "unchanged handoff needs no physical gear changes");
        Equal(writes, ResourceCommittedWrites, "unchanged effective resource intent must not execute combat empty-then-baseline-refill accounting");
    }

    private static void CheckCombatPausedBorrowedFields()
    {
        var plan = ScheduledSetup();
        Set(Get(Get(Character, "buttons"), "augmentation"), "m_Interactable", false);
        foreach (var name in new[] { "wandoosEnergy", "wandoosMagic" }) Set(Get(Get(Character, "advancedTrainingController"), name), "levelFactor", 1000f);
        var targets = (long[])Get(Get(Character, "advancedTraining"), "levelTarget"); targets[3] = 5000; targets[4] = 6000;
        var timer = Get(Get(Character, "arbitrary"), "nukeTimer"); Set(timer, "totalseconds", 17d);
        var run = StartScheduled(plan); Equal("AT", Get(ScheduledPhase(run), "Kind"), "pause acceptance begins with an acquired unfinished native AT gate");
        Equal(0L, targets[3], "pause acceptance actually borrows the first native target");
        Equal(0d, Get(timer, "totalseconds"), "pause acceptance actually holds the native nuke timer");
        var entry = Get(run, "OwnerEntry"); var deadline = Get(run, "DeadlineRebirthSeconds");
        Setting("UseCustomRebirthPlan", false); CallMain("TickCombatPrep");
        Equal(false, Get(MainInstance, "CombatPrepRunActive"), "pause removes current process authority");
        Equal(entry, Get(run, "OwnerEntry"), "pause does not create a new entry");
        Equal(deadline, Get(run, "DeadlineRebirthSeconds"), "pause never renews the original deadline");
        Equal("5000/6000/17", targets[3] + "/" + targets[4] + "/" + Convert.ToDouble(Get(timer, "totalseconds")).ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            "pause returns individually still-owned AT-3/AT-4/nuke counter fields without restoring preferences or progression");
    }

    private static void CheckCombatPausedForeignBorrowedFields()
    {
        foreach (var change in new[] { "field", "epoch", "model" })
        {
            var plan = ScheduledSetup();
            Set(Get(Get(Character, "buttons"), "augmentation"), "m_Interactable", false);
            foreach (var name in new[] { "wandoosEnergy", "wandoosMagic" }) Set(Get(Get(Character, "advancedTrainingController"), name), "levelFactor", 1000f);
            var training = Get(Character, "advancedTraining"); var targets = (long[])Get(training, "levelTarget"); targets[3] = 5000; targets[4] = 6000;
            var arbitrary = Get(Character, "arbitrary"); var timer = Get(arbitrary, "nukeTimer"); Set(timer, "totalseconds", 17d);
            var run = StartScheduled(plan);
            if (change == "field") { targets[3] = 777; Set(timer, "totalseconds", 23d); }
            if (change == "epoch") Set(Get(Character, "stats"), "rebirthNumber", 8L);
            if (change == "model")
            {
                targets = (long[])targets.Clone(); Set(training, "levelTarget", targets);
                timer = Child(arbitrary, "nukeTimer"); Set(timer, "totalseconds", 0d);
            }
            var writes = ResourceCommittedWrites; var swaps = Swaps;
            Setting("UseCustomRebirthPlan", false); CallMain("TickCombatPrep");
            Equal(change == "field" ? 777L : 0L, targets[3], "pause cannot return over foreign target/epoch/model: " + change);
            Equal(change == "field" ? 6000L : 0L, targets[4], "only independently still-owned fields return: " + change);
            Equal(change == "field" ? 23d : 0d, Get(timer, "totalseconds"), "pause cannot overwrite foreign native counter/epoch/model: " + change);
            Equal(writes, ResourceCommittedWrites, "paused foreign return never recalls unrelated resource allocations: " + change);
            Equal(swaps, Swaps, "paused return never restores historical gear: " + change);
        }
    }

    private static void CheckCombatUnloadRouteRetirement()
    {
        var plan = AdventureWeightSetup(); var run = StartScheduled(plan);
        var adventure = Get(Character, "adventure"); var inventory = Get(Character, "inventory"); var head = Get(inventory, "head");
        Equal(true, Get(run, "AdventureRouteOwned"), "unload retirement test owns an actual verified native route");
        var zone = Get(adventure, "zone"); var swaps = Swaps; var writes = ResourceCommittedWrites;
        AdventureWeightFailRetirement = true;
        try
        {
            Reject(() => CallMain("Unload"), "actual unload waits when native safe-zone retirement fails");
            Equal(false, Get(MainInstance, "_unloaded"), "failed retirement cannot discard the only current release authority");
            Equal(zone, Get(adventure, "zone"), "failed safe-zone write leaves native route observed active");
            Equal(swaps, Swaps, "unload cannot withdraw gear before verified route retirement");
            Equal(writes, ResourceCommittedWrites, "unload cannot withdraw resources before verified route retirement");
            AdventureWeightFailRetirement = false; ControlledCombatPrepClock += 1;
            CallMain("Unload");
            Equal(-1, Get(adventure, "zone"), "native route is actually retired before successful reference disposal");
            Equal(null, Get(MainType, "reference"), "safe release permits actual lifecycle completion");
            Equal(true, ReferenceEquals(head, Get(inventory, "head")), "unload preserves phase gear without a historical return baseline");
        }
        finally { AdventureWeightFailRetirement = false; AdventureWeightTrackRoutes = false; }
    }

    private static void CheckCombatSteadyObservation()
    {
        var plan = ScheduledSetup(); ControlledCombatPrepClock = 10000;
        var run = StartScheduled(plan);
        var captures = CombatFullCaptures; var writes = ResourceCommittedWrites; var swaps = Swaps;
        var supportWrites = Get(typeof(PresetIntegrationChecks), "PodRefreshCalls");
        var phase = ScheduledPhase(run); var started = Get(run, "Started"); var deadline = Get(run, "PhaseDeadline");
        for (var refresh = 0; refresh < 20; refresh++) ScheduledRefresh(run);
        Equal(captures, CombatFullCaptures, "steady combat performs no full inventory/material/forecast capture or phase reacquisition");
        Equal(writes, ResourceCommittedWrites, "unchanged combat requirements perform zero duplicate native resource writes");
        Equal(swaps, Swaps, "steady combat does not reapply gear");
        Equal(supportWrites, Get(typeof(PresetIntegrationChecks), "PodRefreshCalls"), "steady combat does not resubmit support inputs");
        Equal(true, ReferenceEquals(phase, ScheduledPhase(run)), "cheap refresh retains the acquired phase");
        Equal(started, Get(run, "Started"), "cheap refresh retains entry time");
        Equal(deadline, Get(run, "PhaseDeadline"), "cheap refresh retains original deadline");
        CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(run, "Id") + "\"}"));
    }

    private static void CheckCombatBorrowedPrerequisiteCallback()
    {
        Setup(); ControlledCombatPrepClock = 10000;
        var run = StartServerPolicy(CallMain("CaptureCombatPrep", .03, .03, null), Phase("Wandoos", "current", 60));
        var timer = Get(Get(Character, "arbitrary"), "nukeTimer"); var w = Get(Character, "wandoos98");
        var funded = (long)Get(w, "wandoosEnergy"); Equal(true, funded > 0, "setting callback test has real native W funding");
        Set(w, "wandoosEnergy", funded - 1); Set(Character, "idleEnergy", (long)Get(Character, "idleEnergy") + 1);
        AllocationRefreshObserver = key => { AllocationRefreshObserver = null; Set(timer, "totalseconds", 60d); };
        ScheduledRefresh(run);
        Equal(-1, Get(run, "AppliedResourcePhase"), "foreign borrowed timer callback invalidates dependent phase admission");
        Equal(false, CallMain("CombatPrepRequirementsVerified", run, ScheduledPhase(run)), "cleanup-owner flag alone is not current native-setting authority");
        var writes = ResourceCommittedWrites;
        CallMain("TickCombatPrep");
        Equal(60d, Get(timer, "totalseconds"), "foreign native timer is never overwritten during failed prerequisite release");
        Equal(writes, ResourceCommittedWrites, "unsafe native nuke retirement preserves funded prerequisites without resource replay");
        Equal(false, Get(run, "NukeTimerOwned"), "unsafe foreign native-setting ownership is retired");
        Equal(funded, Get(w, "wandoosEnergy"), "committed native W accounting remains exact after setting authority loss");
    }
}
