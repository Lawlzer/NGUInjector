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
        private bool _customEquipmentApplicationInvalidated;
        private string _customStageEquipmentKey;
        private string _customStageFightKey;
        private Func<bool> _customStageFightPreparationCurrent;
        private CustomPlan _customRuntimePlanCache;
        private string _customRuntimePlanCacheName;
        private DateTime _customRuntimePlanCacheLoadedAt = DateTime.MinValue;
        private string _customPrecachedLoadoutKey;
        private DateTime _customLastQuickResourceRefresh = DateTime.MinValue;
        private DateTime _customIdleResourceWarningLastShown = DateTime.MinValue;

        private const double CustomQuickResourceRefreshSeconds = 2d;
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
            root["activePresetId"] = string.Empty;
            root["appliedPreset"] = JSONNull.CreateOrGet();
            root["planVersion"] = CustomPlans.CustomPlanStore.CurrentPlanVersion;
            root["schedulerMode"] = "stages";
            root["activeStageIndex"] = -1;
            root["stageProgress"] = JSONNull.CreateOrGet();
            root["elapsedSeconds"] = 0;
            root["stageElapsedSeconds"] = 0;
            root["cycle"] = 0;
            root["repeating"] = false;
            root["loopStartIndex"] = -1;
            root["bloodGoalsPrioritized"] = false;
            root["bloodGoalsPriorityReason"] = string.Empty;
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
                    : snapshot.ScheduleComplete ? "Schedule complete."
                    : snapshot.ActiveStageIndex < 0 ? "Waiting for the allocator to initialize the scheduler." : string.Empty;
                if (status == "running" && snapshot.ActiveStageIndex < 0 && !snapshot.ScheduleComplete) status = "unavailable";
                root["available"] = true;
                root["status"] = status;
                root["reason"] = reason;
                root["planName"] = Settings.CustomPlanFile;
                root["activePresetId"] = plan.ActivePresetId ?? string.Empty;
                var appliedPreset = GlobalPresetAppliedSummary();
                root["appliedPreset"] = appliedPreset ?? (JSONNode)JSONNull.CreateOrGet();
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
                root["protectedWorkComplete"] = snapshot.ProtectedWorkComplete;
                root["protectedSecondsRemaining"] = snapshot.ProtectedSecondsRemaining;
                root["scheduleComplete"] = snapshot.ScheduleComplete;
                root["timingReason"] = snapshot.TimingReason;
                root["combatPrep"] = GetCombatPrepRuntime();
                root["cleanupRecoveryRequired"] = CombatPrepCleanupRecoveryRequired;
                if (CombatPrepCleanupRecoveryRequired) root["reason"] = ProtectedStageScheduler.CleanupRecoveryReason;
                var history = _protectedScheduler != null && _protectedPlanFile == Settings.CustomPlanFile
                    && _protectedScheduler.Matches(plan) && ProtectedEpochMatches() ? _protectedScheduler : null;
                root["stageEntry"] = history == null ? string.Empty
                    : _protectedGeneration + ":" + history.Entry.ToString(System.Globalization.CultureInfo.InvariantCulture);
                root["lastStageOverride"] = history?.LastOverride ?? string.Empty;
                root["lastStageOverrideAt"] = history?.LastOverrideAt ?? 0;
                var applied = history?.AppliedSeconds;
                var elapsedByStage = history?.ElapsedSecondsByStage;
                var visits = history?.Visits;
                var skipped = history?.Skipped;
                var completed = history?.Completed;
                root["bloodReservedForIronPill"] = CustomIronPillPrepActive();
                var catchUpBloodGoals = status == "running" && CustomBloodGoalCatchUpRequested(plan);
                var rows = root["stages"].AsArray;
                var presets = plan.Stages.Any(x => !string.IsNullOrEmpty(x.PresetId))
                    ? CustomPlanStore.PresetLibrary.List() : new CustomLoadoutPreset[0];
                for (var i = 0; i < plan.Stages.Length; i++)
                {
                    var stage = plan.Stages[i];
                    var row = new JSONObject();
                    var weighted = stage.ExecutionMode == (int)CustomStageExecutionMode.Standard && !string.IsNullOrEmpty(stage.PresetId);
                    var resolvedPresetId = StageGlobalPresetId(plan, stage);
                    var preset = presets.FirstOrDefault(x => x.Id == resolvedPresetId);
                    row["presetId"] = stage.PresetId ?? string.Empty;
                    row["resolvedPresetId"] = resolvedPresetId;
                    row["presetName"] = preset?.Name ?? string.Empty;
                    row["allocationMode"] = weighted ? "weighted-preset" : stage.ExecutionMode == 1 ? "automatic-combat-prep" : "legacy";
                    row["legacyAllocationsIgnored"] = weighted;
                    row["name"] = stage.Name;
                    row["index"] = i;
                    row["durationSeconds"] = snapshot.DurationSeconds[i];
                    row["completionMode"] = stage.CompletionMode;
                    row["executionMode"] = stage.ExecutionMode;
                    row["adventureDynamicWeight"] = stage.AdventureDynamicWeight;
                    row["configuredDurationSeconds"] = stage.DurationSeconds;
                    row["flexWeight"] = stage.Percent;
                    row["timingKind"] = stage.ExecutionMode == 1 ? "elapsed" : stage.CompletionMode == 1 ? "target" : stage.DurationSeconds > 0 ? "fixed" : "flexible";
                    row["appliedSeconds"] = applied == null ? (JSONNode) JSONNull.CreateOrGet() : new JSONNumber(applied[i]);
                    row["elapsedSeconds"] = elapsedByStage == null ? (JSONNode) JSONNull.CreateOrGet() : new JSONNumber(elapsedByStage[i]);
                    row["visits"] = visits == null ? (JSONNode) JSONNull.CreateOrGet() : new JSONNumber(visits[i]);
                    row["state"] = history == null ? "pending" : skipped[i] ? "skipped"
                        : i == snapshot.ActiveStageIndex && !snapshot.ScheduleComplete ? "current" : completed[i] ? "completed" : "pending";
                    row["energyFocus"] = weighted ? (JSONNode)JSONNull.CreateOrGet() : new JSONNumber(stage.EnergyFocus);
                    row["magicFocus"] = weighted ? (JSONNode)JSONNull.CreateOrGet() : new JSONNumber(stage.MagicFocus);
                    row["gearGoal"] = weighted ? (JSONNode)JSONNull.CreateOrGet() : new JSONNumber(stage.GearGoal);
                    row["configuredEnergyPriorities"] = ToJsonArray(weighted ? new string[0] : stage.EnergyPriorities ?? new string[0]);
                    row["configuredMagicPriorities"] = ToJsonArray(weighted ? new string[0] : stage.MagicPriorities ?? new string[0]);
                    var active = status == "running" && i == snapshot.ActiveStageIndex;
                    var rowOwner = history != null && i == snapshot.ActiveStageIndex
                        ? $"stage:{Settings.CustomPlanFile}:{plan.Name}:{i}:{_protectedGeneration}:{history.Entry}:{snapshot.Cycle}" : string.Empty;
                    var eventKey = rowOwner + ":" + resolvedPresetId;
                    var hasAppliedPreset = weighted && rowOwner.Length > 0 && appliedPreset != null && appliedPreset["owner"].Value == rowOwner;
                    var affectedExecution = rowOwner.Length > 0 && rowOwner == _resourceExecutionOwner
                        && !string.IsNullOrEmpty(_resourceExecutionReason);
                    var executionBlocked = affectedExecution && (_resourceExecutionState == ResourceExecutionState.NeedsUser
                        || _resourceExecutionState == ResourceExecutionState.Failed);
                    var executionWaiting = affectedExecution && _resourceExecutionState == ResourceExecutionState.Waiting;
                    row["appliedPreset"] = hasAppliedPreset ? (JSONNode)appliedPreset : JSONNull.CreateOrGet();
                    row["presetStatus"] = !weighted ? "not-used" : _presetExecutionBlock?.Event == eventKey ? "failed"
                        : executionBlocked ? "blocked" : executionWaiting || _globalPresetWaitingEvent == eventKey ? "waiting" : hasAppliedPreset
                            ? appliedPreset["active"].AsBool ? "applied" : "displaced" : "pending";
                    row["presetReason"] = _presetExecutionBlock?.Event == eventKey ? _globalPresetFailure ?? string.Empty
                        : executionBlocked || executionWaiting ? _resourceExecutionReason
                        : _globalPresetWaitingEvent == eventKey ? _globalPresetWaitingReason ?? string.Empty
                        : rowOwner == _resourceExecutionOwner ? _resourceExecutionReason ?? string.Empty : string.Empty;
                    row["presetRetryInSeconds"] = _globalPresetWaitingEvent == eventKey ? Math.Max(0, _globalPresetRetryAt - LoadoutPresetClock) : 0;
                    var bloodGoalsPrioritized = active && !weighted && CustomStageAllocator.BloodGoalsPrioritized(stage, catchUpBloodGoals);
                    row["bloodGoalsPrioritized"] = bloodGoalsPrioritized;
                    if (bloodGoalsPrioritized)
                    {
                        root["bloodGoalsPrioritized"] = true;
                        root["bloodGoalsPriorityReason"] = "Unmet Spaghetti or Counterfeit Gold goals; casual Energy NGU magic prioritizes Blood Magic.";
                    }
                    var remaining = active ? (int) Math.Max(0, snapshot.DurationSeconds[i] - Math.Floor(snapshot.StageElapsedSeconds)) : 0;
                    row["energyPriorities"] = ToJsonArray(weighted ? new string[0] : CustomStageAllocator.EffectiveStagePriorities(stage, true, remaining,
                        active ? snapshot.DurationSeconds[i] : 0, out _));
                    row["magicPriorities"] = ToJsonArray(weighted ? new string[0] : CustomStageAllocator.EffectiveStagePriorities(stage, false, remaining,
                        active ? snapshot.DurationSeconds[i] : 0, out _, active && CustomIronPillPrepActive(), active && catchUpBloodGoals));
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
                var root = BlackBarAllocator.GetNguRuntime();
                if (_customSustainedFocusGoal.HasValue)
                {
                    var plan = LoadRuntimeCustomPlan();
                    var stage = FocusSetupStage(_customSustainedFocusGoal.Value);
                    var catchUpBloodGoals = CustomBloodGoalCatchUpRequested(plan);
                    root["bloodGoalsPrioritized"] = CustomStageAllocator.BloodGoalsPrioritized(stage, catchUpBloodGoals);
                    root["effectiveMagicPriorities"] = ToJsonArray(CustomStageAllocator.EffectiveStagePriorities(stage, false, 0, 0, out _, IronPillBloodReserved, catchUpBloodGoals));
                    foreach (var energy in new[] {true, false})
                    {
                        var applied = energy ? _customFocusEnergyNguWeights : _customFocusMagicNguWeights;
                        var configured = energy ? Settings.EnergyNguWeights : Settings.MagicNguWeights;
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

            if (_resourceExecutionState == ResourceExecutionState.Waiting || _resourceExecutionState == ResourceExecutionState.NeedsUser
                || _resourceExecutionState == ResourceExecutionState.Failed) return;

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
            if (_customSustainedFocusGoal.HasValue || _globalPresetManualId != null || CustomIronPillPrepActive())
                return true;

            return (plan != null && Settings.UseCustomRebirthPlan)
                   || Settings.CustomAdventureEnabled
                   || Settings.ManualAdventureEnabled
                   || Settings.ManualTitanEnabled;
        }

        private void RunCustomPlanAutomation(CustomPlan plan)
        {
            if (!ReferenceEquals(reference, this) || _unloaded) return;
            ServiceAutomaticRecovery();
            if (CombatPrepActive)
            {
                SuspendProtectedStageProgress();
                return;
            }
            SetResourceExecution(ResourceExecutionState.Waiting, _customStageEquipmentKey, "");
            if (!ExecutionClaimsActive && (_manualRebirthPending || _customPreRebirthPrepRunning || BossPushRunning || CustomBossFightActive()))
            {
                SuspendProtectedStageProgress();
                return;
            }

            if (plan == null || !Settings.UseCustomRebirthPlan || !plan.StageSchedulerEnabled)
            {
                SuspendProtectedStageProgress();
                SetResourceExecution(ResourceExecutionState.Stopped, "", "");
                return;
            }

            if (_customPreRebirthPrepCompleted && CustomRebirthReady(plan))
                return;

            var runtime = CustomStageAllocator.GetActiveStage(plan);
            if (runtime == null)
            {
                if (CombatPrepCleanupRecoveryRequired || _protectedScheduler != null && !_protectedCheckpointDurable
                    && _automaticRecoveryRetryEntry == AutomaticRecoveryEntry)
                {
                    SetResourceExecution(ResourceExecutionState.Failed, "automatic-recovery",
                        "Automatic checkpoint retry is pending; check runtime directory permissions or file locks. The original entry remains uncompleted.");
                    return;
                }
                SetResourceExecution(ResourceExecutionState.Stopped, "", "");
                return;
            }

            if (runtime.Stage.ExecutionMode == (int) CustomStageExecutionMode.AutoAugmentsWandoos)
            {
                StartAutomaticCombatPrep(plan, runtime);
                return;
            }

            if (string.IsNullOrEmpty(runtime.Stage.PresetId)
                && CustomStageAllocator.UnmanagedMagicBlocksAt(runtime.Stage, runtime.RemainingSeconds, CustomStageDurationSeconds(runtime)))
            {
                SuspendProtectedStageProgress();
                SetCustomAutomationStatus(CustomStageAllocator.PrepareAtEntry(runtime.Stage, runtime.RemainingSeconds, CustomStageDurationSeconds(runtime)));
                return;
            }

            var stageKey = CustomStageRuntimeKey(plan, runtime);
            var cycle = CustomStageAllocator.GetSchedulerSnapshot(plan, Character.rebirthTime.totalseconds).Cycle;
            var equipmentKey = $"stage:{Settings.CustomPlanFile}:{stageKey}:{cycle}";
            if (_customStageEquipmentKey != equipmentKey)
            {
                if (!RetirePreviousGlobalPresetStageRoute(equipmentKey)) return;
                _customStageEquipmentKey = equipmentKey;
                InvalidateInlineStageApplication();
                ResetCustomEquipmentSetup();
            }
            if (_customStageKey != stageKey)
            {
                _customStageKey = stageKey;
                InvalidateInlineStageApplication();
                ResetCustomFruitCapRetryCooldowns();
                CustomStageAllocator.ResetResourceTargets();
                Log($"Custom stage active: {runtime.Message}");
            }

            if (!string.IsNullOrEmpty(runtime.Stage.PresetId))
            {
                var restorationIssue = ProtectedPresetRestorationIssue(equipmentKey);
                if (restorationIssue != null)
                {
                    SuspendProtectedStageProgress();
                    SetResourceExecution(ResourceExecutionState.NeedsUser, equipmentKey, restorationIssue);
                    return;
                }
            }
            if (TryRunCustomStageEntryFight(plan, runtime)) return;

            if (!string.IsNullOrEmpty(runtime.Stage.PresetId))
            {
                if (!RunGlobalPresetStage(plan, runtime, equipmentKey)) return;
                if (!TryActivateCustomFruits(plan, runtime, out var presetFruitMessage))
                    SuspendProtectedStageProgress();
                if (!string.IsNullOrEmpty(presetFruitMessage)) SetCustomAutomationStatus(presetFruitMessage);
                return;
            }

            if (ManualAdventureOwnsEquipment && !ManualAdventureOverlayActive && !ExecutionSetupClaimsActive) return;
            var setupVerified = ReconcileInlineStageSetup(plan, runtime);
            if (!setupVerified && !InlineStageApplicationObserved(plan) && !ExecutionSetupClaimsActive) return;

            var messages = new List<string>();
            string fruitActivation = null;
            if (setupVerified && !TryActivateCustomFruits(plan, runtime, out fruitActivation))
            {
                SuspendProtectedStageProgress();
                SetCustomAutomationStatus($"{runtime.Message}; {fruitActivation}");
                return;
            }

            if (!string.IsNullOrWhiteSpace(fruitActivation))
                messages.Add(fruitActivation);

            if (setupVerified && !InlineStageSetupVerified(plan) && !ReconcileInlineStageSetup(plan, runtime)) return;
            SuspendProtectedStageProgress();
            var resources = CustomStageAllocator.ResolveStageResources(plan, runtime.Stage, runtime.RemainingSeconds,
                stageKey, CustomStageDurationSeconds(runtime));
            try { messages.Add(CustomStageAllocator.ApplyStageResources(resources)); }
            catch (ResourceMutationException error)
            {
                SetResourceExecution(error.Result.Disposition == MutationDisposition.RejectedUnchanged
                    ? ResourceExecutionState.Waiting : ResourceExecutionState.NeedsUser, stageKey, error.Message);
                return;
            }
            CommitProtectedStageApplication(plan);
            var allocation = string.Join("; ", messages.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray());
            var verified = InlineStageSetupVerified(plan) && CustomStageAllocator.LastStageApplicationSucceeded;
            var resourceIssue = CustomStageAllocator.LastStageResourceOutcome?.Disposition;
            SetResourceExecution(verified ? ResourceExecutionState.Applied
                    : resourceIssue == MutationDisposition.ForeignConflict || resourceIssue == MutationDisposition.UnknownOutcome
                        ? ResourceExecutionState.NeedsUser : ResourceExecutionState.Waiting,
                stageKey, verified ? "" : allocation);
            SetCustomAutomationStatus(BuildRuntimeStatus(plan, runtime, allocation));
        }

        private bool TryRunCustomStageEntryFight(CustomPlan plan, CustomStageRuntime runtime)
        {
            if (!runtime.Stage.FightOnEntry) return false;
            if (!Settings.UseCustomRebirthPlan || _protectedScheduler == null || _protectedScheduler.Finished
                || _protectedPlanFile != Settings.CustomPlanFile || !_protectedScheduler.Matches(plan) || !ProtectedEpochMatches()
                || runtime.StageIndex != _protectedScheduler.StageIndex
                || runtime.EntryIdentity != _protectedGeneration + ":" + _protectedScheduler.Entry.ToString(System.Globalization.CultureInfo.InvariantCulture))
                return false;
            // Deadline edits do not create another entry; retain the existing persisted receipt key.
            var fightKey = $"{Settings.CustomPlanFile}:{CustomStageRuntimeKey(plan, runtime)}";
            var retry = _customStageFightKey == fightKey;
            if (retry && _customStageFightPreparationCurrent?.Invoke() != true) return false;
            if (ManualAdventureOwnsEquipment)
            {
                SuspendProtectedStageProgress();
                SetCustomAutomationStatus($"{runtime.Message}; fight on entry waiting for manual adventure equipment ownership.");
                return true;
            }
            if (!retry && ExecutionOwnsDomain(ExecutionDomain.Gear | ExecutionDomain.Support | ExecutionDomain.Route
                | ExecutionDomain.FiniteAction | ExecutionDomain.Inventory))
            {
                SuspendProtectedStageProgress();
                SetCustomAutomationStatus($"{runtime.Message}; fight on entry waiting for preparation ownership.");
                return true;
            }
            var previousFightKey = _customStageFightKey;
            _customStageFightKey = fightKey;
            SuspendProtectedStageProgress();
            var receiptSaved = SaveProtectedStageProgress(true);
            if (!receiptSaved)
            {
                _customStageFightKey = previousFightKey;
                SetCustomAutomationStatus($"{runtime.Message}; fight on entry waiting for a durable entry receipt");
                return true;
            }
            InvalidateInlineStageApplication();
            var epoch = new LoadoutManager.SnapshotEpoch();
            var scheduler = _protectedScheduler; var entry = ExecutionPersistenceEntry; var file = Settings.CustomPlanFile;
            var requested = ActionIdentity("fight-preparation", "fight-preparation", fightKey);
            try { SetCustomAutomationStatus(RunFightPrep(plan, false, false, $"{runtime.Message}; fight on entry", opportunity: fightKey)); }
            finally
            {
                if (!retry)
                {
                    // The durable receipt prevents replay after reload. Only this
                    // live, unstarted record may resume its original setup attempt.
                    var preparation = _fightPreparationRecord;
                    _customStageFightPreparationCurrent = () => epoch.Matches() && ReferenceEquals(scheduler, _protectedScheduler)
                        && entry == ExecutionPersistenceEntry && Settings.UseCustomRebirthPlan && Settings.CustomPlanFile == file
                        && ReferenceEquals(preparation, _fightPreparationRecord) && preparation?.Identity.Key == requested.Key
                        && preparation.State == FiniteActionState.Prepared && !_fightPreparationStarted
                        && (preparation.After == "acquired" || preparation.After == "selected" || preparation.After == "setup-ready");
                }
                if (runtime.Stage.ExecutionMode == (int) CustomStageExecutionMode.AutoAugmentsWandoos)
                    SaveProtectedStageProgress(true); // Include synchronous/failed setup time, even with a stalled game clock.
            }
            return true;
        }

        private void RunQuickCustomResourceRefresh(CustomPlan plan)
        {
            ReconcileCustomResources(plan);
        }

        private bool CustomQuickResourceRefreshBlocked(CustomPlan plan)
        {
            return CombatPrepActive || !ExecutionClaimsActive && (_manualRebirthPending
                   || _customPreRebirthPrepRunning
                   || BossPushRunning
                   || CustomBossFightActive()
                   || CustomRebirthReady(plan));
        }

        private static bool CustomBossFightActive()
        {
            var boss = Character?.bossController;
            return boss != null && (boss.isFighting || boss.nukeBoss);
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

            if (runtime.Stage.ExecutionMode != (int) CustomStageExecutionMode.Standard)
                return false;

            stageKey = CustomStageRuntimeKey(plan, runtime);
            if (_customStageKey != stageKey)
                return false;

            if (_customEquipmentApplicationInvalidated || runtime.Stage.FightOnEntry
                && _customStageFightKey != $"{Settings.CustomPlanFile}:{stageKey}")
                return false;

            return !string.IsNullOrEmpty(runtime.Stage.PresetId)
                ? GlobalPresetStageReceiptValid(plan) : InlineStageSetupVerified(plan);
        }

        private static string CustomStageRuntimeKey(CustomPlan plan, CustomStageRuntime runtime)
        {
            var planName = plan == null ? string.Empty : plan.Name;
            return runtime == null ? planName : $"{planName}:{runtime.StageIndex}:{runtime.EntryIdentity}";
        }

        private static int CustomStageDurationSeconds(CustomStageRuntime runtime)
        {
            return runtime == null ? 0 : runtime.EffectiveDurationSeconds;
        }

        private CustomAutomationGoal CurrentCustomGoal(CustomPlan plan)
        {
            if (_manualRebirthPending)
                return CustomAutomationGoal.ManualRebirth;

            if (_customPreRebirthPrepRunning)
                return CustomAutomationGoal.PreRebirthPrep;

            if (BossPushRunning || CustomBossFightActive())
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
            if (Settings.CustomAdventureGoldPush)
                yield return GearOptimizationGoal.AdventureGold;
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

    }
}
