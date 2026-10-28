using System;
using System.Collections;
using System.Linq;

public static partial class PresetIntegrationChecks
{
    // Parent integration: include this partial and invoke CheckExecutionCoreCutover().
    private static void CheckExecutionCoreCutover()
    {
        var before = Assertions;
        CheckExecutionRetainedFarmingRebind();
        CheckExecutionManualSupportResumeAuthority();
        CheckExecutionUnmanagedCapacityPreset();
        CheckExecutionInlineClaimsAndReservations();
        CheckExecutionPresetClaimsAndReservations();
        CheckExecutionCurrentRouteRetirement();
        CheckExecutionObsoleteStageRouteRetirement();
        CheckExecutionApprovedMaterialStorageRetry();
        CheckExecutionUncontrolledReservation();
        CheckExecutionPausedPresetIdentity();
        CheckExecutionPresetUncontrolledLanes();
        CheckExecutionPresetStopLanes();
        CheckExecutionPresetStopContinuation();
        CheckExecutionFocusStopLanes();
        Console.WriteLine("Execution core cutover: " + (Assertions - before) + " native assertions.");
    }

    private static void CheckExecutionManualSupportResumeAuthority()
    {
        var before = Assertions;
        foreach (var fault in new[] { "none", "revision", "conflict", "claim", "permission", "controller", "model", "epoch", "intent", "post-revision", "post-controller", "post-route" })
        {
            var id = "manual-support-authority-" + fault;
            SetupAdventureDynamic(id, "[2,3,0,0,0,0,0,0,0,2]");
            Set(MainInstance, "_globalPresetBeardRandom", new Random(42));
            var settings = Get(MainType, "Settings"); var store = Get(MainType, "CustomPlanStore");
            var library = Get(store, "<PresetLibrary>k__BackingField"); var root = Invoke(library, "ReadExternalLibrary");
            var row = ((IEnumerable)GetChildren(Json(root, "Presets"))).Cast<object>().Single(x => Property(Json(x, "Id"), "Value").Equals(id));
            SetJson(row, "SupportMode", Json(JsonParse("{\"value\":\"planned\"}"), "value")); SetJson(row, "BeardPriority", JsonParse("[1,3]"));
            SetJson(row, "BeardRotation", JsonParse("[0,2,6]"));
            Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
            Set(Get(Character, "advancedTraining"), "transferredBankedLevels", true);
            Set(Get(Character, "advancedTraining"), "levelTarget", Enumerable.Repeat(-1L, 5).ToArray());
            var plan = Invoke(store, "Load", "fixture");
            var stage = Activator.CreateInstance(Plugin.GetType("NGUInjector.CustomPlans.CustomPlanStage", true));
            Set(stage, "Name", "Manual support authority"); Set(stage, "PresetId", id); Set(stage, "Percent", 100d); Set(stage, "DurationSeconds", 60);
            Set(stage, "CompletionMode", 1); Set(stage, "TargetAdvancedTrainingPower", 10L); Set(stage, "TargetAdvancedTrainingToughness", 20L);
            var stages = Array.CreateInstance(stage.GetType(), 1); stages.SetValue(stage, 0); Set(plan, "Stages", stages);
            Set(plan, "StageSchedulerEnabled", true); Set(plan, "StageLoopStartIndex", -1);
            Set(plan, "AutoActivateFruits", false); Set(plan, "AutoRebirthEnabled", false); Set(plan, "TimeTriggerEnabled", false);
            Set(settings, "_useCustomRebirthPlan", true); Set(settings, "_autoRebirth", false);
            CallMain("RunCustomPlanAutomation", plan);
            var receipt = Get(MainInstance, "_globalPresetReceipt"); var owner = Get(receipt, "Owner"); var policy = Get(receipt, "Policy");
            Equal(true, CallMain("GlobalPresetReceiptValid", owner), fault + " acquires real scheduled native prerequisites");
            PrepareAdventureDynamicManualRule(); Set(settings, "_customAdventureRules", Array.CreateInstance(Plugin.GetType("NGUInjector.AdventureRule", true), 0));
            Set(settings, "_manualAdventureLoadout", -1); Set(settings, "_manualAdventureGearWeights", new double[5]);
            Set(settings, "_manualAdventureBeardWeights", new double[7]); Set(settings, "_manualAdventureDiggerWeights", new double[12]);
            Invoke(settings, "set_ManualAdventureEnabled", true); CallMain("RouteManualAdventureZone", 0, false, "manual support grant fixture");
            Equal(0, Get(Get(Character, "adventure"), "zone"), fault + " enters the actual explicit manual route");
            Equal(true, CallMain("GlobalPresetReceiptValid", owner), fault + " manual route retains verified preset ownership");
            var scheduler = Get(MainInstance, "_protectedScheduler"); var commitment = Invoke(scheduler, "ExportState");
            var acquisition = Get(policy, "Acquisition"); var head = Get(Get(Character, "inventory"), "head");
            var swaps = Get(typeof(Program), "Swaps"); var recalls = Get(typeof(Program), "Recalls"); var magicRecalls = Get(typeof(Program), "MagicRecalls");
            var attack = Get(Get(Character, "adventure"), "autoattacking");
            // Native toughness completion leaves Power's mandatory gate unfinished,
            // but releases the completed AT bar through its real resource renderer.
            ((long[])Get(Get(Character, "advancedTraining"), "level"))[0] = 20;
            Set(Character, "curEnergy", (long)Get(Character, "curEnergy") + 1003L);
            Set(Character, "idleEnergy", (long)Get(Character, "idleEnergy") + 1003L);
            var rotation = DateTime.UtcNow.AddSeconds(-61); Set(receipt, "LastBeardRotation", rotation);
            var entered = new System.Collections.Generic.List<int>();
            var rendered = false; var revoked = false; object claim = null;
            string appliedSupport = null, appliedMap = null;
            Action revoke = () => {
                revoked = true;
                var physical = CallMain("GlobalPresetAllocationState") + "|" + CallMain("GlobalPresetSupportState");
                if (fault == "revision" || fault == "post-revision") CallMain("InvalidateExecutionObservations", Domain("Route"));
                if (fault == "conflict") CallMain("RecordSelectedSetupConflict", Character, Get(Get(Character, "stats"), "rebirthNumber"), "route", "native route-only revocation");
                if (fault == "claim") claim = CallMain("BeginExecutionClaim", "foreign-manual-route", "native-route-claim", Domain("Route"), (Func<bool>)(() => true));
                if (fault == "permission") Set(settings, "_manageMagic", false);
                if (fault == "controller" || fault == "post-controller")
                {
                    var controller = Get(Character, "adventureController"); Set(controller, "zoneSelector", Invoke(Get(controller, "zoneSelector"), "MemberwiseClone"));
                }
                if (fault == "model") Set(Character, "adventure", Invoke(Get(Character, "adventure"), "MemberwiseClone"));
                if (fault == "epoch") Set(Get(Character, "stats"), "rebirthNumber", (long)Get(Get(Character, "stats"), "rebirthNumber") + 1);
                if (fault == "intent") Set(settings, "_manualAdventureZone", 1);
                if (fault == "post-route") Set(Get(Character, "adventure"), "zone", 1);
                if (fault == "revision" || fault == "conflict" || fault == "claim" || fault == "post-revision")
                    Equal(physical, CallMain("GlobalPresetAllocationState") + "|" + CallMain("GlobalPresetSupportState"), fault + " revokes ONLY route authority, not physical funding/support");
            };
            AdventureZoneEntryObserver = entered.Add;
            AdventureZoneEntryCompletedObserver = zone => { if (zone == 0 && fault.StartsWith("post-", StringComparison.Ordinal)) revoke(); };
            AtRefreshObserver = () => {
                if (rendered) return;
                rendered = true;
                Equal(-1, Get(Get(Character, "adventure"), "zone"), fault + " real resource renderer runs behind observed safe retirement");
                appliedSupport = (string)CallMain("GlobalPresetSupportState"); appliedMap = (string)CallMain("GlobalPresetAllocationState");
                if (fault != "none" && !fault.StartsWith("post-", StringComparison.Ordinal)) revoke();
            };
            try { CallMain("RunCustomBeardAutomation", plan); }
            finally { AtRefreshObserver = null; AdventureZoneEntryObserver = AdventureZoneEntryCompletedObserver = null; }
            Equal(true, rendered, fault + " exercises the actual native resource renderer, not a physical-check substitute; status="
                + CallMain("GetCustomAutomationStatus") + "; routes=" + string.Join(",", entered) + "; rotated=" + Get(receipt, "LastBeardRotation")
                + "; supportMode=" + Get(Get(policy, "Preset"), "SupportMode") + "; resources=" + CallMain("GetResourceCoordinationRuntime"));
            Equal(fault != "none", revoked, fault + " exercises its exact authority-loss boundary");
            if (fault == "revision" || fault == "conflict" || fault == "claim" || fault == "post-revision")
            {
                Equal(true, Property(Get(MainType, "_presetResourceOutcome"), "Verified"), fault + " route-only revocation leaves genuine native resource verification healthy");
                Equal(0, Get(Get(MainType, "_presetResourceOutcome"), "AffectedLanes"), fault + " revocation does not manufacture resource uncertainty");
            }
            if (fault == "none")
            {
                Equal("-1,0", string.Join(",", entered), "unchanged common authority retires and resumes exact current manual intent once");
                Equal(true, CallMain("GlobalPresetReceiptValid", owner), "positive manual support resume verifies actual current setup and funding");
                Equal(false, Get(receipt, "SupportRouteRetired"), "verified positive resume completes route retirement state");
                Check((DateTime)Get(receipt, "LastBeardRotation") > rotation, "only verified resume commits minute cadence");
            }
            else
            {
                Equal(fault.StartsWith("post-", StringComparison.Ordinal) ? 1 : 0, entered.Count(x => x == 0), fault + " performs ZERO native manual re-entry after pre-resume revocation");
                Equal(fault == "post-route" ? 1 : fault.StartsWith("post-", StringComparison.Ordinal) ? 0 : -1,
                    Get(Get(Character, "adventure"), "zone"), fault + " preserves safe or newer foreign route without a historical return");
                Equal(true, Get(receipt, "SupportRouteRetired"), fault + " failed common proof does not claim completed route resume");
                Equal(rotation, Get(receipt, "LastBeardRotation"), fault + " failed resume cannot consume cadence");
                var entries = AdventureZoneEntries; var writes = ResourceTransfers;
                CallMain("RefreshGlobalPresetExecution", owner, DateTime.UtcNow);
                Equal(entries, AdventureZoneEntries, fault + " pending retry cannot reacquire old route authority from matching physical fields");
                Equal(writes, ResourceTransfers, fault + " pending route proof never clears/replays funded prerequisites");
                if (claim != null) Equal(false, Get(claim, "Ended"), "manual support resume cannot end another route owner's claim");
            }
            Equal(appliedSupport, CallMain("GlobalPresetSupportState"), fault + " preserves actually applied support prerequisites");
            Equal(appliedMap, CallMain("GlobalPresetAllocationState"), fault + " preserves exact committed funding");
            Check(ReferenceEquals(head, Get(Get(Character, "inventory"), "head")), fault + " preserves actual manual gear without baseline flash");
            Check(ReferenceEquals(receipt, Get(MainInstance, "_globalPresetReceipt")), fault + " retains the acquired preset receipt");
            Equal(acquisition, Get(policy, "Acquisition"), fault + " route verification never renews acquisition");
            Equal(swaps, Get(typeof(Program), "Swaps"), fault + " resume performs zero gear swaps");
            Equal(recalls, Get(typeof(Program), "Recalls"), fault + " resume never recalls Energy");
            Equal(magicRecalls, Get(typeof(Program), "MagicRecalls"), fault + " resume never recalls Magic");
            Equal(attack, Get(Get(Character, "adventure"), "autoattacking"), fault + " preserves native attack preference");
            foreach (var field in new[] { "entry", "cycle", "enteredAt", "expectedEnd", "durations", "earned", "applied", "visits" })
                Equal(Json(commitment, field).ToString(), Json(Invoke(scheduler, "ExportState"), field).ToString(), fault + " preserves scheduler commitment/" + field);
        }
        Console.WriteLine("Manual support resume authority: " + (Assertions - before) + " native assertions.");
    }

    private static void CheckExecutionInlineClaimsAndReservations()
    {
        var plan = SetupInlineStagePlan(); RunVerifiedInline(plan, "core inline A");
        var inventory = Get(Character, "inventory"); var items = (IList)Get(inventory, "inventory");
        var temporary = items.Cast<object>().Single(x => (int)Get(x, "id") == 552);
        var claim = CallMain("BeginExecutionClaim", "native-titan", "spawn-1", Domain("Gear, Support, Route"), (Func<bool>)(() => true));
        var selected = ExactClaimSetup(temporary, "native-titan"); Set(selected, "ChangeSupport", true); Set(selected, "EnforceSupportBudget", false);
        CallMain("ApplyExecutionClaim", claim, selected);
        var searches = InlineGearSelections;
        RunVerifiedInline(plan, "core effective A with exact temporary setup");
        Equal(552, Get(Get(inventory, "head"), "id"), "baseline A cannot overwrite current temporary gear");
        Equal(searches, InlineGearSelections, "temporary allocation service cannot launch another gear search");
        var scheduler = Get(MainInstance, "_protectedScheduler");
        Invoke(scheduler, "OverrideStage", plan, "skip", 0, (double)Get(Get(Character, "rebirthTime"), "totalseconds"), MainType.GetProperty("ProtectedHeartbeat", All).GetValue(null));
        var commitment = Invoke(scheduler, "ExportState");
        RunVerifiedInline(plan, "core current mandatory-AT B under temporary setup");
        var selection = Get(MainInstance, "_inlineStageSelection"); var baseline = Get(selection, "Baseline");
        Equal(551, Property(((Array)Get(baseline, "Gear")).GetValue(0), "id"), "B independently retains selected Magic-cap gear");
        Equal(552, Get(Get(inventory, "head"), "id"), "B acquisition does not flash its covered gear");
        Equal(Property(Get(selected, "Support"), "StableKey"), Property(Property(CallMain("CaptureSupportState"), "Selection"), "StableKey"), "current temporary support stays exact");
        Check(((long[])Get(Get(Character, "advancedTraining"), "energy")).Take(2).All(x => x > 0), "actual composed setup funds unfinished mandatory AT");
        foreach (var field in new[] { "entry", "cycle", "enteredAt", "expectedEnd", "durations", "earned", "applied" })
            Equal(Json(commitment, field).ToString(), Json(Invoke(scheduler, "ExportState"), field).ToString(), "temporary B application preserves commitment/" + field);
        searches = InlineGearSelections;
        CallMain("EndExecutionClaim", claim, true);
        RunVerifiedInline(plan, "core direct B after temporary release");
        Equal(551, Get(Get(inventory, "head"), "id"), "release applies CURRENT B directly");
        Equal(searches, InlineGearSelections, "release does not reselect B");
        Equal(7L, ((long[])Property(Get(Character, "training"), "attackEnergy"))[0], "claims preserve basic training");
        Equal(731L, Get(Get(Character, "res3"), "curRes3"), "claims preserve unmanaged R3 current");
        Equal(431L, Get(Get(Character, "res3"), "idleRes3"), "claims preserve unmanaged R3 idle");
    }

    private static void CheckExecutionPresetClaimsAndReservations()
    {
        SetupResourceCoordination(true, false, "none");
        var receipt = Get(MainInstance, "_globalPresetReceipt"); Check(receipt != null, "core preset baseline is acquired");
        var energy = Get(Character, "curEnergy"); var beforeEnergy = CallMain("GlobalPresetLaneState", true);
        var magic = Get(Character, "magic"); var initialPool = Convert.ToInt64(Get(magic, "curMagic"));
        var claim = CallMain("BeginExecutionClaim", "native-fruit", "fruit-1", Domain("Magic"), (Func<bool>)(() => true));
        CallMain("SetExecutionResourceReservation", claim, 0L, 100L, 0L);
        var result = CallMain("ReconcileExecutionResourceClaim", claim);
        Equal(true, Property(result, "Verified"), "preset reservation commits through shared resources");
        Equal(100L, Get(magic, "idleMagic"), "preset CURRENT budget leaves requested idle Magic");
        Equal(beforeEnergy, CallMain("GlobalPresetLaneState", true), "Magic reservation preserves actual Energy destinations");
        Equal(energy, Get(Character, "curEnergy"), "Magic reservation preserves Energy current pool");
        Equal(true, CallMain("ExecutionResourceReservationReady", claim), "finite action admission observes actual reserved idle");
        Set(magic, "idleMagic", (long)Get(magic, "idleMagic") + 37L); Set(magic, "curMagic", initialPool + 37L);
        CallMain("EndExecutionClaim", claim, true);
        Equal(0L, Get(magic, "idleMagic"), "reservation release funds current baseline with grown pool");
        Equal(initialPool + 37L, ((IList)Get(Get(Character, "NGU"), "magicSkills")).Cast<object>().Sum(x => (long)Get(x, "magic")), "release uses current total, not an old allocation snapshot");
        Check(ReferenceEquals(receipt, Get(MainInstance, "_globalPresetReceipt")), "resource reservation retains acquired AutoUpdate-false policy and receipt");
    }

    private static void CheckExecutionCurrentRouteRetirement()
    {
        foreach (var foreign in new[] { false, true })
        {
            var args = SetupAdventureDynamic("core-route-A-" + foreign, "[1,1,0,0,0,0,0,0,0,0,0,0]");
            var controller = Get(Character, "adventureController");
            Set(Child(controller, "enemyAI"), "player", Child(controller, "playerController"));
            CallMain("EquipGlobalPreset", args, null);
            var adventure = Get(Character, "adventure"); Equal(1000, Get(adventure, "zone"), "route witness starts on actual acquired ITOPOD A");
            if (foreign) Set(adventure, "zone", 1);
            var claim = CallMain("BeginExecutionClaim", "core-harvest", "harvest-1", Domain("Support, Route"), (Func<bool>)(() => true));
            var witness = (Func<bool>)CallMain("CaptureExecutionBaselineRouteRetirement", claim);
            var wanted = Activator.CreateInstance(MainType.GetNestedType("SelectedSetupRequirements", All), true);
            Set(wanted, "ControlsGear", false); Set(wanted, "Support", Selection(new[] { 0 }));
            Set(wanted, "ChangeSupport", true); Set(wanted, "EnforceSupportBudget", false);
            Set(wanted, "RetireRoute", true); Set(wanted, "RouteRetirementAuthorized", witness);
            var entries = AdventureZoneEntries; var transfers = ResourceTransfers;
            if (foreign)
            {
                Equal(false, witness(), "claim grant cannot infer ownership of an unknown native route");
                Throws(() => CallMain("ApplyExecutionClaim", claim, wanted), "authority", "unknown route retains prerequisites before first mutation");
                Equal(1, Get(adventure, "zone"), "foreign route is not forcibly retired");
                Equal(entries, AdventureZoneEntries, "foreign witness rejection invokes no native route setter");
                Equal(transfers, ResourceTransfers, "foreign witness rejection touches no resources");
                CallMain("EndExecutionClaim", claim, false);
                continue;
            }
            Equal(true, witness(), "current verified baseline provides exact route retirement witness");
            CallMain("ApplyExecutionClaim", claim, wanted);
            Equal(-1, Get(adventure, "zone"), "temporary support is installed only after native safe retirement");
            var library = Get(Get(MainType, "CustomPlanStore"), "<PresetLibrary>k__BackingField"); var root = Invoke(library, "ReadExternalLibrary");
            Invoke(Json(root, "Presets"), "Add", "", JsonParse("{\"Id\":\"core-route-B\",\"Name\":\"B\",\"Weights\":[1,1,0,0,0,0,0,0,0,0,0,0],\"SupportMode\":\"planned\",\"BeardPriority\":[3],\"AutoUpdate\":true}"));
            var revision = Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
            CallMain("EquipGlobalPreset", JsonParse("{\"presetId\":\"core-route-B\",\"libraryRevision\":\"" + revision + "\"}"), null);
            var baseline = Get(MainInstance, "_executionBaseline"); var support = Property(Get(baseline, "Support"), "StableKey");
            Equal("0|", Property(Property(CallMain("CaptureSupportState"), "Selection"), "StableKey"), "B remains covered by exact temporary support");
            Equal(true, CallMain("EndExecutionClaim", claim, true), "current route claim ends with verified successor setup");
            CallMain("ReconcileCustomResources", null, false);
            Equal(support, Property(Property(CallMain("CaptureSupportState"), "Selection"), "StableKey"), "harvest exit applies CURRENT B support directly");
            Equal("core-route-B", Get(Get(MainInstance, "_globalPresetReceipt"), "Id"), "exit retains current B acquisition");
            Equal(-1, Get(adventure, "zone"), "exit cannot reactivate historical A farming route");
        }
    }

    private static void CheckExecutionObsoleteStageRouteRetirement()
    {
        foreach (var mode in new[] { "owned", "overlay", "zone", "floor", "attack", "gear", "support", "funding", "boss", "manual-route", "route-claim", "callback" })
        {
            var id = "obsolete-route-" + mode;
            SetupAdventureDynamic(id, "[1,1,0,1,0,0,0,0,0,0,0,0]");
            var settings = Get(MainType, "Settings"); var store = Get(MainType, "CustomPlanStore");
            var plan = Invoke(store, "Load", "fixture");
            var stage = Activator.CreateInstance(Plugin.GetType("NGUInjector.CustomPlans.CustomPlanStage", true));
            Set(stage, "Name", "Retiring baseline"); Set(stage, "PresetId", id); Set(stage, "Percent", 100d); Set(stage, "DurationSeconds", 600);
            var stages = Array.CreateInstance(stage.GetType(), 1); stages.SetValue(stage, 0); Set(plan, "Stages", stages);
            Set(plan, "StageSchedulerEnabled", true); Set(plan, "StageLoopStartIndex", -1);
            Set(plan, "AutoActivateFruits", false); Set(plan, "AutoRebirthEnabled", false); Set(plan, "TimeTriggerEnabled", false);
            Set(settings, "_useCustomRebirthPlan", true); Set(settings, "_autoRebirth", false);
            CallMain("RunCustomPlanAutomation", plan);
            var receipt = Get(MainInstance, "_globalPresetReceipt");
            Check(receipt != null, mode + " acquires a real scheduled farming baseline");
            Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), mode + " baseline has verified native prerequisites");
            var adventure = Get(Character, "adventure"); Equal(1000, Get(adventure, "zone"), mode + " starts in the actual baseline ITOPOD route");
            var entry = Property(Get(MainInstance, "_protectedScheduler"), "Entry");
            object claim = null;
            if (mode == "overlay" || mode == "manual-route")
            {
                Invoke(settings, "set_ManualAdventureEnabled", true);
                if (mode == "manual-route")
                {
                    PrepareAdventureDynamicManualRule();
                    CallMain("RouteManualAdventureZone", 0, false, "retirement ownership fixture");
                    Equal(0, Get(adventure, "zone"), "manual-route enters its actual marked native route");
                }
                else Set(MainInstance, "_manualAdventureSetupActive", true);
            }
            if (mode == "route-claim")
                claim = CallMain("BeginExecutionClaim", "current-route", "current-route-1", Domain("Route"), (Func<bool>)(() => true));
            RestartFixtureStage(plan);
            var scheduler = Get(MainInstance, "_protectedScheduler");
            Check(!Equals(entry, Property(scheduler, "Entry")), mode + " uses an explicit new entry, not reset-as-restart");
            Equal(null, Get(MainInstance, "_customStageEquipmentKey"), mode + " restart has withdrawn old forward stage authority");
            if (mode == "zone") Set(adventure, "zone", 1);
            if (mode == "floor") Set(adventure, "itopodEnd", (int)Get(adventure, "itopodEnd") + 1);
            if (mode == "attack") Set(adventure, "autoattacking", false);
            if (mode == "gear") Set(Get(Get(Character, "inventory"), "head"), "curAttack", 12345f);
            if (mode == "support") SetActiveBeards(3);
            if (mode == "funding")
            {
                var lane = ((IList)Get(Get(Character, "NGU"), "skills"))[0]; Set(lane, "energy", (long)Get(lane, "energy") + 1);
            }
            if (mode == "boss") Set(Get(Character, "bossController"), "isFighting", true);
            var before = Invoke(scheduler, "ExportState"); var zone = Get(adventure, "zone");
            var gear = Invoke(Plugin.GetType("NGUInjector.Managers.LoadoutManager", true), "CaptureExactLoadout");
            var support = CallMain("GlobalPresetSupportState"); var allocations = CallMain("GlobalPresetAllocationState");
            var searches = PresetSearches; var captures = PresetCaptures; var swaps = Get(typeof(Program), "Swaps");
            var transfers = ResourceTransfers; var routes = AdventureZoneEntries;
            if (mode == "callback") AdventureZoneEntryCompletedObserver = target => { if (target == -1) Set(adventure, "zone", 1); };
            var owned = mode == "owned" || mode == "overlay";
            var covered = mode == "manual-route" || mode == "route-claim";
            try { Equal(owned || covered, CallMain("RetirePreviousGlobalPresetStageRoute", "successor"), mode + " handoff requires exact predecessor retirement evidence"); }
            finally { AdventureZoneEntryCompletedObserver = null; }
            Equal(owned ? -1 : mode == "callback" ? 1 : zone, Get(adventure, "zone"), mode + " preserves every non-owned route");
            Equal(routes + (owned || mode == "callback" ? 1 : 0), AdventureZoneEntries, mode + " invokes only the exact necessary safe retirement");
            Equal(searches, PresetSearches, mode + " retirement never selects successor prerequisites");
            Equal(captures, PresetCaptures, mode + " retirement never captures a new planning snapshot");
            Equal(swaps, Get(typeof(Program), "Swaps"), mode + " retirement never flashes baseline gear");
            Equal(transfers, ResourceTransfers, mode + " retirement never clears resource lanes");
            Equal(true, Invoke(Plugin.GetType("NGUInjector.Managers.LoadoutManager", true), "ExactLoadoutMatches", gear), mode + " retains exact native equipment");
            Equal(support, CallMain("GlobalPresetSupportState"), mode + " retains current support");
            Equal(allocations, CallMain("GlobalPresetAllocationState"), mode + " retains current funding");
            Check(ReferenceEquals(receipt, Get(MainInstance, "_globalPresetReceipt")), mode + " retirement does not adopt a new receipt");
            foreach (var field in new[] { "entry", "cycle", "enteredAt", "expectedEnd", "durations", "earned", "applied", "visits" })
                Equal(Json(before, field).ToString(), Json(Invoke(scheduler, "ExportState"), field).ToString(), mode + " retirement preserves scheduler commitment/" + field);
            if (claim != null)
            {
                Equal(false, Get(claim, "Ended"), "baseline handoff cannot end another route claim");
                CallMain("EndExecutionClaim", claim, false);
            }
            if (!owned) continue;
            CallMain("RunCustomPlanAutomation", plan);
            Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), mode + " successor acquires only after observed safe retirement");
            Equal(searches + 1, PresetSearches, mode + " new entry selects exactly once");
            Equal(mode == "overlay", Property(settings, "ManualAdventureEnabled"), mode + " handoff preserves manual intent");
        }
    }

    private static void CheckExecutionApprovedMaterialStorageRetry()
    {
        var plan = SetupResourceCoordination(true, false, "none", nativeBoost: true);
        Set(MainInstance, "_invManager", Activator.CreateInstance(Plugin.GetType("NGUInjector.Managers.InventoryManager", true), true));
        var receipt = Get(MainInstance, "_globalPresetReceipt"); var policy = Get(receipt, "Policy");
        var acquisition = Get(policy, "Acquisition"); var entry = Property(Get(MainInstance, "_protectedScheduler"), "Entry");
        var directory = Get(MainType, "_dir"); var file = System.IO.Path.Combine((string)Get(typeof(Program), "Temp"), "core-material-storage-parent-file");
        System.IO.File.WriteAllText(file, "not a directory");
        var effects = AutoBoostCalls; var head = Get(Get(Character, "inventory"), "head");
        AutoBoostAction = () => ApplyPresetNativeBoost(head, expectedOutcome: "RescanRequired");
        try
        {
            Set(MainType, "_dir", file);
            CallMain("RunCustomAutoBoost");
            Equal(effects + 1, AutoBoostCalls, "owned material effect commits exactly once before storage failure");
            Equal(true, Get(policy, "MaterialPersistencePending"), "failed storage retains the known owned material revision");
            Equal(false, CallMain("GlobalPresetStageReceiptValid", plan), "unpersisted approved material grants no dependent credit");
            Throws(() => CallMain("RunCustomAutoBoost"), "could not be saved", "retry reports storage failure rather than invoking the effect again");
            Equal(effects + 1, AutoBoostCalls, "storage retry cannot repeat the committed inventory effect");
        }
        finally { Set(MainType, "_dir", directory); AutoBoostAction = null; }
        CallMain("RunCustomAutoBoost");
        Equal(false, Get(policy, "MaterialPersistencePending"), "storage recovery commits the SAME approved material");
        Equal(effects + 1, AutoBoostCalls, "successful persistence retry still invokes no inventory effect");
        Equal(acquisition, Get(policy, "Acquisition"), "material persistence never creates a replacement acquisition");
        Equal(entry, Property(Get(MainInstance, "_protectedScheduler"), "Entry"), "material storage failure cannot renew the entry or its budget");
        CallMain("RunCustomPlanAutomation", plan);
        Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), "current policy resumes only after durable material and native verification");
    }

    private static void CheckExecutionUncontrolledReservation()
    {
        SetupCommitBudget(manual: true);
        var writer = Plugin.GetType("NGUInjector.Managers.ResourceWriter", true);
        var seed = Activator.CreateInstance(Plugin.GetType("NGUInjector.Managers.ResourceSetupIntent", true), true);
        var map = (IDictionary)Invoke(writer, "EmptyDesiredMap", false, true, false);
        ((long[])map[Enum.Parse(Plugin.GetType("NGUInjector.Managers.ResourceTarget", true), "MagicNgu")])[0] = 900;
        var coordinator = MainInstance;
        Set(seed, "Owner", "manual-resource-seed"); Set(seed, "Magic", true); Set(seed, "Desired", map);
        Set(seed, "Authorized", (Func<bool>)(() => ReferenceEquals(Get(MainType, "reference"), coordinator)));
        Equal(true, Property(CallMain("ReconcileStageResourceSetup", seed), "Verified"), "uncontrolled reservation starts from actual manual allocations");
        var magic = Get(Character, "magic"); var energy = Get(Character, "curEnergy"); var idleEnergy = Get(Character, "idleEnergy");
        var claim = CallMain("BeginExecutionClaim", "no-baseline-fruit", "fruit-2", Domain("Magic"), (Func<bool>)(() => true));
        CallMain("SetExecutionResourceReservation", claim, 0L, 200L, 0L);
        Equal(true, Property(CallMain("ReconcileExecutionResourceClaim", claim), "Verified"), "explicit idle demand resolves without an invented baseline goal");
        Equal(200L, Get(magic, "idleMagic"), "uncontrolled reservation releases only the missing idle amount");
        Equal(800L, Get(((IList)Get(Get(Character, "NGU"), "magicSkills"))[0], "magic"), "unneeded manual work is preserved rather than recalling the whole lane");
        CallMain("EndExecutionClaim", claim, true);
        Equal(800L, Get(((IList)Get(Get(Character, "NGU"), "magicSkills"))[0], "magic"), "no successor does not restore a historical manual allocation");
        Equal(200L, Get(magic, "idleMagic"), "no goal does not invent an NGU fill target");
        var policyClaim = CallMain("BeginExecutionClaim", "no-baseline-pit", "feed-2", Domain("Magic"), (Func<bool>)(() => true));
        var policy = Activator.CreateInstance(Plugin.GetType("NGUInjector.Managers.ResourceSetupIntent", true), true);
        var ritualKey = Enum.Parse(Plugin.GetType("NGUInjector.Managers.ResourceTarget", true), "BloodRitual");
        var ritualMap = (IDictionary)Invoke(writer, "EmptyDesiredMap", false, true, false); ((long[])ritualMap[ritualKey])[0] = 25;
        Set(policy, "Owner", "no-baseline-pit"); Set(policy, "Magic", true); Set(policy, "Desired", ritualMap);
        CallMain("SetExecutionResourcePolicy", policyClaim, policy);
        Equal(true, Property(CallMain("ReconcileExecutionResourceClaim", policyClaim), "Verified"), "temporary whole-lane policy has explicit typed authority");
        Equal(true, CallMain("EndExecutionClaim", policyClaim, true), "no-successor policy exit releases only its still-owned temporary allocation");
        Equal(0L, Get(((IList)Get(Get(Character, "bloodMagic"), "ritual"))[0], "magic"), "ended pit preparation cannot leave ritual funding behind");
        Equal(1000L, Get(magic, "idleMagic"), "policy release conserves current pool without restoring old manual work");
        var writes = ResourceTransfers; CallMain("EndExecutionClaim", policyClaim, true);
        Equal(writes, ResourceTransfers, "verified release outcome prevents repeated cleanup accounting");
        Equal(energy, Get(Character, "curEnergy"), "Magic-only temporary demand preserves actual Energy current");
        Equal(idleEnergy, Get(Character, "idleEnergy"), "Magic-only temporary demand preserves actual Energy idle");
    }

    private static void CheckExecutionPausedPresetIdentity()
    {
        var plan = SetupResourceCoordination(true, false, "none");
        var store = Get(MainType, "CustomPlanStore"); Invoke(store, "Save", "fixture", plan);
        var original = Get(Get(MainInstance, "_globalPresetReceipt"), "Policy");
        var acquisition = Get(original, "Acquisition"); var definition = Get(original, "Definition");
        var scheduler = Get(MainInstance, "_protectedScheduler"); var entry = Property(scheduler, "Entry");
        var inventory = Get(Character, "inventory"); var head = Get(inventory, "head");
        Set(Get(Get(Character, "buttons"), "adventure"), "m_Interactable", true);
        var replacement = ((IList)Get(inventory, "inventory"))[0];
        Set(replacement, "id", 550); Set(replacement, "type", Get(head, "type")); Set(replacement, "removable", true); Set(replacement, "bossRequired", 1);
        Set(replacement, "curAttack", 1000000f); Set(replacement, "curDefense", 1000000f);
        var library = Get(store, "<PresetLibrary>k__BackingField"); var root = Invoke(library, "ReadExternalLibrary");
        var id = (string)Get(Get(original, "Preset"), "Id");
        var row = ((IEnumerable)GetChildren(Json(root, "Presets"))).Cast<object>().Single(x => Property(Json(x, "Id"), "Value").Equals(id));
        SetJson(row, "Weights", JsonParse("[1,1,0,0,0,0,0,0,0,0,100,0,0,0]"));
        var revision = Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
        var args = JsonParse("{\"presetId\":\"" + id + "\",\"libraryRevision\":\"" + revision + "\"}");
        var preview = CallMain("PreviewGlobalPreset", args);
        var apply = JsonParse(args.ToString().TrimEnd('}') + ",\"previewId\":" + Json(preview, "previewId") + ",\"candidateKey\":\"balanced\"}");
        CallMain("ApplyGlobalPreset", apply, null);
        var dashboard = Get(Get(MainInstance, "_globalPresetReceipt"), "Policy");
        Check(!ReferenceEquals(original, dashboard), "same-ID dashboard takeover has its own independently acquired definition");
        var searches = PresetSearches;
        Invoke(Get(MainType, "Settings"), "set_UseCustomRebirthPlan", true);
        CallMain("ReconcileCustomResources", plan, false);
        Check(ReferenceEquals(original, Get(Get(MainInstance, "_globalPresetReceipt"), "Policy")), "resume resolves original paused acquisition, not same-ID recipe cache or current library");
        Equal(acquisition, Get(original, "Acquisition"), "paused resume preserves original acquisition identity");
        Equal(definition, Get(original, "Definition"), "paused resume preserves original definition");
        Equal(entry, Property(scheduler, "Entry"), "paused resume cannot create a new entry or budget");
        Equal(searches, PresetSearches, "paused AutoUpdate-false resume performs no replacement search");
        Equal(null, Get(MainInstance, "_standaloneSelectedPolicy"), "normal plan resume retires superseded session-only dashboard intent");
        Equal(false, System.IO.File.Exists(System.IO.Path.Combine((string)Get(MainType, "_dir"), "execution-selection.json")), "dashboard takeover and resume never create retired restart intent");
    }

    private static void CheckExecutionPresetUncontrolledLanes()
    {
        foreach (var goal in new[] { "energy", "magic", "gear" })
        {
            SetupResourceCoordination(false, false, "none");
            var writer = Plugin.GetType("NGUInjector.Managers.ResourceWriter", true);
            var seed = Activator.CreateInstance(Plugin.GetType("NGUInjector.Managers.ResourceSetupIntent", true), true);
            var desired = (IDictionary)Invoke(writer, "EmptyDesiredMap", true, true, false);
            var target = Plugin.GetType("NGUInjector.Managers.ResourceTarget", true);
            ((long[])desired[Enum.Parse(target, "EnergyNgu")])[0] = 337;
            ((long[])desired[Enum.Parse(target, "MagicNgu")])[0] = 237;
            Set(seed, "Owner", "manual-lanes"); Set(seed, "Energy", true); Set(seed, "Magic", true); Set(seed, "Desired", desired);
            Set(seed, "Authorized", (Func<bool>)(() => true));
            Equal(true, Property(CallMain("ReconcileStageResourceSetup", seed), "Verified"), goal + " starts from exact current manual allocations");
            var energyBefore = CallMain("GlobalPresetLaneState", true); var magicBefore = CallMain("GlobalPresetLaneState", false);
            var energyPool = Get(Character, "curEnergy"); var energyIdle = Get(Character, "idleEnergy");
            var magic = Get(Character, "magic"); var magicPool = Get(magic, "curMagic"); var magicIdle = Get(magic, "idleMagic");
            if (goal == "gear")
            {
                var inventory = Get(Character, "inventory"); var head = Get(inventory, "head");
                var replacement = ((IList)Get(inventory, "inventory"))[0];
                Set(replacement, "id", 550); Set(replacement, "type", Get(head, "type"));
                Set(replacement, "removable", true); Set(replacement, "bossRequired", 1);
                Set(replacement, "spec1Type", Enum.Parse(Field(replacement, "spec1Type").FieldType, "EnergyCap"));
                Set(replacement, "spec1Cur", 10000f); Set(replacement, "spec1Cap", 10000000f);
            }
            var library = Get(Get(MainType, "CustomPlanStore"), "<PresetLibrary>k__BackingField");
            var root = Invoke(library, "ReadExternalLibrary"); var id = "uncontrolled-" + goal;
            var weights = goal == "energy" ? "[1,0,0,0,0,0]" : goal == "magic" ? "[0,1,0,0,0,0]" : "[0,0,0,0,0,0,0,0,0,0,0,0,1,0]";
            Invoke(Json(root, "Presets"), "Add", "", JsonParse("{\"Id\":\"" + id + "\",\"Name\":\"Uncontrolled lanes\",\"Weights\":" + weights
                + ",\"SupportMode\":\"current\",\"SwapSeconds\":0,\"AutoUpdate\":false}"));
            var revision = Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
            var args = JsonParse("{\"presetId\":\"" + id + "\",\"libraryRevision\":\"" + revision + "\"}");
            var preview = CallMain("PreviewGlobalPreset", args);
            var apply = JsonParse(args.ToString().TrimEnd('}') + ",\"previewId\":" + Json(preview, "previewId")
                + ",\"candidateKey\":\"" + (goal == "gear" ? "balanced" : "current") + "\"}");
            CallMain("ApplyGlobalPreset", apply, null);
            if (goal == "gear") Equal(550, Get(Get(Get(Character, "inventory"), "head"), "id"), "capacity-only setup performs a real gear transition without recalling unclaimed lanes");
            foreach (var refresh in new[] { false, true })
            {
                if (refresh) Equal(true, CallMain("RefreshGlobalPresetAllocations", "dashboard"), goal + " replays acquired lane intent");
                if (goal != "energy")
                {
                    Equal(energyBefore, CallMain("GlobalPresetLaneState", true), goal + " preserves unclaimed Energy destinations/refresh=" + refresh);
                    Equal(energyPool, Get(Character, "curEnergy"), goal + " preserves unclaimed Energy pool");
                    Equal(energyIdle, Get(Character, "idleEnergy"), goal + " preserves unclaimed Energy idle");
                }
                if (goal != "magic")
                {
                    Equal(magicBefore, CallMain("GlobalPresetLaneState", false), goal + " preserves unclaimed Magic destinations/refresh=" + refresh);
                    Equal(magicPool, Get(magic, "curMagic"), goal + " preserves unclaimed Magic pool");
                    Equal(magicIdle, Get(magic, "idleMagic"), goal + " preserves unclaimed Magic idle");
                }
            }
            var receipt = Get(MainInstance, "_globalPresetReceipt"); var execution = Get(receipt, "Execution");
            if (goal == "energy")
            {
                var settings = Get(MainType, "Settings");
                Set(settings, "_customAutoBloodSpellsEnabled", true); Set(settings, "_customBloodSpaghettiGoalPercent", 50);
                Equal(true, CallMain("RefreshGlobalPresetAllocations", "dashboard"), "explicit temporary blood demand may control an otherwise unclaimed Magic lane");
                var ritual = ((IList)Get(Get(Character, "bloodMagic"), "ritual"))[0];
                Check((long)Get(ritual, "magic") > 0, "temporary blood demand actually funds its native ritual");
                Set(settings, "_customAutoBloodSpellsEnabled", false);
                Equal(true, CallMain("RefreshGlobalPresetAllocations", "dashboard"), "ending temporary blood releases its still-owned policy before leaving Magic uncontrolled");
                Equal(0L, Get(ritual, "magic"), "no baseline Magic objective cannot leave temporary ritual funding behind");
                Equal(1000L, Get(magic, "idleMagic"), "temporary release preserves the current pool without restoring historical manual work");
                var manual = ((IList)Get(Get(Character, "NGU"), "magicSkills"))[0];
                Set(manual, "magic", 239L); Set(magic, "idleMagic", 761L);
                Equal(true, CallMain("RefreshGlobalPresetAllocations", "dashboard"), "verified retirement leaves Magic uncontrolled on later service");
                Equal(239L, Get(manual, "magic"), "later manual work is not mistaken for a temporary policy to release");
                Set(settings, "_customAutoBloodSpellsEnabled", true);
                Equal(true, CallMain("RefreshGlobalPresetAllocations", "dashboard"), "a later explicit blood request obtains its own current funding");
                var funded = (long)Get(ritual, "magic");
                Set(ritual, "magic", funded + 1); Set(magic, "idleMagic", (long)Get(magic, "idleMagic") - 1);
                Set(settings, "_customAutoBloodSpellsEnabled", false);
                var transfers = ResourceTransfers;
                Equal(false, CallMain("RefreshGlobalPresetAllocations", "dashboard"), "foreign temporary funding cannot be released using stale proof");
                Equal(funded + 1, Get(ritual, "magic"), "failed retirement preserves the foreign allocation");
                Equal(transfers, ResourceTransfers, "foreign retirement performs no resource writes");
                Equal(2, Property(Get(Property(MainInstance, "_resourceSetupExecutor"), "Application"), "BlockedLanes"), "retirement uncertainty is scoped to Magic");
                magicBefore = CallMain("GlobalPresetLaneState", false);
            }
            if (goal != "gear")
            {
                var empty = Invoke(execution, "Copy");
                Set(empty, "EnergyShares", new long[((long[])Get(execution, "EnergyShares")).Length]);
                Set(empty, "MagicShares", new long[((long[])Get(execution, "MagicShares")).Length]);
                CallMain("ApplyLoadoutPresetAllocations", empty); CallMain("VerifyLoadoutPresetAllocations", empty);
                var lanes = (IList)Get(Get(Character, "NGU"), goal == "energy" ? "skills" : "magicSkills");
                Equal(0L, lanes.Cast<object>().Sum(x => (long)Get(x, goal == "energy" ? "energy" : "magic")), goal + " explicit controlled-empty remains a clearing decision");
                Equal(goal == "energy" ? magicBefore : energyBefore, CallMain("GlobalPresetLaneState", goal != "energy"), goal + " controlled-empty cannot clear the other lane");
            }
        }
    }

    private static object SetupExecutionStopPreset(string goal, bool focusReady = false)
    {
        var plan = focusReady ? SetupInlineStagePlan() : SetupResourceCoordination(false, false, "none");
        var training = Get(Character, "training");
        var basic = 7 - ((long[])Property(training, "attackEnergy"))[0];
        ((long[])Property(training, "attackEnergy"))[0] = 7;
        var fundedEnergy = ((IList)Get(Get(Character, "NGU"), "skills"))[0];
        var borrowed = Math.Min(basic, (long)Get(fundedEnergy, "energy"));
        Set(fundedEnergy, "energy", (long)Get(fundedEnergy, "energy") - borrowed);
        Set(Character, "idleEnergy", (long)Get(Character, "idleEnergy") - (basic - borrowed));
        var wishes = Get(Character, "wishes"); ListField(wishes, "wishes", 512);
        ListField(Get(Character, "hacks"), "hacks", 16);
        Set(((IList)Get(wishes, "wishes"))[0], "res3", 300L);
        var res3 = Get(Character, "res3"); Set(res3, "capRes3", 1000L); Set(res3, "curRes3", 731L); Set(res3, "idleRes3", 431L);
        Invoke(Get(MainType, "Settings"), "set_ManageR3", true);
        var writer = Plugin.GetType("NGUInjector.Managers.ResourceWriter", true);
        var desired = (IDictionary)Invoke(writer, "EmptyDesiredMap", true, true, false);
        var target = Plugin.GetType("NGUInjector.Managers.ResourceTarget", true);
        ((long[])desired[Enum.Parse(target, "EnergyNgu")])[0] = 337;
        ((long[])desired[Enum.Parse(target, "MagicNgu")])[0] = 237;
        var seed = Activator.CreateInstance(Plugin.GetType("NGUInjector.Managers.ResourceSetupIntent", true), true);
        Set(seed, "Owner", "manual-before-stop"); Set(seed, "Energy", true); Set(seed, "Magic", true); Set(seed, "Desired", desired);
        Set(seed, "Authorized", (Func<bool>)(() => true));
        var seeded = CallMain("ReconcileStageResourceSetup", seed);
        Equal(true, Property(seeded, "Verified"), goal + " stop starts from conserved native manual work; " + Get(seeded, "Reason"));
        var library = Get(Get(MainType, "CustomPlanStore"), "<PresetLibrary>k__BackingField");
        var root = Invoke(library, "ReadExternalLibrary"); var id = "stop-" + goal + "-" + Guid.NewGuid().ToString("N");
        var weights = goal == "energy" ? "[1,0,0,0,0,0]" : goal == "magic" ? "[0,1,0,0,0,0]" : "[0,0,0,0,0,0,0,0,0,0,0,0,1,0]";
        Invoke(Json(root, "Presets"), "Add", "", JsonParse("{\"Id\":\"" + id + "\",\"Name\":\"Stop lane ownership\",\"Weights\":" + weights
            + ",\"SupportMode\":\"current\",\"SwapSeconds\":0,\"AutoUpdate\":false}"));
        var revision = Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
        var args = JsonParse("{\"presetId\":\"" + id + "\",\"libraryRevision\":\"" + revision + "\"}");
        var preview = CallMain("PreviewGlobalPreset", args);
        CallMain("ApplyGlobalPreset", JsonParse(args.ToString().TrimEnd('}') + ",\"previewId\":" + Json(preview, "previewId") + ",\"candidateKey\":\"current\"}"), null);
        Equal(true, CallMain("GlobalPresetReceiptValid", "dashboard"), goal + " stop has an actually verified acquired preset");
        return plan;
    }

    private static string ExecutionStopLaneState(bool energy) => CallMain("GlobalPresetLaneState", energy) + "|"
        + (energy ? Get(Character, "curEnergy") : Get(Get(Character, "magic"), "curMagic")) + "|"
        + (energy ? Get(Character, "idleEnergy") : Get(Get(Character, "magic"), "idleMagic"));

    private static string ExecutionStopProtectedState() => string.Join(",", (long[])Property(Get(Character, "training"), "attackEnergy")) + "|"
        + string.Join(",", (long[])Property(Get(Character, "training"), "defenseEnergy")) + "|"
        + Get(Get(Character, "res3"), "curRes3") + "|" + Get(Get(Character, "res3"), "idleRes3") + "|"
        + string.Join(",", ((IList)Get(Get(Character, "wishes"), "wishes")).Cast<object>().Select(x => Get(x, "res3"))) + "|"
        + string.Join(",", ((IList)Get(Get(Character, "hacks"), "hacks")).Cast<object>().Select(x => Get(x, "res3")));

    private static void CheckExecutionUnmanagedCapacityPreset()
    {
        foreach (var ingress in new[] { "ordinary", "preview", "equip", "scheduled", "capacity-loss", "context", "foreign" })
        {
            var plan = SetupExecutionStopPreset("capacity", true);
            var settings = Get(MainType, "Settings");
            var receipt = Get(MainInstance, "_globalPresetReceipt"); var id = (string)Get(receipt, "Id");
            var library = Get(Get(MainType, "CustomPlanStore"), "<PresetLibrary>k__BackingField");
            var root = Invoke(library, "ReadExternalLibrary");
            var revision = Property(Json(root, "__externalUiRevision"), "Value");
            var args = JsonParse("{\"presetId\":\"" + id + "\",\"libraryRevision\":\"" + revision + "\"}");
            Set(settings, "_manageEnergy", false); Set(settings, "_manageMagic", false);
            var inventory = Get(Character, "inventory"); var originalHead = Get(inventory, "head");
            if (ingress == "capacity-loss")
            {
                var magic = Get(Character, "magic");
                Set(magic, "curMagic", 1001L); Set(magic, "idleMagic", (long)Get(magic, "idleMagic") + 1);
            }
            var energyBefore = ExecutionStopLaneState(true); var magicBefore = ExecutionStopLaneState(false);
            var protectedBefore = ExecutionStopProtectedState(); var writes = ResourceTransfers;
            var recalls = Get(typeof(Program), "Recalls"); var magicRecalls = Get(typeof(Program), "MagicRecalls");
            var swaps = (int)Get(typeof(Program), "Swaps");
            var failed = ingress == "capacity-loss" || ingress == "context" || ingress == "foreign";
            if (ingress == "ordinary" || ingress == "scheduled")
            {
                if (ingress == "scheduled")
                {
                    Set(((Array)Get(plan, "Stages")).GetValue(0), "PresetId", id);
                    Set(settings, "_useCustomRebirthPlan", true);
                    Set(MainInstance, "_customRuntimePlanCache", plan); Set(MainInstance, "_customRuntimePlanCacheName", Property(settings, "CustomPlanFile"));
                    Set(MainInstance, "_customRuntimePlanCacheLoadedAt", DateTime.Now);
                }
                CallMain("ReconcileCustomResources", plan, true);
            }
            else if (ingress == "equip") CallMain("EquipGlobalPreset", args, null);
            else
            {
                var preview = CallMain("PreviewGlobalPreset", args);
                var apply = JsonParse(args.ToString().TrimEnd('}') + ",\"previewId\":" + Json(preview, "previewId") + ",\"candidateKey\":\"balanced\"}");
                if (ingress == "context" || ingress == "foreign") GearRenderObserver = () => {
                    GearRenderObserver = null;
                    if (ingress == "context") Set(Character, "magic", Invoke(Get(Character, "magic"), "MemberwiseClone"));
                    else
                    {
                        var skill = ((IList)Get(Get(Character, "NGU"), "magicSkills"))[0];
                        Set(skill, "magic", (long)Get(skill, "magic") + 1);
                        Set(Get(Character, "magic"), "idleMagic", (long)Get(Get(Character, "magic"), "idleMagic") - 1);
                    }
                };
                try
                {
                    if (failed) Throws(() => CallMain("ApplyGlobalPreset", apply, null), ingress == "capacity-loss" ? "capacity" : "authority", ingress + " preserving capacity setup rejects unsafe native state");
                    else CallMain("ApplyGlobalPreset", apply, null);
                }
                finally { GearRenderObserver = null; }
            }
            if (!failed)
            {
                CallMain("ReconcileCustomResources", plan, true);
                var current = Get(MainInstance, "_globalPresetReceipt"); var owner = (string)Get(current, "Owner");
                var runtime = CallMain("GetResourceCoordinationRuntime");
                Equal("Applied", Property(Json(runtime, "state"), "Value"), ingress + " capacity-only ordinary service must actually be Applied: " + Json(runtime, "reason"));
                Equal(true, CallMain("GlobalPresetReceiptValid", owner), ingress + " capacity-only native receipt verifies with both lanes unmanaged");
                Equal(true, CallMain("ExecutionSetupVerified"), ingress + " selected gear/support actually verify");
                var execution = Get(current, "Execution");
                Equal(false, Get(execution, "ControlsEnergy"), ingress + " capacity intent has no Energy control");
                Equal(false, Get(execution, "ControlsMagic"), ingress + " capacity intent has no Magic control");
                var resourceIntent = Get(MainInstance, "_executionBaselineResourceIntent");
                Equal(false, Get(resourceIntent, "Energy"), ingress + " ordinary executor acquires no Energy mutation footprint");
                Equal(false, Get(resourceIntent, "Magic"), ingress + " ordinary executor acquires no Magic mutation footprint");
                Equal(0, Convert.ToInt32(Get(Get(Get(MainInstance, "_selectedSetup"), "Grant"), "Domains")) & 3,
                    ingress + " actual selected-setup grant contains neither resource lane");
                if (ingress == "preview")
                {
                    Equal(550, Get(Get(inventory, "head"), "id"), "unmanaged capacity acquisition performs the actual larger-capacity gear swap");
                    Check((int)Get(typeof(Program), "Swaps") > swaps, "unmanaged capacity acquisition reaches the native gear actuator");
                }
                var acquisition = Get(Get(current, "Policy"), "Acquisition"); var searches = PresetSearches;
                if (ingress == "ordinary")
                {
                    root = Invoke(library, "ReadExternalLibrary");
                    var row = ((IEnumerable)GetChildren(Json(root, "Presets"))).Cast<object>().Single(x => Property(Json(x, "Id"), "Value").Equals(id));
                    SetJson(row, "Weights", JsonParse("[1,1,0,0,0,0]"));
                    Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
                }
                CallMain("ReconcileCustomResources", plan, true);
                Equal("Applied", Property(Json(CallMain("GetResourceCoordinationRuntime"), "state"), "Value"), ingress + " acquired capacity intent remains Applied on ordinary replay");
                Equal(acquisition, Get(Get(Get(MainInstance, "_globalPresetReceipt"), "Policy"), "Acquisition"), ingress + " ordinary replay retains its acquired capacity policy");
                Equal(searches, PresetSearches, ingress + " ordinary replay never reselects gear");
            }
            else
            {
                if (ingress == "capacity-loss")
                {
                    Check(ReferenceEquals(receipt, Get(MainInstance, "_globalPresetReceipt")), "unsafe capacity rejection preserves the previous acquisition without issuing a new receipt");
                    Equal(swaps, Get(typeof(Program), "Swaps"), "unsafe actual capacity loss rejects before the first native gear write");
                    Check(ReferenceEquals(originalHead, Get(inventory, "head")), "unsafe capacity loss preserves actual equipped gear");
                }
                else Equal(false, CallMain("GlobalPresetReceiptValid", "dashboard"), ingress + " rejected setup receives no valid receipt");
            }
            Equal(energyBefore, ExecutionStopLaneState(true), ingress + " preserves every native Energy destination/current/idle");
            if (ingress == "foreign")
            {
                var skill = ((IList)Get(Get(Character, "NGU"), "magicSkills"))[0];
                Equal(238L, Get(skill, "magic"), "foreign callback's native Magic allocation is preserved, never repaired without authority");
                Equal(762L, Get(Get(Character, "magic"), "idleMagic"), "foreign callback's exact idle Magic is preserved");
            }
            else Equal(magicBefore, ExecutionStopLaneState(false), ingress + " preserves every native Magic destination/current/idle");
            Equal(protectedBefore, ExecutionStopProtectedState(), ingress + " preserves Basic Training and managed R3 allocations/current/idle");
            Equal(writes, ResourceTransfers, ingress + " capacity policy never acquires resource model writes");
            Equal(recalls, Get(typeof(Program), "Recalls"), ingress + " never recalls Energy");
            Equal(magicRecalls, Get(typeof(Program), "MagicRecalls"), ingress + " never recalls Magic");
            Equal(false, Property(settings, "ManageEnergy"), ingress + " does not enable Energy management");
            Equal(false, Property(settings, "ManageMagic"), ingress + " does not enable Magic management");
        }
        foreach (var energy in new[] { true, false })
        {
            var plan = SetupExecutionStopPreset(energy ? "energy" : "magic", true);
            var receipt = Get(MainInstance, "_globalPresetReceipt"); var candidate = Get(Get(receipt, "Policy"), "Candidate");
            Check(((long[])Get(candidate, energy ? "EnergyShares" : "MagicShares")).Any(x => x > 0), "permission fixture has real positive selected lane intent");
            var library = Get(Get(MainType, "CustomPlanStore"), "<PresetLibrary>k__BackingField"); var root = Invoke(library, "ReadExternalLibrary");
            var args = JsonParse("{\"presetId\":\"" + Get(receipt, "Id") + "\",\"libraryRevision\":\"" + Property(Json(root, "__externalUiRevision"), "Value") + "\"}");
            var preview = CallMain("PreviewGlobalPreset", args);
            var apply = JsonParse(args.ToString().TrimEnd('}') + ",\"previewId\":" + Json(preview, "previewId") + ",\"candidateKey\":\"current\"}");
            var settings = Get(MainType, "Settings"); Set(settings, energy ? "_manageEnergy" : "_manageMagic", false);
            var beforeEnergy = ExecutionStopLaneState(true); var beforeMagic = ExecutionStopLaneState(false); var protectedState = ExecutionStopProtectedState();
            var writes = ResourceTransfers; var swaps = Get(typeof(Program), "Swaps");
            CallMain("ReconcileCustomResources", plan, true);
            Equal("Waiting", Property(Json(CallMain("GetResourceCoordinationRuntime"), "state"), "Value"), "controlled positive lane cannot become Applied using the other lane's permission");
            Equal(false, CallMain("GlobalPresetReceiptValid", "dashboard"), "controlled lane permission loss invalidates dependent native receipt verification");
            Throws(() => CallMain("ApplyGlobalPreset", apply, null), "requires managed " + (energy ? "Energy" : "Magic"), "Preview/Apply requires the selected positive lane's own permission");
            var empty = Invoke(candidate, "Copy");
            Set(empty, "EnergyShares", new long[((long[])Get(candidate, "EnergyShares")).Length]);
            Set(empty, "MagicShares", new long[((long[])Get(candidate, "MagicShares")).Length]);
            Check(CallMain("LoadoutPresetApplyBlocker", empty) != null, "controlled-empty intent still requires its own lane permission");
            CallMain("ApplyLoadoutPresetAllocations", empty);
            Equal(beforeEnergy, ExecutionStopLaneState(true), "unmanaged positive/empty admission preserves all native Energy/current/idle");
            Equal(beforeMagic, ExecutionStopLaneState(false), "unmanaged positive/empty admission preserves all native Magic/current/idle");
            Equal(protectedState, ExecutionStopProtectedState(), "unmanaged positive/empty admission preserves Basic Training and R3");
            Equal(writes, ResourceTransfers, "controlled-empty without permission never acquires a clearing resource write");
            Equal(swaps, Get(typeof(Program), "Swaps"), "lane permission rejection occurs before gear mutation");
        }
    }

    private static void CheckExecutionPresetStopLanes()
    {
        foreach (var goal in new[] { "energy", "magic", "capacity" })
        foreach (var foreign in new[] { false, true })
        {
            SetupExecutionStopPreset(goal);
            if (foreign)
            {
                var energy = goal != "magic"; var skills = (IList)Get(Get(Character, "NGU"), energy ? "skills" : "magicSkills");
                Set(skills[0], energy ? "energy" : "magic", (long)Get(skills[0], energy ? "energy" : "magic") - 1);
                Set(skills[1], energy ? "energy" : "magic", (long)Get(skills[1], energy ? "energy" : "magic") + 1);
            }
            var beforeEnergy = ExecutionStopLaneState(true); var beforeMagic = ExecutionStopLaneState(false);
            var protectedState = ExecutionStopProtectedState();
            var energyPool = Get(Character, "curEnergy"); var magicPool = Get(Get(Character, "magic"), "curMagic");
            CallMain("StopCustomFocusGoal");
            foreach (var energy in new[] { true, false })
            {
                var controlled = energy ? goal == "energy" : goal == "magic";
                if (!controlled || foreign)
                    Equal(energy ? beforeEnergy : beforeMagic, ExecutionStopLaneState(energy), goal + " stop preserves actual uncontrolled/foreign lane including current/idle/energy=" + energy);
                else
                {
                    Check(((string)CallMain("GlobalPresetLaneState", energy)).Split(',').All(x => x == "0"), goal + " stop releases all still-owned destinations in its declared lane");
                    Equal(energy ? (long)energyPool - 7 : magicPool, energy ? Get(Character, "idleEnergy") : Get(Get(Character, "magic"), "idleMagic"), goal + " stop returns current owned amounts to idle, not a prior pool");
                }
            }
            Equal(energyPool, Get(Character, "curEnergy"), goal + " stop preserves current Energy");
            Equal(magicPool, Get(Get(Character, "magic"), "curMagic"), goal + " stop preserves current Magic");
            Equal(protectedState, ExecutionStopProtectedState(), goal + " stop preserves basic training and managed R3 exactly");
            Equal(null, Get(MainInstance, "_globalPresetManualId"), goal + " stop withdraws only the dashboard intent");
            var writes = ResourceTransfers; var afterEnergy = ExecutionStopLaneState(true); var afterMagic = ExecutionStopLaneState(false);
            CallMain("StopCustomFocusGoal");
            Equal(writes, ResourceTransfers, goal + " duplicate stop cannot repeat accounting");
            Equal(afterEnergy, ExecutionStopLaneState(true), goal + " duplicate stop preserves actual Energy");
            Equal(afterMagic, ExecutionStopLaneState(false), goal + " duplicate stop preserves actual Magic");
        }
        foreach (var goal in new[] { "energy", "magic" })
        {
            SetupExecutionStopPreset(goal);
            var receipt = Get(MainInstance, "_globalPresetReceipt"); var empty = Invoke(Get(receipt, "Execution"), "Copy");
            Set(empty, "EnergyShares", new long[((long[])Get(empty, "EnergyShares")).Length]);
            Set(empty, "MagicShares", new long[((long[])Get(empty, "MagicShares")).Length]);
            CallMain("ApplyLoadoutPresetAllocations", empty); CallMain("VerifyLoadoutPresetAllocations", empty);
            CallMain("ObserveGlobalPresetApplication", receipt, empty, null, Enum.Parse(MainType.GetNestedType("PresetApplicationIssue", All), "None"));
            var energy = ExecutionStopLaneState(true); var magic = ExecutionStopLaneState(false); var writes = ResourceTransfers;
            CallMain("StopCustomFocusGoal");
            Equal(energy, ExecutionStopLaneState(true), goal + " controlled-empty stop preserves current Energy and idle");
            Equal(magic, ExecutionStopLaneState(false), goal + " controlled-empty stop preserves current Magic and idle");
            Equal(writes, ResourceTransfers, goal + " controlled-empty stop never invents writes in the other lane");
        }
    }

    private static void CheckExecutionPresetStopContinuation()
    {
        var plan = SetupExecutionStopPreset("energy");
        Set(Get(MainType, "Settings"), "_useCustomRebirthPlan", true);
        var energy = ExecutionStopLaneState(true); var magic = ExecutionStopLaneState(false); var protectedState = ExecutionStopProtectedState();
        var writes = ResourceTransfers;
        CallMain("StopManualGlobalPreset");
        Equal(energy, ExecutionStopLaneState(true), "a current normal-plan successor prevents outgoing dashboard Energy release");
        Equal(magic, ExecutionStopLaneState(false), "a current normal-plan successor prevents outgoing dashboard Magic release");
        Equal(writes, ResourceTransfers, "dashboard stop cannot clear resources underneath current normal intent");
        CallMain("RunCustomPlanAutomation", plan);
        Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), "normal successor applies its current acquired goals after dashboard withdrawal");
        Equal(0L, Get(Character, "idleEnergy"), "current successor funds actual Energy instead of restoring dashboard/manual allocations");
        Equal(0L, Get(Get(Character, "magic"), "idleMagic"), "current successor funds actual Magic");
        Equal(protectedState, ExecutionStopProtectedState(), "successor handoff preserves basic/R3 allocations and pools");

        SetupExecutionStopPreset("energy");
        var receipt = Get(MainInstance, "_globalPresetReceipt"); var execution = Invoke(Get(receipt, "Execution"), "Copy");
        var activities = New("PresetActivityAllocation"); var at = (long[])Get(activities, "AT"); at[0] = 123;
        Set(execution, "Activities", activities);
        var shares = ((long[])Get(execution, "EnergyShares")).ToArray(); shares[0] -= 123; Set(execution, "EnergyShares", shares);
        CallMain("ApplyLoadoutPresetAllocations", execution); CallMain("VerifyLoadoutPresetAllocations", execution);
        CallMain("ObserveGlobalPresetApplication", receipt, execution, null, Enum.Parse(MainType.GetNestedType("PresetApplicationIssue", All), "None"));
        magic = ExecutionStopLaneState(false); protectedState = ExecutionStopProtectedState(); var reached = false;
        AtRefreshObserver = () => reached = true; FailAtRefresh = true;
        try { CallMain("StopCustomFocusGoal"); }
        finally { AtRefreshObserver = null; FailAtRefresh = false; }
        Equal(true, reached, "stop partial-return case reaches an actual funded AT renderer");
        Equal("ModelAppliedRenderFailed", Get(Property(Get(Property(MainInstance, "_resourceSetupExecutor"), "Application"), "Last"), "Disposition").ToString(), "stopped preset retains model-committed rendering failure without replaying accounting");
        Equal(0L, ((long[])Get(Get(Character, "advancedTraining"), "energy")).Sum(), "render failure cannot restore released AT funding");
        Equal((long)Get(Character, "curEnergy") - 7, Get(Character, "idleEnergy"), "render failure still conserves the released current pool");
        Equal(magic, ExecutionStopLaneState(false), "partial stop cannot acquire the otherwise unchanged uncontrolled Magic lane");
        Equal(protectedState, ExecutionStopProtectedState(), "partial stop preserves basic/R3");
        writes = ResourceTransfers; CallMain("StopCustomFocusGoal");
        Equal(writes, ResourceTransfers, "retry after partial stop does not repeat committed resource accounting");

        SetupExecutionStopPreset("energy");
        var settings = Get(MainType, "Settings");
        Set(settings, "_customAutoBloodSpellsEnabled", true); Set(settings, "_customBloodSpaghettiGoalPercent", 50);
        Equal(true, CallMain("RefreshGlobalPresetAllocations", "dashboard"), "stop temporary-demand case obtains actual explicit Magic funding");
        var ritual = ((IList)Get(Get(Character, "bloodMagic"), "ritual"))[0];
        Check((long)Get(ritual, "magic") > 0, "stop temporary-demand case reaches a funded native ritual");
        protectedState = ExecutionStopProtectedState();
        CallMain("StopCustomFocusGoal");
        Equal(0L, Get(ritual, "magic"), "stop can retire an explicitly declared still-owned temporary Magic policy");
        Equal(Get(Get(Character, "magic"), "curMagic"), Get(Get(Character, "magic"), "idleMagic"), "temporary Magic stop conserves the current pool");
        Equal(protectedState, ExecutionStopProtectedState(), "temporary Magic stop preserves basic/R3");

        SetupExecutionStopPreset("energy", true);
        receipt = Get(MainInstance, "_globalPresetReceipt"); execution = Invoke(Get(receipt, "Execution"), "Copy");
        activities = New("PresetActivityAllocation"); ((long[])Get(activities, "AT"))[0] = 123; Set(execution, "Activities", activities);
        shares = ((long[])Get(execution, "EnergyShares")).ToArray(); shares[0] -= 123; Set(execution, "EnergyShares", shares);
        CallMain("ApplyLoadoutPresetAllocations", execution); CallMain("VerifyLoadoutPresetAllocations", execution);
        CallMain("ObserveGlobalPresetApplication", receipt, execution, null, Enum.Parse(MainType.GetNestedType("PresetApplicationIssue", All), "None"));
        reached = false; object successor = null; string successorMagic = null;
        AtRefreshObserver = () => {
            reached = true; AtRefreshObserver = null;
            CallMain("ApplyCustomFocusGoal", Enum.Parse(Plugin.GetType("NGUInjector.CustomFocusGoal", true), "MagicNgu"));
            Equal("applied", CallMain("GetCustomFocusStatus"), "dashboard release callback actually acquires and funds successor focus");
            successor = Get(MainInstance, "_focusSetupSelection"); successorMagic = ExecutionStopLaneState(false);
        };
        try { CallMain("StopCustomFocusGoal"); }
        finally { AtRefreshObserver = null; }
        Equal(true, reached, "dashboard-to-focus successor occurs inside an actual native stop callback");
        Check(ReferenceEquals(successor, Get(MainInstance, "_focusSetupSelection")), "Quick stop cannot consume focus intent installed while its dashboard release runs");
        Equal("MagicNgu", CallMain("GetActiveCustomFocusGoal").ToString(), "Quick stop remains bound to the outgoing dashboard, not a new focus request");
        Equal(successorMagic, ExecutionStopLaneState(false), "Quick stop preserves the successor focus's current Magic allocations and idle");
    }

    private static void CheckExecutionFocusStopLanes()
    {
        SetupInlineStagePlan();
        var goalType = Plugin.GetType("NGUInjector.CustomFocusGoal", true);
        CallMain("ApplyCustomFocusGoal", Enum.Parse(goalType, "MagicNgu"));
        Equal("applied", CallMain("GetCustomFocusStatus"), "Magic-only focus stop starts from actual applied native funding");
        var energy = ExecutionStopLaneState(true); var protectedState = ExecutionStopProtectedState(); var pool = Get(Get(Character, "magic"), "curMagic");
        CallMain("StopCustomFocusGoal");
        Equal(energy, ExecutionStopLaneState(true), "Magic-only focus stop preserves the entire unclaimed Energy lane/current/idle");
        Equal(pool, Get(Get(Character, "magic"), "idleMagic"), "Magic-only focus stop releases its exact native pool");
        Equal(protectedState, ExecutionStopProtectedState(), "Magic-only focus stop preserves basic/R3");

        SetupInlineStagePlan();
        CallMain("ApplyCustomFocusGoal", Enum.Parse(goalType, "EnergyNgu"));
        Equal("applied", CallMain("GetCustomFocusStatus"), "focus missing-Magic case starts from a real acquired focus");
        var stage = Activator.CreateInstance(Plugin.GetType("NGUInjector.CustomPlans.CustomPlanStage", true));
        Set(stage, "EnergyPriorities", new[] { "NGU" }); Set(stage, "MagicPriorities", new string[0]);
        CallMain("AllocateCustomFocusResources", Enum.Parse(goalType, "EnergyNgu"), false, stage);
        var magic = ExecutionStopLaneState(false); protectedState = ExecutionStopProtectedState();
        CallMain("StopCustomFocusGoal");
        Equal(magic, ExecutionStopLaneState(false), "focus release requires a declared lane plan, not the global Magic management flag");
        Equal((long)Get(Character, "curEnergy") - 7, Get(Character, "idleEnergy"), "focus missing-Magic case releases only its planned Energy");
        Equal(protectedState, ExecutionStopProtectedState(), "focus missing-Magic stop preserves basic/R3");

        foreach (var successorGoal in new[] { "MagicNgu", "AdvancedTraining" })
        {
            SetupInlineStagePlan();
            CallMain("ApplyCustomFocusGoal", Enum.Parse(goalType, "AdvancedTraining"));
            Equal("applied", CallMain("GetCustomFocusStatus"), "focus stop successor starts from real AT focus funding");
            Check(((long[])Get(Get(Character, "advancedTraining"), "energy")).Any(x => x > 0), "focus successor test has actual AT work to release");
            var original = Get(MainInstance, "_focusSetupSelection");
            var reached = false; object successor = null; string successorMagic = null; string successorEnergy = null;
            AtRefreshObserver = () => {
                reached = true; AtRefreshObserver = null;
                CallMain("ApplyCustomFocusGoal", Enum.Parse(goalType, successorGoal));
                Equal("applied", CallMain("GetCustomFocusStatus"), "release callback actually acquires and funds current successor focus");
                successor = Get(MainInstance, "_focusSetupSelection"); successorMagic = ExecutionStopLaneState(false); successorEnergy = ExecutionStopLaneState(true);
            };
            try { CallMain("StopCustomFocusGoal"); }
            finally { AtRefreshObserver = null; }
            Equal(true, reached, "current successor is installed at a real native stop boundary");
            Check(!ReferenceEquals(original, successor), "callback performs a new acquisition even when its goal name is unchanged");
            Check(ReferenceEquals(successor, Get(MainInstance, "_focusSetupSelection")), "outgoing focus stop cannot discard current successor acquisition");
            Equal(successorGoal, CallMain("GetActiveCustomFocusGoal").ToString(), "outgoing focus stop preserves current successor intent");
            Equal(successorMagic, ExecutionStopLaneState(false), "outgoing focus stop cannot consume the successor's Magic cleanup authority");
            Equal(successorEnergy, ExecutionStopLaneState(true), "outgoing focus stop cannot change the successor's current Energy/idle");
            Equal("applied", CallMain("GetCustomFocusStatus"), "outgoing stop does not overwrite the current successor status");
        }
    }
}

public static partial class Program
{
    private static void CheckWeightedRouteOwnerProof()
    {
        var before = Assertions;
        foreach (var fault in new[] { "none", "funding-before", "model-before", "intent-before", "funding", "foreign-lane", "model", "permission", "claim", "epoch", "coordinator", "support", "selection", "settings" })
        {
            var plan = Invoke(typeof(Program), "AdventureWeightSetup", 15d);
            var run = Invoke(typeof(Program), "StartScheduled", plan);
            Equal("active", Get(run, "AdventureStatus"), fault + " starts from the actual funded native phase route");
            Equal(null, Get(MainInstance, "_executionBaselineResourceIntent"), fault + " phase proof cannot depend on a synthetic normal baseline");
            var writer = Plugin.GetType("NGUInjector.Managers.ResourceWriter", true);
            var executor = Get(MainInstance, "_resourceSetupExecutor"); var intent = Get(executor, "Current");
            Equal("combat:" + Get(run, "Id"), Get(intent, "Owner"), fault + " actual funding intent belongs to this combat executor");
            Equal(true, Get(Get(run, "ResourceOutcome"), "Verified"), fault + " actual native phase accounting is verified");
            var map = (IDictionary)Get(intent, "Desired"); var observed = (IDictionary)Invoke(writer, "CaptureAllocations", Character);
            foreach (DictionaryEntry pair in map)
                Equal(true, ((long[])pair.Value).SequenceEqual((long[])observed[pair.Key]), fault + " actual native phase map matches its exact current intent/" + pair.Key);
            CallMain("RetireCombatPrepAdventureRoute", run);
            Equal(-1, Get(Get(Character, "adventure"), "zone"), fault + " owned native route retires before admission test");
            var adventure = Get(Character, "adventure"); var controller = Get(Character, "adventureController");
            var idle = Child(controller, "idleAttackMove"); Set(idle, "character", Character); Set(idle, "pc", Get(controller, "playerController"));
            Set(Get(controller, "playerController"), "character", Character);
            foreach (var model in new[] { Get(controller, "playerController"), adventure })
                foreach (var field in model.GetType().GetFields(All).Where(x => !x.IsStatic && x.FieldType.Name == "PlayerTime"))
                    if (field.GetValue(model) == null) Child(model, field.Name);
            Set(adventure, "autoattacking", false);
            var entry = Get(run, "OwnerEntry"); var deadline = Get(run, "DeadlineRebirthSeconds"); var setup = Get(run, "Setup");
            var writes = Get(typeof(Program), "ResourceModelWrites"); var swaps = Swaps;
            var routes = Get(typeof(Program), "AdventureWeightFarmingWrites");
            var fired = false; object claim = null;
            var expectedAccounting = (string)Invoke(writer, "AllocatedResourceKey");
            Action inject = () => {
                fired = true;
                Set(typeof(Program), "CombatIdleAttackAfter", null);
                if (fault == "funding-before" || fault == "funding" || fault == "foreign-lane")
                {
                    var energy = fault != "foreign-lane";
                    var field = energy ? "energy" : "magic";
                    var funded = ((IEnumerable)Get(Get(Character, "NGU"), energy ? "skills" : "magicSkills")).Cast<object>()
                        .FirstOrDefault(x => (long)Get(x, field) > 0);
                    if (funded != null) Set(funded, field, (long)Get(funded, field) - 1);
                    else
                    {
                        funded = Get(Character, "wandoos98"); field = energy ? "wandoosEnergy" : "wandoosMagic";
                        Equal(true, (long)Get(funded, field) > 0, fault + " has a real funded destination for the native foreign edit");
                        Set(funded, field, (long)Get(funded, field) - 1);
                    }
                    var pool = energy ? Character : Get(Character, "magic"); var idleField = energy ? "idleEnergy" : "idleMagic";
                    Set(pool, idleField, (long)Get(pool, idleField) + 1);
                }
                if (fault == "model-before" || fault == "model") Set(Character, "magic", Invoke(Get(Character, "magic"), "MemberwiseClone"));
                if (fault == "intent-before") Set(MainInstance, "_sharedResourceSetup", null);
                if (fault == "permission") Set(Settings, "_manageMagic", false);
                if (fault == "claim") claim = CallMain("BeginExecutionClaim", "foreign-route", "admitted-claim", Enum.Parse(MainType.GetNestedType("ExecutionDomain", All), "Route"), (Func<bool>)(() => true));
                if (fault == "epoch") Set(Get(Character, "stats"), "rebirthNumber", (long)Get(Get(Character, "stats"), "rebirthNumber") + 1);
                if (fault == "coordinator") Set(MainType, "reference", Blank(MainType));
                if (fault == "support")
                {
                    var beard = ((IList)Get(Get(Character, "beards"), "beards"))[0]; Set(beard, "active", !(bool)Get(beard, "active"));
                }
                if (fault == "selection") Set(run, "Setup", Invoke(setup, "MemberwiseClone"));
                if (fault == "settings") Set(MainType, "Settings", Invoke(Settings, "MemberwiseClone"));
                expectedAccounting = (string)Invoke(writer, "AllocatedResourceKey");
            };
            if (fault.EndsWith("-before", StringComparison.Ordinal)) inject();
            else Set(typeof(Program), "CombatIdleAttackAfter", inject);
            try
            {
                Set(typeof(Program), "ControlledCombatPrepClock", (double)Get(typeof(Program), "ControlledCombatPrepClock") + 1);
                CallMain("RouteCombatPrepAdventure");
                Equal(true, fired, fault + " exercises the actual pre-route native boundary");
                if (fault == "none")
                {
                    Equal("active", Get(run, "AdventureStatus"), "single current phase proof admits actual farming without a normal baseline");
                    Equal(1000, Get(adventure, "zone"), "positive phase proof executes actual native ITOPOD entry");
                    Equal((int)routes + 1, Get(typeof(Program), "AdventureWeightFarmingWrites"), "positive control records exactly one native farming entry");
                }
                else
                {
                    Equal(true, (string)Get(run, "AdventureStatus") != "active", fault + " proof loss never receives route credit");
                    Equal(-1, Get(adventure, "zone"), fault + " proof loss prevents native farming entry");
                    Equal(routes, Get(typeof(Program), "AdventureWeightFarmingWrites"), fault + " proof loss reaches no farming route write");
                    Set(typeof(Program), "ControlledCombatPrepClock", (double)Get(typeof(Program), "ControlledCombatPrepClock") + 1);
                    CallMain("RouteCombatPrepAdventure");
                    Equal(-1, Get(adventure, "zone"), fault + " retry cannot adopt stale phase proof");
                }
                Equal(expectedAccounting, Invoke(writer, "AllocatedResourceKey"), fault + " preserves exact current native funding without cleanup/repair writes");
                Equal(writes, Get(typeof(Program), "ResourceModelWrites"), fault + " route admission never mutates resource accounting");
                Equal(swaps, Swaps, fault + " route admission never reselects or replaces phase gear");
                Equal(entry, Get(run, "OwnerEntry"), fault + " route proof cannot create a new stage entry");
                Equal(deadline, Get(run, "DeadlineRebirthSeconds"), fault + " route proof cannot renew the acquired budget");
                if (claim != null) Equal(false, Get(claim, "Ended"), "failed farming admission preserves the other admitted route claim");
            }
            finally { Set(typeof(Program), "CombatIdleAttackAfter", null); }
        }
        Console.WriteLine("Weighted route owner proof: " + (Assertions - before) + " native assertions.");
    }
}
