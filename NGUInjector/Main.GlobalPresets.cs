using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;
using SimpleJSON;

namespace NGUInjector
{
    internal partial class Main
    {
        private sealed class GlobalPresetPolicy
        {
            internal bool MaterialPersistencePending;
            internal string Owner;
            internal string Acquisition = Guid.NewGuid().ToString("N");
            internal int Horizon;
            internal string Definition;
            internal CustomLoadoutPreset Preset;
            internal CustomLoadoutPreset ExecutionPreset;
            internal string AdventureTaskKey, AdventureTask, AdventureObservation;
            internal CustomPlanStage Stage;
            internal LoadoutManager.SnapshotEpoch Epoch;
            internal object[] EpochObjects;
            internal PresetCandidate Candidate;
            internal PresetPlan Plan;
            internal Equipment[] Items;
            internal string[] Material;
            internal int[] Slots;
            // Acquisition observations for compatibility checks, never allocation configuration.
            internal int[] EnergyWeights, MagicWeights;
        }
        private string _globalPresetSelectionEvent;
        private Dictionary<string, GlobalPresetPolicy> _retainedPresetSelections;

        private GlobalPresetPolicy RetainedPresetSelection(string owner, string id)
        {
            return _retainedPresetSelections != null && _retainedPresetSelections.TryGetValue(owner, out var policy)
                && policy.Preset.Id == id && GlobalPresetEpochMatches(policy) ? policy : null;
        }

        private IEnumerable<GlobalPresetPolicy> RetainedExecutionPresetPolicies() => (_retainedPresetSelections?.Values ?? Enumerable.Empty<GlobalPresetPolicy>())
            .Concat(_globalPresetPolicies?.Values ?? Enumerable.Empty<GlobalPresetPolicy>()).Distinct().Where(GlobalPresetEpochMatches);

        private void ApproveExecutionPolicyMaterialRevision(GlobalPresetPolicy policy)
        {
            if (policy != null && policy.Owner == null && ReferenceEquals(policy, _globalPresetApplyingPolicy)
                && _globalPresetApplyingEvent?.EndsWith(":" + policy.Preset.Id, StringComparison.Ordinal) == true)
                policy.Owner = _globalPresetApplyingEvent.Substring(0, _globalPresetApplyingEvent.Length - policy.Preset.Id.Length - 1);
            if (policy?.Owner == null) throw new InvalidOperationException("Approved material has no retained execution owner.");
            policy.MaterialPersistencePending = true;
            try { PersistApprovedExecutionMaterialRevision(policy, policy.Owner); policy.MaterialPersistencePending = false; }
            catch (Exception error)
            {
                SuspendProtectedStageProgress();
                SetResourceExecution(ResourceExecutionState.Waiting, policy.Owner, "Approved material is waiting for durable storage: " + error.Message);
                LogError("Persist approved material", error);
            }
        }

        private sealed class GlobalPresetReceipt
        {
            internal string Id, Owner, Support, Allocations;
            internal string EnergyAllocations, MagicAllocations;
            internal object[] TemporaryMagicContext;
            internal string TemporaryMagicAllocations;
            internal int[] AppliedEnergyWeights, AppliedMagicWeights;
            internal GlobalPresetPolicy Policy;
            internal PresetCandidate Execution;
            internal DateTime LastBeardRotation;
            internal ih[] Gear;
            internal ih[] TransitionGear;
            internal string[] TransitionMaterial;
            internal string TransitionSupport;
            internal bool CurrentGearContinuation;
            // Read-only/runtime projection of current baseline support; authority
            // and requirements live independently of this application receipt.
            internal PresetSupport BaselineSupport;
            internal bool SupportRouteRetired;
            internal ManualSupportRouteClaim ManualSupportRoute;
            internal Dictionary<ResourceTarget, long[]> RetirementAllocations;
            internal object[] RetirementResourceObjects, RetirementRouteObjects, RetirementSupportObjects;
            internal long[] RetirementAtTargets;
            internal bool RetirementLazyPurchased, RetirementLazyEnabled, RetirementAutoAdvance, RetirementAttack, RetirementBeast;
            internal string RetirementIssue;
        }

        private void ObserveGlobalPresetRetirementFootprint(GlobalPresetReceipt receipt)
        {
            receipt.RetirementAllocations = ResourceWriter.CaptureAllocations(Character);
            receipt.RetirementResourceObjects = ResourceWriter.CaptureResourceContext(Character);
            receipt.RetirementRouteObjects = CombatPrepAdventureRouteObjects();
            receipt.RetirementSupportObjects = SupportObjects();
            receipt.RetirementAtTargets = Character.advancedTraining.levelTarget.ToArray();
            receipt.RetirementAutoAdvance = Character.advancedTraining.autoAdvance;
            receipt.RetirementAttack = Character.adventure.autoattacking;
            receipt.RetirementBeast = Character.adventure.beastModeOn;
            receipt.RetirementLazyPurchased = Character.arbitrary.boughtLazyITOPOD;
            receipt.RetirementLazyEnabled = Character.arbitrary.lazyITOPODOn;
        }

        private bool GlobalPresetRetirementAllocationsMatch(GlobalPresetReceipt receipt)
        {
            if (receipt.RetirementAllocations == null || receipt.RetirementAtTargets == null
                || receipt.RetirementAutoAdvance != Character.advancedTraining.autoAdvance
                || !receipt.RetirementAtTargets.SequenceEqual(Character.advancedTraining.levelTarget)) return false;
            var observed = ResourceWriter.CaptureAllocations(Character);
            var at = Character.advancedTraining; var stage = receipt.Policy.Stage;
            var completed = stage?.CompletionMode == (int)CustomStageCompletionMode.AdvancedTrainingStats
                && !GlobalPresetRequiresNativeTargets(stage) && !at.autoAdvance;
            // Native hitTarget returns a completed mandatory bar to idle without an
            // injector receipt. This proves retirement only, never old-policy replay.
            foreach (var pair in receipt.RetirementAllocations)
            {
                if (!observed.TryGetValue(pair.Key, out var current)) return false;
                if (pair.Value == null) { if (current != null) return false; else continue; }
                if (current == null || pair.Value.Length != current.Length) return false;
                for (var i = 0; i < current.Length; i++)
                    if (pair.Value[i] != current[i] && !(completed && pair.Key == ResourceTarget.AdvancedTraining
                        && i < 2 && pair.Value[i] > 0 && current[i] == 0
                        && (i == 0 ? stage.TargetAdvancedTrainingToughness : stage.TargetAdvancedTrainingPower) > 0)) return false;
            }
            return true;
        }

        private bool GlobalPresetTransitionStillOwned(GlobalPresetReceipt receipt) => receipt.TransitionGear != null
            && GlobalPresetEpochMatches(receipt.Policy) && LoadoutManager.ExactLoadoutMatches(receipt.TransitionGear)
            && !receipt.TransitionGear.Where((x, i) => LoadoutPresetPrimitiveKey(x.equipment) != receipt.TransitionMaterial[i]).Any()
            && CaptureSupportState().StableKey == receipt.TransitionSupport;

        private static bool GlobalPresetHasMagicObjective(GlobalPresetPolicy policy) => policy.ExecutionPreset.ControlsResourceLane(false);

        private Action<ih[], SupportSelection> BeginPresetSetupTransition()
        {
            var receipt = _globalPresetReceipt;
            if (receipt == null || !GlobalPresetEpochMatches(receipt.Policy)
                || !(LoadoutManager.ExactLoadoutMatches(receipt.Gear) && receipt.Support == GlobalPresetSupportState()
                    || GlobalPresetTransitionStillOwned(receipt))) return null;
            return (gear, support) => {
                if (!ReferenceEquals(receipt, _globalPresetReceipt) || !GlobalPresetEpochMatches(receipt.Policy)) return;
                receipt.TransitionGear = gear.ToArray();
                receipt.TransitionMaterial = gear.Select(x => LoadoutPresetPrimitiveKey(x.equipment)).ToArray();
                receipt.TransitionSupport = CaptureSupportState().StableKey;
            };
        }

        // Bounded, session-local ownership of the CURRENT manual route across a
        // support transition. This is not the acquired policy's farming route.
        private sealed class ManualSupportRouteClaim
        {
            internal int Zone;
            internal Func<bool> Authorized;
            internal ExecutionGrant Grant;
        }

        // Evidence of a completed delegated entry, not permission to restore a
        // historical route or to reacquire from reset flags. It authorizes only
        // retiring the exact still-owned route before resolving today's successor.
        private sealed class DelegatedManualRouteClaim
        {
            internal Func<bool> Authorized;
            internal ih[] Gear;
            internal SupportSelection Support;
        }
        private DelegatedManualRouteClaim _delegatedManualRoute;

        private string DelegatedManualRouteSettingsKey() => Settings.ManualAdventureEnabled + ":"
            + Settings.CustomAdventureEnabled + ":" + Settings.ManualAdventureZone + ":" + Settings.ManualAdventureLoadout
            + ":" + Settings.CustomPlanFile + ":" + Settings.ManageEnergy + ":" + Settings.ManageMagic + ":" + Settings.ManageR3
            + ":" + Settings.ManualAdventureBossOnly + ":" + Settings.ManualAdventureRecoverHealth
            + ":" + Settings.ManualAdventurePrecastBuffs + ":" + Settings.ManualAdventureFastCombat
            + ":" + Settings.ManualAdventureBeastMode
            + ":" + LoadoutPresetNumbers(Settings.ManualAdventureGearWeights).ToString()
            + ":" + LoadoutPresetNumbers(Settings.ManualAdventureBeardWeights).ToString()
            + ":" + LoadoutPresetNumbers(Settings.ManualAdventureDiggerWeights).ToString();

        private void AcquireDelegatedManualRoute(GlobalPresetReceipt receipt, int zone)
        {
            if (_delegatedManualRoute != null || receipt == null || Character.adventure.zone != zone
                || zone < 0 || zone == 1000 || ZoneHelpers.ZoneIsTitan(zone)) return;
            var settings = Settings;
            var key = DelegatedManualRouteSettingsKey();
            var epoch = new LoadoutManager.SnapshotEpoch();
            var stage = _customStageKey;
            var generation = _protectedGeneration;
            var entry = _protectedScheduler?.Entry;
            var cycle = _protectedScheduler?.Cycle;
            var acquisition = receipt.Policy.Acquisition;
            var gear = LoadoutManager.CaptureExactLoadout();
            var material = gear.Select(x => LoadoutPresetPrimitiveKey(x.equipment)).ToArray();
            var support = CaptureSupportState().Selection;
            var attack = Character.adventure.autoattacking;
            var start = Character.adventure.itopodStart; var end = Character.adventure.itopodEnd;
            _delegatedManualRoute = new DelegatedManualRouteClaim { Gear = gear, Support = support,
                Authorized = () => ReferenceEquals(reference, this) && !_unloaded && epoch.Matches()
                    && ReferenceEquals(Settings, settings) && DelegatedManualRouteSettingsKey() == key
                    && ReferenceEquals(_globalPresetReceipt, receipt) && GlobalPresetEpochMatches(receipt.Policy)
                    && acquisition == receipt.Policy.Acquisition && stage == _customStageKey
                    && generation == _protectedGeneration && entry == _protectedScheduler?.Entry && cycle == _protectedScheduler?.Cycle
                    && (receipt.Owner == "dashboard" ? !Settings.UseCustomRebirthPlan && _globalPresetManualId == receipt.Id
                        : Settings.UseCustomRebirthPlan && _customStageEquipmentKey == receipt.Owner)
                    && !_manualTitanActive && !_customTitanFightActive && !_customTitanGoldActive
                    && (_questManager == null || _questManager.IsQuesting() < 0)
                    && (Character.adventure.zone == zone || Character.adventure.zone == -1)
                    && Character.adventure.autoattacking == attack
                    && Character.adventure.itopodStart == start && Character.adventure.itopodEnd == end
                    && LoadoutManager.ExactLoadoutMatches(gear)
                    && !gear.Where((x, i) => LoadoutPresetPrimitiveKey(x.equipment) != material[i]).Any()
                    && CaptureSupportState().Selection.StableKey == support.StableKey };
        }

        private bool RetireDelegatedManualRoute()
        {
            var claim = _delegatedManualRoute;
            if (claim == null || _globalPresetManualAdventureActive) return true;
            try
            {
                if (!claim.Authorized()) throw new InvalidOperationException("Delegated manual route ownership changed.");
                ApplySelectedSetup(new SelectedSetupRequirements { Owner = _globalPresetReceipt.Owner,
                    Gear = claim.Gear, Support = claim.Support, ChangeSupport = false,
                    Recall = GearRecallFootprint.None, OwnsManualRoute = true, RetireRoute = true,
                    Authorized = () => ReferenceEquals(_delegatedManualRoute, claim) && claim.Authorized(),
                    RouteRetirementAuthorized = claim.Authorized });
                if (Character.adventure.zone != -1) throw new InvalidOperationException("Delegated route retirement is not verified.");
                if (!CompleteDelegatedManualRouteRetirement()) return false;
                _delegatedManualRoute = null;
                return true;
            }
            catch (Exception error)
            {
                SuspendProtectedStageProgress();
                SetCustomAutomationStatus("Delegated manual release waiting: " + error.Message);
                return false;
            }
        }

        private sealed class GlobalPresetNativeTargets
        {
            internal string Owner;
            internal LoadoutManager.SnapshotEpoch Epoch;
            internal AdvancedTraining Training;
            internal long[] Targets, Before, Applied, Energy, Allocations;
            internal bool[] Owned;
        }

        private sealed class GlobalPresetPendingSearch
        {
            internal string Event, Definition, Context;
            internal LoadoutManager.SnapshotEpoch Epoch;
            internal LoadoutPresetCapture Capture;
            internal PresetPlan Plan;
        }

        private readonly Dictionary<string, GlobalPresetPolicy> _globalPresetPolicies = new Dictionary<string, GlobalPresetPolicy>(StringComparer.Ordinal);
        private GlobalPresetReceipt _globalPresetReceipt;
        private string _globalPresetManualId;
        private string _globalPresetFailure;
        private GlobalPresetNativeTargets _globalPresetNativeTargets;
        private string _globalPresetNativeTargetConflict;
        private GlobalPresetPendingSearch _globalPresetPendingSearch;
        private GlobalPresetPolicy _globalPresetApplyingPolicy;
        private string _globalPresetApplyingEvent;
        private string _globalPresetWaitingEvent, _globalPresetWaitingState, _globalPresetWaitingReason;
        private double _globalPresetRetryAt, _globalPresetRetryDelay;
        private readonly Random _globalPresetBeardRandom = new Random();
        private string _globalNguWeightsIdentity;

        private CustomLoadoutPreset ManualAdventureBasePreset => ManualAdventureBaselineAuthorized()
            ? _manualAdventureBaselinePolicy?.ExecutionPreset : null;

        private string ManualAdventureBaseIdentity => !ManualAdventureBaselineAuthorized() ? ""
            : _manualAdventureBaseline.Owner + ":" + (_manualAdventureBaselinePolicy?.Definition ?? "inline") + ":" + _manualAdventureBaseline.Acquisition;

        private static void ConfigureManualAdventureResourceRequest(PresetRequest request)
        {
            // A transient planner view, never the saved/acquired definition or its resource ratios.
            request.Weights = request.Weights.ToArray();
            Array.Clear(request.Weights, 3, 3);
            if (!request.Weights.Any(x => x > 0)) request.Weights[10] = 1; // Gear search still needs a non-resource objective.
            request.Zone = -1;
            request.MinimumPower = request.MinimumToughness = 0;
        }

        private static bool GlobalPresetWeightsMatch(GlobalPresetPolicy policy)
        {
            return policy.EnergyWeights.SequenceEqual(Settings.EnergyNguWeights)
                && policy.MagicWeights.SequenceEqual(Settings.MagicNguWeights);
        }

        private void InvalidateGlobalNguWeightPolicies()
        {
            var identity = string.Join(",", Settings.EnergyNguWeights) + ":" + string.Join(",", Settings.MagicNguWeights);
            if (identity == _globalNguWeightsIdentity) return;
            if (_globalNguWeightsIdentity != null) SuspendProtectedStageProgress();
            _globalNguWeightsIdentity = identity;
            _loadoutPresetPreview = null;
            _globalPresetPendingSearch = null;
            ClearGlobalPresetWaiting();
        }

        private bool PrepareGlobalPresetWeightChange(string owner)
        {
            InvalidateGlobalNguWeightPolicies();
            // Ratios are live allocation inputs, not a reason to retire the selected gear policy.
            return true;
        }

        private void ClearGlobalPresetWaiting()
        {
            _globalPresetWaitingEvent = _globalPresetWaitingState = _globalPresetWaitingReason = null;
            _globalPresetRetryAt = _globalPresetRetryDelay = 0;
        }

        private void WaitForGlobalPreset(string eventKey, string state, string reason, bool backoff)
        {
            if (_globalPresetWaitingEvent != eventKey || _globalPresetWaitingState != state) _globalPresetRetryDelay = 0;
            _globalPresetWaitingEvent = eventKey; _globalPresetWaitingState = state;
            _globalPresetWaitingReason = "Global preset waiting: " + reason;
            _globalPresetRetryDelay = backoff ? Math.Min(30, Math.Max(1, _globalPresetRetryDelay * 2)) : 0;
            _globalPresetRetryAt = backoff ? LoadoutPresetClock + _globalPresetRetryDelay : 0;
            SetCustomAutomationStatus(_globalPresetWaitingReason);
        }

        private bool GlobalPresetRetryReady(string eventKey, string state)
        {
            if (_globalPresetWaitingEvent != eventKey || _globalPresetWaitingState != state)
            {
                ClearGlobalPresetWaiting();
                return true;
            }
            if (LoadoutPresetClock >= _globalPresetRetryAt) return true;
            SetCustomAutomationStatus(_globalPresetWaitingReason);
            return false;
        }

        private static string GlobalPresetReadiness(CustomLoadoutPreset preset, CustomPlanStage stage, out string state, bool resourceOnly = false)
        {
            state = "unavailable";
            var c = Character;
            if (c?.buttons == null || c.settings == null) return "game data is not available.";
            if (preset.AdventureDynamicWeight > 0)
            {
                try { preset = ResolveAdventurePreset(preset, out _, out _); }
                catch (InvalidOperationException error) { return error.Message; }
            }
            var levelGoals = preset.Weights[0] + preset.Weights[1] + preset.Weights.Skip(6).Take(4).Sum() > 0 || GlobalPresetRequiresNativeTargets(stage);
            var canLevel = !levelGoals || c.canLevel();
            var ngu = c.buttons.ngu?.interactable == true;
            var magic = c.magic?.capMagic >= 10000;
            var blood = c.buttons.bloodMagic?.interactable == true;
            var adventure = c.buttons.adventure?.interactable == true && c.adventure?.beastModeOn == false;
            var tm = c.buttons.brokenTimeMachine?.interactable == true && c.bossID > 29 && c.machine?.transferredBankLevels == true && c.timeMachineController != null
                && c.challenges?.timeMachineChallenge?.inChallenge != true;
            var aug = c.buttons.augmentation?.interactable == true && c.augmentsController != null;
            var wan = c.buttons.wandoos?.interactable == true && c.settings.wandoos98On
                && c.wandoos98?.installed == true && !c.wandoos98.disabled;
            var at = c.buttons.advancedTraining?.interactable == true && c.training != null && c.advancedTraining?.transferredBankedLevels == true
                && c.advancedTrainingController?.advancedTrainingUnlocked() == true;
            state = preset.Id + ":" + preset.AutoUpdate + ":" + LoadoutPresetNumbers(preset.Weights).ToString() + ":" + c.bossID + ":" + string.Join(",", new[] {
                canLevel, ngu, blood, adventure, tm, aug, wan, at, magic, c.advancedTraining?.autoAdvance == true }.Select(x => x ? "1" : "0").ToArray());
            if (!canLevel) return "native level gain is temporarily disabled.";
            if ((preset.Weights[0] > 0 || preset.Weights[1] > 0) && !ngu) return "NGU is locked.";
            if (preset.Weights[2] > 0 && !blood) return "Blood Magic is locked.";
            if (!resourceOnly && preset.Weights.Skip(3).Take(3).Any(x => x > 0) && !adventure) return "farming is locked or Beast Mode is active.";
            if (preset.Weights[6] > 0 && !tm) return "Time Machine is locked, challenged, or bank transfer is pending.";
            if (preset.Weights[7] > 0 && !aug) return "Augments are locked.";
            if (preset.Weights[8] > 0 && !wan) return "Wandoos must be enabled and installed.";
            if ((preset.Weights[9] > 0 || GlobalPresetRequiresNativeTargets(stage)) && !at) return "AT is locked or bank transfer is pending.";
            if ((preset.Weights[10] > 0 || preset.Weights[11] > 0) && c.buttons.adventure?.interactable != true) return "Adventure is locked.";
            if (preset.Weights[13] > 0 && !magic) return "Magic is locked.";
            return null;
        }

        private static bool GlobalPresetRequiresNativeTargets(CustomPlanStage stage)
        {
            if (stage == null || stage.CompletionMode != (int)CustomStageCompletionMode.AdvancedTrainingStats
                || stage.TargetAdvancedTrainingPower <= 0 && stage.TargetAdvancedTrainingToughness <= 0) return false;
            var levels = Character?.advancedTraining?.level;
            return levels == null || levels.Length < 2 || levels[0] < stage.TargetAdvancedTrainingToughness
                || levels[1] < stage.TargetAdvancedTrainingPower;
        }

        private static bool GlobalPresetNativeTargetsEpochMatches(GlobalPresetNativeTargets targets)
        {
            return targets.Epoch.Matches() && ReferenceEquals(Character.advancedTraining, targets.Training)
                && ReferenceEquals(Character.advancedTraining.levelTarget, targets.Targets)
                && ReferenceEquals(Character.advancedTraining.energy, targets.Allocations);
        }

        private static void CaptureUnownedGlobalPresetTargets(PresetSnapshot snapshot)
        {
            var lease = reference?._globalPresetNativeTargets;
            if (lease == null || !GlobalPresetNativeTargetsEpochMatches(lease)) return;
            // Dashboard/legacy previews must not inherit a scheduled execution's borrowed native targets.
            for (var i = 0; i < 2 && i < snapshot.AdvancedTraining.Length; i++)
                if (lease.Owned[i] && lease.Targets[i] == lease.Applied[i])
                {
                    snapshot.AdvancedTraining[i].Target = lease.Before[i];
                    snapshot.AdvancedTraining[i].Unlocked &= lease.Before[i] >= 0;
                }
        }

        private void ApplyGlobalPresetNativeTargets(LoadoutPresetCapture capture, string owner, PresetCandidate candidate)
        {
            if (!GlobalPresetRequiresNativeTargets(capture.Stage))
            {
                RetireGlobalPresetNativeTargetsForSuccessor();
                return;
            }
            var at = Character.advancedTraining;
            if (owner == null || !ReferenceEquals(capture.NativeAt, at) || !ReferenceEquals(capture.NativeAtTargetArray, at.levelTarget)
                || at.levelTarget.Length < 2 || capture.NativeAtTargets == null || capture.NativeAtTargets.Length < 2
                || Enumerable.Range(0, 2).Any(i => at.levelTarget[i] != capture.NativeAtTargets[i]))
                throw new InvalidOperationException("Native AT menu targets changed after preflight; newer settings were not overwritten.");
            var applied = new[] { capture.Stage.TargetAdvancedTrainingToughness, capture.Stage.TargetAdvancedTrainingPower };
            var previous = _globalPresetNativeTargets;
            var transfer = previous != null && GlobalPresetNativeTargetsEpochMatches(previous);
            var lease = new GlobalPresetNativeTargets { Owner = owner, Epoch = new LoadoutManager.SnapshotEpoch(), Training = at,
                Targets = at.levelTarget, Applied = applied, Before = new long[2], Owned = applied.Select(x => x > 0).ToArray(),
                Allocations = at.energy,
                Energy = candidate.Activities == null ? new long[2] : candidate.Activities.AT.Take(2).ToArray() };
            for (var i = 0; i < 2; i++)
                lease.Before[i] = transfer && previous.Owned[i] && at.levelTarget[i] == previous.Applied[i]
                    ? previous.Before[i] : at.levelTarget[i];
            if (transfer)
                for (var i = 0; i < 2; i++)
                    if (previous.Owned[i] && !lease.Owned[i] && previous.Targets[i] == previous.Applied[i])
                        WriteGlobalPresetTarget(previous, i, previous.Before[i]);
            _globalPresetNativeTargets = lease;
            for (var i = 0; i < 2; i++)
                if (lease.Owned[i] && lease.Targets[i] != lease.Applied[i]) WriteGlobalPresetTarget(lease, i, lease.Applied[i]);
            _globalPresetTargetsReturnRequested = false;
            if (_globalPresetNativeTargetConflict == owner) _globalPresetNativeTargetConflict = null;
            if (SelectedSetupConflicts.TryGetValue(Character, out var reviewed) && reviewed.Rebirth == Character.stats.rebirthNumber)
            { reviewed.NativeSettings = reviewed.NativeSettingsOwner = null; }
        }

        private bool GlobalPresetNativeTargetLeaseCurrent(GlobalPresetNativeTargets lease)
        {
            return ReferenceEquals(reference, this) && ReferenceEquals(_globalPresetNativeTargets, lease)
                && GlobalPresetNativeTargetsEpochMatches(lease);
        }

        private bool GlobalPresetNativeResourceReturnAllowed(GlobalPresetNativeTargets lease)
        {
            return !_unloaded && GlobalPresetNativeTargetLeaseCurrent(lease) && Settings?.ManageEnergy == true
                && !lease.Training.autoAdvance
                && Controller != null && !Controller.midDrag && ReferenceEquals(Controller, Character.inventoryController)
                && ReferenceEquals(Controller.character, Character);
        }

        private bool _globalPresetTargetsReturnRequested;

        private void ReleaseGlobalPresetNativeTargets() { _globalPresetTargetsReturnRequested = true; _resourceReconciliationPending = true; }

        private void WriteGlobalPresetTarget(GlobalPresetNativeTargets lease, int index, long value)
        {
            var before = lease.Targets[index];
            var grant = ResolveExecutionGrantCore(lease.Owner, null, ExecutionDomain.NativeSettings,
                () => GlobalPresetNativeTargetLeaseCurrent(lease) && !lease.Training.autoAdvance, null, null, true);
            var result = ApplyExecutionSetting(grant, () => lease.Targets[index] = value, () => lease.Targets[index] == value);
            if (!result.Verified) throw new ResourceMutationException(result);
            var receipt = _globalPresetReceipt;
            if (receipt?.Owner == lease.Owner && GlobalPresetEpochMatches(receipt.Policy)
                && receipt.RetirementAtTargets?[index] == before)
                receipt.RetirementAtTargets[index] = value;
        }

        internal bool ServiceExecutionRetirement()
        {
            if (_executionLifecycle != ExecutionLifecycle.Quiescing || !ReferenceEquals(reference, this) || _unloaded) return false;
            _globalPresetTargetsReturnRequested = true;
            ServiceExecutionNativeSettings();
            return _globalPresetNativeTargets == null;
        }

        private void ServiceExecutionNativeSettings()
        {
            if (!_globalPresetTargetsReturnRequested) return;
            var lease = _globalPresetNativeTargets;
            if (lease == null) { _globalPresetTargetsReturnRequested = false; return; }
            if (!GlobalPresetNativeTargetLeaseCurrent(lease))
            {
                _globalPresetNativeTargets = null; _globalPresetTargetsReturnRequested = false;
                return;
            }
            // Application invalidation is not the end of an acquired mandatory gate.
            if (_executionLifecycle == ExecutionLifecycle.Running && Settings.UseCustomRebirthPlan && _protectedScheduler != null && !_protectedScheduler.Finished
                && _protectedPlan?.Stages != null && _protectedScheduler.StageIndex >= 0
                && _protectedScheduler.StageIndex < _protectedPlan.Stages.Length
                && GlobalPresetRequiresNativeTargets(_protectedPlan.Stages[_protectedScheduler.StageIndex])) return;
            if (lease.Training.autoAdvance || Enumerable.Range(0, 2).Any(i => lease.Owned[i] && lease.Targets[i] != lease.Applied[i]))
                _globalPresetNativeTargetConflict = lease.Owner;
            Exception failure = null;
            // Restore only fields that still hold this execution's value, never a later user's edit or another save.
            for (var i = 0; i < 2; i++)
            {
                if (!GlobalPresetNativeTargetLeaseCurrent(lease)) break;
                if (lease.Owned[i] && lease.Targets[i] == lease.Applied[i])
                {
                    try
                    {
                        ResourceWriter.Execute(() => {
                            if (!GlobalPresetNativeResourceReturnAllowed(lease) || !lease.Owned[i] || lease.Targets[i] != lease.Applied[i]) return;
                            var amount = lease.Allocations[i];
                            if (amount > 0 && amount == lease.Energy[i])
                            {
                                var desired = lease.Allocations.ToArray(); desired[i] = 0;
                                var intent = new ResourceSetupIntent { Owner = lease.Owner + ":at-release", Energy = true,
                                    Desired = new Dictionary<ResourceTarget, long[]> { { ResourceTarget.AdvancedTraining, desired } },
                                    Authorized = () => GlobalPresetNativeResourceReturnAllowed(lease)
                                        && (lease.Targets[i] == lease.Applied[i] || lease.Allocations[i] == 0) };
                                ResourceMutationResult result;
                                if (_executionLifecycle == ExecutionLifecycle.Quiescing)
                                {
                                    var owned = intent.Authorized;
                                    var footprint = ExecutionDomain.Energy | ExecutionDomain.NativeSettings;
                                    var grant = ResolveExecutionGrantCore(intent.Owner, null, footprint, owned, null, null, true);
                                    intent.Authorized = () => ValidateExecutionGrant(grant, footprint);
                                    result = _resourceSetupExecutor.Reconcile(intent);
                                }
                                else result = ReconcileResourceSetup(intent);
                                if (!result.Verified) throw new ResourceMutationException(result);
                            }
                        });
                    }
                    catch (Exception error) { failure = failure ?? error; }
                    finally
                    {
                        // Rendering can fail after a committed return. Target cleanup must still
                        // finish for this and the companion bar, without overwriting a callback edit.
                        if (GlobalPresetNativeTargetLeaseCurrent(lease))
                        {
                            if (lease.Targets[i] == lease.Applied[i]) WriteGlobalPresetTarget(lease, i, lease.Before[i]);
                            else _globalPresetNativeTargetConflict = lease.Owner;
                            lease.Owned[i] = false;
                        }
                    }
                }
            }
            if (ReferenceEquals(_globalPresetNativeTargets, lease)) _globalPresetNativeTargets = null;
            _globalPresetTargetsReturnRequested = false;
            if (failure != null) throw new InvalidOperationException("Native AT resource return failed; owned target cleanup was serviced.", failure);
        }

        private void RetireGlobalPresetNativeTargetsForSuccessor()
        {
            var lease = _globalPresetNativeTargets;
            if (lease == null) return;
            if (!GlobalPresetNativeTargetLeaseCurrent(lease)) { _globalPresetNativeTargets = null; return; }
            if (lease.Training.autoAdvance || Enumerable.Range(0, 2).Any(i => lease.Owned[i] && lease.Targets[i] != lease.Applied[i]))
                _globalPresetNativeTargetConflict = lease.Owner;
            for (var i = 0; i < 2; i++)
                if (GlobalPresetNativeTargetLeaseCurrent(lease) && lease.Owned[i] && lease.Targets[i] == lease.Applied[i]) WriteGlobalPresetTarget(lease, i, lease.Before[i]);
            if (ReferenceEquals(_globalPresetNativeTargets, lease)) _globalPresetNativeTargets = null;
            // A current successor owns resource differences. Only borrowed menu fields return;
            // never recall the predecessor's allocations merely to fund the successor again.
        }

        private bool GlobalPresetNativeTargetsMatch(CustomPlanStage stage, string owner)
        {
            if (stage == null || stage.CompletionMode != (int)CustomStageCompletionMode.AdvancedTrainingStats
                || stage.TargetAdvancedTrainingPower <= 0 && stage.TargetAdvancedTrainingToughness <= 0) return true;
            var lease = _globalPresetNativeTargets;
            if (lease == null) return !GlobalPresetRequiresNativeTargets(stage);
            return lease.Owner == owner && GlobalPresetNativeTargetsEpochMatches(lease)
                && !lease.Training.autoAdvance
                && Enumerable.Range(0, 2).All(i => !lease.Owned[i] || lease.Targets[i] == lease.Applied[i]);
        }

        private bool GlobalPresetNativeTargetsYielded(string owner)
        {
            return owner != null && (_globalPresetNativeTargetConflict == owner
                || SelectedSetupConflicts.TryGetValue(Character, out var issue) && issue.Rebirth == Character.stats.rebirthNumber
                    && issue.NativeSettingsOwner == owner && issue.NativeSettings != null);
        }

        private void ServiceGlobalPresetNativeTargets()
        {
            var lease = _globalPresetNativeTargets;
            if (lease == null) return;
            var index = _protectedScheduler?.StageIndex ?? -1;
            var owner = index < 0 || _protectedPlan == null ? null : "stage:" + Settings?.CustomPlanFile + ":" + _protectedPlan.Name
                + ":" + index + ":" + _protectedGeneration + ":" + _protectedScheduler.Entry + ":" + _protectedScheduler.Cycle;
            var current = GlobalPresetNativeTargetsEpochMatches(lease);
            var unchanged = current && Enumerable.Range(0, 2).All(i => !lease.Owned[i] || lease.Targets[i] == lease.Applied[i]);
            if (!current || Settings == null || !Settings.UseCustomRebirthPlan || !Settings.ManageEnergy || !Settings.ManageMagic
                || _protectedScheduler == null || _protectedScheduler.Finished || !_protectedScheduler.Matches(_protectedPlan)
                || owner != lease.Owner || _globalPresetReceipt?.Owner != owner || _manualRebirthPending || _customPreRebirthPrepRunning
                || _customRequestedFocusGoal.HasValue || CombatPrepActive || BossPushRunning || CustomBossFightActive()
                || !unchanged || lease.Training.autoAdvance)
            {
                if (unchanged && !lease.Training.autoAdvance && _protectedScheduler != null && !_protectedScheduler.Finished && owner == lease.Owner)
                    _customEquipmentApplicationInvalidated = true;
                ReleaseGlobalPresetNativeTargets();
            }
        }

        private static void GlobalPresetArgs(JSONNode args, params string[] fields)
        {
            if (args == null || !args.IsObject || args.Count != fields.Length || args.Linq.Any(x => !fields.Contains(x.Key)))
                throw new FormatException("Expected only: " + string.Join(", ", fields));
            foreach (var field in fields.Where(x => x != "library"))
                if (!args[field].IsString || string.IsNullOrWhiteSpace(args[field].Value))
                    throw new FormatException(field + " must be a nonempty string.");
        }

        private CustomLoadoutPreset ResolveGlobalPreset(JSONNode args, bool apply = false)
        {
            GlobalPresetArgs(args, apply ? new[] { "presetId", "libraryRevision", "previewId", "candidateKey" }
                : new[] { "presetId", "libraryRevision" });
            if (CustomPlanStore == null) throw new InvalidOperationException("Preset library is unavailable.");
            var library = CustomPlanStore.PresetLibrary.ReadExternalLibrary();
            if (!string.Equals(library["__externalUiRevision"].Value, args["libraryRevision"].Value, StringComparison.Ordinal))
                throw new InvalidOperationException("Preset library revision changed; reload the committed library.");
            var node = library["Presets"].Children.FirstOrDefault(x => x["Id"].Value == args["presetId"].Value);
            var preset = node == null ? null : CustomLoadoutPreset.Parse(node);
            if (preset == null) throw new InvalidOperationException("Global preset does not exist: " + args["presetId"].Value);
            return CustomLoadoutPreset.Parse(preset.ToJson());
        }

        private static JSONObject GlobalPresetLegacyArgs(CustomLoadoutPreset preset)
        {
            var args = new JSONObject();
            args["preset"] = preset.ToJson();
            return args;
        }

        private JSONObject PreviewGlobalPreset(JSONNode args)
        {
            var preset = ResolveGlobalPreset(args);
            return PreviewLoadoutPreset(GlobalPresetLegacyArgs(preset));
        }

        private string ApplyGlobalPreset(JSONNode args, out JSONNode data)
        {
            var preset = ResolveGlobalPreset(args, true);
            var legacy = GlobalPresetLegacyArgs(preset);
            legacy["previewId"] = args["previewId"].Value;
            legacy["candidateKey"] = args["candidateKey"].Value;
            return ApplyLoadoutPresetCore(legacy, out data, "dashboard");
        }

        private string EquipGlobalPreset(JSONNode args, out JSONNode data)
        {
            return EquipGlobalPresetDefinition(ResolveGlobalPreset(args), "dashboard", "explicit-equip", true, out data);
        }

        private static string GlobalPresetDefinition(CustomLoadoutPreset preset)
        {
            var definition = preset.ToJson();
            definition.Remove("Name"); definition.Remove("AutoUpdate");
            return LoadoutPresetCanonical(definition);
        }

        private static CustomPlanStage CloneGlobalPresetStage(CustomPlanStage stage)
        {
            return stage == null ? null : new CustomPlanStage { CompletionMode = stage.CompletionMode,
                TargetAdvancedTrainingPower = stage.TargetAdvancedTrainingPower,
                TargetAdvancedTrainingToughness = stage.TargetAdvancedTrainingToughness };
        }

        private static string GlobalPresetStageTargets(CustomPlanStage stage)
        {
            return stage == null || stage.CompletionMode != (int)CustomStageCompletionMode.AdvancedTrainingStats ? ""
                : stage.TargetAdvancedTrainingPower.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":"
                    + stage.TargetAdvancedTrainingToughness.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private GlobalPresetPolicy CreateGlobalPresetPolicy(CustomLoadoutPreset preset, LoadoutPresetCapture capture, PresetPlan plan, PresetCandidate candidate)
        {
            var items = candidate.Items.Select(x => x.Id == 0 ? null : capture.Instances[x.SourceSlot]).ToArray();
            return new GlobalPresetPolicy { Definition = GlobalPresetDefinition(preset), Epoch = new LoadoutManager.SnapshotEpoch(),
                Horizon = capture.Horizon,
                EnergyWeights = capture.Snapshot.EnergyNgu.Select(x => x.Weight).ToArray(),
                MagicWeights = capture.Snapshot.MagicNgu.Select(x => x.Weight).ToArray(),
                Preset = CustomLoadoutPreset.Parse(preset.ToJson()), Stage = CloneGlobalPresetStage(capture.Stage),
                ExecutionPreset = CustomLoadoutPreset.Parse(capture.ExecutionPreset.ToJson()),
                AdventureTaskKey = capture.AdventureTaskKey, AdventureTask = capture.AdventureTask,
                EpochObjects = capture.Epoch.ToArray(),
                Candidate = candidate.Copy(), Plan = plan, Items = items, Slots = capture.Snapshot.Slots.ToArray(),
                Material = items.Select(LoadoutPresetPrimitiveKey).ToArray() };
        }

        // Serialization boundary for selected equipment, NOT an application receipt.
        // Export approved acquisition material, never a fresh native observation. In-session
        // replay still uses exact references; this codec grants no save/epoch/write authority.
        private static JSONObject ExportGlobalPresetEquipmentSelection(GlobalPresetPolicy policy)
        {
            var result = new JSONObject();
            result["version"] = 1;
            result["materialSchema"] = "equipment-primitives-v1";
            result["acquisition"] = policy.Acquisition;
            var rows = new JSONArray();
            for (var i = 0; i < policy.Slots.Length; i++)
            {
                var item = policy.Candidate.Items[i];
                var row = new JSONObject();
                row["targetSlot"] = policy.Slots[i];
                row["sourceSlotHint"] = item.SourceSlot;
                row["id"] = item.Id;
                row["material"] = policy.Material[i];
                rows.Add(row);
            }
            result["equipment"] = rows;
            return result;
        }

        // A detached semantic commitment, not a serialized runtime policy or receipt.
        // In particular, fixed legacy activity amounts are acquired decisions, NOT proof
        // that those amounts were applied. Reviewed lane funding is rebuilt from observation.
        private JSONObject ExportProtectedPresetSelection(GlobalPresetPolicy policy, string owner)
        {
            if (policy?.Preset == null || policy.ExecutionPreset == null || policy.Candidate?.Items == null
                || policy.Plan?.Normalization == null || policy.Plan.ReferenceKeys == null || policy.Plan.ReferenceProduction == null
                || policy.Plan.Normalization.Length != LoadoutPresetPlanner.Keys.Length
                || policy.Plan.ReferenceKeys.Length != LoadoutPresetPlanner.Keys.Length
                || policy.Plan.ReferenceProduction.Length != LoadoutPresetPlanner.Keys.Length
                || policy.Slots == null || policy.Material == null || policy.Slots.Length != policy.Material.Length
                || policy.Slots.Length != policy.Candidate.Items.Length || policy.Candidate.Items.Any(x => x == null)
                || policy.EnergyWeights == null || policy.MagicWeights == null || policy.Candidate.Support?.Beards == null
                || policy.Candidate.Support.Diggers == null || policy.Candidate.Support.DiggerWeights == null
                || policy.Candidate.Activities != null && policy.Candidate.Activities.AT == null
                || string.IsNullOrEmpty(policy.Definition) || string.IsNullOrEmpty(policy.Candidate.Key)
                || string.IsNullOrEmpty(policy.Candidate.Label) || !Guid.TryParseExact(policy.Acquisition, "N", out _)
                || _protectedScheduler == null || string.IsNullOrEmpty(_protectedPlanFile)
                || string.IsNullOrEmpty(_protectedGeneration) || string.IsNullOrEmpty(owner))
                throw new InvalidOperationException("Selected preset policy is incomplete; no commitment was saved.");
            var result = new JSONObject();
            result["version"] = 1;
            result["schema"] = "selected-preset-policy-v1";
            result["acquisition"] = policy.Acquisition;
            result["owner"] = owner;
            result["plan"] = _protectedPlanFile;
            result["generation"] = _protectedGeneration;
            result["entry"] = _protectedScheduler.Entry.ToString(System.Globalization.CultureInfo.InvariantCulture);
            result["rebirth"] = _protectedRebirthNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
            result["origin"] = _protectedRunOrigin;
            result["elapsed"] = _protectedLastElapsed;
            result["total"] = _protectedLastTotal;
            result["definition"] = policy.Definition;
            result["preset"] = policy.Preset.ToJson();
            result["executionPreset"] = policy.ExecutionPreset.ToJson();
            result["horizon"] = policy.Horizon;
            result["adventureTaskKey"] = policy.AdventureTaskKey == null ? JSONNull.CreateOrGet() : (JSONNode)new JSONString(policy.AdventureTaskKey);
            result["adventureTask"] = policy.AdventureTask == null ? JSONNull.CreateOrGet() : (JSONNode)new JSONString(policy.AdventureTask);
            var stage = new JSONObject();
            if (policy.Stage != null)
            {
                stage["completionMode"] = policy.Stage.CompletionMode;
                stage["power"] = policy.Stage.TargetAdvancedTrainingPower.ToString(System.Globalization.CultureInfo.InvariantCulture);
                stage["toughness"] = policy.Stage.TargetAdvancedTrainingToughness.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            result["stage"] = stage;
            result["equipment"] = ExportGlobalPresetEquipmentSelection(policy);
            result["energyNguWeights"] = LoadoutPresetNumbers(policy.EnergyWeights.Select(x => (double)x));
            result["magicNguWeights"] = LoadoutPresetNumbers(policy.MagicWeights.Select(x => (double)x));
            result["normalization"] = LoadoutPresetNumbers(policy.Plan.Normalization);
            var references = new JSONArray();
            for (var i = 0; i < LoadoutPresetPlanner.Keys.Length; i++)
            {
                var row = new JSONObject(); row["objective"] = LoadoutPresetPlanner.Keys[i];
                row["key"] = policy.Plan.ReferenceKeys[i] == null ? JSONNull.CreateOrGet() : (JSONNode)new JSONString(policy.Plan.ReferenceKeys[i]);
                row["production"] = policy.Plan.ReferenceProduction[i].HasValue
                    ? (JSONNode)new JSONNumber(policy.Plan.ReferenceProduction[i].Value) : JSONNull.CreateOrGet();
                references.Add(row);
            }
            result["references"] = references;
            var candidate = policy.Candidate;
            var decision = new JSONObject(); decision["key"] = candidate.Key; decision["label"] = candidate.Label;
            decision["bloodPercent"] = candidate.BloodPercent; decision["ritual"] = candidate.Ritual;
            decision["zone"] = candidate.Zone; decision["floor"] = candidate.Floor;
            decision["regularEligibilityZone"] = candidate.RegularEligibilityZone;
            decision["boostEligibilityZone"] = candidate.BoostEligibilityZone;
            var support = new JSONObject();
            support["beards"] = LoadoutPresetNumbers(candidate.Support.Beards.Select(x => (double)x));
            support["diggerWeights"] = LoadoutPresetNumbers(candidate.Support.DiggerWeights);
            var diggers = new JSONArray();
            foreach (var digger in candidate.Support.Diggers)
            {
                var row = new JSONObject(); row["id"] = digger.Id;
                row["level"] = digger.Level.ToString(System.Globalization.CultureInfo.InvariantCulture); diggers.Add(row);
            }
            support["diggers"] = diggers; decision["support"] = support;
            var activity = candidate.Activities;
            var activities = new JSONObject();
            if (activity != null)
            {
                activities["os"] = activity.Os; activities["augmentId"] = activity.AugmentId;
                var amounts = new JSONArray();
                foreach (var amount in new[] { activity.TimeMachineEnergy, activity.TimeMachineMagic,
                    activity.WandoosEnergy, activity.WandoosMagic, activity.AugmentEnergy, activity.UpgradeEnergy }.Concat(activity.AT))
                    amounts.Add(amount.ToString(System.Globalization.CultureInfo.InvariantCulture));
                activities["fixedLegacyAmounts"] = amounts;
            }
            decision["activities"] = activities; result["decision"] = decision;
            // Round-trip detaches all JSON children from mutable saved/resolved definitions.
            return JsonFiles.ParseObject(result.ToString());
        }

        private static bool EquipmentDescriptorInteger(JSONNode value, int minimum, int maximum)
        {
            return value != null && value.IsNumber && LoadoutPresetPlanner.Finite(value.AsDouble)
                && value.AsDouble >= minimum && value.AsDouble <= maximum && value.AsDouble == Math.Floor(value.AsDouble);
        }

        private static bool EquipmentDescriptorSlot(int slot)
        {
            return slot >= -6 && slot <= -1 || slot >= 0 && slot < 1024 || slot >= 10000 && slot < 10064;
        }

        // Bounded identity matching, not replacement optimization. A caller must separately
        // validate checkpoint/save context and acquire current scoped authority before use.
        // Even an exact old-location match is ambiguous if another equal instance exists.
        private static Equipment[] ResolveGlobalPresetEquipmentSelection(JSONNode selection, string acquisition,
            int[] targetSlots, Dictionary<int, Equipment> instances)
        {
            if (selection == null || !selection.IsObject || selection.Count != 4
                || selection.Linq.Any(x => !new[] { "version", "materialSchema", "acquisition", "equipment" }.Contains(x.Key))
                || !EquipmentDescriptorInteger(selection["version"], 1, 1)
                || !selection["materialSchema"].IsString || selection["materialSchema"].Value != "equipment-primitives-v1"
                || !selection["acquisition"].IsString || !Guid.TryParseExact(selection["acquisition"].Value, "N", out _)
                || selection["acquisition"].Value != acquisition
                || targetSlots == null || targetSlots.Length == 0 || targetSlots.Length > 70
                || targetSlots.Distinct().Count() != targetSlots.Length
                || targetSlots.Any(x => x >= 0 && x < 10000 || !EquipmentDescriptorSlot(x))
                || !selection["equipment"].IsArray || selection["equipment"].Count != targetSlots.Length
                || instances == null || instances.Count > 1094 || instances.Keys.Any(x => !EquipmentDescriptorSlot(x)))
                throw new InvalidOperationException("Selected equipment descriptor is incompatible; no replacement was searched.");

            // Materialize each current instance once. Location is only a hint, never a
            // tie-breaker or permission to adopt same-ID equipment with changed material.
            var current = instances.Where(x => x.Value != null && x.Value.id != 0)
                .Select(x => new { Item = x.Value, Material = LoadoutPresetPrimitiveKey(x.Value) }).ToArray();
            var resolved = new Equipment[targetSlots.Length];
            for (var i = 0; i < resolved.Length; i++)
            {
                var row = selection["equipment"][i];
                if (!row.IsObject || row.Count != 4
                    || row.Linq.Any(x => !new[] { "targetSlot", "sourceSlotHint", "id", "material" }.Contains(x.Key))
                    || !EquipmentDescriptorInteger(row["targetSlot"], -6, 10063) || row["targetSlot"].AsInt != targetSlots[i]
                    || !EquipmentDescriptorInteger(row["sourceSlotHint"], -6, 10063) || !EquipmentDescriptorSlot(row["sourceSlotHint"].AsInt)
                    || !EquipmentDescriptorInteger(row["id"], 0, int.MaxValue)
                    || row["id"].AsInt > 0 && row["id"].AsInt < 40
                    || !row["material"].IsString || row["material"].Value.Length == 0 || row["material"].Value.Length > 32768)
                    throw new InvalidOperationException("Selected equipment descriptor row is invalid; no replacement was searched.");
                if (row["id"].AsInt == 0)
                {
                    if (row["material"].Value != "null")
                        throw new InvalidOperationException("Empty equipment descriptor has unexpected material.");
                    continue; // Controlled empty is a decision, not the current slot's observation.
                }
                var matches = current.Where(x => x.Item.id == row["id"].AsInt && x.Material == row["material"].Value).ToArray();
                if (matches.Length != 1 || resolved.Take(i).Any(x => ReferenceEquals(x, matches[0].Item)))
                    throw new InvalidOperationException("Selected equipment is missing or ambiguous; no replacement was searched.");
                resolved[i] = matches[0].Item;
            }
            return resolved;
        }

        private PresetCandidate ReplayGlobalPreset(GlobalPresetPolicy policy, CustomLoadoutPreset preset, LoadoutPresetCapture capture, PresetCandidate execution = null, bool currentGear = false, bool? currentSupport = null, bool resourceOnly = false, bool liveResources = false)
        {
            if (!liveResources && (!GlobalPresetWeightsMatch(policy) || !policy.EnergyWeights.SequenceEqual(capture.Snapshot.EnergyNgu.Select(x => x.Weight))
                || !policy.MagicWeights.SequenceEqual(capture.Snapshot.MagicNgu.Select(x => x.Weight)))
                || !GlobalPresetEpochMatches(policy) || policy.Definition != GlobalPresetDefinition(preset)
                || policy.AdventureTaskKey != capture.AdventureTaskKey
                || GlobalPresetStageTargets(policy.Stage) != GlobalPresetStageTargets(capture.Stage)
                || !policy.Slots.SequenceEqual(capture.Snapshot.Slots))
                throw new InvalidOperationException("Cached preset policy is incompatible with Misc global NGU weights, the definition, stage AT targets, or save/rebirth epoch. Use Preview + Apply.");
            var items = currentGear ? capture.Snapshot.Current : policy.Items.Select((equipment, index) => {
                if (equipment == null) return new PresetItem { SourceSlot = capture.Snapshot.Slots[index],
                    Group = LoadoutPresetPlanner.Group(capture.Snapshot.Slots[index]), Name = "Empty" };
                if (policy.Material[index] != LoadoutPresetPrimitiveKey(equipment))
                    throw new InvalidOperationException("Cached selected gear was mutated. Use Preview + Apply.");
                var slot = capture.Instances.Where(x => ReferenceEquals(x.Value, equipment)).Select(x => (int?)x.Key).SingleOrDefault();
                var item = slot.HasValue ? capture.Snapshot.Items.SingleOrDefault(x => x.SourceSlot == slot.Value) : null;
                if (item == null) throw new InvalidOperationException("Cached selected gear is missing. Use Preview + Apply.");
                return item;
            }).ToArray();
            var replay = execution ?? policy.Candidate;
            var effective = policy.ExecutionPreset;
            if ((currentSupport ?? currentGear) || effective.SupportMode == "current")
            {
                // Keep current governs both kinds of support, including cached re-equip and refresh.
                replay = replay.Copy();
                replay.Support = new PresetSupport { Beards = capture.Snapshot.CurrentBeards,
                    Diggers = capture.Snapshot.CurrentDiggers, Notes = replay.Support.Notes };
            }
            var request = LoadoutPresetRequest(effective, capture.Horizon);
            if (currentSupport ?? currentGear) request.PlannedSupport = false;
            var weights = LoadoutPresetPlanner.NormalizeWeights(request.Weights);
            if (resourceOnly)
            {
                ConfigureManualAdventureResourceRequest(request);
                Array.Clear(weights, 3, 3);
                replay = replay.Copy();
                replay.Zone = replay.Floor = -1;
            }
            if (capture.ResourceSetupOnlyObservation)
            {
                // Full planning records an incidental best route even with zero
                // farming weights. It is not a resource-refresh prerequisite. Keep
                // its acquired identity, but do not require an unrequested forecast.
                replay = replay.Copy();
                replay.Zone = replay.Floor = -1;
            }
            var candidate = LoadoutPresetPlanner.EvaluateExecution(capture.Snapshot, request, items,
                weights, policy.Plan.Normalization,
                policy.Candidate.Key, policy.Candidate.Label, replay);
            if (resourceOnly || capture.ResourceSetupOnlyObservation)
            { candidate.Zone = policy.Candidate.Zone; candidate.Floor = policy.Candidate.Floor; }
            return candidate;
        }

        private string EquipGlobalPresetDefinition(CustomLoadoutPreset preset, string owner, string reason, bool manual, out JSONNode data, CustomPlanStage stage = null, int horizon = 3600)
        {
            data = null;
            var eventKey = owner + ":" + preset.Id;
            var policy = !manual && _globalPresetApplyingEvent == eventKey && _globalPresetApplyingPolicy != null
                && GlobalPresetEpochMatches(_globalPresetApplyingPolicy) ? _globalPresetApplyingPolicy : null;
            var continuing = policy != null;
            if (continuing) { preset = policy.Preset; stage = policy.Stage; horizon = policy.Horizon; }
            var restorationIssue = ProtectedPresetRestorationIssue(owner);
            if (restorationIssue != null) throw new InvalidOperationException(restorationIssue);
            if (_resourceSetupExecutor.Application.BlockedLanes != 0)
                throw new InvalidOperationException("Resource authority is unresolved; new coupled preset acquisition is waiting.");
            InvalidateGlobalNguWeightPolicies();
            RequireLoadoutPresetContext();
            CustomLoadoutPreset.Validate(preset);
            var previous = _globalPresetReceipt?.Policy;
            var restoring = !manual && _globalPresetReceipt?.Owner == owner && previous.Preset.Id == preset.Id
                && GlobalPresetEpochMatches(previous) && previous.Definition == GlobalPresetDefinition(preset)
                && !GlobalPresetAdventureChanged(previous);
            if (restoring && policy == null && _globalPresetSelectionEvent != previous.Acquisition)
            {
                if (GlobalPresetStageTargets(stage) != GlobalPresetStageTargets(previous.Stage))
                    throw new InvalidOperationException("Cached policy has different stage AT targets; acquire an explicit new entry.");
                policy = previous; continuing = true; preset = policy.Preset; stage = policy.Stage; horizon = policy.Horizon;
            }
            var readiness = GlobalPresetReadiness(continuing ? policy.ExecutionPreset : restoring ? previous.ExecutionPreset : preset, stage, out _);
            if (readiness != null) throw new InvalidOperationException(readiness);
            if (GlobalPresetRequiresNativeTargets(stage))
            {
                var targets = new[] { stage.TargetAdvancedTrainingToughness, stage.TargetAdvancedTrainingPower };
                for (var i = 0; i < targets.Length; i++)
                    if (targets[i] > Character.advancedTraining.level[i]
                        && (preset.Weights[9] <= 0 || preset.AdvancedTrainingWeights[i] <= 0 || targets[i] > 9007199254740991L))
                        throw new FormatException("Mandatory stage AT targets are excluded by preset weights or exceed the exact numeric range.");
            }
            // A disabled optimizer must fail before capture, recalls, settings changes, or gear swaps.
            if (policy == null && !preset.AutoUpdate && !_globalPresetPolicies.TryGetValue(preset.Id, out policy))
                throw new InvalidOperationException("No reusable policy for this preset. Use Preview + Apply first, or enable AutoUpdate.");
            if (manual && CombatPrepRunActive && _combatPrepRun.Automatic)
            {
                // Release the scheduled owner before capture; pause retains its entry and original deadline.
                if (_combatPrepRun.CleanupPending) AdvanceCombatPrep();
                else EndCombatPrep("cancelled", CombatPrepOwnerPausedReason);
            }
            if (manual && !CombatPrepRunActive) ServiceAutomaticRecovery();
            var blocker = LoadoutPresetApplyBlocker();
            if (blocker != null) throw new InvalidOperationException("Preset equip blocked: " + blocker);
            // The explicit manual claim owns its marked CURRENT route independently
            // of the previous baseline receipt. A fresh scheduled entry has already
            // changed the baseline owner; that must not turn this claim into a
            // foreign route (nor give an unmarked route takeover permission).
            Func<bool> manualRouteAuthorized = () => false;
            if (!manual && ManualAdventureOverlayActive && Character.adventure.zone >= 0
                && Character.adventure.zone != 1000 && !ZoneHelpers.ZoneIsTitan(Character.adventure.zone)
                && _manualAdventureOverlayRouteZone == Character.adventure.zone
                && ManualAdventureOverlayGearMatches())
            {
                var zone = Character.adventure.zone;
                var settings = Settings;
                var settingsKey = DelegatedManualRouteSettingsKey();
                var epoch = new LoadoutManager.SnapshotEpoch();
                var entry = _protectedScheduler?.Entry;
                var generation = _protectedGeneration;
                var manualGear = LoadoutManager.CaptureExactLoadout();
                var material = manualGear.Select(x => LoadoutPresetPrimitiveKey(x.equipment)).ToArray();
                var attack = Character.adventure.autoattacking;
                manualRouteAuthorized = () => ReferenceEquals(reference, this) && !_unloaded && epoch.Matches()
                    && ReferenceEquals(Settings, settings) && settingsKey == DelegatedManualRouteSettingsKey()
                    && Settings.UseCustomRebirthPlan && _customStageEquipmentKey == owner
                    && entry == _protectedScheduler?.Entry && generation == _protectedGeneration
                    && ManualAdventureOverlayActive && _manualAdventureOverlayRouteZone == zone
                    && !_manualTitanActive && !_customTitanFightActive && !_customTitanGoldActive
                    && (_questManager == null || _questManager.IsQuesting() < 0)
                    && (Character.adventure.zone == zone || Character.adventure.zone == -1)
                    && Character.adventure.autoattacking == attack && LoadoutManager.ExactLoadoutMatches(manualGear)
                    && !manualGear.Where((x, i) => LoadoutPresetPrimitiveKey(x.equipment) != material[i]).Any();
            }
            // Setup admission can reject known-missing route ownership without
            // inventory/forecast/native-stat capture. Do not put this guard on
            // independent recurring resource observation.
            if (!manual && Character.adventure.zone != -1)
            {
                var controller = Character.adventureController;
                if (controller?.zoneSelector == null || controller.playerController == null || controller.enemyAI == null)
                    throw new InvalidOperationException("Selected setup route retirement controllers are unavailable.");
                var current = _globalPresetReceipt;
                var owned = manualRouteAuthorized() || current != null && GlobalPresetEpochMatches(current.Policy)
                    && (current.Owner == "dashboard" ? !Settings.UseCustomRebirthPlan && _globalPresetManualId == current.Id
                        : Settings.UseCustomRebirthPlan && _customStageEquipmentKey == current.Owner)
                    && (ManualAdventureOverlayActive ? ManualAdventureSupportRouteAuthorized(true, current)
                        : !Settings.ManualAdventureEnabled && current.Policy.ExecutionPreset.Weights.Skip(3).Take(3).Any(x => x > 0)
                            && Character.adventure.zone == current.Execution.Zone
                            && (current.Execution.Zone != 1000 || Character.adventure.itopodStart == current.Execution.Floor
                                && Character.adventure.itopodEnd == current.Execution.Floor));
                if (!owned) throw new InvalidOperationException("Selected setup is waiting for the current adventure owner.");
            }
            var timer = Stopwatch.StartNew();
            var capture = CaptureLoadoutPresetDefinition(continuing ? policy.ExecutionPreset : restoring ? previous.ExecutionPreset : preset, timer, stage);
            if (restoring || continuing)
            {
                capture.AdventureTaskKey = (continuing ? policy : previous).AdventureTaskKey;
                capture.AdventureTask = (continuing ? policy : previous).AdventureTask;
            }
            capture.Horizon = Math.Max(1, Math.Min(604800, horizon));
            var effective = capture.ExecutionPreset;
            var unavailable = Enumerable.Range(0, effective.Weights.Length).Where(i => effective.Weights[i] > 0 && !capture.Snapshot.Available[i]).ToArray();
            if (unavailable.Length > 0) throw new InvalidOperationException(string.Join(" ", unavailable.Select(i =>
                LoadoutPresetPlanner.Keys[i] + ": " + (capture.Snapshot.Reasons[i] ?? "native capture unavailable.")).ToArray()));
            var request = LoadoutPresetRequest(effective, capture.Horizon);
            var definition = GlobalPresetDefinition(preset);
            // A waiting stage's clock can shrink its forecast; replay that context without launching a timer-driven search.
            var context = capture.AdventureTaskKey + ":" + capture.Material + ":" + GlobalPresetStageTargets(capture.Stage) + ":" + capture.Snapshot.ReservedEnergy
                + ":" + LoadoutPresetNumbers(capture.Snapshot.PowerFactors).ToString() + ":" + LoadoutPresetNumbers(capture.Snapshot.ToughnessFactors).ToString();
            var pending = !manual && policy == null ? _globalPresetPendingSearch : null;
            var reuse = pending != null && pending.Event == eventKey && pending.Definition == definition && pending.Context == context
                && pending.Epoch.Matches() && pending.Capture.Instances.Count == capture.Instances.Count
                && pending.Capture.Instances.All(x => capture.Instances.ContainsKey(x.Key) && ReferenceEquals(x.Value, capture.Instances[x.Key]));
            var baseline = policy?.Plan ?? (reuse ? pending.Plan : LoadoutPresetPlanner.Plan(capture.Snapshot, request, timer));
            if (!manual && policy == null && !reuse)
                _globalPresetPendingSearch = new GlobalPresetPendingSearch { Event = eventKey, Definition = definition, Context = context,
                    Epoch = new LoadoutManager.SnapshotEpoch(), Capture = capture, Plan = baseline };
            var plan = reuse ? LoadoutPresetPlanner.ReplayExecution(capture.Snapshot, request, baseline, timer) : baseline;
            var candidate = policy == null ? LoadoutPresetSetupCandidates(plan.Candidates, request.ResourcePrioritiesReviewed).FirstOrDefault()
                : ReplayGlobalPreset(policy, preset, capture, liveResources: true);
            if (candidate == null) throw new InvalidOperationException("No feasible global preset policy was found.");
            blocker = LoadoutPresetApplyBlocker(candidate);
            if (blocker != null) throw new InvalidOperationException("Preset equip blocked: " + blocker);
            if (restoring && !continuing && previous.Candidate.Activities != null)
            {
                var retained = previous.Candidate.Copy(); retained.Support = candidate.Copy().Support;
                retained.Zone = candidate.Zone; retained.Floor = candidate.Floor;
                candidate = LoadoutPresetPlanner.EvaluateExecution(capture.Snapshot, request, candidate.Items,
                    LoadoutPresetPlanner.NormalizeWeights(request.Weights), baseline.Normalization, candidate.Key, candidate.Label, retained);
            }
            var gear = PreflightLoadoutPreset(request, capture, candidate, true);
            var routeReceipt = _globalPresetReceipt;
            var routeEpoch = new LoadoutManager.SnapshotEpoch();
            capture.SelectedRouteAuthorized = manual ? (Func<bool>)(() => true) : () =>
                manualRouteAuthorized() || routeEpoch.Matches() && routeReceipt != null && GlobalPresetEpochMatches(routeReceipt.Policy)
                && routeReceipt.Policy.ExecutionPreset.Weights.Skip(3).Take(3).Any(x => x > 0)
                && (!Settings.ManualAdventureEnabled || ManualAdventureOverlayActive)
                && (routeReceipt.Owner == "dashboard" ? !Settings.UseCustomRebirthPlan && _globalPresetManualId == routeReceipt.Id
                    : Settings.UseCustomRebirthPlan && _customStageEquipmentKey == routeReceipt.Owner)
                && (ManualAdventureOverlayActive ? ManualAdventureSupportRouteAuthorized(true, routeReceipt)
                    : Character.adventure.zone == routeReceipt.Execution.Zone
                        && (routeReceipt.Execution.Zone != 1000 || Character.adventure.itopodStart == routeReceipt.Execution.Floor
                            && Character.adventure.itopodEnd == routeReceipt.Execution.Floor));
            RequireSelectedSetupRoute(gear, new SupportSelection(candidate.Support.Beards,
                candidate.Support.Diggers.Select(x => new SupportDiggerLevel(x.Id, x.Level)).ToArray()), request.PlannedSupport, capture.SelectedRouteAuthorized, ManualAdventureOverlayActive);
            var acquired = continuing ? policy : CreateGlobalPresetPolicy(preset, capture, baseline, candidate);
            acquired.Owner = owner;
            LoadoutPresetDeadline(timer);
            var preview = new LoadoutPresetPreview { Preset = preset, Capture = capture, Plan = baseline };
            PersistProtectedPresetSelection(acquired, owner);
            acquired.MaterialPersistencePending = false;
            // Only this boundary starts mutation. Readiness, search and all preflight failures remain retryable waits.
            ReleaseManualAdventureOverlayForPresetAcquisition(manual);
            BeginPresetSupportAcquisition(eventKey, acquired.Acquisition);
            if (manual || _globalPresetReceipt?.Owner != owner || _globalPresetReceipt?.Id != preset.Id)
                _presetResources = _resourceSetupExecutor.Application;
            _globalPresetApplyingPolicy = acquired; _globalPresetApplyingEvent = eventKey;
            ApplyPreparedLoadoutPreset(preview, request, capture, plan, candidate, gear, "", timer, manual, false, out data, out var execution, out var commit, owner);
            AcquireGlobalPreset(preset, capture, plan, candidate, execution, owner, reason, preset.AutoUpdate ? "optimized" : "cached", commit, acquired, !continuing);
            return "Equipped global preset: " + preset.Name + (preset.AutoUpdate ? " (optimized)" : " (cached policy)");
        }

        private void AcquireGlobalPreset(CustomLoadoutPreset preset, LoadoutPresetCapture capture, PresetPlan plan,
            PresetCandidate candidate, PresetCandidate execution, string owner, string reason, string mode, ExecutionGrant commit, GlobalPresetPolicy acquired = null, bool updateRecipeCache = true)
        {
            RequirePreparedPresetCommit(commit, owner);
            var policy = acquired ?? CreateGlobalPresetPolicy(preset, capture, plan, candidate);
            policy.Owner = owner;
            if (_retainedPresetSelections == null) _retainedPresetSelections = new Dictionary<string, GlobalPresetPolicy>();
            foreach (var ended in _retainedPresetSelections.Where(x => !GlobalPresetEpochMatches(x.Value)).Select(x => x.Key).ToArray()) _retainedPresetSelections.Remove(ended);
            _retainedPresetSelections[owner] = policy;
            if (preset.AdventureDynamicWeight > 0)
            {
                // Our exact gear/support can change the matched stat rule. Only later observations trigger a new task.
                ObserveAdventurePresetTask(out _, out _, out policy.AdventureObservation, out _);
            }
            RequirePreparedPresetCommit(commit, owner);
            if (updateRecipeCache) _globalPresetPolicies[preset.Id] = policy;
            _globalPresetReceipt = new GlobalPresetReceipt { Id = preset.Id, Owner = owner,
                Policy = policy, Execution = execution.Copy(), LastBeardRotation = DateTime.UtcNow,
                Gear = _manualAdventureBaseline.Gear, Support = GlobalPresetSupportState(),
                Allocations = GlobalPresetAllocationState(), EnergyAllocations = GlobalPresetLaneState(true),
                MagicAllocations = GlobalPresetLaneState(false) };
            ObserveGlobalPresetRetirementFootprint(_globalPresetReceipt);
            if (!GlobalPresetHasMagicObjective(policy) && _executionBaselineResourceIntent?.Magic == true
                && _executionBaselineResourceIntent.Desired[ResourceTarget.BloodRitual].Any(x => x > 0))
            {
                _globalPresetReceipt.TemporaryMagicContext = ResourceWriter.CaptureResourceContext(Character);
                _globalPresetReceipt.TemporaryMagicAllocations = GlobalPresetLaneState(false);
            }
            _globalPresetReceipt.AppliedEnergyWeights = Settings.EnergyNguWeights.ToArray();
            _globalPresetReceipt.AppliedMagicWeights = Settings.MagicNguWeights.ToArray();
            _globalPresetReceipt.BaselineSupport = candidate.Copy().Support;
            if (!ManualAdventureOverlayActive) ClearManualAdventureSupportClaim();
            _globalPresetManualId = owner == "dashboard" ? preset.Id : null;
            _presetExecutionBlock = null;
            ClearGlobalPresetWaiting();
            _globalPresetPendingSearch = null;
            _globalPresetApplyingPolicy = null; _globalPresetApplyingEvent = null;
            _globalPresetSelectionEvent = null;
            _customEquipmentApplicationInvalidated = false;
            _loadoutPresetSnapshot["autoUpdate"] = preset.AutoUpdate;
            _loadoutPresetSnapshot["AutoUpdate"] = preset.AutoUpdate;
            _loadoutPresetSnapshot["mode"] = mode;
            _loadoutPresetSnapshot["equipReason"] = reason;
            _loadoutPresetSnapshot["owner"] = owner;
            _loadoutPresetSnapshot["reason"] = "Observed applied policy; allocations refresh without optimizer searches. Temporary owners suspend this receipt.";
        }

        private static string GlobalPresetSupportState()
        {
            var c = Character;
            return string.Join(",", c.beards.beards.Select((x, i) => i + ":" + x.active).ToArray()) + "|"
                + string.Join(",", c.beards.activeBeards.Select(x => x.ToString()).ToArray()) + "|"
                + string.Join(",", c.diggers.diggers.Select((x, i) => i + ":" + x.active + ":" + x.curLevel).ToArray()) + "|"
                + string.Join(",", c.diggers.activeDiggers.Select(x => x.ToString()).ToArray());
        }

        private static string GlobalPresetAllocationState()
        {
            return GlobalPresetLaneState(true) + "|" + GlobalPresetLaneState(false);
        }

        private static string GlobalPresetLaneState(bool energy)
        {
            var c = Character;
            var allocations = energy ? c.NGU.skills.Select(x => x.energy).Concat(c.advancedTraining.energy)
                .Concat(c.augments.augs.SelectMany(x => new[] { x.augEnergy, x.upgradeEnergy }))
                .Concat(new[] { c.machine.speedEnergy, c.wandoos98.wandoosEnergy }).Concat(c.wishes.wishes.Select(x => x.energy))
                : c.NGU.magicSkills.Select(x => x.magic).Concat(c.bloodMagic.ritual.Select(x => x.magic))
                    .Concat(new[] { c.machine.goldMultiMagic, c.wandoos98.wandoosMagic }).Concat(c.wishes.wishes.Select(x => x.magic));
            return string.Join(",", allocations.Select(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray());
        }

        private string StopManualGlobalPreset()
        {
            if (_globalPresetManualId == null) return null;
            ClearPersistedExecutionSelection("dashboard");
            _retainedPresetSelections?.Remove("dashboard");
            var receipt = _globalPresetReceipt;
            var owned = receipt != null && receipt.Owner == "dashboard" && receipt.Id == _globalPresetManualId
                && GlobalPresetEpochMatches(receipt.Policy) && !Settings.UseCustomRebirthPlan
                && !_customRequestedFocusGoal.HasValue && !_manualRebirthPending && !_customPreRebirthPrepRunning
                && !BossPushRunning && !CustomBossFightActive() && !CombatPrepActive
                && !ManualAdventureOwnsEquipment
                && !_customTitanGoldActive && !_customTitanFightActive && !_customAdventureGoldPushActive;
            var energy = owned && receipt.Execution.ControlsEnergy && Settings.ManageEnergy
                && receipt.EnergyAllocations == GlobalPresetLaneState(true);
            var temporaryMagicContext = owned && receipt.TemporaryMagicContext != null ? ResourceWriter.CaptureResourceContext(Character) : null;
            var magic = owned && Settings.ManageMagic && (receipt.Execution.ControlsMagic
                || temporaryMagicContext != null && receipt.TemporaryMagicAllocations == receipt.MagicAllocations
                    && receipt.TemporaryMagicContext.Length == temporaryMagicContext.Length
                    && !temporaryMagicContext.Where((item, i) => !ReferenceEquals(item, receipt.TemporaryMagicContext[i])).Any())
                && receipt.MagicAllocations == GlobalPresetLaneState(false);
            // Retire ownership before exact release, including failures. Keep acquired decisions.
            _globalPresetManualId = null;
            _globalPresetReceipt = null;
            _presetExecutionBlock = null;
            var failed = false;
            foreach (var lane in new[] { true, false })
            {
                if (lane ? !energy : !magic) continue;
                try
                {
                    if (!GlobalPresetEpochMatches(receipt.Policy) || _globalPresetReceipt != null || Settings.UseCustomRebirthPlan
                        || _customRequestedFocusGoal.HasValue
                        || (lane ? receipt.EnergyAllocations : receipt.MagicAllocations) != GlobalPresetLaneState(lane)) continue;
                    BlackBarAllocator.ClearNguAppliedWeights(lane);
                    var result = ReconcileResourceSetup(new ResourceSetupIntent { Owner = "dashboard-release",
                        Desired = ResourceWriter.EmptyDesiredMap(lane, !lane), Energy = lane, Magic = !lane,
                        Authorized = () => GlobalPresetEpochMatches(receipt.Policy) && _globalPresetManualId == null
                            && _globalPresetReceipt == null && !Settings.UseCustomRebirthPlan
                            && !_customRequestedFocusGoal.HasValue && !CombatPrepActive });
                    if (!result.Verified) throw new ResourceMutationException(result);
                }
                catch (Exception error) { failed = true; LogError("Stop global preset " + (lane ? "energy" : "magic"), error); }
            }
            return "Stopped global preset focus." + (failed ? " Owned resource recall failed; ownership was still released."
                : " Released only resource lanes still owned by the preset.");
        }

        private bool GlobalPresetReceiptValid(string owner, bool observeAllocations = true, bool ignoreWaiting = false)
        {
            return (_resourceSetupExecutor.Application.BlockedLanes & 3) == 0
                && _globalPresetReceipt?.Policy.MaterialPersistencePending != true
                && (!_executionClaims.Any(x => !x.Ended && x.ResourcePolicy != null)
                    || ExecutionBaselineResourceMapMatches(_executionBaselineResourceIntent))
                && !(GlobalPresetRequiresNativeTargets(_globalPresetReceipt?.Policy.Stage) && !Settings.ManageEnergy)
                && GetGlobalPresetApplicationIssue(owner, observeAllocations, ignoreWaiting) == PresetApplicationIssue.None;
        }

        private void SuspendCurrentGlobalPresetStage(string owner)
        {
            // A stale/manual receipt must not suspend an unrelated current stage.
            var index = _protectedScheduler?.StageIndex ?? -1;
            if (Settings?.UseCustomRebirthPlan != true || _protectedPlan == null
                || index < 0 || index >= _protectedPlan.Stages.Length
                || _protectedPlan.Stages[index].ExecutionMode != 0
                || string.IsNullOrEmpty(StageGlobalPresetId(_protectedPlan, _protectedPlan.Stages[index]))) return;
            var currentOwner = "stage:" + Settings.CustomPlanFile + ":" + _protectedPlan.Name + ":" + index + ":"
                + _protectedGeneration + ":" + _protectedScheduler.Entry + ":" + _protectedScheduler.Cycle;
            if (owner == currentOwner && _customStageEquipmentKey == owner) SuspendProtectedStageProgress();
        }

        private bool GlobalPresetExecutionPrerequisites(string owner, bool admission, out PresetApplicationIssue issue)
        {
            // Keep observation predicates pure (runtime/UI also use them). Execution
            // ingresses revoke only the current dependent stage's credit on rejection.
            issue = GetGlobalPresetApplicationIssue(owner, admission, !admission);
            var available = admission ? GlobalPresetReceiptValid(owner)
                : issue == PresetApplicationIssue.None || issue == PresetApplicationIssue.WeightsChanged
                    || issue == PresetApplicationIssue.MaterialChanged || issue == PresetApplicationIssue.NativeTargetsChanged;
            available |= !admission && ExecutionSetupClaimsActive && issue == PresetApplicationIssue.EquipmentChanged;
            available |= !admission && issue == PresetApplicationIssue.EquipmentChanged
                && SelectedSetupConflicts.TryGetValue(Character, out var yielded) && yielded.Rebirth == Character.stats.rebirthNumber
                && (yielded.Gear != null || yielded.Support != null);
            if (!available || (_resourceSetupExecutor.Application.BlockedLanes & 3) != 0)
                SuspendCurrentGlobalPresetStage(owner);
            return available;
        }

        private PresetApplicationIssue GetGlobalPresetApplicationIssue(string owner, bool observeAllocations = true, bool ignoreWaiting = false)
        {
            var receipt = _globalPresetReceipt;
            if (receipt == null || Settings == null) return PresetApplicationIssue.NoReceipt;
            if (receipt.Owner != owner) return PresetApplicationIssue.DifferentOwner;
            if (!GlobalPresetEpochMatches(receipt.Policy)) return PresetApplicationIssue.EpochChanged;
            if (SelectedSetupConflicts.TryGetValue(Character, out var conflict) && conflict.Rebirth == Character.stats.rebirthNumber
                && (conflict.Gear != null || conflict.Support != null)) return PresetApplicationIssue.EquipmentChanged;
            if (CombatPrepActive || receipt.Execution.ControlsEnergy && !Settings.ManageEnergy
                || receipt.Execution.ControlsMagic && !Settings.ManageMagic || _protectedTimingLoadingSave || Controller.midDrag
                || Character.bossController == null
                || !ExecutionClaimsActive && (_manualRebirthPending || _customPreRebirthPrepRunning || BossPushRunning || CustomBossFightActive()
                || _customTitanGoldActive || _customTitanFightActive || _customAdventureGoldPushActive
                || (!Settings.ManualAdventureEnabled && _maxItopodPushRemainingDeaths > 0) || ZoneHelpers.ZoneIsTitan(Character.adventure.zone)
                || (ManualAdventureOwnsEquipment && !ManualAdventureOverlayActive))
                || _customRequestedFocusGoal.HasValue) return PresetApplicationIssue.Blocked;
            if (GlobalPresetNativeTargetsYielded(owner)
                || !GlobalPresetNativeTargetsMatch(receipt.Policy.Stage, owner)) return PresetApplicationIssue.NativeTargetsChanged;
            if ((!ignoreWaiting && _globalPresetWaitingEvent == owner + ":" + receipt.Id)
                || GlobalPresetReadiness(receipt.Policy.ExecutionPreset, receipt.Policy.Stage, out _, Settings.ManualAdventureEnabled || ExecutionRouteClaimActive) != null
                || GlobalPresetAdventureChanged(receipt.Policy)) return PresetApplicationIssue.Blocked;
            if (owner == "dashboard" ? Settings.UseCustomRebirthPlan || _globalPresetManualId != receipt.Id
                : !Settings.UseCustomRebirthPlan || _customStageEquipmentKey != owner) return PresetApplicationIssue.SelectionChanged;
            if (owner != "dashboard" && _protectedScheduler != null && _protectedPlan != null)
            {
                var index = _protectedScheduler.StageIndex;
                if (index < 0 || index >= _protectedPlan.Stages.Length
                    || StageGlobalPresetId(_protectedPlan, _protectedPlan.Stages[index]) != receipt.Id
                    || _customStageKey != _protectedPlan.Name + ":" + index + ":" + _protectedGeneration + ":" + _protectedScheduler.Entry)
                    return PresetApplicationIssue.SelectionChanged;
            }
            var preset = receipt.Policy.ExecutionPreset;
            if (!Settings.ManualAdventureEnabled && !ExecutionRouteClaimActive && preset.Weights.Skip(3).Take(3).Any(x => x > 0)
                && !(ignoreWaiting && receipt.SupportRouteRetired && Character.adventure.zone == -1)
                && (Character.adventure.zone != receipt.Policy.Candidate.Zone
                    || receipt.Policy.Candidate.Zone == 1000 && (Character.adventure.itopodStart != receipt.Policy.Candidate.Floor
                         || Character.adventure.itopodEnd != receipt.Policy.Candidate.Floor))) return PresetApplicationIssue.Blocked;
            if (_customEquipmentApplicationInvalidated || !(ManualAdventureOverlayActive ? ManualAdventureOverlayGearMatches() : GlobalPresetGearMatches(receipt)))
                return PresetApplicationIssue.EquipmentChanged;
            if (!receipt.CurrentGearContinuation && receipt.Policy.Items.Where((x, i) => LoadoutPresetPrimitiveKey(x) != receipt.Policy.Material[i]).Any())
                return PresetApplicationIssue.MaterialChanged;
            if (receipt.AppliedEnergyWeights == null || !receipt.AppliedEnergyWeights.SequenceEqual(Settings.EnergyNguWeights)
                || receipt.AppliedMagicWeights == null || !receipt.AppliedMagicWeights.SequenceEqual(Settings.MagicNguWeights)) return PresetApplicationIssue.WeightsChanged;
            if (observeAllocations && (_resourceSetupExecutor.Application.BlockedLanes & 3) != 0)
                return PresetApplicationIssue.ResourceAuthorityUnresolved;
            return observeAllocations && (!CurrentGlobalPresetSupportMatches(receipt) || receipt.Allocations != GlobalPresetAllocationState())
                ? PresetApplicationIssue.ObservationsChanged : PresetApplicationIssue.None;
        }

        private bool CurrentGlobalPresetSupportMatches(GlobalPresetReceipt receipt)
        {
            if (ExecutionSetupClaimsActive) return ExecutionSetupVerified();
            return ManualAdventureOverlayActive && _manualAdventureSupportApplied != null
                ? _manualAdventureSupportApplied.StableKey == CaptureSupportState().Selection.StableKey
                : receipt.Support == GlobalPresetSupportState();
        }

        private static bool GlobalPresetGearMatches(GlobalPresetReceipt receipt)
        {
            if (reference.ExecutionSetupClaimsActive) return reference.ExecutionSetupVerified();
            var gear = LoadoutManager.CaptureExactLoadout();
            return gear.Length == receipt.Gear.Length && !gear.Where((x, i) => x.slot != receipt.Gear[i].slot
                || (x.id == 0 ? receipt.Gear[i].id != 0 : !ReferenceEquals(x.equipment, receipt.Gear[i].equipment))).Any();
        }

        private void RunCustomAutoBoost()
        {
            var receipt = _globalPresetReceipt;
            var pending = RetainedExecutionPresetPolicies()
                .Concat(receipt?.Policy == null ? new GlobalPresetPolicy[0] : new[] { receipt.Policy })
                .Where(x => x.MaterialPersistencePending).Distinct().ToArray();
            if (pending.Length != 0)
            {
                foreach (var selected in pending)
                {
                    PersistApprovedExecutionMaterialRevision(selected, selected.Owner);
                    selected.MaterialPersistencePending = false;
                }
                return; // Retry storage, never the already-committed native effect.
            }
            if (_focusSetupSelection?.PersistencePending == true)
            {
                PersistFocusSelection(_focusSetupSelection);
                return;
            }
            // Let a restored cached owner replay before boosting its pinned material again,
            // but do not wait for a preset replay that prep/recovery currently forbids.
            if (!CombatPrepActive && receipt != null && !receipt.Policy.Preset.AutoUpdate && _customEquipmentApplicationInvalidated
                && GlobalPresetEpochMatches(receipt.Policy) && GlobalPresetGearMatches(receipt)) return;
            var owned = receipt != null && GlobalPresetReceiptValid(receipt.Owner, false, true);
            var policy = receipt?.Policy;
            var owner = receipt?.Owner; var acquisition = policy?.Acquisition;
            var observePreset = policy?.Preset.AutoUpdate == true && !string.IsNullOrEmpty(owner) && !string.IsNullOrEmpty(acquisition)
                && policy.Owner == owner
                && policy.Items != null && policy.Material != null && GlobalPresetEpochMatches(policy)
                && !policy.Items.Where((item, i) => LoadoutPresetPrimitiveKey(item) != policy.Material[i]).Any();
            var approvedBefore = observePreset ? policy.Material.ToArray() : null;
            var settings = Settings;
            var gear = observePreset ? LoadoutManager.CaptureExactLoadout() : null;
            var ids = observePreset ? policy.Items.Select(item => item?.id ?? 0).ToArray() : null;
            var resourceObjects = observePreset ? ResourceWriter.CaptureResourceContext(Character) : null;
            var accounting = observePreset ? ResourceWriter.AllocatedResourceKey() : null;
            var support = observePreset ? CaptureSupportState().StableKey : null;
            var supportObjects = observePreset ? SupportObjects() : null;
            try { _invManager.ManageCustomAutoBoost(); }
            finally
            {
                // Only verified leaves approve material. This wrapper observes their
                // approved delta to request AutoUpdate, never adopts native observations.
                if (observePreset && ReferenceEquals(reference, this) && !_unloaded && _executionLifecycle == ExecutionLifecycle.Running
                    && ReferenceEquals(Settings, settings) && !Controller.midDrag
                    && ReferenceEquals(receipt, _globalPresetReceipt) && receipt.Owner == owner
                    && ReferenceEquals(receipt.Policy, policy) && policy.Owner == owner && policy.Acquisition == acquisition
                    && policy.Preset.AutoUpdate && GlobalPresetEpochMatches(policy)
                    && LoadoutManager.ExactLoadoutMatches(gear) && CaptureSupportState().StableKey == support
                    && ResourceWriter.AllocatedResourceKey() == accounting
                    && SupportObjects().Length == supportObjects.Length
                    && !SupportObjects().Where((item, i) => !ReferenceEquals(item, supportObjects[i])).Any()
                    && ResourceWriter.CaptureResourceContext(Character).Length == resourceObjects.Length
                    && !ResourceWriter.CaptureResourceContext(Character).Where((item, i) => !ReferenceEquals(item, resourceObjects[i])).Any())
                {
                    var instances = LoadoutPresetInstances().Values.ToArray();
                    if (!policy.Material.SequenceEqual(approvedBefore)
                        && !policy.Items.Where((item, i) => LoadoutPresetPrimitiveKey(item) != policy.Material[i] || (item?.id ?? 0) != ids[i]
                             || item != null && instances.Count(value => ReferenceEquals(item, value)) != 1).Any())
                    {
                        _globalPresetSelectionEvent = acquisition;
                        if (owned && GlobalPresetReceiptValid(owner, false, true)) InvalidateCustomEquipmentApplication();
                        else { _customEquipmentApplicationInvalidated = true; _customLastQuickResourceRefresh = DateTime.MinValue; }
                    }
                }
            }
        }

        private static bool GlobalPresetEpochMatches(GlobalPresetPolicy policy)
        {
            var c = Character;
            if (!policy.Epoch.Matches() || c == null) return false;
            var objects = new object[] { c, Controller, c.inventory, c.NGU, c.magic, c.adventure, c.beards, c.diggers, c.stats };
            return policy.EpochObjects.Length == objects.Length && policy.EpochObjects.Where((x, i) => !ReferenceEquals(x, objects[i])).Any() == false;
        }

        private static bool GlobalPresetAdventureChanged(GlobalPresetPolicy policy)
        {
            if (Settings.ManualAdventureEnabled || reference.ExecutionSetupClaimsActive || reference.ExecutionRouteClaimActive) return false;
            if (policy.Preset.AdventureDynamicWeight <= 0) return false;
            ObserveAdventurePresetTask(out var mode, out var zone, out var key, out _);
            // Deferred combat owners always outrank the anti-feedback baseline.
            return mode == CustomAdventureModeManualZone || mode == CustomAdventureModeMaxItopodPush
                || mode == CustomAdventureModeFarmZone && ZoneHelpers.ZoneIsTitan(zone)
                || key != policy.AdventureObservation && key != policy.AdventureTaskKey;
        }

        private bool PrepareGlobalPresetAdventureChange(string owner)
        {
            var receipt = _globalPresetReceipt;
            if (receipt == null || receipt.Owner != owner || !GlobalPresetAdventureChanged(receipt.Policy)) return true;
            SuspendProtectedStageProgress();
            if (!receipt.Policy.Preset.AutoUpdate)
            {
                SetCustomAutomationStatus("Adventure activity changed. Preview + Apply the new activity or enable AutoUpdate; cached gear was not re-optimized.");
                return false;
            }
            _customEquipmentApplicationInvalidated = true;
            return true;
        }

        private static string StageGlobalPresetId(CustomPlan plan, CustomPlanStage stage)
        {
            return stage.PresetId == "@active" ? plan.ActivePresetId ?? "" : stage.PresetId ?? "";
        }

        private bool GlobalPresetStageReceiptValid(CustomPlan plan)
        {
            var index = _protectedScheduler?.StageIndex ?? -1;
            if (index < 0 || index >= plan.Stages.Length || _globalPresetReceipt == null
                || StageGlobalPresetId(plan, plan.Stages[index]) != _globalPresetReceipt.Id) return false;
            var owner = "stage:" + Settings.CustomPlanFile + ":" + plan.Name + ":" + index + ":"
                + _protectedGeneration + ":" + _protectedScheduler.Entry + ":" + _protectedScheduler.Cycle;
            return GlobalPresetReceiptValid(owner);
        }

        private bool GlobalPresetOwnsActivity(CustomPlan plan)
        {
            if (_globalPresetManualId != null && !Settings.UseCustomRebirthPlan) return true;
            if (plan == null || !Settings.UseCustomRebirthPlan || !plan.StageSchedulerEnabled) return false;
            var index = CustomStageAllocator.GetSchedulerSnapshot(plan, Character.rebirthTime.totalseconds)?.ActiveStageIndex ?? -1;
            return index >= 0 && index < plan.Stages.Length && plan.Stages[index].ExecutionMode == 0
                && !string.IsNullOrEmpty(plan.Stages[index].PresetId);
        }

        private bool TryRouteGlobalPresetManualAdventure(CustomPlan plan)
        {
            if (Settings.ManualAdventureEnabled) return false;
            string id = null, owner = null;
            if (_globalPresetManualId != null && !Settings.UseCustomRebirthPlan)
            {
                id = _globalPresetManualId;
                owner = "dashboard";
            }
            else if (plan != null && Settings.UseCustomRebirthPlan && plan.StageSchedulerEnabled)
            {
                var index = CustomStageAllocator.GetSchedulerSnapshot(plan, Character.rebirthTime.totalseconds)?.ActiveStageIndex ?? -1;
                if (index >= 0 && index < plan.Stages.Length)
                {
                    id = StageGlobalPresetId(plan, plan.Stages[index]);
                    owner = _customStageEquipmentKey;
                }
            }
            if (string.IsNullOrEmpty(id)) return false;
            var preset = _globalPresetReceipt?.Id == id && _globalPresetReceipt.Owner == owner
                ? _globalPresetReceipt.Policy.Preset : CustomPlanStore?.PresetLibrary.Get(id);
            if (preset == null || preset.AdventureDynamicWeight <= 0) return false;
            ObserveAdventurePresetTask(out var mode, out var zone, out _, out _);
            if (mode != CustomAdventureModeManualZone) return false;
            SuspendProtectedStageProgress();
            if (_delegatedManualRoute != null && !_delegatedManualRoute.Authorized())
            {
                SetCustomAutomationStatus("Delegated manual route ownership changed; dependent setup is waiting.");
                return true;
            }
            if ((_questManager != null && _questManager.IsQuesting() >= 0) || CustomGoalBlocksAdventureGoldPush(plan, out _))
            {
                ResetManualAdventureSetup();
                return true;
            }
            // Even Keep current combat changes route/support. Remember a restoration event before handing over.
            if (!_globalPresetManualAdventureActive) InvalidateCustomEquipmentApplication();
            _globalPresetManualAdventureActive = true;
            var routed = RouteManualAdventureZone(zone, false, "Adventure-dynamic deferred to manual combat");
            if (routed && _globalPresetReceipt?.Id == id && _globalPresetReceipt.Owner == owner)
                AcquireDelegatedManualRoute(_globalPresetReceipt, zone);
            return routed;
        }

        private JSONObject GlobalPresetAppliedSummary()
        {
            var snapshot = GetLoadoutPresetRuntime();
            if (snapshot == null) return null;
            var summary = new JSONObject();
            foreach (var field in new[] { "id", "candidateKey", "status", "mode", "owner", "currentOwner", "active", "appliedAt", "savedChangesRequireReequip" })
                summary[field] = snapshot[field];
            summary["name"] = snapshot["appliedName"].IsString ? snapshot["appliedName"].Value : snapshot["preset"]["Name"].Value;
            summary["autoUpdate"] = snapshot["appliedAutoUpdate"].IsBoolean ? snapshot["appliedAutoUpdate"].AsBool : snapshot["preset"]["AutoUpdate"].AsBool;
            return summary;
        }

        private bool RetirePreviousGlobalPresetStageRoute(string successor)
        {
            var receipt = _globalPresetReceipt;
            if (receipt?.RetirementIssue != null && receipt.Policy.Epoch.Matches() && receipt.Owner != successor)
            {
                SuspendProtectedStageProgress();
                SetResourceExecution(ResourceExecutionState.NeedsUser, successor, receipt.RetirementIssue);
                return false;
            }
            if (receipt == null || receipt.Owner == "dashboard" || receipt.Owner == successor || Character.adventure.zone == -1
                || ExecutionRouteClaimActive || ManualAdventureOverlayActive && _manualAdventureOverlayRouteZone == Character.adventure.zone
                || !receipt.Policy.ExecutionPreset.Weights.Skip(3).Take(3).Any(x => x > 0)) return true;
            var baseline = _executionBaseline;
            var owner = _customStageEquipmentKey;
            var file = Settings.CustomPlanFile;
            ExecutionClaim retirement = null;
            var mutationStarted = false;
            try
            {
                // Restart withdraws forward baseline authority, not the exact applied
                // route witness. Retire only that route before selecting its successor.
                retirement = BeginExecutionClaim("baseline-handoff", Guid.NewGuid().ToString("N"), ExecutionDomain.Route,
                    () => Settings.UseCustomRebirthPlan && Settings.CustomPlanFile == file && owner == _customStageEquipmentKey
                        && ReferenceEquals(receipt, _globalPresetReceipt) && ReferenceEquals(baseline, _executionBaseline)
                        && baseline?.Owner == receipt.Owner && baseline.Acquisition == receipt.Policy.Acquisition
                        && ReferenceEquals(receipt.Policy, RetainedPresetSelection(receipt.Owner, receipt.Id)));
                var expires = LoadoutPresetClock + 2;
                var owned = CaptureAppliedPresetRouteRetirement(retirement,
                    () => ExecutionClaimCurrent(retirement) && LoadoutPresetClock < expires);
                if (!owned()) throw new InvalidOperationException("Previous farming setup or route changed; review the current owner.");
                mutationStarted = true;
                ApplyExecutionClaim(retirement, new SelectedSetupRequirements { ControlsGear = false,
                    Support = CaptureSupportState().Selection, Recall = GearRecallFootprint.None, RetireRoute = true,
                    Authorized = owned, RouteRetirementAuthorized = owned });
                return true;
            }
            catch (Exception error)
            {
                SuspendProtectedStageProgress();
                var message = "Scheduled preset route handoff blocked: " + error.Message;
                if (mutationStarted) receipt.RetirementIssue = message;
                SetResourceExecution(ResourceExecutionState.NeedsUser, successor, message);
                return false;
            }
            finally { EndExecutionClaim(retirement, false); }
        }

        private bool RunGlobalPresetStage(CustomPlan plan, CustomStageRuntime runtime, string owner, string reason = "stage-entry")
        {
            if (!ReconcileManualAdventureClaims()) return false;
            SuspendProtectedStageProgress();
            var remaining = runtime.RemainingSeconds;
            if (remaining <= 0)
            {
                var budget = plan.TimeTriggerEnabled && plan.TimeTriggerSeconds > 0 ? plan.TimeTriggerSeconds : plan.StageDurationSeconds;
                var left = budget - Character.rebirthTime.totalseconds;
                remaining = left > 0 ? (int)Math.Min(604800, Math.Ceiling(left)) : 3600;
            }
            if (!ReconcileGlobalPreset(owner, StageGlobalPresetId(plan, runtime.Stage), runtime.Stage, remaining, reason)) return false;
            _customEquipmentApplicationInvalidated = false;
            CommitProtectedStageApplication(plan);
            SetCustomAutomationStatus(BuildRuntimeStatus(plan, runtime, "global preset: " + _globalPresetReceipt.Policy.Preset.Name));
            return true;
        }

        private bool RefreshGlobalPresetAllocations(string owner)
        {
            return RefreshGlobalPresetExecution(owner, null);
        }

        private void RotateGlobalPresetBeards(DateTime now)
        {
            if (!ReconcileManualAdventureClaims()) return;
            var receipt = _globalPresetReceipt;
            if (receipt == null || !GlobalPresetExecutionPrerequisites(receipt.Owner, true, out _)) return;
            if (Settings.ManualAdventureEnabled && ManualAdventureSupportOverridesActive) return;
            if (receipt.Policy.Preset.SupportMode != "planned" || receipt.Policy.Preset.BeardRotation == null
                || (now - receipt.LastBeardRotation).TotalSeconds < 60) return;
            RefreshGlobalPresetExecution(receipt.Owner, now);
        }

        private bool RefreshGlobalPresetExecution(string owner, DateTime? rotationAt)
        {
            if (!ReconcileManualAdventureClaims()) return false;
            if (!PrepareGlobalPresetWeightChange(owner)) return false;
            if (!GlobalPresetExecutionPrerequisites(owner, false, out var issue)) return false;
            var receipt = _globalPresetReceipt;
            if (receipt.Policy.MaterialPersistencePending)
            {
                try { PersistApprovedExecutionMaterialRevision(receipt.Policy, owner); receipt.Policy.MaterialPersistencePending = false; }
                catch (Exception error)
                {
                    SuspendCurrentGlobalPresetStage(owner);
                    SetResourceExecution(ResourceExecutionState.Waiting, owner, "Approved material is waiting for durable storage: " + error.Message);
                    return false;
                }
            }
            var preset = receipt.Policy.ExecutionPreset;
            if (issue == PresetApplicationIssue.MaterialChanged)
                RecordSelectedSetupConflict(Character, Character.stats.rebirthNumber, "gear", "Selected material changed without an approved causal revision; review current setup.");
            var foreignSetup = SelectedSetupConflicts.TryGetValue(Character, out var yieldedSetup) && yieldedSetup.Rebirth == Character.stats.rebirthNumber
                && (yieldedSetup.Gear != null || yieldedSetup.Support != null);
            var pendingRotation = PendingPresetRotation(owner + ":" + preset.Id, receipt.Policy.Acquisition);
            if (pendingRotation != null) rotationAt = pendingRotation.At;
            var currentGear = foreignSetup || ExecutionSetupClaimsActive || Settings.ManualAdventureEnabled || receipt.CurrentGearContinuation
                || issue == PresetApplicationIssue.MaterialChanged || issue == PresetApplicationIssue.NativeTargetsChanged;
            var currentSupport = foreignSetup || ExecutionSetupClaimsActive || issue == PresetApplicationIssue.NativeTargetsChanged
                || Settings.ManualAdventureEnabled && ManualAdventureSupportOverridesActive;
            // Allocation ticks observe support; only an unoverridden minute event may change the base selection.
            var observeCurrentSupport = currentSupport || (Settings.ManualAdventureEnabled && !rotationAt.HasValue);
            var resourceOnly = foreignSetup || Settings.ManualAdventureEnabled || ExecutionRouteClaimActive || ExecutionSetupClaimsActive || issue == PresetApplicationIssue.NativeTargetsChanged;
            if (rotationAt.HasValue && (currentSupport || preset.SupportMode != "planned" || preset.BeardRotation == null)) return false;
            var mutationStarted = false;
            try
            {
                RequirePendingPresetRotationSupport(pendingRotation);
                var ownedPendingSupport = pendingRotation?.Mutation != null;
                if ((resourceOnly || receipt.CurrentGearContinuation) && ((!ownedPendingSupport && !CurrentGlobalPresetSupportMatches(receipt))
                    || receipt.EnergyAllocations != GlobalPresetLaneState(true) || receipt.MagicAllocations != GlobalPresetLaneState(false))) return false;
                var timer = Stopwatch.StartNew();
                var capture = CaptureLoadoutPresetRouteExecution(preset, timer, receipt.Policy.Stage, receipt.Policy.Candidate);
                capture.AdventureTaskKey = receipt.Policy.AdventureTaskKey;
                capture.AdventureTask = receipt.Policy.AdventureTask;
                capture.Horizon = receipt.Policy.Horizon;
                var request = LoadoutPresetRequest(preset, capture.Horizon);
                if (observeCurrentSupport) request.PlannedSupport = false;
                var execution = pendingRotation?.Execution ?? receipt.Execution;
                if (pendingRotation == null && !observeCurrentSupport && receipt.BaselineSupport != null)
                {
                    execution = execution.Copy();
                    execution.Support = new PresetCandidate { Support = receipt.BaselineSupport }.Copy().Support;
                }
                if (rotationAt.HasValue && pendingRotation == null)
                {
                    var beards = LoadoutPresetPlanner.SelectRotationBeards(capture.Snapshot, preset.BeardPriority, preset.BeardRotation,
                        receipt.Execution.Support.Beards, _globalPresetBeardRandom);
                    execution = receipt.Execution.Copy();
                    execution.Support = LoadoutPresetPlanner.RotationSupport(capture.Snapshot, request, receipt.Policy.Candidate.Support, beards);
                }
                var candidate = ReplayGlobalPreset(receipt.Policy, receipt.Policy.Preset, capture, execution, currentGear, observeCurrentSupport, resourceOnly, true);
                if (currentGear || resourceOnly)
                {
                    if (!candidate.Feasible || !candidate.Score.HasValue) throw new InvalidOperationException("Acquired policy is no longer feasible with current gear/support.");
                }
                else PreflightLoadoutPreset(request, capture, candidate, true);
                LoadoutPresetDeadline(timer);
                if ((resourceOnly || receipt.CurrentGearContinuation) && ((!ownedPendingSupport && !CurrentGlobalPresetSupportMatches(receipt))
                    || receipt.EnergyAllocations != GlobalPresetLaneState(true) || receipt.MagicAllocations != GlobalPresetLaneState(false)))
                    throw new InvalidOperationException("Preset ownership changed during refresh preflight; resources were not recalled.");
                var selection = new SupportSelection(candidate.Support.Beards,
                    candidate.Support.Diggers.Select(x => new SupportDiggerLevel(x.Id, x.Level)).ToArray());
                if (_resourceSetupExecutor.Application.BlockedLanes != 0
                    && (selection.StableKey != capture.Snapshot.CurrentSelection.StableKey
                        || _presetSupportContinuation != null || _presetSupportPresentationPending))
                {
                    SuspendCurrentGlobalPresetStage(owner);
                    SetResourceExecution(ResourceExecutionState.NeedsUser, owner,
                        "Resource authority is unresolved; dependent support changes are waiting.");
                    return false;
                }
                BeginPresetSupportAcquisition(owner + ":" + preset.Id, receipt.Policy.Acquisition);
                if (rotationAt.HasValue && pendingRotation == null)
                    _presetSupportAcquisition.Rotation = new PresetRotationAttempt { Execution = candidate.Copy(), At = rotationAt.Value };
                Func<bool> refreshAuthorized = () => ReferenceEquals(_globalPresetReceipt, receipt)
                    && GlobalPresetEpochMatches(receipt.Policy)
                    && GlobalPresetNativeTargetsMatch(receipt.Policy.Stage, owner) && !GlobalPresetNativeTargetsYielded(owner)
                    && (owner == "dashboard" ? !Settings.UseCustomRebirthPlan && _globalPresetManualId == receipt.Id
                        : Settings.UseCustomRebirthPlan && _customStageEquipmentKey == owner);
                var manualRoute = receipt.ManualSupportRoute;
                if (manualRoute == null && rotationAt.HasValue && ManualAdventureOverlayActive && !ManualAdventureSupportOverridesActive
                    && Character.adventure.zone >= 0 && Character.adventure.zone != 1000
                    && _manualAdventureOverlayRouteZone == Character.adventure.zone)
                {
                    var zone = Character.adventure.zone;
                    var manualZone = Settings.ManualAdventureZone;
                    var attack = Character.adventure.autoattacking;
                    var gear = LoadoutManager.CaptureExactLoadout();
                    var material = gear.Select(x => LoadoutPresetPrimitiveKey(x.equipment)).ToArray();
                    var settings = Settings; var settingsKey = DelegatedManualRouteSettingsKey();
                    var routeObjects = CombatPrepAdventureRouteObjects();
                    var resourceObjects = ResourceWriter.CaptureResourceContext(Character); var supportObjects = SupportObjects();
                    Func<bool> objectsOwned = () => {
                        var resources = ResourceWriter.CaptureResourceContext(Character); var support = SupportObjects();
                        return resources.Length == resourceObjects.Length && !resources.Where((x, i) => !ReferenceEquals(x, resourceObjects[i])).Any()
                            && support.Length == supportObjects.Length && !support.Where((x, i) => !ReferenceEquals(x, supportObjects[i])).Any()
                            && !CombatPrepAdventureRouteObjects().Where((x, i) => !ReferenceEquals(x, routeObjects[i])).Any();
                    };
                    var epoch = new LoadoutManager.SnapshotEpoch();
                    manualRoute = new ManualSupportRouteClaim { Zone = zone,
                        Authorized = () => refreshAuthorized() && epoch.Matches() && ManualAdventureOverlayActive
                            && ReferenceEquals(Settings, settings) && settingsKey == DelegatedManualRouteSettingsKey() && objectsOwned()
                            && (!SelectedSetupConflicts.TryGetValue(Character, out var routeConflict) || routeConflict.Rebirth != Character.stats.rebirthNumber || routeConflict.Route == null)
                            && !ManualAdventureSupportOverridesActive && Settings.ManualAdventureZone == manualZone
                            && _manualAdventureOverlayRouteZone == zone && Character.adventure.autoattacking == attack
                            && LoadoutManager.ExactLoadoutMatches(gear)
                            && !gear.Where((x, i) => LoadoutPresetPrimitiveKey(x.equipment) != material[i]).Any() };
                    var domains = ExecutionDomain.Route | ExecutionDomain.NativeSettings | ExecutionDomain.Gear | ExecutionDomain.Support
                        | (candidate.ControlsEnergy ? ExecutionDomain.Energy : 0) | (candidate.ControlsMagic ? ExecutionDomain.Magic : 0);
                    manualRoute.Grant = ResolveExecutionGrant(owner, receipt.Policy.Acquisition, domains, manualRoute.Authorized,
                        resolvedClaims: _executionClaims.Where(x => !x.Ended && (x.Domains & domains) != 0).ToArray());
                }
                Func<bool> manualRouteAuthorized = () => manualRoute != null && manualRoute.Authorized()
                    && !_manualTitanActive && !_customTitanFightActive && !_customTitanGoldActive
                    && (_questManager == null || _questManager.IsQuesting() < 0)
                    && (Character.adventure.zone == -1 || Character.adventure.zone == manualRoute.Zone);
                Func<bool> routeAuthorized = () => manualRouteAuthorized() || refreshAuthorized() && !Settings.ManualAdventureEnabled
                    && preset.Weights.Skip(3).Take(3).Any(x => x > 0)
                    && (Character.adventure.zone == -1 || Character.adventure.zone == receipt.Execution.Zone
                        && (receipt.Execution.Zone != 1000 || Character.adventure.itopodStart == receipt.Execution.Floor
                            && Character.adventure.itopodEnd == receipt.Execution.Floor));
                var routeSnapshot = capture.Snapshot;
                Action applySupport = () => {
                    var activeOwnedRoute = Character.adventure.zone != -1 && routeAuthorized();
                    try
                    {
                        ApplySelectedSetup(new SelectedSetupRequirements { Owner = owner,
                            Gear = LoadoutManager.CaptureExactLoadout(), Recall = GearRecallFootprint.None,
                            Support = selection, ChangeSupport = preset.SupportMode == "planned",
                            Budget = preset.DiggerBudgetPercent / 100d,
                            Authorized = () => refreshAuthorized() && (manualRoute == null || manualRouteAuthorized()),
                            OwnsManualRoute = manualRoute != null,
                            RouteRetirementAuthorized = routeAuthorized });
                    }
                    finally
                    {
                        if (activeOwnedRoute && Character.adventure.zone == -1)
                        {
                            receipt.SupportRouteRetired = true;
                            receipt.ManualSupportRoute = manualRoute;
                        }
                    }
                };
                Action resumeRoute = () => {
                    if (ExecutionRouteClaimActive && manualRoute == null) return;
                    if (!receipt.SupportRouteRetired) return;
                    if (!routeAuthorized() || CaptureSupportState().Selection.StableKey != selection.StableKey
                        || Character.bossController == null || Character.bossController.isFighting || Character.bossController.nukeBoss)
                        throw new InvalidOperationException("Preset support route prerequisites or authority changed; route remains retired.");
                    if (manualRoute != null)
                    {
                        Func<bool> prerequisites = () => manualRouteAuthorized()
                            && Character.adventureController?.zoneSelector != null
                            && Character.adventureController.playerController != null && Character.adventureController.enemyAI != null
                            && !Controller.midDrag
                            && _resourceSetupExecutor.Application.BlockedLanes == 0
                            && CaptureSupportState().Selection.StableKey == selection.StableKey
                            && Character.bossController != null && !Character.bossController.isFighting && !Character.bossController.nukeBoss
                            && LoadoutPresetAllocationsMatch(ComposePresetResourceDemands(routeSnapshot, candidate));
                        // Keep the original grant across support/resource callbacks and
                        // pending retries; matching physical state cannot renew Route authority.
                        if (Character.adventure.zone != -1 || !VerifyExecutionPrerequisites(manualRoute.Grant, manualRoute.Grant.Domains, prerequisites))
                            throw new InvalidOperationException("Manual support route is waiting for verified current setup and funding.");
                        // Direct native route entry only: ManualZone would also
                        // change attack preferences and run combat automation.
                        Character.adventureController.zoneSelector.changeZone(manualRoute.Zone);
                        if (Character.adventure.zone != manualRoute.Zone || !VerifyExecutionPrerequisites(manualRoute.Grant, manualRoute.Grant.Domains, prerequisites))
                            throw new InvalidOperationException("Manual support route resume could not be verified.");
                        receipt.SupportRouteRetired = false;
                        receipt.ManualSupportRoute = null;
                        return;
                    }
                    // Recurring refresh never turns a user's native attack preference on.
                    if (!Character.adventure.autoattacking)
                        throw new InvalidOperationException("Preset support route is waiting for native autoattack.");
                    ApplyLoadoutPresetRoute(candidate, routeSnapshot, () => refreshAuthorized()
                        && !Settings.ManualAdventureEnabled
                        && CaptureSupportState().Selection.StableKey == selection.StableKey
                        && Character.bossController != null && !Character.bossController.isFighting && !Character.bossController.nukeBoss
                        && Character.adventure.autoattacking
                        && LoadoutPresetAllocationsMatch(ComposePresetResourceDemands(routeSnapshot, candidate)));
                    receipt.SupportRouteRetired = false;
                };
                if (selection.StableKey == capture.Snapshot.CurrentSelection.StableKey
                    && _resourceSetupExecutor.Application.BlockedLanes == 0
                    && LoadoutPresetAllocationsMatch(ComposePresetResourceDemands(capture.Snapshot, candidate)))
                {
                    if (_presetResourcePresentationPending) ApplyLoadoutPresetAllocations(ComposePresetResourceDemands(capture.Snapshot, candidate));
                    if (_presetSupportPresentationPending || _presetSupportContinuation != null)
                        applySupport();
                    RequirePendingPresetRotationSupport(_presetSupportAcquisition.Rotation);
                    resumeRoute();
                    ObserveGlobalPresetApplication(receipt, candidate, rotationAt, issue);
                    return GlobalPresetReceiptValid(owner);
                }
                if (!observeCurrentSupport && (selection.StableKey != capture.Snapshot.CurrentSelection.StableKey
                    || _presetSupportContinuation != null || _presetSupportPresentationPending))
                {
                    applySupport();
                    mutationStarted = true;
                }
                RequireGlobalPresetNativeTargets(receipt.Policy.Stage, owner, "Native AT menu targets changed during refresh; newer settings were not overwritten.");
                // Replay the acquired split/activity policy; an explicit overlay supplies only actual gear/support.
                mutationStarted = true;
                var actual = CaptureLoadoutPresetRouteExecution(preset, timer, receipt.Policy.Stage, receipt.Policy.Candidate, enforceDeadline: false);
                actual.AdventureTaskKey = receipt.Policy.AdventureTaskKey;
                actual.AdventureTask = receipt.Policy.AdventureTask;
                actual.Horizon = receipt.Policy.Horizon;
                routeSnapshot = actual.Snapshot;
                candidate = ReplayGlobalPreset(receipt.Policy, receipt.Policy.Preset, actual, execution, currentGear, observeCurrentSupport, resourceOnly, true);
                if (!candidate.Feasible || !candidate.Score.HasValue) throw new InvalidOperationException("Acquired policy is no longer feasible.");
                // Never pin the temporary blood split/ritual into the acquired execution: release must replay the original policy.
                var allocation = ComposePresetResourceDemands(actual.Snapshot, candidate);
                if (_globalPresetNativeTargets?.Owner == owner)
                    _globalPresetNativeTargets.Energy = candidate.Activities == null ? new long[2] : candidate.Activities.AT.Take(2).ToArray();
                ApplyLoadoutPresetAllocations(allocation);
                VerifyLoadoutPresetAllocations(allocation);
                RequireGlobalPresetNativeTargets(receipt.Policy.Stage, owner, "Native AT menu targets changed during allocation; receipt rejected.");
                RequirePendingPresetRotationSupport(_presetSupportAcquisition.Rotation);
                resumeRoute();
                ObserveGlobalPresetApplication(receipt, candidate, rotationAt, issue);
                return GlobalPresetReceiptValid(owner);
            }
            catch (Exception error)
            {
                var supportError = error as SupportApplicationException;
                var resourceError = error as ResourceMutationException;
                if (resourceError != null && (resourceError.Result.Disposition == MutationDisposition.ForeignConflict
                    || resourceError.Result.Disposition == MutationDisposition.UnknownOutcome))
                {
                    // Keep acquired intent and target leases. Later passes can service
                    // healthy lanes; no action/credit is admitted for the full setup.
                    SuspendCurrentGlobalPresetStage(owner);
                    SetResourceExecution(ResourceExecutionState.NeedsUser, owner, resourceError.Message);
                    return false;
                }
                if (supportError != null && (supportError.Result.Disposition == MutationDisposition.KnownPartial
                    || supportError.Result.Disposition == MutationDisposition.RejectedUnchanged)
                    || resourceError?.Result.Disposition == MutationDisposition.RejectedUnchanged)
                {
                    SuspendCurrentGlobalPresetStage(owner);
                    GlobalPresetReadiness(preset, receipt.Policy.Stage, out var supportState);
                    WaitForGlobalPreset(owner + ":" + preset.Id, supportState, error.Message, true);
                    SetResourceExecution(ResourceExecutionState.Waiting, owner, _globalPresetWaitingReason);
                    return false;
                }
                var unsafeMutation = resourceError?.Result.Disposition == MutationDisposition.ForeignConflict
                    || resourceError?.Result.Disposition == MutationDisposition.UnknownOutcome
                    || supportError?.Result.Disposition == MutationDisposition.ForeignConflict
                    || supportError?.Result.Disposition == MutationDisposition.UnknownOutcome;
                if (!mutationStarted && !unsafeMutation)
                {
                    GlobalPresetReadiness(preset, receipt.Policy.Stage, out var state);
                    WaitForGlobalPreset(owner + ":" + preset.Id, state, error.Message, true);
                    SetResourceExecution(ResourceExecutionState.Waiting, owner, _globalPresetWaitingReason);
                    return false;
                }
                SuspendCurrentGlobalPresetStage(owner);
                BlockPresetExecution(owner + ":" + preset.Id, error is ResourceMutationException ? "resources" : "support/native-targets", error);
                SetResourceExecution(ResourceExecutionState.NeedsUser, owner, _globalPresetFailure);
                return false;
            }
            finally
            {
                if (!GlobalPresetReceiptValid(owner)) SuspendCurrentGlobalPresetStage(owner);
            }
        }

        private void ObserveGlobalPresetApplication(GlobalPresetReceipt receipt, PresetCandidate baseline,
            DateTime? rotationAt, PresetApplicationIssue issue)
        {
            receipt.Allocations = GlobalPresetAllocationState();
            receipt.EnergyAllocations = GlobalPresetLaneState(true);
            receipt.MagicAllocations = GlobalPresetLaneState(false);
            receipt.Support = GlobalPresetSupportState();
            receipt.Execution = baseline;
            ObserveGlobalPresetRetirementFootprint(receipt);
            receipt.AppliedEnergyWeights = Settings.EnergyNguWeights.ToArray();
            receipt.AppliedMagicWeights = Settings.MagicNguWeights.ToArray();
            var foreignSetup = SelectedSetupConflicts.TryGetValue(Character, out var yielded) && yielded.Rebirth == Character.stats.rebirthNumber
                && (yielded.Gear != null || yielded.Support != null);
            if (!foreignSetup && !ExecutionSetupClaimsActive && (!Settings.ManualAdventureEnabled || !ManualAdventureSupportOverridesActive))
            {
                receipt.BaselineSupport = baseline.Copy().Support;
                if (ReferenceEquals(receipt.Policy, _manualAdventureBaselinePolicy))
                    _manualAdventureBaseline.Support = new SupportSelection(baseline.Support.Beards,
                        baseline.Support.Diggers.Select(x => new SupportDiggerLevel(x.Id, x.Level)).ToArray());
            }
            if (rotationAt.HasValue) receipt.LastBeardRotation = rotationAt.Value;
            if (PendingPresetRotation(receipt.Owner + ":" + receipt.Id, receipt.Policy.Acquisition) != null)
                _presetSupportAcquisition.Rotation = null;
            if (rotationAt.HasValue && receipt.Policy.AdventureTaskKey != null)
                ObserveAdventurePresetTask(out _, out _, out receipt.Policy.AdventureObservation, out _);
            if (ExecutionSetupClaimsActive && ExecutionSetupVerified()) _customEquipmentApplicationInvalidated = false;
            if (GlobalPresetNativeTargetsYielded(receipt.Owner))
            {
                SelectedSetupConflicts.TryGetValue(Character, out var unresolvedSettings);
                SetResourceExecution(ResourceExecutionState.NeedsUser, receipt.Owner,
                    unresolvedSettings?.NativeSettings ?? "Native AT targets changed; Energy intent and dependent credit require review.");
            }
            ClearGlobalPresetWaiting();
        }

        private void RunManualGlobalPresetRefresh()
        {
            if (!ReconcileManualAdventureClaims()) return;
            if (_customRequestedFocusGoal.HasValue || _customAppliedFocusGoal.HasValue)
            {
                _globalPresetManualId = null;
                return;
            }
            if (_globalPresetManualId != null)
                ReconcileGlobalPreset("dashboard", _globalPresetManualId, null, 3600, "manual-selection");
        }
    }
}
