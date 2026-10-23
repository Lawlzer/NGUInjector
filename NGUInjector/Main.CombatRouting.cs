using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using SimpleJSON;
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
            if (capacityTransition && !owner.StartsWith("manual-titan:", StringComparison.Ordinal))
                throw new InvalidOperationException("Only manual titan may opt into the controlled-empty capacity policy.");
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
                process.ResourceObjects = ResourceWriter.CaptureResourceContext(Character).Concat(SupportObjects()).Concat(CombatPrepAdventureRouteObjects()).ToArray();
                process.ProtectedResources = ResourceWriter.CaptureAllocations(Character).Where(pair =>
                    pair.Key == ResourceTarget.BasicAttack || pair.Key == ResourceTarget.BasicDefense
                    || ResourceWriter.Lane(pair.Key) == 2
                    || ResourceWriter.Lane(pair.Key) == 0 && !Settings.ManageEnergy
                    || ResourceWriter.Lane(pair.Key) == 1 && !Settings.ManageMagic
                    || (_resourceSetupExecutor.Application.BlockedLanes & (1 << ResourceWriter.Lane(pair.Key))) != 0)
                    .ToDictionary(x => x.Key, x => x.Value?.ToArray());
                process.ResourceContextCurrent = () => ReferenceEquals(settings, Settings) && stage == _customStageKey
                    && generation == _protectedGeneration && entry == _protectedScheduler?.Entry;
            }
            process.Permission = RoutePermissionKey(owner);
            process.Claim = BeginExecutionClaim(owner, acquisition ?? _routeRebindingAcquisition ?? Guid.NewGuid().ToString("N"),
                domains,
                () => !process.Admitted || ReferenceEquals(process, _manualTitanProcess)
                    || ReferenceEquals(process, _customTitanGoldProcess) || ReferenceEquals(process, _customTitanFightProcess)
                    || ReferenceEquals(process, _customAdventureGoldProcess) || ReferenceEquals(process, _maxItopodProcess)
                    || ReferenceEquals(process, _delegatedManualAdventureProcess) || ReferenceEquals(process, _bossPushProcess));
            // Capture the predecessor before claim.Setup resolves to the not-yet-applied incoming setup.
            var entryRoute = CaptureExecutionBaselineRouteRetirement(process.Claim);
            process.Setup = new SelectedSetupRequirements { Gear = gear, ControlsGear = controlsGear,
                Recall = capacityTransition ? resourceRecall : GearRecallFootprint.None, Support = support, ChangeSupport = selectedSupport != null,
                Budget = ActivityPolicy.Current.DiggerBudgetFraction, EnforceSupportBudget = false,
                Authorized = () => ExecutionClaimCurrent(process.Claim) && process.Permission == RoutePermissionKey(owner),
                MaterialValid = () => !gear.Where((x, i) => LoadoutPresetPrimitiveKey(x.equipment) != material[i]).Any(),
                RouteRetirementAuthorized = () => RouteProcessMatches(process)
                    && (process.Applied || Character.adventure.zone == -1 || entryRoute()),
                ObserveRouteRetirement = () => process.Zone = -1 };
            if (capacityTransition && resourceRecall != GearRecallFootprint.None)
                SetExecutionResourcePolicy(process.Claim, new ResourceSetupIntent { Owner = owner,
                    Energy = (resourceRecall & GearRecallFootprint.Energy) != 0, Magic = (resourceRecall & GearRecallFootprint.Magic) != 0,
                    Desired = ResourceWriter.EmptyDesiredMap((resourceRecall & GearRecallFootprint.Energy) != 0, (resourceRecall & GearRecallFootprint.Magic) != 0),
                    Authorized = () => process.ResourceRetired && CapacityRouteOwnership(process)
                        && CapacityRoutePermissions(process) && (process.Claim.SetupReleased || process.Permission == RoutePermissionKey(owner))
                        && (process.Applied || ValidateExecutionGrant(process.TransitionGrant, process.Claim.Domains))
                        && (!process.ResourceApplied || CapacityRouteEmpty(process)) });
            if (capacityTransition) process.ResourceRevisions = (long[])_executionDomainRevisions.Clone();
            if (capacityTransition)
            {
                process.Claim.CapacityTransition = true;
                process.TransitionGrant = ResolveExecutionGrant(owner, process.Claim.Acquisition, domains,
                    () => CapacityRouteOwnership(process) && CapacityRoutePermissions(process)
                        && process.Permission == RoutePermissionKey(owner) && process.ResourceIssue == null
                        && (!process.ResourceApplied || CapacityRouteEmpty(process)), process.Claim);
                process.Setup.Authorized = () => ValidateExecutionGrant(process.TransitionGrant, process.Claim.Domains);
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
            var context = ResourceWriter.CaptureResourceContext(Character).Concat(SupportObjects()).Concat(CombatPrepAdventureRouteObjects()).ToArray();
            if (context.Length != process.ResourceObjects.Length || context.Where((x, i) => !ReferenceEquals(x, process.ResourceObjects[i])).Any()) return false;
            var current = ResourceWriter.CaptureAllocations(Character);
            return process.ProtectedResources.All(pair => pair.Value == null ? current[pair.Key] == null
                : current[pair.Key] != null && pair.Value.SequenceEqual(current[pair.Key]));
        }

        private bool CapacityRouteEmpty(RouteProcess process)
        {
            var current = ResourceWriter.CaptureAllocations(Character);
            var policy = process.Claim.ResourcePolicy;
            return policy == null ? process.ResourceRecall == GearRecallFootprint.None
                : policy.Desired.All(pair => current[pair.Key] != null && current[pair.Key].SequenceEqual(pair.Value));
        }

        private bool RouteProcessDependenciesReady(RouteProcess process) => process != null && process.Applied
            && ExecutionClaimCurrent(process.Claim) && ExecutionClaimSetupOwned(process.Claim) && process.Setup.MaterialValid()
            && (!process.Setup.ControlsGear || LoadoutManager.ExactLoadoutMatches(process.Setup.Gear))
            && ((process.Claim.Domains & ExecutionDomain.Support) == 0 || CaptureSupportState().Selection.StableKey == process.Setup.Support.StableKey)
            && (!process.CapacityTransition || process.ResourceIssue == null && process.ResourceApplied
                && CapacityRouteOwnership(process) && CapacityRoutePermissions(process) && CapacityRouteEmpty(process));

        private string RoutePermissionKey(string owner)
        {
            if (owner.StartsWith("manual-titan:", StringComparison.Ordinal))
                return Settings.ManualTitanEnabled + ":" + Settings.ManualTitanZone + ":" + Settings.ManualTitanLoadout
                    + ":" + Settings.ManualTitanRequireAT + ":" + Settings.ManualTitanMaxDeaths;
            if (owner == "boss-push") return _bossPushRunning + ":" + _bossPushIdentity;
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
            PersistRouteExecutionCommitments();
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

        private ExecutionGrant RouteProcessGrant(RouteProcess process, bool retirement = false)
        {
            if (process?.CapacityTransition == true)
            {
                if (retirement)
                {
                    if (!RouteProcessMatches(process) || !ExecutionClaimSetupOwned(process.Claim) || !CapacityRouteOwnership(process)
                        || process.ResourceApplied && !CapacityRouteEmpty(process)) return null;
                    return ResolveExecutionRetirementGrant(process.Claim, ExecutionDomain.Route | ExecutionDomain.NativeSettings,
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
                    process.Claim.Domains | ExecutionDomain.NativeSettings,
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
            var changed = process.Zone != Character.adventure.zone || process.AutoAttack != Character.adventure.autoattacking
                || process.BeastMode != Character.adventure.beastModeOn;
            process.Zone = Character.adventure.zone;
            process.AutoAttack = Character.adventure.autoattacking;
            process.BeastMode = Character.adventure.beastModeOn;
            if (changed) PersistRouteExecutionCommitments();
        }

        private bool EndRouteProcess(RouteProcess process)
        {
            if (process == null) return true;
            if (!process.Claim.Epoch.Matches()) { EndExecutionClaim(process.Claim, false); return true; }
            if (process.CapacityTransition && process.ResourceRecall != GearRecallFootprint.None) return EndCapacityRouteProcess(process);
            if (process.Claim.Ended) return EndExecutionClaim(process.Claim, _executionLifecycle == ExecutionLifecycle.Running && !_routeHandoffPending);
            if (!RouteProcessMatches(process) || !ExecutionClaimSetupOwned(process.Claim))
            {
                RecordSelectedSetupConflict(Character, Character.stats.rebirthNumber, "route", "Temporary route changed externally; prerequisites retained.");
                EndExecutionClaim(process.Claim, false);
                return true;
            }
            var grant = RouteProcessGrant(process, true);
            if (Character.bossController.isFighting || Character.bossController.nukeBoss) return false;
            if (Character.adventure.zone != -1
                && !_combManager.MoveToZone(-1, grant, () => ObserveRouteProcess(process))) return false;
            ObserveOrdinaryRoute();
            if (process.BorrowedPreparation) return true;
            return EndExecutionClaim(process.Claim, _executionLifecycle == ExecutionLifecycle.Running && !_routeHandoffPending);
        }

        private bool EndCapacityRouteProcess(RouteProcess process)
        {
            // Only our verified terminal path can make an ended same-epoch hold
            // idempotent. An externally ended claim grants no native cleanup rights.
            if (process.ResourceHoldEnded && process.Claim.Ended)
            { PersistRouteExecutionCommitments(); return true; }
            if (!CapacityRouteOwnership(process) || !ExecutionClaimSetupOwned(process.Claim)
                || !RouteProcessMatches(process) || process.ResourceApplied && !CapacityRouteEmpty(process)
                || SelectedSetupConflicts.TryGetValue(Character, out var unresolved) && unresolved.Rebirth == Character.stats.rebirthNumber
                    && (unresolved.Gear != null || unresolved.Support != null || unresolved.Route != null))
            {
                process.ResourceIssue = "Capacity transition retirement requires exact still-owned setup/resources and route; review is required.";
                return false;
            }
            if (Character.bossController.isFighting || Character.bossController.nukeBoss) return false;
            if (!process.ResourceAttempted && !process.ResourceApplied && !process.Applied) return EndExecutionClaim(process.Claim, false);
            if (Character.adventure.zone != -1 && !_combManager.MoveToZone(-1, RouteProcessGrant(process, true), () => ObserveRouteProcess(process))) return false;
            if (!CapacityRoutePermissions(process)) process.ResourceIssue = "Capacity lane permission/revision changed; explicit review is required.";
            if (process.ResourceIssue != null) return false;
            if (_executionLifecycle == ExecutionLifecycle.Quiescing)
            {
                // Safe terminal shutdown withdraws authority, not native setup.
                // Keep delayed-clamp semantics without installing or funding B.
                LimitExecutionCapacityFunding((int)process.ResourceRecall);
                if (!EndExecutionClaim(process.Claim, false)) return false;
                process.ResourceHoldEnded = true;
                PersistRouteExecutionCommitments();
                return true;
            }
            if (_executionLifecycle != ExecutionLifecycle.Running) return false;
            process.Claim.SetupReleased = true;
            PersistRouteExecutionCommitments();
            var baseline = _executionBaseline;
            if (baseline?.Authorized?.Invoke() != true)
                baseline = FocusSetupCurrent(_focusSetupSelection) ? _focusSetupSelection.Desired
                    : ManualAdventureBaselineAuthorized() ? _manualAdventureBaseline : null;
            if (!_routeHandoffPending && baseline?.Authorized?.Invoke() == true)
            {
                var successor = baseline.Copy(); var current = baseline.Authorized;
                successor.ResolvedClaims = new[] { process.Claim };
                successor.Recall = process.ResourceRecall;
                successor.Authorized = () => current() && CapacityRouteOwnership(process) && CapacityRoutePermissions(process) && CapacityRouteEmpty(process);
                successor.BoundaryAuthorized = () => CapacityRouteOwnership(process) && CapacityRouteEmpty(process);
                if ((process.Claim.Domains & ExecutionDomain.Support) != 0 && (baseline.ControlsSupport || baseline.ChangeSupport)) successor.ChangeSupport = true;
                else if (!successor.ChangeSupport) successor.Support = CaptureSupportState().Selection;
                BeginPresetSupportAcquisition(successor.Owner, successor.Acquisition);
                try { ApplySelectedSetup(successor); }
                catch (SelectedSetupException error) when (error.Outcome.KnownProgressStillOwned()) { return false; }
            }
            LimitExecutionCapacityFunding((int)process.ResourceRecall);
            if (!EndExecutionClaim(process.Claim, false)) return false;
            ObserveOrdinaryRoute();
            if (!_routeHandoffPending) { _resourceReconciliationPending = true; ReconcileCustomResources(); }
            return true;
        }

        private bool _routeHandoffPending;
        private JSONNode _importedRouteCommitments;
        private string _routeCommitmentIssue;
        private string _routeRebindingAcquisition;

        internal JSONObject ExportRouteExecutionCommitments()
        {
            if (_importedRouteCommitments != null) return JsonFiles.ParseObject(_importedRouteCommitments.ToString());
            var saved = new JSONObject(); saved["version"] = 1;
            if (_executionCapacityFundingEpoch?.Matches() == true && _executionCapacityFundingLanes != 0)
            {
                saved["capacityFundingPolicy"] = "manual-titan-clamp-safe-v1";
                saved["capacityFundingLanes"] = _executionCapacityFundingLanes;
            }
            saved["invalidEpoch"] = _manualTitanEpoch != null && !_manualTitanEpoch.Matches()
                || new[] { _manualTitanProcess, _customTitanGoldProcess, _customTitanFightProcess, _customAdventureGoldProcess,
                    _maxItopodProcess, _delegatedManualAdventureProcess, _bossPushProcess }
                    .Any(x => x?.Claim != null && !x.Claim.Epoch.Matches());
            saved["rebirth"] = Character.stats.rebirthNumber.ToString(CultureInfo.InvariantCulture);
            saved["elapsed"] = Character.rebirthTime.totalseconds;
            saved["origin"] = Character.totalPlaytime.totalseconds - Character.rebirthTime.totalseconds;
            var processes = new JSONArray();
            foreach (var process in new[] { _manualTitanProcess, _customTitanGoldProcess, _customTitanFightProcess,
                _customAdventureGoldProcess, _maxItopodProcess, _delegatedManualAdventureProcess, _bossPushProcess })
            {
                if (process?.Claim == null || process.Claim.Ended) continue;
                var row = new JSONObject(); row["owner"] = process.Owner ?? process.Claim.Owner;
                row["claimOwner"] = process.Claim.Owner;
                row["acquisition"] = process.Claim.Acquisition; row["domains"] = (int)process.Claim.Domains;
                row["permission"] = process.Permission; row["zone"] = process.Zone;
                row["idle"] = process.AutoAttack; row["beast"] = process.BeastMode;
                row["controlsGear"] = process.Setup.ControlsGear; row["changeSupport"] = process.Setup.ChangeSupport;
                if (process.CapacityTransition)
                {
                    row["resourcePolicy"] = "manual-titan-controlled-empty-v1";
                    row["resourceRecall"] = (int)process.ResourceRecall;
                    row["resourceApplied"] = process.ResourceApplied;
                    row["releasing"] = process.Claim.SetupReleased;
                    row["resourceIssue"] = process.ResourceIssue ?? "";
                }
                row["gear"] = ExportRouteGear(process.Setup.Gear, process.SelectedMaterial);
                row["beards"] = LoadoutPresetNumbers(process.Setup.Support.Beards.Select(x => (double)x));
                var diggers = new JSONArray();
                foreach (var d in process.Setup.Support.Diggers) { var item = new JSONObject(); item["id"] = d.Id; item["level"] = d.Level.ToString(CultureInfo.InvariantCulture); diggers.Add(item); }
                row["diggers"] = diggers; processes.Add(row);
            }
            saved["processes"] = processes;
            var manual = new JSONArray();
            foreach (var pair in _manualTitanSpawns ?? new Dictionary<int, ManualTitanSpawn>())
            {
                var spawn = pair.Value; var row = new JSONObject(); row["index"] = pair.Key;
                row["started"] = spawn.Started; row["disarmed"] = spawn.Disarmed; row["resetSeen"] = spawn.ResetSeen;
                row["elapsed"] = double.IsNaN(spawn.Elapsed) ? -1 : spawn.Elapsed;
                row["deaths"] = spawn.Deaths; row["limit"] = spawn.Limit; row["kills"] = spawn.Kills; row["phase"] = spawn.Phase;
                row["ownedSeconds"] = spawn.OwnedSeconds + (_manualTitanActive && pair.Key == _manualTitanIndex
                    ? Math.Max(0, (DateTime.UtcNow - _manualTitanLastTick).TotalSeconds) : 0);
                row["outcome"] = spawn.Outcome ?? ""; row["loadout"] = spawn.Loadout;
                if (spawn.CapacityTransition)
                { row["resourcePolicy"] = "manual-titan-controlled-empty-v1"; row["resourceRecall"] = (int)spawn.ResourceRecall; row["acquisition"] = spawn.Acquisition ?? ""; }
                row["gear"] = ExportRouteGear(spawn.Gear, spawn.Material); manual.Add(row);
            }
            saved["manual"] = manual; saved["manualIndex"] = _manualTitanIndex;
            saved["towerDeaths"] = _maxItopodPushRemainingDeaths; saved["towerFloor"] = _maxItopodPushFloor;
            saved["towerMode"] = _maxItopodPushFromAdventureMode; saved["towerHistory"] = _maxItopodHistoryLost ? "lost" : "continuous";
            saved["towerStart"] = _maxItopodPushSavedStart; saved["towerEnd"] = _maxItopodPushSavedEnd; saved["towerShifter"] = _maxItopodPushSavedShifter;
            saved["towerGear"] = ExportRouteGear(_maxItopodSelectedGear, _maxItopodSelectedMaterial);
            saved["goldIndex"] = _customTitanGoldIndex; saved["fightIndex"] = _customTitanFightIndex;
            saved["goldStarted"] = _customTitanGoldStartedAt.Ticks.ToString(CultureInfo.InvariantCulture);
            saved["fightStarted"] = _customTitanFightStartedAt.Ticks.ToString(CultureInfo.InvariantCulture);
            saved["goldPending"] = _customTitanGoldPendingIndex; saved["goldGear"] = _customTitanGoldUsesGoldGear;
            var spawns = new JSONArray();
            foreach (var pair in _customTitanGoldSpawns ?? new Dictionary<int, double>()) { var row = new JSONObject(); row["index"] = pair.Key; row["elapsed"] = pair.Value; spawns.Add(row); }
            saved["goldSpawns"] = spawns;
            var cooldowns = new JSONArray();
            for (var i = 0; i < (_customTitanAutokillCooldownUntil?.Length ?? 0); i++)
            { var row = new JSONObject(); row["until"] = _customTitanAutokillCooldownUntil[i].Ticks.ToString(CultureInfo.InvariantCulture); row["reason"] = _customTitanAutokillCooldownReason[i] ?? ""; cooldowns.Add(row); }
            saved["cooldowns"] = cooldowns;
            saved["adventureGoldZone"] = _customAdventureGoldPushZone; saved["adventureGoldHighest"] = _customAdventureGoldPushHighestZone;
            saved["adventureGoldEntered"] = _customAdventureGoldPushEnteredZone; saved["adventureGoldAttempted"] = _customAdventureAttemptedGoldPushZone;
            saved["bossIdentity"] = _bossPushIdentity ?? ""; saved["bossOrdinal"] = _bossPushFightOrdinal;
            saved["bossSeconds"] = _bossPushFightSecondsRemaining; saved["bossNuke"] = _bossPushNukeStarted; saved["bossStopping"] = _bossPushStopping;
            saved["bossId"] = Character.bossID;
            saved["os"] = (int)Character.wandoos98.os;
            saved["wEnergyLevel"] = Character.wandoos98.energyLevel.ToString(CultureInfo.InvariantCulture);
            saved["wMagicLevel"] = Character.wandoos98.magicLevel.ToString(CultureInfo.InvariantCulture);
            saved["wEnergyProgress"] = Character.wandoos98.energyProgress; saved["wMagicProgress"] = Character.wandoos98.magicProgress;
            saved["wBootup"] = Character.wandoos98.bootupTime.totalseconds;
            saved["atTargets"] = LoadoutPresetNumbers(Character.advancedTraining.levelTarget.Select(x => (double)x));
            var actions = new JSONArray();
            foreach (var record in FiniteActions.Records.Where(x => x.Identity.Kind.StartsWith("route-", StringComparison.Ordinal)))
            { var row = new JSONObject(); row["key"] = record.Identity.Key; row["state"] = record.State.ToString(); row["before"] = record.Before ?? ""; row["after"] = record.After ?? ""; actions.Add(row); }
            saved["actions"] = actions;
            return saved;
        }

        private JSONArray ExportRouteGear(ih[] gear, string[] material = null)
        {
            var result = new JSONArray();
            var index = 0;
            foreach (var item in gear ?? new ih[0])
            { var row = new JSONObject(); row["slot"] = item.slot; row["id"] = item.id; row["material"] = material == null ? LoadoutPresetPrimitiveKey(item.equipment) : material[index]; result.Add(row); index++; }
            return result;
        }

        internal void ImportRouteExecutionCommitments(JSONNode saved)
        {
            if (saved == null || !saved.IsObject || !EquipmentDescriptorInteger(saved["version"], 1, 1) || !saved["processes"].IsArray || !saved["manual"].IsArray)
                throw new FormatException("Invalid route execution commitments.");
            // Import is deliberately quarantined. Semantic history is not native cleanup authority.
            _importedRouteCommitments = JsonFiles.ParseObject(saved.ToString());
            _routeCommitmentIssue = "Route commitments require reviewed same-entry rebind; no acquisition or budget was renewed.";
        }

        internal bool RebindRouteExecutionCommitments()
        {
            var saved = _importedRouteCommitments;
            if (saved == null) return true;
            try
            {
                if (_executionLifecycle != ExecutionLifecycle.Running || saved["invalidEpoch"].AsBool
                    || saved["rebirth"].Value != Character.stats.rebirthNumber.ToString(CultureInfo.InvariantCulture)
                    || !FiniteCheckpointNumber(saved["origin"], false) || !FiniteCheckpointNumber(saved["elapsed"], true)
                    || Math.Abs(saved["origin"].AsDouble - (Character.totalPlaytime.totalseconds - Character.rebirthTime.totalseconds)) > 5
                    || saved["elapsed"].AsDouble > Character.rebirthTime.totalseconds + .1
                    || saved["os"].AsInt != (int)Character.wandoos98.os
                    || Character.wandoos98.energyLevel < SelectionLong(saved["wEnergyLevel"])
                    || Character.wandoos98.magicLevel < SelectionLong(saved["wMagicLevel"]))
                    throw new InvalidOperationException("Route commitment native context changed.");
                if (_executionClaims.Any(x => !x.Ended)) throw new InvalidOperationException("Retire current claims before reviewed route rebind.");
                foreach (var action in saved["actions"].Children)
                {
                    var retained = FiniteActions.Records.SingleOrDefault(x => x.Identity.Key == action["key"].Value);
                    if (action["state"].Value == "Unknown" || action["state"].Value == "Invocation" || retained == null)
                        throw new InvalidOperationException("Imported route effect evidence is uncertain or missing; invocation will not repeat.");
                }
                var rows = saved["processes"].Children.ToArray();
                var fundingLanes = 0;
                if (!string.IsNullOrEmpty(saved["capacityFundingPolicy"].Value))
                {
                    if (saved["capacityFundingPolicy"].Value != "manual-titan-clamp-safe-v1") throw new FormatException("Unsupported capacity funding policy.");
                    fundingLanes = SelectionInt(saved["capacityFundingLanes"], 1, 3);
                    foreach (var lane in new[] { 0, 1 })
                    {
                        if ((fundingLanes & (1 << lane)) == 0) continue;
                        var cap = lane == 0 ? Character.totalCapEnergy() : Character.totalCapMagic();
                        var allocations = ResourceWriter.CaptureAllocations(Character).Where(pair => pair.Value != null && ResourceWriter.Lane(pair.Key) == lane)
                            .Sum(pair => pair.Value.Sum(value => (decimal)value));
                        var total = lane == 0 ? Character.curEnergy : Character.magic.curMagic;
                        var idle = lane == 0 ? Character.idleEnergy : Character.magic.idleMagic;
                        if (allocations > cap || Math.Max(0m, (decimal)total - cap) > idle)
                            throw new InvalidOperationException("Imported capacity buffer no longer protects native allocations.");
                    }
                }
                else if (saved["capacityFundingLanes"].AsInt != 0) throw new FormatException("Capacity funding lanes require their original semantic policy.");
                if (rows.Length > 1) throw new InvalidOperationException("Overlapping imported route commitments require review.");
                var selections = rows.Select(row => {
                    var owner = row["owner"].Value;
                    if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(row["acquisition"].Value)
                        || row["claimOwner"].Value != owner
                        || row["permission"].Value != (owner == "boss-push" ? true + ":" + saved["bossIdentity"].Value : RoutePermissionKey(owner))
                        || row["zone"].AsInt != Character.adventure.zone && !(Character.adventure.zone == -1
                            && (owner.StartsWith("manual-titan:", StringComparison.Ordinal) || owner == "max-itopod"))
                        || row["idle"].AsBool != Character.adventure.autoattacking
                        || row["beast"].AsBool != Character.adventure.beastModeOn)
                        throw new InvalidOperationException("Imported route settings or acquisition changed.");
                    var gear = ImportRouteGear(row["gear"]);
                    var beards = row["beards"].Children.Select(x => SelectionInt(x, 0, 6)).ToArray();
                    var diggers = row["diggers"].Children.Select(x => new SupportDiggerLevel(SelectionInt(x["id"], 0, 11), SelectionLong(x["level"]))).ToArray();
                    var support = new SupportSelection(beards, diggers);
                    var capacity = row["resourcePolicy"].Value == "manual-titan-controlled-empty-v1";
                    var recall = row["resourceRecall"].IsNumber ? SelectionInt(row["resourceRecall"], 0, 3) : 0;
                    var domains = (ExecutionDomain)SelectionInt(row["domains"], 1, 511);
                    if (!string.IsNullOrEmpty(row["resourcePolicy"].Value) && !capacity
                        || capacity && (!owner.StartsWith("manual-titan:", StringComparison.Ordinal)
                            || domains != (ExecutionDomain.Gear | ExecutionDomain.Support | ExecutionDomain.Route | ExecutionDomain.NativeSettings | (ExecutionDomain)recall)
                            || !string.IsNullOrEmpty(row["resourceIssue"].Value)
                            || Character.adventure.zone != -1
                            || (_resourceSetupExecutor.Application.BlockedLanes & recall) != 0
                            || (recall & 1) != 0 && !Settings.ManageEnergy || (recall & 2) != 0 && !Settings.ManageMagic)
                        || !capacity && ((int)domains & (7 | (int)ExecutionDomain.Inventory)) != 0)
                        throw new InvalidOperationException("Imported capacity policy/footprint is unsupported or requires explicit native review.");
                    if (capacity && ResourceWriter.CaptureAllocations(Character).Any(pair => pair.Value != null
                        && pair.Key != ResourceTarget.BasicAttack && pair.Key != ResourceTarget.BasicDefense
                        && (recall & (1 << ResourceWriter.Lane(pair.Key))) != 0 && pair.Value.Any(value => value != 0)))
                        throw new InvalidOperationException("Imported controlled-empty policy does not match actual native resources.");
                    if (capacity)
                    {
                        var index = SelectionInt(saved["manualIndex"], 0, 11);
                        var spawn = saved["manual"].Children.SingleOrDefault(x => x["index"].AsInt == index);
                        if (owner != "manual-titan:" + index || spawn == null || spawn["resourcePolicy"].Value != row["resourcePolicy"].Value
                            || spawn["resourceRecall"].AsInt != recall || spawn["acquisition"].Value != row["acquisition"].Value)
                            throw new InvalidOperationException("Imported process and original spawn capacity commitments disagree.");
                    }
                    if (!LoadoutManager.ExactLoadoutMatches(gear) || CaptureSupportState().Selection.StableKey != support.StableKey)
                        throw new InvalidOperationException("Imported acquired native setup does not match; no replacement was selected.");
                    if (FiniteActions.Records.Any(x => x.Identity.Acquisition == row["acquisition"].Value
                        && (x.State == FiniteActionState.Invocation || x.State == FiniteActionState.Unknown)))
                        throw new InvalidOperationException("Imported finite route invocation remains uncertain.");
                    return new { Row = row, Gear = gear, Support = support };
                }).ToArray();
                var manual = new Dictionary<int, ManualTitanSpawn>();
                foreach (var row in saved["manual"].Children)
                {
                    var index = SelectionInt(row["index"], 0, ZoneHelpers.TitanZones.Length - 1);
                    var spawn = new ManualTitanSpawn { Started = row["started"].AsBool, Disarmed = row["disarmed"].AsBool,
                        ResetSeen = row["resetSeen"].AsBool, Elapsed = row["elapsed"].AsDouble, Deaths = SelectionInt(row["deaths"], 0, 1000),
                        Limit = SelectionInt(row["limit"], 0, 1000), Kills = row["kills"].AsInt, Phase = row["phase"].AsInt,
                        OwnedSeconds = row["ownedSeconds"].AsDouble, Outcome = row["outcome"].Value, Loadout = row["loadout"].AsInt,
                         Gear = row["gear"].Count == 0 ? null : ImportRouteGear(row["gear"]) };
                    if (!string.IsNullOrEmpty(row["resourcePolicy"].Value) && row["resourcePolicy"].Value != "manual-titan-controlled-empty-v1")
                        throw new FormatException("Unsupported retained manual titan resource policy.");
                    spawn.CapacityTransition = row["resourcePolicy"].Value == "manual-titan-controlled-empty-v1";
                    spawn.ResourceRecall = spawn.CapacityTransition ? (GearRecallFootprint)SelectionInt(row["resourceRecall"], 0, 3) : GearRecallFootprint.None;
                    spawn.Acquisition = spawn.CapacityTransition ? row["acquisition"].Value : null;
                    if (spawn.CapacityTransition && string.IsNullOrEmpty(spawn.Acquisition)) throw new FormatException("Missing retained capacity acquisition.");
                    if (!FiniteCheckpointNumber(row["ownedSeconds"], true)) throw new FormatException("Invalid retained owned-time budget.");
                    if (spawn.Started && (ManualTitanNativeInt("titan" + (index + 1) + "Kills") > spawn.Kills
                        || index == 4 && ManualTitanNativeInt("waldoDefeats") > spawn.Phase))
                    { spawn.Disarmed = true; spawn.Outcome = "Native target completed while imported; waiting for observed next spawn"; }
                    // A native-reference death cursor cannot cross import. An unfinished spawn
                    // remains disarmed rather than inventing continuity or refreshing its budget.
                    if (spawn.Started && !spawn.Disarmed) { spawn.Disarmed = true; spawn.Outcome = "Imported death history requires a new observed spawn"; }
                    manual.Add(index, spawn);
                }
                _manualTitanSpawns = manual; _manualTitanEpoch = new LoadoutManager.SnapshotEpoch();
                _maxItopodPushRemainingDeaths = SelectionInt(saved["towerDeaths"], 0, 1000);
                _maxItopodPushFloor = saved["towerFloor"].AsInt; _maxItopodPushFromAdventureMode = saved["towerMode"].AsBool;
                _customTitanGoldIndex = saved["goldIndex"].AsInt; _customTitanFightIndex = saved["fightIndex"].AsInt;
                _customTitanGoldStartedAt = new DateTime(SelectionLong(saved["goldStarted"]), DateTimeKind.Utc);
                _customTitanFightStartedAt = new DateTime(SelectionLong(saved["fightStarted"]), DateTimeKind.Utc);
                _customTitanGoldSpawns = saved["goldSpawns"].Children.ToDictionary(x => SelectionInt(x["index"], 0, 11), x => x["elapsed"].AsDouble);
                _customTitanGoldPendingIndex = saved["goldPending"].AsInt; _customTitanGoldUsesGoldGear = saved["goldGear"].AsBool;
                _customTitanAutokillCooldownUntil = saved["cooldowns"].Children.Select(x => new DateTime(SelectionLong(x["until"]), DateTimeKind.Utc)).ToArray();
                _customTitanAutokillCooldownReason = saved["cooldowns"].Children.Select(x => x["reason"].Value).ToArray();
                _customAdventureGoldPushZone = saved["adventureGoldZone"].AsInt; _customAdventureGoldPushHighestZone = saved["adventureGoldHighest"].AsInt;
                _customAdventureGoldPushEnteredZone = saved["adventureGoldEntered"].AsBool; _customAdventureAttemptedGoldPushZone = saved["adventureGoldAttempted"].AsInt;
                _bossPushIdentity = saved["bossIdentity"].Value; _bossPushFightOrdinal = saved["bossOrdinal"].AsInt;
                _bossPushFightSecondsRemaining = saved["bossSeconds"].AsFloat; _bossPushNukeStarted = saved["bossNuke"].AsBool; _bossPushStopping = saved["bossStopping"].AsBool;
                foreach (var selection in selections)
                {
                    var row = selection.Row; var owner = row["owner"].Value;
                    var footprint = (ExecutionDomain)SelectionInt(row["domains"], 1, 511);
                    var capacity = row["resourcePolicy"].Value == "manual-titan-controlled-empty-v1";
                    if (!capacity && (footprint & (ExecutionDomain.Energy | ExecutionDomain.Magic | ExecutionDomain.Res3 | ExecutionDomain.Inventory)) != 0)
                        throw new InvalidOperationException("Imported route footprint requires its original preparation owner.");
                    _routeRebindingAcquisition = row["acquisition"].Value;
                    var process = BeginRouteProcess(owner, selection.Gear, row["controlsGear"].AsBool,
                        footprint, row["changeSupport"].AsBool ? selection.Support : null, capacity,
                        capacity ? (GearRecallFootprint)SelectionInt(row["resourceRecall"], 0, 3) : GearRecallFootprint.None);
                    if (owner == "titan-gold") { _customTitanGoldProcess = process; _customTitanGoldActive = true; }
                    else if (owner == "titan-autokill") { _customTitanFightProcess = process; _customTitanFightActive = true; }
                    else if (owner == "adventure-gold") { _customAdventureGoldProcess = process; _customAdventureGoldPushActive = true; }
                    else if (owner == "boss-push") { _bossPushProcess = process; _bossPushRunning = true; }
                    else if (owner == "delegated-manual-adventure") _delegatedManualAdventureProcess = process;
                    else if (owner.StartsWith("manual-titan:", StringComparison.Ordinal))
                    {
                        var index = SelectionInt(saved["manualIndex"], 0, 11);
                        if (capacity && (!manual.ContainsKey(index) || manual[index].Acquisition != row["acquisition"].Value
                            || !manual[index].CapacityTransition || manual[index].ResourceRecall != process.ResourceRecall))
                            throw new InvalidOperationException("Imported process and original spawn capacity commitments disagree.");
                        manual[index].Support = selection.Support;
                        _manualTitanProcess = process; _manualTitanIndex = index; _manualTitanActive = _manualTitanRecovering = true;
                        _manualTitanCleanupReason = manual[index].Outcome; _manualTitanLastTick = DateTime.UtcNow;
                    }
                    else if (owner == "max-itopod")
                    {
                        _maxItopodProcess = process; _maxItopodPushRouting = true; _maxItopodHistoryLost = true;
                        _maxItopodPushEpoch = new LoadoutManager.SnapshotEpoch();
                        _maxItopodBorrowedModel = Character.adventure; _maxItopodBorrowedArbitrary = Character.arbitrary;
                        _maxItopodOwnedStart = Character.adventure.itopodStart; _maxItopodOwnedEnd = Character.adventure.itopodEnd;
                        _maxItopodOwnedShifter = Character.arbitrary.lazyITOPODOn;
                        // Saved native references/field values never import cleanup rights.
                        _maxItopodPushSavedStart = _maxItopodPushSavedEnd = 0;
                    }
                    else throw new InvalidOperationException("Unknown route commitment owner.");
                    process.Applied = true; process.Claim.Setup = process.Setup;
                    if (capacity) { process.ResourceRetired = process.ResourceApplied = true; process.Claim.SetupReleased = row["releasing"].AsBool; }
                }
                if (fundingLanes != 0) LimitExecutionCapacityFunding(fundingLanes);
                _importedRouteCommitments = null; _routeCommitmentIssue = null;
                if (_manualTitanRecovering) CancelManualTitan(_manualTitanCleanupReason);
                if (_maxItopodHistoryLost && _maxItopodPushRouting) CancelMaxItopodPush();
                PersistRouteExecutionCommitments();
                return true;
            }
            catch (Exception error) { _routeCommitmentIssue = "Route rebind blocked: " + error.Message; return false; }
            finally { _routeRebindingAcquisition = null; }
        }

        private ih[] ImportRouteGear(JSONNode rows)
        {
            if (!rows.IsArray || rows.Count > 100) throw new FormatException("Invalid route gear selection.");
            var available = Character.inventory.GetConvertedEquips().Concat(Character.inventory.GetConvertedInventory())
                .Where(x => x.equipment != null).GroupBy(x => x.equipment).Select(x => x.First()).ToArray();
            return rows.Children.Select(row => {
                var slot = SelectionInt(row["slot"], -100, 20000); var id = SelectionInt(row["id"], 0, int.MaxValue);
                var matches = available.Where(x => x.id == id && LoadoutPresetPrimitiveKey(x.equipment) == row["material"].Value).ToArray();
                if (id != 0 && matches.Length != 1) throw new InvalidOperationException("Acquired route item is missing or ambiguous.");
                return new ih { slot = slot, id = id, equipment = id == 0 ? new Equipment() : matches[0].equipment };
            }).ToArray();
        }

        private void PersistRouteExecutionCommitments()
        {
            if (_importedRouteCommitments != null) throw new InvalidOperationException(_routeCommitmentIssue);
            if (string.IsNullOrEmpty(_dir)) throw new IOException("Route commitment storage is unavailable.");
            JsonFiles.WriteAtomic(Path.Combine(_dir, "route-execution-v1.json"), ExportRouteExecutionCommitments().ToString());
        }

        private bool RetireRoutesForSuccessor()
        {
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
            if (!_ordinaryRouteZone.HasValue && (Character.adventure.zone == -1
                || _globalPresetReceipt != null && GlobalPresetGearMatches(_globalPresetReceipt)
                    && GlobalPresetSupportState() == _globalPresetReceipt.Support
                    && _globalPresetReceipt.Execution.Zone == Character.adventure.zone)) ObserveOrdinaryRoute();
            var settings = Settings;
            var mode = settings.CustomAdventureMode; var manual = settings.ManualAdventureEnabled;
            var enabled = settings.CustomAdventureEnabled;
            var gear = LoadoutManager.CaptureExactLoadout(); var support = CaptureSupportState().StableKey;
            var objects = CombatPrepAdventureRouteObjects();
            return ResolveExecutionGrant("adventure-route", zone.ToString(), ExecutionDomain.Route | ExecutionDomain.NativeSettings,
                () => ReferenceEquals(settings, Settings) && mode == settings.CustomAdventureMode
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
        private bool _ordinaryRouteIdle, _ordinaryRouteBeast;
        private object[] _ordinaryRouteObjects;

        internal void ObserveOrdinaryRoute()
        {
            _ordinaryRouteZone = Character.adventure.zone;
            _ordinaryRouteIdle = Character.adventure.autoattacking;
            _ordinaryRouteBeast = Character.adventure.beastModeOn;
            _ordinaryRouteObjects = CombatPrepAdventureRouteObjects();
        }

        private void CustomAdventureTick()
        {
            try
            {
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
                if (_routeCommitmentIssue != null) { SetCustomAutomationStatus(_routeCommitmentIssue); return; }
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
            PersistRouteExecutionCommitments();
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
