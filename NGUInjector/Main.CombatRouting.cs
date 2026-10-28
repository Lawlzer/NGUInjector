using System;
using System.Linq;
using System.Collections.Generic;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal partial class Main
    {
        // Retained acquisition is independent of retries and contains no historical return target.
        private sealed class RouteProcess
        {
            internal ExecutionClaim Claim;
            internal SelectedSetupRequirements Setup;
            internal int Zone = -1;
            internal object[] Objects;
            internal bool AutoAttack, BeastMode, Applied, Admitted;
            internal string Permission;
            internal string Owner;
            internal bool BorrowedPreparation;
            internal ExecutionClaim SettingsClaim;
            internal string[] SelectedMaterial;
            internal bool CapacityTransition, ResourceRetired, ResourceApplied, ResourceAttempted;
            internal GearRecallFootprint ResourceRecall;
            internal object[] ResourceObjects;
            internal Dictionary<ResourceTarget, long[]> ProtectedResources;
            internal Func<bool> ResourceContextCurrent;
            internal Func<bool> ApplicationCurrent;
            internal ExecutionGrant CombatGrant;
            internal ExecutionGrant TransitionGrant;
            internal string ResourceIssue;
            internal long[] ResourceRevisions;
            internal bool ResourceHoldEnded;
        }

        private RouteProcess BeginRouteProcess(string owner, ih[] gear, bool controlsGear = true,
            ExecutionDomain domains = ExecutionDomain.Gear | ExecutionDomain.Support | ExecutionDomain.Route | ExecutionDomain.NativeSettings,
            SupportSelection selectedSupport = null, bool capacityTransition = false,
            GearRecallFootprint resourceRecall = GearRecallFootprint.None, string acquisition = null)
        {
            var preparation = owner == "fight-preparation" ? _fightPreparationAttempt : null;
            if (capacityTransition && !owner.StartsWith("manual-titan:", StringComparison.Ordinal)
                && (preparation == null || preparation.Identity.Owner != owner || preparation.Identity.Acquisition != acquisition))
                throw new InvalidOperationException("Controlled-empty capacity requires a manual titan or identified fight preparation.");
            var process = new RouteProcess { Owner = owner, Objects = CombatPrepAdventureRouteObjects(),
                Zone = Character.adventure.zone, AutoAttack = Character.adventure.autoattacking,
                BeastMode = Character.adventure.beastModeOn, CapacityTransition = capacityTransition, ResourceRecall = resourceRecall };
            var material = gear.Select(x => LoadoutPresetPrimitiveKey(x.equipment)).ToArray();
            process.SelectedMaterial = material;
            var support = selectedSupport ?? CaptureSupportState().Selection;
            var preflight = capacityTransition ? GearOptimizer.PreflightCapacityTransition(gear, resourceRecall).Failure
                : controlsGear ? GearOptimizer.CheckExactSwapCapacity(gear, Character.curEnergy, Character.res3.curRes3) : null;
            if (preflight != null) throw new InvalidOperationException(preflight);
            if (capacityTransition)
            {
                domains |= (ExecutionDomain)(int)resourceRecall;
                var settings = Settings; var stage = _customStageKey; var generation = _protectedGeneration;
                var entry = _protectedScheduler?.Entry;
                var scheduler = _protectedScheduler;
                process.ResourceObjects = ResourceWriter.CaptureResourceContext(Character).Concat(SupportObjects()).Concat(CombatPrepAdventureRouteObjects()).ToArray();
                process.ProtectedResources = ResourceWriter.CaptureAllocations(Character).Where(pair =>
                    pair.Key == ResourceTarget.BasicAttack || pair.Key == ResourceTarget.BasicDefense
                    || ResourceWriter.Lane(pair.Key) == 2
                    || ResourceWriter.Lane(pair.Key) == 0 && !Settings.ManageEnergy
                    || ResourceWriter.Lane(pair.Key) == 1 && !Settings.ManageMagic
                    || (_resourceSetupExecutor.Application.BlockedLanes & (1 << ResourceWriter.Lane(pair.Key))) != 0)
                    .ToDictionary(x => x.Key, x => x.Value?.ToArray());
                process.ResourceContextCurrent = () => ReferenceEquals(settings, Settings) && generation == _protectedGeneration
                    && (preparation == null ? stage == _customStageKey && entry == _protectedScheduler?.Entry
                        : ReferenceEquals(preparation, _fightPreparationAttempt) && ReferenceEquals(scheduler, _protectedScheduler)
                            && preparation.Epoch.Matches() && ReferenceEquals(preparation.Boss, Character.bossController)
                            && preparation.PlanFile == Settings.CustomPlanFile);
                if (preparation != null)
                    process.ApplicationCurrent = () => !preparation.Cancelled && !preparation.Completed
                        && (preparation.EntryCurrent == null || preparation.EntryCurrent());
            }
            process.Permission = RoutePermissionKey(owner);
            process.Claim = BeginExecutionClaim(owner, acquisition ?? Guid.NewGuid().ToString("N"),
                domains,
                () => !process.Admitted || ReferenceEquals(process, _manualTitanProcess)
                    || ReferenceEquals(process, _customTitanGoldProcess) || ReferenceEquals(process, _customTitanFightProcess)
                    || ReferenceEquals(process, _customAdventureGoldProcess) || ReferenceEquals(process, _maxItopodProcess)
                    || ReferenceEquals(process, _delegatedManualAdventureProcess) || ReferenceEquals(process, _bossPushProcess)
                    || ReferenceEquals(process, _fightPreparationAttempt?.Process));
            // Capture the predecessor before claim.Setup resolves to the not-yet-applied incoming setup.
            Func<bool> entryRoute = null;
            process.Setup = new SelectedSetupRequirements { Gear = gear, ControlsGear = controlsGear,
                Recall = capacityTransition ? resourceRecall : GearRecallFootprint.None, Support = support, ChangeSupport = selectedSupport != null,
                Budget = ActivityPolicy.Current.DiggerBudgetFraction, EnforceSupportBudget = false,
                Authorized = () => ExecutionClaimCurrent(process.Claim) && process.Permission == RoutePermissionKey(owner)
                    && !Character.bossController.isFighting && !Character.bossController.nukeBoss,
                MaterialValid = () => !gear.Where((x, i) => LoadoutPresetPrimitiveKey(x.equipment) != material[i]).Any(),
                RouteRetirementAuthorized = () => RouteProcessMatches(process)
                    && (process.Applied || Character.adventure.zone == -1 || entryRoute?.Invoke() == true),
                ObserveRouteRetirement = () => process.Zone = -1 };
            if (capacityTransition && resourceRecall != GearRecallFootprint.None)
                SetExecutionResourcePolicy(process.Claim, new ResourceSetupIntent { Owner = owner,
                    Energy = (resourceRecall & GearRecallFootprint.Energy) != 0, Magic = (resourceRecall & GearRecallFootprint.Magic) != 0,
                    Desired = ResourceWriter.EmptyDesiredMap((resourceRecall & GearRecallFootprint.Energy) != 0, (resourceRecall & GearRecallFootprint.Magic) != 0),
                    Authorized = () => process.ResourceRetired && process.ResourceIssue == null && CapacityRouteOwnership(process)
                        && CapacityRoutePermissions(process) && (process.Claim.SetupReleased || process.Permission == RoutePermissionKey(owner))
                        && (process.Applied || ValidateExecutionGrant(process.TransitionGrant, process.Claim.Domains))
                        && (!process.ResourceApplied || CapacityRouteEmpty(process)) });
            if (capacityTransition) process.ResourceRevisions = (long[])_executionDomainRevisions.Clone();
            entryRoute = CaptureExecutionBaselineRouteRetirement(process.Claim);
            if (capacityTransition)
            {
                process.Claim.CapacityTransition = true;
                process.TransitionGrant = ResolveCapacityRouteTransitionGrant(process);
                process.Setup.Authorized = () => !Character.bossController.isFighting && !Character.bossController.nukeBoss
                    && ValidateExecutionGrant(process.TransitionGrant, process.Claim.Domains);
            }
            process.Admitted = true;
            return process;
        }

        private bool CapacityRoutePermissions(RouteProcess process) =>
            ((process.ResourceRecall & GearRecallFootprint.Energy) == 0 || Settings.ManageEnergy)
            && ((process.ResourceRecall & GearRecallFootprint.Magic) == 0 || Settings.ManageMagic)
            && (_resourceSetupExecutor.Application.BlockedLanes & (int)process.ResourceRecall) == 0
            && (process.ResourceRevisions == null || Enumerable.Range(0, 2).All(lane => ((int)process.ResourceRecall & (1 << lane)) == 0
                || process.ResourceRevisions[lane] == _executionDomainRevisions[lane]));

        private bool CapacityRouteOwnership(RouteProcess process)
        {
            if (!ExecutionClaimCurrent(process.Claim) || process.ResourceContextCurrent?.Invoke() != true) return false;
            try
            {
                var context = ResourceWriter.CaptureResourceContext(Character).Concat(SupportObjects()).Concat(CombatPrepAdventureRouteObjects()).ToArray();
                if (context.Length != process.ResourceObjects.Length || context.Where((x, i) => !ReferenceEquals(x, process.ResourceObjects[i])).Any()) return false;
                var current = ResourceWriter.CaptureAllocations(Character);
                return process.ProtectedResources.All(pair => pair.Value == null ? current[pair.Key] == null
                    : current[pair.Key] != null && pair.Value.SequenceEqual(current[pair.Key]));
            }
            catch { return false; }
        }

        private bool CapacityRouteEmpty(RouteProcess process)
        {
            try
            {
                var current = ResourceWriter.CaptureAllocations(Character);
                var policy = process.Claim.ResourcePolicy;
                return policy == null ? process.ResourceRecall == GearRecallFootprint.None
                    : policy.Desired.All(pair => current[pair.Key] != null && current[pair.Key].SequenceEqual(pair.Value));
            }
            catch { return false; }
        }

        private bool RouteProcessDependenciesReady(RouteProcess process) => process != null && process.Applied
            && ExecutionClaimCurrent(process.Claim) && ExecutionClaimSetupOwned(process.Claim) && process.Setup.MaterialValid()
            && (!process.Setup.ControlsGear || LoadoutManager.ExactLoadoutMatches(process.Setup.Gear))
            && ((process.Claim.Domains & ExecutionDomain.Support) == 0 || CaptureSupportState().Selection.StableKey == process.Setup.Support.StableKey)
            && (!process.CapacityTransition || process.ResourceIssue == null && process.ResourceApplied
                && CapacityRouteOwnership(process) && CapacityRoutePermissions(process) && CapacityRouteEmpty(process))
            && (!process.BorrowedPreparation || FightPreparationDependenciesReady
                && ReferenceEquals(FightPreparationClaim, process.Claim) && ReferenceEquals(FightPreparationSetup, process.Setup));

        private string RoutePermissionKey(string owner)
        {
            if (owner.StartsWith("manual-titan:", StringComparison.Ordinal))
                return Settings.ManualTitanEnabled + ":" + Settings.ManualTitanZone + ":" + Settings.ManualTitanLoadout
                    + ":" + Settings.ManualTitanRequireAT + ":" + Settings.ManualTitanMaxDeaths;
            if (owner == "boss-push") return _bossPushRunning + ":" + _bossPushIdentity;
            if (owner == "fight-preparation") return ExecutionRebindPermissions();
            return Settings.CustomAdventureEnabled + ":" + Settings.CustomAdventureGoldPush + ":" + Settings.CustomAdventureMode
                + ":" + Settings.CustomAdventureZone + ":" + Settings.ManualAdventureEnabled + ":" + Settings.ManualAdventureLoadout;
        }

        private bool RouteProcessMatches(RouteProcess process) => process != null && ExecutionClaimCurrent(process.Claim)
            && process.Objects.Length == CombatPrepAdventureRouteObjects().Length
            && !CombatPrepAdventureRouteObjects().Where((x, i) => !ReferenceEquals(x, process.Objects[i])).Any()
            && Character.adventure.zone == process.Zone && Character.adventure.autoattacking == process.AutoAttack
            && Character.adventure.beastModeOn == process.BeastMode;

        private bool ApplyRouteProcess(RouteProcess process)
        {
            if (!ReferenceEquals(reference, this) || _unloaded) return false;
            if (Character.bossController.isFighting || Character.bossController.nukeBoss) return false;
            if (process.CapacityTransition)
            {
                if (!CapacityRouteOwnership(process) || !CapacityRoutePermissions(process) || process.ResourceIssue != null
                    || !ValidateExecutionGrant(process.TransitionGrant, process.Claim.Domains))
                    throw new InvalidOperationException("Acquired capacity transition authority requires review.");
                if (!RouteProcessMatches(process)) throw new InvalidOperationException("Acquired capacity route changed externally; review is required.");
                if (!process.ResourceRetired)
                {
                    if (SelectedSetupConflicts.TryGetValue(Character, out var unresolved) && unresolved.Rebirth == Character.stats.rebirthNumber
                            && (unresolved.Gear != null || unresolved.Support != null || unresolved.Route != null)
                        || _globalPresetReceipt == null && _globalPresetFailure != null && _presetExecutionBlock?.Disposition == MutationDisposition.UnknownOutcome
                        || process.Setup.RouteRetirementAuthorized?.Invoke() != true)
                        throw new InvalidOperationException("Capacity transition has no verified predecessor route/setup witness.");
                    if (Character.bossController.isFighting || Character.bossController.nukeBoss)
                        throw new InvalidOperationException("Capacity transition is waiting for native boss retirement.");
                    ApplyExecutionClaim(process.Claim, new SelectedSetupRequirements { ControlsGear = false,
                        Support = CaptureSupportState().Selection, ChangeSupport = false, RetireRoute = true,
                        Authorized = process.Setup.Authorized, RouteRetirementAuthorized = process.Setup.RouteRetirementAuthorized,
                        ObserveRouteRetirement = () => process.Zone = -1 });
                    if (Character.adventure.zone != -1) throw new InvalidOperationException("Capacity transition requires verified predecessor retirement.");
                    process.ResourceRetired = true;
                }
                if (process.Claim.ResourcePolicy != null)
                {
                    process.ResourceAttempted = true;
                    var result = ReconcileExecutionResourceClaim(process.Claim);
                    if (!result.Verified) throw new ResourceMutationException(result);
                }
                process.ResourceApplied = true;
                if (!CapacityRouteEmpty(process)) throw new InvalidOperationException("Capacity transition empty policy is unverified.");
            }
            ApplyExecutionClaim(process.Claim, process.Setup);
            process.Zone = Character.adventure.zone;
            process.Applied = true;
            return RouteProcessReady(process);
        }

        private bool RouteProcessReady(RouteProcess process) => RouteProcessMatches(process) && RouteProcessDependenciesReady(process);

        private ExecutionGrant ResolveCapacityRouteTransitionGrant(RouteProcess process) =>
            ResolveExecutionGrant(process.Claim.Owner, process.Claim.Acquisition, process.Claim.Domains,
                () => CapacityRouteOwnership(process) && CapacityRoutePermissions(process)
                    && process.Permission == RoutePermissionKey(process.Owner) && process.ResourceIssue == null
                    && (process.ApplicationCurrent == null || process.ApplicationCurrent())
                    && (!process.ResourceApplied || CapacityRouteEmpty(process)), process.Claim);

        // Capture before the purchase; consume only its returned verified result, after
        // the runner invalidates Support. No owner, selection or attempt budget changes.
        internal Action<FiniteActionResult> CaptureRouteSupportGrowthApproval()
        {
            var process = _manualTitanProcess;
            if (process?.CapacityTransition != true || !RouteProcessReady(process) || process.Claim.SetupReleased) return null;
            var claim = process.Claim; var setup = process.Setup;
            var transition = process.TransitionGrant; var combat = process.CombatGrant;
            if (!ValidateExecutionGrant(transition, claim.Domains)
                || combat != null && !ValidateExecutionGrant(combat, claim.Domains)) return null;
            var owner = claim.Owner; var acquisition = claim.Acquisition; var domains = claim.Domains; var routeOwner = process.Owner;
            var permission = process.Permission;
            var current = claim.Current; var policy = claim.ResourcePolicy;
            var settings = Settings; var generation = _executionGeneration;
            var revisions = (long[])_executionDomainRevisions.Clone();
            var conflicts = SelectedSetupConflictVersion(Character);
            var allocations = ResourceWriter.AllocatedResourceKey();
            var consumed = false;
            return result => {
                if (consumed) return;
                consumed = true;
                if (result?.Invoked != true || !result.Verified) return;
                Func<bool> stillOwned = () => ReferenceEquals(_manualTitanProcess, process)
                    && ReferenceEquals(process.Claim, claim) && ReferenceEquals(process.Setup, setup)
                    && ReferenceEquals(claim.Setup, setup) && ReferenceEquals(claim.Current, current)
                    && ReferenceEquals(claim.ResourcePolicy, policy) && !claim.SetupReleased
                    && claim.Owner == owner && claim.Acquisition == acquisition && claim.Domains == domains && process.Owner == routeOwner
                    && ReferenceEquals(process.TransitionGrant, transition) && ReferenceEquals(process.CombatGrant, combat)
                    && ReferenceEquals(Settings, settings) && generation == _executionGeneration
                    && SelectedSetupConflictVersion(Character) == conflicts
                    && Enumerable.Range(0, revisions.Length).All(i => _executionDomainRevisions[i]
                        == revisions[i] + ((1 << i) == (int)ExecutionDomain.Support ? 1 : 0))
                    && process.Permission == permission && permission == RoutePermissionKey(routeOwner) && RouteProcessReady(process)
                    && ResourceWriter.AllocatedResourceKey() == allocations;
                var nextTransition = ResolveCapacityRouteTransitionGrant(process);
                var nextCombat = combat == null ? null : ResolveExecutionGrant(owner, acquisition, domains,
                    () => RouteProcessDependenciesReady(process) && process.Permission == RoutePermissionKey(process.Owner), claim);
                if (!VerifyExecutionPrerequisites(nextTransition, domains, stillOwned)
                    || nextCombat != null && !VerifyExecutionPrerequisites(nextCombat, domains, stillOwned)) return;
                process.TransitionGrant = nextTransition;
                process.CombatGrant = nextCombat;
            };
        }

        private ExecutionGrant RouteProcessGrant(RouteProcess process, bool retirement = false)
        {
            if (process?.BorrowedPreparation == true && retirement)
                return ResolveExecutionGrantCore(process.Claim.Owner, process.Claim.Acquisition,
                    ExecutionDomain.Route | ExecutionDomain.NativeSettings,
                    () => RouteProcessMatches(process) && ExecutionClaimSetupOwned(process.Claim)
                        && ReferenceEquals(FightPreparationClaim, process.Claim) && ReferenceEquals(FightPreparationSetup, process.Setup)
                        && (_fightPreparationAttempt?.Process == null || CapacityRouteOwnership(_fightPreparationAttempt.Process)
                            && (!_fightPreparationAttempt.Process.ResourceApplied || CapacityRouteEmpty(_fightPreparationAttempt.Process))),
                    null, new[] { process.Claim, process.SettingsClaim }, true);
            if (process?.CapacityTransition == true)
            {
                if (retirement)
                {
                    if (!RouteProcessMatches(process) || !ExecutionClaimSetupOwned(process.Claim) || !CapacityRouteOwnership(process)
                        || process.ResourceApplied && !CapacityRouteEmpty(process)) return null;
                    return ResolveExecutionRetirementGrant(process.Claim, process.Claim.Domains & (ExecutionDomain.Route | ExecutionDomain.NativeSettings),
                        () => RouteProcessMatches(process) && CapacityRouteOwnership(process) && (!process.ResourceApplied || CapacityRouteEmpty(process)));
                }
                if (!RouteProcessReady(process)) return null;
                if (process.CombatGrant == null)
                    process.CombatGrant = ResolveExecutionGrant(process.Claim.Owner, process.Claim.Acquisition, process.Claim.Domains,
                        () => RouteProcessDependenciesReady(process) && process.Permission == RoutePermissionKey(process.Owner), process.Claim);
                return process.CombatGrant;
            }
            if (!RouteProcessReady(process)) return null;
            if (process.BorrowedPreparation && !retirement)
                return ResolveExecutionGrant(process.Claim.Owner, process.Claim.Acquisition,
                    // Boss effects depend on the prep's resource hold but do not mutate
                    // those lanes or invalidate its acquired capacity revisions.
                    (process.Claim.Domains & ~(ExecutionDomain)7) | ExecutionDomain.NativeSettings,
                    () => RouteProcessReady(process) && ReferenceEquals(FightPreparationClaim, process.Claim)
                        && ReferenceEquals(FightPreparationSetup, process.Setup) && ReferenceEquals(process, _bossPushProcess),
                    resolvedClaims: new[] { process.Claim, process.SettingsClaim });
            if (retirement)
                return ResolveExecutionRetirementGrant(process.Claim, process.Claim.Domains & (ExecutionDomain.Route | ExecutionDomain.NativeSettings),
                    () => RouteProcessReady(process));
            return ResolveExecutionGrant(process.Claim.Owner, process.Claim.Acquisition, process.Claim.Domains,
                () => RouteProcessReady(process) && process.Permission == RoutePermissionKey(process.Owner ?? process.Claim.Owner), process.Claim);
        }

        private void ObserveRouteProcess(RouteProcess process)
        {
            // Only the typed actuator publishes its own verified route/settings effects.
            process.Zone = Character.adventure.zone;
            process.AutoAttack = Character.adventure.autoattacking;
            process.BeastMode = Character.adventure.beastModeOn;
        }

        private bool EndRouteProcess(RouteProcess process)
        {
            if (process == null) return true;
            if (!ReferenceEquals(reference, this) || _unloaded) return true;
            if (!process.Claim.Epoch.Matches()) return EndExecutionClaim(process.Claim, false);
            if (Character.bossController.isFighting || Character.bossController.nukeBoss) return false;
            if (process.CapacityTransition && (process.ResourceRecall != GearRecallFootprint.None || process.Owner == "fight-preparation"))
                return EndCapacityRouteProcess(process);
            if (process.Claim.Ended) return EndExecutionClaim(process.Claim, _executionLifecycle == ExecutionLifecycle.Running && !_routeHandoffPending);
            if (!RouteProcessMatches(process) || !ExecutionClaimSetupOwned(process.Claim))
            {
                RecordSelectedSetupConflict(Character, Character.stats.rebirthNumber, "route", "Temporary route changed externally; prerequisites retained.");
                if (process.BorrowedPreparation) return false;
                EndExecutionClaim(process.Claim, false);
                return true;
            }
            var grant = RouteProcessGrant(process, true);
            if (Character.adventure.zone != -1
                && !_combManager.MoveToZone(-1, grant, () => ObserveRouteProcess(process))) return false;
            if (Character.bossController.isFighting || Character.bossController.nukeBoss) return false;
            ObserveOrdinaryRoute();
            if (process.BorrowedPreparation) return true;
            return EndExecutionClaim(process.Claim, _executionLifecycle == ExecutionLifecycle.Running && !_routeHandoffPending);
        }

        private bool EndCapacityRouteProcess(RouteProcess process)
        {
            // Only our verified terminal path can make an ended same-epoch hold
            // idempotent. An externally ended claim grants no native cleanup rights.
            if (process.ResourceHoldEnded && process.Claim.Ended)
                return true;
            if (!CapacityRouteOwnership(process) || !ExecutionClaimSetupOwned(process.Claim)
                || !RouteProcessMatches(process) || process.ResourceApplied && !CapacityRouteEmpty(process)
                || SelectedSetupConflicts.TryGetValue(Character, out var unresolved) && unresolved.Rebirth == Character.stats.rebirthNumber
                    && (unresolved.Gear != null || unresolved.Support != null || unresolved.Route != null))
            {
                process.ResourceIssue = "Capacity transition authority changed; native setup/resources retained and attempt cancelled.";
                if (process.Owner == "fight-preparation") return false;
                EndExecutionClaim(process.Claim, false);
                return process.Claim.Ended;
            }
            if (Character.bossController.isFighting || Character.bossController.nukeBoss) return false;
            if (!process.ResourceAttempted && !process.ResourceApplied && !process.Applied) return EndExecutionClaim(process.Claim, false);
            if (Character.adventure.zone != -1 && !_combManager.MoveToZone(-1, RouteProcessGrant(process, true), () => ObserveRouteProcess(process))) return false;
            if (Character.bossController.isFighting || Character.bossController.nukeBoss) return false;
            if (!CapacityRoutePermissions(process) && process.ResourceIssue == null)
            {
                if (process.Owner == "fight-preparation"
                    && (_resourceSetupExecutor.Application.BlockedLanes & (int)process.ResourceRecall) == 0
                    && !ExecutionScopeNeedsReview(ExecutionDomain.Gear | ExecutionDomain.Support | ExecutionDomain.Route | ExecutionDomain.NativeSettings)
                    && (_executionLifecycle == ExecutionLifecycle.Running || _executionLifecycle == ExecutionLifecycle.Quiescing))
                {
                    // Withdraw exact owned authority without applying or funding the expired policy.
                    LimitExecutionCapacityFunding((int)process.ResourceRecall);
                    if (!EndExecutionClaim(process.Claim, false)) return false;
                    process.ResourceHoldEnded = true;
                    return true;
                }
                process.ResourceIssue = "Capacity lane permission/revision changed; native setup retained.";
            }
            if (process.ResourceIssue != null)
            {
                if (process.Owner == "fight-preparation") return false;
                EndExecutionClaim(process.Claim, false);
                return process.Claim.Ended;
            }
            if (_executionLifecycle == ExecutionLifecycle.Quiescing)
            {
                // Safe terminal shutdown withdraws authority, not native setup.
                // Keep delayed-clamp semantics without installing or funding B.
                LimitExecutionCapacityFunding((int)process.ResourceRecall);
                if (!EndExecutionClaim(process.Claim, false)) return false;
                process.ResourceHoldEnded = true;
                return true;
            }
            if (_executionLifecycle != ExecutionLifecycle.Running) return false;
            process.Claim.SetupReleased = true;
            var baseline = _executionBaseline;
            if (baseline?.Authorized?.Invoke() != true)
                baseline = FocusSetupCurrent(_focusSetupSelection) ? _focusSetupSelection.Desired
                    : ManualAdventureBaselineAuthorized() ? _manualAdventureBaseline : null;
            if (!_routeHandoffPending && baseline?.Authorized?.Invoke() == true)
            {
                var successor = baseline.Copy(); var current = baseline.Authorized;
                successor.ResolvedClaims = new[] { process.Claim };
                successor.Recall = process.ResourceRecall;
                successor.Authorized = () => current() && !Character.bossController.isFighting && !Character.bossController.nukeBoss
                    && CapacityRouteOwnership(process) && CapacityRoutePermissions(process) && CapacityRouteEmpty(process);
                successor.BoundaryAuthorized = () => !Character.bossController.isFighting && !Character.bossController.nukeBoss
                    && CapacityRouteOwnership(process) && CapacityRouteEmpty(process);
                if ((process.Claim.Domains & ExecutionDomain.Support) != 0 && (baseline.ControlsSupport || baseline.ChangeSupport)) successor.ChangeSupport = true;
                else if (!successor.ChangeSupport) successor.Support = CaptureSupportState().Selection;
                BeginPresetSupportAcquisition(successor.Owner, successor.Acquisition);
                try { ApplySelectedSetup(successor); }
                catch (SelectedSetupException error) when (error.Outcome.KnownProgressStillOwned()) { return false; }
            }
            LimitExecutionCapacityFunding((int)process.ResourceRecall);
            if (!EndExecutionClaim(process.Claim, false)) return false;
            process.ResourceHoldEnded = true;
            ObserveOrdinaryRoute();
            if (!_routeHandoffPending)
            {
                _resourceReconciliationPending = true;
                // Prep clears its lifecycle references before ordinary dispatch can admit
                // another entry. Do not recursively publish a new attempt during release.
                if (process.Owner != "fight-preparation") ReconcileCustomResources();
            }
            return true;
        }

        private bool _routeHandoffPending;

        private bool RetireRoutesForSuccessor()
        {
            if (Character.bossController.isFighting || Character.bossController.nukeBoss) return false;
            _routeHandoffPending = true;
            try
            {
                CancelCustomTitanGoldKill("temporary successor");
                CancelCustomTitanFightKill("temporary successor");
                CancelCustomAdventureGoldPush("temporary successor");
                PauseMaxItopodPush();
                ResetManualAdventureSetup();
                return !_executionClaims.Any(x => !x.Ended && (x.Domains & (ExecutionDomain.Gear | ExecutionDomain.Support | ExecutionDomain.Route)) != 0);
            }
            finally { _routeHandoffPending = false; }
        }

        internal ExecutionGrant ObserveRouteGrant(int zone)
        {
            // First-session observation is current native state, not an imported cleanup claim.
            if (!_ordinaryRouteZone.HasValue) ObserveOrdinaryRoute();
            var settings = Settings;
            var mode = settings.CustomAdventureMode; var manual = settings.ManualAdventureEnabled;
            var enabled = settings.CustomAdventureEnabled;
            var gear = LoadoutManager.CaptureExactLoadout(); var support = CaptureSupportState().StableKey;
            var objects = CombatPrepAdventureRouteObjects();
            return ResolveExecutionGrant("adventure-route", zone.ToString(), ExecutionDomain.Route | ExecutionDomain.NativeSettings,
                () => !Character.bossController.isFighting && !Character.bossController.nukeBoss
                    && !ExecutionScopeNeedsReview(ExecutionDomain.Route | ExecutionDomain.NativeSettings)
                    && ReferenceEquals(settings, Settings) && mode == settings.CustomAdventureMode
                    && manual == settings.ManualAdventureEnabled && enabled == settings.CustomAdventureEnabled
                    && _ordinaryRouteZone == Character.adventure.zone && _ordinaryRouteIdle == Character.adventure.autoattacking
                    && _ordinaryRouteBeast == Character.adventure.beastModeOn && _ordinaryRouteObjects != null
                    && !CombatPrepAdventureRouteObjects().Where((x, i) => !ReferenceEquals(x, _ordinaryRouteObjects[i])).Any()
                    && LoadoutManager.ExactLoadoutMatches(gear) && CaptureSupportState().StableKey == support
                    && !CombatPrepAdventureRouteObjects().Where((x, i) => !ReferenceEquals(x, objects[i])).Any());
        }

        internal bool RouteResourcesStillOwned(Dictionary<ResourceTarget, long[]> before, object[] objects)
        {
            var lanes = 0;
            try
            {
                var current = ResourceWriter.CaptureResourceContext(Character);
                if (current.Length != objects.Length || current.Where((x, i) => !ReferenceEquals(x, objects[i])).Any()) lanes = 7;
                else
                {
                    var observed = ResourceWriter.CaptureAllocations(Character);
                    lanes = before.Where(pair => pair.Value == null ? observed[pair.Key] != null
                        : observed[pair.Key] == null || !pair.Value.SequenceEqual(observed[pair.Key]))
                        .Aggregate(0, (mask, pair) => mask | (1 << ResourceWriter.Lane(pair.Key)));
                }
            }
            catch { lanes = 7; }
            if (lanes == 0) return true;
            _resourceSetupExecutor.Application.RecordUnresolved(Character, new ResourceMutationResult {
                Disposition = MutationDisposition.ForeignConflict, AffectedLanes = lanes,
                Reason = "Resource model or bars changed across a route callback." });
            return false;
        }

        private int? _manualAdventureAppliedLoadout;
        private DateTime _manualAdventureGearRetryAt;
        private bool _manualAdventureWasEnabled;
        private int? _manualAdventureOverlayRouteZone;
        private RouteProcess _delegatedManualAdventureProcess;
        private int? _ordinaryRouteZone;
        private int _ordinaryRouteStart, _ordinaryRouteEnd;
        private bool _ordinaryRouteIdle, _ordinaryRouteBeast;
        private bool _ordinaryRouteActuated;
        private LoadoutManager.SnapshotEpoch _ordinaryRouteEpoch;
        private object[] _ordinaryRouteObjects;

        internal void ObserveOrdinaryRoute()
        {
            // Observation/settings-only callbacks do not acquire an existing route.
            var actuated = _ordinaryRouteEpoch?.Matches() == true
                && (_ordinaryRouteActuated || _ordinaryRouteZone != Character.adventure.zone);
            _ordinaryRouteZone = Character.adventure.zone;
            _ordinaryRouteIdle = Character.adventure.autoattacking;
            _ordinaryRouteBeast = Character.adventure.beastModeOn;
            _ordinaryRouteStart = Character.adventure.itopodStart;
            _ordinaryRouteEnd = Character.adventure.itopodEnd;
            _ordinaryRouteObjects = CombatPrepAdventureRouteObjects();
            _ordinaryRouteEpoch = new LoadoutManager.SnapshotEpoch();
            _ordinaryRouteActuated = actuated;
        }

        private void CustomAdventureTick()
        {
            if (!ReferenceEquals(reference, this) || _unloaded) return;
            try
            {
                ObserveManualTitanOpportunities();
                if (Character.bossController.isFighting || Character.bossController.nukeBoss) return;
                // Restore idle once on explicit disable; later routes may reclaim manual combat.
                var manualAdventureEnabled = Settings.ManualAdventureEnabled;
                if (_manualAdventureWasEnabled && !manualAdventureEnabled && !Character.adventure.autoattacking)
                {
                    var process = _maxItopodProcess ?? _delegatedManualAdventureProcess;
                    _combManager.SetIdleAttack(true, process == null ? ObserveRouteGrant(-1) : RouteProcessGrant(process, true),
                        process == null ? (Action)null : () => ObserveRouteProcess(process));
                }
                _manualAdventureWasEnabled = manualAdventureEnabled;

                ObserveCustomRebirthEpoch();
                if (ServiceCustomAdventureGoldPushCleanup()) return;
                if (ServiceCustomTitanRestoration()) return;
                SynchronizeMaxItopodAdventureMode();
                ObserveMaxItopodPushDeaths();
                if (TryRouteManualTitan())
                {
                    PauseMaxItopodPush();
                    return;
                }
                if (CombatPrepActive)
                {
                    RouteCombatPrepAdventure();
                    return;
                }
                if (TryRoutePriorityTitan())
                {
                    ResetManualAdventureSetup();
                    PauseMaxItopodPush();
                    return;
                }

                try
                {
                    if (TryRouteMaxItopodPush())
                        return;
                }
                catch
                {
                    CancelMaxItopodPush();
                    throw;
                }

                var plan = Settings.UseCustomRebirthPlan && CustomPlanStore != null ? LoadRuntimeCustomPlan() : null;
                if (!Settings.ManualAdventureEnabled && GlobalPresetOwnsActivity(plan) && TryRouteGlobalPresetManualAdventure(plan))
                    return;

                if (TryRouteManualAdventure())
                    return;

                if (GlobalPresetOwnsActivity(plan))
                {
                    ResetManualAdventureSetup();
                    return;
                }

                TryRouteCustomAdventureGoal();
            }
            catch (Exception e)
            {
                LogError("CustomAdventureTick", e);
            }
        }

        private bool TryRoutePriorityTitan()
        {
            if (Character.bossController.isFighting || Character.bossController.nukeBoss) return false;
            if (_customTitanGoldActive && _customTitanGoldIndex >= 0
                && ManualTitanBlocksZone(ZoneHelpers.TitanZones[_customTitanGoldIndex]))
                CancelCustomTitanGoldKill("target reserved for manual titan rule");
            if (_customTitanFightActive && _customTitanFightIndex >= 0
                && ManualTitanBlocksZone(ZoneHelpers.TitanZones[_customTitanFightIndex]))
                CancelCustomTitanFightKill("target reserved for manual titan rule");
            var plan = Settings.UseCustomRebirthPlan && CustomPlanStore != null ? LoadRuntimeCustomPlan() : null;
            if (CustomGoalBlocksAdventureGoldPush(plan, out var blockedReason))
            {
                CancelCustomTitanGoldKill(blockedReason);
                CancelCustomTitanFightKill(blockedReason);
                return false;
            }

            // Gold selects before retiring any current temporary prerequisites.
            if ((_customTitanGoldActive || FindCustomTitanGoldPreparation() >= 0) && TryRouteCustomTitanGoldKill())
                return true;

            return TryRouteCustomTitanFightKill();
        }

        private bool TryRouteManualAdventure()
        {
            if (!Settings.ManualAdventureEnabled)
            {
                if (!Settings.CustomAdventureEnabled) ResetManualAdventureSetup();
                return false;
            }

            if (_questManager != null && _questManager.IsQuesting() >= 0)
            {
                ResetManualAdventureSetup();
                SetCustomAutomationStatus("Manual adventure paused: questing is active.");
                return true;
            }

            var plan = Settings.UseCustomRebirthPlan && CustomPlanStore != null ? LoadRuntimeCustomPlan() : null;
            if (CustomGoalBlocksAdventureGoldPush(plan, out var blockedReason))
            {
                ResetManualAdventureSetup();
                SetCustomAutomationStatus($"Manual adventure paused: {blockedReason}.");
                return true;
            }

            return RouteManualAdventureZone(Settings.ManualAdventureZone, Settings.ManualAdventureAllowFallback, null);
        }

        private bool RouteManualAdventureZone(int targetZone, bool allowFallback, string note)
        {
            if (Character.bossController.isFighting || Character.bossController.nukeBoss) return false;
            CancelCustomAdventureGoldPush("manual adventure fighting");
            CancelCustomTitanGoldKill("manual adventure fighting");
            var routeZone = targetZone;
            var fallbackNote = string.IsNullOrWhiteSpace(note) ? string.Empty : note + ". ";
            if (!CombatManager.IsZoneUnlocked(routeZone))
            {
                if (allowFallback)
                {
                    var fallbackZone = ZoneHelpers.GetMaxReachableZone(false);
                    if (fallbackZone >= 0)
                    {
                        routeZone = fallbackZone;
                        fallbackNote += $"{ZoneName(targetZone)} locked; fallback to ";
                    }
                }

                if (routeZone == targetZone)
                {
                    ResetManualAdventureSetup();
                    SetCustomAutomationStatus($"Manual adventure: {ZoneName(targetZone)} is locked; waiting in Safe Zone.");
                    _combManager.MoveToZone(-1);
                    return true;
                }
            }

            if (ManualTitanBlocksZone(routeZone))
            {
                ResetManualAdventureSetup();
                _combManager.MoveToZone(-1);
                SetCustomAutomationStatus("Manual adventure: selected titan is reserved for the bounded titan rule. " + _manualTitanStatus);
                return true;
            }

            _manualAdventureSetupActive = true;
            if (!TryApplyManualAdventureLoadout()) return true;
            var diggerMessage = ApplyManualAdventureDiggers(routeZone);
            if (Settings.ManualAdventureEnabled && diggerMessage != null)
            {
                SetCustomAutomationStatus("Manual adventure: " + diggerMessage);
                return true; // No dependent fight before shared support verification.
            }
            var diggerStatus = string.IsNullOrWhiteSpace(diggerMessage) ? string.Empty : $" {diggerMessage}.";
            var bossMode = Settings.ManualAdventureBossOnly ? " boss-only" : string.Empty;
            SetCustomAutomationStatus($"Manual adventure: {fallbackNote}manually fighting{bossMode} {ZoneName(routeZone)}.{diggerStatus}");
            _combManager.ManualZone(
                routeZone,
                Settings.ManualAdventureBossOnly,
                Settings.ManualAdventureRecoverHealth,
                Settings.ManualAdventurePrecastBuffs,
                Settings.ManualAdventureFastCombat,
                Settings.ManualAdventureBeastMode,
                grant: Settings.ManualAdventureEnabled ? ObserveRouteGrant(routeZone) : RouteProcessGrant(_delegatedManualAdventureProcess),
                observe: Settings.ManualAdventureEnabled ? (Action)null : () => ObserveRouteProcess(_delegatedManualAdventureProcess));
            if (Settings.ManualAdventureEnabled)
                _manualAdventureOverlayRouteZone = Character.adventure.zone == routeZone ? (int?)routeZone : null;
            return true;
        }

        private void ResetManualAdventureSetup()
        {
            // Removing intent is independent of native retirement. Keep the exact
            // process/evidence on failure, but never advertise a removed task as active.
            _globalPresetManualAdventureActive = false;
            if (_manualAdventureSetupActive)
                InvalidateInlineStageApplication();
            _manualAdventureSetupActive = false;
            if (!EndRouteProcess(_delegatedManualAdventureProcess)) return;
            _delegatedManualAdventureProcess = null;
            ResetManualAdventureOverlay();
            _manualAdventureAppliedLoadout = null;
            _manualAdventureGearRetryAt = DateTime.MinValue;
            if (_customEquipmentOwner != null && _customEquipmentOwner.StartsWith("manual-adventure:", StringComparison.Ordinal))
            {
                ResetCustomEquipmentSetup();
                InvalidateInlineStageApplication();
            }
        }

        internal bool CompleteDelegatedManualRouteRetirement()
        {
            var process = _delegatedManualAdventureProcess;
            if (process == null) return true;
            if (_globalPresetManualAdventureActive || _manualAdventureSetupActive
                || _delegatedManualRoute?.Authorized?.Invoke() != true
                || Character.adventure.zone != -1 || !ExecutionClaimCurrent(process.Claim)
                || !ExecutionClaimSetupOwned(process.Claim) || !process.Setup.MaterialValid()
                || process.Permission != RoutePermissionKey(process.Owner)
                || process.AutoAttack != Character.adventure.autoattacking || process.BeastMode != Character.adventure.beastModeOn
                || CombatPrepAdventureRouteObjects().Where((x, i) => !ReferenceEquals(x, process.Objects[i])).Any()) return false;
            process.Zone = -1;
            // Core already established retirement with the retained exact witness.
            // Its ordinary reconciliation owns CURRENT successor application.
            if (!EndExecutionClaim(process.Claim, false)) return false;
            _delegatedManualAdventureProcess = null;
            return true;
        }

        private bool TryApplyManualAdventureLoadout()
        {
            if (Settings.ManualAdventureEnabled) return TryApplyManualAdventureOverlay();
            var selection = Settings.ManualAdventureLoadout;
            if (_delegatedManualAdventureProcess?.Applied == true)
                return RouteProcessReady(_delegatedManualAdventureProcess);
            if (_delegatedManualAdventureProcess == null)
            {
                var gear = selection < 0 ? LoadoutManager.CaptureExactLoadout()
                    : SelectSetupGear(GearOptimizationGoal.Fight, (FightScoreMode)selection, true);
                _delegatedManualAdventureProcess = BeginRouteProcess("delegated-manual-adventure", gear, selection >= 0,
                    selectedSupport: SelectFocusSupport(SetupForManualAdventure()));
            }
            if (!ApplyRouteProcess(_delegatedManualAdventureProcess)) return false;
            _manualAdventureAppliedLoadout = selection < 0 ? (int?)null : selection;
            _manualAdventureGearRetryAt = DateTime.MinValue;
            return true;
        }

        private string ApplyManualAdventureDiggers(int routeZone)
        {
            if (Settings.ManualAdventureEnabled) return ApplyManualAdventureOverlaySupport();
            return RouteProcessReady(_delegatedManualAdventureProcess) ? null : "selected setup is unverified";
        }

        private void SnipeZone()
        {
            try
            {
                if (!LegacyAutomationEnabled || !Settings.GlobalEnabled)
                    return;

                //If tm ever drops to 0, reset our gold loadout stuff
                if (Character.machine.realBaseGold == 0.0 && !Settings.DoGoldSwap)
                {
                    Log("Time Machine Gold is 0. Lets reset gold snipe zone.");
                    Settings.DoGoldSwap = true;
                    Settings.TitanMoneyDone = new bool[ZoneHelpers.TitanZones.Length];
                }

                //This logic should trigger only if Time Machine is ready
                if (Character.buttons.brokenTimeMachine.interactable)
                {
                    if (Character.machine.realBaseGold == 0.0)
                    {
                        _combManager.ManualZone(0, false, false, false, true, false);
                        return;
                    }
                }

                var questZone = _questManager.IsQuesting();
                if (!Settings.CombatEnabled || Settings.AdventureTargetITOPOD || !ZoneHelpers.ZoneIsTitan(Settings.SnipeZone) ||
                    ZoneHelpers.ZoneIsTitan(Settings.SnipeZone) &&
                    !ZoneHelpers.TitanSpawningSoon(Array.IndexOf(ZoneHelpers.TitanZones, Settings.SnipeZone)))
                {
                    if (questZone > 0)
                    {
                        if (Settings.QuestCombatMode == 0)
                        {
                            _combManager.ManualZone(questZone, false, false, false, Settings.QuestFastCombat, Settings.BeastMode);
                        }
                        else
                        {
                            _combManager.IdleZone(questZone, false, false);
                        }

                        return;
                    }
                }

                if (!Settings.CombatEnabled)
                    return;

                var tempZone = Settings.AdventureTargetITOPOD ? 1000 : Settings.SnipeZone;
                if (tempZone < 1000)
                {
                    if (!CombatManager.IsZoneUnlocked(Settings.SnipeZone))
                    {
                        tempZone = Settings.AllowZoneFallback ? ZoneHelpers.GetMaxReachableZone(false) : 1000;
                    }
                    else
                    {
                        if (ZoneHelpers.ZoneIsTitan(Settings.SnipeZone) && !ZoneHelpers.TitanSpawningSoon(Array.IndexOf(ZoneHelpers.TitanZones, Settings.SnipeZone)))
                        {
                            tempZone = 1000;
                        }
                    }
                }

                if (tempZone >= 1000)
                {
                    if (Settings.ITOPODCombatMode == 0)
                    {
                        _combManager.ManualZone(tempZone, false, Settings.ITOPODRecoverHP, Settings.ITOPODPrecastBuffs, Settings.ITOPODFastCombat, Settings.ITOPODBeastMode);
                    }
                    else
                    {
                        _combManager.IdleZone(tempZone, false, Settings.ITOPODRecoverHP);
                    }

                    return;
                }

                if (Settings.CombatMode == 0)
                {
                    _combManager.ManualZone(tempZone, Settings.SnipeBossOnly, Settings.RecoverHealth, Settings.PrecastBuffs, Settings.FastCombat, Settings.BeastMode);
                }
                else
                {
                    _combManager.IdleZone(tempZone, Settings.SnipeBossOnly, Settings.RecoverHealth);
                }
            }
            catch (Exception e)
            {
                LogError("SnipeZone", e);
            }
        }

        private void MoveToITOPOD()
        {
            if (!LegacyAutomationEnabled || !Settings.GlobalEnabled)
                return;

            if (_questManager.IsQuesting() >= 0)
                return;

            if (Settings.CombatEnabled)
                return;

            if (Settings.DoGoldSwap)
                return;

            //If we're not in ITOPOD, move there if its set
            if (Character.adventureController.zone >= 1000 || !Settings.AutoQuestITOPOD) return;
            Log($"Moving to ITOPOD to idle.");
            _combManager.MoveToZone(1000);
        }

        public void SetResnipe()
        {
            try
            {
                if (!LegacyAutomationEnabled)
                    return;

                if (Settings.ResnipeTime == 0 && !Settings.GoldCBlockMode) return;

                if (Settings.GoldCBlockMode)
                {
                    var furthest = ZoneHelpers.GetMaxReachableZone(false);
                    if (furthest > _furthestZone)
                    {
                        Settings.DoGoldSwap = true;
                        _furthestZone = furthest;
                    }

                    return;
                }

                if (Math.Abs(Character.rebirthTime.totalseconds - Settings.ResnipeTime) <= 1)
                {
                    Settings.DoGoldSwap = true;
                }
            }
            catch (Exception e)
            {
                LogError("SetResnipe", e);
            }
        }
    }
}
