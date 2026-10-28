using System;
using System.IO;
using System.Linq;
using NGUInjector.CustomPlans;
using SimpleJSON;

namespace NGUInjector
{
    internal static partial class ExternalUiChecks
    {
        private static int RunFightSchedulesOnly()
        {
            _dir = Path.Combine(Path.GetTempPath(), "opencode", "ngu-fight-schedules-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            try
            {
                NGUInjector.Main.Settings = new SavedSettings(_dir);
                NGUInjector.Main.Settings.SaveSettings();
                NGUInjector.Main.CustomPlanStore = new CustomPlanStore(Path.Combine(_dir, "custom-plans"));
                CheckFightSchedules();
                Console.WriteLine("PASS: fight schedule persistence: " + _assertions + " assertions.");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            finally { Directory.Delete(_dir, true); }
        }

        private static void RejectFightSchedule(Action action, string label)
        {
            try { action(); }
            catch (FormatException) { _assertions++; return; }
            throw new Exception("FAIL (accepted): " + label);
        }

        private static void CheckFightSchedules()
        {
            CheckFightScheduleMigration();
            CheckFightScheduleRoundtrip();
            CheckFightScheduleValidation();
            CheckFightScheduleFutureVersion();
        }

        private static void CheckFightScheduleMigration()
        {
            var store = NGUInjector.Main.CustomPlanStore;
            var path = Path.Combine(_dir, "custom-plans", "fight-legacy.json");
            var legacy = "{\"Name\":\"fight-legacy\",\"Stages\":[{}, {\"FightOnEntry\":false}, {\"FightOnEntry\":true}, {\"FightOnEntry\":true,\"FightSchedule\":null}]}";
            foreach (var version in new[] {0, 14, 17, 18})
            {
                var raw = JsonFiles.ParseObject(legacy);
                if (version != 0) raw["Version"] = version;
                File.WriteAllText(path, raw.ToString());
                var before = File.ReadAllBytes(path);
                var loaded = store.Load("fight-legacy");
                var read = store.ReadExternalPlan("fight-legacy");
                Assert(loaded.Version == 18 && read["Version"].AsInt == 18, "fight migration returns version 18");
                Assert(loaded.Stages[0].FightSchedule == null && loaded.Stages[1].FightSchedule == null
                    && loaded.Stages[3].FightSchedule == null, "missing/false disables and explicit schedule null overrides legacy true");
                var schedule = loaded.Stages[2].FightSchedule;
                Assert(schedule.Id == "legacy-entry-v1-2" && schedule.Anchor == 0 && schedule.DelaySeconds == 0 && schedule.IntervalSeconds == 0,
                    "legacy true migrates deterministically to one immediate entry fight");
                Assert(read["Stages"][0]["FightSchedule"].IsNull && read["Stages"][1]["FightSchedule"].IsNull
                    && read["Stages"][3]["FightSchedule"].IsNull && read["Stages"].Children.All(row => !row.Linq.Any(field => field.Key == "FightOnEntry")),
                    "canonical projection removes legacy flags and explicitly serializes disabled schedules");
                Assert(store.ReadExternalPlan("fight-legacy").ToString() == read.ToString()
                    && store.Load("fight-legacy").Stages[2].FightSchedule.Id == schedule.Id
                    && before.SequenceEqual(File.ReadAllBytes(path)), "repeated reads have stable IDs and never rewrite source bytes");
                store.SaveExternalPlan("fight-legacy", read, read[CustomPlanStore.ExternalUiRevisionField].Value);
                Assert(store.Load("fight-legacy").Stages[2].FightSchedule.Id == schedule.Id, "legacy ID survives canonical CAS save");
                loaded.Stages[2].FightSchedule = null;
                store.Save("fight-legacy", loaded);
                Assert(store.Load("fight-legacy").Stages.All(row => row.FightSchedule == null),
                    "disabling a model retaining legacy source true does not resurrect the schedule");
            }
            Assert(typeof(CustomPlanStage).GetField("FightOnEntry") == null, "legacy boolean is absent from the model");
            foreach (var invalid in new[] {"null", "1", "\"true\"", "{}", "[]"})
            foreach (var schedule in new[] {"", ",\"FightSchedule\":null",
                ",\"FightSchedule\":{\"Id\":\"explicit\",\"Anchor\":1,\"DelaySeconds\":5,\"IntervalSeconds\":10}"})
                RejectFightSchedule(() => store.CreateExternalPlan("fight-legacy-invalid", JsonFiles.ParseObject(
                    "{\"Stages\":[{\"FightOnEntry\":" + invalid + schedule + "}]}")), "strict legacy boolean regardless of schedule presence: " + invalid + schedule);
        }

        private static void CheckFightScheduleRoundtrip()
        {
            var store = NGUInjector.Main.CustomPlanStore;
            var raw = JsonFiles.ParseObject("{\"Version\":18,\"futureRoot\":9007199254740993,\"Stages\":[{\"Name\":\"scheduled\",\"futureRow\":42,\"FightOnEntry\":true,"
                + "\"FightSchedule\":{\"Id\":\"stable_A-1\",\"Anchor\":1,\"DelaySeconds\":12,\"IntervalSeconds\":30,\"future\":{\"exact\":1.234567890123456789}}},{\"Name\":\"disabled\"}]}");
            var name = store.CreateExternalPlan("fight-roundtrip", raw);
            var model = store.Load(name);
            model.Stages = model.Stages.Reverse().ToArray();
            model.Stages[1].FightSchedule.DelaySeconds = int.MaxValue;
            store.Save(name, model);
            var read = store.ReadExternalPlan(name);
            store.SaveExternalPlan(name, read, read[CustomPlanStore.ExternalUiRevisionField].Value);
            foreach (var copy in new[] {store.CreatePlan("fight-native-copy", store.Load(name)), store.CreateExternalPlan("fight-external-copy", read)})
            {
                var copied = store.Load(copy);
                var schedule = copied.Stages[1].FightSchedule;
                Assert(copied.Stages[0].FightSchedule == null && schedule.Id == "stable_A-1" && schedule.Anchor == 1
                    && schedule.DelaySeconds == int.MaxValue && schedule.IntervalSeconds == 30, "reorder and both copy paths preserve identity and timing");
                var json = store.ReadExternalPlan(copy);
                Assert(json["futureRoot"].Value == "9007199254740993" && json["Stages"][1]["futureRow"].AsInt == 42
                    && json["Stages"][1]["FightSchedule"]["future"]["exact"].Value == "1.234567890123456789", "root, row and schedule extensions preserve exact numeric tokens");
                copied.Stages[1].FightSchedule = null;
                store.Save(copy, copied);
                Assert(store.Load(copy).Stages[1].FightSchedule == null && store.ReadExternalPlan(copy)["Stages"][1]["FightSchedule"].IsNull,
                    "disabled schedule overwrites retained schedule SourceJson");
                Assert(store.Load(name).Stages[1].FightSchedule.Id == "stable_A-1", "editing a copy does not change its source");
            }
            var first = new CustomFightSchedule(); var second = new CustomFightSchedule();
            Guid parsed;
            Assert(first.Id.Length == 32 && Guid.TryParseExact(first.Id, "N", out parsed) && first.Id != second.Id, "new schedules receive distinct GUID N IDs");
        }

        private static void CheckFightScheduleValidation()
        {
            var store = NGUInjector.Main.CustomPlanStore;
            var name = store.CreateExternalPlan("fight-valid", JsonFiles.ParseObject("{\"Version\":18,\"Stages\":[{\"FightSchedule\":{\"Id\":\"valid\",\"Anchor\":0,\"DelaySeconds\":0,\"IntervalSeconds\":0}}]}"));
            var path = Path.Combine(_dir, "custom-plans", name + ".json");
            var bytes = File.ReadAllBytes(path);
            foreach (var field in new[] {"FightSchedule", "Id", "Anchor", "DelaySeconds", "IntervalSeconds"})
            {
                var invalid = field == "FightSchedule" ? new[] {"true", "0", "\"schedule\"", "[]", "{}"}
                    : field == "Id" ? new[] {"null", "0", "true", "{}", "[]", "\"\"", "\"has space\"", "\"slash/id\"", "\"\\u00e9\"", "\"" + new string('a', 129) + "\""}
                    : new[] {"null", "true", "\"1\"", "{}", "[]", "-1", "1.5", "2147483648", "2147483647.0000000001", "1.000000000000000000001", "1e-400", "-1e-400", "1e-9223372036854775808"};
                foreach (var value in invalid.Concat(field == "Anchor" ? new[] {"2", "2147483647"} : new string[0]))
                {
                    var bad = store.ReadExternalPlan(name);
                    bad["Stages"][0]["FightOnEntry"] = true;
                    var fixture = JsonFiles.ParseObject("{\"value\":" + value + "}")["value"];
                    if (field == "FightSchedule") bad["Stages"][0][field] = fixture;
                    else bad["Stages"][0]["FightSchedule"][field] = fixture;
                    RejectFightSchedule(() => store.SaveExternalPlan(name, bad, bad[CustomPlanStore.ExternalUiRevisionField].Value), field + " rejects " + value);
                    RejectFightSchedule(() => store.CreateExternalPlan("fight-invalid", bad), "copy rejects " + field + "/" + value);
                    Assert(bytes.SequenceEqual(File.ReadAllBytes(path)) && !store.PlanExists("fight-invalid"), "invalid schedules cannot write or fall back to legacy true");
                }
            }
            foreach (var field in new[] {"Id", "Anchor", "DelaySeconds", "IntervalSeconds"})
            {
                var bad = store.ReadExternalPlan(name); bad["Stages"][0]["FightSchedule"].Remove(field);
                RejectFightSchedule(() => store.SaveExternalPlan(name, bad, bad[CustomPlanStore.ExternalUiRevisionField].Value), "missing schedule field " + field);
            }
            var duplicate = store.ReadExternalPlan(name);
            duplicate["Stages"].Add(JsonFiles.ParseObject(duplicate["Stages"][0].ToString()));
            RejectFightSchedule(() => store.SaveExternalPlan(name, duplicate, duplicate[CustomPlanStore.ExternalUiRevisionField].Value), "duplicate schedule IDs in JSON");
            foreach (var mutate in new Action<CustomFightSchedule>[] {
                schedule => schedule.Id = null, schedule => schedule.Id = "", schedule => schedule.Id = "bad id",
                schedule => schedule.Id = new string('a', 129), schedule => schedule.Anchor = -1, schedule => schedule.Anchor = 2,
                schedule => schedule.DelaySeconds = -1, schedule => schedule.IntervalSeconds = -1 })
            {
                var model = store.Load(name); mutate(model.Stages[0].FightSchedule);
                RejectFightSchedule(() => model.ValidateFightSchedules(), "in-memory model rejects invalid schedule");
                RejectFightSchedule(() => store.Save(name, model), "native save rejects invalid schedule");
                RejectFightSchedule(() => store.CreatePlan("fight-invalid-native-copy", model), "native copy rejects invalid schedule");
                Assert(bytes.SequenceEqual(File.ReadAllBytes(path)), "invalid native save preserves file bytes");
            }
            var duplicated = store.Load(name); duplicated.Stages = new[] {duplicated.Stages[0], duplicated.Stages[0]};
            RejectFightSchedule(() => duplicated.ValidateFightSchedules(), "model rejects duplicate IDs");
            RejectFightSchedule(() => store.Save(name, duplicated), "native save rejects duplicate IDs");
            foreach (var value in new[] {"0", "-0", "1.0", "10e-1", "0e-999", "2147483647", "21474836470e-1"})
            {
                var valid = store.ReadExternalPlan(name);
                valid["Stages"][0]["FightSchedule"]["Id"] = new string('a', 128);
                valid["Stages"][0]["FightSchedule"]["Anchor"] = 1;
                var fixture = JsonFiles.ParseObject("{\"value\":" + value + "}")["value"];
                Assert(fixture.IsNumber && fixture.Value == value, "numeric fixture retains its exact JSON token: " + value);
                valid["Stages"][0]["FightSchedule"]["DelaySeconds"] = fixture;
                valid["Stages"][0]["FightSchedule"]["IntervalSeconds"] = fixture;
                store.SaveExternalPlan(name, valid, valid[CustomPlanStore.ExternalUiRevisionField].Value);
                var loaded = store.Load(name).Stages[0].FightSchedule;
                var expected = value.StartsWith("2147483647", StringComparison.Ordinal) ? int.MaxValue : value == "1.0" || value == "10e-1" ? 1 : 0;
                Assert(loaded.Id.Length == 128 && loaded.Anchor == 1 && loaded.DelaySeconds == expected && loaded.IntervalSeconds == expected, "inclusive bounds and exact integer tokens: " + value);
            }
        }

        private static void CheckFightScheduleFutureVersion()
        {
            var store = NGUInjector.Main.CustomPlanStore;
            var path = Path.Combine(_dir, "custom-plans", "fight-future.json");
            foreach (var version in new[] {"19", "2147483647", "2147483648", "18.000000000000000000001", "\"18\"", "null", "true"})
            {
                File.WriteAllText(path, "{\"Version\":" + version + ",\"Stages\":[{\"FightOnEntry\":true}]}");
                var bytes = File.ReadAllBytes(path);
                Assert(JsonFiles.ParseObject(File.ReadAllText(path))["Version"].ToString() == version,
                    "future-version fixture retains its JSON type and exact token: " + version);
                RejectFightSchedule(() => store.Load("fight-future"), "future/invalid version native read " + version);
                RejectFightSchedule(() => store.ReadExternalPlan("fight-future"), "future/invalid version external read " + version);
                RejectFightSchedule(() => store.CreateExternalPlan("fight-future-copy", JsonFiles.ParseObject(File.ReadAllText(path))), "future/invalid version copy " + version);
                RejectFightSchedule(() => store.SaveExternalPlan("fight-future", JsonFiles.ParseObject(File.ReadAllText(path)), JsonFiles.ComputeRevision(bytes)), "future/invalid version CAS " + version);
                Assert(bytes.SequenceEqual(File.ReadAllBytes(path)) && !store.PlanExists("fight-future-copy"), "version rejection never writes or downgrades");
            }
            var future = new CustomPlan {Version = 19, Name = "", GoalPresets = null, Stages = null};
            RejectFightSchedule(() => store.Save("fight-future", future), "future in-memory model rejected");
            Assert(future.Version == 19 && future.Name == "" && future.GoalPresets == null && future.Stages == null,
                "future version is rejected before model normalization mutates it");
            File.Delete(path);
        }
    }
}
