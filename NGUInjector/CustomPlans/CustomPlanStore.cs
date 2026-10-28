using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SimpleJSON;

namespace NGUInjector.CustomPlans
{
    internal class CustomPlanStore
    {
        private const string TwentyFourHourPlanName = "24hr";
        private const string FifteenMinuteNumberPlanName = "15m-NUMBER-increase";
        private const string ThirtyMinuteNumberPlanName = "30m-NUMBER-increase";
        private const string OneHourOneMinuteNumberPlanName = "1h-1m-NUMBER-increase";
        private const string OneHourThreeMinuteRebirthPlanName = "1h-3m-rebirth";
        private const string InfinitePlanName = "infinite";
        private const int MaxRebirthHistoryEntries = 20;
        internal const int CurrentPlanVersion = 18;
        internal const string ExternalUiRevisionField = "__externalUiRevision";
        private static readonly Encoding PlanEncoding = new UTF8Encoding(false, true);
        private readonly string _plansDir;
        internal PresetLibraryStore PresetLibrary { get; private set; }

        internal CustomPlanStore(string plansDir)
        {
            _plansDir = plansDir;
            Directory.CreateDirectory(_plansDir);
            PresetLibrary = new PresetLibraryStore(Path.GetDirectoryName(Path.GetFullPath(_plansDir)), _plansDir);
            EnsureBuiltInPlans();
            RetireObsoleteBuiltIns();
        }

        internal string[] ListPlanNames()
        {
            EnsureBuiltInPlans();
            return Directory.GetFiles(_plansDir, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(x => !string.IsNullOrEmpty(x))
                .OrderBy(x => x)
                .ToArray();
        }

        internal bool PlanExists(string planName)
        {
            return File.Exists(GetPlanPath(planName));
        }

        internal string EnsureDefaultPlan()
        {
            EnsureBuiltInPlans();
            return TwentyFourHourPlanName;
        }

        private void EnsureBuiltInPlans()
        {
            EnsurePlan(TwentyFourHourPlanName, CustomPlan.TwentyFourHourPlan());
            EnsurePlan(FifteenMinuteNumberPlanName, CustomPlan.NumberIncreasePlan(FifteenMinuteNumberPlanName, 900, 900));
            EnsurePlan(ThirtyMinuteNumberPlanName, CustomPlan.NumberIncreasePlan(ThirtyMinuteNumberPlanName, 1800, 1800));
            EnsurePlan(OneHourOneMinuteNumberPlanName, CustomPlan.NumberIncreasePlan(OneHourOneMinuteNumberPlanName, 3660, 3600));
        }

        private void EnsurePlan(string planName, CustomPlan plan)
        {
            if (File.Exists(GetPlanPath(planName)))
                return;

            plan.Name = planName;
            if (planName == TwentyFourHourPlanName)
            {
                var seconds = new[] {3600, 1800, 1800, 10800};
                for (var i = 0; i < seconds.Length; i++)
                {
                    plan.Stages[i].DurationSeconds = seconds[i];
                    plan.Stages[i].DurationUnit = seconds[i] % 3600 == 0 ? "h" : "min";
                    plan.Stages[i].Percent = 1;
                }
                plan.Stages[4].Percent = 1;
            }
            Save(planName, plan);
        }

        private void RetireObsoleteBuiltIns()
        {
            lock (PresetLibraryStore.SyncRoot)
            {
                var runtimeDir = Path.GetDirectoryName(Path.GetFullPath(_plansDir));
                var backupDir = Path.Combine(runtimeDir, "retired-custom-plans-number-v1");
                var completed = Path.Combine(backupDir, "completed.json");
                if (File.Exists(completed)) return;
                Directory.CreateDirectory(backupDir);
                var obsolete = new[] {"default", InfinitePlanName, OneHourThreeMinuteRebirthPlanName};
                // A moved archive is a per-file receipt, even if initialization stopped before completion.
                var paths = obsolete.Select(GetPlanPath).Where(path => File.Exists(path)
                    && !File.Exists(Path.Combine(backupDir, Path.GetFileName(path)) + ".retired")).ToList();
                var settingsPath = Path.Combine(runtimeDir, "settings.json");
                var settings = File.Exists(settingsPath) ? JsonFiles.ParseObject(File.ReadAllText(settingsPath)) : null;
                var selected = ReadString(settings, "_customPlanFile", string.Empty);
                var replaceSelection = obsolete.Contains(selected, StringComparer.OrdinalIgnoreCase);
                if (replaceSelection) paths.Add(settingsPath);
                foreach (var path in paths)
                {
                    var backup = Path.Combine(backupDir, Path.GetFileName(path));
                    if (!File.Exists(backup)) File.Copy(path, backup);
                    if (!File.ReadAllBytes(path).SequenceEqual(File.ReadAllBytes(backup)))
                        throw new IOException("Custom plan retirement backup differs from '" + path + "'; originals were retained.");
                }
                if (replaceSelection)
                {
                    // Persist fallback before moving any legacy selection; settings CAS failure leaves plans intact.
                    if (Main.Settings != null && string.Equals(Main.Settings.CustomPlanFile, selected, StringComparison.OrdinalIgnoreCase))
                    {
                        var patch = new JSONObject();
                        patch["_customPlanFile"] = TwentyFourHourPlanName;
                        Main.Settings.ApplyPatch(patch);
                    }
                    else
                    {
                        settings["_customPlanFile"] = TwentyFourHourPlanName;
                        JsonFiles.WriteAtomic(settingsPath, settings.ToString(4));
                    }
                    paths.Remove(settingsPath);
                }
                foreach (var path in paths)
                {
                    var backup = Path.Combine(backupDir, Path.GetFileName(path));
                    var retired = backup + ".retired";
                    // Move the current pathname without overwrite; never delete a possibly replaced pathname.
                    File.Move(path, retired);
                    if (!File.ReadAllBytes(retired).SequenceEqual(File.ReadAllBytes(backup)))
                        throw new IOException("Custom plan changed during retirement; both versions were preserved in '" + backupDir + "'.");
                }
                JsonFiles.WriteAtomic(completed, "{\"Version\":1}");
            }
        }

        internal CustomPlan Load(string planName)
        {
            planName = NormalizePlanName(planName);
            var path = GetPlanPath(planName);
            try
            {
                if (!File.Exists(path))
                    throw new FileNotFoundException("Plan does not exist: " + planName, path);
                var bytes = File.ReadAllBytes(path);
                var plan = DeserializePlan(PlanEncoding.GetString(bytes).TrimStart('\uFEFF'));
                plan.RuntimeRevision = JsonFiles.ComputeRevision(bytes);
                NormalizePlan(plan, planName);
                PresetLibrary.MigratePlan(plan, planName);
                return plan;
            }
            catch (Exception e)
            {
                Main.Log($"Failed to load custom plan '{planName}': {e.Message}");
                throw;
            }
        }

        internal string CreatePlan(string requestedName, CustomPlan source = null)
        {
            var name = GetAvailablePlanName(requestedName);
            var plan = source == null ? new CustomPlan() : Clone(source);
            plan.Name = name;
            plan.RebirthHistory = new CustomRebirthHistoryEntry[0];
            Save(name, plan);
            return name;
        }

        internal string DeletePlan(string planName)
        {
            planName = NormalizePlanName(planName);
            if (BuiltInTemplate(planName) != null)
                throw new InvalidOperationException("Built-in plans cannot be deleted; copy one to make a removable plan.");
            var plans = ListPlanNames();
            if (plans.Length <= 1)
                return plans[0];
            var path = GetPlanPath(planName);
            if (File.Exists(path))
            {
                File.Delete(path);
                Main.reference?.ForgetStageProgress(planName);
            }
            return EnsureDefaultPlan();
        }

        internal string DeleteExternalPlan(string planName)
        {
            ValidateExternalPlanName(planName);
            if (BuiltInTemplate(planName) != null)
                throw new InvalidOperationException("Built-in plans cannot be deleted; copy one to make a removable plan.");
            var selected = Main.Settings != null && string.Equals(Main.Settings.CustomPlanFile, planName, StringComparison.OrdinalIgnoreCase);
            if (selected && Main.Settings.UseCustomRebirthPlan)
                throw new InvalidOperationException("The selected running plan cannot be deleted. Pause it first.");
            var plans = ListPlanNames();
            if (plans.Length <= 1)
                throw new InvalidOperationException("The last plan cannot be deleted.");

            var path = GetPlanPath(planName);
            if (!File.Exists(path))
                throw new FileNotFoundException("Plan does not exist: " + planName);
            var fallback = EnsureDefaultPlan();
            if (selected)
            {
                // Persist selection before removing the file; a failed settings write leaves it intact.
                var patch = new JSONObject();
                patch["_customPlanFile"] = fallback;
                Main.Settings.ApplyPatch(patch);
            }
            File.Delete(path);
            Main.reference?.ForgetStageProgress(planName);
            if (selected)
                Main.reference?.InvalidateCustomRuntimePlanCache();
            return fallback;
        }

        internal void Save(string planName, CustomPlan plan)
        {
            lock (PresetLibraryStore.SyncRoot)
            {
                planName = NormalizePlanName(planName);
                NormalizePlan(plan, planName);
                PresetLibrary.MigratePlan(plan, planName);
                PresetLibrary.ValidateReferences(plan);
                Main.reference?.ValidateScheduledPlanEdit(planName, plan);
                var path = GetPlanPath(planName);
                var serialized = SerializePlan(plan);
                var previous = File.Exists(path) ? File.ReadAllText(path) : null;
                var changed = previous != serialized;
                var scheduleOnly = false;
                if (changed && previous != null)
                {
                    try
                    {
                        var before = JsonFiles.ParseObject(SerializePlan(ReadForPresetMigration(previous.TrimStart('\uFEFF'), planName)));
                        var after = JsonFiles.ParseObject(serialized);
                        foreach (var root in new[] { before, after })
                            foreach (var stage in root["Stages"].AsArray.Children) stage.Remove("FightSchedule");
                        scheduleOnly = before.ToString() == after.ToString();
                    }
                    catch (FormatException) { /* Replacing invalid input requires full invalidation. */ }
                }
                JsonFiles.WriteAtomic(path, serialized);
                plan.RuntimeRevision = JsonFiles.ComputeRevision(PlanEncoding.GetBytes(serialized));
                if (previous == null)
                    Main.reference?.ForgetStageProgress(planName);
                if (changed && string.Equals(Main.Settings?.CustomPlanFile, planName, StringComparison.OrdinalIgnoreCase))
                {
                    if (scheduleOnly) Main.reference?.InvalidateCustomFightSchedulePlanCache();
                    else Main.reference?.InvalidateCustomRuntimePlanCache();
                }
            }
        }

        internal JSONObject ReadExternalPlan(string name)
        {
            ValidateExternalPlanName(name);
            var bytes = File.ReadAllBytes(GetPlanPath(name));
            var plan = DeserializePlan(PlanEncoding.GetString(bytes).TrimStart('\uFEFF'));
            NormalizePlan(plan, name);
            PresetLibrary.MigratePlan(plan, name);
            var root = JsonFiles.ParseObject(SerializePlan(plan));
            root[ExternalUiRevisionField] = JsonFiles.ComputeRevision(bytes);
            return root;
        }

        internal string SaveExternalPlan(string name, JSONNode root, string expectedRevision)
        {
            lock (PresetLibraryStore.SyncRoot)
            {
                ValidateExternalPlanName(name);
                if (root == null || !root.IsObject)
                    throw new FormatException("args.plan must be a JSON object.");
                if (expectedRevision == null || expectedRevision.Length != 0 && (expectedRevision.Length != 64 || !expectedRevision.All(Uri.IsHexDigit)))
                    throw new FormatException("args.expectedRevision must be a SHA-256 hex token, or an empty string for a nonexistent plan.");
                var path = GetPlanPath(name);
                var bytes = File.Exists(path) ? File.ReadAllBytes(path) : null;
                var revision = bytes == null ? string.Empty : JsonFiles.ComputeRevision(bytes);
                if (!string.Equals(expectedRevision, revision, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Plan revision conflict; readPlan again before saving.");
                var plan = DeserializePlan(root.ToString());
                if (bytes != null && (root["Version"] == null || plan.Version < CurrentPlanVersion))
                    throw new InvalidOperationException("Read the legacy plan through readPlan before editing and saving it.");
                if (bytes != null)
                {
                    var previous = JsonFiles.ParseObject(SerializePlan(ReadForPresetMigration(PlanEncoding.GetString(bytes).TrimStart('\uFEFF'), name)));
                    var proposed = JsonFiles.ParseObject(SerializePlan(plan));
                    foreach (var field in new[] {"LoadoutPresets", "GoalPresets", "ActiveGoalPresetIndex"})
                        if (GetExistingNode(proposed, field)?.ToString() != GetExistingNode(previous, field)?.ToString())
                            throw new InvalidOperationException(field + " is historical provenance; edit the global preset library instead.");
                }
                // The injector owns history. Do not replace it with a stale editor snapshot.
                plan.RebirthHistory = bytes != null
                    ? DeserializePlan(PlanEncoding.GetString(bytes).TrimStart('\uFEFF')).RebirthHistory : new CustomRebirthHistoryEntry[0];
                plan.Name = name;
                Save(name, plan);
                return JsonFiles.ComputeRevision(File.ReadAllBytes(path));
            }
        }

        internal string CreateExternalPlan(string requestedName, JSONNode source)
        {
            ValidateExternalPlanName(requestedName);
            if (source != null && !source.IsNull && !source.IsObject)
                throw new FormatException("args.source must be a plan JSON object or null.");
            return CreatePlan(requestedName, source == null || source.IsNull ? null : DeserializePlan(source.ToString()));
        }

        private void ValidateExternalPlanName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || name != NormalizePlanName(name))
                throw new FormatException("Plan name must be an unmodified valid file stem of at most 128 characters.");
            var device = name.Split('.')[0].ToUpperInvariant();
            if (device == "CON" || device == "PRN" || device == "AUX" || device == "NUL"
                || device.Length == 4 && (device.StartsWith("COM") || device.StartsWith("LPT")) && device[3] >= '1' && device[3] <= '9')
                throw new FormatException("Plan name must not be a reserved Windows device name.");
        }

        internal void AddRebirthHistory(string planName, CustomRebirthHistoryEntry entry)
        {
            if (entry == null)
                return;

            var plan = Load(planName);
            var history = (plan.RebirthHistory ?? new CustomRebirthHistoryEntry[0]).ToList();
            history.Insert(0, entry);
            plan.RebirthHistory = history.Take(MaxRebirthHistoryEntries).ToArray();
            Save(planName, plan);
        }

        internal string NormalizePlanName(string planName)
        {
            if (string.IsNullOrWhiteSpace(planName))
            {
                return TwentyFourHourPlanName;
            }

            var invalid = Path.GetInvalidFileNameChars();
            var chars = planName.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray();
            var safe = new string(chars).Trim(' ', '.');
            return string.IsNullOrWhiteSpace(safe) ? TwentyFourHourPlanName : safe;
        }

        private string GetPlanPath(string planName)
        {
            return Path.Combine(_plansDir, NormalizePlanName(planName) + ".json");
        }

        private string GetAvailablePlanName(string requestedName)
        {
            var baseName = NormalizePlanName(requestedName);
            var name = baseName;
            var suffix = 2;
            while (PlanExists(name))
            {
                var extension = "-" + suffix++;
                name = baseName.Substring(0, Math.Min(baseName.Length, 128 - extension.Length)) + extension;
            }

            return name;
        }

        private static CustomPlan Clone(CustomPlan source)
        {
            return DeserializePlan(SerializePlan(source));
        }

        private static string SerializePlan(CustomPlan plan)
        {
            ValidatePlanVersion(plan.Version);
            plan.ValidateFightSchedules();
            var root = JsonFiles.ParseObject(plan.SourceJson?.ToString() ?? "{}");
            root.Remove(ExternalUiRevisionField);
            root.Remove("NguGoal");
            root.Remove("EnergyNguWeights");
            root.Remove("MagicNguWeights");
            root.Remove("ProtectedStageTiming");
            root["Version"] = plan.Version;
            root["Name"] = plan.Name;
            root["ActivePresetId"] = plan.ActivePresetId ?? string.Empty;
            var migrationIssues = new JSONArray();
            foreach (var issue in plan.PresetMigrationIssues ?? new string[0]) migrationIssues.Add(issue);
            root["PresetMigrationIssues"] = migrationIssues;
            root["AutoRebirthEnabled"] = plan.AutoRebirthEnabled;
            root["AutoIronPillEnabled"] = plan.AutoIronPillEnabled;
            ValidateBloodOverrides(plan);
            root.Remove("AutoBloodSpellsEnabled");
            root.Remove("BloodSpaghettiGoalPercent");
            root.Remove("BloodCounterfeitGoalPercent");
            root.Remove("IronPillSaveSeconds");
            root.Remove("IronPillMinWorth");
            if (plan.AutoBloodSpellsEnabled.HasValue) root["AutoBloodSpellsEnabled"] = plan.AutoBloodSpellsEnabled.Value;
            if (plan.BloodSpaghettiGoalPercent.HasValue) root["BloodSpaghettiGoalPercent"] = plan.BloodSpaghettiGoalPercent.Value;
            if (plan.BloodCounterfeitGoalPercent.HasValue) root["BloodCounterfeitGoalPercent"] = plan.BloodCounterfeitGoalPercent.Value;
            if (plan.IronPillSaveSeconds.HasValue) root["IronPillSaveSeconds"] = plan.IronPillSaveSeconds.Value;
            if (plan.IronPillMinWorth.HasValue) root["IronPillMinWorth"] = plan.IronPillMinWorth.Value;
            root["TimeTriggerEnabled"] = plan.TimeTriggerEnabled;
            root["TimeTriggerSeconds"] = plan.TimeTriggerSeconds;
            root["NumberTriggerEnabled"] = plan.NumberTriggerEnabled;
            root["NumberMultiplierTarget"] = plan.NumberMultiplierTarget;
            root["NumberClimbEnabled"] = plan.NumberClimbEnabled;
            root["NumberClimbMinSeconds"] = plan.NumberClimbMinSeconds;
            root["NumberClimbMultiplierTarget"] = plan.NumberClimbMultiplierTarget;
            root["TriggerMode"] = plan.TriggerMode;
            root["StageSchedulerEnabled"] = plan.StageSchedulerEnabled;
            root["StageDurationSeconds"] = plan.StageDurationSeconds;
            root["StageLoopStartIndex"] = plan.StageLoopStartIndex;
            root["Stages"] = SerializeStages(plan.Stages);
            root["AutoActivateFruits"] = plan.AutoActivateFruits;
            root["GearGoal"] = plan.GearGoal;
            root["FightScoreMode"] = plan.FightScoreMode;
            root["ActiveGoalPresetIndex"] = plan.ActiveGoalPresetIndex;
            root["GoalPresets"] = SerializeGoalPresets(plan.GoalPresets);
            ValidateLoadoutPresets(plan.LoadoutPresets);
            var loadoutPresets = new JSONArray();
            foreach (var preset in plan.LoadoutPresets)
                loadoutPresets.Add(preset.ToJson());
            root["LoadoutPresets"] = plan.SourceJson != null && !ReferenceEquals(GetExistingNode(plan.SourceJson, "LoadoutPresets"), null)
                ? JsonFiles.ParseObject(plan.SourceJson.ToString())["LoadoutPresets"] : loadoutPresets;
            foreach (var preset in root["LoadoutPresets"].Children)
            {
                preset.Remove("EnergyNguWeights");
                preset.Remove("MagicNguWeights");
            }
            root["PrepRequireMaxedFruit"] = plan.PrepRequireMaxedFruit;
            if (plan.RebirthPrepStartSeconds < 0)
                throw new FormatException("RebirthPrepStartSeconds must be a nonnegative whole Int32 number.");
            root["RebirthPrepStartSeconds"] = plan.RebirthPrepStartSeconds;
            root["RebirthHistory"] = SerializeHistory(plan.RebirthHistory);
            return root.ToString(4);
        }

        private static CustomPlan DeserializePlan(string text)
        {
            var root = JsonFiles.ParseObject(text);
            root.Remove(ExternalUiRevisionField);
            var plan = new CustomPlan { SourceJson = root };
            if (root == null || !root.IsObject)
                return plan;

            plan.GoalPresets = null;
            plan.Stages = null;
            plan.RebirthHistory = null;

            // Versionless documents predate the weights schema, unlike newly constructed models.
            var version = GetExistingNode(root, "Version");
            plan.Version = ReferenceEquals(version, null) ? 15 : ReadNonnegativeInt(version, "Version");
            ValidatePlanVersion(plan.Version);
            plan.Name = ReadString(root, "Name", plan.Name);
            plan.ActivePresetId = ReadPresetReference(root, "ActivePresetId");
            var issues = GetExistingNode(root, "PresetMigrationIssues");
            if (!ReferenceEquals(issues, null))
            {
                if (!issues.IsArray || issues.Children.Any(issue => !issue.IsString))
                    throw new FormatException("PresetMigrationIssues must be an array of strings.");
                plan.PresetMigrationIssues = issues.Children.Select(issue => issue.Value).ToArray();
            }
            var builtIn = BuiltInTemplate(plan.Name);
            // These retired names still identify shipped documents with omitted arrays/defaults.
            if (builtIn == null && string.Equals(plan.Name, InfinitePlanName, StringComparison.OrdinalIgnoreCase))
                builtIn = CustomPlan.InfinitePlan();
            if (builtIn == null && string.Equals(plan.Name, OneHourThreeMinuteRebirthPlanName, StringComparison.OrdinalIgnoreCase))
                builtIn = CustomPlan.OneHourThreeMinuteRebirthPlan();
            plan.AutoRebirthEnabled = ReadBool(root, "AutoRebirthEnabled", builtIn?.AutoRebirthEnabled ?? plan.AutoRebirthEnabled);
            var autoIronPill = GetExistingNode(root, "AutoIronPillEnabled");
            if (!ReferenceEquals(autoIronPill, null) && !autoIronPill.IsBoolean)
                throw new FormatException("AutoIronPillEnabled must be a boolean.");
            plan.AutoIronPillEnabled = ReferenceEquals(autoIronPill, null) || autoIronPill.AsBool;
            var autoBlood = GetExistingNode(root, "AutoBloodSpellsEnabled");
            if (!ReferenceEquals(autoBlood, null) && !autoBlood.IsNull)
            {
                if (!autoBlood.IsBoolean) throw new FormatException("AutoBloodSpellsEnabled must be a boolean or null.");
                plan.AutoBloodSpellsEnabled = autoBlood.AsBool;
            }
            plan.BloodSpaghettiGoalPercent = ReadOptionalBloodInt(root, "BloodSpaghettiGoalPercent", 100000);
            plan.BloodCounterfeitGoalPercent = ReadOptionalBloodInt(root, "BloodCounterfeitGoalPercent", 100000);
            plan.IronPillSaveSeconds = ReadOptionalBloodInt(root, "IronPillSaveSeconds", 604800);
            var worth = GetExistingNode(root, "IronPillMinWorth");
            if (!ReferenceEquals(worth, null) && !worth.IsNull)
            {
                if (!worth.IsNumber) throw new FormatException("IronPillMinWorth must be a number or null.");
                plan.IronPillMinWorth = worth.AsDouble;
            }
            ValidateBloodOverrides(plan);
            plan.TimeTriggerEnabled = ReadBool(root, "TimeTriggerEnabled", plan.TimeTriggerEnabled);
            plan.TimeTriggerSeconds = ReadInt(root, "TimeTriggerSeconds", plan.TimeTriggerSeconds);
            plan.NumberTriggerEnabled = ReadBool(root, "NumberTriggerEnabled", plan.NumberTriggerEnabled);
            plan.NumberMultiplierTarget = ReadDouble(root, "NumberMultiplierTarget", plan.NumberMultiplierTarget);
            plan.NumberClimbEnabled = ReadBool(root, "NumberClimbEnabled", plan.NumberClimbEnabled);
            plan.NumberClimbMinSeconds = ReadInt(root, "NumberClimbMinSeconds", plan.NumberClimbMinSeconds);
            plan.NumberClimbMultiplierTarget = ReadDouble(root, "NumberClimbMultiplierTarget", plan.NumberClimbMultiplierTarget);
            plan.TriggerMode = ReadInt(root, "TriggerMode", plan.TriggerMode);
            plan.StageSchedulerEnabled = ReadBool(root, "StageSchedulerEnabled", plan.StageSchedulerEnabled);
            var budget = GetExistingNode(root, "StageDurationSeconds");
            if (!ReferenceEquals(budget, null) && (!budget.IsNumber || budget.AsDouble < 0
                || budget.AsDouble > int.MaxValue || budget.AsDouble != Math.Truncate(budget.AsDouble)))
                throw new FormatException("StageDurationSeconds must be a nonnegative whole Int32 number.");
            plan.StageDurationSeconds = ReadInt(root, "StageDurationSeconds", plan.StageDurationSeconds);
            plan.StageLoopStartIndex = ReadInt(root, "StageLoopStartIndex", plan.StageLoopStartIndex);
            plan.AutoActivateFruits = ReadBool(root, "AutoActivateFruits", plan.AutoActivateFruits);
            plan.GearGoal = ReadInt(root, "GearGoal", plan.GearGoal);
            plan.FightScoreMode = ReadInt(root, "FightScoreMode", plan.FightScoreMode);
            plan.ActiveGoalPresetIndex = ReadInt(root, "ActiveGoalPresetIndex", plan.ActiveGoalPresetIndex);
            plan.PrepRequireMaxedFruit = ReadBool(root, "PrepRequireMaxedFruit", plan.PrepRequireMaxedFruit);
            var prepStart = GetExistingNode(root, "RebirthPrepStartSeconds");
            plan.RebirthPrepStartSeconds = ReferenceEquals(prepStart, null) ? 0 : ReadNonnegativeInt(prepStart, "RebirthPrepStartSeconds");

            var stagesNode = GetExistingNode(root, "Stages");
            if (stagesNode != null && stagesNode.IsArray && stagesNode.Count > 0)
            {
                plan.Stages = stagesNode.AsArray.Children.Select(DeserializeStage).Where(x => x != null).ToArray();
            }
            else
            {
                if (builtIn != null)
                    plan.Stages = builtIn.Stages;
            }

            var presetsNode = GetExistingNode(root, "GoalPresets");
            if (presetsNode != null && presetsNode.IsArray && presetsNode.Count > 0)
            {
                plan.GoalPresets = presetsNode.AsArray.Children.Select(DeserializeGoalPreset).Where(x => x != null).ToArray();
            }
            else if (builtIn != null)
            {
                plan.GoalPresets = builtIn.GoalPresets;
                plan.ActiveGoalPresetIndex = builtIn.ActiveGoalPresetIndex;
                plan.GearGoal = builtIn.GearGoal;
            }

            var historyNode = GetExistingNode(root, "RebirthHistory");
            if (historyNode != null && historyNode.IsArray)
            {
                plan.RebirthHistory = historyNode.AsArray.Children.Select(DeserializeHistoryEntry).Where(x => x != null).ToArray();
            }

            var loadoutPresetsNode = GetExistingNode(root, "LoadoutPresets");
            if (!ReferenceEquals(loadoutPresetsNode, null))
            {
                if (!loadoutPresetsNode.IsArray || loadoutPresetsNode.Count > 32)
                    throw new FormatException("LoadoutPresets must be an array of at most 32 presets.");
                plan.LoadoutPresets = loadoutPresetsNode.Children.Select(CustomLoadoutPreset.Parse).ToArray();
            }
            ValidateLoadoutPresets(plan.LoadoutPresets);
            plan.ValidateFightSchedules();
            return plan;
        }

        private static void ValidateLoadoutPresets(CustomLoadoutPreset[] presets)
        {
            if (presets == null || presets.Length > 32)
                throw new FormatException("LoadoutPresets must be an array of at most 32 presets.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var preset in presets)
            {
                CustomLoadoutPreset.Validate(preset);
                if (!ids.Add(preset.Id))
                    throw new FormatException("LoadoutPresets IDs must be unique: " + preset.Id);
            }
        }

        internal static int[] ReadLegacyNguWeights(JSONNode root, bool energy)
        {
            var key = energy ? "EnergyNguWeights" : "MagicNguWeights";
            var node = GetExistingNode(root, key);
            if (!ReferenceEquals(node, null)) return ReadNguWeights(node, key, energy ? 9 : 7);
            var version = ReadInt(root, "Version", 15);
            if (version > 15)
                return energy ? new[] {0, 0, 0, 0, 1, 0, 1, 0, 0} : new[] {1, 0, 0, 0, 0, 0, 0};
            var adventure = version >= 11 && ReadInt(root, "NguGoal", 0) == 1;
            return energy
                ? adventure ? new[] {0, 0, 0, 0, 1, 0, 1, 0, 0} : new[] {1, 1, 0, 1, 0, 1, 0, 0, 0}
                : adventure ? new[] {0, 0, 0, 0, 0, 0, 1} : new[] {0, 0, 1, 0, 0, 0, 0};
        }

        internal static int[] ReadNguWeights(JSONNode node, string key, int count)
        {
            if (ReferenceEquals(node, null) || !node.IsArray || node.Count != count)
                throw new FormatException(key + " must contain exactly " + count + " nonnegative whole Int32 numbers.");

            var weights = new int[count];
            for (var i = 0; i < count; i++)
                weights[i] = ReadNonnegativeInt(node[i], key + " entries");
            return weights;
        }

        private static int ReadNonnegativeInt(JSONNode entry, string key)
        {
            if (ReferenceEquals(entry, null) || !entry.IsNumber)
                throw new FormatException(key + " must be nonnegative whole Int32 numbers.");

            // Check the exact JSON token, not a rounded double (including tiny fractions).
            var parts = entry.Value.Split(new[] {'e', 'E'});
            var point = parts[0].IndexOf('.');
            var digits = parts[0].Replace(".", "").TrimStart('-').TrimStart('0');
            if (digits.Length == 0) return 0;
            long exponent = 0;
            if (parts[0][0] == '-' || parts.Length > 1 && !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out exponent))
                throw new FormatException(key + " must be nonnegative whole Int32 numbers.");
            var significant = digits.TrimEnd('0');
            var fractional = point < 0 ? 0 : parts[0].Length - point - 1;
            var adjustment = (long) digits.Length - significant.Length - fractional;
            if (exponent < -adjustment || exponent > 10 - significant.Length - adjustment)
                throw new FormatException(key + " must be nonnegative whole Int32 numbers.");
            var integer = significant + new string('0', (int) (exponent + adjustment));
            int value;
            if (!int.TryParse(integer, NumberStyles.None, CultureInfo.InvariantCulture, out value))
                throw new FormatException(key + " must be nonnegative whole Int32 numbers.");
            return value;
        }

        internal static JSONArray SerializeNguWeights(int[] weights, int count, string key)
        {
            if (weights == null || weights.Length != count || weights.Any(weight => weight < 0))
                throw new FormatException(key + " must contain exactly " + count + " nonnegative whole Int32 numbers.");
            var array = new JSONArray();
            foreach (var weight in weights)
                array.Add(weight);
            return array;
        }

        private static int? ReadOptionalBloodInt(JSONNode root, string key, int maximum)
        {
            var node = GetExistingNode(root, key);
            if (ReferenceEquals(node, null) || node.IsNull) return null;
            var value = ReadNonnegativeInt(node, key);
            if (value > maximum) throw new FormatException(key + " must be between 0 and " + maximum + ", or null.");
            return value;
        }

        private static void ValidateBloodOverrides(CustomPlan plan)
        {
            if (plan.BloodSpaghettiGoalPercent < 0 || plan.BloodSpaghettiGoalPercent > 100000)
                throw new FormatException("BloodSpaghettiGoalPercent must be between 0 and 100000, or null.");
            if (plan.BloodCounterfeitGoalPercent < 0 || plan.BloodCounterfeitGoalPercent > 100000)
                throw new FormatException("BloodCounterfeitGoalPercent must be between 0 and 100000, or null.");
            if (plan.IronPillSaveSeconds < 0 || plan.IronPillSaveSeconds > 604800)
                throw new FormatException("IronPillSaveSeconds must be between 0 and 604800, or null.");
            if (plan.IronPillMinWorth.HasValue && (double.IsNaN(plan.IronPillMinWorth.Value)
                || double.IsInfinity(plan.IronPillMinWorth.Value) || plan.IronPillMinWorth < 0 || plan.IronPillMinWorth > 1e8))
                throw new FormatException("IronPillMinWorth must be finite and between 0 and 100000000, or null.");
        }

        private static JSONArray SerializeStages(CustomPlanStage[] stages)
        {
            var array = new JSONArray();
            if (stages == null)
                return array;

            foreach (var stage in stages.Where(x => x != null))
            {
                ValidateAdventureDynamicWeight(stage);
                var item = JsonFiles.ParseObject(stage.SourceJson?.ToString() ?? "{}");
                item["Name"] = stage.Name;
                item["PresetId"] = stage.PresetId ?? string.Empty;
                item["Percent"] = stage.Percent;
                item["DurationSeconds"] = stage.DurationSeconds;
                item["DurationUnit"] = stage.DurationUnit;
                item["EnergyFocus"] = stage.EnergyFocus;
                item["MagicFocus"] = stage.MagicFocus;
                item["UseWandoosWhenTimeMachineLocked"] = stage.UseWandoosWhenTimeMachineLocked;
                item["ExecutionMode"] = stage.ExecutionMode;
                item["EnergyPriorities"] = SerializePriorities(stage.EnergyPriorities);
                item["MagicPriorities"] = SerializePriorities(stage.MagicPriorities);
                item["BlackBarLookaheadSeconds"] = stage.BlackBarLookaheadSeconds;
                item["GearGoal"] = stage.GearGoal;
                item["AdventureDynamicWeight"] = stage.AdventureDynamicWeight;
                item.Remove("FightOnEntry");
                item["FightSchedule"] = JSONNull.CreateOrGet();
                if (stage.FightSchedule != null)
                {
                    var schedule = stage.FightSchedule;
                    schedule.Validate();
                    var fight = JsonFiles.ParseObject(schedule.SourceJson?.ToString() ?? "{}");
                    fight["Id"] = schedule.Id;
                    fight["Anchor"] = schedule.Anchor;
                    fight["DelaySeconds"] = schedule.DelaySeconds;
                    fight["IntervalSeconds"] = schedule.IntervalSeconds;
                    item["FightSchedule"] = fight;
                }
                item["CompletionMode"] = stage.CompletionMode;
                item["TargetAdvancedTrainingPower"] = stage.TargetAdvancedTrainingPower.ToString(CultureInfo.InvariantCulture);
                item["TargetAdvancedTrainingToughness"] = stage.TargetAdvancedTrainingToughness.ToString(CultureInfo.InvariantCulture);
                array.Add(item);
            }

            return array;
        }

        private static CustomPlanStage DeserializeStage(JSONNode node, int index)
        {
            if (node == null || !node.IsObject)
                return null;

            var stage = new CustomPlanStage { SourceJson = node.AsObject };
            stage.Name = ReadString(node, "Name", stage.Name);
            stage.PresetId = ReadPresetReference(node, "PresetId");
            stage.Percent = ReadDouble(node, "Percent", stage.Percent);
            var duration = GetExistingNode(node, "DurationSeconds");
            if (!ReferenceEquals(duration, null) && (!duration.IsNumber || duration.AsDouble < 0 || duration.AsDouble > int.MaxValue
                || duration.AsDouble != Math.Truncate(duration.AsDouble)))
                throw new FormatException("Stage DurationSeconds must be a nonnegative whole Int32 number.");
            stage.DurationSeconds = ReferenceEquals(duration, null) ? 0 : duration.AsInt;
            var unit = GetExistingNode(node, "DurationUnit");
            if (!ReferenceEquals(unit, null) && !unit.IsString)
                throw new FormatException("Stage DurationUnit must be a string: %, s, min, or h.");
            stage.DurationUnit = ReferenceEquals(unit, null) ? "%" : unit.Value;
            stage.EnergyFocus = ReadInt(node, "EnergyFocus", stage.EnergyFocus);
            stage.MagicFocus = ReadInt(node, "MagicFocus", stage.MagicFocus);
            var lockedTimeMachineFallback = GetExistingNode(node, "UseWandoosWhenTimeMachineLocked");
            if (!ReferenceEquals(lockedTimeMachineFallback, null) && !lockedTimeMachineFallback.IsBoolean)
                throw new FormatException("Stage UseWandoosWhenTimeMachineLocked must be a boolean.");
            stage.UseWandoosWhenTimeMachineLocked = !ReferenceEquals(lockedTimeMachineFallback, null) && lockedTimeMachineFallback.AsBool;
            var execution = GetExistingNode(node, "ExecutionMode");
            if (!ReferenceEquals(execution, null) && (!execution.IsNumber || execution.AsDouble != 0 && execution.AsDouble != 1))
                throw new FormatException("Stage ExecutionMode must be 0 (Standard) or 1 (AutoAugmentsWandoos).");
            stage.ExecutionMode = ReferenceEquals(execution, null) ? 0 : execution.AsInt;
            stage.EnergyPriorities = DeserializePriorities(GetExistingNode(node, "EnergyPriorities"));
            stage.MagicPriorities = DeserializePriorities(GetExistingNode(node, "MagicPriorities"));
            stage.BlackBarLookaheadSeconds = ReadInt(node, "BlackBarLookaheadSeconds", stage.BlackBarLookaheadSeconds);
            stage.GearGoal = ReadInt(node, "GearGoal", stage.GearGoal);
            var adventureDynamicWeight = GetExistingNode(node, "AdventureDynamicWeight");
            if (!ReferenceEquals(adventureDynamicWeight, null) && !adventureDynamicWeight.IsNumber)
                throw new FormatException("Stage AdventureDynamicWeight must be a finite number between 0 and 1000000.");
            stage.AdventureDynamicWeight = ReferenceEquals(adventureDynamicWeight, null) ? 0d : adventureDynamicWeight.AsDouble;
            ValidateAdventureDynamicWeight(stage);
            var legacy = GetExistingNode(node, "FightOnEntry");
            if (!ReferenceEquals(legacy, null) && !legacy.IsBoolean)
                throw new FormatException("Stage FightOnEntry must be a boolean.");
            var fight = GetExistingNode(node, "FightSchedule");
            if (!ReferenceEquals(fight, null))
            {
                if (!fight.IsNull)
                {
                    if (!fight.IsObject) throw new FormatException("Stage FightSchedule must be an object or null.");
                    var id = GetExistingNode(fight, "Id");
                    if (ReferenceEquals(id, null) || !id.IsString)
                        throw new FormatException("FightSchedule.Id must be a string.");
                    stage.FightSchedule = new CustomFightSchedule
                    {
                        SourceJson = fight.AsObject,
                        Id = id.Value,
                        Anchor = ReadNonnegativeInt(GetExistingNode(fight, "Anchor"), "FightSchedule.Anchor"),
                        DelaySeconds = ReadNonnegativeInt(GetExistingNode(fight, "DelaySeconds"), "FightSchedule.DelaySeconds"),
                        IntervalSeconds = ReadNonnegativeInt(GetExistingNode(fight, "IntervalSeconds"), "FightSchedule.IntervalSeconds")
                    };
                    stage.FightSchedule.Validate();
                }
            }
            else
            {
                if (!ReferenceEquals(legacy, null) && legacy.AsBool)
                    stage.FightSchedule = new CustomFightSchedule {Id = "legacy-entry-v1-" + index.ToString(CultureInfo.InvariantCulture)};
            }
            stage.CompletionMode = ReadInt(node, "CompletionMode", stage.CompletionMode);
            stage.TargetAdvancedTrainingPower = ReadLong(node, "TargetAdvancedTrainingPower", stage.TargetAdvancedTrainingPower);
            stage.TargetAdvancedTrainingToughness = ReadLong(node, "TargetAdvancedTrainingToughness", stage.TargetAdvancedTrainingToughness);
            return stage;
        }

        private static void ValidateAdventureDynamicWeight(CustomPlanStage stage)
        {
            if (double.IsNaN(stage.AdventureDynamicWeight) || double.IsInfinity(stage.AdventureDynamicWeight)
                || stage.AdventureDynamicWeight < 0 || stage.AdventureDynamicWeight > 1e6)
                throw new FormatException("Stage AdventureDynamicWeight must be a finite number between 0 and 1000000.");
        }

        private static JSONArray SerializePriorities(string[] priorities)
        {
            var array = new JSONArray();
            if (priorities != null)
            {
                foreach (var priority in priorities)
                    array.Add(priority);
            }

            return array;
        }

        private static string[] DeserializePriorities(JSONNode node)
        {
            return node != null && node.IsArray
                ? node.AsArray.Children.Select(x => x.Value).ToArray()
                : null;
        }

        private static JSONArray SerializeGoalPresets(CustomGoalLoadoutPreset[] presets)
        {
            var array = new JSONArray();
            if (presets == null)
                return array;

            foreach (var preset in presets.Where(x => x != null))
            {
                var item = JsonFiles.ParseObject(preset.SourceJson?.ToString() ?? "{}");
                item.Remove("EnergyNguWeights");
                item.Remove("MagicNguWeights");
                item["Name"] = preset.Name;
                item["GearGoal"] = preset.GearGoal;
                array.Add(item);
            }

            return array;
        }

        private static CustomGoalLoadoutPreset DeserializeGoalPreset(JSONNode node)
        {
            if (node == null || !node.IsObject)
                return null;

            var preset = new CustomGoalLoadoutPreset { SourceJson = node.AsObject };
            preset.Name = ReadString(node, "Name", preset.Name);
            preset.GearGoal = ReadInt(node, "GearGoal", preset.GearGoal);
            return preset;
        }

        private static JSONArray SerializeHistory(CustomRebirthHistoryEntry[] history)
        {
            var array = new JSONArray();
            if (history == null)
                return array;

            foreach (var entry in history.Where(x => x != null))
            {
                var item = JsonFiles.ParseObject(entry.SourceJson?.ToString() ?? "{}");
                item["TimestampLocal"] = entry.TimestampLocal ?? string.Empty;
                item["RebirthSeconds"] = entry.RebirthSeconds;
                item["NumberBefore"] = entry.NumberBefore;
                item["NumberAfter"] = entry.NumberAfter;
                item["NumberMultiplier"] = entry.NumberMultiplier;
                item["BossId"] = entry.BossId;
                item["Reason"] = entry.Reason ?? string.Empty;
                array.Add(item);
            }

            return array;
        }

        private static CustomRebirthHistoryEntry DeserializeHistoryEntry(JSONNode node)
        {
            if (node == null || !node.IsObject)
                return null;

            return new CustomRebirthHistoryEntry
            {
                SourceJson = node.AsObject,
                TimestampLocal = ReadString(node, "TimestampLocal", null),
                RebirthSeconds = ReadDouble(node, "RebirthSeconds", 0d),
                NumberBefore = ReadDouble(node, "NumberBefore", 0d),
                NumberAfter = ReadDouble(node, "NumberAfter", 0d),
                NumberMultiplier = ReadDouble(node, "NumberMultiplier", 0d),
                BossId = ReadInt(node, "BossId", 0),
                Reason = ReadString(node, "Reason", null)
            };
        }

        private static JSONNode GetExistingNode(JSONNode root, string key)
        {
            if (root == null || !root.IsObject)
                return null;

            foreach (var item in root.Linq)
            {
                if (item.Key == key)
                    return item.Value;
            }

            return null;
        }

        private static string ReadString(JSONNode root, string key, string fallback)
        {
            var node = GetExistingNode(root, key);
            return node == null || node.IsNull ? fallback : node.Value;
        }

        private static string ReadPresetReference(JSONNode root, string key)
        {
            var node = GetExistingNode(root, key);
            if (ReferenceEquals(node, null)) return string.Empty;
            if (node.IsString && key == "PresetId" && node.Value == "@active") return node.Value;
            if (!node.IsString || node.Value.Length != 0 && !System.Text.RegularExpressions.Regex.IsMatch(node.Value, @"\A[A-Za-z0-9_-]{1,64}\z"))
                throw new FormatException(key + " must be empty or a valid preset ID" + (key == "PresetId" ? ", or @active." : "."));
            return node.Value;
        }

        internal static CustomPlan ReadForPresetMigration(string text, string name)
        {
            var plan = DeserializePlan(text);
            NormalizePlan(plan, name);
            return plan;
        }

        private static int ReadInt(JSONNode root, string key, int fallback)
        {
            var node = GetExistingNode(root, key);
            return node == null || node.IsNull ? fallback : node.AsInt;
        }

        private static double ReadDouble(JSONNode root, string key, double fallback)
        {
            var node = GetExistingNode(root, key);
            return node == null || node.IsNull ? fallback : node.AsDouble;
        }

        private static long ReadLong(JSONNode root, string key, long fallback)
        {
            var node = GetExistingNode(root, key);
            if (node == null || node.IsNull)
                return fallback;

            long value;
            if (long.TryParse(node.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                return value;

            var numeric = node.AsDouble;
            if (double.IsNaN(numeric) || double.IsInfinity(numeric))
                return fallback;

            if (numeric >= long.MaxValue)
                return long.MaxValue;

            if (numeric <= long.MinValue)
                return long.MinValue;

            return (long) Math.Floor(numeric);
        }

        private static bool ReadBool(JSONNode root, string key, bool fallback)
        {
            var node = GetExistingNode(root, key);
            return node == null || node.IsNull ? fallback : node.AsBool;
        }

        private static CustomPlan BuiltInTemplate(string planName)
        {
            if (string.Equals(planName, TwentyFourHourPlanName, StringComparison.OrdinalIgnoreCase))
                return CustomPlan.TwentyFourHourPlan();

            if (string.Equals(planName, FifteenMinuteNumberPlanName, StringComparison.OrdinalIgnoreCase))
                return CustomPlan.NumberIncreasePlan(FifteenMinuteNumberPlanName, 900, 900);
            if (string.Equals(planName, ThirtyMinuteNumberPlanName, StringComparison.OrdinalIgnoreCase))
                return CustomPlan.NumberIncreasePlan(ThirtyMinuteNumberPlanName, 1800, 1800);
            if (string.Equals(planName, OneHourOneMinuteNumberPlanName, StringComparison.OrdinalIgnoreCase))
                return CustomPlan.NumberIncreasePlan(OneHourOneMinuteNumberPlanName, 3660, 3600);

            return null;
        }

        private static void ValidatePlanVersion(int version)
        {
            if (version > CurrentPlanVersion)
                throw new FormatException("Plan version " + version + " is newer than supported version " + CurrentPlanVersion + ".");
        }

        private static void NormalizePlan(CustomPlan plan, string fallbackName)
        {
            ValidatePlanVersion(plan.Version);
            plan.ValidateFightSchedules();
            if (plan.RebirthPrepStartSeconds < 0)
                throw new FormatException("RebirthPrepStartSeconds must be a nonnegative whole Int32 number.");
            ValidateBloodOverrides(plan);
            ValidateLoadoutPresets(plan.LoadoutPresets);
            if (plan.GoalPresets == null || plan.GoalPresets.Length == 0)
            {
                plan.GoalPresets = new[]
                {
                    new CustomGoalLoadoutPreset {Name = "Default", GearGoal = plan.GearGoal}
                };
            }

            for (var i = 0; i < plan.GoalPresets.Length; i++)
            {
                if (plan.GoalPresets[i] == null)
                {
                    plan.GoalPresets[i] = new CustomGoalLoadoutPreset {Name = $"Goal {i + 1}", GearGoal = 0};
                }

                if (string.IsNullOrWhiteSpace(plan.GoalPresets[i].Name))
                {
                    plan.GoalPresets[i].Name = $"Goal {i + 1}";
                }

            }

            if (plan.ActiveGoalPresetIndex < 0 || plan.ActiveGoalPresetIndex >= plan.GoalPresets.Length)
            {
                plan.ActiveGoalPresetIndex = 0;
            }

            plan.GearGoal = plan.GoalPresets[plan.ActiveGoalPresetIndex].GearGoal;

            if (plan.Version < 7 && plan.Stages != null)
            {
                foreach (var stage in plan.Stages.Where(stage => stage != null && stage.EnergyFocus == (int) CustomStageFocus.Augments && stage.MagicFocus == (int) CustomStageFocus.None))
                {
                    stage.MagicFocus = (int) CustomStageFocus.BloodMagic;
                }
            }

            if (plan.Version < 9 && plan.Stages != null)
            {
                foreach (var stage in plan.Stages.Where(stage => stage != null && stage.Name != null && stage.Name.IndexOf("Augment", StringComparison.OrdinalIgnoreCase) >= 0 && stage.GearGoal == (int) NGUInjector.Managers.GearOptimizationGoal.Fight))
                {
                    stage.GearGoal = (int) NGUInjector.Managers.GearOptimizationGoal.Augments;
                }
            }

            if (plan.Version < 10)
            {
                plan.AutoActivateFruits = true;
            }

            NormalizeStages(plan);
            NormalizeHistory(plan);
            plan.Version = CurrentPlanVersion;

            if (string.IsNullOrWhiteSpace(plan.Name))
            {
                plan.Name = fallbackName;
            }

            if (plan.TimeTriggerSeconds < 0)
            {
                plan.TimeTriggerSeconds = 0;
            }

            if (plan.NumberMultiplierTarget < 0)
            {
                plan.NumberMultiplierTarget = 0;
            }

            if (plan.NumberClimbMinSeconds < 0)
            {
                plan.NumberClimbMinSeconds = 0;
            }

            if (plan.NumberClimbMultiplierTarget < 1)
            {
                plan.NumberClimbMultiplierTarget = 1;
            }

            if (plan.StageDurationSeconds < 0)
                throw new FormatException("StageDurationSeconds must be nonnegative.");

            var timedStages = plan.Stages.Where(stage => stage.CompletionMode == (int) CustomStageCompletionMode.TimePercent).ToArray();
            var fixedTotal = timedStages.Sum(stage => (long) stage.DurationSeconds);
            if (fixedTotal > int.MaxValue)
                throw new FormatException("Fixed stage durations exceed the Int32 schedule limit.");
            var flexibleWeight = timedStages.Where(stage => stage.DurationSeconds == 0).Sum(stage => stage.Percent);
            var horizon = plan.TimeTriggerEnabled && plan.TimeTriggerSeconds > 0 ? plan.TimeTriggerSeconds : plan.StageDurationSeconds;
            var flexibleBudget = Math.Max(0, horizon - fixedTotal);
            foreach (var stage in timedStages.Where(stage => stage.ExecutionMode == (int) CustomStageExecutionMode.AutoAugmentsWandoos))
            {
                var budget = stage.DurationSeconds > 0 ? stage.DurationSeconds : flexibleBudget * (stage.Percent / flexibleWeight);
                if (budget > CustomPlanStage.MaximumAutomaticDurationSeconds)
                    throw new FormatException("AutoAugmentsWandoos effective stage budget must not exceed 86400 seconds (24 hours), including flexible allocation. Reduce the stage duration, share, or plan horizon.");
            }

            if (plan.StageLoopStartIndex < -1 || plan.Stages == null || plan.StageLoopStartIndex >= plan.Stages.Length)
            {
                plan.StageLoopStartIndex = -1;
            }

            if (plan.TriggerMode < 0 || plan.TriggerMode > 1)
            {
                plan.TriggerMode = 0;
            }


            if (plan.FightScoreMode < 0 || !NGUInjector.Managers.GearOptimizer.FightScoreModeLabels.ContainsKey(plan.FightScoreMode))
            {
                plan.FightScoreMode = 0;
            }

        }

        private static void NormalizeStages(CustomPlan plan)
        {
            if (plan.Stages == null || plan.Stages.Length == 0)
            {
                plan.Stages = CustomPlan.DefaultNumberClimbStages();
            }

            for (var i = 0; i < plan.Stages.Length; i++)
            {
                if (plan.Stages[i] == null)
                {
                    plan.Stages[i] = new CustomPlanStage {Name = $"Stage {i + 1}", Percent = 100.0};
                }

                ValidateAdventureDynamicWeight(plan.Stages[i]);
                if (string.IsNullOrWhiteSpace(plan.Stages[i].Name))
                {
                    plan.Stages[i].Name = $"Stage {i + 1}";
                }

                if (plan.Stages[i].DurationSeconds < 0)
                    throw new FormatException("Stage DurationSeconds must be nonnegative.");
                if (plan.Stages[i].ExecutionMode < 0 || plan.Stages[i].ExecutionMode > 1)
                    throw new FormatException("Unknown stage ExecutionMode.");
                if (plan.Stages[i].ExecutionMode == 1 && plan.Stages[i].CompletionMode != 0)
                    throw new FormatException("AutoAugmentsWandoos requires a timed stage, not an AT-stat completion gate.");
                if (plan.Stages[i].DurationUnit != "%" && plan.Stages[i].DurationUnit != "s"
                    && plan.Stages[i].DurationUnit != "min" && plan.Stages[i].DurationUnit != "h")
                    throw new FormatException("Stage DurationUnit must be %, s, min, or h.");

                if (plan.Stages[i].Percent <= 0 || double.IsNaN(plan.Stages[i].Percent) || double.IsInfinity(plan.Stages[i].Percent))
                {
                    Main.Log($"Custom stage '{plan.Stages[i].Name}' has an invalid weight; normalized to 1.");
                    plan.Stages[i].Percent = 1.0;
                }

                if (plan.Stages[i].EnergyFocus < 0 || plan.Stages[i].EnergyFocus > (int) CustomStageFocus.AdvancedTraining)
                {
                    plan.Stages[i].EnergyFocus = (int) CustomStageFocus.None;
                }

                if (plan.Stages[i].MagicFocus < 0 || plan.Stages[i].MagicFocus > (int) CustomStageFocus.AdvancedTraining)
                {
                    plan.Stages[i].MagicFocus = (int) CustomStageFocus.None;
                }

                plan.Stages[i].BlackBarLookaheadSeconds = Math.Max(3, plan.Stages[i].BlackBarLookaheadSeconds);


                if (plan.Stages[i].CompletionMode < (int) CustomStageCompletionMode.TimePercent
                    || plan.Stages[i].CompletionMode > (int) CustomStageCompletionMode.AdvancedTrainingStats)
                {
                    plan.Stages[i].CompletionMode = (int) CustomStageCompletionMode.TimePercent;
                }

                if (plan.Stages[i].TargetAdvancedTrainingPower < 0)
                {
                    plan.Stages[i].TargetAdvancedTrainingPower = 0;
                }

                if (plan.Stages[i].TargetAdvancedTrainingToughness < 0)
                {
                    plan.Stages[i].TargetAdvancedTrainingToughness = 0;
                }
            }

            if (double.IsInfinity(plan.Stages.Where(stage => stage.CompletionMode == (int) CustomStageCompletionMode.TimePercent
                && stage.DurationSeconds == 0).Sum(stage => stage.Percent)))
                throw new FormatException("Custom stage weights must have a finite total.");
        }

        private static void NormalizeHistory(CustomPlan plan)
        {
            if (plan.RebirthHistory == null)
            {
                plan.RebirthHistory = new CustomRebirthHistoryEntry[0];
                return;
            }

            plan.RebirthHistory = plan.RebirthHistory
                .Where(x => x != null)
                .Take(MaxRebirthHistoryEntries)
                .ToArray();
        }
    }
}
