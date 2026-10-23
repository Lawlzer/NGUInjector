using System;
using System.IO;
using System.Linq;
using NGUInjector.CustomPlans;
using SimpleJSON;

namespace NGUInjector
{
    internal static partial class ExternalUiChecks
    {
        private static readonly string[] PlanBloodFields = {
            "AutoBloodSpellsEnabled", "BloodSpaghettiGoalPercent", "BloodCounterfeitGoalPercent",
            "IronPillSaveSeconds", "IronPillMinWorth"
        };

        private static int RunPlanBloodPersistenceOnly()
        {
            _dir = Path.Combine(Path.GetTempPath(), "opencode", "ngu-plan-blood-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            try
            {
                NGUInjector.Main.Settings = new SavedSettings(_dir);
                NGUInjector.Main.Settings.SaveSettings();
                NGUInjector.Main.CustomPlanStore = new CustomPlanStore(Path.Combine(_dir, "custom-plans"));
                CheckPlanBloodPersistence();
                CheckAutoIronPillEnabled();
                CheckNguWeights();
                Console.WriteLine("PASS: plan Blood persistence and related Iron Pill/NGU regression checks: " + _assertions + " assertions.");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            finally { Directory.Delete(_dir, true); }
        }

        private static void AssertBloodUnset(CustomPlan plan, string label)
        {
            Assert(!plan.AutoBloodSpellsEnabled.HasValue && !plan.BloodSpaghettiGoalPercent.HasValue
                && !plan.BloodCounterfeitGoalPercent.HasValue && !plan.IronPillSaveSeconds.HasValue
                && !plan.IronPillMinWorth.HasValue, label);
        }

        private static void CheckPlanBloodPersistence()
        {
            var store = NGUInjector.Main.CustomPlanStore;
            var plansDir = Path.Combine(_dir, "custom-plans");
            var settingsPath = Path.Combine(_dir, "settings.json");
            var settingsBefore = File.ReadAllText(settingsPath);
            AssertBloodUnset(new CustomPlan(), "new Blood Magic overrides inherit globals");
            foreach (var builtIn in new[] {"default", "24hr", "infinite", "1h-3m-rebirth"})
                AssertBloodUnset(store.Load(builtIn), "built-in inherits globals: " + builtIn);

            foreach (var version in new[] {14, 17})
            foreach (var explicitNull in new[] {false, true})
            {
                var name = "blood-legacy-" + version + "-" + explicitNull;
                var root = store.ReadExternalPlan("default");
                root.Remove(CustomPlanStore.ExternalUiRevisionField);
                root["Version"] = version;
                root["Name"] = name;
                root.Remove("AutoIronPillEnabled");
                if (version < 16) { root.Remove("EnergyNguWeights"); root.Remove("MagicNguWeights"); }
                foreach (var field in PlanBloodFields)
                {
                    root.Remove(field);
                    if (explicitNull) root[field] = JSONNull.CreateOrGet();
                }
                root["futureBlood"] = JsonFiles.ParseObject("{\"precise\":9007199254740993,\"rows\":[null,false,{\"x\":1.234567890123456789}]}");
                root["Stages"][0]["futureBloodStage"] = "keep";
                var path = Path.Combine(plansDir, name + ".json");
                File.WriteAllText(path, root.ToString(2));
                var before = File.ReadAllText(path);
                var loaded = store.Load(name);
                AssertBloodUnset(loaded, "missing/null overrides remain unset on native load");
                Assert(loaded.AutoIronPillEnabled, "legacy automatic Iron Pill remains true");
                var read = store.ReadExternalPlan(name);
                Assert(PlanBloodFields.All(field => !read.Linq.Any(item => item.Key == field)), "normalized read omits all unset overrides");
                Assert(read["Version"].AsInt == 17 && loaded.Version == 17, "Blood overrides keep schema 17");
                Assert(File.ReadAllText(path) == before, "normalization reads never rewrite legacy or null plan bytes");
                var copy = store.CreatePlan(name + "-copy", loaded);
                AssertBloodUnset(store.Load(copy), "native copy retains fallback rather than snapshotting globals");
                store.SaveExternalPlan(name, read, read[CustomPlanStore.ExternalUiRevisionField].Value);
                AssertBloodUnset(store.Load(name), "explicit save does not materialize global defaults");
                var saved = store.ReadExternalPlan(name);
                Assert(saved["futureBlood"].ToString() == root["futureBlood"].ToString()
                    && saved["Stages"][0]["futureBloodStage"].Value == "keep", "unknown root and stage data survive normalization and save");
            }

            var first = new CustomPlan {AutoBloodSpellsEnabled = false, BloodSpaghettiGoalPercent = 0,
                BloodCounterfeitGoalPercent = 0, IronPillSaveSeconds = 0, IronPillMinWorth = 0, AutoIronPillEnabled = false};
            var second = new CustomPlan {AutoBloodSpellsEnabled = true, BloodSpaghettiGoalPercent = 100000,
                BloodCounterfeitGoalPercent = 100000, IronPillSaveSeconds = 604800, IronPillMinWorth = 1e8};
            store.Save("blood-first", first);
            store.Save("blood-second", second);
            var firstPath = Path.Combine(plansDir, "blood-first.json");
            var secondPath = Path.Combine(plansDir, "blood-second.json");
            var firstBytes = File.ReadAllText(firstPath);
            var secondBytes = File.ReadAllText(secondPath);
            var zero = store.ReadExternalPlan("blood-first");
            Assert(zero["AutoBloodSpellsEnabled"].IsBoolean && !zero["AutoBloodSpellsEnabled"].AsBool, "explicit false is serialized as boolean");
            Assert(PlanBloodFields.Skip(1).All(field => zero[field].IsNumber && zero[field].AsDouble == 0), "all explicit zeros are serialized as numbers");
            var copyName = store.CreateExternalPlan("blood-external-copy", zero);
            var copyPlan = store.Load(copyName);
            Assert(copyPlan.AutoBloodSpellsEnabled == false && copyPlan.BloodSpaghettiGoalPercent == 0
                && copyPlan.BloodCounterfeitGoalPercent == 0 && copyPlan.IronPillSaveSeconds == 0
                && copyPlan.IronPillMinWorth == 0 && !copyPlan.AutoIronPillEnabled, "external copy preserves false and every zero");
            var nativeCopy = store.CreatePlan("blood-native-copy", store.Load("blood-second"));
            var max = store.Load(nativeCopy);
            Assert(max.AutoBloodSpellsEnabled == true && max.BloodSpaghettiGoalPercent == 100000
                && max.BloodCounterfeitGoalPercent == 100000 && max.IronPillSaveSeconds == 604800
                && max.IronPillMinWorth == 1e8 && max.AutoIronPillEnabled, "native copy preserves all inclusive upper bounds");
            max.BloodSpaghettiGoalPercent = 7; max.IronPillMinWorth = 0.125;
            store.Save(nativeCopy, max);
            Assert(store.Load(nativeCopy).IronPillMinWorth == 0.125, "fractional finite worth survives native save/load");
            Assert(File.ReadAllText(firstPath) == firstBytes && File.ReadAllText(secondPath) == secondBytes, "editing copies never mutates either source plan");
            foreach (var field in PlanBloodFields) zero[field] = JSONNull.CreateOrGet();
            store.SaveExternalPlan("blood-first", zero, zero[CustomPlanStore.ExternalUiRevisionField].Value);
            AssertBloodUnset(store.Load("blood-first"), "null clears previously explicit false and zero overrides");
            Assert(File.ReadAllText(secondPath) == secondBytes, "editing first plan leaves second plan byte-identical");
            Assert(!store.Load("blood-first").AutoIronPillEnabled, "clearing overrides does not change existing automatic Iron Pill switch");
            var omission = store.ReadExternalPlan("blood-second");
            foreach (var field in PlanBloodFields) omission.Remove(field);
            store.SaveExternalPlan("blood-second", omission, omission[CustomPlanStore.ExternalUiRevisionField].Value);
            AssertBloodUnset(store.Load("blood-second"), "omission clears previously explicit upper-bound overrides");

            foreach (var field in PlanBloodFields.Skip(1).Take(3))
            foreach (var token in new[] {"0.0", "-0.0", "1e2", "1000e-1", "1.000e0"})
            {
                var read = store.ReadExternalPlan("blood-first");
                read[field] = JsonFiles.ParseObject("{\"value\":" + token + "}")["value"];
                store.SaveExternalPlan("blood-first", read, read[CustomPlanStore.ExternalUiRevisionField].Value);
                Assert(store.ReadExternalPlan("blood-first")[field].AsInt == (int) double.Parse(token, System.Globalization.CultureInfo.InvariantCulture),
                    "exact whole decimal/exponent accepted: " + field + "=" + token);
            }

            foreach (var field in PlanBloodFields)
            {
                var invalid = field == "AutoBloodSpellsEnabled"
                    ? new[] {"0", "1", "\"false\"", "\"true\"", "{}", "[]"}
                    : field == "IronPillMinWorth"
                        ? new[] {"-1", "100000000.01", "1e100", "1e999", "\"0\"", "false", "{}", "[]"}
                        : new[] {"-1", field == "IronPillSaveSeconds" ? "604801" : "100001", "1.5", "1e-400", "-1e-400", "1.0000000000000001", "2147483648", "1e100", "\"0\"", "false", "{}", "[]"};
                foreach (var token in invalid)
                {
                    var read = store.ReadExternalPlan("blood-first");
                    // Parse as a complete document so non-finite JSON uses the production parser too.
                    read.Remove(field);
                    var text = read.ToString().TrimEnd('}') + ",\"" + field + "\":" + token + "}";
                    var before = File.ReadAllText(firstPath);
                    Reject(() => { var bad = JsonFiles.ParseObject(text); store.SaveExternalPlan("blood-first", bad, read[CustomPlanStore.ExternalUiRevisionField].Value); }, "reject invalid Blood save " + field + "=" + token);
                    Assert(File.ReadAllText(firstPath) == before, "rejected Blood save preserves source bytes");
                    Reject(() => store.CreateExternalPlan("blood-invalid", JsonFiles.ParseObject(text)), "reject invalid Blood copy " + field + "=" + token);
                    Assert(!store.PlanExists("blood-invalid"), "invalid Blood copy never creates a file");
                    var badPath = Path.Combine(plansDir, "blood-invalid-read.json");
                    File.WriteAllText(badPath, text);
                    Reject(() => store.Load("blood-invalid-read"), "native load rejects invalid Blood field");
                    Reject(() => store.ReadExternalPlan("blood-invalid-read"), "external read rejects invalid Blood field");
                    Assert(File.ReadAllText(badPath) == text, "rejected reads never rewrite invalid plan");
                    File.Delete(badPath);
                }
            }
            foreach (var mutate in new Action<CustomPlan>[] {
                p => p.BloodSpaghettiGoalPercent = -1, p => p.BloodSpaghettiGoalPercent = 100001,
                p => p.BloodCounterfeitGoalPercent = -1, p => p.BloodCounterfeitGoalPercent = 100001,
                p => p.IronPillSaveSeconds = -1, p => p.IronPillSaveSeconds = 604801,
                p => p.IronPillMinWorth = -1, p => p.IronPillMinWorth = 1e8 + 1,
                p => p.IronPillMinWorth = double.NaN, p => p.IronPillMinWorth = double.PositiveInfinity,
                p => p.IronPillMinWorth = double.NegativeInfinity
            })
            {
                var plan = store.Load("blood-first"); mutate(plan);
                var before = File.ReadAllText(firstPath);
                Reject(() => store.Save("blood-first", plan), "native Blood save validates typed values");
                Reject(() => store.CreatePlan("blood-invalid-native", plan), "native Blood clone validates typed values");
                Assert(File.ReadAllText(firstPath) == before && !store.PlanExists("blood-invalid-native"), "native rejection leaves files unchanged");
            }
            Assert(File.ReadAllText(settingsPath) == settingsBefore, "plan Blood persistence never writes legacy global settings");
        }
    }
}
