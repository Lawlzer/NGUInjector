using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;
using SimpleJSON;

namespace NGUInjector
{
    internal partial class Main
    {
        private string _automaticStageSetupEntry;
        private double _automaticStageSetupStarted;
        private JSONObject _automaticAtContinuation;
        private string _automaticSafeResourcesEntry, _automaticRecoveryRetryEntry, _automaticCancelledEntry;
        private double _automaticRecoveryRetryAt;
        private string _automaticAcquisitionRetryEntry;
        private double _automaticAcquisitionRetryAt;
        private bool _automaticStageAcquiring;

        private string AutomaticRecoveryEntry => _protectedPlanFile + ":" + _protectedGeneration + ":" + _protectedScheduler.Entry.ToString(CultureInfo.InvariantCulture);
        private bool AutomaticContinuationPending => _protectedScheduler != null
            && (_automaticSafeResourcesEntry == AutomaticRecoveryEntry
                || _automaticAtContinuation?["entry"].Value == AutomaticRecoveryEntry && !_protectedScheduler.AutoAtCompleted);

        private bool ValidAutomaticAtContinuation(JSONObject saved, string entry)
        {
            long energy, magic;
            return saved != null && saved["entry"].Value == entry
                && saved["energyTarget"].IsString && long.TryParse(saved["energyTarget"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out energy) && energy >= 0
                && saved["magicTarget"].IsString && long.TryParse(saved["magicTarget"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out magic) && magic >= 0
                && FiniteCheckpointNumber(saved["weight"], true) && saved["weight"].AsDouble <= 1
                && FiniteCheckpointNumber(saved["phaseStart"], true) && FiniteCheckpointNumber(saved["phaseEnd"], true)
                && saved["phaseStart"].AsDouble <= saved["phaseEnd"].AsDouble
                && saved["phaseEnd"].AsDouble <= _protectedScheduler.ExpectedEndSeconds - _protectedScheduler.EnteredAt + .1;
        }

        // Only automation callbacks service recovery. Telemetry must never acquire or retire owners.
        private void ServiceAutomaticRecovery()
        {
            if (!ReferenceEquals(reference, this) || _unloaded) return;
            // Clean cleanup still leaves an interrupted entry, never permission for fresh paid prep.
            if (_protectedScheduler != null && _automaticCancelledEntry == AutomaticRecoveryEntry)
            {
                _automaticSafeResourcesEntry = AutomaticRecoveryEntry;
                _automaticCancelledEntry = null;
            }
            if (CombatPrepRunActive && !_resourceReconciliationRunning) { ReconcileCustomResources(); return; }
            if (CombatPrepRunActive) AdvanceCombatPrep();
            if (CombatPrepRunActive || !CombatPrepCleanupRecoveryRequired || _protectedScheduler == null
                || !_protectedScheduler.Matches(_protectedPlan)) return;
            var entry = AutomaticRecoveryEntry;
            if (_automaticRecoveryRetryEntry == entry && CombatPrepClock < _automaticRecoveryRetryAt) return;
            _automaticRecoveryRetryEntry = entry; _automaticRecoveryRetryAt = CombatPrepClock + 1;
            // A successful earlier checkpoint is not evidence that THIS marker transition was saved.
            if (!TryCommitProtectedEntryChange(scheduler => scheduler.AutoCleanupRecoveryRequired = false))
            {
                SetResourceExecution(ResourceExecutionState.Failed, entry,
                    "Automatic recovery checkpoint could not be saved; check runtime directory permissions or file locks. Retrying automatically.");
                return;
            }
            _protectedRecoveryReason = _automaticSafeResourcesEntry == entry
                ? "Conservative automatic continuation: safe background resources only until the original deadline; unknown AT targets are not marked complete."
                : "Historical restoration ownership retired; current intent resumes under the original deadline.";
            _customStageKey = null; _customStageEquipmentKey = null; InvalidateInlineStageApplication();
            _customEquipmentApplicationInvalidated = true;
            _customLastQuickResourceRefresh = DateTime.MinValue;
            _resourceDemandIdentity = null;
        }

        private bool AutomaticStageSetupBudgetAvailable(CustomStageRuntime runtime)
        {
            var entry = Settings.CustomPlanFile + ":" + runtime.EntryIdentity;
            var started = CombatPrepClock - Math.Max(0, Character.rebirthTime.totalseconds - _protectedScheduler.EnteredAt);
            _automaticStageSetupStarted = _automaticStageSetupEntry == entry ? Math.Min(_automaticStageSetupStarted, started) : started;
            _automaticStageSetupEntry = entry;
            if (CombatPrepClock - _automaticStageSetupStarted < _protectedScheduler.ExpectedEndSeconds - _protectedScheduler.EnteredAt)
                return true;
            if (_automaticSafeResourcesEntry == AutomaticRecoveryEntry
                && Character.rebirthTime.totalseconds < _protectedScheduler.ExpectedEndSeconds)
            {
                SetCustomAutomationStatus("Automatic safe-background continuation is waiting for its original deadline; no fresh preparation budget was acquired.");
                return false;
            }
            CompleteAutomaticEntryDurably();
            SetCustomAutomationStatus("Scheduled Augments + Wandoos original elapsed budget exhausted during entry setup; no fresh run acquired.");
            return false;
        }

        private bool CompleteAutomaticEntryDurably()
        {
            var entry = AutomaticRecoveryEntry;
            if (TryCommitProtectedEntryChange(scheduler => scheduler.CompleteAutomaticEntry(Character.rebirthTime.totalseconds))) return true;
            _automaticRecoveryRetryEntry = entry;
            _automaticRecoveryRetryAt = CombatPrepClock + 1;
            return false;
        }

        private static readonly System.Reflection.FieldInfo[] CombatPrepEquipmentMaterialFields = typeof(Equipment)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(x => x.FieldType.IsPrimitive || x.FieldType.IsEnum).OrderBy(x => x.Name).ToArray();

        private static Dictionary<Equipment, string> CaptureCombatPrepEquipmentMaterial()
        {
            // Reference-keyed and slot-independent: our own phase swaps are not material changes.
            // Native numeric fields cover level/boost/cap changes and equippability; cosmetic paths do not.
            return Character.inventory.GetConvertedEquips().Concat(Character.inventory.GetConvertedInventory())
                .Where(x => x.equipment != null && x.id >= 40 && x.equipment.isEquipment())
                .Select(x => x.equipment).Distinct().ToDictionary(item => item, item => string.Join("|",
                    CombatPrepEquipmentMaterialFields.Select(field => {
                        var value = field.GetValue(item);
                        return value is float ? ((float) value).ToString("R", CultureInfo.InvariantCulture)
                            : value is double ? ((double) value).ToString("R", CultureInfo.InvariantCulture)
                            : Convert.ToString(value, CultureInfo.InvariantCulture);
                    }).ToArray()));
        }

        private static bool CombatPrepHasNativeWandoosWork()
        {
            var w = Character.wandoos98;
            return w.energyLevel > 0 || w.magicLevel > 0 || w.energyProgress > 0 || w.magicProgress > 0
                || w.wandoosEnergy > 0 || w.wandoosMagic > 0;
        }

        private Action CaptureCombatPrepMaterialRevision()
        {
            var run = _combatPrepRun;
            if (run?.Automatic != true || run.PhaseIndex < 0 || run.BlockedReason != null
                || !CombatPrepRequirementsVerified(run, run.Policy.Phases[run.PhaseIndex])) return null;
            var setup = run.Setup;
            var accounting = ResourceWriter.AllocatedResourceKey();
            var objects = ResourceWriter.CaptureResourceContext(Character);
            return () => {
                try
                {
                    if (!ReferenceEquals(_combatPrepRun, run) || !ReferenceEquals(run.Setup, setup)
                        || !CombatPrepPhaseAuthority(run, run.Policy.Phases[run.PhaseIndex])
                        || !CombatPrepGearMatches(run.ExpectedGear) || !CombatPrepSupportStillOwned(run)
                        || ResourceWriter.AllocatedResourceKey() != accounting) return;
                    var current = ResourceWriter.CaptureResourceContext(Character);
                    if (current.Length != objects.Length || current.Where((item, i) => !ReferenceEquals(item, objects[i])).Any()) return;
                    var material = CaptureCombatPrepEquipmentMaterial();
                    var instances = LoadoutPresetInstances().Values.ToArray();
                    if (setup.Gear.Any(item => item.id != item.equipment.id || item.id != 0
                        && (!material.ContainsKey(item.equipment) || instances.Count(x => ReferenceEquals(x, item.equipment)) != 1))) return;
                    if (material.Count == run.EquipmentMaterial.Count && material.All(pair =>
                        run.EquipmentMaterial.TryGetValue(pair.Key, out var value) && value == pair.Value)) return;
                    run.EquipmentMaterial = material;
                    run.SetupMaterial = setup.Gear.Select(x => LoadoutPresetPrimitiveKey(x.equipment)).ToArray();
                    run.ExpectedMaterial = run.ExpectedGear.Select(x => LoadoutPresetPrimitiveKey(x.equipment)).ToArray();
                    run.EquipmentReplanPending = true;
                }
                catch { /* Unverifiable effects never grant new material or selection authority. */ }
            };
        }

        private void RefreshAutomaticAtResources(CombatPrepRun run, CombatPrepPhase phase)
        {
            // Keep the exact acquired AT policy/targets. Its executor already reads native AT rates;
            // only refresh observed resources/caps/power without searching or swapping support.
            var profile = new[] { run.Inputs.Snapshot.Current }.Concat(run.Inputs.Snapshot.Profiles).Single(x => x.Key == phase.GearProfileKey);
            var basic = Character.training.attackEnergy.Concat(Character.training.defenseEnergy).Aggregate(0m, (sum, x) => sum + Math.Max(0, x));
            var cap = Character.totalCapEnergy();
            if (cap < basic) throw new InvalidOperationException("Changed equipment cannot hold reserved basic training energy.");
            profile.EnergyCap = cap; profile.MagicCap = Character.totalCapMagic();
            profile.EnergyPower = Character.totalEnergyPower(); profile.MagicPower = Character.totalMagicPower();
            profile.AtSpeed = Character.totalAdvancedTrainingSpeedBonus();
            profile.EnergyTotal = (long)Math.Max(0m, Math.Min((decimal)Math.Max(Character.curEnergy, Character.idleEnergy), cap) - basic);
            profile.MagicTotal = Math.Max(0, Math.Min(profile.MagicCap, Math.Max(Character.magic.curMagic, Character.magic.idleMagic)));
        }

        internal void ValidateScheduledPlanEdit(string planName, CustomPlan plan)
        {
            if ((CombatPrepRunActive && _combatPrepRun.Automatic && _combatPrepRun.OwnerPlan == planName
                || CombatPrepCleanupRecoveryRequired && _protectedPlanFile == planName)
                && (_protectedScheduler == null || !_protectedScheduler.Matches(plan)))
                throw new InvalidOperationException("Cancel the scheduled Augments + Wandoos run before changing stage structure or execution modes. Its original deadline cannot be reset by an active edit.");
        }

        private bool TryStartAutomaticStage(CustomPlan plan)
        {
            if (plan == null || !Settings.UseCustomRebirthPlan || !plan.StageSchedulerEnabled
                || !plan.Stages.Any(x => x.ExecutionMode == (int) CustomStageExecutionMode.AutoAugmentsWandoos)
                || _manualRebirthPending || _customPreRebirthPrepRunning || BossPushRunning || CustomBossFightActive()
                || _customRequestedFocusGoal.HasValue) return false;
            var runtime = CustomStageAllocator.GetActiveStage(plan);
            if (runtime == null || runtime.Stage.ExecutionMode != (int) CustomStageExecutionMode.AutoAugmentsWandoos
                || _protectedScheduler.Finished) return false;
            StartAutomaticCombatPrep(plan, runtime);
            if (CombatPrepActive) return true;
            var current = CustomStageAllocator.GetActiveStage(plan);
            return current != null && current.Stage.ExecutionMode == (int) CustomStageExecutionMode.AutoAugmentsWandoos && !_protectedScheduler.Finished;
        }

        private bool AutomaticCombatPrepOwnerMatches(CombatPrepRun run)
        {
            if (!ReferenceEquals(reference, this) || _unloaded || Settings == null || !Settings.UseCustomRebirthPlan || Settings.CustomPlanFile != run.OwnerPlan
                || _customRequestedFocusGoal.HasValue || _protectedScheduler == null || _protectedScheduler.Finished
                || _protectedPlanFile != run.OwnerPlan || !ProtectedEpochMatches()
                || _protectedScheduler.StageIndex != run.OwnerStage
                || _protectedGeneration + ":" + _protectedScheduler.Entry.ToString(CultureInfo.InvariantCulture) != run.OwnerEntry)
                return false;
            // Read the selected file, not a cached preview or the ordinary runtime-plan cache.
            var plan = CustomPlanStore?.Load(run.OwnerPlan);
            return plan?.SourceJson != null && plan.StageSchedulerEnabled && _protectedScheduler.Matches(plan)
                && plan.Stages[run.OwnerStage].ExecutionMode == (int) CustomStageExecutionMode.AutoAugmentsWandoos;
        }

        private void StartAutomaticCombatPrep(CustomPlan plan, CustomStageRuntime runtime)
        {
            if (_automaticStageAcquiring || !ReferenceEquals(reference, this) || _unloaded || _protectedScheduler == null || _protectedScheduler.Finished) return;
            if (_automaticCancelledEntry == AutomaticRecoveryEntry)
            {
                _automaticSafeResourcesEntry = AutomaticRecoveryEntry;
                _automaticCancelledEntry = null;
            }
            if (_automaticAcquisitionRetryEntry == AutomaticRecoveryEntry && CombatPrepClock < _automaticAcquisitionRetryAt)
            {
                SetResourceExecution(ResourceExecutionState.Failed, AutomaticRecoveryEntry,
                    "Automatic acquisition retry is pending: " + (_combatPrepRun?.Reason ?? "Check current resource and equipment availability."));
                return;
            }
            if (CombatPrepCleanupRecoveryRequired)
            {
                SetCustomAutomationStatus(ProtectedStageScheduler.CleanupRecoveryReason);
                return;
            }
            if (!AutomaticStageSetupBudgetAvailable(runtime)) return;
            // Both quick and ordinary dispatch use this guard; waiting spends only elapsed budget.
            if (ManualAdventureOwnsEquipment || Controller.midDrag || ExecutionOwnsDomain(ExecutionDomain.FiniteAction))
            {
                SuspendProtectedStageProgress();
                SetCustomAutomationStatus("Scheduled Augments + Wandoos waiting for equipment ownership; its elapsed deadline is unchanged.");
                return;
            }
            if (_manualRebirthPending || _customPreRebirthPrepRunning || BossPushRunning || CustomBossFightActive()) return;
            if (_protectedScheduler.Durations[runtime.StageIndex] > CustomPlanStage.MaximumAutomaticDurationSeconds)
            {
                SetCustomAutomationStatus("Scheduled Augments + Wandoos validation blocked: effective budget exceeds 86400 seconds; reduce the duration or flexible allocation and restart this stage. No run was acquired.");
                return;
            }
            var run = new CombatPrepRun {
                Id = Guid.NewGuid().ToString("N"), OwnerPlan = Settings.CustomPlanFile, OwnerEntry = runtime.EntryIdentity,
                OwnerStage = runtime.StageIndex, Label = "Augments + Wandoos", Policy = new CombatPrepAlternative(),
                ReplanReason = "stage-start",
                Started = _automaticStageSetupStarted,
                Duration = Math.Max(0, _protectedScheduler.ExpectedEndSeconds - _protectedScheduler.EnteredAt),
                DeadlineRebirthSeconds = _protectedScheduler.ExpectedEndSeconds
            };
            if (!AutomaticCombatPrepOwnerMatches(run)) return;
            // Fruit reservation/release may re-enter dispatch before this run is published.
            _automaticStageAcquiring = true;
            try
            {
                var blocker = CombatPrepStartBlocker(run);
                if (blocker != null) throw new InvalidOperationException(blocker);
                if (ServiceStageFightSchedule(plan)) return;
                // Preserve AutoActivateFruits before capture without applying an ordinary stage's gear/support policy.
                if (!TryActivateCustomFruits(plan, null, out var fruitActivation))
                {
                    SuspendProtectedStageProgress();
                    SetCustomAutomationStatus($"{runtime.Message}; {fruitActivation}");
                    return;
                }
                if (!AutomaticCombatPrepOwnerMatches(run)) return;
                SaveProtectedStageProgress(true);
                if (!AutomaticStageSetupBudgetAvailable(runtime)) return;
                run.Started = _automaticStageSetupStarted;
                blocker = CombatPrepStartBlocker(run);
                if (blocker != null) throw new InvalidOperationException(blocker);
                if (!AutomaticCombatPrepOwnerMatches(run)) throw new InvalidOperationException("Scheduled stage is no longer current/enabled.");
                if (!SaveProtectedStageProgress(true)) throw new InvalidOperationException("Scheduled deadline must be durable before acquiring resources.");
                run.Capture = CaptureScheduledCombatPrep(run,
                    _automaticAtContinuation?["entry"].Value == AutomaticRecoveryEntry && !_protectedScheduler.AutoAtCompleted);
                run.ExpectedSupport = run.Capture.OriginalSupportState;
                run.ExpectedGear = run.Capture.OriginalGear;
                run.EnergyPool = run.Capture.Snapshot.Current.EnergyTotal;
                run.MagicPool = run.Capture.Snapshot.Current.MagicTotal;
                _protectedScheduler.AutoEnergyPool = run.EnergyPool;
                _protectedScheduler.AutoMagicPool = run.MagicPool;
                if (_automaticAtContinuation?["entry"].Value == AutomaticRecoveryEntry && CombatPrepHasNativeWandoosWork())
                {
                    // Inspect allocations before Begin recalls them. A restored gate cannot authorize paid AT after native W work.
                    _automaticSafeResourcesEntry = AutomaticRecoveryEntry;
                    _protectedRecoveryReason = "Native Wandoos work conflicts with unresolved AT; only safe background resources will run until the original deadline. The AT gate remains unresolved.";
                }
                if (_automaticAtContinuation?["entry"].Value == AutomaticRecoveryEntry && !_protectedScheduler.AutoAtCompleted
                    && !_protectedScheduler.AutoWandoosStarted && _automaticSafeResourcesEntry != AutomaticRecoveryEntry)
                {
                    var saved = _automaticAtContinuation;
                    var snapshot = run.Capture.Snapshot;
                    ApplyCombatPrepOsPin(run.Capture);
                    snapshot.AtEnergy.TargetLevel = long.Parse(saved["energyTarget"].Value, CultureInfo.InvariantCulture);
                    snapshot.AtMagic.TargetLevel = long.Parse(saved["magicTarget"].Value, CultureInfo.InvariantCulture);
                    var phase = new CombatPrepPhase { Kind = "AT", GearGoal = "AT", GearProfileKey = "current",
                        DurationSeconds = Math.Max(0, saved["phaseEnd"].AsDouble - (CombatPrepClock - run.Started)),
                        AtEnergyTarget = snapshot.AtEnergy.TargetLevel, AtMagicTarget = snapshot.AtMagic.TargetLevel, AtEnergyWeight = saved["weight"].AsDouble };
                    if (CombatPrepTargetsReached(phase))
                    {
                        _protectedScheduler.AutoAtCompleted = true;
                        _automaticAtContinuation = null;
                        run.Forecast = CaptureScheduledCombatPrep(run);
                        ReplanAutomaticCombatPrep(run, "stage-start");
                    }
                    else
                    {
                        run.Policy = CombatPrepPlanner.Project(snapshot, snapshot.CurrentOsId, new[] { phase });
                        // Project refreshes forecasts, not the already-acquired executable gate.
                        // Overdue nominal phases retain their gate until actual completion or the original deadline.
                        run.Policy.Phases = new[] { phase };
                        run.PolicyStarted = run.Started + saved["phaseEnd"].AsDouble - phase.DurationSeconds;
                        run.EquipmentMaterial = CaptureCombatPrepEquipmentMaterial();
                        run.ObservedNetGps = snapshot.NetGoldPerSecond;
                        InitializeCombatPrepTargets(run);
                    }
                }
                else ReplanAutomaticCombatPrep(run, "stage-start");
                if (!AutomaticCombatPrepOwnerMatches(run)) throw new InvalidOperationException("Scheduled owner changed during planning.");
                // Persist before acquiring any native owner. A new coordinator cannot reconstruct live receipts.
                _protectedScheduler.AutoCleanupRecoveryRequired = true;
                if (!SaveProtectedStageProgress(true))
                {
                    _protectedScheduler.AutoCleanupRecoveryRequired = false;
                    throw new InvalidOperationException("Cannot durably record automatic cleanup ownership before acquisition.");
                }
                BeginCombatPrepRun(run);
            }
            catch (Exception error)
            {
                if (run.CleanupPending) { LogError("Scheduled combat prep cleanup pending", error); return; }
                if (CombatPrepActive && ReferenceEquals(_combatPrepRun, run)) EndCombatPrep("failed", error.Message);
                else
                {
                    FreezeCombatPrepTiming(run);
                    run.Status = "failed"; run.Reason = error.Message; _combatPrepRun = run;
                    // Failed preflight spends elapsed time, never the entry itself. The next
                    // attempt keeps its original deadline and any acquired unfinished AT gate.
                    _automaticAcquisitionRetryEntry = AutomaticRecoveryEntry;
                    _automaticAcquisitionRetryAt = CombatPrepClock + 1;
                    CacheCombatPrepRuntime();
                    SetCustomAutomationStatus("Scheduled Augments + Wandoos failed: " + error.Message);
                }
                LogError("Scheduled combat prep", error);
            }
            finally { _automaticStageAcquiring = false; }
        }

        private void ReplanAutomaticCombatPrep(CombatPrepRun run, string reason)
        {
            SettleCombatPrepActivity(run, CombatPrepClock);
            if (!AutomaticCombatPrepOwnerMatches(run)) throw new InvalidOperationException("Scheduled owner changed before replan.");
            run.ReplanReason = reason;
            if (reason != "stage-start") run.ReplanCount++;
            RestoreCombatPrepTargets(run);
            RetireCombatPrepAdventureRoute(run);
            var capture = reason == "stage-start" ? run.Forecast ?? run.Capture : CaptureScheduledCombatPrep(run);
            ApplyCombatPrepOsPin(capture);
            var snapshot = capture.Snapshot;
            if (reason != "stage-start")
                foreach (var os in snapshot.OperatingSystems) os.Unlocked &= os.Id == snapshot.CurrentOsId;
            snapshot.PaidAtForbidden = _protectedScheduler.AutoAtCompleted || _protectedScheduler.AutoWandoosStarted
                || CombatPrepHasNativeWandoosWork();
            snapshot.AugmentsCompleted = _protectedScheduler.AutoAugmentsCompleted;
            // Reporting remains on the entry loadout/support, never the last phase's setup.
            var boss = CombatPrepBossStats(run.Capture.OriginalGear, true, run.Capture.OriginalSupportState.Selection);
            snapshot.BaselineBossAttack = boss.Item1; snapshot.BaselineBossDefense = boss.Item2;
            var now = CombatPrepClock;
            var remaining = Math.Min(run.Duration - (now - run.Started), run.DeadlineRebirthSeconds - Character.rebirthTime.totalseconds);
            var safe = _automaticSafeResourcesEntry == AutomaticRecoveryEntry;
            var result = safe ? null : CombatPrepPlanner.Plan(snapshot, remaining);
            if (!safe && !result.Success) throw new InvalidOperationException("Automatic planning failed: " + result.Error);
            var policy = safe ? CombatPrepPlanner.Project(snapshot, snapshot.CurrentOsId, new[] {
                new CombatPrepPhase { Kind = "NGU", GearGoal = "Current", GearProfileKey = "current", DurationSeconds = Math.Max(0, remaining) }
            }) : result.Recommendation;
            if (policy == null || policy.Phases.Length == 0)
                throw new InvalidOperationException("Automatic planner returned no executable phases; no allocation was acquired.");
            foreach (var key in policy.Phases.Select(x => x.GearProfileKey).Distinct())
            {
                var support = capture.Support[key];
                PreflightExactSupport(support.Evaluation.Selection, support.BudgetFraction, support.EnforceBudget);
            }
            run.Forecast = capture; run.Policy = policy; run.PolicyStarted = now;
            run.EquipmentMaterial = CaptureCombatPrepEquipmentMaterial();
            run.EquipmentReplanPending = false;
            run.ObservedNetGps = snapshot.NetGoldPerSecond;
            run.Label = "Augments + Wandoos / " + policy.Name;
            run.PhaseIndex = -1; run.LastRefresh = -1;
            InitializeCombatPrepTargets(run);
            if (!SaveProtectedStageProgress(true)) throw new InvalidOperationException("Cannot save automatic continuation state.");
        }
    }
}
