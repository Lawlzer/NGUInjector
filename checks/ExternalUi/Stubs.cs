using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;

// Boundary substitutes only. Persistence, command dispatch, lifecycle cleanup and file IO
// come from the linked production sources, not copies of their implementations.
namespace System.Windows.Forms
{
    // SavedSettings imports this namespace but does not use any WinForms APIs.
    internal static class NamespaceStub { }
}

namespace UnityEngine
{
    public class Object
    {
        public static void DontDestroyOnLoad(Object value) { }
        public static void Destroy(Object value) { if (value is GameObject game) game.Destroyed = true; }
    }
    public class GameObject : Object
    {
        public static GameObject Last;
        public static int Created;
        public object Component;
        public bool Destroyed, Active = true;
        public GameObject() { Last = this; Created++; }
        public T AddComponent<T>() { Component = Activator.CreateInstance(typeof(T), true); return (T)Component; }
        public T GetComponent<T>() where T : class { return Component as T; }
        public void SetActive(bool active) { Active = active; }
    }
    public static class Debug { public static void LogError(string message) { } }
    [AttributeUsage(AttributeTargets.Field)] public class SerializeField : Attribute { }
    public static class JsonUtility
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { IncludeFields = true };
        internal static bool OmitCustomIronPillHistory;
        internal static bool OmitCustomAdventureRules;
        internal static bool ZeroMissingSerializedFields;

        public static string ToJson(object value, bool pretty = false)
        {
            var fields = value.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(f => Attribute.IsDefined(f, typeof(SerializeField))
                    && (!OmitCustomIronPillHistory || f.Name != "_customIronPillHistory")
                    && (!OmitCustomAdventureRules || f.Name != "_customAdventureRules"));
            return JsonSerializer.Serialize(fields.ToDictionary(f => f.Name, f => f.GetValue(value)), Options);
        }

        public static T FromJson<T>(string text)
        {
            using (var document = JsonDocument.Parse(text))
            {
                // Model Unity builds that cannot deserialize injected custom record arrays at all.
                if (OmitCustomAdventureRules && document.RootElement.TryGetProperty("_customAdventureRules", out _))
                    throw new InvalidOperationException("Unity cannot deserialize adventure rules in this fixture.");
                var instance = Activator.CreateInstance(typeof(T), new object[] { null });
                foreach (var field in typeof(T).GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (!Attribute.IsDefined(field, typeof(SerializeField))
                        || OmitCustomIronPillHistory && field.Name == "_customIronPillHistory")
                        continue;
                    if (!document.RootElement.TryGetProperty(field.Name, out var value))
                    {
                        // Unity FromJson does not apply field initializers to missing fields.
                        if (ZeroMissingSerializedFields)
                            field.SetValue(instance, field.FieldType.IsValueType ? Activator.CreateInstance(field.FieldType) : null);
                        continue;
                    }
                    field.SetValue(instance, value.Deserialize(field.FieldType, Options));
                }
                return (T)instance;
            }
        }
    }
}

namespace NGUInjector.Managers
{
    internal enum GearOptimizationGoal { EnergyCap, MagicCap, EnergyNgu, MagicNgu, AllNgu, TimeMachine, Blood, Gold, Drop, Fight, Augments, AdvancedTraining, Wandoos, ManualAdventure }
    internal enum FightScoreMode { SqrtPowerToughness, PowerTimesToughness, PowerPlusToughness }
    internal class GearOptimizerResult { internal bool Success = true; internal string Message = "gear"; internal int[] ItemIds = { 40, 41 }; internal double Score = 1.5; internal long EvaluatedCombinations = 2; internal long VisitedNodes = 3; internal long PrunedBranches = 4; internal long ElapsedMilliseconds = 5; }
    internal static class GearOptimizer
    {
        internal static Dictionary<int, string> GoalLabels = Enum.GetValues(typeof(GearOptimizationGoal)).Cast<GearOptimizationGoal>().ToDictionary(x => (int)x, x => x.ToString());
        internal static Dictionary<int, string> FightScoreModeLabels = Enum.GetValues(typeof(FightScoreMode)).Cast<FightScoreMode>().ToDictionary(x => (int)x, x => x.ToString());
        internal static GearOptimizerResult Optimize(GearOptimizationGoal goal, FightScoreMode mode) { NGUInjector.Main.Previews++; return new GearOptimizerResult(); }
        internal static GearOptimizerResult OptimizeAndEquip(GearOptimizationGoal goal, FightScoreMode mode) { NGUInjector.Main.Equips++; return new GearOptimizerResult(); }
    }
    internal static class ZoneHelpers { internal static int[] TitanZones = { 1, 2 }; }
    internal static class YggdrasilManager { internal static void CleanupOwnedHarvest() { } }
    // Real Money Pit recovery and lifecycle integration execute in the compiled Ownership fixture.
    internal static class MoneyPitManager { internal static string RestorePitGear() => null; }
    internal static class ZoneStatHelper { internal static void CreateOverrides(string path) { NGUInjector.Main.ZoneReloads++; } }
    internal class BloodMagicOptimizerResult { internal bool Success; internal double BloodPerSecond; internal int RitualIndex; internal long MagicAllocated; internal string Message; }
    internal static class BloodMagicOptimizer { internal static BloodMagicOptimizerResult OptimizeBest() { return new BloodMagicOptimizerResult { Message = "Blood Magic skipped: no magic available." }; } }
    internal class AugmentOptimizerResult
    {
        internal bool Success = true, UsesUpgrade = true;
        internal string Message = "Best augment: pair 2";
        internal int AugmentIndex = 2;
        internal float EnergyToAugment = 100, EnergyToUpgrade = 200;
        internal double Seconds = 12, ExpectedSeconds, ValuePerSecond = 4, ExpectedValue = 5,
            ProjectedGoldCost = 6, ProjectedGoldBudget = 7, GoldPerSecond = 8, EnergyPower = 9,
            AugmentSpeedMultiplier = 10, ProjectedWork = 11, ProgressFraction = .5;
    }
    internal static class AugmentOptimizer
    {
        internal static bool Fail;
        internal static int Previews, Allocations, LastExpectedSeconds;
        internal static AugmentOptimizerResult OptimizeBest(int expectedSeconds)
        {
            Previews++; LastExpectedSeconds = expectedSeconds;
            return new AugmentOptimizerResult { Success = !Fail, ExpectedSeconds = expectedSeconds };
        }
        internal static AugmentOptimizerResult OptimizeAndAllocate(int expectedSeconds)
        {
            Allocations++; LastExpectedSeconds = expectedSeconds;
            return new AugmentOptimizerResult { Success = !Fail, ExpectedSeconds = expectedSeconds };
        }
    }
}

namespace NGUInjector.AllocationProfiles
{
    internal class CustomAllocation
    {
        internal CustomAllocation(string path, string name) { }
        internal void ReloadAllocation() { NGUInjector.Main.Allocations++; }
    }
}

namespace NGUInjector
{
    internal enum CustomFocusGoal { AugmentsBloodMagic, TimeMachine, Wandoos, EnergyNgu, MagicNgu, Ngu, AdvancedTraining, Adventure, Fight, ManualTitans }
    internal class FakeCharacter
    {
        internal FakeTime rebirthTime;
        internal int bossID;
        internal double attackMulti, nextAttackMulti;
        internal FakeAdventure adventure;
        internal FakeBlood bloodMagic;
        internal FakeBloodController bloodMagicController;
        internal FakeBloodSpells bloodSpells;
        internal FakeInventory inventory;
        internal FakeItemInfo itemInfo;
    }
    // Equipment formulas are verified against the compiled injector in CustomAutoBoostChecks.
    internal class FakeEquipment
    {
        internal int id, level;
        internal bool Equipment = true, FullyBoosted;
        internal bool isEquipment() => Equipment;
        internal bool IsFullyBoosted() => FullyBoosted;
    }
    internal class FakeInventory
    {
        internal FakeEquipment head, chest, legs, boots, weapon, weapon2;
        internal List<FakeEquipment> accs = new List<FakeEquipment>(), inventory = new List<FakeEquipment>();
    }
    internal class FakeItemInfo { internal string[] itemName; }
    internal class FakeBlood
    {
        internal bool lootAutoSpell, goldAutoSpell, rebirthAutoSpell;
        internal double lootSpellBlood, goldSpellBlood, bloodPoints;
        internal List<FakeRitual> ritual = new List<FakeRitual>();
    }
    internal class FakeRitual { internal long magic; }
    internal class FakeBloodController
    {
        internal bool Fail;
        internal double totalBloodGainedPerSecond() { return 0; }
        internal double lootBonus() { if (Fail) throw new InvalidOperationException("simulated blood formula failure"); return 1; }
        internal double goldBonus() { return 1; }
    }
    internal class FakeBloodSpells
    {
        internal double MinimumAdventureBlood = 1;
        internal double minLootBlood() { return 1; }
        internal double minGoldBlood() { return 1; }
        internal double minAdventureBlood() => MinimumAdventureBlood;
    }
    internal class FakeTime { internal double totalseconds; }
    internal class FakeAdventure { internal int zone; internal bool autoattacking; }
    internal class FakeForm : IDisposable
    {
        internal Action OnDispose;
        internal int SettingsUpdates;
        internal void UpdateFromSettings(SavedSettings settings) { SettingsUpdates++; }
        internal void UpdateProfileList(string[] names, string selected) { }
        public void Dispose() { OnDispose?.Invoke(); }
    }
    internal partial class Main
    {
        internal const string Version = "test";
        internal static Main reference;
        internal static SavedSettings Settings;
        internal static CustomPlanStore CustomPlanStore;
        internal static FakeForm settingsForm;
        internal static FakeCharacter Character;
        private static FakeCharacter _presetResourcePresentationCharacter;
        private static bool _presetResourcePresentationPending;
        private static object _presetResourceOutcome;
        private static string _dir, _profilesDir, _customPlansDir;
        private object _protectedScheduler;
        private void SaveProtectedStageProgress(bool force = false) { }
        internal void ForgetStageProgress(string planName)
        {
            File.Delete(Path.Combine(_dir, "stage-progress-"
                + JsonFiles.ComputeRevision(System.Text.Encoding.UTF8.GetBytes(planName.ToLowerInvariant())) + ".json"));
        }
        private static AllocationProfiles.CustomAllocation _profile;
        private bool enabled { get; set; } = true;
        internal static FileSystemWatcher ConfigWatcher, AllocationWatcher, ZoneWatcher;
        internal static StreamWriter OutputWriter, LootWriter, CombatWriter, AllocationWriter, PitSpinWriter;
        internal static object Controller, PlayerController, WishManager;
        internal int CancelCalls, CoroutineStops;
        internal int PushStartCalls, PushCancelCalls, LastPushDeaths;
        internal bool PushStartFails, PushCancelFails;
        internal SimpleJSON.JSONObject PushRuntime = NewPushRuntime(false, 0, 0);
        private static SimpleJSON.JSONObject NewPushRuntime(bool active, int deaths, int floor)
        {
            var state = new SimpleJSON.JSONObject();
            state["active"] = active;
            state["remainingDeaths"] = deaths;
            state["floor"] = floor;
            return state;
        }
        // The Combat harness runs the real push policy. Here only the action boundary is replaced.
        private string StartMaxItopodPush(int deaths)
        {
            PushStartCalls++;
            LastPushDeaths = deaths;
            if (PushStartFails) throw new InvalidOperationException("fixture push start failure");
            PushRuntime = NewPushRuntime(true, deaths, 87);
            return "fixture push started";
        }
        private string CancelMaxItopodPush()
        {
            PushCancelCalls++;
            if (PushCancelFails) throw new InvalidOperationException("fixture push cancel failure");
            PushRuntime = NewPushRuntime(false, 0, 0);
            return "fixture push cancelled";
        }
        private SimpleJSON.JSONObject GetMaxItopodPushRuntime() => PushRuntime;
        private SimpleJSON.JSONObject GetAdventureRulesRuntime() => new SimpleJSON.JSONObject();
        private SimpleJSON.JSONObject GetManualTitanRuntime() => new SimpleJSON.JSONObject();
        private SimpleJSON.JSONObject GetManualAdventureOverlayRuntime() => new SimpleJSON.JSONObject();
        internal bool _manualTitanActive, ManualTitanCleanupFails;
        private void CancelManualTitan(string reason) { if (!ManualTitanCleanupFails) _manualTitanActive = false; }
        private void RequireCombatPrepIdle() { }
        private bool CombatPrepActive => false;
        // Native combat retirement is covered by CombatPrepIntegration. This bridge
        // boundary verifies lifecycle refusal/disposal ordering without a game model.
        internal sealed class CombatPrepBoundaryRun { internal bool CleanupPending, AdventureRouteOwned; internal int Zone; }
        internal CombatPrepBoundaryRun _combatPrepRun;
        internal bool CombatPrepRetirementFails;
        private bool CombatPrepRunActive => _combatPrepRun != null;
        private static bool CombatPrepAdventureRouteMatches(CombatPrepBoundaryRun run) => run.AdventureRouteOwned
            && Character?.adventure?.zone == run.Zone;
        private string PreviewCombatPrep(SimpleJSON.JSONNode args, out SimpleJSON.JSONNode data) { data = new SimpleJSON.JSONObject(); return "combat prep preview boundary"; }
        private string StartCombatPrep(SimpleJSON.JSONNode args) { return "combat prep start boundary"; }
        private string CancelCombatPrep(SimpleJSON.JSONNode args) { return "combat prep cancel boundary"; }
        private void EndCombatPrep(string status, string reason, bool releaseOwned = true)
        {
            if (_combatPrepRun == null) return;
            if (CombatPrepRetirementFails) _combatPrepRun.CleanupPending = true;
            else _combatPrepRun = null;
        }
        private SimpleJSON.JSONObject GetCombatPrepRuntime() { return new SimpleJSON.JSONObject(); }
        // Resource coordination is exercised by compiled checks, not this bridge boundary.
        private SimpleJSON.JSONObject GetResourceCoordinationRuntime() { return new SimpleJSON.JSONObject(); }
        internal const string LoadoutPresetPreviewToken = "0123456789abcdef0123456789abcdef";
        internal int LoadoutPresetPreviews, LoadoutPresetApplies;
        internal SimpleJSON.JSONNode LoadoutPresetPreviewArgs, LoadoutPresetApplyArgs;
        internal SimpleJSON.JSONObject LoadoutPresetRuntime;
        private void InvalidateGlobalNguWeightPolicies() { }
        private static SimpleJSON.JSONNode LoadoutPresetNumber(double? value)
        {
            return value.HasValue && double.IsFinite(value.Value)
                ? (SimpleJSON.JSONNode)new SimpleJSON.JSONNumber(value.Value) : SimpleJSON.JSONNull.CreateOrGet();
        }
        private static SimpleJSON.JSONArray LoadoutPresetNumbers(IEnumerable<double> values)
        {
            var result = new SimpleJSON.JSONArray();
            foreach (var value in values) result.Add(LoadoutPresetNumber(value));
            return result;
        }

        // Only the game adapter is substituted. These checks cannot establish real engine preview safety.
        private static CustomLoadoutPreset ValidateLoadoutPresetBoundaryArgs(SimpleJSON.JSONNode args, bool apply)
        {
            var allowed = apply
                ? new[] { "preset", "previewId", "candidateKey" }
                : new[] { "preset" };
            if (args == null || !args.IsObject || args.Linq.Any(item => !allowed.Contains(item.Key)))
                throw new FormatException("Unexpected loadout preset arguments.");
            var preset = CustomLoadoutPreset.Parse(args["preset"]);
            if (apply && (!args["previewId"].IsString
                || !System.Text.RegularExpressions.Regex.IsMatch(args["previewId"].Value, @"\A[0-9a-f]{32}\z")
                || !args["candidateKey"].IsString || args["candidateKey"].Value.Length > 64))
                throw new FormatException("Apply requires previewId and candidateKey.");
            return preset;
        }

        private SimpleJSON.JSONObject PreviewLoadoutPreset(SimpleJSON.JSONNode args)
        {
            var preset = ValidateLoadoutPresetBoundaryArgs(args, false);
            LoadoutPresetPreviews++;
            LoadoutPresetPreviewArgs = JsonFiles.ParseObject(args.ToString());
            var result = new SimpleJSON.JSONObject();
            result["version"] = 1;
            result["presetId"] = preset.Id;
            result["previewId"] = LoadoutPresetPreviewToken;
            return result;
        }

        private string ApplyLoadoutPreset(SimpleJSON.JSONNode args, out SimpleJSON.JSONNode data)
        {
            var preset = ValidateLoadoutPresetBoundaryArgs(args, true);
            LoadoutPresetApplies++;
            LoadoutPresetApplyArgs = JsonFiles.ParseObject(args.ToString());
            data = new SimpleJSON.JSONObject();
            data["result"] = new SimpleJSON.JSONObject();
            data["result"]["presetId"] = preset.Id;
            data["result"]["previewId"] = args["previewId"].Value;
            data["result"]["candidateKey"] = args["candidateKey"].Value;
            return "loadout preset apply boundary";
        }

        private SimpleJSON.JSONObject GetLoadoutPresetRuntime() => LoadoutPresetRuntime;
        private string _globalPresetManualId;
        private static void GlobalPresetArgs(SimpleJSON.JSONNode args, params string[] fields)
        {
            if (args == null || !args.IsObject || args.Count != fields.Length || args.Linq.Any(x => !fields.Contains(x.Key)))
                throw new FormatException("Unexpected global preset arguments.");
            foreach (var field in fields.Where(x => x != "library"))
                if (!args[field].IsString) throw new FormatException(field + " must be a string.");
        }
        // Full preset execution is exercised by PresetIntegration, not this command/file bridge fixture.
        private SimpleJSON.JSONObject PreviewGlobalPreset(SimpleJSON.JSONNode args) { throw new NotSupportedException("Global preset game boundary is not enabled in this fixture."); }
        private string ApplyGlobalPreset(SimpleJSON.JSONNode args, out SimpleJSON.JSONNode data) { data = null; throw new NotSupportedException("Global preset game boundary is not enabled in this fixture."); }
        private string EquipGlobalPreset(SimpleJSON.JSONNode args, out SimpleJSON.JSONNode data) { data = null; throw new NotSupportedException("Global preset game boundary is not enabled in this fixture."); }
        private void CancelInvoke() { CancelCalls++; }
        private void StopAllCoroutines() { CoroutineStops++; }
        internal static int Saves, Invalidations, Allocations, Focuses, Equips, Previews, ZoneReloads;
        internal static int StageOverrides;
        internal static SimpleJSON.JSONNode StageOverrideArgs;
        internal string OverrideCustomStage(SimpleJSON.JSONNode args)
        {
            StageOverrides++;
            StageOverrideArgs = args;
            return "stage override boundary";
        }
        internal static bool FocusFails;
        internal bool ManualRebirthPending { set { _manualRebirthPending = value; } }
        internal bool PreRebirthPrepRunning { set { _customPreRebirthPrepRunning = value; } }
        private bool _manualRebirthPending, _customPreRebirthPrepRunning;
        private double _customIronPillPrepStartedAt = -1d, _customIronPillRetryAt;
        private string _customIronPillPlanName;
        // Formula/policy coverage stays in the Iron Pill fixtures; telemetry reads supplied boundary results.
        internal double IronPillCooldownSeconds = double.PositiveInfinity, IronPillWorth;
        private double IronPillCooldownRemainingSeconds() => IronPillCooldownSeconds;
        private double CustomIronPillWorth() => IronPillWorth;
        private bool CustomIronPillPrepActive() => false;
        private bool CustomRebirthReady(CustomPlan plan) => false;
        internal bool BossPush, BossPushFails;
        internal bool BossPushRunning { get { if (BossPushFails) throw new InvalidOperationException("simulated boss state failure"); return BossPush; } }
        internal static string LastLog;
        internal static void Log(string message) { LastLog = message; }
        internal static string LogError(string context, Exception error) { return context + ": " + error.Message; }
        internal static string GetCustomAutomationStatus() { return "idle"; }
        internal void InvalidateCustomRuntimePlanCache() { Invalidations++; }
        internal void ValidateScheduledPlanEdit(string name, CustomPlan plan) { }
        // Runtime policy is covered by the Blood/SustainedFocus fixtures; this fixture tests transport and persistence.
        private CustomPlan LoadRuntimeCustomPlan() => CustomPlanStore.Load(Settings.CustomPlanFile);
        internal static bool EffectiveCustomAutoBloodSpellsEnabled(CustomPlan plan) => plan?.AutoBloodSpellsEnabled ?? Settings?.CustomAutoBloodSpellsEnabled ?? false;
        internal static int EffectiveCustomBloodSpaghettiGoalPercent(CustomPlan plan) => plan?.BloodSpaghettiGoalPercent ?? Settings?.CustomBloodSpaghettiGoalPercent ?? 0;
        internal static int EffectiveCustomBloodCounterfeitGoalPercent(CustomPlan plan) => plan?.BloodCounterfeitGoalPercent ?? Settings?.CustomBloodCounterfeitGoalPercent ?? 0;
        internal static int EffectiveCustomIronPillSaveSeconds(CustomPlan plan) => plan?.IronPillSaveSeconds ?? Settings?.CustomIronPillSaveSeconds ?? 0;
        internal static double EffectiveCustomIronPillMinWorth(CustomPlan plan) => plan?.IronPillMinWorth ?? Settings?.CustomIronPillMinWorth ?? 0d;
        internal void InvalidateCustomEquipmentApplication() { }
        private GearOptimizerResult ApplyManualGearSelection(GearOptimizationGoal goal, FightScoreMode mode)
        {
            InvalidateCustomEquipmentApplication();
            return GearOptimizer.OptimizeAndEquip(goal, mode); // Native selected-setup execution is exercised by Ownership.
        }
        private void CancelCustomTitanGoldKill(string reason) { }
        private void CancelCustomTitanFightKill(string reason) { }
        private void CancelCustomAdventureGoldPush(string reason) { }
        private CustomFocusGoal? _fixtureFocus;
        internal CustomFocusGoal? GetActiveCustomFocusGoal() { return _fixtureFocus; }
        internal string GetCustomFocusStatus() { return FocusFails ? "failed" : _fixtureFocus.HasValue ? "applied" : "idle"; }
        internal string GetAppliedCustomFocusGoal() { return FocusFails ? string.Empty : _fixtureFocus?.ToString() ?? string.Empty; }
        internal SimpleJSON.JSONObject GetCustomPlanProgress() { var root = new SimpleJSON.JSONObject(); root["available"] = false; root["status"] = "unavailable"; return root; }
        internal static SimpleJSON.JSONObject UnavailableCustomPlanProgress() { var root = new SimpleJSON.JSONObject(); root["available"] = false; root["status"] = "unavailable"; return root; }
        internal SimpleJSON.JSONObject GetCustomNguRuntime() { return new SimpleJSON.JSONObject(); }
        internal string ApplyCustomFocusGoal(CustomFocusGoal goal) { Focuses++; _fixtureFocus = goal; return FocusFails ? "ERROR in focus: simulated failure" : "apply " + goal; }
        internal string ToggleCustomFocusGoal(CustomFocusGoal goal) { Focuses++; _fixtureFocus = _fixtureFocus == goal ? null : (CustomFocusGoal?) goal; return "toggle " + goal; }
        internal string StopCustomFocusGoal() { _fixtureFocus = null; return "stopped"; }
        private Managers.FightScoreMode GetActiveFightScoreMode() { return Managers.FightScoreMode.SqrtPowerToughness; }
        internal string RunManualFightPrep() { throw new Exception("Test must not run live prep"); }
        internal string RunManualRebirth() { throw new Exception("Test must not run live rebirth"); }
        internal string RunSafeCustomProbe() { return "probe"; }
        internal string RunProbeCommandFile() { throw new Exception("Test must not run probe commands"); }
        internal string PreCacheCustomEquipmentLoadouts() { return "cached"; }
        internal void ResetCustomPreRebirthPrep() { }
        private void QuickSave() { Saves++; }

        // Fixture setup replaces Unity scene discovery in Main.cs. Cleanup and dispatch stay real.
        internal void Initialize(string directory, bool ready = true)
        {
            _injectorLease = JsonFiles.AcquireInjectorLease(directory, 100);
            _dir = directory;
            _profilesDir = Path.Combine(directory, "profiles");
            _customPlansDir = Path.Combine(directory, "custom-plans");
            Directory.CreateDirectory(_profilesDir);
            Directory.CreateDirectory(_customPlansDir);
            Environment.SetEnvironmentVariable("NGUINJECTOR_EXTERNAL_UI", "1");
            reference = this;
            _started = ready;
            InitializeExternalUi();
        }
        internal void SetReady(bool ready) { _started = ready; if (ready) WriteExternalUiRuntimeSnapshot(0); }
        internal long RuntimeRevision { get { return _externalUiRuntimeRevision; } }
        internal SimpleJSON.JSONObject BuildRuntime() { return BuildExternalUiRuntimeState(); }
        internal SimpleJSON.JSONObject PublishRuntimeAt(DateTime timestamp)
        {
            // Freeze only the wall-clock boundary, not the requested/applied action state.
            var state = BuildExternalUiRuntimeState();
            state["updatedAt"] = timestamp.ToString("o");
            return PublishExternalUiRuntimeState(state, true);
        }
        internal SimpleJSON.JSONObject BloodState() { var root = new SimpleJSON.JSONObject(); WriteExternalUiBloodState(root); return root; }
        internal void StartupFailure() { FailStartup(new InvalidOperationException("simulated startup failure")); }
        internal bool LeaseHeld { get { return _injectorLease != null; } }
        internal void Process(string id, string text)
        {
            var path = Path.Combine(_externalUiCommandsDir, id + ".json");
            File.WriteAllText(path, text);
            ProcessExternalUiCommand(path);
        }
        internal void Pump() { ProcessExternalUiCommands(); }
        internal void PumpRuntime() { _externalUiLastPump = DateTime.MinValue; _externalUiLastRuntimeWrite = DateTime.MinValue; PumpExternalUi(30); }
        internal void Watch(int kind) { QueueWatcherReload(kind); }
        internal void PumpWatches() { PumpWatcherReloads(); }
        internal void ForceWatchDue(int kind) { _watcherReloadDue[kind] = DateTime.UtcNow.AddSeconds(-1); }
        internal bool WatchPending(int kind) { return _watcherReloadDue[kind] != DateTime.MinValue; }
    }
}
