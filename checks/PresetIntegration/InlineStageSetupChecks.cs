using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public static partial class PresetIntegrationChecks
{
    private static int InlineGearSelections;
    private static Exception InlineObservedError;
    public static void CountInlineGearSelection() { InlineGearSelections++; }
    public static void ObserveInlineError(Exception error) { InlineObservedError = error; }

    private static void CheckInlineStageSetupRegressions()
    {
        var before = Assertions;
        CheckInlineStageSuccessor();
        CheckInlineStagePartialSupport();
        CheckInlineStageForeignSetup();
        CheckInlineStageUncontrolledGear();
        CheckInlineStagePermissionCallback();
        CheckInlineStageGearRenderFailure();
        CheckInlineStageScopedResourceContinuation();
        CheckInlineStageIndependentBeardCadence();
        CheckInlineStageInvalidationCannotAdoptForeign();
        CheckInlineStageCrossDomainCallbacks();
        CheckInlineStageAffordablePartialContinuation();
        CheckInlineStageOwnedMaterialRevision();
        CheckInlineStageFocusPauseResume();
        CheckInlineStageRenderingBeforeNewEvent();
        CheckInlineStageRejectedSupportAfterGear();
        CheckInlineStagePolicyEdits();
        Console.WriteLine("Inline stage setup: " + (Assertions - before) + " native assertions.");
    }

    private static object SetupInlineStagePlan(bool controlGear = true)
    {
        var plan = SetupCommitBudget();
        var first = ((Array)Get(plan, "Stages")).GetValue(0);
        Set(first, "PresetId", ""); Set(first, "CompletionMode", 0); Set(first, "DurationSeconds", 600);
        Set(first, "TargetAdvancedTrainingPower", 0L); Set(first, "TargetAdvancedTrainingToughness", 0L);
        Set(first, "EnergyPriorities", new[] { "NGU" }); Set(first, "MagicPriorities", new[] { "NGU" });
        var goals = Plugin.GetType("NGUInjector.Managers.GearOptimizationGoal", true);
        Set(first, "GearGoal", controlGear ? (int)Enum.Parse(goals, "EnergyCap") : -1);
        var second = Invoke(first, "MemberwiseClone");
        Set(second, "CompletionMode", 1); Set(second, "TargetAdvancedTrainingPower", 10L);
        Set(second, "TargetAdvancedTrainingToughness", 20L);
        Set(second, "EnergyPriorities", new[] { "AT", "NGU" });
        Set(second, "GearGoal", controlGear ? (int)Enum.Parse(goals, "MagicCap") : -1);
        var stages = Array.CreateInstance(first.GetType(), 2); stages.SetValue(first, 0); stages.SetValue(second, 1);
        Set(plan, "Stages", stages); Set(plan, "StageDurationSeconds", 7200);
        Set(plan, "TimeTriggerEnabled", false); Set(plan, "AutoActivateFruits", false);
        Set(plan, "AutoRebirthEnabled", false);
        var settings = Get(MainType, "Settings"); Set(settings, "_autoRebirth", false);
        Invoke(settings, "SetSaveDisabled", true); // This fixture exercises execution, not Unity settings serialization.
        Set(settings, "_manualAdventureEnabled", false); Set(settings, "_customAdventureEnabled", false);
        Set(settings, "_manualAdventureGearWeights", new double[5]);
        Set(settings, "_manualAdventureBeardWeights", new double[7]);
        Set(settings, "_manualAdventureDiggerWeights", new double[12]);
        Set(Get(Character, "advancedTraining"), "levelTarget", new long[5]);
        var inventory = Get(Character, "inventory"); var items = (IList)Get(inventory, "inventory");
        var oldHead = Get(inventory, "head");
        for (var i = 0; i < 3; i++)
        {
            Set(items[i], "id", 550 + i); Set(items[i], "type", Get(oldHead, "type"));
            Set(items[i], "bossRequired", 1); Set(items[i], "removable", true);
            Set(items[i], "curAttack", i == 2 ? 1000000f : 1f); Set(items[i], "curDefense", i == 2 ? 1000000f : 1f);
            Set(items[i], "spec1Type", Enum.Parse(Field(items[i], "spec1Type").FieldType,
                i == 0 ? "EnergyCap" : i == 1 ? "MagicCap" : "None"));
            Set(items[i], "spec1Cur", i == 2 ? 0f : 10000f); Set(items[i], "spec1Cap", 10000000f);
        }
        var diggers = (IList)Get(Get(Character, "diggers"), "diggers");
        foreach (var id in new[] { 2, 3, 4, 5 })
        {
            Set(diggers[id], "maxLevel", 4L);
            ((IList)Get(Get(Character, "allDiggers"), "baseGPSDrain"))[id] = 10d;
            ((IList)Get(Get(Character, "allDiggers"), "consumesGPS"))[id] = true;
        }
        Set(Get(Character, "wishesController"), "character", Character);
        ListField(Get(Character, "wishesController"), "properties", 512);
        ListField(Get(Character, "wishes"), "wishes", 512);
        ListField(Get(Character, "hacks") ?? Child(Character, "hacks"), "hacks", 16);
        var perks = Get(Get(Character, "adventureController"), "itopod");
        Set(perks, "character", Character);
        Sequence(Get(Get(Character, "adventure"), "itopod"), "perkLevel", 512, 0L);
        Sequence(perks, "effectPerLevel", 512, 0f); ListField(perks, "perkDifficultyReq", 512);
        var quirks = Get(Character, "beastQuestPerkController"); Set(quirks, "character", Character);
        Sequence(Child(Character, "beastQuest"), "quirkLevel", 512, 0L);
        Sequence(quirks, "effectPerLevel", 512, 0f); ListField(quirks, "quirkDifficultyReq", 512);
        var r3 = Get(Character, "res3"); Set(r3, "capRes3", 1000L);
        Set(r3, "curRes3", 731L); Set(r3, "idleRes3", 431L);
        Set(((IList)Get(Get(Character, "wishes"), "wishes"))[0], "res3", 300L);
        ((long[])Property(Get(Character, "training"), "attackEnergy"))[0] = 7;
        Set(Character, "idleEnergy", (long)Get(Character, "idleEnergy") - 7);
        var nativeW = Get(Character, "wandoos98");
        Set(nativeW, "energyLevel", 7L); Set(nativeW, "magicProgress", .25f);
        Invoke(Plugin.GetType("NGUInjector.Managers.GearOptimizer", true), "ClearCache");
        return plan;
    }

    private static void CheckActiveAutomaticNumberObservation()
    {
        var failures = new List<Exception>();
        foreach (var callback in new[] { "QuickStuff", "AutomationRoutine" })
        {
            try { CheckActiveAutomaticNumberObservation(callback); }
            catch (Exception error) { failures.Add(new Exception(callback, error)); }
        }
        if (failures.Count != 0) throw new AggregateException("Active automatic NUMBER observation", failures);
    }

    private static void CheckActiveAutomaticNumberObservation(string callback)
    {
        var plan = SetupInlineStagePlan();
        var first = ((Array)Get(plan, "Stages")).GetValue(0);
        Set(first, "ExecutionMode", 1); Set(first, "DurationSeconds", 120);
        var stages = Array.CreateInstance(first.GetType(), 1); stages.SetValue(first, 0);
        Set(plan, "Stages", stages); Set(plan, "StageLoopStartIndex", 0); Set(plan, "StageDurationSeconds", 86400);
        Set(plan, "AutoRebirthEnabled", true); Set(plan, "NumberTriggerEnabled", true); Set(plan, "NumberMultiplierTarget", 1.5d);
        Set(plan, "AutoIronPillEnabled", false);
        var settings = Get(MainType, "Settings"); Set(settings, "_autoRebirth", true);
        Set(settings, "_customAutoBoostEnabled", false); Set(settings, "_customAutoBloodSpellsEnabled", false);
        Set(Get(Get(Character, "buttons"), "yggdrasil"), "m_Interactable", false);
        var wandoos = Get(Character, "wandoos98");
        Set(wandoos, "energyLevel", 0L); Set(wandoos, "magicLevel", 0L);
        Set(wandoos, "energyProgress", 0f); Set(wandoos, "magicProgress", 0f);
        var rebirth = Plugin.GetType("NGUInjector.AllocationProfiles.RebirthStuff.CustomPlanRebirth", true);
        Invoke(rebirth, "ResetRuntimeState");
        var store = Get(MainType, "CustomPlanStore"); var name = (string)Property(settings, "CustomPlanFile");
        Invoke(store, "Save", name, plan); plan = Invoke(store, "Load", name);
        var attack = (double)Get(Character, "attackMulti"); Set(Character, "nextAttackMulti", attack * 2);
        CallMain("RunCustomPlanAutomation", plan);
        var run = Get(MainInstance, "_combatPrepRun");
        Check(run != null, "real automatic acquisition creates an executor: " + CallMain("GetCustomAutomationStatus"));
        Equal("running", Get(run, "Status"), "actual automatic executor starts successfully: " + Get(run, "Reason"));
        Equal(true, Property(run, "Automatic"), "NUMBER fixture uses a scheduled, not manual, executor");
        Equal(true, Get(run, "SetupVerified"), "active executor has actually verified its gear/support setup");
        Equal(true, Property(Get(run, "ResourceOutcome"), "Verified"), "active executor has actually applied its resource policy");
        var fundedEnergy = ((long[])Get(Get(Character, "advancedTraining"), "energy")).Sum()
            + (long)Get(wandoos, "wandoosEnergy")
            + ((IEnumerable)Get(Get(Character, "augments"), "augs")).Cast<object>().Sum(x => (long)Get(x, "augEnergy") + (long)Get(x, "upgradeEnergy"))
            + ((IList)Get(Get(Character, "NGU"), "skills")).Cast<object>().Sum(x => (long)Get(x, "energy"));
        var fundedMagic = (long)Get(wandoos, "wandoosMagic")
            + ((IList)Get(Get(Character, "NGU"), "magicSkills")).Cast<object>().Sum(x => (long)Get(x, "magic"));
        Check(fundedEnergy > 0 && fundedMagic > 0, "the real active automatic executor funds both native resource lanes");
        var scheduler = Get(MainInstance, "_protectedScheduler"); var entry = Property(scheduler, "Entry");
        var deadline = (double)Get(run, "DeadlineRebirthSeconds"); var origin = (double)Get(Get(Character, "rebirthTime"), "totalseconds");
        var generation = Get(MainInstance, "_protectedGeneration");
        Action<double, double, bool> observe = (offset, multiplier, expected) => {
            var advance = origin + offset - (double)Get(Get(Character, "rebirthTime"), "totalseconds");
            foreach (var clock in new[] { "rebirthTime", "totalPlaytime" })
            {
                var current = Get(Character, clock);
                Set(current, "totalseconds", (double)Get(current, "totalseconds") + advance);
            }
            Set(Character, "nextAttackMulti", attack * multiplier);
            CallMain(callback);
            Equal("running", Get(run, "Status"), "NUMBER observation services the real active executor/" + offset + ": " + Get(run, "Reason"));
            Check(ReferenceEquals(run, Get(MainInstance, "_combatPrepRun")), "NUMBER observation retains the acquired executor rather than reacquiring it/" + offset);
            Equal(true, Property(MainInstance, "CombatPrepActive"), "NUMBER observation occurs before the active-executor early return/" + offset);
            var policy = Activator.CreateInstance(rebirth, All, null, new[] { plan }, null);
            Equal(expected, Invoke(policy, "PeekTriggersAvailable"), "observed NUMBER confirmation respects drop/spike timing/" + offset);
            Equal(entry, Property(scheduler, "Entry"), "NUMBER observation never changes the active entry");
            Equal(deadline, Get(run, "DeadlineRebirthSeconds"), "NUMBER observation preserves the original deadline");
        };
        observe(0, 2, false);
        observe(16, 2, true);
        observe(20, 1.2, false); // A drop withdraws even an already-confirmed trigger.
        observe(21, 2, false);
        observe(31, 3, false); // Significant spike restarts the new confirmation window.
        observe(40, 3, false);
        observe(47, 3, true);
        var delta = deadline + 1 - (double)Get(Get(Character, "rebirthTime"), "totalseconds");
        foreach (var clock in new[] { "rebirthTime", "totalPlaytime" })
            Set(Get(Character, clock), "totalseconds", (double)Get(Get(Character, clock), "totalseconds") + delta);
        CallMain(callback);
        Equal(false, Property(MainInstance, "CombatPrepActive"), "original automatic deadline finishes and cleans up the active executor");
        Equal(true, Property(scheduler, "Finished"), "confirmed NUMBER closes the current pass at automatic completion");
        Equal(0, Property(scheduler, "Cycle"), "active-executor completion admits no extra automatic cycle");
        Equal(entry, Property(scheduler, "Entry"), "completion does not create a new stage entry");
        Equal(generation, Get(MainInstance, "_protectedGeneration"), "completion retains the original scheduler generation");
        Equal("0", string.Join(",", (double[])Property(scheduler, "EarnedSeconds")), "elapsed automatic work never fabricates protected fixed credit");
    }

    private static void CheckInlineStagePolicyEdits()
    {
        var failures = new List<Exception>();
        foreach (var mode in new[] { "gear", "support", "runtime-gear", "foreign", "unobserved-foreign" })
        {
            try { CheckInlineStagePolicyEdit(mode); }
            catch (Exception error) { failures.Add(error); }
        }
        if (failures.Count != 0) throw new AggregateException("Inline policy edits", failures);
    }

    private static void CheckInlineStagePolicyEdit(string mode)
    {
        var foreign = mode == "foreign" || mode == "unobserved-foreign";
        var plan = SetupInlineStagePlan(); RunVerifiedInline(plan, "explicit edit baseline");
        var store = Get(MainType, "CustomPlanStore"); var name = (string)Property(Get(MainType, "Settings"), "CustomPlanFile");
        Invoke(store, "Save", name, plan);
        plan = Invoke(store, "Load", name);
        RunVerifiedInline(plan, "stored baseline replay");
        var selection = Get(MainInstance, "_inlineStageSelection"); var acquisition = Get(selection, "Acquisition");
        var scheduler = Get(MainInstance, "_protectedScheduler");
        foreach (var clock in new[] { "rebirthTime", "totalPlaytime" })
            Set(Get(Character, clock), "totalseconds", (double)Get(Get(Character, clock), "totalseconds") + 2d);
        Set(scheduler, "_lastHeartbeat", (double)MainType.GetProperty("ProtectedHeartbeat", All).GetValue(null) - 2d);
        RunVerifiedInline(plan, "positive earned-work edit baseline");
        Equal(2d, ((double[])Property(scheduler, "EarnedSeconds"))[0], "policy edit fixture starts with actual earned protected work");
        var before = Invoke(scheduler, "ExportState");
        var searches = InlineGearSelections;
        // A cosmetic save is not a request to discard selected instances or foreign evidence.
        Set(((Array)Get(plan, "Stages")).GetValue(0), "Name", "Renamed inline row");
        Invoke(store, "Save", name, plan); plan = Invoke(store, "Load", name);
        RunVerifiedInline(plan, "cosmetic edit replay");
        Check(ReferenceEquals(selection, Get(MainInstance, "_inlineStageSelection")), "cosmetic edit retains selected instances");
        Equal(searches, InlineGearSelections, "cosmetic edit performs no optimizer search");
        var inventory = Get(Character, "inventory"); var items = (IList)Get(inventory, "inventory");
        if (foreign)
        {
            var original = Get(inventory, "head"); var replacement = items.Cast<object>().Single(x => (int)Get(x, "id") == 552);
            Set(inventory, "head", replacement); items[items.IndexOf(replacement)] = original;
            Invoke(typeof(Program), "RefreshBonuses");
            if (mode == "foreign")
            {
                CallMain("RunCustomPlanAutomation", plan);
                Check(Get(selection, "Blocked") != null, "foreign equipment produces durable conflict evidence before editing");
            }
        }
        var blocked = Get(selection, "Blocked"); var writes = ResourceTransfers; var swaps = Get(typeof(Program), "Swaps");
        var first = ((Array)Get(plan, "Stages")).GetValue(0);
        if (mode != "support")
            Set(first, "GearGoal", (int)Enum.Parse(Plugin.GetType("NGUInjector.Managers.GearOptimizationGoal", true), "MagicCap"));
        Set(first, "EnergyPriorities", new[] { "AT", "NGU" });
        if (mode == "runtime-gear")
            Equal(false, CallMain("InlineStageSetupVerified", plan), "fresh changed runtime intent cannot reuse an old setup receipt even without cache invalidation");
        else
        {
            Invoke(store, "Save", name, plan); plan = Invoke(store, "Load", name);
        }
        CallMain("RunCustomPlanAutomation", plan);
        if (foreign)
        {
            Check(ReferenceEquals(selection, Get(MainInstance, "_inlineStageSelection")), "explicit policy edit cannot erase an existing foreign conflict");
            Check(Get(selection, "Blocked") != null, "explicit policy edit retains or detects foreign conflict before acquiring new intent");
            if (blocked != null) Equal(blocked, Get(selection, "Blocked"), "explicit policy edit preserves the original conflict reason");
            Equal(552, Get(Get(inventory, "head"), "id"), "explicit edit leaves foreign equipment untouched");
            Equal(searches, InlineGearSelections, "foreign conflict prevents replacement selection");
            Equal(swaps, Get(typeof(Program), "Swaps"), "foreign conflict prevents equipment writes");
            Equal(writes, ResourceTransfers, "foreign conflict prevents dependent resource writes");
            Equal(false, Get(scheduler, "_armed"), "foreign edit cannot earn protected credit");
        }
        else
        {
            Equal(mode == "support" ? 550 : 551, Get(Get(inventory, "head"), "id"), "explicit setup edit uses requested gear, retaining selected instances for support-only edits");
            var current = Get(MainInstance, "_inlineStageSelection");
            Check(!Equals(acquisition, Get(current, "Acquisition")), "explicit changed intent acquires one new selection independently of scheduler identity");
            Equal(searches + (mode == "support" ? 0 : 1), InlineGearSelections, "only an explicit gear change performs one admitted gear search");
            Equal(true, CallMain("InlineStageSetupVerified", plan), "new selected setup verifies before credit");
            Equal(true, Get(scheduler, "_armed"), "edited entry can resume credit only after complete application");
            var expected = CallMain("SetupForStage", ((Array)Get(plan, "Stages")).GetValue(0), false, false, false);
            Equal(string.Join(",", (int[])Get(expected, "Beards")), string.Join(",", (int[])Get(Get(current, "Policy"), "Beards")), "support policy also follows explicitly edited priorities");
            CallMain("RunCustomPlanAutomation", plan);
            Equal(searches + (mode == "support" ? 0 : 1), InlineGearSelections, "ordinary replay retains the newly selected instances");
        }
        foreach (var field in new[] { "entry", "cycle", "enteredAt", "expectedEnd", "durations", "earned", "applied", "visits" })
            Equal(Json(before, field).ToString(), Json(Invoke(scheduler, "ExportState"), field).ToString(), "setup edit preserves scheduler credit and identity/" + field);
        Equal(7L, Get(Get(Character, "wandoos98"), "energyLevel"), "setup edits preserve native W history");
        Equal(.25f, Get(Get(Character, "wandoos98"), "magicProgress"), "setup edits preserve fractional native progress");
        Equal(731L, Get(Get(Character, "res3"), "curRes3"), "setup edits preserve the unmanaged R3 pool");
        Equal(431L, Get(Get(Character, "res3"), "idleRes3"), "setup edits preserve unmanaged R3 allocation");
    }

    private static void RunVerifiedInline(object plan, string message)
    {
        CallMain("RunCustomPlanAutomation", plan);
        Equal(true, CallMain("InlineStageSetupVerified", plan), message + "; " + CallMain("GetCustomAutomationStatus")
            + "; " + Get(MainInstance, "_inlineStageSetupError"));
        var allocator = Plugin.GetType("NGUInjector.Managers.CustomStageAllocator", true);
        Equal(true, allocator.GetProperty("LastStageApplicationSucceeded", All).GetValue(null), message + " verifies native resources");
        Equal(true, Get(Get(MainInstance, "_protectedScheduler"), "_armed"), message + " arms credit only after complete verification");
    }

    private static void CheckInlineStageSuccessor()
    {
        var plan = SetupInlineStagePlan(); RunVerifiedInline(plan, "inline A acquisition");
        var inventory = Get(Character, "inventory");
        Equal(550, Get(Get(inventory, "head"), "id"), "A actually equips its selected Energy-cap head");
        var a = Get(MainInstance, "_inlineStageSelection");
        var aBaseline = Get(a, "Baseline");
        var gearSearches = InlineGearSelections;
        var swaps = Get(typeof(Program), "Swaps"); var writes = ResourceTransfers;
        CallMain("InvalidateCustomEquipmentApplication"); RunVerifiedInline(plan, "same-entry invalidation replay");
        Check(ReferenceEquals(a, Get(MainInstance, "_inlineStageSelection")), "application invalidation retains acquired A identity");
        Equal(gearSearches, InlineGearSelections, "invalidated A does not reselect equipment");
        Equal(swaps, Get(typeof(Program), "Swaps"), "matching A needs no repeat native swap");
        Equal(writes, ResourceTransfers, "matching A needs no repeat resource accounting");

        var settings = Get(MainType, "Settings");
        Set(settings, "_manualAdventureEnabled", true); Set(settings, "_manualAdventureLoadout", 0);
        var bonuses = new double[7]; bonuses[4] = 100; Set(settings, "_manualAdventureBeardWeights", bonuses);
        Set(MainInstance, "_manualAdventureSetupActive", true);
        Equal(true, CallMain("TryApplyManualAdventureOverlay"), "inline A admits a real selected manual loadout");
        Equal(null, CallMain("ApplyManualAdventureOverlaySupport"), "inline A composes additive manual support");
        Equal(552, Get(Get(inventory, "head"), "id"), "manual claim actually installs the combat head");
        CallMain("RunCustomPlanAutomation", plan);
        Equal(true, CallMain("InlineStageSetupVerified", plan), "composed A verifies against current claim");

        var scheduler = Get(MainInstance, "_protectedScheduler");
        Invoke(scheduler, "OverrideStage", plan, "skip", 0, (double)Get(Get(Character, "rebirthTime"), "totalseconds"),
            MainType.GetProperty("ProtectedHeartbeat", All).GetValue(null));
        var commitment = Invoke(scheduler, "ExportState");
        var observed = new List<int>(); var released = false;
        GearRenderObserver = () => {
            var id = (int)Get(Get(inventory, "head"), "id"); observed.Add(id);
            Check(released || id != 551, "B must not flash beneath an active manual gear claim");
        };
        try
        {
            RunVerifiedInline(plan, "B acquisition underneath current manual claim");
            var b = Get(MainInstance, "_inlineStageSelection"); var baseline = Get(b, "Baseline");
            Check(!ReferenceEquals(a, b), "real entry change acquires B independently");
            Equal(551, Property(((Array)Get(baseline, "Gear")).GetValue(0), "id"), "B retains its own Magic-cap head, not temporary combat gear");
            Equal(552, Get(Get(inventory, "head"), "id"), "B acquisition leaves the composed combat head installed");
            Check(!ReferenceEquals(aBaseline, baseline), "B never aliases historical A requirements");
            gearSearches = InlineGearSelections;
            foreach (var field in new[] { "entry", "cycle", "enteredAt", "expectedEnd", "durations", "earned", "applied" })
                Equal(Json(commitment, field).ToString(), Json(Invoke(scheduler, "ExportState"), field).ToString(), "B setup preserves original commitment/" + field);
            var at = Get(Character, "advancedTraining");
            Check(((long[])Get(at, "energy")).Take(2).All(x => x > 0), "both unfinished mandatory AT lanes actually receive Energy");
            Check(((long[])Get(at, "level")).All(x => x == 0), "acquisition cannot manufacture mandatory AT completion");
            Equal(false, CallMain("HasCompletedProtectedStageWork", plan), "B remains incomplete until real AT reaches its targets");

            released = true; Set(settings, "_manualAdventureEnabled", false);
            RunVerifiedInline(plan, "manual claim removal resolves current B");
            Equal(551, Get(Get(inventory, "head"), "id"), "release directly equips B");
            Equal(Property(Get(baseline, "Support"), "StableKey"), Property(Property(CallMain("CaptureSupportState"), "Selection"), "StableKey"), "release directly applies exact B support");
            Equal(gearSearches, InlineGearSelections, "claim removal never searches a replacement or reselects A/B");
            Check(observed.Contains(551) && !observed.Contains(550), "native callback trace has no historical A detour");
            Equal(7L, ((long[])Property(Get(Character, "training"), "attackEnergy"))[0], "handoff preserves basic training");
            Equal(731L, Get(Get(Character, "res3"), "curRes3"), "handoff preserves unmanaged R3 total");
            Equal(431L, Get(Get(Character, "res3"), "idleRes3"), "handoff preserves unmanaged R3 allocation");
            Equal(7L, Get(Get(Character, "wandoos98"), "energyLevel"), "handoff never rolls back native W history");
            Equal(.25f, Get(Get(Character, "wandoos98"), "magicProgress"), "handoff preserves native fractional W progression");
            swaps = Get(typeof(Program), "Swaps"); writes = ResourceTransfers;
            CallMain("RunQuickCustomResourceRefresh", plan);
            Equal(swaps, Get(typeof(Program), "Swaps"), "steady B quick refresh writes no gear");
            Equal(writes, ResourceTransfers, "steady B quick refresh writes no resources");
            Equal(gearSearches, InlineGearSelections, "steady B quick refresh performs no selection");
        }
        finally { GearRenderObserver = null; }
    }

    private static void CheckInlineStagePartialSupport()
    {
        var plan = SetupInlineStagePlan(); var fired = false;
        PodInterference = pod => {
            if (fired) return;
            fired = true; PodInterference = null;
            throw new InvalidOperationException("fixture inline support interruption");
        };
        try { CallMain("RunCustomPlanAutomation", plan); }
        finally { PodInterference = null; }
        Equal(true, fired, "inline support failure reaches a real native callback");
        Equal("KnownPartial", Get(Get(MainInstance, "_presetSupportOutcome"), "Disposition").ToString(), "inline support partial retains bounded native evidence");
        var selection = Get(MainInstance, "_inlineStageSelection");
        Equal(null, Get(selection, "Applied"), "partial setup has no verified application");
        Equal(false, Get(Get(MainInstance, "_protectedScheduler"), "_armed"), "partial setup admits no stage credit");
        var searches = InlineGearSelections; var swaps = Get(typeof(Program), "Swaps");
        RunVerifiedInline(plan, "bounded inline support continuation");
        Check(ReferenceEquals(selection, Get(MainInstance, "_inlineStageSelection")), "known partial retains original entry selection");
        Equal(searches, InlineGearSelections, "known partial never launches another gear selection");
        Equal(swaps, Get(typeof(Program), "Swaps"), "known partial never repeats a committed gear swap");
    }

    private static void CheckInlineStageForeignSetup()
    {
        var plan = SetupInlineStagePlan(); RunVerifiedInline(plan, "foreign-control initial setup");
        var inventory = Get(Character, "inventory"); var items = (IList)Get(inventory, "inventory");
        var current = Get(inventory, "head"); var foreign = items[1];
        Set(inventory, "head", foreign); items[1] = current; Invoke(typeof(Program), "RefreshBonuses");
        var searches = InlineGearSelections; var swaps = Get(typeof(Program), "Swaps"); var writes = ResourceTransfers;
        CallMain("RunCustomPlanAutomation", plan);
        Equal(false, CallMain("InlineStageSetupVerified", plan), "foreign controlled gear yields setup verification");
        Check(ReferenceEquals(foreign, Get(inventory, "head")), "ordinary retry does not overwrite foreign equipment");
        Equal(false, Get(Get(MainInstance, "_protectedScheduler"), "_armed"), "foreign setup stops dependent stage credit");
        CallMain("InvalidateCustomEquipmentApplication"); CallMain("RunCustomPlanAutomation", plan);
        Equal(searches, InlineGearSelections, "application reset does not authorize replacement selection after foreign edit");
        Equal(swaps, Get(typeof(Program), "Swaps"), "application reset does not erase foreign setup conflict");
        Equal(writes, ResourceTransfers, "unsafe setup admits no dependent resource mutation");
    }

    private static void CheckInlineStageUncontrolledGear()
    {
        var plan = SetupInlineStagePlan(false); RunVerifiedInline(plan, "gearless stage acquisition");
        var selection = Get(MainInstance, "_inlineStageSelection");
        Equal(false, Get(Get(selection, "Baseline"), "ControlsGear"), "no gear goal is uncontrolled, not controlled-empty");
        var inventory = Get(Character, "inventory"); var items = (IList)Get(inventory, "inventory");
        var current = Get(inventory, "head"); var replacement = items[1];
        Set(inventory, "head", replacement); items[1] = current; Invoke(typeof(Program), "RefreshBonuses");
        var searches = InlineGearSelections; var swaps = Get(typeof(Program), "Swaps");
        RunVerifiedInline(plan, "gearless stage observes a legitimate current setup");
        Check(ReferenceEquals(replacement, Get(inventory, "head")), "uncontrolled gear is never restored from a snapshot");
        Equal(searches, InlineGearSelections, "uncontrolled gear has no hidden optimizer fallback");
        Equal(swaps, Get(typeof(Program), "Swaps"), "uncontrolled gear observation performs no native swap");
    }

    private static void CheckInlineStagePermissionCallback()
    {
        var plan = SetupInlineStagePlan(); var settings = Get(MainType, "Settings");
        var fired = false; int[] callbackBeards = null; long[] callbackLevels = null;
        var callbackPods = 0; var callbackWrites = 0;
        PodInterference = pod => {
            if (fired) return;
            fired = true; PodInterference = null;
            callbackBeards = ((IList)Get(Get(Character, "beards"), "activeBeards")).Cast<int>().ToArray();
            callbackLevels = ((IList)Get(Get(Character, "diggers"), "diggers")).Cast<object>().Select(x => (long)Get(x, "curLevel")).ToArray();
            callbackPods = PodRefreshCalls; callbackWrites = ResourceTransfers;
            Set(settings, "_manageMagic", false);
        };
        try { CallMain("RunCustomPlanAutomation", plan); }
        finally { PodInterference = null; }
        Equal(true, fired, "native support callback actually revokes a managed lane");
        Check(callbackBeards.SequenceEqual(((IList)Get(Get(Character, "beards"), "activeBeards")).Cast<int>()),
            "permission revocation stops all subsequent beard mutations without undoing prior commits");
        Check(callbackLevels.SequenceEqual(((IList)Get(Get(Character, "diggers"), "diggers")).Cast<object>().Select(x => (long)Get(x, "curLevel"))),
            "permission revocation stops subsequent exact digger submissions");
        Equal(callbackPods, PodRefreshCalls, "permission revocation stops borrowed-controller cleanup and later callbacks");
        Equal(callbackWrites, ResourceTransfers, "permission revocation admits no later resource withdrawal");
        Equal(false, CallMain("InlineStageSetupVerified", plan), "revoked coupled setup cannot claim verification");
        Equal(false, Get(Get(MainInstance, "_protectedScheduler"), "_armed"), "revoked setup earns no protected credit");
        var swaps = Get(typeof(Program), "Swaps"); var pods = PodRefreshCalls; var writes = ResourceTransfers;
        Set(settings, "_manageMagic", true);
        CallMain("InvalidateCustomEquipmentApplication"); CallMain("RunCustomPlanAutomation", plan);
        Equal(swaps, Get(typeof(Program), "Swaps"), "restored checkbox/application reset does not erase unresolved native authority");
        Equal(pods, PodRefreshCalls, "revoked support is not automatically submitted again");
        Equal(writes, ResourceTransfers, "revoked setup admits no dependent resource action");
    }

    private static void CheckInlineStageGearRenderFailure()
    {
        var plan = SetupInlineStagePlan(); var inventory = Get(Character, "inventory"); var fired = false;
        GearRenderObserver = () => {
            if (!fired && (int)Get(Get(inventory, "head"), "id") == 550)
            {
                fired = true;
                throw new InvalidOperationException("fixture committed inline gear renderer");
            }
        };
        try { RunVerifiedInline(plan, "model-committed inline gear render failure"); }
        finally { GearRenderObserver = null; }
        Equal(true, fired, "positive control throws only after the desired native head is committed");
        var swaps = Get(typeof(Program), "Swaps"); var writes = ResourceTransfers; var searches = InlineGearSelections;
        RunVerifiedInline(plan, "post-render inline observation");
        Equal(swaps, Get(typeof(Program), "Swaps"), "render failure never repeats committed gear accounting");
        Equal(writes, ResourceTransfers, "render failure never repeats committed resource accounting");
        Equal(searches, InlineGearSelections, "render failure does not authorize replacement equipment selection");
    }

    private static void CheckInlineStageScopedResourceContinuation()
    {
        var plan = SetupInlineStagePlan();
        Set(Character, "curEnergy", 9800L); Set(Character, "idleEnergy", 9793L);
        var ngu = Get(Character, "NGU"); var energy = ((IList)Get(ngu, "skills"))[0];
        Set(energy, "level", 100000L); // Productive NGU demand has room for legitimate pool growth.
        RunVerifiedInline(plan, "resource conflict A entry");
        var scheduler = Get(MainInstance, "_protectedScheduler");
        Invoke(scheduler, "OverrideStage", plan, "skip", 0, (double)Get(Get(Character, "rebirthTime"), "totalseconds"),
            MainType.GetProperty("ProtectedHeartbeat", All).GetValue(null));
        RunVerifiedInline(plan, "resource conflict mandatory-AT B entry");
        var selection = Get(MainInstance, "_inlineStageSelection");
        var at = (long[])Get(Get(Character, "advancedTraining"), "energy");
        Check(at[0] > 0, "scope fixture has an actual AT destination to repair");
        at[0]--; Set(Character, "idleEnergy", (long)Get(Character, "idleEnergy") + 1);
        var magic = ((IList)Get(ngu, "magicSkills"))[0]; var magicAmount = (long)Get(magic, "magic");
        Check(magicAmount > 0, "scope fixture has real Magic NGU funding");
        var fired = false;
        AtRefreshObserver = () => {
            fired = true; AtRefreshObserver = null;
            Set(magic, "magic", magicAmount - 1);
            Set(Get(Character, "magic"), "idleMagic", (long)Get(Get(Character, "magic"), "idleMagic") + 1);
        };
        FailAtTargetText = true;
        try
        {
            Set(MainInstance, "_customLastQuickResourceRefresh", DateTime.MinValue);
            CallMain("ReconcileCustomResources", plan);
        }
        finally { AtRefreshObserver = null; FailAtTargetText = false; }
        Equal(true, fired, "scope test performs foreign Magic accounting in a native rendering callback");
        Equal(false, Get(scheduler, "_armed"), "Magic-dependent work receives no full-stage credit");
        var before = (long)Get(energy, "energy") + at.Sum(); var pods = PodRefreshCalls; var searches = InlineGearSelections;
        Set(Character, "curEnergy", 9900L); Set(Character, "idleEnergy", (long)Get(Character, "idleEnergy") + 100);
        Set(MainInstance, "_customLastQuickResourceRefresh", DateTime.MinValue);
        CallMain("ReconcileCustomResources", plan);
        Equal(before + 100, (long)Get(energy, "energy") + at.Sum(), "inline healthy Energy continues through actual below-cap growth");
        Equal(magicAmount - 1, Get(magic, "magic"), "inline continuation preserves the yielded Magic lane");
        Check(ReferenceEquals(selection, Get(MainInstance, "_inlineStageSelection")), "scoped conflict retains acquired inline intent");

        Set(MainInstance, "_customEquipmentApplicationInvalidated", true);
        Set(Character, "curEnergy", 9950L); Set(Character, "idleEnergy", (long)Get(Character, "idleEnergy") + 50);
        CallMain("RunCustomPlanAutomation", plan);
        Equal(before + 150, (long)Get(energy, "energy") + at.Sum(), "deferred coupled setup does not freeze independent healthy resource work");
        Equal(magicAmount - 1, Get(magic, "magic"), "deferred support does not adopt foreign Magic");
        Equal(pods, PodRefreshCalls, "unresolved resources authorize no coupled support write");
        Equal(searches, InlineGearSelections, "scoped failure and setup invalidation never search replacement gear");
        Equal(false, Get(scheduler, "_armed"), "resource-only continuation never certifies the whole stage");
        Equal("NeedsUser", Property(Json(CallMain("GetResourceCoordinationRuntime"), "state"), "Value"), "scoped unresolved authority reports an acknowledgement state, not ordinary waiting");
    }

    private static void CheckInlineStageIndependentBeardCadence()
    {
        var plan = SetupInlineStagePlan(); RunVerifiedInline(plan, "independent beard cadence entry");
        var selection = Get(MainInstance, "_inlineStageSelection");
        var scheduler = Get(MainInstance, "_protectedScheduler"); var entry = Property(scheduler, "Entry");
        var deadline = Property(scheduler, "ExpectedEndSeconds"); var searches = InlineGearSelections;
        var original = ((IList)Get(Get(Character, "beards"), "activeBeards")).Cast<int>().OrderBy(x => x).ToArray();
        var due = DateTime.UtcNow.AddSeconds(-61);
        Set(selection, "SupportAt", DateTime.UtcNow); // Recent support work must not postpone the independent beard minute.
        Set(MainInstance, "_customLastBeardRotation", due);
        var pods = PodRefreshCalls;
        CallMain("RunCustomBeardAutomation", plan);
        Equal(pods, PodRefreshCalls, "beard callback emits an inline event without becoming another native writer");
        RunVerifiedInline(plan, "independent beard minute reconciliation");
        Check((DateTime)Get(MainInstance, "_customLastBeardRotation") > due, "a recent support event cannot postpone a due beard minute");
        var policy = Plugin.GetType("NGUInjector.Managers.ActivityPolicy", true).GetProperty("Current", All).GetValue(null);
        var core = ((IEnumerable)Invoke(policy, "CoreBeards")).Cast<int>();
        var required = (int[])CallMain("ResolveCustomBeardRequirements", Get(Get(selection, "Policy"), "Beards"));
        var slots = (int)Invoke(Get(Character, "allBeards"), "capBeards");
        var mandatory = core.Concat(required).Distinct().Take(slots).ToArray();
        var current = ((IList)Get(Get(Character, "beards"), "activeBeards")).Cast<int>().OrderBy(x => x).ToArray();
        if (mandatory.Length < slots && slots < 7)
            Check(!original.SequenceEqual(current), "an eligible native optional-beard choice actually rotates");
        else
        {
            Check(original.SequenceEqual(current), "fully required beard slots do not invent an optional replacement");
            Equal(pods, PodRefreshCalls, "a due event with no legal replacement performs no redundant support writes");
        }
        Check(ReferenceEquals(selection, Get(MainInstance, "_inlineStageSelection")), "minute rotation retains the acquired stage selection");
        Equal(searches, InlineGearSelections, "minute support work never reselects equipment");
        Equal(entry, Property(scheduler, "Entry"), "minute work preserves the stage entry");
        Equal(deadline, Property(scheduler, "ExpectedEndSeconds"), "minute work never renews the stage budget");
    }

    private static void CheckInlineStageInvalidationCannotAdoptForeign()
    {
        foreach (var eventFirst in new[] { true, false })
        {
            var plan = SetupInlineStagePlan(); RunVerifiedInline(plan, "foreign/event ordering entry");
            if (eventFirst) CallMain("InvalidateCustomEquipmentApplication");
            var inventory = Get(Character, "inventory"); var items = (IList)Get(inventory, "inventory");
            var old = Get(inventory, "head"); var foreign = items[1];
            Set(inventory, "head", foreign); items[1] = old; Invoke(typeof(Program), "RefreshBonuses");
            Set(MainInstance, "_customLastBeardRotation", DateTime.UtcNow.AddSeconds(-61));
            CallMain("RunCustomBeardAutomation", plan);
            var swaps = Get(typeof(Program), "Swaps"); var pods = PodRefreshCalls; var writes = ResourceTransfers;
            var searches = InlineGearSelections;
            CallMain("RunCustomPlanAutomation", plan);
            Check(ReferenceEquals(foreign, Get(inventory, "head")), "event/invalidation ordering never grants authority over a foreign edit/" + eventFirst);
            Equal(swaps, Get(typeof(Program), "Swaps"), "due support event cannot overwrite foreign gear/" + eventFirst);
            Equal(pods, PodRefreshCalls, "due event cannot adopt foreign setup as a new support transaction/" + eventFirst);
            Equal(writes, ResourceTransfers, "foreign setup blocks dependent resource writes/" + eventFirst);
            Equal(searches, InlineGearSelections, "foreign setup cannot trigger a replacement search/" + eventFirst);
            Equal(false, Get(Get(MainInstance, "_protectedScheduler"), "_armed"), "foreign/event ordering never earns full-stage credit/" + eventFirst);
        }
    }

    private static void CheckInlineStageCrossDomainCallbacks()
    {
        var plan = SetupInlineStagePlan(); var inventory = Get(Character, "inventory");
        var fired = false; string foreignSupport = null; var pods = 0; var writes = 0;
        GearRenderObserver = () => {
            if (fired || (int)Get(Get(inventory, "head"), "id") != 550) return;
            fired = true; SetActiveBeards(0, 1);
            foreignSupport = (string)Property(Property(CallMain("CaptureSupportState"), "Selection"), "StableKey");
            pods = PodRefreshCalls; writes = ResourceTransfers;
        };
        try { CallMain("RunCustomPlanAutomation", plan); }
        finally { GearRenderObserver = null; }
        Equal(true, fired, "gear callback actually changes a valid foreign support selection");
        Equal(foreignSupport, Property(Property(CallMain("CaptureSupportState"), "Selection"), "StableKey"), "gear phase cannot adopt and overwrite foreign support");
        Equal(pods, PodRefreshCalls, "foreign support during gear prevents entering the support mutation phase");
        Equal(writes, ResourceTransfers, "foreign support during gear prevents dependent resource submission");
        Equal(false, Get(Get(MainInstance, "_protectedScheduler"), "_armed"), "cross-domain gear conflict earns no credit");

        foreach (var changeGear in new[] { true, false })
        {
            plan = SetupInlineStagePlan(); inventory = Get(Character, "inventory"); fired = false;
            object foreignGear = null; long nativeEnergy = 0;
            PodInterference = pod => {
                if (fired) return;
                fired = true; PodInterference = null;
                if (changeGear)
                {
                    var items = (IList)Get(inventory, "inventory"); foreignGear = items[1];
                    var old = Get(inventory, "head"); Set(inventory, "head", foreignGear); items[1] = old;
                    Invoke(typeof(Program), "RefreshBonuses");
                }
                else
                {
                    var skill = ((IList)Get(Get(Character, "NGU"), "skills"))[0];
                    nativeEnergy = (long)Get(skill, "energy") + 1; Set(skill, "energy", nativeEnergy);
                    Set(Character, "idleEnergy", (long)Get(Character, "idleEnergy") - 1);
                }
                foreignSupport = (string)Property(Property(CallMain("CaptureSupportState"), "Selection"), "StableKey");
                pods = PodRefreshCalls; writes = ResourceTransfers;
            };
            try { CallMain("RunCustomPlanAutomation", plan); }
            finally { PodInterference = null; }
            Equal(true, fired, "support callback reaches foreign companion domain/gear=" + changeGear);
            Equal(pods, PodRefreshCalls, "foreign companion domain stops subsequent native support callbacks/gear=" + changeGear);
            Equal(foreignSupport, Property(Property(CallMain("CaptureSupportState"), "Selection"), "StableKey"), "foreign companion domain stops subsequent support mutations/gear=" + changeGear);
            Equal(writes, ResourceTransfers, "foreign companion domain admits no later resource writes/gear=" + changeGear);
            if (changeGear) Check(ReferenceEquals(foreignGear, Get(inventory, "head")), "support never restores over callback's foreign gear");
            else Equal(nativeEnergy, Get(((IList)Get(Get(Character, "NGU"), "skills"))[0], "energy"), "support preserves the callback's foreign resource allocation");
            Equal(false, Get(Get(MainInstance, "_protectedScheduler"), "_armed"), "cross-domain support conflict earns no credit/gear=" + changeGear);
        }
    }

    private static void CheckInlineStageAffordablePartialContinuation()
    {
        var plan = SetupInlineStagePlan(); var fired = false;
        PodInterference = pod => { if (fired) return; fired = true; PodInterference = null; throw new InvalidOperationException("fixture affordable partial"); };
        try { CallMain("RunCustomPlanAutomation", plan); }
        finally { PodInterference = null; }
        Equal(true, fired, "affordability timeline starts from a real owned support interruption");
        Equal("KnownPartial", Get(Get(MainInstance, "_presetSupportOutcome"), "Disposition").ToString(), "affordability control is known partial, not foreign");
        var selection = Get(MainInstance, "_inlineStageSelection"); var baseline = Get(selection, "Baseline");
        var desired = Get(baseline, "Support");
        Check(((Array)Property(desired, "Diggers")).Length > 0, "original planned support has genuine funded diggers");
        var beards = ((int[])Property(desired, "Beards")).ToArray(); var acceptedAt = Get(selection, "SupportAcceptedAt");
        var searches = InlineGearSelections; var swaps = Get(typeof(Program), "Swaps");
        Set(Get(Character, "machine"), "realBaseGold", 1d);
        RunVerifiedInline(plan, "owned planned support converges after income loss");
        Check(ReferenceEquals(selection, Get(MainInstance, "_inlineStageSelection")), "affordable replacement retains the acquired stage identity");
        Check(beards.SequenceEqual((int[])Property(Get(baseline, "Support"), "Beards")), "affordability change does not redraw the accepted beard event");
        Equal(0, ((Array)Property(Get(baseline, "Support"), "Diggers")).Length, "insufficient income leaves planned diggers dormant rather than overspending");
        Equal(acceptedAt, Get(MainInstance, "_customLastBeardRotation"), "partial continuation retains original accepted cadence time");
        Equal(searches, InlineGearSelections, "affordable planned continuation performs no gear search");
        Equal(swaps, Get(typeof(Program), "Swaps"), "affordable continuation never repeats committed gear swaps");
    }

    private static void CheckInlineStageOwnedMaterialRevision()
    {
        var plan = SetupInlineStagePlan(); PreparePresetNativeBoost(); RunVerifiedInline(plan, "owned material entry");
        var selection = Get(MainInstance, "_inlineStageSelection"); var acquisition = Get(selection, "Acquisition");
        var material = ((string[])Get(selection, "Material")).ToArray();
        var head = Get(Get(Character, "inventory"), "head");
        var searches = InlineGearSelections; var swaps = Get(typeof(Program), "Swaps");
        AutoBoostAction = () => ApplyPresetNativeBoost(head, -1, 27, 1000f);
        try { CallMain("RunCustomAutoBoost"); }
        finally { AutoBoostAction = null; }
        Check(!material.SequenceEqual((string[])Get(selection, "Material")), "causally owned boost revises the same selected equipment's approved material");
        Equal(null, Get(selection, "Applied"), "owned material change still requires actual setup revalidation");
        RunVerifiedInline(plan, "owned material replay");
        Check(ReferenceEquals(selection, Get(MainInstance, "_inlineStageSelection")), "owned material event preserves selected instance/entry identity");
        Equal(acquisition, Get(selection, "Acquisition"), "owned material event is not a new acquisition");
        Equal(searches, InlineGearSelections, "owned boosts do not create an optimizer timer or choose replacement items");
        Equal(swaps, Get(typeof(Program), "Swaps"), "owned material replay needs no repeat gear swap");
        Equal(10000L, Get(Character, "curEnergy"), "boosted capacity does not manufacture a filled resource pool");
    }

    private static void CheckInlineStageFocusPauseResume()
    {
        var plan = SetupInlineStagePlan(); RunVerifiedInline(plan, "focus handoff stage entry");
        var selection = Get(MainInstance, "_inlineStageSelection"); var acquisition = Get(selection, "Acquisition");
        var scheduler = Get(MainInstance, "_protectedScheduler"); var before = Invoke(scheduler, "ExportState");
        var focus = Enum.Parse(Plugin.GetType("NGUInjector.CustomFocusGoal", true), "MagicNgu");
        InlineObservedError = null;
        CallMain("ApplyCustomFocusGoal", focus);
        Equal("applied", CallMain("GetCustomFocusStatus"), "real focus verifies its temporary selected setup; " + CallMain("GetCustomAutomationStatus") + "; " + InlineObservedError);
        Equal(false, Property(Get(MainType, "Settings"), "UseCustomRebirthPlan"), "explicit focus retains existing plan-pause semantics");
        Equal(551, Get(Get(Get(Character, "inventory"), "head"), "id"), "focus really displaces stage Energy-cap gear with its selected Magic setup");
        foreach (var clock in new[] { "rebirthTime", "totalPlaytime" })
            Set(Get(Character, clock), "totalseconds", (double)Get(Get(Character, clock), "totalseconds") + 20d);
        CallMain("GetProtectedStage", plan);
        foreach (var field in new[] { "entry", "cycle", "enteredAt", "durations", "earned", "applied" })
            Equal(Json(before, field).ToString(), Json(Invoke(scheduler, "ExportState"), field).ToString(), "paused focus never renews or credits the stage commitment/" + field);
        CallMain("StopCustomFocusGoal");
        Set(Get(MainType, "Settings"), "_useCustomRebirthPlan", true);
        var searches = InlineGearSelections;
        RunVerifiedInline(plan, "same-entry resume after verified focus handoff");
        Check(ReferenceEquals(selection, Get(MainInstance, "_inlineStageSelection")), "pause/resume retains original stage choices rather than reacquiring");
        Equal(acquisition, Get(selection, "Acquisition"), "focus does not manufacture a new stage acquisition identity");
        Equal(550, Get(Get(Get(Character, "inventory"), "head"), "id"), "resume resolves current retained stage requirements directly");
        Equal(searches, InlineGearSelections, "resuming after an owned temporary setup never searches replacement stage gear");
    }

    private static void CheckInlineStageRenderingBeforeNewEvent()
    {
        var plan = SetupInlineStagePlan();
        foreach (var digger in (IList)Get(Get(Character, "diggers"), "diggers")) Set(digger, "maxLevel", 0L);
        SetActiveBeards(0, 2, 4); // Ensure a beard model change, with no digger model work after its renderer.
        FailBeardRefresh = true;
        try { RunVerifiedInline(plan, "committed support model with renderer pending"); }
        finally { FailBeardRefresh = false; }
        Equal("ModelAppliedRenderFailed", Get(Get(MainInstance, "_presetSupportOutcome"), "Disposition").ToString(), "positive control has committed all support model changes before rendering fails");
        Equal(true, Get(MainInstance, "_presetSupportPresentationPending"), "positive control retains bounded presentation evidence");
        var selection = Get(MainInstance, "_inlineStageSelection"); var originalRevision = (int)Get(selection, "SupportRevision");
        var swaps = Get(typeof(Program), "Swaps"); var searches = InlineGearSelections;
        Set(((IList)Get(Get(Character, "diggers"), "diggers"))[4], "maxLevel", 4L); // Legitimate purchased-capacity progress.
        Set(MainInstance, "_customLastBeardRotation", DateTime.UtcNow.AddSeconds(-61));
        RunVerifiedInline(plan, "new legal support event supersedes old rendering evidence");
        Check((int)Get(selection, "SupportRevision") > originalRevision, "changed support does not remain bound to an incompatible old rendering intent");
        Check(((Array)Property(Get(Get(selection, "Baseline"), "Support"), "Diggers")).Length > 0, "new support event actually funds newly purchased capacity");
        Equal(false, Get(MainInstance, "_presetSupportPresentationPending"), "new verified support renders without replaying old model work");
        Equal(null, Get(MainInstance, "_presetSupportContinuation"), "superseded rendering continuation is retired after verified current intent");
        Equal(swaps, Get(typeof(Program), "Swaps"), "support renderer recovery never repeats committed gear");
        Equal(searches, InlineGearSelections, "new support event does not reselect gear");
        var pods = PodRefreshCalls;
        RunVerifiedInline(plan, "post-render event is stable");
        Equal(pods, PodRefreshCalls, "recovered support event has no recurring model or presentation submissions");
    }

    private static void CheckInlineStageRejectedSupportAfterGear()
    {
        var plan = SetupInlineStagePlan(); RunVerifiedInline(plan, "rejected support handoff entry");
        var selection = Get(MainInstance, "_inlineStageSelection"); var acquisition = Get(selection, "Acquisition");
        var inventory = Get(Character, "inventory"); var machine = Get(Character, "machine"); var gold = Get(machine, "realBaseGold");
        var fired = false; var pods = PodRefreshCalls;
        GearRenderObserver = () => {
            if (fired || (int)Get(Get(inventory, "head"), "id") != 551) return;
            fired = true; Set(machine, "realBaseGold", 1d);
        };
        InlineObservedError = null;
        try { CallMain("ApplyCustomFocusGoal", Enum.Parse(Plugin.GetType("NGUInjector.CustomFocusGoal", true), "MagicNgu")); }
        finally { GearRenderObserver = null; }
        Equal(true, fired, "native callback reduces income only after the temporary gear model commits");
        Equal("RejectedUnchanged", Get(Get(MainInstance, "_presetSupportOutcome"), "Disposition").ToString(), "support preflight rejects an unaffordable target without changing its model; " + InlineObservedError);
        Equal(pods, PodRefreshCalls, "rejected support preflight makes no native support submission");
        Equal(551, Get(Get(inventory, "head"), "id"), "support rejection does not roll back verified temporary gear");
        Equal(551, Property(((Array)Get(Get(selection, "Observed"), "Gear")).GetValue(0), "id"), "verified intermediate gear progress survives the subsequent rejection");
        Equal(null, Get(selection, "Blocked"), "a known verified gear change is not misclassified as foreign");
        CallMain("StopCustomFocusGoal"); Set(machine, "realBaseGold", gold);
        Set(Get(MainType, "Settings"), "_useCustomRebirthPlan", true);
        var searches = InlineGearSelections;
        RunVerifiedInline(plan, "same-entry resume after verified gear and rejected support");
        Check(ReferenceEquals(selection, Get(MainInstance, "_inlineStageSelection")), "rejection recovery keeps the original stage selection");
        Equal(acquisition, Get(selection, "Acquisition"), "rejection recovery does not renew acquisition authority or its budget");
        Equal(550, Get(Get(inventory, "head"), "id"), "rejection recovery directly supplies the current baseline");
        Equal(searches, InlineGearSelections, "rejected support never forces another stage gear search");
    }
}
