using System;
using System.Collections.Generic;
using System.Linq;
using NGUInjector.AllocationProfiles.RebirthStuff;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;
using SimpleJSON;

namespace NGUInjector
{
    internal partial class Main
    {
        private static string _customAutomationStatus = "Custom automation idle.";
        private string _customStageKey;
        private bool _customStageGearDone;
        private string _customStageFightKey;
        private bool _customTitanGoldActive;
        private int _customTitanGoldIndex = -1;
        private string _customTitanGoldReadiness;
        private DateTime _customTitanGoldStartedAt = DateTime.MinValue;
        private bool _customTitanGoldUsesGoldGear;
        private bool _customTitanFightActive;
        private int _customTitanFightIndex = -1;
        private string _customTitanFightReadiness;
        private DateTime _customTitanFightStartedAt = DateTime.MinValue;
        private DateTime[] _customTitanAutokillCooldownUntil = new DateTime[0];
        private string[] _customTitanAutokillCooldownReason = new string[0];
        private CustomPlan _customRuntimePlanCache;
        private string _customRuntimePlanCacheName;
        private DateTime _customRuntimePlanCacheLoadedAt = DateTime.MinValue;
        private string _customPrecachedLoadoutKey;
        private double _customIronPillPrepStartedAt = -1d;
        private double _customIronPillRetryAt;
        private double _customIronPillPrepLastWaitLogAt = -1d;
        private DateTime _customLastQuickResourceRefresh = DateTime.MinValue;
        private DateTime _customEnergyFruitCapLastRetry = DateTime.MinValue;
        private DateTime _customMagicFruitCapLastRetry = DateTime.MinValue;
        private double _customLastObservedRebirthSeconds = -1d;
        private DateTime _customIdleResourceWarningLastShown = DateTime.MinValue;

        private const double CustomQuickResourceRefreshSeconds = 2d;
        private const double CustomFruitCapRetryCooldownSeconds = 5 * 60d;
        private const double CustomTitanAutokillFailureCooldownSeconds = 60 * 60d;
        private const double CustomIdleResourceWarningCooldownSeconds = 2d;

        private enum CustomAutomationGoal
        {
            None,
            StagePlan,
            Adventure,
            RebirthReady,
            BossPush,
            PreRebirthPrep,
            ManualRebirth
        }

        internal static string GetCustomAutomationStatus()
        {
            return _customAutomationStatus;
        }

        internal static void SetCustomAutomationStatus(string status)
        {
            _customAutomationStatus = string.IsNullOrWhiteSpace(status) ? "Custom automation idle." : status;
        }

        internal static JSONObject UnavailableCustomPlanProgress()
        {
            var root = new JSONObject();
            root["available"] = false;
            root["status"] = "unavailable";
            root["planName"] = Settings?.CustomPlanFile ?? string.Empty;
            root["planVersion"] = CustomPlans.CustomPlanStore.CurrentPlanVersion;
            root["schedulerMode"] = "time";
            root["activeStageIndex"] = -1;
            root["stageProgress"] = JSONNull.CreateOrGet();
            root["elapsedSeconds"] = 0;
            root["stageElapsedSeconds"] = 0;
            root["cycle"] = 0;
            root["repeating"] = false;
            root["loopStartIndex"] = -1;
            root["stages"] = new JSONArray();
            root["reason"] = "Injector, settings, plan store, or rebirth clock is unavailable.";
            return root;
        }

        internal JSONObject GetCustomPlanProgress()
        {
            var root = UnavailableCustomPlanProgress();
            if (Settings == null || CustomPlanStore == null || Character?.rebirthTime == null)
                return root;
            try
            {
                var plan = LoadRuntimeCustomPlan();
                if (plan.SourceJson == null)
                {
                    root["reason"] = "The selected plan could not be loaded; fallback stages are not runtime progress.";
                    return root;
                }
                var snapshot = CustomStageAllocator.GetSchedulerSnapshot(plan, Character.rebirthTime.totalseconds);
                if (snapshot == null) return root;
                var status = !plan.StageSchedulerEnabled ? "disabled" : _customRequestedFocusGoal.HasValue ? "interrupted"
                    : !Settings.UseCustomRebirthPlan ? "paused"
                    : _manualRebirthPending || _customPreRebirthPrepRunning || BossPushRunning ? "interrupted"
                    : "running";
                var reason = status == "disabled" ? "Stage scheduler is disabled."
                    : _customRequestedFocusGoal.HasValue ? "Quick focus " + _customRequestedFocusGoal + " (" + _customFocusStatus + ") overrides the plan."
                    : status == "paused" ? "The custom plan is paused; the rebirth clock is not a running plan timer."
                    : status == "interrupted" ? "Rebirth preparation or boss push overrides stage allocation."
                    : snapshot.ActiveStageIndex < 0 ? "Waiting for the allocator to initialize the sequential scheduler." : string.Empty;
                if (status == "running" && snapshot.ActiveStageIndex < 0) status = "unavailable";
                root["available"] = true;
                root["status"] = status;
                root["reason"] = reason;
                root["planName"] = Settings.CustomPlanFile;
                root["planVersion"] = plan.Version;
                if (!string.IsNullOrEmpty(plan.RuntimeRevision)) root["planRevision"] = plan.RuntimeRevision;
                root["schedulerMode"] = snapshot.SchedulerMode;
                root["activeStageIndex"] = status == "running" ? snapshot.ActiveStageIndex : -1;
                root["stageProgress"] = status == "running" && snapshot.StageProgress.HasValue
                    ? (JSONNode) new JSONNumber(snapshot.StageProgress.Value) : JSONNull.CreateOrGet();
                root["elapsedSeconds"] = snapshot.ElapsedSeconds;
                root["stageElapsedSeconds"] = status == "running" ? snapshot.StageElapsedSeconds : 0d;
                root["cycle"] = snapshot.Cycle;
                root["repeating"] = plan.StageLoopStartIndex >= 0;
                root["loopStartIndex"] = plan.StageLoopStartIndex;
                root["bloodReservedForIronPill"] = CustomIronPillPrepActive();
                var rows = root["stages"].AsArray;
                for (var i = 0; i < plan.Stages.Length; i++)
                {
                    var stage = plan.Stages[i];
                    var row = new JSONObject();
                    row["name"] = stage.Name;
                    row["index"] = i;
                    row["durationSeconds"] = snapshot.DurationSeconds[i];
                    row["completionMode"] = stage.CompletionMode;
                    row["energyFocus"] = stage.EnergyFocus;
                    row["magicFocus"] = stage.MagicFocus;
                    row["gearGoal"] = stage.GearGoal;
                    row["configuredEnergyPriorities"] = ToJsonArray(stage.EnergyPriorities ?? new string[0]);
                    row["configuredMagicPriorities"] = ToJsonArray(stage.MagicPriorities ?? new string[0]);
                    var active = status == "running" && i == snapshot.ActiveStageIndex;
                    var remaining = active ? (int) Math.Max(0, snapshot.DurationSeconds[i] - Math.Floor(snapshot.StageElapsedSeconds)) : 0;
                    row["energyPriorities"] = ToJsonArray(CustomStageAllocator.EffectiveStagePriorities(stage, true, remaining,
                        active ? snapshot.DurationSeconds[i] : 0, out _));
                    row["magicPriorities"] = ToJsonArray(CustomStageAllocator.EffectiveStagePriorities(stage, false, remaining,
                        active ? snapshot.DurationSeconds[i] : 0, out _, active && CustomIronPillPrepActive()));
                    row["targetAdvancedTrainingPower"] = stage.TargetAdvancedTrainingPower.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    row["targetAdvancedTrainingToughness"] = stage.TargetAdvancedTrainingToughness.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    rows.Add(row);
                }
            }
            catch (Exception e)
            {
                root = UnavailableCustomPlanProgress();
                root["reason"] = e.Message;
            }
            return root;
        }

        internal JSONObject GetCustomNguRuntime()
        {
            try
            {
                var plan = LoadRuntimeCustomPlan();
                var root = BlackBarAllocator.GetNguRuntime(plan.EnergyNguWeights, plan.MagicNguWeights);
                if (_customSustainedFocusGoal.HasValue)
                {
                    foreach (var energy in new[] {true, false})
                    {
                        var applied = energy ? _customFocusEnergyNguWeights : _customFocusMagicNguWeights;
                        var configured = energy ? plan.EnergyNguWeights : plan.MagicNguWeights;
                        var lane = energy ? "energy" : "magic";
                        root[lane + "WeightsApplied"] = applied != null && applied.SequenceEqual(configured);
                        var weights = new JSONArray();
                        foreach (var weight in applied ?? new int[0]) weights.Add(weight);
                        root[lane + "AppliedWeights"] = applied == null ? (JSONNode) JSONNull.CreateOrGet() : weights;
                    }
                }
                return root;
            }
            catch (Exception e)
            {
                var root = new JSONObject();
                root["available"] = false;
                root["reason"] = e.Message;
                return root;
            }
        }

        private void WarnIfCustomAutomationHasIdleResources(CustomPlan plan)
        {
            if (!CustomIdleResourceWarningActive(plan) || Character == null)
                return;

            var idleResources = new List<string>();
            if (Settings.ManageEnergy && Character.idleEnergy > 0 && Character.idleEnergy != BlackBarAllocator.ExcessIdleEnergy)
                idleResources.Add($"{Character.idleEnergy:n0} idle energy");

            if (Settings.ManageMagic && Character.magic.idleMagic > 0 && Character.magic.idleMagic != BlackBarAllocator.ExcessIdleMagic)
                idleResources.Add($"{Character.magic.idleMagic:n0} idle magic");

            if (idleResources.Count == 0)
                return;

            var current = GetCustomAutomationStatus();
            if (!string.IsNullOrWhiteSpace(current)
                && current.StartsWith("WARNING:", StringComparison.OrdinalIgnoreCase)
                && current.IndexOf("idle", StringComparison.OrdinalIgnoreCase) < 0)
                return;

            var now = DateTime.UtcNow;
            if ((now - _customIdleResourceWarningLastShown).TotalSeconds < CustomIdleResourceWarningCooldownSeconds)
                return;

            _customIdleResourceWarningLastShown = now;
            SetCustomAutomationStatus($"WARNING: Custom automation left {string.Join(" and ", idleResources.ToArray())} after allocation.");
        }

        private bool CustomIdleResourceWarningActive(CustomPlan plan)
        {
            if (_customSustainedFocusGoal.HasValue || CustomIronPillPrepActive())
                return true;

            return (plan != null && Settings.UseCustomRebirthPlan)
                   || Settings.CustomAdventureEnabled
                   || Settings.ManualAdventureEnabled;
        }

        private void RunCustomPlanAutomation(CustomPlan plan)
        {
            if (_manualRebirthPending || _customPreRebirthPrepRunning || BossPushRunning)
                return;

            if (plan == null || !Settings.UseCustomRebirthPlan || !plan.StageSchedulerEnabled)
                return;

            var runtime = CustomStageAllocator.GetActiveStage(plan);
            if (runtime == null)
                return;

            var stageKey = CustomStageRuntimeKey(plan, runtime);
            if (_customStageKey != stageKey)
            {
                _customStageKey = stageKey;
                _customStageGearDone = false;
                ResetCustomFruitCapRetryCooldowns();
                CustomStageAllocator.ResetResourceTargets();
                Log($"Custom stage active: {runtime.Message}");
            }

            if (runtime.Stage.FightOnEntry)
            {
                var cycle = CustomStageAllocator.GetSchedulerSnapshot(plan, Character.rebirthTime.totalseconds).Cycle;
                var fightKey = $"{Settings.CustomPlanFile}:{stageKey}:{cycle}";
                if (_customStageFightKey != fightKey)
                {
                    if (!LoadoutManager.CanSwap() || !DiggerManager.CanSwap())
                    {
                        SetCustomAutomationStatus($"{runtime.Message}; fight on entry waiting for loadout lock {LoadoutManager.CurrentLock}, digger lock {DiggerManager.CurrentLock}");
                        return;
                    }

                    // Keep this separate from gear/cache invalidation; prep is attempted once per entry.
                    _customStageFightKey = fightKey;
                    _customStageGearDone = false;
                    SetCustomAutomationStatus(RunFightPrep(plan, false, false, $"{runtime.Message}; fight on entry"));
                    return;
                }
            }

            if (!TryEquipStageGear(plan, runtime))
                return;

            var messages = new List<string>();
            if (!TryActivateCustomFruits(plan, runtime, out var fruitActivation))
            {
                SetCustomAutomationStatus($"{runtime.Message}; {fruitActivation}");
                return;
            }

            if (!string.IsNullOrWhiteSpace(fruitActivation))
                messages.Add(fruitActivation);

            messages.Add(CustomStageAllocator.ApplyStage(plan, runtime.Stage, runtime.RemainingSeconds, FocusSettleMilliseconds, stageKey, CustomStageDurationSeconds(runtime)));
            var allocation = string.Join("; ", messages.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray());
            SetCustomAutomationStatus(BuildRuntimeStatus(plan, runtime, allocation));
        }

        private void RunQuickCustomResourceRefresh(CustomPlan plan)
        {
            var now = DateTime.UtcNow;
            if ((now - _customLastQuickResourceRefresh).TotalSeconds < CustomQuickResourceRefreshSeconds)
                return;

            _customLastQuickResourceRefresh = now;

            if (CustomQuickResourceRefreshBlocked(plan))
            {
                BlackBarAllocator.ResetExcessIdle();
                return;
            }

            if (CustomStageReadyForQuickRefresh(plan, out var runtime, out var stageKey))
            {
                var allocation = CustomStageAllocator.ApplyStage(plan, runtime.Stage, runtime.RemainingSeconds, 0, stageKey, CustomStageDurationSeconds(runtime));
                if (!string.IsNullOrWhiteSpace(allocation))
                    SetCustomAutomationStatus(BuildRuntimeStatus(plan, runtime, allocation));
            }
            else if (!_customSustainedFocusGoal.HasValue)
            {
                BlackBarAllocator.ResetExcessIdle();
            }

            RunCustomSustainedFocusAllocation(false);
        }

        private bool CustomQuickResourceRefreshBlocked(CustomPlan plan)
        {
            return _manualRebirthPending
                   || _customPreRebirthPrepRunning
                   || BossPushRunning
                   || CustomRebirthReady(plan);
        }

        private bool CustomStageReadyForQuickRefresh(CustomPlan plan, out CustomStageRuntime runtime, out string stageKey)
        {
            runtime = null;
            stageKey = null;
            if (plan == null || !Settings.UseCustomRebirthPlan || !plan.StageSchedulerEnabled)
                return false;

            runtime = CustomStageAllocator.GetActiveStage(plan);
            if (runtime == null)
                return false;

            stageKey = CustomStageRuntimeKey(plan, runtime);
            if (_customStageKey != stageKey)
                return false;

            return _customStageGearDone || runtime.Stage.GearGoal < 0;
        }

        private static string CustomStageRuntimeKey(CustomPlan plan, CustomStageRuntime runtime)
        {
            var planName = plan == null ? string.Empty : plan.Name;
            return runtime == null ? planName : $"{planName}:{runtime.StageIndex}:{runtime.StageStartSeconds}:{runtime.StageEndSeconds}";
        }

        private static int CustomStageDurationSeconds(CustomStageRuntime runtime)
        {
            return runtime == null ? 0 : runtime.EffectiveDurationSeconds;
        }

        private bool TryActivateCustomFruits(CustomPlan plan, CustomStageRuntime runtime, out string message)
        {
            message = null;
            if (!CustomFruitActivationEligible(plan))
                return true;

            var activated = YggdrasilManager.ActivateAffordableFruits();
            var usedCapGear = false;

            if (runtime != null && runtime.Stage.GearGoal >= 0 && LoadoutManager.CanSwap())
            {
                if (YggdrasilManager.HasInactiveUnlockedFruit(true) && !CustomFruitCapRetryOnCooldown(true))
                {
                    var energyActivated = ActivateCustomFruitsWithCapGear(GearOptimizationGoal.EnergyCap, (FightScoreMode) plan.FightScoreMode, out usedCapGear);
                    activated += energyActivated;
                    if (energyActivated <= 0 && YggdrasilManager.HasInactiveUnlockedFruit(true))
                        MarkCustomFruitCapRetry(true);
                }

                var usedMagicCapGear = false;
                if (YggdrasilManager.HasInactiveUnlockedFruit(false) && !CustomFruitCapRetryOnCooldown(false))
                {
                    var magicActivated = ActivateCustomFruitsWithCapGear(GearOptimizationGoal.MagicCap, (FightScoreMode) plan.FightScoreMode, out usedMagicCapGear);
                    activated += magicActivated;
                    if (magicActivated <= 0 && YggdrasilManager.HasInactiveUnlockedFruit(false))
                        MarkCustomFruitCapRetry(false);
                }

                usedCapGear = usedCapGear || usedMagicCapGear;
            }

            if (usedCapGear && !RestoreCustomStageGear(plan, runtime, out var restoreMessage))
            {
                _customStageGearDone = false;
                message = activated <= 0 ? restoreMessage : $"{FruitActivationSummary(activated)}; {restoreMessage}";
                return false;
            }

            if (activated <= 0)
                return true;

            message = FruitActivationSummary(activated);
            return true;
        }

        private void RunCustomFruitActivation(CustomPlan plan)
        {
            if (!CustomFruitActivationAutomationActive())
                return;

            if (plan == null)
            {
                if (CustomPlanStore == null)
                    return;

                plan = LoadRuntimeCustomPlan();
            }

            if (!TryActivateCustomFruits(plan, null, out var fruitActivation))
            {
                if (!string.IsNullOrWhiteSpace(fruitActivation))
                    SetCustomAutomationStatus(fruitActivation);
                return;
            }

            if (!string.IsNullOrWhiteSpace(fruitActivation))
                SetCustomAutomationStatus(fruitActivation);
        }

        private static int ActivateCustomFruitsWithCapGear(GearOptimizationGoal goal, FightScoreMode fightScoreMode, out bool usedCapGear)
        {
            usedCapGear = false;

            LoadoutManager.RemoveResourcesForGearSwap();
            FocusSettle();
            var result = GearOptimizer.OptimizeAndEquip(goal, fightScoreMode, false, true);
            if (!result.Success)
            {
                Log($"Custom fruit activation skipped {GoalLabel((int) goal)} gear: {result.Message}");
                return 0;
            }

            usedCapGear = true;
            FocusSettle();
            Log($"Custom fruit activation using {result.Message}");
            return YggdrasilManager.ActivateAffordableFruits(true);
        }

        private bool RestoreCustomStageGear(CustomPlan plan, CustomStageRuntime runtime, out string message)
        {
            message = null;
            if (runtime == null || runtime.Stage.GearGoal < 0)
                return true;

            LoadoutManager.RemoveResourcesForGearSwap();
            FocusSettle();
            var result = GearOptimizer.OptimizeAndEquip((GearOptimizationGoal) runtime.Stage.GearGoal, (FightScoreMode) plan.FightScoreMode, false, true);
            FocusSettle();
            if (result.Success)
            {
                Log($"Custom fruit activation restored stage gear: {result.Message}");
                return true;
            }

            message = $"fruit activation waiting to restore stage gear: {result.Message}";
            return false;
        }

        private static string FruitActivationSummary(int activated)
        {
            return activated == 1 ? "activated 1 fruit" : $"activated {activated} fruits";
        }

        private bool CustomFruitCapRetryOnCooldown(bool usesEnergy)
        {
            var lastRetry = usesEnergy ? _customEnergyFruitCapLastRetry : _customMagicFruitCapLastRetry;
            return (DateTime.UtcNow - lastRetry).TotalSeconds < CustomFruitCapRetryCooldownSeconds;
        }

        private void MarkCustomFruitCapRetry(bool usesEnergy)
        {
            if (usesEnergy)
                _customEnergyFruitCapLastRetry = DateTime.UtcNow;
            else
                _customMagicFruitCapLastRetry = DateTime.UtcNow;
        }

        private void ResetCustomFruitCapRetryCooldowns()
        {
            _customEnergyFruitCapLastRetry = DateTime.MinValue;
            _customMagicFruitCapLastRetry = DateTime.MinValue;
        }

        private static bool CustomFruitActivationEligible(CustomPlan plan)
        {
            return CustomFruitActivationAutomationActive()
                   && plan != null
                   && plan.AutoActivateFruits
                   && CustomPlannedRebirthSeconds(plan) >= 3600;
        }

        private static bool CustomFruitActivationAutomationActive()
        {
            return Settings.UseCustomRebirthPlan
                   || Settings.CustomAdventureEnabled
                   || Settings.ManualAdventureEnabled;
        }

        private static int CustomPlannedRebirthSeconds(CustomPlan plan)
        {
            if (plan == null)
                return 0;

            var planned = 0;
            if (plan.TimeTriggerEnabled)
                planned = Math.Max(planned, plan.TimeTriggerSeconds);

            if (plan.StageSchedulerEnabled)
                planned = Math.Max(planned, plan.StageDurationSeconds);

            return planned;
        }

        private bool TryEquipStageGear(CustomPlan plan, CustomStageRuntime runtime)
        {
            if (_customStageGearDone || runtime.Stage.GearGoal < 0)
                return true;

            if (!LoadoutManager.CanSwap())
            {
                SetCustomAutomationStatus($"{runtime.Message}; waiting for loadout lock {LoadoutManager.CurrentLock}");
                return false;
            }

            LoadoutManager.RemoveResourcesForGearSwap();
            FocusSettle();
            var result = GearOptimizer.OptimizeAndEquip((GearOptimizationGoal) runtime.Stage.GearGoal, (FightScoreMode) plan.FightScoreMode, false, true);
            if (result.Success)
            {
                _customStageGearDone = true;
                FocusSettle();
                Log(result.Message);
                return true;
            }

            SetCustomAutomationStatus($"{runtime.Message}; gear skipped: {result.Message}");
            return false;
        }

        private void ObserveCustomRebirthEpoch()
        {
            if (Character == null)
                return;

            var seconds = Math.Max(0d, Character.rebirthTime.totalseconds);
            if (_customLastObservedRebirthSeconds >= 0d && seconds + 5d < _customLastObservedRebirthSeconds)
            {
                Log($"Custom rebirth epoch changed: {_customLastObservedRebirthSeconds:0.#}s -> {seconds:0.#}s. Resetting Custom runtime state.");
                ResetCustomPreRebirthPrep();
                ResetCustomAutomationRuntime();
            }

            _customLastObservedRebirthSeconds = seconds;
        }

        private CustomAutomationGoal CurrentCustomGoal(CustomPlan plan)
        {
            if (_manualRebirthPending)
                return CustomAutomationGoal.ManualRebirth;

            if (_customPreRebirthPrepRunning)
                return CustomAutomationGoal.PreRebirthPrep;

            if (BossPushRunning)
                return CustomAutomationGoal.BossPush;

            if (CustomRebirthReady(plan))
                return CustomAutomationGoal.RebirthReady;

            if (plan != null && Settings.UseCustomRebirthPlan)
                return CustomAutomationGoal.StagePlan;

            return Settings.CustomAdventureEnabled ? CustomAutomationGoal.Adventure : CustomAutomationGoal.None;
        }

        private bool CustomGoalBlocksAdventureGoldPush(CustomPlan plan, out string reason)
        {
            switch (CurrentCustomGoal(plan))
            {
                case CustomAutomationGoal.ManualRebirth:
                    reason = "manual rebirth";
                    return true;
                case CustomAutomationGoal.PreRebirthPrep:
                    reason = "pre-rebirth prep";
                    return true;
                case CustomAutomationGoal.BossPush:
                    reason = "boss push";
                    return true;
                case CustomAutomationGoal.RebirthReady:
                    reason = "Custom rebirth is ready";
                    return true;
                default:
                    reason = null;
                    return false;
            }
        }

        internal string PreCacheCustomEquipmentLoadouts()
        {
            var plan = CustomPlanStore?.Load(Settings.CustomPlanFile);
            return PreCacheCustomEquipmentLoadouts(plan, true);
        }

        private string PreCacheCustomEquipmentLoadouts(CustomPlan plan, bool force)
        {
            var goals = GetPlannedEquipmentGoals(plan).Distinct().ToArray();
            var fightScoreMode = plan == null ? FightScoreMode.SqrtPowerToughness : (FightScoreMode) plan.FightScoreMode;
            var key = $"{Settings.CustomPlanFile}:{(int) fightScoreMode}:{string.Join(",", goals.Select(x => ((int) x).ToString()).ToArray())}";
            if (!force && _customPrecachedLoadoutKey == key)
                return "Generated equipment loadouts already pre-cached.";

            GearOptimizer.ClearCache();
            var message = GearOptimizer.PreCache(goals, fightScoreMode);
            _customPrecachedLoadoutKey = key;
            return message;
        }

        private IEnumerable<GearOptimizationGoal> GetPlannedEquipmentGoals(CustomPlan plan)
        {
            if (plan != null)
            {
                if (plan.StageSchedulerEnabled && plan.Stages != null)
                {
                    foreach (var stage in plan.Stages.Where(stage => stage != null && stage.GearGoal >= 0 && GearOptimizer.GoalLabels.ContainsKey(stage.GearGoal)))
                    {
                        yield return (GearOptimizationGoal) stage.GearGoal;
                    }
                }

                if (GearOptimizer.GoalLabels.ContainsKey(plan.GearGoal))
                    yield return (GearOptimizationGoal) plan.GearGoal;

                if (plan.AutoActivateFruits && CustomPlannedRebirthSeconds(plan) >= 3600)
                {
                    yield return GearOptimizationGoal.EnergyCap;
                    yield return GearOptimizationGoal.MagicCap;
                }

                yield return GearOptimizationGoal.Fight;
            }

            if (Settings.CustomAdventureGoldPush || Settings.ManageGoldLoadouts)
                yield return GearOptimizationGoal.Gold;
        }

        private string BuildRuntimeStatus(CustomPlan plan, CustomStageRuntime runtime, string allocation)
        {
            var elapsed = (int) Math.Max(0d, Character.rebirthTime.totalseconds);
            var duration = Math.Max(1, plan.StageDurationSeconds);
            var multiplier = CurrentNumberMultiplier();
            var reservation = CustomIronPillPrepActive()
                ? $"; Iron Pill blood reserved {Math.Max(0d, Character.rebirthTime.totalseconds - _customIronPillPrepStartedAt):0}s, worth {CustomIronPillWorth():0.###E+0}"
                : string.Empty;
            return $"Now: {runtime.Message}; rebirth {CustomStageAllocator.FormatSeconds(elapsed)}/{CustomStageAllocator.FormatSeconds(duration)}; NUMBER x{multiplier:0.###}{reservation}; {allocation}";
        }

        private bool TryRouteCustomTitanGoldKill()
        {
            if (_customTitanGoldActive)
            {
                if (_customTitanGoldIndex >= 0 && LoadoutManager.CurrentLock == LockType.Gold && ZoneHelpers.TitanSpawnWindowActive(_customTitanGoldIndex))
                {
                    if (!TitanAutokillRouteStillSafe(_customTitanGoldIndex, _customTitanGoldStartedAt, out var cancelReason))
                    {
                        MarkTitanAutokillFailed(_customTitanGoldIndex, cancelReason);
                        CancelCustomTitanGoldKill(cancelReason);
                        _combManager.MoveToZone(-1);
                        return false;
                    }

                    var zone = ZoneHelpers.TitanZones[_customTitanGoldIndex];
                    StopAdventureAutoattack();
                    SetCustomAutomationStatus($"Titan gold auto-kill: {_customTitanGoldReadiness}");
                    _combManager.MoveToZone(zone);
                    return true;
                }

                if (_customTitanGoldIndex >= 0 && _customTitanGoldUsesGoldGear)
                    ZoneHelpers.MarkTitanMoneyDone(_customTitanGoldIndex);

                CancelCustomTitanGoldKill("titan gold auto-kill complete");
                return false;
            }

            var titanIndex = ZoneHelpers.GetSpawningTitanGoldTarget();
            if (titanIndex < 0)
                return false;

            if (TitanAutokillOnCooldown(titanIndex, out var cooldownWarning))
            {
                SetCustomAutomationStatus(cooldownWarning);
                return false;
            }

            if (_customAdventureGoldPushActive)
                CancelCustomAdventureGoldPush("titan gold auto-kill");

            if (!LoadoutManager.CanSwap())
            {
                SetCustomAutomationStatus($"Titan gold auto-kill waiting for loadout lock {LoadoutManager.CurrentLock}");
                return true;
            }

            LoadoutManager.SaveCurrentLoadout();
            LoadoutManager.AcquireLock(LockType.Gold);
            if (!TryEquipTitanAutokillGear(titanIndex, out var readiness, out var usedGoldGear))
            {
                MarkTitanAutokillFailed(titanIndex, readiness);
                LoadoutManager.RestoreGear();
                LoadoutManager.ReleaseLock();
                return RouteConfiguredAdventureGoal($"Titan gold auto-kill skipped: {readiness}");
            }

            _customTitanGoldActive = true;
            _customTitanGoldIndex = titanIndex;
            _customTitanGoldReadiness = readiness;
            _customTitanGoldStartedAt = DateTime.UtcNow;
            _customTitanGoldUsesGoldGear = usedGoldGear;

            var titanZone = ZoneHelpers.TitanZones[titanIndex];
            Log($"Titan gold auto-kill in {ZoneName(titanZone)}: {readiness}");
            StopAdventureAutoattack();
            SetCustomAutomationStatus($"Titan gold auto-kill: {readiness}");
            _combManager.MoveToZone(titanZone);
            return true;
        }

        private bool TryRouteCustomTitanFightKill()
        {
            if (_customTitanFightActive)
            {
                if (_customTitanFightIndex >= 0 && LoadoutManager.CurrentLock == LockType.Titan && ZoneHelpers.TitanSpawnWindowActive(_customTitanFightIndex))
                {
                    if (!TitanAutokillRouteStillSafe(_customTitanFightIndex, _customTitanFightStartedAt, out var cancelReason))
                    {
                        MarkTitanAutokillFailed(_customTitanFightIndex, cancelReason);
                        CancelCustomTitanFightKill(cancelReason);
                        _combManager.MoveToZone(-1);
                        return false;
                    }

                    var zone = ZoneHelpers.TitanZones[_customTitanFightIndex];
                    StopAdventureAutoattack();
                    SetCustomAutomationStatus($"Titan autokill route: {_customTitanFightReadiness}");
                    _combManager.MoveToZone(zone);
                    return true;
                }

                CancelCustomTitanFightKill("titan fight auto-route complete");
                return false;
            }

            var titanIndex = ZoneHelpers.GetSpawningTitanFightTarget();
            if (titanIndex < 0)
                return false;

            if (TitanAutokillOnCooldown(titanIndex, out var cooldownWarning))
            {
                SetCustomAutomationStatus(cooldownWarning);
                return false;
            }

            if (_customAdventureGoldPushActive)
                CancelCustomAdventureGoldPush("titan autokill route");

            if (!LoadoutManager.CanSwap())
            {
                SetCustomAutomationStatus($"Titan autokill route waiting for loadout lock {LoadoutManager.CurrentLock}");
                return true;
            }

            LoadoutManager.SaveCurrentLoadout();
            LoadoutManager.AcquireLock(LockType.Titan);
            if (!TryEquipTitanAutokillGear(titanIndex, out var readiness, out var _))
            {
                MarkTitanAutokillFailed(titanIndex, readiness);
                LoadoutManager.RestoreGear();
                LoadoutManager.ReleaseLock();
                SetCustomAutomationStatus($"Titan autokill route skipped: {readiness}");
                return false;
            }

            _customTitanFightActive = true;
            _customTitanFightIndex = titanIndex;
            _customTitanFightReadiness = readiness;
            _customTitanFightStartedAt = DateTime.UtcNow;

            var titanZone = ZoneHelpers.TitanZones[titanIndex];
            Log($"Titan autokill route in {ZoneName(titanZone)}: {readiness}");
            StopAdventureAutoattack();
            SetCustomAutomationStatus($"Titan autokill route: {readiness}");
            _combManager.MoveToZone(titanZone);
            return true;
        }

        private bool TryEquipTitanAutokillGear(int titanIndex, out string readiness, out bool usedGoldGear)
        {
            readiness = null;
            usedGoldGear = false;

            var goldResult = GearOptimizer.OptimizeAndEquip(GearOptimizationGoal.Gold, GetActiveFightScoreMode(), true);
            FocusSettle();
            if (goldResult.Success && ZoneHelpers.TitanAutokillAchieved(titanIndex))
            {
                usedGoldGear = true;
                readiness = $"gold gear is enough to autokill {ZoneName(ZoneHelpers.TitanZones[titanIndex])}. {BuildTitanFightReadiness(titanIndex)}";
                return true;
            }

            var goldMessage = goldResult.Success ? "gold gear is not enough for autokill" : $"gold gear skipped: {goldResult.Message}";
            var fightResult = GearOptimizer.OptimizeAndEquip(GearOptimizationGoal.Fight, FightScoreMode.PowerTimesToughness, true);
            FocusSettle();
            if (fightResult.Success && ZoneHelpers.TitanAutokillAchieved(titanIndex))
            {
                readiness = $"fight gear is enough to autokill {ZoneName(ZoneHelpers.TitanZones[titanIndex])}; {goldMessage}. {BuildTitanFightReadiness(titanIndex)}";
                return true;
            }

            var fightMessage = fightResult.Success ? "fight gear is not enough for autokill" : $"fight gear skipped: {fightResult.Message}";
            readiness = $"{ZoneName(ZoneHelpers.TitanZones[titanIndex])} is not autokillable now; {goldMessage}; {fightMessage}. {BuildTitanFightReadiness(titanIndex)}";
            return false;
        }

        private bool TitanAutokillRouteStillSafe(int titanIndex, DateTime startedAt, out string cancelReason)
        {
            cancelReason = null;
            if (!ZoneHelpers.TitanAutokillAchieved(titanIndex))
            {
                cancelReason = "autokill check stopped passing";
                return false;
            }

            if ((DateTime.UtcNow - startedAt).TotalSeconds > 25)
            {
                cancelReason = "autokill route timed out";
                return false;
            }

            if (Character.adventure.zone == ZoneHelpers.TitanZones[titanIndex] && Character.adventureController.currentEnemy != null)
            {
                cancelReason = "titan enemy appeared instead of autokilling";
                return false;
            }

            return true;
        }

        private bool TitanAutokillOnCooldown(int titanIndex, out string warning)
        {
            warning = null;
            EnsureTitanAutokillCooldownArrays();
            if (titanIndex < 0 || titanIndex >= _customTitanAutokillCooldownUntil.Length)
                return false;

            var until = _customTitanAutokillCooldownUntil[titanIndex];
            if (until <= DateTime.UtcNow)
            {
                _customTitanAutokillCooldownUntil[titanIndex] = DateTime.MinValue;
                _customTitanAutokillCooldownReason[titanIndex] = null;
                return false;
            }

            var remaining = Math.Max(0, (int) Math.Ceiling((until - DateTime.UtcNow).TotalSeconds));
            var reason = string.IsNullOrWhiteSpace(_customTitanAutokillCooldownReason[titanIndex])
                ? "previous autokill attempt failed"
                : _customTitanAutokillCooldownReason[titanIndex];
            warning = $"WARNING: Titan autokill for {ZoneName(ZoneHelpers.TitanZones[titanIndex])} is on cooldown for {CustomStageAllocator.FormatSeconds(remaining)} after failure: {reason}";
            return true;
        }

        private void MarkTitanAutokillFailed(int titanIndex, string reason)
        {
            EnsureTitanAutokillCooldownArrays();
            if (titanIndex < 0 || titanIndex >= _customTitanAutokillCooldownUntil.Length)
                return;

            var until = DateTime.UtcNow.AddSeconds(CustomTitanAutokillFailureCooldownSeconds);
            _customTitanAutokillCooldownUntil[titanIndex] = until;
            _customTitanAutokillCooldownReason[titanIndex] = reason;
            var warning = $"WARNING: Titan autokill failed for {ZoneName(ZoneHelpers.TitanZones[titanIndex])}; will not retry for {CustomStageAllocator.FormatSeconds((int) CustomTitanAutokillFailureCooldownSeconds)}. {reason}";
            Log(warning);
            SetCustomAutomationStatus(warning);
            settingsForm?.ReportCustomError(warning);
        }

        private void EnsureTitanAutokillCooldownArrays()
        {
            var length = ZoneHelpers.TitanZones.Length;
            if (_customTitanAutokillCooldownUntil != null && _customTitanAutokillCooldownUntil.Length == length
                && _customTitanAutokillCooldownReason != null && _customTitanAutokillCooldownReason.Length == length)
            {
                return;
            }

            var oldUntil = _customTitanAutokillCooldownUntil ?? new DateTime[0];
            var oldReason = _customTitanAutokillCooldownReason ?? new string[0];
            _customTitanAutokillCooldownUntil = new DateTime[length];
            _customTitanAutokillCooldownReason = new string[length];
            for (var i = 0; i < Math.Min(length, oldUntil.Length); i++)
            {
                _customTitanAutokillCooldownUntil[i] = oldUntil[i];
            }

            for (var i = 0; i < Math.Min(length, oldReason.Length); i++)
            {
                _customTitanAutokillCooldownReason[i] = oldReason[i];
            }
        }

        private static void StopAdventureAutoattack()
        {
            if (Character.adventure.autoattacking)
                Character.adventureController.idleAttackMove.setToggle();
        }

        private string BuildTitanFightReadiness(int titanIndex)
        {
            var enemy = ZoneHelpers.TitanEnemy(titanIndex);
            if (enemy == null)
                return "Titan stats unavailable; trying with best Fight gear.";

            var power = Math.Max(0d, Character.totalAdvAttack());
            var toughness = Math.Max(0d, Character.totalAdvDefense());
            var hp = Math.Max(0d, Character.totalAdvHP());
            var defenseRatio = enemy.defense > 0 ? power / enemy.defense : 0d;
            var attackRatio = enemy.attack > 0 ? toughness / enemy.attack : 0d;
            return $"Stats: P/T/HP {power:0}/{toughness:0}/{hp:0} vs {enemy.name} D/A/HP {enemy.defense:0}/{enemy.attack:0}/{enemy.maxHP:0} ({defenseRatio:0.##}x defense, {attackRatio:0.##}x attack).";
        }

        private void CancelCustomTitanGoldKill(string reason)
        {
            if (!_customTitanGoldActive)
                return;

            if (LoadoutManager.CurrentLock == LockType.Gold)
            {
                LoadoutManager.RestoreGear();
                LoadoutManager.ReleaseLock();
                FocusSettle();
            }

            _customTitanGoldActive = false;
            _customTitanGoldIndex = -1;
            _customTitanGoldReadiness = null;
            _customTitanGoldStartedAt = DateTime.MinValue;
            _customTitanGoldUsesGoldGear = false;
            Log($"Titan gold auto-kill ended for {reason}.");
        }

        private void CancelCustomTitanFightKill(string reason)
        {
            if (!_customTitanFightActive)
                return;

            if (LoadoutManager.CurrentLock == LockType.Titan)
            {
                LoadoutManager.RestoreGear();
                LoadoutManager.ReleaseLock();
                FocusSettle();
            }

            _customTitanFightActive = false;
            _customTitanFightIndex = -1;
            _customTitanFightReadiness = null;
            _customTitanFightStartedAt = DateTime.MinValue;
            Log($"Titan fight auto-route ended for {reason}.");
        }

        private void RunCustomBloodSpellAutomation(CustomPlan plan)
        {
            RunCustomBloodSpellToggles(plan);
            if (!CustomIronPillPrepActive())
                return;

            var elapsed = Math.Max(0d, Character.rebirthTime.totalseconds - _customIronPillPrepStartedAt);
            if (IronPillAutomation.TargetReached(elapsed, CustomIronPillWorth(), Settings.CustomIronPillSaveSeconds, Settings.CustomIronPillMinWorth))
            {
                if (TryCastCustomIronPill())
                {
                    ResetCustomIronPillPrep();
                    _customIronPillRetryAt = Character.rebirthTime.totalseconds + 10d;
                    // Restore spells without re-entering preparation on this same pass.
                    ApplyCustomBloodSpellToggles(plan);
                    return;
                }

                var waitReason = CustomIronPillWaitReason();
                if (_customIronPillPrepLastWaitLogAt < 0d || elapsed - _customIronPillPrepLastWaitLogAt >= 60d)
                {
                    _customIronPillPrepLastWaitLogAt = elapsed;
                    Log($"Custom Iron Pill target reached after {elapsed:0}s; waiting to cast: {waitReason}.");
                }
            }
        }

        private void UpdateCustomIronPillPrep(CustomPlan plan)
        {
            var eligible = CustomIronPillEligibilityBlocker(plan).Length == 0;
            if (!IronPillAutomation.ShouldReserve(eligible, CustomIronPillPrepActive(), IronPillCooldownRemainingSeconds(),
                Settings.CustomIronPillSaveSeconds, Settings.CustomIronPillMinWorth))
            {
                ResetCustomIronPillPrep();
                return;
            }

            if (CustomIronPillPrepActive())
            {
                if (Character.rebirthTime.totalseconds - _customIronPillPrepStartedAt < IronPillAutomation.PrepLimitSeconds(Settings.CustomIronPillSaveSeconds))
                    return;

                ResetCustomIronPillPrep();
                _customIronPillRetryAt = Character.rebirthTime.totalseconds + 900d;
                Log("Custom Iron Pill saving timed out; ordinary magic priorities and spells restored for 900s.");
                return;
            }

            if (Character.rebirthTime.totalseconds < _customIronPillRetryAt)
                return;

            // Do not spend banked blood while the Blood Magic scene/state is unavailable.
            if (Character.bloodMagicController == null || Character.magic == null || Character.buttons?.bloodMagic == null
                || !Character.buttons.bloodMagic.interactable || Character.bloodMagic.ritual == null || Character.bloodMagic.ritual.Count == 0
                || Character.bloodMagicController.bloodMagics == null || Character.bloodMagicController.bloodMagics.Length == 0)
                return;

            if (!TryFlushCustomBloodToNumber())
            {
                _customIronPillRetryAt = Character.rebirthTime.totalseconds + 900d;
                Log("Custom Iron Pill saving postponed for 900s: NUMBER blood flush did not succeed.");
                return;
            }

            _customIronPillPrepStartedAt = Character.rebirthTime.totalseconds;
            Log($"Custom Iron Pill saving started: boss {Character.bossID}, cooldown remaining {IronPillCooldownRemainingSeconds():0}s.");
        }

        private bool TryFlushCustomBloodToNumber()
        {
            var blood = Character.bloodMagic.bloodPoints;
            if (double.IsNaN(blood) || double.IsInfinity(blood) || blood < 0d)
                return false;
            if (blood == 0d)
                return true;

            // NUMBER has no minimum in RebirthPowerSpell (unlike loot/gold/pill).
            // Call it directly: autoSpell() would split the pool across enabled spells.
            var number = Character.bloodMagic.rebirthPower;
            try
            {
                Character.bloodSpells.castRebirthSpell();
            }
            catch (Exception e)
            {
                LogError("Flushing blood to NUMBER", e);
                return false;
            }
            return Character.bloodMagic.bloodPoints == 0d && Character.bloodMagic.rebirthPower == number + blood;
        }

        private bool CustomRebirthReady(CustomPlan plan)
        {
            if (plan == null || !Settings.AutoRebirth || !Settings.UseCustomRebirthPlan)
                return false;

            try
            {
                return new CustomPlanRebirth(plan).PeekRebirthAvailable();
            }
            catch (Exception e)
            {
                LogError("Custom rebirth readiness check", e);
                return false;
            }
        }

        private void RunCustomBloodSpellToggles(CustomPlan plan)
        {
            UpdateCustomIronPillPrep(plan);
            ApplyCustomBloodSpellToggles(plan);
        }

        private void ApplyCustomBloodSpellToggles(CustomPlan plan)
        {
            if (!Settings.CustomAutoBloodSpellsEnabled || Character == null || Character.bloodMagic == null
                || Character.bloodSpells == null || Character.bloodMagicController == null)
                return;

            var ironPrep = CustomIronPillPrepActive();
            var plannedSeconds = plan == null ? 0 : CustomPlannedRebirthSeconds(plan);
            var shortRebirth = plannedSeconds > 0 && plannedSeconds <= 3600;
            var spaghetti = BloodGoalEstimate.WholePercent(Character.bloodMagicController.lootBonus());
            var counterfeit = BloodGoalEstimate.WholePercent(Character.bloodMagicController.goldBonus());

            var lootGoal = Settings.CustomBloodSpaghettiGoalPercent;
            var goldGoal = Settings.CustomBloodCounterfeitGoalPercent;
            var wantLoot = !ironPrep && !shortRebirth && lootGoal > 0 && spaghetti < lootGoal;
            var wantGold = !ironPrep && !shortRebirth && goldGoal > 0 && counterfeit < goldGoal;
            var wantNumber = !ironPrep && (shortRebirth || (!wantLoot && !wantGold));

            Character.bloodMagic.lootAutoSpell = wantLoot;
            Character.bloodMagic.goldAutoSpell = wantGold;
            Character.bloodMagic.rebirthAutoSpell = wantNumber;
            Character.bloodSpells.updateLootToggleState();
            Character.bloodSpells.updateGoldToggleState();
            Character.bloodSpells.updateRebirthToggleState();
        }

        private double CustomIronPillWorth()
        {
            var bonus = Character.settings.rebirthDifficulty >= difficulty.evil
                ? Character.adventureController.itopod.ironPillBonus() : 1f;
            return IronPillAutomation.Worth(Character.bloodMagic.bloodPoints, bonus);
        }

        private double IronPillCooldownRemainingSeconds()
        {
            if (Character == null || Character.bloodMagic?.adventureSpellTime == null || Character.bloodSpells == null)
                return double.PositiveInfinity;

            return Math.Max(0d, Character.bloodSpells.adventureSpellCooldown - Character.bloodMagic.adventureSpellTime.totalseconds);
        }

        private bool TryCastCustomIronPill()
        {
            if (IronPillCooldownRemainingSeconds() > 0)
                return false;

            if (Character.bloodMagic.bloodPoints < Character.bloodSpells.minAdventureBlood())
                return false;

            var entry = new IronPillHistoryEntry
            {
                CastAtUtc = DateTime.UtcNow.ToString("O"),
                WaitSeconds = Math.Max(0d, Character.rebirthTime.totalseconds - _customIronPillPrepStartedAt),
                Worth = CustomIronPillWorth(),
                RebirthSeconds = Character.rebirthTime.totalseconds
            };
            Character.bloodSpells.castAdventurePowerupSpell();
            // The game resets both on success; a rejected cast must not enter history.
            if (Character.bloodMagic.bloodPoints != 0d || Character.bloodMagic.adventureSpellTime.totalseconds != 0d)
                return false;

            Log($"Custom Iron Pill cast: waited {entry.WaitSeconds:0}s, worth {entry.Worth:0.###E+0} Power/Toughness.");
            try
            {
                Settings.AddIronPillHistory(entry);
            }
            catch (Exception e)
            {
                LogError("Saving Iron Pill history", e);
            }
            return true;
        }

        private string CustomIronPillWaitReason()
        {
            var cooldown = IronPillCooldownRemainingSeconds();
            if (cooldown > 0d)
                return $"cooldown {CustomStageAllocator.FormatSeconds((int) Math.Ceiling(cooldown))}";

            return $"blood {Character.bloodMagic.bloodPoints:0.###}/{Character.bloodSpells.minAdventureBlood():0.###}";
        }

        private bool CustomIronPillPrepActive()
        {
            return _customIronPillPrepStartedAt >= 0 && Character?.rebirthTime != null
                   && Character.rebirthTime.totalseconds >= _customIronPillPrepStartedAt
                   && !_manualRebirthPending && !_customPreRebirthPrepRunning && !BossPushRunning;
        }

        internal static bool IronPillBloodReserved => reference != null && reference.CustomIronPillPrepActive();

        private void ResetCustomIronPillPrep()
        {
            _customIronPillPrepStartedAt = -1d;
            _customIronPillPrepLastWaitLogAt = -1d;
        }

        private FightScoreMode GetActiveFightScoreMode()
        {
            try
            {
                if (CustomPlanStore != null)
                {
                    return (FightScoreMode) LoadRuntimeCustomPlan().FightScoreMode;
                }
            }
            catch
            {
                // Defaulting is safer than breaking combat routing for a cosmetic scoring preference.
            }

            return FightScoreMode.SqrtPowerToughness;
        }

        private static string ZoneName(int zone)
        {
            try
            {
                return $"{Character.adventureController.zoneName(zone)} ({zone})";
            }
            catch
            {
                return $"zone {zone}";
            }
        }

        private CustomPlan LoadRuntimeCustomPlan()
        {
            var planName = Settings.CustomPlanFile;
            if (_customRuntimePlanCache != null
                && _customRuntimePlanCacheName == planName
                && (DateTime.Now - _customRuntimePlanCacheLoadedAt).TotalSeconds < 2)
            {
                return _customRuntimePlanCache;
            }

            _customRuntimePlanCache = CustomPlanStore.Load(planName);
            _customRuntimePlanCacheName = planName;
            _customRuntimePlanCacheLoadedAt = DateTime.Now;
            return _customRuntimePlanCache;
        }

        internal void InvalidateCustomRuntimePlanCache(bool resetStageRuntime = true)
        {
            _customRuntimePlanCache = null;
            _customRuntimePlanCacheName = null;
            _customRuntimePlanCacheLoadedAt = DateTime.MinValue;
            _customStageKey = null;
            _customStageGearDone = false;
            CustomStageAllocator.ResetResourceTargets();
            if (resetStageRuntime) CustomStageAllocator.ResetStageRuntime();
            if (Settings != null && Settings.UseCustomRebirthPlan) ClearCustomSustainedFocus();
        }

        private double CurrentNumberMultiplier()
        {
            if (Character.attackMulti <= 0)
                return 0d;

            return Character.nextAttackMulti / Character.attackMulti;
        }

        private CustomRebirthHistoryEntry CaptureCustomRebirthHistory(string reason)
        {
            var before = Character.attackMulti;
            var after = Character.nextAttackMulti;
            return new CustomRebirthHistoryEntry
            {
                TimestampLocal = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                RebirthSeconds = Character.rebirthTime.totalseconds,
                NumberBefore = before,
                NumberAfter = after,
                NumberMultiplier = before > 0 ? after / before : 0d,
                BossId = Character.bossID,
                Reason = reason
            };
        }

        private string DescribeCustomRebirthReason(CustomPlan plan)
        {
            if (plan == null)
                return "custom";

            var multiplier = CurrentNumberMultiplier();
            var seconds = Character.rebirthTime.totalseconds;
            if (plan.NumberClimbEnabled && seconds >= plan.NumberClimbMinSeconds && multiplier >= plan.NumberClimbMultiplierTarget)
                return $"NUMBER climb x{multiplier:0.###} after {CustomStageAllocator.FormatSeconds((int) seconds)}";

            if (plan.NumberTriggerEnabled && multiplier >= plan.NumberMultiplierTarget)
                return $"NUMBER x{multiplier:0.###}";

            if (plan.TimeTriggerEnabled && seconds >= plan.TimeTriggerSeconds)
                return $"time {CustomStageAllocator.FormatSeconds((int) seconds)}";

            return "custom trigger";
        }

        private void SaveCustomRebirthHistory(string planName, CustomRebirthHistoryEntry entry)
        {
            try
            {
                CustomPlanStore?.AddRebirthHistory(planName, entry);
                settingsForm?.RefreshCustomHistory(Main.CustomPlanStore?.Load(planName));
            }
            catch (Exception e)
            {
                LogError("Save custom rebirth history", e);
            }
        }

        private void ResetCustomAutomationRuntime()
        {
            ResetCustomAdventureRuntime();
            _customStageKey = null;
            _customStageGearDone = false;
            _customStageFightKey = null;
            CustomStageAllocator.ResetResourceTargets();
            CustomStageAllocator.ResetStageRuntime();
            _customTitanGoldActive = false;
            _customTitanGoldIndex = -1;
            _customTitanGoldReadiness = null;
            _customTitanGoldStartedAt = DateTime.MinValue;
            _customTitanGoldUsesGoldGear = false;
            _customTitanFightActive = false;
            _customTitanFightIndex = -1;
            _customTitanFightReadiness = null;
            _customTitanFightStartedAt = DateTime.MinValue;
            _customIdleResourceWarningLastShown = DateTime.MinValue;
            ResetCustomFruitCapRetryCooldowns();
            ResetCustomIronPillPrep();
            _customIronPillRetryAt = 0d;
            CustomPlanRebirth.ResetRuntimeState();
            ClearCustomSustainedFocus();
            _customLastObservedRebirthSeconds = Character == null ? -1d : Math.Max(0d, Character.rebirthTime.totalseconds);
            SetCustomAutomationStatus("Custom automation reset after rebirth.");
        }
    }
}
