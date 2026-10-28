using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using NGUInjector.AllocationProfiles.RebirthStuff;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;
using SimpleJSON;

namespace NGUInjector
{
    internal partial class Main
    {
        private ProtectedStageScheduler _protectedScheduler;
        private CustomPlan _protectedPlan;
        private string _protectedPlanFile;
        private string _protectedGeneration;
        private long _protectedRebirthNumber;
        private double _protectedRunOrigin;
        private double _protectedLastElapsed;
        private double _protectedLastTotal;
        private double _protectedLastSavedAt = -1d;
        private bool _protectedCheckpointDurable;
        private bool _protectedTimingLoadingSave;
        private string _protectedRecoveryReason;
        private string _protectedPersistenceError;
        private string _protectedOuterCheckpointIssue;
        // Immutable serialized intent. No native references, application receipts or return
        // authority survive this boundary. Unresolved records survive subsequent saves.
        private string _protectedSelectedPolicy;
        private string _protectedSelectedPolicyBlockedEntry;
        private string _protectedSelectedPolicyIssue;

        internal JSONObject PreviewExecutionRestart(JSONNode args)
        {
            if (_protectedOuterCheckpointIssue == null || args == null || !args.IsObject || args.Count != 3
                || args.Linq.Any(x => x.Key != "stageIndex" && x.Key != "checkpointRevision" && x.Key != "planRevision")
                || !EquipmentDescriptorInteger(args["stageIndex"], 0, _protectedPlan.Stages.Length - 1)
                || !args["checkpointRevision"].IsString || !args["planRevision"].IsString
                || JsonFiles.ComputeRevision(File.ReadAllBytes(ProtectedCheckpointPath)) != args["checkpointRevision"].Value
                || _protectedPlan.RuntimeRevision != args["planRevision"].Value)
                throw new InvalidOperationException("Unrecoverable checkpoint or selected recovery row/revision changed; refresh before restart.");
            var review = new ExecutionRebindReview { Kind = "restart", Token = Guid.NewGuid().ToString("N"),
                Path = ProtectedCheckpointPath, Revision = args["checkpointRevision"].Value, Selection = args.ToString(),
                Entry = AutomaticRecoveryEntry, Permissions = ExecutionRebindPermissions(), Native = ExecutionRebindNativeKey(),
                Expires = LoadoutPresetClock + 120, Owner = "checkpoint-recovery" };
            _executionRebindReview = review;
            review.Grant = ResolveExecutionGrant(review.Owner, review.Token, ExecutionDomain.None,
                () => ReferenceEquals(_executionRebindReview, review) && review.Entry == AutomaticRecoveryEntry && _protectedOuterCheckpointIssue != null);
            var result = new JSONObject(); result["token"] = review.Token; result["checkpointRevision"] = review.Revision;
            result["operation"] = "restart"; result["stageName"] = _protectedPlan.Stages[args["stageIndex"].AsInt].Name;
            return result;
        }

        private string ApplyExecutionRestart(ExecutionRebindReview review)
        {
            if (_protectedOuterCheckpointIssue == null || JsonFiles.ComputeRevision(File.ReadAllBytes(review.Path)) != review.Revision
                || ExecutionRebindNativeKey() != review.Native) throw new InvalidOperationException("Recovery evidence changed; preview restart again.");
            var request = JsonFiles.ParseObject(review.Selection);
            var args = new JSONObject(); args["name"] = Settings.CustomPlanFile; args["expectedRevision"] = request["planRevision"];
            args["expectedEntry"] = _protectedGeneration + ":" + _protectedScheduler.Entry.ToString(CultureInfo.InvariantCulture);
            args["action"] = "restart"; args["stageIndex"] = request["stageIndex"];
            _executionRebindReview = null;
            return OverrideCustomStage(args);
        }

        private string ProtectedPresetOwner => _protectedScheduler == null || _protectedPlan == null
            || _protectedScheduler.StageIndex < 0 ? null : "stage:" + _protectedPlanFile + ":" + _protectedPlan.Name
                + ":" + _protectedScheduler.StageIndex + ":" + _protectedGeneration + ":" + _protectedScheduler.Entry + ":" + _protectedScheduler.Cycle;

        private string ProtectedPresetRestorationIssue(string owner)
        {
            return _protectedScheduler != null && owner != null && owner == ProtectedPresetOwner
                && _protectedSelectedPolicyBlockedEntry == AutomaticRecoveryEntry
                ? _protectedSelectedPolicyIssue : null;
        }

        private void PersistProtectedPresetSelection(GlobalPresetPolicy policy, string owner)
        {
            if (owner == null) return;
            if (owner != ProtectedPresetOwner) { PersistStandalonePresetSelection(policy, owner); return; }
            var issue = ProtectedPresetRestorationIssue(owner);
            if (issue != null) throw new InvalidOperationException(issue);
            var previous = _protectedSelectedPolicy;
            _protectedSelectedPolicy = ExportProtectedPresetSelection(policy, owner).ToString();
            if (SaveProtectedStageProgress(true)) return;
            _protectedSelectedPolicy = previous;
            throw new IOException("Selected preset policy could not be saved; the selected setup was not applied. " + _protectedPersistenceError);
        }

        private string ValidateProtectedPresetProvenance(JSONNode selection)
        {
            if (selection == null || !selection.IsObject
                || !EquipmentDescriptorInteger(selection["version"], 1, 1)
                || !selection["schema"].IsString || selection["schema"].Value != "selected-preset-policy-v1")
                return "Selected preset policy has a missing or unsupported version/schema.";
            if (!selection["acquisition"].IsString || !Guid.TryParseExact(selection["acquisition"].Value, "N", out _)
                || !selection["owner"].IsString || selection["owner"].Value != ProtectedPresetOwner
                || !selection["plan"].IsString || selection["plan"].Value != _protectedPlanFile
                || !selection["generation"].IsString || selection["generation"].Value != _protectedGeneration
                || !selection["entry"].IsString || selection["entry"].Value != _protectedScheduler.Entry.ToString(CultureInfo.InvariantCulture))
                return "Selected preset policy does not match this plan/stage entry.";
            if (!selection["rebirth"].IsString || selection["rebirth"].Value != _protectedRebirthNumber.ToString(CultureInfo.InvariantCulture)
                || !FiniteCheckpointNumber(selection["origin"], false) || Math.Abs(selection["origin"].AsDouble - _protectedRunOrigin) > 5
                || !FiniteCheckpointNumber(selection["elapsed"], true) || selection["elapsed"].AsDouble > _protectedLastElapsed + .1
                || !FiniteCheckpointNumber(selection["total"], true) || selection["total"].AsDouble > _protectedLastTotal + .1)
                return "Selected preset policy does not match this save context.";
            return null;
        }

        private void LoadProtectedPresetSelection(JSONObject saved)
        {
            var index = _protectedScheduler.StageIndex;
            if (index < 0 || _protectedScheduler.Finished || _protectedPlan.Stages[index].ExecutionMode != 0
                || string.IsNullOrEmpty(_protectedPlan.Stages[index].PresetId)) return;
            var selection = saved["selectedPolicy"];
            _protectedSelectedPolicy = selection == null || selection.IsNull ? null : selection.ToString();
            // A new entry is checkpointed before its first selection is acquired.
            if (_protectedSelectedPolicy == null) return;
            var issue = ValidateProtectedPresetProvenance(selection);
            if (issue == null)
            {
                try
                {
                    // Restore decisions only. Ordinary admission must still acquire
                    // current grants and verify every native effect; no receipt is imported.
                    var policy = ImportExecutionSelection(selection, out _);
                    policy.Owner = ProtectedPresetOwner;
                    _globalPresetPolicies[policy.Preset.Id] = policy;
                    _globalPresetApplyingPolicy = policy;
                    _globalPresetApplyingEvent = policy.Owner + ":" + policy.Preset.Id;
                    return;
                }
                catch (Exception error) { issue = error.Message; }
            }
            _protectedSelectedPolicyBlockedEntry = AutomaticRecoveryEntry;
            _protectedSelectedPolicyIssue = issue;
        }

        private static double ProtectedHeartbeat => (double) Stopwatch.GetTimestamp() / Stopwatch.Frequency;

        private string ProtectedCheckpointPath => string.IsNullOrEmpty(_dir) || string.IsNullOrEmpty(_protectedPlanFile)
            ? null : ProtectedCheckpointForPlan(_protectedPlanFile);

        private static string ProtectedCheckpointForPlan(string planName)
        {
            return Path.Combine(_dir, "stage-progress-" + JsonFiles.ComputeRevision(System.Text.Encoding.UTF8.GetBytes(planName.ToLowerInvariant())) + ".json");
        }

        private bool ProtectedEpochAvailable()
        {
            return !_protectedTimingLoadingSave && Character?.rebirthTime != null && Character.totalPlaytime != null
                   && Character.stats != null && Settings != null;
        }

        private bool ProtectedEpochMatches()
        {
            if (!ProtectedEpochAvailable()) return false;
            var elapsed = Character.rebirthTime.totalseconds;
            var total = Character.totalPlaytime.totalseconds;
            return Character.stats.rebirthNumber == _protectedRebirthNumber
                   && Math.Abs(total - elapsed - _protectedRunOrigin) <= 5d
                   && elapsed + .1d >= _protectedLastElapsed && total + .1d >= _protectedLastTotal;
        }

        private bool ProtectedStageOwnershipAvailable(CustomPlan plan)
        {
            if (CombatPrepActive) return false;
            if (_protectedScheduler != null && (!_protectedScheduler.Matches(plan)
                || _protectedScheduler.StageIndex >= plan.Stages.Length)) return false;
            return ProtectedEpochAvailable() && Settings.UseCustomRebirthPlan && plan.StageSchedulerEnabled
                   && !_customRequestedFocusGoal.HasValue && !_manualRebirthPending && !_customPreRebirthPrepRunning
                   && !BossPushRunning && !CustomBossFightActive()
                     && (!ManualAdventureOwnsEquipment || ManualAdventureOverlayActive || _protectedScheduler != null && _protectedScheduler.StageIndex >= 0
                        && string.IsNullOrEmpty(plan.Stages[_protectedScheduler.StageIndex].PresetId)
                        && plan.Stages[_protectedScheduler.StageIndex].GearGoal < 0)
                    && (_protectedScheduler == null || _protectedScheduler.StageIndex < 0
                        || (!string.IsNullOrEmpty(plan.Stages[_protectedScheduler.StageIndex].PresetId)
                            && plan.Stages[_protectedScheduler.StageIndex].ExecutionMode == 0
                            ? GlobalPresetStageReceiptValid(plan)
                              : CustomStageAllocator.StageApplicationEligible(plan, plan.Stages[_protectedScheduler.StageIndex], 0, 0)
                                 && InlineStageSetupVerified(plan)
                                 && (!ExecutionSetupClaimsActive || ExecutionSetupVerified())
                                 && CustomStageAllocator.StageApplicationStillOwned(ProtectedStageResourceKey(plan))));
        }

        private string ProtectedStageResourceKey(CustomPlan plan)
        {
            return _protectedScheduler == null ? null : plan.Name + ":" + _protectedScheduler.StageIndex + ":"
                + _protectedGeneration + ":" + _protectedScheduler.Entry;
        }

        internal CustomStageRuntime GetProtectedStage(CustomPlan plan)
        {
            if (!ReferenceEquals(reference, this) || _unloaded || !ProtectedEpochAvailable()) return null;
            if (!CombatPrepRunActive) ServiceAutomaticRecovery();
            if (CombatPrepActive && (_protectedScheduler == null || _protectedPlanFile != Settings.CustomPlanFile
                || !_protectedScheduler.Matches(plan) || !ProtectedEpochMatches())) return null;
            if (_protectedScheduler == null || _protectedPlanFile != Settings.CustomPlanFile || !ProtectedEpochMatches())
            {
                if (_protectedScheduler != null && _protectedPlanFile != Settings.CustomPlanFile)
                    SaveProtectedStageProgress(true);
                InitializeProtectedStageProgress(plan);
            }
            if (_protectedOuterCheckpointIssue != null)
            {
                SetResourceExecution(ResourceExecutionState.NeedsUser, ProtectedPresetOwner, _protectedOuterCheckpointIssue);
                return null;
            }
            if (!CombatPrepRunActive) ServiceAutomaticRecovery();
            if (CombatPrepCleanupRecoveryRequired) return null;
            if (_automaticRecoveryRetryEntry == AutomaticRecoveryEntry && !_protectedCheckpointDurable
                && CombatPrepClock < _automaticRecoveryRetryAt) return null;

            var elapsed = Character.rebirthTime.totalseconds;
            var previousEntry = _protectedScheduler.Entry;
            var previousFightSchedule = _stageFightSchedule;
            var wasComplete = _protectedScheduler.ProtectedWorkComplete;
            var automaticBefore = _protectedScheduler.Matches(plan) && _protectedScheduler.StageIndex >= 0
                && _protectedPlan.Stages[_protectedScheduler.StageIndex].ExecutionMode == (int)CustomStageExecutionMode.AutoAugmentsWandoos
                ? _protectedScheduler.ExportState() : null;
            _protectedPlan = plan;
            _protectedScheduler.AllowRepeat = !new CustomPlanRebirth(plan).PeekTriggersAvailable();
            // An active executor must clean up before a new entry can be selected.
            if (!CombatPrepRunActive)
                _protectedScheduler.Update(plan, elapsed, ProtectedHeartbeat, ProtectedStageOwnershipAvailable(plan),
                    stageIndex => CustomStageAllocator.AdvancedTrainingStatsReached(plan.Stages[stageIndex]));
            ServiceGlobalPresetNativeTargets();
            _protectedLastElapsed = elapsed;
            _protectedLastTotal = Character.totalPlaytime.totalseconds;
            AcquireStageFightSchedule(plan);
            var changed = previousEntry != _protectedScheduler.Entry || wasComplete != _protectedScheduler.ProtectedWorkComplete;
            var saved = SaveProtectedStageProgress(changed);
            if (changed && automaticBefore != null && !saved)
            {
                _protectedScheduler.RestoreState(plan, automaticBefore);
                _stageFightSchedule = previousFightSchedule;
                _automaticRecoveryRetryEntry = AutomaticRecoveryEntry;
                _automaticRecoveryRetryAt = CombatPrepClock + 1;
                SetResourceExecution(ResourceExecutionState.Failed, AutomaticRecoveryEntry,
                    "Automatic transition checkpoint could not be saved; check runtime directory permissions or file locks. Retrying automatically.");
                return null;
            }
            var index = _protectedScheduler.StageIndex;
            if (index < 0) return null;
            var durations = _protectedScheduler.Durations;
            var duration = ProtectedSeconds(durations[index]);
            var stageElapsed = plan.Stages[index].ExecutionMode == 1
                ? Math.Max(0, elapsed - _protectedScheduler.EnteredAt) : _protectedScheduler.StageElapsedSeconds;
            return new CustomStageRuntime
            {
                Stage = plan.Stages[index], StageIndex = index,
                StageStartSeconds = ProtectedSeconds(_protectedScheduler.EnteredAt),
                StageEndSeconds = ProtectedSeconds(_protectedScheduler.ExpectedEndSeconds),
                EntryIdentity = _protectedGeneration + ":" + _protectedScheduler.Entry.ToString(CultureInfo.InvariantCulture),
                StageElapsedSeconds = stageElapsed,
                EffectiveDurationSeconds = duration,
                RemainingSeconds = Math.Max(0, ProtectedSeconds(durations[index] - stageElapsed)),
                Message = $"Stage {index + 1}/{plan.Stages.Length}: {plan.Stages[index].Name}; {_protectedScheduler.Reason}"
            };
        }

        private void InitializeProtectedStageProgress(CustomPlan plan)
        {
            _protectedScheduler = new ProtectedStageScheduler();
            _protectedPlan = plan;
            _protectedPlanFile = Settings.CustomPlanFile;
            _protectedGeneration = Guid.NewGuid().ToString("N");
            _protectedRebirthNumber = Character.stats.rebirthNumber;
            _protectedRunOrigin = Character.totalPlaytime.totalseconds - Character.rebirthTime.totalseconds;
            _protectedLastElapsed = Character.rebirthTime.totalseconds;
            _protectedLastTotal = Character.totalPlaytime.totalseconds;
            _protectedCheckpointDurable = false;
            _protectedLastSavedAt = -1d;
            _protectedRecoveryReason = string.Empty;
            _protectedPersistenceError = string.Empty;
            _protectedOuterCheckpointIssue = null;
            _protectedSelectedPolicy = _protectedSelectedPolicyBlockedEntry = _protectedSelectedPolicyIssue = null;
            _stageFightSchedule = null;
            _automaticStageSetupEntry = null;
            _automaticAtContinuation = null;
            _automaticSafeResourcesEntry = null;
            _automaticRecoveryRetryEntry = null;
            _automaticCancelledEntry = null;
            _automaticAcquisitionRetryEntry = null;
            var path = ProtectedCheckpointPath;
            if (path != null && File.Exists(path))
            {
                try
                {
                    var saved = JsonFiles.ParseObject(File.ReadAllText(path));
                    long rebirth;
                    var envelopeValid = EquipmentDescriptorInteger(saved["version"], 1, 2)
                        && saved["plan"].IsString && saved["plan"].Value == _protectedPlanFile
                        && saved["rebirth"].IsString && long.TryParse(saved["rebirth"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out rebirth)
                        && FiniteCheckpointNumber(saved["origin"], false)
                        && FiniteCheckpointNumber(saved["elapsed"], true) && saved["elapsed"].AsDouble <= _protectedLastElapsed + .1d
                        && FiniteCheckpointNumber(saved["total"], true) && saved["total"].AsDouble <= _protectedLastTotal + .1d
                        && saved["generation"].IsString && Guid.TryParseExact(saved["generation"].Value, "N", out _);
                    // Elapsed time resets on native rebirth; validate that case separately.
                    var forward = ProtectedCheckpointForwardRebirth(saved, _protectedRebirthNumber, _protectedRunOrigin, _protectedLastTotal);
                    var valid = (envelopeValid && saved["rebirth"].Value == _protectedRebirthNumber.ToString(CultureInfo.InvariantCulture)
                        && Math.Abs(saved["origin"].AsDouble - _protectedRunOrigin) <= 5d || forward)
                        && _protectedScheduler.RestoreState(plan, saved["scheduler"] as JSONObject);
                    if (valid)
                    {
                        if (forward)
                        {
                            _protectedScheduler = new ProtectedStageScheduler();
                            _protectedScheduler.Update(plan, _protectedLastElapsed, ProtectedHeartbeat, false, i => false);
                            _customStageFightKey = null;
                            _protectedRecoveryReason = "Native rebirth advanced; a new run starts without replaying the prior entry.";
                            SaveProtectedStageProgress(true);
                            return;
                        }
                        _protectedGeneration = saved["generation"].Value;
                        LoadProtectedPresetSelection(saved);
                        _customStageFightKey = saved["fightEntry"].IsString ? saved["fightEntry"].Value : null;
                        ImportStageFightSchedule(saved["fightSchedule"]);
                        var setup = saved["automaticSetup"];
                        var entry = _protectedPlanFile + ":" + _protectedGeneration + ":" + _protectedScheduler.Entry.ToString(CultureInfo.InvariantCulture);
                        // Clean cleanup clears its marker, not evidence that this entry already acquired resources.
                        var acquiredAutomaticEntry = _protectedScheduler.StageIndex >= 0 && !_protectedScheduler.Finished
                            && plan.Stages[_protectedScheduler.StageIndex].ExecutionMode == (int)CustomStageExecutionMode.AutoAugmentsWandoos
                            && (_protectedScheduler.AutoCleanupRecoveryRequired || _protectedScheduler.AutoEnergyPool.HasValue
                                || _protectedScheduler.AutoMagicPool.HasValue || _protectedScheduler.AutoAugmentsCompleted
                                || _protectedScheduler.AutoAtCompleted || _protectedScheduler.AutoWandoosStarted);
                        if (setup.IsObject && setup["entry"].IsString && setup["entry"].Value == entry
                            && FiniteCheckpointNumber(setup["elapsedSeconds"], true) && FiniteCheckpointNumber(setup["rebirthSeconds"], true)
                            && setup["rebirthSeconds"].AsDouble + .1 >= _protectedScheduler.EnteredAt
                            && setup["rebirthSeconds"].AsDouble <= Character.rebirthTime.totalseconds + .1)
                        {
                            _automaticStageSetupEntry = entry;
                            _automaticStageSetupStarted = CombatPrepClock - setup["elapsedSeconds"].AsDouble
                                - Math.Max(0, Character.rebirthTime.totalseconds - setup["rebirthSeconds"].AsDouble);
                            if (setup["safeResources"].AsBool) _automaticSafeResourcesEntry = entry;
                            if (setup["cancelled"].AsBool) _automaticSafeResourcesEntry = entry;
                        }
                        else if ((saved.Linq.Any(x => x.Key == "automaticSetup") || acquiredAutomaticEntry) && _protectedScheduler.StageIndex >= 0
                            && plan.Stages[_protectedScheduler.StageIndex].ExecutionMode == (int) CustomStageExecutionMode.AutoAugmentsWandoos)
                        {
                            _automaticSafeResourcesEntry = entry;
                            _protectedRecoveryReason = "Automatic setup timing was invalid; only safe background resources will run until the original deadline. No new preparation budget was granted.";
                        }
                        var continuation = saved["automaticAtContinuation"] as JSONObject;
                        if (ValidAutomaticAtContinuation(continuation, entry)) _automaticAtContinuation = continuation;
                        else if (acquiredAutomaticEntry && !_protectedScheduler.AutoAtCompleted && !_protectedScheduler.AutoWandoosStarted)
                        {
                            _automaticSafeResourcesEntry = entry;
                            _protectedRecoveryReason = "Legacy automatic recovery has no acquired AT targets; paid AT and Wandoos are withheld until the original deadline. Safe background resources remain available.";
                        }
                        if (saved["automaticFightSuspended"].AsBool && acquiredAutomaticEntry)
                        {
                            _automaticSafeResourcesEntry = entry;
                            _protectedRecoveryReason = "Interrupted scheduled fight continuation is session-local; only safe background work remains until the original deadline.";
                        }
                        if (string.IsNullOrEmpty(_protectedRecoveryReason))
                            _protectedRecoveryReason = "Restored saved progress; offline time is not protected training time.";
                        // The process was cancelled by restart. Its cleanup grant is
                        // gone, but its pools, phase progress and original deadline remain.
                        _protectedScheduler.AutoCleanupRecoveryRequired = false;
                    }
                    else _protectedOuterCheckpointIssue = "Saved stage progress does not match this plan/save. Original checkpoint preserved; explicit restart is required.";
                }
                catch (Exception e)
                {
                    _protectedOuterCheckpointIssue = "Saved stage progress is unreadable. Original checkpoint preserved; explicit restart is required.";
                    LogError("restore protected stage progress", e);
                }
            }
        }

        private static bool FiniteCheckpointNumber(JSONNode node, bool nonnegative)
        {
            return node != null && node.IsNumber && !double.IsNaN(node.AsDouble) && !double.IsInfinity(node.AsDouble)
                   && (!nonnegative || node.AsDouble >= 0);
        }

        private bool ProtectedCheckpointForwardRebirth(JSONObject saved, long rebirth, double origin, double total)
        {
            long previous;
            return EquipmentDescriptorInteger(saved["version"], 1, 2)
                && saved["plan"].IsString && saved["plan"].Value == _protectedPlanFile
                && saved["generation"].IsString && Guid.TryParseExact(saved["generation"].Value, "N", out _)
                && saved["rebirth"].IsString && long.TryParse(saved["rebirth"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out previous)
                && rebirth > 0 && previous == rebirth - 1
                && FiniteCheckpointNumber(saved["origin"], false) && FiniteCheckpointNumber(saved["elapsed"], true)
                && FiniteCheckpointNumber(saved["total"], true) && saved["elapsed"].AsDouble <= saved["total"].AsDouble
                && Math.Abs(saved["total"].AsDouble - saved["elapsed"].AsDouble - saved["origin"].AsDouble) <= 5d
                && saved["total"].AsDouble <= total + .1d && saved["total"].AsDouble <= origin + .1d;
        }

        private bool TryCommitProtectedEntryChange(Action<ProtectedStageScheduler> change)
        {
            var scheduler = _protectedScheduler;
            var plan = _protectedPlan;
            var entry = AutomaticRecoveryEntry;
            var before = scheduler.ExportState();
            change(scheduler);
            if (SaveProtectedStageProgress(true)) return true;
            // A failed save must not roll back a successor that replaced this entry.
            if (ReferenceEquals(scheduler, _protectedScheduler) && entry == AutomaticRecoveryEntry)
                scheduler.RestoreState(plan, before);
            return false;
        }

        private bool SaveProtectedStageProgress(bool force = false)
        {
            if (_protectedOuterCheckpointIssue != null || !ReferenceEquals(reference, this) || _protectedScheduler == null || !_protectedScheduler.Matches(_protectedPlan) || !ProtectedEpochMatches()) return false;
            var heartbeat = ProtectedHeartbeat;
            if (!force && _protectedLastSavedAt >= 0 && heartbeat - _protectedLastSavedAt < 5d) return false;
            _protectedLastSavedAt = heartbeat;
            _protectedCheckpointDurable = false;
            try
            {
                var path = ProtectedCheckpointPath;
                if (path == null) throw new InvalidOperationException("The scheduler checkpoint directory is unavailable.");
                var saved = new JSONObject();
                saved["version"] = 2;
                saved["plan"] = _protectedPlanFile;
                saved["generation"] = _protectedGeneration;
                saved["rebirth"] = _protectedRebirthNumber.ToString(CultureInfo.InvariantCulture);
                saved["origin"] = _protectedRunOrigin;
                saved["elapsed"] = _protectedLastElapsed;
                saved["total"] = _protectedLastTotal;
                saved["fightEntry"] = _customStageFightKey ?? string.Empty;
                var fightSchedule = ExportStageFightSchedule();
                if (fightSchedule != null) saved["fightSchedule"] = fightSchedule;
                saved["automaticFightSuspended"] = (_combatPrepRun?.FightSuspended == true || _combatPrepRun?.FightResuming == true)
                    && _combatPrepRun.OwnerEntry == _protectedGeneration + ":" + _protectedScheduler.Entry.ToString(CultureInfo.InvariantCulture);
                saved["scheduler"] = _protectedScheduler.ExportState();
                // This SimpleJSON parser does not flush bare scalar roots. Preserve even
                // a malformed scalar commitment while it remains quarantined.
                var selection = _protectedSelectedPolicy == null ? null
                    : JSONNode.Parse("{\"selection\":" + _protectedSelectedPolicy + "}")["selection"];
                var retainSelection = selection != null && (_protectedSelectedPolicyBlockedEntry == AutomaticRecoveryEntry
                    || selection.IsObject && selection["owner"].Value == ProtectedPresetOwner);
                if (retainSelection) saved["selectedPolicy"] = selection;
                var entry = _protectedPlanFile + ":" + _protectedGeneration + ":" + _protectedScheduler.Entry.ToString(CultureInfo.InvariantCulture);
                if (_automaticStageSetupEntry == entry)
                {
                    var setup = new JSONObject(); setup["entry"] = entry;
                    setup["elapsedSeconds"] = Math.Max(0, CombatPrepClock - _automaticStageSetupStarted);
                    setup["rebirthSeconds"] = Character.rebirthTime.totalseconds;
                    setup["safeResources"] = _automaticSafeResourcesEntry == entry;
                    setup["cancelled"] = _automaticCancelledEntry == entry;
                    saved["automaticSetup"] = setup;
                }
                else if (_automaticSafeResourcesEntry == entry)
                {
                    // Missing/corrupt setup timing never becomes a new acquisition budget on reload.
                    var setup = new JSONObject(); setup["entry"] = entry;
                    setup["elapsedSeconds"] = Math.Max(0, Character.rebirthTime.totalseconds - _protectedScheduler.EnteredAt);
                    setup["rebirthSeconds"] = Character.rebirthTime.totalseconds; setup["safeResources"] = true;
                    saved["automaticSetup"] = setup;
                }
                if (_automaticAtContinuation?["entry"].Value == entry) saved["automaticAtContinuation"] = _automaticAtContinuation;
                JsonFiles.WriteAtomic(path, saved.ToString(2));
                if (!retainSelection) _protectedSelectedPolicy = _protectedSelectedPolicyBlockedEntry = _protectedSelectedPolicyIssue = null;
                _protectedCheckpointDurable = true;
                _protectedPersistenceError = string.Empty;
                return true;
            }
            catch (Exception e)
            {
                _protectedPersistenceError = "Progress could not be saved; automatic rebirth is blocked. " + e.GetType().Name + ": " + e.Message;
                LogError("save protected stage progress", e);
                return false;
            }
        }

        private void CommitProtectedStageApplication(CustomPlan plan)
        {
            if (_protectedScheduler == null || !_protectedScheduler.Matches(plan)) return;
            var index = _protectedScheduler.StageIndex;
            var applied = index >= 0 && index < plan.Stages.Length && !string.IsNullOrEmpty(plan.Stages[index].PresetId)
                && plan.Stages[index].ExecutionMode == 0 ? GlobalPresetStageReceiptValid(plan)
                : InlineStageSetupVerified(plan) && CustomStageAllocator.LastStageApplicationSucceeded
                    && CustomStageAllocator.StageApplicationStillOwned(ProtectedStageResourceKey(plan));
            if (applied && ProtectedStageOwnershipAvailable(plan))
                _protectedScheduler.MarkApplied(Character.rebirthTime.totalseconds, ProtectedHeartbeat);
            else _protectedScheduler.Suspend();
            SaveProtectedStageProgress();
        }

        private void SuspendProtectedStageProgress()
        {
            _protectedScheduler?.Suspend();
        }

        private void ResetProtectedStageProgress()
        {
            _protectedScheduler = null;
            _stageFightSchedule = null;
            _protectedPlan = null;
            _protectedSelectedPolicy = _protectedSelectedPolicyBlockedEntry = _protectedSelectedPolicyIssue = null;
            _protectedCheckpointDurable = false;
            _executionRebindReview = null;
        }

        internal void ForgetStageProgress(string planName)
        {
            if (string.Equals(_protectedPlanFile, planName, StringComparison.OrdinalIgnoreCase))
            {
                ResetProtectedStageProgress();
                _customStageFightKey = null;
            }
            if (!string.IsNullOrEmpty(_dir)) File.Delete(ProtectedCheckpointForPlan(planName));
        }

        internal bool HasCompletedProtectedStageWork(CustomPlan plan)
        {
            return _protectedScheduler != null && _protectedPlanFile == Settings?.CustomPlanFile
                   && _protectedScheduler.Matches(plan) && ProtectedEpochMatches()
                   && _protectedOuterCheckpointIssue == null && !_protectedScheduler.AutoCleanupRecoveryRequired && _protectedScheduler.ProtectedWorkComplete && _protectedCheckpointDurable;
        }

        private bool ManualTitanCompletedPlanATReady()
        {
            if (Settings?.UseCustomRebirthPlan != true || CustomPlanStore == null) return false;
            CustomPlan plan;
            try { plan = LoadRuntimeCustomPlan(); }
            catch { return false; } // An unreadable current plan cannot prove completed work.
            if (plan?.StageSchedulerEnabled != true || !HasCompletedProtectedStageWork(plan)) return false;
            var completed = _protectedScheduler.Completed;
            var skipped = _protectedScheduler.Skipped;
            for (var i = 0; i < plan.Stages.Length; i++)
            {
                var stage = plan.Stages[i];
                if (stage.ExecutionMode == (int) CustomStageExecutionMode.Standard
                    && stage.CompletionMode == (int) CustomStageCompletionMode.AdvancedTrainingStats
                    && completed[i] && !skipped[i]
                    && stage.TargetAdvancedTrainingPower > 0 && stage.TargetAdvancedTrainingToughness > 0
                    && CustomStageAllocator.AdvancedTrainingStatsReached(stage)) return true;
            }
            return false;
        }

        internal string OverrideCustomStage(JSONNode args)
        {
            if (CombatPrepRunActive) RequireCombatPrepIdle();
            if (args == null || !args.IsObject || args.Count != 5)
                throw new FormatException("Stage actions require name, expectedRevision, expectedEntry, action, and stageIndex.");
            foreach (var field in new[] {"name", "expectedRevision", "expectedEntry", "action"})
                if (args[field] == null || !args[field].IsString || string.IsNullOrWhiteSpace(args[field].Value))
                    throw new FormatException("args." + field + " must be a nonempty string.");
            var indexNode = args["stageIndex"];
            if (indexNode == null || !indexNode.IsNumber || double.IsNaN(indexNode.AsDouble) || double.IsInfinity(indexNode.AsDouble)
                || indexNode.AsDouble < 0 || indexNode.AsDouble > int.MaxValue || indexNode.AsDouble != Math.Floor(indexNode.AsDouble))
                throw new FormatException("args.stageIndex must be a nonnegative whole stage index.");
            var action = args["action"].Value;
            if (action != "skip" && action != "set" && action != "restart")
                throw new FormatException("Stage action must be skip, set, or restart.");
            var name = args["name"].Value;
            if (Settings == null || CustomPlanStore == null || !Settings.UseCustomRebirthPlan
                || !string.Equals(Settings.CustomPlanFile, name, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Only the selected running plan can change stages.");
            if (_manualRebirthPending || _customPreRebirthPrepRunning || _customPreRebirthPrepCompleted
                || BossPushRunning || CustomBossFightActive() || _customRequestedFocusGoal.HasValue)
                throw new InvalidOperationException("Wait for focus, boss fighting, or rebirth preparation to finish before changing stages.");
            var plan = CustomPlanStore.Load(name);
            if (!plan.StageSchedulerEnabled || !string.Equals(plan.RuntimeRevision, args["expectedRevision"].Value, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Plan revision changed; reload the saved plan before changing stages.");
            if (_protectedScheduler == null || _protectedPlanFile != Settings.CustomPlanFile
                || !_protectedScheduler.Matches(plan) && _protectedOuterCheckpointIssue == null || !ProtectedEpochMatches())
                throw new InvalidOperationException("Wait for matching live stage progress before changing stages.");
            var entry = _protectedGeneration + ":" + _protectedScheduler.Entry.ToString(CultureInfo.InvariantCulture);
            if (!string.Equals(entry, args["expectedEntry"].Value, StringComparison.Ordinal))
                throw new InvalidOperationException("The current stage entry changed; refresh before changing stages.");
            var index = indexNode.AsInt;
            if (index >= plan.Stages.Length || action != "set" && index != _protectedScheduler.StageIndex
                && !(_protectedOuterCheckpointIssue != null && action == "restart"))
                throw new InvalidOperationException("Skip and restart must target the current stage; all targets must be valid.");
            if (_protectedOuterCheckpointIssue != null && action != "restart")
                throw new InvalidOperationException(_protectedOuterCheckpointIssue);

            var before = _protectedScheduler.ExportState();
            var schedulerBefore = _protectedScheduler;
            var wasDurable = _protectedCheckpointDurable;
            var lastElapsed = _protectedLastElapsed;
            var lastTotal = _protectedLastTotal;
            var fightEntry = _customStageFightKey;
            var fightScheduleBefore = _stageFightSchedule;
            var persistenceError = _protectedPersistenceError;
            var outerIssue = _protectedOuterCheckpointIssue;
            try
            {
                // An explicit restart is the only operation allowed to replace unrecoverable
                // commitments. Retain the original bytes as evidence before writing a new entry.
                if (outerIssue != null)
                {
                    var path = ProtectedCheckpointPath;
                    File.Copy(path, path + ".recovery.json", true);
                    _protectedOuterCheckpointIssue = null;
                    _protectedScheduler = new ProtectedStageScheduler();
                    _protectedScheduler.Update(plan, Character.rebirthTime.totalseconds, ProtectedHeartbeat, false, i => false);
                }
                _protectedPlan = plan;
                if (!ProtectedStageOwnershipAvailable(plan)) _protectedScheduler.Suspend();
                _protectedScheduler.OverrideStage(plan, outerIssue != null ? "set" : action, index, Character.rebirthTime.totalseconds, ProtectedHeartbeat);
                // A terminal skip invalidates stale commands but is not a new visit or entry fight.
                if (action == "skip" && _protectedScheduler.Finished && _protectedScheduler.StageIndex >= 0)
                    _customStageFightKey = $"{Settings.CustomPlanFile}:{plan.Name}:{_protectedScheduler.StageIndex}:{_protectedGeneration}:{_protectedScheduler.Entry.ToString(CultureInfo.InvariantCulture)}";
                _protectedLastElapsed = Character.rebirthTime.totalseconds;
                _protectedLastTotal = Character.totalPlaytime.totalseconds;
                AcquireStageFightSchedule(plan);
                if (!SaveProtectedStageProgress(true))
                    throw new IOException("Stage change was not applied because its progress checkpoint could not be saved.");
            }
            catch
            {
                if (outerIssue != null) _protectedScheduler = schedulerBefore;
                else _protectedScheduler.RestoreState(plan, before);
                _protectedLastElapsed = lastElapsed;
                _protectedLastTotal = lastTotal;
                _protectedCheckpointDurable = wasDurable;
                _customStageFightKey = fightEntry;
                _stageFightSchedule = fightScheduleBefore;
                _protectedPersistenceError = persistenceError;
                _protectedOuterCheckpointIssue = outerIssue;
                throw;
            }
            ServiceGlobalPresetNativeTargets();
            var reviewedModel = Character;
            var reviewedEpoch = new LoadoutManager.SnapshotEpoch();
            var reviewedGeneration = _protectedGeneration;
            var reviewedEntry = _protectedScheduler.Entry;
            var reviewedConflict = SelectedSetupConflictVersion(reviewedModel);
            var reviewedCombatOwner = "combat-stage:" + Settings.CustomPlanFile + ":" + reviewedGeneration + ":" + reviewedEntry.ToString(CultureInfo.InvariantCulture);
            _selectedSetupReviewAuthorized = owner => ReferenceEquals(reference, this) && !_unloaded
                && ReferenceEquals(Character, reviewedModel) && reviewedEpoch.Matches()
                && Settings?.UseCustomRebirthPlan == true && string.Equals(Settings.CustomPlanFile, name, StringComparison.OrdinalIgnoreCase)
                && _protectedGeneration == reviewedGeneration && _protectedScheduler?.Entry == reviewedEntry
                && !_protectedScheduler.Finished && owner != null && (owner == _customStageEquipmentKey || owner == reviewedCombatOwner)
                && SelectedSetupConflictVersion(reviewedModel) == reviewedConflict;
            _customStageKey = null;
            InvalidateInlineStageApplication();
            _customStageEquipmentKey = null;
            _customLastQuickResourceRefresh = DateTime.MinValue;
            ResetCustomEquipmentSetup();
            CustomStageAllocator.ResetResourceTargets();
            var message = "Manual " + action + " recorded for stage " + (index + 1)
                + "; allocation switches on the next automation pass.";
            SetCustomAutomationStatus(message);
            Log(message);
            return message;
        }

        internal CustomPlanSchedulerSnapshot GetProtectedSchedulerSnapshot(CustomPlan plan, double elapsed)
        {
            var state = _protectedScheduler;
            var valid = state != null && _protectedPlanFile == Settings?.CustomPlanFile && state.Matches(plan) && ProtectedEpochMatches();
            var durations = valid ? state.Durations.Select(ProtectedSeconds).ToArray() : CustomStageAllocator.EffectiveStageDurations(plan);
            var index = valid ? state.StageIndex : -1;
            var stageElapsed = index < 0 ? 0 : plan.Stages[index].ExecutionMode == 1
                ? Math.Max(0, elapsed - state.EnteredAt) : state.StageElapsedSeconds;
            double? progress = index < 0 || plan.Stages[index].CompletionMode == 1 ? (double?) null
                : durations[index] == 0 ? 1d : Math.Min(1d, stageElapsed / durations[index]);
            var remaining = 0d;
            var earned = valid ? state.EarnedSeconds : new double[plan.Stages.Length];
            var completed = valid ? state.Completed : new bool[plan.Stages.Length];
            for (var i = 0; i < plan.Stages.Length; i++)
                if (plan.Stages[i].CompletionMode == 0 && plan.Stages[i].DurationSeconds > 0 && !completed[i])
                    remaining += plan.Stages[i].ExecutionMode == 1 && valid && i == index
                        ? Math.Max(0, state.ExpectedEndSeconds - elapsed)
                        : Math.Max(0, plan.Stages[i].DurationSeconds - earned[i]);
            return new CustomPlanSchedulerSnapshot("stages", index, progress, Math.Max(0, elapsed), stageElapsed,
                valid ? state.Cycle : 0, durations)
            {
                ProtectedWorkComplete = HasCompletedProtectedStageWork(plan), ProtectedSecondsRemaining = remaining,
                ScheduleComplete = valid && state.Finished,
                TimingReason = (valid ? state.Reason : "Waiting to initialize protected timing.")
                    + (_protectedOuterCheckpointIssue == null ? string.Empty : " " + _protectedOuterCheckpointIssue)
                    + (string.IsNullOrEmpty(_protectedRecoveryReason) ? string.Empty : " " + _protectedRecoveryReason)
                    + (string.IsNullOrEmpty(_protectedPersistenceError) ? string.Empty : " " + _protectedPersistenceError)
            };
        }

        private static int ProtectedSeconds(double seconds)
        {
            return (int) Math.Min(int.MaxValue, Math.Max(0, Math.Ceiling(seconds)));
        }
    }
}
