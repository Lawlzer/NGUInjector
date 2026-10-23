using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

public static partial class PresetIntegrationChecks
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static Assembly Plugin, Game;
    private static Type MainType;
    private static object Character, Controller, MainInstance;
    private static int Assertions;
    private static int PresetSearches;
    private static int PresetCaptures;
    private static Action<Stopwatch> PresetSearchObserver, PresetCaptureObserver, PresetPlanCompletedObserver;
    private static Action GearRenderObserver, RecallObserver, CaptureNativeObserver;
    private static int ResourceTransfers; // Counts actual SetAmount model writes, including exact releases.
    private static int AutoBoostCalls;
    private static Action AutoBoostAction;
    private static Action<object, object> PresetEvaluationObserver;
    private static bool BlockPresetLevelGain;
    private static bool AllowPresetSettingsSave;
    private static int PresetSettingsWrites;
    private static bool FailBeardRefresh;
    private static bool FailAtRefresh;
    private static bool FailAtTargetText;
    private static Action AtRefreshObserver;
    private static Action BeardRefreshInterference;
    private static bool FailPodRefresh, ForeignPodRefresh, DropGoldPodRefresh;
    private static int PodRefreshCalls;
    private static int NativePodTextWrites;
    private static int NativeNumberFlushCalls;
    private static int LegacyAdventureRoutes;
    private static int EquipmentInvalidations, AdventureZoneEntries;
    private static bool FailAdventureZoneEntry;
    private static Action<object> PodInterference;
    private static Action<object, string> InputInterference;
    private static float PresetRespawnSeconds = 4;
    private static readonly Dictionary<string, float> AdventureInputs = new Dictionary<string, float>();
    private static readonly Dictionary<string, float[]> NativeTitanRequirements = new Dictionary<string, float[]>();

    public static int Main(string[] args)
    {
        string temp = null;
        try
        {
            var managed = Path.GetFullPath(args[1]);
            AppDomain.CurrentDomain.AssemblyResolve += (sender, request) => {
                if (new AssemblyName(request.Name).Name == "Assembly-CSharp") return Game;
                if (new AssemblyName(request.Name).Name == "UnityEngine.UI") return LoadUi(Path.Combine(managed, "UnityEngine.UI.dll"));
                var path = Path.Combine(managed, new AssemblyName(request.Name).Name + ".dll");
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };
            Game = LoadGame(Path.Combine(managed, "Assembly-CSharp.dll"));
            Plugin = LoadPlugin(args[0]); MainType = Plugin.GetType("NGUInjector.Main", true);
            temp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "opencode", "PresetIntegration-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            // Reuse the established offline fixture's object construction, not copied preset validators.
            Set(typeof(Program), "Game", Game); Set(typeof(Program), "Plugin", Plugin); Set(typeof(Program), "MainType", MainType); Set(typeof(Program), "Temp", temp);
            if (args.Contains("--preset-readiness"))
            {
                CheckPresetReadinessAndProgress();
                Console.WriteLine("PASS: " + Assertions + " assertions against compiled preset readiness, retry, and progress telemetry. No live game actions.");
                return 0;
            }
            Invoke(typeof(Program), "Setup");
            Character = Get(typeof(Program), "Character"); Controller = Get(typeof(Program), "Controller"); MainInstance = Get(typeof(Program), "MainInstance");
            PrepareSupport();
            if (args.Contains("--retained-farming-rebind"))
            {
                CheckExecutionRetainedFarmingRebind();
                Console.WriteLine("PASS: " + Assertions + " retained farming rebind assertions. No live game actions.");
                return 0;
            }
            if (args.Contains("--execution-core"))
            {
                CheckExecutionCoreCutover();
                CheckPresetCommitBudget();
                Console.WriteLine("PASS: " + Assertions + " native execution-core assertions. No live game actions.");
                return 0;
            }
            if (args.Contains("--route-executor-cutover"))
            {
                CheckRouteExecutorCutover();
                Console.WriteLine("PASS: " + Assertions + " native route executor assertions. No live game actions.");
                return 0;
            }
            if (args.Contains("--manual-titan-capacity"))
            {
                CheckManualTitanCapacityTransition();
                Console.WriteLine("PASS: " + Assertions + " native manual-titan capacity assertions. No live game actions.");
                return 0;
            }
            if (args.Contains("--capacity-lifecycle"))
            {
                CheckCapacityLifecycleRegressions();
                Console.WriteLine("PASS: " + Assertions + " native capacity lifecycle assertions. No live game actions.");
                return 0;
            }
            if (args.Contains("--finite-action-cutover"))
            {
                CheckFiniteActionCutover();
                Console.WriteLine("PASS: " + Assertions + " native finite-action assertions. No live game actions.");
                return 0;
            }
            if (args.Contains("--preparation-ingress-entry"))
            {
                CheckPreparationIngressAndEntryRegressions();
                Console.WriteLine("PASS: " + Assertions + " native preparation ingress/entry assertions. No live game actions.");
                return 0;
            }
            if (args.Contains("--execution-persistence"))
            {
                CheckExecutionPersistence();
                Console.WriteLine("PASS: " + Assertions + " execution persistence assertions. No live game actions.");
                return 0;
            }
            if (args.Contains("--execution-foundation"))
            {
                CheckExecutionFoundation();
                Console.WriteLine("PASS: " + Assertions + " execution foundation assertions. No live game actions.");
                return 0;
            }
            if (args.Contains("--settings-json"))
            {
                CheckSettingsJsonBoundary();
                CheckGlobalPresets();
                Console.WriteLine("PASS: " + Assertions + " compiled settings JSON, transactional persistence and global-preset assertions. No live game actions.");
                return 0;
            }
            if (args.Contains("--adventure-settings-isolation"))
            {
                CheckAdventureSettingsIsolation();
                Console.WriteLine("PASS: " + Assertions + " offline adventure settings isolation assertions. No live game actions.");
                return 0;
            }
            if (args.Contains("--scheduled-number-observation"))
            {
                CheckActiveAutomaticNumberObservation();
                Console.WriteLine("PASS: " + Assertions + " active automatic-executor NUMBER callback assertions. No live game actions.");
                return 0;
            }
            if (args.Contains("--stage-policy-edits"))
            {
                CheckInlineStagePolicyEdits();
                Console.WriteLine("PASS: " + Assertions + " compiled explicit inline policy-edit assertions. No live game actions.");
                return 0;
            }
            if (args.Contains("--scheduled-route-handoff"))
            {
                CheckScheduledRouteHandoff();
                Console.WriteLine("PASS: " + Assertions + " scheduled preset farming-route handoff assertions. No live game actions.");
                return 0;
            }
            if (args.Contains("--inline-stage-setup"))
            {
                CheckInlineStageSetupRegressions();
                Console.WriteLine("PASS: " + Assertions + " compiled inline-stage selection, reconciliation and verification assertions. No live game actions.");
                return 0;
            }
            if (args.Contains("--preset-final-review"))
            {
                CheckPresetFinalReviewRegressions();
                Console.WriteLine("PASS: " + Assertions + " compiled native-target authority and rotation-retry assertions. No live game actions.");
                return 0;
            }
            if (args.Contains("--preset-review-retries"))
            {
                CheckPresetReviewRetryRegressions();
                Console.WriteLine("PASS: " + Assertions + " assertions against compiled preset retry ownership regressions. No live game actions.");
                return 0;
            }
            if (args.Contains("--adventure-goal-preference"))
            {
                CheckAdventureGoalPreference();
                Console.WriteLine("PASS: " + Assertions + " assertions against compiled preset Adventure Goal preference preservation. No live game actions.");
                return 0;
            }
            if (args.Contains("--reviewed-executor"))
            {
                CheckReviewedLaneExecution();
                Console.WriteLine("PASS: " + Assertions + " assertions against compiled reviewed lane execution and native accounting. No live game actions.");
                return 0;
            }
            if (args.Contains("--shared-support-transition"))
            {
                CheckSharedSupportTransition();
                Console.WriteLine("PASS: " + Assertions + " shared support/native route retirement assertions. No live game actions.");
                return 0;
            }
            if (args.Contains("--support-overspend"))
            {
                CheckSupportOverspend();
                Console.WriteLine("PASS: " + Assertions + " assertions against compiled support replacement from native overspending states. No live game actions.");
                return 0;
            }
            if (args.Contains("--preset-commit-budget"))
            {
                CheckPresetCommitBudget();
                Console.WriteLine("PASS: " + Assertions + " assertions against compiled preset preparation deadlines and non-interruptible commits. No live game actions.");
                return 0;
            }
            if (args.Contains("--capacity-objectives"))
            {
                CheckCapacityObjectives();
                CheckCapacityPolicyLifecycle();
                Console.WriteLine("PASS: " + Assertions + " assertions against compiled capacity capture/preview, application, auto-update lifecycle and native getters. No live game actions.");
                return 0;
            }
            if (args.Contains("--manual-overlay"))
            {
                CheckManualAdventureOverlay();
                Console.WriteLine("PASS: " + Assertions + " assertions against compiled manual-adventure preset overlay ownership and refresh. No live game actions.");
                return 0;
            }
            if (args.Contains("--adventure-dynamic"))
            {
                CheckAdventureDynamic();
                Console.WriteLine("PASS: " + Assertions + " assertions against compiled adventure-dynamic capture, preview, acquisition, refresh and routing ownership. No live game actions.");
                return 0;
            }
            if (args.Contains("--preset-boost"))
            {
                CheckGlobalPresetAutoBoost();
                Console.WriteLine("PASS: " + Assertions + " assertions against compiled preset auto-boost ownership and invalidation. No live game actions.");
                return 0;
            }
            if (args.Contains("--iron-pill"))
            {
                CheckScheduledIronPillReservation();
                Console.WriteLine("PASS: " + Assertions + " assertions against compiled scheduled preset Iron Pill reservation and restoration. No live game actions.");
                return 0;
            }
            if (args.Contains("--global-ngu-weights"))
            {
                CheckGlobalNguWeights();
                Console.WriteLine("PASS: " + Assertions + " assertions against compiled global NGU capture, preview, exclusion, cache and ownership lifecycle. No live game actions.");
                return 0;
            }
            if (args.Contains("--resource-coordination"))
            {
                CheckResourceCoordination();
                Console.WriteLine("PASS: " + Assertions + " assertions against compiled resource coordination. No live game actions.");
                return 0;
            }
            CheckNativeAllocationShapes();
            if (args.Contains("--allocation-shapes"))
            {
                Console.WriteLine("PASS: " + Assertions + " assertions against compiled preset allocation verification with native backing shapes. No live game actions.");
                return 0;
            }
            CheckBeardCapture(); CheckDropCapture(); CheckNguCapture(); CheckItopodCapture(); CheckFingerprint(); CheckPreviewAndApply(); CheckPassiveProgressBaseline(); CheckExactBlood(); CheckSupportInvalidation(); CheckSharedSupport(); CheckInactiveSupport(); CheckClearJournal(); CheckPodDrafts(); CheckGearPreflight(); CheckLazyItopod(); CheckAdventureFloatParity(); CheckSettingsJsonBoundary(); CheckGlobalPresets(); CheckGlobalBeardRotation(); CheckPresetForecastHorizons(); CheckAdventureStatObjectives(); CheckGlobalStatPolicyLifecycle(); CheckPresetReadinessAndProgress(); CheckWeightedStageTargets(); CheckNativeTitanRequirements(); CheckGlobalNguWeights(); CheckScheduledIronPillReservation(); CheckGlobalPresetAutoBoost(); CheckAdventureDynamic(); CheckManualAdventureOverlay(); CheckResourceCoordination();
            CheckCapacityObjectives();
            CheckCapacityPolicyLifecycle();
            CheckPresetCommitBudget();
            CheckSupportOverspend();
            CheckReviewedLaneExecution();
            CheckAdventureGoalPreference();
            CheckPresetReviewRetryRegressions();
            CheckPresetFinalReviewRegressions();
            CheckInlineStageSetupRegressions();
            CheckActiveAutomaticNumberObservation();
            CheckScheduledRouteHandoff();
            CheckAdventureSettingsIsolation();
            CheckExecutionFoundation();
            CheckExecutionPersistence();
            CheckExecutionCoreCutover();
            CheckRouteExecutorCutover();
            CheckFiniteActionCutover();
            Console.WriteLine("PASS: " + Assertions + " assertions against compiled preset adapters and native game getters. No live game actions.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAILED after " + Assertions + " native assertions (not a pass count)."); Console.Error.WriteLine(error); return 1; }
        finally { if (temp != null && Directory.Exists(temp)) Directory.Delete(temp, true); }
    }

    public static void CountPresetSearch(Stopwatch clock) { PresetSearches++; PresetSearchObserver?.Invoke(clock); }
    public static void CountPresetCapture(Stopwatch clock) { PresetCaptures++; PresetCaptureObserver?.Invoke(clock); }
    public static void ObservePresetPlanCompleted(Stopwatch clock) { PresetPlanCompletedObserver?.Invoke(clock); }
    public static void CountResourceTransfer() { ResourceTransfers++; }
    public static void CountLegacyAdventureRoute() { LegacyAdventureRoutes++; }
    public static void CountEquipmentInvalidation() { EquipmentInvalidations++; }
    public static void CountAdventureZoneEntry()
    {
        AdventureZoneEntries++;
        if (FailAdventureZoneEntry) throw new InvalidOperationException("fixture native route retirement failure");
    }
    private static Action<int> AdventureZoneEntryObserver;
    private static Action<int> AdventureZoneEntryCompletedObserver;
    public static void ObserveAdventureZoneEntry(int zone) { AdventureZoneEntryObserver?.Invoke(zone); }
    public static void ObserveAdventureZoneEntryCompleted(int zone) { AdventureZoneEntryCompletedObserver?.Invoke(zone); }
    public static void CountNativeNumberFlush() { NativeNumberFlushCalls++; }
    public static bool PresetLevelGainBlocked() { return BlockPresetLevelGain; }
    public static void ObservePresetEvaluation(object candidate, object request) { PresetEvaluationObserver?.Invoke(candidate, request); }
    public static string AutoBoostFixture() { AutoBoostCalls++; AutoBoostAction?.Invoke(); return "fixture auto boost"; }
    private static Assembly LoadPlugin(string path)
    {
        using (var plugin = AssemblyDefinition.ReadAssembly(path))
        {
            Console.WriteLine("Production MVID: " + plugin.MainModule.Mvid);
            var logError = plugin.MainModule.Types.Single(x => x.FullName == "NGUInjector.Main").Methods.Single(x => x.Name == "LogError");
            var logEntry = logError.Body.Instructions[0];
            logError.Body.GetILProcessor().InsertBefore(logEntry, Instruction.Create(OpCodes.Ldarg_1));
            logError.Body.GetILProcessor().InsertBefore(logEntry, Instruction.Create(OpCodes.Call,
                plugin.MainModule.ImportReference(typeof(PresetIntegrationChecks).GetMethod("ObserveInlineError", All))));
            var selectGear = plugin.MainModule.Types.Single(x => x.FullName == "NGUInjector.Managers.GearOptimizer")
                .Methods.Single(x => x.Name == "Optimize");
            selectGear.Body.GetILProcessor().InsertBefore(selectGear.Body.Instructions[0], Instruction.Create(OpCodes.Call,
                plugin.MainModule.ImportReference(typeof(PresetIntegrationChecks).GetMethod("CountInlineGearSelection", All))));
            var transfer = plugin.MainModule.Types.Single(x => x.FullName == "NGUInjector.Managers.ResourceWriter")
                .Methods.Single(x => x.Name == "SetAmount");
            transfer.Body.GetILProcessor().InsertBefore(transfer.Body.Instructions[0], Instruction.Create(OpCodes.Call,
                plugin.MainModule.ImportReference(typeof(PresetIntegrationChecks).GetMethod("CountResourceTransfer", All))));
            var plan = plugin.MainModule.Types.Single(x => x.FullName == "NGUInjector.Managers.LoadoutPresetPlanner")
                .Methods.Single(x => x.Name == "Plan");
            var planEntry = plan.Body.Instructions[0];
            plan.Body.GetILProcessor().InsertBefore(planEntry, Instruction.Create(OpCodes.Ldarg, plan.Parameters[2]));
            plan.Body.GetILProcessor().InsertBefore(planEntry, Instruction.Create(OpCodes.Call,
                plugin.MainModule.ImportReference(typeof(PresetIntegrationChecks).GetMethod("CountPresetSearch", All))));
            foreach (var ret in plan.Body.Instructions.Where(x => x.OpCode == OpCodes.Ret).ToArray())
            {
                plan.Body.GetILProcessor().InsertBefore(ret, Instruction.Create(OpCodes.Ldarg, plan.Parameters[2]));
                plan.Body.GetILProcessor().InsertBefore(ret, Instruction.Create(OpCodes.Call,
                    plugin.MainModule.ImportReference(typeof(PresetIntegrationChecks).GetMethod("ObservePresetPlanCompleted", All))));
            }
            foreach (var capture in plugin.MainModule.Types.Single(x => x.FullName == "NGUInjector.Main").Methods
                .Where(x => x.Name == "CaptureLoadoutPresetDefinition" || x.Name == "CaptureLoadoutPresetRouteExecution"))
            {
                var captureEntry = capture.Body.Instructions[0];
                capture.Body.GetILProcessor().InsertBefore(captureEntry, Instruction.Create(OpCodes.Ldarg, capture.Parameters[1]));
                capture.Body.GetILProcessor().InsertBefore(captureEntry, Instruction.Create(OpCodes.Call,
                    plugin.MainModule.ImportReference(typeof(PresetIntegrationChecks).GetMethod("CountPresetCapture", All))));
            }
            var adventureRoute = plugin.MainModule.Types.Single(x => x.FullName == "NGUInjector.Main")
                .Methods.Single(x => x.Name == "TryRouteCustomAdventureGoal");
            adventureRoute.Body.GetILProcessor().InsertBefore(adventureRoute.Body.Instructions[0], Instruction.Create(OpCodes.Call,
                plugin.MainModule.ImportReference(typeof(PresetIntegrationChecks).GetMethod("CountLegacyAdventureRoute", All))));
            var invalidate = adventureRoute.DeclaringType.Methods.Single(x => x.Name == "InvalidateCustomEquipmentApplication");
            invalidate.Body.GetILProcessor().InsertBefore(invalidate.Body.Instructions[0], Instruction.Create(OpCodes.Call,
                plugin.MainModule.ImportReference(typeof(PresetIntegrationChecks).GetMethod("CountEquipmentInvalidation", All))));
            // Observe real replay results without replacing projection, search, or application code.
            var evaluate = plan.DeclaringType.Methods.Single(x => x.Name == "Evaluate");
            foreach (var ret in evaluate.Body.Instructions.Where(x => x.OpCode == OpCodes.Ret).ToArray())
            {
                var il = evaluate.Body.GetILProcessor(); ret.OpCode = OpCodes.Dup;
                var request = Instruction.Create(OpCodes.Ldarg_1); il.InsertAfter(ret, request);
                var observe = Instruction.Create(OpCodes.Call, plugin.MainModule.ImportReference(typeof(PresetIntegrationChecks).GetMethod("ObservePresetEvaluation", All)));
                il.InsertAfter(request, observe); il.InsertAfter(observe, Instruction.Create(OpCodes.Ret));
            }
            ConfigureSettingsJsonBoundary(plugin);
            typeof(PresetIntegrationChecks).GetMethod("ConfigureRouteCapacityPluginFixture", All)?.Invoke(null, new object[] { plugin });
            // Preserve the legacy save recorder; transactional ApplyPatch keeps its real file IO.
            var save = plugin.MainModule.Types.Single(x => x.FullName == "NGUInjector.SavedSettings")
                .Methods.Single(x => x.Name == "SaveSettings");
            save.Body = new Mono.Cecil.Cil.MethodBody(save);
            save.Body.GetILProcessor().Emit(OpCodes.Call, plugin.MainModule.ImportReference(typeof(PresetIntegrationChecks).GetMethod("PersistPresetSettings", All)));
            save.Body.GetILProcessor().Emit(OpCodes.Ret);
            // Isolate native inventory/UI work; the real Main ownership checks and finally block remain intact.
            var boost = plugin.MainModule.Types.Single(x => x.FullName == "NGUInjector.Managers.InventoryManager")
                .Methods.Single(x => x.Name == "ManageCustomAutoBoost");
            boost.Body = new Mono.Cecil.Cil.MethodBody(boost);
            boost.Body.GetILProcessor().Emit(OpCodes.Call, plugin.MainModule.ImportReference(typeof(PresetIntegrationChecks).GetMethod("AutoBoostFixture", All)));
            boost.Body.GetILProcessor().Emit(OpCodes.Ret);
            typeof(Program).GetMethod("ConfigureFiniteActionPluginFixture", All)?.Invoke(null, new object[] { plugin });
            ConfigureRetainedFarmingPluginFixture(plugin);
            using (var bytes = new MemoryStream()) { plugin.Write(bytes); return Assembly.Load(bytes.ToArray()); }
        }
    }

    public static void PersistPresetSettings()
    {
        if (MainType == null || Get(MainType, "Settings") == null || Get(Get(MainType, "Settings"), "_savePath") == null) return;
        if (!AllowPresetSettingsSave) throw new InvalidOperationException("Unexpected settings persistence in legacy preset fixture.");
        PresetSettingsWrites++;
    }

    private static void CheckGlobalBeardRotation()
    {
        Invoke(typeof(Program), "Setup");
        Character = Get(typeof(Program), "Character"); Controller = Get(typeof(Program), "Controller"); MainInstance = Get(typeof(Program), "MainInstance");
        PrepareSupport();
        Set(MainInstance, "_globalPresetPolicies", Activator.CreateInstance(Field(MainInstance, "_globalPresetPolicies").FieldType));
        Set(MainInstance, "_globalPresetBeardRandom", new Random(42));
        Set(Get(Character, "NGU"), "autoAdvance", false);
        ListField(Get(Character, "NGU"), "skills", 11); ListField(Get(Character, "NGU"), "magicSkills", 11);
        Set(Get(Get(Character, "buttons"), "ngu"), "m_Interactable", true);
        var beardRows = (IList)Get(Get(Character, "beards"), "beards");
        Set(beardRows[5], "beardLevel", 0L); Set(beardRows[5], "permLevel", 0L);
        var diggers = (IList)Get(Get(Character, "diggers"), "diggers");
        Set(diggers[4], "maxLevel", 4L);
        var allDiggers = Get(Character, "allDiggers");
        ((IList)Get(allDiggers, "baseGPSDrain"))[4] = 100d;
        ((IList)Get(allDiggers, "boostPerLevel"))[4] = .5d;
        ((IList)Get(allDiggers, "consumesGPS"))[4] = true;
        var settings = Get(MainType, "Settings");
        Set(settings, "_energyNguWeights", new[] { 1, 0, 0, 0, 0, 0, 0, 0, 0 });
        Set(settings, "_magicNguWeights", new int[7]);
        AllowPresetSettingsSave = true;
        Set(settings, "_savePath", Path.Combine((string)Get(MainType, "_dir"), "rotation-settings.json"));
        Set(settings, "_manualAdventureEnabled", false); Set(settings, "_customAdventureEnabled", false);
        var library = Get(Get(MainType, "CustomPlanStore"), "<PresetLibrary>k__BackingField");
        var root = Invoke(library, "ReadExternalLibrary");
        var definition = JsonParse("{\"Id\":\"rotation-fixture\",\"Name\":\"Rotation fixture\",\"Weights\":[1,0,0,0,0,0],\"SupportMode\":\"planned\",\"SwapSeconds\":0,\"BeardPriority\":[1,3,5],\"BeardRotation\":[6,0],\"DiggerWeights\":[0,0,0,0,1,0,0,0,0,0,0,0],\"EnergyNguWeights\":[1,0,0,0,0,0,0,0,0],\"MagicNguWeights\":[0,0,0,0,0,0,0]}");
        Invoke(Json(root, "Presets"), "Add", "", definition);
        var revision = Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
        var args = JsonParse("{\"presetId\":\"rotation-fixture\",\"libraryRevision\":\"" + revision + "\"}");
        var preview = CallMain("PreviewGlobalPreset", args);
        var initialSupport = Json(Json(Json(preview, "candidates"), 0), "support").ToString();
        Equal("[1,3,5,6]", Json(Json(Json(Json(preview, "candidates"), 0), "support"), "beards").ToString(), "initial preview follows required then ordered pool, no random roll");
        var apply = JsonParse("{\"presetId\":\"rotation-fixture\",\"libraryRevision\":\"" + revision + "\",\"previewId\":" + Json(preview, "previewId") + ",\"candidateKey\":\"current\"}");
        object[] invocation = { apply, null };
        // Simulate a pool change at the native support boundary, after cached preflight.
        BeardRefreshInterference = () => { BeardRefreshInterference = null; Set(Character, "curEnergy", 0L); Set(Character, "idleEnergy", 0L); };
        try { Invoke(MainInstance, "ApplyGlobalPreset", invocation); }
        finally { BeardRefreshInterference = null; }
        var receipt = Get(MainInstance, "_globalPresetReceipt"); var policy = Get(receipt, "Policy");
        var acquiredSupport = Get(Get(policy, "Candidate"), "Support");
        Check(((long[])Get(Get(policy, "Candidate"), "EnergyShares")).Any(amount => amount > 0), "cached initial policy retains pre-apply allocation context");
        Check(((long[])Get(Get(receipt, "Execution"), "EnergyShares")).All(amount => amount == 0), "receipt execution records the observed applied allocation, not cached preflight");
        CallMain("VerifyLoadoutPresetAllocations", Get(receipt, "Execution"));
        Set(Character, "curEnergy", 10000L); Set(Character, "idleEnergy", 10000L);
        Equal(4L, Get(diggers[4], "curLevel"), "initial preview/apply preserves the deterministic exact digger level");
        Equal(initialSupport, Json(Json(Json(Json(invocation[1], "result"), "candidates"), 1), "support").ToString(), "Apply reports the exact initially previewed support");
        var snapshot = Get(MainInstance, "_loadoutPresetSnapshot").ToString();
        var start = (DateTime)Get(receipt, "LastBeardRotation"); var searches = PresetSearches;
        var captures = PresetCaptures; var recalls = Get(typeof(Program), "Recalls");
        CallMain("RotateGlobalPresetBeards", start.AddSeconds(59.999));
        Equal(captures, PresetCaptures, "rotation before the minute does not even capture");
        Equal(recalls, Get(typeof(Program), "Recalls"), "rotation before minute does not recall");

        root = Invoke(library, "ReadExternalLibrary");
        var edited = ((IEnumerable)GetChildren(Json(root, "Presets"))).Cast<object>().Single(x => Property(Json(x, "Id"), "Value").Equals("rotation-fixture"));
        SetJson(edited, "BeardPriority", JsonParse("[]")); SetJson(edited, "BeardRotation", JsonParse("[]"));
        SetJson(edited, "SupportMode", Json(JsonParse("{\"value\":\"current\"}"), "value"));
        revision = Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
        CallMain("RotateGlobalPresetBeards", start.AddSeconds(60));
        Equal("0,1,3,5", string.Join(",", ((IList)Get(Get(Character, "beards"), "activeBeards")).Cast<int>().OrderBy(x => x)), "exact minute uses acquired planned support despite saved Keep current/empty edits");
        Equal(3L, Get(diggers[4], "curLevel"), "Gold removal recalculates legal native digger funding");
        Equal(true, Get(diggers[4], "active"), "exact actuator restores the funded digger after Gold clears it");
        Check((double)Invoke(allDiggers, "totalGPSDrain") <= (double)Invoke(Character, "grossGoldPerSecond") * .75, "rotated native support stays within fresh GPS budget");
        Equal(start.AddSeconds(60), Get(receipt, "LastBeardRotation"), "successful minute commits its cadence");
        Check(((int[])Get(acquiredSupport, "Beards")).Contains(6), "rotation leaves acquired candidate support immutable");
        Equal(4L, Get(((Array)Get(acquiredSupport, "Diggers")).GetValue(0), "Level"), "rotation leaves initial support level immutable");
        Equal(true, CallMain("GlobalPresetReceiptValid", "dashboard"), "rotation updates observed support/allocation receipt");
        CallMain("VerifyLoadoutPresetAllocations", Get(receipt, "Execution"));
        Equal(true, CallMain("RefreshGlobalPresetAllocations", "dashboard"), "allocation refresh accepts rotated execution");
        Equal(false, ((IList)Get(Get(Character, "beards"), "activeBeards")).Contains(6), "refresh cannot replay original Gold back on");
        Equal(3L, Get(diggers[4], "curLevel"), "refresh cannot restore stale Gold-funded levels");
        Equal(snapshot, Get(MainInstance, "_loadoutPresetSnapshot").ToString(), "rotation never rewrites the applied historical snapshot");
        var runtime = CallMain("GetLoadoutPresetRuntime");
        Equal("[0,1,3,5]", Json(Json(runtime, "rotation"), "beards").ToString(), "current rotation reported separately");
        Equal("true", Json(runtime, "savedChangesRequireReequip").ToString(), "saved beard edits remain pending explicit re-equip");

        Set(MainInstance, "_customPreRebirthPrepRunning", true);
        captures = PresetCaptures; recalls = Get(typeof(Program), "Recalls");
        CallMain("RotateGlobalPresetBeards", start.AddSeconds(120));
        Equal(captures, PresetCaptures, "temporary owner suppresses minute capture");
        Equal(recalls, Get(typeof(Program), "Recalls"), "temporary owner suppresses minute recalls");
        Equal(start.AddSeconds(60), Get(receipt, "LastBeardRotation"), "temporary owner does not consume minute cadence");
        Set(MainInstance, "_customPreRebirthPrepRunning", false);
        var zone = Get(Character, "adventure"); var originalZone = Get(zone, "zone");
        Set(zone, "zone", 6);
        CallMain("RotateGlobalPresetBeards", start.AddSeconds(120));
        Equal(captures, PresetCaptures, "native titan zone suppresses rotation without temporary-owner flags");
        Equal(recalls, Get(typeof(Program), "Recalls"), "native titan zone suppresses recalls");
        Set(zone, "zone", originalZone);
        Set(MainInstance, "_maxItopodPushRemainingDeaths", 1);
        CallMain("RotateGlobalPresetBeards", start.AddSeconds(120));
        Equal(captures, PresetCaptures, "max ITOPOD owner suppresses rotation");
        Equal(recalls, Get(typeof(Program), "Recalls"), "max ITOPOD owner suppresses recalls");
        Set(MainInstance, "_maxItopodPushRemainingDeaths", 0);
        CallMain("RotateGlobalPresetBeards", start.AddSeconds(120));
        Equal(true, ((IList)Get(Get(Character, "beards"), "activeBeards")).Contains(6), "ownership recovery resumes acquired optional pool");
        Equal(4L, Get(diggers[4], "curLevel"), "returning Gold safely restores acquired exact level ceiling");
        Equal(searches, PresetSearches, "rotation and refresh never call the optimizer");
        Set(receipt, "LastBeardRotation", DateTime.UtcNow.AddSeconds(-61));
        CallMain("RunCustomBeardAutomation", (object)null);
        Equal(false, ((IList)Get(Get(Character, "beards"), "activeBeards")).Contains(6), "actual global beard tick services the owned preset instead of legacy core rotation");
        Equal(searches, PresetSearches, "global beard tick never runs gear search");

        CallMain("RotateGlobalPresetBeards", ((DateTime)Get(receipt, "LastBeardRotation")).AddSeconds(60));
        root = Invoke(library, "ReadExternalLibrary");
        edited = ((IEnumerable)GetChildren(Json(root, "Presets"))).Cast<object>().Single(x => Property(Json(x, "Id"), "Value").Equals("rotation-fixture"));
        SetJson(edited, "SupportMode", Json(JsonParse("{\"value\":\"current\"}"), "value"));
        SetJson(edited, "BeardPriority", JsonParse("[0]")); SetJson(edited, "BeardRotation", JsonParse("[2]"));
        revision = Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
        args = JsonParse("{\"presetId\":\"rotation-fixture\",\"libraryRevision\":\"" + revision + "\"}");
        var currentState = CallMain("GlobalPresetSupportState");
        preview = CallMain("PreviewGlobalPreset", args);
        Equal("[1,3,5,6]", Json(Json(Json(Json(preview, "candidates"), 0), "support"), "beards").ToString(), "Keep current preview ignores hidden enabled/pool settings");
        apply = JsonParse("{\"presetId\":\"rotation-fixture\",\"libraryRevision\":\"" + revision + "\",\"previewId\":" + Json(preview, "previewId") + ",\"candidateKey\":\"current\"}");
        FailBeardRefresh = true;
        try { Invoke(MainInstance, "ApplyGlobalPreset", apply, null); }
        finally { FailBeardRefresh = false; }
        Equal(currentState, CallMain("GlobalPresetSupportState"), "Keep current Apply leaves all observed support unchanged without native support writes");
        receipt = Get(MainInstance, "_globalPresetReceipt"); start = (DateTime)Get(receipt, "LastBeardRotation");
        recalls = Get(typeof(Program), "Recalls"); captures = PresetCaptures; searches = PresetSearches;
        var podCalls = PodRefreshCalls;
        CallMain("RotateGlobalPresetBeards", start.AddSeconds(60));
        Set(receipt, "LastBeardRotation", DateTime.UtcNow.AddSeconds(-61));
        CallMain("RunCustomBeardAutomation", (object)null);
        Equal(captures, PresetCaptures, "Keep current minute ticks never capture rotation");
        Equal(recalls, Get(typeof(Program), "Recalls"), "Keep current minute ticks never recall");
        Equal(podCalls, PodRefreshCalls, "Keep current minute ticks never touch digger inputs");
        Equal(currentState, CallMain("GlobalPresetSupportState"), "Keep current minute ticks preserve both support kinds");
        Equal(null, Get(MainInstance, "_globalPresetWaitingReason"), "Keep current does not report a spurious rotation funding failure");
        Check(!CallMain("GetLoadoutPresetRuntime").ToString().Contains("\"rotation\":"), "Keep current runtime does not advertise active rotation");

        SetActiveBeards(3); Set(diggers[4], "curLevel", 1L);
        currentState = CallMain("GlobalPresetSupportState");
        Equal(true, CallMain("RefreshGlobalPresetAllocations", "dashboard"), "Keep current refresh evaluates newly observed support");
        Equal(currentState, CallMain("GlobalPresetSupportState"), "Keep current refresh never restores old acquired support");
        var originalCurrentPolicy = Get(receipt, "Policy");
        Check(((int[])Get(Get(Get(originalCurrentPolicy, "Candidate"), "Support"), "Beards")).Contains(6), "observing current support leaves acquired candidate immutable");
        SetJson(edited, "AutoUpdate", Json(JsonParse("{\"value\":false}"), "value"));
        revision = Invoke(library, "SaveExternalLibrary", root, revision);
        args = JsonParse("{\"presetId\":\"rotation-fixture\",\"libraryRevision\":\"" + revision + "\"}");
        Invoke(MainInstance, "EquipGlobalPreset", args, null);
        Equal(searches, PresetSearches, "cached Keep current re-equip never searches");
        Equal(currentState, CallMain("GlobalPresetSupportState"), "cached Keep current re-equip preserves observed support, not old Gold/levels");

        // An explicit new acquisition of the edited definition clears beards, rather than enabling auto core.
        SetJson(edited, "SupportMode", Json(JsonParse("{\"value\":\"planned\"}"), "value"));
        SetJson(edited, "AutoUpdate", Json(JsonParse("{\"value\":true}"), "value"));
        SetJson(edited, "BeardPriority", JsonParse("[]")); SetJson(edited, "BeardRotation", JsonParse("[]"));
        revision = Invoke(library, "SaveExternalLibrary", root, revision);
        args = JsonParse("{\"presetId\":\"rotation-fixture\",\"libraryRevision\":\"" + revision + "\"}");
        Invoke(MainInstance, "EquipGlobalPreset", args, null);
        Equal(0, ((IList)Get(Get(Character, "beards"), "activeBeards")).Count, "both explicit empty arrays apply no beards through exact support");
        receipt = Get(MainInstance, "_globalPresetReceipt"); start = (DateTime)Get(receipt, "LastBeardRotation");
        currentState = CallMain("GlobalPresetSupportState"); recalls = Get(typeof(Program), "Recalls"); searches = PresetSearches;
        Set(settings, "_energyNguWeights", new[] { 0, 1, 0, 0, 0, 0, 0, 0, 0 });
        Set(MainInstance, "_protectedTimingLoadingSave", true);
        CallMain("RotateGlobalPresetBeards", start.AddSeconds(60));
        Equal(recalls, Get(typeof(Program), "Recalls"), "weight invalidation at beard tick cannot recall during save loading");
        Set(MainInstance, "_protectedTimingLoadingSave", false);
        CallMain("RotateGlobalPresetBeards", start.AddSeconds(60));
        Check(ReferenceEquals(receipt, Get(MainInstance, "_globalPresetReceipt")), "beard tick retains the acquired gear receipt for live ratios");
        Equal(currentState, CallMain("GlobalPresetSupportState"), "pending global ratios never change support during a beard tick");
        Equal(start, Get(receipt, "LastBeardRotation"), "unrefreshed ratios do not consume rotation cadence");
        Equal(true, CallMain("RefreshGlobalPresetAllocations", "dashboard"), "resource refresh applies pending global ratios");
        Equal(true, CallMain("GlobalPresetReceiptValid", "dashboard"), "live ratio refresh restores verified ownership");
        Equal(searches, PresetSearches, "weight-invalidated beard tick never searches");
    }

    private static void CheckNativeAllocationShapes()
    {
        var originalNgu = Get(Character, "NGU"); var originalAt = Get(Character, "advancedTraining");
        var recalls = Get(typeof(Program), "Recalls"); var swaps = Get(typeof(Program), "Swaps");
        try
        {
            // Native constructors retain reserved backing slots omitted by the shared fixture.
            var ngu = Activator.CreateInstance(Game.GetType("NUMBERSSGOUP", true));
            var at = Activator.CreateInstance(Game.GetType("AdvancedTraining", true));
            Set(Character, "NGU", ngu); Set(Character, "advancedTraining", at);
            Equal(11, ((IList)Get(ngu, "skills")).Count, "native energy NGU backing has eleven slots");
            Equal(11, ((IList)Get(ngu, "magicSkills")).Count, "native magic NGU backing has eleven slots");
            var atEnergy = (long[])Get(at, "energy");
            Equal(10, atEnergy.Length, "native AT backing has ten slots");
            var candidate = New("PresetCandidate");
            Set(candidate, "ControlsEnergy", true); Set(candidate, "ControlsMagic", true);
            // Required final lanes deliberately expect zero: matching a shortened prefix is insufficient.
            var energyShares = new long[] { 11, 13, 17, 19, 23, 29, 31, 37, 0 };
            var magicShares = new long[] { 41, 43, 47, 53, 59, 61, 0 };
            Set(candidate, "EnergyShares", energyShares); Set(candidate, "MagicShares", magicShares);
            foreach (var energy in new[] { true, false })
            {
                var lanes = (IList)Get(ngu, energy ? "skills" : "magicSkills");
                var shares = energy ? energyShares : magicShares;
                var resource = energy ? "energy" : "magic";
                for (var i = 0; i < lanes.Count; i++) Set(lanes[i], resource, i < shares.Length ? shares[i] : 0L);
            }
            Equal(null, CallMain("VerifyLoadoutPresetAllocations", candidate), "nonzero supported NGU shares with zero native tails are accepted");
            foreach (var energy in new[] { true, false })
            {
                var field = energy ? "skills" : "magicSkills";
                var lanes = (IList)Get(ngu, field); var shares = energy ? energyShares : magicShares;
                var resource = energy ? "energy" : "magic";
                for (var i = 0; i < shares.Length; i++)
                {
                    Set(lanes[i], resource, shares[i] + 1);
                    Throws(() => CallMain("VerifyLoadoutPresetAllocations", candidate), "application receipt rejected", resource + " supported one-unit mismatch rejected at " + i);
                    Set(lanes[i], resource, shares[i]);
                }
                for (var i = shares.Length; i < lanes.Count; i++)
                foreach (var value in new[] { 1L, -1L })
                {
                    Set(lanes[i], resource, value);
                    Throws(() => CallMain("VerifyLoadoutPresetAllocations", candidate), "application receipt rejected", resource + " nonzero native tail rejected at " + i + " value " + value);
                    Set(lanes[i], resource, 0L);
                }
                for (var count = 0; count < shares.Length; count++)
                {
                    var missing = (IList)Activator.CreateInstance(lanes.GetType());
                    for (var i = 0; i < count; i++) missing.Add(lanes[i]);
                    Set(ngu, field, missing);
                    Throws(() => CallMain("VerifyLoadoutPresetAllocations", candidate), "application receipt rejected", resource + " missing required lanes rejected with count " + count);
                    Set(ngu, field, lanes);
                }
            }
            var activities = New("PresetActivityAllocation");
            var atShares = new long[] { 67, 71, 73, 79, 0 };
            Set(activities, "AT", atShares); Set(candidate, "Activities", activities);
            Array.Clear(atEnergy, 0, atEnergy.Length); Array.Copy(atShares, atEnergy, atShares.Length);
            Equal(null, CallMain("VerifyLoadoutPresetAllocations", candidate), "native ten-slot AT accepts matching first five shares and zero tail");
            for (var i = 0; i < atEnergy.Length; i++)
            {
                atEnergy[i]++;
                Throws(() => CallMain("VerifyLoadoutPresetAllocations", candidate), "application receipt rejected", i < atShares.Length
                    ? "AT supported one-unit mismatch rejected at " + i : "AT nonzero native tail rejected at " + i);
                atEnergy[i]--;
            }
            for (var i = atShares.Length; i < atEnergy.Length; i++)
            {
                atEnergy[i] = -1;
                Throws(() => CallMain("VerifyLoadoutPresetAllocations", candidate), "application receipt rejected", "AT negative native tail rejected at " + i);
                atEnergy[i] = 0;
            }
            for (var count = 0; count < atShares.Length; count++)
            {
                Set(at, "energy", atShares.Take(count).ToArray());
                Throws(() => CallMain("VerifyLoadoutPresetAllocations", candidate), "application receipt rejected", "AT missing required lanes rejected with count " + count);
                Set(at, "energy", atEnergy);
            }
            Equal(null, CallMain("VerifyLoadoutPresetAllocations", candidate), "restored native NGU and AT allocations still verify");
            Equal(recalls, Get(typeof(Program), "Recalls"), "allocation shape verification never recalls resources");
            Equal(swaps, Get(typeof(Program), "Swaps"), "allocation shape verification never swaps gear");
        }
        finally { Set(Character, "NGU", originalNgu); Set(Character, "advancedTraining", originalAt); }
    }

    private static void CheckAdventureDynamic()
    {
        var before = Assertions;
        CheckAdventureDynamicPreview();
        Console.WriteLine("Adventure-dynamic preview: " + (Assertions - before) + " assertions.");
        var lifecycle = Assertions;
        CheckAdventureDynamicLifecycle();
        Console.WriteLine("Adventure-dynamic lifecycle: " + (Assertions - lifecycle) + " assertions.");
        CheckAdventureDynamicRuleFeedback();
        var manual = Assertions;
        CheckPresetManualRouteAcquisition();
        CheckAdventureDynamicManualPriority();
        CheckAdventureDynamicManualHandoff();
        CheckAdventureDynamicAcquiredManualRouting();
        Console.WriteLine("Adventure-dynamic manual delegation: " + (Assertions - manual) + " assertions.");
        Console.WriteLine("Adventure-dynamic: " + (Assertions - before) + " assertions.");
    }

    private static void CheckManualAdventureOverlay()
    {
        foreach (var scheduled in new[] { false, true })
        {
            var id = "manual-overlay-" + scheduled;
            var args = SetupAdventureDynamic(id, "[2,3,0,0,0,0,0,0,0,0,0,0]");
            Set(MainInstance, "_globalPresetBeardRandom", new Random(42));
            var settings = Get(MainType, "Settings");
            var library = Get(Get(MainType, "CustomPlanStore"), "<PresetLibrary>k__BackingField");
            var root = Invoke(library, "ReadExternalLibrary");
            var row = ((IEnumerable)GetChildren(Json(root, "Presets"))).Cast<object>().Single(x => Property(Json(x, "Id"), "Value").Equals(id));
            SetJson(row, "SupportMode", Json(JsonParse("{\"value\":\"planned\"}"), "value"));
            SetJson(row, "BeardPriority", JsonParse("[1,3]"));
            SetJson(row, "BeardRotation", JsonParse("[0,2,6]"));
            if (scheduled)
            {
                SetJson(row, "Weights", JsonParse("[2,3,0,0,0,0,0,0,0,2]"));
                Set(Get(Character, "advancedTraining"), "transferredBankedLevels", true);
                Set(Get(Character, "advancedTraining"), "levelTarget", Enumerable.Repeat(-1L, 5).ToArray());
            }
            var revision = Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
            args = JsonParse("{\"presetId\":\"" + id + "\",\"libraryRevision\":\"" + revision + "\"}");
            object plan = null;
            if (scheduled)
            {
                plan = Invoke(Get(MainType, "CustomPlanStore"), "Load", "fixture");
                var stage = Activator.CreateInstance(Plugin.GetType("NGUInjector.CustomPlans.CustomPlanStage", true));
                Set(stage, "Name", "Overlay base stage"); Set(stage, "PresetId", id); Set(stage, "Percent", 100d); Set(stage, "DurationSeconds", 60);
                Set(stage, "CompletionMode", 1); Set(stage, "TargetAdvancedTrainingPower", 10L); Set(stage, "TargetAdvancedTrainingToughness", 20L);
                var stages = Array.CreateInstance(stage.GetType(), 1); stages.SetValue(stage, 0);
                Set(plan, "Stages", stages); Set(plan, "StageSchedulerEnabled", true); Set(plan, "StageLoopStartIndex", -1);
                Set(plan, "AutoActivateFruits", false); Set(plan, "AutoRebirthEnabled", false); Set(plan, "TimeTriggerEnabled", false);
                Set(settings, "_useCustomRebirthPlan", true); Set(settings, "_autoRebirth", false);
                CallMain("RunCustomPlanAutomation", plan);
            }
            else Invoke(MainInstance, "EquipGlobalPreset", args, null);
            var receipt = Get(MainInstance, "_globalPresetReceipt"); var policy = Get(receipt, "Policy"); var owner = (string)Get(receipt, "Owner");
            Equal(true, CallMain("GlobalPresetReceiptValid", owner), id + " acquired base receipt");
            var historical = Get(MainInstance, "_loadoutPresetSnapshot").ToString();
            var planning = Get(MainType, "_presetPlanningCaptures"); var tables = Get(MainType, "_presetRouteTableEnumerations");
            var floors = Get(MainType, "_presetFloorForecastRows");
            var effective = Invoke(Get(policy, "ExecutionPreset"), "ToJson").ToString();
            var identity = Property(MainInstance, "ManualAdventureBaseIdentity");
            var nativeTargets = Get(MainInstance, "_globalPresetNativeTargets");
            if (!scheduled)
            {
                Set(settings, "_customAdventureEnabled", true); CallMain("RunManualGlobalPresetRefresh");
                Equal(id, Get(MainInstance, "_globalPresetManualId"), id + " custom-adventure toggle does not clear dashboard identity");
                Set(settings, "_customAdventureEnabled", false);
            }
            var allocations = CallMain("GlobalPresetAllocationState"); var support = CallMain("GlobalPresetSupportState");
            var recalls = Get(typeof(Program), "Recalls"); var magicRecalls = Get(typeof(Program), "MagicRecalls"); var swaps = Get(typeof(Program), "Swaps");
            var searches = PresetSearches; var invalidations = EquipmentInvalidations;
            PrepareAdventureDynamicManualRule(); Set(settings, "_customAdventureRules", Array.CreateInstance(Plugin.GetType("NGUInjector.AdventureRule", true), 0));
            Set(settings, "_manualAdventureGearWeights", new double[5]); Set(settings, "_manualAdventureBeardWeights", new double[7]); Set(settings, "_manualAdventureDiggerWeights", new double[12]);
            Invoke(settings, "set_ManualAdventureEnabled", true);
            Equal(true, CallMain("RouteManualAdventureZone", 0, false, "overlay fixture"), id + " enters actual manual routing");
            Equal(allocations, CallMain("GlobalPresetAllocationState"), id + " entry preserves both resource lanes");
            Equal(support, CallMain("GlobalPresetSupportState"), id + " all-zero support adds no override to base support");
            Equal(recalls, Get(typeof(Program), "Recalls"), id + " entry never recalls energy");
            Equal(magicRecalls, Get(typeof(Program), "MagicRecalls"), id + " entry never recalls magic");
            Equal(swaps, Get(typeof(Program), "Swaps"), id + " Keep gear entry never swaps");
            Equal(invalidations, EquipmentInvalidations, id + " entry never invalidates base equipment");
            Check(ReferenceEquals(nativeTargets, Get(MainInstance, "_globalPresetNativeTargets")), id + " entry preserves native AT target lease");
            Equal(true, CallMain("GlobalPresetReceiptValid", owner), id + " manual route mismatch does not suspend base");
            Equal(false, Get(receipt, "CurrentGearContinuation"), id + " no-op overlay does not acquire gear continuation");
            CheckManualOverlayCurrentReplay(policy, id);
            var originalBeards = ((IList)Get(Get(Character, "beards"), "activeBeards")).Cast<int>().ToArray();
            SetActiveBeards(3);
            Equal(false, CallMain("GlobalPresetReceiptValid", owner), id + " foreign support is not automatically adopted");
            Equal(false, CallMain("RefreshGlobalPresetAllocations", owner), id + " refresh refuses foreign support instead of adopting it");
            Equal(recalls, Get(typeof(Program), "Recalls"), id + " foreign support rejection precedes recall");
            Equal(false, CallMain("GlobalPresetReceiptValid", owner), id + " observer refuses unmarked foreign support");
            var originalAppliedSupport = Get(MainInstance, "_manualAdventureSupportApplied");
            Set(MainInstance, "_manualAdventureSupportApplied", Property(CallMain("CaptureSupportState"), "Selection"));
            Equal(true, CallMain("GlobalPresetReceiptValid", owner), id + " deliberate overlay support is verified independently of baseline receipt");
            FailBeardRefresh = true;
            try { Equal(true, CallMain("RefreshGlobalPresetAllocations", owner), id + " allocation-only no-override refresh accepts deliberately observed support without native beard writes"); }
            finally { FailBeardRefresh = false; }
            Equal("3", string.Join(",", ((IList)Get(Get(Character, "beards"), "activeBeards")).Cast<int>()), id + " allocation-only no-override refresh does not restore cached support");
            recalls = Get(typeof(Program), "Recalls"); magicRecalls = Get(typeof(Program), "MagicRecalls");
            var energyLane = ((IList)Get(Get(Character, "NGU"), "skills"))[0]; var energyBefore = (long)Get(energyLane, "energy");
            var observedEnergy = Get(receipt, "EnergyAllocations");
            Set(energyLane, "energy", energyBefore + 1);
            Equal(observedEnergy, Get(receipt, "EnergyAllocations"), id + " overlay observation never adopts foreign resource lanes");
            Equal(false, CallMain("GlobalPresetReceiptValid", owner), id + " foreign resource lanes still invalidate ownership");
            Equal(false, CallMain("RefreshGlobalPresetAllocations", owner), id + " refresh refuses foreign lanes instead of recalling them");
            Equal(recalls, Get(typeof(Program), "Recalls"), id + " foreign lane rejection precedes recall");
            Set(energyLane, "energy", energyBefore); SetActiveBeards(originalBeards);
            Set(MainInstance, "_manualAdventureSupportApplied", Property(CallMain("CaptureSupportState"), "Selection"));
            Set(MainInstance, "_manualAdventureSupportApplied", originalAppliedSupport);
            // End this synthetic projection probe; restore its original baseline
            // receipt observation, not production ownership via a deleted adopter.
            Set(receipt, "Support", CallMain("GlobalPresetSupportState"));
            Check(ReferenceEquals(Get(policy, "ExecutionPreset"), Property(MainInstance, "ManualAdventureBasePreset")), id + " exposes acquired execution policy to overlay");
            Set(MainInstance, "_maxItopodPushRemainingDeaths", 2);
            Equal(true, CallMain("GlobalPresetReceiptValid", owner), id + " explicit overlay overrides max-push guard");
            var evaluations = 0;
            PresetEvaluationObserver = (candidate, request) => {
                evaluations++;
                Equal(false, Get(request, "PlannedSupport"), id + " refresh observes actual support");
                var resourceWeights = ((double[])Get(Get(policy, "ExecutionPreset"), "Weights")).ToArray(); Array.Clear(resourceWeights, 3, 3);
                Check(((double[])Get(request, "Weights")).SequenceEqual(resourceWeights), id + " transient refresh view excludes only idle farming goals");
                Equal(Get(Get(policy, "Candidate"), "BloodPercent"), Get(candidate, "BloodPercent"), id + " refresh keeps acquired magic split");
                var current = Get(CallMain("CaptureLoadoutPresetDefinition", Get(policy, "ExecutionPreset")), "Snapshot");
                Check(((Array)Get(candidate, "Items")).Cast<object>().Select(x => Get(x, "SourceSlot")).SequenceEqual(((Array)Get(current, "Current")).Cast<object>().Select(x => Get(x, "SourceSlot"))), id + " compiled replay uses actual equipped gear");
            };
            try
            {
                Set(Get(Character, "adventure"), "beastModeOn", true);
                Equal(true, CallMain("GlobalPresetReceiptValid", owner), id + " manual Beast Mode does not block base resource readiness");
                // Natural NGU progress raises the desired cap without surrendering overlay lane ownership.
                Set(energyLane, "level", (long)Get(energyLane, "level") + 1000L);
                Equal(true, CallMain("RefreshGlobalPresetAllocations", owner), id + " refresh proceeds under manual/max-push ownership");
                Check(evaluations >= 2, id + " refresh evaluates before and after normal resource recall");
                if (scheduled) CallMain("RunCustomPlanAutomation", plan); else CallMain("RunManualGlobalPresetRefresh");
                Equal(true, CallMain("GlobalPresetReceiptValid", owner), id + " regular dashboard/stage tick remains valid during Beast Mode");
                Check(ReferenceEquals(receipt, Get(MainInstance, "_globalPresetReceipt")), id + " Beast Mode never replaces the acquired policy");
                CallMain("VerifyLoadoutPresetAllocations", Get(receipt, "Execution"));
                Equal(true, Get(Get(Character, "adventure"), "beastModeOn"), id + " resource refresh preserves manual Beast Mode");
            }
            finally { PresetEvaluationObserver = null; Set(Get(Character, "adventure"), "beastModeOn", false); }
            Equal(support, CallMain("GlobalPresetSupportState"), id + " refresh does not replay planned support");
            Equal(0, Get(Get(Character, "adventure"), "zone"), id + " refresh never steals manual route");
            var minute = ((DateTime)Get(receipt, "LastBeardRotation")).AddSeconds(61);
            var inventory = Get(Character, "inventory"); var head = Get(inventory, "head"); var stored = (IList)Get(inventory, "inventory"); var storedItem = stored[0];
            var combatHead = Invoke(head, "MemberwiseClone"); Set(combatHead, "id", 550); Set(combatHead, "curAttack", 5000f); Set(combatHead, "curDefense", 5000f);
            var trackedGear = Get(MainInstance, "_manualAdventureOverlayGear");
            var rotationRoutes = AdventureZoneEntries;
            var rotationSwaps = Get(typeof(Program), "Swaps");
            var attackPreference = Get(Get(Character, "adventure"), "autoattacking");
            var scheduler = scheduled ? Get(MainInstance, "_protectedScheduler") : null;
            var rotationEntry = scheduled ? Property(scheduler, "Entry") : null;
            var rotationDeadline = scheduled ? Property(scheduler, "ExpectedEndSeconds") : null;
            var rotationCredit = scheduled ? (double[])Property(scheduler, "EarnedSeconds") : null;
            var rotationBudget = scheduled ? (double[])Property(scheduler, "Durations") : null;
            var supportBarriers = 0;
            try
            {
                // Keep the acquired head's resource specs while changing actual combat gear for the rotation replay.
                Set(inventory, "head", combatHead); stored[0] = head; Invoke(typeof(Program), "RefreshBonuses");
                Set(MainInstance, "_manualAdventureOverlayGear", Invoke(Plugin.GetType("NGUInjector.Managers.LoadoutManager", true), "CaptureExactLoadout"));
                foreach (var guard in new[] { "isFighting", "nukeBoss" })
                {
                    Set(Get(Character, "bossController"), guard, true);
                    try { Equal(false, CallMain("RefreshGlobalPresetExecution", owner, minute), id + " " + guard + " rejects rotation execution"); }
                    finally { Set(Get(Character, "bossController"), guard, false); }
                    Equal(rotationRoutes, AdventureZoneEntries, id + " " + guard + " blocks native route retirement");
                    Equal(support, CallMain("GlobalPresetSupportState"), id + " " + guard + " blocks support mutation");
                    Equal(rotationSwaps, Get(typeof(Program), "Swaps"), id + " " + guard + " blocks gear mutation");
                    Check((DateTime)Get(receipt, "LastBeardRotation") < minute, id + " " + guard + " consumes no cadence");
                }
                BeardRefreshInterference = () => {
                    supportBarriers++;
                    Equal(-1, Get(Get(Character, "adventure"), "zone"), id + " native support writes occur only behind safe retirement");
                    Equal(rotationRoutes + 1, AdventureZoneEntries, id + " support barrier has exactly one safe route entry");
                    Check(ReferenceEquals(combatHead, Get(inventory, "head")), id + " support barrier retains actual manual gear");
                    Equal(rotationSwaps, Get(typeof(Program), "Swaps"), id + " support barrier performs no gear swaps");
                    if (scheduled) Equal(false, Get(scheduler, "_armed"), id + " support barrier cannot credit protected work");
                };
                // Execution retry deliberately ignores its own waiting projection;
                // the public minute dispatcher below remains admission-gated.
                Equal(true, CallMain("RefreshGlobalPresetExecution", owner, minute), id + " current manual rotation verifies on retry");
                Check(supportBarriers > 0, id + " observes actual native support mutation barrier");
                Equal(rotationRoutes + 2, AdventureZoneEntries, id + " verified rotation retires and resumes current manual route exactly once");
                Check(ReferenceEquals(combatHead, Get(inventory, "head")), id + " resumed native head remains supplied manual gear");
                Equal(rotationSwaps, Get(typeof(Program), "Swaps"), id + " rotation does not swap native gear");
                Equal(attackPreference, Get(Get(Character, "adventure"), "autoattacking"), id + " resume preserves native attack preference");
                Equal(2, Get(MainInstance, "_maxItopodPushRemainingDeaths"), id + " resume preserves max-ITOPOD death budget");
                if (scheduled)
                {
                    Equal(rotationEntry, Property(scheduler, "Entry"), id + " rotation retains original scheduled entry");
                    Equal(rotationDeadline, Property(scheduler, "ExpectedEndSeconds"), id + " rotation retains original deadline");
                    Check(rotationBudget.SequenceEqual((double[])Property(scheduler, "Durations")), id + " rotation retains original stage budget");
                    Check(rotationCredit.SequenceEqual((double[])Property(scheduler, "EarnedSeconds")), id + " rotation manufactures no stage credit");
                }
                Equal(550, Get(((Array)Get(Get(receipt, "Execution"), "Items")).GetValue(0), "Id"), id + " no-override rotation replays actual combat gear");
                Check((int)Get(((Array)Get(Get(policy, "Candidate"), "Items")).GetValue(0), "Id") != 550, id + " rotation leaves different acquired gear policy unchanged");
                BeardRefreshInterference = null;
                var rotatedSupport = CallMain("GlobalPresetSupportState");
                CallMain("RotateGlobalPresetBeards", minute);
                Equal(rotationRoutes + 2, AdventureZoneEntries, id + " accepted minute cannot repeat native route entries");
                Equal(rotatedSupport, CallMain("GlobalPresetSupportState"), id + " accepted minute cannot rotate support twice");
            }
            finally
            {
                BeardRefreshInterference = null;
                Set(inventory, "head", head); stored[0] = storedItem; Invoke(typeof(Program), "RefreshBonuses");
                Set(MainInstance, "_manualAdventureOverlayGear", trackedGear);
            }
            Check(!Equals(support, CallMain("GlobalPresetSupportState")), id + " zero support bonuses allow ordinary minute rotation during explicit manual combat");
            Equal(minute, Get(receipt, "LastBeardRotation"), id + " no-override minute rotation commits its cadence");
            Equal(true, Property(settings, "ManualAdventureEnabled"), id + " minute rotation never changes manual toggle");
            Equal(0, Get(Get(Character, "adventure"), "zone"), id + " minute rotation preserves manual combat route");
            support = CallMain("GlobalPresetSupportState");
            Equal(true, CallMain("RefreshGlobalPresetAllocations", owner), id + " allocation refresh accepts normally rotated base support under manual combat");
            Equal(support, CallMain("GlobalPresetSupportState"), id + " allocation refresh never replays pre-rotation cached support");
            var overdueMinute = DateTime.UtcNow.AddSeconds(-61);
            Set(receipt, "LastBeardRotation", overdueMinute);
            CallMain("RunCustomBeardAutomation", plan);
            Check(!Equals(support, CallMain("GlobalPresetSupportState")), id + " actual beard automation dispatches no-override minute rotation during manual combat");
            Check((DateTime)Get(receipt, "LastBeardRotation") > overdueMinute, id + " actual no-override beard tick commits rotation cadence");
            Equal(true, Property(settings, "ManualAdventureEnabled"), id + " actual beard tick keeps manual toggle enabled");
            Equal(0, Get(Get(Character, "adventure"), "zone"), id + " actual beard tick keeps manual route");
            minute = (DateTime)Get(receipt, "LastBeardRotation");
            support = CallMain("GlobalPresetSupportState");
            var supportBonuses = new double[7]; supportBonuses[4] = 100;
            Set(settings, "_manualAdventureBeardWeights", supportBonuses);
            CallMain("ApplyManualAdventureOverlaySupport");
            var protectedSupport = CallMain("GlobalPresetSupportState");
            Check(!Equals(support, protectedSupport), id + " real positive support override changes base selection");
            recalls = Get(typeof(Program), "Recalls"); magicRecalls = Get(typeof(Program), "MagicRecalls");
            CallMain("RotateGlobalPresetBeards", minute.AddSeconds(61));
            CallMain("RunCustomBeardAutomation", plan);
            Equal(recalls, Get(typeof(Program), "Recalls"), id + " real support override pauses minute rotation without energy recall");
            Equal(magicRecalls, Get(typeof(Program), "MagicRecalls"), id + " real support override pauses minute rotation without magic recall");
            Equal(protectedSupport, CallMain("GlobalPresetSupportState"), id + " paused rotation protects manual support");
            Equal(minute, Get(receipt, "LastBeardRotation"), id + " paused override does not consume rotation cadence");
            Equal(true, CallMain("RefreshGlobalPresetAllocations", owner), id + " allocation refresh continues under real support override");
            Equal(protectedSupport, CallMain("GlobalPresetSupportState"), id + " allocation refresh preserves real support override");
            Set(settings, "_manualAdventureBeardWeights", new double[7]);
            CallMain("RotateGlobalPresetBeards", minute.AddSeconds(61));
            Equal(protectedSupport, CallMain("GlobalPresetSupportState"), id + " pending support override stays protected after weights become zero");
            CallMain("ApplyManualAdventureOverlaySupport");
            Set(MainInstance, "_maxItopodPushRemainingDeaths", 0);
            if (scheduled) CallMain("RunCustomPlanAutomation", plan); else CallMain("RunManualGlobalPresetRefresh");
            Check(ReferenceEquals(receipt, Get(MainInstance, "_globalPresetReceipt")), id + " regular automation keeps acquired receipt");
            Equal(owner, Get(receipt, "Owner"), id + " base owner remains unchanged");
            Equal(identity, Property(MainInstance, "ManualAdventureBaseIdentity"), id + " base identity remains unchanged");
            Equal(effective, Invoke(Get(policy, "ExecutionPreset"), "ToJson").ToString(), id + " base policy remains immutable");
            Equal(historical, Get(MainInstance, "_loadoutPresetSnapshot").ToString(), id + " applied history remains immutable");
            Check(ReferenceEquals(nativeTargets, Get(MainInstance, "_globalPresetNativeTargets")), id + " refresh preserves native AT target lease");
            Equal(searches, PresetSearches, id + " overlay refresh never optimizes");
            allocations = CallMain("GlobalPresetAllocationState"); recalls = Get(typeof(Program), "Recalls"); magicRecalls = Get(typeof(Program), "MagicRecalls");
            Invoke(settings, "set_ManualAdventureEnabled", false); CallMain("ResetManualAdventureSetup");
            Equal(-1, Get(Get(Character, "adventure"), "zone"), id + " claim removal safely retires only its observed manual route");
            Equal(true, CallMain("RefreshGlobalPresetAllocations", owner), id + " current acquired policy verifies funding before route reactivation");
            Equal(allocations, CallMain("GlobalPresetAllocationState"), id + " exit preserves both resource lanes");
            Equal(recalls, Get(typeof(Program), "Recalls"), id + " exit never recalls energy");
            Equal(magicRecalls, Get(typeof(Program), "MagicRecalls"), id + " exit never recalls magic");
            Check(ReferenceEquals(receipt, Get(MainInstance, "_globalPresetReceipt")), id + " exit preserves base receipt");
            Equal(invalidations, EquipmentInvalidations, id + " exit never invalidates base equipment");
            Check(ReferenceEquals(nativeTargets, Get(MainInstance, "_globalPresetNativeTargets")), id + " exit preserves native AT target lease");
            Equal(false, Get(receipt, "CurrentGearContinuation"), id + " no-op exit never leaves gear continuation behind");
            Equal(false, Get(receipt, "SupportRouteRetired"), id + " ordinary acquired-policy refresh completes route activation");
            Equal(1000, Get(Get(Character, "adventure"), "zone"), id + " exit restores base farming route without recall");
            Equal(Get(Get(policy, "Candidate"), "Floor"), Get(Get(Character, "adventure"), "itopodStart"), id + " exit restores base farming floor");
            Equal(true, CallMain("GlobalPresetReceiptValid", owner), id + " exit restores verified base ownership without a restoration equip");
            Equal(true, CallMain("RefreshGlobalPresetAllocations", owner), id + " normal allocation refresh continues after exit");
            var beforeRotation = CallMain("GlobalPresetSupportState");
            CallMain("RotateGlobalPresetBeards", ((DateTime)Get(receipt, "LastBeardRotation")).AddSeconds(61));
            Check(!Equals(beforeRotation, CallMain("GlobalPresetSupportState")), id + " normal base support rotation resumes after no-op exit");
            if (scheduled)
            {
                Equal(20L, ((long[])Get(Get(Character, "advancedTraining"), "levelTarget"))[0], id + " mandatory toughness target remains owned");
                Equal(10L, ((long[])Get(Get(Character, "advancedTraining"), "levelTarget"))[1], id + " mandatory power target remains owned");
                Invoke(settings, "set_ManualAdventureEnabled", true); CallMain("RouteManualAdventureZone", 0, false, "new stage fixture");
                var freshGear = Invoke(Plugin.GetType("NGUInjector.Managers.LoadoutManager", true), "CaptureExactLoadout");
                var freshSwaps = Get(typeof(Program), "Swaps");
                var freshSearches = PresetSearches;
                var freshAtLevels = ((long[])Get(Get(Character, "advancedTraining"), "level")).ToArray();
                TraceFreshScheduledOverlay(plan, id + "/before-explicit-restart");
                RestartFixtureStage(plan); CallMain("RunCustomPlanAutomation", plan);
                TraceFreshScheduledOverlay(plan, id + "/after-fresh-acquisition");
                Equal(true, Property(settings, "ManualAdventureEnabled"), id + " fresh stage acquisition never turns manual toggle off");
                Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), id + " fresh stage acquires base policy under explicit overlay");
                Equal(freshSwaps, Get(typeof(Program), "Swaps"), id + " fresh baseline never flashes gear under Keep gear claim");
                Equal(true, Invoke(Plugin.GetType("NGUInjector.Managers.LoadoutManager", true), "ExactLoadoutMatches", freshGear), id + " fresh application uses actual manual gear");
                Equal(freshSearches + 1, PresetSearches, id + " fresh AutoUpdate entry performs exactly one authorized search");
                Check(freshAtLevels.SequenceEqual((long[])Get(Get(Character, "advancedTraining"), "level")), id + " fresh application does not manufacture mandatory AT levels");
                Equal(20L, ((long[])Get(Get(Character, "advancedTraining"), "levelTarget"))[0], id + " fresh stage reacquires mandatory toughness target");
                Equal(10L, ((long[])Get(Get(Character, "advancedTraining"), "levelTarget"))[1], id + " fresh stage reacquires mandatory power target");
                var freshScheduler = Get(MainInstance, "_protectedScheduler");
                var freshEntry = Property(freshScheduler, "Entry");
                var freshDeadline = Property(freshScheduler, "ExpectedEndSeconds");
                var freshBudget = ((double[])Property(freshScheduler, "Durations")).ToArray();
                var freshCredit = ((double[])Property(freshScheduler, "EarnedSeconds")).ToArray();
                Check(!Equals(identity, Property(MainInstance, "ManualAdventureBaseIdentity")), id + " fresh stage owner changes overlay reapplication key");
                CallMain("RouteManualAdventureZone", 0, false, "new stage reapplication");
                Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), id + " overlay reapplies to newly acquired stage without losing ownership");
                Equal(freshEntry, Property(freshScheduler, "Entry"), id + " fresh overlay reapplication retains entry");
                Equal(freshDeadline, Property(freshScheduler, "ExpectedEndSeconds"), id + " fresh overlay reapplication retains deadline");
                Check(freshBudget.SequenceEqual((double[])Property(freshScheduler, "Durations")), id + " fresh overlay reapplication retains original budgets");
                Check(freshCredit.SequenceEqual((double[])Property(freshScheduler, "EarnedSeconds")), id + " fresh overlay reapplication manufactures no credit");
                Equal(freshSearches + 1, PresetSearches, id + " overlay reapplication performs no additional search");
            }
            else
            {
                var beforeAcquisition = Property(MainInstance, "ManualAdventureBaseIdentity");
                Invoke(MainInstance, "EquipGlobalPreset", args, null);
                Check(!Equals(beforeAcquisition, Property(MainInstance, "ManualAdventureBaseIdentity")), id + " same dashboard definition reacquisition changes stable acquisition token");
                root = Invoke(library, "ReadExternalLibrary");
                row = ((IEnumerable)GetChildren(Json(root, "Presets"))).Cast<object>().Single(x => Property(Json(x, "Id"), "Value").Equals(id));
                SetJson(row, "SupportMode", Json(JsonParse("{\"value\":\"current\"}"), "value"));
                revision = Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
                args = JsonParse("{\"presetId\":\"" + id + "\",\"libraryRevision\":\"" + revision + "\"}");
                Invoke(MainInstance, "EquipGlobalPreset", args, null);
                var baseSupport = Property(CallMain("CaptureSupportState"), "StableKey");
                var advancedPreview = CallMain("PreviewGlobalPreset", args);
                var previewReceipt = Get(MainInstance, "_loadoutPresetPreview");
                var advancedApply = JsonParse(args.ToString().TrimEnd('}') + ",\"previewId\":" + Json(advancedPreview, "previewId") + ",\"candidateKey\":\"current\"}");
                var bonuses = new double[7]; bonuses[4] = 100;
                Set(settings, "_manualAdventureBeardWeights", bonuses);
                Invoke(settings, "set_ManualAdventureEnabled", true); CallMain("RouteManualAdventureZone", 0, false, "acquisition support fixture");
                Check(!Equals(baseSupport, Property(CallMain("CaptureSupportState"), "StableKey")), id + " overlay changes actual support before Keep current reacquisition");
                var overlaySupport = CallMain("GlobalPresetSupportState");
                var overlayAllocations = CallMain("GlobalPresetAllocationState");
                var overlayKey = Get(MainInstance, "_manualAdventureOverlayKey");
                recalls = Get(typeof(Program), "Recalls"); magicRecalls = Get(typeof(Program), "MagicRecalls");
                Throws(() => CallMain("PreviewGlobalPreset", args), "Turn off Manual Adventure", id + " preview rejects active support overlay with an actionable error");
                Check(ReferenceEquals(previewReceipt, Get(MainInstance, "_loadoutPresetPreview")), id + " rejected preview preserves the previous preview token");
                Throws(() => Invoke(MainInstance, "ApplyGlobalPreset", advancedApply, null), "Turn off Manual Adventure", id + " Apply rejects support overlay before mutation");
                var staleApply = JsonParse(advancedApply.ToString());
                SetJson(staleApply, "candidateKey", Json(JsonParse("{\"value\":\"missing\"}"), "value"));
                Throws(() => Invoke(MainInstance, "ApplyGlobalPreset", staleApply, null), "Candidate key", id + " invalid preview selection rejects before support release");
                Set(settings, "_manualAdventureBeardWeights", new double[7]);
                Throws(() => CallMain("PreviewGlobalPreset", args), "Turn off Manual Adventure", id + " pending applied support still blocks preview after weights are zeroed");
                Set(settings, "_manualAdventureBeardWeights", bonuses);
                Equal(overlaySupport, CallMain("GlobalPresetSupportState"), id + " rejected Preview/Apply never release overlay support");
                Equal(overlayAllocations, CallMain("GlobalPresetAllocationState"), id + " rejected Preview/Apply preserve allocations");
                Equal(overlayKey, Get(MainInstance, "_manualAdventureOverlayKey"), id + " rejected Preview/Apply preserve overlay tracking");
                Equal(recalls, Get(typeof(Program), "Recalls"), id + " rejected Preview/Apply never recall energy");
                Equal(magicRecalls, Get(typeof(Program), "MagicRecalls"), id + " rejected Preview/Apply never recall magic");
                Check(ReferenceEquals(previewReceipt, Get(MainInstance, "_loadoutPresetPreview")), id + " rejected Apply never consumes the preview");
                beforeAcquisition = Property(MainInstance, "ManualAdventureBaseIdentity");
                var currentSupport = Property(CallMain("CaptureSupportState"), "StableKey");
                var supportWrites = PodRefreshCalls;
                var beardWrites = new List<object>();
                BeardRefreshInterference = () => beardWrites.Add(Property(CallMain("CaptureSupportState"), "StableKey"));
                try
                {
                    Invoke(MainInstance, "EquipGlobalPreset", args, null);
                    Check(!Equals(beforeAcquisition, Property(MainInstance, "ManualAdventureBaseIdentity")), id + " active overlay reacquisition changes acquisition token");
                    Equal(currentSupport, Property(CallMain("CaptureSupportState"), "StableKey"), id + " Keep current acquisition observes actual support without a historical baseline write");
                    Equal(supportWrites, PodRefreshCalls, id + " unchanged Keep current acquisition performs no native digger rewrite");
                    Equal(true, Property(settings, "ManualAdventureEnabled"), id + " reacquisition keeps manual toggle enabled");
                    CallMain("RouteManualAdventureZone", 0, false, "acquisition rebase fixture");
                    Equal(currentSupport, Property(CallMain("CaptureSupportState"), "StableKey"), id + " rebased additive claim preserves the current pinned support");
                    Equal(supportWrites, PodRefreshCalls, id + " rebased unchanged claim never restores then reapplies historical diggers");
                    Check(beardWrites.All(x => Equals(x, currentSupport)), id + " every native beard callback observes current support, never the historical baseline");
                    Equal(0, beardWrites.Count, id + " unchanged acquisition and claim replay perform no native beard rewrite");
                }
                finally { BeardRefreshInterference = null; }
            }
        }
    }

    private static void CheckManualOverlayCurrentReplay(object policy, string id)
    {
        var inventory = Get(Character, "inventory"); var oldHead = Get(inventory, "head"); var items = (IList)Get(inventory, "inventory"); var manualHead = items[0];
        var beards = ((IList)Get(Get(Character, "beards"), "activeBeards")).Cast<int>().ToArray();
        Set(manualHead, "id", 550); Set(manualHead, "type", Get(oldHead, "type")); Set(manualHead, "bossRequired", 1); Set(manualHead, "removable", true);
        Set(manualHead, "curAttack", 5000f); Set(manualHead, "curDefense", 5000f);
        try
        {
            // A controlled native inventory change isolates replay from the separately tested safe actuator.
            Set(inventory, "head", manualHead); items[0] = oldHead; SetActiveBeards(3); Invoke(typeof(Program), "RefreshBonuses");
            var capture = CallMain("CaptureLoadoutPresetDefinition", Get(policy, "ExecutionPreset"), null, Get(policy, "Stage"));
            Set(capture, "AdventureTaskKey", Get(policy, "AdventureTaskKey"));
            var execution = Get(Get(MainInstance, "_globalPresetReceipt"), "Execution");
            var replay = CallMain("ReplayGlobalPreset", policy, Get(policy, "Preset"), capture, execution, true);
            Equal(550, Get(((Array)Get(replay, "Items")).GetValue(0), "Id"), id + " actual-gear replay uses the manual head, not displaced acquired head");
            Equal("3", string.Join(",", (int[])Get(Get(replay, "Support"), "Beards")), id + " actual-gear replay uses changed support, not planned acquisition");
            Check(!((int[])Get(Get(Get(policy, "Candidate"), "Support"), "Beards")).SequenceEqual(new[] { 3 }), id + " negative control has different acquired support");
            var pinned = CallMain("ReplayGlobalPreset", policy, Get(policy, "Preset"), capture, execution);
            Check((int)Get(((Array)Get(pinned, "Items")).GetValue(0), "Id") != 550, id + " pinned replay negative control selects displaced base gear");
            Equal(Get(execution, "BloodPercent"), Get(replay, "BloodPercent"), id + " changed gear/support never reselects acquired magic split");
        }
        finally
        {
            Set(inventory, "head", oldHead); items[0] = manualHead; SetActiveBeards(beards); Invoke(typeof(Program), "RefreshBonuses");
        }
    }

    private static object SetupAdventureDynamic(string id, string weights, bool autoUpdate = true)
    {
        Invoke(typeof(Program), "Setup");
        Character = Get(typeof(Program), "Character"); Controller = Get(typeof(Program), "Controller"); MainInstance = Get(typeof(Program), "MainInstance");
        PrepareSupport(); SetActiveBeards();
        Set(MainInstance, "_globalPresetPolicies", Activator.CreateInstance(Field(MainInstance, "_globalPresetPolicies").FieldType));
        var settings = Get(MainType, "Settings");
        Set(settings, "_manualAdventureEnabled", false); Set(settings, "_customAdventureEnabled", false);
        Set(settings, "_customAdventureMode", 0); Set(settings, "_customAdventureZone", 0);
        Set(settings, "_customAdventureRules", Array.CreateInstance(Plugin.GetType("NGUInjector.AdventureRule", true), 0));
        AllowPresetSettingsSave = true;
        Set(settings, "_savePath", Path.Combine((string)Get(MainType, "_dir"), "adventure-dynamic-settings.json"));
        // These cases verify execution and live flags, not Unity JSON or settings-file persistence.
        Invoke(settings, "SetSaveDisabled", true);
        Set(Get(Get(Character, "buttons"), "adventure"), "m_Interactable", true);
        Set(Get(Get(Character, "buttons"), "ngu"), "m_Interactable", true);
        var ngu = Get(Character, "NGU"); Set(ngu, "autoAdvance", false);
        ListField(ngu, "skills", 11); ListField(ngu, "magicSkills", 11);
        var adventure = Get(Character, "adventure"); var controller = Get(Character, "adventureController");
        Set(adventure, "highestItopodLevel", 60); Set(adventure, "autoattacking", true);
        Set(Get(Get(Character, "inventory"), "itemList"), "itopodKeyComplete", true);
        Set(Get(controller, "itopod"), "character", Character); Sequence(Get(adventure, "itopod"), "perkLevel", 300, 0L);
        var enemies = ListField(controller, "itopodEnemyList", 1); Set(enemies[0], "maxHP", 600f); Set(enemies[0], "defense", 10f);
        Child(controller, "itopodStartInput"); Child(controller, "itopodEndInput");
        var selector = Child(controller, "zoneSelector"); Set(selector, "character", Character); Set(selector, "ac", controller);
        Set(Child(Character, "allItemList"), "character", Character); Child(Character, "purchases");
        Sequence(Get(Get(Character, "inventory"), "itemList"), "itemMaxxed", 1000, false);
        Set(Get(Character, "beastQuestPerkController"), "character", Character);
        Sequence(Child(Character, "beastQuest"), "quirkLevel", 300, 0L);
        foreach (var perks in new[] { Get(controller, "itopod"), Get(Character, "beastQuestPerkController") })
        {
            Sequence(perks, "effectPerLevel", 300, 0f);
            ListField(perks, perks == Get(controller, "itopod") ? "perkDifficultyReq" : "quirkDifficultyReq", 300);
        }
        Check(Convert.ToDouble(Invoke(Character, "totalRecycleBonus")) >= 0, "dynamic fixture provides native recycling inputs");
        Check(Convert.ToDouble(Invoke(Get(Character, "allItemList"), "boostBonus")) > 0, "dynamic fixture provides native boost-value inputs");
        var library = Get(Get(MainType, "CustomPlanStore"), "<PresetLibrary>k__BackingField");
        var root = Invoke(library, "ReadExternalLibrary");
        var definition = JsonParse("{\"Id\":\"" + id + "\",\"Name\":\"Adventure dynamic fixture\",\"Weights\":" + weights
            + ",\"AdventureDynamicWeight\":10,\"SupportMode\":\"current\",\"SwapSeconds\":0,\"AutoUpdate\":" + (autoUpdate ? "true" : "false") + "}");
        var presets = Json(root, "Presets");
        var index = ((IEnumerable)GetChildren(presets)).Cast<object>().ToList().FindIndex(row => Equals(id, Property(Json(row, "Id"), "Value")));
        // A repeated full-suite group explicitly replaces its OWN fixture recipe.
        // Keep other definitions and real cross-plan reference validation intact.
        if (index < 0) Invoke(presets, "Add", "", definition);
        else presets.GetType().GetProperty("Item", new[] { typeof(int) }).SetValue(presets, definition, new object[] { index });
        var revision = Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
        return JsonParse("{\"presetId\":\"" + id + "\",\"libraryRevision\":\"" + revision + "\"}");
    }

    private static void CheckAdventureSettingsIsolation()
    {
        SetupAdventureDynamic("settings-isolation", "[1,1,0,0,0,0,0,0,0,0,0,0]");
        var library = Get(Get(MainType, "CustomPlanStore"), "<PresetLibrary>k__BackingField");
        var recipeCount = ((IEnumerable)GetChildren(Json(Invoke(library, "ReadExternalLibrary"), "Presets"))).Cast<object>().Count();
        SetupAdventureDynamic("settings-isolation", "[1,1,0,0,0,0,0,0,0,0,0,0]");
        library = Get(Get(MainType, "CustomPlanStore"), "<PresetLibrary>k__BackingField");
        var recipes = ((IEnumerable)GetChildren(Json(Invoke(library, "ReadExternalLibrary"), "Presets"))).Cast<object>().ToArray();
        Equal(1, recipes.Count(row => Equals("settings-isolation", Property(Json(row, "Id"), "Value"))), "repeated fixture setup retains one persisted recipe with the same semantic ID");
        Equal(recipeCount, recipes.Length, "repeated fixture setup preserves every unrelated persisted recipe and plan reference");
        var settings = Get(MainType, "Settings");
        Equal(true, Get(settings, "_disableSave"), "execution-only adventure fixture explicitly disables Unity settings persistence");
        var path = (string)Get(settings, "_savePath");
        Check(path != null && Path.GetFullPath(path).StartsWith(Path.GetFullPath((string)Get(typeof(Program), "Temp")) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase), "fixture settings path stays inside the isolated temporary directory");
        Check(!File.Exists(path), "adventure setup creates no settings file");
        var writes = PresetSettingsWrites;
        var plan = Invoke(Get(MainType, "CustomPlanStore"), "Load", "fixture");
        foreach (var name in new[] { "AutoRebirth", "UseCustomRebirthPlan" })
        {
            var original = (bool)Property(settings, name);
            foreach (var value in new[] { !original, original })
            {
                Set(MainInstance, "_customRuntimePlanCache", plan);
                Invoke(settings, "set_" + name, value);
                Equal(value, Property(settings, name), "disabled persistence retains real native setter state changes: " + name);
                Equal(null, Get(MainInstance, "_customRuntimePlanCache"), "disabled persistence retains real run-change notifications: " + name);
            }
        }
        Equal(writes, PresetSettingsWrites, "run setters never reach the legacy save substitute in execution-only fixtures");
        Check(!File.Exists(path), "real run setters do not create a settings file while persistence is disabled");
        Throws(() => Invoke(settings, "ApplyPatch", JsonParse("{\"_autoRebirth\":false}")), "Settings saving is unavailable",
            "direct persistence requests fail closed before Unity serialization instead of being stubbed");
        Equal(true, Property(settings, "AutoRebirth"), "rejected persistence request does not change the live run flag");
        Check(File.Exists(Path.Combine((string)Get(typeof(Program), "Temp"), "presets.json")), "preset-library filesystem persistence remains real");
    }

    private static void CheckAdventureGoalPreference()
    {
        foreach (var enabled in new[] { true, false })
        {
            var args = SetupAdventureDynamic("adventure-goal-preference-" + enabled, "[1,1,0,0,0,0,0,0,0,0,0,0]");
            var settings = Get(MainType, "Settings");
            settings.GetType().GetProperty("CustomAdventureEnabled").SetValue(settings, enabled);
            Invoke(MainInstance, "EquipGlobalPreset", args, null);
            Equal(enabled, Property(settings, "CustomAdventureEnabled"), "Equip preserves manual Adventure Goal preference " + enabled);
            CallMain("StopCustomFocusGoal");
            Equal(enabled, Property(settings, "CustomAdventureEnabled"), "stopping Equip preserves Adventure Goal preference " + enabled);
            Equal(null, Get(MainInstance, "_globalPresetManualId"), "stopping Equip actually releases the preset " + enabled);

            var preview = CallMain("PreviewGlobalPreset", args);
            var apply = JsonParse(args.ToString().TrimEnd('}') + ",\"previewId\":" + Json(preview, "previewId") + ",\"candidateKey\":\"balanced\"}");
            Invoke(MainInstance, "ApplyGlobalPreset", apply, null);
            Equal(enabled, Property(settings, "CustomAdventureEnabled"), "Preview + Apply preserves manual Adventure Goal preference " + enabled);
            CallMain("RunManualGlobalPresetRefresh");
            Equal(enabled, Property(settings, "CustomAdventureEnabled"), "preset refresh preserves Adventure Goal preference " + enabled);
            CallMain("StopCustomFocusGoal");
            Equal(enabled, Property(settings, "CustomAdventureEnabled"), "stopping Apply preserves Adventure Goal preference " + enabled);
            Equal(null, Get(MainInstance, "_globalPresetManualId"), "stopping Apply actually releases the preset " + enabled);
        }
    }

    private static void CheckAdventureDynamicPreview()
    {
        var args = SetupAdventureDynamic("dynamic-preview", "[2,3,0,0.5,0.25,0.75,0,0,0,0,4,5]");
        var settings = Get(MainType, "Settings");
        var library = Get(Get(MainType, "CustomPlanStore"), "<PresetLibrary>k__BackingField");
        var preset = Invoke(library, "Get", "dynamic-preview"); var saved = Invoke(preset, "ToJson").ToString();
        var recalls = Get(typeof(Program), "Recalls"); var swaps = Get(typeof(Program), "Swaps");
        foreach (var mode in new[] { 0, 2, 1 })
        {
            Set(settings, "_customAdventureMode", mode);
            var searches = PresetSearches;
            var preview = CallMain("PreviewGlobalPreset", args);
            Equal(searches + 1, PresetSearches, "dynamic preview performs one compiled search for mode " + mode);
            var cached = Get(MainInstance, "_loadoutPresetPreview"); var capture = Get(cached, "Capture");
            var effective = Get(capture, "ExecutionPreset");
            var expected = new double[] { 2, 3, 0, .5, .25, .75, 0, 0, 0, 0, 4, 5, 0, 0 };
            expected[mode == 1 ? 5 : mode == 2 ? 4 : 3] += 10;
            Check(((double[])Get(effective, "Weights")).SequenceEqual(expected), "capture adds full farming weight without changing other fixed goals for mode " + mode);
            Equal(0d, Get(effective, "AdventureDynamicWeight"), "execution definition is fully resolved for mode " + mode);
            Equal(mode == 1 ? 0 : -1, Get(effective, "Zone"), "only explicit zone intent pins a zone for mode " + mode);
            for (var i = 0; i < expected.Length; i++)
                Equal(expected[i], Property(Json(Json(preview, "effectiveWeights"), i), "AsDouble"), "preview exposes effective weight " + mode + "/" + i);
            Equal(Get(capture, "AdventureTaskKey"), Property(Json(preview, "adventureTaskKey"), "Value"), "preview exposes frozen task identity for mode " + mode);
            Check(((string)Get(capture, "AdventureTaskKey")).StartsWith(mode + ":"), "task identity records resolved mode " + mode);
            Equal(mode == 1 ? "Zone farming: " + CallMain("ZoneName", 0) : mode == 2 ? "Boost farming" : "ITOPOD",
                Property(Json(preview, "adventureTask"), "Value"), "preview labels effective activity for mode " + mode);
            Equal(CallMain("LoadoutPresetCanonical", JsonParse(saved)), CallMain("LoadoutPresetCanonical", Invoke(Get(cached, "Preset"), "ToJson")), "preview retains saved dynamic configuration for mode " + mode);

            var apply = JsonParse(args.ToString().TrimEnd('}') + ",\"previewId\":" + Json(preview, "previewId") + ",\"candidateKey\":\"balanced\"}");
            // Even an ignored zone setting is part of the adventure configuration key.
            Set(settings, "_customAdventureZone", 1);
            Throws(() => Invoke(MainInstance, "ApplyGlobalPreset", apply, null), "Adventure activity changed", "changed config rejects preview before mutation for mode " + mode);
            Equal(recalls, Get(typeof(Program), "Recalls"), "stale adventure preview never recalls for mode " + mode);
            Equal(swaps, Get(typeof(Program), "Swaps"), "stale adventure preview never swaps for mode " + mode);
            Equal(searches + 1, PresetSearches, "stale Apply never searches for mode " + mode);
            Check(ReferenceEquals(cached, Get(MainInstance, "_loadoutPresetPreview")), "stale task does not consume preview for mode " + mode);
            Check(Get(MainInstance, "_globalPresetReceipt") == null && Get(MainInstance, "_loadoutPresetSnapshot") == null, "stale task installs no ownership/history for mode " + mode);
            Check(((double[])Get(effective, "Weights")).SequenceEqual(expected), "later settings never mutate captured execution weights for mode " + mode);
            Set(settings, "_customAdventureZone", 0);
            Set(settings, "_customAdventureMode", mode == 0 ? 2 : 0);
            Throws(() => Invoke(MainInstance, "ApplyGlobalPreset", apply, null), "Adventure activity changed", "changed mode rejects preview before mutation for mode " + mode);
            Equal(recalls, Get(typeof(Program), "Recalls"), "changed mode Apply never recalls for mode " + mode);
            Equal(swaps, Get(typeof(Program), "Swaps"), "changed mode Apply never swaps for mode " + mode);
            Equal(searches + 1, PresetSearches, "changed mode Apply never searches for mode " + mode);
            Check(ReferenceEquals(cached, Get(MainInstance, "_loadoutPresetPreview")), "changed mode preserves preview token for mode " + mode);
        }
        Equal(saved, Invoke(Invoke(library, "Get", "dynamic-preview"), "ToJson").ToString(), "preview and stale Apply leave committed definition immutable");
    }

    private static void CheckAdventureDynamicLifecycle()
    {
        foreach (var mixed in new[] { false, true })
        {
            var id = mixed ? "dynamic-ngu-mix" : "dynamic-only";
            var args = SetupAdventureDynamic(id, mixed ? "[2,3,0,0,0,0,0,0,0,0,0,0]" : "[0,0,0,0,0,0,0,0,0,0,0,0]");
            var uncontrolledEnergy = CallMain("GlobalPresetLaneState", true);
            var uncontrolledMagic = CallMain("GlobalPresetLaneState", false);
            var settings = Get(MainType, "Settings"); var searches = PresetSearches;
            Invoke(MainInstance, "EquipGlobalPreset", args, null);
            Equal(searches + 1, PresetSearches, id + " Equip searches exactly once");
            var receipt = Get(MainInstance, "_globalPresetReceipt"); var policy = Get(receipt, "Policy");
            var saved = Invoke(Get(policy, "Preset"), "ToJson").ToString(); var effective = Get(policy, "ExecutionPreset");
            Equal(10d, Get(Get(policy, "Preset"), "AdventureDynamicWeight"), id + " policy preserves saved dynamic weight");
            Equal(0d, Get(effective, "AdventureDynamicWeight"), id + " policy separately stores resolved execution");
            Equal(10d, ((double[])Get(effective, "Weights"))[3], id + " executes PP farming");
            Equal(0d, ((double[])Get(effective, "Weights"))[10], id + " adds no power contribution");
            Equal(mixed ? 2d : 0d, ((double[])Get(effective, "Weights"))[0], id + " retains fixed NGU weight");
            foreach (var energy in new[] { true, false })
            {
                var row = ((IList)Get(Get(Character, "NGU"), energy ? "skills" : "magicSkills"))[0];
                if (mixed)
                {
                    Equal(((long[])Get(Get(receipt, "Execution"), energy ? "EnergyShares" : "MagicShares"))[0], Get(row, energy ? "energy" : "magic"), id + " actual NGU allocation matches resolved execution " + energy);
                    Check((long)Get(row, energy ? "energy" : "magic") > 0, id + " mixed fixed NGU goal receives resources " + energy);
                }
                else Equal(energy ? uncontrolledEnergy : uncontrolledMagic, CallMain("GlobalPresetLaneState", energy), id + " farming-only intent preserves the entire unclaimed resource lane " + energy);
            }
            Equal(true, CallMain("GlobalPresetReceiptValid", "dashboard"), id + " obtains verified ownership");
            Equal(1000, Get(Get(Character, "adventure"), "zone"), id + " applies an actual ITOPOD route");
            Equal(Get(Get(policy, "Candidate"), "Floor"), Get(Get(Character, "adventure"), "itopodStart"), id + " pins actual native floor inputs");
            var historical = Get(MainInstance, "_loadoutPresetSnapshot").ToString();
            var planning = Get(MainType, "_presetPlanningCaptures");
            var tables = Get(MainType, "_presetRouteTableEnumerations");
            var floors = Get(MainType, "_presetFloorForecastRows");
            for (var tick = 0; tick < 3; tick++)
            {
                CallMain("RunManualGlobalPresetRefresh");
                Equal(true, CallMain("GlobalPresetReceiptValid", "dashboard"), id + " repeat refresh keeps verified receipt " + tick);
            }
            Equal(searches + 1, PresetSearches, id + " refresh never re-optimizes");
            Equal(planning, Get(MainType, "_presetPlanningCaptures"), id + " dynamic farming refresh performs no broad capture");
            Equal(tables, Get(MainType, "_presetRouteTableEnumerations"), id + " dynamic farming refresh enumerates no zone alternatives");
            Equal(floors, Get(MainType, "_presetFloorForecastRows"), id + " dynamic farming refresh forecasts no floor alternatives");
            Check(ReferenceEquals(policy, Get(Get(MainInstance, "_globalPresetReceipt"), "Policy")), id + " refresh preserves acquired policy identity");
            Equal(historical, Get(MainInstance, "_loadoutPresetSnapshot").ToString(), id + " refresh leaves historical applied snapshot immutable");
            Equal(saved, Invoke(Get(policy, "Preset"), "ToJson").ToString(), id + " refresh leaves saved intent immutable");

            var routes = LegacyAdventureRoutes; var recalls = Get(typeof(Program), "Recalls");
            CallMain("CustomAdventureTick");
            Equal(routes, LegacyAdventureRoutes, id + " actual adventure tick does not enter legacy router while preset owns activity");
            Equal(recalls, Get(typeof(Program), "Recalls"), id + " owned routing tick never recalls");
            Equal(true, CallMain("GlobalPresetOwnsActivity", (object)null), id + " advertises route ownership");

            Set(settings, "_customAdventureMode", 2);
            CallMain("RunManualGlobalPresetRefresh");
            Equal(searches + 2, PresetSearches, id + " mode change triggers one automatic re-equip search; waiting=" + Get(MainInstance, "_globalPresetWaitingReason"));
            var changedPolicy = Get(Get(MainInstance, "_globalPresetReceipt"), "Policy");
            Equal(10d, ((double[])Get(Get(changedPolicy, "ExecutionPreset"), "Weights"))[4], id + " mode change resolves boosts");
            Equal(true, CallMain("GlobalPresetReceiptValid", "dashboard"), id + " changed task rearms verified ownership");
            CallMain("RunManualGlobalPresetRefresh");
            Equal(searches + 2, PresetSearches, id + " stable changed mode cannot loop searches");
            Set(settings, "_customAdventureZone", 1);
            CallMain("RunManualGlobalPresetRefresh");
            Equal(searches + 3, PresetSearches, id + " configuration-only key change triggers one re-equip");
            CallMain("RunManualGlobalPresetRefresh");
            Equal(searches + 3, PresetSearches, id + " stable changed configuration cannot loop searches");
            Equal(saved, Invoke(Get(policy, "Preset"), "ToJson").ToString(), id + " new acquisitions never rewrite previous saved intent");
            Equal(10d, ((double[])Get(effective, "Weights"))[3], id + " new acquisitions never rewrite previous effective intent");
            CallMain("StopManualGlobalPreset");
            Equal(false, CallMain("GlobalPresetOwnsActivity", (object)null), id + " Stop releases routing ownership");
            CallMain("CustomAdventureTick");
            Equal(routes + 1, LegacyAdventureRoutes, id + " negative control reaches real legacy router after ownership release");
        }

        var disabledArgs = SetupAdventureDynamic("dynamic-disabled", "[0,0,0,0,0,0,0,0,0,0,0,0]", false);
        var disabledSearches = PresetSearches; var disabledRecalls = Get(typeof(Program), "Recalls");
        Throws(() => Invoke(MainInstance, "EquipGlobalPreset", disabledArgs, null), "Preview + Apply", "disabled AutoUpdate requires explicit Preview + Apply seed");
        Equal(disabledSearches, PresetSearches, "unseeded disabled Equip does not search");
        Equal(disabledRecalls, Get(typeof(Program), "Recalls"), "unseeded disabled Equip does not recall");
        var preview = CallMain("PreviewGlobalPreset", disabledArgs);
        var apply = JsonParse(disabledArgs.ToString().TrimEnd('}') + ",\"previewId\":" + Json(preview, "previewId") + ",\"candidateKey\":\"balanced\"}");
        Invoke(MainInstance, "ApplyGlobalPreset", apply, null);
        Invoke(MainInstance, "EquipGlobalPreset", disabledArgs, null);
        Equal(disabledSearches + 1, PresetSearches, "seeded disabled Equip replays without a hidden search");
        var disabledReceipt = Get(MainInstance, "_globalPresetReceipt");
        var disabledPlanning = Get(MainType, "_presetPlanningCaptures"); var disabledTables = Get(MainType, "_presetRouteTableEnumerations");
        for (var tick = 0; tick < 3; tick++)
        {
            CallMain("RunManualGlobalPresetRefresh");
            Equal(true, CallMain("GlobalPresetReceiptValid", "dashboard"), "disabled stable dynamic farming refresh preserves acquired task");
        }
        Equal(disabledPlanning, Get(MainType, "_presetPlanningCaptures"), "disabled stable dynamic farming refresh does no broad capture");
        Equal(disabledTables, Get(MainType, "_presetRouteTableEnumerations"), "disabled stable dynamic farming refresh enumerates no alternatives");
        Set(Get(MainType, "Settings"), "_customAdventureMode", 2);
        disabledRecalls = Get(typeof(Program), "Recalls"); var disabledSwaps = Get(typeof(Program), "Swaps");
        for (var tick = 0; tick < 3; tick++) CallMain("RunManualGlobalPresetRefresh");
        Equal(disabledSearches + 1, PresetSearches, "disabled task-change ticks never search");
        Equal(disabledRecalls, Get(typeof(Program), "Recalls"), "disabled task-change ticks never recall");
        Equal(disabledSwaps, Get(typeof(Program), "Swaps"), "disabled task-change ticks never swap");
        Check(ReferenceEquals(disabledReceipt, Get(MainInstance, "_globalPresetReceipt")), "disabled task change retains old ownership observations");
        Equal(false, CallMain("GlobalPresetReceiptValid", "dashboard"), "disabled task change cannot claim a valid old task");
        Throws(() => Invoke(MainInstance, "EquipGlobalPreset", disabledArgs, null), "Preview + Apply", "new acquisition cannot reuse incompatible cached adventure task");
        Equal(disabledSearches + 1, PresetSearches, "incompatible disabled cache never silently searches");
        Equal(disabledRecalls, Get(typeof(Program), "Recalls"), "incompatible disabled cache rejects before recalls");
        preview = CallMain("PreviewGlobalPreset", disabledArgs);
        apply = JsonParse(disabledArgs.ToString().TrimEnd('}') + ",\"previewId\":" + Json(preview, "previewId") + ",\"candidateKey\":\"balanced\"}");
        Invoke(MainInstance, "ApplyGlobalPreset", apply, null);
        Equal(disabledSearches + 2, PresetSearches, "explicit new Preview + Apply searches only once when AutoUpdate is disabled");
        Equal(true, CallMain("GlobalPresetReceiptValid", "dashboard"), "explicit new task Apply restores disabled policy ownership");
    }

    private static void CheckAdventureDynamicRuleFeedback()
    {
        foreach (var autoUpdate in new[] { true, false })
        {
            var args = SetupAdventureDynamic("dynamic-feedback-" + autoUpdate, "[0,0,0,0,0,0,0,0,0,0,0,0]", autoUpdate);
            var inventory = Get(Character, "inventory"); var oldHead = Get(inventory, "head"); var items = (IList)Get(inventory, "inventory"); var newHead = items[0];
            Set(newHead, "id", 550); Set(newHead, "type", Get(oldHead, "type")); Set(newHead, "bossRequired", 1); Set(newHead, "removable", true);
            Set(newHead, "curAttack", 5000f); Set(newHead, "curDefense", 5000f);
            var beforePower = Convert.ToDouble(Invoke(Character, "totalAdvAttack"));
            Set(inventory, "head", newHead); items[0] = oldHead; Invoke(typeof(Program), "RefreshBonuses");
            var afterPower = Convert.ToDouble(Invoke(Character, "totalAdvAttack"));
            Set(inventory, "head", oldHead); items[0] = newHead; Invoke(typeof(Program), "RefreshBonuses");
            Check(afterPower > beforePower, "feedback negative control proves native gear changes rule power " + autoUpdate);
            var rules = Array.CreateInstance(Plugin.GetType("NGUInjector.AdventureRule", true), 1); var rule = Activator.CreateInstance(rules.GetType().GetElementType());
            Set(rule, "Enabled", true); Set(rule, "MinPower", (beforePower + afterPower) / 2); Set(rule, "Mode", 2); Set(rule, "Zone", 0); rules.SetValue(rule, 0);
            Set(Get(MainType, "Settings"), "_customAdventureRules", rules);
            var preview = CallMain("PreviewGlobalPreset", args);
            var task = Property(Json(preview, "adventureTaskKey"), "Value");
            Check(task.ToString().StartsWith("0:-1:-1:"), "preview resolves fallback before native threshold crossing " + autoUpdate);
            var apply = JsonParse(args.ToString().TrimEnd('}') + ",\"previewId\":" + Json(preview, "previewId") + ",\"candidateKey\":\"balanced\"}");
            var searches = PresetSearches;
            Invoke(MainInstance, "ApplyGlobalPreset", apply, null);
            Check(ReferenceEquals(newHead, Get(inventory, "head")), "real Apply equips the exact threshold-crossing head " + autoUpdate);
            var receipt = Get(MainInstance, "_globalPresetReceipt"); var policy = Get(receipt, "Policy");
            Equal(task, Get(policy, "AdventureTaskKey"), "acquisition retains original task through own gear crossing " + autoUpdate);
            Check(((string)Get(policy, "AdventureObservation")).StartsWith("2:-1:0:"), "post-Apply baseline observes newly matched native rule " + autoUpdate);
            Check(!Equals(task, Get(policy, "AdventureObservation")), "feedback fixture actually crosses task identity " + autoUpdate);
            Equal(10d, ((double[])Get(Get(policy, "ExecutionPreset"), "Weights"))[3], "effective intent stays frozen as PP after own rule crossing " + autoUpdate);
            Equal(0d, ((double[])Get(Get(policy, "ExecutionPreset"), "Weights"))[4], "own rule crossing cannot switch current acquisition to boosts " + autoUpdate);
            for (var tick = 0; tick < 3; tick++) CallMain("RunManualGlobalPresetRefresh");
            Equal(searches, PresetSearches, "gear feedback baseline suppresses repeated optimizer searches " + autoUpdate);
            Equal(true, CallMain("GlobalPresetReceiptValid", "dashboard"), "post-crossing refresh retains valid pinned ownership " + autoUpdate);
            Check(ReferenceEquals(policy, Get(Get(MainInstance, "_globalPresetReceipt"), "Policy")), "post-crossing refresh retains acquisition identity " + autoUpdate);

            if (!autoUpdate)
            {
                var recalls = Get(typeof(Program), "Recalls");
                Throws(() => Invoke(MainInstance, "EquipGlobalPreset", args, null), "Preview + Apply", "explicit new acquisition rejects cached PP task when current native rule selects boosts");
                Equal(recalls, Get(typeof(Program), "Recalls"), "new acquisition task mismatch rejects before mutation");
                Equal(searches, PresetSearches, "new acquisition cannot hide a search behind cached restoration");
                // A temporary owner's head still matches the post-Apply rule, unlike the original head.
                var temporaryHead = items[1];
                Set(temporaryHead, "id", 551); Set(temporaryHead, "type", Get(oldHead, "type")); Set(temporaryHead, "bossRequired", 1); Set(temporaryHead, "removable", true);
                Set(temporaryHead, "curAttack", 4000f); Set(temporaryHead, "curDefense", 4000f);
                var controller = Get(Character, "adventureController");
                Set(Child(controller, "enemyAI"), "player", Child(controller, "playerController"));
                var claim = CallMain("BeginExecutionClaim", "feedback-temporary", "feedback-1", Domain("Gear, Route"), (Func<bool>)(() => true));
                var temporary = ExactClaimSetup(temporaryHead, "feedback-temporary");
                Set(temporary, "RouteRetirementAuthorized", CallMain("CaptureExecutionBaselineRouteRetirement", claim));
                CallMain("ApplyExecutionClaim", claim, temporary);
                Check(Convert.ToDouble(Invoke(Character, "totalAdvAttack")) > (double)Get(rule, "MinPower"), "temporary owner's native gear retains post-Apply rule match");
                var library = Get(Get(MainType, "CustomPlanStore"), "<PresetLibrary>k__BackingField");
                var observed = CallMain("CaptureLoadoutPresetDefinition", Invoke(library, "Get", "dynamic-feedback-" + autoUpdate));
                Equal(Get(policy, "AdventureObservation"), Get(observed, "AdventureTaskKey"), "restoration really starts with the post-Apply key, not the pinned acquisition key");
                CallMain("InvalidateCustomEquipmentApplication");
                Equal(true, CallMain("EndExecutionClaim", claim, true), "authorized temporary feedback gear resolves the current pinned baseline directly");
                CallMain("RunManualGlobalPresetRefresh");
                Equal(searches, PresetSearches, "same-acquisition restoration replays pinned task without search despite post-Apply key difference");
                Check(ReferenceEquals(newHead, Get(inventory, "head")), "same-acquisition restoration returns exact pinned gear; waiting=" + Get(MainInstance, "_globalPresetWaitingReason"));
                Equal(task, Get(Get(Get(MainInstance, "_globalPresetReceipt"), "Policy"), "AdventureTaskKey"), "restoration retains original PP task");
                Equal(true, CallMain("GlobalPresetReceiptValid", "dashboard"), "restored pinned task owns a valid receipt");
            }
            else
            {
                Set(rule, "MinPower", afterPower + 1);
                CallMain("RunManualGlobalPresetRefresh");
                Equal(searches + 1, PresetSearches, "later rule configuration change causes exactly one re-equip");
                CallMain("RunManualGlobalPresetRefresh");
                Equal(searches + 1, PresetSearches, "stable new rule baseline does not loop");
                Equal(true, CallMain("GlobalPresetReceiptValid", "dashboard"), "rule-config reacquisition owns verified receipt");
            }
        }
    }

    private static void PrepareAdventureDynamicManualRule()
    {
        var settings = Get(MainType, "Settings");
        Set(settings, "_manualAdventureLoadout", -1);
        Set(settings, "_manualAdventureRecoverHealth", false); Set(settings, "_manualAdventurePrecastBuffs", false);
        Set(settings, "_manualAdventureFastCombat", false); Set(settings, "_manualAdventureBossOnly", false);
        Set(settings, "_manualAdventureBeastMode", false);
        Set(settings, "_customAdventureRules", Invoke(Plugin.GetType("NGUInjector.AdventureRule", true), "Parse",
            JsonParse("[{\"Enabled\":true,\"MinPower\":0,\"MinToughness\":0,\"MinRegen\":0,\"Mode\":4,\"Zone\":0}]")));
        Set(MainInstance, "_combManager", Activator.CreateInstance(Plugin.GetType("NGUInjector.Managers.CombatManager", true), true));
    }

    private static void CheckAdventureDynamicManualPriority()
    {
        var args = SetupAdventureDynamic("dynamic-manual-priority", "[0,0,0,0,0,0,0,0,0,0,0,0]");
        Invoke(MainInstance, "EquipGlobalPreset", args, null);
        PrepareAdventureDynamicManualRule();
        var receipt = Get(MainInstance, "_globalPresetReceipt"); var historical = Get(MainInstance, "_loadoutPresetSnapshot").ToString();
        var support = CallMain("GlobalPresetSupportState"); var allocations = CallMain("GlobalPresetAllocationState");
        var zone = Get(Get(Character, "adventure"), "zone");
        var searches = PresetSearches; var captures = PresetCaptures; var invalidations = EquipmentInvalidations; var routes = AdventureZoneEntries;
        var recalls = Get(typeof(Program), "Recalls"); var swaps = Get(typeof(Program), "Swaps");
        foreach (var owner in new[] { "_manualRebirthPending", "_customPreRebirthPrepRunning", "_bossPushRunning" })
        {
            Set(MainInstance, owner, true);
            try
            {
                object[] blocker = { null, null };
                Equal(true, Invoke(MainInstance, "CustomGoalBlocksAdventureGoldPush", blocker), owner + " really owns higher-priority activity");
                Equal(true, CallMain("TryRouteGlobalPresetManualAdventure", (object)null), owner + " consumes delegation while blocked");
                Equal(routes, AdventureZoneEntries, owner + " never reaches native zone entry");
                Equal(zone, Get(Get(Character, "adventure"), "zone"), owner + " retains the acquired farming route");
                Equal(invalidations, EquipmentInvalidations, owner + " never calls equipment invalidation");
                Equal(false, Get(MainInstance, "_customEquipmentApplicationInvalidated"), owner + " preserves applied equipment validity");
                Equal(true, CallMain("GlobalPresetGearMatches", receipt), owner + " preserves actual acquired gear");
                Equal(false, Get(MainInstance, "_globalPresetManualAdventureActive"), owner + " does not acquire delegated ownership");
                Equal(false, Property(MainInstance, "ManualAdventureOwnsEquipment"), owner + " does not claim manual gear ownership");
                Equal(support, CallMain("GlobalPresetSupportState"), owner + " never mutates support");
                Equal(allocations, CallMain("GlobalPresetAllocationState"), owner + " never mutates allocations");
            }
            finally { Set(MainInstance, owner, false); }
        }
        Equal(searches, PresetSearches, "higher-priority delegation never searches");
        Equal(captures, PresetCaptures, "higher-priority delegation never captures a replacement policy");
        Equal(recalls, Get(typeof(Program), "Recalls"), "higher-priority delegation never recalls");
        Equal(swaps, Get(typeof(Program), "Swaps"), "higher-priority delegation never swaps");
        Check(ReferenceEquals(receipt, Get(MainInstance, "_globalPresetReceipt")), "higher-priority guards preserve the acquired receipt");
        Equal(historical, Get(MainInstance, "_loadoutPresetSnapshot").ToString(), "higher-priority guards preserve applied history");
        Equal(true, CallMain("TryRouteGlobalPresetManualAdventure", (object)null), "unblocked control delegates the same manual rule");
        Check(AdventureZoneEntries > routes, "unblocked control reaches real native zone entry");
        Equal(0, Get(Get(Character, "adventure"), "zone"), "unblocked control routes to the manual rule zone");
        Equal(invalidations + 1, EquipmentInvalidations, "unblocked control invalidates once despite Keep current gear");
    }

    private static void CheckAdventureDynamicManualHandoff()
    {
        var args = SetupAdventureDynamic("dynamic-manual-handoff", "[0,0,0,0,0,0,0,0,0,0,0,0]", false);
        var preview = CallMain("PreviewGlobalPreset", args);
        var apply = JsonParse(args.ToString().TrimEnd('}') + ",\"previewId\":" + Json(preview, "previewId") + ",\"candidateKey\":\"balanced\"}");
        Invoke(MainInstance, "ApplyGlobalPreset", apply, null);
        var settings = Get(MainType, "Settings"); var receipt = Get(MainInstance, "_globalPresetReceipt"); var policy = Get(receipt, "Policy");
        var candidate = Get(policy, "Candidate"); var task = Get(policy, "AdventureTaskKey");
        var saved = Invoke(Get(policy, "Preset"), "ToJson").ToString(); var effective = Invoke(Get(policy, "ExecutionPreset"), "ToJson").ToString();
        var searches = PresetSearches; var captures = PresetCaptures; var invalidations = EquipmentInvalidations;
        var recalls = Get(typeof(Program), "Recalls"); var swaps = Get(typeof(Program), "Swaps");
        PrepareAdventureDynamicManualRule();
        Equal(false, Property(settings, "CustomAdventureEnabled"), "dashboard starts with custom adventure disabled");
        Equal(false, Property(settings, "ManualAdventureEnabled"), "dashboard starts with standalone manual adventure disabled");
        Equal(-1, Property(settings, "ManualAdventureLoadout"), "manual handoff explicitly keeps current gear");
        CallMain("CustomAdventureTick");
        Equal(0, Get(Get(Character, "adventure"), "zone"), "actual dashboard adventure tick delegates to the manual rule zone");
        Equal(invalidations + 1, EquipmentInvalidations, "Keep-current delegation calls invalidation even without a gear optimizer");
        Equal(true, Get(MainInstance, "_customEquipmentApplicationInvalidated"), "Keep-current delegation records the farming restoration event");
        Equal(false, CallMain("GlobalPresetReceiptValid", "dashboard"), "Keep-current delegation cannot retain farming application authority");
        Equal(true, Get(MainInstance, "_globalPresetManualAdventureActive"), "dashboard records delegated manual ownership");
        Equal(true, Get(MainInstance, "_manualAdventureSetupActive"), "real manual routing activates manual setup");
        Check(Get(MainInstance, "_delegatedManualRoute") != null, "actual delegated entry acquires typed current-route evidence");
        Equal(true, Property(MainInstance, "ManualAdventureOwnsEquipment"), "delegated dashboard manual combat owns equipment despite disabled adventure settings");
        Equal(null, Get(MainInstance, "_manualAdventureAppliedLoadout"), "Keep current never records a generated gear loadout");
        Equal(swaps, Get(typeof(Program), "Swaps"), "Keep-current handoff never swaps gear");
        Equal(true, CallMain("GlobalPresetGearMatches", receipt), "manual handoff retains the exact acquired gear");
        Equal(false, CallMain("GlobalPresetReceiptValid", "dashboard"), "manual delegate suspends farming ownership credit");
        Equal(true, CallMain("TryRouteGlobalPresetManualAdventure", (object)null), "repeated delegate services its current manual owner");
        Equal(invalidations + 1, EquipmentInvalidations, "repeated delegated ticks do not repeatedly invalidate equipment");
        CallMain("RunManualGlobalPresetRefresh");
        Equal(searches, PresetSearches, "active manual owner prevents farming optimizer searches");
        Equal(captures, PresetCaptures, "active manual owner prevents farming capture");
        Equal(recalls, Get(typeof(Program), "Recalls"), "active manual owner prevents farming resource recalls");
        Equal(0, Get(Get(Character, "adventure"), "zone"), "farming refresh cannot steal the delegated manual route");
        Check(ReferenceEquals(receipt, Get(MainInstance, "_globalPresetReceipt")), "manual handoff retains the suspended acquisition");

        var routeClaim = Get(MainInstance, "_delegatedManualRoute");
        var removalEntries = AdventureZoneEntries; var releaseCaptures = PresetCaptures;
        var transfers = ResourceTransfers;
        Console.WriteLine("Delegated removal before: claim=" + (routeClaim != null)
            + " zone=" + Get(Get(Character, "adventure"), "zone") + " entries=" + removalEntries
            + " captures=" + releaseCaptures + " transfers=" + transfers);
        // The routing callback may synchronously reconcile the successor. Hold
        // the real acquired claim pending BEFORE removal, not after retirement.
        AdventureZoneEntryObserver = zone => {
            Equal(-1, zone, "removal callback may only attempt safe retirement");
            Equal(0, Get(Get(Character, "adventure"), "zone"), "removal callback observes the real delegated native route");
            Equal(releaseCaptures, PresetCaptures, "removal retirement precedes successor capture");
            Equal(transfers, ResourceTransfers, "removal retirement precedes resource withdrawal");
            Equal(swaps, Get(typeof(Program), "Swaps"), "removal never installs intermediate historical gear");
        };
        FailAdventureZoneEntry = true;
        try
        {
            Set(settings, "_customAdventureRules", Array.CreateInstance(Plugin.GetType("NGUInjector.AdventureRule", true), 0));
            CallMain("CustomAdventureTick");
        }
        finally { FailAdventureZoneEntry = false; AdventureZoneEntryObserver = null; }
        Console.WriteLine("Delegated removal after: sameClaim=" + ReferenceEquals(routeClaim, Get(MainInstance, "_delegatedManualRoute"))
            + " zone=" + Get(Get(Character, "adventure"), "zone") + " entries=" + AdventureZoneEntries
            + " captures=" + PresetCaptures + " transfers=" + ResourceTransfers);
        Equal(false, Get(MainInstance, "_globalPresetManualAdventureActive"), "routing reset clears delegated manual ownership when farming returns");
        Equal(false, Get(MainInstance, "_manualAdventureSetupActive"), "routing reset clears manual setup");
        Equal(false, Property(MainInstance, "ManualAdventureOwnsEquipment"), "routing reset releases manual equipment ownership");
        Equal(true, Get(MainInstance, "_customEquipmentApplicationInvalidated"), "routing reset retains the explicit restoration event");
        Check(ReferenceEquals(routeClaim, Get(MainInstance, "_delegatedManualRoute")), "failed removal retains acquired route evidence independently from cleared activity flags");
        Equal(0, Get(Get(Character, "adventure"), "zone"), "failed removal does not fabricate a safe native route");
        Equal(releaseCaptures, PresetCaptures, "removal callback cannot capture a successor before retirement");
        Equal(transfers, ResourceTransfers, "removal callback cannot withdraw resources before retirement");
        var releaseEntries = AdventureZoneEntries;
        // If this callback defers reconciliation, exercise the same pending
        // claim below; if synchronous, its failed attempt is counted separately.
        Check(releaseEntries == removalEntries || releaseEntries == removalEntries + 1,
            "removal callback attempts safe retirement at most once");
        var adventure = Get(Character, "adventure");
        Set(adventure, "zone", 4); // Foreign non-safe route, never a fabricated safe barrier.
        CallMain("RunManualGlobalPresetRefresh");
        Equal(4, Get(adventure, "zone"), "foreign route is retained rather than restored from the delegated claim");
        Equal(releaseEntries, AdventureZoneEntries, "foreign route blocks native retirement");
        Equal(releaseCaptures, PresetCaptures, "foreign route blocks successor capture");
        Equal(transfers, ResourceTransfers, "foreign route blocks dependent withdrawal");
        Set(adventure, "zone", 0);
        var inventory = Get(Character, "inventory");
        Set(Character, "inventory", Activator.CreateInstance(inventory.GetType()));
        try
        {
            CallMain("RunManualGlobalPresetRefresh");
            Equal(releaseEntries, AdventureZoneEntries, "epoch loss blocks native retirement");
            Equal(releaseCaptures, PresetCaptures, "epoch loss blocks successor capture");
            Equal(transfers, ResourceTransfers, "epoch loss blocks dependent withdrawal");
        }
        finally { Set(Character, "inventory", inventory); }
        var entryKey = Get(MainInstance, "_customStageKey");
        Set(MainInstance, "_customStageKey", "foreign-entry");
        try
        {
            CallMain("RunManualGlobalPresetRefresh");
            Equal(releaseEntries, AdventureZoneEntries, "entry replacement blocks old route authority");
            Equal(releaseCaptures, PresetCaptures, "entry replacement blocks successor capture");
            Equal(transfers, ResourceTransfers, "entry replacement blocks dependent withdrawal");
        }
        finally { Set(MainInstance, "_customStageKey", entryKey); }
        var recoverHealth = Property(settings, "ManualAdventureRecoverHealth");
        Set(settings, "_manualAdventureRecoverHealth", !(bool)recoverHealth);
        CallMain("RunManualGlobalPresetRefresh");
        Equal(releaseEntries, AdventureZoneEntries, "changed user preference blocks delegated retirement");
        Equal(releaseCaptures, PresetCaptures, "lost delegated authority blocks dependent planning capture");
        Equal(transfers, ResourceTransfers, "lost delegated authority blocks dependent resource withdrawal");
        Equal(0, Get(Get(Character, "adventure"), "zone"), "changed preference is not overwritten by a farming route");
        Set(settings, "_manualAdventureRecoverHealth", recoverHealth);
        FailAdventureZoneEntry = true;
        try { CallMain("RunManualGlobalPresetRefresh"); }
        finally { FailAdventureZoneEntry = false; }
        Equal(releaseEntries + 1, AdventureZoneEntries, "retirement failure attempts only the native safe entry");
        Equal(releaseCaptures, PresetCaptures, "failed native retirement blocks successor capture");
        Equal(transfers, ResourceTransfers, "failed native retirement blocks resource withdrawal");
        Equal(0, Get(Get(Character, "adventure"), "zone"), "failed retirement retains actual manual route");
        Check(ReferenceEquals(routeClaim, Get(MainInstance, "_delegatedManualRoute")), "failure retains the same bounded route evidence");
        Equal(false, CallMain("GlobalPresetReceiptValid", "dashboard"), "failed retirement cannot manufacture farming credit");
        var releaseZones = new List<int>();
        AdventureZoneEntryObserver = zone => {
            releaseZones.Add(zone);
            Equal(zone == -1 ? 0 : -1, Get(Get(Character, "adventure"), "zone"), "native handoff observes manual then safe route in order");
            Equal(swaps, Get(typeof(Program), "Swaps"), "handoff never equips an intermediate historical setup");
            if (zone == -1)
            {
                Equal(transfers, ResourceTransfers, "safe retirement precedes all successor resource application");
                Equal(releaseCaptures, PresetCaptures, "safe retirement precedes successor planning capture");
            }
            else Equal(1000, zone, "only the pinned current ITOPOD successor enters after retirement");
        };
        try { CallMain("RunManualGlobalPresetRefresh"); }
        finally { AdventureZoneEntryObserver = null; }
        Equal("-1,1000", string.Join(",", releaseZones), "successful handoff retires then enters the successor exactly once each");
        Equal(releaseEntries + 3, AdventureZoneEntries, "one failed retirement plus verified retirement/successor native entries");
        Equal(null, Get(MainInstance, "_delegatedManualRoute"), "verified retirement consumes narrow manual evidence");
        Equal(searches, PresetSearches, "disabled AutoUpdate restores pinned farming without a new search");
        Equal(1000, Get(Get(Character, "adventure"), "zone"), "reset allows actual pinned ITOPOD farming restoration");
        Equal(Get(candidate, "Floor"), Get(Get(Character, "adventure"), "itopodStart"), "restoration keeps the acquired farming floor");
        Equal(Get(candidate, "Floor"), Get(Get(Character, "adventure"), "itopodEnd"), "restoration keeps both native floor bounds pinned");
        var restored = Get(MainInstance, "_globalPresetReceipt"); var restoredPolicy = Get(restored, "Policy");
        Equal(task, Get(restoredPolicy, "AdventureTaskKey"), "manual reset restores the pinned adventure task");
        Equal(Get(candidate, "Key"), Get(Get(restoredPolicy, "Candidate"), "Key"), "manual reset restores the selected preview candidate");
        Equal(saved, Invoke(Get(restoredPolicy, "Preset"), "ToJson").ToString(), "manual reset never rewrites the acquired saved intent");
        Equal(effective, Invoke(Get(restoredPolicy, "ExecutionPreset"), "ToJson").ToString(), "manual reset preserves the resolved farming intent");
        Equal(false, Get(MainInstance, "_customEquipmentApplicationInvalidated"), "successful farming restoration clears invalidation");
        Equal(true, CallMain("GlobalPresetReceiptValid", "dashboard"), "reset and restoration rearm verified farming ownership");
        Equal(true, CallMain("GlobalPresetGearMatches", restored), "restoration retains exact pinned gear");
        Equal(saved, Invoke(Get(policy, "Preset"), "ToJson").ToString(), "restoration never mutates the previous acquired saved definition");
        Equal(effective, Invoke(Get(policy, "ExecutionPreset"), "ToJson").ToString(), "restoration never mutates the previous acquired effective definition");
        var settledEntries = AdventureZoneEntries;
        CallMain("RunManualGlobalPresetRefresh");
        Equal(settledEntries, AdventureZoneEntries, "settled handoff does not repeat retirement or successor entry");
        Equal(searches, PresetSearches, "settled AutoUpdate-false handoff performs no replacement search");
        Console.WriteLine("Delegated release verified: zones=" + string.Join(",", releaseZones)
            + " claim=" + (Get(MainInstance, "_delegatedManualRoute") != null)
            + " zone=" + Get(Get(Character, "adventure"), "zone") + " entries=" + AdventureZoneEntries
            + " captures=" + PresetCaptures + " transfers=" + ResourceTransfers);
    }

    private static void CheckAdventureDynamicAcquiredManualRouting()
    {
        foreach (var acquiredDynamic in new[] { true, false })
        {
            var id = "dynamic-manual-saved-" + acquiredDynamic;
            SetupAdventureDynamic(id, "[0,0,0,0,0,0,0,0,0,0,1,0]");
            var settings = Get(MainType, "Settings"); var store = Get(MainType, "CustomPlanStore");
            var library = Get(store, "<PresetLibrary>k__BackingField"); var root = Invoke(library, "ReadExternalLibrary");
            var row = ((IEnumerable)GetChildren(Json(root, "Presets"))).Cast<object>().Single(x => Property(Json(x, "Id"), "Value").Equals(id));
            if (!acquiredDynamic)
            {
                SetJson(row, "AdventureDynamicWeight", Json(JsonParse("{\"value\":0}"), "value"));
                Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
            }
            var plan = Invoke(store, "Load", "fixture");
            var stage = Activator.CreateInstance(Plugin.GetType("NGUInjector.CustomPlans.CustomPlanStage", true));
            Set(stage, "Name", "Acquired routing authority"); Set(stage, "PresetId", id); Set(stage, "Percent", 100d); Set(stage, "DurationSeconds", 60);
            var stages = Array.CreateInstance(stage.GetType(), 1); stages.SetValue(stage, 0);
            Set(plan, "Stages", stages); Set(plan, "StageSchedulerEnabled", true); Set(plan, "StageLoopStartIndex", -1);
            Set(plan, "AutoActivateFruits", false); Set(plan, "AutoRebirthEnabled", false); Set(plan, "TimeTriggerEnabled", false);
            Set(settings, "_useCustomRebirthPlan", true); Set(settings, "_autoRebirth", false);
            CallMain("RunCustomPlanAutomation", plan);
            Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), "scheduled routing fixture acquires a real stage receipt " + acquiredDynamic);
            var receipt = Get(MainInstance, "_globalPresetReceipt"); var policy = Get(receipt, "Policy");
            var owner = Get(receipt, "Owner"); var acquired = Invoke(Get(policy, "Preset"), "ToJson").ToString();
            Equal(Get(MainInstance, "_customStageEquipmentKey"), owner, "receipt and active stage really have the same owner " + acquiredDynamic);
            Equal(acquiredDynamic ? 10d : 0d, Get(Get(policy, "Preset"), "AdventureDynamicWeight"), "stage acquired the intended dynamic setting " + acquiredDynamic);

            root = Invoke(library, "ReadExternalLibrary");
            row = ((IEnumerable)GetChildren(Json(root, "Presets"))).Cast<object>().Single(x => Property(Json(x, "Id"), "Value").Equals(id));
            SetJson(row, "AdventureDynamicWeight", Json(JsonParse("{\"value\":" + (acquiredDynamic ? "0" : "10") + "}"), "value"));
            Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
            Equal(acquiredDynamic ? 0d : 10d, Get(Invoke(library, "Get", id), "AdventureDynamicWeight"), "library really commits the opposite dynamic intent " + acquiredDynamic);
            PrepareAdventureDynamicManualRule();
            var searches = PresetSearches; var captures = PresetCaptures; var routes = AdventureZoneEntries; var invalidations = EquipmentInvalidations;
            var recalls = Get(typeof(Program), "Recalls"); var swaps = Get(typeof(Program), "Swaps");
            var zone = Get(Get(Character, "adventure"), "zone");
            Equal(acquiredDynamic, CallMain("TryRouteGlobalPresetManualAdventure", plan), "same stage owner routes from acquired definition, not newly saved dynamic add/remove " + acquiredDynamic);
            Equal(acquiredDynamic, Get(MainInstance, "_globalPresetManualAdventureActive"), "delegated ownership follows acquired dynamic setting " + acquiredDynamic);
            Equal(acquiredDynamic, Property(MainInstance, "ManualAdventureOwnsEquipment"), "manual gear ownership follows acquired definition " + acquiredDynamic);
            Equal(acquiredDynamic ? 0 : zone, Get(Get(Character, "adventure"), "zone"), "actual route follows acquired intent despite opposite saved edit " + acquiredDynamic);
            Equal(acquiredDynamic, AdventureZoneEntries > routes, "native route entry follows acquired intent " + acquiredDynamic);
            Equal(invalidations + (acquiredDynamic ? 1 : 0), EquipmentInvalidations, "only acquired dynamic activity invalidates farming on delegation " + acquiredDynamic);
            Equal(searches, PresetSearches, "saved dynamic edit never silently searches during routing " + acquiredDynamic);
            Equal(captures, PresetCaptures, "saved dynamic edit never recaptures a replacement policy during routing " + acquiredDynamic);
            Equal(recalls, Get(typeof(Program), "Recalls"), "scheduled delegation never recalls allocations " + acquiredDynamic);
            Equal(swaps, Get(typeof(Program), "Swaps"), "Keep-current scheduled delegation never swaps " + acquiredDynamic);
            Check(ReferenceEquals(receipt, Get(MainInstance, "_globalPresetReceipt")) && ReferenceEquals(policy, Get(receipt, "Policy")), "saved edits and routing retain acquired policy identity " + acquiredDynamic);
            Equal(owner, Get(receipt, "Owner"), "saved edits never change the active acquisition owner " + acquiredDynamic);
            Equal(acquired, Invoke(Get(policy, "Preset"), "ToJson").ToString(), "routing never rewrites acquired definition from library " + acquiredDynamic);
        }
    }

    private sealed class CommitBudgetProbe
    {
        internal readonly List<Stopwatch> Captures = new List<Stopwatch>();
        internal Stopwatch PlanningClock;
        internal bool Fired;

        internal CommitBudgetProbe(string boundary)
        {
            PresetCaptureObserver = clock => {
                Check(clock != null, "production capture receives its operation stopwatch");
                if (Captures.Count > 0) Check(ReferenceEquals(Captures[0], clock), "preparation and actual capture share the same stopwatch");
                Captures.Add(clock);
                if (boundary == "preparation-capture" || boundary == "capture" && Captures.Count == 2) Expire(clock);
            };
            PresetSearchObserver = clock => {
                PlanningClock = clock;
                Check(Captures.Count == 1 && ReferenceEquals(Captures[0], clock), "real Plan receives the exact capture stopwatch, not a fresh budget");
            };
            PresetPlanCompletedObserver = clock => {
                Check(ReferenceEquals(PlanningClock, clock), "Plan returns with the same operation stopwatch");
                if (boundary == "preparation-plan") Expire(clock);
            };
            GearRenderObserver = () => { if (boundary == "gear") Expire(Captures[0]); };
            BeardRefreshInterference = () => { if (boundary == "support") Expire(Captures[0]); };
            RecallObserver = () => { if (boundary == "recall") Expire(Captures[0]); };
            CaptureNativeObserver = () => { if ((boundary == "capture-native" || boundary == "capture-itopod") && Captures.Count == 2) Expire(Captures[0]); };
            PresetEvaluationObserver = (candidate, request) => { if (boundary == "evaluation" && Captures.Count == 2) Expire(Captures[0]); };
            AtRefreshObserver = () => {
                if (boundary != "funding") return;
                if (!((long[])Get(Get(Character, "advancedTraining"), "energy")).Take(2).All(x => x > 0)
                    || (long)Get(((IList)Get(Get(Character, "NGU"), "magicSkills"))[0], "magic") <= 0) return;
                Check(((long[])Get(Get(Character, "advancedTraining"), "energy")).Take(2).All(x => x > 0), "AT render observes both real funded bars before budget expiration");
                Check((long)Get(((IList)Get(Get(Character, "NGU"), "magicSkills"))[0], "magic") > 0, "AT render observes the committed magic allocation");
                Expire(Captures[0]);
            };
        }

        private void Expire(Stopwatch clock)
        {
            if (Fired) return;
            // Set the real Framework stopwatch's accumulated counter. Deadline bodies/getters are untouched.
            clock.Reset();
            typeof(Stopwatch).GetField("elapsed", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(clock, checked(Stopwatch.Frequency * 4));
            Equal(4000L, clock.ElapsedMilliseconds, "controlled real Stopwatch.ElapsedMilliseconds crosses the 3000ms deadline without sleeping");
            Fired = true;
        }
    }

    private static void CheckScheduledRouteHandoff()
    {
        var failures = new List<string>();
        foreach (var itopod in new[] { false, true })
        foreach (var change in new[] { "none", "route", "gear", "support", "lane", "callback" })
        {
            var label = (itopod ? "ITOPOD" : "regular") + "/" + change;
            try
            {
                var id = "scheduled-route-handoff-" + itopod + "-" + change;
                SetupAdventureDynamic(id, "[1,1,0,0,0,0,0,0,0,0,0,0]");
                var settings = Get(MainType, "Settings");
                Set(settings, "_useCustomRebirthPlan", true); Set(settings, "_autoRebirth", false);
                Set(settings, "_customAdventureMode", itopod ? 0 : 1);
                var controller = Get(Character, "adventureController");
                var player = Child(controller, "playerController"); Set(Child(controller, "enemyAI"), "player", player);
                var store = Get(MainType, "CustomPlanStore"); var plan = Invoke(store, "Load", "fixture");
                var stages = Array.CreateInstance(Plugin.GetType("NGUInjector.CustomPlans.CustomPlanStage", true), 2);
                for (var i = 0; i < stages.Length; i++)
                {
                    var stage = Activator.CreateInstance(stages.GetType().GetElementType());
                    Set(stage, "Name", "Farming " + i); Set(stage, "PresetId", id);
                    Set(stage, "DurationSeconds", i == 0 ? 2 : 600); stages.SetValue(stage, i);
                }
                Set(plan, "Stages", stages); Set(plan, "StageSchedulerEnabled", true); Set(plan, "StageLoopStartIndex", -1);
                Set(plan, "TimeTriggerEnabled", false); Set(plan, "StageDurationSeconds", 7200);
                Set(plan, "AutoActivateFruits", false); Set(plan, "AutoRebirthEnabled", false);
                Set(plan, "AutoIronPillEnabled", false); Set(plan, "AutoBloodSpellsEnabled", false);
                Invoke(store, "Save", "fixture", plan);
                CallMain("RunCustomPlanAutomation", plan); RequireInitialScheduledReceipt(plan, label);
                var previous = Get(MainInstance, "_globalPresetReceipt");
                var adventure = Get(Character, "adventure"); var route = itopod ? 1000 : 0;
                Equal(route, Get(adventure, "zone"), label + " first stage acquires the real farming route");
                Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), label + " predecessor setup/funding is verified");
                foreach (var clock in new[] { "rebirthTime", "totalPlaytime" })
                    Set(Get(Character, clock), "totalseconds", (double)Get(Get(Character, clock), "totalseconds") + 2d);
                var scheduler = Get(MainInstance, "_protectedScheduler");
                Set(scheduler, "_lastHeartbeat", (double)MainType.GetProperty("ProtectedHeartbeat", All).GetValue(null) - 2d);
                CallMain("GetProtectedStage", plan);
                Equal(1, Property(scheduler, "StageIndex"), label + " actual protected time selects the successor before handoff");
                var entry = Property(scheduler, "Entry");
                if (change == "route")
                {
                    if (itopod) Set(adventure, "itopodStart", (int)Get(adventure, "itopodStart") + 1);
                    else Set(adventure, "zone", 1);
                }
                if (change == "gear")
                {
                    var head = Get(Get(Character, "inventory"), "head");
                    Set(head, "curAttack", (float)Get(head, "curAttack") + 1f);
                }
                if (change == "support") SetActiveBeards(3);
                if (change == "lane")
                {
                    var evidence = Activator.CreateInstance(Plugin.GetType("NGUInjector.Managers.ResourceMutationResult", true), true);
                    Set(evidence, "Disposition", Enum.Parse(Field(evidence, "Disposition").FieldType, "ForeignConflict"));
                    Set(evidence, "AffectedLanes", 2); Set(evidence, "Reason", "fixture unresolved Magic authority");
                    Invoke(Get(Property(MainInstance, "_resourceSetupExecutor"), "Application"), "RecordUnresolved", Character, evidence);
                }
                var allocations = CallMain("GlobalPresetAllocationState"); var support = CallMain("GlobalPresetSupportState");
                var swaps = Get(typeof(Program), "Swaps"); var transfers = ResourceTransfers;
                var zones = new List<int>(); AdventureZoneEntryObserver = zones.Add;
                AdventureZoneEntryCompletedObserver = zone => {
                    if (change == "callback" && zone == -1) Set(adventure, "zone", 1);
                };
                CallMain("RunCustomPlanAutomation", plan);
                if (change == "none")
                {
                    var successor = Get(MainInstance, "_globalPresetReceipt");
                    Check(!ReferenceEquals(previous, successor), label + " successor must acquire a fresh receipt; waiting=" + Get(MainInstance, "_globalPresetWaitingReason"));
                    Equal(Get(MainInstance, "_customStageEquipmentKey"), Get(successor, "Owner"), label + " receipt belongs to the current stage");
                    Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), label + " successor has verified setup/funding/route");
                    Equal("-1," + route, string.Join(",", zones), label + " retires only the predecessor route then activates the successor once");
                    Equal(true, Get(scheduler, "_armed"), label + " successor earns time only after verified application");
                    CallMain("RunCustomPlanAutomation", plan);
                    Equal(2, zones.Count, label + " unchanged successor does not repeat route retirement or entry");
                }
                else
                {
                    Equal(change == "callback" ? "-1" : "", string.Join(",", zones), label + " foreign prerequisites are never retired/re-entered");
                    Equal(transfers, ResourceTransfers, label + " successor does not fund resources after failed handoff");
                    Equal(swaps, Get(typeof(Program), "Swaps"), label + " failed handoff never changes gear");
                    Equal(allocations, CallMain("GlobalPresetAllocationState"), label + " foreign lane allocations are retained");
                    Equal(support, CallMain("GlobalPresetSupportState"), label + " foreign support is retained");
                    Check(ReferenceEquals(previous, Get(MainInstance, "_globalPresetReceipt")), label + " failure cannot install a successor receipt");
                    Equal(false, Get(scheduler, "_armed"), label + " unverified successor earns no stage time");
                }
                Equal(entry, Property(scheduler, "Entry"), label + " handoff never restarts or skips a stage entry");
                Console.WriteLine("PASS scheduled route handoff " + label);
            }
            catch (Exception error) { failures.Add(label + ": " + error.Message); Console.Error.WriteLine("FAIL scheduled route handoff " + label + ": " + error); }
            finally { AdventureZoneEntryObserver = AdventureZoneEntryCompletedObserver = null; }
        }
        if (failures.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
    }

    private static object SetupCommitBudget(bool manual = false, bool itopod = false)
    {
        // targets=true deliberately leaves this fixture unacquired when scheduled=false.
        var plan = SetupResourceCoordination(false, true, "none", true);
        var stage = ((Array)Get(plan, "Stages")).GetValue(0);
        var library = Get(Get(MainType, "CustomPlanStore"), "<PresetLibrary>k__BackingField");
        var root = Invoke(library, "ReadExternalLibrary"); var id = (string)Get(stage, "PresetId");
        var row = ((IEnumerable)GetChildren(Json(root, "Presets"))).Cast<object>().Single(x => Property(Json(x, "Id"), "Value").Equals(id));
        SetJson(row, "Weights", JsonParse("[1,1,0,0,0,0,0,0,0,1,1,0]"));
        SetJson(row, "SupportMode", Json(JsonParse("{\"value\":\"planned\"}"), "value"));
        SetJson(row, "BeardPriority", JsonParse("[3]"));
        Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
        var inventory = Get(Character, "inventory"); var head = Get(inventory, "head"); var challenger = ((IList)Get(inventory, "inventory"))[0];
        Set(challenger, "id", 550); Set(challenger, "type", Get(head, "type")); Set(challenger, "bossRequired", 1); Set(challenger, "removable", true);
        Set(challenger, "curAttack", 1000000f); Set(challenger, "curDefense", 1000000f);
        Set(challenger, "capAttack", 1000000f); Set(challenger, "capDefense", 1000000f);
        Set(challenger, "spec1Type", Enum.Parse(Field(challenger, "spec1Type").FieldType, "NGU")); Set(challenger, "spec1Cur", 1000f);
        Set(Get(Get(Character, "buttons"), "adventure"), "m_Interactable", true);
        Set(Get(MainType, "Settings"), "_useCustomRebirthPlan", !manual);
        Set(MainInstance, "_invManager", Activator.CreateInstance(Plugin.GetType("NGUInjector.Managers.InventoryManager", true), true));
        if (manual) Set(Get(Character, "advancedTraining"), "levelTarget", new long[5]);
        if (itopod)
        {
            var adventure = Get(Character, "adventure"); var controller = Get(Character, "adventureController");
            Set(Get(inventory, "itemList"), "itopodKeyComplete", true); Set(adventure, "highestItopodLevel", 12);
            Set(Get(controller, "itopod"), "character", Character); Sequence(Get(adventure, "itopod"), "perkLevel", 100, 0L);
            var enemies = ListField(controller, "itopodEnemyList", 1); Set(enemies[0], "maxHP", 600f); Set(enemies[0], "defense", 10f);
        }
        Equal(null, Get(MainInstance, "_globalPresetReceipt"), "budget fixture starts before first acquisition");
        return plan;
    }

    private static void AssertCommitBudgetApplied(object plan, bool manual, int searches, int recalls, int transfers, bool expectFunding = true)
    {
        Equal(searches, PresetSearches, "commit never starts a replacement optimizer search");
        var receipt = Get(MainInstance, "_globalPresetReceipt");
        Check(receipt != null, "commit must complete with an observed receipt, not a partial latch: " + Get(MainInstance, "_globalPresetFailure"));
        var owner = (string)Get(receipt, "Owner");
        Equal(manual ? "dashboard" : Get(MainInstance, "_customStageEquipmentKey"), owner, "commit retains its actual acquisition owner");
        Equal(true, CallMain("GlobalPresetReceiptValid", owner), "expired commit owns a fully valid receipt");
        Equal(null, Get(MainInstance, "_presetExecutionBlock"), "expired cooperative budget never creates a mutation uncertainty block");
        Equal(null, Get(MainInstance, "_globalPresetWaitingEvent"), "completed commit does not remain in retry waiting");
        Check((int)Get(typeof(Program), "Recalls") <= recalls + 3, "only a gear footprint may recall lanes; resource application itself diffs exact amounts");
        Check(ResourceTransfers >= transfers && (!expectFunding || ResourceTransfers > transfers), "commit performs only the exact required resource model differences");
        var execution = Get(receipt, "Execution");
        CallMain("VerifyLoadoutPresetAllocations", execution);
        var at = Get(Character, "advancedTraining"); var energy = (long[])Get(at, "energy"); var targets = (long[])Get(at, "levelTarget");
        var expectedAt = (long[])Get(Get(execution, "Activities"), "AT");
        Check(energy.Take(2).All(x => x > 0), "both real ATAdventure bars remain funded after commit");
        Check(energy.SequenceEqual(expectedAt), "native AT allocations equal the exact committed execution");
        Equal(manual ? 0L : 20L, targets[0], "native toughness target survives successful commit");
        Equal(manual ? 0L : 10L, targets[1], "native power target survives successful commit");
        var ngu = Get(Character, "NGU");
        foreach (var lane in new[] { "energy", "magic" })
        {
            var actual = ((IList)Get(ngu, lane == "energy" ? "skills" : "magicSkills")).Cast<object>().Select(x => (long)Get(x, lane)).ToArray();
            var expected = (long[])Get(execution, lane == "energy" ? "EnergyShares" : "MagicShares");
            Check(expected[0] > 0 && actual.Take(expected.Length).SequenceEqual(expected) && actual.Skip(expected.Length).All(x => x == 0), "actual funded " + lane + " NGU targets match the receipt exactly");
        }
        Equal(10000L, (long)Get(Character, "idleEnergy") + energy.Sum() + ((IList)Get(ngu, "skills")).Cast<object>().Sum(x => (long)Get(x, "energy")), "commit conserves the actual energy pool");
        Equal(1000L, (long)Get(Get(Character, "magic"), "idleMagic") + ((IList)Get(ngu, "magicSkills")).Cast<object>().Sum(x => (long)Get(x, "magic")), "commit conserves the actual magic pool");
        Equal("applied", Property(Json(Get(MainInstance, "_loadoutPresetSnapshot"), "status"), "Value"), "successful commit publishes applied history");
        if (!manual)
        {
            Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), "expired scheduled commit verifies its stage receipt");
            Equal(true, Get(Get(MainInstance, "_protectedScheduler"), "_armed"), "only complete scheduled funding arms stage credit");
            Equal("\"Applied\"", Json(CallMain("GetResourceCoordinationRuntime"), "state").ToString(), "coordinator reports Applied, not NeedsUser");
        }
    }

    private static void CheckPresetCommitBudget()
    {
        var before = Assertions; var failures = new List<string>(); var cases = 0;
        Action<string, Action> run = (name, test) => {
            cases++;
            try { test(); Console.WriteLine("PASS preset commit budget " + name); }
            catch (Exception error) { failures.Add(name + ": " + error.Message); Console.Error.WriteLine("FAIL preset commit budget " + name + ": " + error.Message); }
            finally
            {
                PresetSearchObserver = PresetCaptureObserver = PresetPlanCompletedObserver = null;
                GearRenderObserver = RecallObserver = CaptureNativeObserver = null;
                PresetEvaluationObserver = null; BeardRefreshInterference = AtRefreshObserver = null;
                AutoBoostAction = null; FailAtRefresh = false;
            }
        };
        foreach (var boundary in new[] { "none", "gear", "support", "recall", "capture", "capture-native", "capture-itopod", "evaluation", "funding" })
        run("first scheduled acquisition/" + boundary, () => {
            var plan = SetupCommitBudget(itopod: boundary == "capture-itopod");
            var challenger = ((IList)Get(Get(Character, "inventory"), "inventory"))[0];
            var searches = PresetSearches; var recalls = (int)Get(typeof(Program), "Recalls"); var transfers = ResourceTransfers;
            var probe = new CommitBudgetProbe(boundary);
            CallMain("RunCustomPlanAutomation", plan);
            Equal(boundary != "none", probe.Fired, "requested real-time boundary is reached: " + boundary);
            Check(ReferenceEquals(probe.PlanningClock, probe.Captures[0]), "commit uses the original planning stopwatch");
            AssertCommitBudgetApplied(plan, false, searches + 1, recalls, transfers);
            Check(ReferenceEquals(challenger, Get(Get(Character, "inventory"), "head")), "commit actually equips the dominating searched item");
            Check(((IList)Get(Get(Character, "beards"), "activeBeards")).Cast<int>().SequenceEqual(new[] { 3 }), "commit actually applies planned native support");
            // End the operation observer before subsequent idempotent allocation ticks.
            PresetCaptureObserver = null;
            CallMain("RunCustomPlanAutomation", plan);
            Equal(searches + 1, PresetSearches, "completed acquisition never searches again on the next tick");
        });
        run("scheduled re-equip after own boost", () => {
            var plan = SetupCommitBudget(); CallMain("RunCustomPlanAutomation", plan);
            Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), "boost fixture first acquires its real scheduled policy; "
                + CallMain("GetCustomAutomationStatus") + "; " + Get(MainInstance, "_globalPresetReconcileException"));
            var oldReceipt = Get(MainInstance, "_globalPresetReceipt"); var calls = AutoBoostCalls;
            var head = Get(Get(Character, "inventory"), "head");
            AutoBoostAction = () => { Set(head, "curAttack", (float)Get(head, "curAttack") + 1); Invoke(typeof(Program), "RefreshBonuses"); };
            CallMain("RunCustomAutoBoost"); AutoBoostAction = null;
            Equal(calls + 1, AutoBoostCalls, "owned boost boundary actually executes");
            Equal(true, Get(MainInstance, "_customEquipmentApplicationInvalidated"), "own boost really requests AutoUpdate re-equip");
            var searches = PresetSearches; var recalls = (int)Get(typeof(Program), "Recalls"); var transfers = ResourceTransfers;
            var probe = new CommitBudgetProbe("capture"); CallMain("RunCustomPlanAutomation", plan);
            Equal(true, probe.Fired, "boost re-equip crosses actual-capture budget");
            AssertCommitBudgetApplied(plan, false, searches + 1, recalls, transfers, false);
            Check(!ReferenceEquals(oldReceipt, Get(MainInstance, "_globalPresetReceipt")), "boost restoration installs a fresh completed receipt");
            PresetCaptureObserver = null; CallMain("RunCustomPlanAutomation", plan);
            Equal(searches + 1, PresetSearches, "own boost causes at most one reoptimization");
        });
        foreach (var boundary in new[] { "none", "funding" })
        run("manual Preview + Apply consumes once/" + boundary, () => {
            var plan = SetupCommitBudget(manual: true); var stage = ((Array)Get(plan, "Stages")).GetValue(0);
            var library = Get(Get(MainType, "CustomPlanStore"), "<PresetLibrary>k__BackingField"); var root = Invoke(library, "ReadExternalLibrary");
            var args = JsonParse("{\"presetId\":\"" + Get(stage, "PresetId") + "\",\"libraryRevision\":\"" + Property(Json(root, "__externalUiRevision"), "Value") + "\"}");
            var searches = PresetSearches; var previewProbe = new CommitBudgetProbe("none");
            var preview = CallMain("PreviewGlobalPreset", args);
            Check(ReferenceEquals(previewProbe.PlanningClock, previewProbe.Captures[0]), "manual preview shares capture/search budget");
            var apply = JsonParse(args.ToString().TrimEnd('}') + ",\"previewId\":" + Json(preview, "previewId") + ",\"candidateKey\":\"balanced\"}");
            var recalls = (int)Get(typeof(Program), "Recalls"); var transfers = ResourceTransfers;
            var probe = new CommitBudgetProbe(boundary); Invoke(MainInstance, "ApplyGlobalPreset", apply, null);
            Equal(boundary != "none", probe.Fired, "manual Apply reaches the real funded AT-render boundary");
            Equal(null, probe.PlanningClock, "manual Apply replays the selected preview without Plan");
            AssertCommitBudgetApplied(plan, true, searches + 1, recalls, transfers);
            Equal(null, Get(MainInstance, "_loadoutPresetPreview"), "completed manual commit consumes preview token");
            var allocations = CallMain("GlobalPresetAllocationState"); recalls = (int)Get(typeof(Program), "Recalls"); transfers = ResourceTransfers;
            Throws(() => Invoke(MainInstance, "ApplyGlobalPreset", apply, null), "consumed", "same manual token cannot apply twice");
            Equal(recalls, Get(typeof(Program), "Recalls"), "duplicate Apply never recalls"); Equal(transfers, ResourceTransfers, "duplicate Apply never funds");
            Equal(allocations, CallMain("GlobalPresetAllocationState"), "duplicate Apply preserves committed allocations");
            Equal(searches + 1, PresetSearches, "manual Preview + Apply performs at most one search");
        });
        foreach (var boundary in new[] { "capture", "capture-native", "capture-itopod", "funding" })
        run("real allocation refresh/" + boundary, () => {
            var plan = SetupCommitBudget(itopod: boundary == "capture-itopod"); CallMain("RunCustomPlanAutomation", plan);
            Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), "refresh starts from a real acquisition");
            var receipt = Get(MainInstance, "_globalPresetReceipt"); var policy = Get(receipt, "Policy");
            var history = Get(MainInstance, "_loadoutPresetSnapshot").ToString();
            var ngu = ((IList)Get(Get(Character, "NGU"), "skills"))[0]; var amount = (long)Get(ngu, "energy");
            Check(amount > 1, "refresh fixture has actual funding to displace");
            var atEnergy = (long[])Get(Get(Character, "advancedTraining"), "energy");
            if (boundary == "funding") atEnergy[1]--; else Set(ngu, "energy", amount - 1);
            Set(Character, "idleEnergy", (long)Get(Character, "idleEnergy") + 1);
            Equal(false, CallMain("GlobalPresetStageReceiptValid", plan), "refresh cannot take the no-op branch");
            var searches = PresetSearches; var recalls = (int)Get(typeof(Program), "Recalls"); var transfers = ResourceTransfers;
            var probe = new CommitBudgetProbe(boundary);
            Set(MainInstance, "_customLastQuickResourceRefresh", DateTime.MinValue); CallMain("ReconcileCustomResources", plan);
            Equal(true, probe.Fired, "refresh reaches the requested actual boundary");
            Equal(null, probe.PlanningClock, "allocation refresh never invokes Plan");
            AssertCommitBudgetApplied(plan, false, searches, recalls, transfers);
            Equal(recalls, Get(typeof(Program), "Recalls"), "refresh changes only the displaced amount, never recalls either whole lane");
            Equal(amount, Get(ngu, "energy"), "refresh restores the exact displaced NGU amount");
            Check(ReferenceEquals(receipt, Get(MainInstance, "_globalPresetReceipt")) && ReferenceEquals(policy, Get(receipt, "Policy")), "refresh preserves acquired receipt/policy identity");
            Equal(history, Get(MainInstance, "_loadoutPresetSnapshot").ToString(), "refresh does not rewrite acquisition history");
        });
        foreach (var boundary in new[] { "preparation-capture", "preparation-plan" })
        run("preparation remains bounded/" + boundary, () => {
            var plan = SetupCommitBudget(); var recalls = Get(typeof(Program), "Recalls"); var swaps = Get(typeof(Program), "Swaps");
            var transfers = ResourceTransfers; var searches = PresetSearches;
            var support = CallMain("GlobalPresetSupportState"); var allocation = CallMain("GlobalPresetAllocationState");
            var targets = ((long[])Get(Get(Character, "advancedTraining"), "levelTarget")).ToArray();
            var probe = new CommitBudgetProbe(boundary); CallMain("RunCustomPlanAutomation", plan);
            Equal(true, probe.Fired, "preparation really crosses elapsed deadline");
            Equal(searches + (boundary == "preparation-plan" ? 1 : 0), PresetSearches, "preparation timeout only searches when capture completed");
            Equal(null, Get(MainInstance, "_globalPresetReceipt"), "timed-out preparation never acquires a receipt");
            Equal(null, Get(MainInstance, "_presetExecutionBlock"), "preparation timeout remains retryable, not NeedsUser");
            Equal("\"Waiting\"", Json(CallMain("GetResourceCoordinationRuntime"), "state").ToString(), "preparation timeout reports Waiting");
            Check(((string)Get(MainInstance, "_globalPresetWaitingReason")).Contains("time budget"), "waiting retains real deadline diagnostic");
            Check((double)Get(MainInstance, "_globalPresetRetryDelay") > 0 && (double)Get(MainInstance, "_globalPresetRetryDelay") <= 30, "preparation timeout installs bounded retry backoff");
            Equal(false, Get(Get(MainInstance, "_protectedScheduler"), "_armed"), "preparation timeout earns no credit");
            var captures = PresetCaptures; searches = PresetSearches;
            CallMain("RunCustomPlanAutomation", plan);
            Equal(captures, PresetCaptures, "immediate waiting tick respects capture backoff"); Equal(searches, PresetSearches, "immediate waiting tick respects search backoff");
            Equal(recalls, Get(typeof(Program), "Recalls"), "preparation timeout performs zero native recalls");
            Equal(swaps, Get(typeof(Program), "Swaps"), "preparation timeout performs zero native swaps");
            Equal(transfers, ResourceTransfers, "preparation timeout performs zero resource transfers");
            Equal(support, CallMain("GlobalPresetSupportState"), "preparation timeout preserves all native support");
            Equal(allocation, CallMain("GlobalPresetAllocationState"), "preparation timeout preserves actual allocations");
            Check(targets.SequenceEqual((long[])Get(Get(Character, "advancedTraining"), "levelTarget")), "preparation timeout writes no AT menu targets");
        });
        foreach (var refresh in new[] { false, true })
        run("foreign native target during render blocks dependent execution/refresh=" + refresh, () => {
            var plan = SetupCommitBudget();
            if (refresh)
            {
                CallMain("RunCustomPlanAutomation", plan);
                Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), "negative refresh first acquires real receipt");
                ((long[])Get(Get(Character, "advancedTraining"), "energy"))[1]--;
                Set(Character, "idleEnergy", (long)Get(Character, "idleEnergy") + 1);
            }
            var rendered = false; var targets = (long[])Get(Get(Character, "advancedTraining"), "levelTarget");
            AtRefreshObserver = () => {
                if (!((long[])Get(Get(Character, "advancedTraining"), "energy")).Take(2).All(x => x > 0)) return;
                rendered = true; AtRefreshObserver = null;
                Check(((long[])Get(Get(Character, "advancedTraining"), "energy")).Take(2).All(x => x > 0), "negative render exception follows actual funding");
                targets[1] = 777;
            };
            FailAtRefresh = true;
            if (refresh) { Set(MainInstance, "_customLastQuickResourceRefresh", DateTime.MinValue); CallMain("ReconcileCustomResources", plan); }
            else CallMain("RunCustomPlanAutomation", plan);
            FailAtRefresh = false;
            Equal(true, rendered, "negative control reaches native rendering, not an injected timeout");
            Check(Get(MainInstance, "_globalPresetReceipt") != null, "model-committed accounting retains acquired policy independently of verification");
            Equal(false, CallMain("GlobalPresetStageReceiptValid", plan), "foreign target cannot retain full applied ownership or dependent credit");
            Equal("\"NeedsUser\"", Json(CallMain("GetResourceCoordinationRuntime"), "state").ToString(), "arbitrary native exception still requires acknowledgement");
            Check(((string)CallMain("GetCustomAutomationStatus")).Contains("fixture native AT rendering failure"), "failure retains original native-render cause");
            Equal(20L, targets[0], "foreign setting retains CURRENT mandatory companion prerequisite"); Equal(777L, targets[1], "failure preserves newer foreign target");
            Equal(false, Get(Get(MainInstance, "_protectedScheduler"), "_armed"), "partial mutation never earns stage credit");
            var searches = PresetSearches; var captures = PresetCaptures; var recalls = Get(typeof(Program), "Recalls"); var transfers = ResourceTransfers;
            var allocation = CallMain("GlobalPresetAllocationState");
            var planningCaptures = Get(MainInstance, "_presetPlanningCaptures");
            for (var tick = 0; tick < 2; tick++)
            {
                CallMain("RunCustomPlanAutomation", plan);
                Set(MainInstance, "_customLastQuickResourceRefresh", DateTime.MinValue); CallMain("ReconcileCustomResources", plan);
            }
            Equal(searches, PresetSearches, "foreign target never authorizes a replacement search");
            Equal(planningCaptures, Get(MainInstance, "_presetPlanningCaptures"), "healthy lane continuation uses cheap observations, never broad planning capture");
            Equal(recalls, Get(typeof(Program), "Recalls"), "arbitrary failure never automatically recalls again"); Equal(transfers, ResourceTransfers, "arbitrary failure never automatically funds again");
            Equal(allocation, CallMain("GlobalPresetAllocationState"), "latched ticks preserve partial native state"); Equal(777L, targets[1], "latched ticks preserve foreign target");
            Equal(false, CallMain("GlobalPresetStageReceiptValid", plan), "subsequent healthy resource passes cannot clear foreign setting review");
        });
        Console.WriteLine("Preset commit budget: " + cases + " cases, " + (Assertions - before) + " assertions, " + failures.Count + " failures");
        if (failures.Count > 0) throw new Exception("Preset commit budget failures:\n" + string.Join("\n", failures));
    }

    private static object SetupResourceCoordination(bool scheduled, bool autoUpdate, string demand, bool targets = false)
    {
        Invoke(typeof(Program), "Setup");
        Character = Get(typeof(Program), "Character"); Controller = Get(typeof(Program), "Controller"); MainInstance = Get(typeof(Program), "MainInstance");
        PrepareSupport(); SetActiveBeards();
        // The shared fresh-Character seam isolates checkpoints for every preset case.
        // Deliberate reloads retain that directory and do not call Setup.
        Set(MainInstance, "_globalPresetPolicies", Activator.CreateInstance(Field(MainInstance, "_globalPresetPolicies").FieldType));
        var settings = Get(MainType, "Settings");
        AllowPresetSettingsSave = true;
        Set(settings, "_savePath", Path.Combine((string)Get(MainType, "_dir"), "coordination-settings.json"));
        Set(settings, "_manualAdventureEnabled", false); Set(settings, "_customAdventureEnabled", false);
        // The shared fresh-Character seam already entered native safe mode. Do not
        // reset a route here: preparation helpers can also run inside timelines.
        Equal(-1, Get(Get(Character, "adventure"), "zone"), "resource fixture enters observed native safe mode");
        var ngu = Get(Character, "NGU"); Set(ngu, "autoAdvance", false);
        ListField(ngu, "skills", 11); ListField(ngu, "magicSkills", 11);
        Set(Get(Get(Character, "buttons"), "ngu"), "m_Interactable", true);
        Set(Get(Get(Character, "buttons"), "bloodMagic"), "m_Interactable", true);
        // Keep the pool below each NGU black-bar cap so allocation conservation is exact.
        Set(Character, "energyPower", 1f);
        var magic = Get(Character, "magic");
        Set(magic, "magicPower", 1f); Set(magic, "capMagic", 1000L); Set(magic, "curMagic", 1000L); Set(magic, "idleMagic", 1000L);
        var blood = Get(Character, "bloodMagicController");
        Sequence(blood, "normalSpeedDividers", 1, .002f);
        var ritual = ((Array)Get(blood, "bloodMagics")).GetValue(0);
        Set(ritual, "baseBoost", 10L); Set(ritual, "baseCost", 1f);
        Set(((IList)Get(Get(Character, "bloodMagic"), "ritual"))[0], "boss", 0);
        var spells = Child(Character, "bloodSpells"); Set(spells, "character", Character); Set(blood, "spells", spells);
        Set(spells, "adventureSpellCooldown", 3600); Set(Child(spells, "tooltip"), "character", Character);
        Set(Child(Get(Character, "bloodMagic"), "adventureSpellTime"), "totalseconds", 10000d);
        Set(Get(Character, "hacksController"), "character", Character); Set(Child(Character, "hacks"), "disabled", true);
        var perks = Get(Character, "beastQuestPerkController"); Set(perks, "character", Character);
        Sequence(perks, "quirkDifficultyReq", 92, Enum.Parse(Game.GetType("difficulty", true), "normal"));
        Sequence(perks, "effectPerLevel", 92, 0f); Sequence(Child(Character, "beastQuest"), "quirkLevel", 92, 0L);
        Set(Get(Character, "advancedTraining"), "transferredBankedLevels", true);
        Set(Get(Character, "advancedTraining"), "levelTarget", Enumerable.Repeat(-1L, 5).ToArray());
        var store = Get(MainType, "CustomPlanStore"); var library = Get(store, "<PresetLibrary>k__BackingField");
        var root = Invoke(library, "ReadExternalLibrary");
        var id = "coordination-" + Guid.NewGuid().ToString("N");
        Invoke(Json(root, "Presets"), "Add", "", JsonParse("{\"Id\":\"" + id + "\",\"Name\":\"Resource coordination\",\"Weights\":"
            + (targets ? "[1,1,0,0,0,0,0,0,0,1]" : "[1,1,0,0,0,0]")
            + ",\"MagicBloodPercent\":0,\"SupportMode\":\"current\",\"SwapSeconds\":0,\"AutoUpdate\":" + (autoUpdate || targets ? "true" : "false") + "}"));
        var revision = Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
        var plan = Invoke(store, "Load", "fixture");
        var stage = Activator.CreateInstance(Plugin.GetType("NGUInjector.CustomPlans.CustomPlanStage", true));
        Set(stage, "Name", "Resource coordination stage"); Set(stage, "PresetId", id); Set(stage, "Percent", 100d); Set(stage, "DurationSeconds", 600);
        Set(plan, "StageDurationSeconds", 7200); // Ordinary catchup cases are not short-rebirth suppression cases.
        if (targets)
        {
            Set(stage, "CompletionMode", 1); Set(stage, "TargetAdvancedTrainingPower", 10L); Set(stage, "TargetAdvancedTrainingToughness", 20L);
        }
        var stages = Array.CreateInstance(stage.GetType(), 1); stages.SetValue(stage, 0);
        Set(plan, "Stages", stages); Set(plan, "StageSchedulerEnabled", true); Set(plan, "StageLoopStartIndex", -1);
        Set(plan, "AutoActivateFruits", false); Set(plan, "AutoRebirthEnabled", false); Set(plan, "TimeTriggerEnabled", false); Set(plan, "AutoIronPillEnabled", false);
        var bloodGoals = demand == "blood-goals" || demand == "both";
        Set(plan, "AutoBloodSpellsEnabled", bloodGoals); Set(plan, "BloodSpaghettiGoalPercent", bloodGoals ? 50 : 0);
        Set(settings, "_customAutoBloodSpellsEnabled", bloodGoals); Set(settings, "_customBloodSpaghettiGoalPercent", bloodGoals ? 50 : 0);
        Set(MainInstance, "_customRuntimePlanCache", plan); Set(MainInstance, "_customRuntimePlanCacheName", Property(settings, "CustomPlanFile"));
        Set(MainInstance, "_customRuntimePlanCacheLoadedAt", DateTime.Now);
        if (demand == "iron-pill" || demand == "both")
        {
            Set(MainInstance, "_customIronPillPlanName", Property(settings, "CustomPlanFile"));
            Set(MainInstance, "_customIronPillPrepStartedAt", (double)Get(Get(Character, "rebirthTime"), "totalseconds"));
        }
        var args = JsonParse("{\"presetId\":\"" + id + "\",\"libraryRevision\":\"" + revision + "\"}");
        if (!targets)
        {
            var preview = CallMain("PreviewGlobalPreset", args);
            var apply = JsonParse(args.ToString().TrimEnd('}') + ",\"previewId\":" + Json(preview, "previewId") + ",\"candidateKey\":\"current\"}");
            Invoke(MainInstance, "ApplyGlobalPreset", apply, null);
        }
        // Dashboard acquisition invalidates the selected-plan cache through real settings setters.
        Set(MainInstance, "_customRuntimePlanCache", plan); Set(MainInstance, "_customRuntimePlanCacheName", Property(settings, "CustomPlanFile"));
        Set(MainInstance, "_customRuntimePlanCacheLoadedAt", DateTime.Now);
        if (scheduled)
        {
            Set(settings, "_useCustomRebirthPlan", true);
            CallMain("RunCustomPlanAutomation", plan);
            RequireInitialScheduledReceipt(plan, "resource coordination/targets=" + targets + "/AutoUpdate=" + autoUpdate);
        }
        if (targets && !autoUpdate)
        {
            // Seed mandatory-target context through real scheduled acquisition, then replay with search disabled.
            root = Invoke(library, "ReadExternalLibrary");
            var row = ((IEnumerable)GetChildren(Json(root, "Presets"))).Cast<object>().Single(x => Property(Json(x, "Id"), "Value").Equals(id));
            SetJson(row, "AutoUpdate", Json(JsonParse("{\"value\":false}"), "value"));
            Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
            Invoke(MainInstance, "EquipGlobalPresetDefinition", Invoke(library, "Get", id), Get(MainInstance, "_customStageEquipmentKey"), "explicit-equip", false, null, stage, 600);
        }
        return plan;
    }

    private static void RequireInitialScheduledReceipt(object plan, string context)
    {
        var path = (string)MainType.GetProperty("ProtectedCheckpointPath", All).GetValue(MainInstance);
        var receipt = Get(MainInstance, "_globalPresetReceipt");
        if (receipt != null && Equals(Get(receipt, "Owner"), Get(MainInstance, "_customStageEquipmentKey")))
        {
            Check(path != null && File.Exists(path), context + " initial scheduled acquisition has a real checkpoint");
            var selection = Json(JsonParse(File.ReadAllText(path)), "selectedPolicy");
            Equal(Get(Get(receipt, "Policy"), "Acquisition"),
                Property(Json(selection, "acquisition"), "Value"), context + " initial checkpoint retains the applied acquisition");
            return;
        }
        var scheduler = Get(MainInstance, "_protectedScheduler");
        throw new InvalidOperationException("Initial scheduled acquisition failed/" + context
            + "; waiting=" + Get(MainInstance, "_globalPresetWaitingReason")
            + "; persistence=" + Get(MainInstance, "_protectedPersistenceError")
            + "; recovery=" + Get(MainInstance, "_protectedRecoveryReason")
            + "; quarantine=" + Get(MainInstance, "_protectedSelectedPolicyIssue")
            + "; execution=" + Get(MainInstance, "_resourceExecutionReason")
            + "; failure=" + Get(MainInstance, "_globalPresetFailure")
            + "; caughtException=" + Get(MainInstance, "_globalPresetReconcileException")
            + "; checkpoint=" + path
            + "; scheduler=" + (scheduler == null ? "null" : Invoke(scheduler, "ExportState").ToString())
            + (Environment.GetEnvironmentVariable("NGU_PRESET_ADMISSION_DIAGNOSTICS") == "1"
                ? "; checkpointJson=" + (path != null && File.Exists(path) ? File.ReadAllText(path) : "<absent>") : ""));
    }

    private static void TracePresetAdmission(object plan, string context)
    {
        // Independent probes must not replace the first native/admission failure.
        Action<string, Func<object>> probe = (name, read) => {
            try { Console.WriteLine("Admission/" + context + "/" + name + "=" + read()); }
            catch (Exception error) { Console.WriteLine("Admission/" + context + "/" + name + "=<" + error.Message + ">"); }
        };
        probe("root", () => "waiting=" + Get(MainInstance, "_globalPresetWaitingReason")
            + "; failure=" + Get(MainInstance, "_globalPresetFailure")
            + "; exception=" + Get(MainInstance, "_globalPresetReconcileException")
            + "; zone=" + Get(Get(Character, "adventure"), "zone"));
        if (Environment.GetEnvironmentVariable("NGU_PRESET_ADMISSION_DIAGNOSTICS") != "1") return;
        probe("status", () => CallMain("GetCustomAutomationStatus"));
        probe("resource", () => CallMain("GetResourceCoordinationRuntime"));
        foreach (var field in new[] { "_customStageEquipmentKey", "_globalPresetWaitingEvent", "_globalPresetWaitingReason",
            "_globalPresetFailure", "_protectedRecoveryReason", "_protectedPersistenceError", "_protectedSelectedPolicyIssue",
            "_globalPresetApplyingPolicy", "_globalPresetPendingSearch", "_globalPresetReceipt" })
            probe(field, () => Get(MainInstance, field) ?? "<null>");
        probe("scheduler", () => Invoke(Get(MainInstance, "_protectedScheduler"), "ExportState"));
        probe("progress", () => ReadPresetPlanProgress(plan));
        probe("baselineAuthority", () => CallMain("ManualAdventureBaselineAuthorized"));
        probe("baselineOwner", () => Get(Get(MainInstance, "_manualAdventureBaseline"), "Owner"));
        probe("readiness", () => {
            var stage = ((Array)Get(plan, "Stages")).GetValue(0);
            var id = CallMain("StageGlobalPresetId", plan, stage);
            var preset = Invoke(Get(Get(MainType, "CustomPlanStore"), "<PresetLibrary>k__BackingField"), "Get", id);
            return CallMain("GlobalPresetReadiness", preset, stage, null) ?? "ready";
        });
        probe("native", () => "zone=" + Get(Get(Character, "adventure"), "zone")
            + "; boss=" + CallMain("CustomBossFightActive") + "; support=" + CallMain("GlobalPresetSupportState")
            + "; allocations=" + CallMain("GlobalPresetAllocationState")
            + "; searches=" + PresetSearches + "; captures=" + PresetCaptures + "; transfers=" + ResourceTransfers
            + "; swaps=" + Get(typeof(Program), "Swaps") + "; pods=" + PodRefreshCalls);
        probe("checkpoint", () => {
            var path = (string)MainType.GetProperty("ProtectedCheckpointPath", All).GetValue(MainInstance);
            return path + ":" + (path != null && File.Exists(path) ? File.ReadAllText(path) : "<absent>");
        });
    }

    private static void TraceFreshScheduledOverlay(object plan, string context)
    {
        TracePresetAdmission(plan, context);
        // This failing boundary needs exact state even without verbose inventory /
        // checkpoint diagnostics. Each read is isolated so evidence cannot hide
        // the original acquisition assertion or a native getter failure.
        Action<string, Func<object>> probe = (name, read) => {
            try { Console.WriteLine("FreshOverlay/" + context + "/" + name + "=" + read()); }
            catch (Exception error) { Console.WriteLine("FreshOverlay/" + context + "/" + name + "=<" + error.Message + ">"); }
        };
        probe("resource", () => CallMain("GetResourceCoordinationRuntime"));
        probe("owner", () => Get(MainInstance, "_customStageEquipmentKey"));
        probe("receiptOwner", () => Get(Get(MainInstance, "_globalPresetReceipt"), "Owner"));
        probe("scheduler", () => Invoke(Get(MainInstance, "_protectedScheduler"), "ExportState"));
        probe("prerequisite", () => CallMain("GlobalPresetStageReceiptValid", plan));
        probe("applicationIssue", () => CallMain("GetGlobalPresetApplicationIssue", Get(MainInstance, "_customStageEquipmentKey"), true, true));
        probe("claim", () => "active=" + Property(MainInstance, "ManualAdventureOverlayActive")
            + "; markedZone=" + Get(MainInstance, "_manualAdventureOverlayRouteZone")
            + "; gearMatches=" + CallMain("ManualAdventureOverlayGearMatches")
            + "; baselineAuthorized=" + CallMain("ManualAdventureBaselineAuthorized"));
        probe("native", () => "support=" + CallMain("GlobalPresetSupportState")
            + "; allocations=" + CallMain("GlobalPresetAllocationState")
            + "; ATlevels=" + string.Join(",", (long[])Get(Get(Character, "advancedTraining"), "level"))
            + "; ATtargets=" + string.Join(",", (long[])Get(Get(Character, "advancedTraining"), "levelTarget"))
            + "; searches=" + PresetSearches + "; captures=" + PresetCaptures
            + "; transfers=" + ResourceTransfers + "; swaps=" + Get(typeof(Program), "Swaps"));
    }

    private static void CheckResourceCoordination()
    {
        var before = Assertions; var failures = new List<string>();
        Action<string, Action> run = (name, test) => {
            try { test(); }
            catch (Exception error) { failures.Add(name + ": " + error.Message); Console.Error.WriteLine("FAIL resource coordination " + name + ": " + error); }
            finally { PresetEvaluationObserver = null; FailAtRefresh = false; FailAtTargetText = false; AtRefreshObserver = null; }
        };
        run("complete allocation map rejects unplanned destinations", () => {
            SetupResourceCoordination(false, false, "none");
            var receipt = Get(MainInstance, "_globalPresetReceipt"); var candidate = Invoke(Get(receipt, "Execution"), "Copy");
            var activities = Get(candidate, "Activities") ?? New("PresetActivityAllocation"); var wishes = (IList)Get(Get(Character, "wishes"), "wishes");
            var machine = Get(Character, "machine"); var wandoos = Get(Character, "wandoos98");
            var at = (long[])Get(Get(Character, "advancedTraining"), "energy");
            var augments = (IList)Get(Get(Character, "augments"), "augs");
            var destinations = new List<Tuple<string, Action<long>, Func<long>>>();
            foreach (var item in new[] {
                Tuple.Create(machine, "speedEnergy"), Tuple.Create(machine, "goldMultiMagic"),
                Tuple.Create(wandoos, "wandoosEnergy"), Tuple.Create(wandoos, "wandoosMagic") })
                destinations.Add(Tuple.Create(item.Item2, (Action<long>)(amount => Set(item.Item1, item.Item2, amount)), (Func<long>)(() => (long)Get(item.Item1, item.Item2))));
            for (var i = 0; i < at.Length; i++)
            {
                var index = i;
                destinations.Add(Tuple.Create("AT/" + index, (Action<long>)(amount => at[index] = amount), (Func<long>)(() => at[index])));
            }
            foreach (var augment in augments.Cast<object>())
            foreach (var field in new[] { "augEnergy", "upgradeEnergy" })
                destinations.Add(Tuple.Create("augment/" + field, (Action<long>)(amount => Set(augment, field, amount)), (Func<long>)(() => (long)Get(augment, field))));
            foreach (var index in new[] { 0, wishes.Count - 1 })
            foreach (var field in new[] { "energy", "magic" })
            {
                var wish = wishes[index];
                destinations.Add(Tuple.Create("wish/" + index + "/" + field, (Action<long>)(amount => Set(wish, field, amount)), (Func<long>)(() => (long)Get(wish, field))));
            }
            var recalls = Get(typeof(Program), "Recalls"); var transfers = ResourceTransfers; var captures = PresetCaptures;
            var observed = Get(receipt, "Allocations");
            foreach (var nullActivities in new[] { false, true })
            {
                Set(candidate, "Activities", nullActivities ? null : activities);
                Equal(true, CallMain("LoadoutPresetAllocationsMatch", candidate), "clean complete map matches with null Activities=" + nullActivities);
                foreach (var destination in destinations)
                foreach (var amount in new[] { 1L, -1L })
                {
                    Equal(0L, destination.Item3(), destination.Item1 + " begins empty");
                    destination.Item2(amount);
                    try
                    {
                        Equal(false, CallMain("LoadoutPresetAllocationsMatch", candidate), destination.Item1 + " unexpected allocation rejects no-op, null Activities=" + nullActivities);
                        Throws(() => CallMain("VerifyLoadoutPresetAllocations", candidate), "application receipt rejected", destination.Item1 + " unexpected allocation rejects receipt");
                        Equal(amount, destination.Item3(), destination.Item1 + " verification never rewrites foreign allocation");
                        Equal(observed, Get(receipt, "Allocations"), "verification never adopts a foreign map into the receipt");
                    }
                    finally { destination.Item2(0L); }
                    Equal(true, CallMain("LoadoutPresetAllocationsMatch", candidate), destination.Item1 + " restored map matches exactly");
                }
                Set(wishes[0], "res3", 17L);
                try { Equal(true, CallMain("LoadoutPresetAllocationsMatch", candidate), "energy/magic map does not claim unrelated R3 wishes"); }
                finally { Set(wishes[0], "res3", 0L); }
            }
            Equal(recalls, Get(typeof(Program), "Recalls"), "complete-map verification never recalls resources");
            Equal(transfers, ResourceTransfers, "complete-map verification never transfers resources");
            Equal(captures, PresetCaptures, "complete-map verification never searches or captures");
        });
        foreach (var energy in new[] { false, true })
        run("foreign wish prevents no-op/energy=" + energy, () => {
            var plan = SetupResourceCoordination(false, false, "none"); var receipt = Get(MainInstance, "_globalPresetReceipt");
            var wish = ((IList)Get(Get(Character, "wishes"), "wishes"))[0]; var field = energy ? "energy" : "magic";
            var pool = energy ? Character : Get(Character, "magic"); var current = energy ? "curEnergy" : "curMagic"; var idle = energy ? "idleEnergy" : "idleMagic";
            // Actual pool growth must not conceal an unplanned wish or be clipped
            // back to the prospective equipment cap during current-policy replay.
            Set(pool, current, (long)Get(pool, current) + 1); Set(wish, field, 1L);
            Equal(false, CallMain("LoadoutPresetAllocationsMatch", Get(receipt, "Execution")), "matching NGU amounts cannot conceal a foreign wish");
            var recalls = (int)Get(typeof(Program), "Recalls"); var transfers = ResourceTransfers;
            Set(MainInstance, "_customLastQuickResourceRefresh", DateTime.MinValue); CallMain("ReconcileCustomResources", plan);
            Equal(recalls, Get(typeof(Program), "Recalls"), "unplanned wish is removed by a target diff, never a whole-lane recall");
            Check(ResourceTransfers > transfers, "foreign wish repair executes real exact transfers");
            Equal(0L, Get(wish, field), "native recall clears unplanned wish allocation");
            Equal(0L, Get(pool, idle), "current-policy replay funds the actual grown pool instead of clipping it to the equipment cap");
            Equal(Get(pool, current), ((IList)Get(Get(Character, "NGU"), energy ? "skills" : "magicSkills")).Cast<object>()
                .Sum(skill => (long)Get(skill, field)), "wish release conserves every unit of actual current resources");
            Equal(true, CallMain("LoadoutPresetAllocationsMatch", Get(receipt, "Execution")), "repaired complete allocation map matches");
            Equal(true, CallMain("GlobalPresetReceiptValid", "dashboard"), "only repaired map receives valid ownership");
            transfers = ResourceTransfers;
            Set(MainInstance, "_customLastQuickResourceRefresh", DateTime.MinValue); CallMain("ReconcileCustomResources", plan);
            Equal(recalls, Get(typeof(Program), "Recalls"), "repaired complete map is then idempotent");
            Equal(transfers, ResourceTransfers, "idempotent repaired map performs no transfers");
        });
        foreach (var edit in new[] { "none", "current", "companion", "both" })
        run("AT lease rendering failure/" + edit, () => {
            var plan = SetupResourceCoordination(true, false, "none", true);
            var at = Get(Character, "advancedTraining"); var targets = (long[])Get(at, "levelTarget"); var energy = (long[])Get(at, "energy");
            var first = energy[0]; var second = energy[1]; var idle = (long)Get(Character, "idleEnergy");
            Check(first > 0 && second > 0, "AT cleanup fixture funds both owned bars");
            Equal(20L, targets[0], "cleanup owns toughness target"); Equal(10L, targets[1], "cleanup owns power target");
            var editCurrent = edit == "current" || edit == "both"; var editCompanion = edit == "companion" || edit == "both";
            var calls = new List<long[]>(); var recalls = Get(typeof(Program), "Recalls"); var transfers = ResourceTransfers;
            AtRefreshObserver = () => {
                calls.Add(new[] { energy[0], energy[1], (long)Get(Character, "idleEnergy") });
                if (calls.Count != 1) return;
                if (editCurrent) targets[0] = 777;
                if (editCompanion) targets[1] = 888;
            };
            Exception failure = null;
            FailAtTargetText = true;
            try
            {
                Invoke(Get(MainType, "Settings"), "set_UseCustomRebirthPlan", false);
                CallMain("ReleaseGlobalPresetNativeTargets"); CallMain("ServiceExecutionNativeSettings");
            }
            catch (Exception error) { failure = error; }
            finally { FailAtTargetText = false; AtRefreshObserver = null; }
            var application = Get(Property(MainInstance, "_resourceSetupExecutor"), "Application");
            var outcome = Property(application, "Last");
            Check((failure?.ToString() ?? Convert.ToString(Get(outcome, "Error"))).Contains("fixture native AT rendering failure"), "AT cleanup retains the actual native rendering failure after committed accounting");
            Equal(true, Property(application, "PresentationPending"), "AT return rendering remains pending independently of committed model transfers");
            Equal(editCompanion ? 1 : 2, calls.Select(values => string.Join(",", values)).Distinct().Count(), "AT rendering observes exactly one committed accounting state per still-owned bar");
            Equal(0L, calls[0][0], "first AT recall commits before rendering throws");
            Equal(idle + first, calls[0][2], "first AT recall credits idle before rendering throws");
            if (!editCompanion)
            {
                Equal(0L, calls[1][1], "companion AT recall also commits before its rendering failure");
                Equal(idle + first + second, calls[1][2], "companion return conserves exact idle energy");
                Check(calls.Skip(1).All(values => values.SequenceEqual(calls[1])), "presentation retries observe the same already committed accounting");
            }
            Equal(editCurrent ? 777L : -1L, targets[0], "cleanup restores still-owned first menu field but preserves callback edit");
            Equal(editCompanion ? 888L : -1L, targets[1], "cleanup restores still-owned companion menu field but preserves callback edit");
            Equal(0L, energy[0], "committed first AT resource return is never rolled back");
            Equal(editCompanion ? second : 0L, energy[1], "newer companion target protects its allocation from old-lease recall");
            Equal(idle + first + (editCompanion ? 0 : second), Get(Character, "idleEnergy"), "cleanup conserves all actually returned energy");
            Equal(null, Get(MainInstance, "_globalPresetNativeTargets"), "serviced or foreign target lease is retired even on rendering failure");
            Equal(false, CallMain("GlobalPresetStageReceiptValid", plan), "failed cleanup never claims valid target ownership");
            CallMain("ReleaseGlobalPresetNativeTargets");
            Equal(editCurrent ? 777L : -1L, targets[0], "repeated cleanup never overwrites first callback edit");
            Equal(editCompanion ? 888L : -1L, targets[1], "repeated cleanup never overwrites companion callback edit");
            Equal(recalls, Get(typeof(Program), "Recalls"), "target cleanup never recalls an entire resource lane");
            Equal(transfers + (editCompanion ? 1 : 2), ResourceTransfers, "target cleanup performs exactly one zeroing model write per still-owned AT bar, never funds an activity");
        });
        foreach (var scheduled in new[] { false, true })
        foreach (var autoUpdate in new[] { false, true })
        foreach (var demand in new[] { "none", "iron-pill", "blood-goals", "both" })
        {
            // These field-driven demand cases use stage prep; dashboard activation is tested separately below.
            if (!scheduled && (demand == "iron-pill" || demand == "both")) continue;
            var label = (scheduled ? "stage" : "dashboard") + "/AutoUpdate=" + autoUpdate + "/" + demand;
            run(label, () => {
                var plan = SetupResourceCoordination(scheduled, autoUpdate, demand);
                var settings = Get(MainType, "Settings"); var receipt = Get(MainInstance, "_globalPresetReceipt");
                Check(receipt != null, label + " acquires receipt: " + CallMain("GetCustomAutomationStatus"));
                var owner = (string)Get(receipt, "Owner"); var policy = Get(receipt, "Policy"); var acquired = Get(policy, "Candidate");
                Equal(autoUpdate, Get(Get(policy, "Preset"), "AutoUpdate"), label + " exercises acquired AutoUpdate flag");
                Equal(true, CallMain("GlobalPresetReceiptValid", owner), label + " initial ownership verifies");
                var ritual = ((IList)Get(Get(Character, "bloodMagic"), "ritual"))[0];
                var energy = (IList)Get(Get(Character, "NGU"), "skills"); var magic = (IList)Get(Get(Character, "NGU"), "magicSkills");
                Equal(demand == "none" ? 0L : 101L, Get(ritual, "magic"), label + " initial acquisition composes demand");
                Equal(demand == "none" ? 1000L : 899L, Get(magic[0], "magic"), label + " initial acquisition conserves the shared pool");
                Equal(0L, Get(acquired, "Blood"), label + " acquisition candidate excludes temporary blood demand");
                var history = Get(MainInstance, "_loadoutPresetSnapshot").ToString();
                var definition = Invoke(Get(policy, "ExecutionPreset"), "ToJson").ToString();
                var acquiredEnergyWeights = ((int[])Get(policy, "EnergyWeights")).ToArray(); var acquiredMagicWeights = ((int[])Get(policy, "MagicWeights")).ToArray();
                var acquiredShares = ((long[])Get(acquired, "MagicShares")).ToArray(); var material = ((string[])Get(policy, "Material")).ToArray();
                var searches = PresetSearches; var swaps = Get(typeof(Program), "Swaps");
                Action refresh = () => { Set(MainInstance, "_customLastQuickResourceRefresh", DateTime.MinValue); CallMain("ReconcileCustomResources", plan); };
                for (var selection = 1; selection <= 2; selection++)
                {
                    var ew = new int[9]; var mw = new int[7]; ew[selection] = 3; mw[selection] = 2;
                    Set(settings, "_energyNguWeights", ew); Set(settings, "_magicNguWeights", mw);
                    Equal(false, CallMain("GlobalPresetReceiptValid", owner), label + " pending ratios cannot claim current ownership");
                    var recalls = (int)Get(typeof(Program), "Recalls"); var magicRecalls = (int)Get(typeof(Program), "MagicRecalls");
                    var reentered = false;
                    PresetEvaluationObserver = (candidate, request) => {
                        if (reentered) return;
                        reentered = true; CallMain("ReconcileCustomResources", plan);
                    };
                    try { refresh(); } finally { PresetEvaluationObserver = null; }
                    Equal(true, reentered, label + " exercises a reentrant coordinator callback");
                    Equal(recalls - magicRecalls, (int)Get(typeof(Program), "Recalls") - (int)Get(typeof(Program), "MagicRecalls"), label + " energy preemption uses only an exact desired-map diff");
                    Equal(magicRecalls, Get(typeof(Program), "MagicRecalls"), label + " magic preemption uses only an exact desired-map diff");
                    Equal(0L, Get(energy[selection - 1], "energy"), label + " clears old energy selection");
                    Equal(0L, Get(magic[selection - 1], "magic"), label + " clears old magic selection");
                    Equal(10000L, Get(energy[selection], "energy"), label + " applies latest energy selection on next refresh");
                    Equal(demand == "none" ? 1000L : 899L, Get(magic[selection], "magic"), label + " composes demand on latest magic baseline");
                    Equal(demand == "none" ? 0L : 101L, Get(ritual, "magic"), label + " demand never stacks across refreshes");
                    Equal(true, CallMain("GlobalPresetReceiptValid", owner), label + " refreshed receipt verifies");
                    Check(((int[])Get(receipt, "AppliedEnergyWeights")).SequenceEqual(ew) && ((int[])Get(receipt, "AppliedMagicWeights")).SequenceEqual(mw), label + " actual applied ratios recorded separately");
                    Equal("\"Applied\"", Json(CallMain("GetResourceCoordinationRuntime"), "state").ToString(), label + " coordinator reports applied");
                    Equal("\"" + owner + "\"", Json(CallMain("GetResourceCoordinationRuntime"), "owner").ToString(), label + " coordinator selects exactly the acquired owner");
                    CallMain("ReconcileCustomResources", plan);
                    Equal(recalls, Get(typeof(Program), "Recalls"), label + " second callback shares allocation cadence");
                    if (scheduled) Equal(true, Get(Get(MainInstance, "_protectedScheduler"), "_armed"), label + " only verified allocations arm credit");
                }
                var noOpAllocations = CallMain("GlobalPresetAllocationState"); var noOpSupport = CallMain("GlobalPresetSupportState");
                var noOpRecalls = Get(typeof(Program), "Recalls"); var noOpMagicRecalls = Get(typeof(Program), "MagicRecalls");
                var noOpTransfers = ResourceTransfers; var noOpPods = PodRefreshCalls; var noOpCaptures = PresetCaptures;
                var equivalentEnergy = new int[9]; var equivalentMagic = new int[7]; equivalentEnergy[2] = 6; equivalentMagic[2] = 4;
                Set(settings, "_energyNguWeights", equivalentEnergy); Set(settings, "_magicNguWeights", equivalentMagic);
                Equal(false, CallMain("GlobalPresetReceiptValid", owner), label + " equivalent desired allocation still needs updated ratio observations");
                FailAtRefresh = FailBeardRefresh = true;
                try
                {
                    for (var tick = 0; tick < 2; tick++)
                    {
                        refresh(); // Bypass cadence: this must capture and compose, not merely throttle.
                        Equal(true, CallMain("GlobalPresetReceiptValid", owner), label + " unchanged composed demand stays verified");
                    }
                }
                finally { FailAtRefresh = FailBeardRefresh = false; }
                Equal(noOpCaptures + 2, PresetCaptures, label + " idempotent ticks each perform a fresh capture");
                Equal(noOpRecalls, Get(typeof(Program), "Recalls"), label + " idempotent ticks never recall either lane");
                Equal(noOpMagicRecalls, Get(typeof(Program), "MagicRecalls"), label + " idempotent ticks never recall magic");
                Equal(noOpTransfers, ResourceTransfers, label + " idempotent ticks never invoke resource transfers");
                Equal(noOpPods, PodRefreshCalls, label + " idempotent ticks never write support inputs");
                Equal(noOpAllocations, CallMain("GlobalPresetAllocationState"), label + " idempotent ticks preserve exact native allocations");
                Equal(noOpSupport, CallMain("GlobalPresetSupportState"), label + " idempotent ticks preserve exact native support");
                Check(ReferenceEquals(receipt, Get(MainInstance, "_globalPresetReceipt")), label + " idempotent observation keeps acquired receipt");
                Check(((int[])Get(receipt, "AppliedEnergyWeights")).SequenceEqual(equivalentEnergy)
                    && ((int[])Get(receipt, "AppliedMagicWeights")).SequenceEqual(equivalentMagic), label + " no-op success observes new ratios without native writes");
                Equal(noOpAllocations, Get(receipt, "Allocations"), label + " no-op success observes exact applied native allocations");
                Equal(0L, Get(Get(receipt, "Execution"), "Blood"), label + " no-op success never pins temporary demand into baseline");
                Equal(1000L, ((long[])Get(Get(receipt, "Execution"), "MagicShares"))[2], label + " no-op success retains fresh demand-free baseline shares");
                // Positive-to-positive ratio edits must matter even when the selected set is unchanged.
                foreach (var reverse in new[] { false, true })
                {
                    var ew = new int[9]; var mw = new int[7];
                    ew[2] = mw[2] = reverse ? 3 : 1; ew[3] = mw[3] = reverse ? 1 : 3;
                    Set(settings, "_energyNguWeights", ew); Set(settings, "_magicNguWeights", mw); refresh();
                    Equal(reverse ? 7500L : 2500L, Get(energy[2], "energy"), label + " applies exact positive energy ratio");
                    Equal(reverse ? 2500L : 7500L, Get(energy[3], "energy"), label + " conserves energy after positive ratio edit");
                    var highMagic = demand == "none" ? 750L : 674L; var lowMagic = demand == "none" ? 250L : 225L;
                    Equal(reverse ? highMagic : lowMagic, Get(magic[2], "magic"), label + " applies exact positive magic ratio on demanded baseline");
                    Equal(reverse ? lowMagic : highMagic, Get(magic[3], "magic"), label + " conserves magic with largest-remainder ratio rounding");
                    Equal(demand == "none" ? 0L : 101L, Get(ritual, "magic"), label + " positive ratios never stack blood demand");
                    Check(((int[])Get(receipt, "AppliedEnergyWeights")).SequenceEqual(ew) && ((int[])Get(receipt, "AppliedMagicWeights")).SequenceEqual(mw), label + " receipt records positive-only ratio edits");
                    Equal(true, CallMain("GlobalPresetReceiptValid", owner), label + " positive-only ratio refresh verifies");
                }
                CallMain("ResetCustomIronPillPrep");
                if (demand == "both")
                {
                    refresh();
                    Equal(101L, Get(ritual, "magic"), label + " ordinary catchup continues after Iron Pill releases");
                    Equal(674L, Get(magic[2], "magic"), label + " demand handoff follows latest positive ratio");
                    Equal(225L, Get(magic[3], "magic"), label + " demand handoff never restores stale baseline");
                }
                Set(settings, "_customAutoBloodSpellsEnabled", false); Set(plan, "AutoBloodSpellsEnabled", false);
                refresh();
                Equal(0L, Get(ritual, "magic"), label + " release removes temporary blood demand");
                Equal(750L, Get(magic[2], "magic"), label + " release follows latest ratio, never original selection");
                Equal(250L, Get(magic[3], "magic"), label + " release conserves latest baseline ratio");
                Equal(0L, Get(magic[0], "magic"), label + " release cannot restore stale acquired selection");
                CallMain("VerifyLoadoutPresetAllocations", Get(receipt, "Execution"));
                var currentAllocations = CallMain("GlobalPresetAllocationState");
                var head = Get(Get(Character, "inventory"), "head"); Set(head, "curAttack", (float)Get(head, "curAttack") + 1); Invoke(typeof(Program), "RefreshBonuses");
                Equal("MaterialChanged", CallMain("GetGlobalPresetApplicationIssue", owner).ToString(), label + " diagnoses same-instance material drift");
                refresh();
                Equal(false, Get(receipt, "CurrentGearContinuation"), label + " unapproved drift never grants gear/material authority");
                Equal(false, CallMain("GlobalPresetReceiptValid", owner), label + " unapproved material cannot acquire dependent verification");
                Equal(currentAllocations, CallMain("GlobalPresetAllocationState"), label + " material observation preserves actual resource accounting");
                if (scheduled) Equal(false, Get(Get(MainInstance, "_protectedScheduler"), "_armed"), label + " unapproved material earns no stage credit");
                Check(ReferenceEquals(receipt, Get(MainInstance, "_globalPresetReceipt")) && ReferenceEquals(policy, Get(receipt, "Policy")) && ReferenceEquals(acquired, Get(policy, "Candidate")), label + " never reacquires selected gear policy");
                Check(((int[])Get(policy, "EnergyWeights")).SequenceEqual(acquiredEnergyWeights) && ((int[])Get(policy, "MagicWeights")).SequenceEqual(acquiredMagicWeights), label + " acquisition ratio observations stay immutable");
                Check(((long[])Get(acquired, "MagicShares")).SequenceEqual(acquiredShares) && ((string[])Get(policy, "Material")).SequenceEqual(material), label + " acquired candidate and material observations stay immutable");
                Equal(history, Get(MainInstance, "_loadoutPresetSnapshot").ToString(), label + " historical application stays immutable");
                Equal(definition, Invoke(Get(policy, "ExecutionPreset"), "ToJson").ToString(), label + " demands never mutate acquired definition");
                Equal(searches, PresetSearches, label + " all refreshes and material continuation avoid optimizer search");
                Equal(swaps, Get(typeof(Program), "Swaps"), label + " resource coordinator never swaps gear");
            });
        }
        foreach (var scheduled in new[] { false, true })
        run("cached gear restoration/live ratios/" + (scheduled ? "stage" : "dashboard"), () => {
            var plan = SetupResourceCoordination(scheduled, false, "none");
            var receipt = Get(MainInstance, "_globalPresetReceipt"); var policy = Get(receipt, "Policy"); var id = (string)Get(receipt, "Id");
            var settings = Get(MainType, "Settings"); var ew = new[] {0, 1, 3, 0, 0, 0, 0, 0, 0}; var mw = new[] {0, 3, 1, 0, 0, 0, 0};
            var originalWeights = ((int[])Get(policy, "EnergyWeights")).ToArray(); var selectedItems = ((Array)Get(policy, "Items")).Cast<object>().ToArray();
            var searches = PresetSearches;
            Set(settings, "_energyNguWeights", ew); Set(settings, "_magicNguWeights", mw); CallMain("InvalidateGlobalNguWeightPolicies");
            Check(ReferenceEquals(policy, ((IDictionary)Get(MainInstance, "_globalPresetPolicies"))[id]), "global edit retains cached gear before restoration");
            Throws(() => CallMain("ReplayGlobalPreset", policy, Get(policy, "Preset"), CallMain("CaptureLoadoutPresetDefinition", Get(policy, "Preset"))), "incompatible", "strict replay still rejects stale acquisition ratios");
            var inventory = Get(Character, "inventory"); var rows = (IList)Get(inventory, "inventory"); var head = Get(inventory, "head"); var empty = rows[0];
            var claim = CallMain("BeginExecutionClaim", "resource-ratio-temporary", "ratio-1", Domain("Gear, Support, Route"), (Func<bool>)(() => true));
            CallMain("ApplyExecutionClaim", claim, ExactClaimSetup(empty, "resource-ratio-temporary"));
            CallMain("InvalidateCustomEquipmentApplication");
            Set(MainInstance, "_customLastQuickResourceRefresh", DateTime.MinValue); CallMain("ReconcileCustomResources", plan);
            Check(ReferenceEquals(empty, Get(inventory, "head")), "temporary owner prevents premature cached gear restoration");
            Equal(true, CallMain("EndExecutionClaim", claim, true), "actual temporary ratio claim resolves the current acquired successor");
            Set(MainInstance, "_customLastQuickResourceRefresh", DateTime.MinValue); CallMain("ReconcileCustomResources", plan);
            var restored = Get(MainInstance, "_globalPresetReceipt");
            Equal(Get(policy, "Acquisition"), Get(Get(restored, "Policy"), "Acquisition"), "current successor replay retains its original acquired decision rather than creating new authority");
            Check(ReferenceEquals(head, Get(inventory, "head")), "cached restoration returns exact acquired equipped instance");
            Equal(false, Get(MainInstance, "_customEquipmentApplicationInvalidated"), "cached restoration completes invalidation event");
            Equal(true, CallMain("GlobalPresetReceiptValid", Get(restored, "Owner")), "restored receipt verifies live allocations");
            var restoredPolicy = Get(restored, "Policy");
            Equal(false, Get(Get(restoredPolicy, "Preset"), "AutoUpdate"), "restoration keeps optimizer disabled");
            Check(((Array)Get(restoredPolicy, "Items")).Cast<object>().Select((item, index) => ReferenceEquals(item, selectedItems[index])).All(x => x), "cached restoration preserves every acquired gear decision");
            Check(((int[])Get(policy, "EnergyWeights")).SequenceEqual(originalWeights), "old acquisition observations remain immutable");
            Check(((int[])Get(restored, "AppliedEnergyWeights")).SequenceEqual(ew) && ((int[])Get(restored, "AppliedMagicWeights")).SequenceEqual(mw), "restored receipt records current global ratios");
            var energy = (IList)Get(Get(Character, "NGU"), "skills"); var magic = (IList)Get(Get(Character, "NGU"), "magicSkills");
            Equal(0L, Get(energy[0], "energy"), "restoration excludes acquired old energy selection");
            Equal(2500L, Get(energy[1], "energy"), "cached restoration applies exact current energy ratio"); Equal(7500L, Get(energy[2], "energy"), "cached restoration conserves energy");
            Equal(0L, Get(magic[0], "magic"), "restoration excludes acquired old magic selection");
            Equal(750L, Get(magic[1], "magic"), "cached restoration applies exact current magic ratio"); Equal(250L, Get(magic[2], "magic"), "cached restoration conserves magic");
            CallMain("VerifyLoadoutPresetAllocations", Get(restored, "Execution"));
            if (scheduled) Equal(true, Get(Get(MainInstance, "_protectedScheduler"), "_armed"), "verified cached stage restoration rearms credit");
            Equal(searches, PresetSearches, "ratio edit, temporary displacement and cached restoration never search");
        });
        foreach (var autoUpdate in new[] { false, true })
        {
            foreach (var invalidate in new[] { false, true })
            run("native target with displaced gear/AutoUpdate=" + autoUpdate + "/invalidate=" + invalidate, () => {
                var plan = SetupResourceCoordination(true, autoUpdate, "none", true);
                var receipt = Get(MainInstance, "_globalPresetReceipt"); Check(receipt != null, "gear conflict fixture acquires target lease");
                var owner = (string)Get(receipt, "Owner"); var policy = Get(receipt, "Policy");
                var at = Get(Character, "advancedTraining"); var targets = (long[])Get(at, "levelTarget");
                targets[1] = 77;
                var inventory = Get(Character, "inventory"); var head = Get(inventory, "head");
                var rows = (IList)Get(inventory, "inventory"); var empty = rows[0];
                Set(inventory, "head", empty); rows[0] = head; Invoke(typeof(Program), "RefreshBonuses");
                Equal(false, CallMain("GlobalPresetGearMatches", receipt), "actual equipped instance differs, not just item material");
                if (invalidate) CallMain("InvalidateCustomEquipmentApplication");
                else CallMain("ServiceGlobalPresetNativeTargets");
                CallMain("ReconcileCustomResources", plan, false);
                Equal(owner, Get(MainInstance, "_globalPresetNativeTargetConflict"), "native conflict is latched independently of gear invalidation");
                Equal(20L, targets[0], "foreign companion target cannot withdraw the current mandatory toughness prerequisite");
                Equal(77L, targets[1], "lease release preserves the newer user target");
                var recalls = Get(typeof(Program), "Recalls"); var searches = PresetSearches; var captures = PresetCaptures; var swaps = Get(typeof(Program), "Swaps");
                var planningCaptures = Get(MainType, "_presetPlanningCaptures");
                var allocations = CallMain("GlobalPresetAllocationState");
                var scheduler = Get(MainInstance, "_protectedScheduler"); var earned = ((double[])Property(scheduler, "AppliedSeconds")).ToArray();
                for (var tick = 0; tick < 2; tick++)
                {
                    Set(scheduler, "_lastHeartbeat", (double)MainType.GetProperty("ProtectedHeartbeat", All).GetValue(null) - 2d);
                    foreach (var clock in new[] { "rebirthTime", "totalPlaytime" })
                        Set(Get(Character, clock), "totalseconds", (double)Get(Get(Character, clock), "totalseconds") + 2d);
                    Set(MainInstance, "_customLastQuickResourceRefresh", DateTime.MinValue); CallMain("ReconcileCustomResources", plan);
                    Equal(77L, targets[1], "gear restoration cannot overwrite a latched user target");
                    Check(ReferenceEquals(empty, Get(inventory, "head")), "conflict blocks automatic gear restoration");
                    Equal(owner, Get(MainInstance, "_globalPresetNativeTargetConflict"), "automatic ticks never acknowledge target conflict");
                    Equal(false, Get(scheduler, "_armed"), "conflicted displaced gear earns no stage credit");
                    Equal("\"NeedsUser\"", Json(CallMain("GetResourceCoordinationRuntime"), "state").ToString(), "gear conflict remains actionable NeedsUser");
                }
                Equal(recalls, Get(typeof(Program), "Recalls"), "latched conflict never recalls resources");
                Equal(searches, PresetSearches, "latched conflict never searches gear"); Equal(planningCaptures, Get(MainType, "_presetPlanningCaptures"), "latched conflict never captures replacement planning inputs while healthy-lane observations continue");
                Equal(swaps, Get(typeof(Program), "Swaps"), "latched conflict never swaps gear");
                Equal(allocations, CallMain("GlobalPresetAllocationState"), "latched conflict preserves remaining allocations");
                Check(((double[])Property(scheduler, "AppliedSeconds")).SequenceEqual(earned), "elapsed conflicted ticks earn no applied seconds");
                RestartFixtureStage(plan); CallMain("RunCustomPlanAutomation", plan);
                var acknowledged = Get(MainInstance, "_globalPresetReceipt");
                Check(!ReferenceEquals(receipt, acknowledged), "explicit apply or new stage acknowledges with a new receipt");
                Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), "acknowledged target lease restores verified scheduled ownership");
                Equal(10L, targets[1], "acknowledgement may explicitly acquire the stage target");
                if (!autoUpdate) Check(ReferenceEquals(head, Get(inventory, "head")), "cached acknowledgement restores exact acquired gear");
                else Equal(true, CallMain("GlobalPresetGearMatches", acknowledged), "optimized acknowledgement equips its freshly selected gear");
                Equal(searches + (autoUpdate ? 1 : 0), PresetSearches, "only an acknowledged AutoUpdate acquisition may search");
                Invoke(Get(MainType, "Settings"), "set_UseCustomRebirthPlan", false);
                CallMain("ReleaseGlobalPresetNativeTargets"); CallMain("ServiceExecutionNativeSettings");
                Equal(77L, targets[1], "acknowledged lease later restores the captured user target");
            });
            run("native target/AutoUpdate=" + autoUpdate, () => {
                var plan = SetupResourceCoordination(true, autoUpdate, "none", true);
                var receipt = Get(MainInstance, "_globalPresetReceipt"); Check(receipt != null, "target fixture acquired: " + CallMain("GetCustomAutomationStatus"));
                var owner = (string)Get(receipt, "Owner"); var settings = Get(MainType, "Settings");
                var at = Get(Character, "advancedTraining"); var targets = (long[])Get(at, "levelTarget");
                Equal(20L, targets[0], "stage owns toughness target"); Equal(10L, targets[1], "stage owns power target");
                targets[1] = 77;
                var head = Get(Get(Character, "inventory"), "head"); Set(head, "curAttack", (float)Get(head, "curAttack") + 1); Invoke(typeof(Program), "RefreshBonuses");
                Set(settings, "_energyNguWeights", new[] {0, 1, 0, 0, 0, 0, 0, 0, 0});
                Equal("NativeTargetsChanged", CallMain("GetGlobalPresetApplicationIssue", owner).ToString(), "first target issue outranks material and ratio changes");
                var searches = PresetSearches; var recalls = Get(typeof(Program), "Recalls"); var magicRecalls = Get(typeof(Program), "MagicRecalls");
                var earned = ((double[])Property(Get(MainInstance, "_protectedScheduler"), "AppliedSeconds")).ToArray();
                for (var tick = 0; tick < 2; tick++)
                {
                    Set(Get(MainInstance, "_protectedScheduler"), "_lastHeartbeat", (double)MainType.GetProperty("ProtectedHeartbeat", All).GetValue(null) - 2d);
                    foreach (var clock in new[] { "rebirthTime", "totalPlaytime" })
                        Set(Get(Character, clock), "totalseconds", (double)Get(Get(Character, clock), "totalseconds") + 2d);
                    Set(MainInstance, "_customLastQuickResourceRefresh", DateTime.MinValue); CallMain("ReconcileCustomResources", plan);
                    Equal(77L, targets[1], "coordinator preserves native user target on every tick");
                    Equal(false, CallMain("GlobalPresetStageReceiptValid", plan), "edited native target never earns valid ownership");
                    Equal(false, Get(Get(MainInstance, "_protectedScheduler"), "_armed"), "edited native target suspends credit");
                }
                Equal(searches, PresetSearches, "native target edit never triggers replacement search");
                Equal(recalls, Get(typeof(Program), "Recalls"), "native target rejection precedes whole energy recall");
                Equal(magicRecalls, Get(typeof(Program), "MagicRecalls"), "native target rejection precedes magic recall");
                Check(((double[])Property(Get(MainInstance, "_protectedScheduler"), "AppliedSeconds")).SequenceEqual(earned), "native target edit earns no applied time");
                Equal("\"NeedsUser\"", Json(CallMain("GetResourceCoordinationRuntime"), "state").ToString(), "native target edit needs user action");
                var status = (string)CallMain("GetCustomAutomationStatus");
                Check(status.IndexOf("native-target", StringComparison.OrdinalIgnoreCase) >= 0 && status.Contains("acknowledgement"), "actionable diagnostic identifies native target review rather than only a generic allocation failure: " + status);
                Set(Character, "idleEnergy", 321L); Set(Get(Character, "magic"), "idleMagic", 123L);
                Set(MainInstance, "_customIdleResourceWarningLastShown", DateTime.MinValue); CallMain("WarnIfCustomAutomationHasIdleResources", plan);
                Equal(status, CallMain("GetCustomAutomationStatus"), "generic idle warning never overwrites first actionable diagnostic");
            });
            run("partial mutation/AutoUpdate=" + autoUpdate, () => {
                var plan = SetupResourceCoordination(true, autoUpdate, "none", true);
                Check(Get(MainInstance, "_globalPresetReceipt") != null, "partial failure fixture acquired: " + CallMain("GetCustomAutomationStatus"));
                var searches = PresetSearches; var recalls = (int)Get(typeof(Program), "Recalls");
                var baseline = Get(Get(MainInstance, "_globalPresetReceipt"), "Execution");
                var atAllocations = (long[])Get(Get(Character, "advancedTraining"), "energy"); var funded = atAllocations[0];
                Check(funded > 0, "partial failure fixture has desired energy available to displace");
                atAllocations[0] = funded - 1; Set(Character, "idleEnergy", (long)Get(Character, "idleEnergy") + 1);
                Equal(false, CallMain("GlobalPresetReceiptValid", Get(Get(MainInstance, "_globalPresetReceipt"), "Owner")), "displaced desired allocation requires repair rather than a no-op");
                var observedUi = false;
                AtRefreshObserver = () => {
                    observedUi = true;
                    var atEnergy = (long[])Get(Get(Character, "advancedTraining"), "energy");
                    Check(atEnergy.Take(2).All(x => x > 0), "UI boundary sees both committed AT transfers");
                    CallMain("VerifyLoadoutPresetAllocations", baseline);
                    Equal(10000L, (long)Get(Character, "idleEnergy") + atEnergy.Sum()
                        + ((IList)Get(Get(Character, "NGU"), "skills")).Cast<object>().Sum(x => (long)Get(x, "energy")), "exact writer debits idle energy before UI rendering");
                    Equal(1000L, (long)Get(Get(Character, "magic"), "idleMagic")
                        + ((IList)Get(Get(Character, "NGU"), "magicSkills")).Cast<object>().Sum(x => (long)Get(x, "magic")), "exact writer commits the magic lane before UI rendering");
                    AtRefreshObserver = null;
                };
                FailAtRefresh = true;
                try { Set(MainInstance, "_customLastQuickResourceRefresh", DateTime.MinValue); CallMain("ReconcileCustomResources", plan); }
                finally { FailAtRefresh = false; AtRefreshObserver = null; }
                Equal(true, observedUi, "partial failure reaches native UI after exact allocation commits");
                Equal(recalls, Get(typeof(Program), "Recalls"), "render failure follows exact differences without whole-lane recalls");
                Check(Get(MainInstance, "_globalPresetReceipt") != null, "verified model accounting retains acquired receipt despite rendering failure");
                Equal("\"Applied\"", Json(CallMain("GetResourceCoordinationRuntime"), "state").ToString(), "verified model mutation is Applied, not an unknown native result");
                Equal(true, Get(Get(MainInstance, "_protectedScheduler"), "_armed"), "only fully verified accounting and targets permit stage credit");
                Equal(true, Property(Json(CallMain("GetResourceCoordinationRuntime"), "presentationPending"), "AsBool"), "presentation is pending independently of model accounting");
                var captures = PresetCaptures; var allocations = CallMain("GlobalPresetAllocationState");
                var committedWrites = ResourceTransfers;
                for (var tick = 0; tick < 2; tick++)
                {
                    Set(MainInstance, "_customLastQuickResourceRefresh", DateTime.MinValue); CallMain("ReconcileCustomResources", plan);
                    Equal("\"Applied\"", Json(CallMain("GetResourceCoordinationRuntime"), "state").ToString(), "presentation retry converges without replaying accounting");
                }
                Equal(searches, PresetSearches, "presentation retry never searches"); Equal(captures + 2, PresetCaptures, "normal verification continues while presentation recovers");
                Equal(recalls, Get(typeof(Program), "Recalls"), "presentation retry never recalls again");
                Equal(committedWrites, ResourceTransfers, "presentation retry performs zero model writes");
                Equal(allocations, CallMain("GlobalPresetAllocationState"), "presentation retry preserves all committed accounting");
                Equal(false, Property(Json(CallMain("GetResourceCoordinationRuntime"), "presentationPending"), "AsBool"), "successful separate render retry clears presentation-only evidence");
            });
        }
        foreach (var shortPlan in new[] { false, true })
        foreach (var spellsEnabled in new[] { false, true })
        run("weighted catchup/short=" + shortPlan + "/spells=" + spellsEnabled, () => {
            var plan = SetupResourceCoordination(true, false, "blood-goals"); var settings = Get(MainType, "Settings");
            Set(plan, "StageDurationSeconds", shortPlan ? 3600 : 7200); Set(plan, "AutoBloodSpellsEnabled", spellsEnabled);
            Set(((Array)Get(plan, "Stages")).GetValue(0), "GearGoal", -1);
            Set(settings, "_customAutoBloodSpellsEnabled", true); // Plan-local disable must override globals.
            var blood = Get(Character, "bloodMagic"); Set(blood, "lootAutoSpell", false); Set(blood, "goldAutoSpell", false); Set(blood, "rebirthAutoSpell", false);
            var flushes = NativeNumberFlushCalls; CallMain("RunCustomBloodSpellToggles", plan);
            var requested = spellsEnabled && !shortPlan;
            Equal(requested, CallMain("CustomBloodGoalCatchUpRequested", plan), "catchup and effective short-plan spell policy agree");
            Equal(requested, Get(blood, "lootAutoSpell"), "weighted short-plan suppression does not enable loot spell");
            Equal(spellsEnabled && shortPlan, Get(blood, "rebirthAutoSpell"), "enabled short plans use NUMBER-only spell policy");
            Set(MainInstance, "_customLastQuickResourceRefresh", DateTime.MinValue); CallMain("ReconcileCustomResources", plan);
            Equal(requested ? 101L : 0L, Get(((IList)Get(blood, "ritual"))[0], "magic"), "weighted allocation borrows magic only when ordinary spells can service goals");
            Equal(requested ? 899L : 1000L, Get(((IList)Get(Get(Character, "NGU"), "magicSkills"))[0], "magic"), "suppressed or disabled catchup restores entire NGU baseline");
            Equal(flushes, NativeNumberFlushCalls, "ordinary catchup never flushes banked blood");
            Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), "short-plan policy retains verified stage allocations");
        });
        foreach (var scheduled in new[] { false, true })
        foreach (var manageMagic in new[] { false, true })
        run("Iron Pill activation/" + (scheduled ? "stage" : "dashboard") + "/manageMagic=" + manageMagic, () => {
            var plan = SetupResourceCoordination(scheduled, false, "none"); var settings = Get(MainType, "Settings");
            Set(settings, "_autoRebirth", false); Set(plan, "AutoRebirthEnabled", false); Set(plan, "AutoIronPillEnabled", true);
            Set(plan, "AutoBloodSpellsEnabled", true); Set(plan, "IronPillSaveSeconds", 300);
            Set(settings, "_customAutoBloodSpellsEnabled", true); Set(settings, "_customIronPillSaveSeconds", 300);
            Set(settings, "_manageMagic", manageMagic);
            Set(Get(Character, "rebirthTime"), "totalseconds", 4000d);
            Set(Get(Character, "totalPlaytime"), "totalseconds", 5000d);
            var blood = Get(Character, "bloodMagic"); Set(blood, "bloodPoints", 10000d); Set(blood, "rebirthPower", 11d);
            var allocations = CallMain("GlobalPresetAllocationState"); var recalls = Get(typeof(Program), "Recalls"); var flushes = NativeNumberFlushCalls; var searches = PresetSearches;
            CallMain("ResetCustomIronPillPrep");
            if (manageMagic)
            {
                Set(plan, "AutoIronPillEnabled", false);
                Equal("The selected plan's automatic Iron Pill is disabled.", CallMain("CustomIronPillEligibilityBlocker", plan), "selected plan explicitly controls Iron Pill enablement");
                CallMain("RunCustomBloodSpellToggles", scheduled ? plan : null);
                Equal(flushes, NativeNumberFlushCalls, "disabled plan Iron Pill never flushes banked blood");
                Equal(false, MainType.GetProperty("IronPillBloodReserved", All).GetValue(null), "disabled plan Iron Pill never reserves magic");
                Set(plan, "AutoIronPillEnabled", true);
            }
            var blocker = (string)CallMain("CustomIronPillEligibilityBlocker", plan);
            Equal(manageMagic ? "" : "Magic management is disabled.", blocker, "Iron Pill eligibility does not require automatic rebirth");
            CallMain("RunCustomBloodSpellToggles", scheduled ? plan : null);
            Equal(manageMagic, MainType.GetProperty("IronPillBloodReserved", All).GetValue(null), "native automation activates prep only with magic management");
            Equal(flushes + (manageMagic ? 1 : 0), NativeNumberFlushCalls, "activation invokes real native NUMBER flush exactly once when permitted");
            Equal(manageMagic ? 0d : 10000d, Get(blood, "bloodPoints"), "disabled magic management never spends banked blood");
            Equal(manageMagic ? 10011d : 11d, Get(blood, "rebirthPower"), "successful activation transfers banked blood exactly to native NUMBER");
            Equal(recalls, Get(typeof(Program), "Recalls"), "prep activation never recalls resources itself");
            Equal(allocations, CallMain("GlobalPresetAllocationState"), "prep activation only announces demand before resource reconciliation");
            Set(MainInstance, "_customLastQuickResourceRefresh", DateTime.MinValue); CallMain("ReconcileCustomResources", plan);
            Equal(manageMagic ? 101L : 0L, Get(((IList)Get(blood, "ritual"))[0], "magic"), "dashboard/stage refresh funds only permitted Iron Pill reservation");
            if (!manageMagic)
            {
                Equal(allocations, CallMain("GlobalPresetAllocationState"), "disabled magic management preserves all existing allocations");
                Equal(recalls, Get(typeof(Program), "Recalls"), "disabled magic management prevents flush and allocation recalls");
                // A previously active prep cannot remain reserved when the lane is disabled.
                Set(MainInstance, "_customIronPillPlanName", Property(settings, "CustomPlanFile")); Set(MainInstance, "_customIronPillPrepStartedAt", 4000d);
                Equal(false, MainType.GetProperty("IronPillBloodReserved", All).GetValue(null), "disabling magic invalidates even preexisting reservation fields");
            }
            else
            {
                Equal(899L, Get(((IList)Get(Get(Character, "NGU"), "magicSkills"))[0], "magic"), "activated reservation conserves latest magic baseline");
                Set(blood, "bloodPoints", 321d); CallMain("RunCustomBloodSpellToggles", scheduled ? plan : null);
                Equal(321d, Get(blood, "bloodPoints"), "ongoing prep never flushes newly produced blood again");
                Equal(flushes + 1, NativeNumberFlushCalls, "repeated prep tick never repeats NUMBER flush");
                Set(settings, "_manageMagic", false); var reservedAllocations = CallMain("GlobalPresetAllocationState");
                CallMain("RunCustomBloodSpellToggles", scheduled ? plan : null);
                Equal(false, MainType.GetProperty("IronPillBloodReserved", All).GetValue(null), "disabling magic management cancels genuinely active prep");
                Equal(321d, Get(blood, "bloodPoints"), "disabling active prep never flushes newly produced blood");
                Equal(flushes + 1, NativeNumberFlushCalls, "disabled lane never enters another NUMBER flush");
                Set(MainInstance, "_customLastQuickResourceRefresh", DateTime.MinValue); CallMain("ReconcileCustomResources", plan);
                Equal(reservedAllocations, CallMain("GlobalPresetAllocationState"), "disabled lane preserves allocations rather than forcibly reclaiming manual magic");
            }
            Equal(false, Property(settings, "AutoRebirth"), "Iron Pill activation never enables automatic rebirth");
            Equal(searches, PresetSearches, "Iron Pill eligibility and demand activation never search gear");
        });
        foreach (var activity in new[] { "dashboard", "focus", "stage" })
        foreach (var globalSpells in new[] { false, true })
        foreach (var planSpells in new[] { false, true })
        run("Iron Pill blood setting/" + activity + "/global=" + globalSpells + "/plan=" + planSpells, () => {
            var plan = SetupResourceCoordination(activity == "stage", false, "none"); var settings = Get(MainType, "Settings");
            if (activity == "focus")
            {
                CallMain("StopManualGlobalPreset");
                var focus = Enum.Parse(Plugin.GetType("NGUInjector.CustomFocusGoal", true), "EnergyNgu");
                Set(MainInstance, "_customRequestedFocusGoal", focus); Set(MainInstance, "_customSustainedFocusGoal", focus);
                Equal(focus, CallMain("GetActiveCustomFocusGoal"), "fixture selects an active legacy resource focus");
            }
            Set(settings, "_autoRebirth", false); Set(settings, "_customAutoBloodSpellsEnabled", globalSpells); Set(settings, "_customIronPillSaveSeconds", 300);
            Set(plan, "AutoRebirthEnabled", false); Set(plan, "AutoIronPillEnabled", true); Set(plan, "AutoBloodSpellsEnabled", planSpells); Set(plan, "IronPillSaveSeconds", 300);
            Set(Get(Character, "rebirthTime"), "totalseconds", 4000d); Set(Get(Character, "totalPlaytime"), "totalseconds", 5000d);
            Set(MainInstance, "_customRuntimePlanCache", plan); Set(MainInstance, "_customRuntimePlanCacheName", Property(settings, "CustomPlanFile"));
            Set(MainInstance, "_customRuntimePlanCacheLoadedAt", DateTime.Now);
            var blood = Get(Character, "bloodMagic"); Set(blood, "bloodPoints", 10000d); Set(blood, "rebirthPower", 11d);
            var flushes = NativeNumberFlushCalls; var recalls = Get(typeof(Program), "Recalls"); var allocations = CallMain("GlobalPresetAllocationState");
            CallMain("ResetCustomIronPillPrep");
            var enabled = activity == "stage" ? planSpells : globalSpells;
            if (enabled)
            {
                Set(plan, "AutoIronPillEnabled", false);
                Equal("The selected plan's automatic Iron Pill is disabled.", CallMain("CustomIronPillEligibilityBlocker", plan), "plan Iron Pill permission remains required independently of blood setting scope");
                CallMain("RunCustomBloodSpellToggles", activity == "stage" ? plan : null);
                Equal(flushes, NativeNumberFlushCalls, "missing plan permission never flushes blood");
                Equal(false, MainType.GetProperty("IronPillBloodReserved", All).GetValue(null), "missing plan permission never reserves magic");
                Set(plan, "AutoIronPillEnabled", true);
            }
            Equal(enabled ? "" : "Automatic blood spells are disabled.", CallMain("CustomIronPillEligibilityBlocker", plan), "manual activity uses global blood setting while active plan uses its override");
            CallMain("RunCustomBloodSpellToggles", activity == "stage" ? plan : null);
            Equal(enabled, MainType.GetProperty("IronPillBloodReserved", All).GetValue(null), "reservation follows effective blood setting, not unused plan override");
            Equal(flushes + (enabled ? 1 : 0), NativeNumberFlushCalls, "effective permission controls exact native NUMBER flush count");
            Equal(enabled ? 0d : 10000d, Get(blood, "bloodPoints"), "disabled effective blood setting preserves the entire banked pool");
            Equal(enabled ? 10011d : 11d, Get(blood, "rebirthPower"), "permitted prep flushes banked blood exactly into native NUMBER");
            Equal(recalls, Get(typeof(Program), "Recalls"), "eligibility and flush never recall resource allocations");
            Equal(allocations, CallMain("GlobalPresetAllocationState"), "eligibility announces demand without rewriting resources");
            Equal(globalSpells, Property(settings, "CustomAutoBloodSpellsEnabled"), "eligibility never rewrites global blood preference");
            Equal(planSpells, Get(plan, "AutoBloodSpellsEnabled"), "eligibility never rewrites plan override");
        });
        Console.WriteLine("Resource coordination: " + (Assertions - before) + " assertions; " + failures.Count + " failed cases.");
        if (failures.Count > 0) throw new Exception(string.Join(Environment.NewLine, failures));
    }

    private static void CheckGlobalNguWeights()
    {
        Invoke(typeof(Program), "Setup");
        Character = Get(typeof(Program), "Character"); Controller = Get(typeof(Program), "Controller"); MainInstance = Get(typeof(Program), "MainInstance");
        PrepareSupport();
        Set(MainInstance, "_globalPresetPolicies", Activator.CreateInstance(Field(MainInstance, "_globalPresetPolicies").FieldType));
        AllowPresetSettingsSave = true;
        var settings = Get(MainType, "Settings");
        Set(settings, "_savePath", Path.Combine((string)Get(MainType, "_dir"), "global-ngu-settings.json"));
        Set(settings, "_manualAdventureEnabled", false); Set(settings, "_customAdventureEnabled", false);
        Set(settings, "_energyNguWeights", new[] {1, 0, 0, 0, 0, 0, 0, 0, 0});
        Set(settings, "_magicNguWeights", new[] {1, 0, 0, 0, 0, 0, 0});
        ListField(Get(Character, "NGU"), "skills", 11); ListField(Get(Character, "NGU"), "magicSkills", 11);
        Set(Get(Character, "NGU"), "autoAdvance", false);
        Set(Get(Get(Character, "buttons"), "ngu"), "m_Interactable", true);
        var library = Get(Get(MainType, "CustomPlanStore"), "<PresetLibrary>k__BackingField");
        var root = Invoke(library, "ReadExternalLibrary");
        var definition = JsonParse("{\"Id\":\"global-ngu-lifecycle\",\"Name\":\"Global NGU lifecycle\",\"Weights\":[1,1,0,0,0,0],\"SupportMode\":\"current\",\"SwapSeconds\":0,\"EnergyNguWeights\":[0,0,0,0,0,0,0,0,1],\"MagicNguWeights\":[0,0,0,0,0,0,1]}");
        Invoke(Json(root, "Presets"), "Add", "", definition);
        var revision = Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
        Func<object> args = () => JsonParse("{\"presetId\":\"global-ngu-lifecycle\",\"libraryRevision\":\"" + revision + "\"}");
        var preset = Invoke(library, "Get", "global-ngu-lifecycle");
        var inline = JsonParse("{\"preset\":" + Invoke(preset, "ToJson") + "}");
        foreach (var field in new[] {"energyNguWeights", "magicNguWeights"})
        {
            var deprecated = JsonParse(inline.ToString()); SetJson(deprecated, field, JsonParse("[]"));
            Throws(() => CallMain("PreviewLoadoutPreset", deprecated), "Unexpected", "inline preview rejects deprecated " + field);
            Throws(() => Invoke(MainInstance, "ApplyLoadoutPreset", deprecated, null), "Unexpected", "inline apply rejects deprecated " + field);
        }
        var capture = CallMain("CaptureLoadoutPresetDefinition", preset);
        Equal("1,0,0,0,0,0,0,0,0", string.Join(",", ((Array)Get(Get(capture, "Snapshot"), "EnergyNgu")).Cast<object>().Select(x => Get(x, "Weight"))), "capture ignores conflicting legacy preset ratios");
        var preview = CallMain("PreviewGlobalPreset", args());
        var apply = JsonParse("{\"presetId\":\"global-ngu-lifecycle\",\"libraryRevision\":\"" + revision + "\",\"previewId\":" + Json(preview, "previewId") + ",\"candidateKey\":\"current\"}");
        var recalls = Get(typeof(Program), "Recalls"); var swaps = Get(typeof(Program), "Swaps");
        Set(settings, "_energyNguWeights", new[] {2, 0, 0, 0, 0, 0, 0, 0, 0});
        Throws(() => Invoke(MainInstance, "ApplyGlobalPreset", apply, null), "Preview", "even a positive ratio change rejects an old preview");
        Equal(recalls, Get(typeof(Program), "Recalls"), "stale preview rejection precedes recalls");
        Equal(swaps, Get(typeof(Program), "Swaps"), "stale preview rejection precedes swaps");
        Check(Get(capture, "Material").ToString() != Get(CallMain("CaptureLoadoutPresetDefinition", preset), "Material").ToString(), "material identity includes global NGU weights");
        Invoke(MainInstance, "EquipGlobalPreset", args(), null);
        var receipt = Get(MainInstance, "_globalPresetReceipt"); var policy = Get(receipt, "Policy");
        var searches = PresetSearches;
        var energy = (IList)Get(Get(Character, "NGU"), "skills"); var magic = (IList)Get(Get(Character, "NGU"), "magicSkills");
        Check((long)Get(energy[0], "energy") > 0 && (long)Get(magic[0], "magic") > 0, "global positive selections receive both lanes");
        Equal(0L, Get(energy[8], "energy"), "deprecated preset energy selection is excluded");
        Equal(0L, Get(magic[6], "magic"), "deprecated preset magic selection is excluded");
        var historical = Get(MainInstance, "_loadoutPresetSnapshot").ToString();
        Set(settings, "_energyNguWeights", new[] {0, 1, 0, 0, 0, 0, 0, 0, 0});
        Set(settings, "_magicNguWeights", new[] {0, 1, 0, 0, 0, 0, 0});
        Equal(false, CallMain("GlobalPresetReceiptValid", "dashboard"), "global edits immediately invalidate active receipts");
        Set(MainInstance, "_customPreRebirthPrepRunning", true);
        CallMain("RunManualGlobalPresetRefresh");
        Check(ReferenceEquals(receipt, Get(MainInstance, "_globalPresetReceipt")) && (long)Get(energy[0], "energy") > 0, "temporary owner defers recalls without losing the stale receipt");
        Set(MainInstance, "_customPreRebirthPrepRunning", false);
        CallMain("RunManualGlobalPresetRefresh");
        Equal(0L, Get(energy[0], "energy"), "dashboard refresh clears excluded owned energy");
        Equal(0L, Get(magic[0], "magic"), "dashboard refresh clears excluded owned magic");
        Equal(searches, PresetSearches, "dashboard weight refresh never silently optimizes");
        Check(ReferenceEquals(receipt, Get(MainInstance, "_globalPresetReceipt")), "dashboard retains acquired gear ownership across live ratio changes");
        Equal(historical, Get(MainInstance, "_loadoutPresetSnapshot").ToString(), "applied arrays remain immutable observations");
        Equal(true, CallMain("GlobalPresetReceiptValid", "dashboard"), "dashboard refresh verifies the current global ratios");
        Check((long)Get(energy[1], "energy") > 0 && (long)Get(magic[1], "magic") > 0, "dashboard refresh funds newly selected global lanes");
        Check(((int[])Get(policy, "EnergyWeights")).SequenceEqual(new[] {2, 0, 0, 0, 0, 0, 0, 0, 0}), "acquired ratios remain immutable history");
        Check(((int[])Get(receipt, "AppliedEnergyWeights")).SequenceEqual((int[])Get(settings, "_energyNguWeights")), "receipt observes actual refreshed ratios");
        Throws(() => CallMain("ReplayGlobalPreset", policy, preset, CallMain("CaptureLoadoutPresetDefinition", preset)), "incompatible", "old cached policies cannot replay with new global ratios");
        Equal(1, ((IDictionary)Get(MainInstance, "_globalPresetPolicies")).Count, "global edit retains reusable acquired gear cache");
        Check(ReferenceEquals(policy, ((IDictionary)Get(MainInstance, "_globalPresetPolicies"))["global-ngu-lifecycle"]), "ratio edits never replace the acquired cached gear policy");

        var plan = Invoke(Get(MainType, "CustomPlanStore"), "Load", "fixture");
        var stage = Activator.CreateInstance(Plugin.GetType("NGUInjector.CustomPlans.CustomPlanStage", true));
        Set(stage, "Name", "Global weight stage"); Set(stage, "PresetId", "global-ngu-lifecycle"); Set(stage, "Percent", 100d); Set(stage, "DurationSeconds", 60);
        var stages = Array.CreateInstance(stage.GetType(), 1); stages.SetValue(stage, 0);
        Set(plan, "Stages", stages); Set(plan, "StageSchedulerEnabled", true); Set(plan, "StageLoopStartIndex", -1);
        Set(plan, "AutoActivateFruits", false); Set(plan, "AutoRebirthEnabled", false); Set(plan, "TimeTriggerEnabled", false);
        Set(settings, "_useCustomRebirthPlan", true);
        CallMain("RunCustomPlanAutomation", plan);
        Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), "scheduled entry acquires new global weights");
        searches = PresetSearches;
        Set(settings, "_energyNguWeights", new[] {0, 0, 1, 0, 0, 0, 0, 0, 0});
        CallMain("RunCustomPlanAutomation", plan);
        Equal(searches, PresetSearches, "running AutoUpdate stage refreshes live global ratios without gear search");
        Equal(0L, Get(energy[1], "energy"), "scheduled reacquisition clears newly excluded energy");
        Check((long)Get(energy[2], "energy") > 0, "scheduled reacquisition allocates the new global selection");
        Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), "scheduled reacquisition rearms verified ownership");
        Equal(true, Property(settings, "UseCustomRebirthPlan"), "weight reacquisition does not pause the plan");
        CallMain("RunCustomPlanAutomation", plan);
        Equal(searches, PresetSearches, "stable weights never trigger gear optimization");

        root = Invoke(library, "ReadExternalLibrary");
        var edited = ((IEnumerable)GetChildren(Json(root, "Presets"))).Cast<object>().Single(x => Property(Json(x, "Id"), "Value").Equals("global-ngu-lifecycle"));
        SetJson(edited, "AutoUpdate", Json(JsonParse("{\"value\":false}"), "value"));
        revision = Invoke(library, "SaveExternalLibrary", root, revision);
        preset = Invoke(library, "Get", "global-ngu-lifecycle");
        preview = CallMain("PreviewGlobalPreset", args());
        apply = JsonParse("{\"presetId\":\"global-ngu-lifecycle\",\"libraryRevision\":\"" + revision + "\",\"previewId\":" + Json(preview, "previewId") + ",\"candidateKey\":\"current\"}");
        Invoke(MainInstance, "ApplyGlobalPreset", apply, null);
        Set(settings, "_useCustomRebirthPlan", true);
        CallMain("RunCustomPlanAutomation", plan);
        receipt = Get(MainInstance, "_globalPresetReceipt"); searches = PresetSearches;
        Set(settings, "_energyNguWeights", new[] {0, 0, 0, 1, 0, 0, 0, 0, 0});
        CallMain("RunCustomPlanAutomation", plan);
        Equal(searches, PresetSearches, "AutoUpdate false stage never searches after global edits");
        Equal(0L, Get(energy[2], "energy"), "AutoUpdate false still recalls stale excluded allocations");
        Check(ReferenceEquals(receipt, Get(MainInstance, "_globalPresetReceipt")), "non-updating stage retains the acquired gear receipt");
        Check((long)Get(energy[3], "energy") > 0, "non-updating stage funds the latest global selection");
        Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), "non-updating stage earns credit only after refreshed allocations verify");
        Invoke(MainInstance, "EquipGlobalPreset", args(), null);
        Equal(searches, PresetSearches, "explicit AutoUpdate false Equip reuses cached gear with live global ratios");
        Equal(true, CallMain("GlobalPresetReceiptValid", "dashboard"), "explicit cached Equip verifies current ratios");
        Check((long)Get(energy[3], "energy") > 0 && (long)Get(energy[2], "energy") == 0, "explicit cached Equip never restores acquired stale energy selection");
        var legacyType = Plugin.GetType("NGUInjector.AllocationProfiles.BreakpointTypes.NGUBP", true);
        var resourceType = Plugin.GetType("NGUInjector.AllocationProfiles.BreakpointTypes.ResourceType", true);
        foreach (var energyLane in new[] {true, false})
        {
            var laneWeights = energyLane ? new[] {0, 1, 1, 0, 0, 0, 0, 0, 0} : new[] {0, 1, 1, 0, 0, 0, 0};
            Set(settings, energyLane ? "_energyNguWeights" : "_magicNguWeights", laneWeights);
            Invoke(Character, energyLane ? "removeMostEnergy" : "removeMostMagic");
            var legacy = Activator.CreateInstance(legacyType, true);
            legacyType.BaseType.GetProperty("Type", All).SetValue(legacy, Enum.Parse(resourceType, energyLane ? "Energy" : "Magic"));
            legacyType.BaseType.GetProperty("Index", All).SetValue(legacy, 0);
            Equal(false, Invoke(legacy, "IsValid"), "legacy breakpoint validation excludes global-zero " + energyLane);
            Equal(null, legacyType.GetMethod("Allocate", All), "retired legacy allocation has no bypass writer " + energyLane);
            legacyType.BaseType.GetProperty("Index", All).SetValue(legacy, 1);
            Equal(null, legacyType.GetMethod("Allocate", All), "selected legacy parser entries have no independent writer " + energyLane);
            var allocator = Plugin.GetType("NGUInjector.Managers.BlackBarAllocator", true);
            var requirement = Invoke(allocator, "CalculatePriorities", new[] { "NGU" }, energyLane, 60);
            var intent = New("ResourceSetupIntent"); var coordinator = MainInstance;
            Set(intent, "Owner", "global-weight-check"); Set(intent, "Desired", Get(requirement, "Desired"));
            Set(intent, "Energy", energyLane); Set(intent, "Magic", !energyLane);
            Set(intent, "Authorized", (Func<bool>)(() => ReferenceEquals(Get(MainType, "reference"), coordinator)));
            Equal(true, Property(CallMain("ReconcileStageResourceSetup", intent), "Verified"), "global weights use the current shared resource executor");
            Invoke(allocator, "ObserveApplied", requirement);
            var rows = energyLane ? energy : magic;
            var field = energyLane ? "energy" : "magic";
            Check((long)Get(rows[1], field) > 0 && (long)Get(rows[2], field) > 0, "shared global group funds all positive global targets " + energyLane);
            Equal(0L, Get(rows[0], field), "shared global group leaves excluded index zero empty " + energyLane);
        }
    }

    private static void CheckGlobalPresetAutoBoost()
    {
        var before = Assertions;
        foreach (var autoUpdate in new[] { true, false })
        foreach (var scenario in new[] { "unchanged", "changed", "waiting", "displaced-allocation", "throw-changed", "throw-unchanged", "foreign-premutation" })
        {
            Invoke(typeof(Program), "Setup");
            Character = Get(typeof(Program), "Character"); Controller = Get(typeof(Program), "Controller"); MainInstance = Get(typeof(Program), "MainInstance");
            PrepareSupport(); SetActiveBeards();
            Set(MainInstance, "_invManager", Activator.CreateInstance(Plugin.GetType("NGUInjector.Managers.InventoryManager", true), true));
            Set(MainInstance, "_globalPresetPolicies", Activator.CreateInstance(Field(MainInstance, "_globalPresetPolicies").FieldType));
            var settings = Get(MainType, "Settings");
            Set(settings, "_manualAdventureEnabled", false); Set(settings, "_customAdventureEnabled", false);
            Set(Get(Get(Character, "buttons"), "ngu"), "m_Interactable", true);
            var ngu = Get(Character, "NGU"); Set(ngu, "autoAdvance", false);
            var energyRows = ListField(ngu, "skills", 11); ListField(ngu, "magicSkills", 11);
            var library = Get(Get(MainType, "CustomPlanStore"), "<PresetLibrary>k__BackingField"); var root = Invoke(library, "ReadExternalLibrary");
            var id = "preset-boost-" + (autoUpdate ? "auto-" : "cached-") + scenario;
            var definition = JsonParse("{\"Id\":\"" + id + "\",\"Name\":\"Boost ownership fixture\",\"Weights\":[1,1,0,0,0,0],\"MagicBloodPercent\":0,\"SupportMode\":\"current\",\"SwapSeconds\":0,\"AutoUpdate\":" + (autoUpdate ? "true" : "false") + "}");
            Invoke(Json(root, "Presets"), "Add", "", definition);
            var revision = Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
            var preview = CallMain("PreviewGlobalPreset", JsonParse("{\"presetId\":\"" + id + "\",\"libraryRevision\":\"" + revision + "\"}"));
            var apply = JsonParse("{\"presetId\":\"" + id + "\",\"libraryRevision\":\"" + revision + "\",\"previewId\":" + Json(preview, "previewId") + ",\"candidateKey\":\"current\"}");
            Invoke(MainInstance, "ApplyGlobalPreset", apply, null);
            var label = "auto-boost " + autoUpdate + "/" + scenario;
            Equal(true, CallMain("GlobalPresetReceiptValid", "dashboard"), label + " starts with an owned exact policy");
            var receipt = Get(MainInstance, "_globalPresetReceipt"); var policy = Get(receipt, "Policy");
            var candidate = Get(policy, "Candidate"); var preset = Get(policy, "Preset");
            var items = Get(policy, "Items"); var slots = ((int[])Get(policy, "Slots")).ToArray();
            var selectedItems = ((Array)items).Cast<object>().ToArray();
            var materialReference = (string[])Get(policy, "Material"); var originalMaterial = materialReference.ToArray();
            var originalDefinition = Invoke(preset, "ToJson").ToString(); var historical = Get(MainInstance, "_loadoutPresetSnapshot").ToString();
            var head = Get(Get(Character, "inventory"), "head"); var attack = (float)Get(head, "curAttack");
            var foreign = scenario == "foreign-premutation";
            var changed = scenario != "unchanged" && scenario != "throw-unchanged";
            var fail = scenario.StartsWith("throw-", StringComparison.Ordinal);
            if (scenario == "waiting") Set(MainInstance, "_globalPresetWaitingEvent", "dashboard:" + id);
            if (scenario == "displaced-allocation")
            {
                var amount = (long)Get(energyRows[0], "energy"); Check(amount > 0, label + " has an energy allocation to displace");
                Set(energyRows[0], "energy", 0L); Set(Character, "idleEnergy", (long)Get(Character, "idleEnergy") + amount);
            }
            if (foreign) { Set(head, "curAttack", attack + 1); Invoke(typeof(Program), "RefreshBonuses"); }
            if (foreign || scenario == "waiting" || scenario == "displaced-allocation")
                Equal(false, CallMain("GlobalPresetReceiptValid", "dashboard"), label + " ordinary validation rejects the precondition");
            Equal(!foreign, CallMain("GlobalPresetReceiptValid", "dashboard", false, true), label + " boost ownership ignores waiting/allocations but still checks material");
            var allocations = CallMain("GlobalPresetAllocationState"); var support = CallMain("GlobalPresetSupportState");
            var searches = PresetSearches; var captures = PresetCaptures; var calls = AutoBoostCalls;
            var recalls = Get(typeof(Program), "Recalls"); var swaps = Get(typeof(Program), "Swaps");
            AutoBoostAction = () => {
                if (changed) { Set(head, "curAttack", (float)Get(head, "curAttack") + 1); Invoke(typeof(Program), "RefreshBonuses"); }
                if (fail) throw new InvalidOperationException("fixture auto-boost failure");
            };
            try
            {
                if (fail) Throws(() => CallMain("RunCustomAutoBoost"), "fixture auto-boost failure", label + " propagates native boundary failure");
                else CallMain("RunCustomAutoBoost");
            }
            finally { AutoBoostAction = null; }
            Equal(calls + 1, AutoBoostCalls, label + " invokes inventory boundary exactly once");
            Equal(attack + (foreign ? 1 : 0) + (changed ? 1 : 0), Get(head, "curAttack"), label + " performs the actual fixture material mutation");
            Equal(autoUpdate && changed && !foreign, Get(MainInstance, "_customEquipmentApplicationInvalidated"), label + " invalidates only an owned changed auto-updating policy, including finally on throw");
            Equal(true, CallMain("GlobalPresetGearMatches", receipt), label + " retains exact acquired gear through boost invalidation");
            var expectedMaterial = changed && !foreign
                ? ((Array)items).Cast<object>().Select(x => (string)CallMain("LoadoutPresetPrimitiveKey", x)).ToArray() : originalMaterial;
            Check(((string[])Get(policy, "Material")).SequenceEqual(expectedMaterial), label + " records only the causal revision of the same acquired instances, independently of application invalidation");
            Check(materialReference.SequenceEqual(originalMaterial), label + " never edits the acquired material array in place");
            if (!changed || foreign)
                Check(ReferenceEquals(materialReference, Get(policy, "Material")), label + " never replaces unchanged or unaccepted material observations");
            else Check(!ReferenceEquals(materialReference, Get(policy, "Material")), label + " publishes a new approved material version without mutating the original acquisition array");
            Check(ReferenceEquals(receipt, Get(MainInstance, "_globalPresetReceipt")) && ReferenceEquals(policy, Get(receipt, "Policy"))
                && ReferenceEquals(candidate, Get(policy, "Candidate")) && ReferenceEquals(items, Get(policy, "Items"))
                && ((Array)Get(policy, "Items")).Cast<object>().Select((x, i) => ReferenceEquals(x, selectedItems[i])).All(x => x)
                && ((int[])Get(policy, "Slots")).SequenceEqual(slots), label + " retains exact acquired gear decisions and policy identity");
            Check(ReferenceEquals(policy, ((IDictionary)Get(MainInstance, "_globalPresetPolicies"))[id]), label + " keeps the same reusable cache policy");
            Equal(true, CallMain("GlobalPresetGearMatches", receipt), label + " leaves exact equipped instances in their acquired slots");
            Equal(originalDefinition, Invoke(preset, "ToJson").ToString(), label + " never edits preset configuration");
            Equal(historical, Get(MainInstance, "_loadoutPresetSnapshot").ToString(), label + " never rewrites applied history");
            Equal(allocations, CallMain("GlobalPresetAllocationState"), label + " never allocates resources during boosting");
            Equal(support, CallMain("GlobalPresetSupportState"), label + " never changes support during boosting");
            Equal(searches, PresetSearches, label + " wrapper never searches"); Equal(captures, PresetCaptures, label + " wrapper never captures a new plan");
            Equal(recalls, Get(typeof(Program), "Recalls"), label + " wrapper never recalls"); Equal(swaps, Get(typeof(Program), "Swaps"), label + " wrapper never swaps");
            if (foreign)
            {
                Equal(false, CallMain("GlobalPresetReceiptValid", "dashboard", false, true), label + " cannot launder a prior manual material change");
                Throws(() => CallMain("ReplayGlobalPreset", policy, preset, CallMain("CaptureLoadoutPresetDefinition", preset)), "mutated", label + " cached replay still rejects manually mutated gear");
                CallMain("RunManualGlobalPresetRefresh");
                Equal(searches, PresetSearches, label + " a foreign change does not request automatic re-equip");
                Equal(recalls, Get(typeof(Program), "Recalls"), label + " unchanged desired allocations survive material continuation without recalls");
                Equal(false, Get(receipt, "CurrentGearContinuation"), label + " foreign material remains yielded while independent resources are observed");
                Equal(false, CallMain("GlobalPresetReceiptValid", "dashboard"), label + " fresh resource observation cannot grant validity to foreign material");
                Equal(true, Property(Get(MainType, "_presetResourceOutcome"), "Verified"), label + " independent resource model remains verified without adopting material");
                Check(((string[])Get(policy, "Material")).SequenceEqual(originalMaterial), label + " continuation cannot launder the stale cached material observation");
                Check(ReferenceEquals(receipt, Get(MainInstance, "_globalPresetReceipt")), label + " continuation never reacquires gear");
            }
            else if (changed && autoUpdate)
            {
                Equal(false, CallMain("GlobalPresetReceiptValid", "dashboard", false, true), label + " old policy waits for re-equip");
                CallMain("RunManualGlobalPresetRefresh");
                Equal(searches + 1, PresetSearches, label + " owned boost causes exactly one optimizer acquisition on the next refresh");
                Check(!ReferenceEquals(receipt, Get(MainInstance, "_globalPresetReceipt")), label + " explicit restoration installs a fresh receipt");
                Equal(false, Get(MainInstance, "_customEquipmentApplicationInvalidated"), label + " successful re-equip clears invalidation");
                Equal(true, CallMain("GlobalPresetReceiptValid", "dashboard"), label + " re-equip verifies boosted material");
                CallMain("RunManualGlobalPresetRefresh");
                Equal(searches + 1, PresetSearches, label + " stable boosted gear does not repeatedly optimize");
            }
            else
            {
                Equal(true, CallMain("GlobalPresetReceiptValid", "dashboard", false, true), label + " unchanged or accepted cached material remains owned");
                Equal(true, CallMain("RefreshGlobalPresetAllocations", "dashboard"), label + " same acquired decisions can replay after boosting");
                Equal(searches, PresetSearches, label + " cached or unchanged policy refresh never searches");
                Equal(true, CallMain("GlobalPresetReceiptValid", "dashboard"), label + " replay repairs waiting or displaced allocations with a valid receipt");
                Check(ReferenceEquals(receipt, Get(MainInstance, "_globalPresetReceipt")) && ReferenceEquals(policy, Get(receipt, "Policy"))
                    && ReferenceEquals(candidate, Get(policy, "Candidate"))
                    && ((Array)Get(policy, "Items")).Cast<object>().Select((x, i) => ReferenceEquals(x, selectedItems[i])).All(x => x)
                    && ((int[])Get(policy, "Slots")).SequenceEqual(slots), label + " refresh preserves cached decisions instead of reacquiring");
            }
        }
        CheckScheduledCachedAutoBoostRestoration();
        Console.WriteLine("Preset auto-boost regressions: " + (Assertions - before) + " assertions");
    }

    private static void CheckScheduledCachedAutoBoostRestoration()
    {
        Invoke(typeof(Program), "Setup");
        Character = Get(typeof(Program), "Character"); Controller = Get(typeof(Program), "Controller"); MainInstance = Get(typeof(Program), "MainInstance");
        PrepareSupport(); SetActiveBeards();
        Set(MainInstance, "_invManager", Activator.CreateInstance(Plugin.GetType("NGUInjector.Managers.InventoryManager", true), true));
        Set(MainInstance, "_globalPresetPolicies", Activator.CreateInstance(Field(MainInstance, "_globalPresetPolicies").FieldType));
        var settings = Get(MainType, "Settings");
        Set(settings, "_manualAdventureEnabled", false); Set(settings, "_customAdventureEnabled", false);
        Set(Get(Get(Character, "buttons"), "ngu"), "m_Interactable", true);
        var ngu = Get(Character, "NGU"); Set(ngu, "autoAdvance", false);
        ListField(ngu, "skills", 11); ListField(ngu, "magicSkills", 11);
        var store = Get(MainType, "CustomPlanStore"); var library = Get(store, "<PresetLibrary>k__BackingField");
        var root = Invoke(library, "ReadExternalLibrary");
        const string id = "cached-boost-stage-restoration";
        var definition = JsonParse("{\"Id\":\"" + id + "\",\"Name\":\"Cached boost stage restoration\",\"Weights\":[1,1,0,0,0,0],\"MagicBloodPercent\":0,\"SupportMode\":\"current\",\"SwapSeconds\":0,\"AutoUpdate\":false}");
        Invoke(Json(root, "Presets"), "Add", "", definition);
        var revision = Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
        var preview = CallMain("PreviewGlobalPreset", JsonParse("{\"presetId\":\"" + id + "\",\"libraryRevision\":\"" + revision + "\"}"));
        var apply = JsonParse("{\"presetId\":\"" + id + "\",\"libraryRevision\":\"" + revision + "\",\"previewId\":" + Json(preview, "previewId") + ",\"candidateKey\":\"current\"}");
        Invoke(MainInstance, "ApplyGlobalPreset", apply, null);
        var searches = PresetSearches;
        var plan = Invoke(store, "Load", "fixture");
        var stage = Activator.CreateInstance(Plugin.GetType("NGUInjector.CustomPlans.CustomPlanStage", true));
        Set(stage, "Name", "Cached boost restoration"); Set(stage, "PresetId", id);
        Set(stage, "Percent", 100d); Set(stage, "DurationSeconds", 600);
        var stages = Array.CreateInstance(stage.GetType(), 1); stages.SetValue(stage, 0);
        Set(plan, "Stages", stages); Set(plan, "StageSchedulerEnabled", true); Set(plan, "StageLoopStartIndex", -1);
        Set(plan, "AutoActivateFruits", false); Set(plan, "AutoRebirthEnabled", false); Set(plan, "TimeTriggerEnabled", false); Set(plan, "AutoIronPillEnabled", false);
        Set(settings, "_useCustomRebirthPlan", true);
        CallMain("RunCustomPlanAutomation", plan);
        Equal(searches, PresetSearches, "cached boost stage enters using the preview-seeded policy without search");
        Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), "cached boost stage initially has verified ownership");
        var receipt = Get(MainInstance, "_globalPresetReceipt"); var policy = Get(receipt, "Policy");
        Equal(false, Get(Get(policy, "Preset"), "AutoUpdate"), "boost restoration fixture owns an AutoUpdate=false stage");
        var selectedItems = ((Array)Get(policy, "Items")).Cast<object>().ToArray();
        var slots = ((int[])Get(policy, "Slots")).ToArray(); var material = ((string[])Get(policy, "Material")).ToArray();
        var inventory = Get(Character, "inventory"); var rows = (IList)Get(inventory, "inventory");
        var head = Get(inventory, "head"); var empty = rows[0]; var attack = (float)Get(head, "curAttack");
        // Model a temporary owner's departure and exact gear return before the scheduled replay runs.
        Set(inventory, "head", empty); rows[0] = head; Invoke(typeof(Program), "RefreshBonuses");
        CallMain("InvalidateCustomEquipmentApplication");
        Equal(false, CallMain("GlobalPresetGearMatches", receipt), "temporary gear displacement differs from cached stage gear");
        Set(inventory, "head", head); rows[0] = empty; Invoke(typeof(Program), "RefreshBonuses");
        Equal(true, CallMain("GlobalPresetGearMatches", receipt), "temporary owner returns the exact acquired gear before boosting");
        Equal(true, Get(MainInstance, "_customEquipmentApplicationInvalidated"), "exact gear return still requires cached equipment replay");
        Equal(false, CallMain("GlobalPresetStageReceiptValid", plan), "restored but invalidated stage cannot claim training credit");
        var calls = AutoBoostCalls; var captures = PresetCaptures; var recalls = Get(typeof(Program), "Recalls");
        var allocations = CallMain("GlobalPresetAllocationState");
        AutoBoostAction = () => { Set(head, "curAttack", (float)Get(head, "curAttack") + 1); Invoke(typeof(Program), "RefreshBonuses"); };
        try
        {
            CallMain("RunCustomAutoBoost");
            Equal(calls, AutoBoostCalls, "cached invalidated stage defers inventory boost boundary entirely");
            Equal(attack, Get(head, "curAttack"), "deferred boost cannot mutate returned pinned gear before cached replay");
            Check(((string[])Get(policy, "Material")).SequenceEqual(material), "deferred boost retains the material required for cached replay");
            Equal(true, Get(MainInstance, "_customEquipmentApplicationInvalidated"), "deferred boost does not falsely complete restoration");
            Equal(captures, PresetCaptures, "deferred boost does not capture a replacement policy");
            Equal(recalls, Get(typeof(Program), "Recalls"), "deferred boost does not recall resources");
            Equal(allocations, CallMain("GlobalPresetAllocationState"), "deferred boost preserves actual stage allocations");
            Equal(searches, PresetSearches, "deferred boost never searches");

            CallMain("RunCustomPlanAutomation", plan);
            Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), "real scheduler restores cached stage receipt before boosting");
            Equal(false, Get(MainInstance, "_customEquipmentApplicationInvalidated"), "successful scheduled cached replay clears invalidation");
            Equal(true, CallMain("GlobalPresetGearMatches", Get(MainInstance, "_globalPresetReceipt")), "successful cached replay verifies actual scheduled gear");
            Equal(true, Get(Get(MainInstance, "_protectedScheduler"), "_armed"), "scheduled cached restoration rearms observed stage credit");
            Equal(searches, PresetSearches, "scheduled cached restoration never runs optimizer search");
            Equal(calls, AutoBoostCalls, "scheduled replay restores ownership without consuming the pending boost");
            var restoredReceipt = Get(MainInstance, "_globalPresetReceipt"); var restoredPolicy = Get(restoredReceipt, "Policy");
            Check(((Array)Get(restoredPolicy, "Items")).Cast<object>().Select((x, i) => ReferenceEquals(x, selectedItems[i])).All(x => x)
                && ((int[])Get(restoredPolicy, "Slots")).SequenceEqual(slots), "cached stage restoration preserves exact gear decisions");
            Check(((string[])Get(restoredPolicy, "Material")).SequenceEqual(material), "cached restoration retains unboosted acquired material");
            CallMain("VerifyLoadoutPresetAllocations", Get(restoredReceipt, "Execution"));

            CallMain("RunCustomAutoBoost");
            Equal(calls + 1, AutoBoostCalls, "next boost executes exactly once after scheduled ownership is restored");
            Equal(attack + 1, Get(head, "curAttack"), "post-restoration boost performs the controlled material mutation");
            var boostedMaterial = ((Array)Get(restoredPolicy, "Items")).Cast<object>()
                .Select(x => (string)CallMain("LoadoutPresetPrimitiveKey", x)).ToArray();
            Check(!boostedMaterial.SequenceEqual(material) && ((string[])Get(restoredPolicy, "Material")).SequenceEqual(boostedMaterial),
                "cached owner accepts only the subsequent causally owned boost material");
            Equal(false, Get(MainInstance, "_customEquipmentApplicationInvalidated"), "owned cached boost does not restart restoration");
            Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), "owned boost preserves a valid scheduled receipt");

            Set(MainInstance, "_customLastQuickResourceRefresh", DateTime.MinValue);
            captures = PresetCaptures;
            recalls = Get(typeof(Program), "Recalls"); var transfers = ResourceTransfers;
            CallMain("RunQuickCustomResourceRefresh", plan);
            Equal(captures + 1, PresetCaptures, "post-boost unchanged desired allocations receive one fresh replay");
            Equal(recalls, Get(typeof(Program), "Recalls"), "post-boost idempotent replay never recalls resources");
            Equal(transfers, ResourceTransfers, "post-boost idempotent replay never transfers resources");
            Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), "post-boost quick refresh verifies scheduled cached ownership");
            CallMain("VerifyLoadoutPresetAllocations", Get(restoredReceipt, "Execution"));
            Check(ReferenceEquals(restoredReceipt, Get(MainInstance, "_globalPresetReceipt")) && ReferenceEquals(restoredPolicy, Get(restoredReceipt, "Policy"))
                && ((Array)Get(restoredPolicy, "Items")).Cast<object>().Select((x, i) => ReferenceEquals(x, selectedItems[i])).All(x => x)
                && ((int[])Get(restoredPolicy, "Slots")).SequenceEqual(slots), "post-boost refresh retains the restored receipt and exact cached gear decisions");
            Equal(searches, PresetSearches, "defer, cached restoration, owned boost and quick refresh perform no optimizer search");
            Equal(calls + 1, AutoBoostCalls, "post-boost quick refresh never repeats inventory boosting");
            Equal(true, Property(settings, "UseCustomRebirthPlan"), "cached boost restoration never pauses the scheduled plan");
        }
        finally { AutoBoostAction = null; }
    }

    private static void CheckScheduledIronPillReservation()
    {
        var before = Assertions;
        Invoke(typeof(Program), "Setup");
        Character = Get(typeof(Program), "Character"); Controller = Get(typeof(Program), "Controller"); MainInstance = Get(typeof(Program), "MainInstance");
        PrepareSupport(); SetActiveBeards();
        Set(MainInstance, "_globalPresetPolicies", Activator.CreateInstance(Field(MainInstance, "_globalPresetPolicies").FieldType));
        var settings = Get(MainType, "Settings");
        Set(settings, "_manualAdventureEnabled", false); Set(settings, "_customAdventureEnabled", false);
        Set(settings, "_useCustomRebirthPlan", true);
        var ngu = Get(Character, "NGU"); Set(ngu, "autoAdvance", false);
        ListField(ngu, "skills", 11); var magicRows = ListField(ngu, "magicSkills", 11);
        Set(Get(Get(Character, "buttons"), "ngu"), "m_Interactable", true);
        Set(Get(Get(Character, "buttons"), "bloodMagic"), "m_Interactable", true);
        var magic = Get(Character, "magic");
        Set(magic, "magicPower", 1f); Set(magic, "capMagic", 1000L); Set(magic, "curMagic", 1000L); Set(magic, "idleMagic", 1000L);

        // Populate native formula inputs, not substitutes for ritual getters or the reservation planner.
        var bloodController = Get(Character, "bloodMagicController");
        Sequence(bloodController, "normalSpeedDividers", 1, .002f);
        var ritualController = ((Array)Get(bloodController, "bloodMagics")).GetValue(0);
        Set(ritualController, "baseBoost", 10L); Set(ritualController, "baseCost", 1f);
        var ritual = ((IList)Get(Get(Character, "bloodMagic"), "ritual"))[0]; Set(ritual, "boss", 0);
        Set(Get(Character, "hacksController"), "character", Character);
        Set(Child(Character, "hacks"), "disabled", true);
        var perks = Get(Character, "beastQuestPerkController"); Set(perks, "character", Character);
        Sequence(perks, "quirkDifficultyReq", 92, Enum.Parse(Game.GetType("difficulty", true), "normal"));
        Sequence(perks, "effectPerLevel", 92, 0f);
        Sequence(Child(Character, "beastQuest"), "quirkLevel", 92, 0L);
        Equal(10d, Invoke(ritualController, "bloodAdded"), "Iron Pill fixture uses native productive blood getter");
        Equal(1f, Invoke(ritualController, "currentCost"), "Iron Pill fixture uses native ritual cost getter");

        var store = Get(MainType, "CustomPlanStore");
        var library = Get(store, "<PresetLibrary>k__BackingField"); var root = Invoke(library, "ReadExternalLibrary");
        var definition = JsonParse("{\"Id\":\"iron-pill-stage\",\"Name\":\"Iron Pill stage\",\"Weights\":[1,1,0,0,0,0],\"MagicBloodPercent\":0,\"SupportMode\":\"current\",\"SwapSeconds\":0}");
        Invoke(Json(root, "Presets"), "Add", "", definition);
        Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
        var plan = Invoke(store, "Load", "fixture");
        var stage = Activator.CreateInstance(Plugin.GetType("NGUInjector.CustomPlans.CustomPlanStage", true));
        Set(stage, "Name", "Iron Pill weighted stage"); Set(stage, "PresetId", "iron-pill-stage");
        Set(stage, "Percent", 100d); Set(stage, "DurationSeconds", 600);
        var stages = Array.CreateInstance(stage.GetType(), 1); stages.SetValue(stage, 0);
        Set(plan, "Stages", stages); Set(plan, "StageSchedulerEnabled", true); Set(plan, "StageLoopStartIndex", -1);
        Set(plan, "AutoActivateFruits", false); Set(plan, "AutoRebirthEnabled", false); Set(plan, "TimeTriggerEnabled", false);
        Set(plan, "AutoIronPillEnabled", false); // Drive reservation state directly, without spell casting or cooldown UI.
        CallMain("RunCustomPlanAutomation", plan);
        Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), "zero-blood weighted stage acquires a valid receipt");
        var receipt = Get(MainInstance, "_globalPresetReceipt"); var policy = Get(receipt, "Policy");
        var acquired = Get(policy, "Candidate"); var preset = Get(policy, "Preset");
        var owner = (string)Get(receipt, "Owner"); Check(owner.StartsWith("stage:", StringComparison.Ordinal), "Iron Pill fixture is owned by the scheduled stage");
        Equal(0d, ((double[])Get(preset, "Weights"))[2], "acquired weighted preset gives blood zero weight");
        Equal(0, Get(acquired, "BloodPercent"), "acquired candidate pins original zero-blood percentage");
        Equal(-1, Get(acquired, "Ritual"), "acquired candidate has no original ritual"); Equal(0L, Get(ritual, "magic"), "initial stage allocates no ritual magic");
        Equal(1000L, Get(magicRows[0], "magic"), "initial stage allocates the exact capped magic pool to NGU");
        var originalShares = ((long[])Get(acquired, "MagicShares")).ToArray();
        var originalPreset = Invoke(preset, "ToJson").ToString(); var originalEnergy = CallMain("GlobalPresetLaneState", true);
        var originalMagic = CallMain("GlobalPresetLaneState", false); var originalSupport = CallMain("GlobalPresetSupportState");
        var historical = Get(MainInstance, "_loadoutPresetSnapshot").ToString();
        var searches = PresetSearches; var captures = PresetCaptures; var swaps = Get(typeof(Program), "Swaps");
        Set(MainInstance, "_customIronPillPlanName", Property(settings, "CustomPlanFile"));
        Set(MainInstance, "_customIronPillPrepStartedAt", (double)Get(Get(Character, "rebirthTime"), "totalseconds"));
        Equal(true, MainType.GetProperty("IronPillBloodReserved", All).GetValue(null), "native reservation fields activate Iron Pill overlay");
        for (var refresh = 0; refresh < 2; refresh++)
        {
            var refreshRecalls = (int)Get(typeof(Program), "Recalls"); var refreshTransfers = ResourceTransfers;
            Set(MainInstance, "_customLastQuickResourceRefresh", DateTime.MinValue);
            CallMain("RunQuickCustomResourceRefresh", plan);
            Equal(refreshRecalls, Get(typeof(Program), "Recalls"), "Iron Pill demand uses exact differences without whole-lane recalls " + refresh);
            if (refresh == 1) Equal(refreshTransfers, ResourceTransfers, "repeated composed Iron Pill demand never transfers resources");
            // Native .002f divider and unit magic power need 101 magic including the planner's cap buffer.
            Equal(101L, Get(ritual, "magic"), "quick refresh reserves exact productive ritual magic " + refresh);
            Equal(899L, Get(magicRows[0], "magic"), "quick refresh returns exact ritual overflow to selected NGU " + refresh);
            Check(magicRows.Cast<object>().Skip(1).All(x => (long)Get(x, "magic") == 0), "quick refresh preserves excluded NGU rows and native tails " + refresh);
            Equal(0L, Get(magic, "idleMagic"), "quick refresh conserves the whole capped magic pool " + refresh);
            Check((float)Invoke(ritualController, "progressPerTick") >= 1f, "native ritual getter confirms full productive funding " + refresh);
            Equal(originalEnergy, CallMain("GlobalPresetLaneState", true), "reservation preserves actual energy allocations " + refresh);
            Equal(originalSupport, CallMain("GlobalPresetSupportState"), "reservation preserves actual support " + refresh);
            var execution = Get(receipt, "Execution");
            Equal(-1, Get(execution, "Ritual"), "receipt execution never pins temporary ritual " + refresh);
            Equal(0L, Get(execution, "Blood"), "receipt execution never pins temporary blood amount " + refresh);
            Equal(0, Get(execution, "BloodPercent"), "receipt execution retains original split " + refresh);
            Check(((long[])Get(execution, "MagicShares")).SequenceEqual(originalShares), "receipt execution retains restorable NGU shares " + refresh);
            Equal(CallMain("GlobalPresetAllocationState"), Get(receipt, "Allocations"), "receipt observes actual temporary allocation separately " + refresh);
            Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), "reservation refresh retains valid scheduled ownership " + refresh);
            Equal(true, Get(Get(MainInstance, "_protectedScheduler"), "_armed"), "reservation refresh rearms verified stage credit " + refresh);
            Equal(searches, PresetSearches, "reservation quick refresh never searches " + refresh);
        }
        CallMain("ResetCustomIronPillPrep");
        Equal(false, MainType.GetProperty("IronPillBloodReserved", All).GetValue(null), "cancelling reservation releases overlay eligibility");
        Set(MainInstance, "_customLastQuickResourceRefresh", DateTime.MinValue);
        CallMain("RunQuickCustomResourceRefresh", plan);
        Equal(0L, Get(ritual, "magic"), "release recalls temporary ritual allocation");
        Equal(1000L, Get(magicRows[0], "magic"), "release restores original exact NGU allocation");
        Equal(originalMagic, CallMain("GlobalPresetLaneState", false), "release restores the entire original magic lane");
        Equal(originalEnergy, CallMain("GlobalPresetLaneState", true), "release leaves energy allocations unchanged");
        Equal(-1, Get(Get(receipt, "Execution"), "Ritual"), "release restores original no-ritual policy");
        Equal(0, Get(Get(receipt, "Execution"), "BloodPercent"), "release restores original zero-blood split");
        CallMain("VerifyLoadoutPresetAllocations", Get(receipt, "Execution"));
        Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), "release leaves a valid observed scheduled receipt");
        Check(ReferenceEquals(receipt, Get(MainInstance, "_globalPresetReceipt")) && ReferenceEquals(policy, Get(receipt, "Policy"))
            && ReferenceEquals(acquired, Get(policy, "Candidate")), "reservation and release never reacquire receipt or policy");
        Equal(originalPreset, Invoke(preset, "ToJson").ToString(), "reservation never mutates the acquired preset definition");
        Equal(-1, Get(acquired, "Ritual"), "reservation never mutates acquired ritual"); Equal(0L, Get(acquired, "Blood"), "reservation never mutates acquired blood allocation");
        Check(((long[])Get(acquired, "MagicShares")).SequenceEqual(originalShares), "reservation never mutates acquired NGU shares");
        Equal(historical, Get(MainInstance, "_loadoutPresetSnapshot").ToString(), "reservation never rewrites applied historical snapshot");
        Equal(searches, PresetSearches, "reservation and release perform no optimizer search");
        Equal(swaps, Get(typeof(Program), "Swaps"), "reservation and release never swap gear");
        Equal(captures + 5, PresetCaptures, "demand entry/release capture before and after recall, unchanged demand captures once");
        Equal(true, Property(settings, "UseCustomRebirthPlan"), "Iron Pill refresh never pauses the scheduled plan");
        Console.WriteLine("Scheduled Iron Pill regressions: " + (Assertions - before) + " assertions");
    }

    private static void CheckGlobalPresets()
    {
        Invoke(typeof(Program), "Setup");
        Character = Get(typeof(Program), "Character"); Controller = Get(typeof(Program), "Controller"); MainInstance = Get(typeof(Program), "MainInstance");
        PrepareSupport(); SetActiveBeards(3, 6);
        ListField(Get(Character, "NGU"), "skills", 11); ListField(Get(Character, "NGU"), "magicSkills", 11);
        AllowPresetSettingsSave = true;
        Set(Get(Get(Character, "buttons"), "ngu"), "m_Interactable", true);
        Set(Get(Character, "NGU"), "autoAdvance", false);
        var settings = Get(MainType, "Settings");
        Set(settings, "_savePath", Path.Combine((string)Get(MainType, "_dir"), "global-settings.json"));
        Set(settings, "_manualAdventureEnabled", false); Set(settings, "_customAdventureEnabled", false);
        var store = Get(MainType, "CustomPlanStore");
        var libraryType = Plugin.GetType("NGUInjector.CustomPlans.PresetLibraryStore", true);
        var library = Activator.CreateInstance(libraryType, All, null,
            new[] { Get(typeof(Program), "Temp"), Get(store, "_plansDir") }, null);
        Set(store, "<PresetLibrary>k__BackingField", library);
        Set(MainInstance, "_globalPresetPolicies", Activator.CreateInstance(Field(MainInstance, "_globalPresetPolicies").FieldType));
        var root = Invoke(library, "ReadExternalLibrary");
        var presetJson = JsonParse("{\"Id\":\"runtime-fixture\",\"Name\":\"Runtime fixture\",\"Weights\":[1,1,0,0,0,0],\"SupportMode\":\"current\",\"HorizonSeconds\":10,\"SwapSeconds\":0,\"EnergyNguWeights\":[1,0,0,0,0,0,0,0,0],\"MagicNguWeights\":[1,0,0,0,0,0,0]}");
        Invoke(Json(root, "Presets"), "Add", "", presetJson);
        var revision = Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
        Func<object> args = () => JsonParse("{\"presetId\":\"runtime-fixture\",\"libraryRevision\":\"" + revision + "\"}");
        var searches = PresetSearches;
        Invoke(MainInstance, "EquipGlobalPreset", args(), null);
        Equal(searches + 1, PresetSearches, "AutoUpdate true explicit equip performs exactly one full search");
        Equal(false, Property(settings, "UseCustomRebirthPlan"), "manual equip pauses the plan after preflight");
        Equal(false, Property(settings, "AutoRebirth"), "manual equip pauses automatic rebirth after preflight");
        Equal(true, CallMain("GlobalPresetReceiptValid", "dashboard"), "successful equip creates an observed ownership receipt");
        searches = PresetSearches;
        Equal(true, CallMain("RefreshGlobalPresetAllocations", "dashboard"), "acquired policy allocation refresh succeeds");
        Equal(searches, PresetSearches, "allocation refresh never runs a full search");

        var acquiredReceipt = Get(MainInstance, "_globalPresetReceipt");
        var acquiredPolicy = Get(acquiredReceipt, "Policy");
        root = Invoke(library, "ReadExternalLibrary");
        var edited = ((IEnumerable)GetChildren(Json(root, "Presets"))).Cast<object>().Single(x => Property(Json(x, "Id"), "Value").Equals("runtime-fixture"));
        SetJson(edited, "SwapSeconds", Json(JsonParse("{\"value\":1}"), "value"));
        revision = Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
        CallMain("RunManualGlobalPresetRefresh");
        Equal(searches, PresetSearches, "saving an AutoUpdate true definition never starts a search");
        Check(ReferenceEquals(acquiredReceipt, Get(MainInstance, "_globalPresetReceipt")), "a saved edit never reacquires the Dashboard receipt");
        Equal(0, Get(Get(acquiredPolicy, "Preset"), "SwapSeconds"), "refresh keeps the immutable acquired definition");
        Equal(true, CallMain("GlobalPresetReceiptValid", "dashboard"), "saved edits do not stop active ownership");
        var pendingRuntime = CallMain("GetLoadoutPresetRuntime");
        Equal("true", Json(pendingRuntime, "savedChangesRequireReequip").ToString(), "telemetry marks saved changes pending re-equip");
        Equal("true", Json(pendingRuntime, "active").ToString(), "pending saved edits are not fictitious displacement");
        var mutatedHead = Get(Get(Character, "inventory"), "head");
        Set(mutatedHead, "level", 9);
        var invalidReceiptRecalls = Get(typeof(Program), "Recalls");
        CallMain("RunManualGlobalPresetRefresh");
        Equal(searches, PresetSearches, "AutoUpdate true does not search merely because selected gear material changed");
        Equal(invalidReceiptRecalls, Get(typeof(Program), "Recalls"), "material drift with unchanged desired resources never recalls either lane");
        Check(ReferenceEquals(acquiredReceipt, Get(MainInstance, "_globalPresetReceipt")), "material continuation preserves acquired receipt");
        Equal(false, Get(acquiredReceipt, "CurrentGearContinuation"), "unapproved material drift cannot become acquired gear authority");
        Equal(0, Get(Get(acquiredPolicy, "Preset"), "SwapSeconds"), "material continuation cannot activate the saved edit");
        Equal(false, CallMain("GlobalPresetReceiptValid", "dashboard"), "independent resource observation cannot grant full validity to foreign material");
        Set(mutatedHead, "level", 0);
        SetJson(edited, "SwapSeconds", Json(JsonParse("{\"value\":0}"), "value"));
        revision = Invoke(library, "SaveExternalLibrary", root, revision);

        root = Invoke(library, "ReadExternalLibrary");
        var stored = ((IEnumerable)GetChildren(Json(root, "Presets"))).Cast<object>().Single(x => Property(Json(x, "Id"), "Value").Equals("runtime-fixture"));
        SetJson(stored, "AutoUpdate", Json(JsonParse("{\"value\":false}"), "value"));
        SetJson(stored, "Name", Json(JsonParse("{\"value\":\"Renamed runtime fixture\"}"), "value"));
        revision = Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
        CallMain("RunManualGlobalPresetRefresh");
        pendingRuntime = CallMain("GetLoadoutPresetRuntime");
        Equal("false", Json(pendingRuntime, "autoUpdate").ToString(), "saved AutoUpdate is reported as configuration");
        Equal("true", Json(pendingRuntime, "appliedAutoUpdate").ToString(), "saved AutoUpdate does not change the acquired execution flag");
        Equal("true", Json(pendingRuntime, "savedChangesRequireReequip").ToString(), "configuration-only edits remain pending without invalidating the policy cache");
        Equal("\"Renamed runtime fixture\"", Json(pendingRuntime, "name").ToString(), "saved names are reflected without activation");
        Set(Character, "realGold", 1e10);
        Set(((IList)Get(Get(Character, "NGU"), "skills"))[0], "level", 5L);
        Invoke(MainInstance, "EquipGlobalPreset", args(), null);
        Equal(searches, PresetSearches, "AutoUpdate false replays across name/flag/gold/NGU progress without searching");

        var cachedPolicy = Get(Get(MainInstance, "_globalPresetReceipt"), "Policy");
        var magicSkill = ((IList)Get(Get(Character, "NGU"), "magicSkills"))[0];
        var magicState = Get(Character, "magic");
        Set(magicState, "idleMagic", (long)Get(magicState, "idleMagic") + (long)Get(magicSkill, "magic") - 17L);
        Set(magicSkill, "magic", 17L);
        var training = Get(Character, "training"); ((long[])Get(training, "_attackEnergy"))[0] = 37;
        var res3 = Child(Character, "res3"); Set(res3, "curRes3", 123L); Set(res3, "idleRes3", 51L);
        var stop = CallMain("StopCustomFocusGoal");
        Check(stop.ToString().Contains("Stopped global preset"), "Clear quick stops global focus with no legacy goal");
        Equal(null, Get(MainInstance, "_globalPresetManualId"), "Clear quick retires manual global selection");
        Equal(null, Get(MainInstance, "_globalPresetReceipt"), "Clear quick retires the active application receipt");
        Equal(0L, Get(((IList)Get(Get(Character, "NGU"), "skills"))[0], "energy"), "stop recalls the still-owned energy lane");
        Equal(17L, Get(magicSkill, "magic"), "stop preserves the displaced magic lane");
        Equal(37L, ((long[])Get(training, "_attackEnergy"))[0], "stop preserves basic training");
        Equal(123L, Get(res3, "curRes3"), "stop does not touch R3 total");
        Equal(51L, Get(res3, "idleRes3"), "stop does not touch R3 allocation");
        CallMain("RunQuickCustomResourceRefresh", (object)null);
        Equal(searches, PresetSearches, "stopped global focus cannot auto-refresh or re-equip");
        Equal(0L, Get(((IList)Get(Get(Character, "NGU"), "skills"))[0], "energy"), "stopped energy remains recalled after a quick tick");
        // The stop-preservation probe is complete; later gear tests do not model native R3 cap inputs.
        Set(res3, "curRes3", 0L); Set(res3, "idleRes3", 0L);
        Check(ReferenceEquals(cachedPolicy, ((IDictionary)Get(MainInstance, "_globalPresetPolicies"))["runtime-fixture"]), "stop preserves the exact reusable policy object");
        Set(magicSkill, "magic", 0L); Set(magicState, "idleMagic", (long)Get(magicState, "idleMagic") + 17L);
        ((long[])Get(training, "_attackEnergy"))[0] = 0;
        Invoke(MainInstance, "EquipGlobalPreset", args(), null);
        Equal(searches, PresetSearches, "stop preserves reusable false policies");
        Check((long)Get(magicSkill, "magic") > 0, "re-equipped policy owns a positive magic allocation");
        CallMain("StopCustomFocusGoal");
        Equal(0L, Get(magicSkill, "magic"), "stop recalls a matching owned magic lane");
        Equal(0L, Get(((IList)Get(Get(Character, "NGU"), "skills"))[0], "energy"), "stop recalls both matching owned lanes");
        Invoke(MainInstance, "EquipGlobalPreset", args(), null);
        Equal(searches, PresetSearches, "repeated stop and false re-equip retain the policy cache");

        var preset = Invoke(library, "Get", "runtime-fixture");

        var preview = CallMain("PreviewGlobalPreset", args());
        searches++;
        var apply = JsonParse(args().ToString().TrimEnd('}') + ",\"previewId\":" + Json(preview, "previewId") + ",\"candidateKey\":\"current\"}");
        Invoke(MainInstance, "ApplyGlobalPreset", apply, null);
        Equal(searches, PresetSearches, "explicit selected Apply replays without a second full search");
        var selected = Get(Get(Get(MainInstance, "_globalPresetReceipt"), "Policy"), "Candidate");
        Equal("current", Get(selected, "Key"), "Preview + Apply seeds the exact selected candidate");
        var inv = Get(Character, "inventory"); var inventoryItems = (IList)Get(inv, "inventory");
        var selectedHead = Get(inv, "head"); var empty = inventoryItems[0];
        Set(inv, "head", empty); inventoryItems[0] = selectedHead; Invoke(typeof(Program), "RefreshBonuses");
        var movedCapture = CallMain("CaptureLoadoutPresetDefinition", preset);
        var movedPolicy = CallMain("ReplayGlobalPreset", Get(Get(MainInstance, "_globalPresetReceipt"), "Policy"), preset, movedCapture);
        Equal(0, Get(((Array)Get(movedPolicy, "Items")).GetValue(0), "SourceSlot"), "cached policy resolves exact selected instances across inventory-slot moves");
        Set(inv, "head", selectedHead); inventoryItems[0] = empty; Invoke(typeof(Program), "RefreshBonuses");
        Invoke(MainInstance, "EquipGlobalPreset", args(), null);
        Equal(searches, PresetSearches, "slot-independent replay never searches");

        var plan = Invoke(store, "Load", "fixture");
        var stage = Activator.CreateInstance(Plugin.GetType("NGUInjector.CustomPlans.CustomPlanStage", true));
        Set(stage, "Name", "Weighted fixture"); Set(stage, "PresetId", "runtime-fixture"); Set(stage, "DurationSeconds", 10);
        Set(plan, "ActivePresetId", "runtime-fixture");
        Set(stage, "PresetId", "");
        Equal("", CallMain("StageGlobalPresetId", plan, stage), "empty stage binding retains legacy semantics despite an active plan preset");
        Set(stage, "PresetId", "@active");
        Equal("runtime-fixture", CallMain("StageGlobalPresetId", plan, stage), "explicit @active binding resolves the committed plan selection");
        Set(stage, "PresetId", "runtime-fixture");
        Set(stage, "Percent", 0d); Set(stage, "GearGoal", 0);
        var stages = Array.CreateInstance(stage.GetType(), 1); stages.SetValue(stage, 0);
        Set(plan, "Stages", stages); Set(plan, "StageSchedulerEnabled", true); Set(plan, "StageLoopStartIndex", -1);
        Set(plan, "AutoActivateFruits", false); Set(plan, "AutoRebirthEnabled", false); Set(plan, "TimeTriggerEnabled", false);
        Set(settings, "_useCustomRebirthPlan", true); Set(settings, "_autoRebirth", true);
        var scheduledSearches = PresetSearches; var scheduledSettingsWrites = PresetSettingsWrites;
        CallMain("RunCustomPlanAutomation", plan);
        Equal(true, Property(settings, "UseCustomRebirthPlan"), "real scheduled apply must not self-pause its plan");
        Equal(true, Property(settings, "AutoRebirth"), "real scheduled apply must preserve automatic rebirth");
        Equal(Get(MainInstance, "_customStageEquipmentKey"), Get(Get(MainInstance, "_globalPresetReceipt"), "Owner"), "real scheduled apply preserves its exact acquired owner identity");
        Equal(scheduledSearches, PresetSearches, "real scheduled false re-equip never searches");
        Equal(scheduledSettingsWrites, PresetSettingsWrites, "real scheduled apply performs no settings persistence");
        Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), "weighted stage earns an observed successful application receipt");
        var scheduler = Get(MainInstance, "_protectedScheduler");
        Equal(true, Get(scheduler, "_armed"), "weighted receipt arms protected stage credit without legacy allocator success");
        searches = PresetSearches;
        CallMain("RunCustomPlanAutomation", plan);
        Equal(searches, PresetSearches, "repeated weighted scheduler ticks do not search");
        CallMain("RunQuickCustomResourceRefresh", plan);
        Equal(searches, PresetSearches, "weighted quick allocation refresh does not search");
        Equal(true, Property(settings, "UseCustomRebirthPlan"), "weighted scheduler never pauses itself");
        Set(((IList)Get(Get(Character, "NGU"), "skills"))[0], "energy", 0L);
        Equal(false, CallMain("GlobalPresetStageReceiptValid", plan), "displaced allocations cannot claim a successful receipt");
        CallMain("CommitProtectedStageApplication", plan);
        Equal(false, Get(scheduler, "_armed"), "invalid weighted receipt suspends credit instead of trusting old allocator success");
        CallMain("RunCustomPlanAutomation", plan);
        Equal(searches, PresetSearches, "allocation repair replays the acquired policy without searching");
        Equal(true, Get(scheduler, "_armed"), "observed replay rearms credit after allocation repair");

        var recalls = Get(typeof(Program), "Recalls"); var swaps = Get(typeof(Program), "Swaps");
        var badSettingsWrites = PresetSettingsWrites;
        Set(Get(Get(Character, "inventory"), "head"), "level", 9);
        Throws(() => Invoke(MainInstance, "EquipGlobalPreset", args(), null), "mutated", "mutated selected gear rejects stale reusable policy");
        Equal(recalls, Get(typeof(Program), "Recalls"), "stale cache rejection occurs before recalls");
        Equal(swaps, Get(typeof(Program), "Swaps"), "stale cache rejection occurs before gear mutation");
        Equal(true, Property(settings, "UseCustomRebirthPlan"), "failed public preflight does not pause the plan");
        Equal(true, Property(settings, "AutoRebirth"), "failed public preflight does not pause rebirth");
        Equal(searches, PresetSearches, "stale false policy cannot silently search a replacement");
        Equal(badSettingsWrites, PresetSettingsWrites, "stale public preflight does not write settings");
        Set(Get(inv, "head"), "level", 0);
        var uncached = Invoke(Plugin.GetType("NGUInjector.CustomPlans.CustomLoadoutPreset", true), "Parse", presetJson);
        Set(uncached, "Id", "uncached-runtime-fixture"); Set(uncached, "AutoUpdate", false);
        Throws(() => Invoke(MainInstance, "EquipGlobalPresetDefinition", uncached, "dashboard", "explicit-equip", true, null),
            "Preview + Apply", "disabled optimizer with no cache clearly directs the user to Preview + Apply");
        Equal(recalls, Get(typeof(Program), "Recalls"), "missing cache rejects before recalls");
        Equal(swaps, Get(typeof(Program), "Swaps"), "missing cache rejects before swaps");
        Set(Get(Character, "stats"), "rebirthNumber", 8L);
        Throws(() => Invoke(MainInstance, "EquipGlobalPreset", args(), null), "epoch", "rebirth invalidates reusable false policies");
        Equal(recalls, Get(typeof(Program), "Recalls"), "epoch rejection precedes recalls");
        Equal(swaps, Get(typeof(Program), "Swaps"), "epoch rejection precedes swaps");
        Set(Get(Character, "stats"), "rebirthNumber", 7L);

        root = Invoke(library, "ReadExternalLibrary");
        stored = ((IEnumerable)GetChildren(Json(root, "Presets"))).Cast<object>().Single(x => Property(Json(x, "Id"), "Value").Equals("runtime-fixture"));
        SetJson(stored, "SwapSeconds", Json(JsonParse("{\"value\":1}"), "value"));
        revision = Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
        acquiredReceipt = Get(MainInstance, "_globalPresetReceipt");
        CallMain("RunCustomPlanAutomation", plan);
        Equal(true, CallMain("RefreshGlobalPresetAllocations", Get(MainInstance, "_customStageEquipmentKey")), "false acquired policy refreshes after an incompatible saved edit");
        Equal(searches, PresetSearches, "a saved edit cannot restart the scheduler optimizer");
        Check(ReferenceEquals(acquiredReceipt, Get(MainInstance, "_globalPresetReceipt")), "a saved edit does not apply or replace the false stage policy");
        Equal(0, Get(Get(Get(acquiredReceipt, "Policy"), "Preset"), "SwapSeconds"), "weighted stage retains its acquired definition after save");
        Set(Get(inv, "head"), "level", 9);
        var beforeInvalidTick = Get(typeof(Program), "Recalls");
        CallMain("RunCustomPlanAutomation", plan);
        Equal(searches, PresetSearches, "receipt invalidation without a gear restoration event never searches");
        Equal(beforeInvalidTick, Get(typeof(Program), "Recalls"), "unchanged scheduled resource continuation never recalls or applies saved definition");
        Equal(0, Get(Get(Get(acquiredReceipt, "Policy"), "Preset"), "SwapSeconds"), "scheduled material continuation retains acquired definition");
        Equal(false, CallMain("GlobalPresetStageReceiptValid", plan), "unapproved material observation cannot restore dependent stage credit");
        Set(Get(inv, "head"), "level", 0);
        Throws(() => Invoke(MainInstance, "EquipGlobalPreset", args(), null), "definition", "incompatible definition edits invalidate reusable false policies");
        recalls = Get(typeof(Program), "Recalls");
        Throws(() => Invoke(MainInstance, "EquipGlobalPreset", args(), null), "definition", "repeated explicit stale equip still rejects");
        Equal(recalls, Get(typeof(Program), "Recalls"), "definition rejection precedes recalls");
        Equal(swaps, Get(typeof(Program), "Swaps"), "definition rejection precedes swaps");
        root = Invoke(library, "ReadExternalLibrary");
        stored = ((IEnumerable)GetChildren(Json(root, "Presets"))).Cast<object>().Single(x => Property(Json(x, "Id"), "Value").Equals("runtime-fixture"));
        SetJson(stored, "SwapSeconds", Json(JsonParse("{\"value\":0}"), "value"));
        SetJson(stored, "AutoUpdate", Json(JsonParse("{\"value\":true}"), "value"));
        revision = Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
        RestartFixtureStage(plan);
        searches = PresetSearches;
        CallMain("RunCustomPlanAutomation", plan);
        Equal(searches + 1, PresetSearches, "AutoUpdate true stage entry performs exactly one full search");
        searches = PresetSearches;
        CallMain("RunCustomPlanAutomation", plan);
        Equal(searches, PresetSearches, "AutoUpdate true does not optimize on repeated stage ticks");
        CallMain("InvalidateCustomEquipmentApplication");
        CallMain("RunCustomPlanAutomation", plan);
        Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), "current desired preset reconciliation verifies the invalidated stage");
        Equal(searches, PresetSearches, "application invalidation retains the acquired AutoUpdate policy without replacement search");
        searches = PresetSearches;
        CallMain("InvalidateCustomEquipmentApplication");
        Set(typeof(Program), "FailEnergyRecall", true);
        CallMain("RunCustomPlanAutomation", plan);
        Equal(searches, PresetSearches, "unchanged selected gear after invalidation performs no replacement search");
        searches = PresetSearches;
        CallMain("RunCustomPlanAutomation", plan);
        Equal(searches, PresetSearches, "unchanged re-equip never repeats a full search");
        Equal(true, Get(Get(MainInstance, "_protectedScheduler"), "_armed"), "unused native recall failure cannot poison verified unchanged desired state");
        Equal(true, Get(typeof(Program), "FailEnergyRecall"), "positive control: obsolete whole-lane recall is never invoked by unchanged desired state");
        Set(typeof(Program), "FailEnergyRecall", false);
    }

    private static void CheckPresetForecastHorizons()
    {
        Invoke(typeof(Program), "Setup");
        Character = Get(typeof(Program), "Character"); Controller = Get(typeof(Program), "Controller"); MainInstance = Get(typeof(Program), "MainInstance");
        PrepareSupport(); SetActiveBeards(3, 6);
        Set(MainInstance, "_globalPresetPolicies", Activator.CreateInstance(Field(MainInstance, "_globalPresetPolicies").FieldType));
        Set(Get(Get(Character, "buttons"), "ngu"), "m_Interactable", true); Set(Get(Character, "NGU"), "autoAdvance", false);
        var skill = ((IList)Get(Get(Character, "NGU"), "skills"))[0]; Set(skill, "target", 20L);
        var settings = Get(MainType, "Settings"); var store = Get(MainType, "CustomPlanStore");
        var library = Activator.CreateInstance(Plugin.GetType("NGUInjector.CustomPlans.PresetLibraryStore", true), All, null,
            new[] { Get(typeof(Program), "Temp"), Get(store, "_plansDir") }, null);
        Set(store, "<PresetLibrary>k__BackingField", library);
        var root = Invoke(library, "ReadExternalLibrary");
        Invoke(Json(root, "Presets"), "Add", "", JsonParse("{\"Id\":\"forecast-context\",\"Name\":\"Forecast context\",\"Weights\":[1,0,0,0,0,0],\"SupportMode\":\"current\",\"HorizonSeconds\":17,\"SwapSeconds\":0,\"EnergyNguWeights\":[1,0,0,0,0,0,0,0,0],\"MagicNguWeights\":[0,0,0,0,0,0,0]}"));
        var revision = Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
        var args = JsonParse("{\"presetId\":\"forecast-context\",\"libraryRevision\":\"" + revision + "\"}");
        var searches = PresetSearches;
        var preview = CallMain("PreviewGlobalPreset", args);
        Equal(searches + 1, PresetSearches, "standalone preview performs one real search");
        Equal(3600, Property(Json(preview, "horizonSeconds"), "AsInt"), "standalone preview ignores legacy preset duration and compares one hour");
        var cached = Get(MainInstance, "_loadoutPresetPreview");
        Equal(3600, Get(Get(cached, "Capture"), "Horizon"), "standalone capture carries only the session comparison horizon");
        Equal(20d / 3600, Property(Json(Json(Json(Json(Json(preview, "candidates"), 0), "metrics"), 0), "value"), "AsDouble"),
            "standalone forecast actually divides finite target production by one hour");
        var apply = JsonParse(args.ToString().TrimEnd('}') + ",\"previewId\":" + Json(preview, "previewId") + ",\"candidateKey\":\"current\"}");
        object[] invocation = { apply, null }; Invoke(MainInstance, "ApplyGlobalPreset", invocation);
        var policy = Get(Get(MainInstance, "_globalPresetReceipt"), "Policy");
        var definitionKey = Get(policy, "Definition");
        Equal(3600, Get(policy, "Horizon"), "standalone Preview + Apply acquires a one-hour session policy");
        Equal(3600, Property(Json(Json(invocation[1], "result"), "horizonSeconds"), "AsInt"), "standalone observed apply keeps the comparison horizon");
        foreach (var definition in new[] { Invoke(Get(policy, "Preset"), "ToJson"), Json(CallMain("GetLoadoutPresetRuntime"), "preset") })
            Check(!definition.ToString().Contains("HorizonSeconds") && !definition.ToString().Contains("DurationSeconds"), "acquisition never serializes a preset duration");

        root = Invoke(library, "ReadExternalLibrary");
        var stored = ((IEnumerable)GetChildren(Json(root, "Presets"))).Cast<object>().Single(x => Property(Json(x, "Id"), "Value").Equals("forecast-context"));
        Check(!stored.ToString().Contains("HorizonSeconds") && !stored.ToString().Contains("DurationSeconds"), "library serialization removes ignored legacy duration");
        SetJson(stored, "AutoUpdate", Json(JsonParse("{\"value\":false}"), "value"));
        Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
        var plan = Invoke(store, "Load", "fixture");
        var stage = Activator.CreateInstance(Plugin.GetType("NGUInjector.CustomPlans.CustomPlanStage", true));
        Set(stage, "Name", "Short forecast"); Set(stage, "PresetId", "forecast-context"); Set(stage, "DurationSeconds", 10);
        var stages = Array.CreateInstance(stage.GetType(), 1); stages.SetValue(stage, 0);
        Set(plan, "Stages", stages); Set(plan, "StageSchedulerEnabled", true); Set(plan, "StageLoopStartIndex", -1);
        Set(plan, "StageDurationSeconds", 1800); Set(plan, "TimeTriggerEnabled", false); Set(plan, "AutoRebirthEnabled", false); Set(plan, "AutoActivateFruits", false);
        Set(settings, "_useCustomRebirthPlan", true); searches = PresetSearches;
        CallMain("ResetProtectedStageProgress"); CallMain("RunCustomPlanAutomation", plan);
        Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), "short scheduled acquisition earns an actual receipt");
        policy = Get(Get(MainInstance, "_globalPresetReceipt"), "Policy");
        Equal(10, Get(policy, "Horizon"), "scheduler passes its short remaining stage budget, not the legacy seventeen seconds or one-hour default");
        Equal((double?)2d, ((double?[])Get(Get(policy, "Candidate"), "Metrics"))[0], "actual scheduled candidate forecasts twenty target levels over ten seconds");
        Equal(searches, PresetSearches, "cached AutoUpdate false policy is reusable from standalone to a different scheduled duration without search");

        var scheduler = Get(MainInstance, "_protectedScheduler");
        Set(scheduler, "_lastHeartbeat", (double)MainType.GetProperty("ProtectedHeartbeat", All).GetValue(null) - 2d);
        foreach (var clock in new[] { "rebirthTime", "totalPlaytime" })
            Set(Get(Character, clock), "totalseconds", (double)Get(Get(Character, clock), "totalseconds") + 2d);
        Equal(8, Property(CallMain("GetProtectedStage", plan), "RemainingSeconds"), "refresh fixture has a genuinely shorter live stage remainder");
        Set(skill, "level", 1L);
        var allocated = (long)Get(skill, "energy"); Check(allocated > 0, "forecast fixture can displace one unit to require real mutation");
        Set(skill, "energy", allocated - 1); Set(Character, "idleEnergy", (long)Get(Character, "idleEnergy") + 1);
        var horizons = new List<int>(); var metrics = new List<double?>();
        try
        {
            PresetEvaluationObserver = (candidate, request) => {
                horizons.Add((int)Get(request, "Horizon")); metrics.Add(((double?[])Get(candidate, "Metrics"))[0]);
            };
            Equal(true, CallMain("RefreshGlobalPresetAllocations", Get(MainInstance, "_customStageEquipmentKey")), "frozen-horizon allocation refresh succeeds");
        }
        finally { PresetEvaluationObserver = null; }
        Equal(2, horizons.Count, "both preflight and post-recall refresh forecasts are observed, not the stale acquisition snapshot");
        for (var i = 0; i < horizons.Count; i++)
        {
            Equal(10, horizons[i], "real refresh projection reuses its acquired horizon rather than current remainder or default");
            Equal((double?)(19d / 10), metrics[i], "refresh recomputes natural progress over the frozen ten-second forecast");
        }
        Check(ReferenceEquals(policy, Get(Get(MainInstance, "_globalPresetReceipt"), "Policy")), "refresh retains the exact acquired policy");
        Equal(searches, PresetSearches, "refresh never searches");
        Set(stage, "DurationSeconds", 30); RestartFixtureStage(plan); CallMain("RunCustomPlanAutomation", plan);
        Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), "new duration reacquires a valid cached stage policy");
        policy = Get(Get(MainInstance, "_globalPresetReceipt"), "Policy");
        Equal(30, Get(policy, "Horizon"), "explicit stage re-entry replaces the session horizon");
        Equal(definitionKey, Get(policy, "Definition"), "forecast durations never become part of the reusable serialized definition");
        Equal((double?)(19d / 30), ((double?[])Get(Get(policy, "Candidate"), "Metrics"))[0], "cached re-equip evaluates production using the new duration");
        Equal(searches, PresetSearches, "duration changes do not invalidate cached false policies or launch a replacement search");

        root = Invoke(library, "ReadExternalLibrary");
        stored = ((IEnumerable)GetChildren(Json(root, "Presets"))).Cast<object>().Single(x => Property(Json(x, "Id"), "Value").Equals("forecast-context"));
        SetJson(stored, "AutoUpdate", Json(JsonParse("{\"value\":true}"), "value"));
        Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
        Set(stage, "DurationSeconds", 12); RestartFixtureStage(plan); CallMain("RunCustomPlanAutomation", plan);
        Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), "optimized short-stage acquisition also earns an actual receipt");
        Equal(searches + 1, PresetSearches, "AutoUpdate true short-stage entry performs exactly one new search");
        policy = Get(Get(MainInstance, "_globalPresetReceipt"), "Policy");
        Equal(12, Get(policy, "Horizon"), "new optimizer acquisition uses the scheduled duration");
        var baseline = ((Array)Get(Get(policy, "Plan"), "Candidates")).GetValue(0);
        Equal((double?)(19d / 12), ((double?[])Get(baseline, "Metrics"))[0], "actual optimizer search forecasts remaining target production over the short stage");
        Equal((double?)(19d / 12), ((double?[])Get(Get(policy, "Candidate"), "Metrics"))[0], "observed optimized acquisition retains the short-stage forecast");
    }

    private static void CheckAdventureStatObjectives()
    {
        Invoke(typeof(Program), "Setup");
        Character = Get(typeof(Program), "Character"); Controller = Get(typeof(Program), "Controller"); MainInstance = Get(typeof(Program), "MainInstance");
        PrepareSupport(); SetActiveBeards(3, 6); Set(Get(Character, "NGU"), "autoAdvance", false);
        Set(MainInstance, "_globalPresetPolicies", Activator.CreateInstance(Field(MainInstance, "_globalPresetPolicies").FieldType));
        var adventure = Get(Character, "adventure"); var button = Get(Get(Character, "buttons"), "adventure");
        var inventory = Get(Character, "inventory"); var head = Get(inventory, "head"); var items = (IList)Get(inventory, "inventory");
        Set(adventure, "defense", 2000f); Set(head, "curAttack", 50f); Set(head, "curDefense", 60f);
        for (var i = 0; i < 2; i++)
        {
            Set(items[i], "id", 501 + i); Set(items[i], "type", Get(head, "type")); Set(items[i], "bossRequired", 1); Set(items[i], "removable", true);
            Set(items[i], "curAttack", i == 0 ? 500f : 0f); Set(items[i], "curDefense", i == 1 ? 900f : 0f);
        }
        Set(button, "m_Interactable", true);
        var zone = Get(adventure, "zone");
        var recalls = Get(typeof(Program), "Recalls"); var swaps = Get(typeof(Program), "Swaps");
        object preset = null;
        try
        {
            foreach (var blocked in new[] { false, true })
            foreach (var beast in new[] { false, true })
            foreach (var goal in new[] { 10, 11 })
            {
                BlockPresetLevelGain = blocked; Set(adventure, "beastModeOn", beast);
                Equal(!blocked, Invoke(Character, "canLevel"), "native level-gain boundary is controlled independently of adventure unlock");
                var expectedCurrent = new[] { Convert.ToDouble(Invoke(Character, "totalAdvAttack")), Convert.ToDouble(Invoke(Character, "totalAdvDefense")) };
                Set(head, "curAttack", goal == 10 ? 500f : 0f); Set(head, "curDefense", goal == 11 ? 900f : 0f);
                var expectedSelected = new[] { Convert.ToDouble(Invoke(Character, "totalAdvAttack")), Convert.ToDouble(Invoke(Character, "totalAdvDefense")) };
                Set(head, "curAttack", 50f); Set(head, "curDefense", 60f);
                var weights = new int[12]; weights[goal] = 1;
                var args = JsonParse("{\"preset\":{\"Id\":\"instant-stats\",\"Name\":\"Instant stats\",\"Weights\":[" + string.Join(",", weights)
                    + "],\"SupportMode\":\"current\",\"SwapSeconds\":3600}}");
                var preview = CallMain("PreviewLoadoutPreset", args); var cached = Get(MainInstance, "_loadoutPresetPreview");
                preset = Get(cached, "Preset"); var snapshot = Get(Get(cached, "Capture"), "Snapshot");
                Equal(null, CallMain("GlobalPresetReadiness", preset, null, null), "instant stat readiness ignores canLevel and Beast Mode");
                Equal(14, Property(Json(preview, "objectiveKeys"), "Count"), "preview exposes ten production and four instant objectives");
                var current = Json(Json(preview, "candidates"), 0);
                var selected = ((IEnumerable)GetChildren(Json(preview, "candidates"))).Cast<object>().Single(x => Property(Json(x, "key"), "Value").Equals("balanced"));
                Equal("true", Json(selected, "feasible").ToString(), "stat-only preview is feasible without farming or training");
                Equal("null", Json(preview, "sequence").ToString(), "instant stat objectives never advertise time-shared production sequences");
                Equal(501 + goal - 10, Property(Json(Json(Json(selected, "items"), 0), "id"), "AsInt"), "each stat objective chooses its own native gear improvement despite full-horizon swap overhead");
                for (var i = 0; i < 2; i++)
                {
                    Equal(true, ((bool[])Get(snapshot, "Available"))[10 + i], "activity capture leaves instant stats available even when native leveling is disabled");
                    var metric = Json(Json(selected, "metrics"), 10 + i);
                    Equal(i == 0 ? "adventurePower" : "adventureToughness", Property(Json(metric, "key"), "Value"), "instant stats retain their distinct objective keys");
                    Equal("\"projected\"", Json(metric, "quality").ToString(), "direct stat metric is projected, not training production");
                    Equal(expectedCurrent[i], Property(Json(Json(Json(current, "metrics"), 10 + i), "value"), "AsDouble"), "current stat metric matches the independent native getter");
                    Equal(expectedSelected[i], Property(Json(metric, "value"), "AsDouble"), "selected gear independently projects native power and toughness with no swap-time discount");
                }
                if (blocked)
                    for (var g = 6; g < 10; g++) Equal(false, ((bool[])Get(snapshot, "Available"))[g], "native activity lock still applies to production goal " + g);
                if (beast)
                    for (var g = 3; g < 6; g++) Equal(false, ((bool[])Get(snapshot, "Available"))[g], "Beast Mode still excludes farming goal " + g);
            }
            Equal(recalls, Get(typeof(Program), "Recalls"), "stat previews and native getter oracles never recall resources");
            Equal(swaps, Get(typeof(Program), "Swaps"), "stat previews never equip even temporarily");
            Equal(zone, Get(adventure, "zone"), "stat previews leave the actual adventure route unchanged");
            Set(button, "m_Interactable", false);
            Equal("Adventure is locked.", CallMain("GlobalPresetReadiness", preset, null, null), "instant stats still require the adventure unlock");
            var capture = CallMain("CaptureLoadoutPresetDefinition", preset);
            Equal(false, ((bool[])Get(Get(capture, "Snapshot"), "Available"))[10], "locked adventure makes power unavailable");
            Equal(false, ((bool[])Get(Get(capture, "Snapshot"), "Available"))[11], "locked adventure makes toughness unavailable");
            Set(button, "m_Interactable", true);
            var selectedHead = items[1];
            Invoke(MainInstance, "EquipGlobalPresetDefinition", preset, "dashboard", "explicit-equip", true, null);
            Equal(true, CallMain("GlobalPresetReceiptValid", "dashboard"), "actual stat-only equip acquires ownership with canLevel false and Beast Mode on");
            Equal(3600, Get(Get(Get(MainInstance, "_globalPresetReceipt"), "Policy"), "Horizon"), "standalone explicit equip defaults to a one-hour session forecast");
            Equal(true, Get(adventure, "beastModeOn"), "stat equip never disables Beast Mode");
            Equal(zone, Get(adventure, "zone"), "stat-only equip never applies a farming route");
            Equal(false, Invoke(Character, "canLevel"), "stat equip does not change the native level-gain lock");
            Equal((int)swaps + 1, Get(typeof(Program), "Swaps"), "stat equip performs the selected native inventory data swap");
            Check(ReferenceEquals(items[1], head) && ReferenceEquals(Get(inventory, "head"), selectedHead), "stat equip moves the exact selected toughness item into the head slot");
            Check(((long[])Get(Get(Character, "advancedTraining"), "energy")).All(x => x == 0), "instant adventure objectives do not allocate AT");
        }
        finally { BlockPresetLevelGain = false; }
    }

    private static void CheckCapacityObjectives()
    {
        Invoke(typeof(Program), "Setup");
        Character = Get(typeof(Program), "Character"); Controller = Get(typeof(Program), "Controller"); MainInstance = Get(typeof(Program), "MainInstance");
        PrepareSupport(); SetActiveBeards(3, 6);
        var ngu = Get(Character, "NGU"); Set(ngu, "autoAdvance", true);
        Set(((IList)Get(ngu, "skills"))[0], "target", 1L);
        var magic = Get(Character, "magic");
        Set(Character, "curEnergy", 1L); Set(Character, "idleEnergy", 1L);
        Set(magic, "curMagic", 2L); Set(magic, "idleMagic", 2L);
        Set(Get(Get(Character, "buttons"), "adventure"), "m_Interactable", false);
        Set(Get(Get(Character, "buttons"), "ngu"), "m_Interactable", false);
        var inventory = Get(Character, "inventory"); var head = Get(inventory, "head"); var items = (IList)Get(inventory, "inventory");
        for (var i = 0; i < 3; i++)
        {
            Set(items[i], "id", 560 + i); Set(items[i], "type", Get(head, "type")); Set(items[i], "bossRequired", 1); Set(items[i], "removable", true);
            Set(items[i], "spec1Type", Enum.Parse(Field(items[i], "spec1Type").FieldType, i == 0 ? "EnergyCap3" : i == 1 ? "MagicCap3" : "AllCap"));
            Set(items[i], "spec1Cur", i == 2 ? 2000000f : 4000000f);
            Set(items[i], "spec2Cur", 0f); Set(items[i], "spec3Cur", 0f);
            Set(items[i], "curAttack", i == 2 ? 1e20f : 1f); Set(items[i], "curDefense", i == 2 ? 1e20f : 1f);
        }
        var recalls = Get(typeof(Program), "Recalls"); var swaps = Get(typeof(Program), "Swaps");
        try
        {
            BlockPresetLevelGain = true;
            foreach (var goal in new[] { 12, 13 })
            {
                var weights = new int[14]; weights[goal] = 1;
                var args = JsonParse("{\"preset\":{\"Id\":\"instant-cap\",\"Name\":\"Cap\",\"Weights\":[" + string.Join(",", weights)
                    + "],\"SupportMode\":\"current\",\"SwapSeconds\":3600}}");
                var preview = CallMain("PreviewLoadoutPreset", args);
                var cached = Get(MainInstance, "_loadoutPresetPreview"); var preset = Get(cached, "Preset");
                var snapshot = Get(Get(cached, "Capture"), "Snapshot");
                Equal(true, ((bool[])Get(snapshot, "Available"))[12], "energy cap available with NGU/adventure/level gain locked");
                Equal(true, ((bool[])Get(snapshot, "Available"))[13], "native unlocked magic cap available without NGU");
                Equal(null, CallMain("GlobalPresetReadiness", preset, null, null), "cap-only readiness ignores unrelated native level and adventure locks");
                var selected = Json(Json(preview, "candidates"), 1);
                Equal(560 + goal - 12, Property(Json(Json(Json(selected, "items"), 0), "id"), "AsInt"), "compiled cap preview selects its own high-tier cap rather than adventure/opposite cap");
                var metric = Json(Json(selected, "metrics"), goal);
                Equal(goal == 12 ? "energy" : "magic", Property(Json(metric, "unit"), "Value"), "cap adapter exposes instant resource unit");
                Equal("null", Json(preview, "sequence").ToString(), "cap preview excludes time-shared sequences");
                Set(inventory, "head", items[goal - 12]);
                Invoke(typeof(Program), "RefreshBonuses");
                var expected = Convert.ToDouble(Invoke(Character, goal == 12 ? "totalCapEnergy" : "totalCapMagic"));
                Set(inventory, "head", head);
                Invoke(typeof(Program), "RefreshBonuses");
                Equal(expected, Property(Json(metric, "value"), "AsDouble"), "compiled metric equals independent native getter despite near-empty pools/full swap horizon");
                var request = CallMain("LoadoutPresetRequest", preset);
                var replay = Planner("Replay", snapshot, request, Get(cached, "Plan"));
                Equal(expected, ((double?[])Get(((Array)Get(replay, "Candidates")).GetValue(1), "Metrics"))[goal], "compiled cap replay permits irrelevant finite-target auto-advance");
                Set(magic, "capMagic", 9999L);
                var locked = CallMain("CaptureLoadoutPresetDefinition", preset);
                Equal(false, ((bool[])Get(Get(locked, "Snapshot"), "Available"))[13], "native magic base cap below unlock remains unavailable");
                var lockedState = new object[] { preset, null, null, false };
                Equal(goal == 13 ? "Magic is locked." : null, CallMain("GlobalPresetReadiness", lockedState), "only magic cap readiness waits for the native magic unlock");
                Set(magic, "capMagic", 10000L);
                var unlocked = CallMain("CaptureLoadoutPresetDefinition", preset);
                Equal(true, ((bool[])Get(Get(unlocked, "Snapshot"), "Available"))[13], "native magic base cap at exact unlock is available");
                var unlockedState = new object[] { preset, null, null, false };
                Equal(null, CallMain("GlobalPresetReadiness", unlockedState), "cap readiness resumes at the native magic unlock");
                Check(!Equals(lockedState[2], unlockedState[2]), "magic unlock changes readiness retry state");
            }
            Equal(true, Get(ngu, "autoAdvance"), "cap preview/capture/replay never changes native auto-advance");
            Equal(recalls, Get(typeof(Program), "Recalls"), "cap fixture previews never recall resources");
            Equal(swaps, Get(typeof(Program), "Swaps"), "cap fixture previews never invoke game equipment swaps");
        }
        finally { BlockPresetLevelGain = false; Set(inventory, "head", head); }
    }

    private static void CheckCapacityPolicyLifecycle()
    {
        var before = Assertions;
        foreach (var goal in new[] { 12, 13 })
        foreach (var previewApply in new[] { false, true })
        {
            Invoke(typeof(Program), "Setup");
            Character = Get(typeof(Program), "Character"); Controller = Get(typeof(Program), "Controller"); MainInstance = Get(typeof(Program), "MainInstance");
            PrepareSupport(); SetActiveBeards(3, 6);
            Set(MainInstance, "_invManager", Activator.CreateInstance(Plugin.GetType("NGUInjector.Managers.InventoryManager", true), true));
            Set(MainInstance, "_globalPresetPolicies", Activator.CreateInstance(Field(MainInstance, "_globalPresetPolicies").FieldType));
            var settings = Get(MainType, "Settings");
            Set(settings, "_manualAdventureEnabled", false); Set(settings, "_customAdventureEnabled", false);
            Set(Get(Get(Character, "buttons"), "adventure"), "m_Interactable", false);
            Set(Get(Get(Character, "buttons"), "ngu"), "m_Interactable", false);
            var ngu = Get(Character, "NGU"); Set(ngu, "autoAdvance", true);
            Set(((IList)Get(ngu, "skills"))[0], "target", 1L);
            var adventure = Get(Character, "adventure"); Set(adventure, "zone", 5); Set(adventure, "beastModeOn", true);
            Set(Character, "curEnergy", 1L); Set(Character, "idleEnergy", 1L);
            var magic = Get(Character, "magic"); Set(magic, "curMagic", 2L); Set(magic, "idleMagic", 2L);
            var inventory = Get(Character, "inventory"); var head = Get(inventory, "head"); var items = (IList)Get(inventory, "inventory");
            var selected = items[0]; var challenger = items[3];
            var capType = goal == 12 ? "EnergyCap3" : "MagicCap3";
            var nativeGetter = goal == 12 ? "totalCapEnergy" : "totalCapMagic";
            for (var i = 0; i < 4; i++)
            {
                Set(items[i], "id", 570 + i); Set(items[i], "type", Get(head, "type")); Set(items[i], "bossRequired", 1); Set(items[i], "removable", true);
                Set(items[i], "spec1Type", Enum.Parse(Field(items[i], "spec1Type").FieldType,
                    i == 1 ? (goal == 12 ? "MagicCap3" : "EnergyCap3") : i == 2 ? "AllCap" : capType));
                Set(items[i], "spec1Cur", i == 2 ? 2000000f : i == 3 ? 3000000f : 4000000f);
                Set(items[i], "spec1Cap", 10000000f); Set(items[i], "spec2Cur", 0f); Set(items[i], "spec3Cur", 0f);
                Set(items[i], "curAttack", i == 2 ? 1e20f : 1f); Set(items[i], "curDefense", i == 2 ? 1e20f : 1f);
            }
            var store = Get(MainType, "CustomPlanStore");
            var library = Activator.CreateInstance(Plugin.GetType("NGUInjector.CustomPlans.PresetLibraryStore", true), All, null,
                new[] { Get(typeof(Program), "Temp"), Get(store, "_plansDir") }, null);
            Set(store, "<PresetLibrary>k__BackingField", library);
            var root = Invoke(library, "ReadExternalLibrary"); var weights = new int[14]; weights[goal] = 1;
            var id = "cap-lifecycle-" + goal + (previewApply ? "-apply" : "-equip");
            var label = (goal == 12 ? "energy cap" : "magic cap") + (previewApply ? " Preview + Apply" : " Equip");
            Invoke(Json(root, "Presets"), "Add", "", JsonParse("{\"Id\":\"" + id + "\",\"Name\":\"Capacity lifecycle\",\"Weights\":[" + string.Join(",", weights)
                + "],\"AutoUpdate\":true,\"SupportMode\":\"current\",\"SwapSeconds\":3600}"));
            var revision = Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
            var args = JsonParse("{\"presetId\":\"" + id + "\",\"libraryRevision\":\"" + revision + "\"}");
            var searches = PresetSearches; var swaps = (int)Get(typeof(Program), "Swaps");
            var routeEntries = AdventureZoneEntries;
            try
            {
                BlockPresetLevelGain = true;
                if (previewApply)
                {
                    var recalls = Get(typeof(Program), "Recalls");
                    var preview = CallMain("PreviewGlobalPreset", args);
                    Equal(recalls, Get(typeof(Program), "Recalls"), label + " preview never recalls resources");
                    Equal(swaps, Get(typeof(Program), "Swaps"), label + " preview never equips");
                    var apply = JsonParse(args.ToString().TrimEnd('}') + ",\"previewId\":" + Json(preview, "previewId") + ",\"candidateKey\":\"balanced\"}");
                    Invoke(MainInstance, "ApplyGlobalPreset", apply, null);
                }
                else Invoke(MainInstance, "EquipGlobalPreset", args, null);
                Equal(searches + 1, PresetSearches, label + " acquires with exactly one real search");
                Equal(swaps + 1, Get(typeof(Program), "Swaps"), label + " performs the isolated native inventory swap");
                Equal(-1, Get(adventure, "zone"), label + " observes safe retirement before changing active-route gear");
                Equal(routeEntries + 1, AdventureZoneEntries, label + " retires the previous route exactly once");
                Check(ReferenceEquals(selected, Get(inventory, "head")), label + " equips cap gear over opposite-cap and enormous adventure stats");
                Equal(true, CallMain("GlobalPresetReceiptValid", "dashboard"), label + " acquires verified ownership despite unrelated locks");
                var receipt = Get(MainInstance, "_globalPresetReceipt"); var policy = Get(receipt, "Policy");
                Equal(true, Get(Get(policy, "Preset"), "AutoUpdate"), label + " owns an auto-updating definition");
                Equal("current", Get(Get(policy, "Preset"), "SupportMode"), label + " retains current support mode");
                var initialCap = Convert.ToDouble(Invoke(Character, nativeGetter));
                Equal(initialCap, ((double?[])Get(Get(policy, "Candidate"), "Metrics"))[goal], label + " acquired cap matches independent native getter");
                Equal(true, CallMain("RefreshGlobalPresetAllocations", "dashboard"), label + " refreshes acquired allocations");
                CallMain("RunManualGlobalPresetRefresh");
                Equal(searches + 1, PresetSearches, label + " ordinary allocation and manual refresh never search");
                Equal(swaps + 1, Get(typeof(Program), "Swaps"), label + " ordinary refresh never swaps");
                Check(ReferenceEquals(receipt, Get(MainInstance, "_globalPresetReceipt")), label + " ordinary refresh retains acquired ownership");

                var material = ((string[])Get(policy, "Material")).ToArray();
                var historical = Get(MainInstance, "_loadoutPresetSnapshot").ToString();
                var calls = AutoBoostCalls; var boostRecalls = Get(typeof(Program), "Recalls");
                // Boost the owned item and an inventory challenger so restoration must change the cap decision.
                AutoBoostAction = () => {
                    Set(selected, "spec1Cur", 5000000f); Set(challenger, "spec1Cur", 8000000f);
                    Invoke(typeof(Program), "RefreshBonuses");
                };
                try { CallMain("RunCustomAutoBoost"); }
                finally { AutoBoostAction = null; }
                Equal(calls + 1, AutoBoostCalls, label + " runs the existing isolated inventory boost boundary");
                Equal(true, Get(MainInstance, "_customEquipmentApplicationInvalidated"), label + " owned cap material change requests re-equip");
                Equal(false, CallMain("GlobalPresetReceiptValid", "dashboard"), label + " invalidated gear cannot claim ownership");
                Check(((string[])Get(policy, "Material")).SequenceEqual(((Array)Get(policy, "Items")).Cast<object>()
                    .Select(item => (string)CallMain("LoadoutPresetPrimitiveKey", item))), label + " records the causal version of the same selected material without accepting the application receipt");
                Check(!((string[])Get(policy, "Material")).SequenceEqual(material) && ReferenceEquals(selected, Get(inventory, "head")),
                    label + " material revision neither replaces the selected instance nor equips the grown challenger");
                Equal(historical, Get(MainInstance, "_loadoutPresetSnapshot").ToString(), label + " boost preserves applied history");
                Equal(searches + 1, PresetSearches, label + " boosting itself never searches");
                Equal(boostRecalls, Get(typeof(Program), "Recalls"), label + " boosting never recalls");
                Equal(swaps + 1, Get(typeof(Program), "Swaps"), label + " boosting never equips");
                CallMain("RunManualGlobalPresetRefresh");
                Equal(searches + 2, PresetSearches, label + " owned boost causes exactly one reoptimization");
                Equal(swaps + 2, Get(typeof(Program), "Swaps"), label + " reoptimization performs the new cap swap");
                Check(ReferenceEquals(challenger, Get(inventory, "head")), label + " re-equip selects the newly strongest cap item");
                Check(!ReferenceEquals(receipt, Get(MainInstance, "_globalPresetReceipt")), label + " re-equip installs a fresh receipt");
                Equal(false, Get(MainInstance, "_customEquipmentApplicationInvalidated"), label + " successful re-equip clears invalidation");
                Equal(true, CallMain("GlobalPresetReceiptValid", "dashboard"), label + " reoptimized gear has verified ownership");
                var restoredCap = Convert.ToDouble(Invoke(Character, nativeGetter));
                Check(restoredCap > initialCap, label + " reoptimized native cap improves");
                var restoredPolicy = Get(Get(MainInstance, "_globalPresetReceipt"), "Policy");
                Equal(restoredCap, ((double?[])Get(Get(restoredPolicy, "Candidate"), "Metrics"))[goal], label + " reoptimized cap matches independent native getter");
                CallMain("RunManualGlobalPresetRefresh");
                Equal(searches + 2, PresetSearches, label + " stable boosted policy does not repeatedly optimize");
                Equal(swaps + 2, Get(typeof(Program), "Swaps"), label + " stable boosted policy does not repeatedly equip");
                Equal(-1, Get(adventure, "zone"), label + " never activates an unrequested farming route");
                Equal(routeEntries + 1, AdventureZoneEntries, label + " refresh and reoptimization never restore the foreign route");
                Equal(true, Get(adventure, "beastModeOn"), label + " preserves Beast Mode");
                Equal(true, Get(ngu, "autoAdvance"), label + " preserves irrelevant finite-target auto-advance");
                Check(((long[])Get(Get(Character, "advancedTraining"), "energy")).All(x => x == 0), label + " never turns capacity into AT training");
            }
            finally { BlockPresetLevelGain = false; AutoBoostAction = null; }
        }
        Console.WriteLine("Capacity policy lifecycle regressions: " + (Assertions - before) + " assertions");
    }

    private static void CheckGlobalStatPolicyLifecycle()
    {
        Invoke(typeof(Program), "Setup");
        Character = Get(typeof(Program), "Character"); Controller = Get(typeof(Program), "Controller"); MainInstance = Get(typeof(Program), "MainInstance");
        PrepareSupport(); SetActiveBeards(3, 6); Set(Get(Character, "NGU"), "autoAdvance", false);
        Set(Get(MainType, "Settings"), "_energyNguWeights", new int[9]);
        Set(Get(MainType, "Settings"), "_magicNguWeights", new int[7]);
        Set(Get(Get(Character, "buttons"), "ngu"), "m_Interactable", true);
        Set(Get(Get(Character, "buttons"), "adventure"), "m_Interactable", true);
        Set(MainInstance, "_globalPresetPolicies", Activator.CreateInstance(Field(MainInstance, "_globalPresetPolicies").FieldType));
        var store = Get(MainType, "CustomPlanStore");
        var library = Activator.CreateInstance(Plugin.GetType("NGUInjector.CustomPlans.PresetLibraryStore", true), All, null,
            new[] { Get(typeof(Program), "Temp"), Get(store, "_plansDir") }, null);
        Set(store, "<PresetLibrary>k__BackingField", library);
        var root = Invoke(library, "ReadExternalLibrary");
        Invoke(Json(root, "Presets"), "Add", "", JsonParse("{\"Id\":\"stat-policy-lifecycle\",\"Name\":\"Stat policy\",\"Weights\":[0,0,0,0,0,0,0,0,0,0,1,1],\"SupportMode\":\"current\",\"SwapSeconds\":3600,\"EnergyNguWeights\":[0,0,0,0,0,0,0,0,0],\"MagicNguWeights\":[0,0,0,0,0,0,0]}"));
        var revision = Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
        Func<object> args = () => JsonParse("{\"presetId\":\"stat-policy-lifecycle\",\"libraryRevision\":\"" + revision + "\"}");
        var inventory = Get(Character, "inventory"); var head = Get(inventory, "head"); var item = ((IList)Get(inventory, "inventory"))[0];
        Set(head, "curAttack", 50f); Set(head, "curDefense", 60f);
        Set(item, "id", 550); Set(item, "type", Get(head, "type")); Set(item, "bossRequired", 1); Set(item, "removable", true);
        Set(item, "curAttack", 500f); Set(item, "curDefense", 900f);
        var adventure = Get(Character, "adventure"); Set(adventure, "zone", 5); Set(adventure, "beastModeOn", true);
        var searches = PresetSearches;
        var routeEntries = AdventureZoneEntries;
        try
        {
            BlockPresetLevelGain = true;
            var preview = CallMain("PreviewGlobalPreset", args());
            Equal(searches + 1, PresetSearches, "committed immediate-stat preview performs one real search");
            var apply = JsonParse(args().ToString().TrimEnd('}') + ",\"previewId\":" + Json(preview, "previewId") + ",\"candidateKey\":\"balanced\"}");
            Invoke(MainInstance, "ApplyGlobalPreset", apply, null);
            Equal(searches + 1, PresetSearches, "immediate-stat Preview + Apply never searches twice");
            Check(ReferenceEquals(item, Get(inventory, "head")), "committed stat application equips the exact dominating item");
            Equal(true, CallMain("GlobalPresetReceiptValid", "dashboard"), "stat policy owns a valid receipt with level gain disabled and Beast Mode enabled");
            // Explicit gear takeover retires active combat before withdrawing its
            // prerequisites. Safe -1 is not a selected farming route and the
            // foreign zone5 is deliberately not restored by a stat-only policy.
            Equal(-1, Get(adventure, "zone"), "stat application safely retires the previous route without selecting a farming route");
            Equal(routeEntries + 1, AdventureZoneEntries, "stat takeover performs exactly one native safe-retirement entry, no farming entry");
            Equal(true, Get(adventure, "beastModeOn"), "stat application does not alter Beast Mode");
            var receipt = Get(MainInstance, "_globalPresetReceipt"); var policy = Get(receipt, "Policy");
            Equal("balanced", Get(Get(policy, "Candidate"), "Key"), "stat cache retains the exact selected preview policy");
            Check(((long[])Get(Get(Character, "advancedTraining"), "energy")).All(x => x == 0), "stat application never turns immediate stats into AT training");
            Check(((IList)Get(Get(Character, "NGU"), "skills")).Cast<object>().All(x => (long)Get(x, "energy") == 0), "excluded energy NGUs remain excluded");
            Check(((IList)Get(Get(Character, "NGU"), "magicSkills")).Cast<object>().All(x => (long)Get(x, "magic") == 0), "excluded magic NGUs remain excluded");

            root = Invoke(library, "ReadExternalLibrary");
            var saved = ((IEnumerable)GetChildren(Json(root, "Presets"))).Cast<object>().Single(x => Property(Json(x, "Id"), "Value").Equals("stat-policy-lifecycle"));
            SetJson(saved, "Name", Json(JsonParse("{\"value\":\"Saved stat policy\"}"), "value"));
            SetJson(saved, "AutoUpdate", Json(JsonParse("{\"value\":false}"), "value"));
            SetJson(saved, "Weights", Json(JsonParse("{\"value\":[0,0,0,0,0,0,0,0,0,0,2,1]}"), "value"));
            revision = Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
            var swaps = Get(typeof(Program), "Swaps"); Set(adventure, "zone", 4);
            CallMain("RunManualGlobalPresetRefresh");
            Equal(searches + 1, PresetSearches, "stat policy refresh and saved edits never launch a timer search");
            Equal(swaps, Get(typeof(Program), "Swaps"), "saving stat weights cannot equip a different policy");
            Check(ReferenceEquals(receipt, Get(MainInstance, "_globalPresetReceipt")), "saving stat weights cannot replace the acquired receipt");
            Equal(4, Get(adventure, "zone"), "a stat-only receipt does not acquire route ownership");
            var runtime = CallMain("GetLoadoutPresetRuntime");
            Equal("\"Stat policy\"", Json(runtime, "appliedName").ToString(), "applied stat policy identity stays immutable after edits");
            Equal("true", Json(runtime, "savedChangesRequireReequip").ToString(), "saved stat edits are pending, not active");
            var recalls = Get(typeof(Program), "Recalls");
            Throws(() => Invoke(MainInstance, "EquipGlobalPreset", args(), null), "definition", "false stat cache rejects incompatible weights before mutation");
            Equal(recalls, Get(typeof(Program), "Recalls"), "stale stat cache rejects before recalling resources");
            Equal(swaps, Get(typeof(Program), "Swaps"), "stale stat cache rejects before gear mutation");

            SetJson(saved, "Weights", Json(JsonParse("{\"value\":[0,0,0,0,0,0,0,0,0,0,1,1]}"), "value"));
            revision = Invoke(library, "SaveExternalLibrary", root, revision);
            Invoke(MainInstance, "EquipGlobalPreset", args(), null);
            Equal(searches + 1, PresetSearches, "name and AutoUpdate changes preserve stat cache reuse without search");
            Equal(true, CallMain("GlobalPresetReceiptValid", "dashboard"), "cached false stat policy applies successfully");
            Equal("\"cached\"", Json(CallMain("GetLoadoutPresetRuntime"), "mode").ToString(), "stat telemetry distinguishes cached application");
            CallMain("InvalidateCustomEquipmentApplication");
            recalls = Get(typeof(Program), "Recalls"); var blockedSwaps = Get(typeof(Program), "Swaps");
            var blockedSupport = Property(CallMain("CaptureSupportState"), "StableKey");
            var blockedAllocations = CallMain("GlobalPresetAllocationState");
            var blockedEntries = AdventureZoneEntries; var blockedCaptures = PresetCaptures;
            CallMain("RunManualGlobalPresetRefresh");
            Equal(searches + 1, PresetSearches, "false stat policy restoration never searches");
            Equal(false, CallMain("GlobalPresetReceiptValid", "dashboard"), "invalidated stat policy cannot reacquire through an unowned active route");
            Equal(4, Get(adventure, "zone"), "refused stat reacquisition preserves the foreign route");
            Equal(blockedEntries, AdventureZoneEntries, "refused stat reacquisition never retires or enters a route");
            Equal(blockedCaptures, PresetCaptures, "unowned route rejects restoration before planning capture");
            Equal(recalls, Get(typeof(Program), "Recalls"), "unowned route grants no resource recall permission");
            Equal(blockedSwaps, Get(typeof(Program), "Swaps"), "unowned route grants no gear permission");
            Equal(blockedSupport, Property(CallMain("CaptureSupportState"), "StableKey"), "unowned route grants no support permission");
            Equal(blockedAllocations, CallMain("GlobalPresetAllocationState"), "refused stat reacquisition preserves allocations");
            // Native activity ends independently. Observing safe -1 permits replay;
            // the stat policy never receives implicit authority over zone4.
            Set(adventure, "zone", -1);
            Set(MainInstance, "_globalPresetRetryAt", 0d); // Advance bounded retry eligibility, not native route ownership or stage time.
            CallMain("RunManualGlobalPresetRefresh");
            Equal(true, CallMain("GlobalPresetReceiptValid", "dashboard"), "false stat policy reacquires only after native activity is observed safe");
            Set(Get(Get(Character, "buttons"), "adventure"), "m_Interactable", false);
            recalls = Get(typeof(Program), "Recalls"); var captures = PresetCaptures;
            CallMain("RunManualGlobalPresetRefresh");
            Equal(captures, PresetCaptures, "a locked stat goal waits before capture");
            Equal(recalls, Get(typeof(Program), "Recalls"), "a locked stat goal does not mutate resource allocations");
            Equal(null, Get(MainInstance, "_presetExecutionBlock"), "a transient stat unlock loss does not permanently fail");
            Set(Get(Get(Character, "buttons"), "adventure"), "m_Interactable", true);
            CallMain("RunManualGlobalPresetRefresh");
            Equal(true, CallMain("GlobalPresetReceiptValid", "dashboard"), "stat unlock recovery resumes the acquired policy without manual restart");
            Equal(searches + 1, PresetSearches, "stat unlock recovery uses replay, never re-optimization");
            Equal(-1, Get(adventure, "zone"), "cached stat restoration and unlock recovery install no implicit route");
            Equal(blockedEntries, AdventureZoneEntries, "safe stat restoration and unlock recovery never enter a farming route");
        }
        finally { BlockPresetLevelGain = false; }
    }

    private static object ReadPresetPlanProgress(object plan)
    {
        Set(MainInstance, "_customRuntimePlanCache", plan);
        Set(MainInstance, "_customRuntimePlanCacheName", Property(Get(MainType, "Settings"), "CustomPlanFile"));
        Set(MainInstance, "_customRuntimePlanCacheLoadedAt", DateTime.Now);
        return CallMain("GetCustomPlanProgress");
    }

    private static void CheckPresetReadinessAndProgress()
    {
        foreach (var kind in new[] { "boss", "tm-bank", "level", "at-bank", "capture", "budget", "horizon", "definition" })
        {
            var budgetWait = kind == "budget" || kind == "horizon";
            Invoke(typeof(Program), "Setup");
            Character = Get(typeof(Program), "Character"); Controller = Get(typeof(Program), "Controller"); MainInstance = Get(typeof(Program), "MainInstance");
            PrepareSupport(); SetActiveBeards(3, 6);
            Set(MainInstance, "_globalPresetPolicies", Activator.CreateInstance(Field(MainInstance, "_globalPresetPolicies").FieldType));
            var settings = Get(MainType, "Settings"); Set(settings, "_useCustomRebirthPlan", true); Set(settings, "_autoRebirth", false);
            var store = Get(MainType, "CustomPlanStore");
            var library = Activator.CreateInstance(Plugin.GetType("NGUInjector.CustomPlans.PresetLibraryStore", true), All, null,
                new[] { Get(typeof(Program), "Temp"), Get(store, "_plansDir") }, null);
            Set(store, "<PresetLibrary>k__BackingField", library);
            var id = "readiness-" + kind; var name = "Waiting " + kind;
            var root = Invoke(library, "ReadExternalLibrary");
            var weights = kind == "at-bank" ? "[0,0,0,0,0,0,0,0,0,1]" : "[0,0,0,0,0,0,1,0,0,0]";
            Invoke(Json(root, "Presets"), "Add", "", JsonParse("{\"Id\":\"" + id + "\",\"Name\":\"" + name
                + "\",\"Weights\":" + weights + ",\"SupportMode\":\"current\",\"HorizonSeconds\":10,\"SwapSeconds\":0}"));
            Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
            Set(Get(Get(Character, "buttons"), "brokenTimeMachine"), "m_Interactable", true);
            var machine = Get(Character, "machine"); Set(machine, "transferredBankLevels", kind != "tm-bank");
            var tm = Get(Character, "timeMachineController"); Set(tm, "character", Character);
            Set(tm, "baseNormalSpeedDivider", kind == "capture" ? 0f : 1f); Set(tm, "baseNormalGoldMultiDivider", 1f);
            Set(tm, "baseSpeedCost", 5000000f); Set(tm, "baseGoldMultiCost", 5000000f);
            Set(Get(Character, "advancedTraining"), "transferredBankedLevels", kind != "at-bank");
            Set(Character, "bossID", kind == "boss" ? 29 : 100);
            Set(Character, "realGold", budgetWait ? 5000000d : 1e18);
            var plan = Invoke(store, "Load", "fixture");
            Set(plan, "ActivePresetId", id); Set(plan, "StageSchedulerEnabled", true); Set(plan, "StageLoopStartIndex", -1);
            Set(plan, "AutoActivateFruits", false); Set(plan, "AutoRebirthEnabled", false); Set(plan, "TimeTriggerEnabled", false);
            if (kind == "horizon") Set(plan, "StageDurationSeconds", 530);
            var stageType = Plugin.GetType("NGUInjector.CustomPlans.CustomPlanStage", true);
            var stages = Array.CreateInstance(stageType, 2);
            for (var i = 0; i < 2; i++)
            {
                var stage = Activator.CreateInstance(stageType);
                Set(stage, "Name", "Readiness " + i); Set(stage, "PresetId", i == 0 ? "@active" : id);
                Set(stage, "DurationSeconds", 10); Set(stage, "Percent", 0d);
                if (kind == "horizon" && i == 0) { Set(stage, "DurationSeconds", 0); Set(stage, "Percent", 1d); }
                if (kind == "definition" && i == 0)
                {
                    Set(stage, "CompletionMode", 1);
                    Set(stage, "TargetAdvancedTrainingPower", 10L); Set(stage, "TargetAdvancedTrainingToughness", 20L);
                }
                Set(stage, "GearGoal", 2); Set(stage, "EnergyFocus", 5); Set(stage, "MagicFocus", 6);
                Set(stage, "EnergyPriorities", new[] { "NGU", "AUG" }); Set(stage, "MagicPriorities", new[] { "NGU", "RIT" });
                stages.SetValue(stage, i);
            }
            Set(plan, "Stages", stages);
            var searches = PresetSearches; var captures = PresetCaptures;
            var recalls = Get(typeof(Program), "Recalls"); var swaps = Get(typeof(Program), "Swaps");
            try
            {
                BlockPresetLevelGain = kind == "level";
                CallMain("GetProtectedStage", plan);
                Equal(null, Get(MainInstance, "_protectedSelectedPolicyIssue"), kind + ": fresh readiness case has no reload quarantine");
                TracePresetAdmission(plan, "readiness/" + kind + "/before");
                CallMain("RunCustomPlanAutomation", plan);
                TracePresetAdmission(plan, "readiness/" + kind + "/after");
                Equal(searches + (budgetWait ? 1 : 0), PresetSearches, kind + ": known readiness polls never invoke the optimizer");
                Equal(captures + (budgetWait || kind == "capture" ? 1 : 0), PresetCaptures, kind + ": known locks skip capture and other samples remain bounded");
                Equal(recalls, Get(typeof(Program), "Recalls"), kind + ": readiness failure cannot recall resources");
                Equal(swaps, Get(typeof(Program), "Swaps"), kind + ": readiness failure cannot mutate equipment");
                if (kind == "definition") Check(Get(MainInstance, "_presetExecutionBlock") != null, "invalid definition is blocked before mutation");
                else Equal(null, Get(MainInstance, "_presetExecutionBlock"), kind + ": transient readiness does not latch the stage");
                var core = Get(MainInstance, "_protectedScheduler");
                Equal(false, Get(core, "_armed"), kind + ": an unapplied waiting stage earns no protected credit");
                var coreState = Invoke(core, "ExportState").ToString();
                var progress = ReadPresetPlanProgress(plan); var row = Json(Json(progress, "stages"), 0);
                Equal("\"" + id + "\"", Json(progress, "activePresetId").ToString(), "progress exposes the configured active preset");
                Equal("\"@active\"", Json(row, "presetId").ToString(), "progress preserves the configured stage binding token");
                Equal("\"" + id + "\"", Json(row, "resolvedPresetId").ToString(), "progress exposes the resolved preset ID");
                Equal("\"" + name + "\"", Json(row, "presetName").ToString(), "progress exposes the saved preset name");
                Equal("\"weighted-preset\"", Json(row, "allocationMode").ToString(), "progress identifies weighted execution");
                foreach (var field in new[] { "gearGoal", "energyFocus", "magicFocus" })
                    Equal("null", Json(row, field).ToString(), "weighted progress cannot advertise ignored legacy " + field);
                foreach (var field in new[] { "energyPriorities", "magicPriorities", "configuredEnergyPriorities", "configuredMagicPriorities" })
                    Equal("[]", Json(row, field).ToString(), "weighted progress cannot advertise ignored legacy " + field);
                Equal("false", Json(row, "bloodGoalsPrioritized").ToString(), "weighted execution does not claim the legacy blood overlay");
                Equal(kind == "definition" ? "\"failed\"" : "\"waiting\"", Json(row, "presetStatus").ToString(), "progress reports transient waiting separately from failure");
                Equal("null", Json(row, "appliedPreset").ToString(), "waiting is never fictitious application");
                Equal(coreState, Invoke(core, "ExportState").ToString(), "preset telemetry never advances or arms the scheduler");
                var afterAttemptCaptures = PresetCaptures;
                CallMain("RunCustomPlanAutomation", plan);
                Equal(searches + (budgetWait ? 1 : 0), PresetSearches, "repeated readiness ticks do not repeat full searches");
                Equal(afterAttemptCaptures, PresetCaptures, "known waits and backoff avoid repeated captures");
                if (kind == "definition")
                {
                    root = Invoke(library, "ReadExternalLibrary");
                    var fixedDefinition = ((IEnumerable)GetChildren(Json(root, "Presets"))).Cast<object>().Single(x => Property(Json(x, "Id"), "Value").Equals(id));
                    SetJson(fixedDefinition, "Weights", Json(JsonParse("{\"value\":[0,0,0,0,0,0,1,0,0,1,0,0]}"), "value"));
                    Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
                    CallMain("RunCustomPlanAutomation", plan);
                    Equal(searches + 1, PresetSearches, "corrected rejected-unchanged configuration admits one fresh preflight without a global failure latch");
                    Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), "corrected definition verifies before granting credit");
                    foreach (var field in new[] { "entry", "cycle", "enteredAt", "expectedEnd" })
                        Equal(Json(JsonParse(coreState), field).ToString(), Json(Invoke(core, "ExportState"), field).ToString(), "configuration repair cannot renew original commitment/" + field);
                    CallMain("RunCustomPlanAutomation", plan);
                    Equal(searches + 1, PresetSearches, "verified corrected configuration is retained without another search");
                    continue;
                }
                if (budgetWait)
                {
                    var delay = (double)Get(MainInstance, "_globalPresetRetryDelay");
                    if (kind == "horizon")
                        foreach (var clock in new[] { "rebirthTime", "totalPlaytime" })
                            Set(Get(Character, clock), "totalseconds", (double)Get(Get(Character, clock), "totalseconds") + 2d);
                    Set(MainInstance, "_globalPresetRetryAt", 0d);
                    CallMain("RunCustomPlanAutomation", plan);
                    Equal(searches + 1, PresetSearches, "unchanged infeasibility replays the pending search without re-optimizing");
                    Check((double)Get(MainInstance, "_globalPresetRetryDelay") > delay && (double)Get(MainInstance, "_globalPresetRetryDelay") <= 30,
                        "pre-mutation retries use bounded exponential backoff");
                    Equal(recalls, Get(typeof(Program), "Recalls"), "infeasible policy retries remain pre-mutation");
                    Set(Character, "realGold", 1e18); Set(MainInstance, "_globalPresetRetryAt", 0d);
                }
                if (kind == "capture")
                {
                    Set(tm, "baseNormalSpeedDivider", 1f);
                    Set(MainInstance, "_globalPresetRetryAt", 0d); // Advance the bounded retry boundary, not the stage entry.
                }
                Set(Character, "bossID", 100); Set(machine, "transferredBankLevels", true);
                Set(Get(Character, "advancedTraining"), "transferredBankedLevels", true); BlockPresetLevelGain = false;
                CallMain("RunCustomPlanAutomation", plan);
                Equal(searches + 1, PresetSearches, kind + ": readiness recovery equips with exactly one search and no manual restart; " + CallMain("GetCustomAutomationStatus"));
                Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), kind + ": recovery acquires a valid observed stage receipt");
                if (kind == "horizon") Equal(18, Get(Get(Get(MainInstance, "_globalPresetReceipt"), "Policy"), "Horizon"),
                    "recovery evaluates and freezes the current remaining forecast without a second search");
                Equal(true, Get(core, "_armed"), kind + ": only successful recovery arms stage credit");
                Equal(true, Property(settings, "UseCustomRebirthPlan"), "scheduled readiness recovery must not self-pause");
                progress = ReadPresetPlanProgress(plan); row = Json(Json(progress, "stages"), 0);
                Equal("\"applied\"", Json(row, "presetStatus").ToString(), "successful acquisition is distinguished from waiting");
                Equal("\"" + id + "\"", Json(Json(progress, "appliedPreset"), "id").ToString(), "root progress identifies the actually acquired policy");
                Equal("null", Json(Json(Json(progress, "stages"), 1), "appliedPreset").ToString(), "a future row with the same ID never inherits the current receipt");
                var acquiredReceipt = Get(MainInstance, "_globalPresetReceipt");
                var transfers = ResourceTransfers;
                Set(Controller, "midDrag", true);
                try
                {
                    CallMain("RunCustomPlanAutomation", plan);
                    row = Json(Json(ReadPresetPlanProgress(plan), "stages"), 0);
                    Equal("\"waiting\"", Json(row, "presetStatus").ToString(), "current receipt cannot hide an early equipment wait");
                    Check(((string)Property(Json(row, "presetReason"), "Value")).IndexOf("inventory drag", StringComparison.OrdinalIgnoreCase) >= 0, "receipt wait projects the actual affected reason");
                    Check(ReferenceEquals(acquiredReceipt, Get(MainInstance, "_globalPresetReceipt")), "early wait retains current acquired intent");
                    Equal(transfers, ResourceTransfers, "early wait writes no resources");
                    Equal(false, Get(core, "_armed"), "retained receipt does not credit an unverified waiting interval");
                }
                finally { Set(Controller, "midDrag", false); }
                CallMain("RunCustomPlanAutomation", plan);
                Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), "wait recovery still requires actual native receipt verification");
                root = Invoke(library, "ReadExternalLibrary");
                var saved = ((IEnumerable)GetChildren(Json(root, "Presets"))).Cast<object>().Single(x => Property(Json(x, "Id"), "Value").Equals(id));
                SetJson(saved, "Name", Json(JsonParse("{\"value\":\"Saved name\"}"), "value"));
                SetJson(saved, "SwapSeconds", Json(JsonParse("{\"value\":1}"), "value"));
                Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
                progress = ReadPresetPlanProgress(plan); row = Json(Json(progress, "stages"), 0);
                Equal("\"Saved name\"", Json(row, "presetName").ToString(), "progress resolves the latest saved name");
                Equal("\"" + name + "\"", Json(Json(row, "appliedPreset"), "name").ToString(), "applied progress retains the acquired name independently of edits");
                Equal("true", Json(Json(row, "appliedPreset"), "savedChangesRequireReequip").ToString(), "progress marks saved changes as pending, not already active");
                Set(core, "_lastHeartbeat", (double)MainType.GetProperty("ProtectedHeartbeat", All).GetValue(null) - 2d);
                foreach (var clock in new[] { "rebirthTime", "totalPlaytime" })
                    Set(Get(Character, clock), "totalseconds", (double)Get(Get(Character, clock), "totalseconds") + 2d);
                CallMain("RunCustomPlanAutomation", plan);
                Equal(2d, ((double[])Property(core, "AppliedSeconds"))[0], "recovered preset stage earns its real applied interval");
                if (kind != "horizon") Equal(2d, ((double[])Property(core, "EarnedSeconds"))[0], "recovered fixed stage earns its own protected work");
                Equal(searches + 1, PresetSearches, "applied ticks and library saves never restart optimization");
            }
            finally { BlockPresetLevelGain = false; CallMain("ReleaseGlobalPresetNativeTargets"); }
        }
    }

    private static void CheckWeightedStageTargets()
    {
        Invoke(typeof(Program), "Setup");
        Character = Get(typeof(Program), "Character"); Controller = Get(typeof(Program), "Controller"); MainInstance = Get(typeof(Program), "MainInstance");
        PrepareSupport(); SetActiveBeards(3, 6);
        Set(MainInstance, "_globalPresetPolicies", Activator.CreateInstance(Field(MainInstance, "_globalPresetPolicies").FieldType));
        var settings = Get(MainType, "Settings"); Set(settings, "_useCustomRebirthPlan", true); Set(settings, "_autoRebirth", false);
        var store = Get(MainType, "CustomPlanStore");
        var library = Activator.CreateInstance(Plugin.GetType("NGUInjector.CustomPlans.PresetLibraryStore", true), All, null,
            new[] { Get(typeof(Program), "Temp"), Get(store, "_plansDir") }, null);
        Set(store, "<PresetLibrary>k__BackingField", library);
        var root = Invoke(library, "ReadExternalLibrary");
        var row = JsonParse("{\"Id\":\"at-target-fixture\",\"Name\":\"AT stage targets\",\"Weights\":[0,0,0,0,0,0,0,0,0,1],\"SupportMode\":\"current\",\"HorizonSeconds\":10,\"SwapSeconds\":0,\"AdvancedTrainingWeights\":[1,1,0,0,0]}");
        Invoke(Json(root, "Presets"), "Add", "", row);
        Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
        var preset = Invoke(library, "Get", "at-target-fixture");
        var at = Get(Character, "advancedTraining"); Set(at, "transferredBankedLevels", true);
        var menuTargets = Enumerable.Repeat(-1L, 5).ToArray(); Set(at, "levelTarget", menuTargets);
        var stage = Activator.CreateInstance(Plugin.GetType("NGUInjector.CustomPlans.CustomPlanStage", true));
        Set(stage, "Name", "Mandatory weighted AT"); Set(stage, "PresetId", "at-target-fixture"); Set(stage, "CompletionMode", 1);
        Set(stage, "TargetAdvancedTrainingPower", 10L); Set(stage, "TargetAdvancedTrainingToughness", 20L);
        var capture = CallMain("CaptureLoadoutPresetDefinition", preset, null, stage);
        var snapshot = Get(capture, "Snapshot"); var lanes = (Array)Get(snapshot, "AdvancedTraining");
        Equal(true, ((bool[])Get(snapshot, "Available"))[9], "mandatory stage uses a successful native AT capture");
        Equal(20d, Get(lanes.GetValue(0), "Target"), "stage toughness overrides a disabled native menu target in the snapshot");
        Equal(10d, Get(lanes.GetValue(1), "Target"), "stage power overrides a disabled native menu target in the snapshot");
        Equal(true, Get(lanes.GetValue(0), "Unlocked"), "mandatory toughness is eligible despite native -1");
        Equal(true, Get(lanes.GetValue(1), "Unlocked"), "mandatory power is eligible despite native -1");
        Check(menuTargets.All(x => x == -1), "stage target capture does not mutate native menu targets");
        Set(at, "autoAdvance", true);
        var beforeAutoAdvance = Get(typeof(Program), "Recalls");
        Throws(() => CallMain("CaptureLoadoutPresetDefinition", preset, null, stage),
            "auto-advance", "mandatory weighted AT rejects unmodeled native automatic transfers before mutation");
        Equal(true, Get(at, "autoAdvance"), "unsupported auto-advance preflight never changes the native preference");
        Equal(beforeAutoAdvance, Get(typeof(Program), "Recalls"), "unsupported automatic transfers reject before recall");
        Set(at, "autoAdvance", false);
        var nativeAt = Get(Character, "advancedTrainingController");
        var energy = (long[])Get(at, "energy");
        energy[0] = 1; Set(Character, "idleEnergy", (long)Get(Character, "idleEnergy") - 1);
        Invoke(Get(nativeAt, "defense"), "updateAdvancedTraining");
        Equal(0L, ((long[])Get(at, "level"))[0], "native menu -1 blocks paid AT progression before a stage owns its target");
        Equal(0L, energy[0], "native hitTarget returns blocked energy instead of silently training");
        menuTargets[0] = menuTargets[1] = 0;
        var plan = Invoke(store, "Load", "fixture"); var stages = Array.CreateInstance(stage.GetType(), 1); stages.SetValue(stage, 0);
        Set(plan, "Stages", stages); Set(plan, "StageSchedulerEnabled", true); Set(plan, "StageLoopStartIndex", -1);
        Set(plan, "AutoActivateFruits", false); Set(plan, "TimeTriggerEnabled", false); Set(plan, "AutoRebirthEnabled", false);
        Set(plan, "StageDurationSeconds", 540);
        CallMain("RunCustomPlanAutomation", plan);
        Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), "weighted AT targets acquire a valid scheduled receipt");
        Check(energy[0] > 0 && energy[1] > 0, "mandatory weighted AT funds both requested targets with original native menu 0/0");
        Invoke(Get(nativeAt, "defense"), "updateAdvancedTraining");
        Invoke(Get(nativeAt, "attack"), "updateAdvancedTraining");
        var nativeLevels = (long[])Get(at, "level"); var nativeProgress = (float[])Get(at, "barProgress");
        Check(nativeLevels[0] > 0 || nativeProgress[0] > 0, "scheduler allocation must progress on the actual native toughness tick without a test target override");
        Check(nativeLevels[1] > 0 || nativeProgress[1] > 0, "scheduler allocation must progress on the actual native power tick without a test target override");
        var receipt = Get(MainInstance, "_globalPresetReceipt"); var policy = Get(receipt, "Policy");
        Equal(0, Property(CallMain("GetProtectedStage", plan), "RemainingSeconds"), "mandatory AT stage has no timed stage remainder");
        Equal(40, Get(policy, "Horizon"), "untimed AT acquisition uses the remaining plan horizon rather than legacy preset duration");
        var dashboardCapture = CallMain("CaptureLoadoutPresetDefinition", preset);
        var dashboardAt = (Array)Get(Get(dashboardCapture, "Snapshot"), "AdvancedTraining");
        Equal(0d, Get(dashboardAt.GetValue(0), "Target"), "Dashboard capture does not inherit a scheduled owner's borrowed toughness target");
        Equal(0d, Get(dashboardAt.GetValue(1), "Target"), "Dashboard capture does not inherit a scheduled owner's borrowed power target");
        Equal(20L, menuTargets[0], "read-only Dashboard capture leaves the native execution target intact");
        var searches = PresetSearches;
        Set(stage, "TargetAdvancedTrainingPower", 30L);
        Equal(true, CallMain("RefreshGlobalPresetAllocations", Get(MainInstance, "_customStageEquipmentKey")), "refresh reuses its acquired immutable stage context");
        Equal(10L, Get(Get(policy, "Stage"), "TargetAdvancedTrainingPower"), "editing the plan cannot mutate acquired stage targets");
        Equal(searches, PresetSearches, "mandatory AT refresh does not re-search");
        var changedCapture = CallMain("CaptureLoadoutPresetDefinition", preset, null, stage);
        Throws(() => CallMain("ReplayGlobalPreset", policy, preset, changedCapture), "incompatible", "a different stage target context cannot reuse the cached policy");
        Set(preset, "AutoUpdate", false);
        var recalls = Get(typeof(Program), "Recalls"); var swaps = Get(typeof(Program), "Swaps");
        Throws(() => Invoke(MainInstance, "EquipGlobalPresetDefinition", preset, Get(MainInstance, "_customStageEquipmentKey"), "stage-entry", false, null, stage),
            "stage AT targets", "false equip rejects a different stage context before mutation");
        Equal(recalls, Get(typeof(Program), "Recalls"), "target context rejection precedes resource recall");
        Equal(swaps, Get(typeof(Program), "Swaps"), "target context rejection precedes gear mutation");
        Equal(searches, PresetSearches, "target context mismatch never searches a replacement policy");
        Equal(20L, menuTargets[0], "scheduled execution owns the finite native toughness target");
        Equal(10L, menuTargets[1], "refresh preserves the acquired finite native power target");
        Equal("[1,1,0,0,0]", Json(Invoke(Get(policy, "Preset"), "ToJson"), "AdvancedTrainingWeights").ToString(), "stage target constraints never rewrite global definition ratios");
        Set(stage, "TargetAdvancedTrainingPower", 10L);
        Invoke(store, "Save", "fixture", plan);
        Equal(false, CallMain("HasCompletedProtectedStageWork", plan), "mandatory AT gate remains closed before native training completes");
        for (var tick = 0; tick < 2000 && (nativeLevels[0] < 20 || nativeLevels[1] < 10); tick++)
        {
            Invoke(Get(nativeAt, "defense"), "updateAdvancedTraining");
            Invoke(Get(nativeAt, "attack"), "updateAdvancedTraining");
        }
        Equal(20L, nativeLevels[0], "real native ticks reach the scheduler's toughness target without external target wrapping");
        Equal(10L, nativeLevels[1], "real native ticks reach the scheduler's power target without external target wrapping");
        CallMain("GetProtectedStage", plan);
        Equal(true, CallMain("HasCompletedProtectedStageWork", plan), "scheduler observes native levels and durably opens the actual AT completion gate");
        ServiceFixtureNativeTargets(plan);
        Check(menuTargets.Take(2).All(x => x == 0) && menuTargets.Skip(2).All(x => x == -1), "stage completion restores the original native menu targets");
        Equal(0L, energy[0], "stage completion recalls only its still-owned toughness allocation");
        Equal(0L, energy[1], "stage completion recalls only its still-owned power allocation");
        Equal(null, Get(MainInstance, "_globalPresetNativeTargets"), "finished stage releases native target ownership");
        CheckManualTitanCompletedAT(plan, stage);

        Array.Clear(nativeLevels, 0, nativeLevels.Length); Array.Clear(nativeProgress, 0, nativeProgress.Length);
        RestartFixtureStage(plan); CallMain("RunCustomPlanAutomation", plan);
        Equal(20L, menuTargets[0], "an explicitly restarted stage acquires its native target scope");
        menuTargets[1] = 77;
        CallMain("ServiceGlobalPresetNativeTargets");
        Equal(77L, menuTargets[1], "a newer user menu target is never overwritten on ownership loss");
        Equal(20L, menuTargets[0], "observation cannot withdraw the current mandatory companion target");
        searches = PresetSearches;
        CallMain("RunCustomPlanAutomation", plan);
        Equal(searches, PresetSearches, "user target edits cannot start a replacement search");
        Equal(77L, menuTargets[1], "subsequent scheduler ticks preserve the user's target edit");
        RestartFixtureStage(plan); CallMain("RunCustomPlanAutomation", plan);
        Equal(10L, menuTargets[1], "a new stage entry can explicitly acquire a new target scope");
        Invoke(settings, "set_UseCustomRebirthPlan", false);
        ServiceFixtureNativeTargets(plan);
        Equal(0L, menuTargets[0], "pausing the plan restores its original toughness menu target");
        Equal(77L, menuTargets[1], "pausing restores the newer target captured by this stage entry");
        searches = PresetSearches;
        Invoke(settings, "set_UseCustomRebirthPlan", true);
        CallMain("RunCustomPlanAutomation", plan);
        Equal(searches, PresetSearches, "resuming a paused required-AT stage reuses its acquired policy without search");
        Equal(20L, menuTargets[0], "resumed execution installs its native toughness target");
        Equal(10L, menuTargets[1], "resumed execution installs its native power target");
        menuTargets[1] = 99;
        CallMain("InvalidateCustomEquipmentApplication");
        Equal(20L, menuTargets[0], "application invalidation cannot release a current acquired AT gate");
        Equal(99L, menuTargets[1], "temporary ownership cleanup preserves a newer user target");

        RestartFixtureStage(plan);
        try
        {
            FailAtRefresh = true;
            CallMain("RunCustomPlanAutomation", plan);
        }
        finally { FailAtRefresh = false; }
        Check(Get(MainInstance, "_globalPresetNativeTargets") != null, "verified accounting with failed rendering retains acquired target ownership");
        Equal(20L, menuTargets[0], "render failure does not undo verified toughness target");
        Equal(10L, menuTargets[1], "render failure does not undo verified power target");
        Check(energy[0] > 0 && energy[1] > 0, "render failure retains exactly committed AT funding");
        searches = PresetSearches;
        CallMain("RunCustomPlanAutomation", plan);
        Equal(searches, PresetSearches, "presentation-only recovery never retries full searches");

        RestartFixtureStage(plan);
        var lateEditPlanningComplete = false;
        try
        {
            // Inject after selection, during the actual post-gear evaluation. Resource diffs
            // intentionally no longer invoke the obsolete whole-lane recall callback.
            PresetPlanCompletedObserver = clock => lateEditPlanningComplete = true;
            PresetEvaluationObserver = (candidate, request) => {
                if (!lateEditPlanningComplete) return;
                PresetEvaluationObserver = null; menuTargets[1] = 456;
            };
            CallMain("RunCustomPlanAutomation", plan);
        }
        finally { PresetEvaluationObserver = null; PresetPlanCompletedObserver = null; }
        Equal(456L, menuTargets[1], "a menu edit arriving after preflight is not overwritten by stage acquisition");
        Equal(20L, menuTargets[0], "late menu edit rejection retains the predecessor's current AT prerequisite");

        RestartFixtureStage(plan); CallMain("RunCustomPlanAutomation", plan);
        Equal(10L, menuTargets[1], "a fresh acknowledged entry can own the newest menu snapshot");
        var tail = Activator.CreateInstance(stage.GetType());
        Set(tail, "Name", "Protected tail"); Set(tail, "PresetId", ""); Set(tail, "GearGoal", -1); Set(tail, "DurationSeconds", 10);
        var transitionStages = Array.CreateInstance(stage.GetType(), 2); transitionStages.SetValue(stage, 0); transitionStages.SetValue(tail, 1);
        Set(plan, "Stages", transitionStages);
        CallMain("RunCustomPlanAutomation", plan);
        for (var tick = 0; tick < 2000 && (nativeLevels[0] < 20 || nativeLevels[1] < 10); tick++)
        {
            Invoke(Get(nativeAt, "defense"), "updateAdvancedTraining");
            Invoke(Get(nativeAt, "attack"), "updateAdvancedTraining");
        }
        var next = CallMain("GetProtectedStage", plan);
        Equal(1, Property(next, "StageIndex"), "real native AT completion selects the actual next scheduled stage");
        ServiceFixtureNativeTargets(plan);
        Equal(0L, menuTargets[0], "stage transition restores the original toughness menu target before successor application");
        Equal(456L, menuTargets[1], "stage transition restores the latest power menu snapshot");
        Equal(false, CallMain("HasCompletedProtectedStageWork", plan), "native AT completion cannot waive the protected fixed tail");
        Equal(null, Get(MainInstance, "_globalPresetNativeTargets"), "the successor stage cannot inherit old target ownership");

        Set(plan, "Stages", stages); Array.Clear(nativeLevels, 0, nativeLevels.Length); Array.Clear(nativeProgress, 0, nativeProgress.Length);
        Set(plan, "TimeTriggerEnabled", true);
        foreach (var budget in new[] { 580, 480 })
        {
            Set(plan, "TimeTriggerSeconds", budget);
            RestartFixtureStage(plan); CallMain("RunCustomPlanAutomation", plan);
            Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), "untimed AT acquisition succeeds across plan forecast budgets");
            Equal(budget == 580 ? 80 : 3600, Get(Get(Get(MainInstance, "_globalPresetReceipt"), "Policy"), "Horizon"),
                "untimed acquisition prefers the enabled time trigger and falls back to one hour only when its plan horizon is exhausted");
        }
        Check(Get(MainInstance, "_globalPresetNativeTargets") != null, "save replacement regression starts with a live owned target scope");
        var replacementAt = Child(Character, "advancedTraining");
        var replacementTargets = new long[] { 333, 444, -1, -1, -1 }; Set(replacementAt, "levelTarget", replacementTargets);
        CallMain("ServiceGlobalPresetNativeTargets");
        CallMain("ServiceExecutionNativeSettings");
        Equal(333L, replacementTargets[0], "old target cleanup never writes to a replacement save model");
        Equal(444L, replacementTargets[1], "old menu snapshots cannot overwrite replacement save targets");
        Equal(null, Get(MainInstance, "_globalPresetNativeTargets"), "save-model replacement discards stale target ownership");
    }

    private static void CheckManualTitanCompletedAT(object plan, object stage)
    {
        var before = Assertions;
        Set(MainInstance, "_customRuntimePlanCache", null);
        var settings = Get(MainType, "Settings");
        var at = Get(Character, "advancedTraining");
        var targets = (long[])Get(at, "levelTarget"); var levels = (long[])Get(at, "level");
        var scheduler = Get(MainInstance, "_protectedScheduler");
        var state = Invoke(scheduler, "ExportState");
        var searches = PresetSearches; var transfers = ResourceTransfers; var swaps = Get(typeof(Program), "Swaps");
        Equal(true, CallMain("ManualTitanATReady"), "real completed mandatory AT remains ready after native 0/0 target return");
        for (var axis = 0; axis < 2; axis++)
        {
            var level = levels[axis]; levels[axis]--;
            Equal(false, CallMain("ManualTitanATReady"), "completed receipt cannot replace actual native levels/" + axis);
            levels[axis] = level;
            targets[axis] = level + 1; targets[1 - axis] = levels[1 - axis];
            Equal(false, CallMain("ManualTitanATReady"), "higher explicit native target outranks weaker completed plan/" + axis);
            targets[axis] = level;
            Equal(true, CallMain("ManualTitanATReady"), "met positive native targets retain precedence/" + axis);
            targets[axis] = 0;
            Equal(false, CallMain("ManualTitanATReady"), "partial native targets do not use plan proof/" + axis);
            targets[0] = targets[1] = 0;
        }
        Set(settings, "_useCustomRebirthPlan", false);
        Equal(false, CallMain("ManualTitanATReady"), "paused plan cannot prove AT readiness");
        Set(settings, "_useCustomRebirthPlan", true);
        var file = Get(settings, "_customPlanFile"); Set(settings, "_customPlanFile", "not-the-completed-plan");
        Equal(false, CallMain("ManualTitanATReady"), "switched or missing current plan cannot reuse completion");
        Set(settings, "_customPlanFile", file);
        Set(MainInstance, "_customRuntimePlanCache", null);
        var currentPlan = CallMain("LoadRuntimeCustomPlan");
        var currentStage = ((Array)Get(currentPlan, "Stages")).GetValue(0);
        Set(currentStage, "TargetAdvancedTrainingPower", 0L);
        Equal(false, CallMain("ManualTitanATReady"), "zero configured power target cannot prove mandatory AT");
        Set(currentStage, "TargetAdvancedTrainingPower", Get(stage, "TargetAdvancedTrainingPower"));
        Set(currentStage, "TargetAdvancedTrainingToughness", 0L);
        Equal(false, CallMain("ManualTitanATReady"), "zero configured toughness target cannot prove mandatory AT");
        Set(currentStage, "TargetAdvancedTrainingToughness", Get(stage, "TargetAdvancedTrainingToughness"));
        Set(currentPlan, "StageSchedulerEnabled", false);
        Equal(false, CallMain("ManualTitanATReady"), "disabled scheduler cannot prove AT readiness");
        Set(currentPlan, "StageSchedulerEnabled", true);
        var stats = Get(Character, "stats"); var rebirth = (long)Get(stats, "rebirthNumber");
        Set(stats, "rebirthNumber", rebirth + 1);
        Equal(false, CallMain("ManualTitanATReady"), "changed rebirth epoch cannot reuse AT completion");
        Set(stats, "rebirthNumber", rebirth);
        Set(MainInstance, "_protectedCheckpointDurable", false);
        Equal(false, CallMain("ManualTitanATReady"), "non-durable completion cannot prove readiness");
        Set(MainInstance, "_protectedCheckpointDurable", true);
        Set(MainInstance, "_protectedScheduler", null);
        Equal(false, CallMain("ManualTitanATReady"), "actual levels without scheduler receipt are insufficient");
        targets[0] = levels[0]; targets[1] = levels[1];
        Equal(true, CallMain("ManualTitanATReady"), "native positive targets do not require a plan receipt");
        targets[0] = targets[1] = 0; Set(MainInstance, "_protectedScheduler", scheduler);
        Equal(state.ToString(), Invoke(scheduler, "ExportState").ToString(), "readiness queries never advance scheduler, entry, history or budgets");
        var elapsed = Get(Get(Character, "rebirthTime"), "totalseconds");
        var heartbeat = MainType.GetProperty("ProtectedHeartbeat", All).GetValue(null);
        Invoke(scheduler, "OverrideStage", plan, "restart", 0, elapsed, heartbeat);
        Equal(false, CallMain("ManualTitanATReady"), "restarted uncompleted AT cannot use actual levels as completion receipt");
        Invoke(scheduler, "OverrideStage", plan, "skip", 0, elapsed, heartbeat);
        Equal(true, CallMain("HasCompletedProtectedStageWork", plan), "explicit skip satisfies general protected-work gate");
        Equal(false, CallMain("ManualTitanATReady"), "explicit waiver is not genuinely completed AT");
        Equal(true, Invoke(scheduler, "RestoreState", plan, state), "restore original fixture scheduler after negative controls");
        Equal(true, CallMain("ManualTitanATReady"), "valid native completion proof remains ready on repeated queries");
        Equal(searches, PresetSearches, "AT readiness never replays acquisition or searches");
        Equal(transfers, ResourceTransfers, "AT readiness never writes native allocations");
        Equal(swaps, Get(typeof(Program), "Swaps"), "AT readiness never swaps gear");
        Check(targets.Take(2).All(x => x == 0), "readiness never rewrites returned menu targets");
        Console.WriteLine("Manual titan completed AT: " + (Assertions - before) + " compiled-native assertions.");
    }

    private static object GetChildren(object node) { return Property(node, "Children"); }
    private static void SetJson(object target, string key, object value)
    {
        target.GetType().GetProperties(All).First(p => p.GetIndexParameters().Length == 1
            && p.GetIndexParameters()[0].ParameterType == typeof(string)).SetValue(target, value, new object[] { key });
    }

    private static Assembly LoadGame(string path)
    {
        using (var game = AssemblyDefinition.ReadAssembly(path))
        {
            CaptureRetainedFarmingGameMethods(game);
            // Read literal thresholds from original native IL, independent of the production table.
            var adventure = game.MainModule.Types.Single(t => t.Name == "AdventureController");
            var manage = adventure.Methods.Single(m => m.Name == "manageFight");
            var early = new[] { "totalAdvAttack", "totalAdvDefense", "totalAdvHPRegen" }.Select(name =>
                manage.Body.Instructions.Where(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == name
                    && i.Next.OpCode == OpCodes.Ldc_R4).Select(i => (float)i.Next.Operand).ToArray()).ToArray();
            for (var boss = 0; boss < 5; boss++)
                NativeTitanRequirements.Add(boss + ":0", new[] { early[0][boss], early[1][boss], boss < 3 ? 0f : early[2][boss - 3] });
            for (var boss = 5; boss < 12; boss++)
            for (var version = 1; version <= 4; version++)
            {
                var method = adventure.Methods.Single(m => m.Name == "autokillTitan" + (boss + 1) + "V" + version + "Achieved");
                NativeTitanRequirements.Add(boss + ":" + version, method.Body.Instructions.Where(i => i.Operand is MethodReference
                    && new[] { "totalAdvAttack", "totalAdvDefense", "totalAdvHPRegen" }.Contains(((MethodReference)i.Operand).Name)
                    && i.Next.OpCode == OpCodes.Ldc_R4).Select(i => (float)i.Next.Operand).ToArray());
            }
            var factors = new HashSet<string> { "totalEnergyPowerBonus", "totalMagicPowerBonus", "totalEnergyCapBonus", "totalMagicCapBonus",
                "totalAugSpeedBonus", "totalWandoosSpeedBonus", "totalEnergyWandoosBonus", "totalMagicWandoosBonus", "wandoosBonus", "augmentBonus", "getBonus", "augDiscount",
                "totalBothNGUSpeedBonus", "nguBonus", "totalEnergyNGUBonus", "totalMagicNGUBonus", "energyNGUBonus", "magicNGUBonus", "totalEnergyNGUSpeed", "totalMagicNGUSpeed",
                "statBonus", "totalStatBonus", "permStatBonus", "permStatBonus2", "difficultyModifier", "getTotalAttack", "getTotalDefense", "totalLevelBonus",
                "goldBonus", "timeMachineBonus", "totalGPSbonus", "lootBonus", "luckBonus", "totalDropChanceBonus", "totalAdventureBonus", "adventureBonus", "adventureBonus2",
                "PPBonus", "totalPPGainBonus", "totalTMSpeedBonus", "TMSpeedBonus", "totalDiscount" };
            foreach (var type in game.MainModule.Types)
            foreach (var method in type.Methods.Where(m => m.HasBody))
            {
                if (type.Name == "Character" && method.Name == "canLevel")
                {
                    var first = method.Body.Instructions[0]; var il = method.Body.GetILProcessor();
                    il.InsertBefore(first, Instruction.Create(OpCodes.Call, game.MainModule.ImportReference(typeof(PresetIntegrationChecks).GetMethod("PresetLevelGainBlocked", All))));
                    il.InsertBefore(first, Instruction.Create(OpCodes.Brfalse, first));
                    il.InsertBefore(first, Instruction.Create(OpCodes.Ldc_I4_0)); il.InsertBefore(first, Instruction.Create(OpCodes.Ret));
                }
                if (type.Name != "AllBeardsController" && type.Name != "AllGoldDiggerController" && factors.Contains(method.Name) && new[] { "System.Single", "System.Double" }.Contains(method.ReturnType.FullName))
                {
                    if (method.ReturnType.FullName == "System.Single" && (new[] { "adventureBonus", "adventureBonus2", "totalAdventureBonus" }.Contains(method.Name)
                        || type.Name == "CardsController" && method.Name == "getBonus"))
                        AdventureInput(game.MainModule, method, 1);
                    else Constant(method, 1);
                }
                if (new[] { "completions", "evilCompletions", "sadisticCompletions" }.Contains(method.Name) && type.Name.Contains("Challenge")) Constant(method, 0);
                // Match the combat fixture's unrelated non-gear OS perk boundary. Native
                // support income, drain, bonus getters and replacement evaluation stay real.
                if (type.Name == "ItopodPerkController" && method.Name == "totalOSLevelBonus") Constant(method, 0);
                if (type.Name == "AllNGUController" && new[] { "energySpeedDivider", "magicSpeedDivider" }.Contains(method.Name)) Constant(method, 1000);
                if (type.Name == "AllNGUController" && method.Name == "refreshMenu") Constant(method, 0);
                if (type.Name == "TimeMachineController" && method.Name == "updateMenu"
                    || type.Name == "Wandoos98Controller" && method.Name == "updateText") Constant(method, 0);
                if (type.Name == "AllAdvancedTraining" && method.Name == "refresh")
                {
                    method.Body = new Mono.Cecil.Cil.MethodBody(method); var il = method.Body.GetILProcessor();
                    il.Emit(OpCodes.Call, game.MainModule.ImportReference(typeof(PresetIntegrationChecks).GetMethod("AtRefresh", All))); il.Emit(OpCodes.Ret);
                }
                if (type.Name == "AdvancedTrainingController" && method.Name == "updateText")
                {
                    // RecallOwnedTarget renders an individual AT bar, not the aggregate AT menu.
                    method.Body = new Mono.Cecil.Cil.MethodBody(method); var il = method.Body.GetILProcessor();
                    il.Emit(OpCodes.Call, game.MainModule.ImportReference(typeof(PresetIntegrationChecks).GetMethod("AtTargetText", All))); il.Emit(OpCodes.Ret);
                }
                if (type.Name == "InventoryController" && new[] { "accessorySpaces", "curSpaces", "weapon2Unlocked", "weapon2Factor" }.Contains(method.Name))
                    Constant(method, method.Name == "accessorySpaces" ? 2 : method.Name == "curSpaces" ? 10 : 0);
                if (type.Name == "Character" && method.Name == "machineBossMulti") Constant(method, 1);
                if (type.Name == "Character" && method.Name == "idleAttackPower") Constant(method, 1.2);
                if (type.Name == "AdventureController" && method.Name == "respawnTime")
                {
                    method.Body = new Mono.Cecil.Cil.MethodBody(method); var il = method.Body.GetILProcessor();
                    il.Emit(OpCodes.Call, game.MainModule.ImportReference(typeof(PresetIntegrationChecks).GetMethod("PresetRespawn", All))); il.Emit(OpCodes.Ret);
                }
                if (type.Name == "AdventureController" && new[] { "dropLoot", "resetBar" }.Contains(method.Name)
                    || type.Name == "PlayerLog" && method.Name == "AddEvent") Constant(method, 0);
                if (type.Name == "AdventureController" && method.Name == "enemyDeath")
                {
                    // Retain the complete native kill/shifter logic, removing only scene rendering writes.
                    foreach (var instruction in method.Body.Instructions.ToArray())
                    {
                        var called = instruction.Operand as MethodReference;
                        if (called == null || !new[] { "UnityEngine.UI.Slider", "UnityEngine.UI.Text", "UnityEngine.Behaviour" }.Contains(called.DeclaringType.FullName)
                            || !new[] { "set_value", "set_text", "set_enabled" }.Contains(called.Name)) continue;
                        instruction.OpCode = OpCodes.Pop; instruction.Operand = null;
                        method.Body.GetILProcessor().InsertAfter(instruction, Instruction.Create(OpCodes.Pop));
                    }
                }
                if (type.Name == "ZoneSelector" && method.Name == "changeZone")
                {
                    KeepNativeItopodEntry(method);
                    var entry = method.Body.Instructions[0];
                    var il = method.Body.GetILProcessor();
                    il.InsertBefore(entry, Instruction.Create(OpCodes.Ldarg_1));
                    il.InsertBefore(entry, Instruction.Create(OpCodes.Call,
                        game.MainModule.ImportReference(typeof(PresetIntegrationChecks).GetMethod("ObserveAdventureZoneEntry", All))));
                    method.Body.GetILProcessor().InsertBefore(method.Body.Instructions[0], Instruction.Create(OpCodes.Call,
                        game.MainModule.ImportReference(typeof(PresetIntegrationChecks).GetMethod("CountAdventureZoneEntry", All))));
                    var completed = method.Body.Instructions.Last();
                    il.InsertBefore(completed, Instruction.Create(OpCodes.Ldarg_1));
                    il.InsertBefore(completed, Instruction.Create(OpCodes.Call,
                        game.MainModule.ImportReference(typeof(PresetIntegrationChecks).GetMethod("ObserveAdventureZoneEntryCompleted", All))));
                }
                if (type.Name == "AllAdvancedTraining" && new[] { "adventurePowerBonus", "adventureToughnessBonus" }.Contains(method.Name)) AdventureInput(game.MainModule, method, 0);
                if (type.Name == "AllBeardsController" && method.Name == "beardSize") Constant(method, 7);
                if (type.Name == "InventoryController" && method.Name == "updateInventory" || type.Name == "Character" && method.Name == "removeAllRes3") Constant(method, 0);
                if (type.Name == "InventoryController" && method.Name == "updateBonuses") Boundary(game.MainModule, method, "RefreshBonuses", false);
                // Reuse the linked fixture's native Inventory swap, without unrelated R3/scene refreshes.
                if (type.Name == "InventoryController" && method.Name == "swapHead" && method.Parameters.Count == 0)
                    Boundary(game.MainModule, method, "PresetSwapGear", false);
                if (type.Name == "Character" && new[] { "removeMostEnergy", "removeMostMagic" }.Contains(method.Name))
                    Boundary(game.MainModule, method, method.Name == "removeMostEnergy" ? "PresetRecallEnergy" : "PresetRecallMagic", true);
                if (type.Name == "TimeMachineController" && new[] { "barFillsPerSecond", "speedGoldMultiBonus", "goldMultiBonus" }.Contains(method.Name)) Constant(method, 1);
                if (type.Name == "AllGoldDiggerController" && method.Name == "maxDiggerSlots") Constant(method, 1);
                if (type.Name == "AllBeardsController" && method.Name == "capBeards") Constant(method, 4);
                if (type.Name == "HoverTooltip" && method.Name == "showOverrideTooltip") Constant(method, 0);
                if (type.Name == "AllBloodMagicController" && method.Name == "ritualsUnlocked") Constant(method, 1);
                // Keep native spell transfers; isolate only their scene/tooltip rendering boundaries.
                if (type.Name == "RebirthPowerSpell" && new[] { "updateMenu", "updateLootToggleState", "updateGoldToggleState", "updateRebirthToggleState" }.Contains(method.Name)
                    || type.Name == "HoverTooltip" && method.Name == "showTooltip" && method.Parameters.Count == 2) Constant(method, 0);
                if (type.Name == "RebirthPowerSpell" && method.Name == "castRebirthSpell")
                    method.Body.GetILProcessor().InsertBefore(method.Body.Instructions[0], Instruction.Create(OpCodes.Call,
                        game.MainModule.ImportReference(typeof(PresetIntegrationChecks).GetMethod("CountNativeNumberFlush", All))));
                if (type.Name == "BloodMagicController" && new[] { "updateBloodMagicText", "updateBloodMagicBar" }.Contains(method.Name)) Constant(method, 0);
                if (type.Name == "AllGoldDiggerController" && method.Name == "updateText") Constant(method, 0);
                if (type.Name == "GoldDiggerUIController" && method.Name == "updateUI")
                    KeepNativePodTextPath(game.MainModule, method);
                if (type.Name == "AllBeardsController" && method.Name == "refreshMenu")
                {
                    method.Body = new Mono.Cecil.Cil.MethodBody(method);
                    var il = method.Body.GetILProcessor(); il.Emit(OpCodes.Call, game.MainModule.ImportReference(typeof(PresetIntegrationChecks).GetMethod("BeardRefresh", All))); il.Emit(OpCodes.Ret);
                }
            }
            typeof(Program).GetMethod("ConfigureFiniteActionGameFixture", All)?.Invoke(null, new object[] { game });
            typeof(PresetIntegrationChecks).GetMethod("ConfigureRouteCombatGameFixture", All)?.Invoke(null, new object[] { game });
            ConfigureRetainedFarmingGameFixture(game);
            using (var bytes = new MemoryStream()) { game.Write(bytes); return Assembly.Load(bytes.ToArray()); }
        }
    }

    public static void BeardRefresh() { BeardRefreshInterference?.Invoke(); if (FailBeardRefresh) throw new InvalidOperationException("fixture beard refresh failure"); }
    public static void AtRefresh() { AtRefreshObserver?.Invoke(); if (FailAtRefresh) throw new InvalidOperationException("fixture native AT rendering failure"); }
    public static void AtTargetText()
    {
        AtRefreshObserver?.Invoke();
        if (FailAtTargetText || FailAtRefresh) throw new InvalidOperationException("fixture native AT rendering failure");
    }
    public static float PresetRespawn() { CaptureNativeObserver?.Invoke(); return PresetRespawnSeconds; }
    public static float AdventureInputValue(string key, float fallback)
    { float value; return AdventureInputs.TryGetValue(key, out value) ? value : fallback; }
    private static void AdventureInput(ModuleDefinition module, MethodDefinition method, float fallback)
    {
        method.Body = new Mono.Cecil.Cil.MethodBody(method); var il = method.Body.GetILProcessor();
        if (method.Name == "getBonus")
        {
            var adventure = Instruction.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldc_I4, 9); il.Emit(OpCodes.Beq, adventure);
            il.Emit(OpCodes.Ldc_R4, fallback); il.Emit(OpCodes.Ret); il.Append(adventure);
        }
        il.Emit(OpCodes.Ldstr, method.DeclaringType.Name + "." + method.Name); il.Emit(OpCodes.Ldc_R4, fallback);
        il.Emit(OpCodes.Call, module.ImportReference(typeof(PresetIntegrationChecks).GetMethod("AdventureInputValue", All))); il.Emit(OpCodes.Ret);
    }
    internal static void KeepNativeItopodEntry(MethodDefinition method)
    {
        // Execute the original ITOPOD entry/state-write slices, not rendering or Unity Random state.
        var original = method.Body.Instructions.ToArray();
        var level = original.Single(x => x.OpCode == OpCodes.Stfld && x.Operand is FieldReference && ((FieldReference)x.Operand).Name == "itopodLevel"
            && x.Previous.Operand is FieldReference && ((FieldReference)x.Previous.Operand).Name == "itopodStart");
        var start = original.Last(x => x.Offset < level.Offset && x.OpCode == OpCodes.Ldarg_0
            && x.Next.Operand is FieldReference && ((FieldReference)x.Next.Operand).Name == "ac");
        var end = original.First(x => x.Offset > level.Offset && x.OpCode == OpCodes.Stfld && x.Operand is FieldReference && ((FieldReference)x.Operand).Name == "itopodKillCount");
        var slices = original.Where(x => x.Offset >= start.Offset && x.Offset <= end.Offset).ToList();
        foreach (var store in original.Where(x => x.OpCode == OpCodes.Stfld && x.Operand is FieldReference && ((FieldReference)x.Operand).Name == "zone"
            || x.Operand is MethodReference && ((MethodReference)x.Operand).Name == "set_zone"))
        {
            var first = store.Previous;
            while (first.OpCode != OpCodes.Ldarg_0) first = first.Previous;
            slices.AddRange(original.Where(x => x.Offset >= first.Offset && x.Offset <= store.Offset));
        }
        method.Body = new Mono.Cecil.Cil.MethodBody(method); var il = method.Body.GetILProcessor();
        foreach (var old in slices)
        {
            var copy = Instruction.Create(OpCodes.Nop); copy.OpCode = old.OpCode; copy.Operand = old.Operand; il.Append(copy);
        }
        il.Emit(OpCodes.Ret);
    }
    private static void KeepNativePodTextPath(ModuleDefinition module, MethodDefinition method)
    {
        // Execute the game's menu/ID guards and exact InputField assignment IL; omit unrelated rendering.
        var original = method.Body.Instructions.ToArray();
        var store = original.Single(x => x.Operand is MethodReference && ((MethodReference)x.Operand).DeclaringType.Name == "InputField" && ((MethodReference)x.Operand).Name == "set_text");
        var input = original.Single(x => x.OpCode == OpCodes.Ldfld && x.Operand is FieldReference && ((FieldReference)x.Operand).Name == "diggerLevelInput");
        var firstReturn = original.First(x => x.OpCode == OpCodes.Ret);
        var valid = original.First(x => x.Offset > firstReturn.Offset && (x.OpCode == OpCodes.Blt || x.OpCode == OpCodes.Blt_S) && ((Instruction)x.Operand).Offset > x.Offset);
        // The first BLT rejects a negative ID; the second accepts an ID below the native model count.
        valid = original.First(x => x.Offset > valid.Offset && (x.OpCode == OpCodes.Blt || x.OpCode == OpCodes.Blt_S));
        var guard = original.Where(x => x.Offset <= valid.Offset).ToArray();
        var text = original.Where(x => x.Offset >= input.Previous.Offset && x.Offset <= store.Offset).ToArray();
        var map = guard.Concat(text).ToDictionary(x => x, x => Instruction.Create(OpCodes.Nop));
        var callback = Instruction.Create(OpCodes.Ldarg_0);
        foreach (var old in guard.Concat(text))
        {
            var next = map[old]; next.OpCode = old.OpCode; next.Operand = old.Operand;
            if (old.OpCode == OpCodes.Ret) { next.OpCode = OpCodes.Br; next.Operand = callback; }
            else if (old.Operand is Instruction)
            {
                var target = (Instruction)old.Operand;
                next.Operand = map.ContainsKey(target) ? map[target] : target == valid.Operand ? map[text[0]] : callback;
            }
        }
        method.Body = new Mono.Cecil.Cil.MethodBody(method); var il = method.Body.GetILProcessor();
        foreach (var instruction in guard) il.Append(map[instruction]);
        il.Emit(OpCodes.Br, callback);
        foreach (var instruction in text) il.Append(map[instruction]);
        il.Append(callback); il.Emit(OpCodes.Call, module.ImportReference(typeof(PresetIntegrationChecks).GetMethod("PodRefresh", All))); il.Emit(OpCodes.Ret);
    }
    public static void PodRefresh(object pod)
    {
        PodRefreshCalls++;
        var id = (int)Get(pod, "id");
        if ((int)Get(Character, "menuID") == 44 && id >= 0 && id < 12
            && (string)Property(Get(pod, "diggerLevelInput"), "text") == Get(((IList)Get(Get(Character, "diggers"), "diggers"))[id], "curLevel").ToString()) NativePodTextWrites++;
        if (PodInterference != null) PodInterference(pod);
        if (id != 0 || (long)Get(((IList)Get(Get(Character, "diggers"), "diggers"))[0], "curLevel") != 4) return;
        if (DropGoldPodRefresh) { DropGoldPodRefresh = false; Set(Get(Character, "machine"), "realBaseGold", 0d); }
        if (ForeignPodRefresh) { ForeignPodRefresh = false; Set(((IList)Get(Get(Character, "diggers"), "diggers"))[11], "curLevel", 2L); }
        if (FailPodRefresh) { FailPodRefresh = false; throw new InvalidOperationException("fixture pod refresh failure after exact level"); }
    }
    public static void InputWritten(object input, string text) { if (InputInterference != null) InputInterference(input, text); }
    private static Assembly LoadUi(string path)
    {
        using (var ui = AssemblyDefinition.ReadAssembly(path))
        {
            var input = ui.MainModule.Types.Single(x => x.Name == "InputField"); var text = input.Fields.Single(x => x.Name == "m_Text");
            foreach (var method in input.Methods.Where(x => x.Name == "get_text" || x.Name == "set_text"))
            {
                method.Body = new Mono.Cecil.Cil.MethodBody(method); var il = method.Body.GetILProcessor(); il.Emit(OpCodes.Ldarg_0);
                if (method.Name == "set_text")
                {
                    il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stfld, text);
                    il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Call, ui.MainModule.ImportReference(typeof(PresetIntegrationChecks).GetMethod("InputWritten", All)));
                }
                else il.Emit(OpCodes.Ldfld, text);
                il.Emit(OpCodes.Ret);
            }
            typeof(PresetIntegrationChecks).GetMethod("ConfigureRouteCombatUiFixture", All)?.Invoke(null, new object[] { ui });
            using (var bytes = new MemoryStream()) { ui.Write(bytes); return Assembly.Load(bytes.ToArray()); }
        }
    }
    private static void Boundary(ModuleDefinition module, MethodDefinition method, string callback, bool instance)
    {
        method.Body = new Mono.Cecil.Cil.MethodBody(method); var il = method.Body.GetILProcessor();
        if (instance) il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, module.ImportReference((callback.StartsWith("Preset") ? typeof(PresetIntegrationChecks) : typeof(Program)).GetMethod(callback, All))); il.Emit(OpCodes.Ret);
    }
    public static void PresetSwapGear() { Invoke(typeof(Program), "SwapGear"); GearRenderObserver?.Invoke(); }
    public static void PresetRecallEnergy(object c)
    {
        Invoke(typeof(Program), "RecallEnergy", c);
        var total = (long)Get(c, "idleEnergy");
        foreach (var row in (IEnumerable)Get(Get(c, "NGU"), "skills")) { total += (long)Get(row, "energy"); Set(row, "energy", 0L); }
        var machine = Get(c, "machine"); total += (long)Get(machine, "speedEnergy"); Set(machine, "speedEnergy", 0L);
        Set(c, "idleEnergy", total);
        RecallObserver?.Invoke();
    }
    public static void PresetRecallMagic(object c)
    {
        Invoke(typeof(Program), "RecallMagic", c);
        var magic = Get(c, "magic"); var total = (long)Get(magic, "idleMagic");
        foreach (var row in ((IEnumerable)Get(Get(c, "NGU"), "magicSkills")).Cast<object>()
            .Concat(((IEnumerable)Get(Get(c, "bloodMagic"), "ritual")).Cast<object>()))
        { total += (long)Get(row, "magic"); Set(row, "magic", 0L); }
        var machine = Get(c, "machine"); total += (long)Get(machine, "goldMultiMagic"); Set(machine, "goldMultiMagic", 0L);
        Set(magic, "idleMagic", total);
        RecallObserver?.Invoke();
    }
    private static void Constant(MethodDefinition method, double value)
    {
        method.Body = new Mono.Cecil.Cil.MethodBody(method); var il = method.Body.GetILProcessor();
        if (method.ReturnType.FullName == "System.Single") il.Emit(OpCodes.Ldc_R4, (float)value);
        else if (method.ReturnType.FullName == "System.Double") il.Emit(OpCodes.Ldc_R8, value);
        else if (method.ReturnType.FullName == "System.Int64") il.Emit(OpCodes.Ldc_I8, (long)value);
        else if (method.ReturnType.FullName != "System.Void") il.Emit(OpCodes.Ldc_I4, (int)value);
        il.Emit(OpCodes.Ret);
    }

    private static void PrepareSupport()
    {
        // Native characters always have R3, including before it is unlocked/managed.
        Child(Character, "res3");
        // Adapter fixtures select NGUs via globals, not request/preset-local ratios.
        Set(Get(MainType, "Settings"), "_energyNguWeights", new[] {1, 0, 0, 0, 0, 0, 0, 0, 0});
        Set(Get(MainType, "Settings"), "_magicNguWeights", new[] {1, 0, 0, 0, 0, 0, 0});
        foreach (var name in new[] { "beards", "diggers", "adventure" }) Set(Child(Get(Character, "buttons"), name), "m_Interactable", name != "adventure");
        foreach (var name in new[] { "allBeards", "allDiggers" }) Set(Get(Character, name), "character", Character);
        Child(Get(Character, "allBeards"), "tooltip"); Child(Get(Character, "allDiggers"), "tooltip");
        Set(Get(Character, "allBeards"), "usesEnergy", new bool[7]);
        Set(Get(Get(Character, "challenges"), "trollChallenge"), "curCompletions", 7);
        Child(Character, "machine"); Set(Get(Character, "machine"), "realBaseGold", 400d); Child(Character, "timeMachineController");
        var adventure = Get(Character, "adventure") ?? Child(Character, "adventure"); Set(adventure, "attack", 1000f); Set(adventure, "defense", 1000f); Set(adventure, "attackSpeed", 1f);
        Child(adventure, "itopod");
        Set(Get(Character, "adventureController"), "character", Character);
        var blood = Child(Character, "bloodMagicController"); Set(blood, "character", Character);
        Child(Character, "bloodMagic"); ListField(Get(Character, "bloodMagic"), "ritual", 1);
        var rituals = ArrayField(blood, "bloodMagics", 1); Set(rituals.GetValue(0), "character", Character); Set(rituals.GetValue(0), "id", 0);
        Set(Child(Get(Character, "arbitrary"), "lootcharm1Time"), "totalseconds", 0d);
        Set(Get(Character, "allChallenges"), "character", Character);
        Set(Get(Character, "cardsController"), "character", Character);
        var diggers = Get(Character, "allDiggers");
        foreach (var name in new[] { "baseGPSDrain", "gpsGrowthRate", "startingBoost", "boostPerLevel" })
        {
            Sequence(diggers, name, 12, name == "gpsGrowthRate" ? 2 : 0);
        }
        Sequence(diggers, "consumesGPS", 12, false);
        var pods = ListField(diggers, "pods", 1); var pod = pods[0];
        Set(pod, "character", Character); Set(pod, "id", 7); Child(pod, "diggerLevelInput"); Child(pod, "tooltip");
        Set(Get(pod, "diggerLevelInput"), "m_Text", "previous input");
        foreach (var id in new[] { 1, 3, 5, 6 })
        { var beard = ((IList)Get(Get(Character, "beards"), "beards"))[id]; Set(beard, "beardLevel", 1000L); Set(beard, "permLevel", 100L); }
        SetActiveBeards(1, 3, 5, 6);
    }

    private static void CheckBeardCapture()
    {
        var snapshot = New("PresetSnapshot"); CallMain("CaptureLoadoutPresetSupport", snapshot);
        var temporary = (float[])Get(snapshot, "BeardTemporary"); var permanent = (float[])Get(snapshot, "BeardPermanent");
        var all = Get(Character, "allBeards");
        var names = new[] { "NGU", "Gold", "Adventure", "Loot" };
        for (var i = 0; i < names.Length; i++)
        {
            Equal(Invoke(all, "temp" + names[i] + "Bonus", true), temporary[i], "capture uses native potential " + names[i]);
            Equal(Invoke(all, "perm" + names[i] + "Bonus"), permanent[i], "capture retains native permanent " + names[i]);
        }
        foreach (var active in new[] { new[] { 1, 3, 5, 6 }, new int[0], new[] { 3 }, new[] { 6 } })
        {
            for (var i = 0; i < names.Length; i++)
            {
                var projected = Planner("BeardBonus", snapshot, active, i); SetActiveBeards(active);
                var native = Invoke(all, new[] { "nguBonus", "goldBonus", "adventureBonus", "lootBonus" }[i]);
                if (active.Contains(new[] { 3, 6, 5, 1 }[i])) Equal(native, projected, "unchanged activation retains exact captured native " + names[i]);
                else Check(Math.Abs((float)native - (float)projected) <= Math.Abs((float)native) * 1.2e-7, "changed activation preserves native Single precision " + names[i]);
            }
            var nativeGold = (double)Invoke(Character, "grossGoldPerSecond"); var projectedGold = (double)Planner("GrossGold", snapshot, active);
            if (active.Contains(6)) Equal(nativeGold, projectedGold, "unchanged Gold retains exact native GPS");
            else Check(Math.Abs(nativeGold - projectedGold) <= nativeGold * 1.2e-7, "candidate GPS matches native Single-factor precision after immediate Gold deactivation");
        }
        Set(Get(Character, "beards"), "disabled", true); CallMain("CaptureLoadoutPresetSupport", snapshot);
        Equal(1f, Planner("BeardBonus", snapshot, new[] { 1, 3, 5, 6 }, 0), "disabled native support normalizes safely to one");
        Set(Get(Character, "beards"), "disabled", false); SetActiveBeards(1, 3, 5, 6);
    }

    private static void CheckNguCapture()
    {
        var snapshot = New("PresetSnapshot"); var ngu = Get(Character, "NGU");
        Set(ngu, "autoAdvance", true); var skill = ((IList)Get(ngu, "skills"))[0]; Set(skill, "target", 1L);
        CallMain("CaptureLoadoutPresetNgu", snapshot, new[] { 1, 0, 0, 0, 0, 0, 0, 0, 0 }, true);
        Set(snapshot, "NguAutoAdvance", Get(ngu, "autoAdvance"));
        Throws(() => Planner("RequireSupportedNguAdvance", snapshot), "auto-advance", "native finite target capture rejects unsupported transfer");
        Set(skill, "target", 0L); CallMain("CaptureLoadoutPresetNgu", snapshot, new[] { 1, 0, 0, 0, 0, 0, 0, 0, 0 }, true);
        Planner("RequireSupportedNguAdvance", snapshot); Equal(true, Get(ngu, "autoAdvance"), "capture never writes auto-advance preference");
    }

    private static void CheckDropCapture()
    {
        var snapshot = New("PresetSnapshot"); CallMain("CaptureLoadoutPresetSupport", snapshot);
        Invoke(Plugin.GetType("NGUInjector.Managers.GearOptimizer", true), "CapturePresetGear", snapshot);
        Set(snapshot, "Drop", Convert.ToDouble(Invoke(Character, "lootFactor")));
        var items = Get(snapshot, "Current");
        var projected = Planner("Drop", snapshot, items, new[] { 1, 3, 5, 6 }, 1d);
        Equal(Convert.ToDouble(Invoke(Character, "lootFactor")), projected, "default Current support keeps native loot getter parity");
        SetActiveBeards(3, 5, 6);
        var without = (double)Planner("Drop", snapshot, items, new[] { 3, 5, 6 }, 1d);
        var native = Convert.ToDouble(Invoke(Character, "lootFactor"));
        Check(Math.Abs(native - without) <= native * 1.2e-7, "removing loot changes immediate drop projection at native Single precision");
        SetActiveBeards(1, 3, 5, 6);
    }

    private static void CheckItopodCapture()
    {
        var adventure = Get(Character, "adventure"); var controller = Get(Character, "adventureController");
        var perks = Get(controller, "itopod"); var model = Get(adventure, "itopod"); var items = Get(Get(Character, "inventory"), "itemList");
        var oldKey = Get(items, "itopodKeyComplete"); var oldHighest = Get(adventure, "highestItopodLevel");
        var oldEnemies = Get(controller, "itopodEnemyList"); var oldLevels = Get(model, "perkLevel");
        var oldBeards = ((IList)Get(Get(Character, "beards"), "activeBeards")).Cast<int>().ToArray();
        var oldNgu = Get(Character, "NGU"); var oldAuto = Get(oldNgu, "autoAdvance");
        try
        {
            Set(items, "itopodKeyComplete", true); Set(adventure, "highestItopodLevel", 12); Set(oldNgu, "autoAdvance", false);
            SetActiveBeards(3, 6);
            Set(perks, "character", Character); Sequence(model, "perkLevel", 100, 0L);
            var enemies = ListField(controller, "itopodEnemyList", 1); Set(enemies[0], "maxHP", 600f); Set(enemies[0], "defense", 10f);
            var recalls = Get(typeof(Program), "Recalls"); var swaps = Get(typeof(Program), "Swaps");
            var capture = CallMain("CaptureLoadoutPreset"); var snapshot = Get(capture, "Snapshot");
            var pp = (double[])Get(snapshot, "PpPerKill"); var hp = (double[])Get(snapshot, "ItopodHp"); var defense = (double[])Get(snapshot, "ItopodDefense");
            Equal(12, pp.Length, "native capture limits repeatable floors to highest reached minus one, not global maximum");
            Equal(pp.Length, hp.Length, "native enemy HP capture covers exactly the PP comparison range");
            Equal(pp.Length, defense.Length, "native enemy defense capture covers exactly the PP comparison range");
            for (var f = 0; f < pp.Length; f++) Equal(Convert.ToDouble(Invoke(perks, "progressGained", (long)f)), pp[f], "floor reference uses untouched native progressGained at " + f);
            Equal((double)(600f * 1.02f), hp[0], "capture uses actual enemy base HP and upper native spawn roll");
            Equal((double)(10f * 1.02f), defense[0], "capture uses actual enemy defense and upper native spawn roll");
            Check(hp[11] > hp[1] && defense[11] > defense[1], "native capture preserves floor-scaled combat inputs, not a guessed damage multiplier");
            var material = CallMain("LoadoutPresetMaterial", capture); hp[1] += 1;
            Check(!Equals(material, CallMain("LoadoutPresetMaterial", capture)), "captured native enemy data participates in preview material identity");
            Equal(recalls, Get(typeof(Program), "Recalls"), "ITOPOD capture is read only with no recalls");
            Equal(swaps, Get(typeof(Program), "Swaps"), "ITOPOD capture is read only with no gear swaps");
        }
        finally
        {
            Set(items, "itopodKeyComplete", oldKey); Set(adventure, "highestItopodLevel", oldHighest);
            Set(controller, "itopodEnemyList", oldEnemies); Set(model, "perkLevel", oldLevels); Set(oldNgu, "autoAdvance", oldAuto);
            SetActiveBeards(oldBeards);
        }
    }

    private static void CheckFingerprint()
    {
        var capture = Activator.CreateInstance(MainType.GetNestedType("LoadoutPresetCapture", All), true);
        var snapshot = New("PresetSnapshot"); CallMain("CaptureLoadoutPresetSupport", snapshot);
        Invoke(Plugin.GetType("NGUInjector.Managers.GearOptimizer", true), "CapturePresetGear", snapshot);
        Set(capture, "Snapshot", snapshot); Set(capture, "Instances", Activator.CreateInstance(Field(capture, "Instances").FieldType));
        Set(snapshot, "NguAutoAdvance", false); var before = CallMain("LoadoutPresetMaterial", capture);
        Set(snapshot, "NguAutoAdvance", true); var after = CallMain("LoadoutPresetMaterial", capture);
        Check(!Equals(before, after), "native auto-advance capture participates in the actual material fingerprint");
        var guard = New("PresetPreviewIdentity"); Set(guard, "Id", new string('a', 32)); Set(guard, "Definition", "preset");
        Set(guard, "Material", before); Set(guard, "EnergyWeights", new[] { 1 }); Set(guard, "MagicWeights", new[] { 1 });
        Throws(() => Invoke(guard, "Validate", new string('a', 32), "preset", new[] { 1 }, new[] { 1 }, after, 0d, 0L, 0d, 0d), "material", "auto-advance changes invalidate preview Apply identity");
    }

    private static void CheckExactBlood()
    {
        var bloodButton = Get(Get(Character, "buttons"), "bloodMagic"); var bloodUnlocked = Get(bloodButton, "m_Interactable");
        Set(bloodButton, "m_Interactable", true);
        var magic = Get(Character, "magic"); var ritual = ((IList)Get(Get(Character, "bloodMagic"), "ritual"))[0];
        foreach (var amount in new[] { 9007199254740991L, 9007199254740993L, 9007199254740995L })
        foreach (var progress in new[] { 0f, .4f })
        {
            var candidate = New("PresetCandidate"); Set(candidate, "Ritual", 0); Set(candidate, "Blood", amount);
            Set(candidate, "ControlsMagic", true);
            Set(candidate, "EnergyShares", new long[9]); Set(candidate, "MagicShares", new long[7]);
            Set(ritual, "magic", 7L); Set(ritual, "progress", progress); Set(magic, "idleMagic", amount + 123); Set(Character, "realGold", 987d);
            CallMain("ApplyLoadoutPresetAllocations", candidate);
            Equal(amount, Get(ritual, "magic"), "exact desired blood allocation beyond 53 bits"); Equal(130L, Get(magic, "idleMagic"), "desired-map diff retains the existing seven units rather than adding twice");
            Equal(progress, Get(ritual, "progress"), "allocation preserves native unpaid/paid progress"); Equal(987d, Get(Character, "realGold"), "allocation does not charge native tick startup cost");
            Set(ritual, "magic", 7L); Set(magic, "idleMagic", amount + 123);
            Invoke(((Array)Get(Get(Character, "bloodMagicController"), "bloodMagics")).GetValue(0), "addMagic", amount);
            Equal(amount + 7, Get(ritual, "magic"), "production transfer matches native exact-long addMagic");
            Equal(123L, Get(magic, "idleMagic"), "native exact-long control retains the same remainder");
            Equal(progress, Get(ritual, "progress"), "native addMagic likewise preserves paid/unpaid progress");
        }
        var shared = New("PresetCandidate"); Set(shared, "Ritual", 0); Set(shared, "Blood", 9007199254740995L);
        Set(shared, "ControlsMagic", true);
        Set(shared, "EnergyShares", new long[9]); Set(shared, "MagicShares", new[] { 123L, 0, 0, 0, 0, 0, 0 });
        Set(ritual, "magic", 0L); Set(magic, "idleMagic", 9007199254741118L);
        Set(Get(Get(Character, "buttons"), "ngu"), "m_Interactable", true);
        CallMain("ApplyLoadoutPresetAllocations", shared);
        Equal(9007199254740995L, Get(ritual, "magic"), "blood does not round upward and steal the next NGU share");
        Equal(123L, Get(((IList)Get(Get(Character, "NGU"), "magicSkills"))[0], "magic"), "following NGU receives its exact long share");
        Equal(0L, Get(magic, "idleMagic"), "shared magic application conserves every unit");
        Set(Get(Get(Character, "buttons"), "ngu"), "m_Interactable", false);
        var overflow = New("PresetCandidate"); Set(overflow, "Ritual", 0); Set(overflow, "Blood", 1L);
        Set(overflow, "ControlsMagic", true);
        Set(ritual, "magic", long.MaxValue); Set(magic, "idleMagic", 5L);
        Throws(() => CallMain("ApplyLoadoutPresetAllocations", overflow), "overflow", "overflow fails before either exact-long write");
        Equal(5L, Get(magic, "idleMagic"), "overflow preserves idle magic");
        Set(bloodButton, "m_Interactable", bloodUnlocked);
    }

    private static void CheckPreviewAndApply()
    {
        var settings = Get(MainType, "Settings");
        var savedSettings = new[] { "_useCustomRebirthPlan", "_autoRebirth", "_manualAdventureEnabled", "_savePath" }
            .ToDictionary(name => name, name => Get(settings, name));
        var settingsPath = Path.Combine((string)Get(typeof(Program), "Temp"), "preset-settings.json");
        foreach (var name in new[] { "_useCustomRebirthPlan", "_autoRebirth", "_manualAdventureEnabled" }) Set(settings, name, true);
        Set(settings, "_savePath", settingsPath);
        var scheduler = New("ProtectedStageScheduler");
        Set(scheduler, "<Entry>k__BackingField", 9L); Set(scheduler, "<StageIndex>k__BackingField", 2); Set(scheduler, "_armed", true);
        var identity = new Dictionary<string, object> {
            { "_customStageKey", "fixture:2:entry-9" }, { "_customStageFightKey", "fixture:fixture:2:entry-9" },
            { "_customStageEquipmentKey", "fixture-equipment-entry-9" }, { "_protectedScheduler", scheduler },
            { "_protectedGeneration", "fixture-generation" } };
        var receipts = new Dictionary<string, object> {
            { "_customEquipmentApplicationInvalidated", false },
            { "_customEquipmentOwner", "fixture-owner" },
            { "_customEquipmentLastDiggersAt", DateTime.UtcNow }, { "_bossPushRunning", true } };
        foreach (var name in new[] { "_customSustainedFocusGoal", "_customRequestedFocusGoal", "_customAppliedFocusGoal" })
            receipts.Add(name, Enum.ToObject(Nullable.GetUnderlyingType(Field(MainInstance, name).FieldType), 0));
        var savedState = identity.Keys.Concat(receipts.Keys).ToDictionary(name => name, name => Get(MainInstance, name));
        foreach (var pair in identity.Concat(receipts)) Set(MainInstance, pair.Key, pair.Value);
        Action<bool, string> checkState = (applied, label) => {
            foreach (var name in new[] { "UseCustomRebirthPlan", "AutoRebirth", "ManualAdventureEnabled" })
                Equal(true, Property(settings, name), label + " preserves enabled " + name);
            Check(!File.Exists(settingsPath), label + " never persists settings changes");
            foreach (var pair in identity) Equal(pair.Value, Get(MainInstance, pair.Key), label + " preserves " + pair.Key);
            Equal(9L, Property(scheduler, "Entry"), label + " preserves scheduler entry");
            Equal(2, Property(scheduler, "StageIndex"), label + " preserves scheduler position");
            Equal(!applied, Get(scheduler, "_armed"), label + " only suspends scheduler credit after preflight");
            foreach (var pair in receipts)
            {
                var expected = !applied ? pair.Value : pair.Key == "_customEquipmentApplicationInvalidated" ? (object)true
                    : pair.Value is bool ? false : pair.Value is DateTime ? pair.Value : null;
                Equal(expected, Get(MainInstance, pair.Key), label + " handles " + pair.Key);
            }
        };
        SetActiveBeards(3, 6); Set(Get(Get(Character, "challenges"), "trollChallenge"), "curCompletions", 7);
        Set(Get(Character, "NGU"), "autoAdvance", false);
        var diggerController = Get(Character, "allDiggers"); var diggers = (IList)Get(Get(Character, "diggers"), "diggers");
        Set(diggers[4], "maxLevel", 1L); Set(diggers[0], "maxLevel", 10L);
        var baseDrain = (IList)Get(diggerController, "baseGPSDrain");
        baseDrain[4] = Convert.ChangeType(1000, baseDrain[4].GetType()); baseDrain[0] = Convert.ChangeType(100, baseDrain[0].GetType());
        ((IList)Get(diggerController, "consumesGPS"))[4] = true; ((IList)Get(diggerController, "consumesGPS"))[0] = true;
        Set(Get(Get(Character, "buttons"), "ngu"), "m_Interactable", true);
        CheckCurrentGetterParity();
        var args = JsonParse("{\"preset\":{\"Id\":\"fixture\",\"Name\":\"fixture\",\"Weights\":[1,0,0,0,0,0],\"SupportMode\":\"planned\",\"BeardPriority\":[1],\"DiggerWeights\":[1,0,0,0,2,0,0,0,0,0,0,0],\"HorizonSeconds\":10,\"SwapSeconds\":1}}" );
        var preview = CallMain("PreviewLoadoutPreset", args);
        Equal(0, Get(typeof(Program), "Recalls"), "actual preview does not recall resources");
        Equal(0, Get(typeof(Program), "Swaps"), "actual preview does not swap gear");
        var token = Json(preview, "previewId").ToString();
        var sequenceApply = JsonParse(args.ToString().Substring(0, args.ToString().Length - 1) + ",\"previewId\":" + token + ",\"candidateKey\":\"sequence\"}");
        Throws(() => Invoke(MainInstance, "ApplyLoadoutPreset", sequenceApply, null), "Candidate key", "advisory sequence cannot be applied as an executable policy");
        Check(Get(MainInstance, "_loadoutPresetPreview") != null, "sequence rejection leaves the one-shot token unconsumed");
        Equal(0, Get(typeof(Program), "Recalls"), "advisory sequence rejection never recalls resources");
        checkState(false, "sequence preflight rejection");
        var apply = JsonParse(args.ToString().Substring(0, args.ToString().Length - 1) + ",\"previewId\":" + token + ",\"candidateKey\":\"current\"}");
        object[] invocation = { apply, null };
        Set(Get(Character, "NGU"), "autoAdvance", true);
        Throws(() => Invoke(MainInstance, "ApplyLoadoutPreset", invocation), "material", "native auto-advance edit between Preview and Apply fails actual preflight");
        Check(Get(MainInstance, "_loadoutPresetPreview") != null, "material rejection preserves unconsumed preview token");
        Equal(0, Get(typeof(Program), "Recalls"), "auto-advance material rejection does not recall resources");
        Equal(true, Get(Get(Character, "NGU"), "autoAdvance"), "rejected application preserves user's edited native preference");
        checkState(false, "material preflight rejection");
        Set(Get(Character, "NGU"), "autoAdvance", false);
        var cached = Get(MainInstance, "_loadoutPresetPreview");
        var replay = Planner("Replay", Get(Get(cached, "Capture"), "Snapshot"), CallMain("LoadoutPresetRequest", Get(cached, "Preset")), Get(cached, "Plan"));
        var rawWeights = new double[] { 1, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0 };
        foreach (var candidate in ((Array)Get(replay, "Candidates")).Cast<object>().Concat(((Array)Get(replay, "Specialists")).Cast<object>()))
            Check(((double[])Get(Get(candidate, "Support"), "DiggerWeights")).SequenceEqual(rawWeights), "exact replay retains raw explicit digger weights for every role");
        Invoke(MainInstance, "ApplyLoadoutPreset", invocation);
        checkState(true, "successful Apply");
        var result = Json(invocation[1], "result");
        Equal("\"run-baseline\"", Json(result, "baselineKey").ToString(), "post-apply response identifies original run baseline separately");
        var rows = Json(result, "candidates"); var original = Json(rows, 0); var observed = Json(rows, 1);
        Equal("\"current\"", Json(observed, "key").ToString(), "applying Current has no duplicate baseline/candidate key");
        Equal(Json(Json(Json(preview, "candidates"), 0), "metrics").ToString(), Json(original, "metrics").ToString(), "post-apply retains original run baseline metrics and normalization");
        Check(Json(Json(Json(preview, "candidates"), 0), "metrics").ToString() != Json(observed, "metrics").ToString(), "post-apply candidate is rebuilt from observed setup rather than old pre-apply metrics");
        Equal(Json(preview, "normalization").ToString(), Json(result, "normalization").ToString(), "post-apply preserves run normalization");
        Equal(Json(preview, "productionReferences").ToString(), Json(result, "productionReferences").ToString(), "post-apply keeps fixed reference production and source keys even though specialist rows are no longer displayed");
        Equal("[1]", Json(Json(observed, "support"), "beards").ToString(), "post-apply reports native applied beard selection");
        Equal("[1,0,0,0,2,0,0,0,0,0,0,0]", Json(Json(Json(Json(preview, "candidates"), 0), "support"), "diggerWeights").ToString(), "preview exposes full raw chosen style indexed by digger ID");
        Equal("[1,0,0,0,2,0,0,0,0,0,0,0]", Json(Json(observed, "support"), "diggerWeights").ToString(), "observed Apply retains full chosen style, including unaffordable digger weight");
        Equal("1", Json(Json(Json(Json(observed, "support"), "diggers"), 0), "weight").ToString(), "applied digger row exposes actual selected weight, not allocation level");
        Equal(3L, Get(diggers[0], "curLevel"), "native application keeps affordable backfilled drop digger level after removing Gold");
        Equal(true, Get(diggers[0], "active"), "native activation accepts the beard-adjusted support budget");
        Equal(false, Get(diggers[4], "active"), "expensive top-ranked digger is not predicted affordable using old Gold GPS");
        Check(double.Parse(Json(Json(observed, "support"), "goldBudget").ToString(), System.Globalization.CultureInfo.InvariantCulture) < 451,
            "observed response budgets from actual reduced GPS, not stale Gold-active income");
        Check(Get(MainInstance, "_loadoutPresetPreview") == null, "actual Apply consumes token only after complete preflight");
        Check(((IList)Get(Get(Character, "beards"), "activeBeards")).Cast<int>().SequenceEqual(new[] { 1 }), "actual Apply changes support once without a new owner");
        var runtime = CallMain("GetLoadoutPresetRuntime");
        var reason = Json(runtime, "reason").ToString();
        Check(reason.Contains("settings were unchanged") && !reason.Contains("paused"), "applied metadata truthfully describes preserved automation settings");
        Check(Json(result, "assumptions").ToString().Contains("settings remain unchanged") && !Json(result, "assumptions").ToString().Contains("are paused"), "result assumptions no longer claim plan or rebirth is paused");
        Equal("false", Json(runtime, "active").ToString(), "one-shot snapshot does not claim sustained ownership");

        SetActiveBeards(3, 6);
        preview = CallMain("PreviewLoadoutPreset", args);
        apply = JsonParse(args.ToString().Substring(0, args.ToString().Length - 1) + ",\"previewId\":" + Json(preview, "previewId") + ",\"candidateKey\":\"current\"}");
        foreach (var pair in receipts) Set(MainInstance, pair.Key, pair.Value);
        Set(scheduler, "_armed", true);
        try
        {
            FailBeardRefresh = true;
            Throws(() => Invoke(MainInstance, "ApplyLoadoutPreset", apply, null), "fixture beard refresh failure", "late support failure propagates through actual Apply");
        }
        finally { FailBeardRefresh = false; }
        checkState(true, "late failed Apply");
        Check(Get(MainInstance, "_loadoutPresetPreview") == null, "late failure consumes the preflighted token");
        Equal(null, CallMain("GetLoadoutPresetRuntime"), "late failure clears applied snapshot rather than advertising success");
        Throws(() => Invoke(MainInstance, "ApplyLoadoutPreset", apply, null), "consumed", "late failed Apply cannot reuse the one-shot token");
        foreach (var pair in savedSettings) Set(settings, pair.Key, pair.Value);
        foreach (var pair in savedState) Set(MainInstance, pair.Key, pair.Value);
        Set(Get(Get(Character, "buttons"), "ngu"), "m_Interactable", false);
    }

    private static void CheckPassiveProgressBaseline()
    {
        var settings = Get(MainType, "Settings");
        var savedMagicWeights = Get(settings, "_magicNguWeights");
        Set(settings, "_magicNguWeights", new int[7]);
        var savedSettings = new[] { "_useCustomRebirthPlan", "_autoRebirth", "_manualAdventureEnabled" }
            .ToDictionary(name => name, name => Get(settings, name));
        foreach (var name in savedSettings.Keys) Set(settings, name, false);
        var ngu = Get(Character, "NGU"); var skills = (IList)Get(ngu, "skills");
        Set(ngu, "autoAdvance", false);
        foreach (var skill in skills) { Set(skill, "energy", 0L); Set(skill, "level", 0L); Set(skill, "target", 0L); }
        Set(skills[0], "target", 20L);
        Set(Character, "curEnergy", 10000L); Set(Character, "idleEnergy", 10000L);
        Set(Get(Get(Character, "buttons"), "ngu"), "m_Interactable", true);
        var args = JsonParse("{\"preset\":{\"Id\":\"passive-progress\",\"Name\":\"Passive progress\",\"Weights\":[1,0,0,0,0,0],\"SupportMode\":\"current\",\"HorizonSeconds\":10,\"SwapSeconds\":0}}");
        var preview = CallMain("PreviewLoadoutPreset", args);
        var previewBaseline = Json(Json(preview, "candidates"), 0);
        Equal("[]", Json(Json(previewBaseline, "support"), "diggerWeights").ToString(), "current-support preview has no applicable chosen weights");
        Equal("null", Json(Json(Json(Json(previewBaseline, "support"), "diggers"), 0), "weight").ToString(), "current support never infers a weight from active levels or drain");
        var horizon = (double)Property(Json(preview, "horizonSeconds"), "AsDouble");
        Equal(20d / horizon, Property(Json(Json(Json(previewBaseline, "metrics"), 0), "value"), "AsDouble"), "preview baseline forecasts all twenty remaining target levels");
        var previewMetric = Json(Json(previewBaseline, "metrics"), 0);
        Equal(20d / horizon, Property(Json(previewMetric, "specialistProduction"), "AsDouble"), "API exposes original best-found specialist production in native units");
        Equal("1", Json(previewMetric, "retainedProduction").ToString(), "API exposes production retained as a ratio");
        Equal("100", Json(previewMetric, "retainedProductionPercent").ToString(), "API explicitly exposes retained-production percent");
        Equal("null", Json(Json(Json(previewBaseline, "metrics"), 1), "retainedProduction").ToString(), "zero specialist reference has undefined retained production, not an invented 100 percent");
        Equal("null", Json(Json(Json(previewBaseline, "metrics"), 3), "retainedProduction").ToString(), "unavailable specialist reference has null retained production");
        Set(skills[0], "level", 19L);
        Set(Character, "curEnergy", 123L); Set(Character, "idleEnergy", 123L);
        var apply = JsonParse(args.ToString().Substring(0, args.ToString().Length - 1) + ",\"previewId\":" + Json(preview, "previewId") + ",\"candidateKey\":\"current\"}");
        object[] invocation = { apply, null }; Invoke(MainInstance, "ApplyLoadoutPreset", invocation);
        var result = Json(invocation[1], "result");
        var baseline = Json(Json(result, "candidates"), 0); var observed = Json(Json(result, "candidates"), 1);
        foreach (var name in new[] { "UseCustomRebirthPlan", "AutoRebirth", "ManualAdventureEnabled" })
            Equal(false, Property(settings, name), "successful current-support Apply preserves initially disabled " + name);
        Equal("[]", Json(Json(observed, "support"), "diggerWeights").ToString(), "current-support Apply keeps chosen weights not applicable");
        Equal(Json(previewBaseline, "metrics").ToString(), Json(baseline, "metrics").ToString(), "passive progress cannot replace original preview baseline metrics with replay metrics");
        Equal(Json(previewBaseline, "score").ToString(), Json(baseline, "score").ToString(), "original preview baseline score remains unchanged");
        Equal(Json(previewBaseline, "energyAllocated").ToString(), Json(baseline, "energyAllocated").ToString(), "original preview baseline allocation remains unchanged after pool shrink");
        Equal(Json(preview, "normalization").ToString(), Json(result, "normalization").ToString(), "passive progress does not recompute preview normalization");
        Equal(Json(preview, "productionReferences").ToString(), Json(result, "productionReferences").ToString(), "passive progress does not recompute original reference production or identities");
        Equal("true", Json(observed, "feasible").ToString(), "fresh selected policy remains feasible after passive progress and pool shrink");
        Equal(1d / horizon, Property(Json(Json(Json(observed, "metrics"), 0), "value"), "AsDouble"), "fresh selected metrics forecast only the one remaining target level");
        var retained = (1d / horizon) / (20d / horizon);
        Equal(retained, Property(Json(observed, "score"), "AsDouble"), "fresh selected score uses the original preview normalization");
        var metric = Json(Json(observed, "metrics"), 0);
        Equal(20d / horizon, Property(Json(metric, "specialistProduction"), "AsDouble"), "observed metric retains the original specialist denominator");
        Equal(Json(previewMetric, "specialistReferenceKey").ToString(), Json(metric, "specialistReferenceKey").ToString(), "observed metric retains the original specialist source key");
        Equal(retained, Property(Json(metric, "retainedProduction"), "AsDouble"), "replayed production is five percent retained rather than renormalized to one");
        Equal(retained * 100, Property(Json(metric, "retainedProductionPercent"), "AsDouble"), "replayed retained percent remains interpretable");
        Equal(retained, Property(Json(metric, "weightedContribution"), "AsDouble"), "API weighted contribution matches the actual score for a single goal");
        Equal("123", Json(observed, "energyAllocated").ToString(), "fresh selected allocation uses the changed actual pool");
        Equal(123L, Get(skills[0], "energy"), "actual NGU application receives the fresh exact allocation");
        Equal(0L, Get(Character, "idleEnergy"), "fresh exact allocation conserves the changed actual pool");
        Set(settings, "_magicNguWeights", savedMagicWeights);
        foreach (var pair in savedSettings) Set(settings, pair.Key, pair.Value);
        Set(Get(Get(Character, "buttons"), "ngu"), "m_Interactable", false);
    }

    private static object JsonParse(string text) { return Invoke(Plugin.GetType("SimpleJSON.JSON", true), "Parse", text); }
    private static void CheckCurrentGetterParity()
    {
        var snapshot = New("PresetSnapshot"); CallMain("CaptureLoadoutPresetSupport", snapshot);
        Invoke(Plugin.GetType("NGUInjector.Managers.GearOptimizer", true), "CapturePresetGear", snapshot);
        var bonuses = Planner("ResourceBonuses", snapshot, Get(snapshot, "Current"));
        foreach (var energy in new[] { true, false }) foreach (var capacity in new[] { true, false })
        {
            var projection = Invoke(Plugin.GetType("NGUInjector.Managers.ResourceProjectionCapture", true), "Create", Character, energy, capacity);
            var native = Invoke(Character, capacity ? energy ? "totalCapEnergy" : "totalCapMagic" : energy ? "totalEnergyPower" : "totalMagicPower");
            Equal(Convert.ToDouble(native), Invoke(projection, "Project", bonuses), "default resource getter parity " + energy + "/" + capacity);
        }
        Set(snapshot, "NguFactors", new[] { 1f, 1f, 1f, 1f, 1f, (float)Invoke(Get(Character, "allBeards"), "nguBonus") });
        Equal(Convert.ToDouble(Invoke(Character, "totalNGUSpeedBonus")), Planner("NguBonus", snapshot, bonuses, null), "default native NGU getter parity");
        var totals = (double[])Planner("Totals", snapshot, Get(snapshot, "Current"));
        foreach (var power in new[] { true, false })
        {
            var factors = CallMain("CaptureLoadoutPresetAdventure", power);
            var projected = Planner("AdventureStat", 1000d, 0d, totals[power ? 0 : 1], factors, 1d, float.NaN);
            Equal(Convert.ToDouble(Invoke(Character, power ? "totalAdvAttack" : "totalAdvDefense")), projected, "default native adventure getter parity " + power);
        }
    }
    private static object Json(object target, object key)
    {
        var property = target.GetType().GetProperties(All).First(p => p.GetIndexParameters().Length == 1 && p.GetIndexParameters()[0].ParameterType == key.GetType());
        return property.GetValue(target, new[] { key });
    }

    private static void CheckSupportInvalidation()
    {
        var support = Selection(new int[0]);
        Action apply = () => {
            var wanted = Activator.CreateInstance(Plugin.GetType("NGUInjector.Main+SelectedSetupRequirements", true), true);
            Set(wanted, "Owner", "support-invalidation");
            Set(wanted, "Gear", Invoke(Plugin.GetType("NGUInjector.Managers.LoadoutManager", true), "CaptureExactLoadout"));
            Set(wanted, "Recall", Enum.Parse(Plugin.GetType("NGUInjector.Managers.GearRecallFootprint", true), "None"));
            Set(wanted, "Support", support); Set(wanted, "Budget", 1d); Set(wanted, "ChangeSupport", true);
            var coordinator = MainInstance;
            Set(wanted, "Authorized", (Func<bool>)(() => ReferenceEquals(Get(MainType, "reference"), coordinator)));
            CallMain("BeginPresetSupportAcquisition", "support-invalidation", "empty");
            CallMain("ApplySelectedSetup", wanted);
        };
        var rotation = DateTime.UtcNow; Set(MainInstance, "_customLastBeardRotation", rotation);
        foreach (var fail in new[] { false, true })
        {
            SetActiveBeards(3); Set(MainInstance, "_customBeardRequirements", "cached"); Set(MainInstance, "_customBeardAppliedState", "receipt");
            FailBeardRefresh = fail;
            apply();
            if (fail) Equal("ModelAppliedRenderFailed", Get(Get(MainInstance, "_presetSupportOutcome"), "Disposition").ToString(), "verified completed beard model reports rendering separately instead of replaying deactivation");
            Equal(null, Get(MainInstance, "_customBeardRequirements"), "direct beard mutation invalidates requirement identity even on failure");
            Equal(null, Get(MainInstance, "_customBeardAppliedState"), "direct beard mutation invalidates observed receipt even on failure");
            Equal(rotation, Get(MainInstance, "_customLastBeardRotation"), "direct mutation does not restart independent rotation clock");
        }
        FailBeardRefresh = false;
        apply();
    }

    private static void CheckGearPreflight()
    {
        var loadouts = Plugin.GetType("NGUInjector.Managers.LoadoutManager", true); var gear = Invoke(loadouts, "CaptureExactLoadout");
        var optimizer = Plugin.GetType("NGUInjector.Managers.GearOptimizer", true);
        var recalls = Get(typeof(Program), "Recalls"); var swaps = Get(typeof(Program), "Swaps");
        Set(Get(Character, "training"), "_attackEnergy", new[] { 20000L, 0, 0, 0, 0, 0 });
        var failure = (string)Invoke(optimizer, "CheckPresetEquip", gear);
        Check(failure != null && failure.Contains("capacity"), "compiled preset preflight uses shared retained-capacity guard");
        Equal(recalls, Get(typeof(Program), "Recalls"), "failed preset gear preflight performs no recall");
        Equal(swaps, Get(typeof(Program), "Swaps"), "failed preset gear preflight performs no swap");
        var inventory = Get(Character, "inventory"); var accessory = ((IList)Get(inventory, "accs"))[1];
        Set(accessory, "spec1Type", Enum.Parse(Field(accessory, "spec1Type").FieldType, "EnergyCap")); Set(accessory, "spec1Cur", 10000f);
        Set(Character, "capEnergy", 1000L); Set(Get(Character, "training"), "_attackEnergy", new[] { 1500L, 0, 0, 0, 0, 0 });
        Program.RefreshBonuses();
        var original = (Array)Invoke(loadouts, "CaptureExactLoadout"); var selected = Array.CreateInstance(original.GetType().GetElementType(), 1);
        var moved = Activator.CreateInstance(original.GetType().GetElementType(), true);
        foreach (var pair in new Dictionary<string, object> { { "equipment", accessory }, { "id", Get(accessory, "id") }, { "slot", 10000 } })
            moved.GetType().GetProperty(pair.Key, All).SetValue(moved, pair.Value);
        selected.SetValue(moved, 0); var wanted = Invoke(loadouts, "ComposeExactLoadout", original, selected);
        Equal(2000L, Invoke(Character, "totalCapEnergy"), "intermediate fixture starts with legal retained training");
        var bonus = Invoke(Plugin.GetType("NGUInjector.Managers.ResourceProjectionCapture", true), "GetBonuses", wanted);
        var projection = Invoke(Plugin.GetType("NGUInjector.Managers.ResourceProjectionCapture", true), "Create", Character, true, true);
        Equal(2000d, Invoke(projection, "Project", bonus), "intermediate fixture also has legal final capacity");
        failure = (string)Invoke(optimizer, "CheckPresetEquip", wanted);
        Check(failure != null && failure.Contains("intermediate"), "preset shared preflight rejects temporary capacity loss while staging a moved accessory");
        Check(ReferenceEquals(((IList)Get(inventory, "accs"))[1], accessory), "intermediate preflight preserves actual equipment positions");
        Equal(recalls, Get(typeof(Program), "Recalls"), "intermediate failure performs no recall");
        Equal(swaps, Get(typeof(Program), "Swaps"), "intermediate failure performs no swap");
    }

    private static object Selection(int[] beards, params long[] pairs)
    { return Activator.CreateInstance(Plugin.GetType("NGUInjector.Managers.SupportSelection", true), All, null, new object[] { beards, DiggerLevels(pairs) }, null); }
    private static Array DiggerLevels(params long[] pairs)
    {
        var levelType = Plugin.GetType("NGUInjector.Managers.SupportDiggerLevel", true);
        var levels = Array.CreateInstance(levelType, pairs.Length / 2);
        for (var i = 0; i < levels.Length; i++) levels.SetValue(Activator.CreateInstance(levelType, All, null, new object[] { (int)pairs[i * 2], pairs[i * 2 + 1] }, null), i);
        return levels;
    }
    private static object ProjectSupport(object context, object selection, double fraction = 1d)
    { return Invoke(Plugin.GetType("NGUInjector.Managers.SupportProjection", true), "Evaluate", context, selection, fraction, true); }
    private static object Property(object target, string name) { return target.GetType().GetProperty(name, All).GetValue(target); }
    private static void CheckSharedSupport()
    {
        var all = Get(Character, "allDiggers"); var diggers = (IList)Get(Get(Character, "diggers"), "diggers");
        var pod = ((IList)Get(all, "pods"))[0]; var input = Get(pod, "diggerLevelInput");
        foreach (var id in new[] { 0, 1, 2, 11 }) Set(diggers[id], "maxLevel", 100L);
        for (var i = 0; i < 12; i++)
        {
            ((IList)Get(all, "startingBoost"))[i] = .123456789d; ((IList)Get(all, "boostPerLevel"))[i] = .000123456789d;
        }
        foreach (var id in new[] { 0, 4 })
        { var b = ((IList)Get(Get(Character, "beards"), "beards"))[id]; Set(b, "beardLevel", 1234567L); Set(b, "permLevel", 222L); }
        SetActiveBeards();
        ((IList)Get(all, "consumesGPS"))[0] = false;
        var before = CallMain("CaptureSupportContext");
        var selection = Selection(new[] { 4, 0 }, 1, 17);
        var evaluation = ProjectSupport(before, selection);
        Check((bool)Get(evaluation, "Feasible"), "shared W selection is feasible at fixed levels");
        CallMain("ApplyExactSupport", selection, 1d, true);
        var beard = Get(Character, "allBeards");
        foreach (var pair in new[] { new[] { "BeardW", "wandoosBonus" }, new[] { "BeardStat", "statBonus" } })
        {
            var expected = (double)Get(evaluation, pair[0]); var native = Convert.ToDouble(Invoke(beard, pair[1]));
            Check(Math.Abs(expected - native) <= native * 1e-6, "shared changed-activation effect matches untouched native " + pair[1]);
        }
        Check(Math.Abs((double)Get(evaluation, "DiggerW") - Convert.ToDouble(Invoke(all, "totalWandoosSpeedBonus"))) <= (double)Get(evaluation, "DiggerW") * 1e-6,
            "shared W digger matches untouched native float getter");
        var observedW = CallMain("CaptureSupportContext");
        Equal(Convert.ToDouble(Invoke(all, "totalWandoosSpeedBonus")), Get(ProjectSupport(observedW, selection), "DiggerW"),
            "unchanged W digger preserves the materialized native SINGLE getter exactly");
        var statSelection = Selection(new[] { 0, 4 }, 2, 71); before = CallMain("CaptureSupportContext");
        evaluation = ProjectSupport(before, statSelection); CallMain("ApplyExactSupport", statSelection, 1d, true);
        var nativeStat = Convert.ToDouble(Invoke(all, "totalStatBonus"));
        Check(Math.Abs(nativeStat - (double)Get(evaluation, "DiggerStat")) <= nativeStat * 1e-6,
            "shared stat digger matches untouched native cubic DOUBLE getter across totalLevelBonus Single boundary");
        var observed = CallMain("CaptureSupportContext");
        Equal(nativeStat, Get(ProjectSupport(observed, statSelection), "DiggerStat"), "unchanged stat digger preserves exact native DOUBLE getter");
        Check(nativeStat != (double)(float)nativeStat, "stat fixture detects accidental float narrowing");
        Equal(7, Get(pod, "id"), "native exact input restores previous pod ID");
        Equal("previous input", Property(input, "text"), "native exact input restores previous pod text");
        var huge = 9007199254740993L; Set(diggers[0], "maxLevel", huge);
        Set(Get(Character, "machine"), "realBaseGold", 0d);
        CallMain("ApplyExactSupport", Selection(new int[0], 0, huge), 0d, true);
        Equal(huge, Get(diggers[0], "curLevel"), "native controller parses exact long beyond 53 bits without rounding");
        Equal(true, Get(diggers[0], "active"), "free digger allowed at zero GPS and zero budget");
        var receipt = Get(MainInstance, "_supportMutation"); var stateKey = Property(Get(receipt, "After"), "StableKey");
        Throws(() => CallMain("ApplyExactSupport", Selection(new int[0], 0, huge + 1), 1d, true), "purchased", "purchased max rejected before first mutation");
        Check(ReferenceEquals(receipt, Get(MainInstance, "_supportMutation")), "preflight failure preserves prior bounded receipt");
        Equal(stateKey, Property(CallMain("CaptureSupportState"), "StableKey"), "max preflight rejection preserves whole support state");
        Throws(() => CallMain("ApplyExactSupport", Selection(new[] { 0, 0 }), 1d, true), "duplicate", "duplicate beard IDs fail preflight");
        Throws(() => CallMain("ApplyExactSupport", Selection(new int[0], 0, 1, 0, 2), 1d, true), "duplicate", "duplicate digger IDs fail preflight");
        Set(Controller, "midDrag", true);
        Throws(() => CallMain("ApplyExactSupport", Selection(new int[0]), 1d, true), "drag", "drag guard fails before clear"); Set(Controller, "midDrag", false);
        Set(Get(Character, "machine"), "realBaseGold", 400d); Set(diggers[0], "maxLevel", huge);
        CallMain("ApplyExactSupport", Selection(new int[0], 0, 3), 1d, true);
        FailPodRefresh = true;
        Throws(() => CallMain("ApplyExactSupport", Selection(new[] { 4 }, 0, 4), 1d, true), "fixture pod", "native throw after exact level leaves journal, not rollback claim");
        receipt = Get(MainInstance, "_supportMutation");
        Equal(false, Get(receipt, "Completed"), "throw cannot claim complete transaction");
        Check(Property(Get(receipt, "Before"), "StableKey").ToString() != Property(Get(receipt, "After"), "StableKey").ToString(), "partial receipt records distinct before/after support");
        Check(((IEnumerable)Get(receipt, "TouchedLevelIds")).Cast<int>().SequenceEqual(new[] { 0 }), "partial receipt records touched inactive level");
        Equal(4L, Get(diggers[0], "curLevel"), "partial native exact write remains observable");
        Equal(false, Get(diggers[0], "active"), "native level failure occurs before funding");
        Equal(true, CallMain("SupportMutationStillOwned", receipt), "backend can adopt its own synchronous partial state");
        SetActiveBeards(0);
        Equal(false, CallMain("SupportMutationStillOwned", receipt), "manual beard edit prevents receipt adoption"); SetActiveBeards(4);
        var model = Get(Character, "diggers"); var originalRows = Get(model, "diggers");
        var newRows = (IList)Activator.CreateInstance(originalRows.GetType()); foreach (var row in (IList)originalRows) newRows.Add(row);
        Set(model, "diggers", newRows);
        Equal(false, CallMain("SupportMutationStillOwned", receipt), "identical-value foreign list replacement loses exact support object epoch"); Set(model, "diggers", originalRows);
        Set(diggers[0], "active", true);
        Equal(false, CallMain("SupportMutationStillOwned", receipt), "independent flag edit is part of exact journal state even without list change"); Set(diggers[0], "active", false);
        Set(diggers[11], "curLevel", 1L);
        Equal(false, CallMain("SupportMutationStillOwned", receipt), "foreign inactive-level edit prevents cleanup adoption"); Set(diggers[11], "curLevel", 0L);
        Set(Get(Character, "stats"), "rebirthNumber", 8L);
        Equal(false, CallMain("SupportMutationStillOwned", receipt), "receipt refuses foreign epoch"); Set(Get(Character, "stats"), "rebirthNumber", 7L);
        Equal(7, Get(pod, "id"), "throw path restores pod ID"); Equal("previous input", Property(input, "text"), "throw path restores pod text");
        CallMain("ApplyExactSupport", Selection(new int[0], 0, 3), 1d, true);
        ForeignPodRefresh = true; FailPodRefresh = true;
        Throws(() => CallMain("ApplyExactSupport", Selection(new int[0], 0, 4), 1d, true), "fixture pod", "foreign synchronous change still propagates native failure");
        receipt = Get(MainInstance, "_supportMutation");
        Equal(true, Get(receipt, "Interfered"), "journal detects native callback changing an unrelated inactive level");
        Equal(false, CallMain("SupportMutationStillOwned", receipt), "callback foreign writes cannot become owned cleanup state"); Set(diggers[11], "curLevel", 0L);
        CallMain("ApplyExactSupport", Selection(new int[0], 0, 3), 1d, true);
        ((IList)Get(all, "consumesGPS"))[0] = true; ((IList)Get(all, "baseGPSDrain"))[0] = 10d;
        DropGoldPodRefresh = true;
        Throws(() => CallMain("ApplyExactSupport", Selection(new int[0], 0, 4), 1d, true), "affordable", "late GPS loss fails exact funding before activation");
        Equal(false, Get(diggers[0], "active"), "late GPS loss cannot activate pinned candidate or resize it"); Equal(4L, Get(diggers[0], "curLevel"), "late GPS loss retains exact, unresized partial level");
        Set(Get(Character, "machine"), "realBaseGold", 400d);
        receipt = Get(MainInstance, "_supportMutation");
        CallMain("ApplyExactSupport", Selection(new int[0]), 1d, true);
        Equal(false, CallMain("SupportMutationStillOwned", receipt), "superseded transaction receipt cannot acquire a later identical support state");
        Equal(0, ((IList)Get(Get(Character, "beards"), "activeBeards")).Count, "empty exact beard selection is genuinely empty");
        Equal(0, ((IList)Get(Get(Character, "diggers"), "activeDiggers")).Count, "empty exact digger selection is genuinely empty");
        var active = (IList)Get(Get(Character, "diggers"), "activeDiggers"); active.Add(0); active.Add(0);
        Throws(() => CallMain("CaptureSupportContext"), "duplicate", "capture validates duplicate native active lists"); active.Clear();
        Set(diggers[0], "active", true); Throws(() => CallMain("CaptureSupportContext"), "flags", "capture validates native flag/list disagreement"); Set(diggers[0], "active", false);
        Set(Get(Character, "beards"), "disabled", true);
        Throws(() => CallMain("ApplyExactSupport", Selection(new[] { 4 }), 1d, true), "disabled", "disabled beard selection fails preflight"); Set(Get(Character, "beards"), "disabled", false);
        var pods = Get(all, "pods"); Set(all, "pods", null);
        receipt = Get(MainInstance, "_supportMutation"); stateKey = Property(CallMain("CaptureSupportState"), "StableKey");
        Throws(() => CallMain("ApplyExactSupport", Selection(new int[0], 0, 2), 1d, true), "controller", "missing native exact-input pod rejected before clear");
        Equal(stateKey, Property(CallMain("CaptureSupportState"), "StableKey"), "missing pod preflight does not mutate support");
        Check(ReferenceEquals(receipt, Get(MainInstance, "_supportMutation")), "missing pod does not publish new mutation receipt"); Set(all, "pods", pods);
        var claim = CallMain("BeginExecutionClaim", "foreign-support", "shared-support-negative", Domain("Support"), (Func<bool>)(() => true));
        receipt = Get(MainInstance, "_supportMutation"); stateKey = Property(CallMain("CaptureSupportState"), "StableKey");
        var writes = PodRefreshCalls;
        try
        {
            Throws(() => CallMain("ApplyExactSupport", Selection(new int[0]), 1d, true), "no longer authorized", "foreign support claim rejects before first write");
            Equal(stateKey, Property(CallMain("CaptureSupportState"), "StableKey"), "denied support grant preserves exact native support");
            Equal(writes, PodRefreshCalls, "denied support grant never reaches native input");
            var rejected = Get(MainInstance, "_supportMutation");
            Equal(false, Get(rejected, "Completed"), "denied support grant cannot publish completed support");
            Equal(stateKey, Property(Get(rejected, "After"), "StableKey"), "denied support journal records unchanged native state");
            Equal(0, ((IEnumerable)Get(rejected, "TouchedLevelIds")).Cast<int>().Count(), "denied support grant touches no native levels");
        }
        finally { Equal(true, CallMain("EndExecutionClaim", claim, false), "foreign support claim retires without historical writes"); }
        CallMain("ApplyExactSupport", Selection(new int[0]), 1d, true);
    }

    private static void CheckInactiveSupport()
    {
        var all = Get(Character, "allDiggers"); var diggers = (IList)Get(Get(Character, "diggers"), "diggers");
        var pod = ((IList)Get(all, "pods"))[0]; var input = Get(pod, "diggerLevelInput");
        var empty = Selection(new int[0]);
        CallMain("ApplyExactSupport", Selection(new int[0], 0, 3), 1d, true);
        Set(diggers[11], "curLevel", 7L); // An unrelated manual inactive-level edit is not a restoration request.
        var before = CallMain("CaptureSupportState"); var previousReceipt = Get(MainInstance, "_supportMutation");
        var refreshes = PodRefreshCalls; var gold = Get(Character, "realGold"); var recalls = Get(typeof(Program), "Recalls"); var swaps = Get(typeof(Program), "Swaps");
        var gps = Invoke(Character, "grossGoldPerSecond");
        var preflight = CallMain("PreflightExactSupport", empty, 0d, true, DiggerLevels(0, 0));
        Equal(true, Get(preflight, "Feasible"), "empty selection preflights an explicit inactive zero restoration");
        Equal(Property(before, "StableKey"), Property(CallMain("CaptureSupportState"), "StableKey"), "inactive preflight leaves all support state untouched");
        Equal(refreshes, PodRefreshCalls, "inactive preflight does not invoke native input/UI writes");
        Check(ReferenceEquals(previousReceipt, Get(MainInstance, "_supportMutation")), "inactive preflight does not replace the journal");
        CallMain("ApplyExactSupport", empty, 0d, true, DiggerLevels(0, 0));
        Equal(0L, Get(diggers[0], "curLevel"), "untouched native Int64 parser restores exact inactive zero");
        Equal(false, Get(diggers[0], "active"), "zero restoration does not activate the digger");
        var receipt = Get(MainInstance, "_supportMutation");
        Check(((IEnumerable)Get(receipt, "TouchedLevelIds")).Cast<int>().SequenceEqual(new[] { 0 }), "inactive zero write is included in touched-level journal");
        Equal(true, Get(receipt, "Completed"), "inactive restoration completes the same transaction");
        Equal(true, CallMain("SupportMutationStillOwned", receipt), "explicit restored inactive state can be adopted");
        Equal(7L, Get(diggers[11], "curLevel"), "unrequested foreign inactive level is not restored");
        Equal(17L, Get(diggers[1], "curLevel"), "other originally inactive levels are not restored automatically");
        Equal(0d, Invoke(all, "totalGPSDrain"), "inactive zero restoration has no native GPS drain");
        Equal(gps, Invoke(Character, "grossGoldPerSecond"), "inactive restoration does not change gross GPS");
        Equal(gps, Invoke(Character, "goldPerSecond"), "empty selection has no hidden inactive GPS cost");
        Check(PodRefreshCalls > refreshes, "inactive zero restoration actually used native input controller");
        Equal(7, Get(pod, "id"), "inactive zero write restores pod ID"); Equal("previous input", Property(input, "text"), "inactive zero write restores pod text");
        Equal(gold, Get(Character, "realGold"), "inactive input does not spend gold"); Equal(recalls, Get(typeof(Program), "Recalls"), "inactive input does not recall resources"); Equal(swaps, Get(typeof(Program), "Swaps"), "inactive input does not swap equipment");
        Set(Get(Character, "machine"), "realBaseGold", 0d);
        CallMain("ApplyExactSupport", empty, 0d, true, DiggerLevels(0, 100, 3, 0));
        Equal(100L, Get(diggers[0], "curLevel"), "nonselected nonzero level can be restored even when its active drain is unaffordable");
        Equal(false, Get(diggers[0], "active"), "nonzero extra input remains inactive");
        Equal(0d, Invoke(all, "totalGPSDrain"), "expensive inactive level has zero native drain at zero GPS");
        Equal(0d, Invoke(Character, "goldPerSecond"), "inactive nonzero restoration cannot create negative net GPS");
        Equal(0L, Get(diggers[3], "curLevel"), "zero is valid at a purchased maximum of zero");
        Check(new HashSet<int>(((IEnumerable)Get(Get(MainInstance, "_supportMutation"), "TouchedLevelIds")).Cast<int>()).SetEquals(new[] { 0, 3 }), "all explicit inactive inputs use the same bounded journal");
        Equal(7L, Get(diggers[11], "curLevel"), "foreign inactive edit survives multiple explicit restores");
        Set(Get(Character, "machine"), "realBaseGold", 400d);
        Action<object, Array, string> reject = (selection, levels, reason) => {
            var key = Property(CallMain("CaptureSupportState"), "StableKey"); var oldReceipt = Get(MainInstance, "_supportMutation"); var calls = PodRefreshCalls;
            Throws(() => CallMain("PreflightExactSupport", selection, 1d, true, levels), reason, "explicit inactive preflight rejects " + reason);
            Throws(() => CallMain("ApplyExactSupport", selection, 1d, true, levels), reason, "explicit inactive apply rejects before mutation: " + reason);
            Equal(key, Property(CallMain("CaptureSupportState"), "StableKey"), "rejected inactive request preserves ALL levels/selection/flags");
            Equal(calls, PodRefreshCalls, "rejected inactive request never invokes native input");
            Check(ReferenceEquals(oldReceipt, Get(MainInstance, "_supportMutation")), "rejected inactive request preserves previous receipt");
        };
        reject(Selection(new int[0], 1, 2), DiggerLevels(1, 0), "conflicts");
        reject(empty, DiggerLevels(0, 0, 0, 1), "unique");
        reject(empty, DiggerLevels(0, -1), "bounds");
        reject(empty, DiggerLevels(12, 0), "bounds");
        reject(empty, DiggerLevels(1, 101), "bounds");
        var pods = Get(all, "pods"); Set(all, "pods", null);
        reject(empty, DiggerLevels(0, 0), "controller"); Set(all, "pods", pods);
        Set(Get(Get(Character, "buttons"), "diggers"), "m_Interactable", false);
        reject(empty, DiggerLevels(0, 0), "unavailable"); Set(Get(Get(Character, "buttons"), "diggers"), "m_Interactable", true);
        FailPodRefresh = true;
        Throws(() => CallMain("ApplyExactSupport", empty, 1d, true, DiggerLevels(0, 4)), "fixture pod", "inactive native throw uses the shared partial journal path");
        receipt = Get(MainInstance, "_supportMutation");
        Equal(false, Get(receipt, "Completed"), "inactive native throw cannot claim completed restoration");
        Equal(4L, Get(diggers[0], "curLevel"), "partial inactive write retains exact observed native level");
        Equal(false, Get(diggers[0], "active"), "partial inactive write never reaches activation");
        Equal(true, CallMain("SupportMutationStillOwned", receipt), "backend can adopt its own partial inactive restoration");
        Equal(7L, Get(diggers[11], "curLevel"), "partial inactive failure preserves unrelated foreign level");
        Equal(7, Get(pod, "id"), "inactive throw restores pod ID"); Equal("previous input", Property(input, "text"), "inactive throw restores pod text");
        Set(diggers[11], "curLevel", 8L);
        Equal(false, CallMain("SupportMutationStillOwned", receipt), "later foreign edit blocks cleanup adoption after inactive restoration failure");
        CallMain("ApplyExactSupport", Selection(new int[0], 1, 2), 1d, true, DiggerLevels(0, 0));
        Equal(true, Get(diggers[1], "active"), "original selected digger is activated alongside explicit inactive restoration");
        Equal(2L, Get(diggers[1], "curLevel"), "original selected digger gets its exact level through the common setter");
        Equal(false, Get(diggers[0], "active"), "extra restored digger remains inactive alongside selected diggers");
        Equal(0L, Get(diggers[0], "curLevel"), "extra restored digger retains zero after selected activation");
        Equal(8L, Get(diggers[11], "curLevel"), "new foreign inactive value is not overwritten by explicit cleanup inputs");
        Check(new HashSet<int>(((IEnumerable)Get(Get(MainInstance, "_supportMutation"), "TouchedLevelIds")).Cast<int>()).SetEquals(new[] { 0, 1 }), "selected and inactive level writes share one receipt");
        CallMain("ApplyExactSupport", empty, 1d, true);
    }

    private static void CheckClearJournal()
    {
        var all = Get(Character, "allDiggers"); var rows = (IList)Get(Get(Character, "diggers"), "diggers");
        var empty = Selection(new int[0]);
        var receipt = CallMain("ApplyExactSupport", empty, 1d, true); Set(receipt, "Completed", false);
        Action foreignClear = () => {
            Invoke(all, "clearAllActiveDiggers"); Invoke(all, "activateDigger", 11);
            Equal(true, Get(rows[11], "active"), "negative control actually activates native digger 11 after clear");
            throw new InvalidOperationException("fixture foreign clear failure");
        };
        Throws(() => CallMain("WriteSupport", receipt, foreignClear, -1, -1, -1, true), "fixture foreign clear", "clear/activate/throw preserves primary failure");
        Equal(true, Get(receipt, "Interfered"), "clear receipt rejects a foreign activation even on exception observation");
        Equal(false, CallMain("SupportMutationStillOwned", receipt), "foreign activation after clear cannot be adopted as owned partial state");
        Invoke(all, "clearAllActiveDiggers");
        receipt = CallMain("ApplyExactSupport", empty, 1d, true);
        Action flagOnly = () => { Invoke(all, "clearAllActiveDiggers"); Set(rows[11], "active", true); throw new InvalidOperationException("fixture foreign flag failure"); };
        Throws(() => CallMain("WriteSupport", receipt, flagOnly, -1, -1, -1, true), "fixture foreign flag", "clear independently checks native flags");
        Equal(true, Get(receipt, "Interfered"), "empty active list cannot hide a foreign active flag after clear");
        Equal(false, CallMain("SupportMutationStillOwned", receipt), "flag-only foreign activation is not owned");
        Invoke(all, "clearAllActiveDiggers");
        var menu = Get(Character, "menuID"); Set(Character, "menuID", 44);
        try
        {
            receipt = CallMain("ApplyExactSupport", empty, 1d, true);
            PodInterference = p => {
                PodInterference = null; Invoke(all, "activateDigger", 11);
                throw new InvalidOperationException("fixture foreign native refresh failure");
            };
            Action clear = () => Invoke(all, "clearAllActiveDiggers");
            Throws(() => CallMain("WriteSupport", receipt, clear, -1, -1, -1, true), "foreign native refresh", "real native clear/refresh callback activates and throws");
            Equal(true, Get(receipt, "Interfered"), "native refresh foreign activation marks interference");
            Equal(false, CallMain("SupportMutationStillOwned", receipt), "native refresh foreign activation cannot be adopted");
            Invoke(all, "clearAllActiveDiggers");
            receipt = CallMain("ApplyExactSupport", empty, 1d, true); Set(receipt, "Completed", false);
            PodInterference = p => { PodInterference = null; throw new InvalidOperationException("fixture empty native refresh failure"); };
            Throws(() => CallMain("WriteSupport", receipt, clear, -1, -1, -1, true), "empty native refresh", "positive control throws after native clear emptied flags/lists");
            Equal(false, Get(receipt, "Interfered"), "normal cleared state remains owned after a refresh exception");
            Equal(true, CallMain("SupportMutationStillOwned", receipt), "normal clear/refresh throw with empty lists and flags is adoptable");
            CallMain("RestoreSupportPodDrafts", receipt);
        }
        finally { PodInterference = null; Set(Character, "menuID", menu); Invoke(all, "clearAllActiveDiggers"); }
    }

    private static void CheckPodDrafts()
    {
        var all = Get(Character, "allDiggers"); var rows = (IList)Get(Get(Character, "diggers"), "diggers");
        var pods = (IList)Get(all, "pods"); var pod = pods[0]; var input = Get(pod, "diggerLevelInput");
        var second = Invoke(typeof(Program), "Blank", pod.GetType()); Set(second, "character", Character); Set(second, "id", 2);
        var secondInput = Child(second, "diggerLevelInput"); Child(second, "tooltip"); pods.Add(second);
        var menu = Get(Character, "menuID"); var epoch = Get(Get(Character, "stats"), "rebirthNumber");
        ((IList)Get(all, "consumesGPS"))[0] = false;
        Set(Character, "menuID", 44);
        try
        {
            Set(input, "m_Text", "borrowed unsubmitted 999"); Set(secondInput, "m_Text", "other unsubmitted 888");
            var nativeWrites = NativePodTextWrites;
            Invoke(all, "clearAllActiveDiggers");
            Equal(Get(rows[7], "curLevel").ToString(), Property(input, "text"), "negative control native clear/refresh overwrites borrowed pod draft in menu 44");
            Equal(Get(rows[2], "curLevel").ToString(), Property(secondInput, "text"), "negative control native refresh overwrites nonborrowed pod draft");
            Check(NativePodTextWrites >= nativeWrites + 2, "native updateUI text assignment IL executed for both pods");
            Set(input, "m_Text", "borrowed unsubmitted 999"); Set(secondInput, "m_Text", "other unsubmitted 888");
            nativeWrites = NativePodTextWrites;
            CallMain("ApplyExactSupport", Selection(new int[0], 1, 9), 1d, true);
            Equal(9L, Get(rows[1], "curLevel"), "visible-menu exact level still goes through untouched native parser");
            Equal(true, Get(rows[1], "active"), "visible-menu exact native activation is retained");
            Equal(7, Get(pod, "id"), "whole transaction restores borrowed pod ID"); Equal(2, Get(second, "id"), "whole transaction preserves nonborrowed pod ID");
            Equal("borrowed unsubmitted 999", Property(input, "text"), "outer finally preserves draft captured BEFORE initial clear");
            Equal("other unsubmitted 888", Property(secondInput, "text"), "outer finally preserves nonborrowed draft AFTER final refresh");
            Check(NativePodTextWrites >= nativeWrites + 4, "actual native initial and final refresh text overwrites occurred");
            Set(input, "m_Text", "partial borrowed draft"); Set(secondInput, "m_Text", "partial other draft");
            FailPodRefresh = true;
            Throws(() => CallMain("ApplyExactSupport", Selection(new int[0], 0, 4), 1d, true), "fixture pod", "visible-menu native failure preserves primary exception");
            var receipt = Get(MainInstance, "_supportMutation");
            Equal(4L, Get(rows[0], "curLevel"), "partial visible-menu native exact level remains journaled");
            Equal(7, Get(pod, "id"), "partial failure restores borrowed ID"); Equal(2, Get(second, "id"), "partial failure preserves nonborrowed ID");
            Equal("partial borrowed draft", Property(input, "text"), "partial failure restores pre-clear borrowed draft");
            Equal("partial other draft", Property(secondInput, "text"), "partial failure restores pre-clear nonborrowed draft");
            Equal(true, CallMain("SupportMutationStillOwned", receipt), "ordinary partial failure with restored drafts remains adoptable");
            PodInterference = p => {
                if (!ReferenceEquals(p, pod) || (int)Get(p, "id") != 0) return;
                PodInterference = null; Set(input, "m_Text", "foreign during setID");
            };
            Throws(() => CallMain("ApplyExactSupport", Selection(new int[0], 0, 5), 1d, true), "native controller selection", "foreign setID callback draft is checked before assigning exact input");
            Equal("foreign during setID", Property(input, "text"), "native controller selection cannot overwrite a known foreign draft");
            Equal(4L, Get(rows[0], "curLevel"), "foreign setID draft is never submitted as a level");
            Set(pod, "id", 7); Set(input, "m_Text", "partial borrowed draft");
            InputInterference = (field, text) => {
                if (!ReferenceEquals(field, input) || text != "5") return;
                InputInterference = null; Set(input, "m_Text", "foreign before submission");
            };
            Throws(() => CallMain("ApplyExactSupport", Selection(new int[0], 0, 5), 1d, true), "before native submission", "InputField callback draft is checked before native parsing");
            Equal("foreign before submission", Property(input, "text"), "exact input assignment cannot replace a later callback's foreign draft");
            Equal(4L, Get(rows[0], "curLevel"), "foreign InputField callback text is never parsed or activated");
            Set(pod, "id", 7); Set(input, "m_Text", "partial borrowed draft");
            PodInterference = p => {
                if (!ReferenceEquals(p, pod) || (int)Get(p, "id") != 0 || (long)Get(rows[0], "curLevel") != 5) return;
                PodInterference = null; Set(input, "m_Text", "foreign typed 555");
            };
            Throws(() => CallMain("ApplyExactSupport", Selection(new int[0], 0, 5), 1d, true), "foreign support mutation", "foreign draft during borrowed native input stops ownership");
            Equal("foreign typed 555", Property(input, "text"), "per-setter and outer finally must not overwrite known new foreign text");
            Equal("partial other draft", Property(secondInput, "text"), "unaffected nonborrowed draft is still safely restored");
            Equal(false, CallMain("SupportMutationStillOwned", Get(MainInstance, "_supportMutation")), "foreign draft cannot produce an adoptable receipt");
            Set(pod, "id", 7); Set(input, "m_Text", "new borrowed draft"); Set(secondInput, "m_Text", "old nonborrowed draft");
            PodInterference = p => {
                if (!ReferenceEquals(p, second)) return;
                PodInterference = null; Set(secondInput, "m_Text", "foreign nonborrowed draft");
            };
            Throws(() => CallMain("ApplyExactSupport", Selection(new int[0], 1, 10), 1d, true), "foreign support mutation", "foreign draft in an unborrowed refreshed pod is detected");
            Equal("foreign nonborrowed draft", Property(secondInput, "text"), "outer finally does not overwrite foreign nonborrowed draft");
            Equal("new borrowed draft", Property(input, "text"), "other still-owned pod retains its pre-clear draft");
            Check(!((IEnumerable)Get(Get(MainInstance, "_supportMutation"), "TouchedLevelIds")).Cast<int>().Any(), "foreign initial-refresh edit stops before exact level writes");
            object replacement = null;
            PodInterference = p => {
                if (!ReferenceEquals(p, pod) || (int)Get(p, "id") != 0 || (long)Get(rows[0], "curLevel") != 6) return;
                PodInterference = null; replacement = Child(pod, "diggerLevelInput"); Set(replacement, "m_Text", "replacement context draft");
                throw new InvalidOperationException("fixture input context replacement");
            };
            Throws(() => CallMain("ApplyExactSupport", Selection(new int[0], 0, 6), 1d, true), "input context replacement", "replaced input objects preserve the primary failure");
            Equal("replacement context draft", Property(replacement, "text"), "outer finally never writes into replaced input object");
            Equal("6", Property(input, "text"), "outer finally does not restore into old detached input context");
            Set(pod, "diggerLevelInput", input); Set(pod, "id", 7); Set(input, "m_Text", "epoch original draft");
            PodInterference = p => {
                if (!ReferenceEquals(p, pod) || (int)Get(p, "id") != 0 || (long)Get(rows[0], "curLevel") != 7) return;
                PodInterference = null; Set(Get(Character, "stats"), "rebirthNumber", (long)epoch + 1);
                throw new InvalidOperationException("fixture pod epoch replacement");
            };
            Throws(() => CallMain("ApplyExactSupport", Selection(new int[0], 0, 7), 1d, true), "epoch replacement", "epoch change preserves primary failure");
            Equal("7", Property(input, "text"), "outer finally never restores a draft into a replaced epoch");
            Equal(0, Get(pod, "id"), "lost epoch skips even defensive borrowed-pod ID writes");
            Set(Get(Character, "stats"), "rebirthNumber", epoch); Set(pod, "id", 7);
            Set(input, "m_Text", "primary failure draft"); Set(secondInput, "m_Text", "restore remaining pod");
            FailPodRefresh = true;
            InputInterference = (field, text) => {
                if (!ReferenceEquals(field, input) || text != "primary failure draft") return;
                InputInterference = null; throw new InvalidOperationException("fixture secondary draft restoration failure");
            };
            try { CallMain("ApplyExactSupport", Selection(new int[0], 0, 4), 1d, true); throw new Exception("missing primary failure"); }
            catch (InvalidOperationException error)
            {
                Check(error.Message.Contains("fixture pod") && !error.ToString().Contains("secondary draft restoration"), "outer draft-restoration failure cannot mask primary native failure");
            }
            Equal("restore remaining pod", Property(secondInput, "text"), "outer restoration continues through other pods after one input setter throws");
            Equal(7, Get(pod, "id"), "primary plus restoration failure retains restored pod ID");
            CallMain("ApplyExactSupport", Selection(new int[0]), 1d, true, DiggerLevels(0, 3));
            Set(input, "m_Text", "ID restoration original draft"); Set(secondInput, "m_Text", "ID restoration other draft");
            var idRestoreFailed = false;
            PodInterference = p => {
                if (!ReferenceEquals(p, pod) || (int)Get(p, "id") != 7 || (long)Get(rows[0], "curLevel") != 4) return;
                PodInterference = null; idRestoreFailed = true;
                throw new InvalidOperationException("fixture primary ID restoration failure");
            };
            InputInterference = (field, text) => {
                if (!idRestoreFailed || !ReferenceEquals(field, input) || text != "0") return;
                InputInterference = null; throw new InvalidOperationException("fixture secondary setter text failure");
            };
            try { CallMain("ApplyExactSupport", Selection(new int[0], 0, 4), 1d, true); throw new Exception("missing ID restoration failure"); }
            catch (InvalidOperationException error)
            {
                Check(error.Message.Contains("primary ID restoration") && !error.ToString().Contains("secondary setter text"), "defensive per-setter text cleanup cannot mask an earlier native ID restoration failure");
            }
            Equal("ID restoration original draft", Property(input, "text"), "authoritative outer finally restores draft after defensive setter cleanup failures");
            Equal("ID restoration other draft", Property(secondInput, "text"), "other pod draft survives double defensive cleanup failure");
        }
        finally
        {
            PodInterference = null; InputInterference = null; FailPodRefresh = false;
            Set(Character, "menuID", menu); Set(Get(Character, "stats"), "rebirthNumber", epoch);
            Set(pod, "diggerLevelInput", input); Set(pod, "id", 7); Set(input, "m_Text", "previous input"); pods.Remove(second);
            Invoke(all, "clearAllActiveDiggers");
        }
    }

    private static void CheckLazyItopod()
    {
        // A new isolated object graph avoids inheriting the preceding fault-injection fixtures.
        Invoke(typeof(Program), "Setup");
        Character = Get(typeof(Program), "Character"); Controller = Get(typeof(Program), "Controller"); MainInstance = Get(typeof(Program), "MainInstance");
        PrepareSupport(); SetActiveBeards(3, 6);
        var adventure = Get(Character, "adventure"); var controller = Get(Character, "adventureController");
        var arbitrary = Get(Character, "arbitrary"); var perks = Get(controller, "itopod"); var model = Get(adventure, "itopod");
        Set(Get(Get(Character, "inventory"), "itemList"), "itopodKeyComplete", true);
        Set(Get(Get(Character, "buttons"), "adventure"), "m_Interactable", true);
        Set(adventure, "highestItopodLevel", 60); Set(adventure, "autoattacking", true);
        Set(Get(Character, "NGU"), "autoAdvance", false);
        Set(perks, "character", Character); Sequence(model, "perkLevel", 100, 0L);
        var enemies = ListField(controller, "itopodEnemyList", 1); Set(enemies[0], "maxHP", 600f); Set(enemies[0], "defense", 10f);
        var player = Child(controller, "playerController"); var ai = Child(controller, "enemyAI"); Set(ai, "player", player);
        Child(controller, "log"); Child(controller, "itopodStartInput"); Child(controller, "itopodEndInput");
        var selector = Child(controller, "zoneSelector"); Set(selector, "character", Character); Set(selector, "ac", controller);
        var recalls = Get(typeof(Program), "Recalls"); var swaps = Get(typeof(Program), "Swaps");
        var settings = Get(MainType, "Settings"); var autoRebirth = Property(settings, "AutoRebirth");
        var support = Property(CallMain("CaptureSupportState"), "StableKey");
        PresetRespawnSeconds = 100;
        try
        {
            Set(arbitrary, "boughtLazyITOPOD", true); Set(arbitrary, "lazyITOPODOn", false);
            var args = JsonParse("{\"preset\":{\"Id\":\"lazy-itopod\",\"Name\":\"Lazy ITOPOD\",\"Weights\":[0,0,0,1,0,0],\"SupportMode\":\"current\",\"HorizonSeconds\":1000,\"SwapSeconds\":0}}");
            var preview = CallMain("PreviewLoadoutPreset", args);
            var cached = Get(MainInstance, "_loadoutPresetPreview"); var snapshot = Get(Get(cached, "Capture"), "Snapshot");
            var policy = ((Array)Get(Get(cached, "Plan"), "Candidates")).GetValue(1);
            var nativeFloor = Math.Min((int)Invoke(Character, "calculateBestItopodLevel"), 59);
            var oldAttack = Get(adventure, "attack");
            foreach (var attack in new[] { 600f, 700f, 1000f, 5000f, 1e20f })
            {
                Set(adventure, "attack", attack);
                var native = Math.Max(0, Math.Min((int)Invoke(Character, "calculateBestItopodLevel"), 59));
                Equal(native, Planner("ItopodFloor", Convert.ToDouble(Invoke(Character, "totalAdvAttack")), Convert.ToDouble(Invoke(Character, "idleAttackPower")), 59),
                    "projected shifter floor matches untouched native getter, including low-power and repeatable-floor clamps");
            }
            Set(adventure, "attack", oldAttack);
            Equal(true, Get(snapshot, "LazyItopodPurchased"), "capture retains actual native shifter purchase flag");
            Equal(false, Get(snapshot, "LazyItopodEnabled"), "capture retains actual disabled shifter preference");
            Check((int)Get(policy, "Floor") != nativeFloor && (double)Get(policy, "Hits") > 1,
                "native PP/enemy fixture prefers a multihit floor different from the actual native shifter optimum");
            Equal("false", Json(preview, "lazyItopodShifterActive").ToString(), "API distinguishes purchased but disabled shifter");
            var apply = JsonParse(args.ToString().Substring(0, args.ToString().Length - 1) + ",\"previewId\":" + Json(preview, "previewId") + ",\"candidateKey\":\"balanced\"}");
            Set(arbitrary, "lazyITOPODOn", true);
            var fresh = CallMain("CaptureLoadoutPreset");
            Check(!Equals(Get(Get(cached, "Capture"), "Material"), Get(fresh, "Material")), "enabled shifter toggle changes actual capture material fingerprint");
            Throws(() => CallMain("ApplyLoadoutPreset", apply, null), "Lazy ITOPOD shifter", "toggle between Preview and Apply fails with an explicit preference-change explanation");
            Check(ReferenceEquals(cached, Get(MainInstance, "_loadoutPresetPreview")), "shifter toggle rejection preserves the unconsumed preview token");
            Equal(true, Get(arbitrary, "lazyITOPODOn"), "failed Apply never disables the user's shifter");
            Set(arbitrary, "lazyITOPODOn", false); Set(arbitrary, "boughtLazyITOPOD", false);
            fresh = CallMain("CaptureLoadoutPreset");
            Check(!Equals(Get(Get(cached, "Capture"), "Material"), Get(fresh, "Material")), "purchase flag independently changes material even while shifter is disabled");
            Throws(() => CallMain("ApplyLoadoutPreset", apply, null), "Lazy ITOPOD shifter", "purchase change between Preview and Apply also rejects before mutations");

            Set(arbitrary, "boughtLazyITOPOD", true); Set(arbitrary, "lazyITOPODOn", true);
            preview = CallMain("PreviewLoadoutPreset", args); cached = Get(MainInstance, "_loadoutPresetPreview");
            var activeSnapshot = Get(Get(cached, "Capture"), "Snapshot");
            var activePolicy = ((Array)Get(Get(cached, "Plan"), "Candidates")).GetValue(1);
            Equal(true, Property(activeSnapshot, "LazyItopodActive"), "captured purchased/enabled conjunction activates native routing constraint");
            Equal(nativeFloor, Get(activePolicy, "Floor"), "active-shifter forecast uses the actual native getter optimum instead of the incompatible multihit winner");
            Equal("true", Json(preview, "lazyItopodShifterActive").ToString(), "API exposes the active native route constraint");
            Check(Json(preview, "warnings").ToString().Contains("Lazy ITOPOD shifter"), "preview explains native post-kill floor changes without claiming sustained ownership");
            apply = JsonParse(args.ToString().Substring(0, args.ToString().Length - 1) + ",\"previewId\":" + Json(preview, "previewId") + ",\"candidateKey\":\"balanced\"}");
            var bonuses = (IList)Get(Get(Character, "inventory"), "macguffinBonuses"); var oldBonus = bonuses[19]; bonuses[19] = 1.2f;
            Check((int)Invoke(Character, "calculateBestItopodLevel") != nativeFloor, "passive multiplier negative control moves native shifter optimum");
            Throws(() => CallMain("ApplyLoadoutPreset", apply, null), "Lazy ITOPOD shifter", "exact pinned policy rejects passive power moving the native optimum instead of replaying a replacement floor");
            bonuses[19] = oldBonus;
            Check(ReferenceEquals(cached, Get(MainInstance, "_loadoutPresetPreview")), "incompatible replay preserves token until successful preflight");
            Equal(recalls, Get(typeof(Program), "Recalls"), "shifter incompatibility never recalls resources");
            Equal(swaps, Get(typeof(Program), "Swaps"), "shifter incompatibility never equips gear");
            Equal(support, Property(CallMain("CaptureSupportState"), "StableKey"), "shifter incompatibility never changes support");
            Equal(autoRebirth, Property(settings, "AutoRebirth"), "shifter incompatibility never pauses auto-rebirth");
            Check(Get(MainInstance, "_loadoutPresetSnapshot") == null, "failed preflight installs no one-shot runtime marker or sustained owner");

            var writer = Plugin.GetType("NGUInjector.Managers.ResourceWriter", true);
            var current = (IDictionary)Invoke(writer, "CaptureAllocations", Character);
            var intent = Activator.CreateInstance(Plugin.GetType("NGUInjector.Managers.ResourceSetupIntent", true), true);
            var desired = (IDictionary)Invoke(writer, "EmptyDesiredMap", true, true, false);
            foreach (var key in desired.Keys.Cast<object>().ToArray()) desired[key] = ((long[])current[key]).ToArray();
            var coordinator = MainInstance;
            Set(intent, "Owner", "native-route-fixture"); Set(intent, "Desired", desired); Set(intent, "Energy", true); Set(intent, "Magic", true);
            Set(intent, "Authorized", (Func<bool>)(() => ReferenceEquals(Get(MainType, "reference"), coordinator)));
            Equal(true, Property(CallMain("ReconcileStageResourceSetup", intent), "Verified"), "native route leaf starts from verified exact current resource intent");
            Func<bool> routeAuthority = () => ReferenceEquals(Get(MainType, "reference"), coordinator);
            foreach (var flags in new[] { new[] { false, false }, new[] { true, false }, new[] { false, true } })
            {
                Set(arbitrary, "boughtLazyITOPOD", flags[0]); Set(arbitrary, "lazyITOPODOn", flags[1]);
                CallMain("ApplyLoadoutPresetRoute", policy, snapshot, routeAuthority);
                Equal(1000, Property(controller, "zone"), "native zone-entry slice updates the controller's routing state, not just saved zone");
                var enemy = Child(controller, "currentEnemy"); Set(enemy, "enemyType", Enum.Parse(Field(enemy, "enemyType").FieldType, "itopod")); Set(enemy, "maxHP", 600f);
                var progress = (long)Get(model, "pointProgress");
                Invoke(controller, "enemyDeath");
                Check((long)Get(model, "pointProgress") > progress, "complete native kill executes ITOPOD progress before its post-kill shifter guard");
                Equal(Get(policy, "Floor"), Get(controller, "itopodLevel"), "native kill preserves fixed floor unless both shifter flags are true");
            }
            Set(arbitrary, "boughtLazyITOPOD", true); Set(arbitrary, "lazyITOPODOn", true);
            Set(Get(controller, "itopodStartInput"), "m_Text", "unchanged start"); Set(Get(controller, "itopodEndInput"), "m_Text", "unchanged end");
            Throws(() => CallMain("ApplyLoadoutPresetRoute", policy, snapshot, routeAuthority), "Lazy ITOPOD shifter", "defensive route guard refuses incompatible fixed floor before writing floor inputs");
            Equal("unchanged start", Property(Get(controller, "itopodStartInput"), "text"), "incompatible route guard preserves native start input");
            Equal("unchanged end", Property(Get(controller, "itopodEndInput"), "text"), "incompatible route guard preserves native end input");
            var control = Child(controller, "currentEnemy"); Set(control, "enemyType", Enum.Parse(Field(control, "enemyType").FieldType, "itopod")); Set(control, "maxHP", 600f);
            Invoke(controller, "enemyDeath");
            Equal(nativeFloor, Get(controller, "itopodLevel"), "negative control: one complete native kill really replaces the old incompatible multihit floor");
            CallMain("ApplyLoadoutPresetRoute", activePolicy, activeSnapshot, routeAuthority);
            for (var kill = 0; kill < 11; kill++)
            {
                var enemy = Child(controller, "currentEnemy"); Set(enemy, "enemyType", Enum.Parse(Field(enemy, "enemyType").FieldType, "itopod")); Set(enemy, "maxHP", 600f);
                Invoke(controller, "enemyDeath");
                Equal(nativeFloor, Get(controller, "itopodLevel"), "native kill retains compatible preview floor across repeated kills");
                Equal(nativeFloor, Get(adventure, "itopodStart"), "native shifter start remains the forecast floor");
                Equal(nativeFloor, Get(adventure, "itopodEnd"), "native shifter end remains the forecast floor");
            }
            Equal(true, Get(arbitrary, "boughtLazyITOPOD"), "one-shot route never changes native purchase state");
            Equal(true, Get(arbitrary, "lazyITOPODOn"), "native kills and one-shot route leave shifter enabled");
            Equal(recalls, Get(typeof(Program), "Recalls"), "post-kill route checks do not add resource ownership or recalls");
        }
        finally { PresetRespawnSeconds = 4; }
    }

    private static void CheckAdventureFloatParity()
    {
        Invoke(typeof(Program), "Setup");
        Character = Get(typeof(Program), "Character"); Controller = Get(typeof(Program), "Controller"); MainInstance = Get(typeof(Program), "MainInstance");
        PrepareSupport(); SetActiveBeards(3, 5, 6);
        var adventure = Get(Character, "adventure");
        var nearValues = new[] { 1000f, 1102654.875f, 1102655f };
        var nativeValues = new[] { 2100d, 2315575.25, 2315575.5 };
        for (var i = 0; i < nearValues.Length; i++)
        {
            var basis = nearValues[i];
            Set(adventure, "attack", basis);
            var captured = CallMain("CaptureLoadoutPresetAdventure", true);
            Equal(nativeValues[i], Convert.ToDouble(Invoke(Character, "totalAdvAttack")), "untouched native near-report float reference " + i);
            Equal(nativeValues[i], Planner("AdventureStat", (double)basis, 0d, 0d, captured, 1d, double.NaN), "forward projection exactly matches near-report float reference " + i);
            var full = CallMain("CaptureLoadoutPreset");
            Equal(nativeValues[i], Get(Get(full, "Snapshot"), "Power"), "full capture succeeds without relaxing non-adventure parity or inverting clamped totals");
        }
        for (var offset = -16; offset <= 16; offset++)
        {
            var basis = 1102654.875f + offset * .125f; Set(adventure, "attack", basis);
            Equal(Convert.ToDouble(Invoke(Character, "totalAdvAttack")), Planner("AdventureStat", (double)basis, 0d, 0d, CallMain("CaptureLoadoutPresetAdventure", true), 1d, double.NaN),
                "exact adjacent-input native reference near 2.315-million power; offset=" + offset);
        }
        var inventory = Get(Character, "inventory"); var weapon = Get(inventory, "weapon");
        var beards = (IList)Get(Get(Character, "beards"), "beards");
        var diggers = (IList)Get(Get(Character, "diggers"), "diggers"); var allDiggers = Get(Character, "allDiggers");
        Set(diggers[3], "maxLevel", 100L);
        var random = new Random(214640);
        var inputs = new[] { "AllAdvancedTraining.adventurePowerBonus", "AllAdvancedTraining.adventureToughnessBonus", "AllNGUController.adventureBonus", "AllNGUController.adventureBonus2",
            "AllChallengesController.adventureBonus", "ItopodPerkController.totalAdventureBonus", "BeastQuestPerkController.totalAdventureBonus", "HacksController.totalAdventureBonus",
            "WishesController.totalAdventureBonus", "CardsController.getBonus" };
        var exact = 0; var maxUlps = 0; var legacyRejected = 0; var fullCaptures = 0; var above = 0; var below = 0;
        var regenExact = 0; var regenMaxUlps = 0;
        var recalls = Get(typeof(Program), "Recalls"); var swaps = Get(typeof(Program), "Swaps");
        try
        {
            for (var fixture = 0; fixture < 600; fixture++)
            {
                foreach (var key in inputs) AdventureInputs[key] = (float)(key.StartsWith("AllAdvancedTraining") ? random.NextDouble() * 3 : 1 + random.NextDouble() * .7);
                var basis = fixture % 4 == 0 ? (float)(1102654.875 + (fixture - 300) * .125) : (float)(random.NextDouble() * 20000 + .125);
                Set(adventure, "attack", basis); Set(adventure, "defense", (float)(basis * .754321));
                Set(adventure, "regen", fixture % 3 == 0 ? .1234567f : (float)(fixture * 17.1234567));
                Set(weapon, "curAttack", (float)random.Next(0, 20000)); Set(weapon, "curDefense", (float)random.Next(0, 18000));
                foreach (var armor in new[] { "head", "chest", "legs", "boots" }.Select(name => Get(inventory, name)).Concat(((IList)Get(inventory, "accs")).Cast<object>()))
                { Set(armor, "curAttack", (float)(random.NextDouble() * 10000)); Set(armor, "curDefense", (float)(random.NextDouble() * 9000)); }
                var threshold = (float)(basis + Convert.ToSingle(Invoke(Controller, "adventureAttackBonus")));
                Set(inventory, "cubePower", fixture % 3 == 0 ? 0f : fixture % 3 == 1 ? threshold : (float)(threshold * 100 + .125));
                Set(inventory, "cubeToughness", (float)(random.NextDouble() * threshold * 10));
                Set(beards[5], "beardLevel", (long)random.Next(0, 1001)); Set(beards[5], "permLevel", (long)random.Next(0, 1001));
                SetActiveBeards(fixture % 2 == 0 ? new[] { 3, 5, 6 } : new[] { 3, 6 });
                var active = (IList)Get(Get(Character, "diggers"), "activeDiggers"); active.Clear();
                Set(diggers[3], "active", fixture % 3 == 0); Set(diggers[3], "curLevel", 1L + fixture % 19);
                if (fixture % 3 == 0) active.Add(3);
                ((IList)Get(allDiggers, "startingBoost"))[3] = .123456789d; ((IList)Get(allDiggers, "boostPerLevel"))[3] = .000123456789d;
                ((IList)Get(inventory, "macguffinBonuses"))[19] = (float)(1 + random.NextDouble() * .5);
                Set(Get(inventory, "itemList"), "evilBonusAccComplete", fixture % 2 == 0); Set(adventure, "beastModeOn", fixture % 5 == 0);
                var gearSnapshot = New("PresetSnapshot");
                Invoke(Plugin.GetType("NGUInjector.Managers.GearOptimizer", true), "CapturePresetGear", gearSnapshot);
                var totals = (double[])Planner("Totals", gearSnapshot, Get(gearSnapshot, "Current"));
                foreach (var power in new[] { true, false })
                {
                    var factors = (double[])CallMain("CaptureLoadoutPresetAdventure", power);
                    var gear = Convert.ToDouble(Invoke(Controller, power ? "adventureAttackBonus" : "adventureDefenseBonus"));
                    Equal(gear, totals[power ? 0 : 1], "owned multi-slot gear capture/total matches native inventory getter in fixture " + fixture + "/" + power);
                    var rawBase = Convert.ToDouble(Get(adventure, power ? "attack" : "defense"));
                    var cube = Convert.ToDouble(Get(inventory, power ? "cubePower" : "cubeToughness"));
                    var projected = Planner("AdventureStat", rawBase, cube, gear, factors, factors[4], double.NaN);
                    var expected = Convert.ToDouble(Invoke(Character, power ? "totalAdvAttack" : "totalAdvDefense"));
                    var ulps = Math.Abs(BitConverter.ToInt32(BitConverter.GetBytes((float)expected), 0) - BitConverter.ToInt32(BitConverter.GetBytes((float)(double)projected), 0));
                    if (ulps == 0) exact++; maxUlps = Math.Max(maxUlps, ulps);
                    if ((double)projected > expected) above++; else if ((double)projected < expected) below++;
                    Check((bool)Planner("AdventureParity", projected, expected), "bounded native adventure parity fixture " + fixture + "/" + power + "; ULP=" + ulps);
                    var sum = (float)((float)rawBase + (float)gear);
                    var legacyCube = (float)cube > sum ? (float)(sum + (float)Math.Pow((float)((float)cube - sum), .5)) : (float)cube;
                    var old = (float)(sum + legacyCube);
                    for (var i = 0; i < factors.Length; i++) old = (float)(old * (float)factors[i]);
                    if (!(bool)Planner("AdventureParity", (double)old, expected)) legacyRejected++;
                    var wrong = factors.ToArray(); wrong[2] = 1;
                    Check(!(bool)Planner("AdventureParity", Planner("AdventureStat", rawBase, cube, gear, wrong, wrong[4], double.NaN), expected),
                        "ULP gate does not hide an omitted NGU multiplier in fixture " + fixture + "/" + power);
                }
                var toughnessFactors = (double[])CallMain("CaptureLoadoutPresetAdventure", false);
                var regen = (double)Planner("AdventureRegen", Convert.ToDouble(Get(adventure, "defense")), Convert.ToDouble(Get(adventure, "regen")),
                    Convert.ToDouble(Get(inventory, "cubeToughness")), totals[1], toughnessFactors);
                var nativeRegen = Convert.ToDouble(Invoke(Character, "totalAdvHPRegen"));
                var regenUlps = Math.Abs(BitConverter.ToInt32(BitConverter.GetBytes((float)nativeRegen), 0) - BitConverter.ToInt32(BitConverter.GetBytes((float)regen), 0));
                if (regenUlps == 0) regenExact++; regenMaxUlps = Math.Max(regenMaxUlps, regenUlps);
                Check((bool)Planner("AdventureRegenParity", regen, nativeRegen), "native totalAdvHPRegen parity fixture " + fixture + "; ULP=" + regenUlps);
                var wrongRegenFactors = toughnessFactors.ToArray(); wrongRegenFactors[2] = 1;
                Check(!(bool)Planner("AdventureRegenParity", Planner("AdventureRegen", Convert.ToDouble(Get(adventure, "defense")), Convert.ToDouble(Get(adventure, "regen")),
                    Convert.ToDouble(Get(inventory, "cubeToughness")), totals[1], wrongRegenFactors), nativeRegen), "regen parity cannot hide omitted NGU factor " + fixture);
                if (fixture % 20 == 0) { CallMain("CaptureLoadoutPreset"); fullCaptures++; }
            }
        }
        finally { AdventureInputs.Clear(); }
        Check(exact > 1000 && maxUlps <= 7 && legacyRejected > 0 && fullCaptures == 30 && above > 0 && below > 0,
            "broad native references include exact matches, bounded runtime rounding and rejected legacy cube/rounding errors");
        var bits = BitConverter.ToInt32(BitConverter.GetBytes(2315575.25f), 0);
        Check(!(bool)Planner("AdventureParity", (double)BitConverter.ToSingle(BitConverter.GetBytes(bits + 8), 0), 2315575.25), "eight ULPs fail the adventure-only parity gate");
        Check(!(bool)Planner("AdventureParity", 1.0000001192092896, 1d) && !(bool)Planner("AdventureParity", (double)1e36f, (double)BitConverter.ToSingle(BitConverter.GetBytes(BitConverter.ToInt32(BitConverter.GetBytes(1e36f), 0) - 1), 0)),
            "rounding allowance never absorbs minimum/maximum clamp transitions");
        Check(!(bool)Planner("AdventureParity", double.NaN, double.NaN) && !(bool)Planner("AdventureParity", double.PositiveInfinity, double.PositiveInfinity)
            && !(bool)Planner("AdventureParity", 0d, 0d), "nonfinite and invalid zero adventure totals fail closed");
        Equal(recalls, Get(typeof(Program), "Recalls"), "native precision checks never recall resources");
        Equal(swaps, Get(typeof(Program), "Swaps"), "native precision checks never equip game gear");
        Console.WriteLine("Native adventure precision: 1200 cases, " + exact + " exact, maximum " + maxUlps + " ULP; above=" + above + ", below=" + below + "; 30 full captures");
        Console.WriteLine("Native regen precision: 600 cases, " + regenExact + " exact, maximum " + regenMaxUlps + " ULP");
    }

    private static void CheckNativeTitanRequirements()
    {
        Invoke(typeof(Program), "Setup");
        Character = Get(typeof(Program), "Character"); Controller = Get(typeof(Program), "Controller"); MainInstance = Get(typeof(Program), "MainInstance");
        PrepareSupport(); SetActiveBeards(); Set(Get(Character, "beards"), "disabled", true);
        var adventure = Get(Character, "adventure"); var inventory = Get(Character, "inventory"); var settings = Get(Character, "settings");
        var controller = Get(Character, "adventureController"); var helpers = Plugin.GetType("NGUInjector.Managers.ZoneHelpers", true);
        Set(Character, "bossID", 1000); Set(settings, "autoKillTitans", true);
        Set(Get(inventory, "itemList"), "evilBonusAccComplete", false);
        Set(adventure, "beastModeOn", false); Set(adventure, "boss5Kills", 3);
        Sequence(Get(inventory, "itemList"), "itemMaxxed", 136, false);
        ((IList)Get(Get(inventory, "itemList"), "itemMaxxed"))[135] = true;
        foreach (var item in new[] { "weapon", "head", "chest", "legs", "boots", "weapon2" }.Select(name => Get(inventory, name)).Concat(((IList)Get(inventory, "accs")).Cast<object>()))
        { Set(item, "curAttack", 0f); Set(item, "curDefense", 0f); }
        Set(inventory, "cubePower", 0f); Set(inventory, "cubeToughness", 0f);
        ((IList)Get(inventory, "macguffinBonuses"))[19] = 1f;
        var bestiary = ListField(Child(Character, "bestiary"), "enemies", 377);
        foreach (var enemy in bestiary) Set(enemy, "kills", Convert.ChangeType(0, Field(enemy, "kills").FieldType));
        var before = Assertions;
        foreach (var value in new[] { 0f, .1f, BitConverter.ToSingle(BitConverter.GetBytes(BitConverter.ToInt32(BitConverter.GetBytes(.1f), 0) + 1), 0), .5f, 1e36f, float.PositiveInfinity, float.NaN })
        {
            Set(adventure, "regen", value);
            var actual = Convert.ToDouble(Invoke(Character, "totalAdvHPRegen"));
            Equal(actual, Planner("AdventureRegen", Convert.ToDouble(Get(adventure, "defense")), (double)value, 0d, 0d, CallMain("CaptureLoadoutPresetAdventure", false)), "exact native regen min/max/NaN control " + value);
        }
        for (var boss = 0; boss < 12; boss++)
        for (var version = boss < 5 ? 0 : 1; version <= (boss < 5 ? 0 : 4); version++)
        {
            if (boss >= 5) { Set(adventure, "titan" + (boss + 1) + "Unlocked", true); Set(adventure, "titan" + (boss + 1) + "Version", version - 1); }
            var native = NativeTitanRequirements[boss + ":" + version];
            var requirements = (float[])Invoke(helpers, "TitanAutokillRequirements", boss);
            Check(requirements != null && requirements.Length == 3 && native.Length == 3, "available native requirements shape " + boss + "/" + version);
            for (var dimension = 0; dimension < 3; dimension++)
            {
                var expected = native[dimension];
                if (boss < 3 && dimension < 2) expected = BitConverter.ToSingle(BitConverter.GetBytes(BitConverter.ToInt32(BitConverter.GetBytes(expected), 0) + 1), 0);
                Equal(expected, requirements[dimension], "table matches actual native ldc.r4 " + boss + "/" + version + "/" + dimension);
            }
            if (boss < 5) continue; // Early native thresholds are in the side-effectful manageFight; IL checked above.
            var method = "autokillTitan" + (boss + 1) + "V" + version + "Achieved";
            for (var dimension = 0; dimension < 3; dimension++)
            foreach (var offset in new[] { -1, 0, 1 })
            {
                var stats = requirements.ToArray();
                stats[dimension] = BitConverter.ToSingle(BitConverter.GetBytes(BitConverter.ToInt32(BitConverter.GetBytes(stats[dimension]), 0) + offset), 0);
                Set(adventure, "attack", stats[0]); Set(adventure, "defense", stats[1]); Set(adventure, "regen", stats[2]);
                Equal((double)stats[0], Convert.ToDouble(Invoke(Character, "totalAdvAttack")), "controlled native power boundary");
                Equal((double)stats[1], Convert.ToDouble(Invoke(Character, "totalAdvDefense")), "controlled native toughness boundary");
                Equal((double)stats[2], Convert.ToDouble(Invoke(Character, "totalAdvHPRegen")), "controlled native regen boundary");
                Equal(offset >= 0, Invoke(controller, method), "actual native three-dimensional adjacent-float boundary " + boss + "/" + version + "/" + dimension + "/" + offset);
                Equal(offset >= 0, Invoke(helpers, "TitanAutokillAchieved", boss), "authoritative production native dispatch at boundary");
            }
            if (boss < 8) continue;
            var sprite = (boss == 8 ? 344 : 365 + (boss - 9) * 4) + version - 1;
            Set(bestiary[sprite], "kills", Convert.ChangeType(boss == 8 ? 24 : 5, Field(bestiary[sprite], "kills").FieldType));
            Set(adventure, "attack", 0f); Set(adventure, "defense", 0f); Set(adventure, "regen", 0f);
            Equal(true, Invoke(helpers, "TitanAutokillAchieved", boss), "native bestiary bypass remains authoritative " + boss + "/" + version);
            Equal(requirements[0], ((float[])Invoke(helpers, "TitanAutokillRequirements", boss))[0], "numeric stat target is unchanged by bestiary bypass");
            Set(bestiary[sprite], "kills", Convert.ChangeType(0, Field(bestiary[sprite], "kills").FieldType));
        }
        Console.WriteLine("Native titan table/boundaries: " + (Assertions - before) + " assertions across all 33 selected versions");
    }

    private static void SetActiveBeards(params int[] ids)
    {
        var beards = Get(Character, "beards"); var rows = (IList)Get(beards, "beards"); var active = (IList)Get(beards, "activeBeards"); active.Clear();
        for (var i = 0; i < rows.Count; i++) Set(rows[i], "active", ids.Contains(i));
        foreach (var id in ids) active.Add(id);
    }
    private static object New(string name) { return Activator.CreateInstance(Plugin.GetType("NGUInjector.Managers." + name, true), true); }
    private static object Planner(string name, params object[] args) { return Invoke(Plugin.GetType("NGUInjector.Managers.LoadoutPresetPlanner", true), name, args); }
    private static object CallMain(string name, params object[] args)
    {
        if (name == "ApplyExactSupport" || name == "ApplyPresetDesiredSupport") return InvokeGrantedSupportActuator(name, args);
        return Invoke(MainInstance ?? (object)MainType, name, args);
    }
    private static void RestartFixtureStage(object plan)
    {
        var settings = Get(MainType, "Settings"); var name = (string)Property(settings, "CustomPlanFile");
        var store = Get(MainType, "CustomPlanStore"); Invoke(store, "Save", name, plan);
        var saved = Invoke(store, "Load", name); var scheduler = Get(MainInstance, "_protectedScheduler");
        if (!(bool)Invoke(scheduler, "Matches", saved))
        {
            // A structurally different plan is a new explicit run, not an
            // application reset that silently renews the old entry's budget.
            Invoke(settings, "set_UseCustomRebirthPlan", false); ServiceFixtureNativeTargets(plan);
            name = "fixture-structure-" + Guid.NewGuid().ToString("N"); Set(plan, "Name", name);
            Invoke(store, "Save", name, plan); Invoke(settings, "set_CustomPlanFile", name);
            Invoke(settings, "set_UseCustomRebirthPlan", true);
            return;
        }
        var args = JsonParse("{}");
        SetJson(args, "name", Json(JsonParse("{\"v\":\"" + name + "\"}"), "v"));
        SetJson(args, "expectedRevision", Json(JsonParse("{\"v\":\"" + Get(saved, "RuntimeRevision") + "\"}"), "v"));
        SetJson(args, "expectedEntry", Json(JsonParse("{\"v\":\"" + Get(MainInstance, "_protectedGeneration") + ":" + Property(scheduler, "Entry") + "\"}"), "v"));
        SetJson(args, "action", Json(JsonParse("{\"v\":\"restart\"}"), "v"));
        SetJson(args, "stageIndex", Json(JsonParse("{\"v\":" + Property(scheduler, "StageIndex") + "}"), "v"));
        CallMain("OverrideCustomStage", args);
    }
    private static void ServiceFixtureNativeTargets(object plan)
    {
        CallMain("ServiceGlobalPresetNativeTargets");
        CallMain("ReconcileCustomResources", plan, false);
    }
    // Primitive actuator checks admit a real scoped grant; they do not substitute
    // a writable success/ownership flag for the production authorization boundary.
    private static object InvokeGrantedSupportActuator(string name, object[] args)
    {
        var coordinator = MainInstance; var model = Character; var settings = Get(MainType, "Settings");
        var domain = Enum.Parse(MainType.GetNestedType("ExecutionDomain", All), "Support");
        var gear = Invoke(Plugin.GetType("NGUInjector.Managers.LoadoutManager", true), "CaptureExactLoadout");
        var writer = Plugin.GetType("NGUInjector.Managers.ResourceWriter", true);
        var accounting = Invoke(writer, "AllocatedResourceKey");
        var grant = Invoke(coordinator, "ResolveExecutionGrant", "native-support-fixture", Guid.NewGuid().ToString("N"), domain,
            (Func<bool>)(() => ReferenceEquals(Get(MainType, "reference"), coordinator) && ReferenceEquals(Character, model)
                && ReferenceEquals(Get(MainType, "Settings"), settings)
                && (bool)Invoke(Plugin.GetType("NGUInjector.Managers.LoadoutManager", true), "ExactLoadoutMatches", gear)
                && Equals(accounting, Invoke(writer, "AllocatedResourceKey"))), null, null);
        var previous = Get(coordinator, "_selectedSetupSupportAuthorized");
        Set(coordinator, "_selectedSetupSupportAuthorized", (Func<bool>)(() => (bool)Invoke(coordinator, "ValidateExecutionGrant", grant, domain)));
        try { return Invoke(coordinator, name, args); }
        finally { Set(coordinator, "_selectedSetupSupportAuthorized", previous); }
    }
    private static object Invoke(object target, string name, params object[] args)
    {
        var type = target as Type ?? target.GetType();
        var methods = type.GetMethods(All).Where(m => m.Name == name).ToArray();
        var method = methods.SingleOrDefault(m => m.GetParameters().Length == args.Length)
            ?? methods.Single(m => m.GetParameters().Length > args.Length && m.GetParameters().Skip(args.Length).All(p => p.IsOptional));
        if (method.GetParameters().Length > args.Length) args = args.Concat(method.GetParameters().Skip(args.Length).Select(p => p.DefaultValue)).ToArray();
        try { return method.Invoke(method.IsStatic ? null : target, args); }
        catch (TargetInvocationException error) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    private static FieldInfo Field(object target, string name)
    {
        for (var type = target as Type ?? target.GetType(); type != null; type = type.BaseType)
        { var field = type.GetField(name, All); if (field != null) return field; }
        throw new MissingFieldException((target as Type ?? target.GetType()).FullName, name);
    }
    private static object Get(object target, string name) { return Field(target, name).GetValue(target is Type ? null : target); }
    private static void Set(object target, string name, object value) { Field(target, name).SetValue(target is Type ? null : target, value); }
    private static object Child(object owner, string field)
    { var type = owner.GetType().GetField(field, All).FieldType; var child = Invoke(typeof(Program), "Blank", type); Set(owner, field, child); return child; }
    private static void Sequence(object owner, string field, int count, object value)
    {
        var type = Field(owner, field).FieldType; var element = type.IsArray ? type.GetElementType() : type.GetGenericArguments()[0];
        var values = type.IsArray ? (IList)Array.CreateInstance(element, count) : (IList)Activator.CreateInstance(type);
        for (var i = 0; i < count; i++) if (type.IsArray) values[i] = Convert.ChangeType(value, element); else values.Add(Convert.ChangeType(value, element));
        Set(owner, field, values);
    }
    private static IList ListField(object owner, string field, int count)
    {
        var type = owner.GetType().GetField(field, All).FieldType; var list = (IList)Activator.CreateInstance(type); Set(owner, field, list);
        for (var i = 0; i < count; i++) list.Add(Invoke(typeof(Program), "Blank", type.GetGenericArguments()[0])); return list;
    }
    private static Array ArrayField(object owner, string field, int count)
    {
        var type = owner.GetType().GetField(field, All).FieldType.GetElementType(); var array = Array.CreateInstance(type, count); Set(owner, field, array);
        for (var i = 0; i < count; i++) array.SetValue(Invoke(typeof(Program), "Blank", type), i); return array;
    }
    private static void Check(bool condition, string message) { Assertions++; if (!condition) throw new Exception(message); }
    private static string Number(object value) { return value is float ? ((float)value).ToString("R") : value is double ? ((double)value).ToString("R") : Convert.ToString(value); }
    private static void Equal(object expected, object actual, string message) { Check(Equals(expected, actual), message + ": expected " + Number(expected) + " (" + expected?.GetType().Name + "), actual " + Number(actual) + " (" + actual?.GetType().Name + ")"); }
    private static void Throws(Action action, string message, string name)
    { try { action(); } catch (Exception error) { Check(error.ToString().IndexOf(message, StringComparison.OrdinalIgnoreCase) >= 0, name + ": " + error); return; } throw new Exception(name + ": did not throw"); }
}

// Linked only into runners using the preset native-game boundary. Program.Setup
// invokes this once per NEW Character, never from PrepareSupport or a retry.
public static partial class Program
{
#if PRESET_STAGE_NATIVE_CHECKS
    private static void ConfigureCombatSetupGameFixture(AssemblyDefinition game)
    {
        // LoadFixtureGame also serves the native-AT startup. Share only the route
        // model-write boundary, not LoadGame's unrelated getter substitutions.
        var selector = game.MainModule.Types.Single(type => type.Name == "ZoneSelector");
        PresetIntegrationChecks.KeepNativeItopodEntry(selector.Methods.Single(method => method.Name == "changeZone" && method.HasBody));
    }
#endif

    private static void PrepareNativePresetFixture()
    {
        // Setup means a NEW save/model, not a reload of the preceding case's
        // same-number rebirth. Keep reload tests on their existing directory.
        // No prior checkpoint is removed or made available as fresh-case authority.
        var checkpointDirectory = Path.Combine(Temp, "preset-checkpoints-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(checkpointDirectory);
        Set(MainType, "_dir", checkpointDirectory);
        if (Get(Character, "adventure") == null) Child(Character, "adventure");
        var controller = Get(Character, "adventureController");
        Set(controller, "character", Character);
        var player = Get(controller, "playerController") ?? Child(controller, "playerController");
        var enemy = Get(controller, "enemyAI") ?? Child(controller, "enemyAI");
        Set(enemy, "player", player);
        foreach (var field in new[] { "log", "itopodStartInput", "itopodEndInput" })
            if (Get(controller, field) == null) Child(controller, field);
        var selector = Get(controller, "zoneSelector") ?? Child(controller, "zoneSelector");
        Set(selector, "character", Character); Set(selector, "ac", controller);
        // Uses KeepNativeItopodEntry's real native model-write slice. Default zone
        // zero is active combat, not a valid implicit initial safe world.
        Invoke(selector, "changeZone", -1);
        Equal(-1, Get(Get(Character, "adventure"), "zone"), "fresh preset fixture enters observed native safe mode");
    }
}
