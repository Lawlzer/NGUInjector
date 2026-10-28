using System.IO;
using System.Linq;
using NGUInjector.CustomPlans;
using UnityEngine;

namespace NGUInjector
{
    internal static partial class ExternalUiChecks
    {
        private static void CheckAutoRebirthDefaults()
        {
            var directory = Path.Combine(_dir, "auto-rebirth-defaults");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "settings.json");
            var fresh = new SavedSettings(directory);
            Assert(fresh.AutoRebirth, "fresh global auto-rebirth defaults on");
            Assert(!fresh.UseCustomRebirthPlan && !fresh.GlobalEnabled, "fresh auto-rebirth permission does not start automation");
            fresh.SaveSettings();
            Assert(JsonFiles.ParseObject(File.ReadAllText(path))["_autoRebirth"].AsBool, "fresh global default persists as true");

            var previous = JsonUtility.ZeroMissingSerializedFields;
            try
            {
                JsonUtility.ZeroMissingSerializedFields = true;
                foreach (var value in new[] {"missing", "false", "true"})
                {
                    var root = JsonFiles.ParseObject("{\"future\":{\"_autoRebirth\":false}}");
                    root["_energyNguWeights"] = CustomPlanStore.SerializeNguWeights(fresh.EnergyNguWeights, 9, "energy");
                    root["_magicNguWeights"] = CustomPlanStore.SerializeNguWeights(fresh.MagicNguWeights, 7, "magic");
                    if (value != "missing") root["_autoRebirth"] = value == "true";
                    File.WriteAllText(path, root.ToString());
                    var before = File.ReadAllBytes(path);
                    var settings = new SavedSettings(directory);
                    Assert(settings.LoadSettings(), "global auto-rebirth fixture loads: " + value);
                    Assert(settings.AutoRebirth == (value != "false"), "global missing default and saved choice: " + value);
                    Assert(!settings.UseCustomRebirthPlan && !settings.GlobalEnabled, "loading permission never starts automation: " + value);
                    Assert(before.SequenceEqual(File.ReadAllBytes(path)), "loading rebirth default with already-persisted global NGU ratios never rewrites settings: " + value);
                    settings.ApplyPatch(JsonFiles.ParseObject("{\"_manageEnergy\":true}"));
                    var reloaded = new SavedSettings(directory);
                    Assert(reloaded.LoadSettings() && reloaded.AutoRebirth == settings.AutoRebirth,
                        "unrelated patch preserves auto-rebirth choice on disk: " + value);
                    settings.ApplyPatch(JsonFiles.ParseObject("{\"_autoRebirth\":false}"));
                    Assert(reloaded.LoadSettings() && !reloaded.AutoRebirth, "explicit global false patch survives reload: " + value);
                }
            }
            finally { JsonUtility.ZeroMissingSerializedFields = previous; }

            var liveSettings = JsonUtility.ToJson(NGUInjector.Main.Settings);
            var store = new CustomPlanStore(Path.Combine(directory, "custom-plans"));
            Assert(new CustomPlan().AutoRebirthEnabled, "fresh plan model defaults auto-rebirth on");
            Assert(!new CustomPlan().NumberClimbEnabled, "fresh plan disables retired NUMBER climb");
            foreach (var name in PlanCatalogNames)
                Assert(store.Load(name).AutoRebirthEnabled, "built-in auto-rebirth intent: " + name);
            SeedLegacyDefault(store);
            store.Save("infinite", CustomPlan.InfinitePlan());
            foreach (var name in new[] {store.CreatePlan("fresh-native"), store.CreateExternalPlan("fresh-external", null),
                store.CreateExternalPlan("fresh-json", JsonFiles.ParseObject("{}"))})
                Assert(store.Load(name).AutoRebirthEnabled && store.ReadExternalPlan(name)["AutoRebirthEnabled"].AsBool,
                    "new plan creation persists auto-rebirth on: " + name);

            foreach (var name in new[] {"default", "infinite"})
            foreach (var value in new[] {"missing", "false", "true"})
            {
                var root = store.ReadExternalPlan(name);
                root.Remove("AutoRebirthEnabled");
                if (value != "missing") root["AutoRebirthEnabled"] = value == "true";
                var expected = value == "true" || value == "missing" && name != "infinite";
                var copy = store.CreateExternalPlan("rebirth-" + name + "-" + value, root);
                var loaded = store.Load(copy);
                Assert(loaded.AutoRebirthEnabled == expected, "JSON creation respects missing default or explicit choice: " + name + "/" + value);
                var nativeCopy = store.CreatePlan(copy + "-native", loaded);
                Assert(store.Load(nativeCopy).AutoRebirthEnabled == expected, "native copy preserves auto-rebirth choice: " + name + "/" + value);
                var read = store.ReadExternalPlan(copy);
                store.SaveExternalPlan(copy, read, read[CustomPlanStore.ExternalUiRevisionField].Value);
                Assert(store.Load(copy).AutoRebirthEnabled == expected, "external save round-trips auto-rebirth choice: " + name + "/" + value);
            }
            store.Save("native-optout", new CustomPlan {AutoRebirthEnabled = false});
            Assert(!store.Load("native-optout").AutoRebirthEnabled, "native explicit false survives save/load");
            var saved = File.ReadAllBytes(Path.Combine(directory, "custom-plans", "native-optout.json"));
            var reopened = new CustomPlanStore(Path.Combine(directory, "custom-plans"));
            Assert(!reopened.Load("native-optout").AutoRebirthEnabled
                && saved.SequenceEqual(File.ReadAllBytes(Path.Combine(directory, "custom-plans", "native-optout.json"))),
                "store initialization does not migrate saved opt-outs");
            Assert(JsonUtility.ToJson(NGUInjector.Main.Settings) == liveSettings, "plan creation never changes execution or global switches");

            foreach (var version in new[] {5, 17})
            foreach (var enabled in new[] {false, true})
            {
                var legacy = PlanWithNguWeights("{}");
                legacy["Version"] = version;
                legacy["NumberClimbEnabled"] = enabled;
                legacy["NumberClimbMinSeconds"] = 123;
                legacy["NumberClimbMultiplierTarget"] = 7;
                legacy["PrepRequireMaxedFruit"] = true;
                var name = store.CreateExternalPlan("retired-" + version + "-" + enabled, legacy);
                var read = store.ReadExternalPlan(name);
                Assert(read["NumberClimbEnabled"].AsBool == enabled && read["NumberClimbMinSeconds"].AsInt == 123
                    && read["NumberClimbMultiplierTarget"].AsDouble == 7 && read["PrepRequireMaxedFruit"].AsBool,
                    "legacy retired fields and fruit preference survive migration: " + version + "/" + enabled);
                store.SaveExternalPlan(name, read, read[CustomPlanStore.ExternalUiRevisionField].Value);
                Assert(store.Load(name).NumberClimbEnabled == enabled && store.Load(name).PrepRequireMaxedFruit,
                    "legacy preferences survive save/readback: " + version + "/" + enabled);
            }

            CheckRetiredRebirthRuntime();
        }

        private static void CheckRetiredRebirthRuntime()
        {
            var character = NGUInjector.Main.Character;
            var auto = NGUInjector.Main.Settings.AutoRebirth;
            try
            {
                NGUInjector.Main.Character = new FakeCharacter {attackMulti = 10, nextAttackMulti = 1000, rebirthTime = new FakeTime {totalseconds = 3600}};
                NGUInjector.Main.Settings.AutoRebirth = true;
                var plan = new CustomPlan {TimeTriggerEnabled = false, NumberTriggerEnabled = false, NumberClimbEnabled = true,
                    NumberClimbMinSeconds = 0, NumberClimbMultiplierTarget = 1};
                var rebirth = new AllocationProfiles.RebirthStuff.CustomPlanRebirth(plan);
                foreach (var mode in new[] {0, 1})
                {
                    plan.TriggerMode = mode;
                    Assert(!rebirth.RebirthAvailable() && !rebirth.PeekRebirthAvailable() && !rebirth.PeekTriggersAvailable(),
                        "retired-only trigger cannot rebirth or report readiness: " + mode);
                    plan.TimeTriggerEnabled = true;
                    plan.TimeTriggerSeconds = 3600;
                    plan.NumberClimbMinSeconds = int.MaxValue;
                    plan.NumberClimbMultiplierTarget = double.MaxValue;
                    Assert(rebirth.RebirthAvailable() && rebirth.PeekTriggersAvailable(), "retired trigger neither blocks time nor adds NUMBER confirmation: " + mode);
                    Managers.CustomStageAllocator.Complete = false;
                    Assert(!rebirth.PeekRebirthAvailable() && rebirth.PeekTriggersAvailable(), "protected work guard still applies: " + mode);
                    Managers.CustomStageAllocator.Complete = true;
                    plan.TimeTriggerSeconds = 3601;
                    plan.NumberClimbMinSeconds = 0;
                    plan.NumberClimbMultiplierTarget = 1;
                    Assert(!rebirth.RebirthAvailable(), "retired trigger cannot bypass unmet time: " + mode);
                    plan.TimeTriggerEnabled = false;
                }
                plan.NumberTriggerEnabled = true;
                plan.NumberMultiplierTarget = 100;
                AllocationProfiles.RebirthStuff.CustomPlanRebirth.ResetRuntimeState();
                Assert(!rebirth.RebirthAvailable(), "active NUMBER trigger still requires confirmation");
                NGUInjector.Main.Character.rebirthTime.totalseconds += 15;
                Assert(rebirth.RebirthAvailable(), "active NUMBER trigger confirms normally");
                plan.NumberClimbMultiplierTarget = 12345;
                Assert(rebirth.PeekTriggersAvailable(), "retired field changes do not reset NUMBER confirmation");
                NGUInjector.Main.Character.nextAttackMulti = 9;
                Assert(!rebirth.PeekTriggersAvailable(), "never-lower-NUMBER guard remains");
            }
            finally
            {
                NGUInjector.Main.Character = character;
                NGUInjector.Main.Settings.AutoRebirth = auto;
                Managers.CustomStageAllocator.Complete = true;
                AllocationProfiles.RebirthStuff.CustomPlanRebirth.ResetRuntimeState();
            }
        }

        private static void CheckRunningRebirthPermission()
        {
            var settings = NGUInjector.Main.Settings;
            var name = settings.CustomPlanFile;
            var running = settings.UseCustomRebirthPlan;
            var auto = settings.AutoRebirth;
            var global = settings.GlobalEnabled;
            var store = NGUInjector.Main.CustomPlanStore;
            var edited = store.CreatePlan("permission-sync", new CustomPlan {AutoRebirthEnabled = false});
            try
            {
                foreach (var selected in new[] {false, true})
                foreach (var run in new[] {false, true})
                foreach (var permission in new[] {true, false})
                {
                    settings.CustomPlanFile = selected ? edited : "default";
                    settings.UseCustomRebirthPlan = run;
                    settings.AutoRebirth = !permission;
                    var before = settings.AutoRebirth;
                    var draft = store.ReadExternalPlan(edited);
                    draft["AutoRebirthEnabled"] = permission;
                    var id = "permission-" + selected + "-" + run + "-" + permission;
                    NGUInjector.Main.reference.Process(id, Envelope(id, "savePlan", PlanArgs(edited, draft)));
                    Assert(Result(id)["success"].AsBool, "permission save succeeds: " + id);
                    Assert(settings.AutoRebirth == (selected && run ? permission : before), "permission sync only affects the currently running plan: " + id);
                    Assert(settings.CustomPlanFile == (selected ? edited : "default") && settings.UseCustomRebirthPlan == run
                        && settings.GlobalEnabled == global, "permission autosave never selects or starts automation: " + id);
                    Assert(Result(id)["data"]["runtime"]["settings"]["autoRebirth"].AsBool == settings.AutoRebirth,
                        "save acknowledgement includes synchronized live permission: " + id);
                }
                settings.CustomPlanFile = edited;
                settings.UseCustomRebirthPlan = true;
                foreach (var permission in new[] {true, false})
                {
                    var existing = store.Load(edited);
                    existing.AutoRebirthEnabled = permission;
                    store.Save(edited, existing);
                    settings.AutoRebirth = !permission;
                    var unrelated = store.ReadExternalPlan(edited);
                    unrelated["TimeTriggerSeconds"] = unrelated["TimeTriggerSeconds"].AsInt + 1;
                    var id = "permission-unrelated-" + permission;
                    var settingsBytes = File.ReadAllBytes(Path.Combine(_dir, "settings.json"));
                    NGUInjector.Main.reference.Process(id, Envelope(id, "savePlan", PlanArgs(edited, unrelated)));
                    Assert(Result(id)["success"].AsBool && store.Load(edited).AutoRebirthEnabled == permission,
                        "unrelated save preserves stored rebirth permission: " + permission);
                    Assert(settings.AutoRebirth == !permission && settingsBytes.SequenceEqual(File.ReadAllBytes(Path.Combine(_dir, "settings.json"))),
                        "unrelated save preserves divergent live switch without a settings write: " + permission);
                    Assert(Result(id)["data"]["runtime"]["settings"]["autoRebirth"].AsBool == !permission,
                        "unrelated save acknowledges actual live switch: " + permission);
                    Assert(settings.UseCustomRebirthPlan && settings.CustomPlanFile == edited && settings.GlobalEnabled == global,
                        "unrelated save never selects or starts automation: " + permission);
                }
                settings.AutoRebirth = false;
                var rejected = store.ReadExternalPlan(edited);
                rejected["AutoRebirthEnabled"] = true;
                var args = PlanArgs(edited, rejected);
                args["expectedRevision"] = new string('0', 64);
                NGUInjector.Main.reference.Process("permission-conflict", Envelope("permission-conflict", "savePlan", args));
                Assert(!Result("permission-conflict")["success"].AsBool && !settings.AutoRebirth, "failed plan CAS never enables live rebirth");
                foreach (var permission in new[] {true, false})
                {
                    var existing = store.Load(edited);
                    existing.AutoRebirthEnabled = !permission;
                    store.Save(edited, existing);
                    settings.AutoRebirth = !permission;
                    var draft = store.ReadExternalPlan(edited);
                    draft["AutoRebirthEnabled"] = permission;
                    var id = "permission-partial-" + permission;
                    using (var locked = new FileStream(Path.Combine(_dir, "settings.json"), FileMode.Open, FileAccess.Read, FileShare.None))
                    {
                        NGUInjector.Main.reference.Process(id, Envelope(id, "savePlan", PlanArgs(edited, draft)));
                        Assert(!Result(id)["success"].AsBool && settings.AutoRebirth == !permission && store.Load(edited).AutoRebirthEnabled == permission,
                            "partial save reports failure without publishing live permission: " + permission);
                        Assert(Result(id)["message"].Value.Contains("Plan saved, but live auto-rebirth synchronization failed")
                            && Result(id)["message"].Value.Contains("Retrying the save will not replay the switch"),
                            "partial save explains the recovery limitation: " + permission);
                        Assert(Result(id)["data"]["runtime"]["settings"]["autoRebirth"].AsBool == !permission,
                            "partial save failure acknowledges actual live switch: " + permission);
                    }
                    if (!permission)
                        Assert(!new AllocationProfiles.RebirthStuff.CustomPlanRebirth(store.Load(edited)).PeekTriggersAvailable(),
                            "persisted disable blocks readiness even when live settings write failed");
                    var retryId = "permission-retry-" + permission;
                    NGUInjector.Main.reference.Process(retryId, Envelope(retryId, "savePlan", PlanArgs(edited, store.ReadExternalPlan(edited))));
                    Assert(Result(retryId)["success"].AsBool && settings.AutoRebirth == !permission && settings.UseCustomRebirthPlan,
                        "retry without a new permission transition preserves the live switch: " + permission);
                }
            }
            finally
            {
                settings.CustomPlanFile = name;
                settings.UseCustomRebirthPlan = running;
                settings.AutoRebirth = auto;
            }
        }
    }
}

// Boundaries only: trigger matching and NUMBER confirmation above use production code.
namespace NGUInjector.AllocationProfiles.RebirthStuff
{
    internal class RCTarget { }
    internal abstract class BaseRebirth
    {
        protected FakeCharacter CharObj => Main.Character;
        protected RCTarget[] ChallengeTargets;
        protected bool BaseRebirthChecks() => true;
        internal abstract bool RebirthAvailable();
    }
}

namespace NGUInjector.Managers
{
    internal static class CustomStageAllocator
    {
        internal static bool Complete = true;
        internal static bool ProtectedWorkComplete(CustomPlan plan) => Complete;
    }
}
