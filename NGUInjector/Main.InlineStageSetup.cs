using System;
using System.Linq;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal partial class Main
    {
        private sealed class InlineStageSelection
        {
            internal string Owner, Context, Acquisition, PlanFile, Generation, BeardRequirements, ClaimIdentity, Blocked;
            internal long Entry;
            internal int Cycle;
            internal int GearGoal, FightScoreMode;
            internal readonly LoadoutManager.SnapshotEpoch Epoch = new LoadoutManager.SnapshotEpoch();
            internal CustomEquipmentSetup Policy;
            internal SelectedSetupRequirements Baseline, Applied, Observed;
            internal string[] Material;
            internal string[] ObservedMaterial;
            internal DateTime SupportAt, SupportAcceptedAt;
            internal int SupportRevision;
            internal bool SupportPending, Rotate;
        }

        private InlineStageSelection _inlineStageSelection;
        private Exception _inlineStageSetupError;

        private string InlineStageClaimIdentity() => ManualAdventureOverlayActive + ":" + Settings.ManualAdventureEnabled
            + ":" + Settings.ManualAdventureLoadout + ":" + LoadoutPresetNumbers(Settings.ManualAdventureGearWeights).ToString()
            + ":" + LoadoutPresetNumbers(Settings.ManualAdventureBeardWeights).ToString()
            + ":" + LoadoutPresetNumbers(Settings.ManualAdventureDiggerWeights).ToString();

        private void InvalidateInlineStageApplication()
        {
            if (_inlineStageSelection != null) _inlineStageSelection.Applied = null;
        }

        private bool InlineStageObservationMatches(InlineStageSelection selection)
        {
            var observed = selection.Observed;
            return observed == null || (!selection.Baseline.ControlsGear || LoadoutManager.ExactLoadoutMatches(observed.Gear)
                    && !observed.Gear.Where((item, i) => LoadoutPresetPrimitiveKey(item.equipment) != selection.ObservedMaterial[i]).Any())
                && CaptureSupportState().Selection.StableKey == observed.Support.StableKey;
        }

        private Action<ih[], SupportSelection> BeginInlineStageTransition()
        {
            var selection = _inlineStageSelection;
            if (!InlineStageEntryMatches(selection) || selection.Blocked != null) return null;
            if (!InlineStageObservationMatches(selection))
            {
                selection.Blocked = "Inline setup changed externally; restart the stage to review current authority.";
                return null;
            }
            return (gear, support) => {
                if (InlineStageEntryMatches(selection) && selection.Blocked == null)
                {
                    selection.Observed = new SelectedSetupRequirements { Gear = gear, Support = support };
                    selection.ObservedMaterial = gear.Select(x => LoadoutPresetPrimitiveKey(x.equipment)).ToArray();
                }
            };
        }

        private Action CaptureInlineMaterialRevision()
        {
            var selection = _inlineStageSelection;
            if (!InlineStageEntryMatches(selection) || selection.Blocked != null || selection.Observed == null
                || !selection.Baseline.ControlsGear || !selection.Baseline.MaterialValid() || !InlineStageObservationMatches(selection)) return null;
            var observed = selection.Observed;
            var accounting = ResourceWriter.AllocatedResourceKey();
            var context = ResourceWriter.CaptureResourceContext(Character);
            return () => {
                try
                {
                    if (!InlineStageEntryMatches(selection) || selection.Blocked != null || !ReferenceEquals(observed, selection.Observed)
                        || !LoadoutManager.ExactLoadoutMatches(observed.Gear)
                        || CaptureSupportState().Selection.StableKey != observed.Support.StableKey
                        || ResourceWriter.AllocatedResourceKey() != accounting) return;
                    var current = ResourceWriter.CaptureResourceContext(Character);
                    if (current.Length != context.Length || current.Where((item, i) => !ReferenceEquals(item, context[i])).Any()) return;
                    var instances = LoadoutPresetInstances().Values.ToArray();
                    if (selection.Baseline.Gear.Any(item => item.id != item.equipment.id
                        || item.id != 0 && instances.Count(x => ReferenceEquals(x, item.equipment)) != 1)) return;
                    var material = selection.Baseline.Gear.Select(x => LoadoutPresetPrimitiveKey(x.equipment)).ToArray();
                    var observedMaterial = observed.Gear.Select(x => LoadoutPresetPrimitiveKey(x.equipment)).ToArray();
                    if (material.SequenceEqual(selection.Material) && observedMaterial.SequenceEqual(selection.ObservedMaterial)) return;
                    selection.Material = material;
                    selection.ObservedMaterial = observedMaterial;
                    selection.Applied = null;
                    _customEquipmentApplicationInvalidated = true;
                }
                catch { /* Unverifiable post-action observations never revise acquired identity or mask the action's error. */ }
            };
        }

        private bool InlineStageSelectionCurrent(InlineStageSelection selection)
        {
            return InlineStageEntryMatches(selection)
                && Settings?.UseCustomRebirthPlan == true && !_customRequestedFocusGoal.HasValue
                && selection.Owner == _customStageEquipmentKey && selection.Context == _customStageKey;
        }

        private bool InlineStageEntryMatches(InlineStageSelection selection)
        {
            return selection != null && ReferenceEquals(selection, _inlineStageSelection)
                && ReferenceEquals(reference, this) && !_unloaded && selection.Epoch.Matches()
                && Settings?.CustomPlanFile == selection.PlanFile && _protectedPlanFile == selection.PlanFile
                && _protectedGeneration == selection.Generation && _protectedScheduler?.Entry == selection.Entry
                && _protectedScheduler.Cycle == selection.Cycle;
        }

        private bool InlineStageSetupVerified(CustomPlan plan)
        {
            var index = _protectedScheduler?.StageIndex ?? -1;
            return plan?.Stages != null && index >= 0 && index < plan.Stages.Length
                && InlineStageApplicationObserved(plan) && !_customEquipmentApplicationInvalidated
                && _inlineStageSelection.ClaimIdentity == InlineStageClaimIdentity()
                && InlineStagePolicyMatches(_inlineStageSelection, SetupForStage(plan.Stages[index]), plan.FightScoreMode);
        }

        private static bool InlineStagePolicyMatches(InlineStageSelection selection, CustomEquipmentSetup policy, int fightScoreMode)
        {
            return selection.GearGoal == policy.Stage.GearGoal && selection.FightScoreMode == fightScoreMode
                && selection.Policy.Beards.SequenceEqual(policy.Beards)
                && selection.Policy.Diggers.Select(x => x.Id + ":" + x.Weight).SequenceEqual(policy.Diggers.Select(x => x.Id + ":" + x.Weight));
        }

        private bool InlineStageApplicationObserved(CustomPlan plan)
        {
            var selection = _inlineStageSelection;
            try
            {
                return plan != null && InlineStageSelectionCurrent(selection) && selection.Blocked == null
                    && selection.Applied != null
                    && selection.Baseline.MaterialValid()
                    && LoadoutManager.ExactLoadoutMatches(selection.Applied.Gear)
                    && CaptureSupportState().Selection.StableKey == selection.Applied.Support.StableKey
                    && SupportProjection.Evaluate(CaptureSupportContext(), selection.Applied.Support,
                        selection.Applied.Budget, selection.Applied.EnforceSupportBudget).Feasible;
            }
            catch { return false; }
        }

        private bool ReconcileInlineStageSetup(CustomPlan plan, CustomStageRuntime runtime)
        {
            SuspendProtectedStageProgress();
            _inlineStageSetupError = null;
            var selection = _inlineStageSelection;
            try
            {
                var current = InlineStageSelectionCurrent(selection);
                if (current)
                {
                    if (selection.Blocked != null) throw new InvalidOperationException(selection.Blocked);
                    if (!selection.Baseline.MaterialValid())
                    {
                        selection.Blocked = "Acquired inline equipment changed; restart the stage to review a new selection.";
                        throw new InvalidOperationException(selection.Blocked);
                    }
                    if (!InlineStageObservationMatches(selection))
                    {
                        selection.Blocked = "Inline setup changed externally; restart the stage to review current authority.";
                        throw new InvalidOperationException(selection.Blocked);
                    }
                    if (_presetSupportContinuation != null && !SupportMutationStillOwned(_presetSupportContinuation.Mutation))
                    {
                        selection.Blocked = "Inline support continuation changed externally; restart the stage to review current authority.";
                        throw new InvalidOperationException(selection.Blocked);
                    }
                }
                var policy = SetupForStage(runtime.Stage);
                var gearChanged = !current || selection.GearGoal != runtime.Stage.GearGoal || selection.FightScoreMode != plan.FightScoreMode;
                var policyChanged = !current || !InlineStagePolicyMatches(selection, policy, plan.FightScoreMode);
                if (policyChanged)
                {
                    if (_resourceSetupExecutor.Application.BlockedLanes != 0)
                        throw new InvalidOperationException("Resource authority is unresolved; coupled inline setup changes are withheld.");
                    // Explicit setup edits replace intent, not the scheduler entry. Support-only
                    // edits retain selected gear; generic invalidation never authorizes a search.
                    var gear = !gearChanged ? selection.Baseline.Gear : runtime.Stage.GearGoal < 0 ? null
                        : SelectSetupGear((GearOptimizationGoal)runtime.Stage.GearGoal, (FightScoreMode)plan.FightScoreMode, true);
                    selection = new InlineStageSelection { Owner = _customStageEquipmentKey, Context = _customStageKey,
                        Acquisition = Guid.NewGuid().ToString("N"), Policy = policy, PlanFile = _protectedPlanFile,
                        GearGoal = runtime.Stage.GearGoal, FightScoreMode = plan.FightScoreMode,
                        Observed = current ? selection.Observed : null, ObservedMaterial = current ? selection.ObservedMaterial : null,
                        Generation = _protectedGeneration, Entry = _protectedScheduler.Entry, Cycle = _protectedScheduler.Cycle };
                    selection.Material = gear?.Select(x => LoadoutPresetPrimitiveKey(x.equipment)).ToArray();
                    var acquired = selection;
                    selection.Baseline = new SelectedSetupRequirements { Owner = selection.Owner, Gear = gear,
                        ControlsGear = gear != null, Acquisition = selection.Acquisition,
                        Recall = GearOptimizer.LegacyRecallFootprint(), ChangeSupport = true,
                        Budget = ActivityPolicy.Current.DiggerBudgetFraction,
                        Authorized = () => InlineStageSelectionCurrent(acquired) && acquired.Blocked == null,
                        MaterialValid = () => gear == null || !gear.Where((x, i) =>
                            LoadoutPresetPrimitiveKey(x.equipment) != acquired.Material[i]).Any(),
                        RouteRetirementAuthorized = () => InlineStageSelectionCurrent(acquired)
                            && ManualAdventureSupportRouteAuthorized(Settings.ManualAdventureEnabled, _globalPresetReceipt) };
                    _inlineStageSelection = selection;
                }
                var now = DateTime.UtcNow;
                var requirements = ResolveCustomBeardRequirements(selection.Policy.Beards);
                var key = string.Join(",", requirements);
                var rotationDue = (now - _customLastBeardRotation).TotalSeconds >= 60;
                if (!selection.SupportPending && key == selection.BeardRequirements
                    && !_presetSupportPresentationPending && _presetSupportContinuation == null
                    && !rotationDue && (now - selection.SupportAt).TotalSeconds < 60 && InlineStageSetupVerified(plan)) return true;
                if (_resourceSetupExecutor.Application.BlockedLanes != 0)
                    throw new InvalidOperationException("Resource authority is unresolved; coupled inline setup changes are withheld.");
                var context = CaptureSupportContext();
                SupportEvaluation planSupport(int[] beards, double[] weights) => SupportProjection.Plan(context, beards,
                    context.Diggers.Select(x => new DiggerBudgetRequest { Id = x.Id, Maximum = x.Maximum, Weight = weights[x.Id] }).ToArray(),
                    selection.Baseline.Budget);
                if (selection.SupportPending && !SupportProjection.Evaluate(context, selection.Baseline.Support, selection.Baseline.Budget, true).Feasible)
                {
                    if (_presetSupportContinuation != null && !SupportMutationStillOwned(_presetSupportContinuation.Mutation))
                        throw new InvalidOperationException("Pending inline support changed externally; affordable replacement was not authorized.");
                    var affordable = planSupport(selection.Baseline.Support.Beards, selection.Baseline.DiggerWeights);
                    if (!affordable.Feasible) throw new InvalidOperationException(affordable.Reason);
                    selection.Baseline.Support = affordable.Selection;
                    selection.SupportRevision++;
                }
                if (!selection.SupportPending && (selection.Baseline.Support == null
                    || key != selection.BeardRequirements || rotationDue || (now - selection.SupportAt).TotalSeconds >= 60
                    || !SupportProjection.Evaluate(context, selection.Baseline.Support, selection.Baseline.Budget, true).Feasible))
                {
                    if (_presetSupportPresentationPending) selection.SupportRevision++;
                    selection.Rotate = rotationDue;
                    var beards = context.BeardsAvailable
                        ? BeardManager.SelectBeards(requirements, selection.Rotate) : context.CurrentSelection.Beards;
                    var requests = selection.Policy.Diggers.ToList();
                    try
                    {
                        if (ZoneHelpers.TitansSpawningSoon().SpawningSoon)
                            requests.AddRange(ActivityPolicy.Current.ImminentTitanDiggers().Select(x => Digger(x.Id, x.Weight, x.Name)));
                    }
                    catch { /* Imminent-titan support is opportunistic, not an acquisition prerequisite. */ }
                    var weights = new double[12];
                    foreach (var request in requests) weights[request.Id] += request.Weight;
                    var support = planSupport(beards, weights);
                    if (!support.Feasible) throw new InvalidOperationException(support.Reason);
                    selection.Baseline.Support = support.Selection;
                    selection.Baseline.DiggerWeights = weights;
                    selection.BeardRequirements = key;
                    selection.SupportPending = true;
                    selection.SupportAcceptedAt = now;
                }

                BeginPresetSupportAcquisition(selection.Owner, selection.Acquisition + ":" + selection.SupportRevision);
                selection.Baseline.Recall = GearOptimizer.LegacyRecallFootprint();
                var desired = ResolveExecutionSetup(ComposeManualAdventureClaims(selection.Baseline));
                selection.Applied = null;
                ApplySelectedSetup(desired);
                RecordManualClaimApplication(desired);
                selection.Applied = desired;
                selection.Observed = new SelectedSetupRequirements { Gear = LoadoutManager.CaptureExactLoadout(), Support = desired.Support };
                selection.ObservedMaterial = selection.Observed.Gear.Select(x => LoadoutPresetPrimitiveKey(x.equipment)).ToArray();
                selection.ClaimIdentity = InlineStageClaimIdentity();
                if (selection.SupportPending)
                {
                    selection.SupportPending = false;
                    selection.SupportAt = selection.SupportAcceptedAt;
                    if (selection.Rotate) _customLastBeardRotation = selection.SupportAcceptedAt;
                }
                _customEquipmentApplicationInvalidated = false;
                return InlineStageSetupVerified(plan);
            }
            catch (Exception error)
            {
                _inlineStageSetupError = error;
                if (selection != null && (error is SelectedSetupException gear
                    && (gear.Outcome.Disposition == MutationDisposition.ForeignConflict || gear.Outcome.Disposition == MutationDisposition.UnknownOutcome)
                    || error is SupportApplicationException support
                    && (support.Result.Disposition == MutationDisposition.ForeignConflict || support.Result.Disposition == MutationDisposition.UnknownOutcome)))
                    selection.Blocked = "Inline setup requires review: " + error.Message;
                SetResourceExecution(selection?.Blocked == null ? ResourceExecutionState.Waiting : ResourceExecutionState.NeedsUser,
                    _customStageEquipmentKey, "Inline setup waiting: " + error.Message);
                return false;
            }
        }

        private bool RefreshInlineStageSupport(CustomPlan plan)
        {
            if (plan == null || !Settings.UseCustomRebirthPlan || !plan.StageSchedulerEnabled || CombatPrepActive)
                return false;
            var snapshot = CustomStageAllocator.GetSchedulerSnapshot(plan, Character.rebirthTime.totalseconds);
            var index = snapshot?.ActiveStageIndex ?? -1;
            if (index < 0 || index >= plan.Stages.Length || !string.IsNullOrEmpty(plan.Stages[index].PresetId)
                || plan.Stages[index].ExecutionMode != (int)CustomStageExecutionMode.Standard) return false;
            // The ordinary executor owns application and credit; this callback only
            // invalidates its observation when a new support event is due.
            if (_inlineStageSelection == null || !_inlineStageSelection.SupportPending
                && ((DateTime.UtcNow - _inlineStageSelection.SupportAt).TotalSeconds >= 60
                    || (DateTime.UtcNow - _customLastBeardRotation).TotalSeconds >= 60
                    || _inlineStageSelection.BeardRequirements != string.Join(",", ResolveCustomBeardRequirements(_inlineStageSelection.Policy.Beards))))
                _customEquipmentApplicationInvalidated = true;
            return true;
        }
    }
}
