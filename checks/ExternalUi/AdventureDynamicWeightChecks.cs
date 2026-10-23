using System;
using System.IO;
using System.Linq;
using NGUInjector.CustomPlans;
using SimpleJSON;

namespace NGUInjector
{
    internal static partial class ExternalUiChecks
    {
        private const string AdventureWeightField = "AdventureDynamicWeight";

        private static int RunAdventureDynamicWeightOnly()
        {
            _dir = Path.Combine(Path.GetTempPath(), "opencode", "ngu-adventure-weight-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            try
            {
                NGUInjector.Main.Settings = new SavedSettings(_dir);
                NGUInjector.Main.Settings.SaveSettings();
                NGUInjector.Main.CustomPlanStore = new CustomPlanStore(Path.Combine(_dir, "custom-plans"));
                CheckAdventureDynamicWeight();
                Console.WriteLine("PASS: AdventureDynamicWeight persistence: " + _assertions + " assertions.");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            finally { Directory.Delete(_dir, true); }
        }

        private static void RejectAdventureWeight(Action action, string label)
        {
            try { action(); }
            catch (FormatException) { _assertions++; return; }
            throw new Exception("FAIL (accepted): " + label);
        }

        private static void CheckAdventureDynamicWeight()
        {
            var store = NGUInjector.Main.CustomPlanStore;
            var plansDir = Path.Combine(_dir, "custom-plans");
            Assert(new CustomPlanStage().AdventureDynamicWeight == 0d, "new stage adventure weight defaults to zero");
            foreach (var name in new[] {"default", "24hr", "infinite", "1h-3m-rebirth"})
                Assert(store.Load(name).Stages.All(stage => stage.AdventureDynamicWeight == 0d), "built-in adventure weights default to zero: " + name);

            var legacy = store.ReadExternalPlan("default");
            legacy.Remove(CustomPlanStore.ExternalUiRevisionField);
            legacy["Version"] = 14;
            legacy["Name"] = "adventure-weight-legacy";
            legacy["futureAdventure"] = JsonFiles.ParseObject("{\"precise\":9007199254740993,\"rows\":[null,true,1.234567890123456789]}");
            foreach (var stage in legacy["Stages"].Children) stage.Remove(AdventureWeightField);
            legacy["Stages"][0]["futureAdventureStage"] = JsonFiles.ParseObject("{\"precise\":9223372036854775807,\"rows\":[false,null,{}]}");
            var legacyPath = Path.Combine(plansDir, "adventure-weight-legacy.json");
            File.WriteAllText(legacyPath, legacy.ToString(2));
            var legacyBytes = File.ReadAllBytes(legacyPath);
            var loaded = store.Load("adventure-weight-legacy");
            Assert(loaded.Stages.All(stage => stage.AdventureDynamicWeight == 0d), "absent legacy weights default to zero on native load");
            var read = store.ReadExternalPlan("adventure-weight-legacy");
            Assert(read["Stages"].Children.All(stage => stage[AdventureWeightField].IsNumber && stage[AdventureWeightField].AsDouble == 0d), "external read normalizes missing weights as numeric zero");
            Assert(File.ReadAllBytes(legacyPath).SequenceEqual(legacyBytes), "legacy reads do not rewrite plan bytes");
            loaded.Stages[0].AdventureDynamicWeight = 0.125;
            store.Save("adventure-weight-legacy", loaded);
            Assert(store.Load("adventure-weight-legacy").Stages[0].AdventureDynamicWeight == 0.125, "native edit/save/load retains fractional weight");
            read = store.ReadExternalPlan("adventure-weight-legacy");
            Assert(read["futureAdventure"].ToString() == legacy["futureAdventure"].ToString()
                && read["Stages"][0]["futureAdventureStage"].ToString() == legacy["Stages"][0]["futureAdventureStage"].ToString(), "native roundtrip preserves unknown root and stage JSON exactly");
            var nativeCopy = store.CreatePlan("adventure-weight-native-copy", loaded);
            var externalCopy = store.CreateExternalPlan("adventure-weight-external-copy", read);
            foreach (var name in new[] {nativeCopy, externalCopy})
            {
                var copy = store.ReadExternalPlan(name);
                Assert(store.Load(name).Stages[0].AdventureDynamicWeight == 0.125
                    && copy["Stages"][0]["futureAdventureStage"].ToString() == legacy["Stages"][0]["futureAdventureStage"].ToString(), "copy preserves fractional weight and unknown stage JSON: " + name);
            }

            var path = Path.Combine(plansDir, nativeCopy + ".json");
            foreach (var value in new[] {0d, 0.125, 1d, 1e6})
            {
                var plan = store.Load(nativeCopy);
                plan.Stages[0].AdventureDynamicWeight = value;
                store.Save(nativeCopy, plan);
                var disk = JsonFiles.ParseObject(File.ReadAllText(path));
                Assert(disk["Stages"][0][AdventureWeightField].IsNumber && disk["Stages"][0][AdventureWeightField].AsDouble == value
                    && store.Load(nativeCopy).Stages[0].AdventureDynamicWeight == value, "native numeric boundary/fraction roundtrip: " + value);
                read = store.ReadExternalPlan(externalCopy);
                read["Stages"][0][AdventureWeightField] = value;
                var revision = store.SaveExternalPlan(externalCopy, read, read[CustomPlanStore.ExternalUiRevisionField].Value);
                var saved = store.ReadExternalPlan(externalCopy);
                Assert(saved["Stages"][0][AdventureWeightField].IsNumber && saved["Stages"][0][AdventureWeightField].AsDouble == value
                    && store.Load(externalCopy).Stages[0].AdventureDynamicWeight == value
                    && saved[CustomPlanStore.ExternalUiRevisionField].Value == revision, "external save/read/native-load roundtrip: " + value);
                Assert(saved["futureAdventure"].ToString() == legacy["futureAdventure"].ToString()
                    && saved["Stages"][0]["futureAdventureStage"].ToString() == legacy["Stages"][0]["futureAdventureStage"].ToString(), "external roundtrip preserves unknown JSON");
            }
            read = store.ReadExternalPlan(externalCopy);
            read["Stages"][0].Remove(AdventureWeightField);
            store.SaveExternalPlan(externalCopy, read, read[CustomPlanStore.ExternalUiRevisionField].Value);
            Assert(store.Load(externalCopy).Stages[0].AdventureDynamicWeight == 0d, "omission resets a previously nonzero weight to zero");

            foreach (var token in new[] {"null", "\"0\"", "\"0.125\"", "\"NaN\"", "true", "false", "{}", "[]", "-0.01", "1000000.01", "1e100", "1e999", "NaN", "Infinity", "-Infinity"})
            {
                read = store.ReadExternalPlan(nativeCopy);
                read["Name"] = "must-not-be-written";
                // Put the invalid field after valid rows to catch partial normalization/save bugs.
                var last = read["Stages"].Count - 1;
                var stage = read["Stages"][last];
                stage.Remove(AdventureWeightField);
                var marker = "\"invalidAdventureWeightToken\"";
                stage[AdventureWeightField] = "invalidAdventureWeightToken";
                var text = read.ToString().Replace(marker, token);
                var before = File.ReadAllBytes(path);
                RejectAdventureWeight(() => store.SaveExternalPlan(nativeCopy, JsonFiles.ParseObject(text), read[CustomPlanStore.ExternalUiRevisionField].Value), "invalid external weight " + token);
                Assert(File.ReadAllBytes(path).SequenceEqual(before), "invalid external save leaves destination byte-identical: " + token);
                RejectAdventureWeight(() => store.CreateExternalPlan("adventure-weight-invalid-copy", JsonFiles.ParseObject(text)), "invalid external copy " + token);
                Assert(!store.PlanExists("adventure-weight-invalid-copy"), "invalid external copy never creates a plan");
                var invalidPath = Path.Combine(plansDir, "adventure-weight-invalid-read.json");
                File.WriteAllText(invalidPath, text);
                RejectAdventureWeight(() => store.Load("adventure-weight-invalid-read"), "invalid native read " + token);
                RejectAdventureWeight(() => store.ReadExternalPlan("adventure-weight-invalid-read"), "invalid external read " + token);
                Assert(File.ReadAllText(invalidPath) == text, "rejected reads preserve invalid plan bytes");
                File.Delete(invalidPath);
            }

            foreach (var value in new[] {-0.01, 1e6 + 0.01, double.NaN, double.PositiveInfinity, double.NegativeInfinity})
            {
                var plan = store.Load(nativeCopy);
                plan.Stages[plan.Stages.Length - 1].AdventureDynamicWeight = value;
                var before = File.ReadAllBytes(path);
                RejectAdventureWeight(() => store.Save(nativeCopy, plan), "invalid native save");
                RejectAdventureWeight(() => store.Save("adventure-weight-invalid-native", plan), "invalid native creation");
                RejectAdventureWeight(() => store.CreatePlan("adventure-weight-invalid-native-copy", plan), "invalid native clone");
                Assert(File.ReadAllBytes(path).SequenceEqual(before) && !store.PlanExists("adventure-weight-invalid-native")
                    && !store.PlanExists("adventure-weight-invalid-native-copy"), "invalid typed weights preserve destination and create no plans");
            }
            Assert(!Directory.GetFiles(plansDir, "*.tmp").Any(), "rejected weights leave no temporary plan files");
        }
    }
}
