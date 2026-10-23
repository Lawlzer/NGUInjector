using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public static partial class OwnershipChecks
{
    static object StagePlan()
    {
        var plan = NewPlan(); S(plan, "AutoActivateFruits", false); S(plan, "AutoRebirthEnabled", false); S(plan, "TimeTriggerEnabled", false);
        S(plan, "NumberTriggerEnabled", false); S(plan, "NumberClimbEnabled", false); S(plan, "StageSchedulerEnabled", true); S(plan, "StageLoopStartIndex", -1); S(plan, "StageDurationSeconds", 1000);
        var stage = Activator.CreateInstance(Plugin.GetType("NGUInjector.CustomPlans.CustomPlanStage", true)); S(stage, "GearGoal", 2); S(stage, "DurationSeconds", 100);
        var stages = Array.CreateInstance(stage.GetType(), 1); stages.SetValue(stage, 0); S(plan, "Stages", stages); Setting("UseCustomRebirthPlan", true); return plan;
    }

    static void CheckStageReceipts()
    {
        CheckManualRulePresetHandoff();
        CheckManualStageInvalidation();
        Setup(); var plan = StagePlan(); M("RunCustomPlanAutomation", plan);
        Equal(true, M("InlineStageSetupVerified", plan), "actual normal stage verifies before takeover: " + M("GetCustomAutomationStatus"));
        var selection = G(MainObject, "_inlineStageSelection"); var baseline = G(selection, "Baseline"); var scheduler = G(MainObject, "_protectedScheduler");
        var entry = G(MainObject, "_customStageKey"); var state = Invoke(scheduler, "ExportState").ToString();
        var inv = G(Character, "inventory"); var temporary = NewGear(60, "Head"); ((IList)G(inv, "inventory"))[0] = temporary;
        var claim = BeginClaim("stage-temporary"); ApplyClaim(claim, WithGear(Helper(temporary, -1)));
        Equal(false, G(scheduler, "_armed"), "temporary native transition suspends protected credit");
        M("InvalidateCustomEquipmentApplication"); Equal(null, G(selection, "Applied"), "application invalidation discards applied evidence");
        Check(ReferenceEquals(baseline, G(selection, "Baseline")), "invalidation retains acquired baseline"); Equal(entry, G(MainObject, "_customStageKey"), "invalidation retains stage identity");
        Check(ReferenceEquals(scheduler, G(MainObject, "_protectedScheduler")), "invalidation retains scheduler history"); Equal(true, G(Settings, "UseCustomRebirthPlan"), "invalidation does not pause configured plan");
        var ready = new object[] { plan, null, null }; Equal(false, Invoke(MainObject, "CustomStageReadyForQuickRefresh", ready), "quick refresh cannot use invalidated receipt");
        M("RunCustomPlanAutomation", plan); Check(ReferenceEquals(temporary, G(inv, "head")), "normal service composes current temporary setup rather than flashing baseline");
        Equal(true, M("EndExecutionClaim", claim, true), "normal current successor reconciles on claim exit"); M("RunCustomPlanAutomation", plan);
        Equal(true, M("InlineStageSetupVerified", plan), "actual stage becomes verified after temporary release");
        Check(ReferenceEquals(selection, G(MainObject, "_inlineStageSelection")), "reconciliation reuses acquisition");
        var prior = P("Parse", state); var current = Invoke(scheduler, "ExportState");
        foreach (var field in new[] { "entry", "cycle", "enteredAt", "stageElapsed", "expectedEnd", "durations", "earned", "completed", "applied", "elapsedByStage", "visits", "skipped", "autoAugmentsCompleted", "autoAtCompleted", "autoWandoosStarted" })
            Equal(P("Json", prior, field).ToString(), P("Json", current, field).ToString(), "claim service retains original commitment/" + field);
        CheckSelectedFailureCapture();
    }

    static void CheckManualRulePresetHandoff()
    {
        foreach (var owner in new[] { "manual-adventure:1", "stage:fixture" })
        {
            Setup(); Setting("AutoRebirth", false); Setting("CustomAdventureEnabled", true); Setting("ManualAdventureEnabled", false);
            Setting("CustomAdventureMode", 0); Setting("ManualTitanEnabled", false);
            var rules = Invoke(Plugin.GetType("NGUInjector.AdventureRule", true), "Parse", P("Json", P("Parse", "{\"rules\":[{\"Enabled\":true,\"MinPower\":0,\"MinToughness\":0,\"MinRegen\":0,\"Mode\":4,\"Zone\":1}]}"), "rules"));
            S(Settings, "_customAdventureRules", rules); M("ObserveCustomRebirthEpoch");
            // Stale presentation/cache state is not native authority. A real admitted
            // delegated claim supplies the old process that must actually retire.
            S(MainObject, "_globalPresetManualId", "fixture-preset"); S(MainObject, "_manualAdventureSetupActive", true); S(MainObject, "_customEquipmentOwner", owner);
            var at = DateTime.UtcNow; S(MainObject, "_customEquipmentLastDiggersAt", at);
            var process = M("BeginRouteProcess", "delegated-manual", CapturePitGear(), true, Domain("Gear, Support, Route, NativeSettings"), null);
            S(MainObject, "_delegatedManualAdventureProcess", process); Equal(true, M("ApplyRouteProcess", process), "stale delegated process has actual verified setup");
            Equal(true, G(MainObject, "ManualAdventureOwnsEquipment"), "manual intent starts active");
            var gear = CapturePitGear(); var support = G(M("CaptureSupportState"), "StableKey"); var resources = P("Resources"); var swaps = G(typeof(Program), "Swaps"); var recalls = G(typeof(Program), "Recalls");
            using (var log = new System.IO.MemoryStream())
            using (var writer = new System.IO.StreamWriter(log) { AutoFlush = true })
            {
                S(MainType, "OutputWriter", writer); try { M("CustomAdventureTick"); } finally { S(MainType, "OutputWriter", null); }
                Check(!System.Text.Encoding.UTF8.GetString(log.ToArray()).Contains("ERROR"), "preset handoff reaches actual early return without swallowed routing failure");
            }
            Equal(false, G(MainObject, "_manualAdventureSetupActive"), "preset early return retires removed manual intent"); Equal(false, G(MainObject, "ManualAdventureOwnsEquipment"), "removed manual rule advertises no equipment ownership");
            Equal(null, G(MainObject, "_delegatedManualAdventureProcess"), "actual delegated process retired"); Equal(true, G(G(process, "Claim"), "Ended"), "old callbacks lose real claim authority");
            Equal(owner.StartsWith("manual-adventure:", StringComparison.Ordinal) ? null : owner, G(MainObject, "_customEquipmentOwner"), "reset clears only stale manual support cache");
            Equal(at, G(MainObject, "_customEquipmentLastDiggersAt"), "handoff preserves support timing; removed owner identity, not resetting a timer, prevents stale credit");
            Equal(null, G(MainObject, "_inlineStageSelection"), "handoff fabricates no inline acquisition"); Equal("fixture-preset", G(MainObject, "_globalPresetManualId"), "handoff preserves current preset intent");
            Equal(true, G(Settings, "CustomAdventureEnabled"), "handoff preserves configured rules"); Equal(false, G(Settings, "ManualAdventureEnabled"), "handoff never enables legacy manual mode"); Check(ReferenceEquals(rules, G(Settings, "CustomAdventureRules")), "exact configured rule array retained");
            SamePitMap(gear, CapturePitGear(), "handoff preserves exact native gear"); Equal(support, G(M("CaptureSupportState"), "StableKey"), "handoff preserves native support"); Equal(resources, P("Resources"), "handoff leaves native pools/policy unchanged");
            Equal(swaps, G(typeof(Program), "Swaps"), "no historical gear action during no-successor handoff"); Equal(recalls, G(typeof(Program), "Recalls"), "no resource recall during handoff");
        }
    }

    static void CheckManualStageInvalidation()
    {
        foreach (var partial in new[] { false, true })
        {
            Setup(); var plan = StagePlan(); M("RunCustomPlanAutomation", plan); Equal(true, M("InlineStageSetupVerified", plan), "stage starts verified before manual actuator");
            var selected = G(MainObject, "_inlineStageSelection"); var baseline = G(selected, "Baseline"); var scheduler = G(MainObject, "_protectedScheduler"); var entry = G(MainObject, "_customStageKey");
            var inv = G(Character, "inventory"); var head = NewGear(60, "Head"); var chest = NewGear(61, "Chest");
            foreach (var item in new[] { head, chest }) { P("SetEnum", item, "spec1Type", "GoldDropAmount"); S(item, "spec1Cur", 1e9f); }
            ((IList)G(inv, "inventory"))[0] = head; ((IList)G(inv, "inventory"))[1] = chest;
            if (partial) S(typeof(Program), "FailRefreshAtSwap", (int)G(typeof(Program), "Swaps") + 1);
            var error = Failure(() => M("ApplyManualGearSelectionIngress", Enum.Parse(Manager("GearOptimizationGoal"), "Gold"), Enum.Parse(Manager("FightScoreMode"), "SqrtPowerToughness")));
            Equal(partial, error != null, "actual manual actuator surfaces only genuine partial failure"); Check(ReferenceEquals(head, G(inv, "head")), "manual native application really changes gear");
            Equal(false, M("InlineStageSetupVerified", plan), "manual native success/failure invalidates old stage verification"); Equal(null, G(selected, "Applied"), "manual actuator discards applied evidence");
            Check(ReferenceEquals(baseline, G(selected, "Baseline")), "manual actuator retains acquired stage intent"); Equal(entry, G(MainObject, "_customStageKey"), "manual actuator does not renew entry");
            Check(ReferenceEquals(scheduler, G(MainObject, "_protectedScheduler")), "manual actuator preserves scheduler object/history"); Equal(false, G(scheduler, "_armed"), "manual actuator withholds protected credit"); Equal(true, G(Settings, "UseCustomRebirthPlan"), "manual actuator does not persist an implicit plan pause");
            var ready = new object[] { plan, null, null }; Equal(false, Invoke(MainObject, "CustomStageReadyForQuickRefresh", ready), "quick cannot borrow stale applied stage receipt");
            M("RunCustomPlanAutomation", plan); Equal(true, M("InlineStageSetupVerified", plan), "normal coordinator reconciles retained current stage after manual success/known partial");
            Check(ReferenceEquals(selected, G(MainObject, "_inlineStageSelection")), "normal continuation never reselects stage acquisition");
        }
    }

    static void CheckSelectedFailureCapture()
    {
        foreach (var fault in new[] { "scene", "epoch", "support" })
        {
            Setup(); var inv = G(Character, "inventory"); var before = CapturePitGear(); var support = G(M("CaptureSupportState"), "StableKey");
            var owner = fault == "scene" ? G(Character, "inventoryController") : fault == "epoch" ? Character : G(Character, "allDiggers");
            var field = fault == "scene" ? "itemInfo" : fault == "epoch" ? "stats" : "baseGPSDrain"; var value = G(owner, field);
            var swaps = G(typeof(Program), "Swaps"); var recalls = G(typeof(Program), "Recalls");
            ((IList)G(inv, "inventory"))[0] = NewGear(60, "Head"); Setting("YggdrasilLoadout", new[] { 60 });
            S(owner, field, null); var failure = Failure(() => M("PrepareHarvestAction")); S(owner, field, value);
            Check(failure != null, "actual " + fault + " observation failure withholds setup"); SamePitMap(before, CapturePitGear(), "failed capture preserves native gear");
            Equal(support, G(M("CaptureSupportState"), "StableKey"), "failed capture preserves unrelated support"); Equal(swaps, G(typeof(Program), "Swaps"), "failed capture has no gear effects");
            Equal(recalls, G(typeof(Program), "Recalls"), "failed capture has no resource effects"); Equal(false, M("ExecutionOwnsDomain", Domain("Gear, Support")), "failed capture leaks no admitted claim");
            Equal(null, G(MainObject, "_inlineStageSelection"), "failed capture invents no stage intent");
        }
    }

    static void CheckExactMaps()
    {
        Setup(); var inv = G(Character, "inventory"); var accs = (IList)G(inv, "accs"); accs[0] = NewGear(0, "Accessory");
        var original = CapturePitGear(); Equal(7, original.Length, "exact map covers unlocked topology including empty slot");
        var wanted = WithGear(Helper(accs[1], 10000)); Equal(0, G(At(wanted, 10001), "id"), "moving sparse accessory clears old destination");
        var claim = BeginClaim("sparse"); ApplyClaim(claim, wanted); SamePitMap(wanted, CapturePitGear(), "real claim applies exact sparse map");
        Publish(original, "explicit-current-empty"); Equal(true, M("EndExecutionClaim", claim, true), "CURRENT explicit empty destination applies"); SamePitMap(original, CapturePitGear(), "current empty target, not historical restore");
        var copy = NewGear((int)G(accs[1], "id"), "Accessory"); var copies = WithGear(Helper(copy, 10000)); Equal(0, G(At(copies, 10001), "id"), "same-ID selected copy clears conflicting omitted destination");
        var retained = WithGear(Helper(NewGear(70, "Accessory"), 10000)); Check(ReferenceEquals(accs[1], G(At(retained, 10001), "equipment")), "unrelated legal occupant retained in composition");
        var bag = (IList)G(inv, "inventory"); for (var i = 0; i < bag.Count; i++) bag[i] = NewGear(80 + i, "Accessory");
        var empty = WithGear(Helper(NewGear(0, "Accessory"), 10001)); var current = CapturePitGear(); var swaps = G(typeof(Program), "Swaps");
        var blocked = BeginClaim("full-map"); Reject(() => ApplyClaim(blocked, empty), "full inventory rejects explicit empty target");
        SamePitMap(current, CapturePitGear(), "failed empty preflight has no prefix"); Equal(swaps, G(typeof(Program), "Swaps"), "full map rejection is read-only"); M("EndExecutionClaim", blocked, false);
        Setup(); var allEmpty = CapturePitGear(); foreach (var item in allEmpty) { S(item, "id", 0); S(item, "equipment", NewGear(0, "Accessory")); }
        claim = BeginClaim("controlled-empty"); ApplyClaim(claim, allEmpty); Equal(true, Invoke(Manager("LoadoutManager"), "ExactLoadoutMatches", allEmpty), "explicit complete empty map remains a valid controlled decision");
        M("EndExecutionClaim", claim, true); Equal(true, Invoke(Manager("LoadoutManager"), "ExactLoadoutMatches", allEmpty), "no successor preserves explicit empty map");
        Setup(); accs = (IList)G(G(Character, "inventory"), "accs"); P("SetEnum", accs[1], "spec1Type", "EnergyCap"); S(accs[1], "spec1Cur", 1e9f); P("RefreshBonuses");
        wanted = WithGear(Helper(accs[1], 10000)); swaps = G(typeof(Program), "Swaps"); var recalls = G(typeof(Program), "Recalls");
        var capacity = (string)Invoke(Manager("GearOptimizer"), "CheckExactSwapCapacity", wanted, 15000m, 0m);
        Check(capacity != null && capacity.Contains("intermediate"), "legal final capacity cannot hide unsafe staging"); Equal(swaps, G(typeof(Program), "Swaps"), "intermediate-capacity preflight never swaps"); Equal(recalls, G(typeof(Program), "Recalls"), "capacity preflight never recalls");
    }

    static void CheckNativeCapacityGuard()
    {
        Setup(); var inv = G(Character, "inventory"); var original = G(inv, "head"); P("SetEnum", original, "spec1Type", "EnergyCap"); S(original, "spec1Cur", 1e9f); P("RefreshBonuses");
        var replacement = NewGear(60, "Head"); ((IList)G(inv, "inventory"))[0] = replacement; var cap = (long)Invoke(Character, "totalCapEnergy");
        S(Character, "curEnergy", cap); S(Character, "idleEnergy", cap - 15000); ((long[])G(G(Character, "advancedTraining"), "energy"))[1] = 15000;
        S(inv, "item1", -1); S(inv, "item2", 0); Child(G(Character, "inventoryController"), "tooltip"); Invoke(G(Character, "inventoryController"), "OwnershipNativeSwapHead");
        Check(ReferenceEquals(original, G(inv, "head")), "actual native controller rejects lost capacity"); var recalls = G(typeof(Program), "Recalls");
        var failure = (string)Invoke(Manager("GearOptimizer"), "CheckExactSwapCapacity", WithGear(Helper(replacement, -1)), 15000m, 0m);
        Check(failure.Contains("retained allocations"), "shared read-only preflight agrees with native guard"); Equal(recalls, G(typeof(Program), "Recalls"), "native guard comparison never recalls");
    }

    static void ChangeEpoch(string change)
    {
        if (change == "counter") S(G(Character, "stats"), "rebirthNumber", 8L);
        if (change == "clock") S(G(Character, "rebirthTime"), "totalseconds", 10d);
        if (change == "origin") S(G(Character, "totalPlaytime"), "totalseconds", 2500d);
        if (change == "inventory")
        {
            var old = G(Character, "inventory"); var replacement = P("Blank", old.GetType());
            foreach (var field in old.GetType().GetFields(All).Where(x => !x.IsStatic)) field.SetValue(replacement, field.GetValue(old)); S(Character, "inventory", replacement);
        }
    }
    static void CheckEpochs()
    {
        foreach (var owner in new[] { "titan-gold", "titan-fight", "money-pit", "ygg-harvest" })
        foreach (var change in new[] { "counter", "clock", "inventory", "origin" }) Case(() => {
            Setup(); M("ObserveCustomRebirthEpoch"); var inv = G(Character, "inventory"); var temp = NewGear(60, "Head"); ((IList)G(inv, "inventory"))[0] = temp;
            Publish(CapturePitGear(), "epoch-A"); var claim = BeginClaim(owner); ApplyClaim(claim, WithGear(Helper(temp, -1)));
            var grant = M("ResolveExecutionGrant", G(claim, "Owner"), G(claim, "Acquisition"), Domain("Gear"), (Func<bool>)(() => true), claim, null);
            ChangeEpoch(change); var swaps = G(typeof(Program), "Swaps"); var recalls = G(typeof(Program), "Recalls");
            Equal(false, M("ValidateExecutionGrant", grant, Domain("Gear")), "native epoch change revokes callback " + owner + "/" + change);
            M("ObserveCustomRebirthEpoch"); Equal(true, G(claim, "Ended"), "epoch observer retires old authority"); Equal(false, M("ExecutionOwnsDomain", Domain("Gear, Support")), "epoch leaves no orphan claim");
            Check(ReferenceEquals(temp, G(G(Character, "inventory"), "head")), "epoch never applies old-save A"); Equal(swaps, G(typeof(Program), "Swaps"), "epoch observation never swaps"); Equal(recalls, G(typeof(Program), "Recalls"), "epoch observation never recalls");
        });
    }

    static void CheckUnload()
    {
        Setup(); var inv = G(Character, "inventory"); var temporary = NewGear(60, "Head"); ((IList)G(inv, "inventory"))[0] = temporary;
        var claim = BeginClaim("unload-temporary"); ApplyClaim(claim, WithGear(Helper(temporary, -1)));
        var successor = NewGear(61, "Head"); ((IList)G(inv, "inventory"))[1] = successor; Publish(WithGear(Helper(successor, -1)));
        var grant = M("ResolveExecutionGrant", G(claim, "Owner"), G(claim, "Acquisition"), Domain("Gear"), (Func<bool>)(() => true), claim, null);
        var swaps = G(typeof(Program), "Swaps"); var main = MainObject; var settings = Settings; Setting("ManualAdventureEnabled", true); S(MainType, "ExternalUiEnabled", false);
        S(G(Character, "bossController"), "isFighting", true);
        Reject(() => M("Unload"), "unknown active native boss delays lifecycle retirement"); Equal(false, G(main, "_unloaded"), "blocked unload retains component");
        Check(ReferenceEquals(main, G(MainType, "reference")), "blocked unload retains coordinator"); Equal(false, M("ValidateExecutionGrant", grant, Domain("Gear")), "quiescing revokes old ordinary callback");
        S(G(Character, "bossController"), "isFighting", false); M("Unload"); Equal(true, G(main, "_unloaded"), "native retirement permits unload");
        Equal(null, G(MainType, "reference"), "unload clears injector reference"); Check(ReferenceEquals(temporary, G(inv, "head")), "quiescing preserves native gear rather than applying historical/current setup");
        Equal(swaps, G(typeof(Program), "Swaps"), "lifecycle does not run ordinary successor gear"); Equal(true, G(settings, "ManualAdventureEnabled"), "unload preserves configured manual policy");
        Invoke(main, "Unload"); Equal(swaps, G(typeof(Program), "Swaps"), "unload is idempotent");
    }
}
