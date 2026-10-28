using System;
using System.IO;
using System.Linq;
using System.Text;
using NGUInjector.CustomPlans;
using SimpleJSON;

namespace NGUInjector
{
    internal static partial class ExternalUiChecks
    {
        private static readonly string[] PlanCatalogNames = {"24hr", "15m-NUMBER-increase", "30m-NUMBER-increase", "1h-1m-NUMBER-increase"};
        private static readonly string[] RetiredPlanNames = {"default", "infinite", "1h-3m-rebirth"};

        private static void SeedLegacyDefault(CustomPlanStore store)
        {
            // Historical bridge fixtures deliberately keep their old source plan, not a production default.
            if (!store.PlanExists("default")) store.Save("default", new CustomPlan {Name = "default"});
        }

        private static int RunPlanCatalogOnly()
        {
            _dir = Path.Combine(Path.GetTempPath(), "opencode", "ngu-plan-catalog-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            try
            {
                CheckPlanCatalog();
                Console.WriteLine("PASS: plan catalog, retirement, preservation and prep persistence: " + _assertions + " assertions.");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            finally { Directory.Delete(_dir, true); }
        }

        private static void CheckPlanCatalog()
        {
            var settings = NGUInjector.Main.Settings;
            var main = NGUInjector.Main.reference;
            try
            {
                NGUInjector.Main.reference = null;
                var runtime = Path.Combine(_dir, "number-catalog");
                Directory.CreateDirectory(runtime);
                NGUInjector.Main.Settings = new SavedSettings(runtime);
                NGUInjector.Main.Settings.SaveSettings();
                var settingsPath = Path.Combine(runtime, "settings.json");
                var settingsBytes = File.ReadAllBytes(settingsPath);
                var plans = Path.Combine(runtime, "custom-plans");
                var store = new CustomPlanStore(plans);
                Assert(store.ListPlanNames().OrderBy(name => name).SequenceEqual(PlanCatalogNames.OrderBy(name => name)), "fresh catalog contains exactly the four requested plans");
                Assert(store.EnsureDefaultPlan() == "24hr", "default selection is 24hr");
                foreach (var value in new[] {null, "", " ", ".", "..", " . "})
                    Assert(store.NormalizePlanName(value) == "24hr", "empty normalized names fall back to 24hr: " + value);
                CheckBuiltInPlanDeletion(store, runtime, plans);
                var longPlan = store.Load("24hr");
                Assert(longPlan.StageLoopStartIndex == 4 && longPlan.Stages.Select(stage => stage.DurationSeconds).SequenceEqual(new[] {3600, 1800, 1800, 10800, 0}), "fresh 24hr retains its original schedule and NGU sustain loop");
                Assert(longPlan.RebirthPrepStartSeconds == 0, "24hr retains readiness-based prep");
                AssertBloodUnset(longPlan, "24hr blood settings continue to inherit globals");
                var durations = new[] {900, 1800, 3660};
                var prepTimes = new[] {900, 1800, 3600};
                var stageDurations = new[] {new[] {210, 210, 420, 60}, new[] {435, 435, 870, 60}, new[] {885, 885, 1770, 60, 0}};
                for (var i = 0; i < durations.Length; i++)
                {
                    var name = PlanCatalogNames[i + 1];
                    var plan = store.Load(name);
                    Assert(plan.AutoRebirthEnabled && plan.TimeTriggerEnabled && plan.NumberTriggerEnabled && plan.TriggerMode == 1
                        && plan.TimeTriggerSeconds == durations[i] && plan.NumberMultiplierTarget == 1.000001, "NUMBER plan requires time AND an actual increase: " + name);
                    Assert(!plan.AutoIronPillEnabled && plan.AutoBloodSpellsEnabled == true && plan.BloodSpaghettiGoalPercent == 0
                        && plan.BloodCounterfeitGoalPercent == 0 && !plan.PrepRequireMaxedFruit && plan.AutoActivateFruits, "NUMBER plan explicitly enables heavy blood and nonmaxed fruit prep, not Iron Pill: " + name);
                    Assert(plan.StageSchedulerEnabled && plan.StageDurationSeconds == durations[i] && plan.StageLoopStartIndex == -1
                        && plan.RebirthPrepStartSeconds == prepTimes[i], "NUMBER plan duration and elapsed-age prep deadline: " + name);
                    Assert(plan.Stages.Select(stage => stage.DurationSeconds).SequenceEqual(stageDurations[i])
                        && plan.Stages.Sum(stage => stage.DurationSeconds) == prepTimes[i], "NUMBER mandatory fixed time ends at prep deadline: " + name);
                    var flexible = plan.Stages.Where(stage => stage.DurationSeconds == 0).ToArray();
                    Assert(i == 2 ? flexible.Length == 1 && flexible[0] == plan.Stages.Last() && flexible[0].Percent == 1
                        && plan.TimeTriggerSeconds - prepTimes[i] == 60 : flexible.Length == 0, "only 61m has a weight-one flexible tail funded by the remaining horizon: " + name);
                    for (var row = 0; row < plan.Stages.Length; row++)
                    {
                        var stage = plan.Stages[row];
                        var focus = row == 0 ? CustomStageFocus.TimeMachine : row == 2 ? CustomStageFocus.Wandoos : CustomStageFocus.Augments;
                        var energy = row == 0 ? new[] {"TM", "WAN", "AUG"} : row == 2 ? new[] {"WAN", "TM", "AUG"} : new[] {"AUG", "WAN", "TM"};
                        var magic = row == 2 ? new[] {"RIT", "WAN", "TM"} : new[] {"RIT", "TM", "WAN"};
                        var gear = row == 0 ? Managers.GearOptimizationGoal.TimeMachine : row == 2 ? Managers.GearOptimizationGoal.Wandoos : Managers.GearOptimizationGoal.Augments;
                        Assert(stage.EnergyFocus == (int) focus && stage.MagicFocus == (int) CustomStageFocus.BloodMagic && stage.GearGoal == (int) gear, "phase focus/gear and heavy blood: " + name + "/" + row);
                        Assert(stage.EnergyPriorities.SequenceEqual(energy) && stage.MagicPriorities.SequenceEqual(magic)
                            && !energy.Concat(magic).Contains("NGU"), "phase explicit NGU-free priority order: " + name + "/" + row);
                        Assert(stage.UseWandoosWhenTimeMachineLocked == (row < 3) && stage.ExecutionMode == 0 && stage.FightSchedule == null
                            && stage.DurationUnit == (stage.DurationSeconds > 0 ? "s" : "%") && stage.PresetId == "", "phase retains inline standard allocation and early-only locked-TM fallback: " + name + "/" + row);
                    }
                    var original = File.ReadAllBytes(Path.Combine(plans, name + ".json"));
                    var copy = store.CreatePlan(name + "-copy", plan);
                    var read = store.ReadExternalPlan(copy);
                    store.SaveExternalPlan(copy, read, read[CustomPlanStore.ExternalUiRevisionField].Value);
                    var reopened = new CustomPlanStore(plans).Load(copy);
                    Assert(reopened.Stages.All(stage => stage.PresetId == "" && stage.EnergyPriorities.All(token => token != "NGU") && stage.MagicPriorities[0] == "RIT")
                        && reopened.RebirthPrepStartSeconds == prepTimes[i], "native clone, external save and preset migration retain inline priorities and prep time: " + name);
                    Assert(original.SequenceEqual(File.ReadAllBytes(Path.Combine(plans, name + ".json"))), "copy and restart never rewrite NUMBER source: " + name);
                    CheckNumberCatalogTriggers(plan);
                    CheckNumberCatalogSchedule(plan);
                }
                var savedHold = store.ReadExternalPlan(PlanCatalogNames[3]);
                savedHold["Stages"][4]["DurationSeconds"] = 60;
                savedHold["Stages"][4]["DurationUnit"] = "s";
                savedHold.Remove(CustomPlanStore.ExternalUiRevisionField);
                var savedHoldPath = Path.Combine(plans, PlanCatalogNames[3] + ".json");
                File.WriteAllText(savedHoldPath, savedHold.ToString(2));
                var savedHoldBytes = File.ReadAllBytes(savedHoldPath);
                store = new CustomPlanStore(plans);
                store.ListPlanNames(); store.ReadExternalPlan(PlanCatalogNames[3]);
                Assert(store.Load(PlanCatalogNames[3]).Stages[4].DurationSeconds == 60 && savedHoldBytes.SequenceEqual(File.ReadAllBytes(savedHoldPath)),
                    "existing saved 61m fixed hold is never rewritten to the fresh flexible factory");
                Assert(settingsBytes.SequenceEqual(File.ReadAllBytes(settingsPath)), "catalog initialization does not change global blood or NGU settings");
                Assert(RetiredPlanNames.All(name => !store.PlanExists(name)), "catalog reads never recreate retired names");
                CheckRebirthPrepPersistence(store, plans);
                CheckPlanRetirement();
                CheckPlanRetirementFailures();
                CheckPlanRetirementReplacement();
            }
            finally
            {
                NGUInjector.Main.Settings = settings;
                NGUInjector.Main.reference = main;
            }
        }

        private static void CheckBuiltInPlanDeletion(CustomPlanStore store, string runtime, string plans)
        {
            var main = NGUInjector.Main.reference;
            try
            {
                NGUInjector.Main.reference = new NGUInjector.Main {StageProgressDirectory = runtime};
                Func<string, string> checkpointFor = name => Path.Combine(runtime, "stage-progress-"
                    + JsonFiles.ComputeRevision(Encoding.UTF8.GetBytes(name.ToLowerInvariant())) + ".json");
                foreach (var external in new[] {false, true})
                {
                    var control = store.CreatePlan("delete-control-" + external);
                    var checkpoint = checkpointFor(control);
                    File.WriteAllText(checkpoint, "ordinary plan progress");
                    if (external) store.DeleteExternalPlan(control); else store.DeletePlan(control);
                    Assert(!store.PlanExists(control) && !File.Exists(checkpoint), "deletion fixture actively removes ordinary plans and their progress: " + external);
                }
                foreach (var name in PlanCatalogNames)
                {
                    var path = Path.Combine(plans, name + ".json");
                    var original = File.ReadAllBytes(path);
                    var checkpoint = checkpointFor(name);
                    var sentinel = Encoding.UTF8.GetBytes("protected stage progress: " + name);
                    File.WriteAllBytes(checkpoint, sentinel);
                    var mixedCase = new string(name.Select((c, index) => index % 2 == 0 ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c)).ToArray());
                    foreach (var requested in new[] {name, mixedCase})
                    foreach (var external in new[] {false, true})
                    {
                        var label = (external ? "DeleteExternalPlan" : "DeletePlan") + "/" + requested;
                        try
                        {
                            if (external) store.DeleteExternalPlan(requested); else store.DeletePlan(requested);
                            throw new Exception("FAIL (accepted): built-in deletion " + label);
                        }
                        catch (InvalidOperationException error)
                        {
                            Assert(error.Message.StartsWith("Built-in plans cannot be deleted", StringComparison.Ordinal), "built-in guard rejects deletion: " + label);
                        }
                        // Inspect immediately: listing could hide a deletion by recreating the built-in.
                        Assert(File.Exists(path) && original.SequenceEqual(File.ReadAllBytes(path)), "rejected deletion preserves exact plan bytes: " + label);
                        Assert(File.Exists(checkpoint) && sentinel.SequenceEqual(File.ReadAllBytes(checkpoint)), "rejected deletion preserves stage-progress sentinel: " + label);
                    }
                }
            }
            finally { NGUInjector.Main.reference = main; }
        }

        private static void CheckRebirthPrepPersistence(CustomPlanStore store, string plans)
        {
            Assert(new CustomPlan().RebirthPrepStartSeconds == 0, "raw models default prep timing to zero");
            const string name = "prep-persistence";
            var path = Path.Combine(plans, name + ".json");
            File.WriteAllText(path, "{\"Version\":14,\"Name\":\"prep-persistence\",\"future\":9007199254740993}");
            var bytes = File.ReadAllBytes(path);
            Assert(store.Load(name).RebirthPrepStartSeconds == 0 && store.ReadExternalPlan(name)["RebirthPrepStartSeconds"].AsInt == 0
                && bytes.SequenceEqual(File.ReadAllBytes(path)), "missing legacy prep time defaults to zero without rewriting bytes");
            foreach (var token in new[] {"0", "900", "1800", "3600", "2147483647", "0.0", "-0.0", "9e2", "9000e-1", "1.000e0"})
            {
                var read = store.ReadExternalPlan(name);
                read["RebirthPrepStartSeconds"] = JsonFiles.ParseObject("{\"n\":" + token + "}")["n"];
                store.SaveExternalPlan(name, read, read[CustomPlanStore.ExternalUiRevisionField].Value);
                var expected = (int) double.Parse(token, System.Globalization.CultureInfo.InvariantCulture);
                Assert(store.Load(name).RebirthPrepStartSeconds == expected && store.ReadExternalPlan(name)["Version"].AsInt == 18, "nonnegative exact Int32 prep time round-trips in current schema: " + token);
            }
            foreach (var token in new[] {"null", "-1", "1.5", "1e-400", "-1e-400", "1.0000000000000001", "2147483647.0000000001", "2147483648", "1e100", "\"900\"", "false", "{}", "[]"})
            {
                var read = store.ReadExternalPlan(name);
                read["RebirthPrepStartSeconds"] = JsonFiles.ParseObject("{\"n\":" + token + "}")["n"];
                bytes = File.ReadAllBytes(path);
                Reject(() => store.SaveExternalPlan(name, read, read[CustomPlanStore.ExternalUiRevisionField].Value), "invalid prep time rejects external save: " + token);
                Reject(() => store.CreateExternalPlan("invalid-prep", read), "invalid prep time rejects external copy: " + token);
                Assert(bytes.SequenceEqual(File.ReadAllBytes(path)) && !store.PlanExists("invalid-prep"), "invalid prep time never mutates saved plans: " + token);
                var invalidPath = Path.Combine(plans, "invalid-prep-read.json");
                File.WriteAllText(invalidPath, read.ToString());
                Reject(() => store.Load("invalid-prep-read"), "invalid prep time rejects native load: " + token);
                Reject(() => store.ReadExternalPlan("invalid-prep-read"), "invalid prep time rejects external read: " + token);
                File.Delete(invalidPath);
            }
            var invalid = new CustomPlan {RebirthPrepStartSeconds = -1};
            Reject(() => store.Save(name, invalid), "negative typed prep time rejects native save");
            Reject(() => store.CreatePlan("invalid-native-prep", invalid), "negative typed prep time rejects native clone");
            Assert(!store.PlanExists("invalid-native-prep"), "invalid typed prep clone never creates a file");
        }

        private static void CheckNumberCatalogTriggers(CustomPlan plan)
        {
            var character = NGUInjector.Main.Character;
            try
            {
                AllocationProfiles.RebirthStuff.CustomPlanRebirth.ResetRuntimeState();
                NGUInjector.Main.Character = new FakeCharacter {attackMulti = 10, nextAttackMulti = 10.00002,
                    rebirthTime = new FakeTime {totalseconds = plan.TimeTriggerSeconds - 1}};
                var rebirth = new AllocationProfiles.RebirthStuff.CustomPlanRebirth(plan);
                Assert(!rebirth.RebirthAvailable(), "NUMBER increase cannot bypass elapsed time: " + plan.Name);
                NGUInjector.Main.Character.rebirthTime.totalseconds = plan.TimeTriggerSeconds;
                NGUInjector.Main.Character.nextAttackMulti = 10;
                Assert(!rebirth.RebirthAvailable(), "time cannot bypass unchanged NUMBER: " + plan.Name);
                NGUInjector.Main.Character.nextAttackMulti = 10.00002;
                Assert(!rebirth.RebirthAvailable(), "any qualifying increase still receives existing confirmation safety: " + plan.Name);
                NGUInjector.Main.Character.rebirthTime.totalseconds += 15;
                Assert(rebirth.RebirthAvailable(), "tiny confirmed NUMBER increase is sufficient after elapsed time: " + plan.Name);
                Managers.CustomStageAllocator.Complete = false;
                Assert(!rebirth.PeekRebirthAvailable() && rebirth.PeekTriggersAvailable(), "final readiness still waits for protected stages independently of prep deadline: " + plan.Name);
                NGUInjector.Main.Character.nextAttackMulti = 9;
                Assert(!rebirth.PeekTriggersAvailable(), "NUMBER safety rejects a loss even after prior confirmation: " + plan.Name);
            }
            finally
            {
                NGUInjector.Main.Character = character;
                Managers.CustomStageAllocator.Complete = true;
                AllocationProfiles.RebirthStuff.CustomPlanRebirth.ResetRuntimeState();
            }
        }

        private static void CheckNumberCatalogSchedule(CustomPlan plan)
        {
            var scheduler = new Managers.ProtectedStageScheduler();
            for (var elapsed = 0; elapsed <= plan.TimeTriggerSeconds; elapsed++)
            {
                scheduler.Update(plan, elapsed, elapsed, true, null);
                scheduler.MarkApplied(elapsed, elapsed);
                if (elapsed == plan.RebirthPrepStartSeconds - 1)
                    Assert(!scheduler.ProtectedWorkComplete && scheduler.StageIndex == 3, "final Augments must finish before prep/fight is eligible: " + plan.Name);
                if (elapsed == plan.RebirthPrepStartSeconds)
                {
                    Assert(scheduler.ProtectedWorkComplete, "protected work completes at the prep deadline: " + plan.Name);
                    if (plan.TimeTriggerSeconds > plan.RebirthPrepStartSeconds)
                        Assert(!scheduler.Finished && scheduler.StageIndex == 4 && scheduler.Durations[4] == 60
                            && scheduler.ExpectedEndSeconds == 3660, "61m flexible Augments/Blood hold does not block prep at 3600");
                    else Assert(scheduler.Finished, "15m/30m remain entirely fixed and finish at prep time: " + plan.Name);
                }
                if (plan.TimeTriggerSeconds > plan.RebirthPrepStartSeconds && elapsed == plan.TimeTriggerSeconds - 1)
                    Assert(scheduler.ProtectedWorkComplete && !scheduler.Finished && scheduler.StageIndex == 4,
                        "61m protected work remains complete while the flexible hold runs through 3659");
            }
            Assert(scheduler.Finished && scheduler.ProtectedWorkComplete, "NUMBER schedule reaches its final horizon: " + plan.Name);
        }

        private static void CheckPlanRetirement()
        {
            foreach (var selection in RetiredPlanNames.Concat(new[] {"24hr"}))
            {
                var runtime = Path.Combine(_dir, "retire-" + selection);
                var plans = Path.Combine(runtime, "custom-plans");
                Directory.CreateDirectory(plans);
                var settingsPath = Path.Combine(runtime, "settings.json");
                NGUInjector.Main.Settings = new SavedSettings(runtime);
                NGUInjector.Main.Settings.SetSaveDisabled(true);
                NGUInjector.Main.Settings.CustomPlanFile = selection;
                NGUInjector.Main.Settings.UseCustomRebirthPlan = true;
                NGUInjector.Main.Settings.CustomAutoBoostCubePowerRatio = 2;
                NGUInjector.Main.Settings.CustomAugmentsBloodEstimateSeconds = 300;
                NGUInjector.Main.Settings.SetSaveDisabled(false);
                NGUInjector.Main.Settings.SaveSettings();
                var root = JsonFiles.ParseObject(File.ReadAllText(settingsPath)); root["future"] = JsonFiles.ParseObject("{\"precise\":9007199254740993}");
                File.WriteAllText(settingsPath, root.ToString(2));
                Assert(NGUInjector.Main.Settings.LoadSettings(), "retirement settings fixture loads");
                var settingsBytes = File.ReadAllBytes(settingsPath);
                foreach (var name in RetiredPlanNames)
                    File.WriteAllText(Path.Combine(plans, name + ".json"), "{\"Name\":\"" + name + "\",\"RebirthHistory\":[{\"Reason\":\"saved legacy history\"}],\"future\":9007199254740993}", new UTF8Encoding(true));
                var retiredBytes = RetiredPlanNames.Select(name => File.ReadAllBytes(Path.Combine(plans, name + ".json"))).ToArray();
                var longPath = Path.Combine(plans, "24hr.json");
                // Missing arrays used to trigger a built-in repair write. Keep that shipped shape untouched too.
                var longText = selection == "24hr" ? "{\"Version\":14,\"Name\":\"24hr\",\"GoalPresets\":[],\"Stages\":[],\"future\":9007199254740993}"
                    : "{\"Version\":17,\"Name\":\"24hr\",\"AutoRebirthEnabled\":false,\"AutoIronPillEnabled\":false,\"TimeTriggerSeconds\":98765,\"StageSchedulerEnabled\":false,\"StageLoopStartIndex\":0,\"GoalPresets\":[{\"Name\":\"Mine\",\"GearGoal\":12}],\"Stages\":[{\"Name\":\"Saved\",\"DurationSeconds\":17,\"MagicFocus\":4,\"future\":9007199254740993}],\"RebirthHistory\":[{\"Reason\":\"keep\",\"BossId\":42}],\"future\":9223372036854775807}";
                File.WriteAllText(longPath, longText, new UTF8Encoding(true));
                var longBytes = File.ReadAllBytes(longPath);
                var checkpoint = Path.Combine(runtime, "stage-progress-" + JsonFiles.ComputeRevision(Encoding.UTF8.GetBytes("24hr")) + ".json");
                File.WriteAllText(checkpoint, "24hr scheduler sentinel");
                File.WriteAllText(Path.Combine(plans, "future-user-plan.json"), "{\"Name\":\"future-user-plan\",\"future\":42}");
                var store = new CustomPlanStore(plans);
                var backupDir = Path.Combine(runtime, "retired-custom-plans-number-v1");
                Assert(File.Exists(Path.Combine(backupDir, "completed.json")) && !backupDir.StartsWith(plans, StringComparison.OrdinalIgnoreCase), "retirement writes durable completion outside the watched plan directory");
                for (var i = 0; i < RetiredPlanNames.Length; i++)
                {
                    var backup = Path.Combine(backupDir, RetiredPlanNames[i] + ".json");
                    Assert(!store.PlanExists(RetiredPlanNames[i]) && retiredBytes[i].SequenceEqual(File.ReadAllBytes(backup))
                        && retiredBytes[i].SequenceEqual(File.ReadAllBytes(backup + ".retired")), "obsolete plan is moved only after byte-exact backup and the moved object is preserved: " + RetiredPlanNames[i]);
                }
                Assert(NGUInjector.Main.Settings.CustomPlanFile == "24hr" && NGUInjector.Main.Settings.UseCustomRebirthPlan
                    && JsonFiles.ParseObject(File.ReadAllText(settingsPath))["_customPlanFile"].Value == "24hr", "legacy selection safely falls back while preserving run permission");
                var after = JsonFiles.ParseObject(File.ReadAllText(settingsPath));
                root["_customPlanFile"] = "24hr";
                var changedFields = root.Linq.Where(item => item.Value.ToString() != after[item.Key].ToString()).Select(item => item.Key).ToArray();
                Assert(root.Count == after.Count && changedFields.Length == 0, "selection migration preserves every other settings value and unknown token: " + string.Join(",", changedFields));
                if (selection == "24hr") Assert(settingsBytes.SequenceEqual(File.ReadAllBytes(settingsPath)), "already-selected 24hr settings remain byte-identical");
                else Assert(settingsBytes.SequenceEqual(File.ReadAllBytes(Path.Combine(backupDir, "settings.json"))), "legacy selected settings are backed up before fallback");
                store.Load("24hr"); store.ReadExternalPlan("24hr"); store.EnsureDefaultPlan(); store.ListPlanNames();
                Assert(longBytes.SequenceEqual(File.ReadAllBytes(longPath)) && File.ReadAllText(checkpoint) == "24hr scheduler sentinel", "initialization and normalized reads preserve 24hr bytes/config/history and scheduler");
                if (selection != "24hr")
                {
                    var saved = store.Load("24hr");
                    Assert(!saved.AutoRebirthEnabled && !saved.AutoIronPillEnabled && !saved.StageSchedulerEnabled && saved.TimeTriggerSeconds == 98765
                        && saved.StageLoopStartIndex == 0 && saved.Stages[0].DurationSeconds == 17 && saved.RebirthHistory[0].Reason == "keep", "saved 24hr configuration and history are never regenerated");
                }
                foreach (var name in RetiredPlanNames) store.Save(name, new CustomPlan {Name = name});
                store = new CustomPlanStore(plans); store.ListPlanNames(); store.EnsureDefaultPlan();
                Assert(RetiredPlanNames.All(store.PlanExists) && store.PlanExists("future-user-plan"), "one-time retirement never deletes future user plans, including reused legacy names");
                foreach (var name in RetiredPlanNames) store.DeleteExternalPlan(name);
                Assert(RetiredPlanNames.All(name => !store.PlanExists(name)), "reused legacy names are ordinary removable user plans and never recreated");
            }
        }

        private static void CheckPlanRetirementFailures()
        {
            foreach (var failure in new[] {"backup", "settings", "changed-backup"})
            {
                var runtime = Path.Combine(_dir, "retire-failure-" + failure);
                var plans = Path.Combine(runtime, "custom-plans"); Directory.CreateDirectory(plans);
                NGUInjector.Main.Settings = new SavedSettings(runtime);
                NGUInjector.Main.Settings.CustomPlanFile = "default";
                var settingsPath = Path.Combine(runtime, "settings.json");
                foreach (var name in RetiredPlanNames) File.WriteAllText(Path.Combine(plans, name + ".json"), "{\"Name\":\"" + name + "\"}");
                var backupDir = Path.Combine(runtime, "retired-custom-plans-number-v1");
                if (failure == "changed-backup")
                {
                    Directory.CreateDirectory(backupDir);
                    File.WriteAllText(Path.Combine(backupDir, "default.json"), "older different backup");
                    Reject(() => new CustomPlanStore(plans), "mismatched existing backup fails closed");
                }
                else
                {
                    var lockedPath = failure == "backup" ? Path.Combine(plans, "default.json") : settingsPath;
                    using (var held = new FileStream(lockedPath, FileMode.Open, FileAccess.Read, failure == "backup" ? FileShare.None : FileShare.Read))
                        Reject(() => new CustomPlanStore(plans), "retirement rejects a failed " + failure + " write");
                }
                Assert(RetiredPlanNames.All(name => File.Exists(Path.Combine(plans, name + ".json"))) && NGUInjector.Main.Settings.CustomPlanFile == "default"
                    && !File.Exists(Path.Combine(backupDir, "completed.json")), "retirement failure preserves all old files, selection, and retryability: " + failure);
                if (failure == "changed-backup") continue;
                var store = new CustomPlanStore(plans);
                Assert(RetiredPlanNames.All(name => !store.PlanExists(name)) && NGUInjector.Main.Settings.CustomPlanFile == "24hr", "retirement retries successfully after transient failure: " + failure);
            }
        }

        private static void CheckPlanRetirementReplacement()
        {
            var runtime = Path.Combine(_dir, "retire-atomic-replacement");
            var plans = Path.Combine(runtime, "custom-plans"); Directory.CreateDirectory(plans);
            var backupDir = Path.Combine(runtime, "retired-custom-plans-number-v1"); Directory.CreateDirectory(backupDir);
            NGUInjector.Main.Settings = new SavedSettings(runtime);
            NGUInjector.Main.Settings.CustomPlanFile = "24hr";
            var path = Path.Combine(plans, "default.json");
            var backup = Path.Combine(backupDir, "default.json");
            var retired = backup + ".retired";
            File.WriteAllText(path, "{\"Name\":\"default\",\"future\":\"snapshot\"}");
            var original = File.ReadAllBytes(path);
            File.Copy(path, backup);
            // Deterministically recreate a replacement after the snapshot/open, then an interrupted move.
            using (var opened = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                JsonFiles.WriteAtomic(path, "{\"Name\":\"default\",\"future\":\"replacement before move\"}");
                var replacement = File.ReadAllBytes(path);
                File.Move(path, retired);
                JsonFiles.WriteAtomic(path, "{\"Name\":\"default\",\"future\":\"replacement after move\"}");
                var current = File.ReadAllBytes(path);
                var longPath = Path.Combine(plans, "24hr.json");
                File.WriteAllText(longPath, "{\"Name\":\"24hr\",\"future\":\"saved 24hr\"}");
                var longBytes = File.ReadAllBytes(longPath);
                var store = new CustomPlanStore(plans);
                store.ListPlanNames(); store.EnsureDefaultPlan();
                Assert(original.SequenceEqual(File.ReadAllBytes(backup)) && replacement.SequenceEqual(File.ReadAllBytes(retired))
                    && current.SequenceEqual(File.ReadAllBytes(path)), "interrupted retirement preserves snapshot, moved replacement and newer catalog replacement without overwrite");
                Assert(File.Exists(Path.Combine(backupDir, "completed.json")) && longBytes.SequenceEqual(File.ReadAllBytes(longPath)),
                    "a moved-file receipt completes retirement idempotently without changing 24hr");
                store = new CustomPlanStore(plans);
                Assert(current.SequenceEqual(File.ReadAllBytes(path)) && replacement.SequenceEqual(File.ReadAllBytes(retired)),
                    "restart never retires or overwrites a replacement after an earlier move");
            }
        }
    }
}
