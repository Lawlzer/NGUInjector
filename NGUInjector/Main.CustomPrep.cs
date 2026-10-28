using System;
using System.Linq;
using NGUInjector.AllocationProfiles.RebirthStuff;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal partial class Main
    {
        private bool _customPreRebirthPrepRunning;
        private bool _customPreRebirthPrepCompleted;
        private bool _manualRebirthPending;
        private ExecutionClaim _fightPreparationClaim;
        private SelectedSetupRequirements _fightPreparationSetup;
        private ih[] _fightPreparationGear;
        private SupportSelection _fightPreparationSupport;
        private bool _fightPreparationStarted;
        private string _manualRebirthOpportunity;
        private long? _manualRebirthNumber;

        private sealed class FightPreparationAttempt
        {
            internal FiniteActionIdentity Identity;
            internal object Settings, Boss;
            internal string PlanFile;
            internal bool ConsumePowerFruits, RequireMaxedFruit, BossStartAttempted, Completed, Cancelled;
            internal bool PermissionPaused;
            internal CustomPlan AutomaticPlan;
            internal int GearGoal, FightScoreMode;
            internal int UnverifiedFruit = -1;
            internal int[] FruitHarvests;
            internal object[] Fruits;
            internal bool[] FruitStepsDone;
            internal Func<bool> EntryCurrent;
            internal RouteProcess Process;
            internal readonly LoadoutManager.SnapshotEpoch Epoch = new LoadoutManager.SnapshotEpoch();
        }
        private FightPreparationAttempt _fightPreparationAttempt;

        private sealed class ManualFightPreparationRequest
        {
            internal FiniteActionIdentity Identity;
            internal CustomPlan Plan;
            internal ih[] Gear;
            internal string[] Material;
            internal SupportSelection Support;
            internal object Settings, BossProcess, TitanProcess, CombatProcess;
            internal string PlanFile;
            internal bool Retired;
            internal readonly LoadoutManager.SnapshotEpoch Epoch = new LoadoutManager.SnapshotEpoch();
        }
        private ManualFightPreparationRequest _manualFightPreparationRequest;
        internal ExecutionClaim FightPreparationClaim => _fightPreparationClaim;
        internal SelectedSetupRequirements FightPreparationSetup => _fightPreparationSetup;
        internal string FightPreparationIdentity => _fightPreparationAttempt?.Identity.Key;
        private bool FightPreparationDependenciesReady => _fightPreparationClaim != null && !_fightPreparationClaim.SetupReleased
            && (_fightPreparationStarted || _fightPreparationAttempt == null || ScheduledFightAdmissionPermitted(_fightPreparationAttempt.Identity))
            && (_fightPreparationAttempt?.AutomaticPlan == null || !_fightPreparationAttempt.PermissionPaused
                && AutomaticRebirthPreparationPermitted(_fightPreparationAttempt.AutomaticPlan))
            && (_fightPreparationAttempt?.Process == null || !_fightPreparationAttempt.Cancelled
                && (_fightPreparationAttempt.Process.ApplicationCurrent == null || _fightPreparationAttempt.Process.ApplicationCurrent())
                && _fightPreparationAttempt.Process.Permission == RoutePermissionKey(_fightPreparationAttempt.Process.Owner)
                && RouteProcessDependenciesReady(_fightPreparationAttempt.Process)
                && SupportProjection.Evaluate(CaptureSupportContext(), _fightPreparationSetup.Support,
                    _fightPreparationSetup.Budget, _fightPreparationSetup.EnforceSupportBudget).Feasible);
        internal bool FightPreparationVerified => _fightPreparationClaim != null && _fightPreparationSetup != null
            && FightPreparationDependenciesReady && FiniteActionSetupVerified(_fightPreparationClaim, _fightPreparationSetup);
        internal bool FightPreparationCanRetry(string identity) => _fightPreparationAttempt?.Identity.Key == identity
            && !_fightPreparationAttempt.Cancelled && !_fightPreparationAttempt.Completed
            && !_fightPreparationAttempt.BossStartAttempted && _fightPreparationAttempt.Epoch.Matches()
            && (_fightPreparationAttempt.EntryCurrent == null || _fightPreparationAttempt.EntryCurrent())
            && ReferenceEquals(_fightPreparationAttempt.Settings, Settings) && ReferenceEquals(_fightPreparationAttempt.Boss, Character.bossController)
            && _fightPreparationAttempt.PlanFile == Settings.CustomPlanFile;

        internal void CompleteFightPreparation()
        {
            if (!BossPushRunning && !CustomBossFightActive()) ReleaseFightPreparation();
        }

        private bool ObserveCustomRebirthReadiness(CustomPlan plan)
        {
            var retained = RetainAutomaticFightPreparation(plan);
            var ready = Settings.UseCustomRebirthPlan && plan != null && new CustomPlanRebirth(plan).RebirthAvailable();
            if (!retained && !ready && !CustomRebirthPreparationDue(plan) && !_manualRebirthPending && _manualFightPreparationRequest == null
                && (_customPreRebirthPrepRunning || _customPreRebirthPrepCompleted)) ResetCustomPreRebirthPrep();
            return ready;
        }

        private bool CustomRebirthPreparationDue(CustomPlan plan)
        {
            return Settings.UseCustomRebirthPlan && Settings.AutoRebirth && plan?.AutoRebirthEnabled == true
                && plan.RebirthPrepStartSeconds > 0 && Character?.rebirthTime != null
                && Character.rebirthTime.totalseconds >= plan.RebirthPrepStartSeconds
                && Character.challenges?.noRebirthChallenge?.inChallenge == false
                && CustomStageAllocator.ProtectedWorkComplete(plan);
        }

        private bool CustomRebirthFruitHarvestReserved(CustomPlan plan)
        {
            if (_fightPreparationAttempt?.ConsumePowerFruits == true && !_fightPreparationAttempt.Cancelled
                && !_fightPreparationAttempt.Completed) return true;
            if (_fightPreparationAttempt?.ConsumePowerFruits == true && _fightPreparationAttempt.Cancelled)
                return _fightPreparationClaim != null || _bossPushProcess != null || BossPushRunning || CustomBossFightActive();
            return Settings.UseCustomRebirthPlan && Settings.AutoRebirth && plan?.AutoRebirthEnabled == true
                && plan.RebirthPrepStartSeconds > 0 && Character?.rebirthTime != null
                && Character.rebirthTime.totalseconds >= plan.RebirthPrepStartSeconds;
        }

        internal bool RunCustomPreRebirthPrepIfNeeded(CustomPlan plan)
        {
            RetainAutomaticFightPreparation(plan);
            if (!Settings.UseCustomRebirthPlan || plan == null) return false;
            if (_manualFightPreparationRequest != null) return true;
            if (!AutomaticRebirthPreparationPermitted(plan)) return false;
            if (_fightPreparationAttempt?.PermissionPaused == true) return true;
            if (_customPreRebirthPrepCompleted) return false;
            if (_customPreRebirthPrepRunning)
            {
                if (BossPushRunning || CustomBossFightActive()) return true;
                if (!ReleaseFightPreparation()) return true;
                _customPreRebirthPrepRunning = false;
                if (_fightPreparationAttempt?.Cancelled == true) return true;
                _customPreRebirthPrepCompleted = true;
                Log("Custom pre-rebirth fight prep complete");
                return false;
            }
            if (CustomBossFightActive()) return true;
            CancelManualTitan("automatic rebirth preparation");
            RunFightPrep(plan, true, "Automatic rebirth prep");
            if (_fightPreparationAttempt?.Cancelled == true)
            {
                if (BossPushRunning || CustomBossFightActive()) return true;
                if (!ReleaseFightPreparation()) return true;
                return true;
            }
            if (!_fightPreparationStarted) return true;
            _customPreRebirthPrepRunning = BossPushRunning;
            _customPreRebirthPrepCompleted = !_customPreRebirthPrepRunning;
            return _customPreRebirthPrepRunning;
        }

        private bool AutomaticRebirthPreparationPermitted(CustomPlan plan) => Settings.UseCustomRebirthPlan
            && Settings.AutoRebirth && plan?.AutoRebirthEnabled == true
            && Character.challenges?.noRebirthChallenge?.inChallenge == false;

        private bool RetainAutomaticFightPreparation(CustomPlan plan)
        {
            var attempt = _fightPreparationAttempt;
            if (attempt?.AutomaticPlan == null || attempt.Cancelled || _manualRebirthPending || _manualFightPreparationRequest != null
                || !attempt.Epoch.Matches() || !ReferenceEquals(attempt.Settings, Settings)
                || !ReferenceEquals(attempt.Boss, Character.bossController) || attempt.PlanFile != Settings.CustomPlanFile) return false;
            var permitted = AutomaticRebirthPreparationPermitted(plan);
            if (attempt.Completed)
            {
                if (!_customPreRebirthPrepCompleted) CustomPlanRebirth.ResetRuntimeState();
                _customPreRebirthPrepRunning = false;
                _customPreRebirthPrepCompleted = true;
                return true;
            }
            if (permitted && !attempt.PermissionPaused) return false;
            // Permission suspension retires authority, not the acquired options or spent fruit steps.
            attempt.PermissionPaused = true;
            _customPreRebirthPrepRunning = _customPreRebirthPrepCompleted = false;
            StopBossPush();
            if (!BossPushRunning && !CustomBossFightActive() && ReleaseFightPreparation() && permitted)
            {
                attempt.BossStartAttempted = false;
                attempt.AutomaticPlan = plan;
                attempt.PermissionPaused = false;
            }
            return true;
        }

        internal string RunManualFightPrep()
        {
            if (string.IsNullOrEmpty(CurrentExecutionCommandId)) return "Fight preparation requires an identified explicit request.";
            if (_manualFightPreparationRequest != null) return ContinueManualFightPreparation();
            var identity = ActionIdentity("fight-preparation", "fight-preparation", CurrentExecutionCommandId);
            var plan = CustomPlanStore?.Load(Settings.CustomPlanFile) ?? new CustomPlan();
            if (_fightPreparationAttempt?.Identity.Key == identity.Key)
                return RunFightPrep(plan, false, "Manual fight prep", opportunity: CurrentExecutionCommandId);
            // A native fight may outlive its injector process. Do not touch its setup.
            if (CustomBossFightActive()) return "Manual fight prep: waiting for the current native boss fight to stop.";
            try
            {
                var policy = SetupForFocus(CustomFocusGoal.Fight);
                var gear = SelectSetupGear((GearOptimizationGoal)policy.Stage.GearGoal, (FightScoreMode)plan.FightScoreMode);
                var support = SelectFocusSupport(policy);
                var permitted = (Settings.ManageEnergy ? GearRecallFootprint.Energy : 0) | (Settings.ManageMagic ? GearRecallFootprint.Magic : 0);
                permitted &= (GearRecallFootprint)~_resourceSetupExecutor.Application.BlockedLanes;
                var failure = GearOptimizer.PreflightCapacityTransition(gear, permitted).Failure;
                if (failure != null) return "Manual fight prep: setup waiting: " + failure;
                if (Character.buttons.beards.interactable && !support.Beards.Contains(BeardFuManchu))
                    return "Manual fight prep: setup waiting: required fight beard does not fit.";
                PreflightExactSupport(support, ActivityPolicy.Current.DiggerBudgetFraction, true);
                if (_fightPreparationClaim != null && (!ExecutionClaimCurrent(_fightPreparationClaim) || !ExecutionClaimSetupOwned(_fightPreparationClaim)))
                    return "Manual fight prep: setup waiting: previous preparation authority changed.";
                _manualFightPreparationRequest = new ManualFightPreparationRequest { Identity = identity, Plan = plan, Gear = gear, Support = support,
                    Material = gear.Select(x => LoadoutPresetPrimitiveKey(x.equipment)).ToArray(),
                    BossProcess = _bossPushProcess, TitanProcess = _manualTitanProcess, CombatProcess = _combatPrepRun,
                    Settings = Settings, PlanFile = Settings.CustomPlanFile };
                return ContinueManualFightPreparation();
            }
            catch (Exception error) { return "Manual fight prep: preparation blocked: " + error.Message; }
        }

        private string ContinueManualFightPreparation()
        {
            var request = _manualFightPreparationRequest;
            if (request == null) return "Manual fight prep: no identified request is pending.";
            try
            {
                if (!request.Epoch.Matches() || !ReferenceEquals(request.Settings, Settings) || request.PlanFile != Settings.CustomPlanFile
                    || request.Gear.Where((item, i) => LoadoutPresetPrimitiveKey(item.equipment) != request.Material[i]).Any())
                {
                    _manualFightPreparationRequest = null;
                    if (request.Retired) CancelFightPreparationAttempt();
                    return "Manual fight prep: acquired request changed; request cancelled.";
                }
                if (CustomBossFightActive()) return "Manual fight prep: waiting for the current native boss fight to stop.";
                if (!request.Retired)
                {
                    if (_bossPushProcess != null && !ReferenceEquals(_bossPushProcess, request.BossProcess)
                        || _manualTitanActive && !ReferenceEquals(_manualTitanProcess, request.TitanProcess)
                        || CombatPrepActive && !ReferenceEquals(_combatPrepRun, request.CombatProcess))
                    {
                        _manualFightPreparationRequest = null;
                        return "Manual fight prep: another process owns the prerequisites; request cancelled.";
                    }
                    CancelManualTitan("manual fight preparation");
                    EndCombatPrep("cancelled", "Manual fight prep preempted combat prep.", false);
                    StopBossPush();
                    if (BossPushRunning || _manualTitanActive || CombatPrepActive || CustomBossFightActive() || Character.adventure.zone != -1)
                        return "Manual fight prep: waiting for previous process retirement.";
                    if (!ReleaseFightPreparation()) return "Manual fight prep: waiting for previous preparation retirement.";
                    if (UnverifiedPreparationFruitCurrent(_fightPreparationAttempt))
                    {
                        _manualFightPreparationRequest = null;
                        return "Manual fight prep: previous fruit outcome requires review.";
                    }
                    _manualRebirthPending = false; _manualRebirthNumber = null;
                    _customPreRebirthPrepRunning = _customPreRebirthPrepCompleted = false;
                    if (_customSustainedFocusGoal.HasValue || _focusSetupSelection != null) ClearPersistedExecutionSelection("focus");
                    ClearCustomSustainedFocus();
                    _fightPreparationAttempt = null;
                    _fightPreparationGear = request.Gear; _fightPreparationSupport = request.Support;
                    request.Retired = true;
                }
                var summary = RunFightPrep(request.Plan, false, "Manual fight prep", () => _manualFightPreparationRequest = null, request.Identity.Opportunity);
                if (_fightPreparationAttempt?.Cancelled == true || _fightPreparationAttempt?.Completed == true) _manualFightPreparationRequest = null;
                return summary;
            }
            catch (Exception error) { return "Manual fight prep: handoff/setup waiting: " + error.Message; }
        }

        internal string RunManualRebirth()
        {
            if (_manualFightPreparationRequest != null) return "Manual fight preparation handoff is pending; no rebirth was admitted.";
            if (!_manualRebirthPending)
            {
                if (string.IsNullOrEmpty(CurrentExecutionCommandId)) return "Manual rebirth requires an identified explicit request.";
                _manualRebirthOpportunity = CurrentExecutionCommandId;
                _manualRebirthNumber = Character.stats.rebirthNumber;
                BeginRebirthAutomationPass();
            }
            _manualRebirthPending = true;
            if (CustomBossFightActive()) return "Manual rebirth waiting for the current native boss fight to stop.";
            CancelManualTitan("manual rebirth");
            EndCombatPrep("cancelled", "Manual rebirth preempted combat prep.", false);
            ClearCustomSustainedFocus();
            return ContinueManualRebirth();
        }

        private string ContinueManualRebirth()
        {
            if (_manualRebirthNumber.HasValue && Character.stats.rebirthNumber != _manualRebirthNumber.Value)
            {
                // Current native state retires this session request, never an imported action receipt.
                _manualRebirthPending = false;
                ResetCustomPreRebirthPrep();
                ResetCustomAutomationRuntime();
                return "Manual rebirth request retired after the native rebirth changed.";
            }
            var plan = CustomPlanStore?.Load(Settings.CustomPlanFile) ?? new CustomPlan();
            if (_customPreRebirthPrepRunning)
            {
                if (BossPushRunning || CustomBossFightActive()) return "Manual rebirth prep is still fighting bosses; rebirth will happen automatically when boss push finishes.";
                if (!ReleaseFightPreparation()) return "Manual rebirth prep is waiting for preparation retirement.";
                _customPreRebirthPrepRunning = false;
                _customPreRebirthPrepCompleted = true;
                Log("Manual rebirth prep complete");
            }
            if (!_customPreRebirthPrepCompleted)
            {
                var prepSummary = RunFightPrep(plan, true, "Manual rebirth prep", opportunity: _manualRebirthOpportunity);
                if (_fightPreparationAttempt?.Cancelled == true)
                {
                    ResetCustomPreRebirthPrep();
                    return prepSummary + "; manual rebirth request cancelled.";
                }
                if (!_fightPreparationStarted) return prepSummary;
                _customPreRebirthPrepRunning = BossPushRunning;
                _customPreRebirthPrepCompleted = !_customPreRebirthPrepRunning;
                if (_customPreRebirthPrepRunning) return prepSummary + "; rebirth will happen automatically when it stops.";
            }
            return TryEngageManualRebirth();
        }

        private string TryEngageManualRebirth()
        {
            if (CustomBossFightActive()) return "Manual rebirth waiting for the boss fight to fully stop.";
            var rebirth = new ManualRebirth();
            if (!rebirth.RebirthAvailable())
            {
                ResetCustomPreRebirthPrep();
                return "Manual rebirth unavailable; minimum rebirth time or challenge state is blocking it.";
            }
            if (_profile.DoRebirth(rebirth))
            {
                // Retire the request before checkpoint/history/UI work can throw.
                ResetCustomPreRebirthPrep();
                CommitExecutionRebirthCheckpoint();
                ResetCustomAutomationRuntime();
                return "Manual rebirth engaged";
            }
            if (RebirthAttemptedThisPass)
            {
                ResetCustomPreRebirthPrep();
                return "Manual rebirth attempt stopped without verified native completion; request cancelled.";
            }
            return "Manual rebirth delayed by final pre-rebirth checks; will retry automatically.";
        }

        private bool ProcessPendingManualRebirth()
        {
            if (_manualFightPreparationRequest != null)
            {
                var preparationMessage = ContinueManualFightPreparation();
                settingsForm?.ReportCustomStatus(preparationMessage);
                return true;
            }
            if (!_manualRebirthPending) return false;
            var message = ContinueManualRebirth();
            settingsForm?.ReportCustomStatus(message);
            return true;
        }

        internal void ResetCustomPreRebirthPrep()
        {
            _manualFightPreparationRequest = null;
            _manualRebirthPending = false; _manualRebirthNumber = null;
            _customPreRebirthPrepRunning = _customPreRebirthPrepCompleted = false;
            if (_fightPreparationAttempt != null) _fightPreparationAttempt.Cancelled = true;
            StopBossPush();
            if (!CustomBossFightActive()) ReleaseFightPreparation();
        }

        private string RunFightPrep(CustomPlan plan, bool consumePowerFruits, string summaryPrefix,
            Action onBossPushStarted = null, string opportunity = null)
        {
            plan = plan ?? new CustomPlan();
            try
            {
                var requested = ActionIdentity("fight-preparation", "fight-preparation", opportunity ?? "automatic:" + Settings.CustomPlanFile);
                var attempt = _fightPreparationAttempt;
                if (attempt != null && attempt.Identity.Key != requested.Key)
                {
                    if (_fightPreparationClaim != null || BossPushRunning || UnverifiedPreparationFruitCurrent(attempt))
                        return summaryPrefix + ": previous preparation must retire first.";
                    _fightPreparationAttempt = attempt = null;
                }
                if (attempt?.Cancelled == true) return summaryPrefix + ": this preparation attempt was cancelled.";
                if (attempt?.Completed == true) return summaryPrefix + ": this preparation attempt is complete.";
                if (!_fightPreparationStarted && !ScheduledFightAdmissionPermitted(requested))
                {
                    CancelFightPreparationAttempt();
                    return summaryPrefix + ": schedule admission was disabled or expired.";
                }
                if (attempt != null && (!attempt.Epoch.Matches() || !ReferenceEquals(attempt.Settings, Settings)
                    || !ReferenceEquals(attempt.Boss, Character.bossController) || attempt.PlanFile != Settings.CustomPlanFile))
                {
                    CancelFightPreparationAttempt();
                    return summaryPrefix + ": acquired preparation changed; attempt cancelled.";
                }
                if (CustomBossFightActive()) return summaryPrefix + ": waiting for the current native boss fight to stop.";
                if (attempt == null)
                {
                    attempt = new FightPreparationAttempt { Identity = requested, Settings = Settings, Boss = Character.bossController, PlanFile = Settings.CustomPlanFile,
                        // Automatic rebirth prep takes every available tier; manual rebirth retains its safety preference.
                        ConsumePowerFruits = consumePowerFruits, RequireMaxedFruit = _manualRebirthPending && plan.PrepRequireMaxedFruit,
                        AutomaticPlan = consumePowerFruits && opportunity == null && !_manualRebirthPending ? plan : null,
                        GearGoal = SetupForFocus(CustomFocusGoal.Fight).Stage.GearGoal, FightScoreMode = plan.FightScoreMode };
                    if (!consumePowerFruits && _manualFightPreparationRequest == null && _protectedScheduler != null)
                    {
                        var scheduler = _protectedScheduler; var entry = ExecutionPersistenceEntry;
                        attempt.EntryCurrent = () => ReferenceEquals(scheduler, _protectedScheduler) && entry == ExecutionPersistenceEntry
                            && !scheduler.Finished && Settings.UseCustomRebirthPlan;
                    }
                    if (consumePowerFruits)
                    {
                        var fruits = Character.yggdrasil?.fruits;
                        if (fruits == null) throw new InvalidOperationException("Preparation fruit state is unavailable.");
                        attempt.FruitHarvests = fruits.Select(fruit => fruit.harvests).ToArray();
                        attempt.Fruits = fruits.Cast<object>().ToArray();
                        attempt.FruitStepsDone = new bool[fruits.Count];
                    }
                    _fightPreparationAttempt = attempt;
                }
                if (!FightPreparationFruitsCurrent(attempt))
                {
                    CancelFightPreparationAttempt();
                    return summaryPrefix + ": original fruit opportunity changed; attempt cancelled.";
                }
                if (_fightPreparationSetup == null)
                {
                    ResetCustomIronPillPrep();
                    if (_fightPreparationGear == null) _fightPreparationGear = SelectSetupGear((GearOptimizationGoal)attempt.GearGoal, (FightScoreMode)attempt.FightScoreMode);
                    if (_fightPreparationSupport == null) _fightPreparationSupport = SelectFocusSupport(SetupForFocus(CustomFocusGoal.Fight));
                    if (Character.buttons.beards.interactable && !_fightPreparationSupport.Beards.Contains(BeardFuManchu))
                    {
                        _fightPreparationSupport = null;
                        return summaryPrefix + ": required fight beard does not fit.";
                    }
                    var permitted = (Settings.ManageEnergy ? GearRecallFootprint.Energy : 0) | (Settings.ManageMagic ? GearRecallFootprint.Magic : 0);
                    permitted &= (GearRecallFootprint)~_resourceSetupExecutor.Application.BlockedLanes;
                    var capacity = GearOptimizer.PreflightCapacityTransition(_fightPreparationGear, permitted);
                    if (capacity.Failure != null) return summaryPrefix + ": setup waiting: " + capacity.Failure;
                    PreflightExactSupport(_fightPreparationSupport, 1, true);
                    attempt.Process = BeginRouteProcess(requested.Owner, _fightPreparationGear,
                        domains: ExecutionDomain.Gear | ExecutionDomain.Support | ExecutionDomain.Route | ExecutionDomain.FiniteAction | ExecutionDomain.Inventory,
                        selectedSupport: _fightPreparationSupport, capacityTransition: true, resourceRecall: capacity.Recall, acquisition: requested.Acquisition);
                    var applicationCurrent = attempt.Process.ApplicationCurrent;
                    attempt.Process.ApplicationCurrent = () => !attempt.PermissionPaused
                        && (attempt.AutomaticPlan == null || AutomaticRebirthPreparationPermitted(attempt.AutomaticPlan))
                        && (applicationCurrent == null || applicationCurrent());
                    _fightPreparationClaim = attempt.Process.Claim;
                    if (SuspendedFightHandoff) _combatPrepRun.FightReleaseVerified = false;
                    _fightPreparationSetup = attempt.Process.Setup;
                    _fightPreparationSetup.ControlsSupport = true;
                    _fightPreparationSetup.Budget = 1;
                    _fightPreparationSetup.EnforceSupportBudget = true;
                    _fightPreparationSetup.RequiredBeards = Character.buttons.beards.interactable ? new[] { BeardFuManchu } : new int[0];
                }
                if (!FightPreparationVerified)
                {
                    if (CustomBossFightActive()) return summaryPrefix + ": waiting for the current native boss fight to stop.";
                    ApplyRouteProcess(attempt.Process);
                    if (!FightPreparationVerified) return summaryPrefix + ": waiting for verified exact fight setup.";
                }
                if (!attempt.BossStartAttempted && attempt.ConsumePowerFruits)
                    for (var fruit = 0; fruit < attempt.Fruits.Length; fruit++)
                    {
                        if (!FightPreparationFruitsCurrent(attempt))
                        {
                            CancelFightPreparationAttempt();
                            return summaryPrefix + ": original fruit opportunity changed; attempt cancelled.";
                        }
                        if (attempt.FruitStepsDone[fruit]) continue;
                        var controller = Character.yggdrasilController.fruits[0];
                        if (controller.harvestTier(fruit) < 1 || attempt.RequireMaxedFruit && !controller.fruitMaxxed(fruit))
                            continue;
                        var forceEat = fruit == YggdrasilManager.FruitOfPowerAlphaId || fruit == YggdrasilManager.FruitOfPowerBetaId;
                        var result = RunFruitHarvestAction(fruit, attempt.RequireMaxedFruit, forceEat, _fightPreparationClaim, _fightPreparationSetup,
                            attempt.Identity.Key + ":fruit:" + ActionNumber(fruit), attempt.FruitHarvests[fruit]);
                        if (result.Invoked) attempt.FruitStepsDone[fruit] = true;
                        if (!result.Verified)
                        {
                            if (result.Invoked)
                            {
                                attempt.UnverifiedFruit = fruit;
                                CancelFightPreparationAttempt();
                            }
                            return summaryPrefix + (result.Invoked ? ": fruit outcome unverified; attempt cancelled." : ": waiting for fruit prerequisites.");
                        }
                    }
                if (!FightPreparationFruitsCurrent(attempt))
                {
                    CancelFightPreparationAttempt();
                    return summaryPrefix + ": original fruit opportunity changed; attempt cancelled.";
                }
                if (!FightPreparationVerified) return summaryPrefix + ": fight prerequisites changed.";
                if (!attempt.BossStartAttempted)
                {
                    attempt.BossStartAttempted = true;
                    try { StartBossPush(); }
                    catch { CancelFightPreparationAttempt(); StopBossPush(); throw; }
                    if (!BossPushRunning)
                    {
                        attempt.BossStartAttempted = false;
                        return summaryPrefix + ": boss process admission is pending.";
                    }
                    _fightPreparationStarted = true;
                    onBossPushStarted?.Invoke();
                }
                return summaryPrefix + ": boss push running.";
            }
            catch (Exception error) { return summaryPrefix + ": preparation blocked: " + error.Message; }
        }

        private bool FightPreparationFruitsCurrent(FightPreparationAttempt attempt)
        {
            if (!attempt.ConsumePowerFruits) return true;
            var fruits = Character.yggdrasil?.fruits;
            return fruits != null && fruits.Count == attempt.Fruits.Length
                && !fruits.Where((fruit, i) => !ReferenceEquals(fruit, attempt.Fruits[i])
                    || !attempt.FruitStepsDone[i] && fruit.harvests != attempt.FruitHarvests[i]).Any();
        }

        private void CancelFightPreparationAttempt()
        {
            if (_fightPreparationAttempt != null) _fightPreparationAttempt.Cancelled = true;
            if (!BossPushRunning && !CustomBossFightActive()) ReleaseFightPreparation();
        }

        private bool RetireObsoleteFightPreparation()
        {
            var attempt = _fightPreparationAttempt;
            if (attempt == null) return true;
            var obsolete = !attempt.BossStartAttempted && attempt.EntryCurrent != null && !attempt.EntryCurrent();
            if (!obsolete && !attempt.Cancelled && !_fightPreparationStarted) return true;
            if (obsolete && (!_protectedCheckpointDurable || !ProtectedEpochMatches() || !attempt.Epoch.Matches())) return false;
            if (BossPushRunning || CustomBossFightActive()) return false;
            if (obsolete && attempt.UnverifiedFruit >= 0) return false;
            if (obsolete) attempt.Cancelled = true;
            if (!ReleaseFightPreparation()) return false;
            if (obsolete) _fightPreparationAttempt = null;
            return true;
        }

        private bool ReleaseFightPreparation()
        {
            if (BossPushRunning || CustomBossFightActive()) return false;
            var attempt = _fightPreparationAttempt;
            // Boss ticks can retire the route before the readiness observer sees permission loss.
            if (attempt?.AutomaticPlan != null && !attempt.Cancelled && !attempt.Completed
                && !AutomaticRebirthPreparationPermitted(attempt.AutomaticPlan))
            {
                attempt.PermissionPaused = true;
                _customPreRebirthPrepRunning = _customPreRebirthPrepCompleted = false;
            }
            var claim = _fightPreparationClaim;
            var suspended = SuspendedFightHandoff ? _combatPrepRun : null;
            var witnessed = suspended != null && !suspended.Retired && FightPreparationVerified && CombatPrepEntryAuthority(suspended)
                && CombatPrepTargetsMatch(suspended) && Character.adventure.zone == -1;
            var terminalGear = witnessed ? _fightPreparationSetup.Gear.ToArray() : null;
            var terminalMaterial = terminalGear?.Select(item => LoadoutPresetPrimitiveKey(item.equipment)).ToArray();
            var terminalSupport = witnessed ? CaptureSupportState() : null;
            var terminalObjects = witnessed ? SupportObjects() : null;
            var terminalConflict = SelectedSetupConflictVersion(Character);
            if (claim != null)
            {
                var handoff = _routeHandoffPending;
                _routeHandoffPending |= _manualFightPreparationRequest != null || suspended != null;
                try
                {
                    if (_fightPreparationAttempt?.Process != null)
                    {
                        if (!EndRouteProcess(_fightPreparationAttempt.Process)) return false;
                    }
                    else if (!EndExecutionClaim(claim, _manualFightPreparationRequest == null)) return false;
                }
                catch (Exception error)
                {
                    SetResourceExecution(ResourceExecutionState.Waiting, claim.Owner, "Fight preparation retirement waiting: " + error.Message);
                    return false;
                }
                finally { _routeHandoffPending = handoff; }
            }
            if (witnessed && CombatPrepEntryAuthority(suspended) && !CustomBossFightActive() && Character.adventure.zone == -1
                && CombatPrepGearMatches(terminalGear) && CaptureSupportState().StableKey == terminalSupport.StableKey
                && terminalGear.Where((item, i) => LoadoutPresetPrimitiveKey(item.equipment) != terminalMaterial[i]).Any() == false
                && SelectedSetupConflictVersion(Character) == terminalConflict)
            {
                suspended.FightSuccessorGear = terminalGear;
                suspended.FightSuccessorMaterial = terminalMaterial;
                suspended.FightSuccessorSupport = terminalSupport;
                suspended.FightSuccessorSupportObjects = terminalObjects;
                suspended.FightConflictVersion = terminalConflict;
                suspended.FightReleaseVerified = true;
            }
            if (_fightPreparationAttempt != null && _fightPreparationStarted && !_fightPreparationAttempt.Cancelled
                && !_fightPreparationAttempt.PermissionPaused && _fightPreparationAttempt.UnverifiedFruit < 0)
                _fightPreparationAttempt.Completed = true;
            _fightPreparationClaim = null; _fightPreparationSetup = null;
            if (_fightPreparationAttempt?.PermissionPaused != true || _fightPreparationAttempt.Cancelled)
            {
                _fightPreparationGear = null; _fightPreparationSupport = null;
            }
            _fightPreparationStarted = false;
            return true;
        }
    }
}
