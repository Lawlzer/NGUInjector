using System;
using System.IO;
using System.Linq;
using NGUInjector.CustomPlans;
using SimpleJSON;

namespace NGUInjector
{
    internal static class GlobalNguSettingsChecks
    {
        private static readonly int[] EnergyDefault = {0, 0, 0, 0, 1, 0, 1, 0, 0};
        private static readonly int[] MagicDefault = {1, 0, 0, 0, 0, 0, 0};

        internal static void Run(Action<bool, string> assert, Func<string, string> directoryFor)
        {
            var directory = directoryFor("global-ngu-fresh");
            var settings = new SavedSettings(directory);
            assert(settings.EnergyNguWeights.SequenceEqual(EnergyDefault) && settings.MagicNguWeights.SequenceEqual(MagicDefault), "fresh globals have the exact Misc defaults");
            assert(typeof(CustomPlan).GetField("EnergyNguWeights") == null && typeof(CustomPlan).GetField("MagicNguWeights") == null
                && typeof(CustomLoadoutPreset).GetField("EnergyNguWeights") == null && typeof(CustomLoadoutPreset).GetField("MagicNguWeights") == null,
                "runtime models expose no per-plan or per-preset NGU ratio owners");
            settings.EnsureGlobalNguWeightsSaved();
            var path = Path.Combine(directory, "settings.json");
            var fresh = JsonFiles.ParseObject(File.ReadAllText(path));
            assert(fresh["_energyNguWeights"].ToString() == "[0,0,0,0,1,0,1,0,0]" && fresh["_magicNguWeights"].ToString() == "[1,0,0,0,0,0,0]", "startup persists both global keys before stores initialize");
            var copy = settings.EnergyNguWeights; copy[0] = 99;
            assert(settings.EnergyNguWeights.SequenceEqual(EnergyDefault), "reading globals cannot mutate live weights");
            var unselected = Path.Combine(directory, "custom-plans"); Directory.CreateDirectory(unselected);
            File.WriteAllText(Path.Combine(unselected, "default.json"), "{\"Version\":15,\"NguGoal\":0}");
            foreach (var selected in new[] {null, "missing"})
            {
                var root = new JSONObject();
                if (selected != null) root["_customPlanFile"] = selected;
                File.WriteAllText(path, root.ToString());
                settings = new SavedSettings(directory);
                assert(settings.LoadSettings() && settings.EnergyNguWeights.SequenceEqual(EnergyDefault) && settings.MagicNguWeights.SequenceEqual(MagicDefault), "no existing selected plan uses defaults: " + selected);
            }

            LegacyMigration(assert, directoryFor);
            Precedence(assert, directoryFor);
            Validation(assert, directoryFor);
            AtomicFailure(assert, directoryFor);
            RetiredStorage(assert, directoryFor);
        }

        private static void LegacyMigration(Action<bool, string> assert, Func<string, string> directoryFor)
        {
            foreach (var version in new[] {0, 5, 10, 11, 15, 16, 17})
            foreach (var goal in new[] {0, 1})
            foreach (var explicitWeights in new[] {false, true})
            {
                var directory = directoryFor("global-ngu-migration-" + version + "-" + goal + "-" + explicitWeights);
                var plans = Path.Combine(directory, "custom-plans"); Directory.CreateDirectory(plans);
                var legacy = new JSONObject();
                if (version != 0) legacy["Version"] = version;
                legacy["NguGoal"] = goal;
                if (explicitWeights)
                {
                    legacy["EnergyNguWeights"] = CustomPlanStore.SerializeNguWeights(Enumerable.Repeat(7, 9).ToArray(), 9, "energy");
                    legacy["MagicNguWeights"] = CustomPlanStore.SerializeNguWeights(new int[7], 7, "magic");
                }
                var planPath = Path.Combine(plans, "1h-3m-rebirth.json");
                File.WriteAllText(planPath, legacy.ToString());
                var planBytes = File.ReadAllBytes(planPath);
                var path = Path.Combine(directory, "settings.json");
                File.WriteAllText(path, "{\"_customPlanFile\":\"1h-3m-rebirth\",\"_useCustomRebirthPlan\":false,\"future\":9007199254740993}");
                var settings = new SavedSettings(directory);
                assert(settings.LoadSettings(), "legacy selected plan migrates before repair: " + version + "/" + goal + "/" + explicitWeights);
                var adventure = (version == 0 || version >= 11) && goal == 1;
                var energy = explicitWeights ? Enumerable.Repeat(7, 9).ToArray() : version >= 16 || adventure ? EnergyDefault : new[] {1, 1, 0, 1, 0, 1, 0, 0, 0};
                var magic = explicitWeights ? new int[7] : version >= 16 ? MagicDefault : adventure ? new[] {0, 0, 0, 0, 0, 0, 1} : new[] {0, 0, 1, 0, 0, 0, 0};
                assert(settings.EnergyNguWeights.SequenceEqual(energy) && settings.MagicNguWeights.SequenceEqual(magic), "legacy migration preserves effective per-version behavior");
                assert(planBytes.SequenceEqual(File.ReadAllBytes(planPath)), "settings migration does not remove its source");
                var persisted = JsonFiles.ParseObject(File.ReadAllText(path));
                assert(persisted["future"].Value == "9007199254740993" && persisted["_energyNguWeights"].Children.Select(value => value.AsInt).SequenceEqual(energy)
                    && persisted["_magicNguWeights"].Children.Select(value => value.AsInt).SequenceEqual(magic), "migration atomically persists globals and unknown settings tokens");
                var store = new CustomPlanStore(plans);
                var backupPath = Path.Combine(directory, "retired-custom-plans-number-v1", "1h-3m-rebirth.json");
                assert(!File.Exists(planPath) && planBytes.SequenceEqual(File.ReadAllBytes(backupPath)), "subsequent catalog retirement archives the exact legacy source after global migration");
                settings = new SavedSettings(directory);
                assert(settings.LoadSettings() && settings.CustomPlanFile == "24hr" && !settings.UseCustomRebirthPlan
                    && settings.EnergyNguWeights.SequenceEqual(energy) && settings.MagicNguWeights.SequenceEqual(magic), "restart retains migrated global settings and safe selection after legacy source retirement");
            }
        }

        private static void Precedence(Action<bool, string> assert, Func<string, string> directoryFor)
        {
            foreach (var existing in new[] {"energy", "magic", "both"})
            {
                var directory = directoryFor("global-ngu-precedence-" + existing);
                var plans = Path.Combine(directory, "custom-plans"); Directory.CreateDirectory(plans);
                var planPath = Path.Combine(plans, "selected.json");
                File.WriteAllText(planPath, "{\"Version\":15,\"NguGoal\":1}");
                var root = JsonFiles.ParseObject("{\"_customPlanFile\":\"selected\"}");
                if (existing != "magic") root["_energyNguWeights"] = CustomPlanStore.SerializeNguWeights(new int[9], 9, "energy");
                if (existing != "energy") root["_magicNguWeights"] = CustomPlanStore.SerializeNguWeights(Enumerable.Repeat(int.MaxValue, 7).ToArray(), 7, "magic");
                if (existing == "both") File.WriteAllText(planPath, "invalid legacy source must not be read");
                var path = Path.Combine(directory, "settings.json"); File.WriteAllText(path, root.ToString());
                var settings = new SavedSettings(directory);
                assert(settings.LoadSettings(), "existing global keys win independently: " + existing);
                var energy = existing == "magic" ? EnergyDefault : new int[9];
                var magic = existing == "energy" ? new[] {0, 0, 0, 0, 0, 0, 1} : Enumerable.Repeat(int.MaxValue, 7).ToArray();
                assert(settings.EnergyNguWeights.SequenceEqual(energy) && settings.MagicNguWeights.SequenceEqual(magic), "only missing lanes migrate; all-zero globals are explicit choices");
                File.WriteAllText(Path.Combine(plans, "other.json"), "{\"Version\":15,\"NguGoal\":0}");
                settings.ApplyPatch(JsonFiles.ParseObject("{\"_customPlanFile\":\"other\"}"));
                var reloaded = new SavedSettings(directory);
                assert(reloaded.LoadSettings() && reloaded.EnergyNguWeights.SequenceEqual(energy) && reloaded.MagicNguWeights.SequenceEqual(magic), "plan switching and restart never replay migration");
                var removed = JsonFiles.ParseObject(File.ReadAllText(path)); removed.Remove("_energyNguWeights"); removed.Remove("_magicNguWeights");
                File.WriteAllText(path, removed.ToString());
                assert(reloaded.LoadSettings() && reloaded.EnergyNguWeights.SequenceEqual(energy) && reloaded.MagicNguWeights.SequenceEqual(magic), "live reload cannot replay selected-plan migration if global keys are removed");
            }
        }

        private static void Validation(Action<bool, string> assert, Func<string, string> directoryFor)
        {
            var directory = directoryFor("global-ngu-validation");
            var plans = Path.Combine(directory, "custom-plans"); Directory.CreateDirectory(plans);
            var path = Path.Combine(directory, "settings.json");
            var settings = new SavedSettings(directory); settings.EnsureGlobalNguWeightsSaved();
            var store = new CustomPlanStore(plans);
            foreach (var energy in new[] {true, false})
            {
                var key = energy ? "_energyNguWeights" : "_magicNguWeights";
                var planKey = energy ? "EnergyNguWeights" : "MagicNguWeights";
                var count = energy ? 9 : 7;
                foreach (var invalid in new[] {"null", "[]", "{}", "true", "[1]", "[0,0,0,0,0,0,0,0,0,0]"}
                    .Concat(new[] {"-1", "2147483648", "1.5", "1.0000000000000000001", "2147483647.00000000001", "1e-400", "true", "\"1\"", "null"}
                        .Select(token => "[" + token + "," + string.Join(",", Enumerable.Repeat("0", count - 1)) + "]")))
                {
                    var patch = JsonFiles.ParseObject("{\"" + key + "\":" + invalid + "}");
                    var bytes = File.ReadAllBytes(path); var before = settings.EnergyNguWeights;
                    try { settings.ApplyPatch(patch); throw new Exception("accepted invalid global patch " + invalid); }
                    catch (FormatException) { }
                    assert(bytes.SequenceEqual(File.ReadAllBytes(path)) && settings.EnergyNguWeights.SequenceEqual(before), "invalid patch is rejected without publishing or writing: " + key + "=" + invalid);
                    var bad = JsonFiles.ParseObject(File.ReadAllText(path)); bad[key] = patch[key];
                    File.WriteAllText(path, bad.ToString()); bytes = File.ReadAllBytes(path);
                    assert(!new SavedSettings(directory).LoadSettings() && bytes.SequenceEqual(File.ReadAllBytes(path)), "invalid explicit global array fails loading instead of resetting");
                    File.WriteAllText(path, "{\"_customPlanFile\":\"malformed\"}");
                    var plan = JsonFiles.ParseObject("{\"Version\":15,\"" + planKey + "\":" + invalid + "}");
                    var planPath = Path.Combine(plans, "malformed.json"); File.WriteAllText(planPath, plan.ToString());
                    bytes = File.ReadAllBytes(path); var planBytes = File.ReadAllBytes(planPath);
                    assert(!new SavedSettings(directory).LoadSettings() && bytes.SequenceEqual(File.ReadAllBytes(path)) && planBytes.SequenceEqual(File.ReadAllBytes(planPath)), "malformed explicit legacy array blocks migration without data loss");
                    var ignored = CustomPlanStore.ReadForPresetMigration(plan.ToString(), "malformed");
                    assert(ignored.SourceJson[planKey].ToString() == invalid, "normal plan parsing ignores malformed retired arrays without changing source");
                    var preset = JsonFiles.ParseObject("{\"Id\":\"legacy\",\"Name\":\"Legacy\",\"" + planKey + "\":" + invalid + "}");
                    assert(!CustomLoadoutPreset.Parse(preset).ToJson().Linq.Any(field => field.Key == planKey), "normal preset parsing ignores and strips malformed retired arrays");
                    var globals = JsonFiles.ParseObject(UnityEngine.JsonUtility.ToJson(settings)); globals["_customPlanFile"] = "malformed";
                    File.WriteAllText(path, globals.ToString()); bytes = File.ReadAllBytes(path);
                    assert(settings.LoadSettings() && bytes.SequenceEqual(File.ReadAllBytes(path)), "present globals bypass malformed selected-plan ratios and explicitly refresh the writer baseline without rewriting settings");
                    store.Save("malformed", ignored);
                    assert(!store.ReadExternalPlan("malformed").Linq.Any(field => field.Key == planKey), "normal storage strips malformed retired arrays without blocking preset receipt lookup");
                }
                var valid = "[2.147483647e9,1.000,10e-1," + string.Join(",", Enumerable.Repeat("0", count - 3)) + "]";
                settings.ApplyPatch(JsonFiles.ParseObject("{\"" + key + "\":" + valid + "}"));
                var values = energy ? settings.EnergyNguWeights : settings.MagicNguWeights;
                assert(values.Take(3).SequenceEqual(new[] {int.MaxValue, 1, 1}) && values.Skip(3).All(value => value == 0), "exact integral exponent and decimal tokens remain valid");
            }
            settings.EnergyNguWeights = new int[9]; settings.MagicNguWeights = new int[7];
            var allZero = new SavedSettings(directory);
            assert(allZero.LoadSettings() && allZero.EnergyNguWeights.All(value => value == 0) && allZero.MagicNguWeights.All(value => value == 0), "public setters persist valid all-zero arrays");
            foreach (var energy in new[] {true, false})
            foreach (var invalid in new[] {null, new int[0], new int[1], Enumerable.Repeat(-1, energy ? 9 : 7).ToArray()})
            {
                var before = File.ReadAllBytes(path);
                try
                {
                    if (energy) settings.EnergyNguWeights = invalid; else settings.MagicNguWeights = invalid;
                    throw new Exception("accepted invalid native globals");
                }
                catch (FormatException) { }
                assert(before.SequenceEqual(File.ReadAllBytes(path)) && settings.EnergyNguWeights.All(value => value == 0)
                    && settings.MagicNguWeights.All(value => value == 0), "invalid native setter cannot write or publish malformed globals");
            }
            var supplied = Enumerable.Repeat(2, 9).ToArray(); settings.EnergyNguWeights = supplied; supplied[0] = -1;
            var candidate = new SavedSettings(null); candidate.MassUpdate(settings); settings.EnergyNguWeights = new int[9];
            assert(candidate.EnergyNguWeights.All(value => value == 2), "setters and MassUpdate clone globals instead of sharing mutable ownership");
        }

        private static void AtomicFailure(Action<bool, string> assert, Func<string, string> directoryFor)
        {
            var directory = directoryFor("global-ngu-atomic");
            var plans = Path.Combine(directory, "custom-plans"); Directory.CreateDirectory(plans);
            var planPath = Path.Combine(plans, "1h-3m-rebirth.json"); File.WriteAllText(planPath, "{\"Version\":15,\"NguGoal\":1}");
            var path = Path.Combine(directory, "settings.json"); File.WriteAllText(path, "{\"_customPlanFile\":\"1h-3m-rebirth\"}");
            var before = File.ReadAllBytes(path); var source = File.ReadAllBytes(planPath);
            var settings = new SavedSettings(directory);
            using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                assert(!settings.LoadSettings(), "blocked atomic replace fails settings migration");
            assert(settings.EnergyNguWeights.SequenceEqual(EnergyDefault) && settings.MagicNguWeights.SequenceEqual(MagicDefault)
                && before.SequenceEqual(File.ReadAllBytes(path)) && source.SequenceEqual(File.ReadAllBytes(planPath))
                && Directory.GetFiles(directory, "*.tmp").Length == 0, "failed migration leaves live defaults, original files and no temporary writes");
            assert(settings.LoadSettings() && settings.MagicNguWeights.SequenceEqual(new[] {0, 0, 0, 0, 0, 0, 1}), "migration retries successfully after atomic write failure");
            before = File.ReadAllBytes(path);
            using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try { settings.MagicNguWeights = new int[7]; throw new Exception("accepted blocked global setter"); }
                catch (IOException) { }
            }
            assert(before.SequenceEqual(File.ReadAllBytes(path)) && settings.MagicNguWeights[6] == 1, "failed public setter does not publish the candidate");
        }

        private static void RetiredStorage(Action<bool, string> assert, Func<string, string> directoryFor)
        {
            var directory = directoryFor("global-ngu-retired-storage");
            var plans = Path.Combine(directory, "custom-plans");
            var store = new CustomPlanStore(plans);
            var raw = JsonFiles.ParseObject("{\"Version\":17,\"NguGoal\":1,\"EnergyNguWeights\":[0,0,0,0,7,0,3,0,0],\"MagicNguWeights\":[0,2,0,0,0,0,5],"
                + "\"GoalPresets\":[{\"Name\":\"Goal\",\"GearGoal\":4,\"EnergyNguWeights\":[9,8,7,6,5,4,3,2,1],\"MagicNguWeights\":[7,6,5,4,3,2,1],\"future\":{\"EnergyNguWeights\":\"not a retired field\",\"MagicNguWeights\":true}}],"
                + "\"LoadoutPresets\":[{\"Id\":\"old\",\"Name\":\"Old\",\"Weights\":[1,1,0,0,0,0],\"EnergyNguWeights\":[9,8,7,6,5,4,3,2,1],\"MagicNguWeights\":[7,6,5,4,3,2,1],\"future\":9007199254740993}],\"future\":1.2300e+004}");
            var path = Path.Combine(plans, "legacy.json"); File.WriteAllText(path, raw.ToString());
            var model = store.Load("legacy"); var source = model.SourceJson.ToString();
            var external = store.ReadExternalPlan("legacy");
            assert(!external.Linq.Any(field => field.Key == "NguGoal" || field.Key == "EnergyNguWeights" || field.Key == "MagicNguWeights")
                && !external["LoadoutPresets"][0].Linq.Any(field => field.Key == "EnergyNguWeights" || field.Key == "MagicNguWeights")
                && !external["GoalPresets"][0].Linq.Any(field => field.Key == "EnergyNguWeights" || field.Key == "MagicNguWeights"), "external projection strips retired fields from both historical preset arrays");
            external["EnergyNguWeights"] = raw["EnergyNguWeights"];
            external["MagicNguWeights"] = raw["MagicNguWeights"];
            external["LoadoutPresets"][0]["EnergyNguWeights"] = raw["LoadoutPresets"][0]["EnergyNguWeights"];
            external["LoadoutPresets"][0]["MagicNguWeights"] = raw["LoadoutPresets"][0]["MagicNguWeights"];
            external["GoalPresets"][0]["EnergyNguWeights"] = raw["GoalPresets"][0]["EnergyNguWeights"];
            external["GoalPresets"][0]["MagicNguWeights"] = raw["GoalPresets"][0]["MagicNguWeights"];
            store.SaveExternalPlan("legacy", external, external[CustomPlanStore.ExternalUiRevisionField].Value);
            var saved = JsonFiles.ParseObject(File.ReadAllText(path));
            assert(saved["Version"].AsInt == 18 && !saved.Linq.Any(field => field.Key == "NguGoal" || field.Key == "EnergyNguWeights" || field.Key == "MagicNguWeights")
                && !saved["LoadoutPresets"][0].Linq.Any(field => field.Key == "EnergyNguWeights" || field.Key == "MagicNguWeights")
                && !saved["GoalPresets"][0].Linq.Any(field => field.Key == "EnergyNguWeights" || field.Key == "MagicNguWeights"), "save strips reintroduced retired ratios without bumping plan version");
            assert(saved["future"].Value == "1.2300e+004" && saved["LoadoutPresets"][0]["future"].Value == "9007199254740993"
                && saved["GoalPresets"][0]["future"].ToString() == raw["GoalPresets"][0]["future"].ToString()
                && model.SourceJson.ToString() == source, "retirement preserves unrelated historical lexemes and nested unknown fields without mutating SourceJson");
            var library = store.PresetLibrary.ReadExternalLibrary();
            assert(library["Presets"].Children.All(row => !row.Linq.Any(field => field.Key == "EnergyNguWeights" || field.Key == "MagicNguWeights")), "library persists no per-preset NGU owner arrays");
        }
    }
}
