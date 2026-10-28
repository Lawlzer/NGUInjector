using System;
using System.Linq;
using System.Reflection;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal partial class Main
    {
        private bool _diggerPurchaseRunning;
        private sealed class DiggerPurchaseAction : FiniteAction
        {
            internal int Id;
            internal long Level;
            internal double Gold, Cost;
            internal ExecutionGrant Grant;
            internal ExecutionClaim[] CompatibleClaims;
            internal Func<bool> ClaimsCurrent;
            internal Func<bool, bool> ContextCurrent;
            internal Action ApproveGrowth;
            internal override bool Prerequisites() => ContextCurrent(false)
                && Character.allDiggers.upgradeCost(Id) == Cost && Character.realGold == Gold
                && Character.diggers.diggers[Id].maxLevel == Level
                && Cost > 0d && Cost <= double.MaxValue && Cost <= Gold && Level < Character.allDiggers.hardCapLevel(Id);
            internal override string Observe() => ActionNumber(Character.diggers.diggers[Id].maxLevel) + "|" + ActionNumber(Character.realGold);
            internal override void Invoke() => Character.allDiggers.upgradeMaxLevel(Id);
            internal override MutationDisposition Classify(string before, string after, Exception error)
            {
                if (ContextCurrent(true))
                {
                    if (reference.ValidateExecutionGrant(Grant, Footprint)) ApproveGrowth?.Invoke();
                    return error == null ? MutationDisposition.AppliedVerified : MutationDisposition.ModelAppliedRenderFailed;
                }
                if (error == null && ContextCurrent(false)) return MutationDisposition.RejectedUnchanged;
                return MutationDisposition.UnknownOutcome;
            }
        }

        internal FiniteActionResult PurchaseActionDiggers()
        {
            var refused = new FiniteActionResult { Disposition = MutationDisposition.RejectedUnchanged };
            if (_diggerPurchaseRunning || !DiggerPurchasesAvailable()) return refused;
            _diggerPurchaseRunning = true;
            try
            {
                var c = Character; var controller = c.allDiggers; var gold = c.realGold;
                var selected = Enumerable.Range(0, c.diggers.diggers.Count)
                    .Where(i => c.diggers.diggers[i].maxLevel < controller.hardCapLevel(i))
                    .Select(i => new { Id = i, Level = c.diggers.diggers[i].maxLevel, Cost = controller.upgradeCost(i) })
                    .Where(x => x.Cost > 0d && x.Cost <= double.MaxValue && x.Cost <= gold)
                    .OrderBy(x => x.Level == 0 ? 0 : 1).ThenBy(x => x.Cost).ThenBy(x => x.Id).FirstOrDefault();
                if (selected == null) return refused;
                var identity = ActionIdentity("digger-purchase:" + selected.Id, "digger-purchases", ActionNumber(selected.Level));
                var action = new DiggerPurchaseAction { Identity = identity, Id = selected.Id, Cost = selected.Cost,
                    Gold = gold, Level = selected.Level,
                    Footprint = ExecutionDomain.FiniteAction | ExecutionDomain.Support };
                CaptureDiggerPurchase(action);
                action.Grant = ResolveExecutionGrant(identity.Owner, identity.Acquisition, action.Footprint,
                    () => DiggerPurchasesAvailable() && action.ClaimsCurrent(), resolvedClaims: action.CompatibleClaims);
                var approveRouteGrowth = CaptureRouteSupportGrowthApproval();
                var result = RunFiniteAction(action, action.Grant);
                approveRouteGrowth?.Invoke(result);
                return result;
            }
            catch (Exception error) { refused.Error = error; refused.Reason = error.Message; return refused; }
            finally { _diggerPurchaseRunning = false; }
        }

        private static bool DiggerPurchasesAvailable()
        {
            var c = Character; var controller = c?.allDiggers; var rows = c?.diggers?.diggers;
            return Settings?.UpgradeDiggers == true && c?.settings?.diggersOn == true && c.highestBoss >= 30
                && controller != null && ReferenceEquals(controller.character, c) && rows != null && rows.Count == 12
                && rows.All(x => x != null && x.maxLevel >= 0)
                && c.realGold >= 0d && c.realGold <= double.MaxValue
                && controller.baseGoldCost != null && controller.baseGoldCost.Count >= rows.Count
                && controller.gpsGrowthRate != null && controller.gpsGrowthRate.Count >= rows.Count
                && controller.baseGoldCost.Take(rows.Count).All(x => x > 0d && x <= double.MaxValue)
                && controller.gpsGrowthRate.Take(rows.Count).All(x => x > 1d && x <= double.MaxValue);
        }

        private void CaptureDiggerPurchase(DiggerPurchaseAction action)
        {
            var model = Character; var settings = model.settings; var controller = model.allDiggers;
            var rows = model.diggers.diggers.ToArray(); var maximum = rows.Select(x => x.maxLevel).ToArray();
            var prices = controller.baseGoldCost; var growth = controller.gpsGrowthRate;
            var priceValues = prices.ToArray(); var growthValues = growth.ToArray();
            var epoch = new LoadoutManager.SnapshotEpoch(); var objects = SupportObjects();
            var support = CaptureSupportState().StableKey;
            var originalContext = CaptureSupportContext(); var baseGold = model.machine.realBaseGold;
            var resources = ResourceWriter.CaptureResourceContext(model); var allocations = ResourceWriter.AllocatedResourceKey();
            var gear = LoadoutManager.CaptureExactLoadout(); var material = gear.Select(x => LoadoutPresetPrimitiveKey(x.equipment)).ToArray();
            var claimDomains = action.Footprint | (CombatPrepRunActive ? ExecutionDomain.Gear : ExecutionDomain.None);
            action.CompatibleClaims = _executionClaims.Where(x => !x.Ended && (x.Domains & claimDomains) != 0).ToArray();
            var claims = action.CompatibleClaims.Select(claim => new {
                Claim = claim, claim.Owner, claim.Acquisition, claim.Domains, claim.Current,
                Setup = claim.Setup, Selection = claim.Setup?.Copy(), claim.ObservedGear, claim.ObservedMaterial, claim.ObservedSupport,
                claim.ResourcePolicy, claim.ExplicitReturnBaseline, claim.ReservedEnergy, claim.ReservedMagic, claim.ReservedRes3,
                claim.CapacityTransition
            }).ToArray();
            action.ClaimsCurrent = () => claims.Length <= 3
                && !_executionClaims.Any(x => !x.Ended && (x.Domains & claimDomains) != 0
                    && !action.CompatibleClaims.Contains(x))
                && claims.All(x => {
                    var claim = x.Claim; var owned = x.Setup; var selected = x.Selection;
                    return ExecutionClaimCurrent(claim) && ExecutionClaimSetupOwned(claim) && !claim.SetupReleased
                        && claim.Owner == x.Owner && claim.Acquisition == x.Acquisition && claim.Domains == x.Domains
                        && ReferenceEquals(claim.Current, x.Current) && ReferenceEquals(claim.Setup, owned)
                        && ReferenceEquals(claim.ObservedGear, x.ObservedGear) && ReferenceEquals(claim.ObservedMaterial, x.ObservedMaterial)
                        && claim.ObservedSupport == x.ObservedSupport && ReferenceEquals(claim.ResourcePolicy, x.ResourcePolicy)
                        && ReferenceEquals(claim.ExplicitReturnBaseline, x.ExplicitReturnBaseline) && claim.CapacityTransition == x.CapacityTransition
                        && claim.ReservedEnergy == x.ReservedEnergy && claim.ReservedMagic == x.ReservedMagic && claim.ReservedRes3 == x.ReservedRes3
                        && owned != null && ReferenceEquals(owned.Claim, claim) && owned.Owner == x.Owner && owned.Acquisition == x.Acquisition
                        && ReferenceEquals(owned.Gear, selected.Gear) && ReferenceEquals(owned.Support, selected.Support)
                        && ReferenceEquals(owned.Grant, selected.Grant) && ReferenceEquals(owned.ResolvedClaims, selected.ResolvedClaims)
                        && ReferenceEquals(owned.DiggerWeights, selected.DiggerWeights)
                        && ReferenceEquals(owned.Authorized, selected.Authorized) && ReferenceEquals(owned.MaterialValid, selected.MaterialValid)
                        && ReferenceEquals(owned.BoundaryAuthorized, selected.BoundaryAuthorized)
                        && ReferenceEquals(owned.RouteRetirementAuthorized, selected.RouteRetirementAuthorized) && ReferenceEquals(owned.GearAppliedValid, selected.GearAppliedValid)
                        && ReferenceEquals(owned.RequiredBeards, selected.RequiredBeards)
                        && owned.ControlsGear == selected.ControlsGear && owned.ControlsSupport == selected.ControlsSupport
                        && owned.ChangeSupport == selected.ChangeSupport && owned.Budget == selected.Budget && owned.EnforceSupportBudget == selected.EnforceSupportBudget
                        && owned.RetireRoute == selected.RetireRoute && owned.OwnsManualRoute == selected.OwnsManualRoute && owned.Recall == selected.Recall
                        && owned.Authorized?.Invoke() == true && (owned.MaterialValid == null || owned.MaterialValid())
                        && (owned.BoundaryAuthorized == null || owned.BoundaryAuthorized()) && (owned.GearAppliedValid == null || owned.GearAppliedValid())
                        && (!owned.ControlsGear || LoadoutManager.ExactLoadoutMatches(owned.Gear))
                        && owned.Support != null && CaptureSupportState().Selection.StableKey == owned.Support.StableKey
                        && (owned.RequiredBeards == null || !owned.RequiredBeards.Except(owned.Support.Beards).Any())
                        && (!owned.ChangeSupport || SupportProjection.Evaluate(CaptureSupportContext(), owned.Support, owned.Budget, owned.EnforceSupportBudget).Feasible);
                });
            action.ContextCurrent = after => {
                if (!ReferenceEquals(Character, model) || !ReferenceEquals(model.settings, settings) || !epoch.Matches()
                    || !DiggerPurchasesAvailable() || !action.ClaimsCurrent() || !ReferenceEquals(model.allDiggers, controller)
                    || !ReferenceEquals(controller.baseGoldCost, prices) || !ReferenceEquals(controller.gpsGrowthRate, growth)
                    || !prices.SequenceEqual(priceValues) || !growth.SequenceEqual(growthValues)
                    || model.realGold != (after ? action.Gold - action.Cost : action.Gold)
                    || model.diggers.diggers.Count != rows.Length
                    || rows.Where((row, i) => !ReferenceEquals(row, model.diggers.diggers[i])
                        || row.maxLevel != maximum[i] + (after && i == action.Id ? 1 : 0)).Any()) return false;
                var current = SupportObjects(); var resourceCurrent = ResourceWriter.CaptureResourceContext(model);
                var context = CaptureSupportContext();
                return current.Length == objects.Length && !current.Where((x, i) => !ReferenceEquals(x, objects[i])).Any()
                    && resourceCurrent.Length == resources.Length && !resourceCurrent.Where((x, i) => !ReferenceEquals(x, resources[i])).Any()
                    && CaptureSupportState().StableKey == support && ResourceWriter.AllocatedResourceKey() == allocations
                    && model.machine.realBaseGold == baseGold
                    && context.BeardsAvailable == originalContext.BeardsAvailable && context.BeardsDisabled == originalContext.BeardsDisabled
                    && context.DiggersAvailable == originalContext.DiggersAvailable && context.BeardSlots == originalContext.BeardSlots
                    && context.DiggerSlots == originalContext.DiggerSlots && context.UnlockedBeards.SequenceEqual(originalContext.UnlockedBeards)
                    && !context.Diggers.Where((d, i) => d.BaseDrain != originalContext.Diggers[i].BaseDrain
                        || d.Growth != originalContext.Diggers[i].Growth || d.ConsumesGold != originalContext.Diggers[i].ConsumesGold
                        || d.StartBonus != originalContext.Diggers[i].StartBonus || d.PerLevel != originalContext.Diggers[i].PerLevel).Any()
                    && LoadoutManager.ExactLoadoutMatches(gear)
                    && !gear.Where((x, i) => LoadoutPresetPrimitiveKey(x.equipment) != material[i]).Any();
            };

            var run = _combatPrepRun;
            if (!CombatPrepRunActive || run.CleanupPending || run.PhaseIndex < 0) return;
            var inputs = run.Inputs; var policy = run.Policy; var index = run.PhaseIndex; var phase = policy.Phases[index]; var setup = run.Setup;
            var supportMaterial = CombatPrepSupportMaterial(originalContext);
            if (setup == null) return;
            var prepSelection = setup.Copy();
            var started = run.Started; var duration = run.Duration; var phaseStarted = run.PhaseStarted; var deadline = run.PhaseDeadline;
            var rebirthDeadline = run.DeadlineRebirthSeconds; var entry = run.OwnerEntry;
            var policyStarted = run.PolicyStarted; var replans = run.ReplanCount; var ownerPlan = run.OwnerPlan; var ownerStage = run.OwnerStage;
            var targets = model.advancedTraining.levelTarget; var targetValues = targets.ToArray();
            var prepDomains = ExecutionDomain.Gear | ExecutionDomain.Support;
            // This proves an observation, not prep funding/credit. A compatible owner can suspend the ordinary setup grant.
            var prepGrant = ResolveExecutionGrant(setup.Owner, setup.Acquisition, prepDomains, () =>
                ReferenceEquals(_combatPrepRun, run) && ReferenceEquals(run.Inputs, inputs) && ReferenceEquals(run.Setup, setup)
                && ReferenceEquals(setup.Gear, prepSelection.Gear) && ReferenceEquals(setup.Support, prepSelection.Support)
                && ReferenceEquals(setup.Authorized, prepSelection.Authorized) && ReferenceEquals(setup.MaterialValid, prepSelection.MaterialValid)
                && ReferenceEquals(setup.BoundaryAuthorized, prepSelection.BoundaryAuthorized)
                && setup.Budget == prepSelection.Budget && setup.EnforceSupportBudget == prepSelection.EnforceSupportBudget
                && setup.ControlsGear == prepSelection.ControlsGear && setup.ControlsSupport == prepSelection.ControlsSupport && setup.ChangeSupport == prepSelection.ChangeSupport
                && ReferenceEquals(run.Policy, policy) && run.PhaseIndex == index && ReferenceEquals(policy.Phases[index], phase)
                && run.Started == started && run.Duration == duration && run.PhaseStarted == phaseStarted && run.PhaseDeadline == deadline
                && run.DeadlineRebirthSeconds == rebirthDeadline && run.OwnerEntry == entry
                && run.PolicyStarted == policyStarted && run.ReplanCount == replans && run.OwnerPlan == ownerPlan && run.OwnerStage == ownerStage
                && ReferenceEquals(model.advancedTraining.levelTarget, targets) && targets.SequenceEqual(targetValues)
                && run.BlockedReason == null && inputs.SupportMaterial == supportMaterial
                && action.ClaimsCurrent() && CombatPrepPhaseAuthority(run, phase)
                && setup.Authorized?.Invoke() == true && (setup.BoundaryAuthorized == null || setup.BoundaryAuthorized())
                && setup.MaterialValid() && CombatPrepGearMatches(setup.Gear)
                && !run.ExpectedGear.Where((x, i) => LoadoutPresetPrimitiveKey(x.equipment) != run.ExpectedMaterial[i]).Any()
                && CombatPrepSupportStillOwned(run) && CaptureSupportState().Selection.StableKey == setup.Support.StableKey,
                resolvedClaims: action.CompatibleClaims);
            if (!VerifyExecutionPrerequisites(prepGrant, prepDomains, () => action.ContextCurrent(false))) return;
            action.ApproveGrowth = () => {
                if (!VerifyExecutionPrerequisites(prepGrant, prepDomains, () => action.ContextCurrent(true))) return;
                var context = CaptureSupportContext();
                var evaluations = inputs.Support.ToDictionary(x => x.Key, x => SupportProjection.Evaluate(context,
                    x.Value.Evaluation.Selection, x.Value.BudgetFraction, x.Value.EnforceBudget));
                if (inputs.Support.Any(x => x.Value.Evaluation.Feasible && !evaluations[x.Key].Feasible)) return;
                var profiles = new[] { inputs.Snapshot.Current }.Concat(inputs.Snapshot.Profiles).ToArray();
                var rates = profiles.Select(profile => {
                    var bonuses = CombatPrepGearBonuses(inputs.Gear[profile.Key]);
                    return new[] { CombatPrepWandoosSpeedWithoutAtOrBootup(true, bonuses, evaluations[profile.Key]),
                        CombatPrepWandoosSpeedWithoutAtOrBootup(false, bonuses, evaluations[profile.Key]) };
                }).ToArray();
                if (rates.Any(x => x.Any(rate => !SupportProjection.Finite(rate) || rate < 0))
                    || !VerifyExecutionPrerequisites(prepGrant, prepDomains, () => action.ContextCurrent(true))) return;
                // Only this exact owned increment advances the material boundary. Pinned levels and phase budgets stay unchanged.
                foreach (var pair in evaluations) inputs.Support[pair.Key].Evaluation = pair.Value;
                for (var i = 0; i < profiles.Length; i++)
                {
                    profiles[i].WandoosEnergySpeedWithoutAt = rates[i][0]; profiles[i].WandoosMagicSpeedWithoutAt = rates[i][1];
                    profiles[i].NetGoldPerSecond = evaluations[profiles[i].Key].NetGold;
                }
                inputs.SupportContext = context; inputs.SupportMaterial = CombatPrepSupportMaterial(context);
                inputs.Snapshot.Gold = model.realGold;
            };
        }

        private sealed class ExpPurchaseAction : FiniteAction
        {
            internal int Stat;
            internal long Amount;
            internal long Cost, Currency;
            internal decimal Base;
            internal Character Model;
            internal object Purchases, NativeSettings;
            internal FieldInfo Input;
            internal MethodInfo Purchase;
            internal ExecutionGrant Grant;
            private decimal CurrentBase() => Stat == 0 ? ExactBasePower(Model.energyPower) : Stat == 1 ? Model.capEnergy
                : Stat == 2 ? Model.energyBars : Stat == 3 ? ExactBasePower(Model.magic.magicPower)
                : Stat == 4 ? Model.magic.capMagic : Model.magic.magicPerBar;
            private long CurrentCost() => Stat == 0 ? Character.energyPurchases.customPowerCost((int)Amount)
                : Stat == 1 ? Character.energyPurchases.customCapCost(Amount) : Stat == 2 ? Character.energyPurchases.customBarCost((int)Amount)
                : Stat == 3 ? Character.magicPurchases.customPowerCost((int)Amount)
                : Stat == 4 ? Character.magicPurchases.customCapCost(Amount) : Character.magicPurchases.customBarCost((int)Amount);
            internal override bool Prerequisites() => ReferenceEquals(Model, Character) && ReferenceEquals(NativeSettings, Character.settings)
                && ReferenceEquals(Purchases, Stat < 3 ? (object)Character.energyPurchases : Character.magicPurchases)
                && (Stat < 3 ? ReferenceEquals(Character.energyPurchases.character, Model) : ReferenceEquals(Character.magicPurchases.character, Model))
                && !AutoExpInputFocused() && Character.highestBoss >= (Stat < 3 ? 17 : 37)
                && (Stat != 1 || Character.capEnergy >= 100000)
                && Amount > 0 && Base + Amount <= (Stat % 3 == 1 ? Character.hardCap() : Character.hardCapPowBar())
                && CurrentBase() == Base && Character.realExp == Currency && Currency >= Cost && Cost > 0 && CurrentCost() == Cost;
            internal override string Observe() => CurrentBase().ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + ActionNumber(Model.realExp);
            internal override void Invoke()
            {
                var original = Input.GetValue(NativeSettings);
                var wanted = Stat % 3 == 1 ? (object)Amount : (int)Amount;
                try
                {
                    if (!reference.ValidateExecutionGrant(Grant, Footprint)) throw new InvalidOperationException("EXP input authority changed.");
                    Input.SetValue(NativeSettings, wanted);
                    if (!reference.ValidateExecutionGrant(Grant, Footprint)) throw new InvalidOperationException("EXP purchase authority changed.");
                    Purchase.Invoke(Purchases, null);
                }
                finally
                {
                    // Only return the exact field still borrowed in this native settings object.
                    if (reference.ValidateExecutionGrant(Grant, Footprint) && ReferenceEquals(NativeSettings, Character.settings)
                        && Equals(Input.GetValue(NativeSettings), wanted)) Input.SetValue(NativeSettings, original);
                }
            }
            internal override MutationDisposition Classify(string before, string after, Exception error)
            {
                if (CurrentBase() == Base + Amount && Model.realExp == Currency - Cost)
                    return error == null ? MutationDisposition.AppliedVerified : MutationDisposition.ModelAppliedRenderFailed;
                return MutationDisposition.UnknownOutcome;
            }
        }
    }
}
