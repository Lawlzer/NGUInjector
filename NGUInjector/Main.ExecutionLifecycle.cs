using System;
using System.IO;
using System.Linq;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal partial class Main
    {
        private double _customLastObservedRebirthSeconds = -1d;
        private LoadoutManager.SnapshotEpoch _customObservedEpoch;
        private bool _lifecycleEvidenceCaptured;

        private bool ServiceLifecycleRetirement()
        {
            if (_executionLifecycle != ExecutionLifecycle.Quiescing) return false;
            if (Character?.bossController != null && (Character.bossController.isFighting || Character.bossController.nukeBoss)) return false;
            PauseMaxItopodPush();
            foreach (var process in new[] { _manualTitanProcess, _customTitanGoldProcess, _customTitanFightProcess,
                _customAdventureGoldProcess, _delegatedManualAdventureProcess, _bossPushProcess })
                if (process != null && !EndRouteProcess(process)) return false;
            var run = _combatPrepRun;
            if (run != null && CombatPrepEpochMatches(run.Capture) && !run.Retired)
            {
                if (run.AdventureRouteOwned && Character.adventure.zone != -1)
                {
                    if (!CombatPrepRouteRetirementAuthorized(run)) return false;
                    var zone = Character.adventure.zone; var objects = CombatPrepAdventureRouteObjects();
                    var grant = ResolveExecutionGrantCore("combat:" + run.Id, run.Id, ExecutionDomain.Route | ExecutionDomain.NativeSettings,
                        () => ReferenceEquals(run, _combatPrepRun) && CombatPrepEpochMatches(run.Capture)
                            && (Character.adventure.zone == zone || Character.adventure.zone == -1)
                            && !CombatPrepAdventureRouteObjects().Where((x, i) => !ReferenceEquals(x, objects[i])).Any()
                            && CombatPrepGearMatches(run.ExpectedGear) && CombatPrepSupportStillOwned(run), null, null, true);
                    if (!_combManager.MoveToZone(-1, grant)) return false;
                    run.AdventureRouteOwned = false;
                }
                if (run.PhaseIndex >= 0 && run.ExpectedTargets.TryGetValue(run.PhaseIndex, out var original))
                {
                    var phase = run.Policy.Phases[run.PhaseIndex];
                    var grant = ResolveExecutionGrantCore("combat-target-return:" + run.Id, run.Id, ExecutionDomain.NativeSettings,
                        () => ReferenceEquals(run, _combatPrepRun) && CombatPrepEpochMatches(run.Capture)
                            && Character.adventure.zone == -1 && CombatPrepGearMatches(run.ExpectedGear) && CombatPrepSupportStillOwned(run), null, null, true);
                    if (run.RestoreTargets.Count > 0 && phase.Kind == "AT")
                    {
                        var targets = Character.advancedTraining.levelTarget;
                        for (var i = 0; i < 2; i++)
                        {
                            var index = i + 3; var before = targets[index]; var value = original[i];
                            if (before != 0 && before != value) continue;
                            if (!ApplyExecutionSetting(grant, () => { if (ReferenceEquals(targets, Character.advancedTraining.levelTarget) && targets[index] == before) targets[index] = value; },
                                () => ReferenceEquals(targets, Character.advancedTraining.levelTarget) && targets[index] == value).Verified) return false;
                        }
                    }
                    else if (run.RestoreTargets.Count > 0 && phase.Kind == "Augments")
                    {
                        var aug = Character.augments.augs[phase.AugmentTierId];
                        if (aug.augmentTarget == 0 && !ApplyExecutionSetting(grant, () => aug.augmentTarget = original[0], () => aug.augmentTarget == original[0]).Verified) return false;
                        if (aug.upgradeTarget == 0 && !ApplyExecutionSetting(grant, () => aug.upgradeTarget = original[1], () => aug.upgradeTarget == original[1]).Verified) return false;
                    }
                }
                RestoreCombatPrepNativeNuke(run, true);
                if (run.NukeTimerOwned) return false;
            }
            foreach (var claim in _executionClaims.Where(x => !x.Ended).ToArray())
            {
                if ((claim.Domains & ExecutionDomain.Route) != 0 && Character.adventure.zone != -1) return false;
                EndExecutionClaim(claim, false);
            }
            return ServiceExecutionRetirement();
        }

        private void PersistLifecycleExecutionEvidence()
        {
            if (string.IsNullOrEmpty(_dir) || Character?.stats == null) return;
            if (_lifecycleEvidenceCaptured) return;
            var path = Path.Combine(_dir, "lifecycle-route-commitments-v1.json");
            if (File.Exists(path)) File.Copy(path, path + ".previous-" + Guid.NewGuid().ToString("N"), false);
            JsonFiles.WriteAtomic(path, ExportRouteExecutionCommitments().ToString(2));
            _lifecycleEvidenceCaptured = true;
        }

        // Invoke only after the finite rebirth operation verified commitment, never
        // from a clock/epoch observer. Retain old bytes while permitting a new run.
        internal void CommitExecutionRebirthCheckpoint()
        {
            if (_protectedOuterCheckpointIssue != null || _protectedScheduler == null
                || Character?.stats == null || Character.stats.rebirthNumber == _protectedRebirthNumber) return;
            var path = ProtectedCheckpointPath;
            if (path != null && System.IO.File.Exists(path))
                System.IO.File.Move(path, Path.Combine(Path.GetDirectoryName(path), "stage-rebirth-" + Guid.NewGuid().ToString("N") + ".json"));
        }
        internal void InvalidateCustomEquipmentApplication()
        {
            _manualAdventureAppliedLoadout = null;
            SuspendProtectedStageProgress();
            InvalidateInlineStageApplication();
            _customEquipmentApplicationInvalidated = true;
            _customLastQuickResourceRefresh = DateTime.MinValue;
            ResetCustomEquipmentSetup();
            InvalidateExecutionObservations(ExecutionDomain.Gear | ExecutionDomain.Support);
        }

        private void ObserveCustomRebirthEpoch()
        {
            if (Character == null)
                return;

            var seconds = Math.Max(0d, Character.rebirthTime.totalseconds);
            if (_customObservedEpoch != null && !_customObservedEpoch.Matches()
                || _customLastObservedRebirthSeconds >= 0d && seconds + 5d < _customLastObservedRebirthSeconds)
            {
                Log($"Custom rebirth epoch changed: {_customLastObservedRebirthSeconds:0.#}s -> {seconds:0.#}s. Resetting Custom runtime state.");
                ResetCustomAutomationRuntime();
            }

            _customObservedEpoch = new LoadoutManager.SnapshotEpoch();
            _customLastObservedRebirthSeconds = seconds;
        }

        internal void InvalidateCustomRuntimePlanCache()
        {
            SuspendProtectedStageProgress();
            SaveProtectedStageProgress(true);
            _customRuntimePlanCache = null;
            _customRuntimePlanCacheName = null;
            _customRuntimePlanCacheLoadedAt = DateTime.MinValue;
            InvalidateInlineStageApplication();
            InvalidateExecutionObservations(ExecutionDomain.Energy | ExecutionDomain.Magic | ExecutionDomain.Res3
                | ExecutionDomain.Gear | ExecutionDomain.Support | ExecutionDomain.NativeSettings | ExecutionDomain.Route);
        }

        private void ResetCustomAutomationRuntime()
        {
            _lifecycleEvidenceCaptured = false;
            // Epoch observation revokes authority, not native progression or unresolved
            // operation evidence. Producers retire stale records through their own grants.
            InvalidateExecutionObservations(ExecutionDomain.Energy | ExecutionDomain.Magic | ExecutionDomain.Res3
                | ExecutionDomain.Gear | ExecutionDomain.Support | ExecutionDomain.NativeSettings
                | ExecutionDomain.Route | ExecutionDomain.FiniteAction | ExecutionDomain.Inventory);
            // Old-epoch claims lose references, not their persisted commitments. End
            // without reconciliation; loading/observation must never run a successor.
            foreach (var claim in _executionClaims.Where(x => !x.Ended && !x.Epoch.Matches()).ToArray()) EndExecutionClaim(claim, false);
            _executionBaseline = null; _quickExecutionClaim = null; _reviewedExactSetupPending = null;
            _customRequestedFocusGoal = _customSustainedFocusGoal = _customAppliedFocusGoal = null;
            _customFocusStatus = "needsReview";
            _executionExactLoaded = false; _standaloneSelectionLoaded = false;
            ResetProtectedStageProgress();
            _customStageKey = null;
            _customStageEquipmentKey = null;
            ResetCustomEquipmentSetup();
            _customIdleResourceWarningLastShown = DateTime.MinValue;
            _customLastObservedRebirthSeconds = Character == null ? -1d : Math.Max(0d, Character.rebirthTime.totalseconds);
            _customObservedEpoch = Character == null ? null : new LoadoutManager.SnapshotEpoch();
            if (Character?.stats != null && !string.IsNullOrEmpty(_dir)) EnsureStandaloneSelectionLoaded();
            SetCustomAutomationStatus("Custom automation reset after rebirth.");
        }
    }
}
