using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SimpleJSON;

namespace NGUInjector.CustomPlans
{
    internal class CustomPlanStore
    {
        private const string DefaultPlanName = "default";
        private const string TwentyFourHourPlanName = "24hr";
        private const string OneHourThreeMinuteRebirthPlanName = "1h-3m-rebirth";
        private const string InfinitePlanName = "infinite";
        private const int MaxRebirthHistoryEntries = 20;
        internal const int CurrentPlanVersion = 16;
        internal const string ExternalUiRevisionField = "__externalUiRevision";
        private static readonly Encoding PlanEncoding = new UTF8Encoding(false, true);
        private readonly string _plansDir;

        internal CustomPlanStore(string plansDir)
        {
            _plansDir = plansDir;
            Directory.CreateDirectory(_plansDir);
            EnsureBuiltInPlans();
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
            return DefaultPlanName;
        }

        private void EnsureBuiltInPlans()
        {
            var path = GetPlanPath(DefaultPlanName);
            if (!File.Exists(path))
            {
                Save(DefaultPlanName, new CustomPlan {Name = DefaultPlanName});
            }

            EnsurePlan(TwentyFourHourPlanName, CustomPlan.TwentyFourHourPlan());
            EnsurePlan(OneHourThreeMinuteRebirthPlanName, CustomPlan.OneHourThreeMinuteRebirthPlan());
            EnsurePlan(InfinitePlanName, CustomPlan.InfinitePlan());
        }

        private void EnsurePlan(string planName, CustomPlan plan)
        {
            if (File.Exists(GetPlanPath(planName)))
            {
                RepairBuiltInPlanIfNeeded(planName);
                return;
            }

            plan.Name = planName;
            Save(planName, plan);
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
            var plans = ListPlanNames();
            if (plans.Length <= 1)
                return plans[0];
            var path = GetPlanPath(planName);
            if (File.Exists(path))
                File.Delete(path);
            return ListPlanNames().FirstOrDefault() ?? EnsureDefaultPlan();
        }

        internal string DeleteExternalPlan(string planName)
        {
            ValidateExternalPlanName(planName);
            if (string.Equals(planName, DefaultPlanName, StringComparison.OrdinalIgnoreCase) || BuiltInTemplate(planName) != null)
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
            if (selected)
                Main.reference?.InvalidateCustomRuntimePlanCache();
            return fallback;
        }

        internal void Save(string planName, CustomPlan plan)
        {
            planName = NormalizePlanName(planName);
            NormalizePlan(plan, planName);
            var path = GetPlanPath(planName);
            var serialized = SerializePlan(plan);
            var changed = !File.Exists(path) || File.ReadAllText(path) != serialized;
            JsonFiles.WriteAtomic(path, serialized);
            plan.RuntimeRevision = JsonFiles.ComputeRevision(PlanEncoding.GetBytes(serialized));
            if (changed && string.Equals(Main.Settings?.CustomPlanFile, planName, StringComparison.OrdinalIgnoreCase))
                Main.reference?.InvalidateCustomRuntimePlanCache(false);
        }

        internal JSONObject ReadExternalPlan(string name)
        {
            ValidateExternalPlanName(name);
            var bytes = File.ReadAllBytes(GetPlanPath(name));
            var plan = DeserializePlan(PlanEncoding.GetString(bytes).TrimStart('\uFEFF'));
            NormalizePlan(plan, name);
            var root = JsonFiles.ParseObject(SerializePlan(plan));
            root[ExternalUiRevisionField] = JsonFiles.ComputeRevision(bytes);
            return root;
        }

        internal string SaveExternalPlan(string name, JSONNode root, string expectedRevision)
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
            // The injector owns history. Do not replace it with a stale editor snapshot.
            plan.RebirthHistory = bytes != null
                ? DeserializePlan(PlanEncoding.GetString(bytes).TrimStart('\uFEFF')).RebirthHistory : new CustomRebirthHistoryEntry[0];
            plan.Name = name;
            Save(name, plan);
            return JsonFiles.ComputeRevision(File.ReadAllBytes(path));
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
                return DefaultPlanName;
            }

            var invalid = Path.GetInvalidFileNameChars();
            var chars = planName.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray();
            var safe = new string(chars).Trim(' ', '.');
            return string.IsNullOrWhiteSpace(safe) ? DefaultPlanName : safe;
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

        private void RepairBuiltInPlanIfNeeded(string planName)
        {
            var template = BuiltInTemplate(planName);
            if (template == null)
                return;

            var path = GetPlanPath(planName);
            if (!PlanMissingSerializedArray(path, "Stages") && !BuiltInGoalPresetNeedsRepair(path, planName, template))
                return;

            var plan = Load(planName);
            if (BuiltInGoalPresetNeedsRepair(path, planName, template))
            {
                plan.GoalPresets = template.GoalPresets;
                plan.ActiveGoalPresetIndex = template.ActiveGoalPresetIndex;
                plan.GearGoal = template.GearGoal;
            }

            Save(planName, plan);
        }

        private static string SerializePlan(CustomPlan plan)
        {
            var root = JsonFiles.ParseObject(plan.SourceJson?.ToString() ?? "{}");
            root.Remove(ExternalUiRevisionField);
            root.Remove("NguGoal");
            root["Version"] = plan.Version;
            root["Name"] = plan.Name;
            root["AutoRebirthEnabled"] = plan.AutoRebirthEnabled;
            root["AutoIronPillEnabled"] = plan.AutoIronPillEnabled;
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
            root["EnergyNguWeights"] = SerializeNguWeights(plan.EnergyNguWeights, 9, "EnergyNguWeights");
            root["MagicNguWeights"] = SerializeNguWeights(plan.MagicNguWeights, 7, "MagicNguWeights");
            root["GearGoal"] = plan.GearGoal;
            root["FightScoreMode"] = plan.FightScoreMode;
            root["ActiveGoalPresetIndex"] = plan.ActiveGoalPresetIndex;
            root["GoalPresets"] = SerializeGoalPresets(plan.GoalPresets);
            root["PrepRequireMaxedFruit"] = plan.PrepRequireMaxedFruit;
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
            plan.Version = ReadInt(root, "Version", 15);
            plan.Name = ReadString(root, "Name", plan.Name);
            var builtIn = BuiltInTemplate(plan.Name);
            plan.AutoRebirthEnabled = ReadBool(root, "AutoRebirthEnabled", plan.AutoRebirthEnabled);
            var autoIronPill = GetExistingNode(root, "AutoIronPillEnabled");
            if (!ReferenceEquals(autoIronPill, null) && !autoIronPill.IsBoolean)
                throw new FormatException("AutoIronPillEnabled must be a boolean.");
            plan.AutoIronPillEnabled = ReferenceEquals(autoIronPill, null) || autoIronPill.AsBool;
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
            if (plan.Version >= 15 && !ReferenceEquals(budget, null) && (!budget.IsNumber || budget.AsDouble < 0
                || budget.AsDouble > int.MaxValue || budget.AsDouble != Math.Truncate(budget.AsDouble)))
                throw new FormatException("StageDurationSeconds must be a nonnegative whole Int32 number.");
            plan.StageDurationSeconds = ReadInt(root, "StageDurationSeconds", plan.StageDurationSeconds);
            plan.StageLoopStartIndex = ReadInt(root, "StageLoopStartIndex", plan.StageLoopStartIndex);
            plan.AutoActivateFruits = ReadBool(root, "AutoActivateFruits", plan.AutoActivateFruits);
            var legacyAdventureDrop = plan.Version >= 11 && ReadInt(root, "NguGoal", 0) == 1;
            plan.EnergyNguWeights = DeserializeNguWeights(root, "EnergyNguWeights", 9, plan.Version <= 15
                ? legacyAdventureDrop ? new[] {0, 0, 0, 0, 1, 0, 1, 0, 0} : new[] {1, 1, 0, 1, 0, 1, 0, 0, 0} : null);
            plan.MagicNguWeights = DeserializeNguWeights(root, "MagicNguWeights", 7, plan.Version <= 15
                ? legacyAdventureDrop ? new[] {0, 0, 0, 0, 0, 0, 1} : new[] {0, 0, 1, 0, 0, 0, 0} : null);
            plan.GearGoal = ReadInt(root, "GearGoal", plan.GearGoal);
            plan.FightScoreMode = ReadInt(root, "FightScoreMode", plan.FightScoreMode);
            plan.ActiveGoalPresetIndex = ReadInt(root, "ActiveGoalPresetIndex", plan.ActiveGoalPresetIndex);
            plan.PrepRequireMaxedFruit = ReadBool(root, "PrepRequireMaxedFruit", plan.PrepRequireMaxedFruit);

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

            return plan;
        }

        private static int[] DeserializeNguWeights(JSONNode root, string key, int count, int[] legacyWeights)
        {
            var node = GetExistingNode(root, key);
            if (ReferenceEquals(node, null) && legacyWeights != null)
                return legacyWeights;
            if (ReferenceEquals(node, null) || !node.IsArray || node.Count != count)
                throw new FormatException(key + " must contain exactly " + count + " nonnegative whole Int32 numbers.");

            var weights = new int[count];
            for (var i = 0; i < count; i++)
            {
                var entry = node[i];
                if (!entry.IsNumber)
                    throw new FormatException(key + " entries must be nonnegative whole Int32 numbers.");

                // Check the exact JSON token, not a rounded double (including tiny fractions).
                var parts = entry.Value.Split(new[] {'e', 'E'});
                var point = parts[0].IndexOf('.');
                var digits = parts[0].Replace(".", "").TrimStart('-').TrimStart('0');
                if (digits.Length == 0)
                    continue;
                long exponent = 0;
                if (parts[0][0] == '-' || parts.Length > 1 && !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out exponent))
                    throw new FormatException(key + " entries must be nonnegative whole Int32 numbers.");
                var significant = digits.TrimEnd('0');
                var fractional = point < 0 ? 0 : parts[0].Length - point - 1;
                var adjustment = (long) digits.Length - significant.Length - fractional;
                if (exponent < -adjustment || exponent > 10 - significant.Length - adjustment)
                    throw new FormatException(key + " entries must be nonnegative whole Int32 numbers.");
                var integer = significant + new string('0', (int) (exponent + adjustment));
                if (!int.TryParse(integer, NumberStyles.None, CultureInfo.InvariantCulture, out weights[i]))
                    throw new FormatException(key + " entries must be nonnegative whole Int32 numbers.");
            }
            return weights;
        }

        private static JSONArray SerializeNguWeights(int[] weights, int count, string key)
        {
            if (weights == null || weights.Length != count || weights.Any(weight => weight < 0))
                throw new FormatException(key + " must contain exactly " + count + " nonnegative whole Int32 numbers.");
            var array = new JSONArray();
            foreach (var weight in weights)
                array.Add(weight);
            return array;
        }

        private static JSONArray SerializeStages(CustomPlanStage[] stages)
        {
            var array = new JSONArray();
            if (stages == null)
                return array;

            foreach (var stage in stages.Where(x => x != null))
            {
                var item = JsonFiles.ParseObject(stage.SourceJson?.ToString() ?? "{}");
                item["Name"] = stage.Name;
                item["Percent"] = stage.Percent;
                item["DurationSeconds"] = stage.DurationSeconds;
                item["DurationUnit"] = stage.DurationUnit;
                item["EnergyFocus"] = stage.EnergyFocus;
                item["MagicFocus"] = stage.MagicFocus;
                item["EnergyPriorities"] = SerializePriorities(stage.EnergyPriorities);
                item["MagicPriorities"] = SerializePriorities(stage.MagicPriorities);
                item["BlackBarLookaheadSeconds"] = stage.BlackBarLookaheadSeconds;
                item["GearGoal"] = stage.GearGoal;
                item["FightOnEntry"] = stage.FightOnEntry;
                item["CompletionMode"] = stage.CompletionMode;
                item["TargetAdvancedTrainingPower"] = stage.TargetAdvancedTrainingPower.ToString(CultureInfo.InvariantCulture);
                item["TargetAdvancedTrainingToughness"] = stage.TargetAdvancedTrainingToughness.ToString(CultureInfo.InvariantCulture);
                array.Add(item);
            }

            return array;
        }

        private static CustomPlanStage DeserializeStage(JSONNode node)
        {
            if (node == null || !node.IsObject)
                return null;

            var stage = new CustomPlanStage { SourceJson = node.AsObject };
            stage.Name = ReadString(node, "Name", stage.Name);
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
            stage.EnergyPriorities = DeserializePriorities(GetExistingNode(node, "EnergyPriorities"));
            stage.MagicPriorities = DeserializePriorities(GetExistingNode(node, "MagicPriorities"));
            stage.BlackBarLookaheadSeconds = ReadInt(node, "BlackBarLookaheadSeconds", stage.BlackBarLookaheadSeconds);
            stage.GearGoal = ReadInt(node, "GearGoal", stage.GearGoal);
            var fightOnEntry = GetExistingNode(node, "FightOnEntry");
            if (!ReferenceEquals(fightOnEntry, null) && !fightOnEntry.IsBoolean)
                throw new FormatException("Stage FightOnEntry must be a boolean.");
            stage.FightOnEntry = !ReferenceEquals(fightOnEntry, null) && fightOnEntry.AsBool;
            stage.CompletionMode = ReadInt(node, "CompletionMode", stage.CompletionMode);
            stage.TargetAdvancedTrainingPower = ReadLong(node, "TargetAdvancedTrainingPower", stage.TargetAdvancedTrainingPower);
            stage.TargetAdvancedTrainingToughness = ReadLong(node, "TargetAdvancedTrainingToughness", stage.TargetAdvancedTrainingToughness);
            return stage;
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

        private static bool PlanMissingSerializedArray(string path, string fieldName)
        {
            try
            {
                var root = JSON.Parse(File.ReadAllText(path));
                var node = GetExistingNode(root, fieldName);
                return node == null || !node.IsArray || node.Count == 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool BuiltInGoalPresetNeedsRepair(string path, string planName, CustomPlan template)
        {
            if (!string.Equals(planName, OneHourThreeMinuteRebirthPlanName, StringComparison.OrdinalIgnoreCase))
                return false;

            try
            {
                var root = JSON.Parse(File.ReadAllText(path));
                var presets = GetExistingNode(root, "GoalPresets");
                if (presets == null || !presets.IsArray || presets.Count == 0)
                    return true;

                var first = presets.AsArray[0];
                var templateGoal = template.GoalPresets != null && template.GoalPresets.Length > 0 ? template.GoalPresets[0].GearGoal : template.GearGoal;
                return ReadInt(first, "GearGoal", templateGoal) != templateGoal;
            }
            catch
            {
                return false;
            }
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

            if (string.Equals(planName, OneHourThreeMinuteRebirthPlanName, StringComparison.OrdinalIgnoreCase))
                return CustomPlan.OneHourThreeMinuteRebirthPlan();

            if (string.Equals(planName, InfinitePlanName, StringComparison.OrdinalIgnoreCase))
                return CustomPlan.InfinitePlan();

            return null;
        }

        private static void NormalizePlan(CustomPlan plan, string fallbackName)
        {
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

                if (plan.GoalPresets[i].GearGoal < 0 || !NGUInjector.Managers.GearOptimizer.GoalLabels.ContainsKey(plan.GoalPresets[i].GearGoal))
                {
                    plan.GoalPresets[i].GearGoal = 0;
                }
            }

            if (plan.ActiveGoalPresetIndex < 0 || plan.ActiveGoalPresetIndex >= plan.GoalPresets.Length)
            {
                plan.ActiveGoalPresetIndex = 0;
            }

            plan.GearGoal = plan.GoalPresets[plan.ActiveGoalPresetIndex].GearGoal;

            if (plan.Version < 6)
            {
                plan.NumberClimbEnabled = true;
                plan.NumberClimbMinSeconds = 480;
                plan.NumberClimbMultiplierTarget = Math.Max(100.0, plan.NumberMultiplierTarget);
                plan.StageSchedulerEnabled = true;
                plan.StageDurationSeconds = 1800;
                plan.Stages = CustomPlan.DefaultNumberClimbStages();
                plan.StageLoopStartIndex = 5;
            }

            if (plan.Version < 7 && plan.Stages != null)
            {
                foreach (var stage in plan.Stages.Where(stage => stage != null && stage.EnergyFocus == (int) CustomStageFocus.Augments && stage.MagicFocus == (int) CustomStageFocus.None))
                {
                    stage.MagicFocus = (int) CustomStageFocus.BloodMagic;
                }
            }

            if (plan.Version < 8 && ShouldUpgradeOldNumberClimbStages(plan))
            {
                plan.StageDurationSeconds = 1800;
                plan.Stages = CustomPlan.DefaultNumberClimbStages();
                plan.StageLoopStartIndex = 5;
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

            if (plan.StageDurationSeconds <= 0)
            {
                plan.StageDurationSeconds = plan.TimeTriggerSeconds > 0 ? plan.TimeTriggerSeconds : 1800;
            }

            var timedStages = plan.Stages.Where(stage => stage.CompletionMode == (int) CustomStageCompletionMode.TimePercent).ToArray();
            var fixedTotal = timedStages.Sum(stage => (long) stage.DurationSeconds);
            if (fixedTotal > int.MaxValue)
                throw new FormatException("Fixed stage durations exceed the Int32 schedule limit.");
            if (timedStages.Length > 0 && timedStages.All(stage => stage.DurationSeconds > 0))
                plan.StageDurationSeconds = (int) fixedTotal;
            else if (timedStages.Any(stage => stage.DurationSeconds == 0) && fixedTotal >= plan.StageDurationSeconds)
                throw new FormatException("Fixed stage durations must be less than StageDurationSeconds when percent stages exist.");

            if (plan.StageLoopStartIndex < -1 || plan.Stages == null || plan.StageLoopStartIndex >= plan.Stages.Length)
            {
                plan.StageLoopStartIndex = -1;
            }

            if (plan.TriggerMode < 0 || plan.TriggerMode > 1)
            {
                plan.TriggerMode = 0;
            }

            if (plan.GearGoal < 0)
            {
                plan.GearGoal = 0;
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

                if (string.IsNullOrWhiteSpace(plan.Stages[i].Name))
                {
                    plan.Stages[i].Name = $"Stage {i + 1}";
                }

                if (plan.Stages[i].DurationSeconds < 0)
                    throw new FormatException("Stage DurationSeconds must be nonnegative.");
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

                if (plan.Stages[i].GearGoal < -1 || plan.Stages[i].GearGoal >= 0 && !NGUInjector.Managers.GearOptimizer.GoalLabels.ContainsKey(plan.Stages[i].GearGoal))
                {
                    plan.Stages[i].GearGoal = -1;
                }

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

        private static bool ShouldUpgradeOldNumberClimbStages(CustomPlan plan)
        {
            if (plan.Stages == null || plan.Stages.Length == 0)
                return true;

            if (plan.Stages.Length != 3)
                return false;

            return plan.Stages.Any(stage => stage != null && !string.IsNullOrEmpty(stage.Name) && stage.Name.IndexOf("Time Machine", StringComparison.OrdinalIgnoreCase) >= 0)
                   && plan.Stages.Any(stage => stage != null && !string.IsNullOrEmpty(stage.Name) && stage.Name.IndexOf("Augment", StringComparison.OrdinalIgnoreCase) >= 0)
                   && plan.Stages.Any(stage => stage != null && !string.IsNullOrEmpty(stage.Name) && stage.Name.IndexOf("Wandoos", StringComparison.OrdinalIgnoreCase) >= 0);
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
