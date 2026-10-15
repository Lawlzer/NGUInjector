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
        private const int ExternalUiMaxCommandBytes = 1024 * 1024;
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
            "resetRebirthPrep", "reloadSettings", "quickSave", "status", "unload"
        };
        private DateTime _externalUiLastPump = DateTime.MinValue;
        private DateTime _externalUiLastRuntimeWrite = DateTime.MinValue;
        private static JSONObject _externalUiLastRuntimeSnapshot;

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
                var message = ExecuteExternalUiCommand(command, args, out data);
                // Existing action handlers sometimes return LogError instead of throwing.
                var success = message == null || !message.StartsWith("ERROR in ", StringComparison.Ordinal);
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
            switch (command)
            {
                case "applySettingsPatch":
                    if (Settings == null)
                        throw new InvalidOperationException("Settings are not loaded yet.");
                    Settings.ApplyPatch(args?["patch"]);
                    LoadAllocation();
                    return "Settings patch applied and saved.";
                case "savePlan":
                    if (CustomPlanStore == null)
                        throw new InvalidOperationException("Custom plan store is not loaded yet.");
                    var name = ReadJsonString(args, "name", string.Empty);
                    var revisionNode = args?["expectedRevision"];
                    if (revisionNode == null || !revisionNode.IsString)
                        throw new FormatException("args.expectedRevision is required and must be a string.");
                    var savedRevision = CustomPlanStore.SaveExternalPlan(name, args?["plan"], revisionNode.Value);
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
                case "optimizeGear":
                case "previewGear":
                    var gearGoal = ReadExternalUiEnum<GearOptimizationGoal>(args, "goal");
                    var fightMode = args != null && args["fightScoreMode"] != null
                        ? ReadExternalUiEnum<FightScoreMode>(args, "fightScoreMode") : GetActiveFightScoreMode();
                    var result = command == "previewGear" ? GearOptimizer.Optimize(gearGoal, fightMode)
                        : GearOptimizer.OptimizeAndEquip(gearGoal, fightMode);
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
                        : AugmentOptimizer.OptimizeAndAllocate(expectedSeconds);
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
            if (Settings == null || !Settings.LoadSettings())
                throw new InvalidOperationException("Settings reload failed; live settings retained.");

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
            root["planVersion"] = NGUInjector.CustomPlans.CustomPlanStore.CurrentPlanVersion;
            root["updatedAt"] = DateTime.Now.ToString("o");
            root["status"] = reference?._startupError != null ? "failed" : reference != null && reference._unloaded ? "unloaded"
                : reference == null || !reference._started ? "starting" : "running";
            root["customStatus"] = GetCustomAutomationStatus();
            root["activeFocusGoal"] = reference?.GetActiveCustomFocusGoal()?.ToString() ?? string.Empty;
            root["appliedFocusGoal"] = reference?.GetAppliedCustomFocusGoal() ?? string.Empty;
            root["focusStatus"] = reference?.GetCustomFocusStatus() ?? "idle";
            root["planProgress"] = reference?.GetCustomPlanProgress() ?? UnavailableCustomPlanProgress();
            root["nguAllocation"] = reference?.GetCustomNguRuntime() ?? new JSONObject();
            root["bossPushRunning"] = reference != null && reference.BossPushRunning;
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
            settings["customAdventureZone"] = Settings.CustomAdventureZone;
            settings["customAdventureGoldPush"] = Settings.CustomAdventureGoldPush;
            settings["manualAdventureEnabled"] = Settings.ManualAdventureEnabled;
            settings["manualAdventureZone"] = Settings.ManualAdventureZone;
            settings["manualAdventureBossOnly"] = Settings.ManualAdventureBossOnly;
            settings["customAutoBoostEnabled"] = Settings.CustomAutoBoostEnabled;
            settings["customAutoMoneyPitEnabled"] = Settings.CustomAutoMoneyPitEnabled;
            settings["customAutoBloodSpellsEnabled"] = Settings.CustomAutoBloodSpellsEnabled;
            settings["disableOverlay"] = Settings.DisableOverlay;
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
            return GetBloodGoalEstimates(out status, out reason);
        }

        private static BloodGoalEstimate[] GetBloodGoalEstimates(out string status, out string reason)
        {
            status = "unavailable";
            reason = Settings == null ? "Settings are not loaded." : "Blood Magic game state is not available.";
            try
            {
                if (Settings == null || Character?.bloodMagic == null || Character.bloodMagicController == null || Character.bloodSpells == null)
                    return new BloodGoalEstimate[0];

                var blood = Character.bloodMagic;
                var optimal = BloodMagicOptimizer.OptimizeBest();
                var production = optimal.Success ? optimal.BloodPerSecond : 0d;
                var count = (blood.lootAutoSpell ? 1 : 0) + (blood.goldAutoSpell ? 1 : 0) + (blood.rebirthAutoSpell ? 1 : 0);
                var estimates = new[] {
                    BloodGoalEstimate.Calculate(Settings.CustomBloodSpaghettiGoalPercent, false,
                        BloodGoalEstimate.WholePercent(Character.bloodMagicController.lootBonus()), blood.lootSpellBlood,
                        blood.bloodPoints, Character.bloodSpells.minLootBlood(), production, count, blood.lootAutoSpell),
                    BloodGoalEstimate.Calculate(Settings.CustomBloodCounterfeitGoalPercent, true,
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
            var estimates = GetBloodGoalEstimates(out status, out reason);
            var blood = new JSONObject();
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
            iron["available"] = false;
            iron["eligible"] = JSONNull.CreateOrGet();
            iron["status"] = "unavailable";
            iron["reason"] = "Injector, settings, plan store, or Blood Magic game state is not available.";
            try
            {
                if (reference != null && Settings != null && CustomPlanStore != null && Character?.rebirthTime != null
                    && Character.bloodMagic != null && Character.bloodSpells != null)
                {
                    var blocker = reference.CustomIronPillEligibilityBlocker(CustomPlanStore.Load(Settings.CustomPlanFile));
                    iron["available"] = true;
                    iron["eligible"] = blocker.Length == 0;
                    iron["status"] = blocker.Length == 0 ? "eligible" : "blocked";
                    iron["reason"] = blocker;
                    iron["reservingBlood"] = reference.CustomIronPillPrepActive();
                    iron["savedSeconds"] = reference.CustomIronPillPrepActive()
                        ? Math.Max(0d, Character.rebirthTime.totalseconds - reference._customIronPillPrepStartedAt) : 0d;
                    iron["prepLimitSeconds"] = IronPillAutomation.PrepLimitSeconds(Settings.CustomIronPillSaveSeconds);
                    iron["retryInSeconds"] = Math.Max(0d, reference._customIronPillRetryAt - Character.rebirthTime.totalseconds);
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
                   && Settings.UseCustomRebirthPlan
                   && Settings.AutoRebirth
                   && plan.AutoRebirthEnabled;
        }

        // Shared by reservation and read-only metadata; changing UI metadata must not change policy.
        private string CustomIronPillEligibilityBlocker(NGUInjector.CustomPlans.CustomPlan plan)
        {
            if (Settings == null) return "Settings are not loaded.";
            if (Character?.rebirthTime == null || Character.bloodMagic == null || Character.bloodSpells == null)
                return "Blood Magic game state is not available.";
            if (_manualRebirthPending) return "Manual rebirth is pending.";
            if (_customPreRebirthPrepRunning) return "Pre-rebirth preparation is running.";
            if (BossPushRunning) return "Boss push is running.";
            if (!CustomPlanResourceAutomationActive(plan))
            {
                if (plan == null) return "No custom plan is loaded.";
                if (!Settings.UseCustomRebirthPlan) return "The custom plan is not running.";
                if (!Settings.AutoRebirth) return "Custom auto-rebirth is disabled.";
                return "The selected plan's auto-rebirth is disabled.";
            }
            if (!plan.AutoIronPillEnabled) return "The selected plan's automatic Iron Pill is disabled.";
            if (CustomRebirthReady(plan)) return "Automatic rebirth is ready.";
            return IronPillAutomation.EligibilityBlocker(Settings.CustomAutoBloodSpellsEnabled, true,
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
