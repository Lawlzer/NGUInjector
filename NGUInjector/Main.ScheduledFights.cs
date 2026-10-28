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
        private sealed class StageFightSchedule
        {
            internal string Entry, Id, Reason = "", Opportunity;
            internal int Anchor, Delay, Interval;
            internal double? AnchorAt;
            internal long Consumed = -1;
            internal bool Disabled, Live, Dispatching;
        }

        private StageFightSchedule _stageFightSchedule;

        // Store ingress may use this ONLY after proving the normalized edit changes no fields
        // except stage FightSchedule definitions. Such edits do not revoke live native claims.
        internal void InvalidateCustomFightSchedulePlanCache()
        {
            _customRuntimePlanCache = null;
            _customRuntimePlanCacheName = null;
            _customRuntimePlanCacheLoadedAt = DateTime.MinValue;
            var plan = LoadRuntimeCustomPlan();
            if (_protectedScheduler == null || _protectedPlanFile != Settings.CustomPlanFile || !_protectedScheduler.Matches(plan)) return;
            _protectedPlan = plan;
            StageFightDue(plan); // Removal latches disabled without changing the acquired definition or active prerequisites.
        }

        private void AcquireStageFightSchedule(CustomPlan plan)
        {
            if (_protectedScheduler == null || _protectedScheduler.StageIndex < 0) return;
            var entry = AutomaticRecoveryEntry;
            if (_stageFightSchedule?.Entry == entry) return;
            var index = _protectedScheduler.StageIndex;
            var definition = plan.Stages[index].FightSchedule;
            var schedule = new StageFightSchedule { Entry = entry, Id = definition?.Id,
                Anchor = definition?.Anchor ?? 0, Delay = definition?.DelaySeconds ?? 0,
                Interval = definition?.IntervalSeconds ?? 0, Disabled = definition == null };
            if (schedule.Anchor == 0) schedule.AnchorAt = _protectedScheduler.EnteredAt;
            var legacyKey = Settings.CustomPlanFile + ":" + plan.Name + ":" + index + ":"
                + _protectedGeneration + ":" + _protectedScheduler.Entry.ToString(CultureInfo.InvariantCulture);
            if (schedule.Id == "legacy-entry-v1-" + index && _customStageFightKey == legacyKey) schedule.Consumed = 0;
            _stageFightSchedule = schedule;
        }

        // Fixed cadence, not last-completion + interval. At most the newest due occurrence is admitted.
        private static long StageFightOccurrence(double now, double anchor, int delay, int interval)
        {
            var elapsed = now - anchor - delay;
            if (elapsed < 0) return -1;
            if (interval == 0) return 0;
            var occurrence = Math.Floor(elapsed / interval);
            return occurrence >= long.MaxValue ? long.MaxValue : (long)occurrence;
        }

        private bool StageFightScheduleCurrent(CustomPlan plan)
        {
            return Settings?.UseCustomRebirthPlan == true && plan?.StageSchedulerEnabled == true
                && _protectedScheduler != null && !_protectedScheduler.Finished && _protectedScheduler.StageIndex >= 0
                && _protectedPlanFile == Settings.CustomPlanFile && _protectedScheduler.Matches(plan)
                && ProtectedEpochMatches() && _protectedOuterCheckpointIssue == null
                && _stageFightSchedule?.Entry == AutomaticRecoveryEntry;
        }

        internal void ObserveStageFightAugmentFunding(IDictionary<ResourceTarget, long[]> desired)
        {
            var schedule = _stageFightSchedule;
            if (!StageFightScheduleCurrent(_protectedPlan) || schedule.Disabled || schedule.Anchor != 1
                || schedule.AnchorAt.HasValue || !Settings.ManageEnergy || desired == null
                || _protectedPlan.Stages[_protectedScheduler.StageIndex].FightSchedule == null) return;
            // Callers supply verified owned intent. Also inspect native amounts: configuration is not funding.
            var augments = Character.augments.augs;
            var positive = desired.TryGetValue(ResourceTarget.Augment, out var aug)
                && aug.Where((amount, i) => amount > 0 && i < augments.Length && augments[i].augEnergy == amount).Any()
                || desired.TryGetValue(ResourceTarget.AugmentUpgrade, out var upgrade)
                && upgrade.Where((amount, i) => amount > 0 && i < augments.Length && augments[i].upgradeEnergy == amount).Any();
            if (!positive) return;
            schedule.AnchorAt = Character.rebirthTime.totalseconds;
            // Observation is irrevocable in this session, even when storage fails. Admission retries the save.
            SaveProtectedStageProgress(true);
        }

        internal bool StageFightFundingContextMatches(string context) => _protectedPlan != null
            && StageFightScheduleCurrent(_protectedPlan) && context == ProtectedStageResourceKey(_protectedPlan);

        private JSONObject ExportStageFightSchedule()
        {
            var schedule = _stageFightSchedule;
            if (schedule == null || schedule.Entry != AutomaticRecoveryEntry) return null;
            var saved = new JSONObject();
            saved["entry"] = schedule.Entry; saved["id"] = schedule.Id ?? "";
            saved["anchor"] = schedule.Anchor; saved["delay"] = schedule.Delay; saved["interval"] = schedule.Interval;
            saved["anchorAt"] = schedule.AnchorAt.HasValue ? (JSONNode)new JSONNumber(schedule.AnchorAt.Value) : JSONNull.CreateOrGet();
            saved["consumed"] = schedule.Consumed.ToString(CultureInfo.InvariantCulture);
            saved["disabled"] = schedule.Disabled;
            return saved;
        }

        private void ImportStageFightSchedule(JSONNode saved)
        {
            if (saved == null || saved.IsNull) { AcquireStageFightSchedule(_protectedPlan); return; }
            long consumed;
            if (!saved.IsObject || !saved["entry"].IsString || saved["entry"].Value != AutomaticRecoveryEntry || !saved["id"].IsString
                || !EquipmentDescriptorInteger(saved["anchor"], 0, 1)
                || !EquipmentDescriptorInteger(saved["delay"], 0, int.MaxValue)
                || !EquipmentDescriptorInteger(saved["interval"], 0, int.MaxValue)
                || !saved["disabled"].IsBoolean || !saved["consumed"].IsString
                || !long.TryParse(saved["consumed"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out consumed) || consumed < -1
                || !saved["anchorAt"].IsNull && (!FiniteCheckpointNumber(saved["anchorAt"], true)
                    || saved["anchorAt"].AsDouble < _protectedScheduler.EnteredAt || saved["anchorAt"].AsDouble > _protectedLastElapsed + .1)
                || string.IsNullOrWhiteSpace(saved["id"].Value) && !saved["disabled"].AsBool
                || saved["anchor"].AsInt == 0 && (saved["anchorAt"].IsNull || saved["anchorAt"].AsDouble != _protectedScheduler.EnteredAt)
                || consumed >= 0 && (saved["anchorAt"].IsNull
                    || consumed > StageFightOccurrence(_protectedLastElapsed, saved["anchorAt"].AsDouble, saved["delay"].AsInt, saved["interval"].AsInt)))
                throw new FormatException("Saved fight schedule is invalid; explicit stage restart is required.");
            _stageFightSchedule = new StageFightSchedule { Entry = AutomaticRecoveryEntry, Id = saved["id"].Value,
                Anchor = saved["anchor"].AsInt, Delay = saved["delay"].AsInt, Interval = saved["interval"].AsInt,
                AnchorAt = saved["anchorAt"].IsNull ? (double?)null : saved["anchorAt"].AsDouble,
                Consumed = consumed, Disabled = saved["disabled"].AsBool };
        }

        private bool StageFightDue(CustomPlan plan)
        {
            if (!StageFightScheduleCurrent(plan)) return false;
            var schedule = _stageFightSchedule;
            if (plan.Stages[_protectedScheduler.StageIndex].FightSchedule == null && !schedule.Disabled)
            { schedule.Disabled = true; SaveProtectedStageProgress(true); }
            return !schedule.Disabled && schedule.AnchorAt.HasValue
                && StageFightOccurrence(Character.rebirthTime.totalseconds, schedule.AnchorAt.Value, schedule.Delay, schedule.Interval) > schedule.Consumed;
        }

        private bool ServiceStageFightSchedule(CustomPlan plan)
        {
            if (_fightPreparationAttempt?.EntryCurrent != null && !_fightPreparationAttempt.EntryCurrent()
                && !RetireObsoleteFightPreparation()) return true;
            if (!StageFightScheduleCurrent(plan)) return false;
            var schedule = _stageFightSchedule;
            if (schedule.Dispatching) return true;
            var due = StageFightDue(plan);
            if (schedule.Live)
            {
                if (BossPushRunning || CustomBossFightActive()) return true;
                if (!RetireObsoleteFightPreparation()) return true;
                var identity = ActionIdentity("fight-preparation", "fight-preparation", schedule.Opportunity);
                if (FightPreparationCanRetry(identity.Key) && !schedule.Disabled)
                    return DispatchStageFight(plan, schedule);
                if (FightPreparationCanRetry(identity.Key) && schedule.Disabled)
                {
                    CancelFightPreparationAttempt();
                    if (_fightPreparationClaim != null) return true;
                }
                schedule.Live = false;
                schedule.Reason = "";
                if (schedule.AnchorAt.HasValue)
                    schedule.Consumed = Math.Max(schedule.Consumed, StageFightOccurrence(Character.rebirthTime.totalseconds,
                        schedule.AnchorAt.Value, schedule.Delay, schedule.Interval));
                SaveProtectedStageProgress(true);
                return false;
            }
            if (!due) return false;
            if (CombatPrepRunActive && !_combatPrepRun.FightSuspended) return false;
            if (ManualAdventureOwnsEquipment || Controller.midDrag || _manualRebirthPending || _customPreRebirthPrepRunning
                || BossPushRunning || CustomBossFightActive()
                || ExecutionOwnsDomain(ExecutionDomain.Gear | ExecutionDomain.Support | ExecutionDomain.Route
                    | ExecutionDomain.FiniteAction | ExecutionDomain.Inventory))
            { schedule.Reason = "Waiting for fight ownership."; SuspendProtectedStageProgress(); return true; }
            if (!RetireObsoleteFightPreparation()) return true;
            var previous = schedule.Consumed;
            var previousFightKey = _customStageFightKey;
            schedule.Consumed = StageFightOccurrence(Character.rebirthTime.totalseconds, schedule.AnchorAt.Value, schedule.Delay, schedule.Interval);
            schedule.Opportunity = schedule.Entry + ":" + schedule.Id + ":" + schedule.Consumed.ToString(CultureInfo.InvariantCulture);
            _customStageFightKey = Settings.CustomPlanFile + ":" + _protectedPlan.Name + ":" + _protectedScheduler.StageIndex
                + ":" + _protectedGeneration + ":" + _protectedScheduler.Entry.ToString(CultureInfo.InvariantCulture);
            if (!SaveProtectedStageProgress(true))
            { schedule.Consumed = previous; _customStageFightKey = previousFightKey; schedule.Reason = "Waiting for durable fight schedule."; return true; }
            schedule.Live = true;
            return DispatchStageFight(plan, schedule);
        }

        private bool DispatchStageFight(CustomPlan plan, StageFightSchedule schedule)
        {
            schedule.Dispatching = true;
            try
            {
                SuspendProtectedStageProgress(); InvalidateInlineStageApplication();
                schedule.Reason = RunFightPrep(plan, false, "Scheduled fight", opportunity: schedule.Opportunity);
                SetCustomAutomationStatus(schedule.Reason);
                return true;
            }
            finally { schedule.Dispatching = false; }
        }

        private bool ScheduledFightAdmissionPermitted(FiniteActionIdentity identity)
        {
            var schedule = _stageFightSchedule;
            if (schedule?.Live != true || identity.Key != ActionIdentity("fight-preparation", "fight-preparation", schedule.Opportunity).Key) return true;
            var plan = LoadRuntimeCustomPlan();
            return !schedule.Disabled && StageFightScheduleCurrent(plan)
                && plan.Stages[_protectedScheduler.StageIndex].FightSchedule != null;
        }

        private bool TrySuspendCombatPrepForFight(CombatPrepRun run)
        {
            var plan = LoadRuntimeCustomPlan();
            if (!StageFightDue(plan) || run.PhaseIndex < 0 || !run.SetupVerified || run.AppliedResourcePhase != run.PhaseIndex
                || !CombatPrepRequirementsVerified(run, run.Policy.Phases[run.PhaseIndex])
                || run.BlockedReason != null || run.YieldedResourceLanes != 0 || ManualAdventureOwnsEquipment
                || ExecutionClaimsActive || GetExecutionDomainReviewReason(run.Setup.Owner) != null) return false;
            // Return borrowed menus while their old grants are still valid. No historical resource restore.
            RestoreCombatPrepTargets(run);
            if (!CombatPrepTargetsMatch(run)) throw new InvalidOperationException("Training target return was not verified before scheduled fight.");
            RetireCombatPrepAdventureRoute(run);
            if (Character.adventure.zone != -1 || CustomBossFightActive()) return false;
            SettleCombatPrepActivity(run, CombatPrepClock);
            run.FightSuccessorGear = run.ExpectedGear.ToArray();
            run.FightSuccessorMaterial = run.ExpectedMaterial.ToArray();
            run.FightSuccessorSupport = run.ExpectedSupport;
            run.FightSuccessorSupportObjects = run.ExpectedSupportObjects.ToArray();
            run.FightConflictVersion = SelectedSetupConflictVersion(Character);
            run.FightReleaseVerified = true;
            RestoreCombatPrepNativeNuke(run, false);
            run.FightSuspended = true;
            run.SetupVerified = false; run.RequirementsDue = false; run.AppliedResourcePhase = -1;
            run.Setup = null;
            InvalidateExecutionObservations(ExecutionDomain.Gear | ExecutionDomain.Support | ExecutionDomain.Route
                | ExecutionDomain.NativeSettings | ExecutionDomain.Energy | ExecutionDomain.Magic);
            run.Reason = "Suspended for scheduled fight; original phase and deadline retained.";
            // Persist interrupted-entry intent before any fight admission, not a durable live claim.
            if (SaveProtectedStageProgress(true)) ServiceSuspendedCombatPrep();
            return true;
        }

        private bool ServiceSuspendedCombatPrep()
        {
            var run = _combatPrepRun;
            if (!CombatPrepRunActive || !run.FightSuspended && !run.FightResuming) return false;
            if (run.CleanupPending)
            {
                CancelSuspendedCombatPrep(run, run.CleanupReason);
                return true;
            }
            if (!CombatPrepEpochMatches(run.Capture) || !AutomaticCombatPrepOwnerMatches(run)
                || !CombatPrepDeadlineAvailable(run) || !Settings.ManageEnergy || !Settings.ManageMagic
                || _manualRebirthPending || _customPreRebirthPrepRunning || ManualAdventureOwnsEquipment || Controller.midDrag
                || run.BlockedReason != null || run.YieldedResourceLanes != 0)
            {
                CancelSuspendedCombatPrep(run, "Scheduled fight continuation lost authority or reached its original deadline.");
                return true;
            }
            if (run.Policy.Phases[run.PhaseIndex].Kind == "AT" && CombatPrepHasNativeWandoosWork())
            {
                // In particular, do not recall allocation-only W evidence while reacquiring AT gear.
                CancelSuspendedCombatPrep(run, "Wandoos work appeared during the scheduled fight handoff; further paid AT is forbidden.");
                return true;
            }
            if (run.FightResuming)
            {
                var combat = CombatPrepCombatPreemption();
                if (combat != null)
                {
                    CancelSuspendedCombatPrep(run, combat);
                    return true;
                }
                try { PauseCombatPrepNativeNuke(run, false); }
                catch (Exception error)
                {
                    CancelSuspendedCombatPrep(run, "Scheduled fight continuation nuke pause failed: " + error.Message);
                    return true;
                }
            }
            if (run.FightResuming && (!CombatPrepTargetsMatch(run) || !CombatPrepGearMatches(run.ExpectedGear)
                || !CombatPrepSupportStillOwned(run) || run.OsOutcome != null && (int)Character.wandoos98.os != run.Policy.OsId))
            {
                CancelSuspendedCombatPrep(run, "Retained phase changed during scheduled fight reacquisition.");
                return true;
            }
            if (run.FightSuspended)
            {
                var plan = LoadRuntimeCustomPlan();
                if (ServiceStageFightSchedule(plan)) return true;
                if (BossPushRunning || CustomBossFightActive() || _fightPreparationClaim != null) return true;
                if (!run.FightReleaseVerified || !CombatPrepTargetsMatch(run)
                    || run.OsOutcome != null && (int)Character.wandoos98.os != run.Policy.OsId
                    || !CombatPrepGearMatches(run.FightSuccessorGear)
                    || run.FightSuccessorGear.Where((item, i) => LoadoutPresetPrimitiveKey(item.equipment) != run.FightSuccessorMaterial[i]).Any()
                    || run.Inputs.Gear[run.Policy.Phases[run.PhaseIndex].GearProfileKey]
                        .Where((item, i) => LoadoutPresetPrimitiveKey(item.equipment) != run.SetupMaterial[i]).Any()
                    || CombatPrepSupportMaterial(CaptureSupportContext()) != run.Inputs.SupportMaterial
                    || CaptureSupportState().StableKey != run.FightSuccessorSupport.StableKey
                    || SupportObjects().Length != run.FightSuccessorSupportObjects.Length
                    || SupportObjects().Where((item, i) => !ReferenceEquals(item, run.FightSuccessorSupportObjects[i])).Any()
                    || SelectedSetupConflictVersion(Character) != run.FightConflictVersion
                    || _resourceSetupExecutor.Application.BlockedLanes != 0 || ExecutionClaimsActive || Character.adventure.zone != -1)
                {
                    CancelSuspendedCombatPrep(run, "Scheduled fight successor or retained targets changed; continuation cancelled without adopting manual state.");
                    return true;
                }
                run.ExpectedGear = run.FightSuccessorGear;
                run.ExpectedMaterial = run.FightSuccessorMaterial;
                run.ExpectedSupport = run.FightSuccessorSupport;
                run.ExpectedSupportObjects = run.FightSuccessorSupportObjects;
                run.FightResuming = true;
                try
                {
                    PauseCombatPrepNativeNuke(run, true);
                    run.FightSuspended = false;
                    run.FightResumeCount++;
                    SelectCombatPrepRequirements(run, run.Policy.Phases[run.PhaseIndex]);
                }
                catch (Exception error)
                {
                    CancelSuspendedCombatPrep(run, "Scheduled fight continuation acquisition failed: " + error.Message);
                    return true;
                }
            }
            run.RequirementsDue = true;
            ReconcileCombatPrepRequirements();
            if (CombatPrepRunActive && !run.Retired && run.SetupVerified && run.AppliedResourcePhase == run.PhaseIndex
                && run.ResourceOutcome?.Verified == true && CombatPrepRequirementsVerified(run, run.Policy.Phases[run.PhaseIndex]))
            {
                run.FightResuming = false;
                run.ActivityIndex = Array.IndexOf(CombatPrepActivityKinds, run.Policy.Phases[run.PhaseIndex].Kind);
                run.ActivityStarted = CombatPrepClock;
                run.LastRefresh = CombatPrepClock;
                run.Reason = "Resumed retained phase after scheduled fight.";
                SaveProtectedStageProgress(true);
            }
            return true;
        }

        private void CancelSuspendedCombatPrep(CombatPrepRun run, string reason)
        {
            if (run.Automatic && _protectedScheduler != null && CombatPrepEpochMatches(run.Capture) && ProtectedEpochMatches()
                && _protectedPlanFile == run.OwnerPlan && run.OwnerEntry == _protectedGeneration + ":" + _protectedScheduler.Entry.ToString(CultureInfo.InvariantCulture))
            {
                _automaticSafeResourcesEntry = AutomaticRecoveryEntry;
                _automaticCancelledEntry = AutomaticRecoveryEntry;
                SaveProtectedStageProgress(true);
            }
            // A partial reacquisition can already have borrowed target menus. Return only
            // still-owned settings before dropping these callbacks, never resources or gear.
            if (run.FightResuming && CombatPrepReturnAuthority(run) && CombatPrepCombatPreemption() == null)
            {
                FreezeCombatPrepTiming(run);
                run.Status = "cancelled"; run.RequirementsDue = false;
                run.CleanupStatus = "cancelled"; run.CleanupReason = reason;
                try
                {
                    RestoreCombatPrepTargets(run);
                    RestoreCombatPrepNativeNuke(run, true);
                    if (run.NukeTimerOwned) throw new InvalidOperationException("Native nuke timer return is pending.");
                }
                catch (Exception error)
                {
                    run.CleanupPending = true;
                    CombatPrepCleanupWarning(run, "Scheduled fight borrowed-setting return waiting: " + error.Message, error);
                    CacheCombatPrepRuntime();
                    return;
                }
            }
            // The fight keeps its own prerequisites. This retires only the suspended preparation's local authority.
            RestoreCombatPrepNativeNuke(run, false);
            RetireCombatPrepAuthority(run);
            run.Status = "cancelled"; run.Reason = reason;
            run.FightResuming = false;
            _combatPrepEndedRuns.Add(run.Id);
            CacheCombatPrepRuntime();
        }

        private bool SuspendedFightHandoff => _combatPrepRun?.FightSuspended == true
            && _stageFightSchedule?.Live == true && _fightPreparationAttempt != null
            && _fightPreparationAttempt.Identity.Key == ActionIdentity("fight-preparation", "fight-preparation", _stageFightSchedule.Opportunity).Key;

        private JSONObject GetStageFightScheduleRuntime()
        {
            var result = new JSONObject(); var schedule = _stageFightSchedule;
            var current = StageFightScheduleCurrent(_protectedPlan);
            var matched = schedule != null && _protectedScheduler != null && _protectedPlanFile == Settings?.CustomPlanFile
                && schedule.Entry == AutomaticRecoveryEntry && ProtectedEpochMatches();
            var disabled = !current || schedule.Disabled || _protectedPlan.Stages[_protectedScheduler.StageIndex].FightSchedule == null;
            var now = Character?.rebirthTime?.totalseconds ?? 0;
            double? next = !disabled && schedule.AnchorAt.HasValue && (schedule.Interval > 0 || schedule.Consumed < 0)
                ? schedule.AnchorAt + schedule.Delay + ((double)schedule.Consumed + 1) * schedule.Interval : null;
            result["stageEntry"] = matched ? _protectedGeneration + ":" + _protectedScheduler.Entry.ToString(CultureInfo.InvariantCulture) : "";
            result["scheduleId"] = matched ? schedule.Id ?? "" : "";
            result["state"] = current && schedule.Live && (BossPushRunning || CustomBossFightActive()) ? "fighting" : disabled ? "disabled"
                : schedule.Live ? "waiting"
                : !schedule.AnchorAt.HasValue ? "waiting-anchor" : !next.HasValue ? "completed" : next <= now ? "waiting" : "scheduled";
            result["nextFightAtSeconds"] = next.HasValue ? (JSONNode)new JSONNumber(next.Value) : JSONNull.CreateOrGet();
            result["remainingSeconds"] = next.HasValue ? (JSONNode)new JSONNumber(Math.Max(0, next.Value - now)) : JSONNull.CreateOrGet();
            result["reason"] = current ? schedule.Reason : "";
            return result;
        }
    }
}
