using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

public static partial class Program
{
    private static int AutoBoostRandomReads;
    private static bool FailAutoBoostRandom;
    private static object LastAutoBoostResult;

    private static void ConfigureAutoBoostPluginFixture(AssemblyDefinition plugin)
    {
        var leaf = plugin.MainModule.GetType("NGUInjector.Main").Methods.Single(x => x.Name == "RunInventoryLeaf");
        var run = leaf.Body.Instructions.Single(x => x.Operand is MethodReference call && call.Name == "RunFiniteAction");
        var duplicate = Instruction.Create(OpCodes.Dup); leaf.Body.GetILProcessor().InsertAfter(run, duplicate);
        leaf.Body.GetILProcessor().InsertAfter(duplicate, Instruction.Create(OpCodes.Call,
            plugin.MainModule.ImportReference(typeof(Program).GetMethod(nameof(ObserveNativeBoostResult), All))));
        foreach (var instruction in leaf.Body.Instructions.Where(x => x.Operand is Instruction && x.OpCode.Code.ToString().EndsWith("_S")))
        {
            var name = instruction.OpCode.Code.ToString();
            instruction.OpCode = (OpCode)typeof(OpCodes).GetField(name.Substring(0, name.Length - 2)).GetValue(null);
        }
        leaf.Body.MaxStackSize++;
    }

    public static void ObserveNativeBoostResult(object result) { LastAutoBoostResult = result; }

    private static void ConfigureAutoBoostFixture(AssemblyDefinition game)
    {
        var module = game.MainModule;
        var controller = module.Types.Single(x => x.Name == "InventoryController");
        var apply = controller.Methods.Single(x => x.Name == "applyAllBoosts");
        // Preserve the unmodified native controller bodies before the common equip/render
        // fixture replaces them. Boosts must not go through its data-only Inventory swaps.
        foreach (var swap in controller.Methods.Where(x => x.Parameters.Count == 0 && new[] {
            "swapHead", "swapChest", "swapLegs", "swapBoots", "swapWeapon", "swapWeapon2", "swapAcc"
        }.Contains(x.Name)).ToArray())
        {
            var native = new MethodDefinition("FixtureNativeBoost_" + swap.Name, swap.Attributes, swap.ReturnType) { Body = swap.Body };
            controller.Methods.Add(native);
            swap.Body = new Mono.Cecil.Cil.MethodBody(swap);
            var calls = apply.Body.Instructions.Where(x => (x.Operand as MethodReference)?.Name == swap.Name).ToArray();
            if (calls.Length != 1) throw new Exception("Expected one native boost dispatch to " + swap.Name);
            calls[0].Operand = native;
        }
        var refresh = apply.Body.Instructions.Single(x => (x.Operand as MethodReference)?.Name == "updateInventory");
        refresh.OpCode = OpCodes.Call;
        refresh.Operand = module.ImportReference(typeof(Program).GetMethod("AutoBoostInventoryRefresh", All));
        // Unity RNG is an external input, not a boost/recycle decision. Native deletion,
        // boostEquip, bonus calculation and recycling branches remain executable.
        foreach (var method in controller.Methods.Where(x => x.Name == "degradeBoost"))
        foreach (var instruction in method.Body.Instructions)
        {
            var call = instruction.Operand as MethodReference;
            if (call?.DeclaringType.FullName != "UnityEngine.Random") continue;
            if (call.Name == "set_state") { instruction.OpCode = OpCodes.Pop; instruction.Operand = null; }
            else if (call.Name == "get_value")
            {
                instruction.OpCode = OpCodes.Call;
                instruction.Operand = module.ImportReference(typeof(Program).GetMethod("AutoBoostRandomValue", All));
            }
            else if (call.Name == "get_state")
            {
                var getter = new GenericInstanceMethod(module.ImportReference(typeof(Program).GetMethod("AutoBoostRandomState", All)));
                getter.GenericArguments.Add(call.ReturnType);
                instruction.OpCode = OpCodes.Call; instruction.Operand = getter;
            }
            else throw new Exception("Unexpected native boost RNG boundary: " + call.FullName);
        }
    }

    public static T AutoBoostRandomState<T>() { return default(T); }
    public static float AutoBoostRandomValue()
    {
        AutoBoostRandomReads++;
        if (FailAutoBoostRandom) { FailAutoBoostRandom = false; throw new InvalidOperationException("fixture native boost RNG input failure"); }
        return .5f;
    }
    public static void AutoBoostInventoryRefresh(object controller) { RefreshBonuses(); }

    private static bool TryNativeBoostFixtureSwap()
    {
        var inventory = Get(Character, "inventory"); var bag = (IList)Get(inventory, "inventory");
        var donor = (int)Get(inventory, "item2"); var target = (int)Get(inventory, "item1");
        if (donor < 0 || donor >= bag.Count || !(bool)Invoke(bag[donor], "isBoost")) return false;
        var method = target >= 10000 ? "swapAcc" : target < 0 && target >= -6
            ? new[] { "swapHead", "swapChest", "swapLegs", "swapBoots", "swapWeapon", "swapWeapon2" }[-target - 1] : null;
        if (method == null) return false;
        // The unavailable inventory refresh normally rebuilds this derived cache.
        // Reuse the same actual-equipment cache boundary as fixture gear swaps.
        try { Invoke(Controller, "FixtureNativeBoost_" + method); }
        finally { RefreshBonuses(); }
        return true;
    }

    private static object PrepareAutoBoostInventory()
    {
        AutoBoostRandomReads = 0; FailAutoBoostRandom = false; LastAutoBoostResult = null;
        Set(Settings, "_customAutoBoostEnabled", true);
        Set(Settings, "_boostBlacklist", new int[0]); Set(Settings, "_specialBoostBlacklist", new int[0]); Set(Settings, "_priorityBoosts", new int[0]);
        Set(Character, "itemInfo", Get(Controller, "itemInfo"));
        if (Get(Character, "purchases") == null) Child(Character, "purchases");
        ListField(Get(Get(Character, "inventory"), "itemList"), "itemMaxxed", 1000);
        Set(Child(Character, "allItemList"), "character", Character);
        ListField(Child(Character, "beastQuest"), "quirkLevel", 100);
        ListField(Get(Get(Character, "adventure"), "itopod"), "perkLevel", 300);
        foreach (var name in new[] { "itopod", "beastQuestPerkController" })
        {
            var perks = name == "itopod" ? Get(Get(Character, "adventureController"), name) : Get(Character, name);
            Set(perks, "character", Character);
            ListField(perks, "effectPerLevel", 300);
            ListField(perks, name == "itopod" ? "perkDifficultyReq" : "quirkDifficultyReq", 300);
        }
        Set(Get(Get(Character, "allChallenges"), "level100Challenge"), "maxCompletions", 10);
        Set(MainInstance, "_invManager", Activator.CreateInstance(Plugin.GetType("NGUInjector.Managers.InventoryManager", true), true));
        Equal(0, Invoke(Controller, "totalInvMergeSlots"), "native boost fixture has no purchased merge slots");
        Equal(1f, Invoke(Get(Character, "allItemList"), "boostBonus"), "native boost bonus is calculated from zero fixture purchases/perks");
        return Get(Get(Character, "inventory"), "head");
    }

    private static int PutAutoBoost(int id = 27, float amount = 10000000f)
    {
        var bag = (IList)Get(Get(Character, "inventory"), "inventory");
        var slot = Enumerable.Range(0, bag.Count).First(i => (int)Get(bag[i], "id") == 0);
        var boost = Blank(bag[slot].GetType()); Set(boost, "id", id); Set(boost, "removable", true);
        SetEnum(boost, "type", id == 1 ? "atkBoost" : id == 14 ? "defBoost" : "specBoost");
        Set(boost, id == 1 ? "capAttack" : id == 14 ? "capDefense" : "spec1Cap", amount);
        bag[slot] = boost;
        return slot;
    }

    private static void CheckNativeAutoBoostConsumption()
    {
        var before = Assertions;
        foreach (var id in new[] { 1, 14, 27 })
        {
            Setup(); var head = PrepareAutoBoostInventory();
            var field = id == 1 ? "curAttack" : id == 14 ? "curDefense" : "spec1Cur";
            Set(head, id == 1 ? "capAttack" : id == 14 ? "capDefense" : "spec1Cap", 1000f);
            if (id == 27) SetEnum(head, "spec1Type", "EnergyPower");
            var slot = PutAutoBoost(id, 123f); var gear = CallMain("CombatPrepEquipped"); var swaps = Swaps; var recalls = Recalls;
            CallMain("RunCustomAutoBoost");
            Equal(123f, Get(head, field), "real native boostEquip applies the exact boost: " + id);
            Equal(0, Get(((IList)Get(Get(Character, "inventory"), "inventory"))[slot], "id"), "native degradeBoost deletes the consumed boost: " + id);
            Equal(1, AutoBoostRandomReads, "real native consumption reaches its RNG boundary once: " + id);
            Equal(true, CallMain("CombatPrepGearMatches", gear), "native boosting retains every equipped instance: " + id);
            Equal(swaps, Swaps, "boost consumption does not use fixture gear swaps: " + id);
            Equal(recalls, Recalls, "native boosting does not recall resources: " + id);
        }
        Setup();
        Console.WriteLine("Native auto-boost consumption: " + (Assertions - before) + " assertions.");
    }

    // A suspended cached receipt, not a substitute preset acquisition/optimizer. Only the
    // actual deferral predicates are populated; CombatPrepActive blocks resource ownership.
    private static object InstallAutoBoostCachedReceipt()
    {
        var receipt = Activator.CreateInstance(Field(MainInstance, "_globalPresetReceipt").FieldType, true);
        var policy = Activator.CreateInstance(Field(receipt, "Policy").FieldType, true);
        var preset = Activator.CreateInstance(Field(policy, "Preset").FieldType, true); Set(preset, "AutoUpdate", false);
        Set(policy, "Preset", preset);
        Set(policy, "Epoch", Activator.CreateInstance(Field(policy, "Epoch").FieldType, true));
        Set(policy, "EpochObjects", new[] { Character, Controller, Get(Character, "inventory"), Get(Character, "NGU"), Get(Character, "magic"),
            Get(Character, "adventure"), Get(Character, "beards"), Get(Character, "diggers"), Get(Character, "stats") });
        Set(receipt, "Policy", policy); Set(receipt, "Gear", CallMain("CombatPrepEquipped"));
        Set(MainInstance, "_globalPresetReceipt", receipt); Set(MainInstance, "_customEquipmentApplicationInvalidated", true);
        Equal(true, CallMain("GlobalPresetEpochMatches", policy), "cached boost deferral fixture has the actual matching epoch");
        Equal(true, CallMain("GlobalPresetGearMatches", receipt), "cached boost deferral fixture has exact matching gear");
        return receipt;
    }

    private static void CheckAutoBoostRegressions()
    {
        var before = Assertions;
        var failures = new List<Exception>();
        var checks = new Action[] { CheckNativeAutoBoostConsumption, CheckAutoBoostRecoveryOnly, CheckAutoBoostAutomaticEntry,
            () => CheckAutoBoostRunningAutomatic(false), () => CheckAutoBoostRunningAutomatic(true), CheckAutoBoostSuppression, CheckAutoBoostExceptionContinuation };
        foreach (var check in checks)
        {
            try { check(); }
            catch (Exception error) { failures.Add(error); Console.Error.WriteLine("Auto-boost regression failure: " + error.Message); }
        }
        Setup();
        if (failures.Count != 0) throw new AggregateException("Auto-boost regression failures (" + failures.Count + "/" + checks.Length + " groups).", failures);
        Console.WriteLine("Auto-boost regressions: " + (Assertions - before) + " assertions.");
    }

    private static void CheckAutoBoostRecoveryOnly()
    {
        // Positive deferral control outside prep/recovery, with the real manager available.
        Setup(); var head = PrepareAutoBoostInventory(); Set(head, "capAttack", 1000f);
        var slot = PutAutoBoost(1, 123f); InstallAutoBoostCachedReceipt();
        CallMain("RunCustomAutoBoost");
        Equal(0f, Get(head, "curAttack"), "invalidated non-autoUpdate preset defers boosting outside prep");
        Equal(0, AutoBoostRandomReads, "ordinary cached replay deferral never enters native consumption");
        Equal(1, Get(((IList)Get(Get(Character, "inventory"), "inventory"))[slot], "id"), "ordinary deferral retains the boost");

        object plan, original;
        var run = CleanupRecoveryFixture(out plan, out original);
        var entry = Get(run, "OwnerEntry"); var deadline = Get(run, "DeadlineRebirthSeconds");
        FailDesiredEnergyPreflight = true;
        CallMain("EndCombatPrep", "cancelled", "Injector unloaded.", true);
        ScheduledTime(1200); Set(run, "CleanupLastAttempt", (double)Get(MainType, "CombatPrepClock") + 100);
        var store = Get(MainType, "CustomPlanStore"); CallMain("Unload");
        MainInstance = Blank(MainType); Set(MainInstance, "_combatPrepEndedRuns", new HashSet<string>());
        Set(MainInstance, "_customIronPillPrepStartedAt", -1d);
        Set(MainInstance, "_resourceExecutionOwner", ""); Set(MainInstance, "_resourceExecutionReason", "");
        Set(MainType, "reference", MainInstance); Set(MainType, "Character", Character); Set(MainType, "Controller", Controller);
        Set(MainType, "Settings", Settings); Set(MainType, "CustomPlanStore", store); Set(Controller, "midDrag", false);
        CallMain("InitializeProtectedStageProgress", plan);
        Equal(null, Get(MainInstance, "_combatPrepRun"), "checkpoint recovery has no reconstructed live run");
        Equal(false, Get(MainInstance, "CombatPrepActive"), "restart does not import historical cleanup authority or block ordinary maintenance");
        head = PrepareAutoBoostInventory(); Set(head, "capAttack", 1000f); slot = PutAutoBoost(1, 123f);
        Equal(null, Get(MainInstance, "_globalPresetReceipt"), "restart has no imported cached-preset application receipt");
        var gear = CallMain("CombatPrepEquipped");
        var recalls = Recalls; var writes = ResourceCommittedWrites; var swaps = Swaps; var energy = OwnedResourceTotal(true); var magic = OwnedResourceTotal(false);
        var scheduler = Get(MainInstance, "_protectedScheduler"); var stage = Get(scheduler, "StageIndex");
        for (var tick = 0; tick < 3; tick++)
        {
            CallMain("AutomationRoutine"); CallMain("QuickStuff"); CallMain("RunCustomPlanAutomation", plan); CallMain("GetProtectedStage", plan);
        }
        Equal(123f, Get(head, "curAttack"), "AutomationRoutine resumes native boosting without historical cleanup authority");
        Equal(0, Get(((IList)Get(Get(Character, "inventory"), "inventory"))[slot], "id"), "recovery-only scheduling consumes the native boost");
        Equal(1, AutoBoostRandomReads, "recovery consumes once without replaying the boost");
        Equal(true, CallMain("CombatPrepGearMatches", gear), "recovery maintenance preserves exact equipped identities");
        Equal(swaps, Swaps, "recovery boosting and no-gear successor never swap historical gear");
        Equal(true, ResourceCommittedWrites > writes, "retired recovery falls through to actual successor desired-map resource funding");
        Equal(energy, OwnedResourceTotal(true), "recovery preserves energy ownership"); Equal(magic, OwnedResourceTotal(false), "recovery preserves magic ownership");
        Equal(1, Get(scheduler, "StageIndex"), "expired recovery automatically advances to selected ordinary successor");
        Equal(false, Get(scheduler, "Finished"), "successor still needs actual protected time");
        Equal(false, Equals(entry, Get(CallMain("GetProtectedStage", plan), "EntryIdentity")), "successor receives distinct entry identity");
        Equal(1100d, deadline, "historical automatic deadline was never renewed");
        Equal(false, Get(MainInstance, "CombatPrepCleanupRecoveryRequired"), "coordinator retires recovery without acknowledgement");
        var checkpoint = Parse(File.ReadAllText((string)Get(MainInstance, "ProtectedCheckpointPath")));
        Equal(false, Get(Json(Json(checkpoint, "scheduler"), "autoCleanupRecoveryRequired"), "AsBool"), "marker retirement is durable before successor funding");
        Equal(null, Get(MainInstance, "_globalPresetReceipt"), "boosting does not reconstruct a historical cached preset");
        Equal(false, Get(MainInstance, "_customEquipmentApplicationInvalidated"), "selected ordinary successor finishes its own current equipment reconciliation");
        Equal(null, Get(MainInstance, "_combatPrepRun"), "maintenance does not fabricate a live recovery run");
    }

    private static void CheckAutoBoostAutomaticEntry()
    {
        var plan = ScheduledSetup(); var head = PrepareAutoBoostInventory();
        SetEnum(head, "spec1Type", "EnergyPower"); Set(head, "spec1Cap", 100000000f); RefreshBonuses();
        Set(((IList)Get(Get(Character, "wishes"), "wishes"))[190], "level", 1L);
        PutAutoBoost(); CallMain("AutomationRoutine");
        var run = Get(MainInstance, "_combatPrepRun");
        Equal(10000000f, Get(head, "spec1Cur"), "native boost runs before automatic-stage dispatch returns");
        Equal("running", Get(run, "Status"), "ordinary callback still starts the real automatic executor: " + Get(run, "Reason"));
        Equal(true, Get(run, "Automatic"), "ordinary callback retains scheduled ownership");
        Equal("stage-start", Get(run, "ReplanReason"), "initial capture already includes boost without a corrective replan");
        Equal(0, Get(run, "ReplanCount"), "boost before initial capture needs no material re-search");
        Near(Convert.ToDouble(Invoke(Character, "totalEnergyPower")), (double)Get(Get(Get(Get(run, "Capture"), "Snapshot"), "Current"), "EnergyPower"), "automatic entry captures native boosted power");
        CallMain("EndCombatPrep", "cancelled", "auto-boost entry regression cleanup", true);
    }

    private static void CheckAutoBoostRunningAutomatic(bool pendingAt)
    {
        var plan = ScheduledSetup(); var head = PrepareAutoBoostInventory();
        SetEnum(head, "spec1Type", "EnergyPower"); Set(head, "spec1Cap", 100000000f); RefreshBonuses();
        if (pendingAt)
        {
            Set(Get(Get(Character, "buttons"), "augmentation"), "m_Interactable", false);
            foreach (var name in new[] { "wandoosEnergy", "wandoosMagic" }) Set(Get(Get(Character, "advancedTrainingController"), name), "levelFactor", 1000f);
        }
        else Set(((IList)Get(Get(Character, "wishes"), "wishes"))[190], "level", 1L);
        var run = StartScheduled(plan); var policy = Get(run, "Policy"); var phase = ScheduledPhase(run);
        if (pendingAt) Equal("AT", Get(phase, "Kind"), "boost gate fixture acquires real planner paid AT");
        var capture = Get(run, "Capture"); var started = Get(run, "Started"); var duration = Get(run, "Duration");
        var deadline = Get(run, "DeadlineRebirthSeconds"); var entry = Get(run, "OwnerEntry"); var phaseDeadline = Get(run, "PhaseDeadline");
        var targetE = Get(phase, "AtEnergyTarget"); var targetM = Get(phase, "AtMagicTarget");
        var allocations = (long[])Get(Get(Character, "advancedTraining"), "energy"); var paidE = allocations[3]; var paidM = allocations[4];
        var slot = PutAutoBoost(); var swaps = Swaps; var gear = CallMain("CombatPrepEquipped");
        Set(run, "LastRefresh", -1d); CallMain("AutomationRoutine");
        Equal(10000000f, Get(head, "spec1Cur"), "running automatic prep receives actual native boosting");
        Equal(0, Get(((IList)Get(Get(Character, "inventory"), "inventory"))[slot], "id"), "automatic maintenance actually consumes its boost");
        Equal("running", Get(run, "Status"), "native boosting does not invalidate the automatic owner: " + Get(run, "Reason"));
        Equal(pendingAt ? 0 : 1, Get(run, "ReplanCount"), "same callback detects boosted material or defers behind AT");
        Equal(pendingAt, Get(run, "EquipmentReplanPending"), "unfinished acquired AT explicitly defers boosted-material search");
        Equal(swaps, Swaps, "same-instance boost/replan performs no gear swaps in Current-only fixture");
        Equal(true, CallMain("CombatPrepGearMatches", gear), "native boosting/replanning retains exact equipped instances");
        if (pendingAt)
        {
            Equal(true, ReferenceEquals(policy, Get(run, "Policy")), "boost does not replace unfinished AT policy");
            Equal(targetE, Get(ScheduledPhase(run), "AtEnergyTarget"), "boost preserves acquired energy AT gate");
            Equal(targetM, Get(ScheduledPhase(run), "AtMagicTarget"), "boost preserves acquired magic AT gate");
            Equal(phaseDeadline, Get(run, "PhaseDeadline"), "boost preserves unfinished AT phase deadline");
            Equal(true, allocations[3] < paidE && allocations[4] < paidM, "pending AT allocations recap using actual native boosted power; before=" + paidE + "," + paidM
                + "; after=" + allocations[3] + "," + allocations[4] + "; nativePower=" + Invoke(Character, "totalEnergyPower") + "; reason=" + Get(run, "Reason"));
            Equal(0L, Get(Get(Character, "wandoos98"), "wandoosEnergy"), "boost cannot start W energy before AT");
            Equal(0L, Get(Get(Character, "wandoos98"), "wandoosMagic"), "boost cannot start W magic before AT");
            var levels = (long[])Get(Get(Character, "advancedTraining"), "level");
            levels[3] = (long)targetE; ScheduledRefresh(run);
            Equal(0, Get(run, "ReplanCount"), "one reached AT gate is insufficient after boost");
            levels[4] = (long)targetM; ScheduledRefresh(run);
            Equal(1, Get(run, "ReplanCount"), "native boost is replanned once both original AT gates are reached");
            Equal(false, Get(run, "EquipmentReplanPending"), "successful safe replan clears boosted-material deferral");
        }
        Equal("material-equipment-change", Get(run, "ReplanReason"), "native boost uses the existing material replan path");
        Equal(true, ReferenceEquals(capture, Get(run, "Capture")), "boost never replaces immutable restoration capture");
        Equal(started, Get(run, "Started"), "boost never resets run start"); Equal(duration, Get(run, "Duration"), "boost never renews run budget");
        Equal(deadline, Get(run, "DeadlineRebirthSeconds"), "boost never extends original global deadline"); Equal(entry, Get(run, "OwnerEntry"), "boost retains scheduler entry identity");
        ScheduledRefresh(run); Equal(1, Get(run, "ReplanCount"), "consumed native boost does not repeatedly trigger replanning");
        CallMain("EndCombatPrep", "cancelled", "auto-boost running regression cleanup", true);
    }

    private static void CheckAutoBoostSuppression()
    {
        foreach (var guard in new[] { "disabled", "manual", "drag", "cleanup", "gear-transaction" })
        {
            object run;
            if (guard == "cleanup")
            {
                object plan, original; run = CleanupRecoveryFixture(out plan, out original, false);
                FailDesiredEnergyPreflight = true; CallMain("EndCombatPrep", "cancelled", "Injector unloaded.", true);
                Equal(false, FailDesiredEnergyPreflight, "cleanup suppression reaches an actual failed resource release");
                Set(Controller, "midDrag", false); Set(run, "CleanupLastAttempt", (double)Get(MainType, "CombatPrepClock") + 100);
                PrepareAutoBoostInventory(); Set(Get(Get(Character, "inventory"), "head"), "capAttack", 1000f);
                Equal(true, Get(run, "CleanupPending"), "cleanup suppression has pending live restoration without drag");
            }
            else
            {
                var plan = ScheduledSetup(); var head = PrepareAutoBoostInventory(); Set(head, "capAttack", 1000f);
                if (guard == "manual") run = StartServerPolicy(CallMain("CaptureCombatPrep", .03, .03, null), Phase("Wandoos", "current", 60));
                else run = StartScheduled(plan);
                Set(run, "LastRefresh", (double)Get(MainType, "CombatPrepClock") + 100);
            }
            if (guard == "disabled") Set(Settings, "_customAutoBoostEnabled", false);
            if (guard == "drag") Set(Controller, "midDrag", true);
            if (guard == "gear-transaction") Set(MainInstance, "_resourceReconciliationRunning", true);
            var target = Get(Get(Character, "inventory"), "head"); var attack = Get(target, "curAttack"); var slot = PutAutoBoost(1, 123f);
            var previousWriter = Get(MainType, "OutputWriter");
            using (var log = new MemoryStream())
            using (var writer = new StreamWriter(log, new UTF8Encoding(false), 1024, true) { AutoFlush = true })
            {
                try
                {
                    Set(MainType, "OutputWriter", writer); CallMain("AutomationRoutine");
                    Equal(false, Encoding.UTF8.GetString(log.ToArray()).Contains("ERROR in AutomationRoutine:"), "suppression is not an unrelated outer-loop failure: " + guard);
                }
                finally { Set(MainType, "OutputWriter", previousWriter); }
            }
            Equal(attack, Get(target, "curAttack"), "ordinary callback suppresses boosting: " + guard);
            Equal(1, Get(((IList)Get(Get(Character, "inventory"), "inventory"))[slot], "id"), "suppressed callback retains native boost: " + guard);
            Equal(0, AutoBoostRandomReads, "suppression never reaches native consumption: " + guard);
            Set(Controller, "midDrag", false); Set(MainInstance, "_resourceReconciliationRunning", false);
        }
    }

    private static void CheckAutoBoostExceptionContinuation()
    {
        var plan = ScheduledSetup(); var head = PrepareAutoBoostInventory(); Set(head, "capAttack", 1000f);
        Set(((IList)Get(Get(Character, "wishes"), "wishes"))[190], "level", 1L);
        var run = StartScheduled(plan); var slot = PutAutoBoost(1, 123f);
        var deadline = Get(run, "DeadlineRebirthSeconds"); var entry = Get(run, "OwnerEntry");
        Set(run, "LastRefresh", -1d); FailAutoBoostRandom = true;
        var previousWriter = Get(MainType, "OutputWriter");
        using (var log = new MemoryStream())
        using (var writer = new StreamWriter(log, new UTF8Encoding(false), 1024, true) { AutoFlush = true })
        {
            try
            {
                Set(MainType, "OutputWriter", writer); CallMain("AutomationRoutine");
                var text = Encoding.UTF8.GetString(log.ToArray());
                Equal(false, FailAutoBoostRandom, "fault is actually reached in native boost consumption");
                Equal("ModelAppliedRenderFailed", Get(LastAutoBoostResult, "Disposition").ToString(), "identified leaf retains the native committed-error classification");
                Equal(true, Convert.ToString(Get(LastAutoBoostResult, "Error")).Contains("fixture native boost RNG input failure"), "typed boost result retains the original native input failure");
                Equal(false, text.Contains("ERROR in AutomationRoutine:"), "boost exception does not escape to the outer maintenance catch");
                Equal(123f, Get(head, "curAttack"), "native mutation before the external-input failure is not mocked or rolled back");
                Equal(0, Get(((IList)Get(Get(Character, "inventory"), "inventory"))[slot], "id"), "native boost deletion remains committed when its later RNG input fails");
                Equal("running", Get(run, "Status"), "prep remains active after boost exception");
                Equal(true, (double)Get(run, "LastRefresh") >= 0, "prep tick still refreshes after boost exception in the same callback");
                Equal(1, Get(run, "ReplanCount"), "prep tick observes partially committed native boost after exception");
                Equal("material-equipment-change", Get(run, "ReplanReason"), "exception continuation retains normal material replan semantics");
                Equal(deadline, Get(run, "DeadlineRebirthSeconds"), "boost fault cannot renew the original deadline"); Equal(entry, Get(run, "OwnerEntry"), "boost fault keeps entry identity");
            }
            finally { Set(MainType, "OutputWriter", previousWriter); FailAutoBoostRandom = false; }
        }
        CallMain("EndCombatPrep", "cancelled", "auto-boost exception regression cleanup", true);
    }
}
