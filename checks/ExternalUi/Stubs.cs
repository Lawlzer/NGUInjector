using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using NGUInjector.CustomPlans;

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

        public static string ToJson(object value, bool pretty = false)
        {
            var fields = value.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(f => Attribute.IsDefined(f, typeof(SerializeField)));
            return JsonSerializer.Serialize(fields.ToDictionary(f => f.Name, f => f.GetValue(value)), Options);
        }

        public static T FromJson<T>(string text)
        {
            using (var document = JsonDocument.Parse(text))
            {
                var instance = Activator.CreateInstance(typeof(T), new object[] { null });
                foreach (var field in typeof(T).GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (!Attribute.IsDefined(field, typeof(SerializeField))
                        || !document.RootElement.TryGetProperty(field.Name, out var value))
                        continue;
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
    }
    internal class FakeBlood { internal bool lootAutoSpell, goldAutoSpell, rebirthAutoSpell; internal double lootSpellBlood, goldSpellBlood, bloodPoints; }
    internal class FakeBloodController
    {
        internal bool Fail;
        internal double totalBloodGainedPerSecond() { return 0; }
        internal double lootBonus() { if (Fail) throw new InvalidOperationException("simulated blood formula failure"); return 1; }
        internal double goldBonus() { return 1; }
    }
    internal class FakeBloodSpells { internal double minLootBlood() { return 1; } internal double minGoldBlood() { return 1; } }
    internal class FakeTime { internal double totalseconds; }
    internal class FakeAdventure { internal int zone; internal bool autoattacking; }
    internal class FakeForm : IDisposable
    {
        internal Action OnDispose;
        internal void UpdateFromSettings(SavedSettings settings) { }
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
        private static string _dir, _profilesDir, _customPlansDir;
        private static AllocationProfiles.CustomAllocation _profile;
        private bool enabled { get; set; } = true;
        internal static FileSystemWatcher ConfigWatcher, AllocationWatcher, ZoneWatcher;
        internal static StreamWriter OutputWriter, LootWriter, CombatWriter, AllocationWriter, PitSpinWriter;
        internal static object Controller, PlayerController, WishManager;
        internal int CancelCalls, CoroutineStops;
        private void CancelInvoke() { CancelCalls++; }
        private void StopAllCoroutines() { CoroutineStops++; }
        internal static int Saves, Invalidations, Allocations, Focuses, Equips, Previews, ZoneReloads;
        internal static bool FocusFails;
        internal bool ManualRebirthPending { set { _manualRebirthPending = value; } }
        internal bool PreRebirthPrepRunning { set { _customPreRebirthPrepRunning = value; } }
        private bool _manualRebirthPending, _customPreRebirthPrepRunning;
        private double _customIronPillPrepStartedAt = -1d, _customIronPillRetryAt;
        private bool CustomIronPillPrepActive() => false;
        private bool CustomRebirthReady(CustomPlan plan) => false;
        internal bool BossPush, BossPushFails;
        internal bool BossPushRunning { get { if (BossPushFails) throw new InvalidOperationException("simulated boss state failure"); return BossPush; } }
        internal static string LastLog;
        internal static void Log(string message) { LastLog = message; }
        internal static string LogError(string context, Exception error) { return context + ": " + error.Message; }
        internal static string GetCustomAutomationStatus() { return "idle"; }
        internal int ScheduleResets;
        internal void InvalidateCustomRuntimePlanCache(bool resetStageRuntime = true) { Invalidations++; if (resetStageRuntime) ScheduleResets++; }
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
