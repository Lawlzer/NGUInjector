using System;
using System.Globalization;
using System.Linq;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal partial class Main
    {
        private string ObserveCombatPrepAdventure(CombatPrepRun run, out double weight)
        {
            var plan = CustomPlanStore.Load(run.OwnerPlan);
            weight = plan.Stages[run.OwnerStage].AdventureDynamicWeight;
            if (weight == 0) return "0";
            ObserveAdventurePresetTask(out _, out _, out var key, out _);
            return weight.ToString("R", CultureInfo.InvariantCulture) + ":" + Settings.CustomAdventureEnabled
                + ":" + Settings.ManualAdventureEnabled + ":" + Character.adventure.beastModeOn + ":" + key;
        }

        private CombatPrepCapture CaptureScheduledCombatPrep(CombatPrepRun run, bool continueAt = false)
        {
            var intent = ObserveCombatPrepAdventure(run, out var weight);
            run.AdventureConfiguredWeight = weight;
            var effective = weight;
            run.AdventureStatus = weight > 0 ? "pending" : "disabled";
            if (weight > 0)
            {
                if (continueAt || CombatPrepAdventureDeferred())
                {
                    effective = 0;
                    run.AdventureStatus = "deferred";
                }
            }
            var remaining = Math.Min(run.Duration - (CombatPrepClock - run.Started),
                run.DeadlineRebirthSeconds - Character.rebirthTime.totalseconds);
            var capture = CaptureAutomaticCombatPrep(effective, (int)Math.Max(1, Math.Min(86400, Math.Ceiling(remaining))));
            run.AdventureIntent = run.AdventureObservation = intent;
            run.AdventureReplanPending = continueAt && weight > 0;
            return capture;
        }

        private static bool CombatPrepAdventureDeferred()
        {
            if (!Settings.CustomAdventureEnabled || Settings.ManualAdventureEnabled || Character.adventure.beastModeOn) return true;
            ObserveAdventurePresetTask(out var mode, out var zone, out _, out _);
            return mode != CustomAdventureModeOptimalItopod && mode != CustomAdventureModeFarmBoosts && mode != CustomAdventureModeFarmZone
                || mode == CustomAdventureModeFarmZone && ZoneHelpers.ZoneIsTitan(zone);
        }

        private void ObserveCombatPrepAdventureChange(CombatPrepRun run)
        {
            var current = ObserveCombatPrepAdventure(run, out _);
            if (current != run.AdventureIntent && current != run.AdventureObservation
                || run.Inputs.WeightedProfiles && CombatPrepAdventureDeferred())
            {
                run.AdventureReplanPending = true;
                RetireCombatPrepAdventureRoute(run);
                run.AdventureStatus = "pending";
            }
        }

        private static bool CombatPrepAdventureRouteMatches(CombatPrepRun run)
        {
            var route = run.AdventureRoute;
            return run.AdventureRouteOwned && route != null && Character?.adventure != null && Character.adventure.zone == route.Zone
                && Character.adventure.autoattacking == run.AdventureAutoAttack
                && CombatPrepAdventureRouteObjectsMatch(run)
                && (route.Zone != 1000 || Character.adventure.itopodStart == route.Floor && Character.adventure.itopodEnd == route.Floor);
        }

        private void AcquireCombatPrepEntryRoute(CombatPrepRun run)
        {
            var receipt = _globalPresetReceipt;
            if (receipt == null || Character.adventure.zone == -1 || Settings.ManualAdventureEnabled
                || !receipt.Policy.ExecutionPreset.Weights.Skip(3).Take(3).Any(x => x > 0)) return;
            var settings = Settings; var mode = settings.UseCustomRebirthPlan; var file = settings.CustomPlanFile;
            var energy = settings.ManageEnergy; var magic = settings.ManageMagic; var r3 = settings.ManageR3;
            var epoch = new LoadoutManager.SnapshotEpoch(); var objects = CombatPrepAdventureRouteObjects();
            var attack = Character.adventure.autoattacking;
            // This borrows only retirement of today's verified baseline route, never a return setup.
            run.EntryRouteAuthorized = () => ReferenceEquals(reference, this) && !_unloaded && epoch.Matches()
                && ReferenceEquals(Settings, settings) && mode == settings.UseCustomRebirthPlan && file == settings.CustomPlanFile
                && energy == settings.ManageEnergy && magic == settings.ManageMagic && r3 == settings.ManageR3
                && (!SelectedSetupConflicts.TryGetValue(Character, out var conflict) || conflict.Rebirth != Character.stats.rebirthNumber || conflict.Route == null)
                && ReferenceEquals(_globalPresetReceipt, receipt) && GlobalPresetEpochMatches(receipt.Policy)
                && _globalPresetPolicies.TryGetValue(receipt.Id, out var policy) && ReferenceEquals(policy, receipt.Policy)
                && (receipt.Owner == "dashboard" ? !settings.UseCustomRebirthPlan && _globalPresetManualId == receipt.Id
                    : settings.UseCustomRebirthPlan && _customStageEquipmentKey == receipt.Owner)
                && !settings.ManualAdventureEnabled && !ManualAdventureOwnsEquipment && !_manualTitanActive
                && !_customTitanFightActive && !_customTitanGoldActive && (_questManager == null || _questManager.IsQuesting() < 0)
                && GlobalPresetGearMatches(receipt) && GlobalPresetSupportState() == receipt.Support
                && !receipt.Policy.Items.Where((item, i) => LoadoutPresetPrimitiveKey(item) != receipt.Policy.Material[i]).Any()
                && !receipt.Policy.Slots.Where((slot, i) => !receipt.Gear.Any(item => item.slot == slot
                    && (receipt.Policy.Items[i] == null ? item.id == 0 : ReferenceEquals(item.equipment, receipt.Policy.Items[i])))).Any()
                && Character.adventure.autoattacking == attack && Character.adventure.zone == receipt.Execution.Zone
                && (receipt.Execution.Zone != 1000 || Character.adventure.itopodStart == receipt.Execution.Floor
                    && Character.adventure.itopodEnd == receipt.Execution.Floor)
                && !CombatPrepAdventureRouteObjects().Where((item, i) => !ReferenceEquals(item, objects[i])).Any();
            if (!run.EntryRouteAuthorized()) run.EntryRouteAuthorized = null;
        }

        private static object[] CombatPrepAdventureRouteObjects()
        {
            var controller = Character?.adventureController;
            return new object[] { Character, Character?.adventure, controller, controller?.zoneSelector,
                controller?.playerController, controller?.enemyAI, controller?.itopod,
                controller?.itopodStartInput, controller?.itopodEndInput, controller?.idleAttackMove };
        }

        private static bool CombatPrepAdventureRouteObjectsMatch(CombatPrepRun run)
        {
            var current = CombatPrepAdventureRouteObjects();
            return run.AdventureRouteObjects != null && current.Length == run.AdventureRouteObjects.Length
                && !current.Where((item, i) => !ReferenceEquals(item, run.AdventureRouteObjects[i])).Any();
        }

        private void RetireCombatPrepAdventureRoute(CombatPrepRun run)
        {
            if (!run.AdventureRouteOwned) return;
            if (!CombatPrepAdventureRouteMatches(run) && Character?.adventure?.zone != -1)
                RecordSelectedSetupConflict((Character)run.Capture.SupportObjects[0], run.Capture.Rebirth, "route", "Adventure route changed externally; prerequisites retained.");
            // Stop our farming before replacing its supporting gear. Never restore an old zone over a new owner.
            if (CombatPrepAdventureRouteMatches(run))
            {
                if (!CombatPrepRouteRetirementAuthorized(run))
                {
                    run.AdventureRouteOwned = false;
                    run.AdventureRoute = null;
                    run.AdventureRouteObjects = null;
                    run.AdventureStatus = "ownership-lost";
                    return;
                }
                var phase = run.PhaseIndex;
                ApplySelectedSetup(new SelectedSetupRequirements { Owner = "combat-release:" + run.Id,
                    Gear = LoadoutManager.CaptureExactLoadout(), Support = CaptureSupportState().Selection,
                    Recall = GearRecallFootprint.None, RetireRoute = true,
                    Authorized = () => ReferenceEquals(_combatPrepRun, run) && run.PhaseIndex == phase
                        && CombatPrepEpochMatches(run.Capture) && !run.Retired && CombatPrepAdventureRouteObjectsMatch(run),
                    RouteRetirementAuthorized = () => CombatPrepRouteRetirementAuthorized(run) });
            }
            run.AdventureRouteOwned = false;
            run.AdventureRoute = null;
            run.AdventureRouteObjects = null;
        }

        private bool CombatPrepRouteRetirementAuthorized(CombatPrepRun run) => ReferenceEquals(reference, this)
            && ReferenceEquals(_combatPrepRun, run) && CombatPrepEpochMatches(run.Capture) && !run.Retired
            && CombatPrepAdventureRouteMatches(run) && CombatPrepGearMatches(run.ExpectedGear) && CombatPrepSupportStillOwned(run)
            && !run.ExpectedGear.Where((item, i) => LoadoutPresetPrimitiveKey(item.equipment) != run.ExpectedMaterial[i]).Any()
            && CombatPrepCombatPreemption() == null && !ManualAdventureOwnsEquipment && !Settings.ManualAdventureEnabled
            && !_customTitanFightActive && !_customTitanGoldActive && (_questManager == null || _questManager.IsQuesting() < 0);

        private void RouteCombatPrepAdventure()
        {
            var run = _combatPrepRun;
            if (run?.Automatic != true || run.Status != "running" || run.CleanupPending || run.Retired || run.FightSuspended || run.FightResuming
                || run.PhaseIndex < 0 || !run.Inputs.WeightedProfiles
                || !ReferenceEquals(reference, this) || !AutomaticCombatPrepOwnerMatches(run)) return;
            if (SelectedSetupConflicts.TryGetValue(Character, out var conflict) && conflict.Rebirth == Character.stats.rebirthNumber && conflict.Route != null)
            { run.AdventureStatus = "ownership-lost"; run.Reason = conflict.Route; return; }
            if (run.AdventureReplanPending || CombatPrepAdventureDeferred() || Controller.midDrag || ManualAdventureOwnsEquipment
                || _customTitanFightActive || _customTitanGoldActive || _maxItopodPushRemainingDeaths > 0
                || (_questManager != null && _questManager.IsQuesting() >= 0) || CombatPrepCombatPreemption() != null)
            {
                RetireCombatPrepAdventureRoute(run);
                return;
            }
            var intent = ObserveCombatPrepAdventure(run, out _);
            if (intent != run.AdventureIntent && intent != run.AdventureObservation) return;
            var phase = run.Policy.Phases[run.PhaseIndex];
            if (!CombatPrepRequirementsVerified(run, phase) || run.ResourceOutcome?.Verified != true
                || run.AppliedResourcePhase != run.PhaseIndex || !CombatPrepDeadlineAvailable(run)) return;
            if (!run.Inputs.AdventureRoutes.TryGetValue(phase.GearProfileKey, out var route)
                || !CombatPrepGearMatches(run.ExpectedGear) || !CombatPrepSupportStillOwned(run)) return;
            var snapshot = run.Inputs.AdventureSnapshots[phase.GearProfileKey];
            if (CombatPrepAdventureRouteMatches(run))
            {
                if (route.Zone != 1000 || !Character.arbitrary.boughtLazyITOPOD || !Character.arbitrary.lazyITOPODOn
                    || route.Floor == Math.Min(Character.adventureController.maxItopodLevel(),
                        Math.Min(Character.calculateBestItopodLevel(), Character.adventure.highestItopodLevel - 1))) return;
                run.AdventureReplanPending = true;
                RetireCombatPrepAdventureRoute(run);
                return;
            }
            if (Character.adventure.zone != -1)
            {
                run.AdventureRouteOwned = false;
                run.AdventureRoute = null;
                run.AdventureRouteObjects = null;
                run.AdventureStatus = "ownership-lost";
                run.Reason = "Adventure route changed externally; prerequisites retained.";
                RecordSelectedSetupConflict(Character, run.Capture.Rebirth, "route", run.Reason);
                return;
            }
            if (CombatPrepClock - run.AdventureRouteLastAttempt < .5) return;
            run.AdventureRouteLastAttempt = CombatPrepClock;
            var character = Character;
            var resources = _resourceSetupExecutor.Current;
            var inputs = run.Inputs;
            var allocations = ResourceWriter.CaptureAllocations(character);
            var resourceObjects = ResourceWriter.CaptureResourceContext(Character);
            Func<bool> resourcesOwned = () => {
                try
                {
                    var current = ResourceWriter.CaptureResourceContext(Character);
                    if (current.Length != resourceObjects.Length || current.Where((item, i) => !ReferenceEquals(item, resourceObjects[i])).Any()) return false;
                    var observed = ResourceWriter.CaptureAllocations(Character);
                    return allocations.All(pair => pair.Value == null ? observed[pair.Key] == null
                        : observed[pair.Key] != null && pair.Value.SequenceEqual(observed[pair.Key]));
                }
                catch { return false; }
            };
            try
            {
                // Claim the intended route before applying: a partial native route write must also retire safely.
                run.AdventureRoute = route;
                run.AdventureRouteOwned = true;
                run.AdventureAutoAttack = true;
                run.AdventureRouteObjects = CombatPrepAdventureRouteObjects();
                var routeDomains = ExecutionDomain.Energy | ExecutionDomain.Magic | ExecutionDomain.Gear | ExecutionDomain.Support
                    | ExecutionDomain.Route | ExecutionDomain.NativeSettings;
                Func<bool> prerequisites = () => CombatPrepRequirementsVerified(run, phase)
                    && run.ResourceOutcome?.Verified == true && run.ResourceOutcome.VerifiedLanes == 3
                    && run.AppliedResourcePhase == run.PhaseIndex
                    && resources != null && ReferenceEquals(resources, _resourceSetupExecutor.Current)
                    && resources.Owner == "combat:" + run.Id && resources.Energy && resources.Magic && resources.Authorized()
                    && ExecutionBaselineResourceMapMatches(resources)
                    && ReferenceEquals(inputs, run.Inputs) && ReferenceEquals(route, inputs.AdventureRoutes[phase.GearProfileKey])
                    && ObserveCombatPrepAdventure(run, out _) == intent
                    && resourcesOwned() && CombatPrepAdventureRouteObjectsMatch(run)
                    && !CombatPrepAdventureDeferred() && !run.AdventureReplanPending;
                var routeGrant = ResolveExecutionGrant("combat:" + run.Id, run.Setup.Acquisition, routeDomains, prerequisites);
                ApplyLoadoutPresetRoute(route, snapshot, prerequisites, routeGrant);
                if (!CombatPrepAdventureRouteMatches(run)) throw new InvalidOperationException("Combat route settings or native model changed during activation.");
                run.AdventureStatus = "active";
            }
            catch (Exception error)
            {
                if (!Character.adventure.autoattacking || !CombatPrepAdventureRouteObjectsMatch(run)
                    || Character.adventure.zone != -1 && !CombatPrepAdventureRouteMatches(run))
                    RecordSelectedSetupConflict(character, run.Capture.Rebirth, "route", "Native farming route/settings changed during activation; review the current route.");
                if (!resourcesOwned())
                {
                    var lanes = 0;
                    try
                    {
                        var observed = ResourceWriter.CaptureAllocations(Character);
                        lanes = allocations.Where(pair => pair.Value == null ? observed[pair.Key] != null
                            : observed[pair.Key] == null || !pair.Value.SequenceEqual(observed[pair.Key]))
                            .Aggregate(0, (mask, pair) => mask | (1 << ResourceWriter.Lane(pair.Key)));
                    }
                    catch { lanes = 7; }
                    run.ResourceOutcome = new ResourceMutationResult { Disposition = MutationDisposition.ForeignConflict,
                        AffectedLanes = lanes == 0 ? 7 : lanes, Reason = "Resource authority changed during combat route activation.", Error = error };
                    _resourceSetupExecutor.Application.RecordUnresolved(character, run.ResourceOutcome);
                    run.YieldedResourceLanes |= run.ResourceOutcome.AffectedLanes;
                    run.AppliedResourcePhase = -1;
                }
                try { RetireCombatPrepAdventureRoute(run); }
                catch (Exception retirement)
                {
                    FreezeCombatPrepTiming(run);
                    run.CleanupPending = true;
                    run.CleanupStatus = run.Status = "failed";
                    run.CleanupReason = run.Reason = "Combat route retirement pending: " + retirement.Message;
                    run.CleanupLastAttempt = CombatPrepClock;
                    throw;
                }
                run.AdventureStatus = SelectedSetupConflicts.TryGetValue(character, out var routeConflict)
                    && routeConflict.Rebirth == run.Capture.Rebirth && routeConflict.Route != null ? "ownership-lost" : "blocked";
                CombatPrepCleanupWarning(run, "Adventure routing deferred: " + error.Message);
            }
        }
    }
}
