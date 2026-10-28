using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

public static partial class PresetIntegrationChecks
{
    private static Action FruitCapacitySetupReady;
    private static Action FruitCapacityBeforeReturn;
    private static Action FruitCapacityBatchEntered;
    public static void ObserveFruitCapacitySetup() => FruitCapacitySetupReady?.Invoke();
    public static void ObserveFruitCapacityBeforeReturn() => FruitCapacityBeforeReturn?.Invoke();
    public static void ObserveFruitCapacityBatch() => FruitCapacityBatchEntered?.Invoke();

    private static void ConfigureFruitCapacityPluginFixture(AssemblyDefinition plugin)
    {
        var method = plugin.MainModule.GetType("NGUInjector.Main").Methods.Single(x => x.Name == "ActivateCustomFruitsWithCapGear");
        var setup = method.Body.Instructions.Single(x => (x.Operand as MethodReference)?.Name == "AcquireFiniteActionSetup");
        method.Body.GetILProcessor().InsertAfter(setup, Instruction.Create(OpCodes.Call,
            plugin.MainModule.ImportReference(typeof(PresetIntegrationChecks).GetMethod(nameof(ObserveFruitCapacitySetup), All))));
        var activation = method.Body.Instructions.Last(x => (x.Operand as MethodReference)?.Name == "ActivateAffordableFruits");
        method.Body.GetILProcessor().InsertAfter(activation, Instruction.Create(OpCodes.Call,
            plugin.MainModule.ImportReference(typeof(PresetIntegrationChecks).GetMethod(nameof(ObserveFruitCapacityBeforeReturn), All))));
        var batch = plugin.MainModule.GetType("NGUInjector.Main").Methods.Single(x => x.Name == "TryActivateCustomFruits");
        batch.Body.GetILProcessor().InsertBefore(batch.Body.Instructions[0], Instruction.Create(OpCodes.Call,
            plugin.MainModule.ImportReference(typeof(PresetIntegrationChecks).GetMethod(nameof(ObserveFruitCapacityBatch), All))));
    }

    private static object FruitCapacityGoal(bool energy) => Enum.Parse(
        Plugin.GetType("NGUInjector.Managers.GearOptimizationGoal", true), energy ? "EnergyCap" : "MagicCap");

    private static object FruitCapacityMode() => Enum.ToObject(Plugin.GetType("NGUInjector.Managers.FightScoreMode", true), 0);

    private static object PrepareFruitCapacityWorld(bool energy, bool intermediate = false)
    {
        var plan = SetupInlineStagePlan(false);
        var stage = ((Array)Get(plan, "Stages")).GetValue(0);
        Set(stage, "EnergyPriorities", new[] { "TM" }); Set(stage, "MagicPriorities", new[] { "TM" });
        var inventory = Get(Character, "inventory"); var head = Get(inventory, "head");
        var items = (IList)Get(inventory, "inventory");
        foreach (var item in new[] { head, items[0], items[1], items[2] })
        {
            Set(item, "spec1Type", Enum.Parse(Field(item, "spec1Type").FieldType, "EnergyCap"));
            Set(item, "spec2Type", Enum.Parse(Field(item, "spec2Type").FieldType, "MagicCap"));
            Set(item, "spec3Type", Enum.Parse(Field(item, "spec3Type").FieldType, "None"));
            Set(item, "spec1Cur", ReferenceEquals(item, head) ? 10000f : 0f);
            Set(item, "spec2Cur", ReferenceEquals(item, head) ? 10000f : 0f);
            Set(item, "spec3Cur", 0f);
        }
        Set(items[0], energy ? "spec1Cur" : "spec2Cur", 30000f);
        if (intermediate)
        {
            // Final capacity grows, but relocating this accessory first stages it
            // through inventory, temporarily losing both capacity bonuses.
            Set(items[0], energy ? "spec2Cur" : "spec1Cur", 10000f);
            var accs = (IList)Get(inventory, "accs"); Set(accs[0], "id", 0);
            foreach (var spec in new[] { "spec1", "spec2" })
            {
                Set(accs[1], spec + "Type", Get(head, spec + "Type"));
                Set(accs[1], spec + "Cur", 90000f);
            }
        }
        Set(Character, "capEnergy", 1000L); Set(Get(Character, "magic"), "capMagic", 1000L);
        Set(Get(Get(Character, "buttons"), "brokenTimeMachine"), "m_Interactable", true);
        var tm = Get(Character, "timeMachineController"); Set(tm, "character", Character);
        Set(tm, "baseNormalSpeedDivider", 1e20f); Set(tm, "baseNormalGoldMultiDivider", 1e20f);
        Invoke(typeof(Program), "RefreshBonuses");
        var attack = (long[])Property(Get(Character, "training"), "attackEnergy");
        var defense = (long[])Property(Get(Character, "training"), "defenseEnergy");
        Array.Clear(attack, 0, attack.Length); Array.Clear(defense, 0, defense.Length); attack[0] = 7; defense[5] = 5;
        var e = (long)Invoke(Character, "totalCapEnergy"); var m = (long)Invoke(Character, "totalCapMagic");
        Set(Character, "curEnergy", e); Set(Character, "idleEnergy", e - 12);
        Set(Get(Character, "magic"), "curMagic", m); Set(Get(Character, "magic"), "idleMagic", m);
        var settings = Get(MainType, "Settings");
        Set(settings, "_energyNguWeights", new[] { 1, 1, 0, 1, 0, 1, 0, 0, 0 });
        Set(settings, "_magicNguWeights", new[] { 1, 1, 0, 0, 0, 0, 0 });
        var store = Get(MainType, "CustomPlanStore"); var name = (string)Property(settings, "CustomPlanFile");
        Invoke(store, "Save", name, plan); plan = Invoke(store, "Load", name);
        RunVerifiedInline(plan, "fruit capacity full TM baseline");
        Equal(e - 12, Get(Get(Character, "machine"), "speedEnergy"), "fruit fixture fully funds native TM Energy plus basic12");
        Equal(m, Get(Get(Character, "machine"), "goldMultiMagic"), "fruit fixture fully funds native TM Magic");
        PrepareCapacityFruit(energy);
        return plan;
    }

    private static void PrepareCapacityFruit(bool energy)
    {
        var all = Get(Character, "yggdrasilController"); Set(all, "character", Character);
        var fruits = ListField(Get(Character, "yggdrasil"), "fruits", 1);
        var controller = ArrayField(all, "fruits", 1).GetValue(0);
        Set(controller, "character", Character); Child(controller, "tooltip");
        Set(all, "activationCost", new List<long> { 100L }); Set(all, "usesEnergy", new List<bool> { energy });
        Set(fruits[0], "maxTier", 1L);
        Set(Get(Get(Character, "buttons"), "yggdrasil"), "m_Interactable", true);
    }

    private static void CheckFruitCapacity()
    {
        var before = Assertions;
        foreach (var energy in new[] { true, false })
        foreach (var kind in new[] { "funded", "other-unmanaged", "fruit-unmanaged", "unaffordable", "intermediate", "delayed", "unknown" })
            CheckFruitCapacityFallback(energy, kind);
        CheckFruitCapacitySelectionAuthority();
        CheckFruitCapacityCurrentSuccessor();
        foreach (var energy in new[] { true, false })
        foreach (var successor in new[] { false, true })
        foreach (var immediate in new[] { false, true })
            CheckFruitCapacityGrowingReturn(energy, successor, immediate);
        foreach (var energy in new[] { true, false })
        foreach (var kind in new[] { "unmanaged", "no-authority", "foreign-map", "unknown", "owner", "foreign-route", "recall-permission", "queued", "unaffordable" })
            CheckFruitCapacityGrowingReturn(energy, kind == "no-authority", false, kind);
        CheckFruitCapacityGrowingReturn(true, false, true, "automatic-batch");
        Console.WriteLine("Fruit capacity: " + (Assertions - before) + " native assertions.");
    }

    private static void CheckFruitCapacityFallback(bool energy, string kind)
    {
        var label = (energy ? "Energy" : "Magic") + "/" + kind;
        var plan = PrepareFruitCapacityWorld(energy, kind == "intermediate");
        var settings = Get(MainType, "Settings"); var magic = Get(Character, "magic");
        if (kind == "other-unmanaged") Set(settings, energy ? "_manageMagic" : "_manageEnergy", false);
        if (kind == "fruit-unmanaged") Set(settings, energy ? "_manageEnergy" : "_manageMagic", false);
        if (kind == "unaffordable") Set(Get(Character, "yggdrasilController"), "activationCost", new List<long> { long.MaxValue / 2 });
        var desired = CallMain("SelectSetupGear", FruitCapacityGoal(energy), FruitCapacityMode(), true);
        var optimizer = Plugin.GetType("NGUInjector.Managers.GearOptimizer", true);
        var none = Enum.ToObject(Plugin.GetType("NGUInjector.Managers.GearRecallFootprint", true), 0);
        var preflight = Invoke(optimizer, "PreflightCapacityTransition", desired, none);
        Check(Get(preflight, "Failure") != null, label + " exact native preflight requires a forbidden recall");
        var nativeBefore = CapacityNativeMapKey(Character); var swaps = Get(typeof(Program), "Swaps");
        var rejected = Invoke(Plugin.GetType("NGUInjector.Managers.LoadoutManager", true), "ApplyExactLoadoutObserved", desired, none, null);
        Equal("RejectedUnchanged", Get(rejected, "Disposition").ToString(), label + " original capacity safety guard still refuses the actual swap");
        Check(((string)Get(rejected, "Reason")).Contains("intermediate or final"), label + " retains the exact native guard diagnostic");
        Equal(swaps, Get(typeof(Program), "Swaps"), label + " native negative control makes no gear write");
        Equal(nativeBefore, CapacityNativeMapKey(Character), label + " native negative control preserves all funded bars");
        var application = Get(Property(MainInstance, "_resourceSetupExecutor"), "Application");
        object unresolved = null;
        if (kind == "unknown")
        {
            unresolved = New("ResourceMutationResult"); Set(unresolved, "AffectedLanes", energy ? 1 : 2);
            Set(unresolved, "Disposition", Enum.Parse(Field(unresolved, "Disposition").FieldType, "UnknownOutcome"));
            Set(unresolved, "Reason", "retained fruit capacity uncertainty");
            Invoke(application, "RecordUnresolved", Character, unresolved);
        }
        var blocked = Property(application, "BlockedLanes"); var conflict = CallMain("SelectedSetupConflictVersion", Character);
        var head = Get(Get(Character, "inventory"), "head"); var support = Property(CallMain("CaptureSupportState"), "StableKey");
        var entry = Property(Get(MainInstance, "_protectedScheduler"), "Entry");
        var e = (long)Get(Character, "curEnergy"); var m = (long)Get(magic, "curMagic");
        var other = CallMain("GlobalPresetLaneState", !energy);
        var recalls = Get(typeof(Program), "Recalls"); var magicRecalls = Get(typeof(Program), "MagicRecalls");
        var renders = 0;
        Program.FiniteFruitRendered = () => {
            renders++;
            Equal(false, CallMain("ExecutionOwnsDomain", Domain("Gear, Support, Route")), label + " fallback acquires no setup owner");
        };
        var expected = kind == "fruit-unmanaged" || kind == "unaffordable" || kind == "unknown" ? 0 : 1;
        try { Equal(expected, CallMain("ActivateCustomFruitsWithCapGear", FruitCapacityGoal(energy), FruitCapacityMode()), label + " activates only currently affordable permitted fruit"); }
        finally { Program.FiniteFruitRendered = null; }
        Equal(expected, renders, label + " uses the actual native fruit leaf exactly once or not at all");
        Equal(e - (energy ? expected * 100 : 0), Get(Character, "curEnergy"), label + " spends only actual Energy");
        Equal(m - (energy ? 0 : expected * 100), Get(magic, "curMagic"), label + " spends only actual Magic");
        Equal(other, CallMain("GlobalPresetLaneState", !energy), label + " preserves the complete other lane");
        Check(ReferenceEquals(head, Get(Get(Character, "inventory"), "head")), label + " preserves current head rather than forcing cap gear");
        Equal(swaps, Get(typeof(Program), "Swaps"), label + " performs no unsafe temporary swap or return");
        Equal(recalls, Get(typeof(Program), "Recalls"), label + " acquires no Energy recall authority");
        Equal(magicRecalls, Get(typeof(Program), "MagicRecalls"), label + " acquires no Magic recall authority");
        Equal(support, Property(CallMain("CaptureSupportState"), "StableKey"), label + " preserves support");
        Equal(entry, Property(Get(MainInstance, "_protectedScheduler"), "Entry"), label + " keeps the original stage entry");
        Equal(blocked, Property(application, "BlockedLanes"), label + " never clears unresolved evidence");
        Equal(conflict, CallMain("SelectedSetupConflictVersion", Character), label + " cannot poison unrelated setup ownership");
        Equal(false, CallMain("ExecutionOwnsDomain", Domain("Energy, Magic, Gear, Support, Route, FiniteAction")), label + " leaves no claims");
        Equal(7L, ((long[])Property(Get(Character, "training"), "attackEnergy"))[0], label + " preserves basic attack training");
        Equal(5L, ((long[])Property(Get(Character, "training"), "defenseEnergy"))[5], label + " preserves basic defense training");
        Equal(731L, Get(Get(Character, "res3"), "curRes3"), label + " preserves unmanaged R3 total");
        Equal(431L, Get(Get(Character, "res3"), "idleRes3"), label + " preserves unmanaged R3 idle");
        foreach (var lane in new[] { "skills", "magicSkills" })
            Equal(0L, ((IList)Get(Get(Character, "NGU"), lane)).Cast<object>().Sum(x => (long)Get(x, lane == "skills" ? "energy" : "magic")), label + " preserves all NGU zero exclusions");
        if (kind == "delayed")
        {
            NativeRouteCapacityClamp(3);
            Check((long)Get(Character, "idleEnergy") >= 0 && (long)Get(magic, "idleMagic") >= 0, label + " later native callbacks cannot create debt");
            Equal(Get(Character, "curEnergy"), (long)Get(Get(Character, "machine"), "speedEnergy") + 12 + (long)Get(Character, "idleEnergy"), label + " delayed native Energy conserves exact allocations");
            Equal(Get(magic, "curMagic"), (long)Get(Get(Character, "machine"), "goldMultiMagic") + (long)Get(magic, "idleMagic"), label + " delayed native Magic conserves exact allocations");
        }
        if (kind == "unknown")
        {
            Equal(nativeBefore, CapacityNativeMapKey(Character), label + " preserves exact unknown native evidence");
            Check(ReferenceEquals(unresolved, Property(application, "Last")), label + " retains the original unresolved result");
        }
        if (kind == "funded") RunVerifiedInline(plan, label + " healthy owner continues after optional cap refusal");
        if (kind == "intermediate")
        {
            var projection = Plugin.GetType("NGUInjector.Managers.ResourceProjectionCapture", true);
            var bonuses = Invoke(projection, "GetBonuses", desired);
            var finalE = Convert.ToInt64(Invoke(Invoke(projection, "Create", Character, true, true), "Project", bonuses));
            var finalM = Convert.ToInt64(Invoke(Invoke(projection, "Create", Character, false, true), "Project", bonuses));
            Check(finalE >= e && finalM >= m, label + " final capacity alone would have retained both original pools");
            var coupled = Invoke(optimizer, "PreflightCapacityTransition", desired, Enum.ToObject(none.GetType(), 3));
            Check((long)Get(coupled, "EnergyMinimum") < e || (long)Get(coupled, "MagicMinimum") < m, label + " native actuator slot order really dips below the original pool");
        }
    }

    private static void CheckFruitCapacitySelectionAuthority()
    {
        foreach (var change in new[] { "mode", "entry", "boss", "permission" })
        {
            PrepareFruitCapacityWorld(true);
            var before = CapacityNativeMapKey(Character); var swaps = Get(typeof(Program), "Swaps");
            var e = Get(Character, "curEnergy"); var m = Get(Get(Character, "magic"), "curMagic");
            var settings = Get(MainType, "Settings"); var selections = 0;
            Program.FinitePreparationSelectionStarting = () => {
                selections++;
                if (change == "mode") Set(settings, "_useCustomRebirthPlan", false);
                if (change == "entry") Set(MainInstance, "_customStageEquipmentKey", "new-fruit-stage");
                if (change == "boss") Set(Get(Character, "bossController"), "isFighting", true);
                if (change == "permission") Set(settings, "_manageEnergy", false);
            };
            try { Equal(0, CallMain("ActivateCustomFruitsWithCapGear", FruitCapacityGoal(true), FruitCapacityMode()), change + " callback withdraws fruit admission"); }
            finally { Program.FinitePreparationSelectionStarting = null; }
            Equal(1, selections, change + " reaches the actual gear-selection boundary");
            Equal(before, CapacityNativeMapKey(Character), change + " callback preserves every native bar");
            Equal(e, Get(Character, "curEnergy"), change + " callback spends no Energy");
            Equal(m, Get(Get(Character, "magic"), "curMagic"), change + " callback spends no Magic");
            Equal(swaps, Get(typeof(Program), "Swaps"), change + " callback writes no gear");
            Equal(false, Get(((IList)Get(Get(Character, "yggdrasil"), "fruits"))[0], "activated"), change + " callback invokes no native activation");
        }
    }

    private static void CheckFruitCapacityCurrentSuccessor()
    {
        var plan = SetupInlineStagePlan();
        var settings = Get(MainType, "Settings"); var store = Get(MainType, "CustomPlanStore");
        var name = (string)Property(settings, "CustomPlanFile"); Invoke(store, "Save", name, plan); plan = Invoke(store, "Load", name);
        RunVerifiedInline(plan, "fruit safe swap A");
        var inventory = Get(Character, "inventory"); var items = (IList)Get(inventory, "inventory");
        var temporary = items.Cast<object>().Single(x => (int)Get(x, "id") == 552);
        Set(temporary, "spec1Type", Enum.Parse(Field(temporary, "spec1Type").FieldType, "EnergyCap")); Set(temporary, "spec1Cur", 30000f);
        Set(temporary, "spec2Type", Enum.Parse(Field(temporary, "spec2Type").FieldType, "MagicCap")); Set(temporary, "spec2Cur", 10000f);
        Set(items.Cast<object>().Single(x => (int)Get(x, "id") == 551), "spec1Cur", 20000f);
        Invoke(Plugin.GetType("NGUInjector.Managers.GearOptimizer", true), "ClearCache");
        PrepareCapacityFruit(false);
        var total = (long)Get(Get(Character, "magic"), "curMagic"); var seen = new List<int>();
        GearRenderObserver = () => seen.Add((int)Get(Get(inventory, "head"), "id"));
        var entry = Get(MainInstance, "_customStageEquipmentKey");
        FruitCapacitySetupReady = () => {
            var stage = ((Array)Get(plan, "Stages")).GetValue(0);
            Set(stage, "GearGoal", (int)FruitCapacityGoal(false));
            Set(stage, "EnergyPriorities", new[] { "AT", "NGU" });
            Invoke(store, "Save", name, plan); plan = Invoke(store, "Load", name);
            CallMain("RunCustomPlanAutomation", plan);
            Equal(entry, Get(MainInstance, "_customStageEquipmentKey"), "actual same-entry policy edit keeps fruit admission current");
            var baseline = Get(Get(MainInstance, "_inlineStageSelection"), "Baseline");
            Check(((Array)Get(baseline, "Gear")).Cast<object>().Any(x => (int)Property(x, "slot") == -1 && (int)Property(x, "id") == 551), "real current stage policy independently acquires B");
            Equal(552, Get(Get(inventory, "head"), "id"), "new current B stays covered by the verified fruit setup");
        };
        try { Equal(1, CallMain("ActivateCustomFruitsWithCapGear", FruitCapacityGoal(true), FruitCapacityMode()), "safe cap setup activates native fruit"); }
        finally { GearRenderObserver = null; FruitCapacitySetupReady = null; }
        Check(seen.Contains(552), "safe cap path still applies the actual acquired gear");
        Equal(551, Get(Get(inventory, "head"), "id"), "fruit releases directly to actual scheduler B, not historical A");
        Check(!seen.SkipWhile(x => x != 552).Skip(1).Contains(550), "fruit release never flashes historical A");
        RunVerifiedInline(plan, "fruit actual CURRENT B allocation");
        Equal(total - 100, Get(Get(Character, "magic"), "curMagic"), "successor uses actual post-fruit Magic total");
        Equal(total - 100, ((IList)Get(Get(Character, "NGU"), "magicSkills")).Cast<object>().Sum(x => (long)Get(x, "magic")), "actual current B funds only the native post-cost remainder");
    }

    private static void CheckFruitCapacityGrowingReturn(bool energy, bool successor, bool immediate, string kind = "healthy")
    {
        var label = "fruit growing return/" + (energy ? "E" : "M") + "/" + (successor ? "B" : "A") + "/immediate=" + immediate + "/" + kind;
        var spent = kind == "queued" || kind == "unaffordable" ? 0 : 100;
        var completeReturn = kind == "healthy" || kind == "queued" || kind == "unaffordable";
        var plan = SetupInlineStagePlan();
        var settings = Get(MainType, "Settings"); var inventory = Get(Character, "inventory");
        var a = Get(inventory, "head"); var items = (IList)Get(inventory, "inventory");
        var cap = items.Cast<object>().Single(x => (int)Get(x, "id") == 550);
        var b = items.Cast<object>().Single(x => (int)Get(x, "id") == 551);
        foreach (var item in new[] { a, cap, b })
        {
            Set(item, "spec1Type", Enum.Parse(Field(item, "spec1Type").FieldType, "EnergyCap"));
            Set(item, "spec2Type", Enum.Parse(Field(item, "spec2Type").FieldType, "MagicCap"));
            Set(item, "spec3Type", Enum.Parse(Field(item, "spec3Type").FieldType, "NGU"));
            Set(item, "spec1Cur", ReferenceEquals(item, cap) && energy ? 30000f : ReferenceEquals(item, b) && energy ? 1000f : 0f);
            Set(item, "spec2Cur", ReferenceEquals(item, cap) && !energy ? 30000f : ReferenceEquals(item, b) && !energy ? 1000f : 0f);
            Set(item, "spec3Cur", ReferenceEquals(item, a) ? 10000f : 0f);
            Set(item, "curAttack", ReferenceEquals(item, b) ? 1e20f : 1f);
        }
        Set(Character, "capEnergy", 1000L); Set(Get(Character, "magic"), "capMagic", 1000L);
        Invoke(typeof(Program), "RefreshBonuses");
        var training = Get(Character, "training");
        var attack = (long[])Property(training, "attackEnergy"); var defense = (long[])Property(training, "defenseEnergy");
        Array.Clear(attack, 0, attack.Length); Array.Clear(defense, 0, defense.Length); attack[0] = 7; defense[5] = 5;
        Set(Character, "curEnergy", 1000L); Set(Character, "idleEnergy", 988L);
        Set(Get(Character, "magic"), "curMagic", 1000L); Set(Get(Character, "magic"), "idleMagic", 1000L);
        Set(settings, "_energyNguWeights", new[] { 1, 1, 0, 1, 0, 1, 0, 0, 0 });
        Set(settings, "_magicNguWeights", new[] { 1, 1, 0, 0, 0, 0, 0 });
        var store = Get(MainType, "CustomPlanStore"); var library = Get(store, "<PresetLibrary>k__BackingField");
        var root = Invoke(library, "ReadExternalLibrary");
        var id = "fruit-growing-A-" + Guid.NewGuid().ToString("N"); var bid = "fruit-growing-B-" + Guid.NewGuid().ToString("N");
        Invoke(Json(root, "Presets"), "Add", "", JsonParse("{\"Id\":\"" + id + "\",\"Name\":\"Fruit growing A\",\"Weights\":[1,1,0,0,0,0],\"SupportMode\":\"current\",\"AutoUpdate\":true}"));
        Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
        var stage = ((Array)Get(plan, "Stages")).GetValue(0); Set(stage, "PresetId", id);
        var stages = Array.CreateInstance(stage.GetType(), 1); stages.SetValue(stage, 0); Set(plan, "Stages", stages);
        var name = (string)Property(settings, "CustomPlanFile");
        Invoke(store, "Save", name, plan); plan = Invoke(store, "Load", name);
        CallMain("RunCustomPlanAutomation", plan);
        Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), label + " actual scheduled NGU preset A is acquired: " + CallMain("GetCustomAutomationStatus"));
        Check(ReferenceEquals(a, Get(inventory, "head")), label + " A is the selected actual NGU head");
        Equal(0L, Get(Character, "idleEnergy"), label + " A really funds its complete Energy pool");
        Equal(0L, Get(Get(Character, "magic"), "idleMagic"), label + " A really funds its complete Magic pool");
        PrepareCapacityFruit(energy);
        if (kind == "automatic-batch")
        {
            CheckFruitCapacityAutomaticBatch(plan, a, cap, b);
            return;
        }
        if (kind == "unaffordable") Set(Get(Character, "yggdrasilController"), "activationCost", new List<long> { long.MaxValue / 2 });
        var pool = energy ? Character : Get(Character, "magic");
        var totalField = energy ? "curEnergy" : "curMagic"; var idleField = energy ? "idleEnergy" : "idleMagic";
        var seen = new List<int>(); var nativeGrowth = 0; var activations = 0; var recalls = 0;
        string returnMap = null; object competing = null;
        var entry = Get(MainInstance, "_customStageEquipmentKey"); var original = (int)Get(a, "id");
        GearRenderObserver = () => {
            seen.Add((int)Get(Get(inventory, "head"), "id"));
            if (immediate && !ReferenceEquals(cap, Get(inventory, "head"))) NativeRouteCapacityClamp(energy ? 1 : 2);
        };
        RecallObserver = () => {
            recalls++;
            Equal(-1, Get(Get(Character, "adventure"), "zone"), label + " every recall follows Safe Zone");
            Equal(false, CallMain("ActivateFruitAction", 0), label + " recall callback cannot reenter the fruit reservation or native leaf");
            Equal(0, CallMain("ActivateCustomFruitsWithCapGear", FruitCapacityGoal(!energy), FruitCapacityMode()), label + " recall callback cannot reacquire a cap setup");
            if (kind == "recall-permission") Set(settings, energy ? "_manageEnergy" : "_manageMagic", false);
        };
        FruitCapacitySetupReady = () => {
            Check(ReferenceEquals(cap, Get(inventory, "head")), label + " cap optimization actually equips the raised-cap head");
            for (var tick = 0; tick < 300; tick++) NativeRouteCapacityClamp(energy ? 1 : 2);
            nativeGrowth = checked((int)((long)Get(pool, totalField) - 1000));
            Check(nativeGrowth >= 200, label + " actual native generator, not a synthetic total, raises the pool");
            CallMain("ReconcileCustomResources", plan, false);
            Equal(0L, Get(pool, idleField), label + " current NGU policy really funds the newly generated pool under cap gear");
            if (successor)
            {
                root = Invoke(library, "ReadExternalLibrary");
                var weights = kind == "no-authority" ? energy ? "[0,1,0,0,0,0,0,0,0,0,1000000,0]" : "[1,0,0,0,0,0,0,0,0,0,1000000,0]"
                    : "[1,1,0,0,0,0,0,0,0,0,1000000,0]";
                Invoke(Json(root, "Presets"), "Add", "", JsonParse("{\"Id\":\"" + bid + "\",\"Name\":\"Fruit growing B\",\"Weights\":" + weights + ",\"SupportMode\":\"current\",\"AutoUpdate\":true}"));
                Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
                Set(((Array)Get(plan, "Stages")).GetValue(0), "PresetId", bid);
                Invoke(store, "Save", name, plan); plan = Invoke(store, "Load", name);
                CallMain("RunCustomPlanAutomation", plan);
                Equal(entry, Get(MainInstance, "_customStageEquipmentKey"), label + " B does not reset the acquired stage entry");
                Equal(bid, Get(Get(MainInstance, "_globalPresetReceipt"), "Id"), label + " actual B policy supersedes A while cap gear remains owned");
            }
            if (kind == "queued") Set(MainInstance, "_resourceReconciliationRunning", true);
        };
        Program.FiniteFruitRendered = () => {
            activations++;
            Equal(1000L + nativeGrowth - 100, Get(pool, totalField), label + " native fruit spends exactly its current cost");
            Check(ReferenceEquals(cap, Get(inventory, "head")), label + " native fruit executes under the selected cap setup");
        };
        FruitCapacityBeforeReturn = () => {
            if (kind == "unmanaged") Set(settings, energy ? "_manageEnergy" : "_manageMagic", false);
            if (kind == "foreign-map")
            {
                var skills = (IList)Get(Get(Character, "NGU"), energy ? "skills" : "magicSkills");
                var field = energy ? "energy" : "magic";
                Set(skills[0], field, (long)Get(skills[0], field) + 1);
                Set(skills[1], field, (long)Get(skills[1], field) - 1);
            }
            if (kind == "unknown")
            {
                var unresolved = New("ResourceMutationResult"); Set(unresolved, "AffectedLanes", energy ? 1 : 2);
                Set(unresolved, "Disposition", Enum.Parse(Field(unresolved, "Disposition").FieldType, "UnknownOutcome"));
                Set(unresolved, "Reason", "growing fruit return retained unknown");
                Invoke(Get(Property(MainInstance, "_resourceSetupExecutor"), "Application"), "RecordUnresolved", Character, unresolved);
            }
            if (kind == "owner") competing = CallMain("BeginExecutionClaim", "fruit-return-other-owner", "current", Domain(energy ? "Energy" : "Magic"), (Func<bool>)(() => true));
            if (kind == "foreign-route") Set(Get(Character, "adventure"), "zone", 1000);
            returnMap = CapacityNativeMapKey(Character);
        };
        try
        {
            if (kind == "recall-permission")
                Throws(() => CallMain("ActivateCustomFruitsWithCapGear", FruitCapacityGoal(energy), FruitCapacityMode()), "authority", label + " loss of lane permission during native recall aborts the next operation");
            else Equal(spent == 0 ? 0 : 1, CallMain("ActivateCustomFruitsWithCapGear", FruitCapacityGoal(energy), FruitCapacityMode()), label + " complete native fruit activation reports its actual commitment");
        }
        finally
        {
            FruitCapacitySetupReady = null; FruitCapacityBeforeReturn = null; Program.FiniteFruitRendered = null; GearRenderObserver = null; RecallObserver = null;
            if (kind == "queued") Set(MainInstance, "_resourceReconciliationRunning", false);
        }
        Equal(spent == 0 ? 0 : 1, activations, label + " invokes only a genuinely funded native activation");
        if (!completeReturn)
        {
            Check(ReferenceEquals(cap, Get(inventory, "head")), label + " unavailable return authority preserves cap gear");
            Equal(1000L + nativeGrowth - 100, Get(pool, totalField), label + " refusal invents no depletion or refund");
            if (kind == "recall-permission")
            {
                Equal(1, recalls, label + " aborts after exactly the originally authorized recall");
                Equal((long)Get(pool, totalField) - (energy ? 12 : 0), Get(pool, idleField), label + " preserves the known recalled pool without an unsafe swap");
                Check((long)CallMain("SelectedSetupConflictVersion", Character) > 0, label + " preserves callback authority-loss evidence");
            }
            else
            {
                Equal(0, recalls, label + " refuses before every native recall");
                Equal(returnMap, CapacityNativeMapKey(Character), label + " leaves every exact current bar intact");
            }
            Equal(7L, attack[0], label + " refused return preserves basic attack"); Equal(5L, defense[5], label + " refused return preserves basic defense");
            Equal(731L, Get(Get(Character, "res3"), "curRes3"), label + " refused return preserves R3");
            if (kind == "unknown") Equal(energy ? 1 : 2, Property(Get(Property(MainInstance, "_resourceSetupExecutor"), "Application"), "BlockedLanes"), label + " keeps UNKNOWN evidence");
            if (competing != null) CallMain("EndExecutionClaim", competing, false);
            return;
        }
        Check(recalls > 0, label + " return obtains explicit managed-lane recall rather than bypassing the capacity guard");
        Check(ReferenceEquals(successor ? b : a, Get(inventory, "head")), label + " returns to the actual current acquired successor");
        if (successor) Check(!seen.SkipWhile(x => x != 550).Skip(1).Contains(original), label + " never flashes historical A");
        CallMain("ReconcileCustomResources", plan, false);
        var lowCap = (long)Invoke(Character, energy ? "totalCapEnergy" : "totalCapMagic");
        Check(lowCap < 1000 + nativeGrowth - spent, label + " return really lowers capacity below the actual post-fruit pool");
        var writer = Plugin.GetType("NGUInjector.Managers.ResourceWriter", true);
        var map = (IDictionary)Invoke(writer, "CaptureAllocations", Character);
        var allocated = map.Keys.Cast<object>().Where(key => (int)Invoke(writer, "Lane", key) == (energy ? 0 : 1) && map[key] != null)
            .Sum(key => ((long[])map[key]).Sum());
        Equal(lowCap, allocated, label + " current successor funds exactly its safe capacity including basic training");
        Equal((long)Get(pool, totalField), allocated + (long)Get(pool, idleField), label + " complete pre-clamp accounting is exactly conserved");
        Equal(immediate ? lowCap : 1000L + nativeGrowth - spent, Get(pool, totalField), label + " never fabricates a delayed native cap debit");
        Equal(immediate ? 0L : 1000L + nativeGrowth - spent - lowCap, Get(pool, idleField), label + " leaves the exact pending native debit idle");
        NativeRouteCapacityClamp(energy ? 1 : 2);
        Equal(lowCap, Get(pool, totalField), label + " native clamp, not the injector, applies exact depletion");
        Check((long)Get(pool, idleField) >= 0, label + " delayed native clamp cannot consume funded bars");
        Equal(7L, attack[0], label + " preserves basic attack"); Equal(5L, defense[5], label + " preserves basic defense");
        Equal(731L, Get(Get(Character, "res3"), "curRes3"), label + " preserves R3 total");
        Equal(431L, Get(Get(Character, "res3"), "idleRes3"), label + " preserves R3 idle");
        Equal(300L, Get(((IList)Get(Get(Character, "wishes"), "wishes"))[0], "res3"), label + " preserves the funded R3 allocation");
        var ngu = Get(Character, "NGU");
        foreach (var index in new[] { 2, 4, 6, 7, 8 }) Equal(0L, Get(((IList)Get(ngu, "skills"))[index], "energy"), label + " preserves Energy NGU exclusion " + index);
        foreach (var index in new[] { 2, 3, 4, 5, 6 }) Equal(0L, Get(((IList)Get(ngu, "magicSkills"))[index], "magic"), label + " preserves Magic NGU exclusion " + index);
        Equal(1000L, Get(energy ? Get(Character, "magic") : Character, energy ? "curMagic" : "curEnergy"), label + " preserves the unaffected native pool");
        Equal(0L, Get(energy ? Get(Character, "magic") : Character, energy ? "idleMagic" : "idleEnergy"), label + " never recalls or strands the unaffected lane");
        CallMain("RunCustomPlanAutomation", plan);
        Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), label + " current successor remains useful and verified");
        Equal(false, CallMain("ExecutionOwnsDomain", Domain("Energy, Magic, Gear, Support, Route, FiniteAction")), label + " releases every temporary owner");
        Console.WriteLine(label + ": native pool 1000 -> " + (1000 + nativeGrowth) + " -> fruit cost " + spent + " -> " + (1000 + nativeGrowth - spent)
            + " -> native clamp " + lowCap + "; exact funded=" + allocated + "; recalls=" + recalls + ".");
    }

    private static void CheckFruitCapacityAutomaticBatch(object plan, object baseline, object energyCap, object magicCap)
    {
        var inventory = Get(Character, "inventory"); var magic = Get(Character, "magic");
        Set(magicCap, "spec1Cur", 0f); Set(magicCap, "spec2Cur", 30000f);
        Invoke(Plugin.GetType("NGUInjector.Managers.GearOptimizer", true), "ClearCache");
        var controller = Get(Character, "yggdrasilController");
        var fruits = ListField(Get(Character, "yggdrasil"), "fruits", 3);
        foreach (var fruit in fruits.Cast<object>()) Set(fruit, "maxTier", 1L);
        // Leave one Energy fruit inactive after its genuine cap attempt. Neither
        // affordable leaf can activate until its own lane's cap gear generates more.
        Set(controller, "usesEnergy", new List<bool> { true, true, false });
        Set(controller, "activationCost", new List<long> { long.MaxValue / 2, 1100L, 1100L });
        var expired = DateTime.UtcNow.AddMinutes(-6);
        Set(MainInstance, "_customEnergyFruitCapLastRetry", expired); Set(MainInstance, "_customMagicFruitCapLastRetry", expired);
        Set(plan, "AutoActivateFruits", true);
        var store = Get(MainType, "CustomPlanStore"); var name = (string)Property(Get(MainType, "Settings"), "CustomPlanFile");
        Invoke(store, "Save", name, plan); plan = Invoke(store, "Load", name);
        var entry = Get(MainInstance, "_customStageEquipmentKey");
        var energySetups = 0; var magicSetups = 0; var returnReentries = 0; var activations = 0;
        FruitCapacityBatchEntered = () => {
            if (Get(MainInstance, "_fruitCapActionReady") != null && !(bool)CallMain("ExecutionOwnsDomain", Domain("Gear")))
                returnReentries++;
        };
        FruitCapacitySetupReady = () => {
            var head = Get(inventory, "head");
            var energy = ReferenceEquals(head, energyCap);
            Check(energy || ReferenceEquals(head, magicCap), "automatic fruit batch acquires a real selected cap loadout");
            if (energy) energySetups++;
            else
            {
                magicSetups++;
                Equal(expired, Get(MainInstance, "_customMagicFruitCapLastRetry"), "nested cap denial cannot renew Magic retry before the actual Magic attempt");
            }
            for (var tick = 0; tick < 3000; tick++) NativeRouteCapacityClamp(energy ? 1 : 2);
            Equal(4000L, Get(energy ? Character : magic, energy ? "curEnergy" : "curMagic"), "automatic fruit batch uses a genuinely generated raised-cap pool");
        };
        GearRenderObserver = () => {
            if (ReferenceEquals(baseline, Get(inventory, "head"))) NativeRouteCapacityClamp(3);
        };
        Program.FiniteFruitRendered = () => activations++;
        try { CallMain("RunCustomPlanAutomation", plan); }
        finally { FruitCapacityBatchEntered = null; FruitCapacitySetupReady = null; GearRenderObserver = null; Program.FiniteFruitRendered = null; }
        Equal(1, energySetups, "automatic plan actually attempts Energy cap once");
        Equal(1, magicSetups, "Energy cap return must not suppress the real Magic cap branch");
        Check(returnReentries > 0, "automatic fruit test reaches synchronous full-plan batch reentry during cap return");
        Equal(2, activations, "automatic plan invokes both affordable native fruit activations exactly once");
        Equal(false, Get(fruits[0], "activated"), "unaffordable Energy fruit remains inactive after a genuine attempt");
        Equal(true, Get(fruits[1], "activated"), "automatic plan activates Energy fruit in actual cap gear");
        Equal(true, Get(fruits[2], "activated"), "automatic plan activates Magic fruit in its later actual cap gear");
        Check((DateTime)Get(MainInstance, "_customEnergyFruitCapLastRetry") > expired, "genuine incomplete Energy attempt still receives its normal cooldown");
        Equal(expired, Get(MainInstance, "_customMagicFruitCapLastRetry"), "nested denied batches never manufacture or renew Magic cooldown");
        Check(ReferenceEquals(baseline, Get(inventory, "head")), "automatic fruit batch returns to the current acquired NGU baseline");
        Equal(1000L, Get(Character, "curEnergy"), "automatic Energy return preserves exact native clamped total");
        Equal(1000L, Get(magic, "curMagic"), "automatic Magic return preserves exact native clamped total");
        Equal(0L, Get(Character, "idleEnergy"), "automatic Energy successor funds its safe native remainder");
        Equal(0L, Get(magic, "idleMagic"), "automatic Magic successor funds its safe native remainder");
        Equal(entry, Get(MainInstance, "_customStageEquipmentKey"), "automatic fruit batch does not reset the stage entry");
        Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), "whole automatic fruit batch leaves a verified useful NGU policy");
        Equal(false, CallMain("ExecutionOwnsDomain", Domain("Energy, Magic, Gear, Support, Route, FiniteAction")), "automatic fruit batch releases every temporary claim");
    }
}
