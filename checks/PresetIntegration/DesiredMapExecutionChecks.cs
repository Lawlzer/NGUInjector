using System;
using System.Collections;
using System.Linq;

public static partial class PresetIntegrationChecks
{
    // This SimpleJSON version only materializes tokens inside a container.
    // Keep scalar types (including numeric strings) intact when cloning/mutating fixtures.
    private static object ParseFixtureScalar(string json)
    {
        return Json(JsonParse("{\"value\":" + json + "}"), "value");
    }

    private static void CheckPresetFinalReviewRegressions()
    {
        var failures = new System.Collections.Generic.List<Exception>();
        Action<string, Action> run = (name, test) => {
            try { test(); Console.WriteLine("PASS preset final review " + name); }
            catch (Exception error) { failures.Add(new Exception(name, error)); Console.Error.WriteLine("FAIL preset final review " + name + ": " + error); }
            finally { FailAtTargetText = false; AtRefreshObserver = null; PodInterference = null; }
        };
        run("compiled zero-reference score semantics", CheckCompiledZeroReferenceScoring);
        run("direct selected-policy export before scheduled acquisition", CheckDirectSelectedPolicyExport);
        foreach (var revoked in new[] { "energy", "loadout", "digger", "drag", "coordinator" })
            run("revoked native return/" + revoked, () => CheckNativeTargetReturnAuthority(revoked));
        run("save replacement between native returns", CheckNativeTargetReturnSaveReplacement);
        foreach (var change in new[] { "energy", "lock" })
            run("authority changes between native returns/" + change, () => CheckNativeTargetReturnCallbackAuthority(change));
        run("rotation resumes its accepted decision", () => CheckPendingRotationRetry(false));
        run("foreign rotation partial remains blocked", () => CheckPendingRotationRetry(true));
        foreach (var autoUpdate in new[] { false, true })
        {
            foreach (var fixedTime in new[] { false, true })
                run("selected-policy checkpoint resume/fixed=" + fixedTime + "/AutoUpdate=" + autoUpdate,
                    () => CheckSelectedPolicyCheckpoint(autoUpdate, fixedTime));
            run("versioned equipment identity/AutoUpdate=" + autoUpdate, () => CheckVersionedEquipmentSelection(autoUpdate));
            run("equipped-only allocation observation/AutoUpdate=" + autoUpdate, () => CheckEquippedOnlyAllocationObservation(autoUpdate));
            foreach (var itopod in new[] { false, true })
                run("acquired-route observation/ITOPOD=" + itopod + "/AutoUpdate=" + autoUpdate,
                    () => CheckAcquiredRouteObservation(autoUpdate, itopod, false));
            run("original mandatory-AT zero-reference mix/AutoUpdate=" + autoUpdate,
                () => CheckAcquiredRouteObservation(autoUpdate, true, true));
        }
        if (failures.Count > 0) throw new AggregateException("Preset final review regressions failed.", failures);
    }

    private static void CheckDirectSelectedPolicyExport()
    {
        // Acquire via the already-supported dashboard path, then invoke only the
        // exporter with real policy metadata. No scheduled Equip/persistence catch
        // can hide the original serialization stack in this focused regression.
        var plan = SetupResourceCoordination(false, false, "none");
        var policy = Get(Get(MainInstance, "_globalPresetReceipt"), "Policy");
        Set(Get(MainType, "Settings"), "_useCustomRebirthPlan", true);
        CallMain("GetProtectedStage", plan);
        var owner = (string)MainType.GetProperty("ProtectedPresetOwner", All).GetValue(MainInstance);
        Check(owner != null, "direct export has initialized scheduler provenance");
        var acquiredPlan = Get(policy, "Plan"); var candidate = Get(policy, "Candidate");
        var support = Get(candidate, "Support"); var activity = Get(candidate, "Activities");
        Console.WriteLine("Direct selected-policy export metadata: adventureTaskKey=" + (Get(policy, "AdventureTaskKey") ?? "<null>")
            + "; adventureTask=" + (Get(policy, "AdventureTask") ?? "<null>")
            + "; referenceKeys=" + (Get(acquiredPlan, "ReferenceKeys") == null ? "<null>" : ((Array)Get(acquiredPlan, "ReferenceKeys")).Length.ToString())
            + "; referenceProduction=" + (Get(acquiredPlan, "ReferenceProduction") == null ? "<null>" : ((Array)Get(acquiredPlan, "ReferenceProduction")).Length.ToString())
            + "; nullReferenceKeys=" + (Get(acquiredPlan, "ReferenceKeys") == null ? "<array-null>" : ((string[])Get(acquiredPlan, "ReferenceKeys")).Count(x => x == null).ToString())
            + "; diggerWeights=" + (support == null || Get(support, "DiggerWeights") == null ? "<null>" : ((Array)Get(support, "DiggerWeights")).Length.ToString())
            + "; AT=" + (activity == null || Get(activity, "AT") == null ? "<null>" : ((Array)Get(activity, "AT")).Length.ToString()));
        var transfers = ResourceTransfers; var swaps = Get(typeof(Program), "Swaps");
        var exported = CallMain("ExportProtectedPresetSelection", policy, owner);
        Equal(Get(policy, "Acquisition"), Property(Json(exported, "acquisition"), "Value"), "direct export retains actual acquisition");
        Equal(null, Get(policy, "AdventureTaskKey"), "static preset fixture exercises absent optional task identity");
        Equal(true, Property(Json(exported, "adventureTaskKey"), "IsNull"), "absent optional task identity is JSON null");
        Equal(true, Property(Json(exported, "adventureTask"), "IsNull"), "absent optional task label is JSON null");
        var keys = (string[])Get(acquiredPlan, "ReferenceKeys");
        Check(keys.Any(x => x == null), "export fixture exercises absent objective references");
        for (var i = 0; i < keys.Length; i++)
            Equal(keys[i] == null, Property(Json(Json(Json(exported, "references"), i), "key"), "IsNull"), "reference key preserves optional absence/" + i);
        foreach (var field in new[] { "Normalization", "ReferenceKeys", "ReferenceProduction" })
        {
            var original = Get(acquiredPlan, field);
            try
            {
                Set(acquiredPlan, field, null);
                Throws(() => CallMain("ExportProtectedPresetSelection", policy, owner), "policy is incomplete", "required acquired metadata is not silently fabricated/" + field);
            }
            finally { Set(acquiredPlan, field, original); }
        }
        Equal(transfers, ResourceTransfers, "direct export performs no resource mutation");
        Equal(swaps, Get(typeof(Program), "Swaps"), "direct export performs no gear mutation");
    }

    private static void CheckSelectedPolicyCheckpoint(bool autoUpdate, bool fixedTime)
    {
        var plan = SetupCommitBudget(); var stage = ((Array)Get(plan, "Stages")).GetValue(0);
        if (fixedTime)
        {
            Set(stage, "CompletionMode", 0);
            Set(stage, "TargetAdvancedTrainingPower", 0L); Set(stage, "TargetAdvancedTrainingToughness", 0L);
            // SetupCommitBudget starts with unowned -1 menu exclusions that its
            // mandatory stage normally replaces in capture. This timed fixture
            // deliberately owns no AT requirement; enable its positively weighted
            // ordinary AT activity explicitly, as the manual budget fixture does.
            Equal(null, Get(MainInstance, "_globalPresetNativeTargets"), "timed fixture clears only unowned menu targets");
            Set(Get(Character, "advancedTraining"), "levelTarget", new long[5]);
        }
        CallMain("RunCustomPlanAutomation", plan);
        RequireInitialScheduledReceipt(plan, "selected-policy checkpoint/fixed=" + fixedTime + "/AutoUpdate=" + autoUpdate);
        Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), "checkpoint fixture first applies real selected setup");
        var receipt = Get(MainInstance, "_globalPresetReceipt"); var policy = Get(receipt, "Policy");
        var owner = (string)Get(receipt, "Owner");
        if (fixedTime)
        {
            foreach (var clock in new[] { "rebirthTime", "totalPlaytime" })
                Set(Get(Character, clock), "totalseconds", (double)Get(Get(Character, clock), "totalseconds") + 2d);
            Set(Get(MainInstance, "_protectedScheduler"), "_lastHeartbeat",
                (double)MainType.GetProperty("ProtectedHeartbeat", All).GetValue(null) - 2d);
            CallMain("GetProtectedStage", plan);
            Check(((double[])Property(Get(MainInstance, "_protectedScheduler"), "EarnedSeconds"))[0] > 0,
                "fixed checkpoint contains real scheduler credit from verified applied setup, not just zeros");
        }
        Set(Get(policy, "Preset"), "AutoUpdate", autoUpdate); Set(Get(policy, "ExecutionPreset"), "AutoUpdate", autoUpdate);
        CallMain("PersistProtectedPresetSelection", policy, owner);
        var path = (string)MainType.GetProperty("ProtectedCheckpointPath", All).GetValue(MainInstance);
        var checkpoint = JsonParse(System.IO.File.ReadAllText(path)); var selection = Json(checkpoint, "selectedPolicy");
        Equal(2, Property(Json(checkpoint, "version"), "AsInt"), "policy-bearing checkpoint has exact version two");
        Equal(Get(policy, "Acquisition"), Property(Json(selection, "acquisition"), "Value"), "checkpoint carries acquisition, not application receipt identity");
        Equal(1, Property(Json(selection, "version"), "AsInt"), "selected-policy schema version is independent");
        Equal(Get(policy, "Definition"), Property(Json(selection, "definition"), "Value"), "acquired definition survives without saved-library substitution");
        Equal(Invoke(Get(policy, "Preset"), "ToJson").ToString(), Json(selection, "preset").ToString(), "original preset definition roundtrips");
        Equal(Invoke(Get(policy, "ExecutionPreset"), "ToJson").ToString(), Json(selection, "executionPreset").ToString(), "resolved preset definition roundtrips");
        var decision = Json(selection, "decision"); var candidate = Get(policy, "Candidate");
        foreach (var field in new[] { "BloodPercent", "Ritual", "Zone", "Floor", "RegularEligibilityZone", "BoostEligibilityZone" })
            Equal(Get(candidate, field), Property(Json(decision, char.ToLowerInvariant(field[0]) + field.Substring(1)), "AsInt"), "selected route/split/witness retained/" + field);
        Equal(Get(Get(candidate, "Activities"), "AugmentId"), Property(Json(Json(decision, "activities"), "augmentId"), "AsInt"), "acquired AUG identity retained");
        Equal(Get(Get(candidate, "Activities"), "Os"), Property(Json(Json(decision, "activities"), "os"), "AsInt"), "acquired OS identity retained");
        var serialized = selection.ToString();
        Equal(serialized, CallMain("ExportProtectedPresetSelection", policy, owner).ToString(), "detached semantic snapshot is deterministic");
        var originalHorizon = Get(policy, "Horizon"); Set(policy, "Horizon", 123);
        Equal(serialized, Get(MainInstance, "_protectedSelectedPolicy"), "persisted string cannot alias later mutable runtime policy edits");
        Set(policy, "Horizon", originalHorizon);
        foreach (var change in new[] { "version", "entry", "rebirth", "origin", "elapsed", "total" })
        {
            var invalid = JsonParse(serialized);
            SetJson(invalid, change, ParseFixtureScalar(change == "version" ? "1.5" : change == "entry" || change == "rebirth" ? "\"foreign\"" : "1e100"));
            var issue = (string)CallMain("ValidateProtectedPresetProvenance", invalid);
            Check(issue.Contains(change == "version" ? "version/schema" : change == "entry" ? "stage entry" : "save context"), "typed provenance mismatch rejected/" + change);
        }
        var instances = (IDictionary)CallMain("LoadoutPresetInstances");
        var selected = (Array)CallMain("ResolveGlobalPresetEquipmentSelection", Json(selection, "equipment"),
            Get(policy, "Acquisition"), Get(policy, "Slots"), instances);
        var expected = (Array)Get(policy, "Items");
        for (var i = 0; i < selected.Length; i++)
            Check(ReferenceEquals(selected.GetValue(i), expected.GetValue(i)), "actual checkpoint equipment payload retains approved exact material/" + i);
        var selectedIndex = Enumerable.Range(0, selected.Length).First(i => selected.GetValue(i) != null);
        var selectedItem = selected.GetValue(selectedIndex);
        var selectedSource = instances.Keys.Cast<int>().Single(k => ReferenceEquals(instances[k], selectedItem));
        var matching = (IDictionary)Activator.CreateInstance(instances.GetType());
        foreach (DictionaryEntry row in instances) matching.Add(row.Key, row.Value);
        matching.Remove(selectedSource);
        Throws(() => CallMain("ResolveGlobalPresetEquipmentSelection", Json(selection, "equipment"), Get(policy, "Acquisition"),
            Get(policy, "Slots"), matching), "missing or ambiguous", "actual checkpoint payload rejects missing selected equipment");
        var changedItem = Invoke(selectedItem, "MemberwiseClone"); Set(changedItem, "curAttack", (float)Get(changedItem, "curAttack") + 1f);
        matching[selectedSource] = changedItem;
        Throws(() => CallMain("ResolveGlobalPresetEquipmentSelection", Json(selection, "equipment"), Get(policy, "Acquisition"),
            Get(policy, "Slots"), matching), "missing or ambiguous", "actual checkpoint payload rejects unexplained changed material");
        matching[selectedSource] = selectedItem;
        var extraSource = Enumerable.Range(0, 1024).First(k => !matching.Contains(k));
        matching[extraSource] = Invoke(selectedItem, "MemberwiseClone");
        Throws(() => CallMain("ResolveGlobalPresetEquipmentSelection", Json(selection, "equipment"), Get(policy, "Acquisition"),
            Get(policy, "Slots"), matching), "missing or ambiguous", "actual checkpoint payload rejects duplicate same-material instances");

        var directory = Get(MainType, "_dir"); var beforeFailureTransfers = ResourceTransfers;
        var beforeFailureSwaps = Get(typeof(Program), "Swaps");
        // A regular fixture-owned file cannot serve as a checkpoint directory. No
        // directory deletion/permission changes or production persistence stubs needed.
        try
        {
            Set(MainType, "_dir", path);
            Set(Get(policy, "Preset"), "AutoUpdate", false);
            Throws(() => Invoke(MainInstance, "EquipGlobalPresetDefinition", Get(policy, "Preset"), owner,
                "checkpoint-failure", false, null, stage, (int)Get(policy, "Horizon")), "could not be saved", "real cached Equip refuses selected setup when atomic checkpoint write fails");
            Equal(beforeFailureTransfers, ResourceTransfers, "checkpoint failure precedes selected resource funding");
            Equal(beforeFailureSwaps, Get(typeof(Program), "Swaps"), "checkpoint failure precedes selected gear swaps");
            Equal(serialized, Get(MainInstance, "_protectedSelectedPolicy"), "failed durable replacement retains previous immutable commitment");
        }
        finally { Set(MainType, "_dir", directory); Set(Get(policy, "Preset"), "AutoUpdate", autoUpdate); }
        Equal(true, CallMain("SaveProtectedStageProgress", true), "checkpoint write recovers after fixture file-path failure");

        var searches = PresetSearches; var transfers = ResourceTransfers; var swaps = Get(typeof(Program), "Swaps");
        var planning = Get(MainType, "_presetPlanningCaptures");
        var allocations = CallMain("GlobalPresetAllocationState"); var support = CallMain("GlobalPresetSupportState");
        var nativeW = Get(Character, "wandoos98");
        Set(nativeW, "energyLevel", Convert.ChangeType(7, Get(nativeW, "energyLevel").GetType()));
        Set(nativeW, "magicProgress", Convert.ChangeType(.25, Get(nativeW, "magicProgress").GetType()));
        var wFields = new[] { "os", "energyLevel", "magicLevel", "energyProgress", "magicProgress", "wandoosEnergy", "wandoosMagic" };
        var wValues = wFields.Select(f => Get(nativeW, f)).ToArray();
        // Model reload's discarded authority without doing a historical native target return.
        Set(MainInstance, "_globalPresetNativeTargets", null); Set(MainInstance, "_globalPresetReceipt", null);
        ((IDictionary)Get(MainInstance, "_globalPresetPolicies")).Clear();
        CallMain("InitializeProtectedStageProgress", plan);
        var restored = Invoke(Get(MainInstance, "_protectedScheduler"), "ExportState");
        foreach (var field in new[] { "entry", "cycle", "enteredAt", "expectedEnd", "stageElapsed", "durations", "earned", "applied", "completed", "visits", "autoWandoosStarted", "autoAtCompleted" })
            Equal(Json(Json(checkpoint, "scheduler"), field).ToString(), Json(restored, field).ToString(), "checkpoint retains protected timing/native history/" + field);
        Equal(false, Get(Get(MainInstance, "_protectedScheduler"), "_armed"), "reload does not import an armed application receipt");
        Equal(Property(Json(checkpoint, "fightEntry"), "Value"), Get(MainInstance, "_customStageFightKey") ?? "", "entry fight identity survives reload");
        Equal(null, CallMain("ValidateProtectedPresetProvenance", Json(checkpoint, "selectedPolicy")), "matching provenance passes necessary checks without granting authority");
        Equal(null, CallMain("ProtectedPresetRestorationIssue", owner), "valid frozen stage restores intent without action-history review");
        Equal(null, CallMain("ProtectedPresetRestorationIssue", "unrelated-owner"), "restoration remains scoped to the selected owner");
        Equal(null, Get(MainInstance, "_globalPresetReceipt"), "semantic import never fabricates applied authority");
        Equal(Get(policy, "Acquisition"), Get(Get(MainInstance, "_globalPresetApplyingPolicy"), "Acquisition"), "semantic import retains the exact frozen acquisition");
        Equal(transfers, ResourceTransfers, "semantic import leaves native accounting unchanged");
        CallMain("RunCustomPlanAutomation", plan);
        Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), "ordinary native admission verifies the restored frozen selection");
        Equal(Get(policy, "Acquisition"), Get(Get(Get(MainInstance, "_globalPresetReceipt"), "Policy"), "Acquisition"), "ordinary resume never replaces the acquisition");
        Equal(searches, PresetSearches, "valid reload does not search in either AutoUpdate mode");
        Check((long)Get(MainType, "_presetPlanningCaptures") > (long)planning, "restoration actually captures current native inputs before admitting saved intent");
        Equal(transfers, ResourceTransfers, "already-matching restored policy requires no native accounting writes");
        Equal(swaps, Get(typeof(Program), "Swaps"), "valid reload never equips replacement gear");
        Equal(allocations, CallMain("GlobalPresetAllocationState"), "native allocations are not rollback state");
        Equal(support, CallMain("GlobalPresetSupportState"), "native support is not rollback state");
        for (var i = 0; i < wFields.Length; i++) Equal(wValues[i], Get(nativeW, wFields[i]), "native W is never rolled back/" + wFields[i]);
        Equal(true, CallMain("SaveProtectedStageProgress", true), "resumed stage retains healthy checkpoint progress saving");
        var savedSelection = Json(JsonParse(System.IO.File.ReadAllText(path)), "selectedPolicy");
        var expectedSelection = JsonParse(serialized);
        for (var i = 0; i < expected.Length; i++)
        {
            var descriptor = Json(Json(Json(savedSelection, "equipment"), "equipment"), i);
            Equal(Json(descriptor, "targetSlot").ToString(), Json(descriptor, "sourceSlotHint").ToString(), "resumed descriptor observes its current equipped location/" + i);
            SetJson(Json(Json(Json(expectedSelection, "equipment"), "equipment"), i), "sourceSlotHint", Json(descriptor, "sourceSlotHint"));
        }
        Equal(expectedSelection.ToString(), savedSelection.ToString(), "all frozen decisions survive another checkpoint write; only current location hints may refresh");
        foreach (var invalidKind in new[] { "version", "entry", "save", "legacy" })
        {
            var invalidCheckpoint = JsonParse(checkpoint.ToString()); var invalidSelection = Json(invalidCheckpoint, "selectedPolicy");
            if (invalidKind == "legacy")
            {
                SetJson(invalidCheckpoint, "version", ParseFixtureScalar("1"));
                SetJson(invalidCheckpoint, "selectedPolicy", ParseFixtureScalar("null"));
            }
            else SetJson(invalidSelection, invalidKind == "save" ? "rebirth" : invalidKind,
                ParseFixtureScalar(invalidKind == "version" ? "2" : "\"foreign\""));
            System.IO.File.WriteAllText(path, invalidCheckpoint.ToString());
            Set(MainInstance, "_globalPresetReceipt", null); Set(MainInstance, "_globalPresetApplyingPolicy", null);
            ((IDictionary)Get(MainInstance, "_globalPresetPolicies")).Clear();
            CallMain("InitializeProtectedStageProgress", plan);
            var issue = (string)CallMain("ProtectedPresetRestorationIssue", owner);
            if (invalidKind == "legacy")
            {
                Equal(null, issue, "checkpoint without an acquired snapshot is eligible for first ordinary selection, not history review");
                Equal(null, Get(MainInstance, "_globalPresetApplyingPolicy"), "missing snapshot cannot import the preceding selection");
                Equal(null, Get(MainInstance, "_globalPresetReceipt"), "missing snapshot cannot fabricate applied authority");
                Equal(transfers, ResourceTransfers, "loading a snapshot-free entry never writes native accounting");
                continue;
            }
            Check(issue != null && issue.Contains(invalidKind == "version" ? "version/schema"
                : invalidKind == "entry" ? "stage entry" : "save context"), "actual checkpoint reload reports precise unsafe provenance/" + invalidKind);
            CallMain("RunCustomPlanAutomation", plan);
            Equal(searches, PresetSearches, "invalid/legacy durable policy cannot start a replacement search/" + invalidKind);
            Equal(transfers, ResourceTransfers, "invalid/legacy durable policy grants no native accounting authority/" + invalidKind);
        }
    }

    private static void CheckCompiledZeroReferenceScoring()
    {
        Equal(0d, Planner("Score", new double?[] { 0, 0 }, new[] { .5, .5 }, new[] { 0d, 0 }, true),
            "compiled all-zero/completed output has no fabricated utility");
        Check(Math.Abs(Convert.ToDouble(Planner("Score", new double?[] { 1e90, 221 }, new[] { 1d / 1001, 1000d / 1001 }, new[] { 0d, 221 }, true)) - 1) < 1e-12,
            "compiled late productive candidate cannot amplify a zero reference");
        Equal(null, Planner("Score", new double?[] { null }, new[] { 1d }, new[] { 0d }, true),
            "compiled unavailable positively weighted metric remains unscorable");
        Equal(null, Planner("Score", new double?[] { 1 }, new[] { 1d }, new[] { 0d }, false),
            "compiled locked/infeasible policy cannot gain reference utility");
        Equal(2d, Planner("Score", new double?[] { 4 }, new[] { 1d }, new[] { 2d }, true),
            "compiled positive reference retains original unsaturated ratio");
    }

    private static void CheckVersionedEquipmentSelection(bool autoUpdate)
    {
        var plan = SetupCommitBudget(); CallMain("RunCustomPlanAutomation", plan);
        Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), "equipment descriptor fixture acquires real equipment");
        var receipt = Get(MainInstance, "_globalPresetReceipt"); var policy = Get(receipt, "Policy");
        Set(Get(policy, "Preset"), "AutoUpdate", autoUpdate); Set(Get(policy, "ExecutionPreset"), "AutoUpdate", autoUpdate);
        var searches = PresetSearches; var transfers = ResourceTransfers; var swaps = Get(typeof(Program), "Swaps");
        var planning = Get(MainType, "_presetPlanningCaptures");
        var scheduler = Invoke(Get(MainInstance, "_protectedScheduler"), "ExportState").ToString();
        var history = Get(MainInstance, "_loadoutPresetSnapshot").ToString();
        var selection = CallMain("ExportGlobalPresetEquipmentSelection", policy);
        var serialized = selection.ToString(); var parsed = JsonParse(serialized);
        Equal(1, Property(Json(parsed, "version"), "AsInt"), "equipment identity has a separate exact schema version");
        Equal(4, Property(parsed, "Count"), "descriptor excludes receipts, live references, allocations and scheduler state");
        var acquisition = (string)Get(policy, "Acquisition"); var slots = (int[])Get(policy, "Slots");
        var instances = (IDictionary)CallMain("LoadoutPresetInstances");
        var resolved = (Array)CallMain("ResolveGlobalPresetEquipmentSelection", parsed, acquisition, slots, instances);
        var expected = (Array)Get(policy, "Items");
        for (var i = 0; i < slots.Length; i++)
            Check(ReferenceEquals(expected.GetValue(i), resolved.GetValue(i)), "round-trip resolves only the exact approved material/" + slots[i]);
        var selectedIndex = Enumerable.Range(0, slots.Length).First(i => expected.GetValue(i) != null);
        var selected = expected.GetValue(selectedIndex);
        var source = instances.Keys.Cast<int>().Single(k => ReferenceEquals(instances[k], selected));
        var hint = (int)Property(Json(Json(Json(parsed, "equipment"), selectedIndex), "sourceSlotHint"), "AsInt");
        var spare = Enumerable.Range(0, 1024).First(k => !instances.Contains(k) && k != hint);
        // Use a detached location map: these checks never move native equipment.
        var moved = (IDictionary)Activator.CreateInstance(instances.GetType());
        foreach (DictionaryEntry row in instances) moved.Add(row.Key, row.Value);
        moved.Remove(source); moved.Add(spare, selected);
        resolved = (Array)CallMain("ResolveGlobalPresetEquipmentSelection", parsed, acquisition, slots, moved);
        Check(ReferenceEquals(selected, resolved.GetValue(selectedIndex)), "a unique exact-material instance may move; old location is only a hint");
        moved.Remove(spare);
        Throws(() => CallMain("ResolveGlobalPresetEquipmentSelection", parsed, acquisition, slots, moved), "missing or ambiguous", "missing selected instance never substitutes same-ID gear");
        var changed = Invoke(selected, "MemberwiseClone"); Set(changed, "curAttack", (float)Get(changed, "curAttack") + 1f);
        moved[spare] = changed;
        Throws(() => CallMain("ResolveGlobalPresetEquipmentSelection", parsed, acquisition, slots, moved), "missing or ambiguous", "unexplained material growth cannot identify an acquired instance");
        moved[spare] = selected; moved[hint] = Invoke(selected, "MemberwiseClone");
        Throws(() => CallMain("ResolveGlobalPresetEquipmentSelection", parsed, acquisition, slots, moved), "missing or ambiguous", "old location cannot break equal-material ambiguity");
        moved[hint] = selected;
        Throws(() => CallMain("ResolveGlobalPresetEquipmentSelection", parsed, acquisition, slots, moved), "missing or ambiguous", "aliased native locations are not an unambiguous instance");
        Throws(() => CallMain("ResolveGlobalPresetEquipmentSelection", parsed, Guid.NewGuid().ToString("N"), slots, instances), "incompatible", "descriptor cannot cross acquisition identity");
        Throws(() => CallMain("ResolveGlobalPresetEquipmentSelection", parsed, acquisition, slots.Reverse().ToArray(), instances), "row is invalid", "descriptor cannot silently remap managed target slots");
        foreach (var version in new[] { "0", "1.5", "2", "\"1\"" })
        {
            var invalid = JsonParse(serialized); SetJson(invalid, "version", ParseFixtureScalar(version));
            Equal(version, Json(invalid, "version").ToString(), "version rejection exercises the intended typed value/" + version);
            Throws(() => CallMain("ResolveGlobalPresetEquipmentSelection", invalid, acquisition, slots, instances), "incompatible", "unknown or coerced descriptor version rejected/" + version);
        }
        var invalidSchema = JsonParse(serialized); SetJson(invalidSchema, "materialSchema", ParseFixtureScalar("\"equipment-primitives-v2\""));
        Throws(() => CallMain("ResolveGlobalPresetEquipmentSelection", invalidSchema, acquisition, slots, instances), "incompatible", "unknown material schema is not silently migrated");
        var invalidId = JsonParse(serialized); SetJson(Json(Json(invalidId, "equipment"), selectedIndex), "id", ParseFixtureScalar("1.5"));
        Throws(() => CallMain("ResolveGlobalPresetEquipmentSelection", invalidId, acquisition, slots, instances), "row is invalid", "fractional item identity is never truncated");
        var reused = JsonParse(serialized); var secondIndex = (selectedIndex + 1) % slots.Length;
        var reusedRow = Json(Json(reused, "equipment"), secondIndex); var selectedRow = Json(Json(parsed, "equipment"), selectedIndex);
        foreach (var key in new[] { "id", "material", "sourceSlotHint" })
        {
            SetJson(reusedRow, key, ParseFixtureScalar(Json(selectedRow, key).ToString()));
            Equal(Json(selectedRow, key).ToString(), Json(reusedRow, key).ToString(), "reused-instance fixture preserves typed identity/" + key);
        }
        Throws(() => CallMain("ResolveGlobalPresetEquipmentSelection", reused, acquisition, slots, instances), "missing or ambiguous", "one uniquely matched instance cannot satisfy two selected slots");
        var empty = JsonParse(serialized); var emptyRow = Json(Json(empty, "equipment"), selectedIndex);
        SetJson(emptyRow, "id", ParseFixtureScalar("0")); SetJson(emptyRow, "material", ParseFixtureScalar("\"null\""));
        resolved = (Array)CallMain("ResolveGlobalPresetEquipmentSelection", empty, acquisition, slots, instances);
        Equal(null, resolved.GetValue(selectedIndex), "controlled empty remains empty even when native target slot is occupied");
        SetJson(emptyRow, "material", ParseFixtureScalar("\"foreign\""));
        Throws(() => CallMain("ResolveGlobalPresetEquipmentSelection", empty, acquisition, slots, instances), "unexpected material", "controlled empty cannot carry foreign material");
        var attack = Get(selected, "curAttack");
        try
        {
            Set(selected, "curAttack", (float)attack + 1f);
            Equal(serialized, CallMain("ExportGlobalPresetEquipmentSelection", policy).ToString(), "export uses approved acquired material, not an unexplained native observation");
        }
        finally { Set(selected, "curAttack", attack); }
        Equal(serialized, CallMain("ExportGlobalPresetEquipmentSelection", policy).ToString(), "identity checks leave approved selection immutable");
        Equal(searches, PresetSearches, "descriptor resolution performs no replacement search in either AutoUpdate mode");
        Equal(planning, Get(MainType, "_presetPlanningCaptures"), "descriptor resolution performs no planning capture");
        Equal(transfers, ResourceTransfers, "descriptor resolution grants no resource authority");
        Equal(swaps, Get(typeof(Program), "Swaps"), "descriptor resolution never equips its matches");
        Equal(scheduler, Invoke(Get(MainInstance, "_protectedScheduler"), "ExportState").ToString(), "identity resolution cannot recreate stage budgets, AT gates or W history");
        Equal(history, Get(MainInstance, "_loadoutPresetSnapshot").ToString(), "descriptor resolution does not import or rewrite applied history");
        Check(ReferenceEquals(receipt, Get(MainInstance, "_globalPresetReceipt")), "identity resolution creates no replacement receipt");
    }

    private static void CheckEquippedOnlyAllocationObservation(bool autoUpdate)
    {
        // ITOPOD is unlocked, but this acquired setup has no farming objective.
        // This catches captures that build forecasts just because the route exists.
        var plan = SetupCommitBudget(itopod: true);
        CallMain("RunCustomPlanAutomation", plan);
        Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), "cheap-observation fixture acquires real funding");
        var receipt = Get(MainInstance, "_globalPresetReceipt"); var policy = Get(receipt, "Policy");
        var preset = Get(policy, "ExecutionPreset"); var stage = Get(policy, "Stage");
        // AutoUpdate is not part of acquired definition identity. Neither setting
        // authorizes replacement selection on an ordinary allocation refresh.
        Set(Get(policy, "Preset"), "AutoUpdate", autoUpdate); Set(preset, "AutoUpdate", autoUpdate);
        var full = CallMain("CaptureLoadoutPresetDefinition", preset, null, stage);
        var fullSnapshot = Get(full, "Snapshot");
        Check(((double[])Get(fullSnapshot, "PpPerKill")).Length > 0, "control broad capture really builds unlocked ITOPOD rows");
        var searches = PresetSearches; var planning = Get(MainType, "_presetPlanningCaptures");
        var acquiredZone = Get(Get(policy, "Candidate"), "Zone"); var acquiredFloor = Get(Get(policy, "Candidate"), "Floor");
        var observation = CallMain("CaptureLoadoutPresetExecution", preset, null, stage);
        var snapshot = Get(observation, "Snapshot");
        foreach (var field in new[] { "Energy", "Magic", "ReservedEnergy", "EnergyPower", "MagicPower", "EnergyCap", "MagicCap" })
            Equal(Get(fullSnapshot, field), Get(snapshot, field), "cheap observation retains current native " + field);
        Equal(null, Get(observation, "Material"), "execution observation does not fingerprint inventory/forecast material");
        Equal(0, ((Array)Get(snapshot, "Zones")).Length, "execution observation builds no zone alternatives");
        Equal(null, Get(snapshot, "PpPerKill"), "execution observation builds no ITOPOD PP rows");
        Equal(null, Get(snapshot, "ItopodHp"), "execution observation builds no ITOPOD combat rows");
        var slots = (int[])Get(snapshot, "Slots");
        var items = ((IEnumerable)Get(snapshot, "Items")).Cast<object>().ToArray();
        Check(items.All(x => slots.Contains((int)Get(x, "SourceSlot"))), "only equipped instances are converted, never inventory alternatives");
        Equal(planning, Get(MainType, "_presetPlanningCaptures"), "cheap observation never enters broad planning capture");
        var owner = (string)Get(receipt, "Owner");
        var ngu = ((IList)Get(Get(Character, "NGU"), "skills"))[0]; var amount = (long)Get(ngu, "energy");
        Check(amount > 1, "cheap-observation fixture has real NGU funding to repair");
        Set(ngu, "energy", amount - 1); Set(Character, "idleEnergy", (long)Get(Character, "idleEnergy") + 1);
        var observations = (long)Get(MainType, "_presetExecutionObservations");
        Equal(true, CallMain("RefreshGlobalPresetAllocations", owner), "equipped-only observation repairs actual native funding");
        Equal(observations + 2, Get(MainType, "_presetExecutionObservations"), "changed allocation observes before and after support application");
        Equal(amount, Get(ngu, "energy"), "cheap refresh restores the exact acquired desired amount");
        Equal(acquiredZone, Get(Get(receipt, "Execution"), "Zone"), "resource observation preserves incidental acquired route identity");
        Equal(acquiredFloor, Get(Get(receipt, "Execution"), "Floor"), "resource observation preserves incidental acquired floor identity");
        CallMain("VerifyLoadoutPresetAllocations", Get(receipt, "Execution"));
        var transfers = ResourceTransfers;
        Equal(true, CallMain("RefreshGlobalPresetAllocations", owner), "unchanged cheap observation verifies successfully");
        Equal(transfers, ResourceTransfers, "unchanged desired map performs no native transfer");
        Equal(planning, Get(MainType, "_presetPlanningCaptures"), "changed and steady-state refresh perform zero broad captures");
        Equal(searches, PresetSearches, "changed and steady-state refresh perform zero replacement searches");
        Check(ReferenceEquals(policy, Get(receipt, "Policy")), "cheap refresh preserves acquired policy identity");
    }

    private static void CheckAcquiredRouteObservation(bool autoUpdate, bool itopod, bool originalMixed, bool rotationFixture = false)
    {
        var plan = SetupCommitBudget(itopod: true); var stage = ((Array)Get(plan, "Stages")).GetValue(0);
        var library = Get(Get(MainType, "CustomPlanStore"), "<PresetLibrary>k__BackingField");
        var root = Invoke(library, "ReadExternalLibrary"); var id = (string)Get(stage, "PresetId");
        var row = ((IEnumerable)GetChildren(Json(root, "Presets"))).Cast<object>().Single(x => Property(Json(x, "Id"), "Value").Equals(id));
        // The mandatory-AT specialist has zero Energy NGU and regular-zone output.
        // Retain isolated route coverage as well as the original authorized scoring regression.
        SetJson(row, "Weights", JsonParse(originalMixed ? "[1,1,0,1000,1,1,0,0,0,1,1,0,0,0]"
            : itopod ? "[0,1,0,1000,1,0,0,0,0,1,1,0]" : "[1,1,0,1,1,1,0,0,0,1,1,0]"));
        // Mixed boost/zone weights with a nonnegative Zone pin regular farming.
        // ITOPOD must remain an unrestricted optimizer choice, not a forced candidate.
        var zoneJson = itopod ? "-1" : "0";
        SetJson(row, "Zone", ParseFixtureScalar(zoneJson));
        if (rotationFixture)
        {
            SetJson(row, "BeardPriority", JsonParse("[1,3,5]")); SetJson(row, "BeardRotation", JsonParse("[6,0,4]"));
            SetJson(row, "DiggerWeights", JsonParse("[1,0,0,0,0,0,0,0,0,0,0,0]"));
            Set(((IList)Get(Get(Character, "diggers"), "diggers"))[0], "maxLevel", 4L);
        }
        Equal(zoneJson, Json(JsonParse(row.ToString()), "Zone").ToString(), "farming fixture serializes the intended numeric Zone");
        Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
        var adventure = Get(Character, "adventure"); var controller = Get(Character, "adventureController");
        Set(adventure, "autoattacking", true);
        var player = Child(controller, "playerController"); var ai = Child(controller, "enemyAI"); Set(ai, "player", player);
        Child(controller, "log"); Child(controller, "itopodStartInput"); Child(controller, "itopodEndInput");
        var selector = Child(controller, "zoneSelector"); Set(selector, "character", Character); Set(selector, "ac", controller);
        // SetupCommitBudget funds resource work, but does not initialize the native
        // recycling/boost getters required by the newly weighted farming objectives.
        // Match SetupAdventureDynamic's native inputs; do not stub capture or availability.
        Set(Child(Character, "allItemList"), "character", Character); Child(Character, "purchases");
        Sequence(Get(Get(Character, "inventory"), "itemList"), "itemMaxxed", 1000, false);
        Sequence(Get(adventure, "itopod"), "perkLevel", 300, 0L);
        Set(Get(Character, "beastQuestPerkController"), "character", Character);
        Sequence(Child(Character, "beastQuest"), "quirkLevel", 300, 0L);
        foreach (var perks in new[] { Get(controller, "itopod"), Get(Character, "beastQuestPerkController") })
        {
            Sequence(perks, "effectPerLevel", 300, 0f);
            ListField(perks, perks == Get(controller, "itopod") ? "perkDifficultyReq" : "quirkDifficultyReq", 300);
        }
        // Native IL: items 0..39; direct perks 12/33/107 plus perkEffect 229/230;
        // direct quirks 11/53 plus quirkEffect 72/73 (including difficulty rows).
        Check(((IList)Get(Get(Get(Character, "inventory"), "itemList"), "itemMaxxed")).Count > 39,
            "farming fixture covers native boost item bounds through 39");
        foreach (var isPerk in new[] { true, false })
        {
            var perks = isPerk ? Get(controller, "itopod") : Get(Character, "beastQuestPerkController");
            var levels = (IList)Get(isPerk ? Get(adventure, "itopod") : Get(Character, "beastQuest"), isPerk ? "perkLevel" : "quirkLevel");
            var lastIndex = isPerk ? 230 : 73; var label = isPerk ? "perk" : "quirk";
            Check(levels.Count > lastIndex, "farming fixture covers native " + label + " level bounds through " + lastIndex);
            Check(((IList)Get(perks, "effectPerLevel")).Count > lastIndex, "farming fixture covers native " + label + " effect bounds through " + lastIndex);
            Check(((IList)Get(perks, isPerk ? "perkDifficultyReq" : "quirkDifficultyReq")).Count > lastIndex,
                "farming fixture covers native " + label + " difficulty bounds through " + lastIndex);
            Check(levels.Cast<object>().All(x => Convert.ToInt64(x) == 0), "farming fixture retains zero " + label + " levels");
        }
        Check(Convert.ToDouble(Invoke(Character, "totalRecycleBonus")) >= 0, "farming observation provides native recycling inputs");
        Check(Convert.ToDouble(Invoke(Get(Character, "allItemList"), "boostBonus")) > 0, "farming observation provides native boost-value inputs");
        CallMain("RunCustomPlanAutomation", plan);
        var block = Get(MainInstance, "_presetExecutionBlock");
        Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), "farming observation fixture acquires and funds a real setup"
            + "; waitingReason=" + Get(MainInstance, "_globalPresetWaitingReason")
            + "; waitingEvent=" + Get(MainInstance, "_globalPresetWaitingEvent")
            + "; waitingState=" + Get(MainInstance, "_globalPresetWaitingState")
            + "; failure=" + Get(MainInstance, "_globalPresetFailure")
            + "; block=" + (block == null ? "none" : "event=" + Get(block, "Event") + ",domain=" + Get(block, "Domain")
                + ",disposition=" + Get(block, "Disposition") + ",reason=" + Get(block, "Reason"))
            + "; status=" + CallMain("GetCustomAutomationStatus")
            + "; runtime=" + CallMain("GetResourceCoordinationRuntime"));
        var receipt = Get(MainInstance, "_globalPresetReceipt"); var policy = Get(receipt, "Policy");
        var preset = Get(policy, "ExecutionPreset"); var acquired = Get(policy, "Candidate"); var owner = (string)Get(receipt, "Owner");
        // Verbose post-commit diagnostics are opt-in and never part of refresh accounting.
        if (Environment.GetEnvironmentVariable("NGU_ROUTE_DIAGNOSTICS") == "1")
            PrintAcquiredRouteDiagnostics(policy, itopod, autoUpdate);
        var acquiredPlan = Get(policy, "Plan");
        Equal(itopod ? 1000 : 0, Get(acquired, "Zone"), "fixture acquired the intended route kind"
            + "; score=" + RouteDiagnosticValue(Get(acquired, "Score"))
            + "; metrics=" + RouteDiagnosticValue(Get(acquired, "Metrics"))
            + "; normalization=" + RouteDiagnosticValue(Get(acquiredPlan, "Normalization"))
            + "; references=" + RouteDiagnosticValue(Get(acquiredPlan, "ReferenceKeys")));
        if (itopod && !originalMixed)
        {
            var weights = (double[])Get(preset, "Weights"); var references = (double?[])Get(acquiredPlan, "ReferenceProduction");
            Check(weights.Select((weight, i) => weight <= 0 || (references[i].HasValue && references[i].Value > 0
                && !double.IsInfinity(references[i].Value) && !double.IsNaN(references[i].Value))).All(x => x),
                "ITOPOD observation fixture excludes the unrelated zero-reference scoring corner");
        }
        if (originalMixed)
        {
            var references = (double?[])Get(acquiredPlan, "ReferenceProduction");
            var keys = (string[])Get(acquiredPlan, "ReferenceKeys");
            Check(references[0] > 0 && keys[0].StartsWith("reference-seed-"), "legal original-policy seed supplies positive Energy-NGU reference");
            Check(((IEnumerable)Get(acquiredPlan, "Specialists")).Cast<object>().Where(c => (bool)Get(c, "Feasible"))
                .All(c => ((double?[])Get(c, "Metrics"))[0] == 0), "mandatory AT specialists alone cannot supply Energy-NGU reference");
            Check(Convert.ToDouble(Get(acquired, "Score")) < 100, "original mixed score is not epsilon-amplified");
            Equal("[1,1,0,1000,1,1,0,0,0,1,1,0,0,0]", Json(row, "Weights").ToString(), "original saved importance settings are unchanged");
        }
        var frozenNorms = ((double[])Get(acquiredPlan, "Normalization")).ToArray();
        var frozenReferences = ((double?[])Get(acquiredPlan, "ReferenceProduction")).ToArray();
        var frozenKeys = ((string[])Get(acquiredPlan, "ReferenceKeys")).ToArray();
        var at = Get(Character, "advancedTraining"); var atEnergy = (long[])Get(at, "energy");
        Check(atEnergy.Take(2).All(x => x > 0), "both unfinished mandatory AT lanes remain funded");
        Equal(20L, ((long[])Get(at, "levelTarget"))[0], "mandatory toughness target remains owned");
        Equal(10L, ((long[])Get(at, "levelTarget"))[1], "mandatory power target remains owned");
        Equal(false, CallMain("HasCompletedProtectedStageWork", plan), "route acquisition does not bypass unfinished mandatory AT");
        Set(Get(policy, "Preset"), "AutoUpdate", autoUpdate); Set(preset, "AutoUpdate", autoUpdate);
        var full = CallMain("CaptureLoadoutPresetDefinition", preset, null, Get(policy, "Stage"));
        Set(full, "Horizon", Get(policy, "Horizon"));
        var control = CallMain("ReplayGlobalPreset", policy, Get(policy, "Preset"), full, Get(receipt, "Execution"), false, false, false, true);
        var planning = Get(MainType, "_presetPlanningCaptures"); var searches = PresetSearches;
        var tables = Get(MainType, "_presetRouteTableEnumerations"); var floors = Get(MainType, "_presetFloorForecastRows");
        var bounded = CallMain("CaptureLoadoutPresetRouteExecution", preset, null, Get(policy, "Stage"), acquired);
        Set(bounded, "Horizon", Get(policy, "Horizon"));
        var snapshot = Get(bounded, "Snapshot");
        var replay = CallMain("ReplayGlobalPreset", policy, Get(policy, "Preset"), bounded, Get(receipt, "Execution"), false, false, false, true);
        foreach (var field in new[] { "Feasible", "Score", "Zone", "Floor", "Hits", "Energy", "MagicNgu", "Blood", "GearPower", "GearToughness", "GearDrop" })
            Equal(Get(control, field), Get(replay, field), "bounded route replay matches broad economics/" + field);
        Equal(string.Join(",", (double?[])Get(control, "Metrics")), string.Join(",", (double?[])Get(replay, "Metrics")), "every farming/resource metric agrees with broad replay");
        Equal(null, Get(bounded, "Material"), "farming refresh does not fingerprint inventory");
        Equal(itopod ? 0 : 1, ((Array)Get(snapshot, "Zones")).Length, "only the acquired regular route is captured");
        if (itopod)
        {
            Equal(1, ((Array)Get(snapshot, "PpPerKill")).Length, "only the acquired floor PP row is observed");
            var floor = (int)Get(acquired, "Floor"); var fullSnapshot = Get(full, "Snapshot");
            foreach (var field in new[] { "PpPerKill", "ItopodHp", "ItopodDefense", "ItopodBoostPerKill" })
                Equal(((double[])Get(fullSnapshot, field))[floor], ((double[])Get(snapshot, field))[0], "single-floor observation retains native " + field);
        }
        else Equal(null, Get(snapshot, "PpPerKill"), "regular route never captures unlocked floor rows");
        var slots = (int[])Get(snapshot, "Slots");
        Check(((IEnumerable)Get(snapshot, "Items")).Cast<object>().All(x => slots.Contains((int)Get(x, "SourceSlot"))), "farming refresh converts no inventory alternatives");
        var ngu = ((IList)Get(Get(Character, "NGU"), "skills"))[0];
        var amount = itopod ? atEnergy[0] : (long)Get(ngu, "energy");
        Check(amount > 1, "farming setup has actual funding to repair");
        if (itopod) atEnergy[0] = amount - 1;
        else Set(ngu, "energy", amount - 1);
        Set(Character, "idleEnergy", (long)Get(Character, "idleEnergy") + 1);
        Equal(true, CallMain("RefreshGlobalPresetAllocations", owner), "bounded farming refresh repairs native funding");
        Equal(amount, itopod ? (object)((long[])Get(at, "energy"))[0] : Get(ngu, "energy"), "farming refresh restores exact native Energy");
        CallMain("VerifyLoadoutPresetAllocations", Get(receipt, "Execution"));
        Check(((long[])Get(at, "energy")).SequenceEqual((long[])Get(Get(Get(receipt, "Execution"), "Activities"), "AT")),
            "repaired native AT accounting equals committed execution");
        Equal(10000L, (long)Get(Character, "idleEnergy") + ((long[])Get(at, "energy")).Sum()
            + ((IList)Get(Get(Character, "NGU"), "skills")).Cast<object>().Sum(x => (long)Get(x, "energy")),
            "route refresh conserves the actual Energy pool");
        var transfers = ResourceTransfers;
        Equal(true, CallMain("RefreshGlobalPresetAllocations", owner), "unchanged farming refresh verifies");
        Equal(transfers, ResourceTransfers, "unchanged farming refresh performs no transfers");
        Check(frozenNorms.SequenceEqual((double[])Get(acquiredPlan, "Normalization"))
            && frozenReferences.SequenceEqual((double?[])Get(acquiredPlan, "ReferenceProduction"))
            && frozenKeys.SequenceEqual((string[])Get(acquiredPlan, "ReferenceKeys")), "Apply/replay/refresh retain acquired scoring references");
        var arbitraryFilters = Get(Character, "arbitrary"); var itemList = Get(Get(Character, "inventory"), "itemList");
        var filterEnabled = Get(arbitraryFilters, "lootFilter"); var filtered = Get(itemList, "itemFiltered");
        Set(arbitraryFilters, "lootFilter", true); Set(itemList, "itemFiltered", null);
        try
        {
            var failedEconomics = CallMain("CaptureLoadoutPresetRouteExecution", preset, null, Get(policy, "Stage"), acquired);
            Equal(false, ((bool[])Get(Get(failedEconomics, "Snapshot"), "Available"))[4], "failed live boost economics cannot be replaced by cached eligibility");
            Equal(false, CallMain("RefreshGlobalPresetAllocations", owner), "unavailable boost economics rejects weighted farming before funding");
            Equal(transfers, ResourceTransfers, "failed boost observation has no native mutation");
        }
        finally { Set(arbitraryFilters, "lootFilter", filterEnabled); Set(itemList, "itemFiltered", filtered); }
        if (itopod)
        {
            var enemies = (IList)Get(controller, "itopodEnemyList"); var hp = Get(enemies[0], "maxHP");
            var fullSnapshot = Get(full, "Snapshot"); var capturedHp = Get(fullSnapshot, "ItopodHp"); var capturedDefense = Get(fullSnapshot, "ItopodDefense");
            Set(enemies[0], "maxHP", 0f); Set(fullSnapshot, "ItopodHp", null); Set(fullSnapshot, "ItopodDefense", null);
            try
            {
                var fallback = CallMain("CaptureLoadoutPresetRouteExecution", preset, null, Get(policy, "Stage"), acquired);
                Set(fallback, "Horizon", Get(policy, "Horizon"));
                Equal(null, Get(Get(fallback, "Snapshot"), "ItopodHp"), "unavailable enemy stats do not invent multihit rows");
                var fallbackControl = CallMain("ReplayGlobalPreset", policy, Get(policy, "Preset"), full, Get(receipt, "Execution"), false, false, false, true);
                var fallbackReplay = CallMain("ReplayGlobalPreset", policy, Get(policy, "Preset"), fallback, Get(receipt, "Execution"), false, false, false, true);
                foreach (var field in new[] { "Feasible", "Score", "Zone", "Floor", "Hits" })
                    Equal(Get(fallbackControl, field), Get(fallbackReplay, field), "bounded missing-enemy fallback preserves broad one-hit constraint/" + field);
            }
            finally { Set(enemies[0], "maxHP", hp); Set(fullSnapshot, "ItopodHp", capturedHp); Set(fullSnapshot, "ItopodDefense", capturedDefense); }
            var maximum = (int)Get(adventure, "highestItopodLevel");
            Set(adventure, "highestItopodLevel", (int)Get(acquired, "Floor"));
            Equal(false, CallMain("RefreshGlobalPresetAllocations", owner), "lost repeatable floor waits rather than replacing route");
            Equal(transfers, ResourceTransfers, "unavailable floor fails before native funding changes");
            Set(adventure, "highestItopodLevel", maximum);
            var arbitrary = Get(Character, "arbitrary"); Set(arbitrary, "boughtLazyITOPOD", true); Set(arbitrary, "lazyITOPODOn", true);
            // Make the native shifter optimum differ without changing the acquired decision.
            Set(adventure, "highestItopodLevel", 100); Set(adventure, "attack", 1e20f);
            Equal(false, CallMain("RefreshGlobalPresetAllocations", owner), "changed native lazy floor rejects acquired fixed-floor replay");
            Equal(true, Get(arbitrary, "lazyITOPODOn"), "rejection preserves native lazy preference");
        }
        else
        {
            var helper = Plugin.GetType("NGUInjector.Managers.ZoneStatHelper", true);
            var recommendations = (IDictionary)(Get(helper, "UserOverrides") ?? Get(helper, "Defaults")); var recommendation = recommendations[0];
            recommendations.Remove(0);
            try
            {
                Equal(false, CallMain("RefreshGlobalPresetAllocations", owner), "missing acquired recommendation never substitutes an unlocked alternative");
                Equal(transfers, ResourceTransfers, "missing acquired route fails before resource mutation");
            }
            finally { recommendations[0] = recommendation; }
            Set(adventure, "beastModeOn", true);
            Equal(false, CallMain("RefreshGlobalPresetAllocations", owner), "unavailable regular route waits without replacement");
        }
        Equal(transfers, ResourceTransfers, "unavailable or changed route performs no funding mutation");
        Equal(planning, Get(MainType, "_presetPlanningCaptures"), "all farming refreshes perform zero broad captures");
        Equal(tables, Get(MainType, "_presetRouteTableEnumerations"), "all farming refreshes enumerate zero route tables");
        Equal(floors, Get(MainType, "_presetFloorForecastRows"), "all farming refreshes build zero floor forecasts");
        Equal(searches, PresetSearches, "both AutoUpdate modes perform zero replacement search");
        Check(ReferenceEquals(policy, Get(receipt, "Policy")), "route loss does not replace acquired policy identity");
    }

    private static string RouteDiagnosticValue(object value)
    {
        if (value == null) return "null";
        var sequence = value as IEnumerable;
        if (sequence != null && !(value is string))
        {
            var rows = sequence.Cast<object>().ToArray();
            return "count=" + rows.Length + "[" + string.Join(",", rows.Take(64).Select(RouteDiagnosticValue))
                + (rows.Length > 64 ? ",..." : "") + "]";
        }
        return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void PrintRouteDiagnosticFields(string label, object source, params string[] fields)
    {
        Console.WriteLine("ROUTE-DIAG " + label + " " + (source == null ? "null" : string.Join("; ",
            fields.Select(field => field + "=" + RouteDiagnosticValue(Get(source, field))))));
    }

    private static void PrintAcquiredRouteDiagnostics(object policy, bool itopod, bool autoUpdate)
    {
        Console.WriteLine("ROUTE-DIAG BEGIN ITOPOD=" + itopod + " AutoUpdate=" + autoUpdate);
        // Diagnostics must not hide the original route failure if a native getter fails.
        try
        {
            var preset = Get(policy, "ExecutionPreset"); var acquiredPlan = Get(policy, "Plan");
            Console.WriteLine("ROUTE-DIAG metric order=energyNgu,magicNgu,blood,pp,boosts,zone,timeMachine,augments,wandoos,advancedTraining,adventurePower,adventureToughness,energyCap,magicCap");
            PrintRouteDiagnosticFields("ExecutionPreset", preset, "Zone", "Weights", "SupportMode");
            PrintRouteDiagnosticFields("acquired Plan", acquiredPlan, "Normalization", "ReferenceKeys", "ReferenceProduction", "Evaluated", "Budget");
            var weights = (double[])Get(preset, "Weights"); var weightTotal = weights.Sum();
            var normalization = (double[])Get(acquiredPlan, "Normalization");
            Action<string, object> candidate = (label, c) => {
                PrintRouteDiagnosticFields(label, c, "Key", "Label", "Feasible", "Score", "Zone", "Floor", "Hits",
                    "Metrics", "RawMetrics", "GearPower", "GearToughness", "GearDrop", "GrossGold", "Warnings");
                if (c == null) return;
                var metrics = (double?[])Get(c, "Metrics");
                Console.WriteLine("ROUTE-DIAG " + label + " normalized weighted contributions=" + RouteDiagnosticValue(
                    metrics.Select((metric, i) => metric.HasValue && i < weights.Length
                        ? (double?)(Convert.ToDouble(Planner("ScoringRatio", metric.Value, normalization[i])) * weights[i] / weightTotal) : null).ToArray()));
                var support = Get(c, "Support");
                PrintRouteDiagnosticFields(label + " support", support, "Budget", "Drain", "Remaining", "Beards", "Notes");
                if (support != null)
                    foreach (var digger in ((IEnumerable)Get(support, "Diggers")).Cast<object>().Take(16))
                        PrintRouteDiagnosticFields(label + " digger", digger, "Id", "Level", "Maximum", "BaseDrain", "Growth");
            };
            candidate("acquired Candidate", Get(policy, "Candidate"));
            foreach (var group in new[] { "Candidates", "Specialists" })
                foreach (var alternative in ((IEnumerable)Get(acquiredPlan, group)).Cast<object>().Take(16))
                    candidate("acquired Plan." + group, alternative);

            var capture = CallMain("CaptureLoadoutPresetDefinition", preset, null, Get(policy, "Stage"));
            var snapshot = Get(capture, "Snapshot");
            PrintRouteDiagnosticFields("post-commit control snapshot", snapshot, "Available", "Reasons", "Power", "Toughness",
                "BasePower", "BaseToughness", "IdleModifier", "Respawn", "AttackSeconds", "BeastMode", "LazyItopodPurchased", "LazyItopodEnabled",
                "FarmingEconomicsCaptured", "PpPerKill", "ItopodBoostPerKill", "ItopodHp", "ItopodDefense",
                "BeardsAvailable", "DiggersAvailable", "BeardSlots", "DiggerSlots", "GrossGold", "DiggerEffective");
            PrintRouteDiagnosticFields("control capture", capture, "Warnings");
            foreach (var zone in ((IEnumerable)Get(snapshot, "Zones")).Cast<object>().Take(8))
                PrintRouteDiagnosticFields("regular alternative", zone, "Id", "IdlePower", "IdleToughness", "OneHitPower",
                    "NormalRatio", "BoostValues", "Chances", "Caps");
            PrintRouteDiagnosticFields("native adventure", Get(Character, "adventure"), "highestItopodLevel", "attack", "defense");
            var perks = Get(Get(Character, "adventureController"), "itopod");
            // Floor 1 is legal in this fixture (highest reached is 12); preserve native long argument.
            Console.WriteLine("ROUTE-DIAG native progressGained(1)=" + RouteDiagnosticValue(Invoke(perks, "progressGained", 1L)));
            // Locate the untouched getter on its native owner, not a substituted PP constant.
            var foundTotal = false;
            foreach (var nativeOwner in new[] { perks, Character })
            {
                var total = nativeOwner.GetType().GetMethods(All).FirstOrDefault(m => m.Name == "totalPPBonus" && m.GetParameters().Length == 0);
                if (total != null)
                {
                    foundTotal = true;
                    Console.WriteLine("ROUTE-DIAG native " + total.DeclaringType.FullName + ".totalPPBonus="
                        + RouteDiagnosticValue(total.Invoke(nativeOwner, null)));
                }
            }
            if (!foundTotal) Console.WriteLine("ROUTE-DIAG native totalPPBonus getter not found on perk controller/Character; request native owner IL");
        }
        catch (Exception error) { Console.WriteLine("ROUTE-DIAG diagnostic error (route assertion retained): " + error); }
        Console.WriteLine("ROUTE-DIAG END");
    }

    private static void CheckNativeTargetReturnAuthority(string revoked)
    {
        var plan = SetupResourceCoordination(true, false, "none", true);
        var at = Get(Character, "advancedTraining"); var amounts = (long[])Get(at, "energy"); var targets = (long[])Get(at, "levelTarget");
        var first = amounts[0]; var second = amounts[1]; var idle = Get(Character, "idleEnergy"); var writes = ResourceTransfers;
        Check(first > 0 && second > 0, "return authority fixture has two genuinely funded owned AT bars");
        object claim = null;
        if (revoked == "energy") Set(Get(MainType, "Settings"), "_manageEnergy", false);
        else if (revoked == "drag") Set(Controller, "midDrag", true);
        else if (revoked == "coordinator") Set(MainType, "reference", System.Runtime.Serialization.FormatterServices.GetUninitializedObject(MainType));
        else
        {
            claim = CallMain("BeginExecutionClaim", "foreign-target-return", revoked, Domain(revoked == "loadout" ? "Gear" : "Support"), (Func<bool>)(() => true));
        }
        EndFixtureNativeTargets();
        var allowed = revoked == "loadout" || revoked == "digger";
        Equal(writes + (allowed ? 2 : 0), ResourceTransfers, revoked + " uses current native lane permission, not feature-lock authority");
        Equal(allowed ? 0L : first, amounts[0], revoked + " independently guards the first native allocation");
        Equal(allowed ? 0L : second, amounts[1], revoked + " independently guards the companion native allocation");
        Equal((long)idle + (allowed ? first + second : 0), Get(Character, "idleEnergy"), revoked + " credits only actually authorized returns");
        Equal(revoked == "coordinator" ? 20L : -1L, targets[0], "only current still-owned menu authority can retire the first borrowed target");
        Equal(revoked == "coordinator" ? 10L : -1L, targets[1], "only current still-owned menu authority can retire the companion borrowed target");
        Equal(null, Get(MainInstance, "_globalPresetNativeTargets"), "local lease retirement does not create an endless cleanup obligation");
        Set(MainType, "reference", MainInstance); Set(Get(MainType, "Settings"), "_manageEnergy", true); Set(Controller, "midDrag", false);
        if (claim != null) Equal(true, CallMain("EndExecutionClaim", claim, false), "unrelated setup claim retires without reconciliation");
        if (revoked != "coordinator")
        {
            CallMain("InvalidateCustomEquipmentApplication");
            CallMain("RunCustomPlanAutomation", plan);
            Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), "restored permission acquires current setup without a permanent failure latch");
            var before = (long)Get(Character, "idleEnergy"); var returned = amounts[0] + amounts[1];
            EndFixtureNativeTargets();
            Equal(0L, amounts[0] + amounts[1], "ordinary authorized cleanup still returns both owned bars");
            Equal(before + returned, Get(Character, "idleEnergy"), "ordinary authorized cleanup conserves the exact resource return");
        }
    }

    private static void EndFixtureNativeTargets()
    {
        var settings = Get(MainType, "Settings"); var enabled = Get(settings, "_useCustomRebirthPlan");
        Set(settings, "_useCustomRebirthPlan", false);
        try
        {
            CallMain("ReleaseGlobalPresetNativeTargets");
            CallMain("ServiceExecutionNativeSettings");
        }
        finally { if (ReferenceEquals(settings, Get(MainType, "Settings"))) Set(settings, "_useCustomRebirthPlan", enabled); }
    }

    private static void CheckNativeTargetReturnSaveReplacement()
    {
        SetupResourceCoordination(true, false, "none", true);
        var oldCoordinator = MainInstance; var oldCharacter = Character;
        var oldAt = Get(oldCharacter, "advancedTraining"); var oldEnergy = (long[])Get(oldAt, "energy"); var oldTargets = (long[])Get(oldAt, "levelTarget");
        var first = oldEnergy[0]; var second = oldEnergy[1]; var fired = false;
        AtRefreshObserver = () => {
            AtRefreshObserver = null; fired = true;
            Invoke(typeof(Program), "Setup");
            Character = Get(typeof(Program), "Character"); Controller = Get(typeof(Program), "Controller");
            // Keep the coordinator current to isolate save identity from coordinator identity.
            Set(MainType, "reference", oldCoordinator);
            var at = Get(Character, "advancedTraining"); var energy = (long[])Get(at, "energy"); var targets = (long[])Get(at, "levelTarget");
            energy[0] = first; energy[1] = second; targets[0] = 20; targets[1] = 10;
            Set(Character, "idleEnergy", 5000L);
        };
        FailAtTargetText = true;
        try { Throws(EndFixtureNativeTargets, "Native AT resource return failed", "save replacement makes the old return outcome unresolved"); }
        finally { FailAtTargetText = false; AtRefreshObserver = null; }
        Equal(true, fired, "save-replacement control actually executes between the two returns");
        var currentAt = Get(Character, "advancedTraining"); var currentEnergy = (long[])Get(currentAt, "energy"); var currentTargets = (long[])Get(currentAt, "levelTarget");
        Equal(first, currentEnergy[0], "old lease never recalls an equal first-bar amount in the replacement save");
        Equal(second, currentEnergy[1], "old lease never recalls the equal companion amount in the replacement save");
        Equal(5000L, Get(Character, "idleEnergy"), "replacement save receives no stale return credit");
        Equal("20,10", string.Join(",", currentTargets.Take(2)), "replacement save menu targets remain untouched");
        Equal(0L, oldEnergy[0], "the first authorized old-save return remains committed");
        Equal(second, oldEnergy[1], "epoch loss stops the companion old-save resource return");
        Equal("20,10", string.Join(",", oldTargets.Take(2)), "epoch loss also forbids later writes into retired menu arrays");
        Equal(null, Get(oldCoordinator, "_globalPresetNativeTargets"), "stale lease is retired locally without another native write");
    }

    private sealed class RotationDrawCounter : Random
    {
        internal int Draws;
        public override int Next(int maxValue) { Draws++; return 0; }
    }

    private static void CheckSharedSupportTransition()
    {
        // Acquire a real weighted regular route; no fabricated receipt or model actuator.
        CheckAcquiredRouteObservation(false, false, false, true);
        var receipt = Get(MainInstance, "_globalPresetReceipt"); var owner = (string)Get(receipt, "Owner");
        var adventure = Get(Character, "adventure"); var boss = Get(Character, "bossController");
        // The route-loss checks deliberately leave Beast Mode enabled. Restore this
        // fixture input before testing an otherwise eligible support transition.
        Set(adventure, "beastModeOn", false);
        var resumed = CallMain("RefreshGlobalPresetAllocations", owner);
        Equal(true, resumed, "route fixture clears its deliberate economics wait; runtime=" + CallMain("GetResourceCoordinationRuntime")
            + "; waiting=" + Get(MainInstance, "_globalPresetWaitingReason") + "; failure=" + Get(MainInstance, "_globalPresetFailure"));
        var plan = Get(MainInstance, "_protectedPlan"); var scheduler = Get(MainInstance, "_protectedScheduler");
        CallMain("CommitProtectedStageApplication", plan);
        Equal(true, Get(scheduler, "_armed"), "verified native route baseline earns stage credit before prerequisite loss");
        var entry = Property(scheduler, "Entry"); var enteredAt = Property(scheduler, "EnteredAt");
        var deadline = Property(scheduler, "ExpectedEndSeconds");
        Equal(false, CallMain("RefreshGlobalPresetAllocations", owner + ":stale"), "stale ingress cannot use the current receipt");
        Equal(true, Get(scheduler, "_armed"), "stale ingress does not suspend unrelated current stage credit");
        foreach (var domain in new[] { "Gear", "Support" })
        {
            var claim = CallMain("BeginExecutionClaim", "foreign-setup", domain, Domain(domain), (Func<bool>)(() => true));
            try
            {
                Equal(false, CallMain("RefreshGlobalPresetAllocations", owner), domain + " claim rejects unverified current refresh");
                Equal(false, Get(scheduler, "_armed"), domain + " claim suspends dependent stage credit");
            }
            finally { Equal(true, CallMain("EndExecutionClaim", claim, false), domain + " claim release preserves native state"); }
            Equal(true, CallMain("RefreshGlobalPresetAllocations", owner), domain + " released claim permits verification");
            CallMain("CommitProtectedStageApplication", plan);
            Equal(true, Get(scheduler, "_armed"), domain + " released claim rearms only after verification");
        }
        Set(Get(MainType, "Settings"), "_manageMagic", false);
        try
        {
            Equal(false, CallMain("RefreshGlobalPresetAllocations", owner), "lost Magic permission rejects current setup refresh");
            Equal(false, Get(scheduler, "_armed"), "lost lane permission suspends only dependent stage credit");
        }
        finally { Set(Get(MainType, "Settings"), "_manageMagic", true); }
        Equal(true, CallMain("RefreshGlobalPresetAllocations", owner), "restored permission permits current setup verification");
        CallMain("CommitProtectedStageApplication", plan);
        Equal(true, Get(scheduler, "_armed"), "restored permission rearms only after verification");
        var start = (DateTime)Get(receipt, "LastBeardRotation"); var due = start.AddSeconds(60);
        var support = CallMain("GlobalPresetSupportState"); var pods = PodRefreshCalls; var transfers = ResourceTransfers;
        var searches = PresetSearches; var planning = Get(MainType, "_presetPlanningCaptures");
        var random = new RotationDrawCounter(); Set(MainInstance, "_globalPresetBeardRandom", random);
        foreach (var flag in new[] { "isFighting", "nukeBoss" })
        {
            Set(boss, flag, true);
            CallMain("StopBossPush");
            if (flag == "isFighting") CallMain("RotateGlobalPresetBeards", due);
            else Equal(false, CallMain("RefreshGlobalPresetAllocations", owner), "allocation refresh also observes native nuke retirement");
            Equal(true, Get(boss, flag), "support refresh never cancels native " + flag);
            Equal(support, CallMain("GlobalPresetSupportState"), "native boss retains all support prerequisites/" + flag);
            Equal(0, Get(adventure, "zone"), "native boss wait leaves adventure alone/" + flag);
            Equal(start, Get(receipt, "LastBeardRotation"), "native boss wait consumes no cadence/" + flag);
            Equal(false, Get(Get(MainInstance, "_protectedScheduler"), "_armed"), "native boss wait admits no credit/" + flag);
            Equal(0, random.Draws, "unsafe due event is not admitted or randomly selected/" + flag);
            Equal(null, Get(Get(MainInstance, "_presetSupportAcquisition"), "Rotation"), "unsafe due event creates no accepted rotation/" + flag);
            Set(boss, flag, false);
            // Reverify through the real commit gate before testing the next loss.
            Equal(true, CallMain("RefreshGlobalPresetAllocations", owner), "safe unchanged route can verify after " + flag);
            CallMain("CommitProtectedStageApplication", plan);
            Equal(true, Get(scheduler, "_armed"), "verified prerequisite recovery rearms credit/" + flag);
        }
        Equal(pods, PodRefreshCalls, "boss-blocked rotation performs no model support writes");
        Equal(transfers, ResourceTransfers, "boss-blocked rotation performs no resource writes");
        // The due event above was deferred, not accepted. Admit it explicitly while
        // safe, then fail native route retirement after bounded selection/preflight.
        FailAdventureZoneEntry = true;
        try { CallMain("RotateGlobalPresetBeards", due); }
        finally { FailAdventureZoneEntry = false; }
        var pending = Get(Get(MainInstance, "_presetSupportAcquisition"), "Rotation");
        Check(pending != null, "safe admission retains the selected event after native retirement failure");
        Equal(due, Get(pending, "At"), "accepted event retains its original due time");
        var selected = Get(pending, "Execution");
        Equal(1, random.Draws, "safe admission selects exactly once before retirement failure");
        Equal(start, Get(receipt, "LastBeardRotation"), "failed accepted event does not consume cadence");
        Equal(false, Get(scheduler, "_armed"), "failed native retirement immediately suspends credit");
        Equal(0, Get(adventure, "zone"), "failed native retirement is not presumed safe");
        Equal(support, CallMain("GlobalPresetSupportState"), "failed retirement retains prerequisites");
        Equal(pods, PodRefreshCalls, "failed retirement reaches no support model writes");
        // Now lose authority on a genuinely accepted event. Neither ingress may
        // redraw, withdraw the route, or consume its acquired time/choice.
        Set(boss, "isFighting", true);
        try
        {
            CallMain("RotateGlobalPresetBeards", due.AddSeconds(60));
            Equal(false, CallMain("RefreshGlobalPresetAllocations", owner), "accepted rotation waits for native combat retirement");
            Check(ReferenceEquals(selected, Get(pending, "Execution")), "accepted choice survives lost native prerequisites");
            Equal(1, random.Draws, "accepted wait never redraws");
            Equal(0, Get(adventure, "zone"), "accepted boss wait never withdraws route prerequisites");
            Equal(support, CallMain("GlobalPresetSupportState"), "accepted boss wait preserves support");
            Equal(pods, PodRefreshCalls, "accepted boss wait performs no support writes");
            Equal(transfers, ResourceTransfers, "accepted boss wait performs no resource writes");
            Equal(false, Get(scheduler, "_armed"), "accepted boss wait earns no credit");
            Equal(true, Get(boss, "isFighting"), "accepted wait never cancels native combat");
        }
        finally { Set(boss, "isFighting", false); }
        var interruptedSupport = false;
        PodInterference = pod => {
            PodInterference = null; interruptedSupport = true;
            Equal(-1, Get(adventure, "zone"), "interrupted support begins only behind the safe route barrier");
            throw new InvalidOperationException("fixture interrupts native support after safe route retirement");
        };
        try { Equal(false, CallMain("RefreshGlobalPresetAllocations", owner), "partial support retains a safely retired route"); }
        finally { PodInterference = null; }
        Equal(true, interruptedSupport, "retired-route continuation really crosses a native support callback");
        Equal(-1, Get(adventure, "zone"), "partial support never reactivates the dependent route");
        Equal(true, Get(receipt, "SupportRouteRetired"), "receipt retains only observed owned route retirement");
        Equal(false, Get(scheduler, "_armed"), "retired-route continuation earns no credit");
        Equal(due, Get(pending, "At"), "partial support retains accepted event time");
        Equal(1, random.Draws, "partial support does not redraw accepted choice");
        var observedSafeSupport = false;
        PodInterference = pod => {
            observedSafeSupport = true;
            Equal(-1, Get(adventure, "zone"), "changed support applies only after observed safe retirement");
            Equal(false, Get(Get(MainInstance, "_protectedScheduler"), "_armed"), "support callbacks earn no premature credit");
        };
        try { Equal(true, CallMain("RefreshGlobalPresetAllocations", owner), "retirement recovery verifies selected support and funding"); }
        finally { PodInterference = null; }
        Equal(true, observedSafeSupport, "rotation actually reaches the native support boundary");
        Equal(0, Get(adventure, "zone"), "verified support and allocations reactivate the current route");
        Equal(due, Get(receipt, "LastBeardRotation"), "recovery commits original selected event time");
        Equal(1, random.Draws, "native barriers do not redraw acquired rotation");
        Equal(false, Get(scheduler, "_armed"), "refresh alone does not bypass the verified stage commit gate");
        CallMain("CommitProtectedStageApplication", plan);
        Equal(true, Get(scheduler, "_armed"), "only verified support/funding/route recovery rearms stage credit");
        Equal(entry, Property(scheduler, "Entry"), "native waits retain the original stage entry");
        Equal(enteredAt, Property(scheduler, "EnteredAt"), "native waits retain the original stage entry time");
        Equal(deadline, Property(scheduler, "ExpectedEndSeconds"), "native waits grant no replacement deadline");
        pods = PodRefreshCalls; transfers = ResourceTransfers; var routes = AdventureZoneEntries;
        Equal(true, CallMain("RefreshGlobalPresetAllocations", owner), "steady route support verifies");
        Equal(pods, PodRefreshCalls, "steady support has no duplicate native writes");
        Equal(transfers, ResourceTransfers, "steady resources have no duplicate model writes");
        Equal(routes, AdventureZoneEntries, "steady refresh does not reenter route");
        Equal(searches, PresetSearches, "rotation performs no replacement optimizer search");
        Equal(planning, Get(MainType, "_presetPlanningCaptures"), "rotation performs no full inventory planning capture");
    }

    private static void CheckNativeTargetReturnCallbackAuthority(string change)
    {
        SetupResourceCoordination(true, false, "none", true);
        var at = Get(Character, "advancedTraining"); var energy = (long[])Get(at, "energy"); var targets = (long[])Get(at, "levelTarget");
        var first = energy[0]; var second = energy[1]; var idle = (long)Get(Character, "idleEnergy"); var fired = false; object claim = null;
        AtRefreshObserver = () => {
            AtRefreshObserver = null; fired = true;
            if (change == "energy") Set(Get(MainType, "Settings"), "_manageEnergy", false);
            else
            {
                claim = CallMain("BeginExecutionClaim", "foreign-return-callback", change, Domain("Gear"), (Func<bool>)(() => true));
            }
        };
        FailAtTargetText = true;
        try
        {
            if (change == "energy") Throws(EndFixtureNativeTargets, "Native AT resource return failed", "revoked lane authority makes the old return unresolved");
            else EndFixtureNativeTargets();
        }
        finally
        {
            FailAtTargetText = false; AtRefreshObserver = null;
            if (claim != null) Equal(true, CallMain("EndExecutionClaim", claim, false), "callback setup claim retires without resource writes");
        }
        Equal(true, fired, "authority change actually occurs between the two native returns");
        Equal(0L, energy[0], "first authorized return stays committed");
        Equal(change == "energy" ? second : 0L, energy[1], "fresh lane authority, not a gear flag, guards companion allocation after " + change);
        Equal(idle + first + (change == "energy" ? 0 : second), Get(Character, "idleEnergy"), "return credits exactly the model amounts actually committed");
        Equal("-1,-1", string.Join(",", targets.Take(2)), "still-owned borrowed menu retirement remains separate from resource permission");
        Equal(null, Get(MainInstance, "_globalPresetNativeTargets"), "revocation retires the local lease without replay authority");
    }

    private static void CheckPendingRotationRetry(bool foreign)
    {
        var plan = SetupCommitBudget(); var stage = ((Array)Get(plan, "Stages")).GetValue(0);
        var library = Get(Get(MainType, "CustomPlanStore"), "<PresetLibrary>k__BackingField"); var root = Invoke(library, "ReadExternalLibrary");
        var id = (string)Get(stage, "PresetId");
        var row = ((IEnumerable)GetChildren(Json(root, "Presets"))).Cast<object>().Single(x => Property(Json(x, "Id"), "Value").Equals(id));
        SetJson(row, "BeardPriority", JsonParse("[1,3,5]")); SetJson(row, "BeardRotation", JsonParse("[6,0,4]"));
        SetJson(row, "DiggerWeights", JsonParse("[1,0,0,0,0,0,0,0,0,0,0,0]"));
        Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
        var diggers = Get(Character, "diggers"); var rows = (IList)Get(diggers, "diggers"); Set(rows[0], "maxLevel", 4L); Set(rows[11], "maxLevel", 2L);
        ((IList)Get(Get(Character, "allDiggers"), "baseGPSDrain"))[0] = 10d;
        CallMain("RunCustomPlanAutomation", plan);
        Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), "rotation fixture has a verified scheduled baseline");
        var receipt = Get(MainInstance, "_globalPresetReceipt"); var previous = Get(receipt, "Execution");
        var start = (DateTime)Get(receipt, "LastBeardRotation"); var acceptedAt = start.AddSeconds(60); var random = new RotationDrawCounter();
        Set(MainInstance, "_globalPresetBeardRandom", random);
        var searches = PresetSearches; var fired = false;
        PodInterference = pod => {
            if (((IList)Get(diggers, "activeDiggers")).Count != 0) return;
            PodInterference = null; fired = true;
            throw new InvalidOperationException("fixture digger refresh failure after rotation clear");
        };
        try { CallMain("RotateGlobalPresetBeards", acceptedAt); }
        finally { PodInterference = null; }
        Equal(true, fired, "transient rotation fault really occurs after native diggers were cleared");
        Equal("KnownPartial", Get(Get(MainInstance, "_presetSupportOutcome"), "Disposition").ToString(), "owned post-clear state is a known partial, not foreign support");
        Equal(1, random.Draws, "accepted rotation makes exactly one random decision");
        Check(ReferenceEquals(previous, Get(receipt, "Execution")), "partial rotation does not mark the new execution applied");
        Equal(start, Get(receipt, "LastBeardRotation"), "partial rotation does not consume its cadence event");
        Equal(false, Get(Get(MainInstance, "_protectedScheduler"), "_armed"), "partial rotation suspends stage credit immediately");
        if (foreign)
        {
            var pending = Get(Get(MainInstance, "_presetSupportAcquisition"), "Rotation");
            var wanted = Get(Get(pending, "Execution"), "Support");
            var level = ((Array)Get(wanted, "Diggers")).GetValue(0);
            CallMain("ApplyExactSupport", Selection((int[])Get(wanted, "Beards"), 0, (long)Get(level, "Level")), .75d, true);
            Set(rows[11], "curLevel", 1L);
            Equal(Property(Property(CallMain("CaptureSupportState"), "Selection"), "StableKey"), Get(Get(MainInstance, "_presetSupportContinuation"), "Desired"),
                "foreign control physically matches the desired selection, so values alone cannot prove continuation ownership");
        }
        Set(MainInstance, "_globalPresetRetryAt", 0d); Set(MainInstance, "_customLastQuickResourceRefresh", DateTime.MinValue);
        CallMain("ReconcileCustomResources", plan);
        if (foreign)
        {
            Equal("ForeignConflict", Get(Get(MainInstance, "_presetSupportOutcome"), "Disposition").ToString(), "foreign support still revokes the pending rotation's authority");
            Check(Get(MainInstance, "_presetExecutionBlock") != null, "foreign rotation cannot be silently re-planned");
            Equal(1L, Get(rows[11], "curLevel"), "foreign inactive level survives rotation retry");
            Equal(false, Get(Get(MainInstance, "_protectedScheduler"), "_armed"), "foreign rotation earns no stage credit");
        }
        else
        {
            Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), "ordinary allocation tick resumes and verifies the exact accepted rotation");
            Equal("0,1,3,5", string.Join(",", ((IList)Get(Get(Character, "beards"), "activeBeards")).Cast<int>().OrderBy(x => x)), "ordinary retry applies the selected optional beard, not the previous execution");
            Equal(acceptedAt, Get(receipt, "LastBeardRotation"), "verified retry commits the original cadence event, not retry time");
            Equal(true, Get(Get(MainInstance, "_protectedScheduler"), "_armed"), "only verified resumed rotation rearms stage credit");
            Equal(null, Get(MainInstance, "_presetSupportContinuation"), "verified rotation retires its bounded support continuation");
        }
        Equal(1, random.Draws, "ordinary retry never redraws a pending rotation");
        Equal(searches, PresetSearches, "pending rotation recovery never runs the optimizer");
    }

    private static void CheckPresetReviewRetryRegressions()
    {
        var failures = new System.Collections.Generic.List<Exception>();
        Action<string, Action> run = (name, test) => {
            try { test(); Console.WriteLine("PASS preset retry ownership " + name); }
            catch (Exception error) { failures.Add(new Exception(name, error)); Console.Error.WriteLine("FAIL preset retry ownership " + name + ": " + error); }
            finally { FailAtRefresh = false; AtRefreshObserver = null; FailPodRefresh = false; PodInterference = null; GearRenderObserver = null; }
        };
        run("foreign presentation-only retry", () => CheckForeignPresentationOnlyRetry(false));
        run("unknown presentation-only retry", () => CheckForeignPresentationOnlyRetry(true));
        run("support continuation rejects another intent", CheckSupportContinuationIntentBoundary);
        run("new acquisition after epoch reset", () => CheckFreshSupportAcquisition(true));
        run("new acquisition after explicit restart", () => CheckFreshSupportAcquisition(false));
        if (failures.Count > 0) throw new AggregateException("Preset retry ownership regressions failed.", failures);
    }

    private static void CheckForeignPresentationOnlyRetry(bool unknown)
    {
        var plan = SetupCommitBudget();
        FailAtRefresh = true;
        try { CallMain("RunCustomPlanAutomation", plan); }
        finally { FailAtRefresh = false; }
        var receipt = Get(MainInstance, "_globalPresetReceipt");
        Check(receipt != null, "render-only failure follows complete acquired accounting");
        Equal("ModelAppliedRenderFailed", Get(Get(MainType, "_presetResourceOutcome"), "Disposition").ToString(), "first pass records a model-committed presentation failure");
        Equal(true, Get(MainType, "_presetResourcePresentationPending"), "first pass schedules presentation separately");
        var execution = Get(receipt, "Execution");
        Equal(true, CallMain("LoadoutPresetAllocationsMatch", execution), "presentation-only retry starts with exactly matching allocations");
        var magic = ((IList)Get(Get(Character, "NGU"), "magicSkills"))[0]; var funded = (long)Get(magic, "magic");
        Check(funded > 0, "foreign callback control has actual Magic funding");
        var fired = false;
        AtRefreshObserver = () => {
            fired = true; AtRefreshObserver = null;
            Equal(true, CallMain("LoadoutPresetAllocationsMatch", execution), "retry renders the already-matching map, not a new transfer batch");
            if (unknown) Set(Get(MainType, "Settings"), "_manageMagic", false);
            else { Set(magic, "magic", funded - 1); Set(Get(Character, "magic"), "idleMagic", (long)Get(Get(Character, "magic"), "idleMagic") + 1); }
        };
        Set(MainInstance, "_customLastQuickResourceRefresh", DateTime.MinValue); CallMain("ReconcileCustomResources", plan);
        Equal(true, fired, "second pass actually reaches presentation-only retry");
        Equal(unknown ? "UnknownOutcome" : "ForeignConflict", Get(Get(MainType, "_presetResourceOutcome"), "Disposition").ToString(), "second pass observes a typed unsafe result");
        Equal(null, Get(MainInstance, "_presetExecutionBlock"), "resource uncertainty is lane-scoped, not a blanket preset execution latch");
        Check(ReferenceEquals(receipt, Get(MainInstance, "_globalPresetReceipt")), "foreign presentation retains acquired intent, not verified application authority");
        Equal(false, CallMain("GlobalPresetReceiptValid", Get(receipt, "Owner")), "retained intent cannot admit credit or actions while resource authority is unresolved");
        Equal("\"NeedsUser\"", Json(CallMain("GetResourceCoordinationRuntime"), "state").ToString(), "foreign retry reports unresolved resource authority");
        var scheduler = Get(MainInstance, "_protectedScheduler");
        Equal(false, Get(scheduler, "_armed"), "foreign retry suspends protected credit immediately");
        Equal(unknown ? funded : funded - 1, Get(magic, "magic"), "unsafe retry preserves observed Magic rather than repeating model accounting");
        if (unknown) Set(Get(MainType, "Settings"), "_manageMagic", true);
        var allocations = CallMain("GlobalPresetAllocationState"); var writes = ResourceTransfers; var searches = PresetSearches;
        var credit = ((double[])Property(scheduler, "AppliedSeconds")).ToArray();
        for (var tick = 0; tick < 2; tick++)
        {
            foreach (var clock in new[] { "rebirthTime", "totalPlaytime" })
                Set(Get(Character, clock), "totalseconds", (double)Get(Get(Character, clock), "totalseconds") + 2);
            Set(scheduler, "_lastHeartbeat", (double)MainType.GetProperty("ProtectedHeartbeat", All).GetValue(null) - 2);
            Set(MainInstance, "_globalPresetRetryAt", 0d); Set(MainInstance, "_customLastQuickResourceRefresh", DateTime.MinValue);
            CallMain("ReconcileCustomResources", plan);
        }
        Equal(writes, ResourceTransfers, "subsequent ticks never overwrite the foreign retry map");
        Equal(allocations, CallMain("GlobalPresetAllocationState"), "subsequent ticks preserve complete observed allocations");
        Equal(searches, PresetSearches, "foreign presentation retry never grants replacement search authority");
        Check(credit.SequenceEqual((double[])Property(scheduler, "AppliedSeconds")), "unresolved presentation mutation earns no subsequent protected credit");
    }

    private static object SetupPendingPresetSupport()
    {
        var plan = SetupCommitBudget(); var stage = ((Array)Get(plan, "Stages")).GetValue(0);
        var store = Get(MainType, "CustomPlanStore"); var library = Get(store, "<PresetLibrary>k__BackingField");
        var root = Invoke(library, "ReadExternalLibrary"); var id = (string)Get(stage, "PresetId");
        var row = ((IEnumerable)GetChildren(Json(root, "Presets"))).Cast<object>().Single(x => Property(Json(x, "Id"), "Value").Equals(id));
        SetJson(row, "DiggerWeights", JsonParse("[1,0,0,0,0,0,0,0,0,0,0,0]"));
        Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
        var name = Property(Get(MainType, "Settings"), "CustomPlanFile");
        Invoke(store, "Save", name, plan); plan = Invoke(store, "Load", name);
        Set(MainInstance, "_customRuntimePlanCache", plan); Set(MainInstance, "_customRuntimePlanCacheName", name);
        Set(MainInstance, "_customRuntimePlanCacheLoadedAt", DateTime.Now);
        var rows = (IList)Get(Get(Character, "diggers"), "diggers");
        Set(rows[0], "maxLevel", 4L); Set(rows[11], "maxLevel", 2L);
        ((IList)Get(Get(Character, "allDiggers"), "baseGPSDrain"))[0] = 10d;
        FailPodRefresh = true;
        try { CallMain("RunCustomPlanAutomation", plan); }
        finally { FailPodRefresh = false; }
        Equal("KnownPartial", Get(Get(MainInstance, "_presetSupportOutcome"), "Disposition").ToString(), "fixture reaches a real owned native support partial");
        Check(Get(MainInstance, "_presetSupportContinuation") != null, "known partial retains bounded continuation evidence");
        Equal(null, Get(MainInstance, "_globalPresetReceipt"), "partial support does not claim complete application");
        return plan;
    }

    private static void CheckSupportContinuationIntentBoundary()
    {
        var plan = SetupPendingPresetSupport();
        var originalAcquisition = Get(Get(MainInstance, "_presetSupportContinuation"), "Owner"); var originalSearches = PresetSearches;
        FailPodRefresh = true;
        try { Set(MainInstance, "_globalPresetRetryAt", 0d); CallMain("RunCustomPlanAutomation", plan); }
        finally { FailPodRefresh = false; }
        Equal("KnownPartial", Get(Get(MainInstance, "_presetSupportOutcome"), "Disposition").ToString(), "a second native partial stays within the same accepted attempt");
        var continuation = Get(MainInstance, "_presetSupportContinuation");
        Check(ReferenceEquals(originalAcquisition, Get(continuation, "Owner")), "repeated native failures preserve the original event/acquisition/epoch binding");
        Equal(originalSearches, PresetSearches, "a second native partial cannot authorize another selection search");
        var support = Property(CallMain("CaptureSupportState"), "StableKey"); var calls = PodRefreshCalls;
        var acquisition = Get(continuation, "Owner");
        CallMain("BeginPresetSupportAcquisition", Get(acquisition, "Event"), Get(acquisition, "Id"));
        Throws(() => CallMain("ApplyPresetDesiredSupport", Selection(new int[0], 0, 2), .75d, true), "acquisition", "a different desired setup cannot supersede the pending attempt without fresh authorization");
        var candidateSupport = Get(Get(Get(MainInstance, "_globalPresetApplyingPolicy"), "Candidate"), "Support");
        var wanted = Selection((int[])Get(candidateSupport, "Beards"), 0, 4);
        Throws(() => CallMain("ApplyPresetDesiredSupport", wanted, .5d, true), "acquisition", "a different budget cannot supersede pending support intent");
        Throws(() => CallMain("ApplyPresetDesiredSupport", wanted, .75d, false), "acquisition", "pending support cannot silently drop its budget policy");
        Check(ReferenceEquals(continuation, Get(MainInstance, "_presetSupportContinuation")), "wrong-intent rejection preserves the original retry evidence");
        Equal(support, Property(CallMain("CaptureSupportState"), "StableKey"), "wrong-intent rejection preserves current support");
        Equal(calls, PodRefreshCalls, "wrong-intent rejection makes no native input, clear or activation calls");
        var searches = PresetSearches;
        Set(MainInstance, "_globalPresetRetryAt", 0d); CallMain("RunCustomPlanAutomation", plan);
        Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), "the original accepted attempt still finishes normally");
        Equal(searches, PresetSearches, "same-attempt continuation retains selected decisions rather than searching again");
        Equal(Get(acquisition, "Id"), Get(Get(Get(MainInstance, "_globalPresetReceipt"), "Policy"), "Acquisition"), "same-attempt retries retain their acquired decision identity");
    }

    private static void CheckFreshSupportAcquisition(bool epochReset)
    {
        var plan = SetupPendingPresetSupport(); var oldContinuation = Get(MainInstance, "_presetSupportContinuation");
        var rows = (IList)Get(Get(Character, "diggers"), "diggers");
        Set(rows[0], "curLevel", 3L); Set(rows[11], "curLevel", 1L);
        if (epochReset)
        {
            Set(Get(Character, "stats"), "rebirthNumber", (long)Get(Get(Character, "stats"), "rebirthNumber") + 1);
            CallMain("ResetCustomAutomationRuntime");
            Equal(null, CallMain("GetProtectedStage", plan), "epoch observation cannot silently admit an uncredited replacement entry");
            var recovery = Json(CallMain("GetExecutionPersistenceRuntime"), "recovery");
            var preview = CallMain("PreviewExecutionRestart", JsonParse("{\"stageIndex\":0,\"checkpointRevision\":" + Json(recovery, "checkpointRevision")
                + ",\"planRevision\":" + Json(recovery, "planRevision") + "}"));
            CallMain("ApplyExecutionRebind", JsonParse("{\"token\":" + Json(preview, "token") + ",\"checkpointRevision\":" + Json(preview, "checkpointRevision") + "}"));
            Equal(false, Get(Get(MainInstance, "_protectedScheduler"), "_armed"), "explicit epoch recovery queues new intent without fabricating applied credit");
        }
        else
        {
            Set(MainInstance, "_globalPresetRetryAt", 0d); CallMain("RunCustomPlanAutomation", plan);
            Equal("ForeignConflict", Get(Get(MainInstance, "_presetSupportOutcome"), "Disposition").ToString(), "a same-attempt foreign edit is not silently acknowledged");
            Equal(3L, Get(rows[0], "curLevel"), "same-attempt refusal preserves the foreign level");
            var scheduler = Get(MainInstance, "_protectedScheduler");
            var entry = Get(MainInstance, "_protectedGeneration") + ":" + Property(scheduler, "Entry");
            var args = JsonParse("{\"name\":\"" + Property(Get(MainType, "Settings"), "CustomPlanFile") + "\",\"expectedRevision\":\""
                + Get(plan, "RuntimeRevision") + "\",\"expectedEntry\":\"" + entry + "\",\"action\":\"restart\",\"stageIndex\":0}");
            CallMain("OverrideCustomStage", args);
        }
        var inventory = Get(Character, "inventory"); var head = Get(inventory, "head"); var successor = ((IList)Get(inventory, "inventory"))[1];
        Set(successor, "id", 551); Set(successor, "type", Get(head, "type")); Set(successor, "bossRequired", 1); Set(successor, "removable", true);
        Set(successor, "curAttack", 2000000f); Set(successor, "curDefense", 2000000f); Set(successor, "capAttack", 2000000f); Set(successor, "capDefense", 2000000f);
        Set(successor, "spec1Type", Enum.Parse(Field(successor, "spec1Type").FieldType, "NGU")); Set(successor, "spec1Cur", 2000f);
        var retiredBeforeGear = false;
        GearRenderObserver = () => {
            retiredBeforeGear = true;
            Equal(null, Get(MainInstance, "_presetSupportContinuation"), "fresh acquisition retires stale support evidence before gear application");
        };
        var levels = new System.Collections.Generic.List<long>();
        PodInterference = pod => { if ((int)Get(pod, "id") == 0) levels.Add((long)Get(rows[0], "curLevel")); };
        try { CallMain("RunCustomPlanAutomation", plan); }
        finally { PodInterference = null; GearRenderObserver = null; }
        Equal(true, retiredBeforeGear, "positive control: fresh acquisition executes a real successor gear change");
        Check(ReferenceEquals(successor, Get(inventory, "head")), "fresh authorized acquisition equips its current selected successor");
        Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), "fresh authorized stage succeeds from current support after " + (epochReset ? "epoch reset" : "explicit restart"));
        Equal(null, Get(MainInstance, "_presetExecutionBlock"), "stale support evidence cannot block a fresh authorized acquisition");
        Equal(null, Get(MainInstance, "_presetSupportContinuation"), "fresh verified application retires the previous continuation");
        Equal(4L, Get(rows[0], "curLevel"), "fresh acquisition applies its current planned level");
        Equal(1L, Get(rows[11], "curLevel"), "fresh acquisition preserves unclaimed foreign inactive levels");
        Check(levels.Count > 0 && levels.Contains(3L) && !levels.Contains(0L), "fresh application observes the current level and never restores the old pre-partial zero");
        Check(!ReferenceEquals(oldContinuation, Get(MainInstance, "_presetSupportContinuation")), "the previous event's native receipt is not reused");
    }

    private static void CheckReviewedLaneExecution()
    {
        SetupResourceCoordination(false, true, "none");
        var settings = Get(MainType, "Settings");
        var library = Get(Get(MainType, "CustomPlanStore"), "<PresetLibrary>k__BackingField");
        var root = Invoke(library, "ReadExternalLibrary");
        var id = (string)Get(Get(MainInstance, "_globalPresetReceipt"), "Id");
        var row = ((IEnumerable)GetChildren(Json(root, "Presets"))).Cast<object>().Single(x => Property(Json(x, "Id"), "Value").Equals(id));
        SetJson(row, "EnergyPriorities", JsonParse("[\"NGU\"]")); SetJson(row, "MagicPriorities", JsonParse("[\"NGU\"]"));
        SetJson(row, "ResourcePrioritiesReviewed", Json(JsonParse("{\"value\":true}"), "value"));
        var revision = Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
        var args = JsonParse("{\"presetId\":\"" + id + "\",\"libraryRevision\":\"" + revision + "\"}");
        var preview = CallMain("PreviewGlobalPreset", args);
        var apply = JsonParse(args.ToString().TrimEnd('}') + ",\"previewId\":" + Json(preview, "previewId") + ",\"candidateKey\":\"current\"}");
        Invoke(MainInstance, "ApplyGlobalPreset", apply, null);
        var receipt = Get(MainInstance, "_globalPresetReceipt"); var policy = Get(receipt, "Policy");
        var preset = Get(policy, "ExecutionPreset");
        var request = CallMain("LoadoutPresetRequest", preset);
        Equal(true, Get(request, "ResourcePrioritiesReviewed"), "compiled request copies explicit lane review");
        Check(((string[])Get(request, "EnergyPriorities")).SequenceEqual(new[] { "NGU" })
            && ((string[])Get(request, "MagicPriorities")).SequenceEqual(new[] { "NGU" }), "compiled request copies both independent lane arrays");
        var energy = ((IList)Get(Get(Character, "NGU"), "skills"))[0];
        var magic = ((IList)Get(Get(Character, "NGU"), "magicSkills"))[0];
        var ritual = ((IList)Get(Get(Character, "bloodMagic"), "ritual"))[0];
        Equal(10000L, Get(energy, "energy"), "reviewed baseline funds Energy NGU independently");
        Equal(1000L, Get(magic, "magic"), "reviewed baseline funds Magic NGU");
        var searches = PresetSearches; var recalls = Get(typeof(Program), "Recalls");
        var history = Invoke(preset, "ToJson").ToString();
        Sequence(Get(Character, "bloodMagicController"), "normalSpeedDividers", 1, .02f);
        Set(settings, "_customAutoBloodSpellsEnabled", true); Set(settings, "_customBloodSpaghettiGoalPercent", 50);
        Equal(true, CallMain("CustomBloodGoalCatchUpRequested", (object)null), "full-Magic temporary blood intent is actually eligible");
        request = CallMain("LoadoutPresetRequest", preset);
        Check(((string[])Get(request, "MagicPriorities")).SequenceEqual(new[] { "RIT", "NGU" }), "temporary blood merges before candidate evaluation");
        var scaled = Invoke(Plugin.GetType("NGUInjector.CustomPlans.CustomLoadoutPreset", true), "Parse", Invoke(preset, "ToJson"));
        var importance = new double[14]; importance[0] = .2; importance[1] = .1; Set(scaled, "Weights", importance);
        var scaledRequest = CallMain("LoadoutPresetRequest", scaled);
        Equal(0d, ((double[])Get(scaledRequest, "Weights"))[2], "temporary blood does not introduce a setup objective");
        Equal(.2d, ((double[])Get(scaledRequest, "Weights"))[0], "temporary lane priority preserves independent Energy importance");
        Check(importance.SequenceEqual((double[])Get(scaledRequest, "Weights")), "temporary RIT priority preserves every original objective weight");
        CallMain("RunManualGlobalPresetRefresh");
        Equal(10000L, Get(energy, "energy"), "blood never displaces the Energy lane");
        Equal(0L, Get(magic, "magic"), "higher Magic blood demand preempts all Magic NGU");
        Equal(1000L, Get(ritual, "magic"), "blood receives the exact whole Magic pool");
        var execution = Get(receipt, "Execution");
        Equal(0d, ((Array)Get(execution, "Metrics")).GetValue(1), "unfunded Magic NGU contributes exactly zero candidate utility");
        Equal(true, CallMain("GlobalPresetReceiptValid", "dashboard"), "reviewed blood execution is verified against native allocations");
        var writes = ResourceTransfers; CallMain("RunManualGlobalPresetRefresh");
        Equal(writes, ResourceTransfers, "unchanged reviewed demand performs zero model writes");
        Set(settings, "_customAutoBloodSpellsEnabled", false);
        CallMain("RunManualGlobalPresetRefresh");
        Equal(0L, Get(ritual, "magic"), "release removes only the temporary ritual request");
        Equal(1000L, Get(magic, "magic"), "release recomputes and funds the present baseline");
        Equal(10000L, Get(energy, "energy"), "release preserves independent Energy NGU");
        Equal(recalls, Get(typeof(Program), "Recalls"), "preemption and release never restore an old resource snapshot or recall whole lanes");
        Equal(searches, PresetSearches, "temporary demand changes never search replacement gear");
        Equal(history, Invoke(preset, "ToJson").ToString(), "temporary priority never mutates acquired user intent");
        Check(ReferenceEquals(receipt, Get(MainInstance, "_globalPresetReceipt")), "preemption retains acquisition identity");
        CheckReviewedFallbackExecution();
        CheckReviewedSetupSelection();
        CheckReviewedNguCaptureSafety();
        CheckWandoosImportDependencies();
        CheckCompiledForeignResourceMutation();
        CheckCompiledSupportMutationOutcomes();
        CheckCompiledSupportContinuation();
        CheckSelectedSetupRouteAdmission();
        CheckSelectedSetupSuccessorTimeline();
        CheckCombatPrepSignedSupportRuntime();
    }

    private static void CheckReviewedFallbackExecution()
    {
        SetupResourceCoordination(false, true, "none");
        Set(Get(Get(Character, "buttons"), "adventure"), "m_Interactable", true);
        var preset = Invoke(Plugin.GetType("NGUInjector.CustomPlans.CustomLoadoutPreset", true), "Parse", JsonParse(
            "{\"Id\":\"reviewed-fallback\",\"Name\":\"Reviewed fallback\",\"Weights\":[0,0,0,0,0,0,0,0,0,0,1,0,0,0],"
            + "\"SupportMode\":\"current\",\"SwapSeconds\":0,\"AutoUpdate\":true,\"ResourcePrioritiesReviewed\":true,"
            + "\"EnergyPriorities\":[\"NGU\"],\"MagicPriorities\":[\"NGU\"]}"));
        var settings = Get(MainType, "Settings");
        var transfers = ResourceTransfers; var swaps = Get(typeof(Program), "Swaps"); var recalls = Get(typeof(Program), "Recalls");
        var before = CallMain("GlobalPresetAllocationState"); var support = CallMain("GlobalPresetSupportState");
        var receipt = Get(MainInstance, "_globalPresetReceipt"); var persisted = Get(MainInstance, "_standaloneSelectedPolicy");
        foreach (var energy in new[] { true, false })
        {
            var field = energy ? "_manageEnergy" : "_manageMagic";
            Set(settings, field, false);
            try
            {
                Throws(() => CallMain("EquipGlobalPresetDefinition", preset, "dashboard", "explicit-equip", true, null),
                    "requires managed " + (energy ? "Energy" : "Magic"), "zero-objective reviewed lane requires permission before writes");
                Equal(before, CallMain("GlobalPresetAllocationState"), "permission failure preserves both native lanes");
                Equal(support, CallMain("GlobalPresetSupportState"), "permission failure preserves native support");
                Equal(transfers, ResourceTransfers, "permission failure precedes all resource transfers");
                Equal(swaps, Get(typeof(Program), "Swaps"), "permission failure precedes all equipment writes");
                Equal(recalls, Get(typeof(Program), "Recalls"), "permission failure precedes whole-lane recalls");
                Equal(persisted, Get(MainInstance, "_standaloneSelectedPolicy"), "permission failure does not replace the durable selection");
                Check(ReferenceEquals(receipt, Get(MainInstance, "_globalPresetReceipt")), "permission failure leaves current receipt intact");
            }
            finally { Set(settings, field, true); }
        }
        CallMain("EquipGlobalPresetDefinition", preset, "dashboard", "explicit-equip", true, null);
        receipt = Get(MainInstance, "_globalPresetReceipt"); var policy = Get(receipt, "Policy"); var execution = Get(receipt, "Execution");
        var energyRow = ((IList)Get(Get(Character, "NGU"), "skills"))[0];
        var magicRow = ((IList)Get(Get(Character, "NGU"), "magicSkills"))[0];
        Equal(10000L, Get(energyRow, "energy"), "zero Energy objective still funds reviewed native NGU fallback");
        Equal(1000L, Get(magicRow, "magic"), "zero Magic objective still funds reviewed native NGU fallback");
        Equal(true, Get(execution, "ControlsEnergy"), "funded execution owns reviewed Energy");
        Equal(true, Get(execution, "ControlsMagic"), "funded execution owns reviewed Magic");
        Equal(true, CallMain("GlobalPresetHasMagicObjective", policy), "saved reviewed Magic token is baseline authority, not a temporary claim");
        Equal(null, Get(receipt, "TemporaryMagicContext"), "reviewed fallback does not borrow temporary Magic ownership");
        Equal(3, Convert.ToInt32(CallMain("PresetResourceReviewDomains", preset)), "review footprint includes zero-objective reviewed lanes");
        var preview = CallMain("PreviewExecutionRebind", JsonParse("{}"));
        var domains = Convert.ToInt32(Get(Get(MainInstance, "_executionRebindReview"), "RecoveryDomains"));
        Equal(3, domains & 3, "real rebind recovery includes both reviewed resource lanes");
        Equal(true, Property(Json(Json(preview, "observedResources"), "EnergyNgu"), "IsArray"), "rebind observes zero-objective Energy NGU resources");
        Equal(true, Property(Json(Json(preview, "observedResources"), "MagicNgu"), "IsArray"), "rebind observes zero-objective Magic NGU resources");

        var saved = JsonParse((string)Get(MainInstance, "_standaloneSelectedPolicy"));
        var activities = Json(Json(saved, "decision"), "activities");
        var os = (int)Get(Get(Get(policy, "Candidate"), "Activities"), "Os");
        SetJson(activities, "os", ParseFixtureScalar(((os + 1) % 3).ToString()));
        var imported = CallMain("ImportExecutionSelection", saved, null);
        Equal((os + 1) % 3, Get(Get(Get(imported, "Candidate"), "Activities"), "Os"), "unrelated OS change preserves the acquired OS decision without a false recovery dependency");
        Equal(null, Get(Get(imported, "Candidate"), "SetupScore"), "import retains selection without inventing a setup rank");
        Equal(null, Get(Get(imported, "Candidate"), "SetupEligible"), "import does not fabricate a historical setup-search eligibility witness");
        var importCapture = CallMain("CaptureLoadoutPresetDefinition", Get(imported, "ExecutionPreset"));
        Equal(true, Get(CallMain("ReplayGlobalPreset", imported, Get(imported, "Preset"), importCapture), "Feasible"), "imported null setup rank can replay without reranking");
        SetJson(activities, "fixedLegacyAmounts", JsonParse("[\"0\",\"0\",\"1\",\"0\",\"0\",\"0\",\"0\",\"0\",\"0\",\"0\",\"0\"]"));
        Throws(() => CallMain("ImportExecutionSelection", saved, null), "AUG/OS is unavailable", "actual retained WAN funding still binds acquired OS");

        var empty = Invoke(preset, "MemberwiseClone"); Set(empty, "EnergyPriorities", new string[0]); Set(empty, "MagicPriorities", new string[0]);
        Equal(0, Convert.ToInt32(CallMain("PresetResourceReviewDomains", empty)), "stat-only reviewed empty lanes remain uncontrolled");
        var objectives = new double[14]; objectives[0] = objectives[1] = 1; Set(empty, "Weights", objectives);
        Equal(3, Convert.ToInt32(CallMain("PresetResourceReviewDomains", empty)), "objective-owned reviewed empty lanes stay controlled-empty");
        Set(empty, "Weights", ((double[])Get(preset, "Weights")).ToArray()); Set(empty, "ResourcePrioritiesReviewed", false);
        Set(empty, "EnergyPriorities", new[] { "NGU" }); Set(empty, "MagicPriorities", new[] { "NGU" });
        Equal(0, Convert.ToInt32(CallMain("PresetResourceReviewDomains", empty)), "unreviewed token proposals grant no resource authority");

        CallMain("StopManualGlobalPreset");
        Equal(0L, Get(energyRow, "energy"), "stop releases the owned zero-objective Energy fallback");
        Equal(0L, Get(magicRow, "magic"), "stop releases the owned zero-objective Magic fallback");
        Equal(10000L, Get(Character, "idleEnergy"), "release conserves native Energy");
        Equal(1000L, Get(Get(Character, "magic"), "idleMagic"), "release conserves native Magic");
    }

    private static void CheckReviewedSetupSelection()
    {
        foreach (var bloodFirst in new[] { false, true })
        {
            SetupResourceCoordination(false, true, "none");
            var inventory = Get(Character, "inventory"); var head = Get(inventory, "head"); var challenger = ((IList)Get(inventory, "inventory"))[0];
            Set(challenger, "id", 550); Set(challenger, "type", Get(head, "type")); Set(challenger, "bossRequired", 1); Set(challenger, "removable", true);
            Set(challenger, "spec1Type", Enum.Parse(Field(challenger, "spec1Type").FieldType, "NGU")); Set(challenger, "spec1Cur", 1000f);
            Sequence(Get(Character, "bloodMagicController"), "normalSpeedDividers", 1, .02f);
            var preset = Invoke(Plugin.GetType("NGUInjector.CustomPlans.CustomLoadoutPreset", true), "Parse", JsonParse(
                "{\"Id\":\"setup-primary\",\"Name\":\"Setup primary\",\"Weights\":[0,1,0,0,0,0,0,0,0,0,0,0,0,0],"
                + "\"SupportMode\":\"current\",\"SwapSeconds\":0,\"AutoUpdate\":true,\"ResourcePrioritiesReviewed\":true,"
                + "\"EnergyPriorities\":[\"NGU\"],\"MagicPriorities\":" + (bloodFirst ? "[\"RIT\",\"NGU\"]" : "[\"NGU\",\"RIT\"]") + "}"));
            CallMain("PreviewLoadoutPreset", JsonParse("{\"preset\":" + Invoke(preset, "ToJson") + "}"));
            var candidates = (Array)Get(Get(Get(MainInstance, "_loadoutPresetPreview"), "Plan"), "Candidates");
            var current = candidates.GetValue(0); var balanced = candidates.GetValue(1);
            Check(Convert.ToDouble(Get(balanced, "SetupScore")) > Convert.ToDouble(Get(current, "SetupScore")), "native primary objective distinguishes balanced gear from current");
            if (bloodFirst)
            {
                Equal(0d, Get(current, "Score"), "strict RIT priority leaves current Magic NGU utility zero");
                Equal(0d, Get(balanced, "Score"), "strict RIT priority also leaves improved gear's funded Magic NGU utility zero");
            }
            var picked = ((IEnumerable)CallMain("LoadoutPresetSetupCandidates", candidates, true)).Cast<object>().Single();
            Check(ReferenceEquals(balanced, picked), "shared Equip/manual-adventure selector uses setup rank, not funded score");
            Set(balanced, "Feasible", false); Set(balanced, "Score", null);
            picked = ((IEnumerable)CallMain("LoadoutPresetSetupCandidates", candidates, true)).Cast<object>().Single();
            Check(ReferenceEquals(balanced, picked), "reviewed best setup is selected before funded feasibility, with no feasible replacement");
            var cached = Get(MainInstance, "_loadoutPresetPreview"); var transfers = ResourceTransfers; var swaps = Get(typeof(Program), "Swaps");
            Throws(() => CallMain("PreflightLoadoutPreset", CallMain("LoadoutPresetRequest", preset), Get(cached, "Capture"), picked),
                "exact selected policy", "production preflight rejects the exact unfunded best setup");
            Equal(transfers, ResourceTransfers, "unfunded best setup rejects before native resource writes");
            Equal(swaps, Get(typeof(Program), "Swaps"), "unfunded best setup rejects before native equipment writes");
            Set(balanced, "SetupScore", null); Set(current, "SetupScore", null); Set(current, "Score", 100d);
            Equal(0, ((IEnumerable)CallMain("LoadoutPresetSetupCandidates", candidates, true)).Cast<object>().Count(), "reviewed selector never falls back from missing SetupScore to Score");
            Equal(1, ((IEnumerable)CallMain("LoadoutPresetSetupCandidates", candidates, false)).Cast<object>().Count(), "unreviewed selector retains existing funded filtering");

            CallMain("EquipGlobalPresetDefinition", preset, "dashboard", "explicit-equip", true, null);
            var receipt = Get(MainInstance, "_globalPresetReceipt");
            Equal("balanced", Get(Get(Get(receipt, "Policy"), "Candidate"), "Key"), "production Equip selects the primary setup for either resource order");
            Check(ReferenceEquals(challenger, Get(inventory, "head")), "production equips the exact primary-objective gear despite different resource chains");
            Equal(10000L, Get(((IList)Get(Get(Character, "NGU"), "skills"))[0], "energy"), "zero Energy objective funds actual native fallback");
            Equal(bloodFirst ? 0L : 1000L, Get(((IList)Get(Get(Character, "NGU"), "magicSkills"))[0], "magic"), "actual native Magic respects strict resource order without replacing gear");
            Equal(bloodFirst ? 1000L : 0L, Get(((IList)Get(Get(Character, "bloodMagic"), "ritual"))[0], "magic"), "actual native ritual receives only chain funding");
            Equal(true, CallMain("GlobalPresetReceiptValid", "dashboard"), "primary selected setup is verified with exact native funding");
            if (bloodFirst)
            {
                var searches = PresetSearches;
                Sequence(Get(Character, "bloodMagicController"), "normalSpeedDividers", 1, .002f);
                CallMain("RunManualGlobalPresetRefresh");
                Equal(101L, Get(((IList)Get(Get(Character, "bloodMagic"), "ritual"))[0], "magic"), "reviewed zero-weight ritual takes only its native productive cap");
                Equal(899L, Get(((IList)Get(Get(Character, "NGU"), "magicSkills"))[0], "magic"), "only the real ritual residual funds the next reviewed priority");
                Equal(0L, Get(Get(Character, "magic"), "idleMagic"), "strict residual chain conserves the full native Magic pool");
                Check(ReferenceEquals(challenger, Get(inventory, "head")), "cap change retains the exact acquired primary setup");
                Equal(searches, PresetSearches, "cap change never searches replacement gear");
            }
        }
    }

    private static void CheckReviewedNguCaptureSafety()
    {
        SetupResourceCoordination(false, true, "none");
        var preset = Invoke(Plugin.GetType("NGUInjector.CustomPlans.CustomLoadoutPreset", true), "Parse", JsonParse(
            "{\"Id\":\"cap-fallback\",\"Name\":\"Cap fallback\",\"Weights\":[0,0,0,0,0,0,0,0,0,0,0,0,1,0],"
            + "\"SupportMode\":\"current\",\"SwapSeconds\":0,\"AutoUpdate\":true,\"ResourcePrioritiesReviewed\":true,"
            + "\"EnergyPriorities\":[\"NGU\",\"WAN\",\"TM\",\"AUG\",\"AT\"],\"MagicPriorities\":[\"NGU\",\"RIT\",\"WAN\",\"TM\"]}"));
        var ngu = Get(Character, "NGU"); var row = ((IList)Get(ngu, "skills"))[0];
        Set(ngu, "autoAdvance", true); Set(row, "target", 10L);
        var transfers = ResourceTransfers; var swaps = Get(typeof(Program), "Swaps");
        Throws(() => CallMain("CaptureLoadoutPresetDefinition", preset), "auto-advance", "cap-only reviewed NGU fallback guards unfinished finite native transfers");
        Equal(true, Get(ngu, "autoAdvance"), "auto-advance rejection never changes the native preference");
        Equal(transfers, ResourceTransfers, "auto-advance rejection precedes resource writes"); Equal(swaps, Get(typeof(Program), "Swaps"), "auto-advance rejection precedes equipment writes");
        Set(ngu, "autoAdvance", false);
        var wandoos = Get(Character, "wandoos98"); Set(wandoos, "os", Enum.ToObject(Field(wandoos, "os").FieldType, 2));
        try
        {
            BlockPresetLevelGain = true;
            var capture = CallMain("CaptureLoadoutPresetDefinition", preset); var snapshot = Get(capture, "Snapshot");
            Equal(false, ((bool[])Get(snapshot, "Available"))[0], "native level lock excludes zero-objective Energy NGU funding");
            Equal(false, ((bool[])Get(snapshot, "Available"))[1], "native level lock excludes zero-objective Magic NGU funding");
            Equal(2, Get(snapshot, "WandoosOs"), "unavailable optional WAN still captures the actual native OS tier");
            Equal(null, CallMain("GlobalPresetReadiness", preset, null, null), "optional locked fallback is skipped, not a hard setup readiness failure");
            CallMain("EquipGlobalPresetDefinition", preset, "dashboard", "explicit-equip", true, null);
            Equal(0L, Get(row, "energy"), "level-locked NGU fallback receives no native Energy allocation");
            Equal(0L, Get(((IList)Get(ngu, "magicSkills"))[0], "magic"), "level-locked NGU fallback receives no native Magic allocation");
            Equal(true, CallMain("GlobalPresetReceiptValid", "dashboard"), "cap setup remains valid with ordinary locked fallbacks skipped");
            Equal(2, Get(Get(Get(Get(MainInstance, "_globalPresetReceipt"), "Execution"), "Activities"), "Os"), "locked WAN fallback retains the acquired nondefault OS tier");
            var saved = JsonParse((string)Get(MainInstance, "_standaloneSelectedPolicy"));
            var activities = Json(Json(saved, "decision"), "activities");
            var os = (int)Property(Json(activities, "os"), "AsInt");
            SetJson(activities, "os", ParseFixtureScalar(((os + 1) % 3).ToString()));
            Throws(() => CallMain("ImportExecutionSelection", saved, null), "AUG/OS is unavailable", "configured WAN fallback retains its acquired OS even when currently unfunded");
        }
        finally { BlockPresetLevelGain = false; }
    }

    private static void CheckWandoosImportDependencies()
    {
        foreach (var kind in new[] { "inactive-proposal", "reviewed-dormant", "reviewed-excluded", "legacy-funded" })
        {
            SetupResourceCoordination(false, true, "none");
            var reviewed = kind == "reviewed-dormant" || kind == "reviewed-excluded";
            var wandoosGoal = kind == "reviewed-excluded" || kind == "legacy-funded";
            var blocked = kind == "reviewed-dormant" || kind == "legacy-funded";
            var wandoos = Get(Character, "wandoos98");
            Set(wandoos, "os", Enum.ToObject(Field(wandoos, "os").FieldType, 2));
            Set(Get(Get(Character, "buttons"), "brokenTimeMachine"), "m_Interactable", true);
            Set(Get(Character, "machine"), "transferredBankLevels", true);
            var tm = Get(Character, "timeMachineController"); Set(tm, "character", Character);
            Set(tm, "baseNormalSpeedDivider", 1f); Set(tm, "baseNormalGoldMultiDivider", 1f);
            Set(tm, "baseSpeedCost", 1f); Set(tm, "baseGoldMultiCost", 1f);
            var preset = Invoke(Plugin.GetType("NGUInjector.CustomPlans.CustomLoadoutPreset", true), "Parse", JsonParse(
                "{\"Id\":\"os-dependency\",\"Name\":\"OS dependency\",\"Weights\":"
                + (wandoosGoal ? "[0,0,0,0,0,0,0,0,1,0,0,0,0,0]" : "[0,0,0,0,0,0,1,0,0,0,0,0,0,0]")
                + ",\"SupportMode\":\"current\",\"SwapSeconds\":0,\"AutoUpdate\":true,\"ResourcePrioritiesReviewed\":" + (reviewed ? "true" : "false")
                + ",\"EnergyPriorities\":" + (kind == "inactive-proposal" ? "[\"WAN\"]" : kind == "reviewed-dormant" ? "[\"TM\",\"WAN\"]" : "[\"TM\"]")
                + ",\"MagicPriorities\":[\"TM\"]}"));
            CallMain("EquipGlobalPresetDefinition", preset, "dashboard", "explicit-equip", true, null);
            var receipt = Get(MainInstance, "_globalPresetReceipt"); var policy = Get(receipt, "Policy");
            var acquired = Get(Get(policy, "Candidate"), "Activities");
            Equal(2, Get(acquired, "Os"), kind + " acquires actual native OS2");
            var funded = (long)Get(acquired, "WandoosEnergy") + (long)Get(acquired, "WandoosMagic");
            if (kind == "legacy-funded") Check(funded > 0, "legacy OS dependency has actual pinned WAN funding");
            else Equal(0L, funded, kind + " has no acquired WAN allocation");
            if (!wandoosGoal) Check((long)Get(acquired, "TimeMachineEnergy") + (long)Get(acquired, "TimeMachineMagic") > 0,
                kind + " actually funds its TM-only objective");
            var saved = JsonParse((string)Get(MainInstance, "_standaloneSelectedPolicy"));
            var searches = PresetSearches; var writes = ResourceTransfers; var swaps = Get(typeof(Program), "Swaps");
            Set(wandoos, "os", Enum.ToObject(Field(wandoos, "os").FieldType, 0));
            var capture = CallMain("CaptureLoadoutPresetDefinition", Get(policy, "ExecutionPreset"));
            if (kind == "reviewed-dormant")
                Throws(() => CallMain("ReplayGlobalPreset", policy, Get(policy, "Preset"), capture), "Wandoos OS", "reviewed dormant WAN binds OS during ordinary replay");
            else
                Equal(!blocked, Get(CallMain("ReplayGlobalPreset", policy, Get(policy, "Preset"), capture), "Feasible"), kind + " ordinary replay applies the exact OS dependency");
            if (blocked)
            {
                Throws(() => CallMain("ImportExecutionSelection", saved, null), "AUG/OS is unavailable", kind + " import rejects changed relevant OS");
                Throws(() => CallMain("PreviewExecutionRebind", JsonParse("{}")), "AUG/OS is unavailable", kind + " rebind cannot bypass relevant OS");
            }
            else
            {
                var imported = CallMain("ImportExecutionSelection", saved, null);
                Equal(2, Get(Get(Get(imported, "Candidate"), "Activities"), "Os"), kind + " import preserves acquired OS2 without binding unrelated installed OS0");
                Equal(true, Get(CallMain("ReplayGlobalPreset", imported, Get(imported, "Preset"), capture), "Feasible"), kind + " imported replay agrees with ordinary replay");
                var preview = CallMain("PreviewExecutionRebind", JsonParse("{}"));
                CallMain("ApplyExecutionRebind", ExecutionReviewApplyArgs(preview));
                Check(ReferenceEquals(receipt, Get(MainInstance, "_globalPresetReceipt")), kind + " actual rebind preserves still-owned live setup");
                var rebound = Get(Get(MainInstance, "_globalPresetReceipt"), "Policy");
                Equal(Get(policy, "Acquisition"), Get(rebound, "Acquisition"), kind + " actual rebind retains exact acquisition");
                Equal(2, Get(Get(Get(rebound, "Candidate"), "Activities"), "Os"), kind + " actual rebind does not replace acquired OS choice");
            }
            Equal(0, Convert.ToInt32(Get(wandoos, "os")), kind + " review never changes current native OS0");
            Equal(writes, ResourceTransfers, kind + " replay/import/rebind perform no native allocation writes");
            Equal(swaps, Get(typeof(Program), "Swaps"), kind + " replay/import/rebind perform no gear writes");
            Equal(searches, PresetSearches, kind + " replay/import/rebind perform no replacement search");
        }
    }

    private static void CheckCompiledForeignResourceMutation()
    {
        var plan = SetupCommitBudget();
        // Leave native growth headroom before acquiring the policy so the later
        // healthy-lane assertion does not depend on over-cap fixture resources.
        var cap = Convert.ToInt64(Invoke(Character, "totalCapEnergy"));
        Check(cap > 200, "foreign callback fixture has native Energy growth headroom");
        Set(Character, "curEnergy", cap - 200); Set(Character, "idleEnergy", cap - 200);
        CallMain("RunCustomPlanAutomation", plan);
        var receipt = Get(MainInstance, "_globalPresetReceipt");
        Check(receipt != null, "compiled foreign callback test starts from acquired native funding");
        var energy = ((IList)Get(Get(Character, "NGU"), "skills"))[0];
        var at = (long[])Get(Get(Character, "advancedTraining"), "energy");
        Check(at[0] > 0, "foreign callback fixture has actual AT funding to reconcile");
        at[0]--; Set(Character, "idleEnergy", (long)Get(Character, "idleEnergy") + 1);
        var magic = ((IList)Get(Get(Character, "NGU"), "magicSkills"))[0];
        var beforeMagic = (long)Get(magic, "magic"); var reached = false;
        AtRefreshObserver = () => {
            reached = true; AtRefreshObserver = null;
            Set(magic, "magic", beforeMagic - 1); Set(Get(Character, "magic"), "idleMagic", (long)Get(Get(Character, "magic"), "idleMagic") + 1);
        };
        FailAtRefresh = true;
        try { Set(MainInstance, "_customLastQuickResourceRefresh", DateTime.MinValue); CallMain("ReconcileCustomResources", plan); }
        finally { AtRefreshObserver = null; FailAtRefresh = false; }
        Equal(true, reached, "foreign control actually mutates native allocations inside rendering");
        Equal("ForeignConflict", Get(Get(MainType, "_presetResourceOutcome"), "Disposition").ToString(), "compiled resource actuator distinguishes foreign callback from a render-only exception");
        Equal(2, Get(Get(MainType, "_presetResourceOutcome"), "AffectedLanes"), "foreign Magic callback does not claim Energy or R3 interference");
        Equal(beforeMagic - 1, Get(magic, "magic"), "foreign native allocation is preserved, never silently adopted or repaired");
        Equal(false, Get(Get(MainInstance, "_protectedScheduler"), "_armed"), "foreign-dependent application never earns protected credit");
        var writes = ResourceTransfers; var searches = PresetSearches;
        Set(MainInstance, "_customLastQuickResourceRefresh", DateTime.MinValue); CallMain("ReconcileCustomResources", plan);
        Equal(writes, ResourceTransfers, "unresolved foreign operation is never replayed"); Equal(searches, PresetSearches, "foreign callback never authorizes a replacement search");
        Check(ReferenceEquals(receipt, Get(MainInstance, "_globalPresetReceipt")), "a scoped Magic conflict preserves the acquired policy for later healthy work");
        // Natural pool growth is not a manual edit to an owned destination. The
        // current Energy policy must fund it while preserving the foreign Magic edit.
        var beforeEnergy = (long)Get(energy, "energy");
        Set(Character, "curEnergy", (long)Get(Character, "curEnergy") + 100);
        Set(Character, "idleEnergy", (long)Get(Character, "idleEnergy") + 100);
        Set(MainInstance, "_customLastQuickResourceRefresh", DateTime.MinValue); CallMain("ReconcileCustomResources", plan);
        Check((long)Get(energy, "energy") > beforeEnergy, "healthy Energy continues from actual pool growth after Magic conflict"
            + "; before=" + beforeEnergy + "; after=" + Get(energy, "energy")
            + "; pool=" + Get(Character, "curEnergy") + "; idle=" + Get(Character, "idleEnergy")
            + "; cap=" + Invoke(Character, "totalCapEnergy")
            + "; execution=" + CallMain("GetResourceCoordinationRuntime")
            + "; waiting=" + Get(MainInstance, "_globalPresetWaitingReason")
            + "; failure=" + Get(MainInstance, "_globalPresetFailure"));
        Equal(beforeMagic - 1, Get(magic, "magic"), "healthy continuation never repairs or adopts foreign Magic");
        Equal(2, Get(Get(MainType, "_presetResourceOutcome"), "AffectedLanes"), "continuation retains only Magic uncertainty");
        Equal(1, Get(Get(MainType, "_presetResourceOutcome"), "VerifiedLanes"), "continuation publishes verified Energy separately");
        Equal(false, Get(Get(MainInstance, "_protectedScheduler"), "_armed"), "partial lane verification never admits full-stage credit");
        Equal(searches, PresetSearches, "scoped continuation performs no replacement acquisition");
    }

    private static void CheckCompiledSupportMutationOutcomes()
    {
        SetupResourceCoordination(false, true, "none");
        var rows = (IList)Get(Get(Character, "diggers"), "diggers");
        Set(rows[0], "maxLevel", 4L); Set(rows[11], "maxLevel", 2L); var wanted = Selection(new int[0], 0, 4);
        FailPodRefresh = true;
        Throws(() => CallMain("ApplyPresetDesiredSupport", wanted, 1d, false), "fixture", "partial native support submission surfaces a typed retryable result");
        FailPodRefresh = false;
        Equal("KnownPartial", Get(Get(MainInstance, "_presetSupportOutcome"), "Disposition").ToString(), "owned support partial is not a permanent unknown-outcome latch");
        CallMain("ApplyPresetDesiredSupport", wanted, 1d, false);
        Equal("AppliedVerified", Get(Get(MainInstance, "_presetSupportOutcome"), "Disposition").ToString(), "known partial converges to exact desired support");
        Equal("|0:4", Property(Property(CallMain("CaptureSupportState"), "Selection"), "StableKey"), "support retry uses the exact candidate, not rebudgeted or historical support");
        var calls = PodRefreshCalls; CallMain("ApplyPresetDesiredSupport", wanted, 1d, false);
        Equal(calls, PodRefreshCalls, "unchanged desired support causes no native clears, inputs or refreshes");

        var beards = Selection(new[] { 3 }); FailBeardRefresh = true;
        try { CallMain("ApplyPresetDesiredSupport", beards, 1d, false); }
        finally { FailBeardRefresh = false; }
        Equal("ModelAppliedRenderFailed", Get(Get(MainInstance, "_presetSupportOutcome"), "Disposition").ToString(), "completed support model distinguishes rendering failure");
        var mutation = Get(MainInstance, "_supportMutation");
        CallMain("ApplyPresetDesiredSupport", beards, 1d, false);
        Check(ReferenceEquals(mutation, Get(MainInstance, "_supportMutation")), "support presentation retry reuses bounded evidence instead of clearing and reapplying the model");
        Equal(false, Get(MainInstance, "_presetSupportPresentationPending"), "separate support rendering retry completes");

        ForeignPodRefresh = true;
        try { Throws(() => CallMain("ApplyPresetDesiredSupport", wanted, 1d, false), "foreign", "foreign native callback cannot use the known-partial retry path"); }
        finally { ForeignPodRefresh = false; }
        Equal("ForeignConflict", Get(Get(MainInstance, "_presetSupportOutcome"), "Disposition").ToString(), "foreign support callback remains an unsafe dependent-domain conflict");
    }

    private static void CheckCompiledSupportContinuation()
    {
        var plan = SetupCommitBudget(); var stage = ((Array)Get(plan, "Stages")).GetValue(0);
        var library = Get(Get(MainType, "CustomPlanStore"), "<PresetLibrary>k__BackingField");
        var root = Invoke(library, "ReadExternalLibrary"); var id = (string)Get(stage, "PresetId");
        var row = ((IEnumerable)GetChildren(Json(root, "Presets"))).Cast<object>().Single(x => Property(Json(x, "Id"), "Value").Equals(id));
        SetJson(row, "DiggerWeights", JsonParse("[1,0,0,0,0,0,0,0,0,0,0,0]"));
        Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
        var rows = (IList)Get(Get(Character, "diggers"), "diggers");
        Set(rows[0], "maxLevel", 4L); ((IList)Get(Get(Character, "allDiggers"), "baseGPSDrain"))[0] = 10d;
        CallMain("GetProtectedStage", plan); var scheduler = Get(MainInstance, "_protectedScheduler");
        var entry = Property(scheduler, "Entry"); var deadline = Property(scheduler, "ExpectedEndSeconds");
        var searches = PresetSearches; FailPodRefresh = true;
        try { CallMain("RunCustomPlanAutomation", plan); }
        finally { FailPodRefresh = false; }
        Equal("KnownPartial", Get(Get(MainInstance, "_presetSupportOutcome"), "Disposition").ToString(), "scheduled acquisition records its own exact support partial");
        Equal(null, Get(MainInstance, "_presetExecutionBlock"), "known support partial never installs an unknown/foreign execution block");
        Equal(false, Get(scheduler, "_armed"), "partial support cannot credit protected work");
        Check(Get(MainInstance, "_globalPresetApplyingPolicy") != null, "selected setup decisions survive partial support without granting new search authority");
        Equal(searches + 1, PresetSearches, "first acquisition performs exactly one setup search");
        Set(MainInstance, "_globalPresetRetryAt", 0d); CallMain("RunCustomPlanAutomation", plan);
        Equal(searches + 1, PresetSearches, "partial support retry replays the same acquired setup, never searches replacement gear");
        Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), "support continuation verifies the actual full desired setup and allocations");
        Equal(true, Get(scheduler, "_armed"), "only completed verified continuation arms protected credit");
        Equal(entry, Property(scheduler, "Entry"), "support continuation retains scheduler commitment identity");
        Equal(deadline, Property(scheduler, "ExpectedEndSeconds"), "support continuation never renews elapsed deadlines");
    }

    private static void CheckSelectedSetupRouteAdmission()
    {
        foreach (var missingSelector in new[] { true, false })
        {
            var plan = SetupCommitBudget();
            var controller = Get(Character, "adventureController");
            Set(Get(Character, "adventure"), "zone", 0);
            if (missingSelector) Set(controller, "zoneSelector", null);
            var entries = AdventureZoneEntries;
            PrepareSupport();
            Equal(0, Get(Get(Character, "adventure"), "zone"), "support preparation does not reset an active timeline route");
            Equal(entries, AdventureZoneEntries, "support preparation never reenters the native safe route");
            Equal(missingSelector, Get(controller, "zoneSelector") == null, "support preparation does not repair deliberate missing-selector evidence");
            // PrepareSupport supplies support-only defaults (including a locked
            // Adventure button). This case must reach the route guard, not stop at
            // the unrelated AT-Adventure readiness gate.
            Set(Get(Get(Character, "buttons"), "adventure"), "m_Interactable", true);
            CallMain("GetProtectedStage", plan);
            Equal(null, Get(MainInstance, "_protectedSelectedPolicyIssue"), "fresh route negative has no reload quarantine");
            var head = Get(Get(Character, "inventory"), "head");
            var swaps = Get(typeof(Program), "Swaps"); var transfers = ResourceTransfers; var pods = PodRefreshCalls;
            var captures = PresetCaptures;
            var targets = ((long[])Get(Get(Character, "advancedTraining"), "levelTarget")).ToArray();
            TracePresetAdmission(plan, "route/missingSelector=" + missingSelector + "/before");
            CallMain("RunCustomPlanAutomation", plan);
            TracePresetAdmission(plan, "route/missingSelector=" + missingSelector + "/after");
            var diagnostic = (string)Get(MainInstance, "_globalPresetReconcileException");
            Console.WriteLine("Selected setup route admission/missingSelector=" + missingSelector + ": " + diagnostic);
            Check(diagnostic != null && diagnostic.Contains(missingSelector ? "route retirement controllers are unavailable" : "current adventure owner")
                && !diagnostic.Contains("NullReferenceException"), "route dependency rejects explicitly with retained exact diagnostic");
            Equal(null, Get(MainInstance, "_globalPresetApplyingPolicy"), "route dependency rejects before policy application commitment");
            Equal(null, Get(MainInstance, "_globalPresetReceipt"), "route dependency cannot grant an application receipt");
            var path = (string)MainType.GetProperty("ProtectedCheckpointPath", All).GetValue(MainInstance);
            Check(!System.IO.File.Exists(path) || !System.IO.File.ReadAllText(path).Contains("\"selectedPolicy\""),
                "route dependency persists no selected acquisition");
            Check(ReferenceEquals(head, Get(Get(Character, "inventory"), "head")), "blocked route retains its supporting native gear");
            Equal(swaps, Get(typeof(Program), "Swaps"), "blocked route writes no gear");
            Equal(transfers, ResourceTransfers, "blocked route writes no resources");
            Equal(pods, PodRefreshCalls, "blocked route writes no support");
            Equal(captures, PresetCaptures, "known missing route ownership rejects before broad planning capture");
            Equal(entries, AdventureZoneEntries, "blocked route performs no native retirement or resume");
            Check(targets.SequenceEqual((long[])Get(Get(Character, "advancedTraining"), "levelTarget")), "blocked route writes no native targets");
            Equal(0, Get(Get(Character, "adventure"), "zone"), "unknown active route is never adopted or retired");
            Equal(false, Get(Get(MainInstance, "_protectedScheduler"), "_armed"), "blocked route earns no protected credit");
        }
    }

    private static void CheckSelectedSetupSuccessorTimeline()
    {
        var plan = SetupCommitBudget();
        var training = Get(Character, "training");
        ((long[])Property(training, "attackEnergy"))[0] = 7L;
        Set(Character, "idleEnergy", (long)Get(Character, "idleEnergy") - 7L);
        CallMain("RunCustomPlanAutomation", plan);
        var receipt = Get(MainInstance, "_globalPresetReceipt");
        Check(receipt != null, "coupled setup timeline acquires an actual funded baseline");
        var execution = Get(receipt, "Execution");
        var inventory = Get(Character, "inventory"); var items = (IList)Get(inventory, "inventory");
        var baseline = Get(inventory, "head"); var temporary = items[1];
        Set(temporary, "id", 551); Set(temporary, "type", Get(baseline, "type"));
        Set(temporary, "bossRequired", 1); Set(temporary, "removable", true);
        Set(temporary, "curAttack", 2000000f); Set(temporary, "curDefense", 2000000f);
        Set(temporary, "capAttack", 2000000f); Set(temporary, "capDefense", 2000000f);
        var rows = (IList)Get(Get(Character, "diggers"), "diggers"); Set(rows[0], "maxLevel", 4L);
        var manager = Plugin.GetType("NGUInjector.Managers.LoadoutManager", true);
        var requirementsType = Plugin.GetType("NGUInjector.Main+SelectedSetupRequirements", true);
        var footprintType = Plugin.GetType("NGUInjector.Managers.GearRecallFootprint", true);
        Func<object, object, string, object> requirements = (head, support, owner) => {
            var map = (Array)Invoke(manager, "CaptureExactLoadout");
            var oldHead = map.Cast<object>().Single(x => (int)Property(x, "slot") == -1);
            var desiredHead = Activator.CreateInstance(oldHead.GetType(), true);
            desiredHead.GetType().GetProperty("slot", All).SetValue(desiredHead, -1);
            desiredHead.GetType().GetProperty("id", All).SetValue(desiredHead, Get(head, "id"));
            desiredHead.GetType().GetProperty("equipment", All).SetValue(desiredHead, head);
            var index = Array.IndexOf(map.Cast<object>().ToArray(), oldHead); map.SetValue(desiredHead, index);
            var wanted = Activator.CreateInstance(requirementsType, true);
            Set(wanted, "Owner", owner); Set(wanted, "Gear", map);
            var coordinator = MainInstance;
            Set(wanted, "Authorized", (Func<bool>)(() => ReferenceEquals(Get(MainType, "reference"), coordinator)));
            Set(wanted, "Recall", Enum.Parse(footprintType, "Energy, Magic"));
            Set(wanted, "Support", support); Set(wanted, "Budget", 1d); Set(wanted, "ChangeSupport", true);
            Set(wanted, "RouteRetirementAuthorized", (Func<bool>)(() => true)); // Explicit fixture claim takeover, not inferred native ownership.
            return wanted;
        };
        var r3 = Get(Character, "res3"); var r3Current = Get(r3, "curRes3"); var r3Idle = Get(r3, "idleRes3");
        var searches = PresetSearches; var observedHeads = new System.Collections.Generic.List<int>();
        GearRenderObserver = () => {
            Equal(-1, Get(Get(Character, "adventure"), "zone"), "supporting gear changes only behind an observed safe route barrier");
            observedHeads.Add((int)Get(Get(inventory, "head"), "id"));
        };
        try
        {
            CallMain("BeginPresetSupportAcquisition", "timeline", "temporary");
            var temporaryIntent = requirements(temporary, Selection(new[] { 3 }, 0, 4), "temporary");
            Set(Get(Character, "adventure"), "zone", 0);
            FailPodRefresh = true;
            try { Throws(() => CallMain("ApplySelectedSetup", temporaryIntent), "fixture", "committed temporary gear precedes recoverable native support failure"); }
            finally { FailPodRefresh = false; }
            Check(ReferenceEquals(temporary, Get(inventory, "head")), "support failure never rolls back committed temporary gear");
            Equal(false, Get(Get(MainInstance, "_protectedScheduler"), "_armed"), "partial coupled setup admits no stage credit");
            var swaps = Get(typeof(Program), "Swaps");
            CallMain("ApplySelectedSetup", temporaryIntent);
            Equal(swaps, Get(typeof(Program), "Swaps"), "support continuation never repeats the committed native gear write");
            // Claim removal resolves a changed current successor, not the old baseline.
            var successor = items[1]; Set(successor, "id", 552);
            CallMain("BeginPresetSupportAcquisition", "timeline", "current-successor");
            var successorIntent = requirements(successor, Selection(new[] { 3 }, 0, 2), "current-successor");
            CallMain("ApplySelectedSetup", successorIntent);
            Check(ReferenceEquals(successor, Get(inventory, "head")), "removed temporary claim goes directly to changed current successor");
            Equal("3|0:2", Property(Property(CallMain("CaptureSupportState"), "Selection"), "StableKey"), "successor applies exact current support, not historical baseline");
            swaps = Get(typeof(Program), "Swaps"); var pods = PodRefreshCalls;
            CallMain("ApplySelectedSetup", successorIntent);
            Equal(swaps, Get(typeof(Program), "Swaps"), "verified successor repeats no model gear writes");
            Equal(pods, PodRefreshCalls, "verified successor repeats no model support writes");
            CallMain("ApplyLoadoutPresetAllocations", execution); CallMain("VerifyLoadoutPresetAllocations", execution);
            Equal(7L, ((long[])Property(training, "attackEnergy"))[0], "coupled transitions preserve basic training");
            Equal(r3Current, Get(r3, "curRes3"), "coupled transitions preserve unmanaged R3 current pool");
            Equal(r3Idle, Get(r3, "idleRes3"), "coupled transitions preserve unmanaged R3 idle pool");
            Check(observedHeads.Contains(551) && observedHeads.Contains(552) && !observedHeads.Contains(550), "native timeline has no historical gear detour");
            Equal(searches, PresetSearches, "actuator continuation/removal performs no replacement search");
            Equal(false, Get(Get(MainInstance, "_protectedScheduler"), "_armed"), "actuator verification alone never admits dependent stage credit");
        }
        finally { GearRenderObserver = null; FailPodRefresh = false; }
    }

    private static void CheckCombatPrepSignedSupportRuntime()
    {
        SetupResourceCoordination(false, true, "none");
        var all = Get(Character, "allDiggers"); var rows = (IList)Get(Get(Character, "diggers"), "diggers");
        foreach (var beard in (IList)Get(Get(Character, "beards"), "beards"))
        { Set(beard, "beardLevel", 0L); Set(beard, "permLevel", 0L); }
        Set(rows[0], "maxLevel", 20L); Set(rows[0], "curLevel", 7L);
        ((IList)Get(all, "baseGPSDrain"))[0] = 10d; ((IList)Get(all, "consumesGPS"))[0] = true;
        Set(Get(Character, "machine"), "realBaseGold", 1000d); Invoke(all, "activateDigger", 0);
        Set(Get(Character, "machine"), "realBaseGold", 400d);
        var context = CallMain("CaptureSupportContext"); var observed = CallMain("ObserveCombatPrepSupport", context);
        Equal(false, Get(observed, "Feasible"), "overspending live support is an observation, not an affordable desired policy");
        var json = CallMain("CombatPrepSupportJson", context, observed, 1d, false);
        Equal(-240d, Property(Json(json, "netGoldPerSecond"), "AsDouble"), "Combat Prep support JSON retains signed finite native net GPS");
        var state = JsonParse("{}"); var calls = PodRefreshCalls; var writes = ResourceTransfers;
        CallMain("CacheCombatPrepActual", state);
        Equal(-240d, Property(Json(Json(state, "actualSupport"), "netGoldPerSecond"), "AsDouble"), "actual support reporting is present even when desired feasibility is false");
        Equal(calls, PodRefreshCalls, "signed support reporting never mutates native support");
        Equal(writes, ResourceTransfers, "signed support reporting never mutates resource accounting");
    }
}
