using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using NGUInjector.Managers;
using SimpleJSON;

namespace NGUInjector
{
    internal partial class Main
    {
        private const string ExternalUiMarkerFile = "external-ui.enabled";
        private const int ExternalUiPumpMilliseconds = 250;
        private const int ExternalUiRuntimeMilliseconds = 1000;
        private const int ExternalUiMaxCommandsPerPump = 4;
        private const int ExternalUiCommandLifetimeSeconds = 120;
        private const int ExternalUiMaxCommandBytes = 3 * 1024 * 1024;
        private const int ExternalUiActionResultMaxBytes = 256 * 1024;
        private const long ExternalUiMaxRuntimeRevision = 9007199254740991L;
        private static readonly DateTime ExternalUiStartedAt = DateTime.Now;
        private static readonly object ExternalUiRuntimePublicationLock = new object();
        private static long _externalUiRuntimeRevision;
        private static string _externalUiRuntimePath;
        private static string _externalUiCommandsDir;
        private static string _externalUiCommandResultsDir;
        private static string _externalUiClaimsDir;
        private static readonly string[] ExternalUiCommands = {
            "applySettingsPatch", "savePlan", "focusGoal", "stopFocus", "optimizeGear", "previewGear",
            "createPlan", "deletePlan", "readPlan", "previewAugments", "allocateAugments",
            "manualFightPrep", "manualRebirth", "safeProbe", "probeCommands", "precacheLoadouts",
            "resetRebirthPrep", "reloadSettings", "quickSave", "captureLocalSave", "scanSaves", "loadLongestSave", "status", "unload", "setPlanStage",
            "startMaxItopodPush", "cancelMaxItopodPush", "previewCombatPrep", "startCombatPrep", "cancelCombatPrep",
            "previewLoadoutPreset", "applyLoadoutPreset", "readPresetLibrary", "savePresetLibrary",
            "previewGlobalPreset", "applyGlobalPreset", "equipGlobalPreset", "previewExecutionRebind", "applyExecutionRebind", "previewExecutionRestart"
        };
        private DateTime _externalUiLastPump = DateTime.MinValue;
        private DateTime _externalUiLastRuntimeWrite = DateTime.MinValue;
        private static JSONObject _externalUiLastRuntimeSnapshot;
        private string _currentExecutionCommandId;
        internal string CurrentExecutionCommandId => _currentExecutionCommandId;

        private void PersistExecutionCommandAcceptance(string id, string command, bool completed)
        {
            var saved = new JSONObject(); saved["version"] = 1; saved["commandId"] = id;
            saved["command"] = command; saved["state"] = completed ? "accepted" : "admitted";
            saved["plan"] = Settings?.CustomPlanFile ?? ""; saved["entry"] = ExecutionPersistenceEntry ?? "";
            saved["acceptedAt"] = DateTime.UtcNow.ToString("o");
            if (completed)
            {
                var selections = new JSONArray();
                foreach (var path in Directory.EnumerateFiles(_dir, "execution-*-selection.json"))
                {
                    var selection = JsonFiles.ParseObject(File.ReadAllText(path));
                    if (!selection["active"].IsBoolean || selection["active"].AsBool) selections.Add(selection);
                }
                saved["selectedCommitments"] = selections;
                var process = ExportAcceptedExecutionProcess();
                if (process["id"].IsNull || string.IsNullOrEmpty(process["id"].Value)) process["id"] = id;
                saved["process"] = process;
            }
            // A failed acceptance publication is NOT permission to replay the claimed
            // command. Preview tokens remain only in the original audited request.
            JsonFiles.WriteAtomic(Path.Combine(_externalUiClaimsDir, id + ".accepted.json"), saved.ToString(2));
        }

        internal static bool ExternalUiEnabled { get; private set; }

        private void InitializeExternalUi()
        {
            ExternalUiEnabled = ExternalUiRequested();
            if (!ExternalUiEnabled)
                return;

            _externalUiRuntimePath = Path.Combine(_dir, "runtime.json");
            _externalUiCommandsDir = Path.Combine(_dir, "commands");
            _externalUiCommandResultsDir = Path.Combine(_dir, "command-results");
            _externalUiClaimsDir = Path.Combine(_externalUiCommandsDir, "claimed");

            Directory.CreateDirectory(_externalUiCommandsDir);
            Directory.CreateDirectory(_externalUiCommandResultsDir);
            Directory.CreateDirectory(_externalUiClaimsDir);
            WriteExternalUiRuntimeSnapshot(0);
        }

        private static bool ExternalUiRequested()
        {
            var env = Environment.GetEnvironmentVariable("NGUINJECTOR_EXTERNAL_UI");
            if (string.Equals(env, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(env, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(env, "yes", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            try
            {
                return File.Exists(Path.Combine(_dir, ExternalUiMarkerFile));
            }
            catch
            {
                return false;
            }
        }

        private void PumpExternalUi(int progressPercent)
        {
            if (!ExternalUiEnabled || _unloaded)
                return;

            var now = DateTime.UtcNow;
            if ((now - _externalUiLastPump).TotalMilliseconds >= ExternalUiPumpMilliseconds)
            {
                _externalUiLastPump = now;
                ProcessExternalUiCommands();
            }

            if (_unloaded)
                return;
            if ((now - _externalUiLastRuntimeWrite).TotalMilliseconds >= ExternalUiRuntimeMilliseconds)
            {
                _externalUiLastRuntimeWrite = now;
                WriteExternalUiRuntimeSnapshot(progressPercent);
            }
        }

        private void ProcessExternalUiCommands()
        {
            try
            {
                if (string.IsNullOrEmpty(_externalUiCommandsDir) || !Directory.Exists(_externalUiCommandsDir))
                    return;

                var commandFiles = Directory.EnumerateFiles(_externalUiCommandsDir, "*.json")
                    .Take(ExternalUiMaxCommandsPerPump).ToArray();
                var timer = Stopwatch.StartNew();

                foreach (var path in commandFiles)
                {
                    ProcessExternalUiCommand(path);
                    if (_unloaded || timer.ElapsedMilliseconds >= 25)
                        break;
                }
            }
            catch (Exception e)
            {
                LogError("external UI command pump", e);
            }
        }

        private void ProcessExternalUiCommand(string path)
        {
            var id = Path.GetFileNameWithoutExtension(path);
            var command = string.Empty;
            var validFileId = IsExternalUiCommandId(id);
            if (!validFileId)
                id = "invalid-" + Guid.NewGuid().ToString("N");
            var claimedPath = Path.Combine(_externalUiClaimsDir, id + ".claimed");

            // A persistent claim is the at-most-once boundary, even if result reporting fails.
            try
            {
                if (File.Exists(claimedPath) || File.Exists(Path.Combine(_externalUiCommandResultsDir, id + ".json")))
                {
                    SafeDelete(path);
                    return;
                }
                File.Move(path, claimedPath);
            }
            catch (IOException) { return; } // Busy/incomplete producer writes can be claimed on a later pump.
            catch (UnauthorizedAccessException) { return; }

            try
            {
                if (!validFileId)
                    throw new FormatException("Invalid command filename ID.");
                if (new FileInfo(claimedPath).Length > ExternalUiMaxCommandBytes)
                    throw new FormatException("Command exceeds the 1 MiB limit.");
                var root = JsonFiles.ParseObject(File.ReadAllText(claimedPath));
                var envelopeId = root["id"];
                if (envelopeId != null && (!envelopeId.IsString || !IsExternalUiCommandId(envelopeId.Value) || envelopeId.Value != id))
                    throw new FormatException("Command ID must be valid and match its filename.");
                command = ReadJsonString(root, "command", string.Empty);
                if (string.IsNullOrWhiteSpace(command))
                    throw new InvalidOperationException("Command file did not include a command name.");
                var now = DateTime.UtcNow;
                var createdAt = ReadExternalUiTimestamp(root["createdAt"], File.GetLastWriteTimeUtc(claimedPath));
                var deadline = createdAt.AddSeconds(ExternalUiCommandLifetimeSeconds);
                var expiresAt = ReadExternalUiTimestamp(root["expiresAt"], deadline);
                if (createdAt > now.AddSeconds(30) || now >= deadline || now >= expiresAt)
                    throw new InvalidOperationException("Command expired or has a future creation timestamp.");
                var args = root["args"];
                if (args != null && !args.IsNull && !args.IsObject)
                    throw new FormatException("Command args must be an object or null.");

                JSONNode data;
                var asynchronous = new[] { "focusGoal", "manualFightPrep", "manualRebirth", "startCombatPrep", "startMaxItopodPush",
                    "equipGlobalPreset", "applyGlobalPreset", "applyLoadoutPreset" }.Contains(command);
                if (asynchronous) PersistExecutionCommandAcceptance(id, command, false);
                string message;
                _currentExecutionCommandId = id;
                try { message = ExecuteExternalUiCommand(command, args, out data); }
                finally { _currentExecutionCommandId = null; }
                // Existing action handlers sometimes return LogError instead of throwing.
                var success = message == null || !message.StartsWith("ERROR in ", StringComparison.Ordinal);
                if (asynchronous && success) PersistExecutionCommandAcceptance(id, command, true);
                data = AttachExternalUiRuntime(data);
                WriteExternalUiCommandResult(id, command, success, message, data);
            }
            catch (Exception e)
            {
                var message = LogError($"external UI command '{command}'", e);
                WriteExternalUiCommandResult(id, command, false, message, AttachExternalUiRuntime(null));
            }
        }

        private string ExecuteExternalUiCommand(string command, JSONNode args, out JSONNode data)
        {
            data = null;
            if (!_started || _unloaded || _injectorLease == null || !ReferenceEquals(reference, this))
                throw new InvalidOperationException("Injector is not ready: startup is incomplete, failed, or unloaded.");
            if (_executionLifecycle != ExecutionLifecycle.Running && command != "status" && command != "unload")
                throw new InvalidOperationException("Execution is quiesced; ordinary commands are not admitted.");
            if (command == "focusGoal" || command == "stopFocus" || command == "optimizeGear" || command == "allocateAugments"
                || command == "startMaxItopodPush" || command == "resetRebirthPrep"
                || command == "probeCommands")
                RequireCombatPrepIdle();
            switch (command)
            {
                case "captureLocalSave":
                case "scanSaves":
                case "loadLongestSave":
                    return ExecuteSaveSafetyCommand(command, args, out data);
                case "previewExecutionRebind":
                    data = new JSONObject(); data["result"] = PreviewExecutionRebind(args);
                    return "Acquired selection review generated.";
                case "previewExecutionRestart":
                    data = new JSONObject(); data["result"] = PreviewExecutionRestart(args);
                    return "Explicit stage restart reviewed.";
                case "applyExecutionRebind":
                    return ApplyExecutionRebind(args);
                case "previewCombatPrep":
                    return PreviewCombatPrep(args, out data);
                case "startCombatPrep":
                    return StartCombatPrep(args);
                case "cancelCombatPrep":
                    return CancelCombatPrep(args);
                case "startMaxItopodPush":
                    var deaths = args?["deaths"];
                    if (args == null || args.Count != 1 || deaths == null || !deaths.IsNumber
                        || deaths.AsDouble < 1 || deaths.AsDouble > 1000 || deaths.AsDouble != Math.Truncate(deaths.AsDouble))
                        throw new FormatException("args.deaths must be an integer between 1 and 1000, with no other arguments.");
                    return StartMaxItopodPush(deaths.AsInt);
                case "cancelMaxItopodPush":
                    if (args != null && !args.IsNull && args.Count != 0)
                        throw new FormatException("cancelMaxItopodPush does not accept arguments.");
                    return CancelMaxItopodPush();
                case "setPlanStage":
                    return OverrideCustomStage(args);
                case "applySettingsPatch":
                    if (Settings == null)
                        throw new InvalidOperationException("Settings are not loaded yet.");
                    if (CombatPrepActive && (args?["patch"]?["_manageEnergy"]?.IsBoolean == true && !args["patch"]["_manageEnergy"].AsBool
                        || args?["patch"]?["_manageMagic"]?.IsBoolean == true && !args["patch"]["_manageMagic"].AsBool))
                        throw new InvalidOperationException("Cancel combat prep before disabling managed resource lanes.");
                    var settingsRevision = args?["expectedRevision"];
                    if (settingsRevision == null || !settingsRevision.IsString)
                        throw new FormatException("args.expectedRevision is required and must be a string.");
                    data = Settings.ApplyExternalPatch(args?["patch"], settingsRevision.Value);
                    InvalidateGlobalNguWeightPolicies();
                    LoadAllocation();
                    return "Settings patch applied and saved.";
                case "savePlan":
                    if (CustomPlanStore == null)
                        throw new InvalidOperationException("Custom plan store is not loaded yet.");
                    var name = ReadJsonString(args, "name", string.Empty);
                    var revisionNode = args?["expectedRevision"];
                    if (revisionNode == null || !revisionNode.IsString)
                        throw new FormatException("args.expectedRevision is required and must be a string.");
                    string savedRevision;
                    lock (NGUInjector.CustomPlans.PresetLibraryStore.SyncRoot)
                    {
                        var previousPermission = CustomPlanStore.PlanExists(name)
                            ? (bool?) CustomPlanStore.Load(name).AutoRebirthEnabled : null;
                        savedRevision = CustomPlanStore.SaveExternalPlan(name, args?["plan"], revisionNode.Value);
                        var permission = CustomPlanStore.Load(name).AutoRebirthEnabled;
                        // Unrelated saves (including retries after a partial save) must preserve live opt-outs.
                        if (previousPermission.HasValue && previousPermission.Value != permission
                            && Settings != null && Settings.UseCustomRebirthPlan
                            && string.Equals(Settings.CustomPlanFile, name, StringComparison.OrdinalIgnoreCase)
                            && Settings.AutoRebirth != permission)
                        {
                            var rebirthPatch = new JSONObject();
                            rebirthPatch["_autoRebirth"] = permission;
                            try { Settings.ApplyPatch(rebirthPatch); }
                            catch (Exception e)
                            {
                                throw new InvalidOperationException("Plan saved, but live auto-rebirth synchronization failed. Retrying the save will not replay the switch. Explicitly Disable plan, then Enable plan to apply its saved permission.", e);
                            }
                        }
                    }
                    var saved = new JSONObject();
                    saved["name"] = name;
                    saved["revision"] = savedRevision;
                    data = saved;
                    return "Plan saved: " + name;
                case "readPlan":
                    if (CustomPlanStore == null)
                        throw new InvalidOperationException("Custom plan store is not loaded yet.");
                    var readName = ReadJsonString(args, "name", string.Empty);
                    var read = new JSONObject();
                    read["plan"] = CustomPlanStore.ReadExternalPlan(readName);
                    data = read;
                    return "Plan read and normalized: " + readName;
                case "createPlan":
                    if (CustomPlanStore == null)
                        throw new InvalidOperationException("Custom plan store is not loaded yet.");
                    var createdName = CustomPlanStore.CreateExternalPlan(ReadJsonString(args, "requestedName", string.Empty), args?["source"]);
                    var created = new JSONObject();
                    created["name"] = createdName;
                    data = created;
                    return "Plan created: " + createdName;
                case "deletePlan":
                    if (CustomPlanStore == null || Settings == null)
                        throw new InvalidOperationException("Settings and custom plan store must be loaded first.");
                    var deletedName = ReadJsonString(args, "name", string.Empty);
                    CustomPlanStore.DeleteExternalPlan(deletedName);
                    var deleted = new JSONObject();
                    deleted["name"] = deletedName;
                    deleted["selectedPlan"] = Settings.CustomPlanFile;
                    data = deleted;
                    return "Plan deleted: " + deletedName;
                case "focusGoal":
                    var goal = ReadExternalUiEnum<CustomFocusGoal>(args, "goal");
                    var mode = ReadJsonString(args, "mode", "toggle");
                    if (mode != "toggle" && mode != "apply")
                        throw new FormatException("Focus mode must be toggle or apply.");
                    return mode == "apply" ? ApplyCustomFocusGoal(goal) : ToggleCustomFocusGoal(goal);
                case "stopFocus":
                    return StopCustomFocusGoal();
                case "previewLoadoutPreset":
                    data = new JSONObject();
                    data["result"] = PreviewLoadoutPreset(args);
                    return "Loadout preset comparison generated without changing gear, support or resources.";
                case "applyLoadoutPreset":
                    return ApplyLoadoutPreset(args, out data);
                case "readPresetLibrary":
                    if (args != null && !args.IsNull && (!args.IsObject || args.Count != 0))
                        throw new FormatException("readPresetLibrary does not accept arguments.");
                    if (CustomPlanStore == null) throw new InvalidOperationException("Preset library is unavailable.");
                    data = new JSONObject(); data["library"] = CustomPlanStore.PresetLibrary.ReadExternalLibrary();
                    return "Global preset library read.";
                case "savePresetLibrary":
                    GlobalPresetArgs(args, "library", "expectedRevision");
                    if (CustomPlanStore == null) throw new InvalidOperationException("Preset library is unavailable.");
                    var libraryRevision = CustomPlanStore.PresetLibrary.SaveExternalLibrary(args["library"], args["expectedRevision"].Value);
                    data = new JSONObject(); data["revision"] = libraryRevision;
                    return "Global preset library saved.";
                case "previewGlobalPreset":
                    data = new JSONObject(); data["result"] = PreviewGlobalPreset(args);
                    return "Global preset preview generated without mutation.";
                case "applyGlobalPreset":
                    return ApplyGlobalPreset(args, out data);
                case "equipGlobalPreset":
                    return EquipGlobalPreset(args, out data);
                case "optimizeGear":
                case "previewGear":
                    var gearGoal = ReadExternalUiEnum<GearOptimizationGoal>(args, "goal");
                    var fightMode = args != null && args["fightScoreMode"] != null
                        ? ReadExternalUiEnum<FightScoreMode>(args, "fightScoreMode") : GetActiveFightScoreMode();
                    var result = command == "previewGear" ? GearOptimizer.Optimize(gearGoal, fightMode)
                        : ApplyManualGearSelection(gearGoal, fightMode);
                    if (!result.Success)
                        throw new InvalidOperationException(result.Message);
                    var gear = new JSONObject();
                    var ids = new JSONArray();
                    foreach (var itemId in result.ItemIds ?? new int[0]) ids.Add(itemId);
                    gear["itemIds"] = ids;
                    gear["score"] = result.Score;
                    gear["evaluatedCombinations"] = result.EvaluatedCombinations;
                    gear["visitedNodes"] = result.VisitedNodes;
                    gear["prunedBranches"] = result.PrunedBranches;
                    gear["elapsedMilliseconds"] = result.ElapsedMilliseconds;
                    data = gear;
                    return result.Message;
                case "previewAugments":
                case "allocateAugments":
                    var expectedSeconds = Settings == null ? 300 : Settings.CustomAugmentsBloodEstimateSeconds;
                    var expectedNode = args?["expectedSeconds"];
                    if (expectedNode != null && !expectedNode.IsNull)
                    {
                        if (!expectedNode.IsNumber || expectedNode.AsDouble < 1 || expectedNode.AsDouble > int.MaxValue
                            || expectedNode.AsDouble != Math.Truncate(expectedNode.AsDouble))
                            throw new FormatException("args.expectedSeconds must be a positive integer.");
                        expectedSeconds = expectedNode.AsInt;
                    }
                    var augmentResult = command == "previewAugments" ? AugmentOptimizer.OptimizeBest(expectedSeconds)
                        : RequestAugmentAllocation(AugmentOptimizer.OptimizeBest(expectedSeconds));
                    if (!augmentResult.Success)
                        throw new InvalidOperationException(augmentResult.Message);
                    var augment = new JSONObject();
                    augment["augmentIndex"] = augmentResult.AugmentIndex;
                    augment["usesUpgrade"] = augmentResult.UsesUpgrade;
                    augment["energyToAugment"] = augmentResult.EnergyToAugment;
                    augment["energyToUpgrade"] = augmentResult.EnergyToUpgrade;
                    augment["seconds"] = augmentResult.Seconds;
                    augment["expectedSeconds"] = augmentResult.ExpectedSeconds;
                    augment["valuePerSecond"] = augmentResult.ValuePerSecond;
                    augment["expectedValue"] = augmentResult.ExpectedValue;
                    augment["projectedGoldCost"] = augmentResult.ProjectedGoldCost;
                    augment["projectedGoldBudget"] = augmentResult.ProjectedGoldBudget;
                    augment["goldPerSecond"] = augmentResult.GoldPerSecond;
                    augment["energyPower"] = augmentResult.EnergyPower;
                    augment["augmentSpeedMultiplier"] = augmentResult.AugmentSpeedMultiplier;
                    augment["projectedWork"] = augmentResult.ProjectedWork;
                    augment["progressFraction"] = augmentResult.ProgressFraction;
                    data = augment;
                    return string.Join(Environment.NewLine, new[] {
                        augmentResult.Message,
                        $"Energy: aug {augmentResult.EnergyToAugment:0}, upgrade {augmentResult.EnergyToUpgrade:0}; energy power {augmentResult.EnergyPower:0.###}; augment multiplier {augmentResult.AugmentSpeedMultiplier:0.###}.",
                        $"Expected work: {augmentResult.ProjectedWork:0.###} over {augmentResult.ExpectedSeconds:0.#}s; expected value {augmentResult.ExpectedValue:0.###}; score {augmentResult.ValuePerSecond:0.###}/s.",
                        $"Gold: projected cost {augmentResult.ProjectedGoldCost:0.###}; budget {augmentResult.ProjectedGoldBudget:0.###}; net GPS {augmentResult.GoldPerSecond:0.###}."
                    });
                case "manualFightPrep":
                    return RunManualFightPrep();
                case "manualRebirth":
                    return RunManualRebirth();
                case "safeProbe":
                    return RunSafeCustomProbe();
                case "probeCommands":
                    return RunProbeCommandFile();
                case "precacheLoadouts":
                    return PreCacheCustomEquipmentLoadouts();
                case "resetRebirthPrep":
                    ResetCustomPreRebirthPrep();
                    return "Custom rebirth prep state reset.";
                case "reloadSettings":
                    return ReloadSettingsForExternalUi();
                case "quickSave":
                    QuickSave();
                    return "Quicksave written.";
                case "unload":
                    Loader.Unload();
                    return "Unload requested.";
                case "status":
                    return GetCustomAutomationStatus();
                default:
                    throw new InvalidOperationException("Unknown external UI command: " + command);
            }
        }

        private static string ReloadSettingsForExternalUi()
        {
            reference?.EndCombatPrep("cancelled", "Settings reload preempted combat prep.");
            if (Settings == null || !Settings.LoadSettings())
                throw new InvalidOperationException("Settings reload failed; live settings retained.");
            reference?.InvalidateGlobalNguWeightPolicies();

            LoadAllocation();
            settingsForm?.UpdateFromSettings(Settings);
            return "Settings reloaded.";
        }

        private static void WriteExternalUiCommandResult(string id, string command, bool success, string message, JSONNode data)
        {
            try
            {
                if (string.IsNullOrEmpty(_externalUiCommandResultsDir) || !IsExternalUiCommandId(id))
                    return;

                Directory.CreateDirectory(_externalUiCommandResultsDir);
                var root = new JSONObject();
                root["id"] = id ?? string.Empty;
                root["command"] = command ?? string.Empty;
                root["success"] = success;
                root["message"] = message ?? string.Empty;
                root["completedAt"] = DateTime.UtcNow.ToString("o");
                if (data != null) root["data"] = data;
                WriteJsonAtomic(Path.Combine(_externalUiCommandResultsDir, id + ".json"), root);
            }
            catch
            {
                // Command result reporting must not interrupt the injector.
            }
        }

        private static void WriteExternalUiRuntimeSnapshot(int progressPercent)
        {
            try
            {
                if (string.IsNullOrEmpty(_externalUiRuntimePath))
                    return;

                var root = BuildExternalUiRuntimeState();
                root["version"] = Version;
                root["updatedAt"] = DateTime.Now.ToString("o");
                root["baseDir"] = _dir ?? string.Empty;
                root["profilesDir"] = _profilesDir ?? string.Empty;
                root["customPlansDir"] = _customPlansDir ?? string.Empty;
                root["progressPercent"] = Math.Max(0, Math.Min(100, progressPercent));
                root["customStatus"] = GetCustomAutomationStatus();
                root["settingsLoaded"] = Settings != null;
                root["customPlanStoreLoaded"] = CustomPlanStore != null;
                root["status"] = reference?._startupError != null ? "failed" : reference != null && reference._unloaded ? "unloaded"
                    : reference == null || !reference._started ? "starting" : "running";
                root["startupError"] = reference?._startupError ?? string.Empty;
                root["activeFocusGoal"] = reference?.GetActiveCustomFocusGoal()?.ToString() ?? string.Empty;
                root["bossPushRunning"] = reference != null && reference.BossPushRunning;
                root["ownershipLockPath"] = Path.Combine(_dir, ".injector.lock");
                root["planRevisionField"] = NGUInjector.CustomPlans.CustomPlanStore.ExternalUiRevisionField;
                root["planRevisionAlgorithm"] = "sha256";
                root["supportedCommands"] = ToJsonArray(ExternalUiCommands);
                root["commandLifetimeSeconds"] = ExternalUiCommandLifetimeSeconds;
                root["maxCommandBytes"] = ExternalUiMaxCommandBytes;
                root["maxCommandsPerPump"] = ExternalUiMaxCommandsPerPump;
                root["focusGoals"] = ToJsonArray(Enum.GetNames(typeof(CustomFocusGoal)));
                root["gearGoals"] = ToJsonArray(Enum.GetNames(typeof(GearOptimizationGoal)));
                root["fightScoreModes"] = ToJsonArray(Enum.GetNames(typeof(FightScoreMode)));
                root["legacyBaseDir"] = Path.Combine(Environment.ExpandEnvironmentVariables("%userprofile%/Desktop"), "NGUInjector");

                WriteExternalUiSettingsState(root);
                WriteExternalUiCharacterState(root);
                WriteExternalUiBloodState(root);
                PublishExternalUiRuntimeState(root, false);
            }
            catch (Exception e)
            {
                LogError("external UI runtime snapshot", e);
            }
        }

        private static JSONObject BuildExternalUiRuntimeState()
        {
            var root = new JSONObject();
            root["startedAt"] = ExternalUiStartedAt.ToString("o");
            root["externalUiEnabled"] = ExternalUiEnabled;
            root["protocolVersion"] = 3;
            root["settingsCasVersion"] = 1;
            root["planVersion"] = NGUInjector.CustomPlans.CustomPlanStore.CurrentPlanVersion;
            root["stageTimingVersion"] = 1;
            root["stageOverridesV1"] = true;
            root["updatedAt"] = DateTime.Now.ToString("o");
            root["status"] = reference?._startupError != null ? "failed" : reference != null && reference._unloaded ? "unloaded"
                : reference == null || !reference._started ? "starting" : "running";
            root["customStatus"] = GetCustomAutomationStatus();
            root["activeFocusGoal"] = reference?.GetActiveCustomFocusGoal()?.ToString() ?? string.Empty;
            root["appliedFocusGoal"] = reference?.GetAppliedCustomFocusGoal() ?? string.Empty;
            root["focusStatus"] = reference?.GetCustomFocusStatus() ?? "idle";
            root["resourceAllocation"] = reference?.GetResourceCoordinationRuntime();
            root["executionReview"] = reference?.GetExecutionPersistenceRuntime() ?? new JSONObject();
            root["execution"] = reference?.GetExecutionStateRuntime() ?? new JSONObject();
            root["saveSafety"] = reference?.GetSaveSafetyRuntime() ?? new JSONObject();
            root["planProgress"] = reference?.GetCustomPlanProgress() ?? UnavailableCustomPlanProgress();
            root["nguAllocation"] = reference?.GetCustomNguRuntime() ?? new JSONObject();
            root["bossPushRunning"] = reference != null && reference.BossPushRunning;
            root["maxItopodPush"] = reference?.GetMaxItopodPushRuntime() ?? new JSONObject();
            root["manualTitan"] = reference?.GetManualTitanRuntime() ?? new JSONObject();
            root["manualAdventureOverlay"] = reference?.GetManualAdventureOverlayRuntime() ?? new JSONObject();
            root["adventureRules"] = reference?.GetAdventureRulesRuntime() ?? new JSONObject();
            root["combatPrep"] = reference?.GetCombatPrepRuntime() ?? new JSONObject();
            root["loadoutPresetsVersion"] = 2;
            root["globalPresetsVersion"] = 2;
            root["adventureDynamicPresetsVersion"] = 1;
            root["globalNguWeightsVersion"] = 1;
            root["resourceLanePrioritiesVersion"] = 1;
            root["planBloodMagicVersion"] = 1;
            root["loadoutPreset"] = reference?.GetLoadoutPresetRuntime() ?? new JSONObject();
            root["equipmentBoosts"] = GetEquipmentBoostsRuntime();
            WriteExternalUiSettingsState(root, false);
            WriteExternalUiCharacterState(root);
            return root;
        }

        // Fast publication reuses metadata/estimates from the heartbeat, never runs gear/probes.
        private static JSONObject PublishExternalUiRuntime()
        {
            if (reference == null && _externalUiLastRuntimeSnapshot != null
                && (_externalUiLastRuntimeSnapshot["status"].Value == "unloaded" || _externalUiLastRuntimeSnapshot["status"].Value == "failed"))
                return JsonFiles.ParseObject(_externalUiLastRuntimeSnapshot.ToString());
            return PublishExternalUiRuntimeState(BuildExternalUiRuntimeState(), true);
        }

        private static JSONObject PublishExternalUiRuntimeState(JSONObject state, bool compact)
        {
            // Keep revisions ordered with file writes. Builders/read-only scheduler pulls do not consume them.
            lock (ExternalUiRuntimePublicationLock)
            {
                state["runtimeRevision"] = _externalUiRuntimeRevision;
                if (!ExternalUiEnabled || string.IsNullOrEmpty(_externalUiRuntimePath))
                    return state;
                if (_externalUiRuntimeRevision >= ExternalUiMaxRuntimeRevision)
                    throw new InvalidOperationException("Runtime revision exhausted the JavaScript safe integer range.");
                state["runtimeRevision"] = ++_externalUiRuntimeRevision;
                try
                {
                    var root = compact ? JsonFiles.ParseObject(_externalUiLastRuntimeSnapshot?.ToString() ?? "{}") : state;
                    if (compact)
                        foreach (var item in state.Linq) root[item.Key] = item.Value;
                    WriteJsonAtomic(_externalUiRuntimePath, root);
                    _externalUiLastRuntimeSnapshot = root;
                }
                catch (Exception e) { LogError("external UI runtime publication", e); }
                return state;
            }
        }

        private static JSONObject GetEquipmentBoostsRuntime()
        {
            var state = new JSONObject();
            state["available"] = false;
            state["reason"] = "Owned equipment game state is not available.";
            state["items"] = new JSONArray();
            try
            {
                var inv = Character?.inventory;
                var names = Character?.itemInfo?.itemName;
                if (inv == null || inv.inventory == null || inv.accs == null || names == null)
                    return state;

                // Read raw owned equipment, including unlocked inventory, without controller queries/actions.
                var owned = new[] { inv.head, inv.chest, inv.legs, inv.boots, inv.weapon, inv.weapon2 }
                    .Concat(inv.accs).Concat(inv.inventory)
                    .Where(x => x != null && x.id > 0 && x.isEquipment())
                    .Select(x => new { Equipment = x, FullyBoosted = x.IsFullyBoosted() })
                    .GroupBy(x => x.Equipment.id).OrderBy(x => x.Key);
                var items = new JSONArray();
                foreach (var group in owned)
                {
                    var incomplete = group.Where(x => x.Equipment.level < 100 || !x.FullyBoosted).ToArray();
                    if (incomplete.Length == 0) continue;
                    var row = new JSONObject();
                    row["id"] = group.Key;
                    row["name"] = names[group.Key] ?? string.Empty;
                    row["copies"] = group.Count();
                    row["level"] = incomplete.Min(x => x.Equipment.level);
                    row["fullyBoosted"] = group.All(x => x.FullyBoosted);
                    items.Add(row);
                }
                state["items"] = items;
                state["reason"] = string.Empty;
                state["available"] = true;
            }
            catch (Exception)
            {
                // Discard partial results; optional telemetry must never interrupt runtime publication.
                state["reason"] = "Owned equipment snapshot could not be read.";
            }
            return state;
        }

        private static JSONNode AttachExternalUiRuntime(JSONNode data)
        {
            var result = data?.AsObject ?? new JSONObject();
            if (data != null && result.Linq.Any(item => item.Key == "itemIds" || item.Key == "augmentIndex"))
                result["result"] = JsonFiles.ParseObject(data.ToString());
            result["runtime"] = PublishExternalUiRuntime();
            return result;
        }

        private static void WriteExternalUiSettingsState(JSONObject root, bool includePlanNames = true)
        {
            if (Settings == null)
                return;

            var settings = new JSONObject();
            settings["allocationFile"] = Settings.AllocationFile ?? string.Empty;
            settings["customPlanFile"] = Settings.CustomPlanFile ?? string.Empty;
            settings["globalEnabled"] = Settings.GlobalEnabled;
            settings["combatEnabled"] = Settings.CombatEnabled;
            settings["autoRebirth"] = Settings.AutoRebirth;
            settings["useCustomRebirthPlan"] = Settings.UseCustomRebirthPlan;
            settings["customAdventureEnabled"] = Settings.CustomAdventureEnabled;
            settings["customAdventureMode"] = Settings.CustomAdventureMode;
            settings["customAdventureMaxItopodDeaths"] = Settings.CustomAdventureMaxItopodDeaths;
            settings["customAdventureZone"] = Settings.CustomAdventureZone;
            settings["customAdventureGoldPush"] = Settings.CustomAdventureGoldPush;
            settings["manualAdventureEnabled"] = Settings.ManualAdventureEnabled;
            settings["manualAdventureZone"] = Settings.ManualAdventureZone;
            settings["manualAdventureBossOnly"] = Settings.ManualAdventureBossOnly;
            settings["customAutoBoostEnabled"] = Settings.CustomAutoBoostEnabled;
            settings["customAutoMoneyPitEnabled"] = Settings.CustomAutoMoneyPitEnabled;
            settings["customAutoBloodSpellsEnabled"] = Settings.CustomAutoBloodSpellsEnabled;
            settings["disableOverlay"] = Settings.DisableOverlay;
            settings["_energyNguWeights"] = LoadoutPresetNumbers(Settings.EnergyNguWeights.Select(x => (double)x));
            settings["_magicNguWeights"] = LoadoutPresetNumbers(Settings.MagicNguWeights.Select(x => (double)x));
            root["settings"] = settings;

            if (!includePlanNames) return;

            try
            {
                root["planNames"] = ToJsonArray(CustomPlanStore?.ListPlanNames());
            }
            catch
            {
                root["planNames"] = new JSONArray();
            }
        }

        private static void WriteExternalUiCharacterState(JSONObject root)
        {
            try
            {
                var character = new JSONObject();
                character["available"] = Character != null;
                if (Character != null)
                {
                    character["rebirthSeconds"] = Character.rebirthTime?.totalseconds ?? 0;
                    character["bossId"] = Character.bossID;
                    character["attackMultiplier"] = Character.attackMulti;
                    character["nextAttackMultiplier"] = Character.nextAttackMulti;
                    if (Character.adventure != null)
                    {
                        character["adventureZone"] = Character.adventure.zone;
                        character["autoAttacking"] = Character.adventure.autoattacking;
                    }
                }

                root["character"] = character;
            }
            catch
            {
                root["character"] = new JSONObject();
            }
        }

        internal static BloodGoalEstimate[] GetBloodGoalEstimates()
        {
            string status, reason;
            return GetBloodGoalEstimates(out status, out reason, out _);
        }

        private static BloodGoalEstimate[] GetBloodGoalEstimates(out string status, out string reason, out NGUInjector.CustomPlans.CustomPlan plan)
        {
            plan = null;
            status = "unavailable";
            reason = Settings == null ? "Settings are not loaded." : "Blood Magic game state is not available.";
            try
            {
                if (Settings != null && Settings.UseCustomRebirthPlan && reference != null && CustomPlanStore != null)
                    plan = reference.LoadRuntimeCustomPlan();
                if (Settings == null || Character?.bloodMagic == null || Character.bloodMagicController == null || Character.bloodSpells == null)
                    return new BloodGoalEstimate[0];

                var blood = Character.bloodMagic;
                var optimal = BloodMagicOptimizer.OptimizeBest();
                var production = optimal.Success ? optimal.BloodPerSecond : 0d;
                var count = (blood.lootAutoSpell ? 1 : 0) + (blood.goldAutoSpell ? 1 : 0) + (blood.rebirthAutoSpell ? 1 : 0);
                var estimates = new[] {
                    BloodGoalEstimate.Calculate(EffectiveCustomBloodSpaghettiGoalPercent(plan), false,
                        BloodGoalEstimate.WholePercent(Character.bloodMagicController.lootBonus()), blood.lootSpellBlood,
                        blood.bloodPoints, Character.bloodSpells.minLootBlood(), production, count, blood.lootAutoSpell),
                    BloodGoalEstimate.Calculate(EffectiveCustomBloodCounterfeitGoalPercent(plan), true,
                        BloodGoalEstimate.WholePercent(Character.bloodMagicController.goldBonus()), blood.goldSpellBlood,
                        blood.bloodPoints, Character.bloodSpells.minGoldBlood(), production, count, blood.goldAutoSpell)
                };
                foreach (var estimate in estimates)
                {
                    estimate.ProductionAssumption = optimal.Success
                        ? $"Optimal ritual {optimal.RitualIndex} with all {optimal.MagicAllocated:n0} magic"
                        : optimal.Message;
                    if (!optimal.Success && estimate.Status == "No blood production")
                        estimate.Status = optimal.Message;
                }
                status = "available";
                reason = string.Empty;
                return estimates;
            }
            catch (Exception e)
            {
                status = "error";
                reason = LogError("Blood goal estimates", e);
                return new BloodGoalEstimate[0];
            }
        }

        private static void WriteExternalUiBloodState(JSONObject root)
        {
            string status, reason;
            var estimates = GetBloodGoalEstimates(out status, out reason, out var plan);
            var blood = new JSONObject();
            blood["planName"] = plan != null ? Settings.CustomPlanFile : string.Empty;
            blood["autoBloodSpellsEnabled"] = EffectiveCustomAutoBloodSpellsEnabled(plan);
            blood["available"] = status == "available";
            blood["status"] = status;
            blood["reason"] = reason;
            blood["spaghetti"] = JSONNull.CreateOrGet();
            blood["counterfeit"] = JSONNull.CreateOrGet();
            for (var i = 0; i < estimates.Length; i++)
            {
                var estimate = estimates[i];
                var goal = new JSONObject();
                goal["goalPercent"] = estimate.GoalPercent;
                goal["currentPercent"] = estimate.CurrentPercent;
                goal["requiredBlood"] = estimate.RequiredBlood.HasValue ? (JSONNode)new JSONNumber(estimate.RequiredBlood.Value) : JSONNull.CreateOrGet();
                goal["remainingBlood"] = estimate.RemainingBlood.HasValue ? (JSONNode)new JSONNumber(estimate.RemainingBlood.Value) : JSONNull.CreateOrGet();
                goal["bloodPerSecond"] = estimate.BloodPerSecond;
                goal["estimatedSeconds"] = estimate.EstimatedSeconds.HasValue ? (JSONNode)new JSONNumber(estimate.EstimatedSeconds.Value) : JSONNull.CreateOrGet();
                goal["status"] = estimate.Status;
                goal["productionAssumption"] = estimate.ProductionAssumption;
                blood[i == 0 ? "spaghetti" : "counterfeit"] = goal;
            }
            root["bloodGoals"] = blood;

            var iron = new JSONObject();
            iron["planName"] = plan != null ? Settings.CustomPlanFile : string.Empty;
            iron["saveSeconds"] = EffectiveCustomIronPillSaveSeconds(plan);
            iron["minWorth"] = EffectiveCustomIronPillMinWorth(plan);
            iron["available"] = false;
            iron["eligible"] = JSONNull.CreateOrGet();
            iron["status"] = "unavailable";
            iron["reason"] = "Injector, settings, plan store, or Blood Magic game state is not available.";
            try
            {
                if (reference != null && Settings != null && CustomPlanStore != null && Character?.rebirthTime != null
                    && Character.bloodMagic != null && Character.bloodSpells != null)
                {
                    if (status == "error" && plan == null && Settings.UseCustomRebirthPlan)
                        throw new InvalidOperationException(reason);
                    var blocker = reference.CustomIronPillEligibilityBlocker(plan);
                    iron["available"] = true;
                    iron["eligible"] = blocker.Length == 0;
                    iron["status"] = blocker.Length == 0 ? "eligible" : "blocked";
                    iron["reason"] = blocker;
                    iron["cooldownRemainingSeconds"] = LoadoutPresetNumber(reference.IronPillCooldownRemainingSeconds());
                    iron["bloodPoints"] = LoadoutPresetNumber(Character.bloodMagic.bloodPoints);
                    iron["currentWorth"] = LoadoutPresetNumber(reference.CustomIronPillWorth());
                    iron["minimumBlood"] = Character.bloodSpells.minAdventureBlood();
                    iron["ritualMagic"] = ToJsonArray(Character.bloodMagic.ritual.Select(x => x.magic.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray());
                    iron["lootAutoSpell"] = Character.bloodMagic.lootAutoSpell;
                    iron["goldAutoSpell"] = Character.bloodMagic.goldAutoSpell;
                    iron["numberAutoSpell"] = Character.bloodMagic.rebirthAutoSpell;
                    iron["reservingBlood"] = reference.CustomIronPillPrepActive();
                    iron["savedSeconds"] = reference.CustomIronPillPrepActive()
                        ? Math.Max(0d, Character.rebirthTime.totalseconds - reference._customIronPillPrepStartedAt) : 0d;
                    iron["prepLimitSeconds"] = IronPillAutomation.PrepLimitSeconds(EffectiveCustomIronPillSaveSeconds(plan));
                    iron["retryInSeconds"] = plan != null && reference._customIronPillPlanName == Settings.CustomPlanFile
                        ? Math.Max(0d, reference._customIronPillRetryAt - Character.rebirthTime.totalseconds) : 0d;
                    if (reference.CustomIronPillPrepActive()) iron["status"] = "saving";
                }
            }
            catch (Exception e)
            {
                iron["status"] = "error";
                iron["reason"] = LogError("Iron Pill eligibility", e);
            }
            root["ironPill"] = iron;
        }

        private bool CustomPlanResourceAutomationActive(NGUInjector.CustomPlans.CustomPlan plan)
        {
            return plan != null
                   && (Settings.UseCustomRebirthPlan || _globalPresetManualId != null
                       || GetActiveCustomFocusGoal().HasValue && GetActiveCustomFocusGoal() != CustomFocusGoal.Fight
                           && GetActiveCustomFocusGoal() != CustomFocusGoal.ManualTitans);
        }

        // Shared by reservation and read-only metadata; changing UI metadata must not change policy.
        private string CustomIronPillEligibilityBlocker(NGUInjector.CustomPlans.CustomPlan plan)
        {
            if (Settings == null) return "Settings are not loaded.";
            if (plan == null && (_globalPresetManualId != null || GetActiveCustomFocusGoal().HasValue))
                plan = CustomPlanStore?.Load(Settings.CustomPlanFile);
            if (Character?.rebirthTime == null || Character.bloodMagic == null || Character.bloodSpells == null)
                return "Blood Magic game state is not available.";
            if (_manualRebirthPending) return "Manual rebirth is pending.";
            if (!Settings.ManageMagic) return "Magic management is disabled.";
            if (_customPreRebirthPrepRunning) return "Pre-rebirth preparation is running.";
            if (BossPushRunning) return "Boss push is running.";
            if (!CustomPlanResourceAutomationActive(plan))
            {
                if (plan == null) return "No custom plan is loaded.";
                return "No resource allocation activity is selected.";
            }
            if (!plan.AutoIronPillEnabled) return "The selected plan's automatic Iron Pill is disabled.";
            if (CustomRebirthReady(plan)) return "Automatic rebirth is ready.";
            return IronPillAutomation.EligibilityBlocker(EffectiveCustomAutoBloodSpellsEnabled(Settings.UseCustomRebirthPlan ? plan : null), true,
                Character.rebirthTime.totalseconds, Character.bossID);
        }

        private static JSONArray ToJsonArray(string[] values)
        {
            var array = new JSONArray();
            if (values == null)
                return array;

            foreach (var value in values)
            {
                array.Add(value ?? string.Empty);
            }

            return array;
        }

        private static string ReadJsonString(JSONNode root, string key, string fallback)
        {
            if (root == null || !root.IsObject)
                return fallback;

            var value = root[key];
            if (value == null || value.IsNull)
                return fallback;

            var text = value.Value;
            if (!value.IsString)
                throw new FormatException(key + " must be a string.");
            return string.IsNullOrWhiteSpace(text) ? fallback : text;
        }

        private static void WriteJsonAtomic(string path, JSONNode root)
        {
            JsonFiles.WriteAtomic(path, root.ToString(2));
        }

        private static bool IsExternalUiCommandId(string id)
        {
            return !string.IsNullOrEmpty(id) && id.Length <= 128 && char.IsLetterOrDigit(id[0])
                && id.All(c => c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '-' || c == '_');
        }

        private static DateTime ReadExternalUiTimestamp(JSONNode node, DateTime fallback)
        {
            if (node == null || node.IsNull)
                return fallback;
            long milliseconds;
            if ((node.IsString || node.IsNumber) && long.TryParse(node.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out milliseconds))
                return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(milliseconds);
            if (node.IsNumber && node.AsDouble == Math.Truncate(node.AsDouble))
                return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(node.AsDouble);
            DateTimeOffset timestamp;
            if (node.IsString && DateTimeOffset.TryParse(node.Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out timestamp))
                return timestamp.UtcDateTime;
            throw new FormatException("Timestamp must be Unix epoch milliseconds or an ISO-8601 string.");
        }

        private static T ReadExternalUiEnum<T>(JSONNode args, string key) where T : struct
        {
            var node = args?[key];
            var text = node != null && node.IsNumber && node.AsDouble >= int.MinValue && node.AsDouble <= int.MaxValue
                && node.AsDouble == Math.Truncate(node.AsDouble) ? node.AsInt.ToString(CultureInfo.InvariantCulture) : node?.Value;
            T value;
            if (node == null || (!node.IsString && !node.IsNumber)
                || !Enum.TryParse(text, false, out value) || !Enum.IsDefined(typeof(T), value))
                throw new FormatException("Invalid " + key + " for " + typeof(T).Name + ".");
            return value;
        }

        private static void SafeDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // The durable claim/result prevents replay even when duplicate cleanup fails.
            }
        }
    }
}
