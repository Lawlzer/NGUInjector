using System;
using System.Diagnostics;
using System.Linq;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;
using SimpleJSON;

namespace NGUInjector
{
    internal partial class Main
    {
        private string _manualAdventureOverlayKey, _manualAdventureSupportKey;
        private ih[] _manualAdventureOverlayGear;
        // Acquired instances survive application invalidation and successor acquisition.
        // An actuator retry is not an authorized inventory-selection event.
        private ih[] _manualAdventureSelectedGear;
        private ih[] _manualAdventureGearObserved;
        private string _manualAdventureSelectedGearKey;
        private string _manualAdventureSelectedBaselineKey;
        private LoadoutManager.SnapshotEpoch _manualAdventureSelectedGearEpoch;
        // Current accepted producer requirements, independent of application receipts.
        // References are session-local selections, never authority: every use checks
        // epoch, current policy/owner and the selected actuator's boundary guards.
        private SelectedSetupRequirements _manualAdventureBaseline;
        private GlobalPresetPolicy _manualAdventureBaselinePolicy;
        private sealed class ManualAdventureGearConflict { internal string Reason; }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, ManualAdventureGearConflict>
            ManualAdventureGearConflicts = new System.Runtime.CompilerServices.ConditionalWeakTable<object, ManualAdventureGearConflict>();
        private LoadoutManager.SnapshotEpoch _manualAdventureOverlayEpoch;
        // Only an uncontrolled domain needs an observation as the additive basis.
        // It is never a return target. Controlled domains resolve their CURRENT intent.
        private SupportSelection _manualAdventureUncontrolledSupport, _manualAdventureSupportApplied;
        private DateTime _manualAdventureSupportRefreshAt;
        private sealed class ManualAdventureSupportConflict { internal string Reason; }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, ManualAdventureSupportConflict>
            ManualAdventureSupportConflicts = new System.Runtime.CompilerServices.ConditionalWeakTable<object, ManualAdventureSupportConflict>();
        private string ManualAdventureSupportConflictReason => Character != null
            && ManualAdventureSupportConflicts.TryGetValue(Character, out var conflict) ? conflict.Reason : null;

        private bool ManualAdventureOverlayActive => Settings.ManualAdventureEnabled && _manualAdventureSetupActive && !_manualTitanActive;
        private bool ManualAdventureSupportOverridesActive => Settings.ManualAdventureBeardWeights.Any(x => x > 0)
            || Settings.ManualAdventureDiggerWeights.Any(x => x > 0) || _manualAdventureSupportApplied != null;

        private bool ManualAdventureBaselineAuthorized()
        {
            var policy = _manualAdventureBaselinePolicy;
            var wanted = _manualAdventureBaseline;
            if (policy == null) return wanted != null && wanted.Authorized?.Invoke() == true;
            return wanted != null && GlobalPresetEpochMatches(policy)
                && (ReferenceEquals(policy, _globalPresetApplyingPolicy)
                    || _globalPresetPolicies.TryGetValue(policy.Preset.Id, out var current) && ReferenceEquals(policy, current))
                && (wanted.Owner == "dashboard" ? !Settings.UseCustomRebirthPlan && _globalPresetManualId == policy.Preset.Id
                    : Settings.UseCustomRebirthPlan && _customStageEquipmentKey == wanted.Owner);
        }

        private void RecordManualClaimApplication(SelectedSetupRequirements selected)
        {
            if (ManualAdventureOverlayActive)
            {
                _manualAdventureOverlayGear = LoadoutManager.CaptureExactLoadout();
                if (Settings.ManualAdventureLoadout >= 0) _manualAdventureGearObserved = _manualAdventureOverlayGear;
                _manualAdventureOverlayEpoch = new LoadoutManager.SnapshotEpoch();
                _manualAdventureSupportApplied = Settings.ManualAdventureBeardWeights.Any(x => x > 0)
                    || Settings.ManualAdventureDiggerWeights.Any(x => x > 0) ? selected.Support : null;
            }
            else if (!Settings.ManualAdventureEnabled)
            {
                _manualAdventureOverlayEpoch = null;
                _manualAdventureOverlayGear = _manualAdventureGearObserved = null;
                _manualAdventureOverlayKey = null;
                _manualAdventureOverlayRouteZone = null;
                ClearManualAdventureSupportClaim();
            }
        }

        // Resolve BEFORE the first shared mutation. B is acquired baseline intent,
        // not an intermediate native setup to equip and subsequently cover again.
        private SelectedSetupRequirements ComposeManualAdventureClaims(SelectedSetupRequirements baseline,
            GlobalPresetPolicy policy = null)
        {
            _manualAdventureBaseline = baseline;
            _manualAdventureBaselinePolicy = policy;
            if (policy != null)
            {
                baseline.Acquisition = policy.Acquisition;
                baseline.DiggerWeights = policy.Candidate.Support.DiggerWeights;
                baseline.MaterialValid = () => !policy.Items.Where((item, i) =>
                    LoadoutPresetPrimitiveKey(item) != policy.Material[i]).Any();
                baseline.Authorized = () => ManualAdventureBaselineAuthorized();
            }
            PublishExecutionBaseline(baseline);
            if (!ManualAdventureOverlayActive) return baseline;
            // Keep gear is a claim over the legal observed setup, not permission
            // to install B beneath it. B's selected intent remains separate above.
            var gear = LoadoutManager.CaptureExactLoadout();
            var selection = Settings.ManualAdventureLoadout;
            var gearKey = selection + ":" + LoadoutPresetNumbers(Settings.ManualAdventureGearWeights).ToString();
            if (selection >= 0)
            {
                if (ManualAdventureGearConflicts.TryGetValue(Character, out var conflict))
                    throw new InvalidOperationException(conflict.Reason);
                if (_manualAdventureSelectedGear == null || _manualAdventureSelectedGearKey != gearKey
                    || _manualAdventureSelectedGearEpoch?.Matches() != true)
                    throw new InvalidOperationException("Manual adventure selected equipment is unavailable; select the manual loadout before acquiring a successor.");
                gear = _manualAdventureSelectedGear;
            }
            var support = baseline.Support;
            var bonuses = Settings.ManualAdventureBeardWeights.Any(x => x > 0) || Settings.ManualAdventureDiggerWeights.Any(x => x > 0);
            var fraction = baseline.Budget;
            if (bonuses)
            {
                if (ManualAdventureSupportConflictReason != null) throw new InvalidOperationException(ManualAdventureSupportConflictReason);
                support = ResolveManualAdventureSupport(CaptureSupportContext(), support, policy?.ExecutionPreset,
                    baseline.DiggerWeights, out fraction);
            }
            var epoch = new LoadoutManager.SnapshotEpoch();
            var beards = LoadoutPresetNumbers(Settings.ManualAdventureBeardWeights).ToString();
            var diggers = LoadoutPresetNumbers(Settings.ManualAdventureDiggerWeights).ToString();
            return new SelectedSetupRequirements { Owner = baseline.Owner, Gear = gear,
                BoundaryAuthorized = baseline.BoundaryAuthorized,
                Recall = GearRecallFootprint.None, Support = support, ChangeSupport = baseline.ChangeSupport || bonuses,
                Budget = fraction, EnforceSupportBudget = !bonuses || Settings.ManualAdventureDiggerWeights.Any(x => x > 0),
                OwnsManualRoute = true, RouteRetirementAuthorized = baseline.RouteRetirementAuthorized,
                Authorized = () => epoch.Matches() && ManualAdventureBaselineAuthorized() && ManualAdventureOverlayActive
                    && selection == Settings.ManualAdventureLoadout
                    && gearKey == Settings.ManualAdventureLoadout + ":" + LoadoutPresetNumbers(Settings.ManualAdventureGearWeights).ToString()
                    && beards == LoadoutPresetNumbers(Settings.ManualAdventureBeardWeights).ToString()
                    && diggers == LoadoutPresetNumbers(Settings.ManualAdventureDiggerWeights).ToString() };
        }

        // Removing a claim resolves today's accepted baseline directly. No original
        // setup or full planning capture is used, and an uncontrolled domain is left alone.
        private bool ReconcileManualAdventureClaims()
        {
            if (!RetireDelegatedManualRoute()) return false;
            if (Settings.ManualAdventureEnabled || _manualAdventureOverlayEpoch == null) return true;
            if (!ManualAdventureBaselineAuthorized()) return true;
            if (ManualAdventureSupportConflictReason != null || ManualAdventureGearConflicts.TryGetValue(Character, out _)) return false;
            if (_manualAdventureOverlayGear != null && !ManualAdventureOverlayGearMatches()) return false;
            if (_manualAdventureGearObserved != null && !LoadoutManager.ExactLoadoutMatches(_manualAdventureGearObserved)) return false;
            if (_manualAdventureBaseline.MaterialValid?.Invoke() != true) return false;
            if (_manualAdventureSupportApplied != null && CaptureSupportState().Selection.StableKey != _manualAdventureSupportApplied.StableKey
                && (_presetSupportContinuation == null || !SupportMutationStillOwned(_presetSupportContinuation.Mutation))) return false;
            var baseline = _manualAdventureBaseline;
            var policy = _manualAdventureBaselinePolicy;
            var receipt = _globalPresetReceipt;
            try
            {
                BeginPresetSupportAcquisition(baseline.Owner, baseline.Acquisition);
                ApplySelectedSetup(new SelectedSetupRequirements { Owner = baseline.Owner, Gear = baseline.Gear,
                    ControlsGear = baseline.ControlsGear,
                    Support = baseline.Support, ChangeSupport = baseline.ChangeSupport || _manualAdventureSupportApplied != null, Budget = baseline.Budget,
                    EnforceSupportBudget = baseline.ChangeSupport,
                    Recall = GearRecallFootprint.None, OwnsManualRoute = true,
                    RetireRoute = true,
                    Authorized = () => !Settings.ManualAdventureEnabled && ReferenceEquals(baseline, _manualAdventureBaseline)
                        && ManualAdventureBaselineAuthorized(),
                    RouteRetirementAuthorized = () => ManualAdventureSupportRouteAuthorized(false, receipt) });
                if (policy != null && receipt != null && ReferenceEquals(receipt.Policy, policy))
                {
                    receipt.Support = GlobalPresetSupportState();
                    receipt.Gear = LoadoutManager.CaptureExactLoadout();
                    receipt.CurrentGearContinuation = false;
                    receipt.Execution = receipt.Execution.Copy();
                    receipt.Execution.Support = policy.Candidate.Copy().Support;
                    receipt.Execution.Support.Beards = baseline.Support.Beards;
                    receipt.Execution.Support.Diggers = baseline.Support.Diggers.Select(x => new SupportDigger { Id = x.Id, Level = x.Level }).ToArray();
                    // Ordinary acquired-policy refresh owns route activation after
                    // its exact funding checks; removal never restores a foreign route.
                    receipt.SupportRouteRetired = Character.adventure.zone == -1
                        && policy.ExecutionPreset.Weights.Skip(3).Take(3).Any(x => x > 0);
                }
                _manualAdventureOverlayEpoch = null;
                _manualAdventureOverlayGear = null;
                _manualAdventureGearObserved = null;
                ClearManualAdventureSupportClaim();
                return true;
            }
            catch (Exception error)
            {
                RecordManualAdventureSetupFailure(error);
                SuspendProtectedStageProgress();
                SetCustomAutomationStatus("Manual adventure release waiting: " + error.Message);
                return false;
            }
        }

        private void RecordManualAdventureSetupFailure(Exception error)
        {
            var gear = (error as SelectedSetupException)?.Outcome;
            var support = (error as SupportApplicationException)?.Result;
            if (gear?.Disposition == MutationDisposition.ForeignConflict || gear?.Disposition == MutationDisposition.UnknownOutcome)
                ManualAdventureGearConflicts.GetValue(Character, _ => new ManualAdventureGearConflict()).Reason = "Manual adventure gear requires review: " + error.Message;
            if (support?.Disposition == MutationDisposition.ForeignConflict || support?.Disposition == MutationDisposition.UnknownOutcome)
                YieldManualAdventureSupport("Manual adventure support requires review: " + error.Message);
            if (gear?.Disposition == MutationDisposition.KnownPartial || support?.Disposition == MutationDisposition.KnownPartial)
            {
                // Only bounded actuator evidence permits advancing this observation.
                _manualAdventureOverlayGear = LoadoutManager.CaptureExactLoadout();
                _manualAdventureGearObserved = _manualAdventureOverlayGear;
                _manualAdventureOverlayEpoch = new LoadoutManager.SnapshotEpoch();
            }
        }

        private JSONObject GetManualAdventureOverlayRuntime()
        {
            var state = new JSONObject();
            state["available"] = false;
            try
            {
                var c = Character;
                state["active"] = ManualAdventureOverlayActive;
                state["basePresetId"] = ManualAdventureBasePreset?.Id ?? "";
                state["baseOwner"] = _globalPresetReceipt?.Owner ?? "";
                state["loadout"] = Settings.ManualAdventureLoadout;
                state["energyAllocations"] = GlobalPresetLaneState(true);
                state["magicAllocations"] = GlobalPresetLaneState(false);
                state["energy"] = c.curEnergy.ToString(System.Globalization.CultureInfo.InvariantCulture);
                state["idleEnergy"] = c.idleEnergy.ToString(System.Globalization.CultureInfo.InvariantCulture);
                state["magic"] = c.magic.curMagic.ToString(System.Globalization.CultureInfo.InvariantCulture);
                state["idleMagic"] = c.magic.idleMagic.ToString(System.Globalization.CultureInfo.InvariantCulture);
                state["res3"] = c.res3.curRes3.ToString(System.Globalization.CultureInfo.InvariantCulture);
                state["idleRes3"] = c.res3.idleRes3.ToString(System.Globalization.CultureInfo.InvariantCulture);
                state["support"] = CaptureSupportState().Selection.StableKey;
                state["supportWaiting"] = ManualAdventureSupportConflictReason ?? "";
                state["gearIds"] = LoadoutPresetNumbers(LoadoutManager.CaptureExactLoadout().Select(x => (double)x.id));
                state["available"] = true;
            }
            catch { /* Optional observations must not block startup or command results. */ }
            return state;
        }

        private bool ManualAdventureOverlayGearMatches()
        {
            if (_manualAdventureOverlayGear == null) return _globalPresetReceipt != null && GlobalPresetGearMatches(_globalPresetReceipt);
            if (_manualAdventureOverlayEpoch == null || !_manualAdventureOverlayEpoch.Matches()) return false;
            var actual = LoadoutManager.CaptureExactLoadout();
            return actual.Length == _manualAdventureOverlayGear.Length && actual.Select((item, i) =>
                item.slot == _manualAdventureOverlayGear[i].slot && (item.id == 0 ? _manualAdventureOverlayGear[i].id == 0
                    : ReferenceEquals(item.equipment, _manualAdventureOverlayGear[i].equipment))).All(x => x);
        }

        private bool TryApplyManualAdventureOverlay()
        {
            if (_manualAdventureGearObserved != null && _manualAdventureSelectedGearEpoch?.Matches() == true
                && !LoadoutManager.ExactLoadoutMatches(_manualAdventureGearObserved))
            {
                ManualAdventureGearConflicts.GetValue(Character, _ => new ManualAdventureGearConflict()).Reason =
                    "Manual adventure gear was changed by another owner; review the current setup.";
                SuspendProtectedStageProgress();
                return false;
            }
            var selection = Settings.ManualAdventureLoadout;
            var identity = ManualAdventureBaseIdentity;
            var preset = selection < 0 ? null : ManualAdventureGearBasePreset(out identity);
            var key = identity + ":" + selection + ":" + LoadoutPresetNumbers(Settings.ManualAdventureGearWeights).ToString();
            if (_manualAdventureOverlayKey == key && ManualAdventureOverlayGearMatches()
                && (selection < 0 || _manualAdventureAppliedLoadout == selection)) return true;
            if (DateTime.UtcNow < _manualAdventureGearRetryAt) return false;
            _manualAdventureGearRetryAt = DateTime.UtcNow.AddSeconds(5);
            if (selection >= 0)
            {
                if (ManualAdventureGearConflicts.TryGetValue(Character, out var conflict))
                {
                    SuspendProtectedStageProgress();
                    SetCustomAutomationStatus(conflict.Reason);
                    return false;
                }
                var selectionKey = selection + ":" + LoadoutPresetNumbers(Settings.ManualAdventureGearWeights).ToString();
                var baselineKey = GlobalPresetDefinition(preset);
                if (_manualAdventureSelectedGear != null && _manualAdventureSelectedGearKey == selectionKey
                    && _manualAdventureSelectedGearEpoch?.Matches() != true)
                {
                    SetCustomAutomationStatus("Manual adventure selected equipment crossed a save/rebirth boundary; explicit loadout review is required.");
                    return false;
                }
                if (_manualAdventureSelectedGear == null || _manualAdventureSelectedGearKey != selectionKey
                    || preset.AutoUpdate && _manualAdventureSelectedBaselineKey != baselineKey)
                {
                    var timer = Stopwatch.StartNew();
                    var capture = CaptureLoadoutPresetDefinition(preset, timer);
                    var request = LoadoutPresetRequest(preset);
                    if (capture.Snapshot.BeastMode) ConfigureManualAdventureResourceRequest(request);
                    request.PlannedSupport = false;
                    request.GearWeights = Settings.ManualAdventureGearWeights.ToArray();
                    // Selecting a combat loadout adds pushing preference; it does not replace the base objectives.
                    request.GearWeights[0] += 100;
                    request.GearFightMode = selection;
                    var plan = LoadoutPresetPlanner.Plan(capture.Snapshot, request, timer);
                    string failure = "No feasible allocation-preserving manual adventure loadout.";
                    ih[] selected = null;
                    foreach (var candidate in LoadoutPresetSetupCandidates(plan.Candidates, request.ResourcePrioritiesReviewed))
                    {
                        if (!candidate.Feasible || !candidate.Score.HasValue)
                        {
                            failure = "The exact selected setup cannot be funded. " + string.Join(" ", candidate.Warnings);
                            break;
                        }
                        var gear = candidate.Items.Select((item, i) => {
                            var wrapper = item.Id == 0 ? new ih { equipment = new Equipment(), id = 0 } : capture.Gear[item.SourceSlot];
                            return new ih { id = wrapper.id, name = wrapper.name, equipment = wrapper.equipment,
                                level = wrapper.level, slot = capture.Snapshot.Slots[i] };
                        }).ToArray();
                        gear = LoadoutManager.ComposeExactLoadout(LoadoutManager.CaptureExactLoadout(), gear);
                        // Only unreviewed preflight may try another candidate.
                        failure = GearOptimizer.CheckExactSwapCapacity(gear, Character.curEnergy, Character.res3.curRes3);
                        if (failure == null) { selected = gear; break; }
                    }
                    if (selected == null)
                    {
                        SetCustomAutomationStatus("Manual adventure: loadout waiting; resources unchanged. " + failure);
                        return false;
                    }
                    _manualAdventureSelectedGear = selected;
                    _manualAdventureSelectedGearKey = selectionKey;
                    _manualAdventureSelectedBaselineKey = baselineKey;
                    _manualAdventureSelectedGearEpoch = new LoadoutManager.SnapshotEpoch();
                }
                var selectedGear = _manualAdventureSelectedGear;
                var selectedEpoch = _manualAdventureSelectedGearEpoch;
                var receipt = _globalPresetReceipt;
                try
                {
                    ApplySelectedSetup(new SelectedSetupRequirements { Owner = "manual-adventure-gear",
                        Gear = selectedGear, Recall = GearRecallFootprint.None,
                        Support = CaptureSupportState().Selection, ChangeSupport = false, OwnsManualRoute = true,
                        Authorized = () => ManualAdventureOverlayActive && selectedEpoch.Matches()
                            && ReferenceEquals(selectedGear, _manualAdventureSelectedGear)
                            && selectionKey == Settings.ManualAdventureLoadout + ":" + LoadoutPresetNumbers(Settings.ManualAdventureGearWeights).ToString()
                            && ReferenceEquals(receipt, _globalPresetReceipt),
                        RouteRetirementAuthorized = () => selectedEpoch.Matches() && ManualAdventureSupportRouteAuthorized(true, receipt) });
                }
                catch (Exception error)
                {
                    RecordManualAdventureSetupFailure(error);
                    SuspendProtectedStageProgress();
                    var outcome = (error as SelectedSetupException)?.Outcome;
                    if (outcome != null && (outcome.Disposition == MutationDisposition.ForeignConflict
                        || outcome.Disposition == MutationDisposition.UnknownOutcome))
                        ManualAdventureGearConflicts.GetValue(Character, _ => new ManualAdventureGearConflict()).Reason =
                            "Manual adventure gear requires review: " + error.Message;
                    SetCustomAutomationStatus("Manual adventure: loadout waiting. " + error.Message);
                    return false;
                }
            }
            _manualAdventureOverlayGear = LoadoutManager.CaptureExactLoadout();
            if (selection >= 0) _manualAdventureGearObserved = _manualAdventureOverlayGear;
            _manualAdventureOverlayEpoch = new LoadoutManager.SnapshotEpoch();
            _manualAdventureOverlayKey = key;
            _manualAdventureAppliedLoadout = selection;
            _manualAdventureGearRetryAt = DateTime.MinValue;
            return true;
        }

        private CustomLoadoutPreset ManualAdventureGearBasePreset(out string identity)
        {
            var preset = ManualAdventureBasePreset;
            identity = ManualAdventureBaseIdentity;
            if (preset != null) return preset;
            CustomPlanStage stage = null;
            if (Settings.UseCustomRebirthPlan && CustomPlanStore != null)
            {
                var plan = LoadRuntimeCustomPlan();
                var index = plan == null ? -1 : CustomStageAllocator.GetSchedulerSnapshot(plan, Character.rebirthTime.totalseconds)?.ActiveStageIndex ?? -1;
                if (index >= 0 && index < plan.Stages.Length) stage = plan.Stages[index];
                identity = "inline:" + Settings.CustomPlanFile + ":" + _customStageKey;
            }
            else if (_customAppliedFocusGoal.HasValue)
            {
                stage = FocusSetupStage(_customAppliedFocusGoal.Value);
                identity = "focus:" + _customAppliedFocusGoal.Value;
            }
            var weights = new double[12];
            if (stage != null)
                foreach (var energy in new[] { true, false })
                {
                    var token = SetupPrimaryPriority(stage, energy)?.Split('-')[0];
                    var index = token == "NGU" ? (energy ? 0 : 1) : token == "RIT" ? 2 : token == "TM" ? 6
                        : token == "AUG" ? 7 : token == "WAN" ? 8 : token == "AT" ? 9 : -1;
                    if (index >= 0) weights[index] = 1;
                }
            if (!weights.Any(x => x > 0)) weights[10] = weights[11] = 1;
            identity += ":" + LoadoutPresetNumbers(weights).ToString();
            return new CustomLoadoutPreset { Id = "manual-adventure", Name = "Manual Adventure", Weights = weights };
        }

        private string ApplyManualAdventureOverlaySupport()
        {
            var beardBonuses = Settings.ManualAdventureBeardWeights;
            var diggerBonuses = Settings.ManualAdventureDiggerWeights;
            var hasBonuses = beardBonuses.Any(x => x > 0) || diggerBonuses.Any(x => x > 0);
            if (!hasBonuses && _manualAdventureSupportApplied == null)
            {
                return null;
            }
            var basis = CurrentManualAdventureSupportBaseline();
            var key = ManualAdventureBaseIdentity + ":" + basis?.StableKey + ":" + LoadoutPresetNumbers(beardBonuses).ToString() + ":" + LoadoutPresetNumbers(diggerBonuses).ToString();
            var now = DateTime.UtcNow;
            var context = CaptureSupportContext();
            if (_manualAdventureSupportApplied != null && _manualAdventureSupportApplied.StableKey != context.CurrentSelection.StableKey
                && (_presetSupportContinuation == null || !SupportMutationStillOwned(_presetSupportContinuation.Mutation)))
                return YieldManualAdventureSupport("Manual adventure bonuses paused: support was changed by another owner.");
            if (key == _manualAdventureSupportKey && now < _manualAdventureSupportRefreshAt
                && _presetSupportContinuation == null && !_presetSupportPresentationPending
                && _manualAdventureSupportApplied?.StableKey == context.CurrentSelection.StableKey) return null;
            // With no controlled successor, removal relinquishes the claim; it is
            // not permission to reinstall a historical native observation.
            if (!hasBonuses && basis == null) { ClearManualAdventureSupportClaim(); return null; }
            if (basis == null) basis = _manualAdventureUncontrolledSupport
                ?? (_manualAdventureUncontrolledSupport = context.CurrentSelection);
            var preset = ManualAdventureBasePreset;
            SupportSelection selected; double fraction;
            try { selected = ResolveManualAdventureSupport(context, basis, preset,
                _manualAdventureBaseline?.DiggerWeights, out fraction); }
            catch (Exception error) { SuspendProtectedStageProgress(); return error.Message; }
            var failure = ApplyManualAdventureSupportRequirements(selected, fraction, true, diggerBonuses.Any(x => x > 0));
            if (failure != null) return failure;
            _manualAdventureSupportApplied = selected;
            _manualAdventureSupportKey = key;
            _manualAdventureSupportRefreshAt = now.AddSeconds(60);
            if (!beardBonuses.Any(x => x > 0) && !diggerBonuses.Any(x => x > 0))
            {
                ClearManualAdventureSupportClaim();
                if (ManualAdventureBaselineAuthorized() && ReferenceEquals(_globalPresetReceipt?.Policy, _manualAdventureBaselinePolicy))
                    _globalPresetReceipt.Support = GlobalPresetSupportState();
            }
            return null;
        }

        private SupportSelection ResolveManualAdventureSupport(SupportContext context, SupportSelection basis,
            CustomLoadoutPreset preset, double[] acquiredWeights, out double fraction)
        {
            var beardBonuses = Settings.ManualAdventureBeardWeights;
            var diggerBonuses = Settings.ManualAdventureDiggerWeights;
            var priorities = preset?.SupportMode == "planned" && preset.BeardPriority.Length > 0
                ? preset.BeardPriority.Concat(basis.Beards).Distinct().ToArray() : basis.Beards;
            var beards = beardBonuses.Any(x => x > 0) ? Enumerable.Range(0, 7)
                .Select(id => new { Id = id, Weight = (priorities.Contains(id) ? priorities.Length - Array.IndexOf(priorities, id) : 0) + beardBonuses[id] })
                .Where(x => x.Weight > 0 && context.UnlockedBeards.Contains(x.Id)).OrderByDescending(x => x.Weight).ThenBy(x => x.Id)
                .Take(context.BeardSlots).Select(x => x.Id).ToArray() : basis.Beards;
            fraction = (preset?.DiggerBudgetPercent ?? 75) / 100d;
            var selected = new SupportSelection(beards, basis.Diggers);
            if (diggerBonuses.Any(x => x > 0))
            {
                var weights = acquiredWeights?.Length == 12 ? acquiredWeights : preset?.SupportMode == "planned" && preset.DiggerWeights.Length == 12 ? preset.DiggerWeights
                    : preset != null && preset.SupportMode == "planned" ? PresetDefinitions.DefaultDiggerWeights(LoadoutPresetPlanner.NormalizeWeights(preset.Weights),
                        preset.MinimumPower > 0 || preset.MinimumToughness > 0)
                    : Enumerable.Range(0, 12).Select(id => basis.Diggers.Any(x => x.Id == id) ? 1d : 0d).ToArray();
                var requests = context.Diggers.Select(d => new DiggerBudgetRequest { Id = d.Id, Maximum = d.Maximum,
                    Weight = weights[d.Id] + diggerBonuses[d.Id] }).ToArray();
                var planned = SupportProjection.Plan(context, beards, requests, fraction);
                if (!planned.Feasible)
                {
                    throw new InvalidOperationException("Manual adventure support waiting: " + planned.Reason);
                }
                selected = planned.Selection;
            }
            return selected;
        }

        private SupportSelection CurrentManualAdventureSupportBaseline()
        {
            if (ManualAdventureBaselineAuthorized()) return _manualAdventureBaseline.Support;
            if (!Settings.UseCustomRebirthPlan && _customAppliedFocusGoal.HasValue)
                return SelectFocusSupport(SetupForFocus(_customAppliedFocusGoal.Value));
            return null; // Uncontrolled is not controlled-empty or an NGU fallback.
        }

        private string ApplyManualAdventureSupportRequirements(SupportSelection selection, double fraction, bool active, bool enforceBudget = false)
        {
            var conflict = ManualAdventureSupportConflictReason;
            if (conflict != null)
            {
                SuspendProtectedStageProgress();
                return conflict;
            }
            var receipt = _globalPresetReceipt;
            var identity = ManualAdventureBaseIdentity;
            var epoch = new LoadoutManager.SnapshotEpoch();
            var desired = identity + ":" + selection.StableKey + ":" + fraction + ":" + enforceBudget;
            try
            {
                // Check bounded evidence BEFORE a changed desired selection can
                // retire its continuation. Reset never grants ownership of a foreign edit.
                if (_presetSupportContinuation != null && !SupportMutationStillOwned(_presetSupportContinuation.Mutation))
                    return YieldManualAdventureSupport("Manual adventure bonuses paused: support was changed by another owner.");
                BeginPresetSupportAcquisition("manual-adventure-support", desired);
                ApplySelectedSetup(new SelectedSetupRequirements { Owner = "manual-adventure-support",
                    Gear = LoadoutManager.CaptureExactLoadout(), Recall = GearRecallFootprint.None,
                    Support = selection, ChangeSupport = true, Budget = fraction, EnforceSupportBudget = enforceBudget,
                    OwnsManualRoute = true,
                    Authorized = () => epoch.Matches() && ReferenceEquals(receipt, _globalPresetReceipt)
                        && identity == ManualAdventureBaseIdentity && active == Settings.ManualAdventureEnabled
                        && !_manualTitanActive && (!active || ManualAdventureOverlayActive),
                    RouteRetirementAuthorized = () => epoch.Matches() && !_manualTitanActive
                        && ManualAdventureSupportRouteAuthorized(active, receipt) });
                return null;
            }
            catch (Exception error)
            {
                SuspendProtectedStageProgress();
                var supportError = error as SupportApplicationException;
                if (supportError != null && (supportError.Result.Disposition == MutationDisposition.ForeignConflict
                    || supportError.Result.Disposition == MutationDisposition.UnknownOutcome))
                {
                    YieldManualAdventureSupport("Manual adventure support requires review: " + error.Message);
                }
                return "Manual adventure support waiting: " + error.Message;
            }
        }

        private string YieldManualAdventureSupport(string reason)
        {
            SuspendProtectedStageProgress();
            ManualAdventureSupportConflicts.GetValue(Character, _ => new ManualAdventureSupportConflict()).Reason = reason;
            return reason;
        }

        private bool ManualAdventureSupportRouteAuthorized(bool active, GlobalPresetReceipt receipt)
        {
            if (active && !ManualAdventureOverlayActive) return false;
            var adventure = Character.adventure;
            if (adventure.zone == -1) return true;
            if (_manualAdventureOverlayRouteZone == adventure.zone) return true;
            // Initial takeover may retire a currently verified baseline route,
            // not an arbitrary native/user route merely because a goal was enabled.
            return active && receipt != null && GlobalPresetEpochMatches(receipt.Policy)
                && adventure.zone == receipt.Execution.Zone
                && (adventure.zone != 1000 || adventure.itopodStart == receipt.Execution.Floor
                    && adventure.itopodEnd == receipt.Execution.Floor)
                && GlobalPresetReceiptValid(receipt.Owner, true, true);
        }

        private void ClearManualAdventureSupportClaim()
        {
            _manualAdventureSupportApplied = _manualAdventureUncontrolledSupport = null;
            _manualAdventureSupportKey = null;
            _manualAdventureSupportRefreshAt = DateTime.MinValue;
        }

        private void ResetManualAdventureOverlay()
        {
            if (Settings.ManualAdventureEnabled && _manualAdventureOverlayEpoch?.Matches() == true)
            {
                // A quest/titan can interrupt combat without deleting the goal.
                _manualAdventureOverlayKey = null;
                _manualAdventureOverlayGear = null;
                _manualAdventureOverlayRouteZone = null;
                return;
            }
            if (!ReconcileManualAdventureClaims()) return;
            _manualAdventureOverlayKey = _manualAdventureSupportKey = null;
            _manualAdventureOverlayGear = null;
            _manualAdventureOverlayEpoch = null;
            _manualAdventureOverlayRouteZone = null;
            _manualAdventureGearObserved = null;
            ClearManualAdventureSupportClaim();
        }

        private void ReleaseManualAdventureOverlayForPresetAcquisition(bool reviewed = false)
        {
            var conflict = ManualAdventureSupportConflictReason;
            if (conflict != null && !reviewed) throw new InvalidOperationException(conflict);
            if (!reviewed && ManualAdventureGearConflicts.TryGetValue(Character, out var gearConflict))
                throw new InvalidOperationException(gearConflict.Reason);
            if (!reviewed && _manualAdventureGearObserved != null && _manualAdventureSelectedGearEpoch?.Matches() == true
                && !LoadoutManager.ExactLoadoutMatches(_manualAdventureGearObserved))
            {
                ManualAdventureGearConflicts.GetValue(Character, _ => new ManualAdventureGearConflict()).Reason =
                    "Manual adventure gear was changed by another owner; review the current setup.";
                throw new InvalidOperationException("Manual adventure gear was changed by another owner; review the current setup.");
            }
            // Acquisition supplies its current successor directly. Never restore A
            // before applying B, and do not erase an unresolved foreign edit.
            if (!reviewed && _manualAdventureSupportApplied != null && _manualAdventureOverlayEpoch?.Matches() == true
                && CaptureSupportState().Selection.StableKey != _manualAdventureSupportApplied.StableKey
                && (_presetSupportContinuation == null || !SupportMutationStillOwned(_presetSupportContinuation.Mutation)))
                throw new InvalidOperationException("Manual adventure support was changed by another owner; review the current setup before acquisition.");
            if (reviewed)
            {
                // Explicit Equip/Preview+Apply only, after fresh preflight. A
                // reviewed acquisition accepts today's full observation, not old
                // mutation evidence, for its newly selected successor.
                if (!ReferenceEquals(reference, this) || _unloaded
                    || Character.bossController == null || Character.bossController.isFighting || Character.bossController.nukeBoss
                    || _resourceSetupExecutor.Application.BlockedLanes != 0)
                    throw new InvalidOperationException("Reviewed support acquisition authority changed.");
                CaptureSupportState();
                SelectedSetupConflicts.Remove(Character);
                _selectedSetupReviewAuthorized = null;
                ManualAdventureSupportConflicts.Remove(Character);
                ManualAdventureGearConflicts.Remove(Character);
                ClearManualAdventureSupportClaim();
                _manualAdventureGearObserved = null;
            }
            _manualAdventureSupportKey = _manualAdventureOverlayKey = null;
            _manualAdventureOverlayGear = null;
            _manualAdventureAppliedLoadout = null;
        }
    }
}
