using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SimpleJSON;

namespace NGUInjector.CustomPlans
{
    // One runtime-folder library. Receipts survive deletion so historical plan arrays cannot resurrect presets.
    internal sealed class PresetLibraryStore
    {
        internal const int MaximumPresets = 1024;
        internal const int MaximumFileBytes = 2 * 1024 * 1024;
        private const string CapPresetsAdded = "CapPresetsAdded";
        internal static readonly object SyncRoot = new object();
        private static readonly Encoding Encoding = new UTF8Encoding(false, true);
        private readonly string _path, _plansDir;
        private bool _initialized;
        private static readonly Lazy<JSONObject> Policy = new Lazy<JSONObject>(() =>
        {
            using (var stream = typeof(PresetLibraryStore).Assembly.GetManifestResourceStream("NGUInjector.ActivityPolicies.json"))
            {
                if (stream == null) throw new InvalidOperationException("Missing embedded ActivityPolicies.json for preset migration.");
                using (var reader = new StreamReader(stream)) return JsonFiles.ParseObject(reader.ReadToEnd());
            }
        });

        internal PresetLibraryStore(string runtimeDir, string plansDir)
        {
            _path = Path.Combine(runtimeDir, "presets.json");
            _plansDir = plansDir;
        }

        internal JSONObject ReadExternalLibrary()
        {
            lock (SyncRoot)
            {
                EnsureInitialized(true);
                var bytes = ReadFileBytes();
                var root = Read(bytes);
                root[CustomPlanStore.ExternalUiRevisionField] = JsonFiles.ComputeRevision(bytes);
                return root;
            }
        }

        internal string SaveExternalLibrary(JSONNode library, string expectedRevision)
        {
            lock (SyncRoot)
            {
                if (library == null || !library.IsObject) throw new FormatException("args.library must be a JSON object.");
                if (expectedRevision == null || expectedRevision.Length != 64 || !expectedRevision.All(Uri.IsHexDigit))
                    throw new FormatException("args.expectedRevision must be a SHA-256 hex token.");
                EnsureInitialized(true);
                var bytes = ReadFileBytes();
                if (!string.Equals(expectedRevision, JsonFiles.ComputeRevision(bytes), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Preset library revision conflict; readPresetLibrary again before saving.");
                var previous = Read(bytes);
                var next = JsonFiles.ParseObject(library.ToString());
                // Migration state is injector-owned, not an editable part of the UI snapshot.
                foreach (var field in new[] {"MigrationReceipts", CapPresetsAdded})
                    if (Has(previous, field)) next[field] = previous[field];
                    else next.Remove(field);
                next = Normalize(next);
                var ids = new HashSet<string>(next["Presets"].Children.Select(row => row["Id"].Value), StringComparer.Ordinal);
                foreach (var path in Directory.GetFiles(_plansDir, "*.json").OrderBy(path => path, StringComparer.Ordinal))
                {
                    // Fail closed on unreadable plans. Derive unsaved imported references before allowing deletion.
                    var plan = CustomPlanStore.ReadForPresetMigration(File.ReadAllText(path, Encoding), Path.GetFileNameWithoutExtension(path));
                    Migrate(previous, plan, Path.GetFileNameWithoutExtension(path));
                    foreach (var id in References(plan))
                        if (!ids.Contains(id)) throw new InvalidOperationException("Preset '" + id + "' is referenced by plan '" + Path.GetFileNameWithoutExtension(path) + "'.");
                }
                if (Has(previous, "MigrationReceipts")) next["MigrationReceipts"] = previous["MigrationReceipts"];
                Write(next);
                return JsonFiles.ComputeRevision(ReadFileBytes());
            }
        }

        internal CustomLoadoutPreset Get(string id)
        {
            return List().FirstOrDefault(preset => string.Equals(preset.Id, id, StringComparison.Ordinal));
        }

        internal CustomLoadoutPreset[] List()
        {
            lock (SyncRoot)
            {
                EnsureInitialized();
                return Read(ReadFileBytes())["Presets"].Children.Select(CustomLoadoutPreset.Parse).ToArray();
            }
        }

        internal void MigratePlan(CustomPlan plan, string source)
        {
            lock (SyncRoot)
            {
                EnsureInitialized();
                var root = Read(ReadFileBytes());
                var before = root.ToString();
                var active = plan.ActivePresetId;
                var stages = plan.Stages.Select(stage => stage.PresetId).ToArray();
                var issues = plan.PresetMigrationIssues;
                try
                {
                    var seedStages = plan.SourceJson == null && !File.Exists(Path.Combine(_plansDir, source + ".json"));
                    Migrate(root, plan, source, seedStages);
                    ValidateInheritedActive(plan, new HashSet<string>(root["Presets"].Children.Select(row => row["Id"].Value), StringComparer.Ordinal));
                    if (before != root.ToString()) Write(root);
                }
                catch
                {
                    plan.ActivePresetId = active;
                    for (var i = 0; i < stages.Length; i++) plan.Stages[i].PresetId = stages[i];
                    plan.PresetMigrationIssues = issues;
                    throw;
                }
            }
        }

        internal void ValidateReferences(CustomPlan plan)
        {
            var ids = new HashSet<string>(List().Select(preset => preset.Id), StringComparer.Ordinal);
            ValidateInheritedActive(plan, ids);
            foreach (var id in References(plan))
                if (!ids.Contains(id)) throw new FormatException("Unknown global preset ID: " + id);
        }

        private static IEnumerable<string> References(CustomPlan plan)
        {
            if (!string.IsNullOrEmpty(plan.ActivePresetId)) yield return plan.ActivePresetId;
            foreach (var stage in plan.Stages ?? new CustomPlanStage[0])
            {
                if (stage == null || string.IsNullOrEmpty(stage.PresetId)) continue;
                if (stage.PresetId != "@active") yield return stage.PresetId;
                else if (string.IsNullOrEmpty(plan.ActivePresetId))
                    throw new FormatException("Stage PresetId '@active' requires a nonempty plan.ActivePresetId.");
                else yield return plan.ActivePresetId;
            }
        }

        private static void ValidateInheritedActive(CustomPlan plan, HashSet<string> ids)
        {
            if (!(plan.Stages ?? new CustomPlanStage[0]).Any(stage => stage != null && stage.PresetId == "@active")) return;
            if (string.IsNullOrEmpty(plan.ActivePresetId))
                throw new FormatException("Stage PresetId '@active' requires a nonempty plan.ActivePresetId.");
            if (!ids.Contains(plan.ActivePresetId))
                throw new FormatException("Stage PresetId '@active' references missing active preset ID: " + plan.ActivePresetId);
        }

        private void EnsureInitialized(bool refresh = false)
        {
            if (_initialized && !refresh) return;
            var exists = File.Exists(_path);
            var root = exists ? Read(ReadFileBytes()) : Seed();
            var before = root.ToString();
            if (!Has(root, CapPresetsAdded) || !root[CapPresetsAdded].AsBool)
            {
                var seeds = PresetDefinitions.SeedRows();
                var ids = new HashSet<string>(root["Presets"].Children.Select(row => row["Id"].Value), StringComparer.Ordinal);
                // Only extend libraries with recognizable catalog ancestry, never custom-only or empty libraries.
                if (seeds.Children.Any(row => ids.Contains(row["Id"].Value))
                    || new[] {"builtin_pp", "builtin_boosts", "builtin_zone", "builtin_balanced"}.Any(ids.Contains))
                {
                    foreach (var definition in seeds.Children.Where(row => row["Id"].Value == "builtin_energyCap" || row["Id"].Value == "builtin_magicCap"))
                        if (!ids.Contains(definition["Id"].Value)) root["Presets"].Add(CustomLoadoutPreset.Parse(definition).ToJson());
                    root[CapPresetsAdded] = true;
                }
            }
            var priorityFields = new[] {"ResourcePrioritiesReviewed", "EnergyPriorities", "MagicPriorities"};
            var currentDefinitions = PresetDefinitions.SeedRows().Children.ToDictionary(row => row["Id"].Value, StringComparer.Ordinal);
            foreach (var row in root["Presets"].Children)
            {
                // Any explicit proposal/review belongs to the user, including empty or unreviewed arrays.
                if (priorityFields.Any(field => Has(row, field))) continue;
                var prior = PresetDefinitions.BeforeResourcePriorities(row["Id"].Value, Has(row, "BeardRotation"));
                JSONNode definition;
                if (prior == null || Canonical(row) != Canonical(prior)
                    || !currentDefinitions.TryGetValue(row["Id"].Value, out definition)) continue;
                foreach (var field in priorityFields) row[field] = definition[field];
            }
            foreach (var path in Directory.GetFiles(_plansDir, "*.json").OrderBy(path => path, StringComparer.Ordinal))
            {
                CustomPlan plan;
                try { plan = CustomPlanStore.ReadForPresetMigration(File.ReadAllText(path, Encoding), Path.GetFileNameWithoutExtension(path)); }
                catch (Exception error)
                {
                    Main.Log("Preset migration skipped unreadable plan '" + Path.GetFileNameWithoutExtension(path) + "': " + error.Message);
                    continue;
                }
                Migrate(root, plan, Path.GetFileNameWithoutExtension(path));
            }
            if (!exists || before != root.ToString()) Write(root);
            _initialized = true;
        }

        private byte[] ReadFileBytes()
        {
            using (var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var length = stream.Length;
                ValidateFileSize(length);
                using (var bytes = new MemoryStream((int) length))
                {
                    var buffer = new byte[8192];
                    int count;
                    // Bound the actual read too, even if the file grows after its length was checked.
                    while ((count = stream.Read(buffer, 0, Math.Min(buffer.Length, MaximumFileBytes + 1 - (int) bytes.Length))) > 0)
                    {
                        bytes.Write(buffer, 0, count);
                        ValidateFileSize(bytes.Length);
                    }
                    return bytes.ToArray();
                }
            }
        }

        private static JSONObject Read(byte[] bytes)
        {
            ValidateFileSize(bytes.Length);
            return Normalize(JsonFiles.ParseObject(Encoding.GetString(bytes).TrimStart('\uFEFF')));
        }

        private static JSONObject Normalize(JSONObject root)
        {
            root.Remove(CustomPlanStore.ExternalUiRevisionField);
            if (!Has(root, "Version") || !root["Version"].IsNumber || root["Version"].Value != "1")
                throw new FormatException("Preset library Version must be 1.");
            if (!Has(root, "Presets") || !root["Presets"].IsArray)
                throw new FormatException("Preset library Presets must be an array.");
            if (root["Presets"].Count > MaximumPresets)
                throw new FormatException("Preset library Presets must contain at most 1024 presets; file was not modified.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var rows = new JSONArray();
            foreach (var row in root["Presets"].Children)
            {
                var preset = CustomLoadoutPreset.Parse(row);
                if (!ids.Add(preset.Id)) throw new FormatException("Duplicate global preset ID: " + preset.Id);
                rows.Add(preset.ToJson());
            }
            root["Presets"] = rows;
            if (Has(root, "MigrationReceipts") && !root["MigrationReceipts"].IsObject)
                throw new FormatException("MigrationReceipts must be an object.");
            if (Has(root, CapPresetsAdded) && !root[CapPresetsAdded].IsBoolean)
                throw new FormatException("CapPresetsAdded must be a boolean.");
            Serialize(root);
            return root;
        }

        private void Write(JSONObject root)
        {
            root.Remove(CustomPlanStore.ExternalUiRevisionField);
            JsonFiles.WriteAtomic(_path, Serialize(root));
        }

        private static string Serialize(JSONObject root)
        {
            if (root["Presets"].Count > MaximumPresets)
                throw new FormatException("Preset library Presets must contain at most 1024 presets; file was not modified.");
            var text = root.ToString(4);
            ValidateFileSize(Encoding.GetByteCount(text));
            return text;
        }

        private static void ValidateFileSize(long bytes)
        {
            if (bytes > MaximumFileBytes)
                throw new FormatException("Preset library exceeds the 2 MiB (2097152 UTF-8 bytes) file limit; file was not modified.");
        }

        private static JSONObject Seed()
        {
            var root = new JSONObject();
            root["Version"] = 1;
            var rows = new JSONArray();
            foreach (var definition in PresetDefinitions.SeedRows().Children)
                rows.Add(CustomLoadoutPreset.Parse(definition).ToJson());
            root["Presets"] = rows;
            root[CapPresetsAdded] = true;
            return root;
        }

        private static void Migrate(JSONObject root, CustomPlan plan, string source, bool seedStages = false)
        {
            var issues = new List<string>();
            foreach (var legacy in plan.LoadoutPresets ?? new CustomLoadoutPreset[0])
            {
                var definition = CustomLoadoutPreset.Parse(legacy.ToJson());
                Import(root, definition, source, "loadout:" + legacy.Id, issues, plan.SourceJson, legacy.SourceJson);
            }
            if (string.IsNullOrEmpty(plan.ActivePresetId) && !plan.Stages.Any(stage => stage.PresetId == "@active"))
            {
                var goal = plan.GoalPresets[plan.ActiveGoalPresetIndex];
                var definition = FromGoal(goal.GearGoal, goal.Name);
                if (definition == null) issues.Add("ActivePresetId: legacy gear goal " + goal.GearGoal + " has no equivalent preset; retained inline.");
                else if (plan.SourceJson == null || !Has(plan.SourceJson, "ActivePresetId"))
                    plan.ActivePresetId = Import(root, definition, source, "active", issues, plan.SourceJson);
            }
            for (var i = 0; i < plan.Stages.Length; i++)
            {
                var stage = plan.Stages[i];
                if (!string.IsNullOrEmpty(stage.PresetId)) continue;
                var definition = FromStage(stage);
                if (definition == null)
                    issues.Add("Stages[" + i + "].PresetId: legacy priorities, gear goal, execution, fight or cap semantics have no equivalent standard preset; retained inline.");
                else
                {
                    var importedId = Import(root, definition, source, "stage", issues, plan.SourceJson);
                    // Template resemblance does not make capped/overflow allocation equivalent to fixed weights.
                    if (seedStages && (stage.SourceJson == null || !Has(stage.SourceJson, "PresetId"))) stage.PresetId = importedId;
                    else issues.Add("Stages[" + i + "].PresetId: explicit conversion required to use weighted preset"
                        + (importedId.Length == 0 ? string.Empty : " '" + importedId + "'") + "; legacy cap/overflow allocation retained inline.");
                }
            }
            plan.PresetMigrationIssues = issues.Distinct().ToArray();
        }

        private static CustomLoadoutPreset FromGoal(int goal, string name)
        {
            var weights = new double[PresetDefinitions.ObjectiveKeys.Length];
            switch (goal)
            {
                case 2: weights[0] = 1; break;
                case 3: weights[1] = 1; break;
                case 4: weights[0] = weights[1] = 1; break;
                case 5: weights[6] = 1; break;
                case 6: weights[2] = 1; break;
                case 10: weights[7] = 1; break;
                case 11: weights[9] = 1; break;
                case 12: weights[8] = 1; break;
                default: return null;
            }
            return new CustomLoadoutPreset
            {
                Id = "migration", Name = string.IsNullOrWhiteSpace(name) ? "Imported" : name.Substring(0, Math.Min(100, name.Length)),
                Weights = weights
            };
        }

        private static CustomLoadoutPreset FromStage(CustomPlanStage stage)
        {
            if (stage.ExecutionMode != 0 || stage.FightSchedule != null || stage.BlackBarLookaheadSeconds != 5) return null;
            var policy = Policy.Value;
            var template = policy["stagePresets"].Children.FirstOrDefault(row => row["GearGoal"].AsInt == stage.GearGoal
                && row["EnergyFocus"].AsInt == stage.EnergyFocus && row["MagicFocus"].AsInt == stage.MagicFocus);
            if (template == null) return null;
            foreach (var energy in new[] {true, false})
            {
                var configured = energy ? stage.EnergyPriorities : stage.MagicPriorities;
                if (configured == null || configured.Length == 0) continue;
                var field = energy ? "EnergyPriorities" : "MagicPriorities";
                var standard = policy["prioritySets"][template[field].Value].Children.Select(row => row.Value).ToArray();
                var focus = energy ? stage.EnergyFocus : stage.MagicFocus;
                var defaults = policy["defaults"].Children.First(row => row["id"].AsInt == focus);
                var legacy = policy["prioritySets"][defaults[energy ? "energy" : "magic"].Value].Children.Select(row => row.Value).ToArray();
                if (!configured.SequenceEqual(standard) && !configured.SequenceEqual(legacy)) return null;
            }
            var definition = FromGoal(stage.GearGoal, template["label"].Value);
            if (definition == null) return null;
            if (stage.EnergyFocus == 2 && stage.MagicFocus == 4) definition.Weights[2] = 1;
            if (stage.EnergyFocus == 7)
            {
                if (stage.MagicFocus == 3)
                {
                    definition.Weights[8] = 1;
                    definition.AdvancedTrainingWeights = new[] {0, 0, 0, 1, 1};
                }
                else definition.Weights[2] = 1;
            }
            return definition;
        }

        private static string PreviousSchemaImportHash(JSONObject json, string prefix, JSONObject source)
        {
            if (json["Weights"].Children.Skip(10).Any(weight => weight.AsDouble != 0)
                || source != null && Has(source, "Weights") && source["Weights"].Count != 6 && source["Weights"].Count != 10)
                return null;
            var horizon = 3600;
            if (source != null && Has(source, "HorizonSeconds"))
            {
                var node = source["HorizonSeconds"];
                var number = node.AsDouble;
                if (!node.IsNumber || number < 1 || number > 604800 || number != Math.Truncate(number)) return null;
                // Match the previous parser's exact integer check, not a rounded double or decimal.
                var parts = node.Value.Split(new[] {'e', 'E'});
                var point = parts[0].IndexOf('.');
                var digits = parts[0].Replace(".", "").TrimStart('0');
                var significant = digits.TrimEnd('0');
                var fractional = point < 0 ? 0 : parts[0].Length - point - 1;
                var adjustment = (long) digits.Length - significant.Length - fractional;
                long exponent = 0;
                if (parts.Length > 1 && !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out exponent)
                    || exponent < -adjustment || exponent > 6 - significant.Length - adjustment)
                    return null;
                horizon = (int) number;
            }
            // Only reconstruct the ten-lane/horizon identity for lookup; never persist this shape.
            var previous = JsonFiles.ParseObject(json.ToString());
            var weights = new JSONArray();
            foreach (var weight in json["Weights"].Children.Take(10)) weights.Add(weight);
            previous["Weights"] = weights;
            previous["HorizonSeconds"] = horizon;
            return JsonFiles.ComputeRevision(Encoding.GetBytes(prefix + Canonical(previous)));
        }

        private static string Import(JSONObject root, CustomLoadoutPreset definition, string source, string kind, List<string> issues, JSONObject planSource, JSONObject legacySource = null)
        {
            var json = definition.ToJson();
            json.Remove("Id");
            var prefix = source.ToLowerInvariant() + "\n" + kind + "\n";
            var hash = JsonFiles.ComputeRevision(Encoding.GetBytes(prefix + Canonical(json)));
            var currentHash = hash;
            if (!Has(root, "MigrationReceipts")) root["MigrationReceipts"] = new JSONObject();
            var receipts = root["MigrationReceipts"];
            // Reconstruct retired fields only for durable hash lookup, never as preset storage.
            var ratioJson = JsonFiles.ParseObject(json.ToString());
            try
            {
                foreach (var energy in new[] {true, false})
                {
                    var key = energy ? "EnergyNguWeights" : "MagicNguWeights";
                    var weights = legacySource != null && Has(legacySource, key)
                        ? CustomPlanStore.ReadNguWeights(legacySource[key], key, energy ? 9 : 7)
                        : CustomPlanStore.ReadLegacyNguWeights(planSource ?? JsonFiles.ParseObject("{\"Version\":17}"), energy);
                    ratioJson[key] = CustomPlanStore.SerializeNguWeights(weights, energy ? 9 : 7, key);
                }
            }
            catch (FormatException)
            {
                // Malformed retired ratios cannot identify an old valid receipt or block normal reads.
                ratioJson = null;
            }
            var implicitSupport = definition.SupportMode == "planned"
                && (legacySource != null ? !Has(legacySource, "SupportMode") : kind == "active" || kind == "stage");
            foreach (var oldSupport in new[] {false, true})
            {
                if (Has(receipts, hash) || oldSupport && !implicitSupport) break;
                foreach (var shape in new[] {json, ratioJson})
                {
                    if (shape == null || json["Weights"].Children.Skip(12).Any(weight => weight.AsDouble != 0)
                        || legacySource != null && Has(legacySource, "Weights") && legacySource["Weights"].Count > 12) continue;
                    // Reconstruct the twelve-lane identities before caps, with and without retired ratios.
                    var legacy = JsonFiles.ParseObject(shape.ToString());
                    var weights = new JSONArray();
                    foreach (var weight in legacy["Weights"].Children.Take(12)) weights.Add(weight);
                    legacy["Weights"] = weights;
                    if (oldSupport) legacy["SupportMode"] = "current";
                    var legacyHash = JsonFiles.ComputeRevision(Encoding.GetBytes(prefix + Canonical(legacy)));
                    var previousHash = shape == ratioJson ? PreviousSchemaImportHash(legacy, prefix, legacySource) : null;
                    if (Has(receipts, legacyHash)) hash = legacyHash;
                    else if (previousHash != null && Has(receipts, previousHash)) hash = previousHash;
                    if (Has(receipts, hash)) break;
                }
            }
            if (Has(receipts, hash))
            {
                // Keep the historical hash and add a ratio-free alias before plans can be stripped.
                if (hash != currentHash) receipts[currentHash] = JsonFiles.ParseObject(receipts[hash].ToString());
                var importedId = receipts[hash]["PresetId"].Value;
                if (root["Presets"].Children.Any(row => row["Id"].Value == importedId)) return importedId;
                issues.Add(kind + ": imported preset '" + importedId + "' was deleted; retained inline.");
                return string.Empty;
            }
            definition.Id = "import_" + hash.Substring(0, 56);
            var existing = root["Presets"].Children.FirstOrDefault(row => row["Id"].Value == definition.Id);
            if (existing != null && Canonical(existing) != Canonical(definition.ToJson()))
                throw new InvalidOperationException("Deterministic imported preset ID collision: " + definition.Id);
            if (existing == null)
            {
                if (root["Presets"].Count >= MaximumPresets)
                    throw new FormatException("Preset library migration would exceed 1024 presets while importing plan '" + source + "'; existing library and plan files were not modified.");
                root["Presets"].Add(definition.ToJson());
            }
            var receipt = new JSONObject();
            receipt["PresetId"] = definition.Id;
            receipt["Source"] = source;
            receipt["Kind"] = kind;
            receipts[hash] = receipt;
            return definition.Id;
        }

        private static bool Has(JSONNode node, string key)
        {
            return node.Linq.Any(row => row.Key == key);
        }

        private static string Canonical(JSONNode node)
        {
            if (node.IsObject)
                return "{" + string.Join(",", node.Linq.OrderBy(row => row.Key, StringComparer.Ordinal)
                    .Select(row => new JSONString(row.Key).ToString() + ":" + Canonical(row.Value))) + "}";
            if (node.IsArray) return "[" + string.Join(",", node.Children.Select(Canonical)) + "]";
            return node.ToString();
        }
    }
}
