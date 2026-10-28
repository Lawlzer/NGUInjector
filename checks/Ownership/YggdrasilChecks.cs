using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

public static partial class Program
{
    private static void ConfigureLiveResourceFixture(AssemblyDefinition game) { OwnershipChecks.ConfigureFruitScene(game.MainModule); }
}

public static partial class OwnershipChecks
{
    static Action FruitRendered, FruitTooltipAction;
    static int FruitId, FruitRenders;
    static Exception NativeFruitError;
    static readonly List<string> FruitEntries = new List<string>();
    static object YggController => G(Character, "yggdrasilController");
    static IList YggFruits => (IList)G(G(Character, "yggdrasil"), "fruits");
    static object YggManager => G(MainObject, "_yggManager");

    internal static void ConfigureFruitScene(ModuleDefinition module)
    {
        var fruit = module.GetType("FruitController");
        GameBoundary(module, fruit.Methods.Single(x => x.Name == "updateFruitDisplay"), nameof(FruitPresentation));
        Constant(fruit.Methods.Single(x => x.Name == "updateFruitSlider"), 0);
        foreach (var method in module.GetType("HoverTooltip").Methods.Where(x => x.HasBody && (x.Name == "showTooltip" || x.Name == "showOverrideTooltip")))
            GameBoundary(module, method, nameof(FruitTooltip));
        foreach (var method in fruit.Methods.Where(x => new[] { "consumeFruit", "harvest", "activate" }.Contains(x.Name) && x.Parameters.Count == 1).ToArray())
        {
            var il = method.Body.GetILProcessor(); var first = method.Body.Instructions[0];
            il.InsertBefore(first, il.Create(OpCodes.Ldstr, method.Name)); il.InsertBefore(first, il.Create(OpCodes.Ldarg_1));
            il.InsertBefore(first, il.Create(OpCodes.Call, module.ImportReference(typeof(OwnershipChecks).GetMethod(nameof(FruitInvoked), All))));
            var wrapper = new MethodDefinition(method.Name, method.Attributes, module.TypeSystem.Void); method.Name = "OwnershipNative" + method.Name;
            wrapper.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32)); fruit.Methods.Add(wrapper);
            var body = wrapper.Body.GetILProcessor(); var start = body.Create(OpCodes.Ldarg_0); var end = body.Create(OpCodes.Ret);
            var handler = body.Create(OpCodes.Call, module.ImportReference(typeof(OwnershipChecks).GetMethod(nameof(FruitFailed), All)));
            body.Append(start); body.Emit(OpCodes.Ldarg_1); body.Emit(OpCodes.Call, method); body.Emit(OpCodes.Leave, end); body.Append(handler); body.Emit(OpCodes.Rethrow); body.Append(end);
            wrapper.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch) { TryStart = start, TryEnd = handler, HandlerStart = handler, HandlerEnd = end, CatchType = module.ImportReference(typeof(Exception)) });
        }
        Constant(module.GetType("AllYggdrasil").Methods.Single(x => x.Name == "updateBonusTexts"), 0);
    }
    public static void FruitInvoked(string operation, int id) { FruitId = id; FruitEntries.Add(operation + ":" + id); }
    public static void FruitFailed(Exception error) { NativeFruitError = error; }
    public static void FruitPresentation() { FruitRenders++; FruitRendered?.Invoke(); }
    public static void FruitTooltip() { FruitTooltipAction?.Invoke(); }

    static void SetupYgg(bool ripe = true, int page = 2)
    {
        Setup(); ConfigurePitOrdinaryLoop();
        foreach (var name in new[] { "ManageYggdrasil", "ActivateFruits", "SwapYggdrasilLoadouts", "ManageEnergy", "ManageMagic" }) Setting(name, false);
        S(G(G(Character, "buttons"), "yggdrasil"), "m_Interactable", true); S(YggController, "character", Character);
        P("ListField", G(Character, "yggdrasil"), "fruits", 21);
        S(YggController, "activationCost", Enumerable.Repeat(100L, 21).ToList()); S(YggController, "usesEnergy", Enumerable.Repeat(true, 21).ToList());
        S(YggController, "baseSeedReward", Enumerable.Repeat(1L, 21).ToList());
        P("ListField", Child(Character, "beastQuest"), "quirkLevel", 100);
        var adventure = G(Character, "adventure"); P("ListField", Child(adventure, "itopod"), "perkLevel", 200);
        foreach (var name in new[] { "itopod" }) S(G(G(Character, "adventureController"), name), "character", Character);
        S(G(Character, "beastQuestPerkController"), "character", Character); S(G(Character, "NGUController"), "character", Character);
        P("ListField", G(G(Character, "adventureController"), "itopod"), "effectPerLevel", 200);
        P("ListField", G(Character, "beastQuestPerkController"), "effectPerLevel", 100);
        P("ListField", G(Character, "NGUController"), "normalMagicBoostFactor", 7);
        var slots = (Array)P("ArrayField", YggController, "fruits", 9); foreach (var slot in slots) { S(slot, "character", Character); Child(slot, "tooltip"); }
        Child(Child(Character, "tooltip"), "log"); S(MainObject, "_yggManager", Activator.CreateInstance(Manager("YggdrasilManager"), true));
        foreach (var id in new[] { 0, 4, 9, 20, 1, 10, 19 })
        { S(YggFruits[id], "maxTier", 4L); S(YggFruits[id], "activated", true); S(YggFruits[id], "seconds", 7200f); S(YggFruits[id], "eatFruit", id == 4); }
        if (ripe) foreach (var id in new[] { 0, 4, 9, 20 }) S(YggFruits[id], "seconds", 14400f);
        S(YggController, "curPage", page); FruitEntries.Clear(); FruitRenders = 0; FruitRendered = FruitTooltipAction = null; NativeFruitError = null;
    }

    static void ConfigureYggSwap()
    {
        Setting("SwapYggdrasilLoadouts", true); Setting("YggdrasilLoadout", new[] { 60 }); Setting("YggSwapThreshold", 1);
        ((IList)G(G(Character, "inventory"), "inventory"))[0] = NewGear(60, "Head");
    }

    static void AssertYggHarvested(int page)
    {
        Equal("harvest:0,consumeFruit:4,harvest:9,harvest:20", string.Join(",", FruitEntries), "identified native leaves preserve eat/harvest choice over all three pages");
        Equal(page, G(YggController, "curPage"), "identified harvest preserves current UI page without page traversal");
        foreach (var id in new[] { 0, 4, 9, 20 }) { Equal(0f, G(YggFruits[id], "seconds"), "native fruit deactivated " + id + ": " + NativeFruitError); Equal(1, G(YggFruits[id], "harvests"), "native reward commits once " + id); }
        foreach (var id in new[] { 1, 10, 19 }) { Equal(7200f, G(YggFruits[id], "seconds"), "immature growth retained " + id); Equal(true, G(YggFruits[id], "activated"), "immature activation retained " + id); Equal(0, G(YggFruits[id], "harvests"), "immature fruit not consumed " + id); }
        Equal(0, G(YggFruits[2], "harvests"), "locked fruit not consumed"); Equal(64L, G(G(Character, "yggdrasil"), "seeds"), "actual native harvest/consume formulas grant exact seeds");
    }

    static void CheckYggdrasilRegressions()
    {
        foreach (var page in new[] { 0, 1, 2 }) Case(() => {
            SetupYgg(page: page); Equal(true, Invoke(YggManager, "NeedsHarvest"), "native max-growth predicate detects ripe fruit");
            Invoke(YggManager, "ManageYggHarvest", true); AssertYggHarvested(page); Equal(false, Invoke(YggManager, "NeedsHarvest"), "native max predicate clears after real consumption");
            var renders = FruitRenders; Invoke(YggManager, "ManageYggHarvest", true); Equal(4, FruitEntries.Count, "second pass invokes no reward leaf"); Equal(renders, FruitRenders, "no redundant scene refresh");
        });
        foreach (var swap in new[] { false, true }) Case(() => {
            SetupYgg(false); if (swap) ConfigureYggSwap(); var gear = CapturePitGear(); var swaps = G(typeof(Program), "Swaps");
            Equal(false, Invoke(YggManager, "NeedsHarvest"), "immature/locked fruit never requests harvest"); Invoke(YggManager, "ManageYggHarvest", true);
            Equal(0, FruitEntries.Count, "no-max pass invokes no leaf"); Equal(swaps, G(typeof(Program), "Swaps"), "no-max pass never selects/applies gear"); SamePitMap(gear, CapturePitGear(), "no-max pass preserves gear");
        });
        foreach (var successor in new[] { false, true })
        foreach (var failure in new[] { "none", "render", "unknown" }) Case(() => {
            SetupYgg(); ConfigureYggSwap(); var inv = G(Character, "inventory"); Publish(CapturePitGear(), "Ygg-A"); var b = NewGear(61, "Head"); ((IList)G(inv, "inventory"))[1] = b;
            Action publish = () => { if (successor) Publish(WithGear(Helper(b, -1)), "Ygg-current-B"); else M("PublishExecutionBaseline", (object)null); };
            var callbacks = 0;
            FruitRendered = () => { if (callbacks++ != 0) return; Equal(60, G(G(inv, "head"), "id"), "native fruit reward runs in actually claimed setup"); publish(); if (failure == "render") throw new InvalidOperationException("fruit model committed; renderer failed"); };
            FruitTooltipAction = () => { if (failure != "unknown" || FruitId != 0) return; publish(); throw new InvalidOperationException("native harvest granted seeds before tooltip failed"); };
            try { Invoke(YggManager, "ManageYggHarvest", true); } finally { FruitRendered = FruitTooltipAction = null; }
            Equal(successor ? 61 : 60, G(G(inv, "head"), "id"), "harvest exits directly to CURRENT B or preserves temporary native gear");
            Equal(false, M("ExecutionOwnsDomain", Domain("Gear, Support")), "harvest retires real setup claim");
            if (failure == "unknown")
            {
                Equal(0, G(YggFruits[0], "harvests"), "tooltip interruption genuinely precedes native harvest counter");
                Equal(0f, G(YggFruits[0], "seconds"), "native harvest clears its ripe timer before the interrupted tooltip");
                var seeds = (long)G(G(Character, "yggdrasil"), "seeds"); Invoke(YggManager, "ManageYggHarvest", true);
                Equal(seeds, G(G(Character, "yggdrasil"), "seeds"), "next pass never reconsumes a fruit whose native timer was cleared");
                Equal(1, FruitEntries.Count(x => x == "harvest:0"), "native current state, not an UNKNOWN journal, withholds consumed fruit");
                S(YggFruits[0], "activated", true); S(YggFruits[0], "seconds", 14400f);
                var controller = ((Array)G(YggController, "fruits")).GetValue(0);
                var tier = (int)Invoke(controller, "harvestTier", 0);
                var reward = (long)Invoke(controller, "harvestSeedReward", 0, (int)Invoke(controller, "tierFactor", tier), 1f);
                Invoke(YggManager, "ManageYggHarvest", true);
                Equal(seeds + reward, G(G(Character, "yggdrasil"), "seeds"), "a genuinely ripe later fruit grants only its exact current native reward");
                Equal(2, FruitEntries.Count(x => x == "harvest:0"), "a fresh ripe opportunity is not permanently banned by the prior unknown result");
                Equal(1, G(YggFruits[0], "harvests"), "successful fresh harvest advances the actual native counter once");
                Equal(0f, G(YggFruits[0], "seconds"), "successful fresh harvest consumes the ripe native opportunity");
                Invoke(YggManager, "ManageYggHarvest", true);
                Equal(2, FruitEntries.Count(x => x == "harvest:0"), "consumed native opportunity has no third invocation");
            }
            else Equal(1, G(YggFruits[0], "harvests"), "committed render failure remains one native harvest");
        });
        Case(() => {
            SetupYgg(); RunPitLoop(); AssertYggHarvested(2); Equal(false, G(Settings, "ManageYggdrasil"), "ordinary harvest does not enable legacy management"); Equal(false, G(Settings, "ActivateFruits"), "ordinary harvest does not enable activation");
        });
        Case(() => { SetupYgg(); S(G(G(Character, "buttons"), "yggdrasil"), "m_Interactable", false); RunPitLoop(); Equal(0, FruitEntries.Count, "inaccessible fruit has no native effects"); Equal(14400f, G(YggFruits[20], "seconds"), "inaccessible ripe fruit preserved"); });
        foreach (var domain in new[] { "Gear", "Support" }) Case(() => {
            SetupYgg(); ConfigureYggSwap(); var foreign = BeginClaim("foreign-" + domain, domain); var gear = CapturePitGear(); var support = G(M("CaptureSupportState"), "StableKey");
            Reject(() => Invoke(YggManager, "ManageYggHarvest", true), "overlapping real claim blocks configured harvest"); Equal(0, FruitEntries.Count, "blocked setup invokes no fruit effect");
            SamePitMap(gear, CapturePitGear(), "failed takeover preserves exact foreign setup"); Equal(support, G(M("CaptureSupportState"), "StableKey"), "failed takeover preserves support"); Equal(false, G(foreign, "Ended"), "failed takeover cannot end foreign claim");
            M("EndExecutionClaim", foreign, false); Invoke(YggManager, "ManageYggHarvest", true); AssertYggHarvested(2);
        });
        foreach (var configured in new[] { false, true }) Case(() => CheckYggPrepPreservation(configured));
        foreach (var fault in new[] { "none", "render", "unknown" }) Case(() => CheckPreRebirthFruitDeferral(fault));
        foreach (var fault in new[] { "render", "partial", "foreign" }) Case(() => CheckFruitCapFailure(fault));
    }

    static void CheckPreRebirthFruitDeferral(string fault)
    {
        SetupYgg(false); ConfigureYggSwap(); Setting("ManageYggdrasil", true);
        foreach (var fruit in YggFruits) { S(fruit, "maxTier", 0L); S(fruit, "seconds", 0f); }
        S(YggFruits[0], "maxTier", 4L); S(YggFruits[0], "seconds", 3600f);
        var baseline = CapturePitGear(); Publish(baseline, "pre-rebirth-current");
        var rebirth = Invoke(Plugin.GetType("NGUInjector.AllocationProfiles.RebirthStuff.BaseRebirth", true), "CreateRebirth", 1d, "TIME", new string[0]);
        FruitRendered = () => { if (fault == "render") throw new InvalidOperationException("pre-rebirth harvest render failed after native commitment"); };
        FruitTooltipAction = () => { if (fault == "unknown") throw new InvalidOperationException("pre-rebirth harvest partial reward"); };
        try { Equal(true, Invoke(rebirth, "PreRebirth"), "actual pre-rebirth coordinator defers one pass for fruit effects"); }
        finally { FruitRendered = FruitTooltipAction = null; }
        Equal(1, FruitEntries.Count(x => x == "harvest:0"), "pre-rebirth uses one identified native fruit leaf");
        SamePitMap(baseline, CapturePitGear(), "pre-rebirth cleanup applies authorized CURRENT baseline, not an implicit snapshot"); Equal(false, M("ExecutionOwnsDomain", Domain("Gear, Support")), "pre-rebirth releases acquired setup on committed/unknown action result");
        Equal(false, Invoke(rebirth, "PreRebirth"), "next pass sees no harvestable native fruit despite an interrupted result");
        Equal(1, FruitEntries.Count, "cleared native timer prevents another harvest even when the counter was not reached");
        Equal(fault == "unknown" ? 0 : 1, G(YggFruits[0], "harvests"), "readiness never fabricates a native counter after interrupted presentation");
        Equal(0f, G(YggFruits[0], "seconds"), "current native timer proves no fruit work remains");
        Equal(false, Invoke(rebirth, "PreRebirth"), "consumed native fruit no longer blocks rebirth on an obsolete unknown history");
        Equal(1, FruitEntries.Count, "readiness after consumption invokes no extra harvest");
        Equal(7L, G(G(Character, "stats"), "rebirthNumber"), "deferral never fabricates native rebirth engagement");
    }

    static void CheckFruitCapFailure(string fault)
    {
        SetupYgg(false); Setting("ManageEnergy", true); Setting("ManageMagic", true);
        foreach (var fruit in YggFruits) { S(fruit, "maxTier", 0L); S(fruit, "seconds", 0f); }
        S(YggFruits[0], "maxTier", 4L); S(YggFruits[0], "activated", false);
        var plan = StagePlan(); var stage = ((Array)G(plan, "Stages")).GetValue(0); S(stage, "GearGoal", 7); M("RunCustomPlanAutomation", plan);
        Equal(true, M("InlineStageSetupVerified", plan), "fruit-cap timeline starts from real verified stage");
        var baseline = CapturePitGear(); var inv = G(Character, "inventory"); var head = NewGear(60, "Head"); var chest = NewGear(61, "Chest");
        foreach (var item in new[] { head, chest }) { P("SetEnum", item, "spec1Type", "EnergyCap"); S(item, "spec1Cur", 10000f); }
        var bag = (IList)G(inv, "inventory"); bag[0] = head; bag[1] = chest; Invoke(Manager("GearOptimizer"), "ClearCache");
        if (fault == "render") FruitRendered = () => { throw new InvalidOperationException("native activation committed before renderer failed"); };
        if (fault == "partial") S(typeof(Program), "FailRefreshAtSwap", (int)G(typeof(Program), "Swaps") + 1);
        if (fault == "foreign") Program.NativeGearSwapped = _ => { Program.NativeGearSwapped = null; S(inv, "head", NewGear(72, "Head")); };
        object count = null; var error = Failure(() => count = M("ActivateCustomFruitsWithCapGear", Enum.Parse(Manager("GearOptimizationGoal"), "EnergyCap"), Enum.Parse(Manager("FightScoreMode"), "SqrtPowerToughness")));
        FruitRendered = null; Program.NativeGearSwapped = null;
        if (fault == "render")
        { Check(error == null, "verified native activation classifies render fault without inventing rollback: " + error); Equal(1, count, "actual native activation counted once"); Equal(true, G(YggFruits[0], "activated"), "activation really committed"); }
        else { Check(error != null, "failed cap setup blocks dependent native activation"); Equal(false, G(YggFruits[0], "activated"), "partial/foreign setup spends no activation cost"); }
        Equal(false, M("ExecutionOwnsDomain", Domain("Gear, Support")), "fruit finally retires temporary setup authority");
        if (fault == "foreign")
        {
            Equal(72, G(G(inv, "head"), "id"), "fruit finally cannot overwrite foreign gear with old stage"); Equal(false, M("InlineStageSetupVerified", plan), "foreign cap outcome yields stage credit");
            var ready = new object[] { plan, null, null }; Equal(false, Invoke(MainObject, "CustomStageReadyForQuickRefresh", ready), "foreign state cannot reuse old quick receipt");
        }
        else
        {
            SamePitMap(baseline, CapturePitGear(), "fruit finally resolves authorized CURRENT stage selection");
            M("RunCustomPlanAutomation", plan); Equal(true, M("InlineStageSetupVerified", plan), "normal stage re-verifies after cap model/render/known-partial result");
        }
    }

    static void CheckYggPrepPreservation(bool configured)
    {
        SetupYgg(); if (configured) ConfigureYggSwap(); var diggers = (IList)G(G(Character, "diggers"), "diggers");
        for (var i = 0; i < diggers.Count; i++) { S(diggers[i], "curLevel", (long)i + 1); S(diggers[i], "maxLevel", 100L); }
        var beards = (IList)G(G(Character, "beards"), "beards");
        for (var i = 0; i < beards.Count; i++) { S(beards[i], "beardLevel", 10L + i); S(beards[i], "permLevel", 20L + i); S(beards[i], "bankedLevel", 30L + i); }
        var support = G(M("CaptureSupportState"), "StableKey"); var gear = CapturePitGear(); var swaps = G(typeof(Program), "Swaps"); var recalls = G(typeof(Program), "Recalls");
        S(diggers[2], "active", true); ((IList)G(G(Character, "diggers"), "activeDiggers")).Add(2);
        S(beards[1], "active", true); ((IList)G(G(Character, "beards"), "activeBeards")).Add(1);
        support = G(M("CaptureSupportState"), "StableKey");
        // Only seed retained process observations. The ordinary loop and native
        // authority decide whether harvesting may change its prerequisites.
        var capture = Activator.CreateInstance(MainType.GetNestedType("CombatPrepCapture", All), true);
        S(capture, "Wandoos", G(Character, "wandoos98")); S(capture, "Augments", G(Character, "augments")); S(capture, "Training", G(Character, "advancedTraining"));
        S(capture, "Rebirth", 7L); S(capture, "Inventory", G(Character, "inventory")); S(capture, "InventoryController", G(Character, "inventoryController"));
        S(capture, "Origin", 1000d); S(capture, "Elapsed", 500d); S(capture, "Total", 1500d); S(capture, "SupportObjects", M("SupportObjects"));
        var run = Activator.CreateInstance(MainType.GetNestedType("CombatPrepRun", All), true); S(run, "Status", "running"); S(run, "Capture", capture);
        S(run, "ExpectedSupport", M("CaptureSupportState")); S(run, "ExpectedSupportObjects", M("SupportObjects")); S(MainObject, "_combatPrepRun", run);
        S(run, "Id", "retained-preparation"); S(run, "Started", (double)G(MainType, "CombatPrepClock")); S(run, "Duration", 1000000d); S(run, "LastRefresh", double.PositiveInfinity);
        S(capture, "Snapshot", Activator.CreateInstance(Manager("CombatPrepSnapshot"), true));
        M("PauseCombatPrepNativeNuke", run, true);
        Equal(true, G(MainObject, "CombatPrepActive"), "real prep predicate is active before ordinary harvesting"); Equal(true, M("CombatPrepSupportStillOwned", run), "retained prep support truly matches native model");
        RunPitLoop(); AssertYggHarvested(2);
        Equal(true, G(MainObject, "CombatPrepActive"), "harvest cannot retire active preparation"); Equal(true, M("CombatPrepSupportStillOwned", run), "actual prep ownership survives configured/unconfigured harvest");
        SamePitMap(gear, CapturePitGear(), "prep-safe harvest preserves every equipped item"); Equal(support, G(M("CaptureSupportState"), "StableKey"), "prep-safe harvest preserves all inactive digger levels");
        Equal(swaps, G(typeof(Program), "Swaps"), "prep-safe harvest never temporarily swaps"); Equal(recalls, G(typeof(Program), "Recalls"), "prep-safe harvest never recalls");
        for (var i = 0; i < diggers.Count; i++) Equal(100L, G(diggers[i], "maxLevel"), "harvest preserves purchased digger bound " + i);
        for (var i = 0; i < beards.Count; i++) { Equal(10L + i, G(beards[i], "beardLevel"), "temporary beard progression preserved"); Equal(20L + i, G(beards[i], "permLevel"), "permanent beard progression preserved"); Equal(30L + i, G(beards[i], "bankedLevel"), "banked beard progression preserved"); }
    }

    static void CheckYggRestorationRecovery()
    {
        foreach (var fault in new[] { "full", "partial", "unknown", "foreign", "permission" }) Case(() => {
            SetupYgg(); ConfigureYggSwap(); var inv = G(Character, "inventory"); var bag = (IList)G(inv, "inventory");
            ((IList)G(inv, "accs"))[0] = NewGear(0, "Accessory"); bag[1] = NewGear(70, "Accessory"); Setting("YggdrasilLoadout", new[] { 60, 70 });
            Equal(true, M("PrepareHarvestAction"), "actual finite harvest setup verifies"); var claim = G(MainObject, "_harvestActionClaim"); var acquired = G(claim, "Acquisition");
            var b = NewGear(61, "Head"); bag[2] = b; var target = WithGear(Helper(b, -1), Helper(NewGear(0, "Accessory"), 10000));
            var allowed = true; Publish(target, "harvest-current-B", () => allowed); var swaps = (int)G(typeof(Program), "Swaps");
            if (fault == "full") for (var i = 0; i < bag.Count; i++) if ((int)G(bag[i], "id") == 0) bag[i] = NewGear(90 + i, "Accessory");
            if (fault == "partial") S(typeof(Program), "FailRefreshAtSwap", swaps + 1);
            if (fault == "unknown") Program.NativeGearSwapped = _ => { Program.NativeGearSwapped = null; S(inv, "inventory", null); };
            if (fault == "foreign") S(inv, "head", NewGear(72, "Head"));
            if (fault == "permission") Program.NativeGearSwapped = _ => { Program.NativeGearSwapped = null; allowed = false; };
            object result = null; var error = Failure(() => result = M("EndExecutionClaim", claim, true)); Program.NativeGearSwapped = null; S(inv, "inventory", bag);
            Check(error != null || Equals(result, false), "unverified CURRENT successor never reports successful release: " + fault);
            Equal(true, G(claim, "Ended"), "exit revokes temporary authority before successor resolution"); Equal(acquired, G(claim, "Acquisition"), "exit cannot reacquire/rebudget old process");
            if (fault == "full") { Equal(swaps, G(typeof(Program), "Swaps"), "full inventory preflight applies no prefix"); bag[9] = NewGear(0, "Accessory"); }
            if (fault == "full" || fault == "partial")
            { Equal(true, M("EndExecutionClaim", claim, true), "same CURRENT successor retries safe incomplete transition"); SamePitMap(target, CapturePitGear(), "recovery finishes CURRENT B, never old A"); }
            else
            {
                var current = CapturePitGear(); var held = G(typeof(Program), "Swaps"); Failure(() => M("EndExecutionClaim", claim, true));
                Equal(held, G(typeof(Program), "Swaps"), "foreign/unknown/withdrawn successor never replays native mutation"); SamePitMap(current, CapturePitGear(), "foreign/unknown native setup preserved");
            }
            Equal(0, FruitEntries.Count, "setup/release recovery cannot invoke a fruit reward");
        });
    }

    static void CheckFruitResourceLanes()
    {
        foreach (var energy in new[] { true, false })
        foreach (var deficit in new[] { false, true })
        foreach (var policy in new[] { "managed", "unmanaged", "missing-settings" }) Case(() => {
            SetupYgg(false); foreach (var fruit in YggFruits) S(fruit, "maxTier", 0L);
            var id = energy ? 0 : 9; var selected = YggFruits[id]; S(selected, "maxTier", 4L); S(selected, "activated", false); S(selected, "seconds", 0f);
            ((IList)G(YggController, "usesEnergy"))[id] = energy; Setting("ManageEnergy", policy == "managed" && energy); Setting("ManageMagic", policy == "managed" && !energy);
            var idle = deficit ? 75L : 1000L; var magic = G(Character, "magic"); var w = G(Character, "wandoos98"); var wishes = (IList)G(G(Character, "wishes"), "wishes"); var wish = wishes[wishes.Count - 1];
            S(Character, "curEnergy", idle + 500); S(Character, "idleEnergy", idle); S(magic, "curMagic", idle + 500); S(magic, "idleMagic", idle);
            S(w, "wandoosEnergy", 200L); S(w, "wandoosMagic", 200L); S(wish, "energy", 300L); S(wish, "magic", 300L);
            var settings = Settings; if (policy == "missing-settings") S(MainType, "Settings", null);
            object count = null; var error = Failure(() => count = Invoke(Manager("YggdrasilManager"), "ActivateAffordableFruits", false)); S(MainType, "Settings", settings);
            if (policy == "missing-settings") Check(error != null || Equals(count, 0), "missing settings fails closed before native activation");
            else { Check(error == null, "native activation has no unrelated scene failure: " + error); Equal(policy == "managed" ? 1 : 0, count, "only managed lane may activate"); }
            var managed = policy == "managed"; Equal(managed, G(selected, "activated"), "actual native activation state");
            foreach (var laneEnergy in new[] { true, false })
            {
                var spent = managed && laneEnergy == energy; var released = spent && deficit ? 25L : 0L; var owner = laneEnergy ? Character : magic;
                Equal(idle + released - (spent ? 100 : 0), G(owner, laneEnergy ? "idleEnergy" : "idleMagic"), "reservation releases only actual deficit, not historical whole-lane recall");
                Equal(idle + 500 - (spent ? 100 : 0), G(owner, laneEnergy ? "curEnergy" : "curMagic"), "native current pool decreases exactly by activation cost");
                Equal(200L - released, G(w, laneEnergy ? "wandoosEnergy" : "wandoosMagic"), "unneeded Wandoos work preserved"); Equal(300L, G(wish, laneEnergy ? "energy" : "magic"), "locked/non-visible wish preserved");
            }
            Equal(2, G(YggController, "curPage"), "activation leaves page untouched"); Equal(1000L, G(G(Character, "res3"), "idleRes3"), "activation leaves R3 untouched");
        });
    }

    static void ConfigurePitOrdinaryLoop()
    {
        foreach (var name in new[] { "UseCustomRebirthPlan", "CustomAdventureEnabled", "ManualAdventureEnabled", "ManualTitanEnabled", "CustomAutoBoostEnabled", "CustomAutoBloodSpellsEnabled", "AutoRebirth", "AutoMoneyPit", "GlobalEnabled", "CustomAutoMoneyPitEnabled" }) Setting(name, false);
        S(Settings, "_customAutoExpEnabled", false); S(MainObject, "_started", true); S(MainObject, "_timeLeft", -1f);
    }
    static void RunPitLoop()
    {
        using (var log = new System.IO.MemoryStream())
        using (var writer = new System.IO.StreamWriter(log) { AutoFlush = true })
        {
            S(MainType, "OutputWriter", writer); try { M("AutomationRoutine"); } finally { S(MainType, "OutputWriter", null); }
            var text = System.Text.Encoding.UTF8.GetString(log.ToArray()); Check(!text.Contains("ERROR"), "real ordinary loop reports no swallowed failure: " + text);
        }
        Equal(10f, G(MainObject, "_timeLeft"), "ordinary loop reaches normal reschedule boundary"); Check(G(MainObject, "_customObservedEpoch") != null, "ordinary loop observes actual epoch");
    }
}
