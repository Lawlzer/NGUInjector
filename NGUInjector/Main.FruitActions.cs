using System;
using System.Linq;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal partial class Main
    {
        private ExecutionClaim _harvestActionClaim;
        private SelectedSetupRequirements _harvestActionSetup;
        private Func<bool> _fruitCapActionReady;

        internal bool PrepareHarvestAction()
        {
            if (CustomBossFightActive()) return false;
            if (_harvestActionClaim != null)
                return FiniteActionSetupVerified(_harvestActionClaim, _harvestActionSetup);
            var id = ActionIdentity("harvest-setup", "ygg-harvest", "harvest");
            var claim = BeginExecutionClaim(id.Owner, id.Acquisition,
                ExecutionDomain.Gear | ExecutionDomain.Support | ExecutionDomain.Route | ExecutionDomain.Inventory | ExecutionDomain.FiniteAction,
                () => !CustomBossFightActive());
            _harvestActionClaim = claim;
            try
            {
                var gear = SelectConfiguredActionGear(Settings.YggdrasilLoadout);
                var support = SelectActionSupport(CaptureSupportState().Selection.Beards, 8, 11);
                _harvestActionSetup = AcquireFiniteActionSetup(claim, gear, support, true, true);
                return FiniteActionSetupVerified(claim, _harvestActionSetup);
            }
            catch
            {
                ReleaseHarvestAction();
                throw;
            }
        }

        internal void ReleaseHarvestAction(bool reconcile = true)
        {
            var claim = _harvestActionClaim;
            _harvestActionClaim = null; _harvestActionSetup = null;
            if (claim != null) EndExecutionClaim(claim, reconcile);
        }

        private sealed class FruitHarvestAction : FiniteAction
        {
            internal int Id, Harvests;
            internal bool RequireMaxed, Eat;
            internal Func<bool> SetupVerified;
            internal override bool Prerequisites() => SetupVerified() && Character.buttons.yggdrasil.interactable
                && Character.yggdrasil.fruits[Id].harvests == Harvests
                && Character.yggdrasilController.fruits[0].harvestTier(Id) >= 1
                && (!RequireMaxed || Character.yggdrasilController.fruits[0].fruitMaxxed(Id));
            internal override string Observe() => ActionNumber(Character.yggdrasil.fruits[Id].harvests) + "|"
                + ActionNumber(Character.yggdrasil.fruits[Id].seconds) + "|" + Character.yggdrasil.fruits[Id].activated;
            internal override void Invoke()
            {
                if (Eat) Character.yggdrasilController.fruits[0].consumeFruit(Id);
                else Character.yggdrasilController.fruits[0].harvest(Id);
            }
            internal override MutationDisposition Classify(string before, string after, Exception error) =>
                Character.yggdrasil.fruits[Id].harvests == Harvests + 1 && Character.yggdrasil.fruits[Id].seconds == 0f
                    ? error == null ? MutationDisposition.AppliedVerified : MutationDisposition.ModelAppliedRenderFailed
                    : MutationDisposition.UnknownOutcome;
        }

        internal bool HarvestFruitAction(int fruitId, bool requireMaxed, bool forceEat = false,
            ExecutionClaim setupClaim = null, SelectedSetupRequirements selectedSetup = null)
        {
            return RunFruitHarvestAction(fruitId, requireMaxed, forceEat, setupClaim, selectedSetup, null, null).Verified;
        }

        private FiniteActionResult RunFruitHarvestAction(int fruitId, bool requireMaxed, bool forceEat, ExecutionClaim setupClaim,
            SelectedSetupRequirements selectedSetup, string opportunity, int? originalHarvests)
        {
            if (Character?.yggdrasil?.fruits == null || fruitId < 0 || fruitId >= Character.yggdrasil.fruits.Count)
                return new FiniteActionResult { Disposition = MutationDisposition.RejectedUnchanged, Reason = "Fruit state is unavailable." };
            var fruit = Character.yggdrasil.fruits[fruitId];
            var preparation = _fightPreparationAttempt;
            // Final rebirth's ordinary harvest must not retry an ambiguous prep invocation.
            if (preparation?.UnverifiedFruit == fruitId && UnverifiedPreparationFruitCurrent(preparation))
                return new FiniteActionResult { Disposition = MutationDisposition.RejectedUnchanged, Reason = "Preparation fruit outcome is unverified." };
            var claim = setupClaim ?? _harvestActionClaim;
            var identity = ActionIdentity("fruit-harvest:" + fruitId, claim?.Owner ?? "ygg-harvest", opportunity ?? ActionNumber(fruit.harvests));
            var gear = LoadoutManager.CaptureExactLoadout(); var support = CaptureSupportState().StableKey;
            var footprint = ExecutionDomain.FiniteAction | ExecutionDomain.Inventory | ExecutionDomain.Gear | ExecutionDomain.Support
                | (claim == null ? 0 : ExecutionDomain.Route);
            var action = new FruitHarvestAction { Id = fruitId, Harvests = originalHarvests ?? fruit.harvests, Identity = identity,
                RequireMaxed = requireMaxed, Eat = forceEat || fruit.eatFruit, Footprint = footprint,
                SetupVerified = () => claim != null ? FiniteActionSetupVerified(claim, selectedSetup ?? _harvestActionSetup)
                    : LoadoutManager.ExactLoadoutMatches(gear) && CaptureSupportState().StableKey == support };
            var grant = ResolveExecutionGrant(identity.Owner, identity.Acquisition, footprint,
                () => ReferenceEquals(Character.yggdrasil.fruits[fruitId], fruit), claim);
            return RunFiniteAction(action, grant);
        }

        private bool UnverifiedPreparationFruitCurrent(FightPreparationAttempt preparation)
        {
            if (preparation == null || preparation.UnverifiedFruit < 0 || !preparation.Epoch.Matches()) return false;
            var fruitId = preparation.UnverifiedFruit;
            var fruits = Character.yggdrasil?.fruits;
            // Unavailable state is not evidence that the ambiguous native opportunity disappeared.
            return fruits == null || fruitId >= fruits.Count || fruits[fruitId] == null
                || ReferenceEquals(preparation.Fruits[fruitId], fruits[fruitId]) && preparation.FruitHarvests[fruitId] == fruits[fruitId].harvests;
        }

        private sealed class FruitActivationAction : FiniteAction
        {
            internal int Id, Harvests;
            internal bool Energy;
            internal long Cost, Idle, Current;
            internal Func<bool> Funded;
            internal override bool Prerequisites() => Funded() && Character.buttons.yggdrasil.interactable
                && Character.yggdrasil.fruits[Id].harvests == Harvests && !Character.yggdrasil.fruits[Id].activated
                && !Character.yggdrasil.fruits[Id].permCostPaid && Character.yggdrasil.fruits[Id].maxTier > 0
                && Character.yggdrasilController.activationCost[Id] == Cost
                && (Energy ? Character.idleEnergy : Character.magic.idleMagic) == Idle && Idle >= Cost
                && (Energy ? Character.curEnergy : Character.magic.curMagic) == Current;
            internal override string Observe() => Character.yggdrasil.fruits[Id].activated + "|"
                + ActionNumber(Energy ? Character.idleEnergy : Character.magic.idleMagic) + "|"
                + ActionNumber(Energy ? Character.curEnergy : Character.magic.curMagic);
            internal override void Invoke() => Character.yggdrasilController.fruits[0].activate(Id);
            internal override MutationDisposition Classify(string before, string after, Exception error) =>
                Character.yggdrasil.fruits[Id].activated && Character.yggdrasil.fruits[Id].harvests == Harvests
                && (Energy ? Character.idleEnergy : Character.magic.idleMagic) == Idle - Cost
                && (Energy ? Character.curEnergy : Character.magic.curMagic) == Current - Cost
                    ? error == null ? MutationDisposition.AppliedVerified : MutationDisposition.ModelAppliedRenderFailed
                    : MutationDisposition.UnknownOutcome;
        }

        internal bool ActivateFruitAction(int fruitId)
        {
            if (_fruitCapActionReady != null && !_fruitCapActionReady()) return false;
            var fruit = Character.yggdrasil.fruits[fruitId];
            var energy = Character.yggdrasilController.usesEnergy[fruitId];
            if (energy ? !Settings.ManageEnergy : !Settings.ManageMagic) return false;
            var id = ActionIdentity("fruit-activate:" + fruitId, "fruit-activation", ActionNumber(fruit.harvests));
            var footprint = ExecutionDomain.FiniteAction | (energy ? ExecutionDomain.Energy : ExecutionDomain.Magic);
            if (ExecutionOwnsDomain(footprint)) return false;
            ExecutionClaim claim = null;
            try
            {
                claim = BeginExecutionClaim(id.Owner, id.Acquisition, footprint, () => ReferenceEquals(Character.yggdrasil.fruits[fruitId], fruit));
                var cost = Character.yggdrasilController.activationCost[fruitId];
                SetExecutionResourceReservation(claim, energy ? cost : 0L, energy ? 0L : cost);
                ReconcileExecutionResourceClaim(claim);
                if (!ExecutionResourceReservationReady(claim)) return false;
                var action = new FruitActivationAction { Identity = id, Footprint = footprint, Id = fruitId, Energy = energy,
                    Cost = cost, Harvests = fruit.harvests, Idle = energy ? Character.idleEnergy : Character.magic.idleMagic,
                    Current = energy ? Character.curEnergy : Character.magic.curMagic,
                    Funded = () => ExecutionResourceReservationReady(claim) && (_fruitCapActionReady == null || _fruitCapActionReady()) };
                return RunFiniteAction(action, ResolveExecutionGrant(id.Owner, id.Acquisition, footprint,
                    () => ExecutionClaimCurrent(claim), claim)).Verified;
            }
            finally { if (claim != null) EndExecutionClaim(claim); }
        }
    }
}
