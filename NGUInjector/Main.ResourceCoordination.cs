using System;
using System.Linq;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;
using SimpleJSON;

namespace NGUInjector
{
    internal partial class Main
    {
        private enum ResourceExecutionState { Stopped, Applied, Waiting, NeedsUser, Failed }
        private enum PresetApplicationIssue
        {
            None, NoReceipt, DifferentOwner, EpochChanged, Blocked, NativeTargetsChanged,
            SelectionChanged, EquipmentChanged, MaterialChanged, WeightsChanged, ObservationsChanged, ResourceAuthorityUnresolved
        }

        private bool _resourceReconciliationRunning;
        private bool _resourceReconciliationPending;
        private ResourceExecutionState _resourceExecutionState;
        private string _resourceExecutionOwner = "", _resourceExecutionReason = "";
        private string _resourceDemandIdentity;

        private sealed class ExecutionDomainIssue
        {
            internal string Event, Domain, Reason;
            internal MutationDisposition Disposition;
        }
        // Last diagnostic only. Mutation authority lives in native domain evidence,
        // never this producer/status projection (lifecycle may clear the projection).
        private ExecutionDomainIssue _presetExecutionBlock;

        private sealed class NativeConfigurationConflictException : InvalidOperationException
        {
            internal NativeConfigurationConflictException(string reason, Exception error) : base(reason, error) { }
        }

        private void RequireGlobalPresetNativeTargets(CustomPlanStage stage, string owner, string reason)
        {
            if (GlobalPresetNativeTargetsMatch(stage, owner) && !GlobalPresetNativeTargetsYielded(owner)) return;
            ObserveGlobalPresetTargetConflict(owner);
            if (Settings.ManageMagic) return;
            var rendering = _presetResourceOutcome?.Error;
            throw new NativeConfigurationConflictException(reason + (rendering == null ? "" : " " + rendering.Message), rendering);
        }

        private void ObserveGlobalPresetTargetConflict(string owner)
        {
            _globalPresetNativeTargetConflict = owner;
            var evidence = SelectedSetupConflicts.GetValue(Character, _ => new SelectedSetupConflict { Rebirth = Character.stats.rebirthNumber });
            if (evidence.Rebirth != Character.stats.rebirthNumber)
            {
                evidence.Rebirth = Character.stats.rebirthNumber; evidence.Gear = evidence.Support = evidence.Route = null;
                evidence.NativeSettings = evidence.NativeSettingsOwner = null;
                evidence.GearObservation = evidence.SupportObservation = evidence.RouteObservation = null;
            }
            if (evidence.NativeSettingsOwner != owner || evidence.NativeSettings == null)
            {
                evidence.NativeSettingsOwner = owner;
                evidence.NativeSettings = "Native AT targets changed externally; Energy intent and dependent credit require review. "
                    + (_presetResourceOutcome?.Error?.Message ?? "");
                evidence.Revision++;
            }
            SuspendCurrentGlobalPresetStage(owner);
        }

        private void BlockPresetExecution(string eventKey, string domain, Exception error)
        {
            var mutation = error as ResourceMutationException;
            var support = error as SupportApplicationException;
            if (error is NativeConfigurationConflictException) domain = "native-targets";
            _presetExecutionBlock = new ExecutionDomainIssue { Event = eventKey, Domain = domain,
                Disposition = mutation?.Result.Disposition ?? support?.Result.Disposition
                    ?? (error is NativeConfigurationConflictException ? MutationDisposition.ForeignConflict
                        : error is FormatException ? MutationDisposition.RejectedUnchanged : MutationDisposition.UnknownOutcome),
                Reason = error.Message };
            _globalPresetFailure = domain + " requires acknowledgement: " + error.Message
                + " Restart the stage or explicitly Equip to acquire new authority.";
        }

        private void SetResourceExecution(ResourceExecutionState state, string owner, string reason)
        {
            _resourceExecutionState = state;
            _resourceExecutionOwner = owner ?? "";
            _resourceExecutionReason = reason ?? "";
            if (state != ResourceExecutionState.Applied && !string.IsNullOrEmpty(reason))
                SetCustomAutomationStatus(reason);
        }

        private JSONObject GetResourceCoordinationRuntime()
        {
            var result = new JSONObject();
            result["state"] = _resourceExecutionState.ToString();
            result["owner"] = _resourceExecutionOwner;
            result["reason"] = _resourceExecutionReason;
            result["blocked"] = _resourceExecutionState == ResourceExecutionState.NeedsUser
                || _resourceExecutionState == ResourceExecutionState.Failed
                || _resourceExecutionState == ResourceExecutionState.Waiting
                    && _resourceExecutionReason.StartsWith("Preset allocation blocked:", StringComparison.Ordinal);
            result["ironPill"] = CustomIronPillPrepActive();
            result["bloodGoals"] = CustomBloodGoalCatchUpRequested(Settings?.UseCustomRebirthPlan == true ? _customRuntimePlanCache : null);
            if (_presetExecutionBlock != null)
            {
                result["blockedDomain"] = _presetExecutionBlock.Domain;
                result["mutationOutcome"] = _presetExecutionBlock.Disposition.ToString();
            }
            var resourceOutcome = CombatPrepRunActive ? _combatPrepRun.ResourceOutcome
                : _customSustainedFocusGoal.HasValue ? _customFocusResourceOutcome
                : Settings?.UseCustomRebirthPlan == true && _globalPresetReceipt == null ? CustomStageAllocator.LastStageResourceOutcome : _presetResourceOutcome;
            if (resourceOutcome != null)
            {
                result["resourceOutcome"] = resourceOutcome.Disposition.ToString();
                result["presentationPending"] = CombatPrepRunActive ? _resourceSetupExecutor.Application.PresentationPending
                    : _customSustainedFocusGoal.HasValue ? _customFocusResourceApplication?.PresentationPending == true
                    : Settings?.UseCustomRebirthPlan == true && _globalPresetReceipt == null ? CustomStageAllocator.StageResourcePresentationPending
                    : _presetResources?.PresentationPending == true;
                result["affectedResourceLanes"] = resourceOutcome.AffectedLanes;
                result["verifiedResourceLanes"] = resourceOutcome.VerifiedLanes;
            }
            if (_presetSupportOutcome != null) result["supportOutcome"] = _presetSupportOutcome.Disposition.ToString();
            return result;
        }

        // Frame, quick and ordinary callbacks share selection and allocation cadence. Combat
        // lifecycle/cleanup must be serviced even when ordinary allocation is throttled.
        private void ReconcileCustomResources(CustomPlan plan = null, bool allowSetup = false)
        {
            if (!ReferenceEquals(reference, this) || _unloaded || Settings == null || Character?.rebirthTime == null
                || _executionLifecycle != ExecutionLifecycle.Running) return;
            if (_resourceReconciliationRunning) { _resourceReconciliationPending = true; return; }
            var requested = _resourceReconciliationPending;
            _resourceReconciliationPending = false;
            _resourceReconciliationRunning = true;
            try
            {
                if (Settings.UseCustomRebirthPlan && _globalPresetReceipt?.Owner == "dashboard"
                    && Character.adventure.zone != -1 && !ExecutionRouteClaimActive)
                {
                    var applied = _globalPresetReceipt;
                    var retirement = BeginExecutionClaim("baseline-handoff", Guid.NewGuid().ToString("N"), ExecutionDomain.Route,
                        () => ReferenceEquals(applied, _globalPresetReceipt));
                    try
                    {
                        ApplyExecutionClaim(retirement, new SelectedSetupRequirements { ControlsGear = false,
                            Support = CaptureSupportState().Selection, ChangeSupport = false, RetireRoute = true,
                            RouteRetirementAuthorized = CaptureExecutionBaselineRouteRetirement(retirement, true) });
                    }
                    finally { EndExecutionClaim(retirement, false); }
                }
                if (Settings.UseCustomRebirthPlan && (_customRequestedFocusGoal.HasValue || _globalPresetManualId != null))
                {
                    ClearPersistedExecutionSelection("focus"); ClearPersistedExecutionSelection("dashboard");
                    _retainedPresetSelections?.Remove("dashboard");
                    ClearCustomSustainedFocus(); _globalPresetManualId = null;
                }
                if (ServiceSuspendedCombatPrep()) return;
                ServiceAutomaticRecovery();
                if (CombatPrepActive)
                {
                    ReconcileCombatPrepRequirements();
                    SetResourceExecution(CombatPrepCleanupRecoveryRequired ? ResourceExecutionState.Failed
                        : _combatPrepRun?.BlockedReason != null || _combatPrepRun?.YieldedResourceLanes != 0 ? ResourceExecutionState.NeedsUser
                        : _combatPrepRun?.ResourceOutcome?.Verified == true && _combatPrepRun.SetupVerified && _combatPrepRun.AppliedResourcePhase == _combatPrepRun.PhaseIndex
                            ? ResourceExecutionState.Applied : ResourceExecutionState.Waiting,
                        "combat-prep", CombatPrepCleanupRecoveryRequired ? "Automatic recovery is waiting for its checkpoint retry; check runtime directory permissions or file locks."
                        : _combatPrepRun.CleanupPending ? "Combat prep route release is pending." : _combatPrepRun.Reason);
                    return;
                }
                var now = DateTime.UtcNow;
                var identity = Settings.UseCustomRebirthPlan + ":" + Settings.CustomPlanFile + ":" + _customStageKey
                    + ":" + _globalPresetManualId + ":" + _customRequestedFocusGoal + ":" + _customEquipmentApplicationInvalidated
                    + ":" + Settings.ManageEnergy + ":" + Settings.ManageMagic + ":" + Settings.ManageR3
                    + ":" + CustomIronPillPrepActive() + ":" + string.Join(",", Settings.EnergyNguWeights)
                    + ":" + string.Join(",", Settings.MagicNguWeights);
                if (!allowSetup && !requested && identity == _resourceDemandIdentity
                    && (now - _customLastQuickResourceRefresh).TotalSeconds < CustomQuickResourceRefreshSeconds) return;
                _resourceDemandIdentity = identity;
                _customLastQuickResourceRefresh = now;
                if (Settings.UseCustomRebirthPlan && plan == null) plan = LoadRuntimeCustomPlan();
                var currentStage = Settings.UseCustomRebirthPlan && plan?.StageSchedulerEnabled == true ? GetProtectedStage(plan) : null;
                if (currentStage?.Stage.ExecutionMode == (int)CustomStageExecutionMode.Standard && ServiceStageFightSchedule(plan)) return;
                ServiceExecutionNativeSettings();
                if (CombatPrepActive) return;
                if (CustomQuickResourceRefreshBlocked(plan))
                {
                    SuspendProtectedStageProgress();
                    SetResourceExecution(ResourceExecutionState.Waiting, "", "Resource allocation is waiting for combat or rebirth preparation.");
                    return;
                }
                ResourceWriter.Execute(() =>
                {
                    if (_globalPresetManualId != null && !Settings.UseCustomRebirthPlan)
                    {
                        RunManualGlobalPresetRefresh();
                        return;
                    }
                    if (Settings.UseCustomRebirthPlan && plan != null)
                    {
                        if (allowSetup || !CustomStageReadyForQuickRefresh(plan, out var runtime, out var stageKey))
                        {
                            RunCustomPlanAutomation(plan);
                            return;
                        }
                        SuspendProtectedStageProgress();
                        if (!string.IsNullOrEmpty(runtime.Stage.PresetId))
                        {
                            RunGlobalPresetStage(plan, runtime, _customStageEquipmentKey);
                            return;
                        }
                        var resources = CustomStageAllocator.ResolveStageResources(plan, runtime.Stage, runtime.RemainingSeconds,
                            stageKey, CustomStageDurationSeconds(runtime));
                        var message = CustomStageAllocator.ApplyStageResources(resources);
                        CommitProtectedStageApplication(plan);
                        var disposition = CustomStageAllocator.LastStageResourceOutcome?.Disposition;
                        SetResourceExecution(CustomStageAllocator.LastStageApplicationSucceeded ? ResourceExecutionState.Applied
                            : disposition == MutationDisposition.ForeignConflict || disposition == MutationDisposition.UnknownOutcome
                                ? ResourceExecutionState.NeedsUser : ResourceExecutionState.Waiting, stageKey, message);
                        if (!string.IsNullOrEmpty(message)) SetCustomAutomationStatus(BuildRuntimeStatus(plan, runtime, message));
                        return;
                    }
                    if (_customSustainedFocusGoal.HasValue)
                    {
                        RunCustomSustainedFocusAllocation(allowSetup);
                        SetResourceExecution(_customFocusStatus == "applied" ? ResourceExecutionState.Applied : ResourceExecutionState.Failed,
                            "focus:" + _customRequestedFocusGoal, _customFocusStatus == "failed" ? GetCustomAutomationStatus() : "");
                        return;
                    }
                    SetResourceExecution(ResourceExecutionState.Stopped, "", "");
                });
            }
            catch (Exception error)
            {
                if (!ReferenceEquals(reference, this) || _unloaded || Settings == null) return;
                SuspendProtectedStageProgress();
                var mutation = error as ResourceMutationException;
                SetResourceExecution(mutation?.Result.Disposition == MutationDisposition.RejectedUnchanged ? ResourceExecutionState.Waiting
                    : mutation != null ? ResourceExecutionState.NeedsUser : ResourceExecutionState.Failed, _resourceExecutionOwner,
                    LogError("Resource reconciliation", error));
            }
            finally { _resourceReconciliationRunning = false; }
        }

        private PresetCandidate ComposePresetResourceDemands(PresetSnapshot snapshot, PresetCandidate baseline)
        {
            var allocation = baseline;
            if (baseline.PriorityFunding == null)
            {
                var plan = Settings.UseCustomRebirthPlan ? LoadRuntimeCustomPlan() : null;
                allocation = LoadoutPresetPlanner.ComposeResourceDemands(snapshot, baseline, IronPillBloodReserved,
                    CustomBloodGoalCatchUpRequested(plan));
            }
            var receipt = _globalPresetReceipt;
            if (allocation.ControlsMagic || !Settings.ManageMagic || receipt?.TemporaryMagicContext == null
                || (_resourceSetupExecutor.Application.BlockedLanes & 2) != 0) return allocation;
            var context = ResourceWriter.CaptureResourceContext(Character);
            if (!GlobalPresetEpochMatches(receipt.Policy) || context.Length != receipt.TemporaryMagicContext.Length
                || context.Where((item, i) => !ReferenceEquals(item, receipt.TemporaryMagicContext[i])).Any()
                || GlobalPresetLaneState(false) != receipt.TemporaryMagicAllocations)
            {
                var conflict = new ResourceMutationResult { Disposition = MutationDisposition.ForeignConflict,
                    AffectedLanes = 2, Reason = "Temporary Magic policy changed before release; current native allocations require review." };
                _resourceSetupExecutor.Application.RecordUnresolved(Character, conflict);
                throw new ResourceMutationException(conflict);
            }
            // Retire only the still-owned temporary policy before leaving this lane
            // uncontrolled. The earlier manual allocations are not a return target.
            allocation = allocation.Copy(); allocation.ControlsMagic = true;
            allocation.MagicShares = new long[allocation.MagicShares.Length]; allocation.MagicNgu = 0;
            allocation.Blood = 0; allocation.Ritual = -1;
            return allocation;
        }

        // Keep detailed exceptions separate from the concise automation status.
        private string _globalPresetReconcileException;

        private bool ReconcileGlobalPreset(string owner, string id, CustomPlanStage stage, int horizon, string reason)
        {
            var eventKey = owner + ":" + id;
            var reviewed = _globalPresetApplyingEvent == eventKey && _globalPresetApplyingPolicy != null
                && GlobalPresetEpochMatches(_globalPresetApplyingPolicy) ? _globalPresetApplyingPolicy : null;
            if (reviewed == null && (_globalPresetReceipt?.Owner != owner || _globalPresetReceipt.Id != id))
            {
                reviewed = RetainedPresetSelection(owner, id);
                if (reviewed != null) { _globalPresetApplyingPolicy = reviewed; _globalPresetApplyingEvent = eventKey; }
            }
            var state = "unavailable";
            try
            {
                var restorationIssue = ProtectedPresetRestorationIssue(owner);
                if (restorationIssue != null)
                {
                    SetResourceExecution(ResourceExecutionState.NeedsUser, owner, restorationIssue);
                    return false;
                }
                InvalidateGlobalNguWeightPolicies();
                var blocker = LoadoutPresetApplyBlocker();
                if (blocker != null || ManualAdventureOwnsEquipment && !ManualAdventureOverlayActive && !ExecutionSetupClaimsActive)
                {
                    SetResourceExecution(ResourceExecutionState.Waiting, owner, blocker ?? "Manual adventure owns equipment.");
                    return false;
                }
                if (reviewed == null && !PrepareGlobalPresetAdventureChange(owner))
                {
                    SetResourceExecution(ResourceExecutionState.NeedsUser, owner, GetCustomAutomationStatus());
                    return false;
                }
                var receipt = _globalPresetReceipt;
                var matching = reviewed == null && receipt?.Owner == owner && receipt.Id == id;
                if (matching && !ExecutionSetupClaimsActive && !ManualAdventureOverlayActive && !GlobalPresetTransitionStillOwned(receipt))
                {
                    if (!LoadoutManager.ExactLoadoutMatches(receipt.Gear))
                        RecordSelectedSetupConflict(Character, Character.stats.rebirthNumber, "gear", "Preset gear changed outside an authorized transition; review current setup.");
                    if (receipt.Support != GlobalPresetSupportState() && _presetSupportContinuation == null && !_presetSupportPresentationPending)
                        RecordSelectedSetupConflict(Character, Character.stats.rebirthNumber, "support", "Preset support changed outside an authorized transition; review current setup.");
                }
                if (matching && receipt.Policy.MaterialPersistencePending)
                {
                    try { PersistApprovedExecutionMaterialRevision(receipt.Policy, owner); receipt.Policy.MaterialPersistencePending = false; }
                    catch (Exception error)
                    {
                        SuspendCurrentGlobalPresetStage(owner);
                        SetResourceExecution(ResourceExecutionState.Waiting, owner, "Approved material is waiting for durable storage: " + error.Message);
                        return false;
                    }
                }
                if (matching && (GlobalPresetNativeTargetsYielded(owner)
                    || (_globalPresetNativeTargets != null || !_customEquipmentApplicationInvalidated)
                        && !GlobalPresetNativeTargetsMatch(receipt.Policy.Stage, owner)))
                {
                    SetResourceExecution(ResourceExecutionState.NeedsUser, owner,
                        "Preset allocation blocked: NativeTargetsChanged. Native/manual settings were preserved.");
                    ObserveGlobalPresetTargetConflict(owner);
                }
                var preset = matching ? receipt.Policy.Preset : reviewed?.Preset ?? CustomPlanStore?.PresetLibrary.Get(id);
                if (preset == null) throw new FormatException("Bound global preset is missing: " + id);
                var readiness = GlobalPresetReadiness(matching && !_customEquipmentApplicationInvalidated
                    ? receipt.Policy.ExecutionPreset : preset, stage, out state,
                    Settings.ManualAdventureEnabled || ExecutionRouteClaimActive || ExecutionSetupClaimsActive);
                if (readiness != null)
                {
                    WaitForGlobalPreset(eventKey, state, readiness, false);
                    SetResourceExecution(ResourceExecutionState.Waiting, owner, _globalPresetWaitingReason);
                    return false;
                }
                if (!GlobalPresetRetryReady(eventKey, state)) return false;
                if (matching && (GlobalPresetNativeTargetsYielded(owner) || ExecutionSetupClaimsActive || !_customEquipmentApplicationInvalidated
                    && (ManualAdventureOverlayActive ? ManualAdventureOverlayGearMatches() : GlobalPresetGearMatches(receipt))))
                {
                    var issue = GetGlobalPresetApplicationIssue(owner, false, true);
                    if (issue == PresetApplicationIssue.EpochChanged
                        || issue == PresetApplicationIssue.SelectionChanged || issue == PresetApplicationIssue.Blocked)
                    {
                        SetResourceExecution(issue == PresetApplicationIssue.Blocked ? ResourceExecutionState.Waiting : ResourceExecutionState.NeedsUser,
                            owner, "Preset allocation blocked: " + issue + ". Native/manual settings were preserved.");
                        return false;
                    }
                    if (!RefreshGlobalPresetAllocations(owner)) return false;
                }
                else
                {
                    EquipGlobalPresetDefinition(preset, owner, matching ? "temporary-owner-restoration" : reason,
                        false, out _, reviewed?.Stage ?? stage, reviewed?.Horizon ?? Math.Max(1, horizon));
                }
                if (!GlobalPresetReceiptValid(owner))
                {
                    var waitingReason = SelectedSetupConflicts.TryGetValue(Character, out var unresolved)
                        && unresolved.NativeSettingsOwner == owner ? unresolved.NativeSettings : "Selected setup prerequisites are not verified.";
                    SetResourceExecution(GlobalPresetNativeTargetsYielded(owner) ? ResourceExecutionState.NeedsUser : ResourceExecutionState.Waiting, owner, waitingReason);
                    return false;
                }
                SetResourceExecution(ResourceExecutionState.Applied, owner, "");
                _globalPresetReconcileException = null;
                return true;
            }
            catch (Exception error)
            {
                _globalPresetReconcileException = error.ToString();
                LogError("Preset reconciliation: " + owner, error);
                if (!(error is FormatException) && !(error is SelectedSetupAuthorityException)
                    && !(error is ResourceMutationException) && !(error is SupportApplicationException))
                {
                    WaitForGlobalPreset(eventKey, state, error.Message, true);
                    SetResourceExecution(ResourceExecutionState.Waiting, owner, _globalPresetWaitingReason);
                    return false;
                }
                BlockPresetExecution(eventKey, "definition", error);
                SetResourceExecution(ResourceExecutionState.NeedsUser, owner, _globalPresetFailure);
                return false;
            }
        }
    }
}
