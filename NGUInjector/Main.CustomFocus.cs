using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal enum CustomFocusGoal
    {
        AugmentsBloodMagic,
        TimeMachine,
        Wandoos,
        EnergyNgu,
        MagicNgu,
        Ngu,
        AdvancedTraining,
        Adventure,
        Fight,
        ManualTitans
    }

    internal partial class Main
    {
        private const int DefaultFocusExpectedSeconds = 300;
        private const int FocusSettleMilliseconds = 30;

        private const int BeardFuManchu = 0;
        private const int BeardDropChance = 1;
        private const int BeardReverseHitler = 2;
        private const int BeardNgu = 3;
        private const int BeardLadybeard = 4;
        private const int BeardAdventure = 5;
        private const int BeardGold = 6;

        private const int DiggerDropChance = 0;
        private const int DiggerWandoos = 1;
        private const int DiggerStat = 2;
        private const int DiggerAdventure = 3;
        private const int DiggerEnergyNgu = 4;
        private const int DiggerMagicNgu = 5;
        private const int DiggerEnergyBeard = 6;
        private const int DiggerMagicBeard = 7;
        private const int DiggerPp = 8;
        private const int DiggerDaycare = 9;
        private const int DiggerBlood = 10;
        private const int DiggerExp = 11;

        private CustomFocusGoal? _customSustainedFocusGoal;
        private CustomFocusGoal? _customRequestedFocusGoal;
        private CustomFocusGoal? _customAppliedFocusGoal;
        private string _customFocusStatus = "idle";
        private int _customFocusAugmentIndex = -1;
        private bool _customFocusAugmentUsesUpgrade;
        private int _customFocusBloodRitualIndex = -1;
        private int[] _customFocusEnergyNguWeights;
        private int[] _customFocusMagicNguWeights;
        private DateTime _customSustainedFocusLastStatus = DateTime.MinValue;
        private DateTime _customFocusLastResourceRefresh = DateTime.MinValue;
        private DateTime _customFocusNextRetry = DateTime.MinValue;
        private int _customFocusRetryCount;
        private bool _customFocusActivationPending;
        private Func<bool> _customFocusActivationContextMatches;
        private Func<bool> _customFocusEnergyCleanupOwned, _customFocusMagicCleanupOwned;
        private ResourceApplication _customFocusResourceApplication;
        private ResourceMutationResult _customFocusResourceOutcome;
        private sealed class FocusSetupSelection
        {
            internal CustomFocusGoal Goal;
            internal SelectedSetupRequirements Desired;
            internal readonly LoadoutManager.SnapshotEpoch Epoch = new LoadoutManager.SnapshotEpoch();
            internal string[] Material, ObservedMaterial;
            internal ih[] ObservedGear;
            internal string ObservedSupport;
            internal object Settings;
            internal string PlanFile;
            internal bool PersistencePending;
            internal string CommitmentKey;
        }
        private FocusSetupSelection _focusSetupSelection;

        private bool FocusSetupCurrent(FocusSetupSelection selection) => selection != null
            && ReferenceEquals(_focusSetupSelection, selection) && ReferenceEquals(reference, this) && !_unloaded
            && selection.Epoch.Matches() && _customRequestedFocusGoal == selection.Goal
            && ReferenceEquals(selection.Settings, Settings) && selection.PlanFile == Settings.CustomPlanFile;

        private bool FocusSetupObservationMatches(FocusSetupSelection selection) => LoadoutManager.ExactLoadoutMatches(selection.ObservedGear)
            && !selection.ObservedGear.Where((item, i) => LoadoutPresetPrimitiveKey(item.equipment) != selection.ObservedMaterial[i]).Any()
            && CaptureSupportState().StableKey == selection.ObservedSupport;

        private Action<ih[], SupportSelection> BeginFocusSetupTransition()
        {
            var selection = _focusSetupSelection;
            if (!FocusSetupCurrent(selection)) return null;
            if (!FocusSetupObservationMatches(selection))
            {
                RecordSelectedSetupConflict(Character, Character.stats.rebirthNumber, "gear", "Focus setup changed externally; review current setup.");
                return null;
            }
            return (gear, support) => {
                if (!FocusSetupCurrent(selection)) return;
                selection.ObservedGear = gear;
                selection.ObservedMaterial = gear.Select(item => LoadoutPresetPrimitiveKey(item.equipment)).ToArray();
                selection.ObservedSupport = CaptureSupportState().StableKey;
                if (_customFocusActivationPending) _customFocusActivationContextMatches = CaptureCustomFocusActivationContext();
            };
        }

        private Action CaptureFocusMaterialRevision()
        {
            var selection = _focusSetupSelection;
            if (!FocusSetupCurrent(selection) || !FocusSetupObservationMatches(selection)
                || !selection.Desired.MaterialValid()) return null;
            var accounting = ResourceWriter.AllocatedResourceKey(); var objects = ResourceWriter.CaptureResourceContext(Character);
            return () => {
                if (!FocusSetupCurrent(selection) || !LoadoutManager.ExactLoadoutMatches(selection.ObservedGear)
                    || CaptureSupportState().StableKey != selection.ObservedSupport || ResourceWriter.AllocatedResourceKey() != accounting) return;
                var current = ResourceWriter.CaptureResourceContext(Character);
                if (current.Length != objects.Length || current.Where((item, i) => !ReferenceEquals(item, objects[i])).Any()) return;
                selection.Material = selection.Desired.Gear.Select(item => LoadoutPresetPrimitiveKey(item.equipment)).ToArray();
                selection.ObservedMaterial = selection.ObservedGear.Select(item => LoadoutPresetPrimitiveKey(item.equipment)).ToArray();
                selection.PersistencePending = true;
                try { PersistFocusSelection(selection); }
                catch (Exception error)
                {
                    SuspendProtectedStageProgress(); _customFocusStatus = "waiting"; _customAppliedFocusGoal = null;
                    SetCustomAutomationStatus("Approved focus material is waiting for durable storage: " + error.Message);
                    LogError("Persist approved focus material", error);
                }
            };
        }

        private void PersistFocusSelection(FocusSetupSelection selection)
        {
            if (!selection.PersistencePending) return;
            if (selection.Desired.Gear.Where((x, i) => LoadoutPresetPrimitiveKey(x.equipment) != selection.Material[i]).Any())
                throw new InvalidOperationException("Pending approved focus material changed externally; review the selection.");
            PersistExecutionSetupSelection("focus", selection.Goal.ToString(), selection.Desired);
            selection.CommitmentKey = FocusCommitmentKey();
            selection.PersistencePending = false;
        }

        private string FocusCommitmentKey() => _customFocusAugmentIndex + ":" + _customFocusAugmentUsesUpgrade + ":" + _customFocusBloodRitualIndex
            + ":" + string.Join(",", Settings.EnergyNguWeights) + ":" + string.Join(",", Settings.MagicNguWeights)
            + ":" + Character.wandoos98.os + ":" + string.Join(",", Character.advancedTraining.levelTarget);

        private void ReconcileRetainedFocusSetup(CustomFocusGoal goal)
        {
            var selection = _focusSetupSelection;
            if (!FocusSetupCurrent(selection) || selection.Goal != goal)
                throw new InvalidOperationException("Focus setup is not acquired in the current native epoch; apply focus again.");
            PersistFocusSelection(selection);
            if (!selection.Desired.MaterialValid() || !FocusSetupObservationMatches(selection))
            {
                RecordSelectedSetupConflict(Character, Character.stats.rebirthNumber, "gear", "Retained focus setup changed externally; review current setup.");
                throw new SelectedSetupAuthorityException("gear", "Retained focus setup changed externally.");
            }
            if (SelectedSetupConflicts.TryGetValue(Character, out var conflict) && conflict.Rebirth == Character.stats.rebirthNumber
                && (conflict.Gear != null || conflict.Support != null))
                throw new SelectedSetupAuthorityException(conflict.Gear != null ? "gear" : "support", conflict.Gear ?? conflict.Support);
            PublishExecutionBaseline(selection.Desired);
            var desired = ResolveExecutionSetup(selection.Desired);
            if (LoadoutManager.ExactLoadoutMatches(desired.Gear) && CaptureSupportState().Selection.StableKey == desired.Support.StableKey)
                return;
            var preserved = goal == CustomFocusGoal.MagicNgu ? new FocusUnownedAllocations(Character) : null;
            if (preserved != null)
            {
                var error = GearOptimizer.CheckExactSwapCapacity(desired.Gear, preserved.EnergyTotal, preserved.Res3Total);
                if (error != null) throw new InvalidOperationException(error);
            }
            BeginPresetSupportAcquisition(desired.Owner, desired.Acquisition);
            ApplySelectedSetup(new SelectedSetupRequirements { Owner = desired.Owner, Acquisition = desired.Acquisition,
                Gear = desired.Gear, Support = desired.Support, ChangeSupport = true, Budget = desired.Budget,
                EnforceSupportBudget = desired.EnforceSupportBudget, MaterialValid = desired.MaterialValid,
                ResolvedClaims = desired.ResolvedClaims,
                Recall = desired.ResolvedClaims != null ? GearRecallFootprint.None : (goal != CustomFocusGoal.MagicNgu && Settings.ManageEnergy ? GearRecallFootprint.Energy : 0)
                    | (Settings.ManageMagic ? GearRecallFootprint.Magic : 0),
                Authorized = () => FocusSetupCurrent(selection) && !CombatPrepActive && !Settings.UseCustomRebirthPlan,
                GearAppliedValid = () => preserved == null || preserved.Matches(Character), RouteRetirementAuthorized = () => false });
            _customFocusLastResourceRefresh = DateTime.MinValue;
        }
        private ResourceSetupExecutor _sharedResourceSetup;
        private ResourceSetupExecutor _resourceSetupExecutor => _sharedResourceSetup
            ?? (_sharedResourceSetup = new ResourceSetupExecutor());

        // Selected requirements, not a saved return setup. Preset/focus producers
        // select; this ingress owns the ordered native gear/support transition.
        internal sealed class SelectedSetupRequirements
        {
            internal ExecutionClaim Claim;
            internal ExecutionGrant Grant;
            internal ExecutionClaim[] ResolvedClaims;
            internal SelectedSetupRequirements Copy() => (SelectedSetupRequirements)MemberwiseClone();
            internal string Owner;
            internal ih[] Gear;
            internal bool ControlsGear = true;
            internal string Acquisition;
            internal double[] DiggerWeights;
            internal Func<bool> MaterialValid;
            internal GearRecallFootprint Recall;
            internal SupportSelection Support;
            internal int[] RequiredBeards;
            internal double Budget;
            internal bool ChangeSupport;
            internal bool ControlsSupport;
            internal bool EnforceSupportBudget = true;
            // A temporary producer may retire its own manual route, never a
            // titan/quest route. This does not grant allocation authority.
            internal bool OwnsManualRoute;
            internal bool RetireRoute;
            internal Func<bool> Authorized;
            internal Func<bool> BoundaryAuthorized;
            internal Func<bool> RouteRetirementAuthorized;
            internal Func<bool> GearAppliedValid;
            internal GearMutationOutcome GearOutcome;
            internal ResourceMutationResult ResourceOutcome;
            internal Action<ih[], SupportSelection> ObserveProgress;
            internal Action ObserveRouteRetirement;
        }
        private SelectedSetupRequirements _selectedSetup;
        private Func<bool> _selectedSetupSupportAuthorized;
        private sealed class SelectedSetupConflict
        {
            internal long Rebirth, Revision;
            internal string Gear, Support, Route;
            internal string NativeSettings, NativeSettingsOwner;
            internal string GearObservation, SupportObservation, RouteObservation;
        }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, SelectedSetupConflict>
            SelectedSetupConflicts = new System.Runtime.CompilerServices.ConditionalWeakTable<object, SelectedSetupConflict>();
        private Func<string, bool> _selectedSetupReviewAuthorized;

        private static long SelectedSetupConflictVersion(Character model) => model?.stats != null
            && SelectedSetupConflicts.TryGetValue(model, out var conflict) && conflict.Rebirth == model.stats.rebirthNumber ? conflict.Revision : 0;

        private static void RecordSelectedSetupConflict(Character model, long rebirth, string domain, string reason)
        {
            if (model?.stats == null || model.stats.rebirthNumber != rebirth) return;
            reason = string.IsNullOrEmpty(reason) ? "Native " + domain + " authority changed; review current setup." : reason;
            var conflict = SelectedSetupConflicts.GetValue(model, _ => new SelectedSetupConflict { Rebirth = rebirth });
            if (conflict.Rebirth != rebirth)
            {
                conflict.Rebirth = rebirth; conflict.Gear = conflict.Support = conflict.Route = null;
                conflict.NativeSettings = conflict.NativeSettingsOwner = null;
                conflict.GearObservation = conflict.SupportObservation = conflict.RouteObservation = null;
            }
            string observed;
            try
            {
                observed = domain == "gear" ? string.Join("|", LoadoutManager.CaptureExactLoadout().Select(x => x.slot + ":" + x.id + ":"
                        + (x.id == 0 ? "empty" : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(x.equipment) + ":" + LoadoutPresetPrimitiveKey(x.equipment))))
                    : domain == "route" ? model.adventure.zone + ":" + model.adventure.itopodStart + ":" + model.adventure.itopodEnd + ":" + model.adventure.autoattacking
                    : CaptureSupportState().StableKey;
            }
            catch { observed = "unavailable"; }
            if (domain == "gear" && conflict.Gear == reason && conflict.GearObservation == observed
                || domain == "route" && conflict.Route == reason && conflict.RouteObservation == observed
                || domain != "gear" && domain != "route" && conflict.Support == reason && conflict.SupportObservation == observed) return;
            conflict.Revision++;
            if (domain == "gear") { conflict.Gear = reason; conflict.GearObservation = observed; }
            else if (domain == "route") { conflict.Route = reason; conflict.RouteObservation = observed; }
            else { conflict.Support = reason; conflict.SupportObservation = observed; }
        }
        private sealed class SelectedSetupAuthorityException : InvalidOperationException
        {
            internal readonly string Domain;
            internal SelectedSetupAuthorityException(string domain, string reason) : base("Selected setup requires review: " + reason) { Domain = domain; }
        }
        private sealed class SelectedSetupException : InvalidOperationException
        {
            internal readonly GearMutationOutcome Outcome;
            internal SelectedSetupException(GearMutationOutcome outcome) : base(outcome.Reason, outcome.Error) { Outcome = outcome; }
        }

        private void RequireSelectedSetupRoute(ih[] gear, SupportSelection support, bool changeSupport, Func<bool> routeAuthorized, bool ownsManualRoute = false, bool retireRoute = false)
        {
            if (Character?.adventure == null || Controller == null)
                throw new InvalidOperationException("Selected setup adventure observation is unavailable.");
            if (!retireRoute && LoadoutManager.ExactLoadoutMatches(gear)
                && (!changeSupport || CaptureSupportState().Selection.StableKey == support.StableKey)) return;
            // StopBossPush only stops automation flags. It is not evidence that
            // native combat (including a user-started nuke) has actually ended.
            if (Character.bossController == null || Character.bossController.isFighting || Character.bossController.nukeBoss)
                throw new InvalidOperationException("Selected setup is waiting for observed native boss retirement.");
            if (Character.adventure.zone == -1) return;
            // A non-safe native zone is activity, not implicit ownership granted by
            // disabled automation flags. Unknown combat retains its prerequisites.
            var controller = Character.adventureController;
            if (controller?.zoneSelector == null || controller.playerController == null || controller.enemyAI == null)
                throw new InvalidOperationException("Selected setup route retirement controllers are unavailable.");
            if (_activeSetupGrant != null && !ValidateExecutionGrant(_activeSetupGrant, ExecutionDomain.Route)
                || routeAuthorized == null || !routeAuthorized())
                throw new InvalidOperationException("Selected setup is waiting for the current adventure owner.");
        }

        private void ApplySelectedSetup(SelectedSetupRequirements wanted)
        {
            wanted = ResolveExecutionSetup(wanted);
            if (!wanted.ControlsGear) wanted.Gear = LoadoutManager.CaptureExactLoadout();
            var previous = _activeSetupGrant;
            wanted.Grant = GrantSelectedSetup(wanted);
            _activeSetupGrant = wanted.Grant;
            try { ApplySelectedSetupCore(wanted); }
            finally { _activeSetupGrant = previous; }
        }

        private void ApplySelectedSetupCore(SelectedSetupRequirements wanted)
        {
            if (wanted.RequiredBeards != null && wanted.RequiredBeards.Except(wanted.Support.Beards).Any())
                throw new InvalidOperationException("Required beard prerequisites do not fit the acquired support selection.");
            if (!wanted.ControlsGear) wanted.Gear = LoadoutManager.CaptureExactLoadout();
            _selectedSetup = wanted;
            var grant = wanted.Grant;
            var needsSafeRoute = false;
            var needsRetiredBoss = false;
            var supportBefore = CaptureSupportState();
            var supportObjects = SupportObjects();
            var nativeModel = Character; var rebirth = Character.stats.rebirthNumber;
            var retirementOnly = wanted.RetireRoute && wanted.Recall == GearRecallFootprint.None && !wanted.ChangeSupport
                && LoadoutManager.ExactLoadoutMatches(wanted.Gear);
            SelectedSetupConflict conflict = null;
            var conflicted = !retirementOnly && SelectedSetupConflicts.TryGetValue(nativeModel, out conflict) && conflict.Rebirth == rebirth
                && (wanted.ControlsGear || wanted.ChangeSupport) && (conflict.Gear != null || conflict.Support != null);
            var review = _selectedSetupReviewAuthorized;
            var reviewed = (conflicted || conflict?.Rebirth == rebirth && conflict.Route != null) && review?.Invoke(wanted.Owner) == true;
            if (conflicted && !reviewed)
                throw new SelectedSetupAuthorityException(conflict.Gear != null ? "gear" : "support", conflict.Gear ?? conflict.Support);
            Action acceptReview = () => {
                if (reviewed && ReferenceEquals(review, _selectedSetupReviewAuthorized) && review(wanted.Owner))
                {
                    if (wanted.ControlsGear) { conflict.Gear = null; conflict.GearObservation = null; }
                    if (wanted.ChangeSupport || wanted.ControlsSupport) { conflict.Support = null; conflict.SupportObservation = null; }
                    if ((grant.Domains & ExecutionDomain.Route) != 0) { conflict.Route = null; conflict.RouteObservation = null; }
                    conflict.Revision++;
                    _selectedSetupReviewAuthorized = null;
                }
            };
            var supportPhase = false; var gearPhase = false;
            var expectedGear = LoadoutManager.CaptureExactLoadout();
            var gearMaterial = expectedGear.Select(x => LoadoutPresetPrimitiveKey(x.equipment)).ToArray();
            var accounting = ResourceWriter.AllocatedResourceKey();
            var resourceObjects = ResourceWriter.CaptureResourceContext(Character);
            var allocations = ResourceWriter.CaptureAllocations(Character);
            Func<bool> gearOwned = () => LoadoutManager.ExactLoadoutMatches(expectedGear)
                && !expectedGear.Where((item, i) => LoadoutPresetPrimitiveKey(item.equipment) != gearMaterial[i]).Any();
            Func<bool> resourcesOwned = () => {
                var current = ResourceWriter.CaptureResourceContext(Character);
                return current.Length == resourceObjects.Length
                    && !current.Where((item, i) => !ReferenceEquals(item, resourceObjects[i])).Any()
                    && ResourceWriter.AllocatedResourceKey() == accounting;
            };
            Func<ResourceMutationResult> recordResourceConflict = () => {
                if (!ReferenceEquals(Character, nativeModel)) return null;
                var lanes = 0;
                var disposition = MutationDisposition.ForeignConflict;
                try
                {
                    var current = ResourceWriter.CaptureResourceContext(Character);
                    if (current.Length != resourceObjects.Length || current.Where((item, i) => !ReferenceEquals(item, resourceObjects[i])).Any()) lanes = 7;
                    var observed = ResourceWriter.CaptureAllocations(Character);
                    lanes |= allocations.Where(pair => pair.Value == null ? observed[pair.Key] != null
                        : observed[pair.Key] == null || !pair.Value.SequenceEqual(observed[pair.Key]))
                        .Aggregate(0, (mask, pair) => mask | (1 << ResourceWriter.Lane(pair.Key)));
                }
                catch { lanes = 7; disposition = MutationDisposition.UnknownOutcome; }
                if (lanes == 0) return null;
                wanted.ResourceOutcome = new ResourceMutationResult { Disposition = disposition,
                    AffectedLanes = lanes, Reason = "Resource authority changed across a selected setup callback." };
                _resourceSetupExecutor.Application.RecordUnresolved(nativeModel, wanted.ResourceOutcome);
                return wanted.ResourceOutcome;
            };
            Func<bool> crossDomainOwned = () => {
                if (!supportPhase)
                {
                    var current = SupportObjects();
                    return supportBefore.StableKey == CaptureSupportState().StableKey && current.Length == supportObjects.Length
                        && !current.Where((item, i) => !ReferenceEquals(item, supportObjects[i])).Any()
                        && (gearPhase || gearOwned() && resourcesOwned());
                }
                return gearOwned() && resourcesOwned();
            };
            Func<bool> authorized = () => ValidateExecutionGrant(grant, grant.Domains) && ReferenceEquals(_selectedSetup, wanted)
                && (wanted.BoundaryAuthorized == null || wanted.BoundaryAuthorized())
                && (wanted.MaterialValid == null || wanted.MaterialValid())
                && (!reviewed || ReferenceEquals(review, _selectedSetupReviewAuthorized) && review(wanted.Owner))
                && crossDomainOwned()
                && (retirementOnly || _resourceSetupExecutor.Application.BlockedLanes == 0)
                && (!needsSafeRoute || Character?.adventure?.zone == -1)
                && (!needsRetiredBoss || Character?.bossController != null
                    && !Character.bossController.isFighting && !Character.bossController.nukeBoss)
                && Controller != null && !Controller.midDrag;
            Action require = () => {
                if (authorized()) return;
                if (!gearPhase || supportPhase)
                {
                    var foreignGear = !gearOwned();
                    if (foreignGear) RecordSelectedSetupConflict(nativeModel, rebirth, "gear", "Gear changed across a selected setup callback.");
                    var resourceConflict = recordResourceConflict();
                    if (resourceConflict != null) throw new ResourceMutationException(resourceConflict);
                    if (foreignGear) throw new SelectedSetupAuthorityException("gear", "Gear changed across a selected setup callback.");
                }
                throw new InvalidOperationException("Selected setup authority changed; native transition stopped.");
            };
            require();
            var recordInlineTransition = BeginInlineStageTransition();
            var recordFocusTransition = BeginFocusSetupTransition();
            var recordPresetTransition = BeginPresetSetupTransition() ?? BeginExecutionCapacityPresetTransition(wanted);
            Action<ih[], SupportSelection> recordProgress = (items, support) => {
                recordInlineTransition?.Invoke(items, support);
                recordFocusTransition?.Invoke(items, support);
                recordPresetTransition?.Invoke(items, support);
                ObserveExecutionClaimProgress(wanted, items);
                if (_customSupportObserved != null) _customSupportObserved = CaptureSupportState().StableKey;
                if (_customBeardObservedSupport != null) _customBeardObservedSupport = CaptureSupportState().StableKey;
                wanted.ObserveProgress?.Invoke(items, support);
            };
            var gearChanged = !LoadoutManager.ExactLoadoutMatches(wanted.Gear);
            var supportChanged = CaptureSupportState().Selection.StableKey != wanted.Support.StableKey;
            if (gearChanged || wanted.ChangeSupport && supportChanged || wanted.RetireRoute && Character.adventure.zone != -1)
            {
                SuspendProtectedStageProgress();
                if (wanted.RetireRoute && Character.adventure.zone != -1 && wanted.RouteRetirementAuthorized?.Invoke() != true)
                    throw new InvalidOperationException("Selected route release authority changed.");
                RequireSelectedSetupRoute(wanted.Gear, wanted.Support, wanted.ChangeSupport, wanted.RouteRetirementAuthorized, wanted.OwnsManualRoute, wanted.RetireRoute);
                needsRetiredBoss = true;
                if (Character.adventure.zone != -1)
                {
                    require();
                    RequireSelectedSetupRoute(wanted.Gear, wanted.Support, wanted.ChangeSupport, wanted.RouteRetirementAuthorized, wanted.OwnsManualRoute, wanted.RetireRoute);
                    Character.adventureController.zoneSelector.changeZone(-1);
                    require();
                    if (Character.adventure.zone != -1) throw new InvalidOperationException("Selected setup could not observe safe route retirement.");
                }
                needsSafeRoute = true;
                wanted.ObserveRouteRetirement?.Invoke();
            }
            require();
            gearPhase = true;
            var gear = LoadoutManager.ApplyExactLoadoutObserved(wanted.Gear, wanted.Recall, authorized: authorized);
            wanted.GearOutcome = gear;
            if (!gear.Verified)
            {
                if (gear.Disposition == MutationDisposition.ForeignConflict || gear.Disposition == MutationDisposition.UnknownOutcome)
                    RecordSelectedSetupConflict(nativeModel, rebirth, "gear", gear.Reason);
                if (gear.KnownProgressStillOwned() && authorized())
                    recordProgress(LoadoutManager.CaptureExactLoadout(), supportBefore.Selection);
                throw new SelectedSetupException(gear);
            }
            require();
            if (wanted.GearAppliedValid != null && !wanted.GearAppliedValid())
                throw new InvalidOperationException("Non-owned energy/R3 pools changed during Magic NGU gear application.");
            expectedGear = LoadoutManager.CaptureExactLoadout();
            gearMaterial = expectedGear.Select(x => LoadoutPresetPrimitiveKey(x.equipment)).ToArray();
            // Pool refill/depletion is reobserved for funding after support. Changes
            // to owned bars or model identity during support are not adopted.
            accounting = ResourceWriter.AllocatedResourceKey();
            resourceObjects = ResourceWriter.CaptureResourceContext(Character);
            allocations = ResourceWriter.CaptureAllocations(Character);
            supportPhase = true;
            // Verified gear progress is owned even if support rejects before its
            // first write. Retaining it prevents a retry from calling it foreign.
            recordProgress(expectedGear, supportBefore.Selection);
            if (!wanted.ChangeSupport)
            {
                if (CaptureSupportState().Selection.StableKey != wanted.Support.StableKey)
                    throw new InvalidOperationException("Current support changed after preflight; Keep current did not overwrite it.");
                recordProgress(expectedGear, wanted.Support);
                acceptReview();
                return;
            }
            var previous = _selectedSetupSupportAuthorized;
            _selectedSetupSupportAuthorized = authorized;
            try { ApplyPresetDesiredSupport(wanted.Support, wanted.Budget, wanted.EnforceSupportBudget); }
            catch (SupportApplicationException error)
            {
                if (error.Result.Disposition == MutationDisposition.ForeignConflict || error.Result.Disposition == MutationDisposition.UnknownOutcome)
                {
                    RecordSelectedSetupConflict(nativeModel, rebirth, "support", error.Message);
                    if (!gearOwned()) RecordSelectedSetupConflict(nativeModel, rebirth, "gear", "Gear changed during selected support application.");
                    recordResourceConflict();
                }
                if (error.Result.Disposition == MutationDisposition.KnownPartial && authorized()
                    && SupportMutationStillOwned(error.Result.Mutation))
                    recordProgress(expectedGear, CaptureSupportState().Selection);
                throw;
            }
            finally { _selectedSetupSupportAuthorized = previous; }
            require();
            recordProgress(expectedGear, wanted.Support);
            acceptReview();
        }

        private void RefreshSelectedFocusSupport(CustomFocusGoal goal)
        {
            if (ExecutionOwnsDomain(ExecutionDomain.Support)) return;
            if (FocusSetupCurrent(_focusSetupSelection) && !FocusSetupObservationMatches(_focusSetupSelection))
            {
                RecordSelectedSetupConflict(Character, Character.stats.rebirthNumber, "support", "Focus setup changed before support refresh.");
                throw new SelectedSetupAuthorityException("support", "Focus setup changed before support refresh.");
            }
            var support = SelectFocusSupport(SetupForFocus(goal));
            if (FocusSetupCurrent(_focusSetupSelection) && _focusSetupSelection.Goal == goal)
            { _focusSetupSelection.Desired.Support = support; _focusSetupSelection.Desired.Budget = ActivityPolicy.Current.DiggerBudgetFraction; }
            BeginPresetSupportAcquisition("focus:" + goal, "focus:" + goal + ":" + support.StableKey);
            ApplySelectedSetup(new SelectedSetupRequirements { Owner = "focus:" + goal,
                Gear = LoadoutManager.CaptureExactLoadout(), Recall = GearRecallFootprint.None,
                Support = support, ChangeSupport = true, Budget = ActivityPolicy.Current.DiggerBudgetFraction,
                Authorized = () => FocusSetupCurrent(_focusSetupSelection) && _focusSetupSelection.Goal == goal });
        }

        private SupportSelection SelectFocusSupport(CustomEquipmentSetup setup, int[] selectedBeards = null)
        {
            var requests = setup.Diggers.ToList();
            try
            {
                if (ZoneHelpers.TitansSpawningSoon().SpawningSoon)
                    requests.AddRange(ActivityPolicy.Current.ImminentTitanDiggers().Select(x => Digger(x.Id, x.Weight, x.Name)));
            }
            catch { /* Opportunistic titan timing is not a setup prerequisite. */ }
            var context = CaptureSupportContext();
            var beards = selectedBeards ?? setup.Beards;
            if ((setup.RequiredBeards ?? new int[0]).Except(beards).Any())
                throw new InvalidOperationException("Required beard prerequisites are missing from selected support.");
            var planned = SupportProjection.Plan(context, beards, requests.Select(x => new DiggerBudgetRequest {
                Id = x.Id, Weight = x.Weight, Maximum = context.Diggers.Single(d => d.Id == x.Id).Maximum
            }).ToArray(), ActivityPolicy.Current.DiggerBudgetFraction);
            if (!planned.Feasible) throw new InvalidOperationException(planned.Reason);
            return planned.Selection;
        }

        // Shared resource-domain actuator ingress for acquired presets and focus. Setup
        // selection remains with producers; neither producer owns a resource retry engine.
        private ResourceMutationResult ReconcileResourceSetup(ResourceSetupIntent intent)
        {
            _executionBaselineResourceIntent = new ResourceSetupIntent { Owner = intent.Owner, Energy = intent.Energy, Magic = intent.Magic,
                Res3 = intent.Res3, Desired = intent.Desired.ToDictionary(x => x.Key, x => x.Value?.ToArray()) };
            intent = ComposeExecutionResources(intent);
            if (!ExecutionCapacityFundingSafe(intent))
                return new ResourceMutationResult { Disposition = MutationDisposition.RejectedUnchanged,
                    Reason = "Current funding must preserve the delayed native capacity-depletion buffer." };
            var settings = Settings;
            var stage = _customStageKey;
            var focus = _customRequestedFocusGoal;
            var customPlan = settings.UseCustomRebirthPlan;
            var planFile = settings.CustomPlanFile;
            var authority = intent.Authorized;
            var gear = LoadoutManager.CaptureExactLoadout();
            var material = gear.Select(x => LoadoutPresetPrimitiveKey(x.equipment)).ToArray();
            var support = CaptureSupportState().StableKey;
            var domains = (intent.Energy ? ExecutionDomain.Energy : 0) | (intent.Magic ? ExecutionDomain.Magic : 0)
                | (intent.Res3 ? ExecutionDomain.Res3 : 0);
            var grant = ResolveExecutionGrant(intent.Owner, null, domains, () => stage == _customStageKey && focus == _customRequestedFocusGoal
                && customPlan == settings.UseCustomRebirthPlan && planFile == settings.CustomPlanFile
                && authority != null && authority()
                && LoadoutManager.ExactLoadoutMatches(gear)
                && !gear.Where((x, i) => LoadoutPresetPrimitiveKey(x.equipment) != material[i]).Any()
                && CaptureSupportState().StableKey == support
                && _executionClaims.Where(x => !x.Ended && x.ResourcePolicy != null && ((int)x.Domains & (int)domains) != 0)
                    .All(x => x.ResourcePolicy.Authorized == null || x.ResourcePolicy.Authorized()),
                resolvedClaims: _executionClaims.Where(x => !x.Ended && (x.Domains & domains) != 0).ToArray());
            intent.Authorized = () => ValidateExecutionGrant(grant, domains);
            return _resourceSetupExecutor.Reconcile(intent);
        }

        internal ResourceMutationResult ReconcileStageResourceSetup(ResourceSetupIntent intent)
        {
            return ReconcileResourceSetup(intent);
        }

        internal bool ResourceSetupPresentationPending => _resourceSetupExecutor.Application.PresentationPending;

        internal CustomFocusGoal? GetActiveCustomFocusGoal()
        {
            return _customRequestedFocusGoal;
        }

        internal string GetCustomFocusStatus() { return _customFocusStatus; }
        internal string GetAppliedCustomFocusGoal() { return _customAppliedFocusGoal?.ToString() ?? string.Empty; }

        internal string ToggleCustomFocusGoal(CustomFocusGoal goal)
        {
            if (_customRequestedFocusGoal == goal)
                return StopCustomFocusGoal();

            return ApplyCustomFocusGoal(goal);
        }

        internal string StopCustomFocusGoal()
        {
            RequireCombatPrepIdle();
            var requested = _customRequestedFocusGoal;
            var selection = _focusSetupSelection;
            ClearPersistedExecutionSelection("focus");
            var globalMessage = StopManualGlobalPreset();
            if (_customRequestedFocusGoal != requested || !ReferenceEquals(_focusSetupSelection, selection))
                return globalMessage ?? "Focus changed; newer intent was preserved.";
            if (!requested.HasValue)
            {
                if (globalMessage == null) return "No sustained focus is active.";
                SetCustomAutomationStatus(globalMessage);
                Log(globalMessage);
                PublishExternalUiRuntime();
                return globalMessage;
            }

            var goal = requested.Value;
            var label = FocusGoalLabel(goal);
            var releaseMessage = string.Empty;
            try
            {
                if (SustainedFocusEnabled(goal)) releaseMessage = ReleaseOwnedCustomFocusResources(goal);
                if (goal == CustomFocusGoal.Fight) StopBossPush();
            }
            catch (Exception e)
            {
                releaseMessage = " Resource recall failed; focus was still stopped. Review remaining allocations.";
                LogError($"Stop custom focus {goal}", e);
            }

            var message = $"Stopped {label} focus.{releaseMessage}";
            if (_customRequestedFocusGoal != goal || !ReferenceEquals(_focusSetupSelection, selection)) return message;
            ClearCustomSustainedFocus();
            SetCustomAutomationStatus(message);
            Log(message);
            PublishExternalUiRuntime();
            return message;
        }

        internal string ApplyCustomFocusGoal(CustomFocusGoal goal)
        {
            RequireCombatPrepIdle();
            var messages = new List<string>();
            var existingOwner = _customSustainedFocusGoal ?? _customAppliedFocusGoal;
            var handoffStarted = false;
            try
            {
                if (SustainedFocusEnabled(goal) && LoadReadyNguFocusPlan(goal) == null)
                    throw new InvalidOperationException("NGU focus requires loaded resource data and a readable active plan.");
                if (SustainedFocusEnabled(goal) && !Settings.ManageMagic
                    && (!Settings.ManageEnergy || goal == CustomFocusGoal.MagicNgu))
                    throw new InvalidOperationException("Sustained focus has no managed resource lane.");
                if (_resourceSetupExecutor.Application.BlockedLanes != 0)
                    throw new InvalidOperationException("Resource authority is unresolved; coupled focus setup was not changed.");
                var setup = SetupForFocus(goal);
                ih[] magicGear = null;
                FocusUnownedAllocations preserved = null;
                var retained = _customFocusActivationPending && _customRequestedFocusGoal == goal
                    && _customFocusActivationContextMatches != null && _customFocusActivationContextMatches()
                    && FocusSetupCurrent(_focusSetupSelection) ? _focusSetupSelection.Desired : null;
                if (retained != null)
                {
                    magicGear = retained.Gear;
                    if (goal == CustomFocusGoal.MagicNgu) preserved = new FocusUnownedAllocations(Character);
                }
                else if (goal == CustomFocusGoal.MagicNgu)
                {
                    preserved = new FocusUnownedAllocations(Character);
                    magicGear = SelectFocusGear((GearOptimizationGoal)setup.Stage.GearGoal);
                    var failure = GearOptimizer.CheckExactSwapCapacity(magicGear, preserved.EnergyTotal, preserved.Res3Total);
                    if (failure != null) throw new InvalidOperationException(failure);
                }
                else magicGear = SelectFocusGear((GearOptimizationGoal)setup.Stage.GearGoal);
                var selectedSupport = retained?.Support ?? SelectFocusSupport(setup);
                // This explicit manual request may take over an unowned native route,
                // but unavailable retirement dependencies must reject before pausing
                // owners or preparing native AT inputs.
                RequireSelectedSetupRoute(magicGear, selectedSupport, true, () => true);
                if (CustomStageAllocator.UnmanagedMagicBlocksAt(setup.Stage, 0, 0))
                    throw new InvalidOperationException("AT blocked: unmanaged magic is allocated to Wandoos; recall it manually or enable managed magic.");
                // A read-only rejection above must not retire the previous focus or its receipts.
                ClearPersistedExecutionSelection("focus");
                handoffStarted = true;
                _customFocusEnergyCleanupOwned = _customFocusMagicCleanupOwned = null;
                if (_customRequestedFocusGoal != goal || !_customFocusActivationPending)
                    _customFocusRetryCount = 0;
                // Pausing an enabled plan can clear focus through settings callbacks.
                // Publish the new request only after that old-owner invalidation.
                PauseCustomRebirthForManualFocus(messages);
                _customRequestedFocusGoal = goal;
                _customAppliedFocusGoal = null;
                _customSustainedFocusGoal = null;
                _customFocusStatus = "pending";
                _customFocusActivationContextMatches = null;
                ResetCustomSustainedFocusState();
                StopBossPush();
                PublishExternalUiRuntime();

                InvalidateCustomEquipmentApplication();
                BeginPresetSupportAcquisition("focus:" + goal, "focus:" + goal + ":" + selectedSupport.StableKey);
                var focusSelection = new FocusSetupSelection { Goal = goal, Settings = Settings, PlanFile = Settings.CustomPlanFile, PersistencePending = true };
                focusSelection.Material = magicGear.Select(item => LoadoutPresetPrimitiveKey(item.equipment)).ToArray();
                focusSelection.ObservedGear = LoadoutManager.CaptureExactLoadout();
                focusSelection.ObservedMaterial = focusSelection.ObservedGear.Select(item => LoadoutPresetPrimitiveKey(item.equipment)).ToArray();
                focusSelection.ObservedSupport = CaptureSupportState().StableKey;
                focusSelection.Desired = new SelectedSetupRequirements { Owner = "focus:" + goal, Gear = magicGear,
                    Acquisition = retained?.Acquisition ?? Guid.NewGuid().ToString("N"),
                    Recall = goal == CustomFocusGoal.MagicNgu
                        ? (Settings.ManageMagic ? GearRecallFootprint.Magic : GearRecallFootprint.None)
                        : GearOptimizer.LegacyRecallFootprint(),
                    Support = selectedSupport, ChangeSupport = true, Budget = ActivityPolicy.Current.DiggerBudgetFraction,
                    GearAppliedValid = () => preserved == null || preserved.Matches(Character),
                    RouteRetirementAuthorized = () => true,
                    Authorized = () => FocusSetupCurrent(focusSelection),
                    MaterialValid = () => !magicGear.Where((item, i) => LoadoutPresetPrimitiveKey(item.equipment) != focusSelection.Material[i]).Any() };
                _focusSetupSelection = focusSelection;
                PublishExecutionBaseline(focusSelection.Desired);
                ClearPersistedExecutionSelection("dashboard");
                ClearPersistedExecutionSelection("manual");
                PersistFocusSelection(focusSelection);
                var atBlocker = CustomStageAllocator.PrepareAtEntry(setup.Stage, 0, 0);
                if (atBlocker != null) throw new InvalidOperationException(atBlocker);
                ApplySelectedSetup(focusSelection.Desired);
                if (preserved != null && !preserved.Matches(Character))
                    throw new InvalidOperationException("Non-owned energy/R3 pools changed during Magic NGU gear application.");
                messages.Add($"equipped {GearOptimizer.GoalLabels[setup.Stage.GearGoal]} gear/support");
                ResetCustomEquipmentSetup();
                if (goal == CustomFocusGoal.Fight)
                {
                    StartBossPush();
                    messages.Add("started boss push");
                }
                if (goal == CustomFocusGoal.ManualTitans)
                    messages.Add("manual titan focus ready");

                if (SustainedFocusEnabled(goal))
                    messages.Add(AllocateCustomFocusResources(goal, stage: setup.Stage));

                if (goal == CustomFocusGoal.Adventure)
                {
                    Settings.CustomAdventureEnabled = true;
                    RouteConfiguredAdventureGoal("Manual Adventure focus");
                }

                SetSustainedFocus(goal, messages);
                _customRequestedFocusGoal = goal;
                _customAppliedFocusGoal = goal;
                _customFocusStatus = "applied";
                _customFocusActivationPending = false;
                _customFocusActivationContextMatches = null;
                _customFocusRetryCount = 0;
                _customFocusNextRetry = DateTime.MinValue;

                var summary = $"Focus {FocusGoalLabel(goal)}: {string.Join("; ", messages.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray())}";
                SetCustomAutomationStatus(summary);
                Log(summary);
                PublishExternalUiRuntime();
                return summary;
            }
            catch (Exception e)
            {
                if (!ReferenceEquals(reference, this) || _unloaded || Settings == null)
                    return "Focus authority retired during application: " + e.Message;
                if (handoffStarted && SustainedFocusEnabled(goal))
                {
                    _customFocusActivationPending = !(e is ResourceMutationException)
                        && (!(e is SelectedSetupException setupFailure)
                            || setupFailure.Outcome.Disposition == MutationDisposition.KnownPartial
                            || setupFailure.Outcome.Disposition == MutationDisposition.RejectedUnchanged)
                        && (!(e is SupportApplicationException supportFailure)
                            || supportFailure.Result.Disposition == MutationDisposition.KnownPartial
                            || supportFailure.Result.Disposition == MutationDisposition.RejectedUnchanged);
                    MarkCustomFocusFailed(goal);
                    if (_customFocusActivationPending)
                    {
                        _customFocusActivationContextMatches = CaptureCustomFocusActivationContext();
                        if (_customFocusActivationContextMatches == null) _customFocusNextRetry = DateTime.MaxValue;
                    }
                    else if (e is ResourceMutationException mutation && mutation.Result.Disposition != MutationDisposition.RejectedUnchanged)
                        _customFocusNextRetry = DateTime.MaxValue;
                }
                else if (_customFocusActivationPending && _customRequestedFocusGoal == goal)
                    MarkCustomFocusFailed(goal);
                else if (handoffStarted || !existingOwner.HasValue)
                {
                    ClearCustomSustainedFocus();
                    _customRequestedFocusGoal = goal;
                    _customFocusStatus = "failed";
                }
                var error = LogError($"Apply custom focus {goal}", e);
                if (_customFocusActivationPending && _customFocusActivationContextMatches == null)
                    error += " Apply the focus again to continue.";
                SetCustomAutomationStatus(error);
                PublishExternalUiRuntime();
                return error;
            }
        }

        private void SetSustainedFocus(CustomFocusGoal goal, List<string> messages)
        {
            if (SustainedFocusEnabled(goal))
            {
                _customSustainedFocusGoal = goal;
                messages.Add("sustained resource focus active");
                return;
            }

            ClearCustomSustainedFocus();
        }

        private sealed class FocusUnownedAllocations
        {
            internal readonly IReadOnlyList<long> Energy;
            internal readonly IReadOnlyList<long> Res3;
            private readonly long energyIdle, energyCurrent, res3Idle, res3Current;
            internal decimal EnergyTotal => Math.Max(energyCurrent, energyIdle);
            internal decimal Res3Total => Math.Max(res3Current, res3Idle);

            internal FocusUnownedAllocations(Character c)
            {
                energyIdle = c.idleEnergy;
                energyCurrent = c.curEnergy;
                res3Idle = c.res3?.idleRes3 ?? 0;
                res3Current = c.res3?.curRes3 ?? 0;
                var energy = new List<long>();
                energy.AddRange(c.training.attackEnergy);
                energy.AddRange(c.training.defenseEnergy);
                energy.AddRange(c.advancedTraining.energy);
                energy.AddRange(c.augments.augs.SelectMany(x => new[] { x.augEnergy, x.upgradeEnergy }));
                energy.Add(c.wandoos98.wandoosEnergy);
                energy.Add(c.machine.speedEnergy);
                energy.AddRange(c.NGU.skills.Select(x => x.energy));
                energy.AddRange(c.wishes.wishes.Select(x => x.energy));
                Energy = energy.AsReadOnly();
                Res3 = (c.hacks?.hacks.Select(x => x.res3) ?? Enumerable.Empty<long>())
                    .Concat(c.wishes.wishes.Select(x => x.res3)).ToList().AsReadOnly();
                if (Energy.Any(x => x < 0) || Res3.Any(x => x < 0)
                    || energyIdle < 0 || energyCurrent < 0 || res3Idle < 0 || res3Current < 0
                    || Energy.Aggregate(0m, (sum, value) => sum + value) != (decimal)energyCurrent - energyIdle
                    || Res3.Aggregate(0m, (sum, value) => sum + value) != (decimal)res3Current - res3Idle)
                    throw new InvalidOperationException("Cannot account for all non-owned energy/R3 allocations; Magic NGU focus was not started.");
            }

            internal bool Matches(Character c)
            {
                var actual = new FocusUnownedAllocations(c);
                return Energy.SequenceEqual(actual.Energy) && Res3.SequenceEqual(actual.Res3)
                    && energyIdle == actual.energyIdle && energyCurrent == actual.energyCurrent
                    && res3Idle == actual.res3Idle && res3Current == actual.res3Current;
            }
        }

        private static bool SustainedFocusEnabled(CustomFocusGoal goal)
        {
            switch (goal)
            {
                case CustomFocusGoal.TimeMachine:
                case CustomFocusGoal.AugmentsBloodMagic:
                case CustomFocusGoal.Wandoos:
                case CustomFocusGoal.EnergyNgu:
                case CustomFocusGoal.MagicNgu:
                case CustomFocusGoal.Ngu:
                case CustomFocusGoal.AdvancedTraining:
                case CustomFocusGoal.Adventure:
                    return true;
                default:
                    return false;
            }
        }

        private void RunCustomSustainedFocusAllocation()
        {
            RunCustomSustainedFocusAllocation(false);
        }

        private void RunCustomSustainedFocusAllocation(bool recallResources)
        {
            if (!ReferenceEquals(reference, this) || _unloaded) return;
            ServiceAutomaticRecovery();
            if (CombatPrepActive) return;
            if (Settings == null || Character?.rebirthTime == null)
                return;

            if (Settings.UseCustomRebirthPlan && !_customFocusActivationPending)
            {
                ClearCustomSustainedFocus();
                return;
            }

            if (!_customSustainedFocusGoal.HasValue)
                return;

            if (DateTime.UtcNow < _customFocusNextRetry)
                return;

            var goal = _customSustainedFocusGoal.Value;
            try
            {
                // Missing data is a pre-mutation wait, not a failed allocation or retry attempt.
                var plan = LoadReadyNguFocusPlan(goal);
                if (plan == null)
                    return;
                if (_customFocusActivationPending && (_customFocusActivationContextMatches == null
                    || !_customFocusActivationContextMatches()))
                {
                    _customFocusActivationContextMatches = null;
                    if (_customFocusEnergyCleanupOwned != null && !_customFocusEnergyCleanupOwned()) _customFocusEnergyCleanupOwned = null;
                    if (_customFocusMagicCleanupOwned != null && !_customFocusMagicCleanupOwned()) _customFocusMagicCleanupOwned = null;
                    _customFocusNextRetry = DateTime.MaxValue;
                    _customFocusStatus = "failed";
                    SetCustomAutomationStatus($"Focus {FocusGoalLabel(goal)} context changed; apply the focus again to continue.");
                    PublishExternalUiRuntime();
                    return;
                }
                if (CustomQuickResourceRefreshBlocked(null))
                    return;
                if (!ExecutionSetupClaimsActive || ExecutionSetupVerified()) ReconcileRetainedFocusSetup(goal);
                if (_customFocusActivationPending)
                {
                    _customFocusActivationPending = false;
                    _customFocusActivationContextMatches = null;
                }
                var weightsChanged = Settings.ManageEnergy && goal != CustomFocusGoal.MagicNgu
                    && (_customFocusEnergyNguWeights == null || !Settings.EnergyNguWeights.SequenceEqual(_customFocusEnergyNguWeights))
                    || Settings.ManageMagic && (_customFocusMagicNguWeights == null || !Settings.MagicNguWeights.SequenceEqual(_customFocusMagicNguWeights));
                if (!weightsChanged && !recallResources
                    && (DateTime.UtcNow - _customFocusLastResourceRefresh).TotalSeconds < CustomQuickResourceRefreshSeconds)
                    return;
                if (weightsChanged)
                {
                    _customFocusLastResourceRefresh = DateTime.MinValue;
                    _customSustainedFocusLastStatus = DateTime.MinValue;
                }

                // Even the idle-resource tick must rebalance to preempt lower priorities as caps grow.
                var message = AllocateCustomFocusResources(goal, recallResources);
                if (ExecutionSetupClaimsActive && !ExecutionSetupVerified())
                {
                    _customAppliedFocusGoal = null;
                    _customFocusStatus = "waiting";
                    SetCustomAutomationStatus("Independent focus resources were serviced; temporary setup prerequisites require review.");
                    return;
                }
                var recovered = _customFocusStatus == "failed";
                _customRequestedFocusGoal = goal;
                _customAppliedFocusGoal = goal;
                _customFocusStatus = "applied";
                _customFocusRetryCount = 0;
                _customFocusNextRetry = DateTime.MinValue;
                if (!string.IsNullOrEmpty(message) && ShouldUpdateCustomFocusStatus())
                    SetCustomAutomationStatus($"Sustaining {FocusGoalLabel(goal)}: {message}");
                if (weightsChanged || recovered)
                    PublishExternalUiRuntime();
            }
            catch (Exception e)
            {
                if (!ReferenceEquals(reference, this) || _unloaded || Settings == null) return;
                MarkCustomFocusFailed(goal);
                // A lane-local conflict is retained by the executor. Continue bounded
                // passes for independent lanes, without admitting the full focus.
                if (e is ResourceMutationException mutation && mutation.Result.Disposition == MutationDisposition.UnknownOutcome
                    && (_customFocusResourceApplication?.BlockedLanes & 3) == 3)
                    _customFocusNextRetry = DateTime.MaxValue;
                SetCustomAutomationStatus(LogError($"Sustain custom focus {goal}", e));
                PublishExternalUiRuntime();
            }
        }

        private void MarkCustomFocusFailed(CustomFocusGoal goal)
        {
            _customRequestedFocusGoal = _customSustainedFocusGoal = goal;
            _customAppliedFocusGoal = null;
            _customFocusStatus = "failed";
            if (Settings.ManageEnergy && goal != CustomFocusGoal.MagicNgu)
            {
                _customFocusEnergyNguWeights = null;
                BlackBarAllocator.ClearNguAppliedWeights(true);
            }
            if (Settings.ManageMagic)
            {
                _customFocusMagicNguWeights = null;
                BlackBarAllocator.ClearNguAppliedWeights(false);
            }
            _customFocusRetryCount = Math.Min(5, _customFocusRetryCount + 1);
            _customFocusNextRetry = DateTime.UtcNow.AddSeconds(Math.Min(30d,
                CustomQuickResourceRefreshSeconds * Math.Pow(2d, _customFocusRetryCount - 1)));
        }

        private static Func<bool> CaptureCustomFocusActivationContext()
        {
            try
            {
                var epoch = new LoadoutManager.SnapshotEpoch();
                var settings = Settings;
                var store = CustomPlanStore;
                var planFile = Settings.CustomPlanFile;
                Func<bool[]> policy = () => new[] {Settings.ManualAdventureEnabled, Settings.CustomAdventureEnabled,
                    Settings.UseCustomRebirthPlan, Settings.AutoRebirth, Settings.ManageEnergy, Settings.ManageMagic, Settings.ManageR3};
                var expectedPolicy = policy();
                var gear = LoadoutManager.CaptureExactLoadout().Select(x => new ih {
                    slot = x.slot, id = x.id, level = x.level, equipment = x.equipment
                }).OrderBy(x => x.slot).ToArray();
                var support = CaptureSupportState().StableKey;
                var resources = CustomFocusActivationAllocationMap();
                return () =>
                {
                    try
                    {
                        if (!ReferenceEquals(settings, Settings) || !ReferenceEquals(store, CustomPlanStore)
                            || planFile != Settings.CustomPlanFile || !expectedPolicy.SequenceEqual(policy()) || !epoch.Matches())
                            return false;
                        var actual = LoadoutManager.CaptureExactLoadout().OrderBy(x => x.slot).ToArray();
                        return gear.Length == actual.Length && gear.Zip(actual, (expected, observed) =>
                            expected.slot == observed.slot && expected.id == observed.id && expected.level == observed.level
                            && ReferenceEquals(expected.equipment, observed.equipment)).All(x => x)
                            && support == CaptureSupportState().StableKey && resources == CustomFocusActivationAllocationMap();
                    }
                    catch { return false; }
                };
            }
            catch { return null; }
        }

        private static string CustomFocusActivationAllocationMap()
        {
            var c = Character;
            // Observe allocations, not idle generation. These values never authorize snapshot restoration.
            return string.Join(";", new[] {
                c.training.attackEnergy, c.training.defenseEnergy, c.advancedTraining.energy,
                c.augments.augs.SelectMany(x => new[] {x.augEnergy, x.upgradeEnergy}).ToArray(),
                new[] {c.machine.speedEnergy, c.machine.goldMultiMagic, c.wandoos98.wandoosEnergy, c.wandoos98.wandoosMagic},
                c.NGU.skills.Select(x => x.energy).ToArray(), c.NGU.magicSkills.Select(x => x.magic).ToArray(),
                c.bloodMagic.ritual.Select(x => x.magic).ToArray(),
                c.wishes.wishes.SelectMany(x => new[] {x.energy, x.magic, x.res3}).ToArray(),
                c.hacks?.hacks.Select(x => x.res3).ToArray() ?? new long[0]
            }.Select(amounts => string.Join(",", amounts)));
        }

        private static string CustomFocusResourceAllocationMap(bool energy)
        {
            var c = Character;
            var lanes = energy ? new[] {
                c.training.attackEnergy, c.training.defenseEnergy, c.advancedTraining.energy,
                c.augments.augs.SelectMany(x => new[] {x.augEnergy, x.upgradeEnergy}).ToArray(),
                new[] {c.machine.speedEnergy, c.wandoos98.wandoosEnergy},
                c.NGU.skills.Select(x => x.energy).ToArray(), c.wishes.wishes.Select(x => x.energy).ToArray()
            } : new[] {
                new[] {c.machine.goldMultiMagic, c.wandoos98.wandoosMagic},
                c.NGU.magicSkills.Select(x => x.magic).ToArray(), c.bloodMagic.ritual.Select(x => x.magic).ToArray(),
                c.wishes.wishes.Select(x => x.magic).ToArray()
            };
            return string.Join(";", lanes.Select(amounts => string.Join(",", amounts)));
        }

        private static Func<bool> CaptureCustomFocusResourceOwnership(bool energy)
        {
            var epoch = new LoadoutManager.SnapshotEpoch();
            var allocations = CustomFocusResourceAllocationMap(energy);
            return () =>
            {
                try
                {
                    return Settings != null && (energy ? Settings.ManageEnergy : Settings.ManageMagic)
                        && epoch.Matches() && allocations == CustomFocusResourceAllocationMap(energy);
                }
                catch { return false; }
            };
        }

        private string ReleaseOwnedCustomFocusResources(CustomFocusGoal goal)
        {
            var preserved = false;
            var released = false;
            Exception failure = null;
            var selection = _focusSetupSelection;
            // Retire only this acquisition's cleanup rights before any native callback.
            var cleanup = new[] { _customFocusEnergyCleanupOwned, _customFocusMagicCleanupOwned };
            _customFocusEnergyCleanupOwned = _customFocusMagicCleanupOwned = null;
            foreach (var energy in new[] {true, false})
            {
                if (energy && goal == CustomFocusGoal.MagicNgu) continue;
                var owned = cleanup[energy ? 0 : 1];
                if (owned == null)
                {
                    preserved = true;
                    continue;
                }
                try
                {
                    var recalled = ResourceWriter.Execute(() =>
                    {
                        if (_customRequestedFocusGoal != goal || !ReferenceEquals(_focusSetupSelection, selection) || !owned()) return false;
                        BlackBarAllocator.ClearNguAppliedWeights(energy);
                        var epoch = new LoadoutManager.SnapshotEpoch();
                        var result = ReconcileResourceSetup(new ResourceSetupIntent { Owner = "focus-release:" + goal,
                            Desired = ResourceWriter.EmptyDesiredMap(energy, !energy), Energy = energy, Magic = !energy,
                            Authorized = () => epoch.Matches() && _customRequestedFocusGoal == goal
                                && ReferenceEquals(_focusSetupSelection, selection)
                                && !Settings.UseCustomRebirthPlan && !CombatPrepActive });
                        if (!result.Verified) throw new ResourceMutationException(result);
                        return result.Verified;
                    });
                    if (!recalled)
                    {
                        preserved = true;
                        continue;
                    }
                    released = true;
                    FocusSettle();
                }
                catch (Exception error) { failure = failure ?? error; }
            }
            if (failure != null) throw new InvalidOperationException("Focused resource recall failed.", failure);
            return !preserved ? " Released focused resources." : released
                ? " Released verified focused resources; review allocations left untouched."
                : " Unverified or changed allocations were left untouched; review them.";
        }

        private void ClearCustomSustainedFocus()
        {
            _focusSetupSelection = null;
            _customSustainedFocusGoal = null;
            _customRequestedFocusGoal = null;
            _customAppliedFocusGoal = null;
            _customFocusStatus = "idle";
            _customFocusNextRetry = DateTime.MinValue;
            _customFocusRetryCount = 0;
            _customFocusActivationPending = false;
            _customFocusActivationContextMatches = null;
            _customFocusEnergyCleanupOwned = _customFocusMagicCleanupOwned = null;
            ResetCustomSustainedFocusState();
        }

        private void ResetCustomSustainedFocusState()
        {
            _customFocusResourceApplication = _resourceSetupExecutor.Application; _customFocusResourceOutcome = null;
            BlackBarAllocator.ResetExcessIdle();
            _customFocusAugmentIndex = -1;
            _customFocusAugmentUsesUpgrade = false;
            _customFocusBloodRitualIndex = -1;
            _customFocusEnergyNguWeights = null;
            _customFocusMagicNguWeights = null;
            _customSustainedFocusLastStatus = DateTime.MinValue;
            _customFocusLastResourceRefresh = DateTime.MinValue;
        }

        private bool ShouldUpdateCustomFocusStatus()
        {
            var now = DateTime.UtcNow;
            if ((now - _customSustainedFocusLastStatus).TotalSeconds < 1d)
                return false;

            _customSustainedFocusLastStatus = now;
            return true;
        }

        private void RememberAugmentFocusTarget(AugmentOptimizerResult result)
        {
            if (result == null || !result.Success)
                return;

            _customFocusAugmentIndex = result.AugmentIndex;
            _customFocusAugmentUsesUpgrade = result.UsesUpgrade;
        }

        private void RememberBloodFocusTarget(BloodMagicOptimizerResult result)
        {
            if (result == null || !result.Success)
                return;

            _customFocusBloodRitualIndex = result.RitualIndex;
        }

        private string AllocateCustomFocusResources(CustomFocusGoal goal, bool settle = true, CustomPlanStage stage = null)
        {
            var messages = new List<string>();
            var plan = LoadReadyNguFocusPlan(goal);
            if (plan == null)
                throw new InvalidOperationException("NGU focus requires loaded resource data and a readable active plan.");
            if (!Settings.ManageMagic && (!Settings.ManageEnergy || goal == CustomFocusGoal.MagicNgu))
                throw new InvalidOperationException("Sustained focus has no managed resource lane.");
            var energyWeights = Settings.EnergyNguWeights.ToArray();
            var magicWeights = Settings.MagicNguWeights.ToArray();
            stage = stage ?? FocusSetupStage(goal);
            // A new attempt retires old cleanup permission before any recall or partial transfer.
            _customFocusEnergyCleanupOwned = _customFocusMagicCleanupOwned = null;
            var atBlocker = CustomStageAllocator.UnmanagedMagicBlocksAt(stage, 0, 0) ? CustomStageAllocator.PrepareAtEntry(stage, 0, 0) : null;
            if (atBlocker != null) throw new InvalidOperationException(atBlocker);
            var catchUpBloodGoals = CustomBloodGoalCatchUpRequested(plan);
            var desired = new Dictionary<ResourceTarget, long[]>();
            var lanePlans = new List<BlackBarAllocator.PriorityResourcePlan>();
            var settings = Settings; var owner = reference; var requested = _customRequestedFocusGoal;
            var manageEnergy = settings.ManageEnergy; var manageMagic = settings.ManageMagic;
            var epoch = new LoadoutManager.SnapshotEpoch();
            var blocksWandoos = false;
            foreach (var energy in new[] {true, false})
            {
                if (energy ? !Settings.ManageEnergy || goal == CustomFocusGoal.MagicNgu : !Settings.ManageMagic)
                    continue;

                var priorities = CustomStageAllocator.EffectiveStagePriorities(stage, energy, 0, 0, out var atGate, IronPillBloodReserved, catchUpBloodGoals);
                blocksWandoos |= atGate;
                if (priorities == null || priorities.Length == 0)
                    continue;
                var lanePlan = BlackBarAllocator.CalculatePriorities(priorities, energy,
                    BlackBarAllocator.DefaultLookaheadSeconds,
                    (available, goldBudget) => EnsureCustomFocusBloodTarget(available, goldBudget) ? _customFocusBloodRitualIndex : -1,
                    SelectCustomFocusAugmentTarget);
                lanePlans.Add(lanePlan);
                foreach (var pair in lanePlan.Desired) desired.Add(pair.Key, pair.Value);
                if (!string.IsNullOrWhiteSpace(lanePlan.Message)) messages.Add(lanePlan.Message);
            }

            if (blocksWandoos && manageMagic && !desired.ContainsKey(ResourceTarget.WandoosMagic))
                desired.Add(ResourceTarget.WandoosMagic, new long[1]);
            if (FocusSetupCurrent(_focusSetupSelection) && _focusSetupSelection.CommitmentKey != FocusCommitmentKey())
            {
                _focusSetupSelection.PersistencePending = true;
                PersistFocusSelection(_focusSetupSelection);
            }
            _customFocusResourceApplication = _resourceSetupExecutor.Application;
            _customFocusResourceOutcome = ReconcileResourceSetup(new ResourceSetupIntent { Owner = "focus:" + goal,
                Desired = desired, Energy = manageEnergy && goal != CustomFocusGoal.MagicNgu, Magic = manageMagic,
                Authorized = () => epoch.Matches() && ReferenceEquals(settings, Settings) && ReferenceEquals(owner, reference)
                    && requested == _customRequestedFocusGoal });
            if (!_customFocusResourceOutcome.Verified) throw new ResourceMutationException(_customFocusResourceOutcome);
            if (!ExecutionBaselineResourceMapMatches(_executionBaselineResourceIntent))
                throw new ResourceMutationException(new ResourceMutationResult { Disposition = MutationDisposition.RejectedUnchanged,
                    Reason = "Temporary resource policy is funded; full focus prerequisites are waiting." });
            foreach (var lanePlan in lanePlans)
            {
                BlackBarAllocator.ObserveApplied(lanePlan);
                if (lanePlan.Energy) _customFocusEnergyCleanupOwned = CaptureCustomFocusResourceOwnership(true);
                else _customFocusMagicCleanupOwned = CaptureCustomFocusResourceOwnership(false);
            }

            if (goal != CustomFocusGoal.MagicNgu && Settings.ManageEnergy)
                _customFocusEnergyNguWeights = energyWeights;
            if (Settings.ManageMagic)
                _customFocusMagicNguWeights = magicWeights;
            _customFocusLastResourceRefresh = DateTime.UtcNow;
            return messages.Count == 0 ? null : string.Join("; ", messages.ToArray());
        }

        private AugmentOptimizerResult SelectCustomFocusAugmentTarget(long availableEnergy)
        {
            var result = _customFocusAugmentIndex >= 0
                ? AugmentOptimizer.OptimizeForActualBudget(CustomAugmentsBloodEstimateSeconds(), availableEnergy,
                    BlackBarAllocator.DefaultLookaheadSeconds, _customFocusAugmentIndex, _customFocusAugmentUsesUpgrade)
                : null;
            if (result == null || !result.Success)
            {
                _customFocusAugmentIndex = -1;
                _customFocusAugmentUsesUpgrade = false;
                result = AugmentOptimizer.OptimizeForActualBudget(CustomAugmentsBloodEstimateSeconds(), availableEnergy,
                    BlackBarAllocator.DefaultLookaheadSeconds);
            }

            RememberAugmentFocusTarget(result);
            return result;
        }

        private static int CustomAugmentsBloodEstimateSeconds()
        {
            return Settings == null ? DefaultFocusExpectedSeconds : Settings.CustomAugmentsBloodEstimateSeconds;
        }

        private bool EnsureCustomFocusBloodTarget(long availableMagic, double goldBudget)
        {
            var result = _customFocusBloodRitualIndex >= 0
                ? BloodMagicOptimizer.OptimizeTarget(_customFocusBloodRitualIndex, availableMagic, goldBudget)
                : null;
            if (result == null || !result.Success)
            {
                _customFocusBloodRitualIndex = -1;
                result = BloodMagicOptimizer.OptimizeBest(availableMagic, goldBudget);
            }

            RememberBloodFocusTarget(result);
            return result.Success;
        }

        private static string FocusGoalLabel(CustomFocusGoal goal)
        {
            switch (goal)
            {
                case CustomFocusGoal.AugmentsBloodMagic:
                    return "Augments + Blood Magic";
                case CustomFocusGoal.TimeMachine:
                    return "Time Machine";
                case CustomFocusGoal.Wandoos:
                    return "Wandoos";
                case CustomFocusGoal.EnergyNgu:
                    return "Energy NGU";
                case CustomFocusGoal.MagicNgu:
                    return "Magic NGU";
                case CustomFocusGoal.Ngu:
                    return "All NGU";
                case CustomFocusGoal.AdvancedTraining:
                    return "Advanced Training";
                case CustomFocusGoal.Adventure:
                    return "Adventure";
                case CustomFocusGoal.Fight:
                    return "Fight";
                case CustomFocusGoal.ManualTitans:
                    return "Manual Titans";
                default:
                    return goal.ToString();
            }
        }

        private void PauseCustomRebirthForManualFocus(List<string> messages)
        {
            if (Settings.AutoRebirth)
            {
                Settings.AutoRebirth = false;
                messages.Add("auto-rebirth paused");
            }

            if (Settings.UseCustomRebirthPlan)
            {
                Settings.UseCustomRebirthPlan = false;
                messages.Add("custom rebirth plan paused");
            }

            if (Settings.ManualAdventureEnabled)
            {
                Settings.ManualAdventureEnabled = false;
                messages.Add("manual adventure paused");
            }

            ResetCustomPreRebirthPrep();
            _customStageKey = null;
            InvalidateInlineStageApplication();
            CustomStageAllocator.ResetResourceTargets();
        }

        private ih[] SelectFocusGear(GearOptimizationGoal goal)
        {
            return SelectSetupGear(goal, GetActiveFightScoreMode());
        }

        private ih[] SelectSetupGear(GearOptimizationGoal goal, FightScoreMode fightScoreMode, bool useCache = false)
        {
            var result = useCache ? GearOptimizer.GetCachedOrOptimize(goal, fightScoreMode) : GearOptimizer.Optimize(goal, fightScoreMode);
            if (!result.Success) throw new InvalidOperationException(result.Message);
            return SelectedGearMap(result);
        }

        private ih[] SelectedGearMap(GearOptimizerResult result)
        {
            var weapon = -5; var accessory = 10000;
            var destinations = result.Items.Select(item => new ih {
                slot = item.equipment.type == part.Head ? -1 : item.equipment.type == part.Chest ? -2
                    : item.equipment.type == part.Legs ? -3 : item.equipment.type == part.Boots ? -4
                    : item.equipment.type == part.Weapon ? weapon-- : accessory++,
                equipment = item.equipment, id = item.id, name = item.name, level = item.level }).ToArray();
            return LoadoutManager.ComposeExactLoadout(LoadoutManager.CaptureExactLoadout(), destinations);
        }

        private GearOptimizerResult ApplyManualGearSelection(GearOptimizationGoal goal, FightScoreMode mode)
        {
            var result = GearOptimizer.Optimize(goal, mode);
            if (!result.Success) return result;
            var desired = new SelectedSetupRequirements { Owner = "manual-gear", Gear = SelectedGearMap(result),
                Acquisition = Guid.NewGuid().ToString("N"),
                Recall = GearOptimizer.LegacyRecallFootprint(), Support = CaptureSupportState().Selection,
                ChangeSupport = false, Authorized = () => ReferenceEquals(reference, this) && !_unloaded,
                RouteRetirementAuthorized = () => true };
            var material = desired.Gear.Select(x => LoadoutPresetPrimitiveKey(x.equipment)).ToArray();
            desired.MaterialValid = () => !desired.Gear.Where((x, i) => LoadoutPresetPrimitiveKey(x.equipment) != material[i]).Any();
            RequireSelectedSetupRoute(desired.Gear, desired.Support, false, desired.RouteRetirementAuthorized);
            PersistExecutionSetupSelection("manual", goal + ":" + mode, desired);
            InvalidateCustomEquipmentApplication();
            ApplySelectedSetup(desired);
            if (desired.GearOutcome.LegacyFailure != null)
                throw new InvalidOperationException(desired.GearOutcome.LegacyFailure, desired.GearOutcome.Error);
            return result;
        }

        private static void FocusSettle()
        {
            Thread.Sleep(FocusSettleMilliseconds);
        }

        private static DiggerManager.DiggerRequest Digger(int id, int weight, string name)
        {
            return new DiggerManager.DiggerRequest {Id = id, Weight = weight, Name = name};
        }

        private CustomPlan LoadReadyNguFocusPlan(CustomFocusGoal goal)
        {
            if (Settings == null || CustomPlanStore == null || Character?.rebirthTime == null || Character.NGU == null
                || goal != CustomFocusGoal.MagicNgu && (Character.NGU.skills == null || Character.NGU.skills.Count < 9)
                || Character.magic == null || Character.NGU.magicSkills == null || Character.NGU.magicSkills.Count < 7)
                return null;
            try
            {
                var plan = LoadRuntimeCustomPlan();
                return plan?.SourceJson == null ? null : plan;
            }
            catch
            {
                // A bad plan is not permission to recall resources or execute default weights.
                return null;
            }
        }
    }
}
