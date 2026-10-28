using System;
using System.Collections.Generic;
using System.Linq;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal partial class Main
    {
        [Flags]
        internal enum ExecutionDomain
        {
            None = 0, Energy = 1, Magic = 2, Res3 = 4, Gear = 8, Support = 16,
            NativeSettings = 32, Route = 64, FiniteAction = 128, Inventory = 256
        }

        internal enum ExecutionLifecycle { Running, Quiescing, LoadingSave, Unloaded }

        internal sealed class ExecutionClaim
        {
            internal string Owner, Acquisition;
            internal ExecutionDomain Domains;
            internal Func<bool> Current;
            internal SelectedSetupRequirements Setup;
            internal ih[] ObservedGear;
            internal string[] ObservedMaterial;
            internal string ObservedSupport;
            internal readonly LoadoutManager.SnapshotEpoch Epoch = new LoadoutManager.SnapshotEpoch();
            internal bool Ended;
            internal long ReservedEnergy, ReservedMagic, ReservedRes3;
            internal ResourceSetupIntent ResourcePolicy;
            internal ResourceMutationResult ReleaseOutcome;
            internal SelectedSetupRequirements ExplicitReturnBaseline;
            internal bool SetupReleased;
            internal bool CapacityTransition;
        }

        internal sealed class ExecutionGrant
        {
            internal string Owner, Acquisition;
            internal ExecutionDomain Domains;
            internal Func<bool> Current;
        }

        private List<ExecutionClaim> _executionClaimState;
        private List<ExecutionClaim> _executionClaims => _executionClaimState ?? (_executionClaimState = new List<ExecutionClaim>());
        private long[] _executionRevisionState;
        private long[] _executionDomainRevisions => _executionRevisionState ?? (_executionRevisionState = new long[9]);
        private ExecutionLifecycle _executionLifecycle;
        private long _executionGeneration;
        private SelectedSetupRequirements _executionBaseline;
        private ExecutionGrant _activeSetupGrant;
        private ResourceSetupIntent _executionBaselineResourceIntent;
        private int _executionCapacityFundingLanes;
        private LoadoutManager.SnapshotEpoch _executionCapacityFundingEpoch;

        private void LimitExecutionCapacityFunding(int lanes)
        {
            if (_executionCapacityFundingEpoch == null || !_executionCapacityFundingEpoch.Matches()) _executionCapacityFundingLanes = 0;
            _executionCapacityFundingEpoch = new LoadoutManager.SnapshotEpoch();
            _executionCapacityFundingLanes |= lanes;
        }

        private long ExecutionCapacityBudget(bool energy, long available)
        {
            if (_executionCapacityFundingEpoch == null || !_executionCapacityFundingEpoch.Matches()) return available;
            var lane = energy ? 1 : 2;
            if ((_executionCapacityFundingLanes & lane) == 0) return available;
            var cap = energy ? Character.totalCapEnergy() : Character.totalCapMagic();
            var total = energy ? Character.curEnergy : Character.magic.curMagic;
            if (total <= cap) { _executionCapacityFundingLanes &= ~lane; return available; }
            var basic = energy ? Character.training.attackEnergy.Concat(Character.training.defenseEnergy)
                .Aggregate(0m, (sum, amount) => sum + Math.Max(0, amount)) : 0m;
            return (long)Math.Min(available, Math.Max(0m, (decimal)cap - basic));
        }

        private bool ExecutionCapacityFundingSafe(ResourceSetupIntent intent)
        {
            foreach (var energy in new[] { true, false })
            {
                if (energy ? !intent.Energy : !intent.Magic) continue;
                var available = ResourceWriter.Reclaimable(energy);
                var safe = ExecutionCapacityBudget(energy, available);
                if (safe == available) continue;
                var observed = ResourceWriter.CaptureAllocations(Character);
                var total = observed.Where(x => ResourceWriter.Lane(x.Key) == (energy ? 0 : 1)
                    && x.Key != ResourceTarget.BasicAttack && x.Key != ResourceTarget.BasicDefense)
                    .Aggregate(0m, (sum, pair) => sum + (intent.Desired.ContainsKey(pair.Key) ? intent.Desired[pair.Key] : pair.Value ?? new long[0]).Sum(x => (decimal)x));
                if (total > safe) return false;
            }
            return true;
        }

        private Action<ih[], SupportSelection> BeginExecutionCapacityPresetTransition(SelectedSetupRequirements wanted)
        {
            var receipt = _globalPresetReceipt;
            var claims = wanted.ResolvedClaims;
            if (receipt == null || claims == null || claims.Length != 1 || !claims[0].CapacityTransition || !claims[0].SetupReleased
                || !ExecutionClaimCurrent(claims[0]) || !ExecutionClaimSetupOwned(claims[0])
                || receipt.Owner != wanted.Owner || receipt.Policy.Acquisition != wanted.Acquisition
                || !GlobalPresetEpochMatches(receipt.Policy) || !ValidateExecutionGrant(wanted.Grant, wanted.Grant.Domains)) return null;
            // The selected actuator, not a newer observation, supplies each exact
            // authorized progress map. Do not convert it into a return snapshot.
            return (gear, support) => {
                if (!ReferenceEquals(receipt, _globalPresetReceipt) || !GlobalPresetEpochMatches(receipt.Policy)
                    || !ValidateExecutionGrant(wanted.Grant, wanted.Grant.Domains) || !LoadoutManager.ExactLoadoutMatches(gear)
                    || CaptureSupportState().Selection.StableKey != support.StableKey)
                    throw new InvalidOperationException("Capacity successor progress authority changed.");
                receipt.TransitionGear = gear.ToArray();
                receipt.TransitionMaterial = gear.Select(x => LoadoutPresetPrimitiveKey(x.equipment)).ToArray();
                receipt.TransitionSupport = CaptureSupportState().StableKey;
            };
        }

        internal bool ExecutionBaselineResourceMapMatches(ResourceSetupIntent intent)
        {
            if (intent?.Desired == null) return false;
            var lanes = (intent.Energy ? 1 : 0) | (intent.Magic ? 2 : 0) | (intent.Res3 ? 4 : 0);
            if (_executionClaims.Any(x => !x.Ended && ((int)x.Domains & lanes) != 0 && !ExecutionClaimCurrent(x))) return false;
            var observed = ResourceWriter.CaptureAllocations(Character);
            return intent.Desired.Where(x => ResourceWriter.Lane(x.Key) == 0 ? intent.Energy && Settings.ManageEnergy
                : ResourceWriter.Lane(x.Key) == 1 ? intent.Magic && Settings.ManageMagic : intent.Res3 && Settings.ManageR3)
                .All(x => x.Value != null && observed[x.Key] != null && x.Value.SequenceEqual(observed[x.Key]));
        }

        internal bool ExecutionOwnsDomain(ExecutionDomain domains) => _executionClaims.Any(x => !x.Ended && (x.Domains & domains) != 0);

        internal bool ExecutionClaimReservesEquipment(Equipment item)
        {
            if (item == null) return false;
            bool contains(SelectedSetupRequirements setup) => setup?.Gear?.Any(x => ReferenceEquals(x.equipment, item)) == true;
            return _executionClaims.Any(x => !x.Ended && (contains(x.Setup) || contains(x.ExplicitReturnBaseline)))
                || RetainedExecutionPresetPolicies().Any(x => x.Items.Any(selected => ReferenceEquals(selected, item)))
                || _executionBaseline?.Authorized?.Invoke() == true && contains(_executionBaseline);
        }

        internal string GetExecutionDomainReviewReason(string owner)
        {
            if (Character?.stats == null) return null;
            if (SelectedSetupConflicts.TryGetValue(Character, out var issue) && issue.Rebirth == Character.stats.rebirthNumber)
            {
                var reason = issue.Gear ?? issue.Support ?? issue.Route ?? (issue.NativeSettingsOwner == owner ? issue.NativeSettings : null);
                if (reason != null) return reason;
            }
            return _resourceSetupExecutor.Application.BlockedLanes == 0 ? null
                : _resourceSetupExecutor.Application.Last?.Reason ?? "Managed resource authority requires explicit current-session review.";
        }
        private bool ExecutionClaimsActive => _executionClaims.Any(x => !x.Ended);
        private bool ExecutionSetupClaimsActive => ExecutionOwnsDomain(ExecutionDomain.Gear | ExecutionDomain.Support);
        private bool ExecutionRouteClaimActive => ExecutionOwnsDomain(ExecutionDomain.Route);

        internal Func<bool> CaptureExecutionBaselineRouteRetirement(ExecutionClaim claim, bool endingAppliedDashboard = false)
        {
            if (!ExecutionClaimCurrent(claim) || (claim.Domains & ExecutionDomain.Route) == 0) return () => false;
            var receipt = _globalPresetReceipt;
            var applied = CaptureAppliedPresetRouteRetirement(claim, () => receipt != null
                && (receipt.Owner == "dashboard" ? endingAppliedDashboard || !Settings.UseCustomRebirthPlan && _globalPresetManualId == receipt.Id
                    : Settings.UseCustomRebirthPlan && _customStageEquipmentKey == receipt.Owner));
            var ordinary = CaptureOrdinaryRouteRetirement(claim);
            return () => applied() || ordinary();
        }

        private Func<bool> CaptureOrdinaryRouteRetirement(ExecutionClaim claim = null)
        {
            var route = Character.adventure;
            var inline = _inlineStageSelection;
            var receipt = _globalPresetReceipt;
            var resourcesBaseline = _executionBaselineResourceIntent;
            // Safe Zone is an allowed retirement effect, not evidence for initial admission.
            if (!_ordinaryRouteActuated || _ordinaryRouteEpoch?.Matches() != true || route.zone < 0
                || _ordinaryRouteZone != route.zone || _ordinaryRouteStart != route.itopodStart || _ordinaryRouteEnd != route.itopodEnd
                || _ordinaryRouteIdle != route.autoattacking || _ordinaryRouteBeast != route.beastModeOn
                || receipt != null && (receipt.Owner == "dashboard" ? !Settings.UseCustomRebirthPlan && _globalPresetManualId == receipt.Id
                    : Settings.UseCustomRebirthPlan && receipt.Owner == _customStageEquipmentKey)
                || Settings.ManualAdventureEnabled || ZoneHelpers.ZoneIsTitan(route.zone)
                || _questManager != null && _questManager.IsQuesting() >= 0
                || claim != null && (!ExecutionClaimCurrent(claim) || (claim.Domains & ExecutionDomain.Route) == 0)
                || _executionClaims.Any(x => !x.Ended && !ReferenceEquals(x, claim) && (x.Domains & ExecutionDomain.Route) != 0)
                || ExecutionScopeNeedsReview(ExecutionDomain.Gear | ExecutionDomain.Support | ExecutionDomain.Route | ExecutionDomain.NativeSettings)
                || _resourceSetupExecutor.Application.BlockedLanes != 0
                || resourcesBaseline != null && !ExecutionBaselineResourceMapMatches(resourcesBaseline)
                || Settings.UseCustomRebirthPlan && !InlineStageRetirementObservationMatches(inline)) return () => false;
            var zone = route.zone; var start = route.itopodStart; var end = route.itopodEnd;
            var attack = route.autoattacking; var beast = route.beastModeOn;
            var objects = _ordinaryRouteObjects;
            var epoch = _ordinaryRouteEpoch;
            var settings = Settings; var permissions = ExecutionRebindPermissions() + ":" + RoutePermissionKey("adventure-route");
            var generation = _executionGeneration; var entry = ExecutionPersistenceEntry; var stage = _customStageKey;
            var revisions = (long[])_executionDomainRevisions.Clone();
            var conflicts = SelectedSetupConflictVersion(Character);
            var gear = LoadoutManager.CaptureExactLoadout(); var material = gear.Select(x => LoadoutPresetPrimitiveKey(x.equipment)).ToArray();
            var support = CaptureSupportState().StableKey; var accounting = ResourceWriter.AllocatedResourceKey();
            var boss = Character.bossController;
            var context = ResourceWriter.CaptureResourceContext(Character).Concat(SupportObjects()).ToArray();
            return () => {
                if (!epoch.Matches() || generation != _executionGeneration || entry != ExecutionPersistenceEntry || stage != _customStageKey
                    || !revisions.SequenceEqual(_executionDomainRevisions)
                    || !ReferenceEquals(Settings, settings) || permissions != ExecutionRebindPermissions() + ":" + RoutePermissionKey("adventure-route")
                    || !ReferenceEquals(receipt, _globalPresetReceipt) || !ReferenceEquals(boss, Character.bossController)
                    || !ReferenceEquals(resourcesBaseline, _executionBaselineResourceIntent)
                    || !ReferenceEquals(objects, _ordinaryRouteObjects) || !_ordinaryRouteActuated
                    || SelectedSetupConflictVersion(Character) != conflicts || _resourceSetupExecutor.Application.BlockedLanes != 0
                    || claim != null && (!ExecutionClaimCurrent(claim) || !ExecutionClaimSetupOwned(claim))
                    || _executionClaims.Any(x => !x.Ended && !ReferenceEquals(x, claim) && (x.Domains & ExecutionDomain.Route) != 0)
                    || Settings.UseCustomRebirthPlan && (!ReferenceEquals(inline, _inlineStageSelection)
                        || !InlineStageRetirementObservationMatches(inline))
                    || Character.bossController == null || Character.bossController.isFighting || Character.bossController.nukeBoss
                    || _questManager != null && _questManager.IsQuesting() >= 0) return false;
                var current = CombatPrepAdventureRouteObjects();
                var resources = ResourceWriter.CaptureResourceContext(Character).Concat(SupportObjects()).ToArray();
                return objects != null && current.Length == objects.Length && !current.Where((x, i) => !ReferenceEquals(x, objects[i])).Any()
                    && resources.Length == context.Length && !resources.Where((x, i) => !ReferenceEquals(x, context[i])).Any()
                    && (_ordinaryRouteZone == zone || _ordinaryRouteZone == -1) && _ordinaryRouteStart == start && _ordinaryRouteEnd == end
                    && _ordinaryRouteIdle == attack && _ordinaryRouteBeast == beast
                    && (route.zone == zone || route.zone == -1) && route.itopodStart == start && route.itopodEnd == end
                    && route.autoattacking == attack && route.beastModeOn == beast
                    && LoadoutManager.ExactLoadoutMatches(gear) && !gear.Where((x, i) => LoadoutPresetPrimitiveKey(x.equipment) != material[i]).Any()
                    && CaptureSupportState().StableKey == support && ResourceWriter.AllocatedResourceKey() == accounting;
            };
        }

        private Func<bool> CaptureAppliedPresetRouteRetirement(ExecutionClaim claim, Func<bool> selectionCurrent)
        {
            if (!ExecutionClaimCurrent(claim) || (claim.Domains & ExecutionDomain.Route) == 0) return () => false;
            var receipt = _globalPresetReceipt;
            var route = Character.adventure;
            var lazy = receipt != null && receipt.RetirementLazyPurchased && receipt.RetirementLazyEnabled
                && Character.arbitrary.boughtLazyITOPOD && Character.arbitrary.lazyITOPODOn;
            if (SelectedSetupConflicts.TryGetValue(Character, out var unresolved) && unresolved.Rebirth == Character.stats.rebirthNumber
                && (unresolved.Gear != null || unresolved.Support != null || unresolved.Route != null
                    || unresolved.NativeSettingsOwner == receipt?.Owner && unresolved.NativeSettings != null)) return () => false;
            if (receipt == null || !GlobalPresetEpochMatches(receipt.Policy) || route.zone < 0
                || !GlobalPresetNativeTargetsMatch(receipt.Policy.Stage, receipt.Owner)
                || receipt.Policy.MaterialPersistencePending || receipt.Policy.Items.Where((x, i) => LoadoutPresetPrimitiveKey(x) != receipt.Policy.Material[i]).Any()
                || !receipt.Policy.ExecutionPreset.Weights.Skip(3).Take(3).Any(x => x > 0)
                || receipt.RetirementLazyPurchased != Character.arbitrary.boughtLazyITOPOD
                || receipt.RetirementLazyEnabled != Character.arbitrary.lazyITOPODOn
                || receipt.RetirementAttack != route.autoattacking || receipt.RetirementBeast != route.beastModeOn
                || route.zone != receipt.Execution.Zone || route.zone == 1000
                    && (route.itopodStart != route.itopodEnd || route.itopodStart != receipt.Execution.Floor
                        && (!lazy || route.itopodStart != Math.Min(Character.adventureController.maxItopodLevel(),
                            Math.Min(Character.calculateBestItopodLevel(), route.highestItopodLevel - 1))))
                || !LoadoutManager.ExactLoadoutMatches(receipt.Gear) || receipt.Support != GlobalPresetSupportState()
                || !GlobalPresetRetirementAllocationsMatch(receipt) || _resourceSetupExecutor.Application.BlockedLanes != 0)
                return () => false;
            var zone = route.zone; var start = route.itopodStart; var end = route.itopodEnd; var attack = route.autoattacking;
            var settings = Settings; var settingsKey = DelegatedManualRouteSettingsKey();
            var generation = _protectedGeneration; var entry = _protectedScheduler?.Entry;
            var owner = _customStageEquipmentKey; var conflicts = SelectedSetupConflictVersion(Character);
            var gear = LoadoutManager.CaptureExactLoadout(); var material = gear.Select(x => LoadoutPresetPrimitiveKey(x.equipment)).ToArray();
            var support = CaptureSupportState().StableKey; var allocations = ResourceWriter.AllocatedResourceKey();
            var resourceObjects = receipt.RetirementResourceObjects;
            var routeObjects = receipt.RetirementRouteObjects;
            var revisions = (long[])_executionDomainRevisions.Clone();
            var targets = Character.advancedTraining.levelTarget.ToArray(); var autoAdvance = Character.advancedTraining.autoAdvance;
            var beast = route.beastModeOn;
            var lazyPurchased = Character.arbitrary.boughtLazyITOPOD; var lazyEnabled = Character.arbitrary.lazyITOPODOn;
            var supportObjects = receipt.RetirementSupportObjects;
            var controller = Character.adventureController; var selector = controller?.zoneSelector;
            var boss = Character.bossController;
            Func<bool> resourceContextOwned = () => {
                var current = ResourceWriter.CaptureResourceContext(Character);
                var supportCurrent = SupportObjects();
                var routeCurrent = CombatPrepAdventureRouteObjects();
                return resourceObjects != null && current.Length == resourceObjects.Length && !current.Where((x, i) => !ReferenceEquals(x, resourceObjects[i])).Any()
                    && routeObjects != null && routeCurrent.Length == routeObjects.Length && !routeCurrent.Where((x, i) => !ReferenceEquals(x, routeObjects[i])).Any()
                    && supportObjects != null && supportCurrent.Length == supportObjects.Length && !supportCurrent.Where((x, i) => !ReferenceEquals(x, supportObjects[i])).Any();
            };
            if (!resourceContextOwned()) return () => false;
            return () => ExecutionClaimCurrent(claim) && ExecutionClaimSetupOwned(claim) && ReferenceEquals(receipt, _globalPresetReceipt)
                && GlobalPresetEpochMatches(receipt.Policy) && ReferenceEquals(Settings, settings) && settingsKey == DelegatedManualRouteSettingsKey()
                && generation == _protectedGeneration && entry == _protectedScheduler?.Entry && owner == _customStageEquipmentKey
                && revisions.SequenceEqual(_executionDomainRevisions)
                && selectionCurrent()
                && SelectedSetupConflictVersion(Character) == conflicts && _resourceSetupExecutor.Application.BlockedLanes == 0
                && ReferenceEquals(route, Character.adventure) && ReferenceEquals(boss, Character.bossController)
                && ReferenceEquals(controller, Character.adventureController) && ReferenceEquals(selector, controller.zoneSelector)
                && resourceContextOwned()
                && (route.zone == zone || route.zone == -1) && route.itopodStart == start && route.itopodEnd == end
                && route.beastModeOn == beast && Character.advancedTraining.autoAdvance == autoAdvance
                && targets.SequenceEqual(Character.advancedTraining.levelTarget)
                && lazyPurchased == Character.arbitrary.boughtLazyITOPOD && lazyEnabled == Character.arbitrary.lazyITOPODOn
                && route.autoattacking == attack && attack && Character.bossController != null
                && !Character.bossController.isFighting && !Character.bossController.nukeBoss
                && LoadoutManager.ExactLoadoutMatches(gear) && !gear.Where((x, i) => LoadoutPresetPrimitiveKey(x.equipment) != material[i]).Any()
                && CaptureSupportState().StableKey == support && ResourceWriter.AllocatedResourceKey() == allocations;
        }

        internal void SetExecutionReturnBaseline(ExecutionClaim claim, SelectedSetupRequirements baseline)
        {
            if (!ExecutionClaimCurrent(claim) || claim.Owner != "quick:F8" || baseline == null
                || baseline.ControlsGear && LoadoutManager.ValidateExactLoadout(baseline.Gear) != null
                || baseline.Support == null || baseline.MaterialValid != null && !baseline.MaterialValid())
                throw new InvalidOperationException("Only F8 may admit an explicit, exact current-session return baseline.");
            var epoch = new LoadoutManager.SnapshotEpoch(); var current = baseline.Authorized;
            var selected = baseline.Copy(); selected.Gear = baseline.Gear?.ToArray(); selected.Recall = GearRecallFootprint.None;
            selected.Owner = "return:quick:F8"; selected.Acquisition = claim.Acquisition;
            selected.Authorized = () => ReferenceEquals(reference, this) && !_unloaded && epoch.Matches() && (current == null || current());
            claim.ExplicitReturnBaseline = selected;
        }

        internal void SetExecutionResourceReservation(ExecutionClaim claim, long energy, long magic, long res3 = 0)
        {
            if (!ExecutionClaimCurrent(claim) || energy < 0 || magic < 0 || res3 < 0)
                throw new InvalidOperationException("Resource reservation requires a current claim and nonnegative exact amounts.");
            var domains = (energy > 0 ? ExecutionDomain.Energy : 0) | (magic > 0 ? ExecutionDomain.Magic : 0) | (res3 > 0 ? ExecutionDomain.Res3 : 0);
            if ((domains & ~claim.Domains) != 0 || !ValidateExecutionGrant(ResolveExecutionGrant(claim.Owner, claim.Acquisition,
                domains, () => ExecutionClaimCurrent(claim), claim), domains))
                throw new InvalidOperationException("Reservation exceeds current lane authority.");
            claim.ReservedEnergy = energy; claim.ReservedMagic = magic; claim.ReservedRes3 = res3;
            InvalidateExecutionObservations(claim.Domains & (ExecutionDomain)7);
        }

        internal void SetExecutionResourcePolicy(ExecutionClaim claim, ResourceSetupIntent policy)
        {
            if (!ExecutionClaimCurrent(claim) || policy == null || policy.Desired == null)
                throw new InvalidOperationException("Temporary resource policy requires a current claim and explicit targets.");
            var domains = (policy.Energy ? ExecutionDomain.Energy : 0) | (policy.Magic ? ExecutionDomain.Magic : 0) | (policy.Res3 ? ExecutionDomain.Res3 : 0);
            if ((domains & ~claim.Domains) != 0 || policy.Desired.Keys.Any(x => x == ResourceTarget.BasicAttack || x == ResourceTarget.BasicDefense
                || ((int)domains & (1 << ResourceWriter.Lane(x))) == 0))
                throw new InvalidOperationException("Temporary policy exceeds the declared resource footprint.");
            claim.ResourcePolicy = new ResourceSetupIntent { Owner = claim.Owner, Energy = policy.Energy, Magic = policy.Magic, Res3 = policy.Res3,
                Desired = policy.Desired.ToDictionary(x => x.Key, x => x.Value?.ToArray()), Authorized = policy.Authorized };
            InvalidateExecutionObservations(domains);
        }

        internal long ExecutionResourceBudget(bool energy, long available)
        {
            available = ExecutionCapacityBudget(energy, available);
            var reserved = _executionClaims.Where(x => !x.Ended).Aggregate(0m, (sum, x) => sum + (energy ? x.ReservedEnergy : x.ReservedMagic));
            return (long)Math.Max(0m, available - reserved);
        }

        internal bool ExecutionResourceReservationReady(ExecutionClaim claim)
        {
            if (!ExecutionClaimCurrent(claim) || (_resourceSetupExecutor.Application.BlockedLanes & (int)claim.Domains & 7) != 0) return false;
            return Character.idleEnergy >= claim.ReservedEnergy && Character.magic.idleMagic >= claim.ReservedMagic
                && (claim.ReservedRes3 == 0 || Character.res3.idleRes3 >= claim.ReservedRes3);
        }

        internal ResourceMutationResult ReconcileExecutionResourceClaim(ExecutionClaim claim)
        {
            if (!ExecutionClaimCurrent(claim)) throw new InvalidOperationException("Resource claim is no longer current.");
            if (claim.CapacityTransition && claim.ResourcePolicy != null)
            {
                var policy = claim.ResourcePolicy;
                var domains = (policy.Energy ? ExecutionDomain.Energy : 0) | (policy.Magic ? ExecutionDomain.Magic : 0) | (policy.Res3 ? ExecutionDomain.Res3 : 0);
                var grant = ResolveExecutionGrant(claim.Owner, claim.Acquisition, domains,
                    () => ExecutionClaimCurrent(claim) && (policy.Authorized == null || policy.Authorized()), claim);
                return _resourceSetupExecutor.Reconcile(new ResourceSetupIntent { Owner = claim.Owner, Desired = policy.Desired,
                    Energy = policy.Energy, Magic = policy.Magic, Res3 = policy.Res3,
                    Authorized = () => ValidateExecutionGrant(grant, domains) });
            }
            if (_resourceReconciliationRunning)
            {
                _resourceReconciliationPending = true;
                return new ResourceMutationResult { Disposition = MutationDisposition.RejectedUnchanged, Reason = "Resource claim is queued behind current reconciliation." };
            }
            if (Settings.UseCustomRebirthPlan || _globalPresetManualId != null || _customSustainedFocusGoal.HasValue)
            {
                _resourceReconciliationPending = true;
                ReconcileCustomResources();
                var result = _resourceSetupExecutor.Application.Last;
                return result ?? new ResourceMutationResult { Disposition = MutationDisposition.RejectedUnchanged, Reason = "Current baseline resource service is waiting." };
            }
            var lanes = claim.Domains & (ExecutionDomain)7;
            var desired = new Dictionary<ResourceTarget, long[]>();
            if (claim.ReservedEnergy > 0) foreach (var pair in ResolveExecutionIdleReservation(0, claim.ReservedEnergy)) desired[pair.Key] = pair.Value;
            if (claim.ReservedMagic > 0) foreach (var pair in ResolveExecutionIdleReservation(1, claim.ReservedMagic)) desired[pair.Key] = pair.Value;
            if (claim.ReservedRes3 > 0) foreach (var pair in ResolveExecutionIdleReservation(2, claim.ReservedRes3)) desired[pair.Key] = pair.Value;
            return ReconcileResourceSetup(new ResourceSetupIntent { Owner = claim.Owner,
                Desired = desired,
                Energy = (lanes & ExecutionDomain.Energy) != 0, Magic = (lanes & ExecutionDomain.Magic) != 0, Res3 = (lanes & ExecutionDomain.Res3) != 0,
                Authorized = () => ExecutionClaimCurrent(claim) });
        }

        private Dictionary<ResourceTarget, long[]> ResolveExecutionIdleReservation(int lane, long amount)
        {
            var desired = ResourceWriter.CaptureAllocations(Character).Where(x => x.Value != null && ResourceWriter.Lane(x.Key) == lane
                && x.Key != ResourceTarget.BasicAttack && x.Key != ResourceTarget.BasicDefense).ToDictionary(x => x.Key, x => x.Value.ToArray());
            var idle = lane == 0 ? Character.idleEnergy : lane == 1 ? Character.magic.idleMagic : Character.res3.idleRes3;
            if (idle < 0 || desired.Values.Any(x => x.Any(value => value < 0))) throw new InvalidOperationException("Invalid current resource accounting for idle reservation.");
            var deficit = Math.Max(0, amount - idle);
            foreach (var pair in desired)
                for (var i = 0; i < pair.Value.Length && deficit > 0; i++)
                {
                    var released = Math.Min(deficit, pair.Value[i]); pair.Value[i] -= released; deficit -= released;
                }
            return desired;
        }

        private ResourceSetupIntent ComposeExecutionResources(ResourceSetupIntent baseline)
        {
            var energy = baseline.Energy && Settings.ManageEnergy; var magic = baseline.Magic && Settings.ManageMagic; var res3 = baseline.Res3 && Settings.ManageR3;
            var desired = baseline.Desired.Where(x => ResourceWriter.Lane(x.Key) == 0 ? energy : ResourceWriter.Lane(x.Key) == 1 ? magic : res3)
                .ToDictionary(x => x.Key, x => x.Value?.ToArray());
            foreach (var claim in _executionClaims.Where(x => !x.Ended && x.ResourcePolicy != null))
            {
                var policy = claim.ResourcePolicy;
                var lanes = (policy.Energy ? 1 : 0) | (policy.Magic ? 2 : 0) | (policy.Res3 ? 4 : 0);
                foreach (var target in desired.Keys.Where(x => (lanes & (1 << ResourceWriter.Lane(x))) != 0).ToArray()) desired.Remove(target);
                foreach (var pair in ResourceWriter.EmptyDesiredMap(policy.Energy, policy.Magic, policy.Res3)) desired[pair.Key] = pair.Value;
                foreach (var pair in policy.Desired) desired[pair.Key] = pair.Value?.ToArray();
                energy |= policy.Energy; magic |= policy.Magic; res3 |= policy.Res3;
            }
            foreach (var claim in _executionClaims.Where(x => !x.Ended))
                for (var lane = 0; lane < 3; lane++)
                {
                    var reserved = lane == 0 ? claim.ReservedEnergy : lane == 1 ? claim.ReservedMagic : claim.ReservedRes3;
                    if (reserved <= 0 || desired.Keys.Any(x => ResourceWriter.Lane(x) == lane)) continue;
                    foreach (var pair in ResolveExecutionIdleReservation(lane, reserved)) desired[pair.Key] = pair.Value;
                    energy |= lane == 0; magic |= lane == 1; res3 |= lane == 2;
                }
            foreach (var claim in _executionClaims.Where(x => !x.Ended && !ExecutionClaimCurrent(x)))
            {
                foreach (var target in desired.Keys.Where(x => ((int)claim.Domains & (1 << ResourceWriter.Lane(x))) != 0).ToArray()) desired.Remove(target);
                if ((claim.Domains & ExecutionDomain.Energy) != 0) energy = false;
                if ((claim.Domains & ExecutionDomain.Magic) != 0) magic = false;
                if ((claim.Domains & ExecutionDomain.Res3) != 0) res3 = false;
            }
            return new ResourceSetupIntent { Owner = baseline.Owner, Desired = desired, Energy = energy, Magic = magic, Res3 = res3,
                Authorized = baseline.Authorized, Render = null };
        }

        internal bool ExecutionSetupVerified()
        {
            try
            {
                if (_executionBaseline == null || _executionBaseline.Authorized?.Invoke() != true) return false;
                if (_focusSetupSelection?.PersistencePending == true || _globalPresetReceipt?.Policy.MaterialPersistencePending == true) return false;
                if (SelectedSetupConflicts.TryGetValue(Character, out var conflict) && conflict.Rebirth == Character.stats.rebirthNumber
                    && (conflict.Gear != null || conflict.Support != null)) return false;
                var desired = ResolveExecutionSetup(_executionBaseline);
                return (desired.MaterialValid == null || desired.MaterialValid())
                    && (!desired.ControlsGear || LoadoutManager.ExactLoadoutMatches(desired.Gear))
                    && CaptureSupportState().Selection.StableKey == desired.Support.StableKey
                    && SupportProjection.Evaluate(CaptureSupportContext(), desired.Support, desired.Budget, desired.EnforceSupportBudget).Feasible;
            }
            catch { return false; }
        }

        // A claim is current-session authority, not a serialized selection or return snapshot.
        internal ExecutionClaim BeginExecutionClaim(string owner, string acquisition, ExecutionDomain domains, Func<bool> current)
        {
            if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(acquisition) || domains == ExecutionDomain.None || current == null)
                throw new ArgumentException("A claim requires an owner, acquisition, footprint and current identity predicate.");
            if (_executionLifecycle != ExecutionLifecycle.Running || !ReferenceEquals(reference, this) || _unloaded || !current())
                throw new InvalidOperationException("Execution admission is not current.");
            if (_executionClaims.Any(x => !x.Ended && (x.Domains & domains) != 0))
                throw new InvalidOperationException("An execution claim already owns part of this footprint; retire it before admission.");
            var claim = new ExecutionClaim { Owner = owner, Acquisition = acquisition, Domains = domains, Current = current };
            if ((domains & (ExecutionDomain.Gear | ExecutionDomain.Support)) != 0)
            {
                claim.ObservedGear = LoadoutManager.CaptureExactLoadout();
                claim.ObservedMaterial = claim.ObservedGear.Select(x => LoadoutPresetPrimitiveKey(x.equipment)).ToArray();
                claim.ObservedSupport = CaptureSupportState().StableKey;
            }
            _executionClaims.Add(claim);
            InvalidateExecutionObservations(domains);
            return claim;
        }

        private bool ExecutionClaimCurrent(ExecutionClaim claim) => claim != null && !claim.Ended
            && _executionClaims.Contains(claim) && claim.Epoch.Matches() && claim.Current();

        private bool ExecutionClaimSetupOwned(ExecutionClaim claim) => claim.ObservedGear == null
            || LoadoutManager.ExactLoadoutMatches(claim.ObservedGear)
                && !claim.ObservedGear.Where((x, i) => LoadoutPresetPrimitiveKey(x.equipment) != claim.ObservedMaterial[i]).Any()
                && CaptureSupportState().StableKey == claim.ObservedSupport;

        private void ObserveExecutionClaimProgress(SelectedSetupRequirements setup, ih[] gear)
        {
            var claims = setup.ResolvedClaims ?? (setup.Claim == null ? new ExecutionClaim[0] : new[] { setup.Claim });
            foreach (var claim in claims.Where(ExecutionClaimCurrent))
            {
                claim.ObservedGear = gear;
                claim.ObservedMaterial = gear.Select(x => LoadoutPresetPrimitiveKey(x.equipment)).ToArray();
                claim.ObservedSupport = CaptureSupportState().StableKey;
            }
        }

        internal ExecutionGrant ResolveExecutionGrant(string owner, string acquisition, ExecutionDomain domains,
            Func<bool> current, ExecutionClaim claim = null, ExecutionClaim[] resolvedClaims = null)
        {
            return ResolveExecutionGrantCore(owner, acquisition, domains, current, claim, resolvedClaims, false);
        }

        internal ExecutionGrant ResolveExecutionRetirementGrant(ExecutionClaim claim, ExecutionDomain domains, Func<bool> stillOwned)
        {
            var allowed = ExecutionDomain.Route | ExecutionDomain.NativeSettings;
            if (claim == null || stillOwned == null || domains == ExecutionDomain.None
                || (domains & ~allowed) != 0 || (domains & ~claim.Domains) != 0
                || !ExecutionClaimCurrent(claim) || !ExecutionClaimSetupOwned(claim))
                throw new InvalidOperationException("Retirement requires an admitted claim and exact still-owned route/settings.");
            var expires = LoadoutPresetClock + 2;
            return ResolveExecutionGrantCore(claim.Owner, claim.Acquisition, domains,
                () => LoadoutPresetClock < expires && ExecutionClaimSetupOwned(claim) && stillOwned(), claim, null, true);
        }

        private ExecutionGrant ResolveExecutionGrantCore(string owner, string acquisition, ExecutionDomain domains,
            Func<bool> current, ExecutionClaim claim, ExecutionClaim[] resolvedClaims, bool retirement)
        {
            if (string.IsNullOrEmpty(owner) || current == null)
                throw new ArgumentException("An execution grant requires current identity and explicit domains.");
            if (claim != null && (owner != claim.Owner || acquisition != claim.Acquisition))
                throw new ArgumentException("A claim grant must retain its admitted owner and acquisition identity.");
            var generation = _executionGeneration;
            var epoch = new LoadoutManager.SnapshotEpoch();
            var settings = Settings;
            var energy = settings.ManageEnergy; var magic = settings.ManageMagic; var res3 = settings.ManageR3;
            var revisions = (long[])_executionDomainRevisions.Clone();
            var conflicts = SelectedSetupConflictVersion(Character);
            var owners = _executionClaims.Where(x => !x.Ended && (x.Domains & domains) != 0).ToArray();
            var expires = retirement ? LoadoutPresetClock + 2 : double.PositiveInfinity;
            return new ExecutionGrant { Owner = owner, Acquisition = acquisition, Domains = domains, Current = () => {
                if (!ReferenceEquals(reference, this) || _unloaded
                    || (_executionLifecycle != ExecutionLifecycle.Running && (!retirement || _executionLifecycle != ExecutionLifecycle.Quiescing))
                    || generation != _executionGeneration || !ReferenceEquals(Settings, settings) || !epoch.Matches()
                    || LoadoutPresetClock >= expires || !current()) return false;
                if ((domains & ExecutionDomain.Energy) != 0 && (!energy || !settings.ManageEnergy)
                    || (domains & ExecutionDomain.Magic) != 0 && (!magic || !settings.ManageMagic)
                    || (domains & ExecutionDomain.Res3) != 0 && (!res3 || !settings.ManageR3)) return false;
                if ((domains & (ExecutionDomain.Gear | ExecutionDomain.Support | ExecutionDomain.Route)) != 0
                    && conflicts != SelectedSetupConflictVersion(Character)) return false;
                for (var i = 0; i < revisions.Length; i++)
                    if (((int)domains & (1 << i)) != 0 && revisions[i] != _executionDomainRevisions[i]) return false;
                if (claim != null && (!ExecutionClaimCurrent(claim) || (domains & ~claim.Domains) != 0)) return false;
                return owners.All(x => ExecutionClaimCurrent(x)
                    && (resolvedClaims != null ? resolvedClaims.Contains(x) : ReferenceEquals(x, claim)))
                    && !_executionClaims.Any(x => !x.Ended && (x.Domains & domains) != 0 && !owners.Contains(x));
            } };
        }

        internal bool ValidateExecutionGrant(ExecutionGrant grant, ExecutionDomain footprint)
        {
            try { return grant != null && (grant.Domains & footprint) == footprint && grant.Current(); }
            catch { return false; }
        }

        internal bool ReauthorizeExecutionDomains(ExecutionGrant grant, ExecutionDomain domains, Func<bool> reviewed)
        {
            if (!ValidateExecutionGrant(grant, domains) || reviewed == null || !reviewed()) return false;
            if (!_resourceSetupExecutor.Application.Reauthorize(Character, (int)domains & 7,
                () => ValidateExecutionGrant(grant, domains) && reviewed())) return false;
            if (SelectedSetupConflicts.TryGetValue(Character, out var conflict) && conflict.Rebirth == Character.stats.rebirthNumber)
            {
                if ((domains & ExecutionDomain.Gear) != 0) conflict.Gear = null;
                if ((domains & ExecutionDomain.Support) != 0) conflict.Support = null;
                if ((domains & ExecutionDomain.Route) != 0) conflict.Route = null;
                if ((domains & ExecutionDomain.NativeSettings) != 0)
                { conflict.NativeSettings = conflict.NativeSettingsOwner = null; _globalPresetNativeTargetConflict = null; }
                conflict.Revision++;
            }
            if ((domains & ExecutionDomain.Support) != 0)
            {
                _customSupportObserved = CaptureSupportState().StableKey;
                _customBeardObservedSupport = _customSupportObserved;
                _customBeardAppliedState = CustomBeardObservedState();
                _customSupportEpoch = new LoadoutManager.SnapshotEpoch();
                _customBeardEpoch = new LoadoutManager.SnapshotEpoch();
            }
            InvalidateExecutionObservations(domains);
            return true;
        }

        internal bool VerifyExecutionPrerequisites(ExecutionGrant grant, ExecutionDomain footprint, Func<bool> observe)
        {
            if (!ValidateExecutionGrant(grant, footprint) || observe == null
                || (_resourceSetupExecutor.Application.BlockedLanes & (int)footprint & 7) != 0) return false;
            try { return observe() && ValidateExecutionGrant(grant, footprint); }
            catch { return false; }
        }

        internal ResourceMutationResult ApplyExecutionSetting(ExecutionGrant grant, Action write, Func<bool> verify)
        {
            var result = new ResourceMutationResult { Disposition = MutationDisposition.RejectedUnchanged };
            if (write == null || verify == null || !ValidateExecutionGrant(grant, ExecutionDomain.NativeSettings))
            { result.Reason = "Native setting authority is not current."; return result; }
            try { write(); result.ModelWrites = 1; }
            catch (Exception error) { result.Error = error; result.Reason = error.Message; }
            try
            {
                result.Disposition = !ValidateExecutionGrant(grant, ExecutionDomain.NativeSettings) ? MutationDisposition.ForeignConflict
                    : verify() ? result.Error == null ? MutationDisposition.AppliedVerified : MutationDisposition.ModelAppliedRenderFailed
                    : MutationDisposition.UnknownOutcome;
            }
            catch (Exception error) { result.Error = result.Error ?? error; result.Disposition = MutationDisposition.UnknownOutcome; result.Reason = error.Message; }
            return result;
        }

        internal void InvalidateExecutionObservations(ExecutionDomain domains)
        {
            for (var i = 0; i < _executionDomainRevisions.Length; i++)
                if (((int)domains & (1 << i)) != 0) _executionDomainRevisions[i]++;
            _resourceDemandIdentity = null;
            _resourceReconciliationPending = true;
            if ((domains & ((ExecutionDomain)7 | ExecutionDomain.Gear | ExecutionDomain.Support)) != 0)
            { _customFocusLastResourceRefresh = DateTime.MinValue; _customFocusNextRetry = DateTime.MinValue; }
        }

        internal void SetExecutionLifecycle(ExecutionLifecycle lifecycle)
        {
            if (_executionLifecycle == lifecycle) return;
            _executionLifecycle = lifecycle;
            _executionGeneration++;
            _resourceReconciliationPending = true;
        }

        private void PublishExecutionBaseline(SelectedSetupRequirements baseline)
        {
            var previous = _executionBaseline;
            _executionBaseline = baseline;
            if (!ReferenceEquals(previous, baseline) && (previous == null || baseline == null
                || previous.Owner != baseline.Owner || previous.Acquisition != baseline.Acquisition
                || previous.ControlsGear != baseline.ControlsGear || previous.ChangeSupport != baseline.ChangeSupport
                || previous.Support?.StableKey != baseline.Support?.StableKey
                || previous.Gear == null != (baseline.Gear == null)
                || previous.Gear != null && !previous.Gear.Select(x => x.equipment).SequenceEqual(baseline.Gear.Select(x => x.equipment))))
                InvalidateExecutionObservations(ExecutionDomain.Gear | ExecutionDomain.Support);
        }

        private SelectedSetupRequirements ResolveExecutionSetup(SelectedSetupRequirements baseline)
        {
            if (baseline.Claim != null || baseline.ResolvedClaims != null) return baseline;
            var claims = _executionClaims.Where(x => !x.Ended && !x.SetupReleased && (x.Domains & (ExecutionDomain.Gear | ExecutionDomain.Support)) != 0).ToArray();
            if (claims.Length == 0) return baseline;
            if (claims.Any(x => !ExecutionClaimCurrent(x) || x.Setup == null || !ExecutionClaimSetupOwned(x)))
                throw new InvalidOperationException("A temporary setup claim is waiting for its exact acquired selection.");
            var resolved = baseline.Copy();
            resolved.ResolvedClaims = claims;
            var authority = baseline.Authorized;
            var material = baseline.MaterialValid;
            resolved.Authorized = () => (authority == null || authority()) && claims.All(ExecutionClaimCurrent);
            resolved.MaterialValid = () => (material == null || material())
                && claims.All(x => x.Setup.MaterialValid == null || x.Setup.MaterialValid());
            foreach (var claim in claims)
            {
                var setup = claim.Setup;
                if ((claim.Domains & ExecutionDomain.Gear) != 0)
                {
                    resolved.Gear = setup.Gear;
                    resolved.ControlsGear = setup.ControlsGear;
                    resolved.Recall = setup.Recall;
                }
                if ((claim.Domains & ExecutionDomain.Support) != 0)
                {
                    resolved.Support = setup.Support;
                    resolved.ChangeSupport = setup.ChangeSupport;
                    resolved.Budget = setup.Budget;
                    resolved.EnforceSupportBudget = setup.EnforceSupportBudget;
                }
                if ((claim.Domains & ExecutionDomain.Route) != 0)
                {
                    resolved.RouteRetirementAuthorized = setup.RouteRetirementAuthorized;
                    resolved.OwnsManualRoute = setup.OwnsManualRoute;
                }
            }
            resolved.ObserveProgress = (gear, support) => baseline.ObserveProgress?.Invoke(gear, support);
            return resolved;
        }

        private Func<bool> CaptureSelectedSetupRouteRetirement(SelectedSetupRequirements wanted)
        {
            var route = wanted.RouteRetirementAuthorized;
            var inline = _inlineStageSelection;
            if (wanted.Claim != null || wanted.ResolvedClaims != null || Character?.adventure == null || Character.adventure.zone < 0
                || !InlineStageSelectionCurrent(inline) || wanted.Owner != inline.Owner || wanted.Acquisition != inline.Acquisition) return route;
            var ordinary = CaptureOrdinaryRouteRetirement();
            return () => route?.Invoke() == true || ordinary();
        }

        internal void ApplyExecutionClaim(ExecutionClaim claim, SelectedSetupRequirements setup)
        {
            if (!ExecutionClaimCurrent(claim)) throw new InvalidOperationException("Setup claim is no longer current.");
            if (!ExecutionClaimSetupOwned(claim))
            {
                RecordSelectedSetupConflict(Character, Character.stats.rebirthNumber, "gear", "Temporary setup changed externally; newer native state was preserved.");
                throw new SelectedSetupAuthorityException("gear", "Temporary setup changed externally.");
            }
            var domains = (setup.ControlsGear ? ExecutionDomain.Gear : 0) | (setup.ChangeSupport ? ExecutionDomain.Support : 0);
            if ((domains & ~claim.Domains) != 0) throw new InvalidOperationException("Setup exceeds the claim footprint.");
            setup.Owner = claim.Owner; setup.Acquisition = claim.Acquisition;
            if (setup.Authorized == null) setup.Authorized = () => ExecutionClaimCurrent(claim);
            if (setup.Support == null && !setup.ChangeSupport) setup.Support = CaptureSupportState().Selection;
            setup.Claim = claim;
            claim.Setup = setup;
            if (setup.ChangeSupport) BeginPresetSupportAcquisition(claim.Owner, claim.Acquisition);
            ApplySelectedSetup(setup);
        }

        // End authority before resolving the CURRENT successor. No original gear is recorded.
        internal bool EndExecutionClaim(ExecutionClaim claim, bool reconcile = true)
        {
            if (claim == null) return true;
            if (!claim.Ended)
            {
                if (!_executionClaims.Remove(claim)) return false;
                claim.Ended = true;
                InvalidateExecutionObservations(claim.Domains);
                if (claim.Epoch.Matches() && !ExecutionClaimSetupOwned(claim))
                {
                    RecordSelectedSetupConflict(Character, Character.stats.rebirthNumber, "gear", "Temporary setup changed before release; review current setup.");
                    return false;
                }
            }
            if (!reconcile) return true;
            if (!claim.Epoch.Matches()) return false;
            var baseline = _executionBaseline;
            if (baseline?.Authorized?.Invoke() != true)
                baseline = FocusSetupCurrent(_focusSetupSelection) ? _focusSetupSelection.Desired
                    : ManualAdventureBaselineAuthorized() ? _manualAdventureBaseline : null;
            if (baseline?.Authorized?.Invoke() != true) baseline = claim.ExplicitReturnBaseline;
            var reconciled = true;
            var fruitCapacityReturn = false;
            if (baseline != null && baseline.Authorized?.Invoke() == true)
            {
                var successor = baseline.Copy(); successor.Recall = GearRecallFootprint.None;
                if ((claim.Domains & ExecutionDomain.Support) != 0 && (baseline.ControlsSupport || baseline.ChangeSupport)) successor.ChangeSupport = true;
                else if (!successor.ChangeSupport) successor.Support = CaptureSupportState().Selection;
                successor = ResolveExecutionSetup(successor);
                // Fruit cap gear can grow the real pool while the current baseline
                // funds it. Return with that successor's authority, never the outgoing
                // gear-only claim's permissions or a historical allocation snapshot.
                if (claim.Owner == "fruit-cap" && successor.ResolvedClaims == null && successor.ControlsGear
                    && !LoadoutManager.ExactLoadoutMatches(successor.Gear))
                {
                    var resources = _executionBaselineResourceIntent;
                    var permitted = baseline.Recall & ((resources?.Energy == true && Settings.ManageEnergy ? GearRecallFootprint.Energy : 0)
                        | (resources?.Magic == true && Settings.ManageMagic ? GearRecallFootprint.Magic : 0));
                    var transition = GearOptimizer.PreflightCapacityTransition(successor.Gear, permitted);
                    if (transition.Failure != null) return false;
                    if (transition.Recall != GearRecallFootprint.None)
                    {
                        var domains = ExecutionDomain.Gear | ExecutionDomain.Support | ExecutionDomain.Route | (ExecutionDomain)(int)transition.Recall;
                        if (!ReferenceEquals(baseline, _executionBaseline) || ExecutionScopeNeedsReview(domains)
                            || ExecutionOwnsDomain((ExecutionDomain)(int)transition.Recall)
                            || resources == null || (resources.Owner != "preset:" + baseline.Owner && resources.Owner != baseline.Owner
                                && resources.Owner != "inline-stage:" + _customStageKey)
                            || !ExecutionBaselineResourceMapMatches(resources)) return false;
                        var context = ResourceWriter.CaptureResourceContext(Character);
                        var authority = successor.Authorized;
                        successor.Recall = transition.Recall;
                        successor.Authorized = () => authority() && ReferenceEquals(baseline, _executionBaseline)
                            && ReferenceEquals(resources, _executionBaselineResourceIntent) && !ExecutionScopeNeedsReview(domains)
                            && ResourceWriter.CaptureResourceContext(Character).SequenceEqual(context);
                        fruitCapacityReturn = true;
                    }
                }
                var changes = successor.RetireRoute || successor.ControlsGear && !LoadoutManager.ExactLoadoutMatches(successor.Gear)
                    || successor.ChangeSupport && CaptureSupportState().Selection.StableKey != successor.Support.StableKey;
                // Rejected successor admission is not a failed native mutation. In
                // particular, a finite action's finally must not starve healthy lanes.
                reconciled = !changes || Character.bossController != null && !Character.bossController.isFighting && !Character.bossController.nukeBoss
                    && (Character.adventure.zone == -1 || CaptureSelectedSetupRouteRetirement(successor)?.Invoke() == true);
                if (reconciled)
                {
                    BeginPresetSupportAcquisition(baseline.Owner, baseline.Acquisition);
                    ApplySelectedSetup(successor);
                }
            }
            if (claim.ResourcePolicy != null && claim.ReleaseOutcome?.Verified != true
                && !Settings.UseCustomRebirthPlan && _globalPresetManualId == null && !_customSustainedFocusGoal.HasValue)
            {
                var policy = claim.ResourcePolicy;
                var expected = ResourceWriter.EmptyDesiredMap(policy.Energy, policy.Magic, policy.Res3);
                foreach (var pair in policy.Desired) expected[pair.Key] = pair.Value;
                var observed = ResourceWriter.CaptureAllocations(Character);
                if (expected.Any(x => observed[x.Key] == null || !x.Value.SequenceEqual(observed[x.Key]))) return false;
                var result = ReconcileResourceSetup(new ResourceSetupIntent { Owner = "release:" + claim.Owner,
                    Desired = ResourceWriter.EmptyDesiredMap(policy.Energy, policy.Magic, policy.Res3),
                    Energy = policy.Energy, Magic = policy.Magic, Res3 = policy.Res3,
                    Authorized = () => claim.Epoch.Matches() && !Settings.UseCustomRebirthPlan && _globalPresetManualId == null && !_customSustainedFocusGoal.HasValue });
                claim.ReleaseOutcome = result;
                if (!result.Verified) return false;
            }
            if ((claim.Domains & (ExecutionDomain)7) != 0 || fruitCapacityReturn && reconciled) { _resourceReconciliationPending = true; ReconcileCustomResources(); }
            return reconciled;
        }

        private ExecutionGrant GrantSelectedSetup(SelectedSetupRequirements wanted)
        {
            var changesGear = !LoadoutManager.ExactLoadoutMatches(wanted.Gear);
            var domains = (wanted.ControlsGear ? ExecutionDomain.Gear : 0) | (wanted.ChangeSupport ? ExecutionDomain.Support : 0)
                | (changesGear ? (ExecutionDomain)(int)wanted.Recall : 0) | (wanted.RetireRoute ? ExecutionDomain.Route : 0);
            if (changesGear
                || wanted.ChangeSupport && CaptureSupportState().Selection.StableKey != wanted.Support.StableKey)
                domains |= ExecutionDomain.Route;
            var explicitClaim = wanted.Claim;
            var stage = _customStageKey; var focus = _customRequestedFocusGoal;
            var mode = Settings.UseCustomRebirthPlan; var file = Settings.CustomPlanFile;
            var manual = Settings.ManualAdventureEnabled;
            var applying = _globalPresetApplyingPolicy; var support = _presetSupportAcquisition;
            var authority = wanted.Authorized;
            return ResolveExecutionGrant(wanted.Owner, wanted.Acquisition, domains, () => {
                if (authority == null || !authority()) return false;
                if (explicitClaim != null) return ExecutionClaimCurrent(explicitClaim)
                    && ((domains & ExecutionDomain.Route) == 0 || Character.adventure.zone == -1
                        || (explicitClaim.Domains & ExecutionDomain.Route) != 0)
                    && !_executionClaims.Any(x => !x.Ended && !ReferenceEquals(x, explicitClaim) && (x.Domains & domains) != 0);
                return stage == _customStageKey && focus == _customRequestedFocusGoal
                    && mode == Settings.UseCustomRebirthPlan && file == Settings.CustomPlanFile
                    && manual == Settings.ManualAdventureEnabled
                    && ReferenceEquals(applying, _globalPresetApplyingPolicy) && ReferenceEquals(support, _presetSupportAcquisition)
                    && !_executionClaims.Any(x => !x.Ended && (x.Domains & domains) != 0
                        && (wanted.ResolvedClaims == null || !wanted.ResolvedClaims.Contains(x)));
            }, resolvedClaims: wanted.ResolvedClaims ?? (explicitClaim == null ? null : new[] { explicitClaim }));
        }

    }
}
