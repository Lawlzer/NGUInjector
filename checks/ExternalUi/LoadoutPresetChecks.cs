using System;
using System.IO;
using System.Linq;
using NGUInjector.CustomPlans;
using SimpleJSON;

namespace NGUInjector
{
    internal static partial class ExternalUiChecks
    {
        private static void RejectLoadout(Action action, string name)
        {
            try { action(); }
            catch (FormatException) { Assert(true, name); return; }
            throw new Exception("FAIL (accepted): " + name);
        }

        private static void RejectInvalidOperation(Action action, string name)
        {
            try { action(); }
            catch (InvalidOperationException) { Assert(true, name); return; }
            throw new Exception("FAIL (accepted): " + name);
        }

        private static JSONObject LoadoutRow(string fields = "")
        {
            return JsonFiles.ParseObject("{\"Id\":\"mixed_1-A\",\"Name\":\"Mixed loadout\"" + fields + "}");
        }

        private static void CheckLoadoutPresets()
        {
            var assertionsBefore = _assertions;
            var settingsPath = Path.Combine(_dir, "settings.json");
            var settingsBytes = File.ReadAllBytes(settingsPath);
            var energyWeights = NGUInjector.Main.Settings.EnergyNguWeights;
            var magicWeights = NGUInjector.Main.Settings.MagicNguWeights;
            var minimal = LoadoutRow();
            var original = minimal.ToString();
            var defaults = CustomLoadoutPreset.Parse(minimal);
            var objectiveKeys = PresetDefinitions.ObjectiveKeys;
            var defaultWeights = PresetDefinitions.DefaultJson()["Weights"].Children.Select(weight => weight.AsDouble).ToArray();
            Assert(minimal.ToString() == original, "parsing missing preset fields never creates lazy JSON keys");
            Assert(defaultWeights.Length == objectiveKeys.Length && defaults.Weights.SequenceEqual(defaultWeights),
                "default weights match the current objective metadata and definition defaults");
            Assert(objectiveKeys.Skip(10).SequenceEqual(new[] {"adventurePower", "adventureToughness", "energyCap", "magicCap"}),
                "objective metadata retains adventure objectives followed by Energy Cap and Magic Cap");
            Assert(defaults.AutoUpdate && defaults.AdvancedTrainingWeights.SequenceEqual(new[] {1, 1, 0, 0, 0}),
                "preset-owned update and advanced training defaults remain independent of global NGU ratios");
            Assert(typeof(CustomLoadoutPreset).GetField("EnergyNguWeights") == null && typeof(CustomLoadoutPreset).GetField("MagicNguWeights") == null,
                "presets have no runtime-owned NGU ratio fields");
            Assert(defaults.MagicBloodPercent == -1 && defaults.SupportMode == "planned"
                && defaults.DiggerBudgetPercent == 75 && defaults.Zone == -1 && defaults.MinimumPower == 0
                && defaults.MinimumToughness == 0 && defaults.SwapSeconds == 5, "missing optional preset scalars use contract defaults");
            Assert(defaults.BeardPriority.Length == 0 && defaults.DiggerWeights.Length == 0, "default support arrays remain empty auto");
            var defaultJson = defaults.ToJson();
            Assert(new CustomLoadoutPreset().SupportMode == "planned" && defaultJson["SupportMode"].Value == "planned"
                && CustomLoadoutPreset.Parse(defaultJson).SupportMode == "planned", "new and missing-mode presets default to optimized support through serialization");
            foreach (var mode in new[] {"current", "planned"})
            {
                var row = LoadoutRow(",\"SupportMode\":\"" + mode + "\"");
                var source = row.ToString();
                Assert(CustomLoadoutPreset.Parse(CustomLoadoutPreset.Parse(row).ToJson()).SupportMode == mode,
                    "explicit support mode survives parse and serialization: " + mode);
                Assert(row.ToString() == source, "support default never mutates an explicit source choice: " + mode);
            }
            Assert(defaultJson["BeardPriority"].ToString() == "[]" && defaultJson["DiggerWeights"].ToString() == "[]",
                "serialization preserves empty auto support arrays");
            Assert(defaultJson.Count == 14 && !defaultJson.Linq.Any(item => item.Key == "ActiveLoadoutPresetId" || item.Key == "HorizonSeconds"
                || item.Key == "EnergyNguWeights" || item.Key == "MagicNguWeights"), "preset JSON exposes the ratio-free shared contract without a horizon");
            Assert(minimal.ToString() == original, "serializing a preset does not mutate its retained source");
            foreach (var legacy in new[] {"3600", "1", "604800", "0", "604801", "-1", "null", "true", "\"invalid\"", "[]", "{}", "1.5", "1e100"})
            {
                var row = LoadoutRow(",\"HorizonSeconds\":" + legacy);
                var source = row.ToString();
                var preset = CustomLoadoutPreset.Parse(row);
                Assert(preset.ToJson().ToString() == defaultJson.ToString(), "legacy horizon is ignored and stripped regardless of value: " + legacy);
                Assert(row.ToString() == source && preset.SourceJson.ToString() == source, "ignoring legacy horizon preserves source JSON: " + legacy);
            }

            var full = LoadoutRow(",\"Weights\":[0,0.25,3,4,5,1000000],\"HorizonSeconds\":604800,\"MagicBloodPercent\":100,\"SupportMode\":\"planned\","
                + "\"DiggerBudgetPercent\":95,\"Zone\":43,\"MinimumPower\":1e200,\"MinimumToughness\":1.25,\"SwapSeconds\":3600,"
                + "\"BeardPriority\":[6,0,5,1,4,2,3],\"DiggerWeights\":[0,1,2,3,4,5,6,7,8,9,10,1000000]");
            var fullPreset = CustomLoadoutPreset.Parse(full);
            var fullRoundTrip = CustomLoadoutPreset.Parse(fullPreset.ToJson());
            Assert(fullRoundTrip.Weights.SequenceEqual(new double[] {0, 0.25, 3, 4, 5, 1000000, 0, 0, 0, 0, 0, 0, 0, 0})
                && fullRoundTrip.BeardPriority.SequenceEqual(new[] {6, 0, 5, 1, 4, 2, 3})
                && fullRoundTrip.DiggerWeights.SequenceEqual(new double[] {0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 1000000}),
                "explicit preset arrays round trip without reordering objectives or support priorities");
            Assert(full["Weights"].Count == 6 && fullPreset.ToJson()["Weights"].Count == objectiveKeys.Length,
                "legacy six-weight source is untouched while serialization pads to the current objective count");
            var ten = LoadoutRow(",\"Weights\":[0,0,0,0,0,0,1,2,3,4]");
            var tenPreset = CustomLoadoutPreset.Parse(ten);
            Assert(ten["Weights"].Count == 10 && CustomLoadoutPreset.Parse(tenPreset.ToJson()).Weights.SequenceEqual(new double[] {0,0,0,0,0,0,1,2,3,4,0,0,0,0}),
                "legacy ten-weight source is untouched while serialization zero-pads adventure and cap objectives");
            var modern = CustomLoadoutPreset.Parse(LoadoutRow(",\"Weights\":[0,0,0,0,0,0,1,2,3,4,0.25,1000000],\"AutoUpdate\":false,"
                + "\"EnergyNguWeights\":[9,8,7,6,5,4,3,2,1],\"MagicNguWeights\":[7,6,5,4,3,2,1],\"AdvancedTrainingWeights\":[0,0,0,2,3]"));
            var modernRoundTrip = CustomLoadoutPreset.Parse(modern.ToJson());
            Assert(modernRoundTrip.Weights.SequenceEqual(new double[] {0,0,0,0,0,0,1,2,3,4,0.25,1000000,0,0}) && !modernRoundTrip.AutoUpdate
                && modernRoundTrip.AdvancedTrainingWeights.SequenceEqual(new[] {0,0,0,2,3}), "legacy twelve objectives retain adventure positions and training ratios while cap objectives default to zero");
            Assert(!modern.ToJson().Linq.Any(item => item.Key == "EnergyNguWeights" || item.Key == "MagicNguWeights")
                && modern.SourceJson["EnergyNguWeights"].ToString() == "[9,8,7,6,5,4,3,2,1]"
                && modern.SourceJson["MagicNguWeights"].ToString() == "[7,6,5,4,3,2,1]",
                "serialization strips valid retired NGU arrays without mutating retained legacy source");
            foreach (var index in Enumerable.Range(10, objectiveKeys.Length - 10))
            {
                var weights = new double[objectiveKeys.Length]; weights[index] = 1000000;
                var row = LoadoutRow(); var array = new JSONArray();
                foreach (var weight in weights) array.Add(weight);
                row["Weights"] = array;
                Assert(CustomLoadoutPreset.Parse(CustomLoadoutPreset.Parse(row).ToJson()).Weights.SequenceEqual(weights), objectiveKeys[index] + " alone satisfies positive-weight requirement and survives round trip");
                row["Weights"][0] = 1;
                foreach (var invalid in new[] {"-1", "1000001", "null", "true", "\"1\"", "[]", "{}", "1e100"})
                {
                    row["Weights"][index] = JsonFiles.ParseObject("{\"value\":" + invalid + "}")["value"];
                    RejectLoadout(() => CustomLoadoutPreset.Parse(row), "invalid trailing objective at " + index + "=" + invalid);
                }
                foreach (var invalid in new[] {-1d, 1000001d, double.NaN, double.PositiveInfinity, double.NegativeInfinity})
                {
                    var preset = CustomLoadoutPreset.Parse(LoadoutRow()); preset.Weights[index] = invalid;
                    RejectLoadout(() => preset.ToJson(), "native invalid trailing objective at " + index + "=" + invalid);
                }
            }
            Assert(fullRoundTrip.MagicBloodPercent == 100 && fullRoundTrip.SupportMode == "planned"
                && fullRoundTrip.DiggerBudgetPercent == 95 && fullRoundTrip.Zone == 43 && fullRoundTrip.MinimumPower == 1e200
                && fullRoundTrip.MinimumToughness == 1.25 && fullRoundTrip.SwapSeconds == 3600, "explicit preset scalars round trip at upper bounds");
            var lower = CustomLoadoutPreset.Parse(LoadoutRow(",\"MagicBloodPercent\":0,\"DiggerBudgetPercent\":0,\"Zone\":0,\"SwapSeconds\":0,\"DiggerWeights\":[0,0,0,0,0,0,0,0,0,0,0,0]"));
            Assert(lower.MagicBloodPercent == 0 && lower.DiggerBudgetPercent == 0
                && lower.Zone == 0 && lower.SwapSeconds == 0 && lower.DiggerWeights.All(weight => weight == 0), "inclusive lower bounds and all-zero explicit digger weights are valid");
            var exponentInts = CustomLoadoutPreset.Parse(LoadoutRow(",\"MagicBloodPercent\":-1.0,\"SwapSeconds\":50e-1"));
            Assert(exponentInts.MagicBloodPercent == -1 && exponentInts.SwapSeconds == 5,
                "whole integer tokens accept exact exponent and decimal notation");
            for (var zone = -2; zone <= 46; zone++)
            {
                var candidate = zone;
                var valid = zone == -1 || zone >= 0 && zone <= 43 && !new[] {6, 8, 11, 14, 16, 19, 23, 26, 30, 34, 38, 42}.Contains(zone);
                var row = LoadoutRow(",\"Zone\":" + candidate);
                if (valid) Assert(CustomLoadoutPreset.Parse(row).Zone == candidate, "regular or auto zone accepted: " + candidate);
                else RejectLoadout(() => CustomLoadoutPreset.Parse(row), "titan or nonexistent zone rejected: " + candidate);
            }

            foreach (var field in new[] {"Id", "Name", "SupportMode"})
            foreach (var invalid in new[] {"null", "true", "1", "[]", "{}"})
            {
                var row = LoadoutRow();
                row[field] = JsonFiles.ParseObject("{\"value\":" + invalid + "}")["value"];
                RejectLoadout(() => CustomLoadoutPreset.Parse(row), "strict string type " + field + "=" + invalid);
            }
            foreach (var field in new[] {"MagicBloodPercent", "DiggerBudgetPercent", "Zone", "SwapSeconds"})
            foreach (var invalid in new[] {"null", "true", "\"5\"", "[]", "{}", "1.5", "1.0000000000000000001", "1e-999", "2147483648", "1e100"})
                RejectLoadout(() => CustomLoadoutPreset.Parse(LoadoutRow(",\"" + field + "\":" + invalid)), "strict integer " + field + "=" + invalid);
            foreach (var field in new[] {"MinimumPower", "MinimumToughness"})
            foreach (var invalid in new[] {"null", "true", "\"5\"", "[]", "{}", "-0.01"})
                RejectLoadout(() => CustomLoadoutPreset.Parse(LoadoutRow(",\"" + field + "\":" + invalid)), "strict minimum " + field + "=" + invalid);
            foreach (var fields in new[] {
                ",\"Id\":\"\"", ",\"Id\":\"@active\"", ",\"Id\":\"bad.id\"", ",\"Id\":\"bad/id\"", ",\"Id\":\"bad id\"", ",\"Id\":\"bad\\n\"",
                ",\"Name\":\"\"", ",\"Name\":\"  \"", ",\"SupportMode\":\"auto\"", ",\"SupportMode\":\"Current\"",
                ",\"MagicBloodPercent\":-2", ",\"MagicBloodPercent\":101",
                ",\"DiggerBudgetPercent\":-1", ",\"DiggerBudgetPercent\":96", ",\"SwapSeconds\":-1", ",\"SwapSeconds\":3601", ",\"Zone\":1000",
                ",\"Weights\":[]", ",\"Weights\":[1,1,0,0,0]", ",\"Weights\":[1,1,0,0,0,0,0]", ",\"Weights\":[0,0,0,0,0,0]", ",\"Weights\":[0,0,0,0,0,0,0,0,0,0]",
                ",\"Weights\":[1,0,0,0,0,0,0,0,0,0,0]", ",\"Weights\":[1,0,0,0,0,0,0,0,0,0,0,0,0]", ",\"Weights\":[0,0,0,0,0,0,0,0,0,0,0,0]",
                ",\"Weights\":[0,0,0,0,0,0,0,0,0,0,0,0,0,0]", ",\"Weights\":[1,0,0,0,0,0,0,0,0,0,0,0,0,0,0]",
                ",\"Weights\":[1,-1,0,0,0,0]", ",\"Weights\":[1000001,0,0,0,0,0]", ",\"Weights\":[1,\"1\",0,0,0,0]",
                ",\"Weights\":[1,null,0,0,0,0]", ",\"Weights\":[1,true,0,0,0,0]", ",\"Weights\":null", ",\"Weights\":{}",
                ",\"BeardPriority\":null", ",\"BeardPriority\":{}", ",\"BeardPriority\":[1,1]", ",\"BeardPriority\":[-1]",
                ",\"BeardPriority\":[7]", ",\"BeardPriority\":[1.5]", ",\"BeardPriority\":[true]", ",\"BeardPriority\":[\"1\"]",
                ",\"DiggerWeights\":null", ",\"DiggerWeights\":{}", ",\"DiggerWeights\":[1]",
                ",\"DiggerWeights\":[-1,0,0,0,0,0,0,0,0,0,0,0]", ",\"DiggerWeights\":[1000001,0,0,0,0,0,0,0,0,0,0,0]",
                ",\"DiggerWeights\":[true,0,0,0,0,0,0,0,0,0,0,0]"})
            {
                // Assign overrides rather than introducing duplicate JSON keys into the strict parser.
                var row = LoadoutRow();
                foreach (var item in JsonFiles.ParseObject("{" + fields.Substring(1) + "}").Linq) row[item.Key] = item.Value;
                RejectLoadout(() => CustomLoadoutPreset.Parse(row), "invalid preset value " + fields);
            }
            foreach (var invalid in new[] {"null", "0", "\"true\"", "[]", "{}"})
                RejectLoadout(() => CustomLoadoutPreset.Parse(LoadoutRow(",\"AutoUpdate\":" + invalid)), "strict AutoUpdate boolean " + invalid);
            foreach (var pair in new[] {new {Key = "EnergyNguWeights", Count = 9}, new {Key = "MagicNguWeights", Count = 7}, new {Key = "AdvancedTrainingWeights", Count = 5}})
            {
                foreach (var invalid in new[] {"null", "{}", "[]", "[1]"}.Concat(new[] {"-1", "true", "\"1\"", "1.0000000000000000001", "2147483648"}
                    .Select(token => "[" + token + "," + string.Join(",", Enumerable.Repeat("0", pair.Count - 1)) + "]")))
                {
                    var row = LoadoutRow(",\"" + pair.Key + "\":" + invalid);
                    if (pair.Key == "AdvancedTrainingWeights") RejectLoadout(() => CustomLoadoutPreset.Parse(row), "strict AT ratios " + invalid);
                    else Assert(!CustomLoadoutPreset.Parse(row).ToJson().Linq.Any(field => field.Key == pair.Key), "malformed retired preset ratios ignored and stripped: " + pair.Key + "=" + invalid);
                }
            }
            foreach (var field in new[] {"Id", "Name"})
            {
                var row = LoadoutRow(); row.Remove(field);
                RejectLoadout(() => CustomLoadoutPreset.Parse(row), "required " + field);
                row = LoadoutRow(); row[field] = new string('x', field == "Id" ? 65 : 101);
                RejectLoadout(() => CustomLoadoutPreset.Parse(row), "bounded " + field);
                row[field] = new string('x', field == "Id" ? 64 : 100);
                Assert(CustomLoadoutPreset.Parse(row).ToJson()[field].Value == row[field].Value, "inclusive maximum " + field);
            }
            foreach (var malformed in new JSONNode[] {null, JSONNull.CreateOrGet(), new JSONArray(), new JSONString("preset")})
                RejectLoadout(() => CustomLoadoutPreset.Parse(malformed), "preset requires an object");
            RejectLoadout(() => CustomLoadoutPreset.Validate(null), "native null preset invalid");
            foreach (var value in new[] {double.NaN, double.PositiveInfinity, double.NegativeInfinity})
            foreach (var field in new[] {"Weights", "DiggerWeights", "MinimumPower", "MinimumToughness"})
            {
                var preset = CustomLoadoutPreset.Parse(LoadoutRow());
                if (field == "Weights") preset.Weights[0] = value;
                if (field == "DiggerWeights") { preset.DiggerWeights = new double[12]; preset.DiggerWeights[0] = value; }
                if (field == "MinimumPower") preset.MinimumPower = value;
                if (field == "MinimumToughness") preset.MinimumToughness = value;
                RejectLoadout(() => preset.ToJson(), "native nonfinite " + field);
            }

            var directory = Path.Combine(_dir, "loadout-presets", "plans");
            var store = new CustomPlanStore(directory);
            var path = Path.Combine(directory, "library.json");
            File.WriteAllText(path, "{\"Version\":15,\"Name\":\"library\",\"futureRoot\":9223372036854775807}");
            var legacyBytes = File.ReadAllBytes(path);
            Assert(store.Load("library").LoadoutPresets.Length == 0, "old plans without weighted library load empty");
            var normalized = store.ReadExternalPlan("library");
            Assert(normalized["LoadoutPresets"].ToString() == "[]" && normalized["Version"].AsInt == 17,
                "normalized old plan reads expose an empty library without a schema bump");
            Assert(legacyBytes.SequenceEqual(File.ReadAllBytes(path)), "load and normalized read never write defaults to disk");
            var minimalPath = Path.Combine(directory, "minimal.json");
            File.WriteAllText(minimalPath, "{\"Version\":15,\"LoadoutPresets\":[" + LoadoutRow().ToString() + "]}");
            var minimalBytes = File.ReadAllBytes(minimalPath);
            var minimalPlan = store.Load("minimal");
            Assert(minimalPlan.LoadoutPresets[0].Weights.SequenceEqual(defaults.Weights) && minimalPlan.LoadoutPresets[0].SourceJson.Count == 2,
                "loading persisted optional defaults leaves source JSON fields absent");
            var minimalRead = store.ReadExternalPlan("minimal");
            Assert(minimalRead["LoadoutPresets"][0].ToString() == LoadoutRow().ToString(), "historical plan rows remain exact provenance rather than receiving modern defaults");
            var globalMinimalId = store.PresetLibrary.ReadExternalLibrary()["MigrationReceipts"].Children.Single(row => row["Source"].Value == "minimal" && row["Kind"].Value == "loadout:mixed_1-A")["PresetId"].Value;
            Assert(store.PresetLibrary.Get(globalMinimalId).Weights.SequenceEqual(defaultWeights), "imported global definitions expose the current metadata defaults");
            Assert(minimalBytes.SequenceEqual(File.ReadAllBytes(minimalPath)), "normalized reads never persist missing optional preset defaults");
            var first = CustomLoadoutPreset.Parse(LoadoutRow(",\"future\":{\"integer\":9223372036854775807,\"decimal\":1.234567890123456789,\"exponent\":1.2300e+004,\"nested\":[null,true,{\"Name\":\"untouched\"}]}"));
            var second = CustomLoadoutPreset.Parse(LoadoutRow(",\"future\":{\"integer\":9007199254740993,\"decimal\":9.876543210987654321}"));
            first.SourceJson["EnergyNguWeights"] = PlanWithNguWeights("{}")["EnergyNguWeights"];
            first.SourceJson["MagicNguWeights"] = PlanWithNguWeights("{}")["MagicNguWeights"];
            second.Id = "second";
            var firstFuture = first.SourceJson["future"].ToString();
            var secondFuture = second.SourceJson["future"].ToString();
            var fixture = store.ReadExternalPlan("library"); fixture.Remove(CustomPlanStore.ExternalUiRevisionField);
            fixture["GoalPresets"] = JsonFiles.ParseObject("{\"rows\":[{\"Name\":\"Legacy A\",\"GearGoal\":9},{\"Name\":\"Legacy B\",\"GearGoal\":10}]}")["rows"];
            fixture["ActiveGoalPresetIndex"] = 1; fixture["Stages"][0]["GearGoal"] = 9;
            fixture["LoadoutPresets"] = new JSONArray();
            fixture["LoadoutPresets"].Add(first.SourceJson);
            var secondSource = JsonFiles.ParseObject(second.SourceJson.ToString()); secondSource["Id"] = second.Id;
            fixture["LoadoutPresets"].Add(secondSource);
            File.WriteAllText(path, fixture.ToString());
            var plan = store.Load("library"); store.Save("library", plan);
            var saved = store.ReadExternalPlan("library");
            Assert(saved["LoadoutPresets"][0]["future"].ToString() == firstFuture && saved["LoadoutPresets"][1]["future"].ToString() == secondFuture,
                "store preserves nested unknown preset numeric text");
            Assert(saved["futureRoot"].Value == "9223372036854775807", "weighted library save preserves unknown plan data");
            Assert(saved["LoadoutPresets"].Children.All(row => !row.Linq.Any(item => item.Key == "EnergyNguWeights" || item.Key == "MagicNguWeights"))
                && !File.ReadAllText(path).Contains("NguWeights"), "native plan save strips retired NGU arrays from historical preset provenance on disk");
            var legacyGoals = saved["GoalPresets"].ToString();
            var legacyStages = saved["Stages"].ToString();
            var legacyRows = saved["LoadoutPresets"].ToString();
            var global = store.PresetLibrary.ReadExternalLibrary();
            var firstId = global["MigrationReceipts"].Children.Single(row => row["Source"].Value == "library" && row["Kind"].Value == "loadout:mixed_1-A")["PresetId"].Value;
            var secondId = global["MigrationReceipts"].Children.Single(row => row["Source"].Value == "library" && row["Kind"].Value == "loadout:second")["PresetId"].Value;
            var globalFirst = global["Presets"].Children.Single(row => row["Id"].Value == firstId);
            globalFirst["EnergyNguWeights"] = first.SourceJson["EnergyNguWeights"];
            globalFirst["MagicNguWeights"] = first.SourceJson["MagicNguWeights"];
            global["futureLibrary"] = JsonFiles.ParseObject("{\"precise\":9223372036854775807,\"decimal\":1.234567890123456789,\"exponent\":1.2300e+004}");
            var futureLibrary = global["futureLibrary"].ToString();
            var reordered = new JSONArray(); foreach (var row in global["Presets"].Children.Reverse()) reordered.Add(row);
            global["Presets"] = reordered;
            var globalRevision = store.PresetLibrary.SaveExternalLibrary(global, global[CustomPlanStore.ExternalUiRevisionField].Value);
            var globalSaved = store.PresetLibrary.ReadExternalLibrary();
            Assert(globalRevision == globalSaved[CustomPlanStore.ExternalUiRevisionField].Value && globalSaved["futureLibrary"].ToString() == futureLibrary, "global save returns revision and preserves exact unknown root tokens");
            var libraryPath = Path.Combine(_dir, "loadout-presets", "presets.json");
            var persistedLibrary = JsonFiles.ParseObject(File.ReadAllText(libraryPath));
            Assert(persistedLibrary["Presets"].Children.All(row => !row.Linq.Any(item => item.Key == "EnergyNguWeights" || item.Key == "MagicNguWeights")),
                "global library persistence strips submitted retired NGU arrays rather than retaining them as extensions");
            var restartedLibrary = new PresetLibraryStore(Path.GetDirectoryName(directory), directory);
            Assert(restartedLibrary.Get(firstId).ToJson().ToString() == store.PresetLibrary.Get(firstId).ToJson().ToString()
                && restartedLibrary.ReadExternalLibrary()[CustomPlanStore.ExternalUiRevisionField].Value == globalRevision,
                "ratio-free global definitions and their exact persisted revision survive store restart");
            Assert(store.PresetLibrary.Get(firstId).ToJson()["future"].ToString() == firstFuture && store.PresetLibrary.Get(secondId).ToJson()["future"].ToString() == secondFuture,
                "unknown data follows global preset identity through reorder");
            Assert(saved["GoalPresets"].ToString() == legacyGoals && saved["Stages"].ToString() == legacyStages
                && saved["ActiveGoalPresetIndex"].AsInt == 1 && saved["GearGoal"].AsInt == 10,
                "global library reorder leaves legacy goals, active gear, and stages unchanged");
            var copied = store.ReadExternalPlan(store.CreatePlan("library-copy", plan));
            Assert(copied["LoadoutPresets"].ToString() == saved["LoadoutPresets"].ToString(), "native plan clone retains preset order and exact unknown numbers");
            copied = store.ReadExternalPlan(store.CreateExternalPlan("external-copy", saved));
            Assert(copied["LoadoutPresets"].ToString() == saved["LoadoutPresets"].ToString(), "external plan copy retains exact preset unknown data");
            var edited = saved["LoadoutPresets"][1];
            saved["LoadoutPresets"][1] = saved["LoadoutPresets"][0];
            saved["LoadoutPresets"][0] = edited;
            var provenanceBytes = File.ReadAllBytes(path);
            RejectInvalidOperation(() => store.SaveExternalPlan("library", saved, saved[CustomPlanStore.ExternalUiRevisionField].Value), "historical preset reorder is no longer externally writable");
            Assert(provenanceBytes.SequenceEqual(File.ReadAllBytes(path)) && store.ReadExternalPlan("library")["LoadoutPresets"].ToString() == legacyRows, "rejected provenance edit leaves historical definitions intact");

            foreach (var invalid in new[] {"null", "{}", "true", "\"presets\"", "[null]", "[{}]", "[1]"})
            {
                var root = store.ReadExternalPlan("library");
                root["LoadoutPresets"] = JsonFiles.ParseObject("{\"value\":" + invalid + "}")["value"];
                var bytes = File.ReadAllBytes(path);
                RejectLoadout(() => store.SaveExternalPlan("library", root, root[CustomPlanStore.ExternalUiRevisionField].Value), "invalid persisted collection " + invalid);
                Assert(bytes.SequenceEqual(File.ReadAllBytes(path)), "invalid collection save leaves disk unchanged");
                File.WriteAllText(Path.Combine(directory, "invalid.json"), root.ToString());
                RejectLoadout(() => store.Load("invalid"), "invalid collection load " + invalid);
                File.Delete(Path.Combine(directory, "invalid.json"));
            }
            foreach (var count in new[] {0, 32, 33})
            {
                var candidate = new CustomPlan {LoadoutPresets = Enumerable.Range(0, count).Select(index => new CustomLoadoutPreset {Id = "preset-" + index, Name = "Preset"}).ToArray()};
                if (count <= 32)
                {
                    store.Save("limits-" + count, candidate);
                    Assert(store.Load("limits-" + count).LoadoutPresets.Length == count, "legacy per-plan count boundary " + count);
                }
                else RejectLoadout(() => store.Save("limits-" + count, candidate), "native legacy collection rejects more than 32 presets");
            }
            var oversized = store.ReadExternalPlan("limits-32");
            oversized["LoadoutPresets"].Add(LoadoutRow());
            RejectLoadout(() => store.SaveExternalPlan("limits-32", oversized, oversized[CustomPlanStore.ExternalUiRevisionField].Value), "external legacy collection rejects more than 32 presets");
            File.WriteAllText(Path.Combine(directory, "invalid.json"), oversized.ToString());
            RejectLoadout(() => store.Load("invalid"), "persisted library rejects more than 32 presets");
            File.Delete(Path.Combine(directory, "invalid.json"));
            plan.LoadoutPresets = new[] {first, first};
            RejectLoadout(() => store.Save("library", plan), "native duplicate preset IDs rejected");
            var duplicate = store.ReadExternalPlan("library");
            duplicate["LoadoutPresets"][1]["Id"] = duplicate["LoadoutPresets"][0]["Id"].Value;
            var beforeDuplicate = File.ReadAllBytes(path);
            RejectLoadout(() => store.SaveExternalPlan("library", duplicate, duplicate[CustomPlanStore.ExternalUiRevisionField].Value), "external duplicate preset IDs rejected");
            Assert(beforeDuplicate.SequenceEqual(File.ReadAllBytes(path)), "duplicate IDs cannot partially overwrite persisted library");
            File.WriteAllText(Path.Combine(directory, "invalid.json"), duplicate.ToString());
            RejectLoadout(() => store.Load("invalid"), "persisted duplicate preset IDs rejected");
            File.Delete(Path.Combine(directory, "invalid.json"));
            foreach (var invalid in new[] {null, new CustomLoadoutPreset[] {null}})
            {
                plan.LoadoutPresets = invalid;
                RejectLoadout(() => store.Save("library", plan), "native invalid collection rejected");
            }
            saved = store.ReadExternalPlan("library");
            saved["LoadoutPresets"] = new JSONArray();
            RejectInvalidOperation(() => store.SaveExternalPlan("library", saved, saved[CustomPlanStore.ExternalUiRevisionField].Value), "historical definitions cannot be cleared through plan editing");
            global = store.PresetLibrary.ReadExternalLibrary();
            foreach (var index in Enumerable.Range(0, 33)) global["Presets"].Add(new CustomLoadoutPreset {Id = "global-" + index, Name = "Global"}.ToJson());
            store.PresetLibrary.SaveExternalLibrary(global, global[CustomPlanStore.ExternalUiRevisionField].Value);
            Assert(store.PresetLibrary.List().Count(row => row.Id.StartsWith("global-")) == 33, "global library is not constrained by the historical per-plan 32-row limit");
            global = store.PresetLibrary.ReadExternalLibrary(); var retained = new JSONArray();
            foreach (var row in global["Presets"].Children.Where(row => !row["Id"].Value.StartsWith("global-"))) retained.Add(row);
            global["Presets"] = retained;
            store.PresetLibrary.SaveExternalLibrary(global, global[CustomPlanStore.ExternalUiRevisionField].Value);
            Assert(store.PresetLibrary.List().All(row => !row.Id.StartsWith("global-")) && store.PresetLibrary.ReadExternalLibrary()["futureLibrary"].ToString() == futureLibrary,
                "unused global presets can be deleted without losing unknown library fields");
            saved = store.ReadExternalPlan("library");
            Assert(saved["LoadoutPresets"].ToString() == legacyRows && saved["GoalPresets"].ToString() == legacyGoals && saved["Stages"].ToString() == legacyStages,
                "global additions and deletions never modify historical plan definitions or stages");
            CheckGlobalPresetTransport(store);
            Assert(settingsBytes.SequenceEqual(File.ReadAllBytes(settingsPath))
                && NGUInjector.Main.Settings.EnergyNguWeights.SequenceEqual(energyWeights) && NGUInjector.Main.Settings.MagicNguWeights.SequenceEqual(magicWeights),
                "preset import, editing, persistence and transport never change live or durable global NGU ratios");
            Console.WriteLine("Loadout preset checks passed: " + (_assertions - assertionsBefore) + " assertions");
        }

        private static void CheckGlobalPresetTransport(CustomPlanStore store)
        {
            const int fileLimit = 2 * 1024 * 1024;
            const int commandLimit = 3 * 1024 * 1024;
            var previous = NGUInjector.Main.CustomPlanStore; var main = NGUInjector.Main.reference;
            var effects = LoadoutPresetBridgeEffects(main);
            try
            {
                NGUInjector.Main.CustomPlanStore = store;
                var root = store.PresetLibrary.ReadExternalLibrary(); var revision = root[CustomPlanStore.ExternalUiRevisionField].Value;
                root.Remove(CustomPlanStore.ExternalUiRevisionField);
                root["Presets"].Add(new CustomLoadoutPreset {Id = "transport_adventure", Name = "Transport adventure", AutoUpdate = false,
                    Weights = new double[] {0,0,0,0,0,0,0,0,0,0,2,5}}.ToJson());
                root["transportPadding"] = "";
                root["transportPadding"] = new string('x', fileLimit - System.Text.Encoding.UTF8.GetByteCount(root.ToString(4)));
                var args = new JSONObject(); args["library"] = root; args["expectedRevision"] = revision;
                var envelope = JsonFiles.ParseObject(Envelope("global-library-boundary", "savePresetLibrary", args)); envelope["padding"] = "";
                envelope["padding"] = new string('x', commandLimit - System.Text.Encoding.UTF8.GetByteCount(envelope.ToString()));
                var text = envelope.ToString();
                Assert(System.Text.Encoding.UTF8.GetByteCount(text) == commandLimit && System.Text.Encoding.UTF8.GetByteCount(root.ToString(4)) == fileLimit,
                    "transport fixture independently reaches exact 3 MiB command and 2 MiB library boundaries");
                main.Process("global-library-boundary", text);
                var result = Result("global-library-boundary");
                Assert(result["success"].AsBool && result["data"]["revision"].Value == store.PresetLibrary.ReadExternalLibrary()[CustomPlanStore.ExternalUiRevisionField].Value,
                    "real command dispatcher accepts maximum-size library save at the exact 3 MiB transport bound");
                var path = Path.Combine(_dir, "loadout-presets", "presets.json"); var bytes = File.ReadAllBytes(path);
                Assert(bytes.Length == fileLimit && store.PresetLibrary.Get("transport_adventure").Weights.SequenceEqual(new double[] {0,0,0,0,0,0,0,0,0,0,2,5,0,0}),
                    "maximum-size transport persists an actual 2 MiB file with adventure objectives intact and zero-padded cap objectives");
                main.Process("global-library-read", Envelope("global-library-read", "readPresetLibrary"));
                var snapshot = Result("global-library-read"); var read = snapshot["data"]["library"].AsObject;
                Assert(snapshot["success"].AsBool && read[CustomPlanStore.ExternalUiRevisionField].Value == JsonFiles.ComputeRevision(bytes)
                    && read["transportPadding"].Value == root["transportPadding"].Value, "maximum-size read response preserves unknown fields and hashes exact persisted bytes");
                Assert(read["Presets"].Children.All(row => row["Weights"].Count == PresetDefinitions.ObjectiveKeys.Length && !row.Linq.Any(field => field.Key == "HorizonSeconds"
                    || field.Key == "EnergyNguWeights" || field.Key == "MagicNguWeights")),
                    "global read transport emits the current objective count with horizon-free and NGU-ratio-free definitions");
                read["transportPadding"] = read["transportPadding"].Value + "x";
                args = new JSONObject(); args["library"] = read; args["expectedRevision"] = read[CustomPlanStore.ExternalUiRevisionField];
                main.Process("global-library-too-large", Envelope("global-library-too-large", "savePresetLibrary", args));
                result = Result("global-library-too-large");
                Assert(!result["success"].AsBool && result["message"].Value.Contains("2 MiB") && bytes.SequenceEqual(File.ReadAllBytes(path)),
                    "file limit remains 2 MiB even when a command fits the larger transport envelope");
                args["library"] = root;
                envelope = JsonFiles.ParseObject(Envelope("global-command-too-large", "savePresetLibrary", args)); envelope["padding"] = "";
                envelope["padding"] = new string('x', commandLimit + 1 - System.Text.Encoding.UTF8.GetByteCount(envelope.ToString())); text = envelope.ToString();
                Assert(System.Text.Encoding.UTF8.GetByteCount(text) == commandLimit + 1, "over-limit command differs from the valid transport bound by one byte");
                main.Process("global-command-too-large", text); result = Result("global-command-too-large");
                Assert(!result["success"].AsBool && result["message"].Value.Contains("limit") && bytes.SequenceEqual(File.ReadAllBytes(path)),
                    "one-byte-over command is rejected before writing its otherwise valid library");
                Assert(effects.SequenceEqual(LoadoutPresetBridgeEffects(main)), "library persistence commands invoke no game allocation or equipment action boundaries");
                root = store.PresetLibrary.ReadExternalLibrary(); root.Remove("transportPadding"); var retained = new JSONArray();
                foreach (var row in root["Presets"].Children.Where(row => row["Id"].Value != "transport_adventure")) retained.Add(row);
                root["Presets"] = retained; store.PresetLibrary.SaveExternalLibrary(root, root[CustomPlanStore.ExternalUiRevisionField].Value);
            }
            finally { NGUInjector.Main.CustomPlanStore = previous; }
        }
    }
}
