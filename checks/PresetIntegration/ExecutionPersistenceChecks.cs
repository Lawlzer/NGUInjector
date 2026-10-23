using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public static partial class PresetIntegrationChecks
{
    private static object ExecutionReviewApplyArgs(object preview) => JsonParse("{\"token\":" + Json(preview, "token")
        + ",\"checkpointRevision\":" + Json(preview, "checkpointRevision") + "}");

    private static void CheckExecutionPersistence()
    {
        var before = Assertions;
        CheckRouteExecutionReviewPrecedence();
        foreach (var autoUpdate in new[] { false, true })
        {
            var plan = SetupResourceCoordination(true, autoUpdate, "none");
            var receipt = Get(MainInstance, "_globalPresetReceipt");
            var acquisition = Get(Get(receipt, "Policy"), "Acquisition");
            var acquiredPolicy = Get(receipt, "Policy");
            var horizon = Get(acquiredPolicy, "Horizon");
            var definition = Get(acquiredPolicy, "Definition");
            var norms = ((double[])Get(Get(acquiredPolicy, "Plan"), "Normalization")).ToArray();
            var path = (string)MainType.GetProperty("ProtectedCheckpointPath", All).GetValue(MainInstance);
            if (autoUpdate)
            {
                Set(MainInstance, "_customStageFightKey", "completed-entry-fight-history");
                Equal(true, CallMain("SaveProtectedStageProgress", true), "nonempty historical fight commitment saved before reload");
            }
            var bytes = File.ReadAllText(path);
            var fight = Get(MainInstance, "_customStageFightKey");
            CallMain("InitializeProtectedStageProgress", plan);
            Equal(fight ?? "", Get(MainInstance, "_customStageFightKey"), "checkpoint normalizes only absent fight identity to its shipped empty encoding");
            var restoredFight = Get(MainInstance, "_customStageFightKey");
            Set(MainInstance, "_globalPresetReceipt", null);
            ((IDictionary)Get(MainInstance, "_globalPresetPolicies")).Clear();
            Check(Get(MainInstance, "_protectedSelectedPolicyIssue") != null, "reload requires review in AutoUpdate=" + autoUpdate);
            var scheduler = Get(MainInstance, "_protectedScheduler");
            var commitment = Invoke(scheduler, "ExportState").ToString();
            var searches = PresetSearches; var transfers = ResourceTransfers; var swaps = Get(typeof(Program), "Swaps");
            var preview = CallMain("PreviewExecutionRebind", JsonParse("{}"));
            Equal(acquisition, Property(Json(preview, "acquisition"), "Value"), "preview retains original acquisition");
            Equal(commitment, Invoke(scheduler, "ExportState").ToString(), "preview cannot change entry, credit, deadlines or W history");
            Equal(bytes, File.ReadAllText(path), "preview cannot rewrite checkpoint evidence");
            Equal(searches, PresetSearches, "preview performs no replacement search");
            Equal(transfers, ResourceTransfers, "preview never writes resource accounting");
            Equal(swaps, Get(typeof(Program), "Swaps"), "preview never equips resolved native instances");
            var args = ExecutionReviewApplyArgs(preview);
            CallMain("ApplyExecutionRebind", args);
            Equal(null, Get(MainInstance, "_protectedSelectedPolicyIssue"), "positive review releases only this entry's quarantine");
            Equal(commitment, Invoke(scheduler, "ExportState").ToString(), "approval retains all scheduler commitments");
            Equal(restoredFight, Get(MainInstance, "_customStageFightKey"), "approval cannot reset the loaded entry-fight identity");
            Equal(acquisition, Get(Get(MainInstance, "_globalPresetApplyingPolicy"), "Acquisition"), "approval publishes original acquisition");
            Equal(bytes, File.ReadAllText(path), "approval is session authority, not persistent continuity evidence");
            Equal(transfers, ResourceTransfers, "approval queues intent without native allocation writes");
            Equal(swaps, Get(typeof(Program), "Swaps"), "approval does not fabricate an applied setup");
            Throws(() => CallMain("ApplyExecutionRebind", args), "expired", "review token is consumed once");
            var boss = Get(Character, "bossController");
            var fighting = Get(boss, "isFighting"); var nuking = Get(boss, "nukeBoss");
            var actionFiles = Directory.GetFiles((string)Get(MainType, "_dir"), "*.json", SearchOption.AllDirectories)
                .Where(x => x.Contains("finite-actions")).OrderBy(x => x).Select(File.ReadAllText).ToArray();
            var store = Get(MainType, "CustomPlanStore"); var library = Get(store, "<PresetLibrary>k__BackingField");
            var edited = Invoke(library, "ReadExternalLibrary");
            var definitionRow = ((IEnumerable)GetChildren(Json(edited, "Presets"))).Cast<object>().Single(x => Property(Json(x, "Id"), "Value").Equals(Get(Get(acquiredPolicy, "Preset"), "Id")));
            SetJson(definitionRow, "Weights", Json(JsonParse("{\"v\":[0,0,1,0,0,0,0,0,0,0,0,0,0,0]}"), "v"));
            Invoke(library, "SaveExternalLibrary", edited, Property(Json(edited, "__externalUiRevision"), "Value"));
            CallMain("ReconcileCustomResources", plan, true);
            var resumed = Get(MainInstance, "_globalPresetReceipt");
            Check(resumed != null, "positive reviewed SAME-entry reconciliation produces actual verified preset evidence/AutoUpdate=" + autoUpdate);
            var resumedPolicy = Get(resumed, "Policy");
            Equal(acquisition, Get(resumedPolicy, "Acquisition"), "native reconciliation retains ORIGINAL acquisition");
            Equal(horizon, Get(resumedPolicy, "Horizon"), "native reconciliation retains ORIGINAL horizon");
            Equal(definition, Get(resumedPolicy, "Definition"), "native reconciliation retains ORIGINAL definition");
            Check(norms.SequenceEqual((double[])Get(Get(resumedPolicy, "Plan"), "Normalization")), "native reconciliation retains frozen scoring references");
            foreach (var field in new[] { "entry", "cycle", "enteredAt", "expectedEnd", "earned", "completed", "autoAtCompleted", "autoWandoosStarted" })
                Equal(Json(JsonParse(commitment), field).ToString(), Json(Invoke(scheduler, "ExportState"), field).ToString(), "same-entry reconciliation preserves commitment/" + field);
            Equal(restoredFight, Get(MainInstance, "_customStageFightKey"), "actual reconciliation cannot reopen historical entry fight");
            Equal(fighting, Get(boss, "isFighting"), "review/reconcile does not start native fight");
            Equal(nuking, Get(boss, "nukeBoss"), "review/reconcile does not start native nuke");
            Check(actionFiles.SequenceEqual(Directory.GetFiles((string)Get(MainType, "_dir"), "*.json", SearchOption.AllDirectories)
                .Where(x => x.Contains("finite-actions")).OrderBy(x => x).Select(File.ReadAllText)), "review/reconcile invokes no new finite fight/action opportunity");
            Equal(searches, PresetSearches, "positive native replay performs no replacement search");
            CallMain("InitializeProtectedStageProgress", plan);
            Check(Get(MainInstance, "_protectedSelectedPolicyIssue") != null, "next restart reviews again despite prior approval");
            preview = CallMain("PreviewExecutionRebind", JsonParse("{}"));
            File.AppendAllText(path, " ");
            Throws(() => CallMain("ApplyExecutionRebind", ExecutionReviewApplyArgs(preview)), "Checkpoint changed", "exact-byte checkpoint revision is bound");
            File.WriteAllText(path, bytes);
            preview = CallMain("PreviewExecutionRebind", JsonParse("{}"));
            CallMain("InvalidateExecutionObservations", Domain("Support"));
            Throws(() => CallMain("ApplyExecutionRebind", ExecutionReviewApplyArgs(preview)), "authority changed", "observation revision revokes review authority");
            preview = CallMain("PreviewExecutionRebind", JsonParse("{}"));
            Set(Get(MainInstance, "_executionRebindReview"), "Expires", 0d);
            Throws(() => CallMain("ApplyExecutionRebind", ExecutionReviewApplyArgs(preview)), "expired", "short-lived token expiry is enforced");
            Equal(searches, PresetSearches, "failed review retries do not search with either AutoUpdate mode");
        }
        CheckInvalidOuterExecutionCheckpoint();
        CheckQuickExecutionRebind();
        CheckFocusExecutionRebind();
        CheckExecutionLifecycleRevocation();
        CheckExplicitExecutionRestart();
        CheckScopedExecutionRecovery();
        CheckForeignNativeSettingsReviewScope();
        CheckFightPreparationExecutionRebind();
        CheckCompletedFightPreparationExecutionRebind();
        CheckFightPreparationEffectUncertainty();
        Console.WriteLine("Execution persistence: " + (Assertions - before) + " native assertions.");
    }

    private static string LoadUnstartedWalderpRouteReview()
    {
        // Keep the real scheduled acquisition; PrepareRouteProcessWorld would replace this world.
        var settings = Get(MainType, "Settings"); var adventure = Get(Character, "adventure");
        var controller = Get(Character, "adventureController");
        Set(settings, "_manualTitanEnabled", true); Set(settings, "_manualTitanZone", 16);
        Set(settings, "_manualTitanLoadout", -1); Set(settings, "_manualTitanRequireAT", true); Set(settings, "_manualTitanMaxDeaths", 2);
        Set(settings, "_manualAdventureRecoverHealth", false); Set(settings, "_manualAdventurePrecastBuffs", false);
        Set(settings, "_blacklistedBosses", new int[0]);
        Set(Character, "bossID", 200); Set(Character, "highestBoss", 200);
        Set(adventure, "waldoDefeats", 4); Set(adventure, "waldoFinds", 4); Set(adventure, "titan5Kills", 0);
        Set(adventure, "maxHP", 100f); Set(adventure, "curHP", 100f); Set(adventure, "autoattacking", false);
        var timer = Get(adventure, "boss5Spawn") ?? Child(adventure, "boss5Spawn");
        Set(timer, "totalseconds", Convert.ToDouble(Invoke(controller, "boss5SpawnTime")) + 1);
        var at = Get(Character, "advancedTraining");
        var targets = (long[])Get(at, "levelTarget"); targets[0] = 10; targets[1] = 20;
        var levels = (long[])Get(at, "level"); levels[0] = 10; levels[1] = 19;
        ((long[])Get(Get(Character, "training"), "_attackTraining"))[1] = 1;
        Set(Get(controller, "log"), "Eventlog", new List<string> { new string("native review baseline".ToCharArray()) });
        Set(MainType, "PlayerController", Get(controller, "playerController"));
        Set(MainInstance, "_customTitanGoldPendingIndex", -1); Set(MainInstance, "_customTitanGoldIndex", -1); Set(MainInstance, "_customTitanFightIndex", -1);
        Set(Get(Get(Character, "buttons"), "adventure"), "m_Interactable", true);
        Set(Get(Get(Character, "buttons"), "adventure"), "m_GroupsAllowInteraction", true);
        Equal(true, CallMain("ManualTitanUnlocked", 4), "route review fixture has a native-unlocked Walderp");
        Equal(false, CallMain("TryRouteManualTitan"), "incomplete native AT creates an observation without starting Walderp");
        Equal("Waiting for positive AT power/toughness targets and completed levels", Get(MainInstance, "_manualTitanStatus"), "real readiness gate, not another blocker, produced the observation");
        var saved = CallMain("ExportRouteExecutionCommitments");
        Equal(0, Property(Json(saved, "processes"), "Count"), "unstarted observation has no acquired route processes");
        Equal(0, Property(Json(saved, "towerDeaths"), "AsInt"), "unstarted observation has no tower budget");
        var observation = ((IEnumerable)GetChildren(Json(saved, "manual"))).Cast<object>().Single();
        Equal(4, Property(Json(observation, "index"), "AsInt"), "real readiness observation is Walderp index four");
        Equal(false, Property(Json(observation, "started"), "AsBool"), "real observation was never started");
        Equal(false, Property(Json(observation, "disarmed"), "AsBool"), "readiness gate does not disarm an unstarted spawn");
        foreach (var field in new[] { "deaths", "limit", "ownedSeconds" })
            Equal(0d, Property(Json(observation, field), "AsDouble"), "unstarted observation retains zero " + field);
        Equal(0, Property(Json(observation, "gear"), "Count"), "readiness gate has selected no gear");
        var path = Path.Combine((string)Get(MainType, "_dir"), "lifecycle-route-commitments-v1.json");
        File.WriteAllText(path, saved.ToString());
        Set(MainInstance, "_executionExactLoaded", false); CallMain("EnsureExecutionExactLoaded");
        Equal(path, Get(MainInstance, "_executionRoutePath"), "actual lifecycle loader selects the nonempty manual observation");
        Equal(saved.ToString(), CallMain("ExportRouteExecutionCommitments").ToString(), "actual loader quarantines the original exported observation");
        return path;
    }

    private static void CheckRouteExecutionReviewPrecedence()
    {
        foreach (var running in new[] { true, false })
        foreach (var pendingStage in new[] { true, false })
        {
            var context = "route-first/running=" + running + "/pending-stage=" + pendingStage;
            var plan = SetupResourceCoordination(true, false, "none");
            var originalPolicy = Get(Get(MainInstance, "_globalPresetReceipt"), "Policy");
            var acquisition = Get(originalPolicy, "Acquisition"); var owner = Get(MainInstance, "_customStageEquipmentKey");
            CallMain("InitializeProtectedStageProgress", plan);
            Check(Get(MainInstance, "_protectedSelectedPolicyIssue") != null, context + " loads a real pending stage selection");
            if (!pendingStage)
                CallMain("ApplyExecutionRebind", ExecutionReviewApplyArgs(CallMain("PreviewExecutionRebind", JsonParse("{}"))));
            if (!running)
            {
                var library = Get(Get(MainType, "CustomPlanStore"), "<PresetLibrary>k__BackingField");
                var root = Invoke(library, "ReadExternalLibrary");
                var args = JsonParse("{\"presetId\":\"" + Get(Get(originalPolicy, "Preset"), "Id")
                    + "\",\"libraryRevision\":" + Json(root, "__externalUiRevision") + "}");
                CallMain("EquipGlobalPreset", args, null);
                Equal("dashboard", Get(Get(MainInstance, "_globalPresetReceipt"), "Owner"), context + " public Equip acquires the actual paused owner");
            }
            var settings = Get(MainType, "Settings");
            Equal(running, Property(settings, "UseCustomRebirthPlan"), context + " uses the requested plan state");
            var stageIssue = Get(MainInstance, "_protectedSelectedPolicyIssue");
            Equal(pendingStage, stageIssue != null, context + " stage review state is real, not a fabricated receipt");
            var blockedEntry = Get(MainInstance, "_protectedSelectedPolicyBlockedEntry");
            var selection = Get(MainInstance, "_protectedSelectedPolicy");
            var routePath = LoadUnstartedWalderpRouteReview(); var routeBytes = File.ReadAllText(routePath);
            var checkpointPath = (string)Property(MainInstance, "ProtectedCheckpointPath"); var checkpoint = File.ReadAllText(checkpointPath);
            if (running && pendingStage)
            {
                const string invalid = "{unreadable outer checkpoint with pending routes";
                File.WriteAllText(checkpointPath, invalid); CallMain("InitializeProtectedStageProgress", plan);
                Throws(() => CallMain("PreviewExecutionRebind", JsonParse("{}")), "unreadable", "outer checkpoint rejection still precedes pending route dispatch");
                var blocked = CallMain("GetExecutionPersistenceRuntime");
                Equal(Get(MainInstance, "_protectedOuterCheckpointIssue"), Property(Json(blocked, "reason"), "Value"), "outer checkpoint reason outranks route quarantine");
                Equal(false, Property(Json(blocked, "canPreview"), "AsBool"), "pending routes cannot bypass outer checkpoint recovery");
                Equal(invalid, File.ReadAllText(checkpointPath), "rejected preview preserves invalid outer evidence");
                Equal(routeBytes, CallMain("ExportRouteExecutionCommitments").ToString(), "outer rejection preserves pending route evidence");
                File.WriteAllText(checkpointPath, checkpoint); CallMain("InitializeProtectedStageProgress", plan);
            }
            var scheduler = Get(MainInstance, "_protectedScheduler"); var state = Invoke(scheduler, "ExportState").ToString();
            var receipt = Get(MainInstance, "_globalPresetReceipt"); var applying = Get(MainInstance, "_globalPresetApplyingPolicy");
            var native = CallMain("ExecutionRebindNativeKey"); var material = CallMain("ExactExecutionMaterialKey");
            var allocations = CallMain("GlobalPresetAllocationState");
            Func<int[]> activity = () => new[] { PresetSearches, InlineGearSelections, ResourceTransfers, AdventureZoneEntries,
                PodRefreshCalls, (int)Get(typeof(Program), "Swaps"), (int)Get(typeof(Program), "Recalls") };
            var counts = activity();
            CallMain("CustomAdventureTick");
            Equal(-1, Get(Get(Character, "adventure"), "zone"), context + " loaded quarantine blocks the ordinary adventure tick");
            var preview = CallMain("PreviewExecutionRebind", JsonParse("{}"));
            Equal("Route processes", Property(Json(preview, "owner"), "Value"), context + " public preview reaches routes before any selected preset");
            Equal(routeBytes, Json(preview, "commitments").ToString(), context + " preview retains the actual loaded observation");
            var runtime = CallMain("GetExecutionPersistenceRuntime");
            Equal(true, Property(Json(runtime, "reviewRequired"), "AsBool"), context + " runtime requires route review");
            Equal(true, Property(Json(runtime, "canPreview"), "AsBool"), context + " runtime advertises the reachable route review");
            Equal("Route processes", Property(Json(runtime, "owner"), "Value"), context + " runtime owner agrees with public preview");
            Equal(Get(MainInstance, "_routeCommitmentIssue"), Property(Json(runtime, "reason"), "Value"), context + " route reason outranks pending stage reason");
            Equal(checkpoint, File.ReadAllText(checkpointPath), context + " preview preserves checkpoint bytes");
            Equal(routeBytes, File.ReadAllText(routePath), context + " preview preserves lifecycle evidence");
            Equal(state, Invoke(scheduler, "ExportState").ToString(), context + " preview preserves entry, timing, credit and fight history");
            Equal(native, CallMain("ExecutionRebindNativeKey"), context + " preview preserves native route, support and targets");
            Equal(material, CallMain("ExactExecutionMaterialKey"), context + " preview preserves native gear");
            Equal(allocations, CallMain("GlobalPresetAllocationState"), context + " preview preserves native accounting");
            Check(counts.SequenceEqual(activity()), context + " preview performs no preset/gear search, allocation, route, support, equip or recall writes");

            Set(settings, "_useCustomRebirthPlan", !running);
            try { Throws(() => CallMain("ApplyExecutionRebind", ExecutionReviewApplyArgs(preview)), "authority changed", context + " plan toggle invalidates route review token"); }
            finally { Set(settings, "_useCustomRebirthPlan", running); }
            if (running && pendingStage)
            {
                preview = CallMain("PreviewExecutionRebind", JsonParse("{}"));
                var adventure = Get(Character, "adventure"); var end = (int)Get(adventure, "itopodEnd");
                Set(adventure, "itopodEnd", end + 1);
                try
                {
                    Throws(() => CallMain("ApplyExecutionRebind", ExecutionReviewApplyArgs(preview)), "Route review evidence changed", "foreign native route edit rejects review without approving the stage");
                    Equal(end + 1, Get(adventure, "itopodEnd"), "rejected route review preserves the foreign native value");
                }
                finally { Set(adventure, "itopodEnd", end); }
                preview = CallMain("PreviewExecutionRebind", JsonParse("{}"));
                var model = Character; var coordinator = MainInstance;
                // A disjoint real claim leaves the route token fresh but must still block the semantic importer.
                var claim = CallMain("BeginExecutionClaim", "route-review-negative", "retained-native-owner", Domain("Energy"),
                    (Func<bool>)(() => ReferenceEquals(model, Character) && ReferenceEquals(coordinator, MainInstance)));
                try { Throws(() => CallMain("ApplyExecutionRebind", ExecutionReviewApplyArgs(preview)), "Retire current claims", "route review cannot import over an admitted native claim"); }
                finally { Equal(true, CallMain("EndExecutionClaim", claim, false), "negative-control claim retires without native reconciliation"); }
            }
            Equal(routeBytes, CallMain("ExportRouteExecutionCommitments").ToString(), context + " rejected tokens retain original quarantine evidence");
            Equal(routeBytes, File.ReadAllText(routePath), context + " rejected tokens do not rewrite lifecycle evidence");
            Check(Get(MainInstance, "_routeCommitmentIssue") != null, context + " rejected tokens do not release route quarantine");
            Equal(stageIssue, Get(MainInstance, "_protectedSelectedPolicyIssue"), context + " rejected route review never approves the original stage");
            preview = CallMain("PreviewExecutionRebind", JsonParse("{}"));
            CallMain("ApplyExecutionRebind", ExecutionReviewApplyArgs(preview));
            Equal(null, Get(MainInstance, "_routeCommitmentIssue"), context + " explicit public route approval releases quarantine");
            Equal(null, Get(MainInstance, "_importedRouteCommitments"), context + " explicit route approval consumes only imported route intent");
            Equal(false, Get(MainInstance, "_manualTitanActive"), context + " approval itself never starts Walderp");
            Equal(false, Property(Json(JsonParse(File.ReadAllText(routePath)), "active"), "AsBool"), context + " reviewed lifecycle record is durably inactive");
            Equal(Json(JsonParse(routeBytes), "manual").ToString(), Json(CallMain("ExportRouteExecutionCommitments"), "manual").ToString(), context + " approval retains never-started history without inventing a budget");
            Equal(stageIssue, Get(MainInstance, "_protectedSelectedPolicyIssue"), context + " route approval leaves stage review state untouched");
            Equal(blockedEntry, Get(MainInstance, "_protectedSelectedPolicyBlockedEntry"), context + " route approval retains the original blocked entry");
            Check(ReferenceEquals(receipt, Get(MainInstance, "_globalPresetReceipt")) && ReferenceEquals(applying, Get(MainInstance, "_globalPresetApplyingPolicy")), context + " route approval preserves the actual current preset owner");
            Throws(() => CallMain("ApplyExecutionRebind", ExecutionReviewApplyArgs(preview)), "expired", context + " route token is single-use");
            runtime = CallMain("GetExecutionPersistenceRuntime");
            Equal(running && pendingStage, Property(Json(runtime, "reviewRequired"), "AsBool"), context + " paused stage issues are retained but not surfaced");
            Equal(running && pendingStage, Property(Json(runtime, "canPreview"), "AsBool"), context + " only an enabled pending stage remains reviewable");
            Equal(running && pendingStage ? stageIssue : "", Property(Json(runtime, "reason"), "Value"), context + " runtime advances to the next applicable review reason");
            Equal(running && pendingStage ? owner : "", Property(Json(runtime, "owner"), "Value"), context + " runtime advances to the next applicable owner");
            if (running && pendingStage)
            {
                preview = CallMain("PreviewExecutionRebind", JsonParse("{}"));
                Equal(owner, Property(Json(preview, "owner"), "Value"), context + " next public preview is the original enabled stage");
                Equal(acquisition, Property(Json(preview, "acquisition"), "Value"), context + " next stage review retains the original acquisition");
                CallMain("ApplyExecutionRebind", ExecutionReviewApplyArgs(preview));
                Equal(null, Get(MainInstance, "_protectedSelectedPolicyIssue"), context + " separate stage approval releases only stage quarantine");
            }
            else if (!running)
            {
                preview = CallMain("PreviewExecutionRebind", JsonParse("{}"));
                Equal("dashboard", Property(Json(preview, "owner"), "Value"), context + " stale paused selectedIssue cannot dispatch scheduled review");
                Equal(Get(Get(receipt, "Policy"), "Acquisition"), Property(Json(preview, "acquisition"), "Value"), context + " paused preview retains the actual dashboard acquisition");
                Equal(stageIssue, Get(MainInstance, "_protectedSelectedPolicyIssue"), context + " dashboard preview preserves paused stage evidence");
            }
            Equal(selection, Get(MainInstance, "_protectedSelectedPolicy"), context + " review never replaces the selected stage policy");
            Equal(checkpoint, File.ReadAllText(checkpointPath), context + " all review/rejection paths preserve stage checkpoint bytes");
            Equal(state, Invoke(scheduler, "ExportState").ToString(), context + " reviews cannot renew scheduler commitments");
            Equal(native, CallMain("ExecutionRebindNativeKey"), context + " review acceptance does not actuate native route or settings");
            Equal(material, CallMain("ExactExecutionMaterialKey"), context + " review acceptance does not equip gear");
            Equal(allocations, CallMain("GlobalPresetAllocationState"), context + " review acceptance does not allocate resources");
            Check(counts.SequenceEqual(activity()), context + " all reviews and rejected controls perform no searches or native writes");
            ((long[])Get(Get(Character, "advancedTraining"), "level"))[1] = 20;
            CallMain("CustomAdventureTick");
            Equal(true, Get(MainInstance, "_manualTitanActive"), context + " next ordinary tick admits the original unstarted Walderp observation: " + CallMain("GetManualTitanRuntime"));
            Equal(16, Get(Get(Character, "adventure"), "zone"), context + " next ordinary tick actually selects the native Walderp zone");
            Equal(counts[3] + 1, AdventureZoneEntries, context + " reviewed routing reaches the native zone entry once");
            Equal(counts[0], PresetSearches, context + " reviewed routing does not replace the preset acquisition");
            Equal(counts[1], InlineGearSelections, context + " Keep current Walderp routing does not search gear");
            CallMain("CancelManualTitan", "route review precedence check complete");
        }
    }

    private static object SetupScopedExecutionReview(int objective, bool bothPermissions = false)
    {
        var plan = SetupResourceCoordination(false, true, "none");
        CallMain("StopManualGlobalPreset");
        var settings = Get(MainType, "Settings");
        Set(settings, "_manageEnergy", bothPermissions || objective != 1);
        Set(settings, "_manageMagic", bothPermissions || objective == 1);
        ((long[])Property(Get(Character, "training"), "attackEnergy"))[0] = 7;
        Set(Character, "idleEnergy", (long)Get(Character, "idleEnergy") - 7);
        var perks = Get(Get(Character, "adventureController"), "itopod"); Set(perks, "character", Character);
        Sequence(Get(Get(Character, "adventure"), "itopod"), "perkLevel", 512, 0L);
        Sequence(perks, "effectPerLevel", 512, 0f); ListField(perks, "perkDifficultyReq", 512);
        Set(Get(Character, "wishesController"), "character", Character);
        ListField(Get(Character, "wishesController"), "properties", 512);
        ListField(Get(Character, "wishes"), "wishes", 512);
        ListField(Get(Character, "hacks"), "hacks", 16);
        var quirks = Get(Character, "beastQuestPerkController"); Set(quirks, "character", Character);
        Sequence(Get(Character, "beastQuest"), "quirkLevel", 512, 0L);
        Sequence(quirks, "effectPerLevel", 512, 0f); ListField(quirks, "quirkDifficultyReq", 512);
        var r3 = Get(Character, "res3"); Set(r3, "capRes3", 1000L); Set(r3, "curRes3", 73L); Set(r3, "idleRes3", 43L);
        Set(((IList)Get(Get(Character, "wishes"), "wishes"))[0], "res3", 30L);
        if (objective == 12)
        {
            var item = Invoke(Get(Get(Character, "inventory"), "head"), "MemberwiseClone");
            Set(item, "id", 550); Set(item, "spec1Type", Enum.Parse(Field(item, "spec1Type").FieldType, "EnergyCap"));
            Set(item, "spec1Cur", 10000f); Set(item, "spec1Cap", 10000000f);
            ((IList)Get(Get(Character, "inventory"), "inventory"))[0] = item;
        }
        var store = Get(MainType, "CustomPlanStore"); var library = Get(store, "<PresetLibrary>k__BackingField");
        var root = Invoke(library, "ReadExternalLibrary"); var id = "scoped-review-" + Guid.NewGuid().ToString("N");
        var weights = Enumerable.Range(0, 14).Select(i => i == objective ? "1" : "0");
        Invoke(Json(root, "Presets"), "Add", "", JsonParse("{\"Id\":\"" + id + "\",\"Name\":\"Scoped original policy\",\"Weights\":["
            + string.Join(",", weights) + "],\"MagicBloodPercent\":0,\"SupportMode\":\"current\",\"SwapSeconds\":0,\"AutoUpdate\":false}"));
        var revision = Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
        var args = JsonParse("{\"presetId\":\"" + id + "\",\"libraryRevision\":\"" + revision + "\"}");
        var preview = CallMain("PreviewGlobalPreset", args);
        var capacityWrites = ResourceTransfers;
        var capacityBefore = CallMain("GlobalPresetAllocationState");
        var apply = JsonParse(args.ToString().TrimEnd('}') + ",\"previewId\":" + Json(preview, "previewId") + ",\"candidateKey\":\"" + (objective == 12 ? "balanced" : "current") + "\"}");
        Invoke(MainInstance, "ApplyGlobalPreset", apply, null);
        if (objective == 12)
        {
            Equal(550, Get(Get(Get(Character, "inventory"), "head"), "id"), "capacity-only native acquisition actually swaps to the selected larger-capacity item");
            Equal(capacityBefore, CallMain("GlobalPresetAllocationState"), "capacity gear transition proves complete preserving accounting without resource intent");
            Equal(capacityWrites, ResourceTransfers, "capacity-only gear transition does not acquire recall/write authority from Manage flags");
        }
        Set(((Array)Get(plan, "Stages")).GetValue(0), "PresetId", id);
        Set(MainInstance, "_customRuntimePlanCache", plan); Set(MainInstance, "_customRuntimePlanCacheName", Property(settings, "CustomPlanFile"));
        Set(MainInstance, "_customRuntimePlanCacheLoadedAt", DateTime.Now); Set(settings, "_useCustomRebirthPlan", true);
        CallMain("RunCustomPlanAutomation", plan);
        RequireInitialScheduledReceipt(plan, "scoped exact review/" + objective);
        return plan;
    }

    private static void InjectNativeReviewConflict(bool energy)
    {
        var writer = Plugin.GetType("NGUInjector.Managers.ResourceWriter", true);
        var map = (IDictionary)Invoke(writer, "CaptureAllocations", Character);
        var targetType = Plugin.GetType("NGUInjector.Managers.ResourceTarget", true);
        var target = Enum.Parse(targetType, energy ? "EnergyNgu" : "MagicNgu");
        var amounts = (long[])map[target];
        var index = Array.FindIndex(amounts, value => value > 2);
        if (index < 0)
        {
            index = 0; amounts[index] = 20;
            var skills = (IList)Get(Get(Character, "NGU"), energy ? "skills" : "magicSkills");
            Set(skills[index], energy ? "energy" : "magic", 20L);
            var pool = energy ? Character : Get(Character, "magic"); var idle = energy ? "idleEnergy" : "idleMagic";
            Set(pool, idle, (long)Get(pool, idle) - 20);
        }
        amounts[index]--;
        var native = ((IList)Get(Get(Character, "NGU"), energy ? "skills" : "magicSkills"))[index];
        var fired = false;
        var desired = (IDictionary)Activator.CreateInstance(map.GetType());
        foreach (DictionaryEntry pair in map)
            if ((int)Invoke(writer, "Lane", pair.Key) == (energy ? 0 : 1)
                && pair.Key.ToString() != "BasicAttack" && pair.Key.ToString() != "BasicDefense") desired.Add(pair.Key, pair.Value);
        var model = Character; var domain = Domain(energy ? "Energy" : "Magic");
        var grant = CallMain("ResolveExecutionGrant", "native-review-boundary", "existing-native-context", domain,
            (Func<bool>)(() => ReferenceEquals(model, Get(MainType, "Character"))), null, null);
        var application = Get(Property(MainInstance, "_resourceSetupExecutor"), "Application");
        var outcome = Invoke(application, "Apply", desired, energy, !energy, false,
            (Func<bool>)(() => (bool)CallMain("ValidateExecutionGrant", grant, domain)), (Action)(() => {
            fired = true;
            Set(native, energy ? "energy" : "magic", (long)Get(native, energy ? "energy" : "magic") - 1);
            var pool = energy ? Character : Get(Character, "magic"); var idle = energy ? "idleEnergy" : "idleMagic";
            Set(pool, idle, (long)Get(pool, idle) + 1);
        }));
        Equal(true, fired, "foreign control mutates actual native allocation during the real grant-checked actuator callback; " + Get(outcome, "Reason"));
        Equal("ForeignConflict", Get(outcome, "Disposition").ToString(), "real native callback produces typed resource uncertainty");
        Equal(energy ? 1 : 2, Get(outcome, "AffectedLanes"), "foreign native callback has an exact affected resource lane");
    }

    private static void CheckScopedExecutionRecovery()
    {
        foreach (var energy in new[] { true, false })
        {
            var plan = SetupScopedExecutionReview(energy ? 0 : 1);
            var policy = Get(Get(MainInstance, "_globalPresetReceipt"), "Policy");
            var acquisition = Get(policy, "Acquisition"); var scheduler = Get(MainInstance, "_protectedScheduler");
            var state = Invoke(scheduler, "ExportState"); var searches = PresetSearches;
            InjectNativeReviewConflict(energy);
            var application = Get(Property(MainInstance, "_resourceSetupExecutor"), "Application");
            Equal(energy ? 1 : 2, Property(application, "BlockedLanes"), "review starts with real scoped unresolved evidence");
            var old = CallMain("ResolveExecutionGrant", "review-old", "old", Domain(energy ? "Energy" : "Magic"), (Func<bool>)(() => true), null, null);
            var unmanaged = CallMain("GlobalPresetLaneState", !energy);
            var basic = ((long[])Property(Get(Character, "training"), "attackEnergy")).ToArray();
            var r3 = Get(Character, "res3"); var r3Current = Get(r3, "curRes3"); var r3Idle = Get(r3, "idleRes3");
            var preview = CallMain("PreviewExecutionRebind", JsonParse("{}"));
            var review = Get(MainInstance, "_executionRebindReview"); var grant = Get(review, "Grant");
            Equal(false, CallMain("ValidateExecutionGrant", grant, Domain(energy ? "Magic" : "Energy")), "single-lane review never requests the unmanaged lane");
            var writes = ResourceTransfers;
            var invalid = ExecutionReviewApplyArgs(preview); SetJson(invalid, "token", Json(JsonParse("{\"v\":\"00000000000000000000000000000000\"}"), "v"));
            Throws(() => CallMain("ApplyExecutionRebind", invalid), "expired", "invalid recovery token cannot clear uncertainty");
            Equal(writes, ResourceTransfers, "invalid token performs zero native writes");
            Equal(energy ? 1 : 2, Property(application, "BlockedLanes"), "invalid token retains unresolved evidence");
            var settings = Get(MainType, "Settings"); var permission = energy ? "_manageEnergy" : "_manageMagic";
            Set(settings, permission, false);
            Throws(() => CallMain("ApplyExecutionRebind", ExecutionReviewApplyArgs(preview)), "authority changed", "permission loss rejects a previously valid review token");
            Set(settings, permission, true);
            preview = CallMain("PreviewExecutionRebind", JsonParse("{}"));
            CallMain("InvalidateExecutionObservations", Domain(energy ? "Energy" : "Magic"));
            Throws(() => CallMain("ApplyExecutionRebind", ExecutionReviewApplyArgs(preview)), "authority changed", "named-domain revision loss rejects approval before reauthorization");
            preview = CallMain("PreviewExecutionRebind", JsonParse("{}"));
            var stats = Get(Character, "stats"); Set(Character, "stats", Invoke(stats, "MemberwiseClone"));
            Throws(() => CallMain("ApplyExecutionRebind", ExecutionReviewApplyArgs(preview)), "authority changed", "native epoch/model replacement invalidates the token");
            Set(Character, "stats", stats);
            preview = CallMain("PreviewExecutionRebind", JsonParse("{}"));
            var nativeSkills = (IList)Get(Get(Character, "NGU"), energy ? "skills" : "magicSkills");
            var nativeIndex = Enumerable.Range(0, nativeSkills.Count).First(i => (long)Get(nativeSkills[i], energy ? "energy" : "magic") > 0);
            Set(nativeSkills[nativeIndex], energy ? "energy" : "magic", (long)Get(nativeSkills[nativeIndex], energy ? "energy" : "magic") - 1);
            var nativePool = energy ? Character : Get(Character, "magic"); var nativeIdle = energy ? "idleEnergy" : "idleMagic";
            Set(nativePool, nativeIdle, (long)Get(nativePool, nativeIdle) + 1);
            Throws(() => CallMain("ApplyExecutionRebind", ExecutionReviewApplyArgs(preview)), "scope changed", "unobserved native bar edit after Preview cannot be adopted by approval");
            Equal(writes, ResourceTransfers, "all rejected token/permission/epoch/domain/resource orderings perform zero native model writes");
            Equal(energy ? 1 : 2, Property(application, "BlockedLanes"), "rejected freshness checks never clear uncertainty");
            preview = CallMain("PreviewExecutionRebind", JsonParse("{}"));
            grant = Get(Get(MainInstance, "_executionRebindReview"), "Grant");
            old = CallMain("ResolveExecutionGrant", "review-old-before-approval", "old", Domain(energy ? "Energy" : "Magic"), (Func<bool>)(() => true), null, null);
            CallMain("ApplyExecutionRebind", ExecutionReviewApplyArgs(preview));
            Equal(0, Property(application, "BlockedLanes"), "explicit tokenized production caller reauthorizes only the reviewed resource lane");
            Equal(false, CallMain("ValidateExecutionGrant", old, Domain(energy ? "Energy" : "Magic")), "old ordinary grant becomes stale after reviewed reauthorization");
            Equal(false, CallMain("ValidateExecutionGrant", grant, Domain(energy ? "Energy" : "Magic")), "spent review grant cannot authorize subsequent writes");
            var beforeRecoveryWrites = ResourceTransfers;
            CallMain("ReconcileCustomResources", plan, true);
            var current = Get(MainInstance, "_globalPresetReceipt");
            Check(current != null, "same-entry selected policy actually reconverges after native foreign conflict");
            Equal(true, CallMain("LoadoutPresetAllocationsMatch", Get(current, "Execution")), "actual native funded allocation converges to the retained selected policy");
            Check(ResourceTransfers > beforeRecoveryWrites, "reviewed reconciliation really repairs the native deficit rather than reporting a retained receipt");
            Equal(acquisition, Get(Get(current, "Policy"), "Acquisition"), "resource recovery never performs a new acquisition");
            Equal(unmanaged, CallMain("GlobalPresetLaneState", !energy), "unmanaged native allocations remain exactly untouched");
            Check(basic.SequenceEqual((long[])Property(Get(Character, "training"), "attackEnergy")), "reviewed Energy authority never grants Basic Training authority");
            Equal(r3Current, Get(r3, "curRes3"), "review leaves unmanaged R3 current pool unchanged");
            Equal(r3Idle, Get(r3, "idleRes3"), "review leaves unmanaged R3 idle pool unchanged");
            Equal(30L, Get(((IList)Get(Get(Character, "wishes"), "wishes"))[0], "res3"), "review preserves actual unrelated R3 funding");
            foreach (var field in new[] { "entry", "enteredAt", "expectedEnd", "earned", "autoWandoosStarted" })
                Equal(Json(state, field).ToString(), Json(Invoke(scheduler, "ExportState"), field).ToString(), "scoped recovery preserves original scheduler commitment/" + field);
            Equal(searches, PresetSearches, "resource review/recovery performs no replacement optimizer search");
        }
        var scoped = SetupScopedExecutionReview(0, true);
        InjectNativeReviewConflict(true); InjectNativeReviewConflict(false);
        var identityType = Plugin.GetType("NGUInjector.FiniteActionIdentity", true);
        var unknownId = Activator.CreateInstance(identityType, All, null, new object[] { "review-irreversible-proof", "foreign-action", "original-acquisition", "original-entry", "original-opportunity" }, null);
        var unknown = Activator.CreateInstance(Plugin.GetType("NGUInjector.FiniteActionRecord", true), true);
        Set(unknown, "Identity", unknownId); Set(unknown, "State", Enum.Parse(Plugin.GetType("NGUInjector.FiniteActionState", true), "Unknown"));
        Set(unknown, "Before", "irreversible invocation uncertain");
        var ledger = Property(MainInstance, "FiniteActions"); Invoke(ledger, "Save", unknown);
        var foreignMagic = CallMain("GlobalPresetLaneState", false);
        var unreviewedGrant = CallMain("ResolveExecutionGrant", "unreviewed-magic-observer", "original", Domain("Magic"), (Func<bool>)(() => true), null, null);
        var exact = CallMain("PreviewExecutionRebind", JsonParse("{}"));
        CallMain("ApplyExecutionRebind", ExecutionReviewApplyArgs(exact));
        Equal("Unknown", Get(Invoke(ledger, "Find", unknownId), "State").ToString(), "resource review cannot clear or rearm unknown finite-action history");
        Equal(true, CallMain("ValidateExecutionGrant", unreviewedGrant, Domain("Magic")), "reviewed Energy revision does not revoke an unreviewed Magic-only grant");
        Equal(2, Property(Get(Property(MainInstance, "_resourceSetupExecutor"), "Application"), "BlockedLanes"), "unreviewed Magic conflict survives Energy recovery");
        Equal(foreignMagic, CallMain("GlobalPresetLaneState", false), "review cannot overwrite an unreviewed foreign lane");
        CallMain("ReconcileCustomResources", scoped, true);
        Equal(foreignMagic, CallMain("GlobalPresetLaneState", false), "subsequent shared execution preserves the unreviewed foreign lane");
        var energyAfter = Get(Get(MainInstance, "_globalPresetReceipt"), "Execution");
        var energyShares = (long[])Get(energyAfter, "EnergyShares");
        var actualEnergy = (IList)Get(Get(Character, "NGU"), "skills");
        for (var i = 0; i < energyShares.Length; i++) Equal(energyShares[i], Get(actualEnergy[i], "energy"), "reviewed healthy Energy converges independently/" + i);
        var capacity = SetupScopedExecutionReview(12);
        Set(Get(MainType, "Settings"), "_manageEnergy", false);
        var before = CallMain("GlobalPresetAllocationState");
        var capPreview = CallMain("PreviewExecutionRebind", JsonParse("{}"));
        var capGrant = Get(Get(MainInstance, "_executionRebindReview"), "Grant");
        Equal(false, CallMain("ValidateExecutionGrant", capGrant, Domain("Energy")), "capacity-only review claims no Energy authority even with Energy unmanaged");
        Equal(false, CallMain("ValidateExecutionGrant", capGrant, Domain("Magic")), "capacity-only review claims no Magic authority");
        CallMain("ApplyExecutionRebind", ExecutionReviewApplyArgs(capPreview));
        Equal(before, CallMain("GlobalPresetAllocationState"), "capacity-only approval preserves every native resource allocation");
        var noResourceWrites = ResourceTransfers;
        CallMain("ReconcileCustomResources", capacity, true);
        Equal(before, CallMain("GlobalPresetAllocationState"), "ordinary capacity-only service preserves all allocations with both lanes unmanaged");
        Equal(noResourceWrites, ResourceTransfers, "ordinary capacity-only service gains no resource-write authority from reviewed setup");
        Equal(550, Get(Get(Get(Character, "inventory"), "head"), "id"), "ordinary capacity-only service retains the actual acquired capacity gear");
        var capacityRuntime = CallMain("GetResourceCoordinationRuntime");
        Console.WriteLine("Capacity-only unmanaged ordinary service: " + Property(Json(capacityRuntime, "state"), "Value")
            + "; " + Property(Json(capacityRuntime, "reason"), "Value"));
    }

    private static void CheckFightPreparationExecutionRebind()
    {
        var plan = SetupInlineStagePlan(); RunVerifiedInline(plan, "preparation original baseline");
        var current = (long)Get(Character, "curEnergy");
        var idle = (long)Get(Character, "idleEnergy");
        Set(Character, "curEnergy", current + 6000); Set(Character, "idleEnergy", idle + 6000);
        var searches = InlineGearSelections; var writes = ResourceTransfers;
        const string opportunity = "native-original-preparation-request";
        CallMain("RunFightPrep", plan, false, false, "Native preparation", null, opportunity);
        var claim = Property(MainInstance, "FightPreparationClaim");
        Check(claim != null, "real preparation retains its claim after preserving-capacity preflight rejection");
        Equal(false, Property(MainInstance, "FightPreparationVerified"), "preparation cannot pretend an unaffordable exact setup was applied");
        Equal(false, Property(MainInstance, "BossPushRunning"), "rejected setup never enters a boss process");
        var commitment = CallMain("ExportFightPreparationCommitment");
        Equal("selected", Property(Json(commitment, "stage"), "Value"), "durable selected stage records preflight attempt without invoking a fight");
        var identity = Property(MainInstance, "FightPreparationIdentity");
        var path = Path.Combine((string)Get(MainType, "_dir"), "execution-fight-prep-selection.json");
        var saved = JsonParse(File.ReadAllText(path));
        foreach (var field in new[] { "kind", "owner", "acquisition", "entry", "opportunity", "original" })
            Equal(Json(commitment, field).ToString(), Json(Json(saved, "commitments"), field).ToString(), "exact setup stores C's original semantic preparation commitment/" + field);
        Equal("acquired", Property(Json(Json(saved, "commitments"), "stage"), "Value"), "selection checkpoint precedes the separately durable selected-attempt ledger stage");
        Equal(writes, ResourceTransfers, "failed preserving preflight performs no native resource cleanup");
        var acquiredSearches = InlineGearSelections;
        Check(acquiredSearches > searches, "initial explicit preparation actually selects gear once");
        CallMain("EndExecutionClaim", claim, false);
        Set(MainInstance, "_fightPreparationClaim", null); Set(MainInstance, "_fightPreparationSetup", null);
        Set(MainInstance, "_fightPreparationRecord", null); Set(MainInstance, "_finiteActionLedger", null);
        // An external native pool change makes the original selected setup feasible;
        // it is not an optimizer event or permission to renew the process window.
        Set(Character, "curEnergy", current); Set(Character, "idleEnergy", idle);
        Set(MainInstance, "_executionExactLoaded", false); CallMain("EnsureExecutionExactLoaded");
        var preview = CallMain("PreviewExecutionRebind", JsonParse("{}"));
        Equal(opportunity, Property(Json(Json(preview, "commitments"), "opportunity"), "Value"), "review retains the actual original request opportunity");
        var rawOriginal = Json(commitment, "original").ToString();
        Equal(rawOriginal, Json(Json(preview, "commitments"), "original").ToString(), "review retains original start/entry/fruit options/window bytes");
        var args = ExecutionReviewApplyArgs(preview);
        CallMain("ApplyExecutionRebind", args);
        Equal(identity, Property(MainInstance, "FightPreparationIdentity"), "C's importer grants fresh authority to the SAME process identity");
        CallMain("ServiceReviewedExactSetup");
        Equal(true, Property(MainInstance, "FightPreparationVerified"), "reviewed original preparation setup actually verifies through shared native execution");
        Equal(false, Property(MainInstance, "BossPushRunning"), "review/setup reconciliation does not invoke an entry fight");
        Equal(acquiredSearches, InlineGearSelections, "preparation review/retry never searches replacement gear");
        Equal(rawOriginal, Json(CallMain("ExportFightPreparationCommitment"), "original").ToString(), "native setup replay retains original process budget/history");
        CallMain("RunFightPrep", plan, false, true, "Changed caller text", null, "different-retry-opportunity");
        Equal(false, Property(MainInstance, "BossPushRunning"), "a different request cannot supersede the retained preparation or its budget");
        CallMain("RunFightPrep", plan, false, true, "Original request retry", null, opportunity);
        Equal(true, Property(MainInstance, "BossPushRunning"), "verified original preparation starts its original route process once");
        Equal(identity, Get(MainInstance, "_bossPushIdentity"), "route process consumes the original preparation identity rather than caller retry text");
        Equal("fight-requested", Property(Json(CallMain("ExportFightPreparationCommitment"), "stage"), "Value"), "durable stage records actual fight request");
        CallMain("RunFightPrep", plan, false, true, "Repeated request", null, opportunity);
        Equal(identity, Get(MainInstance, "_bossPushIdentity"), "still-true preparation conditions cannot replace the admitted process");
        Equal(acquiredSearches, InlineGearSelections, "started process retries retain its exact selected setup");
        var process = Get(MainInstance, "_bossPushProcess");
        var settingsClaim = Get(process, "SettingsClaim");
        CallMain("EndExecutionClaim", Property(MainInstance, "FightPreparationClaim"), false);
        if (settingsClaim != null) CallMain("EndExecutionClaim", settingsClaim, false);
        Set(MainInstance, "_bossPushProcess", null); Set(MainInstance, "_bossPushRunning", false);
        Set(MainInstance, "_fightPreparationClaim", null); Set(MainInstance, "_fightPreparationSetup", null);
        Set(MainInstance, "_fightPreparationRecord", null); Set(MainInstance, "_executionExactLoaded", false);
        CallMain("EnsureExecutionExactLoaded");
        preview = CallMain("PreviewExactExecutionRebind", "fight-prep");
        writes = ResourceTransfers; var swaps = Get(typeof(Program), "Swaps");
        Throws(() => CallMain("ApplyExecutionRebind", ExecutionReviewApplyArgs(preview)), "route-history", "started history is a scoped blocker, never replayed as fresh preparation");
        Equal(writes, ResourceTransfers, "unknown started preparation review performs zero native resource writes");
        Equal(swaps, Get(typeof(Program), "Swaps"), "unknown started preparation retains native prerequisites");
        Equal(false, Property(MainInstance, "BossPushRunning"), "started-history ambiguity never repeats entry fight");
    }

    private static void CheckForeignNativeSettingsReviewScope()
    {
        SetupResourceCoordination(true, true, "none", true);
        var targets = (long[])Get(Get(Character, "advancedTraining"), "levelTarget");
        targets[3] = 777;
        CallMain("ObserveGlobalPresetTargetConflict", "foreign-native-setting-owner");
        var preview = CallMain("PreviewExecutionRebind", JsonParse("{}"));
        var writes = ResourceTransfers; var swaps = Get(typeof(Program), "Swaps");
        Throws(() => CallMain("ApplyExecutionRebind", ExecutionReviewApplyArgs(preview)), "scope changed", "reviewed AT gate cannot acknowledge another native-setting owner's uncertainty");
        Equal("foreign-native-setting-owner", Get(MainInstance, "_globalPresetNativeTargetConflict"), "foreign native-setting owner evidence is not globally cleared");
        Equal(777L, targets[3], "unreviewed native target is preserved");
        Equal(writes, ResourceTransfers, "foreign native-setting review rejection performs zero resource writes");
        Equal(swaps, Get(typeof(Program), "Swaps"), "foreign native-setting review rejection retains native prerequisites");
    }

    private static void CheckCompletedFightPreparationExecutionRebind()
    {
        var plan = SetupInlineStagePlan(); RunVerifiedInline(plan, "completed preparation baseline");
        const string opportunity = "native-completed-manual-preparation";
        CallMain("RunFightPrep", plan, false, false, "Explicit terminal preparation", null, opportunity);
        Equal(true, Property(MainInstance, "BossPushRunning"), "terminal-review fixture admits the actual original boss process");
        var original = CallMain("ExportFightPreparationCommitment");
        CallMain("StopBossPush");
        Equal(false, Property(MainInstance, "BossPushRunning"), "actual route owner observes safe retirement before classifying completion");
        var completed = CallMain("ExportFightPreparationCommitment");
        Equal("Committed", Property(Json(completed, "state"), "Value"), "C retains durable completed process evidence after native retirement");
        var searches = InlineGearSelections; var writes = ResourceTransfers; var swaps = Get(typeof(Program), "Swaps");
        var scheduler = Get(MainInstance, "_protectedScheduler"); var state = Invoke(scheduler, "ExportState");
        Set(MainInstance, "_completedFightPreparationRecord", null); Set(MainInstance, "_finiteActionLedger", null);
        Set(MainInstance, "_executionExactLoaded", false); CallMain("EnsureExecutionExactLoaded");
        CallMain("RecordSelectedSetupConflict", Character, Get(Get(Character, "stats"), "rebirthNumber"), "route", "unreviewed route observation");
        var unreviewed = CallMain("ResolveExecutionGrant", "unreviewed-gear-observation", "original", Domain("Gear"), (Func<bool>)(() => true), null, null);
        var preview = CallMain("PreviewExactExecutionRebind", "fight-prep");
        Equal("Committed", Property(Json(Json(preview, "commitments"), "state"), "Value"), "review reads terminal classification from the real durable ledger");
        var grant = Get(Get(MainInstance, "_executionRebindReview"), "Grant");
        Equal(false, CallMain("ValidateExecutionGrant", grant, Domain("Gear")), "terminal history review does not import setup mutation authority");
        Equal(false, CallMain("ValidateExecutionGrant", grant, Domain("FiniteAction")), "terminal history review never authorizes another finite effect");
        CallMain("ApplyExecutionRebind", ExecutionReviewApplyArgs(preview));
        Equal(true, CallMain("ValidateExecutionGrant", unreviewed, Domain("Gear")), "metadata-only process review does not clear or revoke any unreviewed domain");
        Equal(null, Property(MainInstance, "FightPreparationClaim"), "completed process does not reacquire a preparation claim");
        Equal(false, Property(MainInstance, "BossPushRunning"), "completed process review cannot reopen the entry fight");
        Equal(false, Get(MainInstance, "_customPreRebirthPrepCompleted"), "manual fight-only history is not fabricated automatic rebirth preparation");
        var restored = CallMain("ExportFightPreparationCommitment");
        foreach (var field in new[] { "owner", "acquisition", "entry", "opportunity", "original" })
            Equal(Json(original, field).ToString(), Json(restored, field).ToString(), "terminal process import retains original identity/start/window/relationship/" + field);
        Equal(writes, ResourceTransfers, "terminal process review performs zero resource writes");
        Equal(swaps, Get(typeof(Program), "Swaps"), "terminal process review performs zero gear writes");
        Equal(searches, InlineGearSelections, "terminal process review performs zero replacement selection");
        foreach (var field in new[] { "entry", "enteredAt", "expectedEnd", "earned", "autoWandoosStarted" })
            Equal(Json(state, field).ToString(), Json(Invoke(scheduler, "ExportState"), field).ToString(), "terminal process review cannot renew paused plan commitments/" + field);
        var path = Path.Combine((string)Get(MainType, "_dir"), "execution-fight-prep-selection.json");
        Equal("false", Json(JsonParse(File.ReadAllText(path)), "active").ToString(), "reviewed completed process is durably inactive, with evidence preserved");
    }

    private static void CheckFightPreparationEffectUncertainty()
    {
        var plan = SetupInlineStagePlan(); RunVerifiedInline(plan, "preparation fruit-identity baseline");
        var yggManager = Plugin.GetType("NGUInjector.Managers.YggdrasilManager", true);
        var alpha = (int)Get(yggManager, "FruitOfPowerAlphaId"); var beta = (int)Get(yggManager, "FruitOfPowerBetaId");
        var ygg = Get(Character, "yggdrasil") ?? Child(Character, "yggdrasil");
        var fruits = ListField(ygg, "fruits", Math.Max(alpha, beta) + 1);
        Set(fruits[alpha], "harvests", 5); Set(fruits[beta], "harvests", 11);
        Set(Character, "curEnergy", (long)Get(Character, "curEnergy") + 6000);
        Set(Character, "idleEnergy", (long)Get(Character, "idleEnergy") + 6000);
        CallMain("RunFightPrep", plan, false, true, "Original power-fruit preparation", null, "original-fruit-preparation-request");
        Equal(false, Property(MainInstance, "BossPushRunning"), "capacity rejection holds actual preparation before all fruit/fight effects");
        var preparation = Get(MainInstance, "_fightPreparationRecord"); var identity = Get(preparation, "Identity");
        var semantic = CallMain("ExportFightPreparationCommitment");
        Equal("5", Json(Json(Json(semantic, "original"), "fruitHarvests"), alpha.ToString()).ToString(), "C acquires the original alpha opportunity before setup effects");
        Equal("11", Json(Json(Json(semantic, "original"), "fruitHarvests"), beta.ToString()).ToString(), "C acquires the original beta opportunity before setup effects");
        CallMain("EndExecutionClaim", Property(MainInstance, "FightPreparationClaim"), false);
        Set(MainInstance, "_fightPreparationClaim", null); Set(MainInstance, "_fightPreparationSetup", null); Set(MainInstance, "_fightPreparationRecord", null);
        Set(MainInstance, "_executionExactLoaded", false); CallMain("EnsureExecutionExactLoaded");
        var preview = CallMain("PreviewExactExecutionRebind", "fight-prep");
        var fruitOpportunity = CallMain("PreparationFruitOpportunity", identity, alpha);
        var effectId = CallMain("ActionIdentity", "fruit-harvest:" + alpha, Get(identity, "Owner"), fruitOpportunity, null);
        var record = Activator.CreateInstance(Plugin.GetType("NGUInjector.FiniteActionRecord", true), true);
        Set(record, "Identity", effectId); Set(record, "State", Enum.Parse(Plugin.GetType("NGUInjector.FiniteActionState", true), "Unknown"));
        Set(record, "Before", "original alpha invocation outcome unavailable");
        var ledger = Property(MainInstance, "FiniteActions"); Invoke(ledger, "Save", record);
        var writes = ResourceTransfers; var swaps = Get(typeof(Program), "Swaps"); var searches = InlineGearSelections;
        Throws(() => CallMain("ApplyExecutionRebind", ExecutionReviewApplyArgs(preview)), "unresolved", "new uncertainty in the original effect invalidates a previously reviewed process");
        Set(fruits[alpha], "harvests", 6); // A newer native observation must not acquire a different action opportunity.
        Throws(() => CallMain("PreviewExactExecutionRebind", "fight-prep"), "unresolved", "current fruit growth/count cannot replace the original uncertain action identity");
        Equal("Unknown", Get(Invoke(ledger, "Find", effectId), "State").ToString(), "preparation review preserves original irreversible uncertainty");
        Equal(null, Property(MainInstance, "FightPreparationClaim"), "unresolved effects do not gain a fresh process claim");
        Equal(writes, ResourceTransfers, "uncertain preparation effect produces zero native resource writes");
        Equal(swaps, Get(typeof(Program), "Swaps"), "uncertain preparation effect preserves native setup");
        Equal(searches, InlineGearSelections, "uncertain preparation effect performs no replacement selection");
        Equal(false, Property(MainInstance, "BossPushRunning"), "uncertain preparation effect cannot repeat entry fight");
    }

    private static void CheckQuickExecutionRebind()
    {
        SetupInlineStagePlan();
        var settings = Get(MainType, "Settings");
        Set(settings, "_quickLoadout", new[] { 552 }); Set(settings, "_quickDiggers", new int[0]);
        var inventory = Get(Character, "inventory"); var original = Get(inventory, "head");
        var searches = PresetSearches;
        CallMain("ToggleQuickExecutionClaim");
        var claim = Get(MainInstance, "_quickExecutionClaim"); var acquisition = Get(claim, "Acquisition");
        Equal(552, Get(Get(inventory, "head"), "id"), "native F8 first toggle applies exact selected gear");
        Check(Get(claim, "ExplicitReturnBaseline") != null, "F8 explicitly acquires its shipped return baseline");
        var file = Path.Combine((string)Get(MainType, "_dir"), "execution-quick-selection.json");
        var saved = JsonParse(File.ReadAllText(file));
        Check(Property(Json(saved, "returnBaseline"), "IsObject").Equals(true), "Quick return selection is semantic durable input, not a native lease");
        CallMain("EndExecutionClaim", claim, false); Set(MainInstance, "_quickExecutionClaim", null);
        Set(MainInstance, "_executionExactLoaded", false); CallMain("EnsureExecutionExactLoaded");
        var preview = CallMain("PreviewExecutionRebind", JsonParse("{}"));
        Equal(acquisition, Property(Json(preview, "acquisition"), "Value"), "Quick restart review retains original acquisition");
        CallMain("ApplyExecutionRebind", ExecutionReviewApplyArgs(preview));
        CallMain("ServiceReviewedExactSetup");
        Equal(acquisition, Get(Get(MainInstance, "_quickExecutionClaim"), "Acquisition"), "Quick reviewed claim has fresh authority with the SAME semantic identity");
        Equal(552, Get(Get(inventory, "head"), "id"), "Quick reviewed replay retains the exact native instance");
        CallMain("ToggleQuickExecutionClaim");
        Check(ReferenceEquals(original, Get(inventory, "head")), "no-successor F8 explicit return baseline is honored after reviewed restart");
        Equal("false", Json(JsonParse(File.ReadAllText(file)), "active").ToString(), "ended Quick intent is durably tombstoned");
        Equal(searches, PresetSearches, "F8 reviewed replay/return never performs replacement optimization");
    }

    private static void CheckFocusExecutionRebind()
    {
        var plan = SetupInlineStagePlan(); RunVerifiedInline(plan, "durable focus baseline");
        var focus = Enum.Parse(Plugin.GetType("NGUInjector.CustomFocusGoal", true), "MagicNgu");
        CallMain("ApplyCustomFocusGoal", focus);
        Equal("applied", CallMain("GetCustomFocusStatus"), "native focus acquired before restart review");
        var selection = Get(MainInstance, "_focusSetupSelection"); var desired = Get(selection, "Desired");
        var acquisition = Get(desired, "Acquisition"); var scheduler = Get(MainInstance, "_protectedScheduler");
        var state = Invoke(scheduler, "ExportState");
        var gear = Get(Get(Character, "inventory"), "head"); var searches = InlineGearSelections;
        Set(Get(MainType, "Settings"), "_manageEnergy", false);
        var energyBefore = CallMain("GlobalPresetLaneState", true);
        InjectNativeReviewConflict(false);
        var livePreview = CallMain("PreviewExecutionRebind", JsonParse("{}"));
        var liveGrant = Get(Get(MainInstance, "_executionRebindReview"), "Grant");
        Equal(false, CallMain("ValidateExecutionGrant", liveGrant, Domain("Energy")), "Magic focus review does not ask for unmanaged Energy authority");
        CallMain("ApplyExecutionRebind", ExecutionReviewApplyArgs(livePreview));
        CallMain("RunCustomSustainedFocusAllocation", true);
        Equal("applied", CallMain("GetCustomFocusStatus"), "explicit review actually recovers a live foreign Magic focus allocation");
        Equal(energyBefore, CallMain("GlobalPresetLaneState", true), "focus recovery preserves unmanaged Energy allocations");
        Equal(acquisition, Get(Get(Get(MainInstance, "_focusSetupSelection"), "Desired"), "Acquisition"), "live focus recovery retains original acquisition");
        Set(MainInstance, "_focusSetupSelection", null); Set(MainInstance, "_customRequestedFocusGoal", null);
        Set(MainInstance, "_customSustainedFocusGoal", null); Set(MainInstance, "_executionBaseline", null);
        Set(MainInstance, "_executionExactLoaded", false); CallMain("EnsureExecutionExactLoaded");
        var preview = CallMain("PreviewExecutionRebind", JsonParse("{}"));
        Equal(acquisition, Property(Json(preview, "acquisition"), "Value"), "focus review retains original acquisition");
        CallMain("ApplyExecutionRebind", ExecutionReviewApplyArgs(preview));
        CallMain("RunCustomSustainedFocusAllocation", true);
        Equal("applied", CallMain("GetCustomFocusStatus"), "reviewed focus continues through actual native allocation");
        Equal(acquisition, Get(Get(Get(MainInstance, "_focusSetupSelection"), "Desired"), "Acquisition"), "focus continuation never acquires a substitute");
        Check(ReferenceEquals(gear, Get(Get(Character, "inventory"), "head")), "focus continuation pins the SAME native selected instance");
        Equal(searches, InlineGearSelections, "focus continuation performs no replacement gear search");
        foreach (var field in new[] { "entry", "enteredAt", "expectedEnd", "earned", "completed", "autoWandoosStarted" })
            Equal(Json(state, field).ToString(), Json(Invoke(scheduler, "ExportState"), field).ToString(), "focus paused-plan commitment remains unchanged/" + field);
        CallMain("StopCustomFocusGoal");
        var path = Path.Combine((string)Get(MainType, "_dir"), "execution-focus-selection.json");
        Equal("false", Json(JsonParse(File.ReadAllText(path)), "active").ToString(), "explicit focus stop durably invalidates its original acquired intent");
    }

    private static void CheckExecutionLifecycleRevocation()
    {
        SetupInlineStagePlan();
        var grant = CallMain("ResolveExecutionGrant", "lifecycle-check", "existing", Domain("Energy"), (Func<bool>)(() => true), null, null);
        var boss = Get(Character, "bossController"); var head = Get(Get(Character, "inventory"), "head");
        Set(boss, "isFighting", true);
        Throws(() => CallMain("QuiesceExecution", "test bounded quiesce"), "Quiesced", "unknown native boss withholds retirement rather than removing prerequisites");
        Equal(false, CallMain("ValidateExecutionGrant", grant, Domain("Energy")), "quiescence revokes all preceding ordinary callbacks");
        Check(ReferenceEquals(head, Get(Get(Character, "inventory"), "head")), "unknown native boss prerequisites retained");
        var transfers = ResourceTransfers;
        CallMain("QuickStuff"); CallMain("AutomationRoutine"); CallMain("ReconcileCustomResources", null, true);
        Equal(transfers, ResourceTransfers, "ordinary callbacks perform no resource writes after revocation");
        Set(boss, "isFighting", false); // External native completion, not an injector cancellation.
        CallMain("QuiesceExecution", "retry after observed native completion");
        Equal("Quiescing", Get(MainInstance, "_executionLifecycle").ToString(), "bounded retirement finishes without a Running bypass");
        Equal(transfers, ResourceTransfers, "bounded native retirement does not clear/reapply ordinary resources");
    }

    private static void CheckExplicitExecutionRestart()
    {
        var plan = SetupInlineStagePlan();
        var settings = Get(MainType, "Settings"); var name = (string)Property(settings, "CustomPlanFile");
        Invoke(Get(MainType, "CustomPlanStore"), "Save", name, plan);
        RunVerifiedInline(plan, "explicit recovery original entry");
        var path = (string)MainType.GetProperty("ProtectedCheckpointPath", All).GetValue(MainInstance);
        const string corrupt = "{unrecoverable original bytes";
        File.WriteAllText(path, corrupt); CallMain("InitializeProtectedStageProgress", plan);
        var runtime = CallMain("GetExecutionPersistenceRuntime"); var recovery = Json(runtime, "recovery");
        var args = JsonParse("{\"stageIndex\":0,\"checkpointRevision\":" + Json(recovery, "checkpointRevision")
            + ",\"planRevision\":" + Json(recovery, "planRevision") + "}");
        var preview = CallMain("PreviewExecutionRestart", args);
        Equal(corrupt, File.ReadAllText(path), "explicit recovery preview preserves original corrupt bytes");
        var writes = ResourceTransfers;
        CallMain("ApplyExecutionRebind", ExecutionReviewApplyArgs(preview));
        Equal(null, Get(MainInstance, "_protectedOuterCheckpointIssue"), "only tokenized explicit restart releases unrecoverable checkpoint block");
        var archives = Directory.GetFiles(Path.GetDirectoryName(path), "stage-recovery-*.json");
        Check(archives.Length == 1 && File.ReadAllText(archives[0]) == corrupt, "explicit restart archives exact original evidence before replacement");
        var scheduler = Get(MainInstance, "_protectedScheduler");
        Equal(0, Property(scheduler, "StageIndex"), "explicitly selected recovery stage is used without implicit row choice");
        Check(((double[])Property(scheduler, "EarnedSeconds")).All(x => x == 0), "explicit new entry cannot fabricate protected credit");
        Equal(writes, ResourceTransfers, "restart acceptance queues new intent without hidden native allocation cleanup");
        Throws(() => CallMain("ApplyExecutionRebind", ExecutionReviewApplyArgs(preview)), "expired", "recovery token cannot be reused to renew another budget");
    }

    private static void CheckInvalidOuterExecutionCheckpoint()
    {
        var plan = SetupResourceCoordination(true, true, "none");
        var path = (string)MainType.GetProperty("ProtectedCheckpointPath", All).GetValue(MainInstance);
        foreach (var bytes in new[] { "{unreadable", "{\"version\":999}" })
        {
            File.WriteAllText(path, bytes);
            CallMain("InitializeProtectedStageProgress", plan);
            var searches = PresetSearches; var transfers = ResourceTransfers;
            Equal(null, CallMain("GetProtectedStage", plan), "invalid outer checkpoint cannot admit a fresh uncredited entry");
            Equal(false, CallMain("SaveProtectedStageProgress", true), "invalid outer checkpoint blocks writes");
            Equal(bytes, File.ReadAllText(path), "invalid outer bytes survive observation and forced persistence");
            Equal(searches, PresetSearches, "invalid checkpoint cannot authorize replacement selection");
            Equal(transfers, ResourceTransfers, "invalid checkpoint cannot fund an uncredited new budget");
        }
    }
}
